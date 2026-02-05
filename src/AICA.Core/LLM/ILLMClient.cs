using System.Collections.Generic;
using System.Threading;
using AICA.Core.Agent;

namespace AICA.Core.LLM
{
    /// <summary>
    /// Interface for LLM client communication
    /// </summary>
    public interface ILLMClient
    {
        /// <summary>
        /// Stream chat completion with optional tool calling support
        /// </summary>
        IAsyncEnumerable<LLMChunk> StreamChatAsync(
            IEnumerable<ChatMessage> messages,
            IEnumerable<ToolDefinition> tools = null,
            CancellationToken ct = default);

        /// <summary>
        /// Abort the current request
        /// </summary>
        void Abort();
    }

    /// <summary>
    /// Chunk of data from LLM streaming response
    /// </summary>
    public class LLMChunk
    {
        public LLMChunkType Type { get; set; }
        public string Text { get; set; }
        public ToolCall ToolCall { get; set; }
        public UsageInfo Usage { get; set; }

        public static LLMChunk TextContent(string text) => new LLMChunk { Type = LLMChunkType.Text, Text = text };
        public static LLMChunk Tool(ToolCall call) => new LLMChunk { Type = LLMChunkType.ToolCall, ToolCall = call };
        public static LLMChunk Done(UsageInfo usage = null) => new LLMChunk { Type = LLMChunkType.Done, Usage = usage };
    }

    public enum LLMChunkType
    {
        Text,
        ToolCall,
        Done
    }

    /// <summary>
    /// Token usage information
    /// </summary>
    public class UsageInfo
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens => PromptTokens + CompletionTokens;
    }
}
