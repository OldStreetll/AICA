using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for finding files by name pattern using glob syntax.
    /// Fast file discovery without reading content.
    /// </summary>
    public class GlobTool : IAgentTool
    {
        public string Name => "glob";
        public string Description =>
            "Find files by name pattern using glob syntax (e.g., '**/*.cpp', 'src/**/*.h'). " +
            "Returns matching file paths sorted by modification time. " +
            "Do NOT use for searching file contents — use 'grep_search' instead. " +
            "Do NOT use for browsing a single directory — use 'list_dir' instead.";

        private static readonly HashSet<string> ExcludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", "bin", "obj", "node_modules", "packages",
            "__pycache__", ".idea", "dist", ".next", ".nuget", "TestResults"
        };

        private const int DefaultMaxResults = 200;
        private const int MaxSearchDepth = 20;
        private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(15);

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.DirectoryOps,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 15,
                IsModifying = false,
                Tags = new[] { "file", "glob", "find", "pattern" }
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
                        ["pattern"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Glob pattern to match files (e.g., '**/*.cpp', 'src/**/*.h', '*.cs'). " +
                                          "Use ** for recursive directory matching, * for single directory wildcard, ? for single character."
                        },
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Starting directory for the search (relative to workspace root). Default: workspace root."
                        },
                        ["max_results"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Maximum number of results to return. Default: 200."
                        }
                    },
                    Required = new[] { "pattern" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                var pattern = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "pattern");

                string searchPath = context.WorkingDirectory;
                if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj != null)
                {
                    var pathStr = pathObj.ToString().Trim();
                    if (!string.IsNullOrEmpty(pathStr) && pathStr != "." && pathStr != "./")
                    {
                        if (!context.IsPathAccessible(pathStr))
                            return Task.FromResult(ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(pathStr)));

                        searchPath = Path.IsPathRooted(pathStr)
                            ? pathStr
                            : Path.Combine(context.WorkingDirectory, pathStr);
                    }
                }

                if (!Directory.Exists(searchPath))
                    return Task.FromResult(ToolResult.Fail($"Directory not found: {searchPath}"));

                var maxResults = DefaultMaxResults;
                if (call.Arguments.TryGetValue("max_results", out var maxObj) && maxObj != null)
                {
                    if (maxObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                        maxResults = je.GetInt32();
                    else
                        int.TryParse(maxObj.ToString(), out maxResults);
                }
                maxResults = Math.Max(1, Math.Min(1000, maxResults));

                // Convert glob pattern to regex
                var regex = GlobToRegex(pattern);

                // Search for matching files
                var matches = new List<FileMatch>();
                int totalMatches = 0;
                var deadline = DateTime.UtcNow + SearchTimeout;

                SearchDirectory(searchPath, searchPath, regex, pattern.Contains("**"), 0,
                    matches, ref totalMatches, maxResults, deadline);

                // Sort by modification time (most recent first)
                matches.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                // Format output
                var sb = new StringBuilder();
                sb.AppendLine($"Pattern: {pattern}");
                sb.AppendLine($"Search root: {GetRelativePath(searchPath, context.WorkingDirectory)}");
                sb.AppendLine();

                if (matches.Count == 0)
                {
                    sb.AppendLine("No matching files found.");
                    sb.AppendLine("Suggestions: check the pattern syntax, try a broader pattern, or verify the search path.");
                }
                else
                {
                    foreach (var match in matches)
                    {
                        sb.AppendLine($"  {match.RelativePath}  ({FormatSize(match.Size)})");
                    }

                    sb.AppendLine();
                    if (totalMatches > maxResults)
                    {
                        sb.AppendLine($"[Showing {matches.Count} of {totalMatches} matches. Use max_results to see more, or narrow the pattern.]");
                    }
                    else
                    {
                        sb.AppendLine($"[{matches.Count} file(s) matched]");
                    }
                }

                return Task.FromResult(ToolResult.Ok(sb.ToString()));
            }
            catch (ToolParameterException ex)
            {
                return Task.FromResult(ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message)));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "glob");
                return Task.FromResult(ToolErrorHandler.HandleError(error));
            }
        }

        private void SearchDirectory(string dirPath, string rootPath, Regex regex, bool recursive,
            int depth, List<FileMatch> matches, ref int totalMatches, int maxResults, DateTime deadline)
        {
            if (depth > MaxSearchDepth) return;
            if (DateTime.UtcNow > deadline) return;

            try
            {
                // Check files in current directory
                foreach (var file in Directory.GetFiles(dirPath))
                {
                    var relativePath = GetRelativePath(file, rootPath);
                    if (regex.IsMatch(relativePath))
                    {
                        totalMatches++;
                        if (matches.Count < maxResults)
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                matches.Add(new FileMatch
                                {
                                    RelativePath = relativePath,
                                    Size = info.Length,
                                    LastWriteTime = info.LastWriteTimeUtc
                                });
                            }
                            catch { /* skip inaccessible files */ }
                        }
                    }
                }

                // Recurse into subdirectories (always recurse to support ** patterns)
                if (recursive || depth == 0)
                {
                    foreach (var subDir in Directory.GetDirectories(dirPath))
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (ExcludedDirs.Contains(dirName)) continue;

                        SearchDirectory(subDir, rootPath, regex, recursive, depth + 1,
                            matches, ref totalMatches, maxResults, deadline);
                    }
                }
            }
            catch (UnauthorizedAccessException) { /* skip */ }
            catch (DirectoryNotFoundException) { /* skip */ }
        }

        /// <summary>
        /// Convert a glob pattern to a .NET Regex.
        /// Supports **, *, and ? wildcards.
        /// </summary>
        internal static Regex GlobToRegex(string pattern)
        {
            // Normalize path separators
            pattern = pattern.Replace('\\', '/');

            var sb = new StringBuilder("^");
            int i = 0;
            while (i < pattern.Length)
            {
                var c = pattern[i];
                if (c == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        // ** matches any number of directories
                        if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                        {
                            sb.Append("(.+/)?");
                            i += 3;
                        }
                        else
                        {
                            sb.Append(".*");
                            i += 2;
                        }
                    }
                    else
                    {
                        // * matches anything except /
                        sb.Append("[^/]*");
                        i++;
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                    i++;
                }
                else if (c == '.')
                {
                    sb.Append("\\.");
                    i++;
                }
                else if (c == '{')
                {
                    sb.Append("(");
                    i++;
                }
                else if (c == '}')
                {
                    sb.Append(")");
                    i++;
                }
                else if (c == ',')
                {
                    sb.Append("|");
                    i++;
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }
            sb.Append("$");

            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !basePath.EndsWith("/"))
            {
                basePath += Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(basePath.Length).Replace('\\', '/');
            }

            return fullPath.Replace('\\', '/');
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
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

        private class FileMatch
        {
            public string RelativePath { get; set; }
            public long Size { get; set; }
            public DateTime LastWriteTime { get; set; }
        }
    }
}
