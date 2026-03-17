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
        public string Description => "List files and directories in the specified path. " +
            "Use recursive=true when user asks for full/complete structure, directory tree, 完整结构, 目录树. " +
            "For large projects, set max_depth=2 or 3 to avoid excessive output.";

        private static readonly HashSet<string> ExcludedDirs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", "bin", "obj", "node_modules", "packages",
            "__pycache__", ".idea", "dist", ".next", ".nuget", "TestResults"
        };

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
                        },
                        ["recursive"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, list contents recursively in tree format. Default: false"
                        },
                        ["max_depth"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Maximum depth for recursive listing (1-10). Default: 3. Only used when recursive=true"
                        }
                    },
                    Required = new[] { "path" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
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

            // Resolve full path (supports source roots)
            string fullPath;
            if (relativePath == "." || relativePath == "./")
                fullPath = context.WorkingDirectory;
            else if (Path.IsPathRooted(relativePath))
                fullPath = relativePath;
            else
            {
                var resolved = context.ResolveDirectoryPath(relativePath);
                fullPath = resolved ?? Path.Combine(context.WorkingDirectory, relativePath);
            }

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Fail($"Directory not found: {relativePath}"));
            }

            // Parse recursive and max_depth parameters
            bool recursive = false;
            if (call.Arguments.TryGetValue("recursive", out var recObj) && recObj != null)
            {
                if (recObj is bool b) recursive = b;
                else if (recObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.True) recursive = true;
                else bool.TryParse(recObj.ToString(), out recursive);
            }

            int maxDepth = 3;
            if (call.Arguments.TryGetValue("max_depth", out var depthObj) && depthObj != null)
            {
                if (depthObj is System.Text.Json.JsonElement dje && dje.ValueKind == System.Text.Json.JsonValueKind.Number)
                    maxDepth = dje.GetInt32();
                else
                    int.TryParse(depthObj.ToString(), out maxDepth);
            }
            maxDepth = System.Math.Max(1, System.Math.Min(10, maxDepth));

            var sb = new StringBuilder();
            int itemCount_total = 0;
            const int maxItems = 800;

            try
            {
                if (recursive)
                {
                    sb.AppendLine($"{relativePath}/");
                    ListDirectoryRecursive(fullPath, sb, "", 1, maxDepth, ref itemCount_total, maxItems);
                    if (itemCount_total >= maxItems)
                    {
                        int totalEstimate = CountTotalItemsRecursive(fullPath, maxDepth);
                        int remaining = System.Math.Max(0, totalEstimate - maxItems);
                        sb.AppendLine($"\n... (output truncated at {maxItems} items, approximately {remaining} more items not shown)");
                        sb.AppendLine("Tip: Use a more specific path or reduce max_depth to see complete results for a subdirectory.");
                        sb.AppendLine($"[Listed {maxItems} of {totalEstimate} items]");
                    }
                    else
                    {
                        sb.AppendLine($"\n[Total: {itemCount_total} items listed]");
                    }
                }
                else
                {
                    sb.AppendLine($"{relativePath}/");
                    foreach (var dir in Directory.GetDirectories(fullPath))
                    {
                        var name = Path.GetFileName(dir);
                        var count = GetItemCount(dir);
                        sb.AppendLine($"  {name}/ ({count} items)");
                    }
                    var allFiles = Directory.GetFiles(fullPath);
                    foreach (var file in allFiles)
                    {
                        var name = Path.GetFileName(file);
                        var size = new FileInfo(file).Length;
                        sb.AppendLine($"  {name} ({FormatSize(size)})");
                    }
                    var dirCount = Directory.GetDirectories(fullPath).Length;
                    sb.AppendLine($"\n[Total: {dirCount} directories, {allFiles.Length} files]");
                }
            }
            catch (System.UnauthorizedAccessException)
            {
                return Task.FromResult(ToolResult.Fail($"Access denied to directory contents: {relativePath}"));
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }

        private void ListDirectoryRecursive(string dirPath, StringBuilder sb, string indent, int currentDepth, int maxDepth, ref int itemCount, int maxItems)
        {
            if (itemCount >= maxItems) return;

            string[] dirs;
            string[] files;
            try
            {
                dirs = Directory.GetDirectories(dirPath);
                files = Directory.GetFiles(dirPath);
            }
            catch (System.UnauthorizedAccessException)
            {
                sb.AppendLine($"{indent}  [access denied]");
                return;
            }

            // Sort for consistent output
            System.Array.Sort(dirs);
            System.Array.Sort(files);

            // BREADTH-FIRST: List ALL items at current level first
            // This ensures the model sees all top-level directories before budget is consumed
            var subDirsToRecurse = new List<(string path, string name)>();

            foreach (var dir in dirs)
            {
                if (itemCount >= maxItems) return;
                var name = Path.GetFileName(dir);
                if (ExcludedDirs.Contains(name)) continue;

                var count = GetItemCount(dir);
                sb.AppendLine($"{indent}  {name}/ ({count} items)");
                itemCount++;

                if (currentDepth < maxDepth)
                {
                    subDirsToRecurse.Add((dir, name));
                }
            }

            foreach (var file in files)
            {
                if (itemCount >= maxItems) return;
                var name = Path.GetFileName(file);
                var size = new FileInfo(file).Length;
                sb.AppendLine($"{indent}  {name} ({FormatSize(size)})");
                itemCount++;
            }

            // THEN recurse into subdirectories
            foreach (var (subDir, _) in subDirsToRecurse)
            {
                if (itemCount >= maxItems) return;
                ListDirectoryRecursive(subDir, sb, indent + "  ", currentDepth + 1, maxDepth, ref itemCount, maxItems);
            }
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

        /// <summary>
        /// Fast count of total items in a directory tree for truncation reporting.
        /// Stops counting after reaching a reasonable limit to avoid excessive I/O.
        /// </summary>
        private int CountTotalItemsRecursive(string dirPath, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return 0;

            int count = 0;
            const int countLimit = 5000; // Safety limit to avoid excessive counting

            try
            {
                var dirs = Directory.GetDirectories(dirPath);
                var files = Directory.GetFiles(dirPath);
                count += dirs.Length + files.Length;

                if (count > countLimit) return count;

                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    if (ExcludedDirs.Contains(name)) continue;
                    count += CountTotalItemsRecursive(dir, maxDepth, currentDepth + 1);
                    if (count > countLimit) return count;
                }
            }
            catch { }

            return count;
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

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.DirectoryOps,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 30,
                Tags = new[] { "directory", "list", "files", "tree" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }
    }
}
