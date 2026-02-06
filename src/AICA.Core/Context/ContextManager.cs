using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Context
{
    /// <summary>
    /// Manages conversation context within a token budget
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
        /// Rough token estimation (~4 chars per token for English, ~2 for CJK)
        /// </summary>
        private int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough heuristic: average ~3.5 characters per token
            return (int)Math.Ceiling(text.Length / 3.5);
        }

        private string TruncateToTokens(string text, int targetTokens)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int targetChars = (int)(targetTokens * 3.5);
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
