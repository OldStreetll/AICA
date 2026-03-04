using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Security
{
    /// <summary>
    /// Manages auto-approval rules for operations that require user confirmation.
    /// Allows users to configure which operations can be automatically approved based on patterns.
    /// </summary>
    public class AutoApproveManager
    {
        private readonly AutoApproveOptions _options;
        private readonly HashSet<string> _autoApprovedOperations;
        private readonly List<AutoApproveRule> _rules;

        public AutoApproveManager(AutoApproveOptions options)
        {
            _options = options ?? new AutoApproveOptions();
            _autoApprovedOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _rules = new List<AutoApproveRule>();

            InitializeRules();
        }

        private void InitializeRules()
        {
            // File operations
            if (_options.AutoApproveFileRead)
            {
                _autoApprovedOperations.Add("read_file");
                _autoApprovedOperations.Add("list_dir");
                _autoApprovedOperations.Add("grep_search");
                _autoApprovedOperations.Add("find_by_name");
            }

            if (_options.AutoApproveFileCreate)
            {
                _rules.Add(new AutoApproveRule
                {
                    OperationType = "Create File",
                    Condition = (op, details) => true
                });
            }

            if (_options.AutoApproveFileEdit)
            {
                _rules.Add(new AutoApproveRule
                {
                    OperationType = "Edit File",
                    Condition = (op, details) => true
                });
            }

            if (_options.AutoApproveFileDelete)
            {
                _rules.Add(new AutoApproveRule
                {
                    OperationType = "Delete File",
                    Condition = (op, details) => true
                });
            }

            // Command execution
            if (_options.AutoApproveSafeCommands)
            {
                _rules.Add(new AutoApproveRule
                {
                    OperationType = "Run Command",
                    Condition = (op, details) => IsSafeCommand(details)
                });
            }

            // Pattern-based rules
            if (_options.FilePatterns != null && _options.FilePatterns.Length > 0)
            {
                foreach (var pattern in _options.FilePatterns)
                {
                    _rules.Add(new AutoApproveRule
                    {
                        OperationType = "File Operation",
                        Condition = (op, details) => MatchesPattern(details, pattern)
                    });
                }
            }
        }

        /// <summary>
        /// Check if an operation should be auto-approved
        /// </summary>
        /// <param name="operation">Operation name (e.g., "Create File", "Run Command")</param>
        /// <param name="details">Operation details</param>
        /// <returns>True if the operation should be auto-approved</returns>
        public bool ShouldAutoApprove(string operation, string details)
        {
            if (string.IsNullOrEmpty(operation))
                return false;

            // Check if operation is in the auto-approved list
            if (_autoApprovedOperations.Contains(operation))
                return true;

            // Check rules
            foreach (var rule in _rules)
            {
                if (rule.OperationType.Equals(operation, StringComparison.OrdinalIgnoreCase) ||
                    operation.IndexOf(rule.OperationType, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (rule.Condition(operation, details))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Add a custom auto-approve rule
        /// </summary>
        public void AddRule(AutoApproveRule rule)
        {
            if (rule != null)
            {
                _rules.Add(rule);
            }
        }

        /// <summary>
        /// Remove all custom rules
        /// </summary>
        public void ClearCustomRules()
        {
            _rules.Clear();
            InitializeRules();
        }

        private bool IsSafeCommand(string details)
        {
            if (string.IsNullOrEmpty(details))
                return false;

            var safeCommands = new[] { "dotnet", "npm", "git", "nuget", "node", "python", "pip" };
            var lowerDetails = details.ToLowerInvariant();

            foreach (var cmd in safeCommands)
            {
                if (lowerDetails.Contains(cmd))
                    return true;
            }

            return false;
        }

        private bool MatchesPattern(string details, string pattern)
        {
            if (string.IsNullOrEmpty(details) || string.IsNullOrEmpty(pattern))
                return false;

            // Simple wildcard matching
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                details,
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    /// <summary>
    /// Configuration options for auto-approval
    /// </summary>
    public class AutoApproveOptions
    {
        /// <summary>
        /// Auto-approve read-only file operations (read_file, list_dir, grep_search)
        /// </summary>
        public bool AutoApproveFileRead { get; set; } = true;

        /// <summary>
        /// Auto-approve file creation operations
        /// </summary>
        public bool AutoApproveFileCreate { get; set; } = false;

        /// <summary>
        /// Auto-approve file edit operations
        /// </summary>
        public bool AutoApproveFileEdit { get; set; } = false;

        /// <summary>
        /// Auto-approve file deletion operations
        /// </summary>
        public bool AutoApproveFileDelete { get; set; } = false;

        /// <summary>
        /// Auto-approve safe commands (dir, git, dotnet, npm, etc.)
        /// </summary>
        public bool AutoApproveSafeCommands { get; set; } = false;

        /// <summary>
        /// File patterns to auto-approve (e.g., "*.txt", "test/*")
        /// </summary>
        public string[] FilePatterns { get; set; }

        /// <summary>
        /// Command patterns to auto-approve (e.g., "git status", "npm install")
        /// </summary>
        public string[] CommandPatterns { get; set; }
    }

    /// <summary>
    /// Represents an auto-approve rule
    /// </summary>
    public class AutoApproveRule
    {
        /// <summary>
        /// Type of operation (e.g., "Create File", "Run Command")
        /// </summary>
        public string OperationType { get; set; }

        /// <summary>
        /// Condition function that determines if the rule applies
        /// </summary>
        public Func<string, string, bool> Condition { get; set; }

        /// <summary>
        /// Optional description of the rule
        /// </summary>
        public string Description { get; set; }
    }
}
