using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Semantic tool groups for intent-based filtering.
    /// Each tool belongs to one group; intents select a combination of groups.
    /// </summary>
    [Flags]
    public enum ToolGroup
    {
        Core     = 1,
        Edit     = 2,
        Search   = 4,
        Advanced = 8,
        All      = Core | Edit | Search | Advanced
    }

    /// <summary>
    /// Selects tools based on user intent and task complexity.
    /// Reduces tool definition token overhead by sending only relevant tool subsets.
    /// Graceful fallback: unknown tools default to Advanced; unknown intents get All.
    /// </summary>
    public static class DynamicToolSelector
    {
        private static readonly Dictionary<string, ToolGroup> ToolGroupMap =
            new Dictionary<string, ToolGroup>(StringComparer.OrdinalIgnoreCase)
            {
                ["read_file"] = ToolGroup.Core,
                ["ask_followup_question"] = ToolGroup.Core,
                ["validate_file"] = ToolGroup.Core,
                ["edit"] = ToolGroup.Edit,
                ["write_file"] = ToolGroup.Edit,
                ["grep_search"] = ToolGroup.Search,
                ["glob"] = ToolGroup.Search,
                ["list_dir"] = ToolGroup.Search,
                ["list_code_definition_names"] = ToolGroup.Search,
                ["list_projects"] = ToolGroup.Search,
                ["run_command"] = ToolGroup.Advanced,
            };

        private static readonly string[] ConversationKeywords = new[]
        {
            "你好", "hello", "hi", "hey", "谢谢", "thanks", "再见", "bye"
        };

        private static readonly string[] ReadKeywords = new[]
        {
            "read", "show", "display", "print", "cat", "view", "look", "see", "check",
            "读", "看", "查看", "显示", "打印", "浏览"
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
        /// Select tools for the LLM based on intent and complexity.
        /// Conversation → ask_followup_question only.
        /// Other intents → tool subset based on ToolGroup mapping.
        /// Unknown/general intent → all tools (safe fallback).
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
                return allTools.Where(t => t.Name == "ask_followup_question").ToList();

            var groups = GetGroupsForIntent(intent, complexity);
            if (groups == ToolGroup.All)
                return allTools;

            return allTools.Where(t => IsToolInGroups(t.Name, groups)).ToList();
        }

        /// <summary>
        /// Map intent + complexity to required tool groups.
        /// </summary>
        internal static ToolGroup GetGroupsForIntent(string intent, TaskComplexity complexity)
        {
            bool isComplex = complexity == TaskComplexity.Complex;
            switch (intent)
            {
                case "read":
                case "analyze":
                    return isComplex
                        ? ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced
                        : ToolGroup.Core | ToolGroup.Search;
                case "modify":
                case "bug_fix":
                    return isComplex
                        ? ToolGroup.All
                        : ToolGroup.Core | ToolGroup.Edit | ToolGroup.Search;
                case "command":
                    return isComplex
                        ? ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced
                        : ToolGroup.Core | ToolGroup.Advanced;
                case "general":
                default:
                    return ToolGroup.All;
            }
        }

        /// <summary>
        /// Check if a tool belongs to any of the specified groups.
        /// Unmapped tools (e.g., MCP/gitnexus_*) default to Advanced.
        /// </summary>
        internal static bool IsToolInGroups(string toolName, ToolGroup groups)
        {
            if (ToolGroupMap.TryGetValue(toolName, out var group))
                return (groups & group) != 0;
            // Unmapped tools (MCP dynamic registration) → Advanced
            return (groups & ToolGroup.Advanced) != 0;
        }

        /// <summary>
        /// Classify user request intent.
        /// Priority: conversation > bug_fix > modify > command > analyze > read > general.
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

            if (ReadKeywords.Any(k => lower.Contains(k)))
                return "read";

            return "general";
        }
    }
}
