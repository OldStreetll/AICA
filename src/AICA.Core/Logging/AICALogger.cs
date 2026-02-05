using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Logging
{
    /// <summary>
    /// Simple logger for AICA operations
    /// </summary>
    public class AICALogger : ILogger
    {
        private readonly string _categoryName;
        private readonly LogLevel _minLevel;
        private readonly Action<string> _outputAction;
        private static readonly object _lock = new object();

        public AICALogger(string categoryName, LogLevel minLevel = LogLevel.Information, Action<string> outputAction = null)
        {
            _categoryName = categoryName;
            _minLevel = minLevel;
            _outputAction = outputAction;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {formatter(state, exception)}";
            
            if (exception != null)
            {
                message += Environment.NewLine + exception.ToString();
            }

            lock (_lock)
            {
                _outputAction?.Invoke(message);
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Logger factory for AICA
    /// </summary>
    public class AICALoggerFactory : ILoggerFactory
    {
        private readonly LogLevel _minLevel;
        private readonly Action<string> _outputAction;

        public AICALoggerFactory(LogLevel minLevel = LogLevel.Information, Action<string> outputAction = null)
        {
            _minLevel = minLevel;
            _outputAction = outputAction;
        }

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName)
        {
            return new AICALogger(categoryName, _minLevel, _outputAction);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Extension methods for logging
    /// </summary>
    public static class LoggerExtensions
    {
        public static void LogToolExecution(this ILogger logger, string toolName, bool success, string details = null)
        {
            if (success)
                logger.LogInformation("Tool {ToolName} executed successfully. {Details}", toolName, details ?? "");
            else
                logger.LogWarning("Tool {ToolName} failed. {Details}", toolName, details ?? "");
        }

        public static void LogAgentIteration(this ILogger logger, int iteration, string action)
        {
            logger.LogDebug("Agent iteration {Iteration}: {Action}", iteration, action);
        }

        public static void LogLLMRequest(this ILogger logger, string model, int messageCount)
        {
            logger.LogDebug("LLM request to {Model} with {MessageCount} messages", model, messageCount);
        }

        public static void LogLLMResponse(this ILogger logger, int tokenCount, bool hasToolCalls)
        {
            logger.LogDebug("LLM response: {TokenCount} tokens, HasToolCalls={HasToolCalls}", tokenCount, hasToolCalls);
        }
    }
}
