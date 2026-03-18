using System;
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
        /// Clear the current index (e.g., when solution is closed).
        /// </summary>
        public void Clear()
        {
            _index = null;
        }
    }
}
