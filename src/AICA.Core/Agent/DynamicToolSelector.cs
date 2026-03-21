using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Selects a subset of tools to inject into the system prompt based on
    /// user intent classification and task complexity.
    /// Reduces token usage for simple requests by ~3000 tokens.
    /// </summary>
    public static class DynamicToolSelector
    {
        // Core tools always included
        private static readonly HashSet<string> CoreTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "attempt_completion",
            "ask_followup_question"
        };

        // Read-only tools for analysis/exploration
        private static readonly HashSet<string> ReadTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "read_file", "list_dir", "list_code_definition_names",
            "grep_search", "find_by_name", "list_projects", "log_analysis"
        };

        // Write tools for modification tasks
        private static readonly HashSet<string> WriteTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "edit", "write_to_file", "run_command"
        };

        // Context management tools
        private static readonly HashSet<string> ContextTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "condense", "update_plan"
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

        private static readonly string[] AnalyzeKeywords = new[]
        {
            "analyze", "analysis", "overview", "architecture", "structure", "investigate",
            "分析", "概览", "架构", "结构", "调查", "研究", "全面", "完整"
        };

        /// <summary>
        /// Select tools based on user request intent and complexity.
        /// Complex tasks always get full tool set (safety fallback).
        /// </summary>
        public static IReadOnlyList<ToolDefinition> SelectTools(
            string userRequest,
            TaskComplexity complexity,
            IReadOnlyList<ToolDefinition> allTools)
        {
            if (allTools == null || allTools.Count == 0)
                return allTools;

            // Complex tasks: always return all tools
            if (complexity == TaskComplexity.Complex)
                return allTools;

            var intent = ClassifyIntent(userRequest);
            var selectedNames = GetToolNamesForIntent(intent);

            // If no filtering needed, return all
            if (selectedNames == null)
                return allTools;

            return allTools.Where(t => selectedNames.Contains(t.Name)).ToList();
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

            // Modification intent
            if (ModifyKeywords.Any(k => lower.Contains(k)))
                return "modify";

            // Analysis intent
            if (AnalyzeKeywords.Any(k => lower.Contains(k)))
                return "analyze";

            // Default: read (covers most simple questions)
            return "read";
        }

        /// <summary>
        /// Get tool names for a given intent. Returns null for "full" (all tools).
        /// </summary>
        private static HashSet<string> GetToolNamesForIntent(string intent)
        {
            switch (intent)
            {
                case "conversation":
                    return CoreTools;

                case "read":
                    var readSet = new HashSet<string>(CoreTools, StringComparer.OrdinalIgnoreCase);
                    foreach (var t in ReadTools) readSet.Add(t);
                    return readSet;

                case "analyze":
                    var analyzeSet = new HashSet<string>(CoreTools, StringComparer.OrdinalIgnoreCase);
                    foreach (var t in ReadTools) analyzeSet.Add(t);
                    foreach (var t in ContextTools) analyzeSet.Add(t);
                    return analyzeSet;

                case "modify":
                    // Modify needs everything
                    return null;

                default:
                    return null;
            }
        }
    }
}
