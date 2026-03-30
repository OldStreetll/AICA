using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for creating new files or completely overwriting existing files.
    /// Separated from EditFileTool to provide clear semantics for file creation.
    /// </summary>
    public class WriteFileTool : IAgentTool
    {
        public string Name => "write_file";
        public string Description =>
            "Create a new file with the provided content. Parent directories are created automatically. " +
            "Do NOT use this to modify parts of an existing file — use 'edit' instead. " +
            "If the file already exists, set overwrite=true (requires user confirmation).";

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
                        ["file_path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the file to create or overwrite (relative to workspace root)"
                        },
                        ["content"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The complete content to write to the file"
                        },
                        ["overwrite"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, overwrite an existing file. If false (default), refuse to overwrite and suggest using 'edit' instead.",
                            Default = false
                        }
                    },
                    Required = new[] { "file_path", "content" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "file_path");
                var content = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "content");
                var overwrite = ToolParameterValidator.GetOptionalParameter<bool>(call.Arguments, "overwrite", false);

                // Validate path access
                if (!context.IsPathAccessible(path))
                    return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));

                var fileExists = await context.FileExistsAsync(path, ct);

                if (fileExists && !overwrite)
                {
                    return ToolResult.Fail(
                        $"File already exists: {path}\n" +
                        "To modify specific parts of an existing file, use the 'edit' tool.\n" +
                        "To completely overwrite, set overwrite=true.");
                }

                // Detect project line ending convention
                var normalizedContent = NormalizeToProjectLineEnding(content, context);

                if (fileExists)
                {
                    // Overwrite: show diff for user confirmation
                    var existingContent = await context.ReadFileAsync(path, ct);
                    var result = await context.ShowDiffAndApplyAsync(path, existingContent, normalizedContent, ct);

                    if (!result.Applied)
                        return ToolResult.Ok("File overwrite cancelled by user.");

                    // v2.1 T6: Record edit for conflict detection
                    FileTimeTracker.Instance.RecordEdit(path);

                    var lineCount = normalizedContent.Split('\n').Length;
                    var byteCount = System.Text.Encoding.UTF8.GetByteCount(normalizedContent);
                    return ToolResult.Ok($"File overwritten: {path} ({lineCount} lines, {FormatSize(byteCount)})");
                }
                else
                {
                    // New file: ensure parent directory exists
                    var resolvedPath = ResolveFullPath(path, context);
                    var parentDir = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    // Show diff (empty → new content) for user confirmation
                    var result = await context.ShowDiffAndApplyAsync(path, string.Empty, normalizedContent, ct);

                    if (!result.Applied)
                        return ToolResult.Ok("File creation cancelled by user.");

                    // v2.1 T6: Record new file for conflict detection
                    FileTimeTracker.Instance.RecordEdit(resolvedPath);

                    var lineCount = normalizedContent.Split('\n').Length;
                    var byteCount = System.Text.Encoding.UTF8.GetByteCount(normalizedContent);
                    return ToolResult.Ok($"File created: {path} ({lineCount} lines, {FormatSize(byteCount)})");
                }
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "write_file");
                return ToolErrorHandler.HandleError(error);
            }
        }

        /// <summary>
        /// Detect the project's dominant line ending style and normalize content to match.
        /// </summary>
        private string NormalizeToProjectLineEnding(string content, IAgentContext context)
        {
            // On Windows projects, default to CRLF; otherwise keep LF
            // Check working directory for existing files to detect convention
            try
            {
                var workDir = context.WorkingDirectory;
                if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
                {
                    // Sample up to 5 source files to detect dominant line ending
                    var sampleFiles = Directory.EnumerateFiles(workDir, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            return ext == ".cs" || ext == ".cpp" || ext == ".h" || ext == ".c" || ext == ".hpp";
                        })
                        .Take(5)
                        .ToList();

                    if (sampleFiles.Count > 0)
                    {
                        int crlfCount = 0;
                        int lfCount = 0;
                        foreach (var file in sampleFiles)
                        {
                            var raw = File.ReadAllText(file);
                            if (raw.Contains("\r\n")) crlfCount++;
                            else if (raw.Contains("\n")) lfCount++;
                        }

                        if (crlfCount > lfCount)
                        {
                            // Project uses CRLF — normalize content to CRLF
                            return content.Replace("\r\n", "\n").Replace("\n", "\r\n");
                        }
                    }
                }
            }
            catch
            {
                // Fall through to default behavior
            }

            // Default: keep content as-is (LF)
            return content;
        }

        private string ResolveFullPath(string path, IAgentContext context)
        {
            if (Path.IsPathRooted(path))
                return path;
            return Path.Combine(context.WorkingDirectory, path);
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB" };
            int idx = 0;
            double size = bytes;
            while (size >= 1024 && idx < suffixes.Length - 1)
            {
                size /= 1024;
                idx++;
            }
            return $"{size:0.##} {suffixes[idx]}";
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
