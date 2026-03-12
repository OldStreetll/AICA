using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Categorizes tools for organization and filtering
    /// </summary>
    public enum ToolCategory
    {
        /// <summary>
        /// Tools for reading file contents
        /// </summary>
        FileRead,

        /// <summary>
        /// Tools for writing or editing files
        /// </summary>
        FileWrite,

        /// <summary>
        /// Tools for deleting files
        /// </summary>
        FileDelete,

        /// <summary>
        /// Tools for directory operations (list, create, etc.)
        /// </summary>
        DirectoryOps,

        /// <summary>
        /// Tools for searching files and content
        /// </summary>
        Search,

        /// <summary>
        /// Tools for executing commands
        /// </summary>
        Command,

        /// <summary>
        /// Tools for code analysis
        /// </summary>
        Analysis,

        /// <summary>
        /// Tools for user interaction (questions, confirmations)
        /// </summary>
        Interaction
    }

    /// <summary>
    /// Metadata about a tool describing its properties and requirements
    /// </summary>
    public class ToolMetadata
    {
        /// <summary>
        /// Tool name (must match IAgentTool.Name)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Tool description for documentation
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category this tool belongs to
        /// </summary>
        public ToolCategory Category { get; set; }

        /// <summary>
        /// Whether this tool requires user confirmation before execution
        /// </summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>
        /// Whether this tool requires explicit user approval
        /// </summary>
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// Timeout in seconds for tool execution (null = no timeout)
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// Tags for categorizing and filtering tools
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether this tool modifies the workspace
        /// </summary>
        public bool IsModifying { get; set; }

        /// <summary>
        /// Whether this tool requires network access
        /// </summary>
        public bool RequiresNetwork { get; set; }

        /// <summary>
        /// Whether this tool is experimental or unstable
        /// </summary>
        public bool IsExperimental { get; set; }
    }

    /// <summary>
    /// Registry for tool metadata
    /// </summary>
    public static class ToolMetadataRegistry
    {
        private static readonly Dictionary<string, ToolMetadata> Metadata = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register metadata for a tool
        /// </summary>
        public static void Register(ToolMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (string.IsNullOrWhiteSpace(metadata.Name))
                throw new ArgumentException("Metadata name cannot be empty", nameof(metadata));

            Metadata[metadata.Name] = metadata;
        }

        /// <summary>
        /// Get metadata for a tool by name
        /// </summary>
        public static ToolMetadata Get(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be empty", nameof(toolName));

            return Metadata.TryGetValue(toolName, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// Get all tools in a specific category
        /// </summary>
        public static IEnumerable<ToolMetadata> GetByCategory(ToolCategory category)
        {
            return Metadata.Values.Where(m => m.Category == category);
        }

        /// <summary>
        /// Get all tools with a specific tag
        /// </summary>
        public static IEnumerable<ToolMetadata> GetByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Enumerable.Empty<ToolMetadata>();

            return Metadata.Values.Where(m => m.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false);
        }

        /// <summary>
        /// Get all tools that require confirmation
        /// </summary>
        public static IEnumerable<ToolMetadata> GetRequiringConfirmation()
        {
            return Metadata.Values.Where(m => m.RequiresConfirmation);
        }

        /// <summary>
        /// Get all tools that require approval
        /// </summary>
        public static IEnumerable<ToolMetadata> GetRequiringApproval()
        {
            return Metadata.Values.Where(m => m.RequiresApproval);
        }

        /// <summary>
        /// Get all modifying tools
        /// </summary>
        public static IEnumerable<ToolMetadata> GetModifyingTools()
        {
            return Metadata.Values.Where(m => m.IsModifying);
        }

        /// <summary>
        /// Get all registered tool metadata
        /// </summary>
        public static IEnumerable<ToolMetadata> GetAll()
        {
            return Metadata.Values;
        }

        /// <summary>
        /// Clear all registered metadata (useful for testing)
        /// </summary>
        public static void Clear()
        {
            Metadata.Clear();
        }

        /// <summary>
        /// Check if metadata is registered for a tool
        /// </summary>
        public static bool Contains(string toolName)
        {
            return Metadata.ContainsKey(toolName);
        }
    }
}
