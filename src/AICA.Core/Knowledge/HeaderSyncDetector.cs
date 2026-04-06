using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// S3: Detects when C/C++ function signatures change in .cpp files
    /// and identifies corresponding .h/.hpp declarations that need synchronization.
    ///
    /// Compares symbol signatures before and after an edit. When a signature changes
    /// (not just the function body), looks up the header file containing the declaration
    /// by matching Namespace + Name in the project symbol index.
    /// </summary>
    public class HeaderSyncDetector
    {
        private static readonly HashSet<string> SourceExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cpp", ".cxx", ".cc", ".c" };

        private static readonly HashSet<string> HeaderExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".h", ".hpp", ".hxx" };

        /// <summary>
        /// Result of header sync detection for one symbol.
        /// </summary>
        public class SyncWarning
        {
            public string SymbolName { get; set; }
            public string HeaderFilePath { get; set; }
            public string OldSignature { get; set; }
            public string NewSignature { get; set; }
        }

        /// <summary>
        /// Detect header files that may need synchronization after a .cpp edit.
        /// Returns an empty list if the edited file is a header (headers are the source of truth),
        /// if no signature changes are detected, or if no matching header declarations are found.
        /// </summary>
        /// <param name="editedFilePath">Path of the edited file</param>
        /// <param name="symbolsBefore">Symbols parsed from the file before the edit</param>
        /// <param name="symbolsAfter">Symbols parsed from the file after the edit</param>
        /// <param name="projectIndex">Current project symbol index (for header lookup)</param>
        public List<SyncWarning> Detect(
            string editedFilePath,
            IReadOnlyList<SymbolRecord> symbolsBefore,
            IReadOnlyList<SymbolRecord> symbolsAfter,
            ProjectIndex projectIndex)
        {
            var warnings = new List<SyncWarning>();

            if (string.IsNullOrEmpty(editedFilePath) || projectIndex == null)
                return warnings;

            // Only check source files, not headers (headers are the source of truth)
            var ext = Path.GetExtension(editedFilePath);
            if (!SourceExtensions.Contains(ext))
                return warnings;

            if (symbolsBefore == null || symbolsAfter == null)
                return warnings;

            // Build lookup: (Namespace, Name) → Signature for functions before edit
            var beforeSigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in symbolsBefore)
            {
                if (sym.Kind == SymbolKind.Function && !string.IsNullOrEmpty(sym.Signature))
                {
                    var key = MakeKey(sym.Namespace, sym.Name);
                    beforeSigs[key] = sym.Signature;
                }
            }

            // Compare with after-edit symbols: detect signature changes
            var changedSymbols = new List<(string Key, string Namespace, string Name, string OldSig, string NewSig)>();
            foreach (var sym in symbolsAfter)
            {
                if (sym.Kind != SymbolKind.Function || string.IsNullOrEmpty(sym.Signature))
                    continue;

                var key = MakeKey(sym.Namespace, sym.Name);
                if (beforeSigs.TryGetValue(key, out var oldSig) && !string.Equals(oldSig, sym.Signature, StringComparison.Ordinal))
                {
                    changedSymbols.Add((key, sym.Namespace, sym.Name, oldSig, sym.Signature));
                }
            }

            if (changedSymbols.Count == 0)
                return warnings;

            // Look up header declarations in the project index
            foreach (var (key, ns, name, oldSig, newSig) in changedSymbols)
            {
                var headerDecl = projectIndex.Symbols
                    .Where(s => s.Kind == SymbolKind.Function
                        && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(s.Namespace, ns, StringComparison.OrdinalIgnoreCase)
                        && IsHeaderFile(s.FilePath))
                    .FirstOrDefault();

                if (headerDecl != null)
                {
                    warnings.Add(new SyncWarning
                    {
                        SymbolName = string.IsNullOrEmpty(ns) ? name : $"{ns}::{name}",
                        HeaderFilePath = headerDecl.FilePath,
                        OldSignature = oldSig,
                        NewSignature = newSig
                    });
                }
            }

            return warnings;
        }

        private static string MakeKey(string ns, string name)
        {
            return string.IsNullOrEmpty(ns) ? name : $"{ns}::{name}";
        }

        private static bool IsHeaderFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            return HeaderExtensions.Contains(ext);
        }
    }
}
