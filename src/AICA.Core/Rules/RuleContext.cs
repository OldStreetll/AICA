using System;
using System.Collections.Generic;

namespace AICA.Core.Rules
{
    /// <summary>
    /// Context information for evaluating rule activation conditions.
    /// </summary>
    public class RuleContext
    {
        /// <summary>
        /// Current working directory.
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Candidate file paths extracted from user input, tool operations, etc.
        /// Used for path-based rule matching.
        /// </summary>
        public List<string> CandidatePaths { get; set; } = new List<string>();

        /// <summary>
        /// Custom context data for rule evaluation.
        /// </summary>
        public Dictionary<string, object> Custom { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Create a new RuleContext with the given working directory.
        /// </summary>
        public RuleContext(string workingDirectory)
        {
            WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        }

        /// <summary>
        /// Create an empty RuleContext (for testing).
        /// </summary>
        public RuleContext()
        {
        }
    }
}
