using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tests.Agent.Mocks;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Test harness that wires up AgentExecutor with mock components.
    /// Simplifies writing AgentExecutor integration tests.
    /// </summary>
    public class AgentEvalHarness
    {
        private readonly MockLlmClient _llmClient;
        private readonly MockAgentContext _agentContext;
        private readonly MockUIContext _uiContext;
        private readonly ToolDispatcher _toolDispatcher;
        private readonly AgentExecutor _executor;

        public AgentEvalHarness(
            IEnumerable<MockLlmResponse> responses,
            int maxIterations = 10,
            int maxTokenBudget = 32000)
        {
            _llmClient = new MockLlmClient(responses);
            _agentContext = new MockAgentContext();
            _uiContext = new MockUIContext();
            _toolDispatcher = new ToolDispatcher();

            // Register the real attempt_completion tool (returns TASK_COMPLETED: prefix that AgentExecutor checks)
            _toolDispatcher.RegisterTool(new AICA.Core.Tools.AttemptCompletionTool());

            _executor = new AgentExecutor(
                _llmClient,
                _toolDispatcher,
                logger: null,
                maxIterations: maxIterations,
                maxTokenBudget: maxTokenBudget);
        }

        /// <summary>
        /// Access the mock LLM client for assertions.
        /// </summary>
        public MockLlmClient LlmClient => _llmClient;

        /// <summary>
        /// Access the mock agent context for assertions.
        /// </summary>
        public MockAgentContext AgentContext => _agentContext;

        /// <summary>
        /// Access the mock UI context for assertions.
        /// </summary>
        public MockUIContext UIContext => _uiContext;

        /// <summary>
        /// Access the tool dispatcher for registering additional tools.
        /// </summary>
        public ToolDispatcher ToolDispatcher => _toolDispatcher;

        /// <summary>
        /// Access the executor for property inspection.
        /// </summary>
        public AgentExecutor Executor => _executor;

        /// <summary>
        /// Register a stub tool that returns a fixed result.
        /// </summary>
        public AgentEvalHarness WithTool(string name, string description = null, string resultContent = "OK")
        {
            _toolDispatcher.RegisterTool(new StubTool(name, description ?? name, resultContent));
            return this;
        }

        /// <summary>
        /// Register a stub tool that returns failure.
        /// </summary>
        public AgentEvalHarness WithFailingTool(string name, string error = "Tool failed")
        {
            _toolDispatcher.RegisterTool(new StubTool(name, name, error, success: false));
            return this;
        }

        /// <summary>
        /// Add a file to the mock file system.
        /// </summary>
        public AgentEvalHarness WithFile(string path, string content)
        {
            _agentContext.WithFile(path, content);
            return this;
        }

        /// <summary>
        /// Run a user request through the AgentExecutor and collect all steps.
        /// </summary>
        public async Task<List<AgentStep>> RunAsync(
            string userRequest,
            List<LLM.ChatMessage> previousMessages = null,
            CancellationToken ct = default)
        {
            var steps = new List<AgentStep>();
            await foreach (var step in _executor.ExecuteAsync(
                userRequest, _agentContext, _uiContext, previousMessages, ct))
            {
                steps.Add(step);
            }
            return steps;
        }

        // --- Assertion helpers ---

        /// <summary>
        /// Check if any step is a tool call with the given name.
        /// </summary>
        public static bool HasToolCall(List<AgentStep> steps, string toolName)
        {
            return steps.Any(s =>
                s.Type == AgentStepType.ToolStart && s.ToolCall?.Name == toolName);
        }

        /// <summary>
        /// Check if the execution completed (has a Complete step).
        /// </summary>
        public static bool IsCompleted(List<AgentStep> steps)
        {
            return steps.Any(s => s.Type == AgentStepType.Complete);
        }

        /// <summary>
        /// Check if there are any error steps.
        /// </summary>
        public static bool HasErrors(List<AgentStep> steps)
        {
            return steps.Any(s => s.Type == AgentStepType.Error);
        }

        /// <summary>
        /// Get all text chunks concatenated.
        /// </summary>
        public static string GetAllText(List<AgentStep> steps)
        {
            return string.Join("", steps
                .Where(s => s.Type == AgentStepType.TextChunk)
                .Select(s => s.Text));
        }

        /// <summary>
        /// Get the completion text.
        /// </summary>
        public static string GetCompletionText(List<AgentStep> steps)
        {
            var complete = steps.FirstOrDefault(s => s.Type == AgentStepType.Complete);
            return complete?.Text;
        }
    }

    /// <summary>
    /// Minimal IAgentTool implementation for testing.
    /// Returns a fixed result on execution.
    /// </summary>
    internal class StubTool : IAgentTool
    {
        private readonly string _resultContent;
        private readonly bool _success;

        public StubTool(string name, string description, string resultContent = "OK", bool success = true)
        {
            Name = name;
            Description = description;
            _resultContent = resultContent;
            _success = success;
        }

        public string Name { get; }
        public string Description { get; }

        public ToolDefinition GetDefinition()
        {
            return new ToolDefinition
            {
                Name = Name,
                Description = Description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new System.Collections.Generic.Dictionary<string, ToolParameterProperty>
                    {
                        ["result"] = new ToolParameterProperty { Type = "string", Description = "Result" }
                    },
                    Required = new string[0]
                }
            };
        }

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata { Name = Name, Description = Description };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            if (_success)
                return Task.FromResult(ToolResult.Ok(_resultContent));
            else
                return Task.FromResult(ToolResult.Fail(_resultContent));
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
