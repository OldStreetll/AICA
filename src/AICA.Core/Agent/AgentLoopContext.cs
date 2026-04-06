using System.Collections.Generic;
using AICA.Core.LLM;
using Microsoft.SemanticKernel;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Shared mutable state for a single AgentExecutor.ExecuteAsync invocation.
    /// Passed to all extracted modules (TokenBudgetManager, ToolCallProcessor, etc.)
    /// so they can read/write execution state without being tightly coupled to AgentExecutor.
    /// </summary>
    public class AgentLoopContext
    {
        /// <summary>
        /// Per-execution task state tracking (iterations, errors, tool counts, etc.)
        /// </summary>
        public TaskState TaskState { get; set; }

        /// <summary>
        /// Conversation history (system prompt + user/assistant/tool messages).
        /// Modified by condense, truncation, and tool result insertion.
        /// </summary>
        public List<ChatMessage> ConversationHistory { get; set; }

        /// <summary>
        /// Signatures of successfully executed tool calls, used for dedup.
        /// </summary>
        public HashSet<string> ExecutedToolSignatures { get; set; }

        /// <summary>
        /// Tool definitions available for this execution.
        /// </summary>
        public List<ToolDefinition> ToolDefinitions { get; set; }

        /// <summary>
        /// The user's original request for this execution.
        /// </summary>
        public string UserRequest { get; set; }

        /// <summary>
        /// Agent context for file/workspace/task operations.
        /// </summary>
        public IAgentContext AgentContext { get; set; }

        /// <summary>
        /// UI context for user interactions.
        /// </summary>
        public IUIContext UIContext { get; set; }

        /// <summary>
        /// Maximum token budget for the context window.
        /// </summary>
        public int MaxTokenBudget { get; set; }

        /// <summary>
        /// Maximum number of iterations before forced completion.
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Optional custom instructions from user settings.
        /// </summary>
        public string CustomInstructions { get; set; }

        /// <summary>
        /// Optional Semantic Kernel instance.
        /// </summary>
        public Kernel Kernel { get; set; }

        /// <summary>
        /// Last condense summary produced. UI layer can persist this.
        /// </summary>
        public string LastCondenseSummary { get; set; }

        /// <summary>
        /// Number of messages at last condense. UI layer can persist this.
        /// </summary>
        public int CondenseUpToMessageCount { get; set; }
    }
}
