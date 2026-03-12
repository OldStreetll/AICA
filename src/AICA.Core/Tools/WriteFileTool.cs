using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for creating new files
    /// </summary>
    public class WriteFileTool : IAgentTool
    {
        public string Name => "write_to_file";
        public string Description => "Create a NEW file with content. IMPORTANT: Only use this for files that don't exist yet. For existing files, use the 'edit' tool instead. This will fail if the file already exists.";

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.FileWrite,
                RequiresConfirmation = true,
                RequiresApproval = false,
                TimeoutSeconds = 15,
                IsModifying = true,
                Tags = new[] { "file", "write", "create" }
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
                            Description = "The path for the new file (relative to workspace root)"
                        },
                        ["content"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The content to write to the file"
                        }
                    },
                    Required = new[] { "path", "content" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                // Validate required parameters
                var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "path");
                var content = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "content");

                if (!context.IsPathAccessible(path))
                {
                    return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));
                }

                // Check if file already exists
                if (await context.FileExistsAsync(path, ct))
                {
                    return ToolResult.Fail($"File already exists: {path}. Use 'edit' tool to modify existing files.");
                }

                // Request confirmation
                var confirmed = await context.RequestConfirmationAsync(
                    "Create File",
                    $"Create new file: {path}\n\nContent preview:\n{content.Substring(0, System.Math.Min(500, content.Length))}...",
                    ct);

                if (!confirmed)
                {
                    return ToolResult.Fail("Operation cancelled by user");
                }

                await context.WriteFileAsync(path, content, ct);

                return ToolResult.Ok($"File created: {path}");
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "write_to_file");
                return ToolErrorHandler.HandleError(error);
            }
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
