using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.LLM;

namespace AICA.Core.Context
{
    /// <summary>
    /// Manages conversation context within a token budget.
    /// Provides conversation history truncation (keep first + last, trim middle)
    /// and improved token estimation for mixed CJK/Latin text.
    /// </summary>
    public class ContextManager
    {
        private readonly int _maxTokenBudget;
        private readonly List<ContextItem> _items = new List<ContextItem>();

        public ContextManager(int maxTokenBudget = 32000)
        {
            _maxTokenBudget = maxTokenBudget;
        }

        /// <summary>
        /// Add a context item with a given priority
        /// </summary>
        public void AddItem(string key, string content, ContextPriority priority)
        {
            // Remove existing item with same key
            _items.RemoveAll(i => i.Key == key);
            _items.Add(new ContextItem
            {
                Key = key,
                Content = content,
                Priority = priority,
                EstimatedTokens = EstimateTokens(content),
                AddedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Get context items that fit within the token budget, ordered by priority
        /// </summary>
        public IReadOnlyList<ContextItem> GetContextWithinBudget()
        {
            var sorted = _items
                .OrderByDescending(i => (int)i.Priority)
                .ThenByDescending(i => i.AddedAt)
                .ToList();

            var result = new List<ContextItem>();
            int totalTokens = 0;

            foreach (var item in sorted)
            {
                if (totalTokens + item.EstimatedTokens <= _maxTokenBudget)
                {
                    result.Add(item);
                    totalTokens += item.EstimatedTokens;
                }
                else if (item.Priority == ContextPriority.Critical)
                {
                    // Critical items are always included, truncate if needed
                    var available = _maxTokenBudget - totalTokens;
                    if (available > 100)
                    {
                        var truncated = TruncateToTokens(item.Content, available);
                        result.Add(new ContextItem
                        {
                            Key = item.Key,
                            Content = truncated + "\n... (truncated)",
                            Priority = item.Priority,
                            EstimatedTokens = available,
                            AddedAt = item.AddedAt
                        });
                        totalTokens += available;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Truncate conversation history to fit within a token budget.
        /// Strategy: keep the system message (index 0) and the first user message (index 1),
        /// keep the most recent N messages, and trim messages in between.
        /// A notice is inserted where messages were removed.
        /// </summary>
        /// <param name="messages">Full conversation history</param>
        /// <param name="maxTokens">Maximum total tokens for the conversation</param>
        /// <param name="keepRecentCount">Number of recent messages to always keep</param>
        /// <returns>Truncated conversation history</returns>
        public static List<ChatMessage> TruncateConversation(
            List<ChatMessage> messages,
            int maxTokens,
            int keepRecentCount = 10)
        {
            if (messages == null || messages.Count == 0)
                return messages;

            int totalTokens = messages.Sum(m => EstimateTokens(m.Content));

            // If within budget, return as-is
            if (totalTokens <= maxTokens)
                return messages;

            // Strategy: keep [0] (system), [1] (first user message), and the last keepRecentCount messages.
            // Remove messages in between until we're under budget.

            // At minimum, we need system + first user + recent messages
            int preserveStart = Math.Min(2, messages.Count); // system + first user
            int preserveEnd = Math.Min(keepRecentCount, messages.Count - preserveStart);

            if (preserveStart + preserveEnd >= messages.Count)
            {
                // Not enough messages to trim from the middle; truncate individual long messages instead
                return TruncateLongMessages(messages, maxTokens);
            }

            // Build result: start messages + notice + end messages
            var result = new List<ChatMessage>();

            // Add preserved start messages
            for (int i = 0; i < preserveStart; i++)
                result.Add(messages[i]);

            // Calculate how many middle messages we removed
            int removedStart = preserveStart;
            int removedEnd = messages.Count - preserveEnd;
            int removedCount = removedEnd - removedStart;

            if (removedCount > 0)
            {
                // Insert truncation notice
                result.Add(ChatMessage.System(
                    $"[NOTE: {removedCount} earlier messages were removed to fit the context window. " +
                    "The conversation continues with the most recent messages below.]"));
            }

            // Add preserved end messages
            for (int i = messages.Count - preserveEnd; i < messages.Count; i++)
                result.Add(messages[i]);

            // If still over budget, recursively truncate long individual messages
            int resultTokens = result.Sum(m => EstimateTokens(m.Content));
            if (resultTokens > maxTokens)
            {
                result = TruncateLongMessages(result, maxTokens);
            }

            return result;
        }

        /// <summary>
        /// Truncate individual long messages (tool results, file contents) to fit budget.
        /// Preserves system and user messages, truncates assistant and tool messages.
        /// </summary>
        private static List<ChatMessage> TruncateLongMessages(List<ChatMessage> messages, int maxTokens)
        {
            var result = new List<ChatMessage>(messages);
            int totalTokens = result.Sum(m => EstimateTokens(m.Content));

            if (totalTokens <= maxTokens)
                return result;

            // Truncate from oldest non-system, non-user messages first
            for (int i = 1; i < result.Count && totalTokens > maxTokens; i++)
            {
                var msg = result[i];
                if (msg.Role == LLM.ChatRole.System) continue;

                int msgTokens = EstimateTokens(msg.Content);
                // Only truncate messages > 500 tokens
                if (msgTokens > 500)
                {
                    int targetTokens = Math.Max(200, msgTokens / 3);
                    var truncated = TruncateToTokens(msg.Content, targetTokens);
                    int saved = msgTokens - EstimateTokens(truncated);
                    msg.Content = truncated + "\n... (truncated to save context space)";
                    totalTokens -= saved;
                }
            }

            return result;
        }

        /// <summary>
        /// Get total estimated tokens of all items
        /// </summary>
        public int GetTotalEstimatedTokens()
        {
            return _items.Sum(i => i.EstimatedTokens);
        }

        /// <summary>
        /// Get remaining token budget
        /// </summary>
        public int GetRemainingBudget()
        {
            return Math.Max(0, _maxTokenBudget - GetContextWithinBudget().Sum(i => i.EstimatedTokens));
        }

        /// <summary>
        /// Remove a context item by key
        /// </summary>
        public bool Remove(string key)
        {
            return _items.RemoveAll(i => i.Key == key) > 0;
        }

        /// <summary>
        /// Clear all context items
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// Improved token estimation that accounts for CJK characters.
        /// CJK characters typically use ~1.5 tokens each, while Latin text averages ~4 chars/token.
        /// </summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int cjkCount = 0;
            int otherCount = 0;

            foreach (char c in text)
            {
                if (IsCJK(c))
                    cjkCount++;
                else
                    otherCount++;
            }

            // CJK: ~1.5 tokens per char; Latin/other: ~0.25 tokens per char (4 chars/token)
            int tokens = (int)Math.Ceiling(cjkCount * 1.5 + otherCount * 0.25);
            return Math.Max(1, tokens);
        }

        private static bool IsCJK(char c)
        {
            // CJK Unified Ideographs, CJK Extension A, Hangul, Hiragana, Katakana, full-width
            return (c >= 0x4E00 && c <= 0x9FFF)   // CJK Unified
                || (c >= 0x3400 && c <= 0x4DBF)    // CJK Extension A
                || (c >= 0xAC00 && c <= 0xD7AF)    // Hangul
                || (c >= 0x3040 && c <= 0x309F)     // Hiragana
                || (c >= 0x30A0 && c <= 0x30FF)     // Katakana
                || (c >= 0xFF00 && c <= 0xFFEF);    // Fullwidth
        }

        private static string TruncateToTokens(string text, int targetTokens)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Use a conservative estimate for truncation: ~2.5 chars per token average
            int targetChars = (int)(targetTokens * 2.5);
            if (text.Length <= targetChars) return text;
            return text.Substring(0, targetChars);
        }
    }

    public class ContextItem
    {
        public string Key { get; set; }
        public string Content { get; set; }
        public ContextPriority Priority { get; set; }
        public int EstimatedTokens { get; set; }
        public DateTime AddedAt { get; set; }
    }

    public enum ContextPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
}
