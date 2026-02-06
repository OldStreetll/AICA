using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;
using AICA.Core.Prompt;
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
            // Build system prompt with tool definitions
            var toolDefinitions = _toolDispatcher.GetToolDefinitions();
            var systemPrompt = SystemPromptBuilder.GetDefaultPrompt(
                context?.WorkingDirectory ?? Environment.CurrentDirectory,
                toolDefinitions);

            var conversationHistory = new List<ChatMessage>
            {
                ChatMessage.System(systemPrompt),
                ChatMessage.User(userRequest)
            };

            var iteration = 0;

            while (iteration < _maxIterations && !ct.IsCancellationRequested)
            {
                iteration++;
                _logger?.LogDebug("Agent iteration {Iteration}", iteration);
                System.Diagnostics.Debug.WriteLine($"[AICA] Agent iteration {iteration}");

                // Get LLM response - collect text chunks to yield after try-catch
                string assistantResponse = null;
                var toolCalls = new List<ToolCall>();
                var pendingTextChunks = new List<string>();
                string streamError = null;
                bool wasCancelled = false;

                try
                {
                    await foreach (var chunk in _llmClient.StreamChatAsync(conversationHistory, toolDefinitions, ct))
                    {
                        if (chunk.Type == LLMChunkType.Text)
                        {
                            assistantResponse = (assistantResponse ?? string.Empty) + chunk.Text;
                            pendingTextChunks.Add(chunk.Text);
                        }
                        else if (chunk.Type == LLMChunkType.ToolCall)
                        {
                            toolCalls.Add(chunk.ToolCall);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] LLM stream error: {ex.Message}");
                    streamError = ex.Message;
                }

                // Yield collected text chunks
                foreach (var text in pendingTextChunks)
                {
                    yield return AgentStep.TextChunk(text);
                }

                // Handle errors after yielding text
                if (wasCancelled)
                {
                    yield return AgentStep.WithError("Operation cancelled.");
                    yield break;
                }
                if (streamError != null)
                {
                    yield return AgentStep.WithError($"LLM communication error: {streamError}");
                    yield break;
                }

                // Build assistant message with tool calls for proper conversation history
                var assistantMsg = ChatMessage.Assistant(assistantResponse);
                if (toolCalls.Count > 0)
                {
                    assistantMsg.ToolCalls = new System.Collections.Generic.List<ToolCallMessage>();
                    foreach (var tc in toolCalls)
                    {
                        assistantMsg.ToolCalls.Add(new ToolCallMessage
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new FunctionCall
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments != null 
                                    ? System.Text.Json.JsonSerializer.Serialize(tc.Arguments) 
                                    : "{}"
                            }
                        });
                    }
                }
                conversationHistory.Add(assistantMsg);

                // If no tool calls, we're done
                if (toolCalls.Count == 0)
                {
                    yield return AgentStep.Complete(assistantResponse);
                    yield break;
                }

                System.Diagnostics.Debug.WriteLine($"[AICA] Executing {toolCalls.Count} tool calls");

                // Execute tool calls
                foreach (var toolCall in toolCalls)
                {
                    yield return AgentStep.ToolStart(toolCall);

                    ToolResult result;
                    try
                    {
                        result = await _toolDispatcher.ExecuteAsync(toolCall, context, uiContext, ct);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Tool {toolCall.Name} exception: {ex.Message}");
                        result = ToolResult.Fail($"Tool execution error: {ex.Message}");
                    }

                    yield return AgentStep.WithToolResult(toolCall, result);

                    // Add tool result to conversation
                    var resultContent = result.Success ? result.Content : $"Error: {result.Error}";
                    // Truncate very long results to avoid token overflow
                    if (resultContent != null && resultContent.Length > 8000)
                    {
                        resultContent = resultContent.Substring(0, 8000) + "\n... (truncated, total length: " + resultContent.Length + " chars)";
                    }
                    conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, resultContent));
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
