using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// Middleware that logs tool execution lifecycle events
    /// </summary>
    public class LoggingMiddleware : IToolExecutionMiddleware
    {
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(ILogger<LoggingMiddleware> logger = null)
        {
            _logger = logger;
        }

        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var toolName = context.Tool?.Name ?? "unknown";
            var callId = context.Call?.Id ?? "unknown";

            try
            {
                _logger?.LogInformation(
                    "Tool execution started: {ToolName} (CallId: {CallId})",
                    toolName, SanitizeCallId(callId));

                var startTime = DateTime.UtcNow;

                try
                {
                    // Execute the tool
                    var result = await context.Next(ct).ConfigureAwait(false);

                    var elapsed = DateTime.UtcNow - startTime;
                    var success = result?.Success ?? false;

                    _logger?.LogInformation(
                        "Tool execution completed: {ToolName} (CallId: {CallId}, Success: {Success}, Elapsed: {ElapsedMs}ms)",
                        toolName, SanitizeCallId(callId), success, elapsed.TotalMilliseconds);

                    return result;
                }
                catch (OperationCanceledException)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    _logger?.LogWarning(
                        "Tool execution cancelled: {ToolName} (CallId: {CallId}, Elapsed: {ElapsedMs}ms)",
                        toolName, SanitizeCallId(callId), elapsed.TotalMilliseconds);
                    throw;
                }
                catch (Exception ex)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    _logger?.LogError(
                        ex,
                        "Tool execution failed: {ToolName} (CallId: {CallId}, Elapsed: {ElapsedMs}ms)",
                        toolName, SanitizeCallId(callId), elapsed.TotalMilliseconds);
                    throw;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogError(ex, "Logging middleware error for tool: {ToolName}", toolName);
                throw;
            }
        }

        /// <summary>
        /// Sanitize call ID to prevent information disclosure
        /// </summary>
        private string SanitizeCallId(string callId)
        {
            if (string.IsNullOrEmpty(callId))
                return "unknown";

            // Only keep first 8 characters for identification, mask the rest
            if (callId.Length > 8)
                return callId.Substring(0, 8) + "***";

            return callId;
        }
    }
}
