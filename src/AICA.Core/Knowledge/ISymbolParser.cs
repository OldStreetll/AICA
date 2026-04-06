using System.Collections.Generic;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// Interface for extracting symbol definitions from source code.
    /// Implementations: RegexSymbolParser (fallback), CodeModelSymbolParser (VS2022 DTE).
    /// </summary>
    public interface ISymbolParser
    {
        /// <summary>
        /// Parse a file's content and extract symbols.
        /// </summary>
        /// <param name="filePath">Relative file path (used for extension detection and symbol metadata)</param>
        /// <param name="content">Source file content</param>
        /// <returns>List of extracted symbols</returns>
        IReadOnlyList<SymbolRecord> Parse(string filePath, string content);
    }
}
