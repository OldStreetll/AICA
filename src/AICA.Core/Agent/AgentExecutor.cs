using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Core Agent executor that manages the Agent loop
    /// </summary>
    public class AgentExecutor : IAgentExecutor
    {
        private readonly ILLMClient _llmClient;
        private readonly ToolDispatcher _toolDispatcher;
        private readonly ILogger<AgentExecutor> _logger;
        private readonly int _maxIterations;

        public AgentExecutor(
            ILLMClient llmClient,
            ToolDispatcher toolDispatcher,
            ILogger<AgentExecutor> logger = null,
            int maxIterations = 50)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _toolDispatcher = toolDispatcher ?? throw new ArgumentNullException(nameof(toolDispatcher));
            _logger = logger;
            _maxIterations = maxIterations;
        }

        /// <summary>
        /// Execute a user request through the Agent loop
        /// </summary>
        public async IAsyncEnumerable<AgentStep> ExecuteAsync(
            string userRequest,
            IAgentContext context,
            IUIContext uiContext,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var conversationHistory = new List<ChatMessage>
            {
                ChatMessage.User(userRequest)
            };

            var iteration = 0;

            while (iteration < _maxIterations && !ct.IsCancellationRequested)
            {
                iteration++;
                _logger?.LogDebug("Agent iteration {Iteration}", iteration);

                // Get LLM response
                var toolDefinitions = _toolDispatcher.GetToolDefinitions();
                string assistantResponse = null;
                var toolCalls = new List<ToolCall>();

                await foreach (var chunk in _llmClient.StreamChatAsync(conversationHistory, toolDefinitions, ct))
                {
                    if (chunk.Type == LLMChunkType.Text)
                    {
                        assistantResponse = (assistantResponse ?? string.Empty) + chunk.Text;
                        yield return AgentStep.TextChunk(chunk.Text);
                    }
                    else if (chunk.Type == LLMChunkType.ToolCall)
                    {
                        toolCalls.Add(chunk.ToolCall);
                    }
                }

                // Add assistant message to history
                if (!string.IsNullOrEmpty(assistantResponse))
                {
                    conversationHistory.Add(ChatMessage.Assistant(assistantResponse));
                }

                // If no tool calls, we're done
                if (toolCalls.Count == 0)
                {
                    yield return AgentStep.Complete(assistantResponse);
                    yield break;
                }

                // Execute tool calls
                foreach (var toolCall in toolCalls)
                {
                    yield return AgentStep.ToolStart(toolCall);

                    var result = await _toolDispatcher.ExecuteAsync(toolCall, context, uiContext, ct);

                    yield return AgentStep.WithToolResult(toolCall, result);

                    // Add tool result to conversation
                    conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, result.Success ? result.Content : result.Error));
                }
            }

            if (iteration >= _maxIterations)
            {
                yield return AgentStep.WithError("Maximum iterations reached. The Agent may be stuck in a loop.");
            }
        }

        public void Abort()
        {
            _llmClient.Abort();
        }
    }

    /// <summary>
    /// Interface for the Agent executor
    /// </summary>
    public interface IAgentExecutor
    {
        IAsyncEnumerable<AgentStep> ExecuteAsync(
            string userRequest,
            IAgentContext context,
            IUIContext uiContext,
            CancellationToken ct = default);

        void Abort();
    }

    /// <summary>
    /// Represents a step in the Agent execution
    /// </summary>
    public class AgentStep
    {
        public AgentStepType Type { get; set; }
        public string Text { get; set; }
        public ToolCall ToolCall { get; set; }
        public ToolResult Result { get; set; }
        public string ErrorMessage { get; set; }

        public static AgentStep TextChunk(string text) => new AgentStep { Type = AgentStepType.TextChunk, Text = text };
        public static AgentStep ToolStart(ToolCall call) => new AgentStep { Type = AgentStepType.ToolStart, ToolCall = call };
        public static AgentStep WithToolResult(ToolCall call, ToolResult result) => new AgentStep { Type = AgentStepType.ToolResult, ToolCall = call, Result = result };
        public static AgentStep Complete(string finalText) => new AgentStep { Type = AgentStepType.Complete, Text = finalText };
        public static AgentStep WithError(string error) => new AgentStep { Type = AgentStepType.Error, ErrorMessage = error };
    }

    public enum AgentStepType
    {
        TextChunk,
        ToolStart,
        ToolResult,
        Complete,
        Error
    }
}
