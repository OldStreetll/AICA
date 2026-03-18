using System.Text.RegularExpressions;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Heuristic analyzer that determines whether a user request is complex enough
    /// to warrant automatic task planning. False positives are harmless (LLM just
    /// creates a plan); false negatives degrade to existing single-step behavior.
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

        private const int LengthThreshold = 80;
        private const int MinActionVerbCount = 2;

        /// <summary>
        /// Determines whether a user request is complex enough to require task planning.
        /// </summary>
        public static bool IsComplexRequest(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return false;

            // Rule 1: Chinese multi-step keywords
            if (ChineseComplexPattern.IsMatch(userRequest))
                return true;

            // Rule 2: English multi-step keywords
            if (EnglishComplexPattern.IsMatch(userRequest))
                return true;

            // Rule 3: Explicit numbered steps or step markers
            if (NumberedStepsPattern.IsMatch(userRequest))
                return true;

            // Rule 4: Long request with multiple action verbs
            if (userRequest.Length > LengthThreshold)
            {
                var verbMatches = ActionVerbPattern.Matches(userRequest);
                if (verbMatches.Count >= MinActionVerbCount)
                    return true;
            }

            return false;
        }
    }
}
