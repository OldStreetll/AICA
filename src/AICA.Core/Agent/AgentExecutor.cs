using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

                // Fallback: parse text-based tool calls if model outputs XML format
                if (toolCalls.Count == 0 && !string.IsNullOrEmpty(assistantResponse))
                {
                    var parsedCalls = TryParseTextToolCalls(assistantResponse);
                    if (parsedCalls.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Parsed {parsedCalls.Count} text-based tool call(s) from response");
                        toolCalls.AddRange(parsedCalls);

                        // Rebuild assistant message with parsed tool calls
                        assistantMsg.ToolCalls = new List<ToolCallMessage>();
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
                }

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

        /// <summary>
        /// Fallback parser for text-based tool calls that some models output.
        /// Supports formats like:
        ///   <function=tool_name> <parameter=key> value </tool_call>
        ///   <tool_call> {"name": "tool_name", "arguments": {...}} </tool_call>
        /// </summary>
        private List<ToolCall> TryParseTextToolCalls(string text)
        {
            var result = new List<ToolCall>();

            // Pattern 1: <function=NAME> <parameter=KEY> VALUE ... </tool_call>
            var funcPattern = new Regex(
                @"<function=(\w+)>(.*?)</tool_call>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in funcPattern.Matches(text))
            {
                var funcName = match.Groups[1].Value.Trim();
                var body = match.Groups[2].Value.Trim();

                var args = new Dictionary<string, object>();
                var paramPattern = new Regex(
                    @"<parameter=(\w+)>\s*(.*?)(?=<parameter=|$)",
                    RegexOptions.Singleline);

                foreach (Match paramMatch in paramPattern.Matches(body))
                {
                    var key = paramMatch.Groups[1].Value.Trim();
                    var value = paramMatch.Groups[2].Value.Trim();
                    args[key] = value;
                }

                if (!string.IsNullOrEmpty(funcName) && args.Count > 0)
                {
                    result.Add(new ToolCall
                    {
                        Id = "text_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = funcName,
                        Arguments = args
                    });
                }
            }

            // Pattern 2: JSON-style tool calls in text
            if (result.Count == 0)
            {
                var jsonPattern = new Regex(
                    @"\{[^{}]*""name""\s*:\s*""(\w+)""[^{}]*""arguments""\s*:\s*(\{[^}]*\})[^{}]*\}",
                    RegexOptions.Singleline);

                foreach (Match match in jsonPattern.Matches(text))
                {
                    try
                    {
                        var name = match.Groups[1].Value;
                        var argsJson = match.Groups[2].Value;
                        var args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson);

                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Add(new ToolCall
                            {
                                Id = "text_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                Name = name,
                                Arguments = args ?? new Dictionary<string, object>()
                            });
                        }
                    }
                    catch { }
                }
            }

            return result;
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
