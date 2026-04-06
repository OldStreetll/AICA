using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// Middleware that checks tool permissions before execution
    /// </summary>
    public class PermissionCheckMiddleware : IToolExecutionMiddleware
    {
        private readonly IPermissionHandler _permissionHandler;
        private readonly ILogger<PermissionCheckMiddleware> _logger;

        public PermissionCheckMiddleware(
            IPermissionHandler permissionHandler,
            ILogger<PermissionCheckMiddleware> logger = null)
        {
            _permissionHandler = permissionHandler ?? throw new ArgumentNullException(nameof(permissionHandler));
            _logger = logger;
        }

        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var toolName = context.Tool?.Name ?? "unknown";

            try
            {
                _logger?.LogDebug("Checking permissions for tool: {ToolName}", toolName);

                // Check if tool requires approval
                if (context.Metadata?.RequiresApproval ?? false)
                {
                    _logger?.LogDebug("Tool {ToolName} requires approval", toolName);

                    var approved = await _permissionHandler.RequestApprovalAsync(
                        context.Tool,
                        context.Call,
                        context.UIContext,
                        ct).ConfigureAwait(false);

                    if (!approved)
                    {
                        _logger?.LogWarning("Tool {ToolName} execution denied by user", toolName);
                        return ToolResult.Fail($"Tool execution denied: {toolName}");
                    }
                }

                // Check if tool requires confirmation
                if (context.Metadata?.RequiresConfirmation ?? false)
                {
                    _logger?.LogDebug("Tool {ToolName} requires confirmation", toolName);

                    var confirmed = await _permissionHandler.RequestConfirmationAsync(
                        context.Tool,
                        context.Call,
                        context.UIContext,
                        ct).ConfigureAwait(false);

                    if (!confirmed)
                    {
                        _logger?.LogWarning("Tool {ToolName} execution not confirmed by user", toolName);
                        return ToolResult.Fail($"Tool execution not confirmed: {toolName}");
                    }
                }

                _logger?.LogDebug("Permissions granted for tool: {ToolName}", toolName);

                // Continue to next middleware
                return await context.Next(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Permission check cancelled for tool: {ToolName}", toolName);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Permission check failed for tool: {ToolName}", toolName);
                return ToolResult.Fail($"Permission check failed: {ex.Message}");
            }
        }
    }
}
