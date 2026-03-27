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
        private readonly Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> _fallbackHandler;
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
            IGitNexusProcessManager processManager,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallbackHandler)
        {
            Name = aicaName;
            _mcpToolName = mcpToolName;
            Description = description;
            _definition = definition;
            _metadata = metadata;
            _processManager = processManager;
            _fallbackHandler = fallbackHandler;
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
                // Fallback if available, otherwise error
                if (_fallbackHandler != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus unavailable, falling back for {Name}");
                    return await _fallbackHandler(call, context, uiContext, ct).ConfigureAwait(false);
                }
                return ToolResult.Fail($"GitNexus MCP server is not available. Tool '{Name}' cannot execute without GitNexus.");
            }

            // Step 2: Forward arguments to MCP tool
            var mcpArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (call.Arguments != null)
            {
                foreach (var kvp in call.Arguments)
                {
                    mcpArgs[kvp.Key] = kvp.Value;
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
        /// Create all 6 GitNexus bridge tools.
        /// </summary>
        /// <param name="processManager">GitNexus process manager (injected for testability).</param>
        /// <param name="grepFallback">
        /// Optional fallback for search-type tools when GitNexus is unavailable.
        /// Typically a delegate that invokes GrepSearchTool.
        /// </param>
        public static IReadOnlyList<McpBridgeTool> CreateAllTools(
            IGitNexusProcessManager processManager,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback = null)
        {
            if (processManager == null)
                throw new ArgumentNullException(nameof(processManager));

            return new[]
            {
                CreateContextTool(processManager, grepFallback),
                CreateImpactTool(processManager, grepFallback),
                CreateQueryTool(processManager, grepFallback),
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
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback = null,
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
                return BuildToolsFromNativeDefinitions(mcpLookup, processManager, grepFallback);
            }

            // Fallback to hardcoded definitions
            return CreateAllTools(processManager, grepFallback);
        }

        /// <summary>
        /// Build bridge tools using native MCP tool definitions.
        /// Preserves AICA-specific ToolMetadata (Category, Tags, Timeout, etc.).
        /// </summary>
        private static IReadOnlyList<McpBridgeTool> BuildToolsFromNativeDefinitions(
            Dictionary<string, McpToolDefinition> mcpLookup,
            IGitNexusProcessManager pm,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback)
        {
            // Map: AICA tool name → MCP tool name → ToolMetadata factory
            var toolSpecs = new[]
            {
                new { AicaName = "gitnexus_context",        McpName = "context",        Category = ToolCategory.Analysis, Tags = new[] { "search", "read", "context", "gitnexus" },  Fallback = grepFallback, IsModifying = false, RequiresConfirmation = false, Timeout = 30 },
                new { AicaName = "gitnexus_impact",         McpName = "impact",         Category = ToolCategory.Analysis, Tags = new[] { "search", "analysis", "gitnexus" },         Fallback = grepFallback, IsModifying = false, RequiresConfirmation = false, Timeout = 30 },
                new { AicaName = "gitnexus_query",          McpName = "query",          Category = ToolCategory.Search,   Tags = new[] { "search", "gitnexus" },                     Fallback = grepFallback, IsModifying = false, RequiresConfirmation = false, Timeout = 30 },
                new { AicaName = "gitnexus_detect_changes", McpName = "detect_changes", Category = ToolCategory.Analysis, Tags = new[] { "search", "analysis", "gitnexus" },         Fallback = (Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>>)null, IsModifying = false, RequiresConfirmation = false, Timeout = 30 },
                new { AicaName = "gitnexus_rename",         McpName = "rename",         Category = ToolCategory.FileWrite, Tags = new[] { "modify", "refactor", "gitnexus" },        Fallback = (Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>>)null, IsModifying = true,  RequiresConfirmation = true,  Timeout = 60 },
                new { AicaName = "gitnexus_cypher",         McpName = "cypher",         Category = ToolCategory.Analysis, Tags = new[] { "search", "analysis", "gitnexus" },         Fallback = (Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>>)null, IsModifying = false, RequiresConfirmation = false, Timeout = 30 },
            };

            var tools = new List<McpBridgeTool>();
            foreach (var spec in toolSpecs)
            {
                // Use native definition if available, otherwise skip (caller already has hardcoded fallback)
                if (!mcpLookup.TryGetValue(spec.McpName, out var mcpDef))
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] McpBridgeTool: No native definition for '{spec.McpName}', skipping");
                    continue;
                }

                var nativeDesc = mcpDef.Description ?? spec.AicaName;
                // Truncate overly long descriptions to prevent function calling token overflow
                if (nativeDesc.Length > 4000)
                {
                    nativeDesc = nativeDesc.Substring(0, 4000) + "...";
                }

                var def = new ToolDefinition
                {
                    Name = spec.AicaName,
                    Description = nativeDesc,
                    Parameters = ConvertMcpSchema(mcpDef.InputSchema)
                };

                var meta = new ToolMetadata
                {
                    Name = spec.AicaName,
                    Description = nativeDesc,
                    Category = spec.Category,
                    Tags = spec.Tags,
                    TimeoutSeconds = spec.Timeout,
                    RequiresNetwork = false,
                    IsModifying = spec.IsModifying,
                    RequiresConfirmation = spec.RequiresConfirmation
                };

                tools.Add(new McpBridgeTool(spec.AicaName, spec.McpName, nativeDesc, def, meta, pm, spec.Fallback));
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
            IGitNexusProcessManager pm,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallback)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_context",
                Description = "Get 360° symbol context: callers, callees, execution flow participation, and related symbols. Use this to deeply understand how a function, class, or variable is used across the codebase.",
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

            return new McpBridgeTool("gitnexus_context", "context", def.Description, def, meta, pm, fallback);
        }

        private static McpBridgeTool CreateImpactTool(
            IGitNexusProcessManager pm,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallback)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_impact",
                Description = "Analyze blast radius: what code will be affected or broken if a symbol is changed. Use before refactoring to understand downstream impact.",
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

            return new McpBridgeTool("gitnexus_impact", "impact", def.Description, def, meta, pm, fallback);
        }

        private static McpBridgeTool CreateQueryTool(
            IGitNexusProcessManager pm,
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallback)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_query",
                Description = "Hybrid code search combining BM25 keyword matching, semantic search, and reciprocal rank fusion (RRF). More powerful than grep for understanding code intent.",
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

            return new McpBridgeTool("gitnexus_query", "query", def.Description, def, meta, pm, fallback);
        }

        private static McpBridgeTool CreateDetectChangesTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_detect_changes",
                Description = "Map git diff changes to affected execution flows. Shows which features and code paths are impacted by recent changes.",
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

            // No fallback — returns error if unavailable
            return new McpBridgeTool("gitnexus_detect_changes", "detect_changes", def.Description, def, meta, pm, null);
        }

        private static McpBridgeTool CreateRenameTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_rename",
                Description = "Coordinated multi-file rename: renames a symbol across all files that reference it. Requires user confirmation before applying changes.",
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

            // No fallback — rename requires GitNexus
            return new McpBridgeTool("gitnexus_rename", "rename", def.Description, def, meta, pm, null);
        }

        private static McpBridgeTool CreateCypherTool(IGitNexusProcessManager pm)
        {
            var def = new ToolDefinition
            {
                Name = "gitnexus_cypher",
                Description = "Execute raw Cypher queries against the GitNexus code knowledge graph. Powerful for complex structural queries like finding all classes that implement an interface, call chains, or dependency patterns.",
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

            // No fallback — Cypher requires GitNexus
            return new McpBridgeTool("gitnexus_cypher", "cypher", def.Description, def, meta, pm, null);
        }
    }
}
