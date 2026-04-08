namespace AICA.Core.Storage
{
    /// <summary>
    /// Represents a single parsed memory entry from .aica/memory/*.md.
    /// Used by RelevanceScorer for structured retrieval.
    /// </summary>
    internal class MemoryEntry
    {
        /// <summary>Memory name (from frontmatter 'name' field or filename).</summary>
        public string Name { get; set; }

        /// <summary>One-line description (from frontmatter 'description' field).</summary>
        public string Description { get; set; }

        /// <summary>Memory type: user, feedback, project, or reference.</summary>
        public string Type { get; set; }

        /// <summary>Body content after frontmatter.</summary>
        public string Body { get; set; }

        /// <summary>Source file path.</summary>
        public string FilePath { get; set; }
    }
}
