using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace AICA.Core.SK.Adapters
{
    /// <summary>
    /// Adapts AICA's ILLMClient to SK's IChatCompletionService.
    /// Reuses AICA's existing HTTP layer, SSE stream parsing, and ToolCallBuilder —
    /// no new HTTP client is introduced.
    /// </summary>
    public sealed class LLMClientChatCompletionService : IChatCompletionService
    {
        private readonly ILLMClient _llmClient;
        private readonly LLMClientOptions _options;
        private readonly ILogger _logger;

        private readonly Dictionary<string, object> _attributes;

        public IReadOnlyDictionary<string, object> Attributes => _attributes;

        public LLMClientChatCompletionService(
            ILLMClient llmClient,
            LLMClientOptions options,
            ILogger logger = null)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;

            _attributes = new Dictionary<string, object>
            {
                { AIServiceExtensions.ModelIdKey, options.Model ?? "unknown" }
            };
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings executionSettings = null,
            Kernel kernel = null,
            CancellationToken cancellationToken = default)
        {
            var messages = ChatMessageConverter.ToAICAMessages(chatHistory);
            var tools = ExtractToolDefinitions(kernel);

            var textBuilder = new StringBuilder();
            var toolCalls = new List<ToolCallMessage>();

            await foreach (var chunk in _llmClient.StreamChatAsync(messages, tools, cancellationToken)
                .ConfigureAwait(false))
            {
                switch (chunk.Type)
                {
                    case LLMChunkType.Text:
                        textBuilder.Append(chunk.Text);
                        break;

                    case LLMChunkType.ToolCall:
                        if (chunk.ToolCall != null)
                        {
                            toolCalls.Add(new ToolCallMessage
                            {
                                Id = chunk.ToolCall.Id,
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = chunk.ToolCall.Name,
                                    Arguments = chunk.ToolCall.Arguments != null
                                        ? JsonSerializer.Serialize(chunk.ToolCall.Arguments)
                                        : "{}"
                                }
                            });
                        }
                        break;

                    case LLMChunkType.Done:
                        break;
                }
            }

            // Build the AICA ChatMessage, then convert to SK
            var assistantMessage = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = textBuilder.Length > 0 ? textBuilder.ToString() : null,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null
            };

            var skMessage = ChatMessageConverter.ToSKMessage(assistantMessage);
            return new List<ChatMessageContent> { skMessage };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings executionSettings = null,
            Kernel kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messages = ChatMessageConverter.ToAICAMessages(chatHistory);
            var tools = ExtractToolDefinitions(kernel);

            await foreach (var chunk in _llmClient.StreamChatAsync(messages, tools, cancellationToken)
                .ConfigureAwait(false))
            {
                switch (chunk.Type)
                {
                    case LLMChunkType.Text:
                        yield return new StreamingChatMessageContent(
                            AuthorRole.Assistant,
                            chunk.Text);
                        break;

                    case LLMChunkType.ToolCall:
                        if (chunk.ToolCall != null)
                        {
                            // Emit tool call as a streaming function call update
                            var arguments = chunk.ToolCall.Arguments != null
                                ? JsonSerializer.Serialize(chunk.ToolCall.Arguments)
                                : "{}";

                            var streamingContent = new StreamingChatMessageContent(
                                AuthorRole.Assistant,
                                content: null);

                            streamingContent.Items.Add(
                                new StreamingFunctionCallUpdateContent(
                                    callId: chunk.ToolCall.Id,
                                    name: chunk.ToolCall.Name,
                                    arguments: arguments));

                            yield return streamingContent;
                        }
                        break;

                    case LLMChunkType.Done:
                        // Stream is complete
                        break;
                }
            }
        }

        /// <summary>
        /// Extract tool definitions from registered Kernel plugins.
        /// Returns null if no plugins are registered (preserving AICA's behavior
        /// of omitting tools from the request when none are available).
        /// </summary>
        private static IEnumerable<ToolDefinition> ExtractToolDefinitions(Kernel kernel)
        {
            if (kernel == null) return null;

            var functions = kernel.Plugins.GetFunctionsMetadata();
            if (!functions.Any()) return null;

            var definitions = new List<ToolDefinition>();

            foreach (var func in functions)
            {
                var properties = new Dictionary<string, ToolParameterProperty>();
                var required = new List<string>();

                foreach (var param in func.Parameters)
                {
                    properties[param.Name] = new ToolParameterProperty
                    {
                        Type = MapParameterType(param.ParameterType),
                        Description = param.Description
                    };

                    if (param.IsRequired)
                    {
                        required.Add(param.Name);
                    }
                }

                definitions.Add(new ToolDefinition
                {
                    Name = func.Name,
                    Description = func.Description,
                    Parameters = new ToolParameters
                    {
                        Type = "object",
                        Properties = properties,
                        Required = required.Count > 0 ? required.ToArray() : null
                    }
                });
            }

            return definitions.Count > 0 ? definitions : null;
        }

        private static string MapParameterType(Type type)
        {
            if (type == null) return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
                return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            return "string";
        }
    }
}
