using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AICA.Core.Agent;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Builder for system prompts that defines the Agent's role, available tools,
    /// behavioral rules, workspace context, and custom instructions.
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
            _builder.AppendLine("You help developers with code generation, editing, refactoring, testing, debugging, and code understanding.");
            _builder.AppendLine("You operate primarily in an offline/private environment. Do not assume internet access.");
            _builder.AppendLine();
        }

        public SystemPromptBuilder AddTools(IEnumerable<ToolDefinition> tools)
        {
            _tools.AddRange(tools);
            return this;
        }

        /// <summary>
        /// Generate tool usage documentation from registered tool definitions
        /// </summary>
        public SystemPromptBuilder AddToolDescriptions()
        {
            if (_tools.Count == 0) return this;

            _builder.AppendLine("## Available Tools");
            _builder.AppendLine();
            _builder.AppendLine("You have access to the following tools via the OpenAI function calling API.");
            _builder.AppendLine("When you need to perform an action, use the tool_calls mechanism — do NOT write out tool calls as text or XML.");
            _builder.AppendLine("IMPORTANT: Call tools IMMEDIATELY when needed. Do not describe what you would do — just call the tool directly.");
            _builder.AppendLine();

            foreach (var tool in _tools)
            {
                _builder.AppendLine($"### {tool.Name}");
                _builder.AppendLine(tool.Description);
                if (tool.Parameters?.Properties != null && tool.Parameters.Properties.Count > 0)
                {
                    _builder.AppendLine("Parameters:");
                    foreach (var param in tool.Parameters.Properties)
                    {
                        var required = tool.Parameters.Required != null &&
                                       tool.Parameters.Required.Contains(param.Key) ? " (required)" : " (optional)";
                        _builder.AppendLine($"  - `{param.Key}` ({param.Value.Type}){required}: {param.Value.Description}");
                    }
                }
                _builder.AppendLine();
            }

            return this;
        }

        /// <summary>
        /// Add tool calling rules, behavioral rules, and safety rules
        /// </summary>
        public SystemPromptBuilder AddRules()
        {
            _builder.AppendLine("## Rules");
            _builder.AppendLine();

            // Tool calling rules
            _builder.AppendLine("### Tool Calling");
            _builder.AppendLine("- ALWAYS use the function calling API to invoke tools. NEVER output tool calls as text, XML, or JSON in your response.");
            _builder.AppendLine("- **CRITICAL: Do NOT generate answers or descriptions BEFORE calling tools. Call the tool FIRST, then describe the results AFTER you receive the tool output.** For example, if the user asks to list a directory, call `list_dir` immediately — do NOT write out the directory contents from imagination.");
            _builder.AppendLine("- Call tools directly when you know what to do. Do not ask for permission for read-only operations.");
            _builder.AppendLine("- When a coding task is complete, call the `attempt_completion` tool with a result summary.");
            _builder.AppendLine("- Keep your text output minimal before tool calls. A brief one-line plan is acceptable, but never write the expected results before receiving actual tool output.");
            _builder.AppendLine("- For casual conversation or greetings (e.g. \"你好\", \"hello\"), respond naturally in text WITHOUT calling any tools. Only use tools when the user has a specific task or question about code/files.");
            _builder.AppendLine();

            // Tool usage tips
            _builder.AppendLine("### Tool Usage Tips");
            _builder.AppendLine("- `list_dir`: Use `recursive=true` when the user asks for 'full structure', 'complete tree', '完整结构', '目录树' etc. Set `max_depth` to control depth (default 3, max 10).");
            _builder.AppendLine("- `list_code_definition_names`: Use this to understand code structure (classes, methods, properties) without reading entire files. Ideal for project overview requests.");
            _builder.AppendLine("- `grep_search`: Prefer this over `read_file` when looking for specific patterns across multiple files.");
            _builder.AppendLine("- `edit`: Always `read_file` first. The `old_string` must exactly match file content and be unique.");
            _builder.AppendLine();

            // Code editing rules
            _builder.AppendLine("### Code Editing");
            _builder.AppendLine("- ALWAYS read a file with `read_file` before editing it.");
            _builder.AppendLine("- Use `edit` for precise, targeted changes. The `old_string` must exactly match the file content (including whitespace and indentation) and must be unique in the file.");
            _builder.AppendLine("- Use `write_to_file` ONLY for creating new files. Never use it to overwrite existing files.");
            _builder.AppendLine("- Preserve the existing code style, naming conventions, and indentation.");
            _builder.AppendLine("- Do not add or remove comments unless explicitly asked.");
            _builder.AppendLine("- Add necessary imports/using statements when adding new code.");
            _builder.AppendLine();

            // Command rules
            _builder.AppendLine("### Command Execution");
            _builder.AppendLine("- The `run_command` tool executes commands in a shell. Some commands may require user confirmation.");
            _builder.AppendLine("- Prefer non-destructive commands. Avoid `rm -rf`, `del /s`, `format`, etc.");
            _builder.AppendLine("- Always specify the appropriate working directory via the tool parameter.");
            _builder.AppendLine();

            // Anti-hallucination rules
            _builder.AppendLine("### Anti-Hallucination (CRITICAL)");
            _builder.AppendLine("- **NEVER fabricate or imagine file contents, code structures, or directory listings.** Every piece of information in your response MUST come from actual tool output.");
            _builder.AppendLine("- If `read_file` returns 'File not found', clearly tell the user the file does not exist. Do NOT proceed to describe what the file 'would contain' or 'typically contains'.");
            _builder.AppendLine("- If a file is not found, suggest where it might be located (e.g., check `.vcxproj.filters` for source file paths) or ask the user for the correct path.");
            _builder.AppendLine("- When summarizing tool results, only include information that was actually returned by the tool. Do not add extra details from your training knowledge.");
            _builder.AppendLine();

            // Efficiency rules
            _builder.AppendLine("### Efficiency");
            _builder.AppendLine("- **Minimize tool calls.** Most tasks can be completed in 2-5 tool calls. If you find yourself making more than 8 calls, stop and reconsider your approach.");
            _builder.AppendLine("- **Reuse results.** Never call the same tool with similar arguments twice. If you already have a directory listing, use it instead of listing again.");
            _builder.AppendLine("- **Stay focused.** Only explore directories and files directly relevant to the user's question. Do not wander into unrelated directories.");
            _builder.AppendLine("- **One search is usually enough.** For `grep_search`, one well-crafted query is better than multiple vague ones. Review the results before searching again.");
            _builder.AppendLine("- After gathering sufficient information, call `attempt_completion` promptly. Do not keep searching for more data if you already have a good answer.");
            _builder.AppendLine();

            // Search strategy
            _builder.AppendLine("### Search Strategy");
            _builder.AppendLine("- Start searching in the most specific directory first (e.g., if asked about `src/App`, search there, not the entire project).");
            _builder.AppendLine("- If a file is not found, check the parent directory listing to see what IS available before searching elsewhere.");
            _builder.AppendLine("- Prefer `grep_search` and `find_by_name` over `run_command` for searching files. The built-in tools are faster and safer.");
            _builder.AppendLine();

            // Safety rules
            _builder.AppendLine("### Safety");
            _builder.AppendLine("- Never modify files outside the working directory without explicit permission.");
            _builder.AppendLine("- Dangerous or destructive operations require user confirmation.");
            _builder.AppendLine("- If a tool returns an error, analyze the error and adjust your approach instead of retrying the exact same call.");
            _builder.AppendLine();

            // Response guidelines
            _builder.AppendLine("### Response Style");
            _builder.AppendLine("- Be concise and direct. Focus on the task at hand.");
            _builder.AppendLine("- When explaining code or providing analysis, use Markdown formatting.");
            _builder.AppendLine("- If the user's request is ambiguous, make reasonable assumptions and proceed. Include your assumptions in the response text along with your tool calls.");
            _builder.AppendLine("- Support both Chinese and English. Respond in the same language as the user's request.");
            _builder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddWorkspaceContext(
            string workingDirectory,
            IEnumerable<string> sourceRoots = null,
            IEnumerable<string> recentFiles = null)
        {
            _builder.AppendLine("## Workspace");
            _builder.AppendLine($"Working Directory: {workingDirectory}");

            // Source roots from solution/project file analysis
            if (sourceRoots != null)
            {
                var rootList = sourceRoots.ToList();
                if (rootList.Count > 0)
                {
                    _builder.AppendLine();
                    _builder.AppendLine("### Source Roots");
                    _builder.AppendLine("The following directories contain source files referenced by the solution's project files (.vcxproj/.csproj).");
                    _builder.AppendLine("These are outside the working directory but are accessible for reading and searching.");
                    foreach (var root in rootList)
                    {
                        _builder.AppendLine($"- {root}");
                    }
                    _builder.AppendLine();
                    _builder.AppendLine("### Path Resolution");
                    _builder.AppendLine("- File paths are automatically resolved across the working directory AND source roots.");
                    _builder.AppendLine("- You can use relative paths like 'src/App/Application.h' — the system will search source roots automatically.");
                    _builder.AppendLine("- If multiple files match the same name, use the full relative path to disambiguate.");
                    _builder.AppendLine("- Write operations on source files outside the working directory require explicit user confirmation.");
                }
            }

            if (recentFiles != null)
            {
                var fileList = recentFiles.ToList();
                if (fileList.Count > 0)
                {
                    _builder.AppendLine();
                    _builder.AppendLine("Recently accessed files:");
                    foreach (var file in fileList.Take(20))
                    {
                        _builder.AppendLine($"- {file}");
                    }
                    if (fileList.Count > 20)
                    {
                        _builder.AppendLine($"  ... and {fileList.Count - 20} more");
                    }
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
            return _builder.ToString();
        }

        /// <summary>
        /// Get the default system prompt with full tool descriptions and rules
        /// </summary>
        public static string GetDefaultPrompt(
            string workingDirectory,
            IEnumerable<ToolDefinition> tools,
            string customInstructions = null,
            IEnumerable<string> sourceRoots = null)
        {
            return new SystemPromptBuilder()
                .AddTools(tools)
                .AddToolDescriptions()
                .AddRules()
                .AddWorkspaceContext(workingDirectory, sourceRoots)
                .AddCustomInstructions(customInstructions)
                .Build();
        }
    }
}
