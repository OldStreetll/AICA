using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Dispatches tool calls to the appropriate tool handlers
    /// </summary>
    public class ToolDispatcher
    {
        private readonly Dictionary<string, IAgentTool> _tools = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolExecutionPipeline _pipeline;
        private readonly ILogger<ToolDispatcher> _logger;
        private TaskCompletionSource<bool> _mcpUpgradeTcs;

        /// <summary>
        /// v2.1 O11: Alias mapping for LLM tool name hallucinations.
        /// Maps common tool names from LLM training data to AICA's actual tool names.
        /// Only used when exact name lookup fails — does not affect normal tool calls.
        /// NOTE: If a future MCP server registers a tool with an alias name, the alias
        /// will not trigger (exact lookup succeeds first). Review if adding new MCP servers.
        /// </summary>
        private static readonly Dictionary<string, string> ToolAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["grep"] = "grep_search",
            ["bash"] = "run_command",
            ["shell"] = "run_command",
            ["search"] = "grep_search",
            ["find"] = "glob",
            ["cat"] = "read_file",
            ["read"] = "read_file",
            ["write"] = "write_file",
            ["ls"] = "list_dir",
            ["list"] = "list_dir",
        };

        public ToolDispatcher(ILogger<ToolDispatcher> logger = null)
        {
            _logger = logger;
            _pipeline = new ToolExecutionPipeline();
        }

        /// <summary>
        /// Create a gate that callers can await to ensure MCP native definitions are loaded.
        /// Call SignalMcpUpgradeComplete() when the background upgrade finishes (success or failure).
        /// </summary>
        public void BeginMcpUpgrade()
        {
            _mcpUpgradeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Signal that the MCP upgrade has completed (success or failure).
        /// </summary>
        public void SignalMcpUpgradeComplete()
        {
            _mcpUpgradeTcs?.TrySetResult(true);
        }

        /// <summary>
        /// Wait for MCP native definitions to be loaded. Returns immediately if no upgrade is pending.
        /// </summary>
        /// <param name="timeoutMs">Maximum wait time in milliseconds (default 5000)</param>
        public async Task WaitForMcpUpgradeAsync(int timeoutMs = 5000)
        {
            if (_mcpUpgradeTcs == null) return;

            var completed = await Task.WhenAny(
                _mcpUpgradeTcs.Task,
                Task.Delay(timeoutMs)).ConfigureAwait(false);

            if (completed != _mcpUpgradeTcs.Task)
            {
                _logger?.LogWarning("MCP upgrade timed out after {Timeout}ms, proceeding with current definitions", timeoutMs);
            }
        }

        /// <summary>
        /// Register a tool
        /// </summary>
        public void RegisterTool(IAgentTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _tools[tool.Name] = tool;
            _logger?.LogDebug("Registered tool: {ToolName}", tool.Name);
        }

        /// <summary>
        /// Register middleware in the execution pipeline
        /// </summary>
        public void UseMiddleware(IToolExecutionMiddleware middleware)
        {
            if (middleware == null) throw new ArgumentNullException(nameof(middleware));
            _pipeline.Use(middleware);
            _logger?.LogDebug("Registered middleware: {MiddlewareType}", middleware.GetType().Name);
        }

        /// <summary>
        /// Get all tool definitions for LLM
        /// </summary>
        public IEnumerable<ToolDefinition> GetToolDefinitions()
        {
            return _tools.Values.Select(t => t.GetDefinition());
        }

        /// <summary>
        /// v2.1 O11: Resolve tool by name with alias fallback.
        /// Returns null if tool not found and no alias matches.
        /// If alias resolved, updates call.Name via out parameter.
        /// </summary>
        private IAgentTool ResolveTool(string name, out string resolvedName)
        {
            resolvedName = name;
            if (_tools.TryGetValue(name, out var tool))
                return tool;

            // Alias fallback
            if (ToolAliases.TryGetValue(name, out var realName) && _tools.TryGetValue(realName, out tool))
            {
                _logger?.LogInformation("Tool alias resolved: {Alias} → {RealName}", name, realName);
                resolvedName = realName;
                return tool;
            }

            return null;
        }

        /// <summary>
        /// Execute a tool call through the pipeline
        /// </summary>
        public async Task<ToolResult> ExecuteAsync(
            ToolCall call,
            IAgentContext context,
            IUIContext uiContext,
            CancellationToken ct = default)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));

            var tool = ResolveTool(call.Name, out var resolvedName);
            if (tool == null)
            {
                _logger?.LogWarning("Unknown tool: {ToolName}", call.Name);
                return ToolResult.Fail($"Unknown tool: {call.Name}");
            }

            // Apply resolved name if alias was used
            if (resolvedName != call.Name)
            {
                call = new ToolCall { Id = call.Id, Name = resolvedName, Arguments = call.Arguments };
            }

            try
            {
                _logger?.LogDebug("Executing tool: {ToolName}", call.Name);
                var result = await _pipeline.ExecuteAsync(call, tool, context, uiContext, ct).ConfigureAwait(false);
                _logger?.LogDebug("Tool {ToolName} completed: Success={Success}", call.Name, result.Success);
                return result;
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Operation was cancelled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Tool {ToolName} failed with exception", call.Name);
                return ToolResult.Fail($"Tool execution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle partial tool call during streaming
        /// </summary>
        public async Task HandlePartialAsync(
            ToolCall call,
            IUIContext uiContext,
            CancellationToken ct = default)
        {
            if (call == null) return;
            var tool = ResolveTool(call.Name, out _);
            if (tool == null) return;

            try
            {
                await tool.HandlePartialAsync(call, uiContext, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Partial handling for {ToolName} failed", call.Name);
            }
        }

        /// <summary>
        /// Check if a tool is registered
        /// </summary>
        public bool HasTool(string name) => _tools.ContainsKey(name);

        /// <summary>
        /// Get registered tool names
        /// </summary>
        public IEnumerable<string> GetToolNames() => _tools.Keys;

        /// <summary>
        /// Get a tool by name
        /// </summary>
        public IAgentTool GetTool(string name)
        {
            _tools.TryGetValue(name, out var tool);
            return tool;
        }

        /// <summary>
        /// Get the execution pipeline (for advanced middleware configuration)
        /// </summary>
        public ToolExecutionPipeline Pipeline => _pipeline;
    }
}
