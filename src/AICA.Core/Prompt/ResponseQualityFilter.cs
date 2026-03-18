using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AICA.Core.LLM;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Stateless response quality filter that post-processes LLM output
    /// to remove verbosity, internal reasoning leaks, and conversational filler.
    /// Inspired by Cline's forbidden phrases and learn-claude-code's micro-compaction.
    /// </summary>
    public static class ResponseQualityFilter
    {
        private static readonly Regex ThinkingTagRegex = new Regex(
            @"<thinking>(.*?)</thinking>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        #region Forbidden Openers

        private static readonly (string phrase, bool isCaseSensitive)[] ForbiddenOpeners = new[]
        {
            ("Great, ", false),
            ("Great! ", false),
            ("Certainly, ", false),
            ("Certainly! ", false),
            ("Okay, ", false),
            ("Sure, ", false),
            ("Sure! ", false),
            ("Of course, ", false),
            ("Of course! ", false),
            ("Absolutely, ", false),
            ("Absolutely! ", false),
            ("No problem, ", false),
            ("No problem! ", false),
            ("Happy to help, ", false),
            ("Happy to help! ", false),
            ("I'd be happy to ", false),
            // Chinese
            ("好的，", true),
            ("好的！", true),
            ("当然，", true),
            ("当然！", true),
            ("没问题，", true),
            ("没问题！", true),
            ("当然可以，", true),
            ("当然可以！", true),
            ("很高兴", true),
        };

        #endregion

        #region Trailing Offer Patterns

        private static readonly Regex[] TrailingOfferPatterns = new[]
        {
            // English — longer/more-specific patterns first to avoid partial matches
            new Regex(@"(?<=[.!?。！？])\s*Let me know if you need\b.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Do you want me to\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Would you like me to\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Need help with anything else\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Need anything else\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Is there anything else\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?<=[.!?。！？])\s*Would you like\b.*[?？]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // Chinese — longer/more-specific patterns first
            new Regex(@"(?<=[。！？])\s*如果你需要.*[。.]?\s*$", RegexOptions.Compiled),
            new Regex(@"(?<=[。！？])\s*如果需要.*请告诉我.*[。.]?\s*$", RegexOptions.Compiled),
            new Regex(@"(?<=[。！？])\s*还需要我.*[?？]?\s*$", RegexOptions.Compiled),
            new Regex(@"(?<=[。！？])\s*要我继续.*[?？]?\s*$", RegexOptions.Compiled),
            new Regex(@"(?<=[。！？])\s*需要其他帮助.*[?？]?\s*$", RegexOptions.Compiled),
        };

        #endregion

        #region Internal Reasoning Patterns

        private static readonly string[] ReasoningStartPatterns = new[]
        {
            // English planning/narration
            "i need to", "i should", "i will", "i'm going to", "let me",
            "i'll ", "first, i", "next, i", "then, i", "now i",
            "i will call", "i will use", "let me check", "let me analyze",
            "let me read", "let me search", "let me look", "let me summarize",
            // Chinese planning/narration
            "我需要", "我应该", "我将", "让我", "我来",
            "首先，", "接下来，", "然后，", "现在我",
            "我将调用", "我将使用", "让我总结",
        };

        private static readonly string[] MetaReasoningPatterns = new[]
        {
            "the user is asking", "the user wants", "the user is requesting",
            "the user might", "the user may", "the user probably",
            "looking at the instructions", "actually,", "wait,",
            "i think the user", "let me re-read",
            "oh wait", "i see -", "this might be",
            // Chinese meta
            "用户想要", "用户在问", "用户在请求", "用户可能",
            "看起来用户", "实际上，", "等等，",
        };

        #endregion

        /// <summary>
        /// Extract &lt;thinking&gt; blocks from response, separating internal reasoning from user-facing text.
        /// </summary>
        public static (string ThinkingContent, string UserFacingContent) ExtractThinking(string response)
        {
            if (string.IsNullOrEmpty(response))
                return (null, response);

            var matches = ThinkingTagRegex.Matches(response);
            if (matches.Count == 0)
                return (null, response);

            var thinkingParts = new List<string>();
            foreach (Match match in matches)
            {
                var content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    thinkingParts.Add(content);
            }

            var userFacing = ThinkingTagRegex.Replace(response, "");
            // Clean up leading/trailing whitespace but preserve internal structure
            userFacing = userFacing.TrimStart('\r', '\n');

            var thinking = thinkingParts.Count > 0
                ? string.Join("\n", thinkingParts)
                : null;

            return (thinking, userFacing);
        }

        /// <summary>
        /// Remove conversational filler phrases from the start of a response.
        /// Only matches at the very beginning of the text.
        /// </summary>
        public static string StripForbiddenOpeners(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            foreach (var (phrase, isCaseSensitive) in ForbiddenOpeners)
            {
                var comparison = isCaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

                if (text.StartsWith(phrase, comparison))
                {
                    return text.Substring(phrase.Length);
                }
            }

            return text;
        }

        /// <summary>
        /// Remove trailing "do you want me to..." / "need anything else?" offers.
        /// Only matches at the end of the text, after a sentence boundary.
        /// </summary>
        public static string StripTrailingOffers(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            foreach (var pattern in TrailingOfferPatterns)
            {
                var match = pattern.Match(text);
                if (match.Success && match.Index > 0)
                {
                    // Only strip if there's meaningful content before the offer
                    var before = text.Substring(0, match.Index).TrimEnd();
                    if (before.Length > 0)
                        return before;
                }
            }

            return text;
        }

        /// <summary>
        /// Detect if text is internal reasoning/planning that should not be shown to the user.
        /// Returns false for code blocks and legitimate user-facing content.
        /// </summary>
        public static bool IsInternalReasoning(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Skip if text is primarily a code block
            if (text.TrimStart().StartsWith("```"))
                return false;

            var lower = text.ToLowerInvariant().Trim();

            // Check if starts with reasoning patterns
            foreach (var pattern in ReasoningStartPatterns)
            {
                if (lower.StartsWith(pattern))
                    return true;
            }

            // Check for meta-reasoning patterns anywhere in text
            foreach (var pattern in MetaReasoningPatterns)
            {
                if (lower.Contains(pattern))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Replace old tool result messages with informative summaries to save context space.
        /// Preserves the most recent N tool results and any short results (&lt;12 chars).
        /// Summaries include tool name and key parameters so the LLM knows what was returned.
        /// </summary>
        public static List<ChatMessage> MicroCompactToolResults(
            List<ChatMessage> messages,
            int keepRecent = 3,
            int minLengthToCompact = 12)
        {
            if (messages == null || messages.Count == 0)
                return new List<ChatMessage>();

            // Find all tool result message indices
            var toolResultIndices = new List<int>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Role == ChatRole.Tool)
                    toolResultIndices.Add(i);
            }

            // If fewer tool results than threshold, no compaction needed
            if (toolResultIndices.Count <= keepRecent)
                return new List<ChatMessage>(messages);

            // Build a lookup from ToolCallId → (toolName, argsJson) from Assistant messages
            var toolCallLookup = new Dictionary<string, (string Name, string Args)>();
            foreach (var msg in messages)
            {
                if (msg.Role != ChatRole.Assistant || msg.ToolCalls == null) continue;
                foreach (var tc in msg.ToolCalls)
                {
                    if (tc.Id != null)
                        toolCallLookup[tc.Id] = (tc.Function?.Name, tc.Function?.Arguments);
                }
            }

            // Determine which indices to compact (all except the last N)
            var indicesToCompact = new HashSet<int>(
                toolResultIndices.Take(toolResultIndices.Count - keepRecent));

            var result = new List<ChatMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                if (indicesToCompact.Contains(i))
                {
                    var msg = messages[i];
                    // Only compact if content is long enough
                    if (msg.Content != null && msg.Content.Length >= minLengthToCompact)
                    {
                        var summary = BuildCompactSummary(msg, toolCallLookup);
                        result.Add(ChatMessage.ToolResult(msg.ToolCallId, summary));
                    }
                    else
                    {
                        result.Add(msg);
                    }
                }
                else
                {
                    result.Add(messages[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// Build an informative compact summary for a tool result message.
        /// Uses the corresponding tool call info to produce tool-specific summaries.
        /// </summary>
        private static string BuildCompactSummary(
            ChatMessage toolResultMsg,
            Dictionary<string, (string Name, string Args)> toolCallLookup)
        {
            var content = toolResultMsg.Content ?? "";

            // Try to find the corresponding tool call
            if (toolResultMsg.ToolCallId == null
                || !toolCallLookup.TryGetValue(toolResultMsg.ToolCallId, out var callInfo)
                || callInfo.Name == null)
            {
                return $"[Previous tool result ({content.Length} chars)]";
            }

            var toolName = callInfo.Name;
            var args = TryParseArgs(callInfo.Args);

            switch (toolName)
            {
                case "read_file":
                {
                    var path = GetArg(args, "path") ?? GetArg(args, "file_path") ?? "unknown";
                    var lineCount = content.Split('\n').Length;
                    return $"[Previously read: {path} ({lineCount} lines, {content.Length} chars)]";
                }

                case "grep_search":
                {
                    var query = GetArg(args, "query") ?? "?";
                    var searchPath = GetArg(args, "path") ?? "workspace";
                    var matchCount = CountOccurrences(content, "Match");
                    return $"[Previously searched: \"{query}\" in {searchPath} — {matchCount} matches]";
                }

                case "edit":
                case "write_to_file":
                {
                    var path = GetArg(args, "path") ?? GetArg(args, "file_path") ?? "unknown";
                    return $"[Previously edited: {path}]";
                }

                case "run_command":
                {
                    var command = GetArg(args, "command") ?? "?";
                    if (command.Length > 50) command = command.Substring(0, 50) + "...";
                    var firstLine = content.Split('\n')[0];
                    if (firstLine.Length > 80) firstLine = firstLine.Substring(0, 80) + "...";
                    return $"[Previously ran: {command} — {firstLine}]";
                }

                case "find_by_name":
                {
                    var pattern = GetArg(args, "pattern") ?? "?";
                    var resultCount = content.Split('\n').Length;
                    return $"[Previously found: \"{pattern}\" — {resultCount} results]";
                }

                case "list_dir":
                {
                    var path = GetArg(args, "path") ?? "workspace";
                    return $"[Previously listed: {path}]";
                }

                default:
                {
                    var preview = content.Length > 80 ? content.Substring(0, 80) + "..." : content;
                    // Remove newlines for compact display
                    preview = preview.Replace('\n', ' ').Replace('\r', ' ');
                    return $"[Previously called {toolName}: {preview}]";
                }
            }
        }

        private static Dictionary<string, string> TryParseArgs(string argsJson)
        {
            if (string.IsNullOrEmpty(argsJson)) return null;
            try
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var doc = System.Text.Json.JsonDocument.Parse(argsJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value.ToString();
                    }
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static string GetArg(Dictionary<string, string> args, string key)
        {
            if (args == null) return null;
            return args.TryGetValue(key, out var val) ? val : null;
        }

        private static int CountOccurrences(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return 0;
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }
    }
}
