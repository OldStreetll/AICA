using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Security;
using AICA.Core.Workspace;
using AICA.Options;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace AICA.Agent
{
    /// <summary>
    /// Visual Studio specific implementation of IAgentContext
    /// </summary>
    public class VSAgentContext : IAgentContext
    {
        private readonly DTE2 _dte;
        private TaskPlan _currentPlan;
        private readonly Func<string, string, CancellationToken, Task<bool>> _confirmationHandler;
        private SafetyGuard _safetyGuard;
        private SolutionSourceIndex _sourceIndex;
        private PathResolver _pathResolver;

        public string WorkingDirectory { get; private set; }

        public TaskPlan CurrentPlan => _currentPlan;

        private string _pathMismatchWarning;
        public string PathMismatchWarning => _pathMismatchWarning;

        public IReadOnlyList<string> SourceRoots =>
            _sourceIndex?.SourceRoots?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();

        public VSAgentContext(
            DTE2 dte,
            string workingDirectory = null,
            Func<string, string, CancellationToken, Task<bool>> confirmationHandler = null)
        {
            _dte = dte;
            _confirmationHandler = confirmationHandler;
            _currentPlan = new TaskPlan();

            // Determine working directory
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                WorkingDirectory = workingDirectory;
            }
            else
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    WorkingDirectory = GetSolutionDirectory();
                });
            }

            // Build source file index from solution
            InitializeSourceIndex();

            // Check if project is at its original build location
            CheckPathMismatch();

            // Initialize SafetyGuard with security options (after source index, so we can pass SourceRoots)
            InitializeSafetyGuard();
        }

        private void InitializeSourceIndex()
        {
            try
            {
                string slnPath = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (_dte?.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                        slnPath = _dte.Solution.FullName;
                });

                if (!string.IsNullOrEmpty(slnPath) && File.Exists(slnPath))
                {
                    _sourceIndex = SolutionSourceIndex.BuildFromSolution(slnPath, WorkingDirectory);
                    _pathResolver = new PathResolver(WorkingDirectory, _sourceIndex);

                    if (_sourceIndex.SourceRoots.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Source index built: {_sourceIndex.TotalFiles} files, " +
                            $"{_sourceIndex.SourceRoots.Count} source roots: " +
                            string.Join(", ", _sourceIndex.SourceRoots));
                    }
                }
                else
                {
                    _pathResolver = new PathResolver(WorkingDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Source index initialization failed: {ex.Message}");
                _pathResolver = new PathResolver(WorkingDirectory);
            }
        }

        /// <summary>
        /// Check if the current project is opened from a different location than where it was originally built.
        /// Uses CMakeCache.txt (for CMake projects) or source index detection.
        /// </summary>
        private void CheckPathMismatch()
        {
            try
            {
                // First check source index detection
                if (!string.IsNullOrEmpty(_sourceIndex?.PathMismatchInfo))
                {
                    _pathMismatchWarning = _sourceIndex.PathMismatchInfo;
                    return;
                }

                // Fallback: check CMakeCache.txt directly
                if (string.IsNullOrEmpty(WorkingDirectory))
                    return;

                var cmakeCachePath = Path.Combine(WorkingDirectory, "CMakeCache.txt");
                if (!File.Exists(cmakeCachePath))
                    return;

                string cmakeHomeDir = null;
                using (var reader = new StreamReader(cmakeCachePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("CMAKE_HOME_DIRECTORY:INTERNAL=", StringComparison.OrdinalIgnoreCase))
                        {
                            cmakeHomeDir = line.Substring("CMAKE_HOME_DIRECTORY:INTERNAL=".Length).Trim();
                            cmakeHomeDir = cmakeHomeDir.Replace('/', '\\');
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(cmakeHomeDir))
                    return;

                // Check if CMAKE_HOME_DIRECTORY is a sibling of the current WorkingDirectory
                // (i.e., they share the same parent). For a correctly located project:
                //   WorkingDirectory = D:\Project_Decode\FreeCAD0191\build\
                //   CMAKE_HOME_DIR  = D:\Project_Decode\FreeCAD0191\FreeCAD-0.19.1
                //   → same parent: D:\Project_Decode\FreeCAD0191\ → OK
                // For a relocated project:
                //   WorkingDirectory = D:\Project\AIConsProject\FreeCAD0191\build\
                //   CMAKE_HOME_DIR  = D:\Project_Decode\FreeCAD0191\FreeCAD-0.19.1
                //   → different parents → WARNING
                var workingParent = Path.GetDirectoryName(WorkingDirectory.TrimEnd('\\', '/'));
                var cmakeParent = Path.GetDirectoryName(cmakeHomeDir.TrimEnd('\\', '/'));

                bool isUnderSameRoot = false;
                if (!string.IsNullOrEmpty(workingParent) && !string.IsNullOrEmpty(cmakeParent))
                {
                    var wpNorm = workingParent.TrimEnd('\\', '/') + "\\";
                    var cpNorm = cmakeParent.TrimEnd('\\', '/') + "\\";
                    // Check if they share a common parent (one contains the other, or they're equal)
                    isUnderSameRoot =
                        wpNorm.Equals(cpNorm, StringComparison.OrdinalIgnoreCase) ||
                        cmakeHomeDir.StartsWith(wpNorm, StringComparison.OrdinalIgnoreCase) ||
                        WorkingDirectory.StartsWith(cpNorm, StringComparison.OrdinalIgnoreCase);
                }

                if (!isUnderSameRoot)
                {
                    _pathMismatchWarning =
                        $"\u5f53\u524d\u9879\u76ee\u4e0d\u5728\u539f\u59cb\u7f16\u8bd1\u76ee\u5f55\u4e2d\u6253\u5f00\uff1a\n" +
                        $"\u539f\u59cb\u6e90\u7801\u76ee\u5f55: {cmakeHomeDir}\n" +
                        $"\u5f53\u524d\u5de5\u4f5c\u76ee\u5f55: {WorkingDirectory}\n\n" +
                        $"AICA \u53ef\u80fd\u65e0\u6cd5\u6b63\u786e\u89e3\u6790\u6e90\u7801\u6587\u4ef6\u8def\u5f84\u3002\n" +
                        $"\u8bf7\u5728\u539f\u59cb\u7f16\u8bd1\u76ee\u5f55\u4e2d\u6253\u5f00\u89e3\u51b3\u65b9\u6848\uff0c\u6216\u91cd\u65b0\u8fd0\u884c CMake \u751f\u6210\u4ee5\u66f4\u65b0\u8def\u5f84\u3002";
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Path mismatch: CMAKE_HOME_DIRECTORY={cmakeHomeDir}, WorkingDirectory={WorkingDirectory}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] CheckPathMismatch failed: {ex.Message}");
            }
        }

        private void InitializeSafetyGuard()
        {
            try
            {
                var secOptions = SecurityOptions.Instance;
                _safetyGuard = new SafetyGuard(new SafetyGuardOptions
                {
                    WorkingDirectory = WorkingDirectory,
                    SourceRoots = _sourceIndex?.SourceRoots?.ToArray(),
                    ProtectedPaths = secOptions.ProtectedPaths?
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim()).ToArray(),
                    CommandWhitelist = secOptions.CommandWhitelist?
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim()).ToArray(),
                    CommandBlacklist = secOptions.CommandBlacklist?
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim()).ToArray()
                });
            }
            catch
            {
                // Fallback with defaults if options unavailable
                _safetyGuard = new SafetyGuard(new SafetyGuardOptions
                {
                    WorkingDirectory = WorkingDirectory
                });
            }
        }

        private string GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte?.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
            {
                return Path.GetDirectoryName(_dte.Solution.FullName);
            }

            // Fallback to active document's directory
            if (_dte?.ActiveDocument != null && !string.IsNullOrEmpty(_dte.ActiveDocument.FullName))
            {
                return Path.GetDirectoryName(_dte.ActiveDocument.FullName);
            }

            return Environment.CurrentDirectory;
        }

        public async Task<IEnumerable<string>> GetAccessibleFilesAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
                    return Enumerable.Empty<string>();

                try
                {
                    return Directory.EnumerateFiles(WorkingDirectory, "*.*", SearchOption.AllDirectories)
                        .Where(f => !IsExcludedPath(f))
                        .Select(f => GetRelativePath(WorkingDirectory, f))
                        .Take(1000); // Limit for performance
                }
                catch
                {
                    return Enumerable.Empty<string>();
                }
            }, ct);
        }

        private bool IsExcludedPath(string path)
        {
            var excludedPatterns = new[]
            {
                @"\.git\", @"\.vs\", @"\bin\", @"\obj\",
                @"\node_modules\", @"\packages\", @"\.nuget\"
            };

            return excludedPatterns.Any(p => path.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool IsPathAccessible(string path)
        {
            // Treat empty, ".", "./", "/" as working directory root
            if (string.IsNullOrEmpty(path) || path == "." || path == "./" || path == "/" || path == "\\")
            {
                return !string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory);
            }

            // Use SafetyGuard for path access checks
            var result = _safetyGuard.CheckPathAccess(path);
            return result.IsAllowed;
        }

        /// <summary>
        /// Check command safety level via SafetyGuard
        /// </summary>
        public CommandCheckResult CheckCommandSafety(string command)
        {
            return _safetyGuard.CheckCommand(command);
        }

        public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
        {
            // Try PathResolver first (covers source roots), fallback to GetFullPath
            var fullPath = ResolveFilePath(path) ?? GetFullPath(path);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {path}", fullPath);

            using (var reader = new StreamReader(fullPath))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(path);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var writer = new StreamWriter(fullPath, false))
            {
                await writer.WriteAsync(content);
            }
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            var fullPath = ResolveFilePath(path) ?? GetFullPath(path);
            return Task.FromResult(File.Exists(fullPath));
        }

        public void UpdatePlan(TaskPlan plan)
        {
            _currentPlan = plan ?? new TaskPlan();
        }

        public async Task<bool> RequestConfirmationAsync(string operation, string details, CancellationToken ct = default)
        {
            if (_confirmationHandler != null)
            {
                return await _confirmationHandler(operation, details, ct);
            }

            // Default: auto-approve read operations, require confirmation for writes
            var readOnlyOps = new[] { "read_file", "list_dir", "search" };
            return readOnlyOps.Any(op => operation.IndexOf(op, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public async Task<bool> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                // Create temp files for diff comparison
                var ext = Path.GetExtension(filePath);
                var tempDir = Path.Combine(Path.GetTempPath(), "AICA_Diff");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                var originalFile = Path.Combine(tempDir, $"original{ext}");
                var modifiedFile = Path.Combine(tempDir, $"modified{ext}");

                File.WriteAllText(originalFile, originalContent ?? "");
                File.WriteAllText(modifiedFile, newContent ?? "");

                // Try to use VS built-in diff viewer
                var diffSvc = await AsyncServiceProvider.GlobalProvider
                    .GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsDifferenceService))
                    as Microsoft.VisualStudio.Shell.Interop.IVsDifferenceService;

                if (diffSvc != null)
                {
                    var fileName = Path.GetFileName(filePath);
                    var frame = diffSvc.OpenComparisonWindow2(
                        originalFile,
                        modifiedFile,
                        $"AICA Diff: {fileName}",   // caption
                        $"Review changes to {fileName}", // tooltip
                        $"Original: {fileName}",     // leftLabel
                        $"Modified: {fileName}",     // rightLabel
                        $"Changes to {fileName}",    // inlineLabel
                        null,                        // roles
                        0);                          // grfDiffOptions

                    // Ask user to confirm after viewing the diff
                    var result = Microsoft.VisualStudio.Shell.VsShellUtilities.ShowMessageBox(
                        ServiceProvider.GlobalProvider,
                        $"Apply the changes shown in the diff to {fileName}?",
                        "AICA - Confirm Changes",
                        Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY,
                        Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                        Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    // Close diff window after decision
                    try { frame?.CloseFrame((uint)Microsoft.VisualStudio.Shell.Interop.__FRAMECLOSE.FRAMECLOSE_NoSave); }
                    catch { }

                    // Clean up temp files
                    try { File.Delete(originalFile); File.Delete(modifiedFile); } catch { }

                    return result == 6; // IDYES
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Diff preview failed: {ex.Message}");
            }

            // Fallback: simple confirmation dialog
            return await RequestConfirmationAsync(
                "Edit File",
                $"Apply changes to {Path.GetFileName(filePath)}?\n\n" +
                $"Original: {(originalContent?.Split('\n').Length ?? 0)} lines\n" +
                $"Modified: {(newContent?.Split('\n').Length ?? 0)} lines",
                ct);
        }

        public string ResolveFilePath(string requestedPath)
        {
            return _pathResolver?.ResolveFile(requestedPath);
        }

        public string ResolveDirectoryPath(string requestedPath)
        {
            return _pathResolver?.ResolveDirectory(requestedPath);
        }

        private string GetFullPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "." || path == "./" || path == "/" || path == "\\")
                return WorkingDirectory ?? Environment.CurrentDirectory;

            // Sanitize: remove control characters and illegal path chars
            path = SanitizePath(path);

            if (string.IsNullOrEmpty(path))
                return WorkingDirectory ?? Environment.CurrentDirectory;

            if (Path.IsPathRooted(path))
                return path;

            return Path.Combine(WorkingDirectory ?? Environment.CurrentDirectory, path);
        }

        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Remove control characters
            var sb = new System.Text.StringBuilder(path.Length);
            foreach (var c in path)
            {
                if (c >= 0x20 && c != 0x7F) // skip control chars
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

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath))
                return fullPath;

            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);

            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
