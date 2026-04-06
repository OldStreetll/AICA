using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.SK.Adapters;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AICA.Core.Tests.SK
{
    public class ChatMessageConverterTests
    {
        // ===== ToSKMessage: plain text messages =====

        [Fact]
        public void ToSKMessage_SystemMessage_MapsCorrectly()
        {
            var msg = ChatMessage.System("You are a helpful assistant.");

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal(AuthorRole.System, result.Role);
            Assert.Equal("You are a helpful assistant.", result.Content);
        }

        [Fact]
        public void ToSKMessage_UserMessage_MapsCorrectly()
        {
            var msg = ChatMessage.User("Hello world");

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal(AuthorRole.User, result.Role);
            Assert.Equal("Hello world", result.Content);
        }

        [Fact]
        public void ToSKMessage_AssistantMessage_MapsCorrectly()
        {
            var msg = ChatMessage.Assistant("I can help with that.");

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal(AuthorRole.Assistant, result.Role);
            Assert.Equal("I can help with that.", result.Content);
        }

        [Fact]
        public void ToSKMessage_EmptyContent_PreservesEmptyString()
        {
            var msg = new ChatMessage { Role = ChatRole.User, Content = "" };

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal("", result.Content);
        }

        [Fact]
        public void ToSKMessage_NullContent_ReturnsEmptyString()
        {
            var msg = new ChatMessage { Role = ChatRole.User, Content = null };

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal("", result.Content);
        }

        // ===== ToSKMessage: tool call messages =====

        [Fact]
        public void ToSKMessage_AssistantWithToolCalls_CreatesFunctionCallContent()
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = null,
                ToolCalls = new List<ToolCallMessage>
                {
                    new ToolCallMessage
                    {
                        Id = "call_123",
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = "read_file",
                            Arguments = "{\"path\":\"/src/main.cs\",\"limit\":20}"
                        }
                    }
                }
            };

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal(AuthorRole.Assistant, result.Role);
            var functionCalls = result.Items.OfType<FunctionCallContent>().ToList();
            Assert.Single(functionCalls);
            Assert.Equal("read_file", functionCalls[0].FunctionName);
            Assert.Equal("call_123", functionCalls[0].Id);
            Assert.NotNull(functionCalls[0].Arguments);
            Assert.Equal("/src/main.cs", functionCalls[0].Arguments["path"]?.ToString());
        }

        [Fact]
        public void ToSKMessage_AssistantWithTextAndToolCalls_PreservesBoth()
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = "Let me read that file.",
                ToolCalls = new List<ToolCallMessage>
                {
                    new ToolCallMessage
                    {
                        Id = "call_456",
                        Function = new FunctionCall { Name = "read_file", Arguments = "{}" }
                    }
                }
            };

            var result = ChatMessageConverter.ToSKMessage(msg);

            var textItems = result.Items.OfType<TextContent>().ToList();
            var callItems = result.Items.OfType<FunctionCallContent>().ToList();
            Assert.Single(textItems);
            Assert.Equal("Let me read that file.", textItems[0].Text);
            Assert.Single(callItems);
            Assert.Equal("read_file", callItems[0].FunctionName);
        }

        [Fact]
        public void ToSKMessage_AssistantWithMultipleToolCalls_MapsAll()
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = null,
                ToolCalls = new List<ToolCallMessage>
                {
                    new ToolCallMessage
                    {
                        Id = "call_1",
                        Function = new FunctionCall { Name = "read_file", Arguments = "{}" }
                    },
                    new ToolCallMessage
                    {
                        Id = "call_2",
                        Function = new FunctionCall { Name = "grep_search", Arguments = "{\"pattern\":\"Logger\"}" }
                    }
                }
            };

            var result = ChatMessageConverter.ToSKMessage(msg);

            var functionCalls = result.Items.OfType<FunctionCallContent>().ToList();
            Assert.Equal(2, functionCalls.Count);
            Assert.Equal("read_file", functionCalls[0].FunctionName);
            Assert.Equal("grep_search", functionCalls[1].FunctionName);
        }

        // ===== ToSKMessage: tool result messages =====

        [Fact]
        public void ToSKMessage_ToolResult_CreatesFunctionResultContent()
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = "File contents here...",
                ToolCallId = "call_123",
                Name = "read_file"
            };

            var result = ChatMessageConverter.ToSKMessage(msg);

            Assert.Equal(AuthorRole.Tool, result.Role);
            var functionResults = result.Items.OfType<FunctionResultContent>().ToList();
            Assert.Single(functionResults);
            Assert.Equal("read_file", functionResults[0].FunctionName);
            Assert.Equal("call_123", functionResults[0].CallId);
            Assert.Equal("File contents here...", functionResults[0].Result?.ToString());
        }

        // ===== ToAICAMessage: plain text messages =====

        [Fact]
        public void ToAICAMessage_SKSystem_MapsCorrectly()
        {
            var skMsg = new ChatMessageContent(AuthorRole.System, "System prompt");

            var result = ChatMessageConverter.ToAICAMessage(skMsg);

            Assert.Equal(ChatRole.System, result.Role);
            Assert.Equal("System prompt", result.Content);
        }

        [Fact]
        public void ToAICAMessage_SKUser_MapsCorrectly()
        {
            var skMsg = new ChatMessageContent(AuthorRole.User, "User question");

            var result = ChatMessageConverter.ToAICAMessage(skMsg);

            Assert.Equal(ChatRole.User, result.Role);
            Assert.Equal("User question", result.Content);
        }

        [Fact]
        public void ToAICAMessage_SKAssistant_MapsCorrectly()
        {
            var skMsg = new ChatMessageContent(AuthorRole.Assistant, "Assistant reply");

            var result = ChatMessageConverter.ToAICAMessage(skMsg);

            Assert.Equal(ChatRole.Assistant, result.Role);
            Assert.Equal("Assistant reply", result.Content);
        }

        // ===== ToAICAMessage: function call content =====

        [Fact]
        public void ToAICAMessage_SKFunctionCall_MapsToToolCallMessage()
        {
            var items = new ChatMessageContentItemCollection
            {
                new FunctionCallContent(
                    functionName: "read_file",
                    pluginName: null,
                    id: "call_789",
                    arguments: new KernelArguments { ["path"] = "/test.cs" })
            };
            var skMsg = new ChatMessageContent(AuthorRole.Assistant, items);

            var result = ChatMessageConverter.ToAICAMessage(skMsg);

            Assert.Equal(ChatRole.Assistant, result.Role);
            Assert.NotNull(result.ToolCalls);
            Assert.Single(result.ToolCalls);
            Assert.Equal("call_789", result.ToolCalls[0].Id);
            Assert.Equal("read_file", result.ToolCalls[0].Function.Name);
            Assert.Contains("path", result.ToolCalls[0].Function.Arguments);
        }

        // ===== ToAICAMessage: function result content =====

        [Fact]
        public void ToAICAMessage_SKFunctionResult_MapsToToolResult()
        {
            var items = new ChatMessageContentItemCollection
            {
                new FunctionResultContent(
                    functionName: "read_file",
                    callId: "call_789",
                    result: "file contents")
            };
            var skMsg = new ChatMessageContent(AuthorRole.Tool, items);

            var result = ChatMessageConverter.ToAICAMessage(skMsg);

            Assert.Equal(ChatRole.Tool, result.Role);
            Assert.Equal("call_789", result.ToolCallId);
            Assert.Equal("read_file", result.Name);
            Assert.Equal("file contents", result.Content);
        }

        // ===== Roundtrip tests =====

        [Theory]
        [InlineData(ChatRole.System, "system prompt")]
        [InlineData(ChatRole.User, "user message")]
        [InlineData(ChatRole.Assistant, "assistant reply")]
        public void Roundtrip_PlainTextMessage_PreservesRoleAndContent(ChatRole role, string content)
        {
            var original = new ChatMessage { Role = role, Content = content };

            var sk = ChatMessageConverter.ToSKMessage(original);
            var roundtripped = ChatMessageConverter.ToAICAMessage(sk);

            Assert.Equal(original.Role, roundtripped.Role);
            Assert.Equal(original.Content, roundtripped.Content);
        }

        [Fact]
        public void Roundtrip_ToolCallMessage_PreservesFields()
        {
            var original = new ChatMessage
            {
                Role = ChatRole.Assistant,
                ToolCalls = new List<ToolCallMessage>
                {
                    new ToolCallMessage
                    {
                        Id = "call_rt",
                        Function = new FunctionCall
                        {
                            Name = "grep_search",
                            Arguments = "{\"pattern\":\"class Logger\"}"
                        }
                    }
                }
            };

            var sk = ChatMessageConverter.ToSKMessage(original);
            var roundtripped = ChatMessageConverter.ToAICAMessage(sk);

            Assert.Equal(ChatRole.Assistant, roundtripped.Role);
            Assert.NotNull(roundtripped.ToolCalls);
            Assert.Single(roundtripped.ToolCalls);
            Assert.Equal("call_rt", roundtripped.ToolCalls[0].Id);
            Assert.Equal("grep_search", roundtripped.ToolCalls[0].Function.Name);
        }

        [Fact]
        public void Roundtrip_ToolResultMessage_PreservesFields()
        {
            var original = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = "Found 3 matches",
                ToolCallId = "call_rt2",
                Name = "grep_search"
            };

            var sk = ChatMessageConverter.ToSKMessage(original);
            var roundtripped = ChatMessageConverter.ToAICAMessage(sk);

            Assert.Equal(ChatRole.Tool, roundtripped.Role);
            Assert.Equal("call_rt2", roundtripped.ToolCallId);
            Assert.Equal("grep_search", roundtripped.Name);
            Assert.Equal("Found 3 matches", roundtripped.Content);
        }

        // ===== Batch conversion =====

        [Fact]
        public void ToSKHistory_ConvertsFullConversation()
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System("You are AICA."),
                ChatMessage.User("Read main.cs"),
                ChatMessage.Assistant("I'll read that file.")
            };

            var history = ChatMessageConverter.ToSKHistory(messages);

            Assert.Equal(3, history.Count);
            Assert.Equal(AuthorRole.System, history[0].Role);
            Assert.Equal(AuthorRole.User, history[1].Role);
            Assert.Equal(AuthorRole.Assistant, history[2].Role);
        }

        [Fact]
        public void ToSKHistory_EmptyList_ReturnsEmptyHistory()
        {
            var history = ChatMessageConverter.ToSKHistory(new List<ChatMessage>());

            Assert.Empty(history);
        }

        [Fact]
        public void ToSKHistory_Null_ReturnsEmptyHistory()
        {
            var history = ChatMessageConverter.ToSKHistory(null);

            Assert.Empty(history);
        }

        [Fact]
        public void ToAICAMessages_Null_ReturnsEmptyList()
        {
            var result = ChatMessageConverter.ToAICAMessages(null);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        // ===== Null guard =====

        [Fact]
        public void ToSKMessage_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ChatMessageConverter.ToSKMessage(null));
        }

        [Fact]
        public void ToAICAMessage_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ChatMessageConverter.ToAICAMessage(null));
        }
    }
}
