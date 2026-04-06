using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICA.Core.Workspace
{
    /// <summary>
    /// Unified path resolver that searches across the working directory and
    /// source roots discovered from solution/project file parsing.
    /// 
    /// Resolution priority:
    /// 1. Absolute path (if exists and accessible)
    /// 2. Relative to working directory
    /// 3. Relative path match in source index
    /// 4. Relative to each SourceRoot
    /// 5. File name lookup in source index (with disambiguation)
    /// </summary>
    public class PathResolver
    {
        private readonly string _workingDirectory;
        private readonly SolutionSourceIndex _sourceIndex;

        public PathResolver(string workingDirectory, SolutionSourceIndex sourceIndex = null)
        {
            _workingDirectory = NormalizeDirPath(workingDirectory);
            _sourceIndex = sourceIndex;
        }

        /// <summary>
        /// Resolve a requested file path to an absolute path.
        /// Returns null if the file cannot be found.
        /// </summary>
        /// <param name="requestedPath">Path from LLM or user (relative, absolute, or file name).</param>
        /// <returns>Absolute path to the file, or null if not found.</returns>
        public string ResolveFile(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return null;

            var cleaned = CleanPath(requestedPath);
            if (string.IsNullOrEmpty(cleaned))
                return null;

            // 1. Absolute path
            if (Path.IsPathRooted(cleaned))
            {
                if (File.Exists(cleaned))
                    return Path.GetFullPath(cleaned);
                return null;
            }

            // When SourceRoots exist (out-of-source build), check source roots FIRST.
            // This is because the build directory often mirrors the source tree structure,
            // and users intend to access actual source files, not build artifacts.
            if (_sourceIndex != null && _sourceIndex.SourceRoots.Count > 0)
            {
                // 2a. Relative path match in source index
                var indexResolved = _sourceIndex.ResolveFile(cleaned);
                if (indexResolved != null && File.Exists(indexResolved))
                    return indexResolved;

                // 2b. Relative to each SourceRoot
                foreach (var root in _sourceIndex.SourceRoots)
                {
                    var rootPath = Path.GetFullPath(Path.Combine(root, cleaned));
                    if (File.Exists(rootPath))
                        return rootPath;
                }
            }

            // 3. Relative to working directory (fallback, with overlap detection)
            if (!string.IsNullOrEmpty(_workingDirectory))
            {
                var workspacePath = SmartCombine(_workingDirectory, cleaned);
                if (File.Exists(workspacePath))
                    return workspacePath;

                // Also try naive combine in case SmartCombine was too aggressive
                var naivePath = Path.GetFullPath(Path.Combine(_workingDirectory, cleaned));
                if (naivePath != workspacePath && File.Exists(naivePath))
                    return naivePath;
            }

            // 4. Source index resolution (when no SourceRoots — normal project)
            if (_sourceIndex != null && _sourceIndex.SourceRoots.Count == 0)
            {
                var indexResolved = _sourceIndex.ResolveFile(cleaned);
                if (indexResolved != null && File.Exists(indexResolved))
                    return indexResolved;
            }

            return null;
        }

        /// <summary>
        /// Resolve a requested directory path to an absolute path.
        /// Returns null if the directory cannot be found.
        /// </summary>
        public string ResolveDirectory(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return _workingDirectory;

            var cleaned = CleanPath(requestedPath);
            if (string.IsNullOrEmpty(cleaned))
                return _workingDirectory;

            if (cleaned == "." || cleaned == "./")
                return _workingDirectory;

            // 1. Absolute path
            if (Path.IsPathRooted(cleaned))
            {
                if (Directory.Exists(cleaned))
                    return Path.GetFullPath(cleaned);
                return null;
            }

            // When SourceRoots exist (out-of-source build), check source roots FIRST.
            // Build directories often mirror the source tree (e.g., build/src/App/ vs source/src/App/),
            // and users intend to browse actual source directories, not build output.
            if (_sourceIndex != null && _sourceIndex.SourceRoots.Count > 0)
            {
                foreach (var root in _sourceIndex.SourceRoots)
                {
                    var rootPath = Path.GetFullPath(Path.Combine(root, cleaned));
                    if (Directory.Exists(rootPath))
                        return rootPath;
                }
            }

            // 3. Relative to working directory (fallback, with overlap detection)
            if (!string.IsNullOrEmpty(_workingDirectory))
            {
                var workspacePath = SmartCombine(_workingDirectory, cleaned);
                if (Directory.Exists(workspacePath))
                    return workspacePath;

                // Also try naive combine in case SmartCombine was too aggressive
                var naivePath = Path.GetFullPath(Path.Combine(_workingDirectory, cleaned));
                if (naivePath != workspacePath && Directory.Exists(naivePath))
                    return naivePath;
            }

            return null;
        }

        /// <summary>
        /// Check if a path is accessible (within working directory or source roots).
        /// </summary>
        public bool IsAccessible(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var cleaned = CleanPath(path);
            if (string.IsNullOrEmpty(cleaned))
                return false;

            // Special cases
            if (cleaned == "." || cleaned == "./" || cleaned == "/" || cleaned == "\\")
                return !string.IsNullOrEmpty(_workingDirectory) && Directory.Exists(_workingDirectory);

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(cleaned)
                    ? Path.GetFullPath(cleaned)
                    : Path.GetFullPath(Path.Combine(_workingDirectory ?? "", cleaned));
            }
            catch
            {
                return false;
            }

            // Within working directory
            if (!string.IsNullOrEmpty(_workingDirectory) &&
                fullPath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase))
                return true;

            // Within source roots
            if (_sourceIndex != null)
            {
                foreach (var root in _sourceIndex.SourceRoots)
                {
                    var normalizedRoot = root.TrimEnd('\\', '/') + "\\";
                    if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Exact match in indexed paths
                if (_sourceIndex.ContainsPath(fullPath))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a resolved absolute path is outside the working directory
        /// (i.e., in a source root). Used to trigger extra confirmation for writes.
        /// </summary>
        public bool IsExternalPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(_workingDirectory))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(absolutePath);
                return !fullPath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get disambiguation candidates when a file name matches multiple indexed paths.
        /// Returns user-friendly relative paths from source roots.
        /// </summary>
        public List<string> GetDisambiguationCandidates(string requestedPath)
        {
            if (_sourceIndex == null)
                return new List<string>();

            var candidates = _sourceIndex.GetCandidates(requestedPath);
            if (candidates.Count <= 1)
                return candidates;

            // Convert to relative paths from source roots for readability
            var result = new List<string>();
            foreach (var candidate in candidates)
            {
                var relative = GetRelativeFromSourceRoot(candidate);
                result.Add(relative ?? candidate);
            }
            return result;
        }

        /// <summary>
        /// Get a source-root-relative path for display purposes.
        /// </summary>
        public string GetRelativeFromSourceRoot(string absolutePath)
        {
            if (_sourceIndex == null || string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            foreach (var root in _sourceIndex.SourceRoots)
            {
                var normalizedRoot = root.TrimEnd('\\', '/') + "\\";
                if (absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return absolutePath.Substring(normalizedRoot.Length);
                }
            }

            return absolutePath;
        }

        /// <summary>
        /// Get all search directories (working directory + source roots).
        /// Used by search tools to expand their search scope.
        /// </summary>
        public List<string> GetAllSearchRoots()
        {
            var roots = new List<string>();

            if (!string.IsNullOrEmpty(_workingDirectory) && Directory.Exists(_workingDirectory))
                roots.Add(_workingDirectory);

            if (_sourceIndex != null)
            {
                foreach (var root in _sourceIndex.SourceRoots)
                {
                    if (Directory.Exists(root) && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                        roots.Add(root);
                }
            }

            return roots;
        }

        #region Helpers

        /// <summary>
        /// Safely combine a base directory with a relative path, detecting and removing
        /// overlapping path segments. This prevents the common bug where:
        ///   base = "D:\project\src\"  +  relative = "src/CalcManager"
        ///   naive Combine = "D:\project\src\src\CalcManager"  (WRONG — doubled "src")
        ///   smart Combine = "D:\project\src\CalcManager"      (CORRECT)
        /// </summary>
        private static string SmartCombine(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(basePath))
                return relativePath;
            if (string.IsNullOrEmpty(relativePath))
                return basePath;

            // Normalize separators for comparison
            var normalizedBase = basePath.Replace('/', '\\').TrimEnd('\\');
            var normalizedRel = relativePath.Replace('/', '\\').TrimStart('\\');

            // Split into segments
            var baseSegments = normalizedBase.Split('\\');
            var relSegments = normalizedRel.Split('\\');

            if (relSegments.Length == 0)
                return basePath;

            // Check if the relative path's leading segments overlap with the base path's trailing segments.
            // Example: base = [..., "src"], rel = ["src", "CalcManager"]
            //   → overlap at "src", result strips the duplicate prefix from rel
            int maxOverlap = System.Math.Min(baseSegments.Length, relSegments.Length);
            int bestOverlap = 0;

            for (int overlapLen = 1; overlapLen <= maxOverlap; overlapLen++)
            {
                bool match = true;
                for (int j = 0; j < overlapLen; j++)
                {
                    var baseIdx = baseSegments.Length - overlapLen + j;
                    if (!string.Equals(baseSegments[baseIdx], relSegments[j], System.StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    bestOverlap = overlapLen;
            }

            // Only apply overlap removal if it doesn't consume the entire relative path.
            // This prevents false positives like base="...\test\test" rel="test\test\data.txt"
            // from silently discarding "data.txt".
            if (bestOverlap > 0 && bestOverlap < relSegments.Length)
            {
                // Remove overlapping prefix from relative path
                var trimmedRel = string.Join("\\", relSegments, bestOverlap, relSegments.Length - bestOverlap);
                if (string.IsNullOrEmpty(trimmedRel))
                    return basePath;
                return Path.GetFullPath(Path.Combine(basePath, trimmedRel));
            }

            return Path.GetFullPath(Path.Combine(basePath, relativePath));
        }

        private static string CleanPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Remove control characters
            var sb = new System.Text.StringBuilder(path.Length);
            foreach (var c in path)
            {
                if (c >= 0x20 && c != 0x7F)
                    sb.Append(c);
            }

            var cleaned = sb.ToString().Trim();

            // Remove surrounding quotes
            if (cleaned.Length >= 2 &&
                ((cleaned[0] == '"' && cleaned[cleaned.Length - 1] == '"') ||
                 (cleaned[0] == '\'' && cleaned[cleaned.Length - 1] == '\'')))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }

            return cleaned;
        }

        private static string NormalizeDirPath(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            try
            {
                return Path.GetFullPath(dir).TrimEnd('\\', '/') + "\\";
            }
            catch
            {
                return dir.TrimEnd('\\', '/') + "\\";
            }
        }

        #endregion
    }
}
