using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for making precise edits to existing files
    /// </summary>
    public class EditFileTool : IAgentTool
    {
        public string Name => "edit";
        public string Description => "Make a precise edit to an existing file by replacing a unique string with new content.";

        public ToolDefinition GetDefinition()
        {
            return new ToolDefinition
            {
                Name = Name,
                Description = Description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["file_path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the file to edit"
                        },
                        ["old_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The exact text to replace. MUST be unique in the file."
                        },
                        ["new_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The new text to replace old_string with"
                        },
                        ["replace_all"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, replace all occurrences. Default is false.",
                            Default = false
                        }
                    },
                    Required = new[] { "file_path", "old_string", "new_string" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            // Validate required parameters
            if (!call.Arguments.TryGetValue("file_path", out var pathObj) || pathObj == null)
                return ToolResult.Fail("Missing required parameter: file_path");

            if (!call.Arguments.TryGetValue("old_string", out var oldObj) || oldObj == null)
                return ToolResult.Fail("Missing required parameter: old_string");

            if (!call.Arguments.TryGetValue("new_string", out var newObj) || newObj == null)
                return ToolResult.Fail("Missing required parameter: new_string");

            var path = pathObj.ToString();
            var oldString = oldObj.ToString();
            var newString = newObj.ToString();
            var replaceAll = false;

            if (call.Arguments.TryGetValue("replace_all", out var replaceAllObj) && replaceAllObj != null)
            {
                bool.TryParse(replaceAllObj.ToString(), out replaceAll);
            }

            // Validate path access
            if (!context.IsPathAccessible(path))
                return ToolResult.Fail($"Access denied: {path}");

            // Check file exists
            if (!await context.FileExistsAsync(path, ct))
                return ToolResult.Fail($"File not found: {path}");

            // Read current content
            var content = await context.ReadFileAsync(path, ct);

            // Check old_string exists
            if (!content.Contains(oldString))
                return ToolResult.Fail($"old_string not found in file. Make sure the string matches exactly including whitespace.");

            // Check uniqueness (unless replace_all)
            if (!replaceAll)
            {
                var firstIndex = content.IndexOf(oldString);
                var lastIndex = content.LastIndexOf(oldString);
                if (firstIndex != lastIndex)
                {
                    return ToolResult.Fail("old_string is not unique in the file. Provide more context to make it unique, or use replace_all=true.");
                }
            }

            // Check if old_string equals new_string
            if (oldString == newString)
                return ToolResult.Fail("old_string and new_string are identical. This is a no-op.");

            // Apply the edit
            var newContent = replaceAll 
                ? content.Replace(oldString, newString)
                : ReplaceFirst(content, oldString, newString);

            // Show diff preview and request confirmation
            var confirmed = await context.ShowDiffPreviewAsync(path, content, newContent, ct);

            if (!confirmed)
                return ToolResult.Fail("Operation cancelled by user");

            // Write the edited content
            await context.WriteFileAsync(path, newContent, ct);

            var occurrences = replaceAll ? CountOccurrences(content, oldString) : 1;
            return ToolResult.Ok($"File edited: {path} ({occurrences} replacement(s) made)");
        }

        private string ReplaceFirst(string text, string oldValue, string newValue)
        {
            var index = text.IndexOf(oldValue);
            if (index < 0) return text;
            return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "...";
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
