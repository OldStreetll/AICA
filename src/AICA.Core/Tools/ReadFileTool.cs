using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for reading file contents
    /// </summary>
    public class ReadFileTool : IAgentTool
    {
        public string Name => "read_file";
        public string Description => "Read the contents of a file. Use this to view file content before making changes. Supports reading specific line ranges with offset and limit parameters.";

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.FileRead,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 10,
                IsModifying = false,
                Tags = new[] { "file", "read", "view" }
            };
        }

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
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the file to read (relative to workspace root or source roots)"
                        },
                        ["offset"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Optional. The 1-indexed line number to start reading from."
                        },
                        ["limit"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Optional. The number of lines to read."
                        }
                    },
                    Required = new[] { "path" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                // Validate required parameters
                var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "path");
                var offset = ToolParameterValidator.GetOptionalParameter<int?>(call.Arguments, "offset");
                var limit = ToolParameterValidator.GetOptionalParameter<int?>(call.Arguments, "limit");

                // Try to resolve across working directory and source roots
                var resolvedPath = context.ResolveFilePath(path);

                if (resolvedPath != null)
                {
                    // Resolved path found — check accessibility
                    if (!context.IsPathAccessible(resolvedPath))
                        return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));
                }
                else
                {
                    // Not resolved — check original path accessibility
                    if (!context.IsPathAccessible(path))
                        return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));

                    if (!await context.FileExistsAsync(path, ct))
                        return ToolErrorHandler.HandleError(ToolErrorHandler.NotFound(path));
                }

                // ReadFileAsync already uses ResolveFilePath internally
                var content = await context.ReadFileAsync(path, ct);

                if (offset.HasValue || limit.HasValue)
                {
                    var lines = content.Split('\n');
                    var startIndex = (offset ?? 1) - 1;
                    var count = limit ?? (lines.Length - startIndex);

                    if (startIndex < 0) startIndex = 0;
                    if (startIndex >= lines.Length) return ToolResult.Ok("(empty - offset beyond file length)");

                    count = System.Math.Min(count, lines.Length - startIndex);

                    // Add line numbers to help Agent reference code accurately
                    var numberedLines = new System.Text.StringBuilder();
                    numberedLines.AppendLine($"[Showing lines {startIndex + 1}-{startIndex + count} of {lines.Length}]");
                    numberedLines.AppendLine();
                    for (int i = 0; i < count; i++)
                    {
                        var lineNumber = startIndex + i + 1;
                        numberedLines.AppendLine($"{lineNumber,6}: {lines[startIndex + i]}");
                    }
                    content = numberedLines.ToString();
                }

                return ToolResult.Ok(content);
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "read_file");
                return ToolErrorHandler.HandleError(error);
            }
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
