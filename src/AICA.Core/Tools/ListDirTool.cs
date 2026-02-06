using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for listing directory contents
    /// </summary>
    public class ListDirTool : IAgentTool
    {
        public string Name => "list_dir";
        public string Description => "List files and directories in the specified path.";

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
                            Description = "The directory path to list (relative to workspace root)"
                        }
                    },
                    Required = new[] { "path" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            string relativePath = ".";
            if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                relativePath = pathObj.ToString();
            }

            // Normalize empty/root paths to "."
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "/" || relativePath == "\\")
            {
                relativePath = ".";
            }

            if (!context.IsPathAccessible(relativePath))
            {
                return Task.FromResult(ToolResult.Fail($"Access denied: {relativePath}"));
            }

            // Resolve full path
            string fullPath;
            if (relativePath == "." || relativePath == "./")
                fullPath = context.WorkingDirectory;
            else if (Path.IsPathRooted(relativePath))
                fullPath = relativePath;
            else
                fullPath = Path.Combine(context.WorkingDirectory, relativePath);

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Fail($"Directory not found: {relativePath}"));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{relativePath}/");

            try
            {
                // List directories
                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    var name = Path.GetFileName(dir);
                    var itemCount = GetItemCount(dir);
                    sb.AppendLine($"  {name}/ ({itemCount} items)");
                }

                // List files
                foreach (var file in Directory.GetFiles(fullPath))
                {
                    var name = Path.GetFileName(file);
                    var size = new FileInfo(file).Length;
                    sb.AppendLine($"  {name} ({FormatSize(size)})");
                }
            }
            catch (System.UnauthorizedAccessException)
            {
                return Task.FromResult(ToolResult.Fail($"Access denied to directory contents: {relativePath}"));
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }

        private int GetItemCount(string directory)
        {
            try
            {
                return Directory.GetFileSystemEntries(directory).Length;
            }
            catch
            {
                return 0;
            }
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
