using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Project primary language classification.
    /// </summary>
    public enum ProjectLanguage
    {
        Unknown,
        CppC,
        CSharp,
        Python,
        TypeScript
    }

    /// <summary>
    /// Detects the primary programming language of a project by scanning file extensions.
    /// Results are cached per working directory to avoid repeated filesystem scans.
    /// </summary>
    public static class ProjectLanguageDetector
    {
        private static readonly ConcurrentDictionary<string, ProjectLanguage> Cache =
            new ConcurrentDictionary<string, ProjectLanguage>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> CppExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cpp", ".c", ".cc", ".cxx", ".h", ".hpp", ".hxx" };

        private static readonly HashSet<string> CSharpExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs" };

        private static readonly HashSet<string> PythonExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".py", ".pyw" };

        private static readonly HashSet<string> TypeScriptExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx" };

        private static readonly HashSet<string> ExcludedDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", "debug", "release", "node_modules", ".git", ".vs",
                ".svn", "packages", "build", "out", "dist", ".aica-rules"
            };

        /// <summary>
        /// Detect the primary language of a project by scanning file extensions in the working directory.
        /// </summary>
        public static ProjectLanguage DetectLanguage(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
                return ProjectLanguage.Unknown;

            return Cache.GetOrAdd(workingDirectory, dir => ScanDirectory(dir));
        }

        /// <summary>
        /// Detect language from a single file path (fallback when directory scan is inconclusive).
        /// </summary>
        public static ProjectLanguage DetectFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return ProjectLanguage.Unknown;

            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext))
                return ProjectLanguage.Unknown;

            if (CppExtensions.Contains(ext)) return ProjectLanguage.CppC;
            if (CSharpExtensions.Contains(ext)) return ProjectLanguage.CSharp;
            if (PythonExtensions.Contains(ext)) return ProjectLanguage.Python;
            if (TypeScriptExtensions.Contains(ext)) return ProjectLanguage.TypeScript;

            return ProjectLanguage.Unknown;
        }

        /// <summary>
        /// Clear the cache (useful for testing or when the project changes).
        /// </summary>
        public static void ClearCache()
        {
            Cache.Clear();
        }

        private static ProjectLanguage ScanDirectory(string directory)
        {
            var counts = new Dictionary<ProjectLanguage, int>
            {
                { ProjectLanguage.CppC, 0 },
                { ProjectLanguage.CSharp, 0 },
                { ProjectLanguage.Python, 0 },
                { ProjectLanguage.TypeScript, 0 }
            };

            int totalSourceFiles = 0;
            const int maxFiles = 2000; // Safety limit

            try
            {
                foreach (var file in EnumerateSourceFiles(directory, maxFiles))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext)) continue;

                    if (CppExtensions.Contains(ext)) counts[ProjectLanguage.CppC]++;
                    else if (CSharpExtensions.Contains(ext)) counts[ProjectLanguage.CSharp]++;
                    else if (PythonExtensions.Contains(ext)) counts[ProjectLanguage.Python]++;
                    else if (TypeScriptExtensions.Contains(ext)) counts[ProjectLanguage.TypeScript]++;
                    else continue;

                    totalSourceFiles++;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] ProjectLanguageDetector scan error: {ex.Message}");
            }

            if (totalSourceFiles == 0)
                return ProjectLanguage.Unknown;

            // Find the language with highest count
            var dominant = counts.OrderByDescending(kv => kv.Value).First();

            // Require >50% share to be confident
            if (dominant.Value > totalSourceFiles / 2)
                return dominant.Key;

            // Inconclusive — return the plurality winner
            return dominant.Value > 0 ? dominant.Key : ProjectLanguage.Unknown;
        }

        private static IEnumerable<string> EnumerateSourceFiles(string directory, int maxFiles)
        {
            int count = 0;
            var stack = new Stack<string>();
            stack.Push(directory);

            while (stack.Count > 0 && count < maxFiles)
            {
                var dir = stack.Pop();

                // Enumerate files in current directory
                string[] files;
                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch
                {
                    continue; // Skip inaccessible directories
                }

                foreach (var file in files)
                {
                    if (count >= maxFiles) yield break;
                    count++;
                    yield return file;
                }

                // Push subdirectories (excluding build/output dirs)
                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var subdir in subdirs)
                {
                    var dirName = Path.GetFileName(subdir);
                    if (!ExcludedDirs.Contains(dirName))
                    {
                        stack.Push(subdir);
                    }
                }
            }
        }
    }
}
