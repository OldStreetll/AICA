using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Security;
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

        public string WorkingDirectory { get; private set; }

        public TaskPlan CurrentPlan => _currentPlan;

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

            // Initialize SafetyGuard with security options
            InitializeSafetyGuard();
        }

        private void InitializeSafetyGuard()
        {
            try
            {
                var secOptions = SecurityOptions.Instance;
                _safetyGuard = new SafetyGuard(new SafetyGuardOptions
                {
                    WorkingDirectory = WorkingDirectory,
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
            var fullPath = GetFullPath(path);

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
            var fullPath = GetFullPath(path);
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
