using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Config;
using AICA.Core.Logging;
using AICA.Core.Security;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// Middleware that checks tool permissions before execution.
    /// v2.1 H3b: Queries persistent decisions before prompting user.
    /// </summary>
    public class PermissionCheckMiddleware : IToolExecutionMiddleware
    {
        private readonly IPermissionHandler _permissionHandler;
        private readonly ILogger<PermissionCheckMiddleware> _logger;
        private readonly TelemetryLogger _telemetryLogger;
        private readonly string _sessionId;
        private SafetyGuard _safetyGuard;

        /// <summary>
        /// v2.1 H3b: Late-bind SafetyGuard after construction (VSIX creates middleware
        /// before VSAgentContext/SafetyGuard are initialized).
        /// </summary>
        public SafetyGuard SafetyGuard
        {
            get => _safetyGuard;
            set => _safetyGuard = value;
        }

        public PermissionCheckMiddleware(
            IPermissionHandler permissionHandler,
            ILogger<PermissionCheckMiddleware> logger = null,
            TelemetryLogger telemetryLogger = null,
            string sessionId = null,
            SafetyGuard safetyGuard = null)
        {
            _permissionHandler = permissionHandler ?? throw new ArgumentNullException(nameof(permissionHandler));
            _logger = logger;
            _telemetryLogger = telemetryLogger;
            _sessionId = sessionId;
            _safetyGuard = safetyGuard;
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
                    // v2.1 H3b: Check persistent decision first
                    if (AicaConfig.Current.Features.PermissionPersistence && _safetyGuard != null)
                    {
                        var persistent = _safetyGuard.CheckPersistentDecision(toolName);
                        if (persistent.HasValue)
                        {
                            _telemetryLogger?.LogEvent(_sessionId, "persistent_decision_used",
                                new Dictionary<string, object>
                                {
                                    { "tool", toolName },
                                    { "decision", persistent.Value ? "allow" : "deny" }
                                });

                            if (persistent.Value)
                            {
                                _logger?.LogDebug("Tool {ToolName} auto-allowed by persistent decision", toolName);
                                return await context.Next(ct).ConfigureAwait(false);
                            }
                            else
                            {
                                _logger?.LogDebug("Tool {ToolName} auto-denied by persistent decision", toolName);
                                return ToolResult.SecurityDenied(
                                    $"Tool execution denied by persistent decision: {toolName}");
                            }
                        }
                    }

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

        /// <summary>
        /// v2.1 H3b: Store a persistent permission decision for a tool in the current project.
        /// Called by the UI layer when user selects "Always Allow" or "Always Deny".
        /// Respects DangerousTools constraints.
        /// </summary>
        public bool StorePersistentDecision(string toolName, string decision)
        {
            if (_safetyGuard?.DecisionStore == null || !AicaConfig.Current.Features.PermissionPersistence)
                return false;

            // RunCommand (and other dangerous tools) cannot have "always allow"
            if (string.Equals(decision, "allow", StringComparison.OrdinalIgnoreCase)
                && SafetyGuard.DangerousTools.Contains(toolName))
            {
                _logger?.LogWarning("Cannot store 'always allow' for dangerous tool: {ToolName}", toolName);
                return false;
            }

            _safetyGuard.DecisionStore.Store(toolName, _safetyGuard.WorkingDirectory, decision);

            _telemetryLogger?.LogEvent(_sessionId, "persistent_decisions_count",
                new Dictionary<string, object>
                {
                    { "count", _safetyGuard.DecisionStore.GetDecisions(_safetyGuard.WorkingDirectory).Count }
                });

            return true;
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
