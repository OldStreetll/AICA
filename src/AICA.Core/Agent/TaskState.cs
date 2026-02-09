namespace AICA.Core.Agent
{
    /// <summary>
    /// Tracks the state of an Agent task execution.
    /// Extracted from AgentExecutor for clarity and reuse.
    /// </summary>
    public class TaskState
    {
        // Streaming flags
        public bool IsStreaming { get; set; }

        // Tool execution flags
        public bool DidRejectTool { get; set; }
        public bool DidEditFile { get; set; }
        public bool HasEverUsedTools { get; set; }
        public string LastToolName { get; set; } = string.Empty;

        // Error tracking
        public int ConsecutiveMistakeCount { get; set; }
        public int MaxConsecutiveMistakes { get; set; } = 3;

        // Task control
        public bool Abort { get; set; }
        public bool IsCompleted { get; set; }
        public int ApiRequestCount { get; set; }
        public int Iteration { get; set; }

        // noToolsUsed tracking
        public int ConsecutiveNoToolCount { get; set; }

        /// <summary>
        /// Reset mistake counter (called when a tool executes successfully)
        /// </summary>
        public void ResetMistakeCount()
        {
            ConsecutiveMistakeCount = 0;
        }

        /// <summary>
        /// Increment mistake counter and check threshold
        /// </summary>
        /// <returns>True if threshold exceeded</returns>
        public bool IncrementMistakeCount()
        {
            ConsecutiveMistakeCount++;
            return ConsecutiveMistakeCount >= MaxConsecutiveMistakes;
        }

        /// <summary>
        /// Record that no tools were used in this iteration
        /// </summary>
        /// <returns>True if consecutive no-tool count exceeds 2</returns>
        public bool RecordNoToolsUsed()
        {
            ConsecutiveNoToolCount++;
            return ConsecutiveNoToolCount > 2;
        }

        /// <summary>
        /// Reset the no-tools counter (called when a tool is executed)
        /// </summary>
        public void ResetNoToolCount()
        {
            ConsecutiveNoToolCount = 0;
        }
    }
}
