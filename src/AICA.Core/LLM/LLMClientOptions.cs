namespace AICA.Core.LLM
{
    /// <summary>
    /// Configuration options for LLM client
    /// </summary>
    public class LLMClientOptions
    {
        /// <summary>
        /// Base URL of the LLM API endpoint
        /// </summary>
        public string ApiEndpoint { get; set; } = "http://localhost:8000/v1/";

        /// <summary>
        /// API key for authentication
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Model identifier (e.g., "MiniMax-M2.5", "qwen3-coder", "deepseek-coder")
        /// </summary>
        public string Model { get; set; } = "MiniMax-M2.5-Test";

        /// <summary>
        /// Maximum tokens for response
        /// </summary>
        public int MaxTokens { get; set; } = 16384;

        /// <summary>
        /// Total context window size of the model (input + output tokens).
        /// This is independent of MaxTokens which only controls output length.
        /// Common values: MiniMax-M2.5=192000, GPT-4=128000, Claude=200000
        /// </summary>
        public int ContextWindowSize { get; set; } = 196608;

        /// <summary>
        /// Temperature for response randomness (0.0 - 2.0)
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Top P (nucleus sampling) for response diversity (0.0 - 1.0)
        /// </summary>
        public double TopP { get; set; } = 1.0;

        /// <summary>
        /// Top K for limiting token candidates
        /// </summary>
        public int TopK { get; set; } = 0;

        /// <summary>
        /// Request timeout in seconds
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Enable streaming responses
        /// </summary>
        public bool Stream { get; set; } = true;

        /// <summary>
        /// When true, bypass system/global proxy and connect directly
        /// </summary>
        public bool BypassProxy { get; set; } = false;

        /// <summary>
        /// When true, send stream_options.include_usage=true to get token usage in the final stream chunk.
        /// Not all providers support this (OpenAI does, MiniMax may not). Safe to enable — unsupported
        /// providers will ignore the field.
        /// </summary>
        public bool StreamUsageEnabled { get; set; } = true;
    }
}
