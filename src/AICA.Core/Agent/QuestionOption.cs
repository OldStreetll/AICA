using System.Collections.Generic;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Represents an option in a followup question
    /// </summary>
    public class QuestionOption
    {
        /// <summary>
        /// Display label shown to the user
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Value returned when this option is selected
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Optional description providing more context about this option
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// Result from a followup question
    /// </summary>
    public class FollowupQuestionResult
    {
        /// <summary>
        /// The selected value or custom input from the user
        /// </summary>
        public string Answer { get; set; }

        /// <summary>
        /// Whether the user cancelled the question
        /// </summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// Whether the answer came from custom input (vs predefined option)
        /// </summary>
        public bool IsCustomInput { get; set; }

        public static FollowupQuestionResult FromOption(string value) =>
            new FollowupQuestionResult { Answer = value, Cancelled = false, IsCustomInput = false };

        public static FollowupQuestionResult FromCustomInput(string input) =>
            new FollowupQuestionResult { Answer = input, Cancelled = false, IsCustomInput = true };

        public static FollowupQuestionResult Canceled() =>
            new FollowupQuestionResult { Cancelled = true };
    }
}
