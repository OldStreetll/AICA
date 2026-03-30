using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Selects tools based on user intent.
    /// Trust-based design: LLM sees all tool descriptions and decides which to use.
    /// Only conversation intent (greetings) uses a minimal tool set.
    /// </summary>
    public static class DynamicToolSelector
    {
        // Minimal tool set for pure conversation (greetings don't need 14 tools)
        private static readonly HashSet<string> ConversationTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ask_followup_question"
        };

        private static readonly string[] ConversationKeywords = new[]
        {
            "你好", "hello", "hi", "hey", "谢谢", "thanks", "再见", "bye"
        };

        private static readonly string[] ModifyKeywords = new[]
        {
            "edit", "modify", "fix", "refactor", "add", "remove", "delete", "create", "write",
            "change", "update", "implement", "replace", "insert", "append",
            "修改", "编辑", "修复", "重构", "添加", "删除", "创建", "写入",
            "改", "加", "替换", "实现", "插入"
        };

        private static readonly string[] CommandKeywords = new[]
        {
            "run", "execute", "command", "shell", "terminal", "cmd",
            "运行", "执行", "命令", "终端",
            "dotnet", "npm", "pip", "git", "nuget", "node", "python"
        };

        private static readonly string[] AnalyzeKeywords = new[]
        {
            "analyze", "analysis", "overview", "architecture", "structure", "investigate",
            "分析", "概览", "架构", "结构", "调查", "研究", "全面", "完整"
        };

        private static readonly string[] BugFixKeywords = new[]
        {
            "报错", "错误", "崩溃", "异常", "bug", "error", "crash", "exception",
            "段错误", "segfault", "内存泄漏", "leak", "死锁", "deadlock",
            "不工作", "doesn't work", "失败", "coredump", "core dump"
        };

        /// <summary>
        /// Select tools for the LLM. Only filters for conversation intent.
        /// All other intents get the full tool set — trust the LLM to choose.
        /// </summary>
        public static IReadOnlyList<ToolDefinition> SelectTools(
            string userRequest,
            TaskComplexity complexity,
            IReadOnlyList<ToolDefinition> allTools,
            bool gitNexusAvailable = false)
        {
            if (allTools == null || allTools.Count == 0)
                return allTools;

            var intent = ClassifyIntent(userRequest);
            if (intent == "conversation")
                return allTools.Where(t => ConversationTools.Contains(t.Name)).ToList();

            return allTools;
        }

        /// <summary>
        /// Classify user request intent.
        /// </summary>
        internal static string ClassifyIntent(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return "conversation";

            var lower = userRequest.ToLowerInvariant();

            if (userRequest.Length < 15 && ConversationKeywords.Any(k => lower.Contains(k)))
                return "conversation";

            if (BugFixKeywords.Any(k => lower.Contains(k)))
                return "bug_fix";

            if (ModifyKeywords.Any(k => lower.Contains(k)))
                return "modify";

            if (CommandKeywords.Any(k => lower.Contains(k)))
                return "command";

            if (AnalyzeKeywords.Any(k => lower.Contains(k)))
                return "analyze";

            return "read";
        }
    }
}
