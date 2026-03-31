namespace AICA.Core.Agent
{
    /// <summary>
    /// Classification of tool failures for recovery handling.
    /// </summary>
    public enum ToolFailureKind
    {
        RecoverableFeedback,
        Blocking
    }

    /// <summary>
    /// Tracks the state of an Agent task execution.
    /// Simplified for trust-based design — removed compensatory state tracking.
    /// </summary>
    public class TaskState
    {
        // Tool execution tracking
        public System.Collections.Generic.HashSet<string> EditedFiles { get; } = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        public bool DidEditFile => EditedFiles.Count > 0;
        public bool HasEverUsedTools { get; set; }
        public string LastToolName { get; set; } = string.Empty;

        // Error tracking
        public int ConsecutiveBlockingFailureCount { get; set; }
        public int ConsecutiveRecoverableFailureCount { get; set; }

        // Task control
        public bool Abort { get; set; }
        public bool IsCompleted { get; set; }
        public int ApiRequestCount { get; set; }
        public int Iteration { get; set; }
        public int TotalToolCallCount { get; set; }

        // User cancellation tracking
        public int UserCancellationCount { get; set; }
        public const int MaxUserCancellations = 3;

        // Task planning
        public bool HasActivePlan { get; set; }

        // Context management
        public bool HasAutoCondensed { get; set; }

        // Phase tracking (for telemetry/progress)
        public string CurrentPhase { get; set; }
        public int PhaseIterationCount { get; set; }

        /// <summary>
        /// Reset failure counters after a successful step.
        /// </summary>
        public void ResetFailureCounts()
        {
            ConsecutiveBlockingFailureCount = 0;
            ConsecutiveRecoverableFailureCount = 0;
        }

        /// <summary>
        /// Record a tool failure.
        /// </summary>
        public bool RecordToolFailure(ToolFailureKind kind)
        {
            if (kind == ToolFailureKind.Blocking)
            {
                ConsecutiveBlockingFailureCount++;
                ConsecutiveRecoverableFailureCount = 0;
                return ConsecutiveBlockingFailureCount >= 3;
            }
            ConsecutiveRecoverableFailureCount++;
            return false;
        }
    }
}
