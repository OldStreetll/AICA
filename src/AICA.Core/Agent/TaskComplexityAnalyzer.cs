using System;
using System.Text.RegularExpressions;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Three-tier task complexity classification.
    /// </summary>
    public enum TaskComplexity
    {
        Simple,
        Medium,
        Complex
    }

    /// <summary>
    /// Heuristic analyzer that determines whether a user request is complex enough
    /// to warrant automatic task planning. Uses a scoring system to classify requests
    /// into Simple, Medium, or Complex tiers.
    /// </summary>
    public static class TaskComplexityAnalyzer
    {
        // Chinese multi-step keywords
        private static readonly Regex ChineseComplexPattern = new Regex(
            @"分析.*架构|重构|实现.*并.*测试|添加.*并|比较.*和|迁移|优化.*性能",
            RegexOptions.Compiled);

        // English multi-step keywords
        private static readonly Regex EnglishComplexPattern = new Regex(
            @"analyze.*architecture|refactor|implement.*and.*test|add.*and|compare.*and|migrate",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Explicit numbered steps: "1." "2." "3." or step/步骤 markers
        private static readonly Regex NumberedStepsPattern = new Regex(
            @"(?:^|\s)1\.\s.*(?:^|\s)2\.\s.*(?:^|\s)3\.\s|步骤|step\s*\d",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Action verbs for density check
        private static readonly Regex ActionVerbPattern = new Regex(
            @"\b(?:read|write|create|delete|analyze|fix|test|search|implement|refactor|build|deploy|migrate|compare|optimize)\b|读取|写入|创建|删除|分析|修复|测试|搜索|实现|重构|构建|部署|迁移|比较|优化",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Classify request complexity using a scoring system.
        /// </summary>
        public static TaskComplexity AnalyzeComplexity(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return TaskComplexity.Simple;

            var score = 0;

            // Strong signals → +3 each
            if (ChineseComplexPattern.IsMatch(userRequest)) score += 3;
            if (EnglishComplexPattern.IsMatch(userRequest)) score += 3;
            if (NumberedStepsPattern.IsMatch(userRequest)) score += 3;

            // Moderate signals → +1 each (capped at 3)
            var verbCount = ActionVerbPattern.Matches(userRequest).Count;
            score += Math.Min(verbCount, 3);

            // Length signal (scaled thresholds)
            if (userRequest.Length > 200) score += 2;
            else if (userRequest.Length > 120) score += 1;

            if (score >= 4) return TaskComplexity.Complex;
            if (score >= 2) return TaskComplexity.Medium;
            return TaskComplexity.Simple;
        }

        /// <summary>
        /// Backward-compatible wrapper. Returns true only for Complex requests.
        /// </summary>
        public static bool IsComplexRequest(string userRequest)
        {
            return AnalyzeComplexity(userRequest) == TaskComplexity.Complex;
        }
    }
}
