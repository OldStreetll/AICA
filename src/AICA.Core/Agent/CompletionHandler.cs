using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Handles modification conflict detection and completion-related analysis.
    /// Extracted from AgentExecutor to separate completion concerns.
    /// </summary>
    public static class CompletionHandler
    {
        /// <summary>
        /// Detect conflict where user requested a modification but the LLM concludes
        /// the code is already in the desired state (no changes needed).
        /// </summary>
        public static bool DetectModificationConflict(
            string userRequest,
            string assistantResponse,
            List<ChatMessage> conversationHistory,
            TaskState taskState)
        {
            if (string.IsNullOrWhiteSpace(userRequest) || string.IsNullOrWhiteSpace(assistantResponse))
                return false;

            var lowerRequest = userRequest.ToLowerInvariant();
            var lowerResponse = assistantResponse.ToLowerInvariant();

            var modificationKeywords = new[]
            {
                "修改", "更改", "改为", "替换", "重构", "重写", "添加", "删除", "移除",
                "modify", "change", "replace", "refactor", "rewrite", "add", "remove", "delete",
                "update", "fix", "改成", "换成", "调整", "优化"
            };

            bool userRequestedModification = modificationKeywords.Any(k => lowerRequest.Contains(k));
            if (!userRequestedModification)
                return false;

            var alreadyCompliantPatterns = new[]
            {
                "already", "已经是", "已经符合", "无需修改", "不需要修改", "no changes needed",
                "no modification needed", "already in the desired state", "already correct",
                "already implemented", "已经实现", "代码已经", "当前代码已",
                "no changes are necessary", "nothing to change"
            };

            bool responseIndicatesNoChange = alreadyCompliantPatterns.Any(p => lowerResponse.Contains(p));

            bool noEditsPerformed = !taskState.DidEditFile && !taskState.HasEverUsedTools;

            return responseIndicatesNoChange || (noEditsPerformed && taskState.Iteration > 0);
        }
    }
}
