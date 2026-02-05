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
        private readonly ILogger<ToolDispatcher> _logger;

        public ToolDispatcher(ILogger<ToolDispatcher> logger = null)
        {
            _logger = logger;
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
        /// Get all tool definitions for LLM
        /// </summary>
        public IEnumerable<ToolDefinition> GetToolDefinitions()
        {
            return _tools.Values.Select(t => t.GetDefinition());
        }

        /// <summary>
        /// Execute a tool call
        /// </summary>
        public async Task<ToolResult> ExecuteAsync(
            ToolCall call,
            IAgentContext context,
            IUIContext uiContext,
            CancellationToken ct = default)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));

            if (!_tools.TryGetValue(call.Name, out var tool))
            {
                _logger?.LogWarning("Unknown tool: {ToolName}", call.Name);
                return ToolResult.Fail($"Unknown tool: {call.Name}");
            }

            try
            {
                _logger?.LogDebug("Executing tool: {ToolName}", call.Name);
                var result = await tool.ExecuteAsync(call, context, ct);
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
            if (call == null || !_tools.TryGetValue(call.Name, out var tool))
                return;

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
    }
}
