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

        // Context menu request templates (Explain/Refactor/GenerateTest)
        // These are single-purpose tasks that should NOT be classified as Complex
        // even though they contain long code snippets.
        private static readonly Regex ContextMenuPattern = new Regex(
            @"^请用中文(?:详细)?(?:解释|重构|.*生成.*(?:单元)?测试)|^(?:explain|refactor|generate\s*tests?\s*for)\s",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Code block pattern — content after ``` should not affect complexity scoring
        private static readonly Regex CodeBlockPattern = new Regex(
            @"```[\s\S]*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Classify request complexity using a scoring system.
        /// </summary>
        public static TaskComplexity AnalyzeComplexity(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return TaskComplexity.Simple;

            // BF-06 fix: Context menu requests (Explain/Refactor/GenerateTest) are
            // single-purpose tasks. They should be Medium at most, not Complex.
            bool isContextMenuRequest = ContextMenuPattern.IsMatch(userRequest.TrimStart());

            // Strip code blocks before scoring — embedded code should not inflate
            // the action verb count or length signal.
            var textForScoring = CodeBlockPattern.Replace(userRequest, "").Trim();

            var score = 0;

            // Strong signals → +3 each
            if (ChineseComplexPattern.IsMatch(textForScoring)) score += 3;
            if (EnglishComplexPattern.IsMatch(textForScoring)) score += 3;
            if (NumberedStepsPattern.IsMatch(textForScoring)) score += 3;

            // Moderate signals → +1 each (capped at 3)
            var verbCount = ActionVerbPattern.Matches(textForScoring).Count;
            score += Math.Min(verbCount, 3);

            // Length signal (scaled thresholds) — use stripped text
            if (textForScoring.Length > 200) score += 2;
            else if (textForScoring.Length > 120) score += 1;

            var complexity = TaskComplexity.Simple;
            if (score >= 4) complexity = TaskComplexity.Complex;
            else if (score >= 2) complexity = TaskComplexity.Medium;

            // Cap context menu requests at Medium — they are never truly Complex
            if (isContextMenuRequest && complexity == TaskComplexity.Complex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] Context menu request capped from Complex to Medium (score={score})");
                complexity = TaskComplexity.Medium;
            }

            return complexity;
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
