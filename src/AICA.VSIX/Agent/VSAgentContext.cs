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
        private AutoApproveManager _autoApproveManager;
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
                        .Select(p => p.Trim()).ToArray(),
                    AicaIgnorePath = secOptions.RespectAicaIgnore
                        ? null  // null = auto-detect .aicaignore in working directory
                        : ""    // empty = skip loading (no ignore patterns)
                });

                // Initialize AutoApproveManager
                _autoApproveManager = new AutoApproveManager(new AutoApproveOptions
                {
                    AutoApproveFileRead = secOptions.AutoApproveReadOperations,
                    AutoApproveFileCreate = secOptions.AutoApproveFileCreation,
                    AutoApproveFileEdit = secOptions.AutoApproveFileEdits,
                    AutoApproveFileDelete = false, // Always require confirmation for deletion
                    AutoApproveSafeCommands = secOptions.AutoApproveSafeCommands
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

        private void RefreshAutoApproveSettings()
        {
            try
            {
                var secOptions = SecurityOptions.Instance;
                _autoApproveManager = new AutoApproveManager(new AutoApproveOptions
                {
                    AutoApproveFileRead = secOptions.AutoApproveReadOperations,
                    AutoApproveFileCreate = secOptions.AutoApproveFileCreation,
                    AutoApproveFileEdit = secOptions.AutoApproveFileEdits,
                    AutoApproveFileDelete = false,
                    AutoApproveSafeCommands = secOptions.AutoApproveSafeCommands
                });
            }
            catch { /* keep existing _autoApproveManager on failure */ }
        }

        private string GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte?.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
            {
                var slnDir = Path.GetDirectoryName(_dte.Solution.FullName);

                // Try to find the project root by walking up from the solution directory.
                // In VS 2022, solutions are often nested (e.g., src/App.sln) while the
                // project root (containing .git, README, etc.) is a parent directory.
                // Using the project root ensures user-relative paths resolve correctly.
                var projectRoot = FindProjectRoot(slnDir);
                if (projectRoot != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Project root detected: {projectRoot} (sln dir: {slnDir})");
                    return projectRoot;
                }

                return slnDir;
            }

            // Fallback to active document's directory
            if (_dte?.ActiveDocument != null && !string.IsNullOrEmpty(_dte.ActiveDocument.FullName))
            {
                return Path.GetDirectoryName(_dte.ActiveDocument.FullName);
            }

            return Environment.CurrentDirectory;
        }

        /// <summary>
        /// Walk up from the solution directory to find the project root.
        /// Uses strong markers (.git, .svn, .hg) which are trusted at any depth,
        /// and weak markers (.gitignore, README.md, etc.) which are only trusted
        /// at depth 0 (sln dir) or depth 1 to prevent accidentally adopting a
        /// grandparent directory that doesn't own this solution.
        /// Returns null if no project root is found within a reasonable depth.
        /// </summary>
        private static string FindProjectRoot(string startDir)
        {
            if (string.IsNullOrEmpty(startDir))
                return null;

            // Strong VCS markers — authoritative, trusted at any depth
            var strongMarkers = new[] { ".git", ".svn", ".hg" };
            // Weak markers — only trusted at depth 0 (sln dir itself) or depth 1
            var weakMarkers = new[] { ".gitignore", ".editorconfig", "README.md", "README.rst", "LICENSE", "LICENSE.md" };

            // Check the solution directory itself (depth 0)
            if (HasStrongMarkers(startDir, strongMarkers) || HasWeakMarkers(startDir, weakMarkers))
                return startDir;

            // Walk up at most 3 levels
            var current = startDir;
            for (int depth = 1; depth <= 3; depth++)
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                    break;

                // Strong markers are trusted at any depth
                if (HasStrongMarkers(parent, strongMarkers))
                    return parent;

                // Weak markers only trusted at depth 1 (immediate parent of sln dir)
                if (depth == 1 && HasWeakMarkers(parent, weakMarkers))
                    return parent;

                current = parent;
            }

            // No project root found — use the solution directory as-is
            return null;
        }

        private static bool HasStrongMarkers(string dir, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (Directory.Exists(Path.Combine(dir, marker)))
                    return true;
            }
            return false;
        }

        private static bool HasWeakMarkers(string dir, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (File.Exists(Path.Combine(dir, marker)))
                    return true;
            }
            return false;
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
            // Use PathResolver for consistent path resolution across all tools.
            // For new files, ResolveFile returns null (file doesn't exist yet),
            // so we resolve the parent directory and append the filename.
            var fullPath = ResolveWritePath(path);

            // Re-validate the resolved path against SafetyGuard.
            // The caller may have validated the raw input path, but after resolution
            // (e.g., through source roots) the actual path could differ.
            if (_safetyGuard != null)
            {
                var check = _safetyGuard.CheckPathAccess(fullPath);
                if (!check.IsAllowed)
                {
                    throw new UnauthorizedAccessException(
                        $"Write blocked by SafetyGuard: {fullPath} — {check.Reason}");
                }
            }

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

        /// <summary>
        /// Resolve a path for write operations. Since the target file may not exist yet,
        /// we resolve the parent directory via PathResolver and append the filename.
        /// Falls back to GetFullPath if the parent directory cannot be resolved.
        /// </summary>
        private string ResolveWritePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return GetFullPath(path);

            // If absolute, use as-is
            if (Path.IsPathRooted(path))
                return path;

            // Try to resolve the parent directory
            var parentDir = Path.GetDirectoryName(path);
            var fileName = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parentDir))
            {
                var resolvedDir = ResolveDirectoryPath(parentDir);
                if (resolvedDir != null)
                    return Path.Combine(resolvedDir, fileName);
            }

            // Fallback to simple combine
            return GetFullPath(path);
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
            // Refresh auto-approve settings from current options (ensures settings changes apply immediately)
            RefreshAutoApproveSettings();

            // Check if operation should be auto-approved
            if (_autoApproveManager != null && _autoApproveManager.ShouldAutoApprove(operation, details))
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Auto-approved operation: {operation}");
                return true;
            }

            if (_confirmationHandler != null)
            {
                return await _confirmationHandler(operation, details, ct);
            }

            // Default: auto-approve read operations, require confirmation for writes
            var readOnlyOps = new[] { "read_file", "list_dir", "search" };
            return readOnlyOps.Any(op => operation.IndexOf(op, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public async Task<DiffPreviewResult> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            System.Diagnostics.Debug.WriteLine($"[AICA] ShowDiffPreviewAsync called for: {filePath}");

            // Check if edit operation should be auto-approved
            if (_autoApproveManager != null && _autoApproveManager.ShouldAutoApprove("edit", filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Auto-approved edit operation: {filePath}");
                return DiffPreviewResult.Approved(newContent);
            }

            System.Diagnostics.Debug.WriteLine($"[AICA] Not auto-approved, showing DiffEditorDialog");

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                System.Diagnostics.Debug.WriteLine($"[AICA] Creating DiffEditorDialog");

                // Use custom diff editor dialog
                var dialog = new AICA.VSIX.Dialogs.DiffEditorDialog(filePath, originalContent, newContent);

                System.Diagnostics.Debug.WriteLine($"[AICA] Showing DiffEditorDialog");
                var result = dialog.ShowDialog();

                System.Diagnostics.Debug.WriteLine($"[AICA] DiffEditorDialog result: {result}");

                if (result == true)
                {
                    // User clicked "Apply Changes"
                    // Return the edited content (which may have been modified by the user)
                    System.Diagnostics.Debug.WriteLine($"[AICA] Returning edited content");
                    return DiffPreviewResult.Approved(dialog.ModifiedContent);
                }
                else
                {
                    // User clicked "Cancel"
                    return DiffPreviewResult.Cancelled();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Diff editor dialog failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AICA] Stack trace: {ex.StackTrace}");

                // Fallback: simple confirmation dialog
                var confirmed = await RequestConfirmationAsync(
                    "Edit File",
                    $"Apply changes to {Path.GetFileName(filePath)}?\n\n" +
                    $"Original: {(originalContent?.Split('\n').Length ?? 0)} lines\n" +
                    $"Modified: {(newContent?.Split('\n').Length ?? 0)} lines",
                    ct);

                return confirmed ? DiffPreviewResult.Approved(newContent) : DiffPreviewResult.Cancelled();
            }
        }

        public string ResolveFilePath(string requestedPath)
        {
            return _pathResolver?.ResolveFile(requestedPath);
        }

        public string ResolveDirectoryPath(string requestedPath)
        {
            return _pathResolver?.ResolveDirectory(requestedPath);
        }

        public Dictionary<string, ProjectInfo> GetProjects()
        {
            return _sourceIndex?.Projects ?? new Dictionary<string, ProjectInfo>();
        }

        public async Task OpenFileInEditorAsync(string filePath, CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            try
            {
                var fullPath = ResolveFilePath(filePath) ?? GetFullPath(filePath);

                if (!File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] OpenFileInEditorAsync: File not found: {fullPath}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[AICA] Opening file in editor: {fullPath}");

                // Open the file in VS editor
                if (_dte?.ItemOperations != null)
                {
                    _dte.ItemOperations.OpenFile(fullPath, EnvDTE.Constants.vsViewKindTextView);
                    System.Diagnostics.Debug.WriteLine($"[AICA] File opened successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] OpenFileInEditorAsync error: {ex.Message}");
            }
        }

        public async Task<DiffApplyResult> ShowDiffAndApplyAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            try
            {
                var fullPath = GetFullPath(filePath);
                System.Diagnostics.Debug.WriteLine($"[AICA] ShowDiffAndApplyAsync called for: {fullPath}");

                // Create a temporary file for the new content
                var tempFile = Path.Combine(Path.GetTempPath(), $"{Path.GetFileName(fullPath)}.new");
                File.WriteAllText(tempFile, newContent);
                System.Diagnostics.Debug.WriteLine($"[AICA] Created temp file: {tempFile}");

                // Save original content to a backup file
                var backupFile = fullPath + ".backup";
                File.WriteAllText(backupFile, originalContent);

                // Use VS diff command to show the comparison
                if (_dte != null)
                {
                    // Use Tools.DiffFiles command to open diff view
                    _dte.ExecuteCommand("Tools.DiffFiles", $"\"{fullPath}\" \"{tempFile}\"");
                    System.Diagnostics.Debug.WriteLine($"[AICA] Diff view opened");
                }

                // Show status bar message (non-blocking)
                var statusBar = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar))
                    as Microsoft.VisualStudio.Shell.Interop.IVsStatusbar;

                if (statusBar != null)
                {
                    statusBar.SetText($"Review changes in diff view. Click 'Apply Changes' when ready.");
                    System.Diagnostics.Debug.WriteLine($"[AICA] Status bar message shown");
                }

                // Wait for user to review and confirm using non-modal dialog
                await Task.Delay(2000, ct); // Give user time to see the diff

                // Use non-modal confirmation dialog
                bool confirmed = false;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                var dialog = new AICA.VSIX.Dialogs.NonModalConfirmDialog(
                    $"文件对比视图已打开：{Path.GetFileName(fullPath)}\n\n" +
                    $"操作步骤：\n" +
                    $"1. 在右侧的diff视图中查看AI建议的修改\n" +
                    $"2. 您可以直接在右侧面板中编辑内容（添加、删除或修改）\n" +
                    $"3. 编辑完成后，按 Ctrl+S 保存右侧面板的内容\n" +
                    $"4. 点击下方的\"是\"按钮，将右侧面板的内容应用到原文件\n" +
                    $"5. 如果不想应用修改，点击\"否\"按钮取消\n\n" +
                    $"注意：\n" +
                    $"- 此窗口不会阻塞VS操作，您可以自由编辑\n" +
                    $"- 必须先保存右侧面板（Ctrl+S），再点击\"是\"按钮\n" +
                    $"- 点击\"是\"后，右侧面板的内容将覆盖原文件");

                confirmed = await dialog.ShowDialogAsync();
                System.Diagnostics.Debug.WriteLine($"[AICA] User confirmation: {confirmed}");

                if (!confirmed)
                {
                    // Clean up temp files
                    try { File.Delete(tempFile); } catch { }
                    try { File.Delete(backupFile); } catch { }
                    System.Diagnostics.Debug.WriteLine($"[AICA] User cancelled");
                    return DiffApplyResult.Cancelled();
                }

                // Read the content from the temp file (user may have edited it in the diff view)
                var finalContent = File.ReadAllText(tempFile);

                // Apply the changes to the original file
                File.WriteAllText(fullPath, finalContent);
                System.Diagnostics.Debug.WriteLine($"[AICA] Changes applied to file");

                // Clean up temp files
                try { File.Delete(tempFile); } catch { }
                try { File.Delete(backupFile); } catch { }

                System.Diagnostics.Debug.WriteLine($"[AICA] Changes applied successfully");
                return DiffApplyResult.Success();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] ShowDiffAndApplyAsync error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AICA] Stack trace: {ex.StackTrace}");
                return DiffApplyResult.Cancelled();
            }
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
