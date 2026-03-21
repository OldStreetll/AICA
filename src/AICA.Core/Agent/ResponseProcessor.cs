using System.Linq;
using System.Text.RegularExpressions;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Handles response quality analysis: tool execution claim detection,
    /// conversational message detection, and tool planning text detection.
    /// Extracted from AgentExecutor to separate response processing concerns.
    /// </summary>
    public static class ResponseProcessor
    {
        /// <summary>
        /// Detect if the model claims to have executed a tool but didn't actually call it.
        /// This is a common hallucination pattern.
        /// </summary>
        public static bool DetectToolExecutionClaim(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lowerText = text.ToLowerInvariant();

            var executionClaimPatterns = new[]
            {
                "i have executed", "i've executed", "i executed",
                "i have run", "i've run", "i ran",
                "i have called", "i've called", "i called",
                "already executed", "already run", "already called",
                "the command has been", "command was executed",
                "successfully executed", "execution complete",
                "已执行", "已运行", "已调用",
                "执行了", "运行了", "调用了",
                "执行完成", "运行完成",
                "成功执行", "成功运行",
                "here are the results", "here is the result",
                "the results are", "the result is",
                "结果如下", "结果为", "结果是",
                "以下是结果", "查看结果"
            };

            bool hasExecutionClaim = executionClaimPatterns.Any(p => lowerText.Contains(p));

            bool hasStructuredOutput =
                (lowerText.Contains("exit code:") || lowerText.Contains("stdout:") || lowerText.Contains("stderr:")) ||
                (lowerText.Contains("状态") && lowerText.Contains("文件")) ||
                (text.Contains("|") && text.Contains("---"));

            bool hasFakeExactStats = text.Contains("[TOOL_EXACT_STATS:");

            bool hasFakeSearchResults = false;
            if (lowerText.Contains("match") && lowerText.Contains("file"))
            {
                hasFakeSearchResults =
                    Regex.IsMatch(text, @"[Ff]ound \d+ match") ||
                    Regex.IsMatch(text, @"\d+\s*处匹配") ||
                    Regex.IsMatch(text, @"匹配.*\d+.*文件");
            }

            return hasExecutionClaim || hasStructuredOutput || hasFakeExactStats || hasFakeSearchResults;
        }

        /// <summary>
        /// Detect if a user message is likely conversational (greeting, thanks)
        /// rather than a coding task.
        /// </summary>
        public static bool IsLikelyConversational(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            var trimmed = message.Trim();

            if (trimmed.Length > 20) return false;

            var taskKeywords = new[]
            {
                "文件", "代码", "读", "写", "编辑", "修改", "创建", "删除", "搜索", "查找", "查看",
                "运行", "执行", "构建", "编译", "测试", "分析", "重构", "调试", "打开", "关闭",
                "添加", "移除", "更新", "生成", "实现", "修复", "优化", "部署", "安装", "配置",
                "项目", "目录", "函数", "类", "方法", "变量", "错误", "bug", "报错",
                "是什么", "怎么", "哪些", "有多少", "在哪", "为什么", "如何",
                "file", "code", "read", "write", "edit", "create", "delete", "search", "find",
                "run", "exec", "build", "compile", "test", "debug", "refactor", "fix",
                "list", "grep", "dir", "open", "close", "add", "remove", "update", "generate",
                "implement", "deploy", "install", "config", "project", "class", "function",
                "method", "variable", "error", "help me", "帮我", "请帮"
            };

            var lower = trimmed.ToLowerInvariant();
            foreach (var keyword in taskKeywords)
            {
                if (lower.Contains(keyword)) return false;
            }

            return true;
        }

        /// <summary>
        /// Detect if text is primarily about planning future tool usage
        /// (as opposed to summarizing results or answering the user's question).
        /// </summary>
        public static bool IsToolPlanningText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lower = text.ToLowerInvariant().Trim();

            var toolPlanningMarkers = new[]
            {
                "i will call", "i will use the", "i'll use the", "i'll call",
                "let me use the", "let me call", "let me run the",
                "i need to use the", "i need to call", "i need to run",
                "i should use the", "i should call",
                "我将调用", "我将使用工具", "让我调用", "让我使用工具",
                "我需要调用", "我需要使用工具",
                "接下来我将调用", "下一步我将使用",
            };

            foreach (var marker in toolPlanningMarkers)
            {
                if (lower.Contains(marker)) return true;
            }

            if (lower.Length < 150)
            {
                var planningStarts = new[]
                {
                    "i need to check", "i need to search", "i need to read",
                    "i should check", "i should search", "i should read",
                    "我需要查看", "我需要搜索", "我需要读取", "我需要检查",
                    "我应该查看", "我应该搜索",
                };
                foreach (var start in planningStarts)
                {
                    if (lower.StartsWith(start)) return true;
                }
            }

            return false;
        }
    }
}
