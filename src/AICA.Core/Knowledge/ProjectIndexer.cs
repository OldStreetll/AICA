using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// Kind of symbol extracted from source code.
    /// </summary>
    public enum SymbolKind
    {
        Class,
        Struct,
        Enum,
        Function,
        Namespace,
        Typedef,
        Define
    }

    /// <summary>
    /// A single symbol extracted from a source file.
    /// </summary>
    public class SymbolRecord
    {
        public string Id { get; }
        public string Name { get; }
        public SymbolKind Kind { get; }
        public string FilePath { get; }
        public string Namespace { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Keywords { get; }

        public SymbolRecord(
            string id,
            string name,
            SymbolKind kind,
            string filePath,
            string ns,
            string summary,
            IReadOnlyList<string> keywords)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            FilePath = filePath ?? "";
            Namespace = ns ?? "";
            Summary = summary ?? "";
            Keywords = keywords ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// The result of indexing a project directory.
    /// </summary>
    public class ProjectIndex
    {
        public IReadOnlyList<SymbolRecord> Symbols { get; }
        public DateTime IndexedAt { get; }
        public int FileCount { get; }
        public TimeSpan IndexDuration { get; }

        public ProjectIndex(
            IReadOnlyList<SymbolRecord> symbols,
            DateTime indexedAt,
            int fileCount,
            TimeSpan indexDuration)
        {
            Symbols = symbols ?? Array.Empty<SymbolRecord>();
            IndexedAt = indexedAt;
            FileCount = fileCount;
            IndexDuration = indexDuration;
        }
    }

    /// <summary>
    /// Scans a project directory and builds a symbol index from source files.
    /// Parses .h, .hpp, .cpp, .cs files using regex-based symbol extraction.
    /// </summary>
    public class ProjectIndexer
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".h", ".hpp", ".hxx", ".cpp", ".cxx", ".c", ".cs"
        };

        private static readonly HashSet<string> SkipDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "build", "cmake", ".git", "bin", "obj", "debug", "release",
            "packages", "node_modules", ".vs", "x64", "x86",
            "CMakeFiles", "TestResults"
        };

        /// <summary>
        /// Index all supported source files under rootPath.
        /// If rootPath appears to be a build subdirectory, automatically
        /// walks up to find the actual project root (containing .git, etc.).
        /// Skips build/, cmake/, .git/ and similar directories.
        /// </summary>
        public async Task<ProjectIndex> IndexDirectoryAsync(
            string rootPath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentNullException(nameof(rootPath));

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

            // If the given path looks like a build subdirectory, try to find the real project root
            rootPath = FindProjectRoot(rootPath);

            var sw = Stopwatch.StartNew();
            var allSymbols = new List<SymbolRecord>();
            var fileCount = 0;

            var files = EnumerateSourceFiles(rootPath);

            // Process files in parallel batches for performance
            await Task.Run(() =>
            {
                foreach (var filePath in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var content = File.ReadAllText(filePath);
                        var relativePath = GetRelativePath(rootPath, filePath);
                        var symbols = SymbolParser.Parse(relativePath, content);

                        if (symbols.Count > 0)
                        {
                            lock (allSymbols)
                            {
                                allSymbols.AddRange(symbols);
                            }
                        }

                        Interlocked.Increment(ref fileCount);
                    }
                    catch (IOException)
                    {
                        // Skip files that can't be read
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip files without permission
                    }
                }
            }, ct);

            sw.Stop();

            return new ProjectIndex(
                symbols: allSymbols,
                indexedAt: DateTime.UtcNow,
                fileCount: fileCount,
                indexDuration: sw.Elapsed);
        }

        /// <summary>
        /// Index a single file and return its symbols.
        /// </summary>
        public Task<IReadOnlyList<SymbolRecord>> IndexFileAsync(
            string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                return Task.FromResult<IReadOnlyList<SymbolRecord>>(Array.Empty<SymbolRecord>());

            var content = File.ReadAllText(filePath);
            var symbols = SymbolParser.Parse(filePath, content);
            return Task.FromResult(symbols);
        }

        private IEnumerable<string> EnumerateSourceFiles(string rootPath)
        {
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] files;

                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (SupportedExtensions.Contains(ext))
                        yield return file;
                }

                try
                {
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (!SkipDirectories.Contains(dirName))
                            stack.Push(subDir);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }

        /// <summary>
        /// Walk up from the given directory to find the project root.
        /// Looks for .git, .svn, .hg directories as strong markers.
        /// Also checks if the current directory name is in the skip list
        /// (e.g. "build"), indicating we should look at the parent.
        /// </summary>
        internal static string FindProjectRoot(string startDir)
        {
            if (string.IsNullOrEmpty(startDir))
                return startDir;

            var strongMarkers = new[] { ".git", ".svn", ".hg" };

            // Check startDir itself for strong markers
            foreach (var marker in strongMarkers)
            {
                if (Directory.Exists(Path.Combine(startDir, marker)))
                    return startDir;
            }

            // If current directory name is in skip list, it's likely a build subdir
            var dirName = Path.GetFileName(startDir);
            var isSkipDir = SkipDirectories.Contains(dirName);

            // Walk up at most 3 levels looking for strong markers
            var current = startDir;
            for (var depth = 1; depth <= 3; depth++)
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                    break;

                foreach (var marker in strongMarkers)
                {
                    if (Directory.Exists(Path.Combine(parent, marker)))
                        return parent;
                }

                current = parent;
            }

            // No project root found — use original path
            return startDir;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length).Replace('\\', '/');

            return fullPath;
        }
    }
}
