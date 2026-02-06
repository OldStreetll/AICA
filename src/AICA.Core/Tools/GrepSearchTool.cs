using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for searching text patterns in files within the workspace
    /// </summary>
    public class GrepSearchTool : IAgentTool
    {
        public string Name => "grep_search";
        public string Description => "Search for a text pattern (regex or fixed string) in files within the workspace.";

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
                        ["query"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The search pattern (regex by default, or fixed string if fixed_strings is true)"
                        },
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Directory or file path to search in (relative to workspace root). Defaults to workspace root."
                        },
                        ["includes"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "File glob pattern to include (e.g. '*.cs', '*.py'). Optional."
                        },
                        ["fixed_strings"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, treat query as a literal string instead of regex. Default is false."
                        },
                        ["case_sensitive"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, search is case-sensitive. Default is false."
                        },
                        ["max_results"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Maximum number of matching lines to return. Default is 50."
                        }
                    },
                    Required = new[] { "query" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            if (!call.Arguments.TryGetValue("query", out var queryObj) || queryObj == null)
                return ToolResult.Fail("Missing required parameter: query");

            var query = queryObj.ToString();
            if (string.IsNullOrEmpty(query))
                return ToolResult.Fail("Search query cannot be empty");

            // Parse optional parameters
            var searchPath = ".";
            if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                var p = pathObj.ToString();
                if (!string.IsNullOrWhiteSpace(p)) searchPath = p;
            }

            string includePattern = null;
            if (call.Arguments.TryGetValue("includes", out var includesObj) && includesObj != null)
                includePattern = includesObj.ToString();

            bool fixedStrings = false;
            if (call.Arguments.TryGetValue("fixed_strings", out var fixedObj) && fixedObj != null)
                bool.TryParse(fixedObj.ToString(), out fixedStrings);

            bool caseSensitive = false;
            if (call.Arguments.TryGetValue("case_sensitive", out var caseObj) && caseObj != null)
                bool.TryParse(caseObj.ToString(), out caseSensitive);

            int maxResults = 50;
            if (call.Arguments.TryGetValue("max_results", out var maxObj) && maxObj != null)
                int.TryParse(maxObj.ToString(), out maxResults);

            // Resolve full path
            string fullPath;
            if (string.IsNullOrEmpty(searchPath) || searchPath == "." || searchPath == "./")
                fullPath = context.WorkingDirectory;
            else if (Path.IsPathRooted(searchPath))
                fullPath = searchPath;
            else
                fullPath = Path.Combine(context.WorkingDirectory, searchPath);

            if (!context.IsPathAccessible(searchPath))
                return ToolResult.Fail($"Access denied: {searchPath}");

            // Build regex
            Regex regex;
            try
            {
                var pattern = fixedStrings ? Regex.Escape(query) : query;
                var options = RegexOptions.Compiled;
                if (!caseSensitive) options |= RegexOptions.IgnoreCase;
                regex = new Regex(pattern, options);
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Fail($"Invalid regex pattern: {ex.Message}");
            }

            // Execute search - ConfigureAwait(false) to avoid deadlock with UI thread
            return await Task.Run(() =>
            {
                var results = new StringBuilder();
                int matchCount = 0;
                int filesSearched = 0;
                int filesMatched = 0;

                try
                {
                    IEnumerable<string> files;
                    if (File.Exists(fullPath))
                    {
                        files = new[] { fullPath };
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        files = GetSearchFiles(fullPath, includePattern);
                    }
                    else
                    {
                        return ToolResult.Fail($"Path not found: {searchPath}");
                    }

                    foreach (var file in files)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (matchCount >= maxResults) break;
                        if (IsExcludedFile(file)) continue;

                        filesSearched++;

                        try
                        {
                            var lines = File.ReadAllLines(file);
                            bool fileHasMatch = false;

                            for (int i = 0; i < lines.Length; i++)
                            {
                                if (matchCount >= maxResults) break;
                                if (regex.IsMatch(lines[i]))
                                {
                                    if (!fileHasMatch)
                                    {
                                        var relativePath = GetRelativePath(context.WorkingDirectory, file);
                                        results.AppendLine($"\n{relativePath}:");
                                        filesMatched++;
                                        fileHasMatch = true;
                                    }

                                    var lineContent = lines[i].Length > 200 
                                        ? lines[i].Substring(0, 200) + "..." 
                                        : lines[i];
                                    results.AppendLine($"  {i + 1}: {lineContent}");
                                    matchCount++;
                                }
                            }
                        }
                        catch
                        {
                            // Skip files that can't be read (binary, locked, etc.)
                        }
                    }
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail($"Search error: {ex.Message}");
                }

                if (matchCount == 0)
                {
                    return ToolResult.Ok($"No matches found for '{query}' in {filesSearched} files.");
                }

                var summary = $"Found {matchCount} match(es) in {filesMatched} file(s) (searched {filesSearched} files)";
                if (matchCount >= maxResults)
                    summary += $" [truncated at {maxResults} results]";

                return ToolResult.Ok(summary + "\n" + results.ToString());
            }, ct).ConfigureAwait(false);
        }

        private IEnumerable<string> GetSearchFiles(string directory, string includePattern)
        {
            var searchPattern = "*.*";
            if (!string.IsNullOrEmpty(includePattern))
            {
                searchPattern = includePattern;
            }

            // Use manual recursive enumeration to handle per-directory access errors gracefully
            return EnumerateFilesSafe(directory, searchPattern);
        }

        /// <summary>
        /// Recursively enumerate files, skipping directories that throw access errors.
        /// Unlike Directory.EnumerateFiles(AllDirectories), this won't abort on permission errors.
        /// </summary>
        private IEnumerable<string> EnumerateFilesSafe(string directory, string searchPattern, int maxFiles = 10000)
        {
            int count = 0;
            var dirs = new Stack<string>();
            dirs.Push(directory);

            while (dirs.Count > 0 && count < maxFiles)
            {
                var currentDir = dirs.Pop();

                // Skip excluded directories early
                if (IsExcludedDirectory(currentDir))
                    continue;

                // Enumerate files in current directory
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir, searchPattern);
                }
                catch
                {
                    continue; // Skip directories we can't read
                }

                foreach (var file in files)
                {
                    if (count >= maxFiles) yield break;
                    count++;
                    yield return file;
                }

                // Enumerate subdirectories
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        dirs.Push(subDir);
                    }
                }
                catch
                {
                    // Skip if we can't enumerate subdirectories
                }
            }
        }

        private bool IsExcludedDirectory(string dirPath)
        {
            var dirName = Path.GetFileName(dirPath);
            if (string.IsNullOrEmpty(dirName)) return false;

            var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".vs", "bin", "obj", "node_modules", "packages", ".nuget", "TestResults"
            };

            return excludedNames.Contains(dirName);
        }

        private bool IsExcludedFile(string path)
        {
            var excludedDirs = new[]
            {
                $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}packages{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.nuget{Path.DirectorySeparatorChar}"
            };

            foreach (var dir in excludedDirs)
            {
                if (path.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Skip binary/large files by extension
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            var binaryExtensions = new HashSet<string>
            {
                ".exe", ".dll", ".pdb", ".obj", ".o", ".lib", ".so",
                ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
                ".zip", ".tar", ".gz", ".rar", ".7z",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                ".vsix", ".nupkg", ".snk"
            };

            return binaryExtensions.Contains(ext);
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath)) return fullPath;
            try
            {
                var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var fullUri = new Uri(fullPath);
                return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return fullPath;
            }
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
