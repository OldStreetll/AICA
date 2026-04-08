using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Config;
using AICA.Core.Logging;
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
        private readonly TelemetryLogger _telemetryLogger;
        private readonly string _sessionId;

        public PermissionCheckMiddleware(
            IPermissionHandler permissionHandler,
            ILogger<PermissionCheckMiddleware> logger = null,
            TelemetryLogger telemetryLogger = null,
            string sessionId = null)
        {
            _permissionHandler = permissionHandler ?? throw new ArgumentNullException(nameof(permissionHandler));
            _logger = logger;
            _telemetryLogger = telemetryLogger;
            _sessionId = sessionId;
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
                        return await BuildDenialResultAsync(toolName, context, ct).ConfigureAwait(false);
                    }
                }

                // RequiresConfirmation is handled by each tool internally with specialized UI
                // (diff preview, command preview, etc.). Middleware only handles RequiresApproval
                // to avoid double-confirmation.

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

        private async Task<ToolResult> BuildDenialResultAsync(
            string toolName, ToolExecutionContext context, CancellationToken ct)
        {
            if (AicaConfig.Current.Features.PermissionFeedback && context.UIContext != null)
            {
                try
                {
                    var feedback = await context.UIContext.RequestDenialFeedbackAsync(
                        toolName, toolName, ct).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(feedback))
                    {
                        feedback = feedback.Substring(0, Math.Min(feedback.Length, 500));
                        LogPermissionDenied(true);
                        return ToolResult.SecurityDenied(
                            $"Permission denied. User feedback: {feedback}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to collect denial feedback for tool: {ToolName}", toolName);
                }
            }

            LogPermissionDenied(false);
            return ToolResult.SecurityDenied($"Tool execution denied: {toolName}");
        }

        private void LogPermissionDenied(bool withFeedback)
        {
            if (_telemetryLogger != null)
            {
                _telemetryLogger.LogEvent(_sessionId, "permission_denied_with_feedback",
                    new Dictionary<string, object>
                    {
                        { "with_feedback", withFeedback }
                    });
            }
        }
    }
}
