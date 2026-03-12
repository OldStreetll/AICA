using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// Metrics for a single tool execution
    /// </summary>
    public class ToolExecutionMetrics
    {
        public string ToolName { get; set; }
        public DateTime ExecutionTime { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Middleware that tracks execution metrics and statistics
    /// </summary>
    public class MonitoringMiddleware : IToolExecutionMiddleware
    {
        private readonly ILogger<MonitoringMiddleware> _logger;
        private readonly ConcurrentBag<ToolExecutionMetrics> _metrics;

        public MonitoringMiddleware(ILogger<MonitoringMiddleware> logger = null)
        {
            _logger = logger;
            _metrics = new ConcurrentBag<ToolExecutionMetrics>();
        }

        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var toolName = context.Tool?.Name ?? "unknown";
            var startTime = DateTime.UtcNow;

            try
            {
                // Execute the tool
                var result = await context.Next(ct).ConfigureAwait(false);

                var elapsed = DateTime.UtcNow - startTime;
                var metrics = new ToolExecutionMetrics
                {
                    ToolName = toolName,
                    ExecutionTime = startTime,
                    ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
                    Success = result?.Success ?? false,
                    ErrorMessage = result?.Success == false ? result?.Error : null
                };

                _metrics.Add(metrics);

                _logger?.LogDebug(
                    "Tool metrics recorded: {ToolName} (Success: {Success}, Elapsed: {ElapsedMs}ms)",
                    toolName, metrics.Success, metrics.ElapsedMilliseconds);

                return result;
            }
            catch (OperationCanceledException)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var metrics = new ToolExecutionMetrics
                {
                    ToolName = toolName,
                    ExecutionTime = startTime,
                    ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
                    Success = false,
                    ErrorMessage = "Cancelled"
                };

                _metrics.Add(metrics);
                throw;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var metrics = new ToolExecutionMetrics
                {
                    ToolName = toolName,
                    ExecutionTime = startTime,
                    ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
                    Success = false,
                    ErrorMessage = SanitizeErrorMessage(ex.Message)
                };

                _metrics.Add(metrics);
                throw;
            }
        }

        /// <summary>
        /// Get all recorded metrics
        /// </summary>
        public ConcurrentBag<ToolExecutionMetrics> GetMetrics() => _metrics;

        /// <summary>
        /// Get metrics for a specific tool
        /// </summary>
        public System.Collections.Generic.IEnumerable<ToolExecutionMetrics> GetMetricsForTool(string toolName)
        {
            return _metrics.Where(m => m.ToolName == toolName);
        }

        /// <summary>
        /// Get average execution time for a tool
        /// </summary>
        public long GetAverageExecutionTime(string toolName)
        {
            var toolMetrics = GetMetricsForTool(toolName).ToList();
            return toolMetrics.Count > 0 ? (long)toolMetrics.Average(m => m.ElapsedMilliseconds) : 0;
        }

        /// <summary>
        /// Get success rate for a tool
        /// </summary>
        public double GetSuccessRate(string toolName)
        {
            var toolMetrics = GetMetricsForTool(toolName).ToList();
            if (toolMetrics.Count == 0) return 0;
            return (double)toolMetrics.Count(m => m.Success) / toolMetrics.Count;
        }

        /// <summary>
        /// Clear all metrics
        /// </summary>
        public void ClearMetrics()
        {
            while (_metrics.TryTake(out _)) { }
        }

        /// <summary>
        /// Sanitize error messages to prevent information disclosure
        /// </summary>
        private string SanitizeErrorMessage(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return "Unknown error";

            // Remove file paths
            errorMessage = System.Text.RegularExpressions.Regex.Replace(
                errorMessage,
                @"[A-Za-z]:\\[^\\/:*?""<>|\r\n]*",
                "[PATH]");

            // Remove Unix paths
            errorMessage = System.Text.RegularExpressions.Regex.Replace(
                errorMessage,
                @"/[^/\s]*(/[^/\s]*)*",
                "[PATH]");

            // Remove IP addresses
            errorMessage = System.Text.RegularExpressions.Regex.Replace(
                errorMessage,
                @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
                "[IP]");

            // Remove URLs
            errorMessage = System.Text.RegularExpressions.Regex.Replace(
                errorMessage,
                @"https?://[^\s]+",
                "[URL]");

            // Truncate if too long
            if (errorMessage.Length > 200)
                errorMessage = errorMessage.Substring(0, 200) + "...";

            return errorMessage;
        }
    }
}
