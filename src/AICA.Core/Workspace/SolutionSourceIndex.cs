using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace AICA.Core.Workspace
{
    /// <summary>
    /// Parses Visual Studio solution and project files to build an index of all
    /// source file paths referenced by the solution. This enables AICA to find
    /// source files that reside outside the working directory (e.g., CMake
    /// out-of-source builds where .sln is in build/ but sources are elsewhere).
    /// </summary>
    public class SolutionSourceIndex
    {
        /// <summary>
        /// File name (case-insensitive) → list of absolute paths.
        /// Multiple entries exist when different projects contain files with the same name.
        /// </summary>
        public Dictionary<string, List<string>> FileNameIndex { get; }
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Relative path (from a source root, case-insensitive) → absolute path.
        /// </summary>
        public Dictionary<string, string> RelativePathIndex { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Computed source root directories (common parent directories of all indexed files
        /// that are outside the working directory).
        /// </summary>
        public List<string> SourceRoots { get; } = new List<string>();

        /// <summary>
        /// All indexed absolute paths (for quick membership checks).
        /// </summary>
        public HashSet<string> AllIndexedPaths { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Project name → list of source file absolute paths.
        /// </summary>
        public Dictionary<string, List<string>> ProjectFiles { get; }
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Total number of indexed source files.
        /// </summary>
        public int TotalFiles => AllIndexedPaths.Count;

        /// <summary>
        /// If non-null, indicates that the project was opened from a different location
        /// than where it was originally built. Contains a user-friendly warning message.
        /// </summary>
        public string PathMismatchInfo { get; set; }

        /// <summary>
        /// Maximum number of files to index (safety limit for huge solutions).
        /// </summary>
        private const int MaxIndexedFiles = 50000;

        /// <summary>
        /// Build a source index from a Visual Studio solution file.
        /// </summary>
        /// <param name="slnPath">Absolute path to the .sln file.</param>
        /// <param name="workingDirectory">The working directory (solution directory). 
        /// Files within this directory are still indexed but SourceRoots only includes
        /// directories outside the working directory.</param>
        /// <returns>A populated SolutionSourceIndex, or an empty one if parsing fails.</returns>
        public static SolutionSourceIndex BuildFromSolution(string slnPath, string workingDirectory = null)
        {
            var index = new SolutionSourceIndex();

            if (string.IsNullOrEmpty(slnPath) || !File.Exists(slnPath))
                return index;

            var slnDir = Path.GetDirectoryName(slnPath);
            if (string.IsNullOrEmpty(workingDirectory))
                workingDirectory = slnDir;

            // Normalize working directory
            workingDirectory = NormalizePath(workingDirectory);

            try
            {
                // Step 1: Parse .sln to find all project file paths
                var projectPaths = ParseSolutionFile(slnPath);

                // Step 2: For each project file, extract source file references
                var allSourceFiles = new List<(string projectName, string filePath)>();

                foreach (var (projName, projRelPath) in projectPaths)
                {
                    var projAbsPath = Path.IsPathRooted(projRelPath)
                        ? projRelPath
                        : Path.GetFullPath(Path.Combine(slnDir, projRelPath));

                    if (!File.Exists(projAbsPath))
                        continue;

                    var ext = Path.GetExtension(projAbsPath).ToLowerInvariant();
                    List<string> files = null;

                    if (ext == ".vcxproj")
                        files = ParseVcxproj(projAbsPath);
                    else if (ext == ".csproj")
                        files = ParseCsproj(projAbsPath);

                    if (files != null && files.Count > 0)
                    {
                        foreach (var f in files)
                        {
                            allSourceFiles.Add((projName, f));
                            if (allSourceFiles.Count >= MaxIndexedFiles)
                                break;
                        }
                    }

                    if (allSourceFiles.Count >= MaxIndexedFiles)
                        break;
                }

                // Step 2.5: Detect path mismatch (project relocated from original build location)
                if (allSourceFiles.Count > 0)
                {
                    var sampleFiles = allSourceFiles
                        .Where(f => Path.IsPathRooted(f.filePath))
                        .Take(5)
                        .ToList();

                    int existCount = sampleFiles.Count(f => File.Exists(f.filePath));

                    if (existCount == 0 && sampleFiles.Count > 0)
                    {
                        // Source files don't exist at recorded paths — project was relocated
                        var samplePath = sampleFiles[0].filePath;
                        var expectedDir = Path.GetDirectoryName(samplePath);
                        index.PathMismatchInfo = $"项目文件中引用的源码路径不存在：\n" +
                            $"期望路径: {expectedDir}\n" +
                            $"当前工作目录: {workingDirectory}\n\n" +
                            $"请确保在原始编译目录中打开解决方案，或重新运行 CMake 生成以更新路径。";
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Path mismatch detected: source files not found at {expectedDir}");
                    }
                }

                // Step 3: Build indexes
                foreach (var (projName, filePath) in allSourceFiles)
                {
                    var normalized = NormalizePath(filePath);
                    if (string.IsNullOrEmpty(normalized) || !File.Exists(normalized))
                        continue;

                    // Add to AllIndexedPaths
                    index.AllIndexedPaths.Add(normalized);

                    // Add to FileNameIndex
                    var fileName = Path.GetFileName(normalized);
                    if (!index.FileNameIndex.TryGetValue(fileName, out var fileList))
                    {
                        fileList = new List<string>();
                        index.FileNameIndex[fileName] = fileList;
                    }
                    if (!fileList.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        fileList.Add(normalized);

                    // Add to ProjectFiles
                    if (!index.ProjectFiles.TryGetValue(projName, out var projFileList))
                    {
                        projFileList = new List<string>();
                        index.ProjectFiles[projName] = projFileList;
                    }
                    projFileList.Add(normalized);
                }

                // Step 4: Compute SourceRoots (directories outside working directory)
                var externalPaths = index.AllIndexedPaths
                    .Where(p => !p.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (externalPaths.Count > 0)
                {
                    var roots = ComputeSourceRoots(externalPaths);
                    index.SourceRoots.AddRange(roots);
                }

                // Step 5: Build RelativePathIndex (relative to each SourceRoot)
                foreach (var root in index.SourceRoots)
                {
                    var normalizedRoot = EnsureTrailingSlash(root);
                    foreach (var absPath in index.AllIndexedPaths)
                    {
                        if (absPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            var relPath = absPath.Substring(normalizedRoot.Length);
                            if (!index.RelativePathIndex.ContainsKey(relPath))
                            {
                                index.RelativePathIndex[relPath] = absPath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] SolutionSourceIndex build failed: {ex.Message}");
            }

            return index;
        }

        /// <summary>
        /// Resolve a requested path to an absolute file path using the index.
        /// Returns null if not found.
        /// </summary>
        /// <param name="requestedPath">A relative path, file name, or partial path.</param>
        /// <returns>The resolved absolute path, or null.</returns>
        public string ResolveFile(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return null;

            // Normalize separators
            var normalized = requestedPath.Replace('/', '\\').TrimEnd('\\');

            // 1. Check RelativePathIndex (exact relative path match)
            if (RelativePathIndex.TryGetValue(normalized, out var absPath))
                return absPath;

            // 2. Try with forward slashes replaced
            var altPath = requestedPath.Replace('\\', '/').TrimEnd('/');
            var altNorm = altPath.Replace('/', '\\');
            if (altNorm != normalized && RelativePathIndex.TryGetValue(altNorm, out absPath))
                return absPath;

            // 3. Check FileNameIndex (file name only)
            var fileName = Path.GetFileName(normalized);
            if (FileNameIndex.TryGetValue(fileName, out var matches))
            {
                // If the request includes directory components, try to match suffix
                if (normalized.Contains("\\"))
                {
                    var suffixMatch = matches
                        .FirstOrDefault(m => m.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));
                    if (suffixMatch != null)
                        return suffixMatch;
                }

                // Single match → return directly
                if (matches.Count == 1)
                    return matches[0];

                // Multiple matches → return null (caller should handle disambiguation)
                return null;
            }

            return null;
        }

        /// <summary>
        /// Get disambiguation candidates when a file name matches multiple paths.
        /// </summary>
        public List<string> GetCandidates(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return new List<string>();

            var fileName = Path.GetFileName(requestedPath.Replace('/', '\\'));
            if (FileNameIndex.TryGetValue(fileName, out var matches))
                return matches;

            return new List<string>();
        }

        /// <summary>
        /// Check if a given absolute path is in the index.
        /// </summary>
        public bool ContainsPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return false;
            return AllIndexedPaths.Contains(NormalizePath(absolutePath));
        }

        /// <summary>
        /// Check if a given absolute path is within any SourceRoot.
        /// </summary>
        public bool IsWithinSourceRoots(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || SourceRoots.Count == 0)
                return false;

            var normalized = NormalizePath(absolutePath);
            return SourceRoots.Any(root =>
                normalized.StartsWith(EnsureTrailingSlash(root), StringComparison.OrdinalIgnoreCase));
        }

        #region Parsing

        /// <summary>
        /// Parse a .sln file and extract project names and relative paths to .vcxproj/.csproj files.
        /// </summary>
        private static List<(string name, string path)> ParseSolutionFile(string slnPath)
        {
            var result = new List<(string, string)>();

            // Pattern: Project("{GUID}") = "Name", "RelativePath.vcxproj", "{GUID}"
            var projectPattern = new Regex(
                @"Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+\.(?:vcxproj|csproj))""\s*,",
                RegexOptions.IgnoreCase);

            var lines = File.ReadAllLines(slnPath);
            foreach (var line in lines)
            {
                var match = projectPattern.Match(line);
                if (match.Success)
                {
                    var projName = match.Groups[1].Value;
                    var projPath = match.Groups[2].Value;
                    result.Add((projName, projPath));
                }
            }

            return result;
        }

        /// <summary>
        /// Parse a .vcxproj file and extract all source file paths from
        /// ClCompile and ClInclude elements.
        /// </summary>
        private static List<string> ParseVcxproj(string vcxprojPath)
        {
            var files = new List<string>();
            var projDir = Path.GetDirectoryName(vcxprojPath);

            try
            {
                var doc = new XmlDocument();
                doc.Load(vcxprojPath);

                var nsMgr = new XmlNamespaceManager(doc.NameTable);
                nsMgr.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003");

                // Extract ClCompile Include paths
                var compileNodes = doc.SelectNodes("//ms:ClCompile[@Include]", nsMgr);
                if (compileNodes != null)
                {
                    foreach (XmlNode node in compileNodes)
                    {
                        var include = node.Attributes?["Include"]?.Value;
                        if (!string.IsNullOrEmpty(include))
                            AddResolvedPath(files, include, projDir);
                    }
                }

                // Extract ClInclude Include paths
                var includeNodes = doc.SelectNodes("//ms:ClInclude[@Include]", nsMgr);
                if (includeNodes != null)
                {
                    foreach (XmlNode node in includeNodes)
                    {
                        var include = node.Attributes?["Include"]?.Value;
                        if (!string.IsNullOrEmpty(include))
                            AddResolvedPath(files, include, projDir);
                    }
                }

                // Extract CustomBuild Include paths (for .xml, .py, CMakeLists.txt etc.)
                var customNodes = doc.SelectNodes("//ms:CustomBuild[@Include]", nsMgr);
                if (customNodes != null)
                {
                    foreach (XmlNode node in customNodes)
                    {
                        var include = node.Attributes?["Include"]?.Value;
                        if (!string.IsNullOrEmpty(include))
                            AddResolvedPath(files, include, projDir);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Failed to parse vcxproj {vcxprojPath}: {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// Parse a .csproj file and extract source file paths.
        /// For SDK-style projects, source files are implicitly included.
        /// For old-style projects, source files are listed via Compile Include.
        /// </summary>
        private static List<string> ParseCsproj(string csprojPath)
        {
            var files = new List<string>();
            var projDir = Path.GetDirectoryName(csprojPath);

            try
            {
                var doc = new XmlDocument();
                doc.Load(csprojPath);

                // Check if SDK-style (has Sdk attribute on Project element)
                var isSdkStyle = doc.DocumentElement?.GetAttribute("Sdk") != null
                    && !string.IsNullOrEmpty(doc.DocumentElement.GetAttribute("Sdk"));

                if (isSdkStyle)
                {
                    // SDK-style: all .cs files in project directory are implicitly included
                    if (Directory.Exists(projDir))
                    {
                        files.AddRange(
                            Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories)
                                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                                .Select(f => Path.GetFullPath(f)));
                    }
                }
                else
                {
                    // Old-style: extract Compile Include paths
                    var nsMgr = new XmlNamespaceManager(doc.NameTable);
                    nsMgr.AddNamespace("ms", "http://schemas.microsoft.com/developer/msbuild/2003");

                    var compileNodes = doc.SelectNodes("//ms:Compile[@Include]", nsMgr);
                    if (compileNodes != null)
                    {
                        foreach (XmlNode node in compileNodes)
                        {
                            var include = node.Attributes?["Include"]?.Value;
                            if (!string.IsNullOrEmpty(include))
                                AddResolvedPath(files, include, projDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Failed to parse csproj {csprojPath}: {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// Resolve an Include path (may be absolute or relative) and add to the list.
        /// Skips paths containing MSBuild variables like $(VarName).
        /// </summary>
        private static void AddResolvedPath(List<string> files, string includePath, string projectDir)
        {
            // Skip MSBuild variable references (cannot resolve statically)
            if (includePath.Contains("$(") || includePath.Contains("%("))
                return;

            try
            {
                string absPath;
                if (Path.IsPathRooted(includePath))
                {
                    absPath = Path.GetFullPath(includePath);
                }
                else
                {
                    absPath = Path.GetFullPath(Path.Combine(projectDir, includePath));
                }

                files.Add(absPath);
            }
            catch
            {
                // Invalid path characters etc. — skip silently
            }
        }

        #endregion

        #region Source Roots Computation

        /// <summary>
        /// Given a list of absolute file paths, compute the minimal set of root directories
        /// that contain all of them. Groups paths by their drive/volume and finds the longest
        /// common prefix for each group.
        /// </summary>
        private static List<string> ComputeSourceRoots(IEnumerable<string> absolutePaths)
        {
            var roots = new List<string>();

            // Group by drive letter (or UNC prefix)
            var groups = absolutePaths
                .Select(p => NormalizePath(p))
                .Where(p => !string.IsNullOrEmpty(p))
                .GroupBy(p => Path.GetPathRoot(p), StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var dirs = group
                    .Select(p => Path.GetDirectoryName(p))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (dirs.Count == 0)
                    continue;

                if (dirs.Count == 1)
                {
                    roots.Add(dirs[0]);
                    continue;
                }

                // Find longest common prefix at directory boundary
                var commonPrefix = GetLongestCommonDirectoryPrefix(dirs);
                if (!string.IsNullOrEmpty(commonPrefix))
                {
                    roots.Add(commonPrefix);
                }
            }

            return roots;
        }

        /// <summary>
        /// Find the longest common directory prefix among a set of directory paths.
        /// </summary>
        private static string GetLongestCommonDirectoryPrefix(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return null;
            if (paths.Count == 1) return paths[0];

            // Split all paths into segments
            var splitPaths = paths
                .Select(p => p.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                .ToList();

            var minLength = splitPaths.Min(s => s.Length);
            var commonSegments = new List<string>();

            for (int i = 0; i < minLength; i++)
            {
                var segment = splitPaths[0][i];
                if (splitPaths.All(s => string.Equals(s[i], segment, StringComparison.OrdinalIgnoreCase)))
                {
                    commonSegments.Add(segment);
                }
                else
                {
                    break;
                }
            }

            if (commonSegments.Count == 0)
                return null;

            // Reconstruct path
            var result = string.Join("\\", commonSegments);

            // Add drive separator if needed (e.g., "D:" → "D:\")
            if (result.Length == 2 && result[1] == ':')
                result += "\\";

            return result;
        }

        #endregion

        #region Helpers

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch
            {
                return path.TrimEnd('\\', '/');
            }
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.TrimEnd('\\', '/') + "\\";
        }

        #endregion
    }
}
