using System;
using System.Collections.Generic;

namespace AICA.Core.Rules.Models
{
    /// <summary>
    /// Represents a single rule loaded from a file or configuration.
    /// </summary>
    public class Rule
    {
        /// <summary>
        /// Unique identifier for the rule (typically derived from filename).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Human-readable name of the rule.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The actual rule content (Markdown format).
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Metadata extracted from YAML frontmatter.
        /// </summary>
        public RuleMetadata Metadata { get; set; }

        /// <summary>
        /// Source of the rule (workspace, global, remote, or builtin).
        /// </summary>
        public RuleSource Source { get; set; }

        /// <summary>
        /// Priority level for rule ordering. Higher priority rules override lower priority ones.
        /// Builtin: 0, Global: 10, Workspace: 20, Custom: 100
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Whether this rule is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Path to the rule file (if loaded from filesystem).
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// When the rule was loaded.
        /// </summary>
        public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

        public Rule()
        {
            Metadata = new RuleMetadata();
        }
    }

    /// <summary>
    /// Metadata extracted from YAML frontmatter in rule files.
    /// </summary>
    public class RuleMetadata
    {
        /// <summary>
        /// Glob patterns for path-based rule activation.
        /// If empty, the rule is always active (universal rule).
        /// </summary>
        public List<string> Paths { get; set; } = new List<string>();

        /// <summary>
        /// Custom metadata fields from YAML frontmatter.
        /// </summary>
        public Dictionary<string, object> Custom { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Indicates the source/origin of a rule.
    /// </summary>
    public enum RuleSource
    {
        /// <summary>
        /// Rule from workspace .aica-rules directory (highest priority).
        /// </summary>
        Workspace = 20,

        /// <summary>
        /// Rule from global ~/.aica/rules directory.
        /// </summary>
        Global = 10,

        /// <summary>
        /// Rule from remote source (future).
        /// </summary>
        Remote = 5,

        /// <summary>
        /// Built-in rule (lowest priority).
        /// </summary>
        Builtin = 0
    }
}
