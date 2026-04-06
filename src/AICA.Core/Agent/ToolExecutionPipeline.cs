using System;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context passed through the tool execution pipeline
    /// </summary>
    public class ToolExecutionContext
    {
        /// <summary>
        /// The tool call being executed
        /// </summary>
        public ToolCall Call { get; set; }

        /// <summary>
        /// The tool being executed
        /// </summary>
        public IAgentTool Tool { get; set; }

        /// <summary>
        /// Metadata about the tool
        /// </summary>
        public ToolMetadata Metadata { get; set; }

        /// <summary>
        /// When the execution started
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the execution completed (null if still running)
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// The result of execution (null if still running)
        /// </summary>
        public ToolResult Result { get; set; }

        /// <summary>
        /// Any error that occurred during execution
        /// </summary>
        public ToolError Error { get; set; }

        /// <summary>
        /// Custom data that can be passed between middleware
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> State { get; set; } = new();

        /// <summary>
        /// Agent context for tool execution
        /// </summary>
        public IAgentContext AgentContext { get; set; }

        /// <summary>
        /// UI context for tool execution
        /// </summary>
        public IUIContext UIContext { get; set; }

        /// <summary>
        /// Delegate to call the next middleware or tool in the pipeline
        /// </summary>
        public System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ToolResult>> Next { get; set; }

        /// <summary>
        /// Get the elapsed time since execution started
        /// </summary>
        public TimeSpan Elapsed => (EndTime ?? DateTime.UtcNow) - StartTime;

        /// <summary>
        /// Check if execution has timed out
        /// </summary>
        public bool HasTimedOut
        {
            get
            {
                if (Metadata?.TimeoutSeconds == null)
                    return false;

                return Elapsed.TotalSeconds > Metadata.TimeoutSeconds.Value;
            }
        }
    }

    /// <summary>
    /// Middleware interface for tool execution pipeline
    /// </summary>
    public interface IToolExecutionMiddleware
    {
        /// <summary>
        /// Process a tool execution
        /// Return a ToolResult to short-circuit the pipeline, or null to continue
        /// </summary>
        System.Threading.Tasks.Task<ToolResult> ProcessAsync(ToolExecutionContext context, System.Threading.CancellationToken ct);
    }

    /// <summary>
    /// Pipeline for executing tools with middleware support
    /// Enables cross-cutting concerns like permission checking, timeout control, monitoring, etc.
    /// </summary>
    public class ToolExecutionPipeline
    {
        private readonly System.Collections.Generic.List<IToolExecutionMiddleware> _middlewares = new();
        private readonly Microsoft.Extensions.Logging.ILogger<ToolExecutionPipeline> _logger;

        public ToolExecutionPipeline(Microsoft.Extensions.Logging.ILogger<ToolExecutionPipeline> logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Register a middleware in the pipeline
        /// </summary>
        public void Use(IToolExecutionMiddleware middleware)
        {
            if (middleware == null)
                throw new ArgumentNullException(nameof(middleware));

            _middlewares.Add(middleware);
            _logger?.LogDebug("Registered middleware: {MiddlewareType}", middleware.GetType().Name);
        }

        /// <summary>
        /// Execute a tool through the pipeline
        /// </summary>
        public async System.Threading.Tasks.Task<ToolResult> ExecuteAsync(
            ToolCall call,
            IAgentTool tool,
            IAgentContext context,
            IUIContext uiContext,
            System.Threading.CancellationToken ct = default)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            if (tool == null)
                throw new ArgumentNullException(nameof(tool));

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (uiContext == null)
                throw new ArgumentNullException(nameof(uiContext));

            // Build the middleware chain from inside out:
            // The innermost function executes the actual tool.
            System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ToolResult>> coreExecution =
                async (token) => await tool.ExecuteAsync(call, context, uiContext, token).ConfigureAwait(false);

            // Create execution context
            var executionContext = new ToolExecutionContext
            {
                Call = call,
                Tool = tool,
                Metadata = tool.GetMetadata(),
                StartTime = DateTime.UtcNow,
                AgentContext = context,
                UIContext = uiContext,
                Next = coreExecution
            };

            try
            {
                // Wrap middleware in reverse order so the first registered middleware runs first
                var chain = coreExecution;
                for (int i = _middlewares.Count - 1; i >= 0; i--)
                {
                    var middleware = _middlewares[i];
                    var nextInChain = chain;
                    chain = async (token) =>
                    {
                        executionContext.Next = nextInChain;
                        _logger?.LogDebug("Processing middleware: {MiddlewareType}", middleware.GetType().Name);
                        return await middleware.ProcessAsync(executionContext, token).ConfigureAwait(false);
                    };
                }

                // Execute the chain
                _logger?.LogDebug("Executing tool: {ToolName}", tool.Name);
                var toolResult = await chain(ct).ConfigureAwait(false);

                executionContext.Result = toolResult;
                executionContext.EndTime = DateTime.UtcNow;

                _logger?.LogDebug("Tool {ToolName} completed: Success={Success}, Elapsed={Elapsed}ms",
                    tool.Name, toolResult?.Success, executionContext.Elapsed.TotalMilliseconds);

                return toolResult;
            }
            catch (OperationCanceledException ex)
            {
                executionContext.EndTime = DateTime.UtcNow;
                executionContext.Error = ToolErrorHandler.Cancelled(call.Name);
                _logger?.LogWarning(ex, "Tool {ToolName} was cancelled", call.Name);
                return ToolErrorHandler.HandleError(executionContext.Error);
            }
            catch (Exception ex)
            {
                executionContext.EndTime = DateTime.UtcNow;
                executionContext.Error = ToolErrorHandler.ClassifyException(ex, call.Name);
                _logger?.LogError(ex, "Tool {ToolName} failed with exception", call.Name);
                return ToolErrorHandler.HandleError(executionContext.Error);
            }
        }

        /// <summary>
        /// Get the number of registered middleware
        /// </summary>
        public int MiddlewareCount => _middlewares.Count;
    }
}
