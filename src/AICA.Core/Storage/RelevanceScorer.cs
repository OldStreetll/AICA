using System;
using System.Collections.Generic;
using System.Text;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Scores memory entries against a query for relevance-based retrieval.
    /// Supports mixed Chinese/English tokenization.
    /// </summary>
    internal class RelevanceScorer
    {
        /// <summary>
        /// Calculate relevance score of a single memory entry against a query.
        /// Description hits are weighted x2, body hits x1.
        /// </summary>
        public double Score(MemoryEntry entry, string query)
        {
            if (entry == null || string.IsNullOrWhiteSpace(query))
                return 0;

            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0)
                return 0;

            double score = 0;

            // Description: x2 weight
            if (!string.IsNullOrEmpty(entry.Description))
            {
                var descTokens = TokenizeToSet(entry.Description);
                foreach (var qt in queryTokens)
                {
                    if (descTokens.Contains(qt))
                        score += 2;
                }
            }

            // Body: x1 weight
            if (!string.IsNullOrEmpty(entry.Body))
            {
                var bodyTokens = TokenizeToSet(entry.Body);
                foreach (var qt in queryTokens)
                {
                    if (bodyTokens.Contains(qt))
                        score += 1;
                }
            }

            return score;
        }

        /// <summary>
        /// Select top entries by relevance score, respecting a token budget.
        /// </summary>
        public List<MemoryEntry> SelectTopN(List<MemoryEntry> entries, string query, int maxTokens)
        {
            if (entries == null || entries.Count == 0)
                return new List<MemoryEntry>();

            // Score all entries
            var scored = new List<ScoredEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                double s = Score(entries[i], query);
                scored.Add(new ScoredEntry { Entry = entries[i], Score = s, Index = i });
            }

            // Sort by score descending, then by original index for stability
            scored.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                return a.Index.CompareTo(b.Index);
            });

            var result = new List<MemoryEntry>();
            int usedTokens = 0;

            foreach (var item in scored)
            {
                int entryTokens = EstimateTokens(item.Entry);
                if (usedTokens + entryTokens > maxTokens && result.Count > 0)
                    break;

                result.Add(item.Entry);
                usedTokens += entryTokens;
            }

            return result;
        }

        /// <summary>
        /// Estimate token count for a memory entry.
        /// Uses length/3 formula for mixed Chinese/English content.
        /// </summary>
        internal static int EstimateTokens(MemoryEntry entry)
        {
            int len = 0;
            if (entry.Name != null) len += entry.Name.Length;
            if (entry.Description != null) len += entry.Description.Length;
            if (entry.Body != null) len += entry.Body.Length;
            return Math.Max(1, len / 3);
        }

        /// <summary>
        /// Tokenize text into a list of normalized tokens.
        /// English: split by whitespace/punctuation, lowercase, filter length &lt; 3.
        /// Chinese: single-character split, filter stopwords.
        /// </summary>
        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            var tokens = new List<string>();
            var englishWord = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsChinese(c))
                {
                    // Flush any pending English word
                    FlushEnglishWord(englishWord, tokens);

                    if (!ChineseStopwords.IsStopword(c))
                    {
                        tokens.Add(c.ToString());
                    }
                }
                else if (char.IsLetterOrDigit(c))
                {
                    englishWord.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    // Separator — flush English word
                    FlushEnglishWord(englishWord, tokens);
                }
            }

            FlushEnglishWord(englishWord, tokens);
            return tokens;
        }

        private static HashSet<string> TokenizeToSet(string text)
        {
            var tokens = Tokenize(text);
            return new HashSet<string>(tokens, StringComparer.Ordinal);
        }

        private static void FlushEnglishWord(StringBuilder word, List<string> tokens)
        {
            if (word.Length >= 3)
            {
                tokens.Add(word.ToString());
            }
            word.Clear();
        }

        private static bool IsChinese(char c)
        {
            return c >= 0x4E00 && c <= 0x9FFF;
        }

        private struct ScoredEntry
        {
            public MemoryEntry Entry;
            public double Score;
            public int Index;
        }
    }
}
