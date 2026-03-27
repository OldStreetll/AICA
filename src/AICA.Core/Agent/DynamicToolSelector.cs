using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Selects tools based on user intent and GitNexus availability.
    /// Trust-based design: LLM sees tool descriptions and decides which to use.
    /// Only conversation intent (greetings) uses a minimal tool set.
    /// When GitNexus is available, overlapping built-in tools are hidden.
    /// </summary>
    public static class DynamicToolSelector
    {
        // Core tools always included (conversation-only minimal set)
        private static readonly HashSet<string> CoreTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "attempt_completion",
            "ask_followup_question"
        };

        // Tools that overlap with GitNexus capabilities — hidden when GitNexus is available
        // to reduce tool competition and improve GitNexus selection rate
        private static readonly HashSet<string> GitNexusOverlapTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "list_code_definition_names",   // gitnexus_context provides richer structural info
            "find_by_name"                  // gitnexus_query covers file/symbol lookup
        };

        // Intent classification keywords
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
        /// Select tools based on user request intent and GitNexus availability.
        /// Trust-based design: tools are visible to the LLM so it can choose based
        /// on its own reasoning and the tool descriptions.
        /// When GitNexus is available, overlapping built-in tools are hidden to
        /// reduce competition and improve GitNexus selection rate.
        /// </summary>
        public static IReadOnlyList<ToolDefinition> SelectTools(
            string userRequest,
            TaskComplexity complexity,
            IReadOnlyList<ToolDefinition> allTools,
            bool gitNexusAvailable = false)
        {
            if (allTools == null || allTools.Count == 0)
                return allTools;

            // Only filter for pure conversation intent (greetings don't need 15 tools)
            var intent = ClassifyIntent(userRequest);
            if (intent == "conversation")
                return allTools.Where(t => CoreTools.Contains(t.Name)).ToList();

            // When GitNexus is available, hide overlapping built-in tools
            if (gitNexusAvailable)
                return allTools.Where(t => !GitNexusOverlapTools.Contains(t.Name)).ToList();

            // GitNexus unavailable: return full tool set (keep built-in fallbacks)
            return allTools;
        }

        /// <summary>
        /// Classify user request intent for tool selection.
        /// </summary>
        internal static string ClassifyIntent(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return "conversation";

            var lower = userRequest.ToLowerInvariant();

            // Short greetings → conversation
            if (userRequest.Length < 15 && ConversationKeywords.Any(k => lower.Contains(k)))
                return "conversation";

            // Bug fix intent (before modify — "fix" is in both, but bug keywords take priority)
            if (BugFixKeywords.Any(k => lower.Contains(k)))
                return "bug_fix";

            // Modification intent
            if (ModifyKeywords.Any(k => lower.Contains(k)))
                return "modify";

            // Command execution intent
            if (CommandKeywords.Any(k => lower.Contains(k)))
                return "command";

            // Analysis intent
            if (AnalyzeKeywords.Any(k => lower.Contains(k)))
                return "analyze";

            // Default: read (covers most simple questions)
            return "read";
        }
    }
}
