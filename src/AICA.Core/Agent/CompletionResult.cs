using System;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Represents the result of an attempt_completion tool call
    /// </summary>
    public class CompletionResult
    {
        /// <summary>
        /// Summary of what was accomplished
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Optional command to demonstrate the result
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// Timestamp when the task was completed
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// User feedback (satisfied/unsatisfied/none)
        /// </summary>
        public CompletionFeedback Feedback { get; set; }

        /// <summary>
        /// Optional feedback comment from user
        /// </summary>
        public string FeedbackComment { get; set; }

        public CompletionResult()
        {
            Timestamp = DateTime.Now;
            Feedback = CompletionFeedback.None;
        }

        /// <summary>
        /// Serialize to string format for tool result
        /// </summary>
        public string Serialize()
        {
            var parts = new System.Collections.Generic.List<string>
            {
                $"SUMMARY:{Summary}"
            };

            if (!string.IsNullOrWhiteSpace(Command))
            {
                parts.Add($"COMMAND:{Command}");
            }

            parts.Add($"TIMESTAMP:{Timestamp:O}");

            return "TASK_COMPLETED:" + string.Join("|", parts);
        }

        /// <summary>
        /// Deserialize from tool result string
        /// </summary>
        public static CompletionResult Deserialize(string serialized)
        {
            if (string.IsNullOrEmpty(serialized) || !serialized.StartsWith("TASK_COMPLETED:"))
                return null;

            var result = new CompletionResult();
            var content = serialized.Substring("TASK_COMPLETED:".Length);
            var parts = content.Split('|');

            foreach (var part in parts)
            {
                if (part.StartsWith("SUMMARY:"))
                {
                    result.Summary = part.Substring("SUMMARY:".Length);
                }
                else if (part.StartsWith("COMMAND:"))
                {
                    result.Command = part.Substring("COMMAND:".Length);
                }
                else if (part.StartsWith("TIMESTAMP:"))
                {
                    var timestampStr = part.Substring("TIMESTAMP:".Length);
                    if (DateTime.TryParse(timestampStr, out var timestamp))
                    {
                        result.Timestamp = timestamp;
                    }
                }
            }

            // Fallback: if no SUMMARY found, treat entire content as summary
            if (string.IsNullOrEmpty(result.Summary))
            {
                result.Summary = content;
            }

            return result;
        }
    }

    /// <summary>
    /// User feedback on task completion
    /// </summary>
    public enum CompletionFeedback
    {
        None,
        Satisfied,
        Unsatisfied
    }
}
