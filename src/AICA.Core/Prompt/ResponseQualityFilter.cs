using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AICA.Core.LLM;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Response quality filter that post-processes LLM output
    /// to remove verbosity, internal reasoning leaks, and conversational filler.
    /// Supports configuration via ResponseQualityConfig for per-model customization.
    /// Inspired by Cline's forbidden phrases and learn-claude-code's micro-compaction.
    /// </summary>
    public static class ResponseQualityFilter
    {
        /// <summary>
        /// Active configuration. Defaults to hardcoded patterns.
        /// Call Configure() to override with custom/model-specific patterns.
        /// </summary>
        private static ResponseQualityConfig _config;

        /// <summary>
        /// Compiled regex patterns from config, lazily built on first use after Configure().
        /// </summary>
        private static Regex[] _configuredTrailingPatterns;
        private static (string phrase, bool isCaseSensitive)[] _configuredOpeners;
        private static string[] _configuredReasoningPatterns;
        private static string[] _configuredMetaPatterns;

        /// <summary>
        /// Apply a custom configuration. Call this at startup or when switching models.
        /// Pass null to reset to defaults.
        /// </summary>
        public static void Configure(ResponseQualityConfig config)
        {
            _config = config;
            // Invalidate compiled patterns
            _configuredTrailingPatterns = null;
            _configuredOpeners = null;
            _configuredReasoningPatterns = null;
            _configuredMetaPatterns = null;
        }

        /// <summary>
        /// Get the active forbidden openers (from config or defaults).
        /// </summary>
        private static (string phrase, bool isCaseSensitive)[] GetForbiddenOpeners()
        {
            if (_config == null) return ForbiddenOpeners;
            if (_configuredOpeners != null) return _configuredOpeners;
            _configuredOpeners = _config.ForbiddenOpeners
                .ConvertAll(e => (e.Phrase, e.IsCaseSensitive)).ToArray();
            return _configuredOpeners;
        }

        /// <summary>
        /// Get the active trailing offer patterns (from config or defaults).
        /// </summary>
        private static Regex[] GetTrailingOfferPatterns()
        {
            if (_config == null) return TrailingOfferPatterns;
            if (_configuredTrailingPatterns != null) return _configuredTrailingPatterns;
            _configuredTrailingPatterns = _config.TrailingOfferPatterns
                .ConvertAll(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToArray();
            return _configuredTrailingPatterns;
        }

        /// <summary>
        /// Get the active reasoning start patterns (from config or defaults).
        /// </summary>
        private static string[] GetReasoningStartPatterns()
        {
            if (_config == null) return ReasoningStartPatterns;
            if (_configuredReasoningPatterns != null) return _configuredReasoningPatterns;
            _configuredReasoningPatterns = _config.ReasoningStartPatterns.ToArray();
            return _configuredReasoningPatterns;
        }

        /// <summary>
        /// Get the active meta-reasoning patterns (from config or defaults).
        /// </summary>
        private static string[] GetMetaReasoningPatterns()
        {
            if (_config == null) return MetaReasoningPatterns;
            if (_configuredMetaPatterns != null) return _configuredMetaPatterns;
            _configuredMetaPatterns = _config.MetaReasoningPatterns.ToArray();
            return _configuredMetaPatterns;
        }

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
            // BF-02 fix: catch "请问...帮助/需要" trailing offers
            new Regex(@"\s*请问.*帮助.*[?？]?\s*$", RegexOptions.Compiled),
            new Regex(@"\s*请问.*需要.*[?？]?\s*$", RegexOptions.Compiled),
            new Regex(@"\s*请问有什么.*[?？]?\s*$", RegexOptions.Compiled),
        };

        #endregion

        #region Internal Reasoning Patterns

        private static readonly string[] ReasoningStartPatterns = new[]
        {
            // English planning/narration — only tool-specific patterns
            "i need to check", "i need to search", "i need to read", "i need to look",
            "i should check", "i should search", "i should read", "i should look",
            "i will call", "i will use the", "i'm going to use",
            "let me check", "let me search", "let me look at",
            "let me read the", "let me find", "let me analyze the",
            "i'll use the", "i'll check", "i'll search",
            "first, i need to", "first, i should", "first, let me check",
            "next, i need to", "next, i should",
            // Chinese planning/narration — only tool-specific
            "我需要查看", "我需要搜索", "我需要读取", "我需要检查",
            "我需要使用", "我需要调用",
            "我应该查看", "我应该搜索",
            "我将调用", "我将使用工具",
            "让我查看", "让我搜索", "让我检查", "让我读取",
            "首先，我需要", "首先，让我查",
            "接下来，我需要", "接下来，让我",
            // BF-02 fix: catch LLM reasoning about user intent
            "用户要求", "用户只是", "用户用", "用户在",
            "用户想要", "用户希望",
        };

        private static readonly string[] MetaReasoningPatterns = new[]
        {
            "the user is asking me to", "the user wants me to", "the user is requesting",
            "the user might want", "the user may want",
            "looking at the instructions",
            "i think the user wants me to", "let me re-read the",
            // Chinese meta — only clear meta-reasoning
            "用户想要我", "用户在请求我", "用户可能想让我",
            "看起来用户想",
            // BF-02/TC-01 fix: catch LLM meta-reasoning about instructions/rules
            "let me re-read", "let me reconsider", "let me think about",
            "according to the rules", "based on the instructions",
            "根据规则", "让我重新阅读", "让我重新考虑",
            "the user asked me to read", "the user asked me to search",
            "the user asked me to analyze", "the user asked me to check",
            "the user is asking me to",
            // BF-02 iterative fix: catch MiniMax self-referential meta statements
            "我不需要调用", "不需要执行", "不需要调用任何工具",
            "由于这是一个简单", "这是一个简单的问候",
            "根据自定义指令", "根据我的指导",
            "i should respond", "i don't need to call", "i should not call",
            "since this is a simple", "no tools are needed",
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

            foreach (var (phrase, isCaseSensitive) in GetForbiddenOpeners())
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

            foreach (var pattern in GetTrailingOfferPatterns())
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
        ///
        /// BF-02 fix: MiniMax model outputs reasoning and answer in one text block (no thinking tags).
        /// The old 300-char threshold skipped all pattern checks for mixed content.
        /// New approach: check the BEGINNING of text for reasoning patterns regardless of total length.
        /// Meta-reasoning (Contains match) still uses length threshold to avoid false positives.
        /// </summary>
        public static bool IsInternalReasoning(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Skip if text is primarily a code block
            if (text.TrimStart().StartsWith("```"))
                return false;

            var trimmed = text.Trim();
            var lower = trimmed.ToLowerInvariant();

            // Check if text STARTS with reasoning patterns.
            // Only check the first 200 chars for StartsWith — this works regardless of total length,
            // catching MiniMax's mixed reasoning+answer output.
            var checkPortion = lower.Length > 200 ? lower.Substring(0, 200) : lower;

            foreach (var pattern in GetReasoningStartPatterns())
            {
                if (checkPortion.StartsWith(pattern))
                    return true;
            }

            // Meta-reasoning patterns use Contains — keep the length threshold
            // to avoid false positives on long legitimate answers.
            if (trimmed.Length <= 500)
            {
                foreach (var pattern in GetMetaReasoningPatterns())
                {
                    if (lower.Contains(pattern))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Apply all quality filters to a response: strip reasoning prefix, forbidden openers, trailing offers.
        /// Single entry point for the conversational message path in AgentExecutor.
        /// </summary>
        public static string ApplyAllFilters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;

            // Strip reasoning prefix if detected (MiniMax mixed output)
            if (IsInternalReasoning(result))
            {
                result = StripReasoningPrefix(result);
            }

            result = StripForbiddenOpeners(result);
            result = StripTrailingOffers(result);

            return result;
        }

        /// <summary>
        /// Strip internal reasoning prefix from mixed reasoning+answer text.
        /// MiniMax may output multiple reasoning paragraphs before the actual answer.
        /// Iteratively strips paragraphs that start with reasoning patterns until
        /// a non-reasoning paragraph is found.
        /// </summary>
        public static string StripReasoningPrefix(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var current = text.Trim();
            var maxStrips = 5; // Safety limit

            for (int i = 0; i < maxStrips; i++)
            {
                var splitIdx = current.IndexOf("\n\n");
                if (splitIdx <= 0 || splitIdx >= current.Length - 20)
                    break;

                var afterSplit = current.Substring(splitIdx).Trim();
                if (string.IsNullOrWhiteSpace(afterSplit))
                    break;

                // Check if the remaining text still starts with reasoning
                var lower = afterSplit.ToLowerInvariant();
                var checkPortion = lower.Length > 200 ? lower.Substring(0, 200) : lower;

                bool stillReasoning = false;

                // Check reasoning-start patterns (StartsWith)
                foreach (var pattern in GetReasoningStartPatterns())
                {
                    if (checkPortion.StartsWith(pattern))
                    {
                        stillReasoning = true;
                        break;
                    }
                }

                // Also check meta-reasoning patterns (Contains) for short paragraphs
                // MiniMax often has multiple meta-reasoning paragraphs before the answer
                if (!stillReasoning && afterSplit.Length < 200)
                {
                    foreach (var pattern in GetMetaReasoningPatterns())
                    {
                        if (lower.Contains(pattern))
                        {
                            stillReasoning = true;
                            break;
                        }
                    }
                }

                current = afterSplit;

                // If the remaining text is NOT reasoning, we found the answer
                if (!stillReasoning)
                    break;
            }

            return current;
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
