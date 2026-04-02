using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for searching text patterns in files within the workspace.
    /// v2.1 T4: Ripgrep-first strategy — always prefers rg (respects .gitignore), C# fallback when unavailable.
    /// </summary>
    public class GrepSearchTool : IAgentTool
    {
        public string Name => "grep_search";
        public string Description =>
            "Search file contents using regex patterns. Returns matching lines with file paths and line numbers. " +
            "Do NOT use for finding files by name — use 'glob' instead. " +
            "Do NOT use for reading entire files — use 'read_file' instead.";

        /// <summary>Timeout for ripgrep process (seconds).</summary>
        private static int RipgrepTimeoutSeconds => Config.AicaConfig.Current.Tools.GrepTimeoutSeconds;

        /// <summary>Cached ripgrep path (null = not searched, empty = not found).</summary>
        private static string _ripgrepPath;
        private static readonly object _ripgrepLock = new object();

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
                            Description = "The regex search pattern"
                        },
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Directory or file path to search in. Defaults to workspace root."
                        },
                        ["include"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "File glob pattern to filter (e.g. '*.cs', '*.cpp')"
                        }
                    },
                    Required = new[] { "pattern" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            // Support both "pattern" (new) and "query" (legacy) parameter names
            if (!call.Arguments.TryGetValue("pattern", out var queryObj) || queryObj == null)
            {
                if (!call.Arguments.TryGetValue("query", out queryObj) || queryObj == null)
                    return ToolResult.Fail("Missing required parameter: pattern");
            }

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
            // Support both "include" (new) and "includes" (legacy)
            object includesObj = null;
            if (!call.Arguments.TryGetValue("include", out includesObj) || includesObj == null)
                call.Arguments.TryGetValue("includes", out includesObj);
            if (includesObj != null)
            {
                // Handle JsonElement arrays: ["*.h", "*.cpp"] -> "*.h,*.cpp"
                if (includesObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in je.EnumerateArray())
                        parts.Add(item.GetString() ?? item.ToString());
                    includePattern = string.Join(",", parts);
                }
                else
                {
                    includePattern = includesObj.ToString();
                }
            }

            bool fixedStrings = false;
            if (call.Arguments.TryGetValue("fixed_strings", out var fixedObj) && fixedObj != null)
                bool.TryParse(fixedObj.ToString(), out fixedStrings);

            bool caseSensitive = false;
            if (call.Arguments.TryGetValue("case_sensitive", out var caseObj) && caseObj != null)
                bool.TryParse(caseObj.ToString(), out caseSensitive);

            int maxResults = 200;
            if (call.Arguments.TryGetValue("max_results", out var maxObj) && maxObj != null)
                int.TryParse(maxObj.ToString(), out maxResults);

            // Resolve full path (supports source roots)
            string fullPath;
            List<string> searchPaths = new List<string>();

            if (string.IsNullOrEmpty(searchPath) || searchPath == "." || searchPath == "./")
            {
                // Default search: include working directory AND all source roots
                searchPaths.Add(context.WorkingDirectory);

                // Add source roots if available
                if (context.SourceRoots != null && context.SourceRoots.Count > 0)
                {
                    searchPaths.AddRange(context.SourceRoots);
                }

                fullPath = context.WorkingDirectory; // For compatibility
            }
            else if (Path.IsPathRooted(searchPath))
            {
                fullPath = searchPath;
                searchPaths.Add(fullPath);
            }
            else
            {
                // Try resolving via source roots first
                var resolved = context.ResolveDirectoryPath(searchPath);
                fullPath = resolved ?? Path.Combine(context.WorkingDirectory, searchPath);
                searchPaths.Add(fullPath);
            }

            if (!context.IsPathAccessible(searchPath))
            {
                // Also check if the resolved path is accessible
                if (fullPath == null || !context.IsPathAccessible(fullPath))
                    return ToolResult.SecurityDenied($"Access denied: {searchPath}");
            }

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

            // Prefer ripgrep when available — natively respects .gitignore at all project sizes
            var rgPath = FindRipgrep();
            if (rgPath != null)
            {
                var rgResult = SearchWithRipgrep(rgPath, query, fullPath,
                    includePattern, caseSensitive, maxResults, context.WorkingDirectory, ct);
                if (rgResult != null)
                    return rgResult; // ripgrep succeeded
                // else: fallback to C# below
            }

            // C# in-memory search (ripgrep unavailable)
            return await Task.Run(() =>
            {
                var results = new StringBuilder();
                int matchCount = 0;
                int filesSearched = 0;
                int filesMatched = 0;

                // Track per-file match counts for accurate statistics
                var fileMatchCounts = new Dictionary<string, int>();

                try
                {
                    // Search in all paths (working directory + source roots)
                    foreach (var searchDir in searchPaths)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (matchCount >= maxResults) break;

                        IEnumerable<string> files;
                        if (File.Exists(searchDir))
                        {
                            files = new[] { searchDir };
                        }
                        else if (Directory.Exists(searchDir))
                        {
                            files = GetSearchFiles(searchDir, includePattern);
                        }
                        else
                        {
                            continue; // Skip non-existent paths
                        }

                        foreach (var file in files)
                        {
                            if (ct.IsCancellationRequested) break;
                            if (IsExcludedFile(file)) continue;

                            filesSearched++;

                            try
                            {
                                // Skip files larger than 5MB to avoid reading huge generated files
                                // (was 1MB, raised to support large C/C++ source files like decoder.c at 1.4MB)
                                var fileInfo = new FileInfo(file);
                                if (fileInfo.Length > 5 * 1024 * 1024)
                                {
                                    continue;
                                }

                                var lines = File.ReadAllLines(file);
                                int fileMatchCount = 0;
                                var relativePath = GetRelativePath(context.WorkingDirectory, file);
                                bool fileHeaderWritten = false;

                                for (int i = 0; i < lines.Length; i++)
                                {
                                    if (regex.IsMatch(lines[i]))
                                    {
                                        fileMatchCount++;

                                        // Only write to results if we haven't hit the display limit
                                        if (matchCount < maxResults)
                                        {
                                            if (!fileHeaderWritten)
                                            {
                                                results.AppendLine($"\n{relativePath}:");
                                                fileHeaderWritten = true;
                                            }
                                            results.AppendLine($"  {i + 1}: {lines[i].Trim()}");
                                            matchCount++;
                                        }
                                    }
                                }

                                // Record file match count even if display was truncated
                                if (fileMatchCount > 0)
                                {
                                    fileMatchCounts[relativePath] = fileMatchCount;
                                    filesMatched++;
                                }
                            }
                            catch
                            {
                                // Skip files we can't read
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail($"Search error: {ex.Message}");
                }

                if (fileMatchCounts.Count == 0)
                {
                    return ToolResult.Ok($"No matches found for '{query}' in {filesSearched} files.\n[TOOL_EXACT_STATS: matches=0, files_matched=0, files_searched={filesSearched}]");
                }

                // Calculate total matches (may be more than displayed)
                int totalMatches = fileMatchCounts.Values.Sum();

                var summary = new StringBuilder();
                summary.AppendLine($"Found {totalMatches} match(es) in {filesMatched} file(s) (searched {filesSearched} files)");

                // If results were truncated, provide per-file statistics
                if (totalMatches > maxResults)
                {
                    summary.AppendLine($"[Display truncated at {maxResults} results, but all {totalMatches} matches were counted]");
                    summary.AppendLine();
                    summary.AppendLine("Per-file match counts:");
                    foreach (var kvp in fileMatchCounts.OrderByDescending(x => x.Value))
                    {
                        summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
                    }
                    summary.AppendLine();
                    summary.AppendLine("Detailed matches (first " + maxResults + "):");
                }

                var output = summary.ToString() + results.ToString();
                output += $"\n[TOOL_EXACT_STATS: matches={totalMatches}, files_matched={filesMatched}, files_searched={filesSearched}]";
                return ToolResult.Ok(output);
            }, ct).ConfigureAwait(false);
        }

        private IEnumerable<string> GetSearchFiles(string directory, string includePattern)
        {
            // Support multiple patterns separated by comma or semicolon
            // e.g. "*.h,*.cpp" or "*.h;*.cpp" or just "*.h"
            var patterns = new List<string>();
            if (!string.IsNullOrEmpty(includePattern))
            {
                // Clean up array-like input from LLM: ["*.h", "*.cpp"] -> *.h, *.cpp
                var cleaned = includePattern.Trim().TrimStart('[').TrimEnd(']');
                cleaned = cleaned.Replace("\"", "").Replace("'", "");
                foreach (var p in cleaned.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        patterns.Add(trimmed);
                }
            }
            if (patterns.Count == 0)
                patterns.Add("*.*");

            // Use manual recursive enumeration to handle per-directory access errors gracefully
            // Merge results from all patterns
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in patterns)
            {
                foreach (var file in EnumerateFilesSafe(directory, pattern))
                {
                    if (seen.Add(file))
                        yield return file;
                }
            }
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

            var excludedNames = new HashSet<string>(Config.AicaConfig.Current.Tools.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);

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
            var binaryExtensions = new HashSet<string>(Config.AicaConfig.Current.Tools.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);

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

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.Search,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 60,
                Tags = new[] { "search", "grep", "find", "code" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }

        #region v2.1 T4: Ripgrep Integration

        /// <summary>
        /// Find ripgrep executable. Cached after first call.
        /// Search order: VSIX embedded → PATH → null (fallback to C#).
        /// </summary>
        internal static string FindRipgrep()
        {
            lock (_ripgrepLock)
            {
                if (_ripgrepPath != null)
                    return _ripgrepPath.Length == 0 ? null : _ripgrepPath;

                // 1. VSIX assembly directory: tools/ripgrep/rg.exe
                try
                {
                    var asmDir = Path.GetDirectoryName(typeof(GrepSearchTool).Assembly.Location);
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        var embedded = Path.Combine(asmDir, "tools", "ripgrep", "rg.exe");
                        if (File.Exists(embedded))
                        {
                            _ripgrepPath = embedded;
                            return _ripgrepPath;
                        }
                    }
                }
                catch { /* ignore */ }

                // 2. PATH lookup via "where rg"
                try
                {
                    var psi = new ProcessStartInfo("where", "rg")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadLine();
                        proc.WaitForExit(3000);
                        if (!string.IsNullOrEmpty(output) && File.Exists(output.Trim()))
                        {
                            _ripgrepPath = output.Trim();
                            return _ripgrepPath;
                        }
                    }
                }
                catch { /* ignore */ }

                _ripgrepPath = string.Empty; // Not found
                return null;
            }
        }

        /// <summary>
        /// Execute search using ripgrep process with --json output.
        /// Returns null if rg fails (caller should fallback to C#).
        /// </summary>
        private ToolResult SearchWithRipgrep(string rgPath, string query, string searchDir,
            string includePattern, bool caseSensitive, int maxResults, string workingDirectory, CancellationToken ct)
        {
            try
            {
                var args = new StringBuilder();
                args.Append("--json ");
                args.Append($"--max-count={maxResults} ");
                args.Append("--max-filesize=50M ");

                // Excluded directories
                args.Append("--glob=\"!.git\" --glob=\"!.vs\" --glob=\"!bin\" --glob=\"!obj\" ");
                args.Append("--glob=\"!node_modules\" --glob=\"!packages\" --glob=\"!.nuget\" ");
                args.Append("--glob=\"!TestResults\" ");

                // Exclude binary extensions
                args.Append("--glob=\"!*.exe\" --glob=\"!*.dll\" --glob=\"!*.pdb\" --glob=\"!*.obj\" ");
                args.Append("--glob=\"!*.png\" --glob=\"!*.jpg\" --glob=\"!*.zip\" --glob=\"!*.vsix\" ");

                if (!caseSensitive)
                    args.Append("--ignore-case ");

                // Include pattern
                if (!string.IsNullOrEmpty(includePattern))
                {
                    var cleaned = includePattern.Trim().TrimStart('[').TrimEnd(']')
                        .Replace("\"", "").Replace("'", "");
                    foreach (var p in cleaned.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = p.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            args.Append($"--glob=\"{trimmed}\" ");
                    }
                }

                // Pattern and search path
                args.Append($"-- \"{query.Replace("\"", "\\\"")}\" ");
                args.Append($"\"{searchDir}\"");

                var psi = new ProcessStartInfo(rgPath, args.ToString())
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                var proc = Process.Start(psi);
                if (proc == null)
                    return null; // Fallback to C#

                var results = new StringBuilder();
                int matchCount = 0;
                int filesMatched = 0;
                string currentFile = null;
                var fileMatchCounts = new Dictionary<string, int>();

                // Read JSON lines from stdout
                using (var reader = proc.StandardOutput)
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            using (var doc = JsonDocument.Parse(line))
                            {
                                var root = doc.RootElement;
                                if (!root.TryGetProperty("type", out var typeElem))
                                    continue;

                                var type = typeElem.GetString();
                                if (type == "match" && matchCount < maxResults)
                                {
                                    var data = root.GetProperty("data");
                                    var path = data.GetProperty("path").GetProperty("text").GetString();
                                    var lineNum = data.GetProperty("line_number").GetInt32();
                                    var lineText = data.GetProperty("lines").GetProperty("text").GetString()?.TrimEnd('\n', '\r');

                                    // Relative path
                                    var relPath = path;
                                    if (relPath.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
                                        relPath = relPath.Substring(workingDirectory.Length).TrimStart('/', '\\');
                                    relPath = relPath.Replace('/', Path.DirectorySeparatorChar);

                                    if (relPath != currentFile)
                                    {
                                        currentFile = relPath;
                                        results.AppendLine($"\n{relPath}:");
                                        filesMatched++;
                                    }

                                    results.AppendLine($"  {lineNum}: {lineText?.Trim()}");
                                    matchCount++;

                                    if (!fileMatchCounts.ContainsKey(relPath))
                                        fileMatchCounts[relPath] = 0;
                                    fileMatchCounts[relPath]++;
                                }
                            }
                        }
                        catch
                        {
                            // Skip malformed JSON lines
                        }
                    }
                }

                // Wait for process with timeout
                if (!proc.WaitForExit(RipgrepTimeoutSeconds * 1000))
                {
                    try { proc.Kill(); } catch { /* ignore */ }
                }

                if (matchCount == 0)
                {
                    return ToolResult.Ok($"No matches found for '{query}'.\n[TOOL_EXACT_STATS: matches=0, files_matched=0, engine=ripgrep]");
                }

                var totalMatches = fileMatchCounts.Values.Sum();
                var summary = new StringBuilder();
                summary.AppendLine($"Found {totalMatches} match(es) in {filesMatched} file(s) [engine: ripgrep]");

                if (totalMatches > maxResults)
                {
                    summary.AppendLine($"[Display truncated at {maxResults} results]");
                }

                var output = summary.ToString() + results.ToString();
                output += $"\n[TOOL_EXACT_STATS: matches={totalMatches}, files_matched={filesMatched}, engine=ripgrep]";
                return ToolResult.Ok(output);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] ripgrep failed: {ex.Message}, falling back to C#");
                return null; // Fallback to C#
            }
        }

        #endregion
    }
}
