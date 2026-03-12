using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Registry for managing and discovering tools
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToolMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly Microsoft.Extensions.Logging.ILogger<ToolRegistry> _logger;

        public ToolRegistry(Microsoft.Extensions.Logging.ILogger<ToolRegistry> logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Register a tool
        /// </summary>
        public void Register(IAgentTool tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));

            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new ArgumentException("Tool name cannot be empty", nameof(tool));

            _tools[tool.Name] = tool;

            // Also register metadata
            var metadata = tool.GetMetadata();
            if (metadata != null)
            {
                _metadata[tool.Name] = metadata;
                ToolMetadataRegistry.Register(metadata);
            }

            _logger?.LogDebug("Registered tool: {ToolName}", tool.Name);
        }

        /// <summary>
        /// Get a tool by name
        /// </summary>
        public IAgentTool GetTool(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            _tools.TryGetValue(name, out var tool);
            return tool;
        }

        /// <summary>
        /// Get metadata for a tool
        /// </summary>
        public ToolMetadata GetMetadata(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            _metadata.TryGetValue(toolName, out var metadata);
            return metadata;
        }

        /// <summary>
        /// Get all tools in a specific category
        /// </summary>
        public IEnumerable<IAgentTool> GetByCategory(ToolCategory category)
        {
            return _tools.Values.Where(t =>
            {
                var metadata = GetMetadata(t.Name);
                return metadata?.Category == category;
            });
        }

        /// <summary>
        /// Get all tools with a specific tag
        /// </summary>
        public IEnumerable<IAgentTool> GetByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Enumerable.Empty<IAgentTool>();

            return _tools.Values.Where(t =>
            {
                var metadata = GetMetadata(t.Name);
                return metadata?.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false;
            });
        }

        /// <summary>
        /// Get all tools that require confirmation
        /// </summary>
        public IEnumerable<IAgentTool> GetRequiringConfirmation()
        {
            return _tools.Values.Where(t =>
            {
                var metadata = GetMetadata(t.Name);
                return metadata?.RequiresConfirmation ?? false;
            });
        }

        /// <summary>
        /// Get all tools that require approval
        /// </summary>
        public IEnumerable<IAgentTool> GetRequiringApproval()
        {
            return _tools.Values.Where(t =>
            {
                var metadata = GetMetadata(t.Name);
                return metadata?.RequiresApproval ?? false;
            });
        }

        /// <summary>
        /// Get all modifying tools
        /// </summary>
        public IEnumerable<IAgentTool> GetModifyingTools()
        {
            return _tools.Values.Where(t =>
            {
                var metadata = GetMetadata(t.Name);
                return metadata?.IsModifying ?? false;
            });
        }

        /// <summary>
        /// Get all registered tools
        /// </summary>
        public IEnumerable<IAgentTool> GetAll()
        {
            return _tools.Values;
        }

        /// <summary>
        /// Get all registered tool names
        /// </summary>
        public IEnumerable<string> GetToolNames()
        {
            return _tools.Keys;
        }

        /// <summary>
        /// Check if a tool is registered
        /// </summary>
        public bool Contains(string toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName) && _tools.ContainsKey(toolName);
        }

        /// <summary>
        /// Get the number of registered tools
        /// </summary>
        public int Count => _tools.Count;

        /// <summary>
        /// Clear all registered tools
        /// </summary>
        public void Clear()
        {
            _tools.Clear();
            _metadata.Clear();
            _logger?.LogDebug("Tool registry cleared");
        }
    }
}
