using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// Thread-safe singleton that stores the current project's symbol index.
    /// Accessed by SystemPromptBuilder and AgentExecutor to inject knowledge context.
    /// </summary>
    public sealed class ProjectKnowledgeStore
    {
        public static ProjectKnowledgeStore Instance { get; } = new ProjectKnowledgeStore();

        private volatile ProjectIndex _index;
        private readonly object _updateLock = new object();

        private ProjectKnowledgeStore() { }

        /// <summary>
        /// Replace the current index with a new one.
        /// Thread-safe via volatile write.
        /// </summary>
        public void SetIndex(ProjectIndex index)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        /// <summary>
        /// Get the current index, or null if not yet indexed.
        /// </summary>
        public ProjectIndex GetIndex()
        {
            return _index;
        }

        /// <summary>
        /// Whether an index is available.
        /// </summary>
        public bool HasIndex => _index != null;

        /// <summary>
        /// Create a KnowledgeContextProvider from the current index.
        /// Returns null if no index is available.
        /// </summary>
        public KnowledgeContextProvider CreateProvider()
        {
            var index = _index;
            return index != null ? new KnowledgeContextProvider(index) : null;
        }

        /// <summary>
        /// Replace all symbols for a single file in the current index.
        /// If no index exists, this is a no-op.
        /// Thread-safe: builds a new ProjectIndex and atomically swaps it in.
        /// </summary>
        public void UpdateFileSymbols(string relativeFilePath, IReadOnlyList<SymbolRecord> newSymbols)
        {
            lock (_updateLock)
            {
                var current = _index;
                if (current == null)
                    return;

                // Remove old symbols for this file, add new ones
                var updatedSymbols = current.Symbols
                    .Where(s => !string.Equals(s.FilePath, relativeFilePath, StringComparison.OrdinalIgnoreCase))
                    .Concat(newSymbols)
                    .ToList();

                // Recount distinct files
                var fileCount = updatedSymbols
                    .Select(s => s.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var updatedIndex = new ProjectIndex(
                    symbols: updatedSymbols,
                    indexedAt: DateTime.UtcNow,
                    fileCount: fileCount,
                    indexDuration: current.IndexDuration);

                _index = updatedIndex;
            }
        }

        /// <summary>
        /// Clear the current index (e.g., when solution is closed).
        /// </summary>
        public void Clear()
        {
            _index = null;
        }
    }
}
