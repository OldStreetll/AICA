using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Bridges a single GitNexus MCP tool into the AICA tool system.
    /// One instance per MCP tool; created via <see cref="CreateAllTools"/>.
    /// </summary>
    public sealed class McpBridgeTool : IAgentTool
    {
        private readonly IGitNexusProcessManager _processManager;
        private readonly string _mcpToolName;
        private readonly ToolDefinition _definition;
        private readonly ToolMetadata _metadata;

        public string Name { get; }
        public string Description { get; }

        private McpBridgeTool(
            string aicaName,
            string mcpToolName,
            string description,
            ToolDefinition definition,
            ToolMetadata metadata,
            IGitNexusProcessManager processManager)
        {
            Name = aicaName;
            _mcpToolName = mcpToolName;
            Description = description;
            _definition = definition;
            _metadata = metadata;
            _processManager = processManager;
        }

        public ToolDefinition GetDefinition() => _definition;
        public ToolMetadata GetMetadata() => _metadata;

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            // Step 1: Ensure GitNexus MCP server is running
            bool ready;
            try
            {
                ready = await _processManager.EnsureRunningAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus EnsureRunning failed: {ex.Message}");
                ready = false;
            }

            // Snapshot client reference atomically to avoid TOCTOU race
            // (process can exit between EnsureRunningAsync and Client access)
            var client = ready ? _processManager.Client : null;

            if (!ready || client == null)
            {
                return ToolResult.Fail(
                    $"GitNexus MCP server is not available. Tool '{Name}' cannot execute. " +
                    "Use grep_search or read_file as alternatives for code exploration.");
            }

            // Step 2: Forward arguments to MCP tool, auto-inject repo if missing
            var mcpArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (call.Arguments != null)
            {
                foreach (var kvp in call.Arguments)
                {
                    // Normalize LLM parameter name hallucinations:
                    // LLM sometimes sends "pattern" instead of "query" (confused with grep_search)
                    var key = NormalizeParamName(kvp.Key);
                    mcpArgs[key] = kvp.Value;
                }
            }

            // Auto-inject repo parameter when LLM omits it (P1 fix, moved from system prompt)
            if (!mcpArgs.ContainsKey("repo") && context?.WorkingDirectory != null)
            {
                var repoName = ResolveRepoName(context.WorkingDirectory);
                if (repoName != null)
                {
                    mcpArgs["repo"] = repoName;
                    System.Diagnostics.Debug.WriteLine($"[AICA] Auto-injected repo=\"{repoName}\" for {Name}");
                }
            }

            McpToolResult mcpResult;
            try
            {
                mcpResult = await client.CallToolAsync(_mcpToolName, mcpArgs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus CallToolAsync exception: {ex.Message}");
                return ToolResult.Fail($"GitNexus tool '{Name}' failed: {ex.Message}");
            }

            if (mcpResult.Success)
            {
                return ToolResult.Ok(mcpResult.Content);
            }

            // MCP returned an error — return as tool failure (no fallback for MCP-level errors)
            return ToolResult.Fail(mcpResult.Error ?? "GitNexus tool returned an unknown error.");
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolve git repo name from working directory for auto-injection.
        /// </summary>
        /// <summary>
        /// Normalize LLM parameter name hallucinations for a given MCP tool.
        /// LLM sometimes confuses parameter names between tools (e.g., sends "pattern"
        /// from grep_search when calling gitnexus_query which expects "query").
        /// Only remaps when the target MCP tool actually expects the normalized name.
        /// </summary>
        private string NormalizeParamName(string key)
        {
            // Only remap for tools whose primary parameter is "query"
            // (query, context, impact, cypher all accept "query" or "name")
            if (_mcpToolName == "query" || _mcpToolName == "context" ||
                _mcpToolName == "impact" || _mcpToolName == "cypher")
            {
                switch (key?.ToLowerInvariant())
                {
                    case "pattern":
                    case "search_term":
                    case "search_query":
                    case "search":
                        return "query";
                }
            }
            return key;
        }

        private static string ResolveRepoName(string workingDirectory)
        {
            var dir = workingDirectory;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, ".git")))
                    return System.IO.Path.GetFileName(dir);
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Create all 6 GitNexus bridge tools.
        /// No fallback — each tool returns a clear error if GitNexus is unavailable,
        /// letting the LLM choose an alternative tool explicitly.
        /// </summary>
        public static IReadOnlyList<McpBridgeTool> CreateAllTools(
            IGitNexusProcessManager processManager)
        {
            if (processManager == null)
                throw new ArgumentNullException(nameof(processManager));

            return new[]
            {
                CreateContextTool(processManager),
                CreateImpactTool(processManager),
                CreateQueryTool(processManager),
                CreateDetectChangesTool(processManager),
                CreateRenameTool(processManager),
                CreateCypherTool(processManager)
            };
        }

        /// <summary>
        /// Create GitNexus bridge tools using native MCP tool definitions from tools/list.
        /// Falls back to hardcoded definitions if ListToolsAsync fails.
        /// </summary>
        public static async Task<IReadOnlyList<McpBridgeTool>> CreateAllToolsAsync(
            IGitNexusProcessManager processManager,
            CancellationToken ct = default)
        {
            if (processManager == null)
                throw new ArgumentNullException(nameof(processManager));

            // Try to get native definitions from MCP server
            List<McpToolDefinition> mcpDefs = null;
            try
            {
                var ready = await processManager.EnsureRunningAsync(ct).ConfigureAwait(false);
                var client = ready ? processManager.Client : null;
                if (client != null)
                {
                    mcpDefs = await client.ListToolsAsync(ct).ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[AICA] McpBridgeTool: ListToolsAsync returned {mcpDefs.Count} tools");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] McpBridgeTool: ListToolsAsync failed, using hardcoded fallback: {ex.Message}");
            }

            // If we got native definitions, build tools from them
            if (mcpDefs != null && mcpDefs.Count > 0)
            {
                var mcpLookup = mcpDefs.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
                return BuildToolsFromNativeDefinitions(mcpLookup, processManager);
            }

            // Fallback to hardcoded definitions
            return CreateAllTools(processManager);
        }

        /// <summary>
        /// Build bridge tools using native MCP tool definitions.
        /// Iterates over ALL tools returned by the MCP server — known tools get AICA-specific
        /// metadata (Category, Tags, etc.), unknown tools get sensible defaults.
        /// This ensures new MCP tools are automatically registered without hardcoding.
        /// </summary>
        private static IReadOnlyList<McpBridgeTool> BuildToolsFromNativeDefinitions(
            Dictionary<string, McpToolDefinition> mcpLookup,
            IGitNexusProcessManager pm)
        {
            // Known tools: AICA-specific metadata overrides
            var knownSpecs = new Dictionary<string, (string AicaName, ToolCategory Category, string[] Tags, bool IsModifying, bool RequiresConfirmation, int Timeout)>(StringComparer.OrdinalIgnoreCase)
            {
                ["context"]        = ("gitnexus_context",        ToolCategory.Analysis,  new[] { "search", "read", "context", "gitnexus" },  false, false, 30),
                ["impact"]         = ("gitnexus_impact",         ToolCategory.Analysis,  new[] { "search", "analysis", "gitnexus" },         false, false, 30),
                ["query"]          = ("gitnexus_query",          ToolCategory.Search,    new[] { "search", "gitnexus" },                     false, false, 30),
                ["detect_changes"] = ("gitnexus_detect_changes", ToolCategory.Analysis,  new[] { "search", "analysis", "gitnexus" },         false, false, 30),
                ["rename"]         = ("gitnexus_rename",         ToolCategory.FileWrite, new[] { "modify", "refactor", "gitnexus" },         true,  true,  60),
                ["cypher"]         = ("gitnexus_cypher",         ToolCategory.Analysis,  new[] { "search", "analysis", "gitnexus" },         false, false, 30),
            };

            var tools = new List<McpBridgeTool>();

            // Iterate over ALL MCP tools (not just known specs)
            foreach (var kvp in mcpLookup)
            {
                var mcpName = kvp.Key;
                var mcpDef = kvp.Value;

                string aicaName;
                ToolCategory category;
                string[] tags;
                bool isModifying;
                bool requiresConfirmation;
                int timeout;

                if (knownSpecs.TryGetValue(mcpName, out var spec))
                {
                    // Known tool — use AICA-specific metadata
                    aicaName = spec.AicaName;
                    category = spec.Category;
                    tags = spec.Tags;
                    isModifying = spec.IsModifying;
                    requiresConfirmation = spec.RequiresConfirmation;
                    timeout = spec.Timeout;
                }
                else
                {
                    // Unknown tool — auto-register with sensible defaults
                    aicaName = $"gitnexus_{mcpName}";
                    category = ToolCategory.Analysis;
                    tags = new[] { "gitnexus", mcpName };
                    isModifying = false;
                    requiresConfirmation = false;
                    timeout = 30;
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] McpBridgeTool: Auto-registering unknown MCP tool '{mcpName}' as '{aicaName}'");
                }

                var nativeDesc = mcpDef.Description ?? aicaName;

                // v2.1 O10 [C2]: For gitnexus_cypher, always use the hand-crafted trimmed description
                if (aicaName == "gitnexus_cypher")
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] McpBridgeTool: {aicaName} using trimmed hardcoded desc (skipping native {nativeDesc.Length} chars)");
                    continue; // Keep the hardcoded trimmed version from CreateCypherTool
                }

                // Truncate overly long descriptions
                if (nativeDesc.Length > 4000)
                {
                    nativeDesc = nativeDesc.Substring(0, 4000) + "...";
                }

                var def = new ToolDefinition
                {
                    Name = aicaName,
                    Description = nativeDesc,
                    Parameters = ConvertMcpSchema(mcpDef.InputSchema),
                    RawParametersJson = mcpDef.InputSchema
                };

                var meta = new ToolMetadata
                {
                    Name = aicaName,
                    Description = nativeDesc,
                    Category = category,
                    Tags = tags,
                    TimeoutSeconds = timeout,
                    RequiresNetwork = false,
                    IsModifying = isModifying,
                    RequiresConfirmation = requiresConfirmation
                };

                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] McpBridgeTool: {aicaName} native desc ({nativeDesc.Length} chars): " +
                    $"{(nativeDesc.Length > 120 ? nativeDesc.Substring(0, 120) + "..." : nativeDesc)}");
                tools.Add(new McpBridgeTool(aicaName, mcpName, nativeDesc, def, meta, pm));
            }

            System.Diagnostics.Debug.WriteLine($"[AICA] McpBridgeTool: Built {tools.Count} tools from native MCP definitions");
            return tools;
        }

        /// <summary>
        /// Convert MCP JSON Schema inputSchema to AICA ToolParameters.
        /// </summary>
        private static ToolParameters ConvertMcpSchema(JsonElement inputSchema)
        {
            var parameters = new ToolParameters
            {
                Properties = new Dictionary<string, ToolParameterProperty>(),
                Required = Array.Empty<string>()
            };

            if (inputSchema.ValueKind != JsonValueKind.Object)
                return parameters;

            // Extract required array
            if (inputSchema.TryGetProperty("required", out var reqArray) &&
                reqArray.ValueKind == JsonValueKind.Array)
            {
                parameters.Required = reqArray.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .ToArray();
            }

            // Extract properties
            if (inputSchema.TryGetProperty("properties", out var props) &&
                props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    var paramProp = new ToolParameterProperty();

                    if (prop.Value.TryGetProperty("type", out var typeEl))
                        paramProp.Type = typeEl.GetString();

                    if (prop.Value.TryGetProperty("description", out var descEl))
                        paramProp.Description = descEl.GetString();

                    if (prop.Value.TryGetProperty("default", out var defEl))
                    {
                        paramProp.Default = defEl.ValueKind switch
                        {
                            JsonValueKind.String => defEl.GetString(),
                            JsonValueKind.Number => defEl.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => defEl.GetRawText()
                        };
                    }

                    if (prop.Value.TryGetProperty("enum", out var enumEl) &&
                        enumEl.ValueKind == JsonValueKind.Array)
                    {
                        paramProp.Enum = enumEl.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString())
                            .ToArray();
                    }

                    parameters.Properties[prop.Name] = paramProp;
                }
            }

            return parameters;
        }

        private static McpBridgeTool CreateContextTool(
            IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_context",
                Description = "360-degree view of a single code symbol.\n" +
                    "Shows categorized incoming/outgoing references (calls, imports, extends, implements, methods, properties, overrides), process participation, and file location.\n\n" +
                    "WHEN TO USE: After query() to understand a specific symbol in depth. When you need to know all callers, callees, and what execution flows a symbol participates in.\n" +
                    "AFTER THIS: Use impact() if planning changes.\n\n" +
                    "Handles disambiguation: if multiple symbols share the same name, returns candidates for you to pick from.",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["name"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Symbol name to analyze (function, class, variable, etc.)"
                        },
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional, uses default if omitted)"
                        }
                    },
                    Required = new[] { "name" }
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_context",
                Description = def.Description,
                Category = ToolCategory.Analysis,
                Tags = new[] { "search", "read", "context", "gitnexus" },
                TimeoutSeconds = 30,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_context", "context", def.Description, def, meta, pm);
        }

        private static McpBridgeTool CreateImpactTool(
            IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_impact",
                Description = "Analyze the blast radius of changing a code symbol.\n" +
                    "Returns affected symbols grouped by depth, plus risk assessment, affected execution flows, and affected modules.\n\n" +
                    "WHEN TO USE: Before making code changes — especially refactoring, renaming, or modifying shared code. Shows what would break.\n" +
                    "AFTER THIS: Review d=1 items (WILL BREAK). Use context() on high-risk symbols.\n\n" +
                    "Depth groups:\n- d=1: WILL BREAK (direct callers/importers)\n- d=2: LIKELY AFFECTED (indirect)\n- d=3: MAY NEED TESTING (transitive)",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["target"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Symbol or function to analyze impact for (e.g. 'CAxis::SetPosition')"
                        },
                        ["direction"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Analysis direction: 'upstream' (what calls this), 'downstream' (what this calls), or 'both'",
                            Default = "both",
                            Enum = new[] { "upstream", "downstream", "both" }
                        },
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional)"
                        }
                    },
                    Required = new[] { "target" }
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_impact",
                Description = def.Description,
                Category = ToolCategory.Analysis,
                Tags = new[] { "search", "analysis", "gitnexus" },
                TimeoutSeconds = 30,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_impact", "impact", def.Description, def, meta, pm);
        }

        private static McpBridgeTool CreateQueryTool(
            IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_query",
                Description = "Query the code knowledge graph for execution flows related to a concept.\n" +
                    "Returns processes (call chains) ranked by relevance, each with its symbols and file locations.\n\n" +
                    "WHEN TO USE: Understanding how code works together. Use this when you need execution flows and relationships, not just file matches. Complements grep/IDE search.\n" +
                    "AFTER THIS: Use context() on a specific symbol for 360-degree view (callers, callees, categorized refs).\n\n" +
                    "Hybrid ranking: BM25 keyword + semantic vector search, ranked by Reciprocal Rank Fusion.",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["query"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Natural language or keyword query to search for"
                        },
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional)"
                        }
                    },
                    Required = new[] { "query" }
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_query",
                Description = def.Description,
                Category = ToolCategory.Search,
                Tags = new[] { "search", "gitnexus" },
                TimeoutSeconds = 30,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_query", "query", def.Description, def, meta, pm);
        }

        private static McpBridgeTool CreateDetectChangesTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_detect_changes",
                Description = "Analyze uncommitted git changes and find affected execution flows.\n" +
                    "Maps git diff hunks to indexed symbols, then traces which processes are impacted.\n\n" +
                    "WHEN TO USE: Before committing — to understand what your changes affect. Pre-commit review, PR preparation.\n" +
                    "AFTER THIS: Review affected processes. Use context() on high-risk symbols.\n\n" +
                    "Returns: changed symbols, affected processes, and a risk summary.",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional)"
                        }
                    },
                    Required = Array.Empty<string>()
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_detect_changes",
                Description = def.Description,
                Category = ToolCategory.Analysis,
                Tags = new[] { "search", "analysis", "gitnexus" },
                TimeoutSeconds = 30,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_detect_changes", "detect_changes", def.Description, def, meta, pm);
        }

        private static McpBridgeTool CreateRenameTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_rename",
                Description = "Multi-file coordinated rename using the knowledge graph + text search.\n" +
                    "Finds all references via graph (high confidence) and regex text search (lower confidence). Preview by default.\n\n" +
                    "WHEN TO USE: Renaming a function, class, method, or variable across the codebase. Safer than find-and-replace.\n" +
                    "AFTER THIS: Run detect_changes() to verify no unexpected side effects.\n\n" +
                    "Each edit is tagged with confidence:\n- \"graph\": found via knowledge graph (high confidence, safe to accept)\n- \"text_search\": found via regex (lower confidence, review carefully)",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["old_name"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Current symbol name to rename"
                        },
                        ["new_name"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "New name for the symbol"
                        },
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional)"
                        }
                    },
                    Required = new[] { "old_name", "new_name" }
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_rename",
                Description = def.Description,
                Category = ToolCategory.FileWrite,
                Tags = new[] { "modify", "refactor", "gitnexus" },
                RequiresConfirmation = true,
                IsModifying = true,
                TimeoutSeconds = 60,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_rename", "rename", def.Description, def, meta, pm);
        }

        private static McpBridgeTool CreateCypherTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_cypher",
                // v2.1 O10 [C2]: Trimmed from ~2762 chars to ~600 chars. Removed EXAMPLES section.
                // Full schema with examples available via gitnexus://setup MCP Resource.
                Description = "Execute Cypher query against the code knowledge graph.\n\n" +
                    "WHEN TO USE: Complex structural queries that gitnexus_query/gitnexus_context can't answer.\n" +
                    "AFTER THIS: Use gitnexus_context on result symbols for deeper context.\n\n" +
                    "SCHEMA:\n" +
                    "- Nodes: File, Folder, Function, Class, Interface, Method, CodeElement, Community, Process\n" +
                    "- All edges via single CodeRelation table with 'type' property\n" +
                    "- Edge types: CONTAINS, DEFINES, CALLS, IMPORTS, EXTENDS, IMPLEMENTS, HAS_METHOD, HAS_PROPERTY, ACCESSES, OVERRIDES, MEMBER_OF, STEP_IN_PROCESS\n\n" +
                    "OUTPUT: Returns { markdown, row_count } — results formatted as a Markdown table.",
                Parameters = new ToolParameters
                {
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["query"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Cypher query to execute against the code knowledge graph"
                        },
                        ["repo"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Repository name (optional)"
                        }
                    },
                    Required = new[] { "query" }
                }
            };

            var meta = new ToolMetadata
            {
                Name = "gitnexus_cypher",
                Description = def.Description,
                Category = ToolCategory.Analysis,
                Tags = new[] { "search", "analysis", "gitnexus" },
                TimeoutSeconds = 30,
                RequiresNetwork = false
            };

            return new McpBridgeTool("gitnexus_cypher", "cypher", def.Description, def, meta, pm);
        }
    }
}
