using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// Middleware that enforces timeout limits on tool execution
    /// </summary>
    public class TimeoutMiddleware : IToolExecutionMiddleware
    {
        private readonly ILogger<TimeoutMiddleware> _logger;
        private readonly int _defaultTimeoutSeconds;

        public TimeoutMiddleware(
            int defaultTimeoutSeconds = 60,
            ILogger<TimeoutMiddleware> logger = null)
        {
            if (defaultTimeoutSeconds <= 0)
                throw new ArgumentException("Timeout must be positive", nameof(defaultTimeoutSeconds));

            _defaultTimeoutSeconds = defaultTimeoutSeconds;
            _logger = logger;
        }

        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var toolName = context.Tool?.Name ?? "unknown";
            var timeoutSeconds = context.Metadata?.TimeoutSeconds ?? _defaultTimeoutSeconds;

            try
            {
                _logger?.LogDebug("Executing tool {ToolName} with timeout: {TimeoutSeconds}s", toolName, timeoutSeconds);

                // Create a cancellation token with timeout
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    try
                    {
                        // Execute with timeout
                        var result = await context.Next(timeoutCts.Token).ConfigureAwait(false);
                        _logger?.LogDebug("Tool {ToolName} completed within timeout", toolName);
                        return result;
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        // Timeout occurred
                        _logger?.LogWarning("Tool {ToolName} exceeded timeout of {TimeoutSeconds}s", toolName, timeoutSeconds);
                        return ToolResult.Fail($"Tool execution timeout: {toolName} exceeded {timeoutSeconds} seconds");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Tool {ToolName} execution cancelled", toolName);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Timeout middleware error for tool: {ToolName}", toolName);
                throw;
            }
        }
    }
}
