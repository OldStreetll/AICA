using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.Rules.Models;
using AICA.Core.Rules.Parsers;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Rules
{
    /// <summary>
    /// Evaluates whether rules should be activated based on context conditions.
    /// </summary>
    public class RuleEvaluator
    {
        private readonly PathMatcher _pathMatcher;
        private readonly ILogger<RuleEvaluator> _logger;

        public RuleEvaluator(ILogger<RuleEvaluator> logger = null)
        {
            _pathMatcher = new PathMatcher();
            _logger = logger;
        }

        /// <summary>
        /// Evaluate whether a single rule should be activated.
        /// </summary>
        public bool EvaluateRule(Rule rule, RuleContext context)
        {
            if (rule == null)
                return false;

            // Check if rule is enabled
            if (!rule.Enabled)
            {
                _logger?.LogDebug($"Rule '{rule.Id}' is disabled");
                return false;
            }

            // If no path conditions, rule is always active (universal rule)
            if (rule.Metadata?.Paths == null || rule.Metadata.Paths.Count == 0)
            {
                _logger?.LogDebug($"Rule '{rule.Id}' is universal (no path conditions)");
                return true;
            }

            // Check path conditions
            if (context?.CandidatePaths == null || context.CandidatePaths.Count == 0)
            {
                _logger?.LogDebug($"Rule '{rule.Id}' requires path conditions but no candidate paths provided");
                return false;
            }

            var matches = _pathMatcher.MatchAny(rule.Metadata.Paths, context.CandidatePaths);
            if (matches)
            {
                _logger?.LogDebug($"Rule '{rule.Id}' activated by path match");
            }
            else
            {
                _logger?.LogDebug($"Rule '{rule.Id}' not activated (no path match)");
            }

            return matches;
        }

        /// <summary>
        /// Evaluate all rules and return those that should be activated.
        /// Rules are sorted by priority (higher priority first).
        /// </summary>
        public List<Rule> EvaluateRules(List<Rule> rules, RuleContext context)
        {
            if (rules == null || rules.Count == 0)
                return new List<Rule>();

            var activated = rules
                .Where(r => EvaluateRule(r, context))
                .OrderByDescending(r => r.Priority)
                .ToList();

            _logger?.LogDebug($"Evaluated {rules.Count} rules, {activated.Count} activated");
            return activated;
        }

        /// <summary>
        /// Merge multiple rules by priority, with higher priority rules overriding lower ones.
        /// </summary>
        public List<Rule> MergeRules(List<Rule> rules)
        {
            if (rules == null || rules.Count == 0)
                return new List<Rule>();

            // Group by ID and keep the highest priority version
            var merged = rules
                .GroupBy(r => r.Id)
                .Select(g => g.OrderByDescending(r => r.Priority).First())
                .OrderByDescending(r => r.Priority)
                .ToList();

            _logger?.LogDebug($"Merged {rules.Count} rules into {merged.Count} unique rules");
            return merged;
        }
    }
}
