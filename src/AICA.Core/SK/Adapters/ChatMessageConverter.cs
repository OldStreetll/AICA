using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICA.Core.Agent;
using AICA.Core.LLM;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AICA.Core.SK.Adapters
{
    /// <summary>
    /// Bidirectional converter between AICA ChatMessage and SK ChatMessageContent.
    /// All conversions are pure functions — no side effects or mutations.
    /// </summary>
    public static class ChatMessageConverter
    {
        /// <summary>
        /// Convert AICA ChatMessage to SK ChatMessageContent.
        /// </summary>
        public static ChatMessageContent ToSKMessage(ChatMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var role = ToSKRole(message.Role);

            // Tool result messages carry FunctionResultContent
            if (message.Role == ChatRole.Tool && !string.IsNullOrEmpty(message.ToolCallId))
            {
                var items = new ChatMessageContentItemCollection
                {
                    new FunctionResultContent(
                        functionName: message.Name ?? "unknown",
                        callId: message.ToolCallId,
                        result: message.Content)
                };
                return new ChatMessageContent(role, items);
            }

            // Assistant messages with tool calls carry FunctionCallContent
            if (message.Role == ChatRole.Assistant && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                var items = new ChatMessageContentItemCollection();

                // Include text content if present
                if (!string.IsNullOrEmpty(message.Content))
                {
                    items.Add(new TextContent(message.Content));
                }

                foreach (var toolCall in message.ToolCalls)
                {
                    var arguments = ParseArguments(toolCall.Function?.Arguments);
                    items.Add(new FunctionCallContent(
                        functionName: toolCall.Function?.Name ?? "unknown",
                        pluginName: null,
                        id: toolCall.Id,
                        arguments: arguments));
                }

                return new ChatMessageContent(role, items);
            }

            // Plain text messages
            return new ChatMessageContent(role, message.Content ?? string.Empty);
        }

        /// <summary>
        /// Convert SK ChatMessageContent to AICA ChatMessage.
        /// </summary>
        public static ChatMessage ToAICAMessage(ChatMessageContent skMessage)
        {
            if (skMessage == null) throw new ArgumentNullException(nameof(skMessage));

            var role = ToAICARole(skMessage.Role);

            // Check for function result content (tool responses)
            var functionResult = skMessage.Items?.OfType<FunctionResultContent>().FirstOrDefault();
            if (functionResult != null)
            {
                return new ChatMessage
                {
                    Role = ChatRole.Tool,
                    Content = functionResult.Result?.ToString() ?? string.Empty,
                    ToolCallId = functionResult.CallId,
                    Name = functionResult.FunctionName
                };
            }

            // Check for function call content (assistant tool calls)
            var functionCalls = skMessage.Items?.OfType<FunctionCallContent>().ToList();
            if (functionCalls != null && functionCalls.Count > 0)
            {
                var textContent = string.Join("",
                    skMessage.Items?.OfType<TextContent>().Select(t => t.Text) ?? Enumerable.Empty<string>());

                var toolCalls = functionCalls.Select(fc => new ToolCallMessage
                {
                    Id = fc.Id ?? Guid.NewGuid().ToString(),
                    Type = "function",
                    Function = new FunctionCall
                    {
                        Name = fc.FunctionName,
                        Arguments = SerializeArguments(fc.Arguments)
                    }
                }).ToList();

                return new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = string.IsNullOrEmpty(textContent) ? null : textContent,
                    ToolCalls = toolCalls
                };
            }

            // Plain text message
            return new ChatMessage
            {
                Role = role,
                Content = skMessage.Content ?? string.Empty
            };
        }

        /// <summary>
        /// Convert a full AICA conversation history to SK ChatHistory.
        /// </summary>
        public static ChatHistory ToSKHistory(IEnumerable<ChatMessage> messages)
        {
            var history = new ChatHistory();
            foreach (var message in messages ?? Enumerable.Empty<ChatMessage>())
            {
                history.Add(ToSKMessage(message));
            }
            return history;
        }

        /// <summary>
        /// Convert SK ChatHistory to AICA message list.
        /// </summary>
        public static List<ChatMessage> ToAICAMessages(ChatHistory history)
        {
            if (history == null) return new List<ChatMessage>();
            return history.Select(ToAICAMessage).ToList();
        }

        private static AuthorRole ToSKRole(ChatRole role)
        {
            switch (role)
            {
                case ChatRole.System: return AuthorRole.System;
                case ChatRole.User: return AuthorRole.User;
                case ChatRole.Assistant: return AuthorRole.Assistant;
                case ChatRole.Tool: return AuthorRole.Tool;
                default: return AuthorRole.User;
            }
        }

        private static ChatRole ToAICARole(AuthorRole role)
        {
            if (role == AuthorRole.System) return ChatRole.System;
            if (role == AuthorRole.User) return ChatRole.User;
            if (role == AuthorRole.Assistant) return ChatRole.Assistant;
            if (role == AuthorRole.Tool) return ChatRole.Tool;
            return ChatRole.User;
        }

        private static KernelArguments ParseArguments(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return null;

                var args = new KernelArguments();
                foreach (var kvp in dict)
                {
                    args[kvp.Key] = kvp.Value?.ToString();
                }
                return args;
            }
            catch
            {
                return null;
            }
        }

        private static string SerializeArguments(IReadOnlyDictionary<string, object> arguments)
        {
            if (arguments == null || arguments.Count == 0) return "{}";

            try
            {
                return JsonSerializer.Serialize(arguments);
            }
            catch
            {
                return "{}";
            }
        }
    }
}
