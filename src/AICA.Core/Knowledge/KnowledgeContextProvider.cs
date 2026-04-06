using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// Retrieves relevant symbols from a ProjectIndex using TF-IDF keyword matching
    /// and formats them for injection into the system prompt.
    /// </summary>
    public class KnowledgeContextProvider
    {
        private readonly ProjectIndex _index;
        private readonly Dictionary<string, double> _idfScores;

        public KnowledgeContextProvider(ProjectIndex index)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _idfScores = ComputeIdf(index.Symbols);
        }

        /// <summary>
        /// Retrieve top-N relevant symbols for the given query and format as context string.
        /// </summary>
        /// <param name="query">User query text</param>
        /// <param name="maxTokens">Approximate token budget for the output (1 token ≈ 4 chars)</param>
        /// <returns>Formatted context string for system prompt injection</returns>
        public string RetrieveContext(string query, int maxTokens = 2000)
        {
            if (string.IsNullOrWhiteSpace(query) || _index.Symbols.Count == 0)
                return "";

            var queryTerms = Tokenize(query);
            if (queryTerms.Count == 0)
                return "";

            // Score each symbol
            var scored = new List<(SymbolRecord Symbol, double Score)>();
            foreach (var symbol in _index.Symbols)
            {
                var score = ComputeScore(queryTerms, symbol);
                if (score > 0)
                    scored.Add((symbol, score));
            }

            if (scored.Count == 0)
                return "";

            // Sort by score descending, take top results
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var sb = new StringBuilder();
            sb.AppendLine("## Project Knowledge (auto-indexed)");
            sb.AppendLine($"Indexed {_index.FileCount} files, {_index.Symbols.Count} symbols.");
            sb.AppendLine();
            sb.AppendLine("**IMPORTANT**: The following symbol information was extracted from the project source code.");
            sb.AppendLine("Use this information to answer the user's question DIRECTLY without calling read_file.");
            sb.AppendLine("Only call read_file if the user explicitly asks to see source code or if you need implementation details not shown below.");
            sb.AppendLine();
            sb.AppendLine("Relevant symbols for your query:");

            var maxChars = maxTokens * 4;
            var count = 0;
            var maxResults = 10;

            foreach (var (symbol, score) in scored)
            {
                if (count >= maxResults) break;

                var line = FormatSymbol(symbol);
                if (sb.Length + line.Length > maxChars)
                    break;

                sb.AppendLine(line);
                count++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get a summary of the current index state.
        /// </summary>
        public string GetIndexSummary()
        {
            if (_index.Symbols.Count == 0)
                return "No symbols indexed.";

            var kindCounts = _index.Symbols
                .GroupBy(s => s.Kind)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}s");

            return $"Indexed {_index.FileCount} files, {_index.Symbols.Count} symbols " +
                   $"({string.Join(", ", kindCounts)}) in {_index.IndexDuration.TotalSeconds:F1}s";
        }

        /// <summary>
        /// Tokenize a query string by splitting on whitespace, camelCase, underscores.
        /// </summary>
        internal static IReadOnlyList<string> Tokenize(string text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Split on whitespace and punctuation first
            var words = text.Split(new[] { ' ', '\t', ',', '.', '?', '!', '(', ')', '[', ']', '{', '}', ';', ':' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                tokens.Add(word.ToLowerInvariant());

                // Also split camelCase/PascalCase
                foreach (var part in RegexSymbolParser.SplitIdentifier(word))
                {
                    if (part.Length > 1) // skip single chars
                        tokens.Add(part.ToLowerInvariant());
                }
            }

            // Remove common stop words
            tokens.Remove("the");
            tokens.Remove("is");
            tokens.Remove("a");
            tokens.Remove("an");
            tokens.Remove("in");
            tokens.Remove("of");
            tokens.Remove("to");
            tokens.Remove("and");
            tokens.Remove("for");
            tokens.Remove("with");
            tokens.Remove("what");
            tokens.Remove("how");
            tokens.Remove("does");
            tokens.Remove("this");
            tokens.Remove("that");

            return new List<string>(tokens);
        }

        private double ComputeScore(IReadOnlyList<string> queryTerms, SymbolRecord symbol)
        {
            var score = 0.0;

            foreach (var term in queryTerms)
            {
                // Exact name match (highest weight)
                if (string.Equals(symbol.Name, term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10.0;
                    continue;
                }

                // Name contains term
                if (symbol.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 5.0;
                    continue;
                }

                // Keyword match with IDF weighting
                foreach (var keyword in symbol.Keywords)
                {
                    if (string.Equals(keyword, term, StringComparison.OrdinalIgnoreCase))
                    {
                        var idf = _idfScores.ContainsKey(keyword) ? _idfScores[keyword] : 1.0;
                        score += idf;
                        break; // Only count each term once per symbol
                    }
                }
            }

            return score;
        }

        /// <summary>
        /// Compute Inverse Document Frequency for all keywords across all symbols.
        /// IDF = log(N / df) where N = total symbols, df = symbols containing the keyword.
        /// </summary>
        private static Dictionary<string, double> ComputeIdf(IReadOnlyList<SymbolRecord> symbols)
        {
            var idf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (symbols.Count == 0) return idf;

            var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalDocs = (double)symbols.Count;

            foreach (var symbol in symbols)
            {
                // Count each keyword once per symbol
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kw in symbol.Keywords)
                {
                    if (seen.Add(kw))
                    {
                        if (docFreq.ContainsKey(kw))
                            docFreq[kw]++;
                        else
                            docFreq[kw] = 1;
                    }
                }
            }

            foreach (var kvp in docFreq)
            {
                idf[kvp.Key] = Math.Log(totalDocs / kvp.Value);
            }

            return idf;
        }

        private static string FormatSymbol(SymbolRecord symbol)
        {
            var kindLabel = $"[{symbol.Kind.ToString().ToLowerInvariant()}]";
            var qualifiedName = string.IsNullOrEmpty(symbol.Namespace)
                ? symbol.Name
                : $"{symbol.Namespace}::{symbol.Name}";

            // v2.8: Include line range and signature when available
            var location = symbol.FilePath;
            if (symbol.StartLine > 0)
            {
                location += symbol.EndLine > symbol.StartLine
                    ? $":{symbol.StartLine}-{symbol.EndLine}"
                    : $":{symbol.StartLine}";
            }

            var line1 = $"- {kindLabel} {qualifiedName} ({location})";
            if (!string.IsNullOrEmpty(symbol.Signature))
                line1 += $" `{symbol.Signature}`";

            return $"{line1}\n  {symbol.Summary}";
        }
    }
}
