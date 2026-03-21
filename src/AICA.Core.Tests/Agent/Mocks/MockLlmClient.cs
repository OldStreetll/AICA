using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using AICA.Core.Agent;
using AICA.Core.LLM;

namespace AICA.Core.Tests.Agent.Mocks
{
    /// <summary>
    /// A single scripted LLM response consisting of a sequence of chunks.
    /// </summary>
    public class MockLlmResponse
    {
        public List<LLMChunk> Chunks { get; }

        public MockLlmResponse(params LLMChunk[] chunks)
        {
            Chunks = chunks?.ToList() ?? new List<LLMChunk>();
        }

        /// <summary>
        /// Create a response that returns text content and then Done.
        /// </summary>
        public static MockLlmResponse Text(string text)
        {
            return new MockLlmResponse(
                LLMChunk.TextContent(text),
                LLMChunk.Done(new UsageInfo { PromptTokens = 100, CompletionTokens = 50 })
            );
        }

        /// <summary>
        /// Create a response that returns a tool call and then Done.
        /// </summary>
        public static MockLlmResponse WithToolCall(string name, Dictionary<string, object> args = null)
        {
            var toolCall = new ToolCall
            {
                Id = $"call_{name}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}",
                Name = name,
                Arguments = args ?? new Dictionary<string, object>()
            };

            return new MockLlmResponse(
                LLMChunk.Tool(toolCall),
                LLMChunk.Done(new UsageInfo { PromptTokens = 100, CompletionTokens = 50 })
            );
        }

        /// <summary>
        /// Create a response with text followed by a tool call.
        /// </summary>
        public static MockLlmResponse TextThenToolCall(string text, string toolName, Dictionary<string, object> args = null)
        {
            var toolCall = new ToolCall
            {
                Id = $"call_{toolName}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}",
                Name = toolName,
                Arguments = args ?? new Dictionary<string, object>()
            };

            return new MockLlmResponse(
                LLMChunk.TextContent(text),
                LLMChunk.Tool(toolCall),
                LLMChunk.Done(new UsageInfo { PromptTokens = 100, CompletionTokens = 50 })
            );
        }

        /// <summary>
        /// Create a completion response (attempt_completion tool call).
        /// </summary>
        public static MockLlmResponse Completion(string result)
        {
            return WithToolCall("attempt_completion", new Dictionary<string, object>
            {
                ["result"] = result
            });
        }

        /// <summary>
        /// Create a text response that claims to execute a tool without actually calling it
        /// (for hallucination detection testing).
        /// </summary>
        public static MockLlmResponse HallucinatedToolCall(string text)
        {
            return Text(text);
        }
    }

    /// <summary>
    /// Mock ILLMClient that returns scripted responses in order.
    /// Records all received messages for assertion.
    /// </summary>
    public class MockLlmClient : ILLMClient
    {
        private readonly Queue<MockLlmResponse> _responses;
        private readonly List<List<ChatMessage>> _receivedMessages = new List<List<ChatMessage>>();
        private readonly List<List<ToolDefinition>> _receivedTools = new List<List<ToolDefinition>>();
        private bool _aborted;

        public MockLlmClient(params MockLlmResponse[] responses)
        {
            _responses = new Queue<MockLlmResponse>(responses);
        }

        public MockLlmClient(IEnumerable<MockLlmResponse> responses)
        {
            _responses = new Queue<MockLlmResponse>(responses);
        }

        /// <summary>
        /// All message sets sent to the LLM across all calls.
        /// </summary>
        public IReadOnlyList<List<ChatMessage>> ReceivedMessages => _receivedMessages;

        /// <summary>
        /// All tool definition sets sent to the LLM across all calls.
        /// </summary>
        public IReadOnlyList<List<ToolDefinition>> ReceivedTools => _receivedTools;

        /// <summary>
        /// Number of times StreamChatAsync was called.
        /// </summary>
        public int CallCount => _receivedMessages.Count;

        /// <summary>
        /// Whether Abort() was called.
        /// </summary>
        public bool WasAborted => _aborted;

#pragma warning disable CS1998 // Async method lacks 'await' — required for IAsyncEnumerable with yield return
        public async IAsyncEnumerable<LLMChunk> StreamChatAsync(
            IEnumerable<ChatMessage> messages,
            IEnumerable<ToolDefinition> tools = null,
            [EnumeratorCancellation] CancellationToken ct = default)
#pragma warning restore CS1998
        {
            _receivedMessages.Add(messages?.ToList() ?? new List<ChatMessage>());
            _receivedTools.Add(tools?.ToList() ?? new List<ToolDefinition>());

            if (_responses.Count == 0)
            {
                // Default: return a simple text response
                yield return LLMChunk.TextContent("No more scripted responses.");
                yield return LLMChunk.Done();
                yield break;
            }

            var response = _responses.Dequeue();
            foreach (var chunk in response.Chunks)
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }

        public void Abort()
        {
            _aborted = true;
        }
    }
}
