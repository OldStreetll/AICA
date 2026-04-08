using System.Collections.Generic;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Common Chinese stopwords for single-character tokenization filtering.
    /// Used by RelevanceScorer to improve memory relevance matching.
    /// </summary>
    internal static class ChineseStopwords
    {
        private static readonly HashSet<char> Stopwords = new HashSet<char>
        {
            '的', '了', '在', '是', '我', '他', '她', '你', '们',
            '这', '那', '和', '与', '或', '但', '而', '也', '都',
            '就', '会', '要', '有', '不', '没', '很', '把', '被',
            '让', '从', '到', '为', '以', '及', '于', '上', '下',
            '中', '大', '小', '多', '少', '能', '可', '已', '又',
            '还', '才', '只', '更', '最', '每'
        };

        /// <summary>
        /// Check if a character is a common Chinese stopword.
        /// </summary>
        public static bool IsStopword(char c)
        {
            return Stopwords.Contains(c);
        }
    }
}
