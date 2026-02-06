using System;
using System.Collections.Generic;
using System.Text;
using AICA.Core.Agent;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Builder for system prompts
    /// </summary>
    public class SystemPromptBuilder
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly List<ToolDefinition> _tools = new List<ToolDefinition>();

        public SystemPromptBuilder()
        {
            AddBasePrompt();
        }

        private void AddBasePrompt()
        {
            _builder.AppendLine("You are AICA (AI Coding Assistant), an intelligent programming assistant running inside Visual Studio 2022.");
            _builder.AppendLine();
            _builder.AppendLine("## IMPORTANT: Tool Usage");
            _builder.AppendLine("You MUST use the provided tools to complete tasks. Do NOT just describe what you would do - actually call the tools!");
            _builder.AppendLine("- To read a file: call the `read_file` tool");
            _builder.AppendLine("- To list directory contents: call the `list_dir` tool");
            _builder.AppendLine("- To write a file: call the `write_file` tool");
            _builder.AppendLine("- To edit a file: call the `edit_file` tool");
            _builder.AppendLine();
            _builder.AppendLine("When the user asks you to read, list, or modify files, you should IMMEDIATELY call the appropriate tool.");
            _builder.AppendLine("Do not say 'I will read the file' - instead, actually call read_file right away.");
            _builder.AppendLine();
        }

        public SystemPromptBuilder AddTools(IEnumerable<ToolDefinition> tools)
        {
            _tools.AddRange(tools);
            return this;
        }

        public SystemPromptBuilder AddWorkspaceContext(string workingDirectory, IEnumerable<string> recentFiles = null)
        {
            _builder.AppendLine("## Workspace");
            _builder.AppendLine($"Working Directory: {workingDirectory}");
            
            if (recentFiles != null)
            {
                _builder.AppendLine();
                _builder.AppendLine("Recently accessed files:");
                foreach (var file in recentFiles)
                {
                    _builder.AppendLine($"- {file}");
                }
            }
            _builder.AppendLine();
            return this;
        }

        public SystemPromptBuilder AddCustomInstructions(string instructions)
        {
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                _builder.AppendLine("## Custom Instructions");
                _builder.AppendLine(instructions);
                _builder.AppendLine();
            }
            return this;
        }

        public SystemPromptBuilder AddGuidelines()
        {
            _builder.AppendLine("## Guidelines");
            _builder.AppendLine("1. Always read files before modifying them to understand the context");
            _builder.AppendLine("2. Use the `edit` tool for precise modifications instead of rewriting entire files");
            _builder.AppendLine("3. The `old_string` parameter must be unique in the file");
            _builder.AppendLine("4. Follow the project's existing code style and conventions");
            _builder.AppendLine("5. Dangerous operations require user confirmation");
            _builder.AppendLine("6. Keep responses concise and focused on the task");
            _builder.AppendLine();
            return this;
        }

        public string Build()
        {
            if (_tools.Count > 0)
            {
                _builder.AppendLine("## Available Tools");
                foreach (var tool in _tools)
                {
                    _builder.AppendLine($"- **{tool.Name}**: {tool.Description}");
                }
                _builder.AppendLine();
            }

            return _builder.ToString();
        }

        /// <summary>
        /// Get the default system prompt
        /// </summary>
        public static string GetDefaultPrompt(string workingDirectory, IEnumerable<ToolDefinition> tools)
        {
            return new SystemPromptBuilder()
                .AddTools(tools)
                .AddWorkspaceContext(workingDirectory)
                .AddGuidelines()
                .Build();
        }
    }
}
