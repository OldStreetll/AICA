using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Workspace;

namespace AICA.Core.Tests.Agent.Mocks
{
    /// <summary>
    /// Mock IAgentContext with in-memory file system for testing.
    /// </summary>
    public class MockAgentContext : IAgentContext
    {
        private readonly ConcurrentDictionary<string, string> _files = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MockAgentContext(string workingDirectory = "/workspace")
        {
            WorkingDirectory = workingDirectory;
        }

        // --- IWorkspaceContext ---

        public string WorkingDirectory { get; }

        public IReadOnlyList<string> SourceRoots { get; set; } = new List<string>();

        public string PathMismatchWarning { get; set; }

        public Task<IEnumerable<string>> GetAccessibleFilesAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IEnumerable<string>>(_files.Keys);
        }

        public string ResolveFilePath(string requestedPath)
        {
            if (_files.ContainsKey(requestedPath))
                return requestedPath;
            return null;
        }

        public string ResolveDirectoryPath(string requestedPath)
        {
            return requestedPath;
        }

        public Dictionary<string, ProjectInfo> GetProjects()
        {
            return new Dictionary<string, ProjectInfo>();
        }

        // --- IFileContext ---

        public Task<string> ReadFileAsync(string path, CancellationToken ct = default)
        {
            if (_files.TryGetValue(path, out var content))
                return Task.FromResult(content);
            return Task.FromResult<string>(null);
        }

        public Task WriteFileAsync(string path, string content, CancellationToken ct = default)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            return Task.FromResult(_files.ContainsKey(path));
        }

        public bool IsPathAccessible(string path)
        {
            return true;
        }

        public Task<DiffPreviewResult> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            return Task.FromResult(DiffPreviewResult.Approved(newContent));
        }

        public Task<DiffApplyResult> ShowDiffAndApplyAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            _files[filePath] = newContent;
            return Task.FromResult(DiffApplyResult.Success());
        }

        public Task OpenFileInEditorAsync(string filePath, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        // --- ITaskContext ---

        public Task<bool> RequestConfirmationAsync(string operation, string details, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        // H6: Edited files tracking
        public IReadOnlyCollection<string> EditedFilesInSession => Array.Empty<string>();

        // v2.3: Diagnostics (returns empty by default; override via MockDiagnostics)
        public List<FileDiagnostic> MockDiagnostics { get; set; } = new List<FileDiagnostic>();

        public Task<List<FileDiagnostic>> GetDiagnosticsAsync(string filePath, CancellationToken ct = default)
        {
            var filtered = MockDiagnostics.FindAll(d =>
                string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(filtered);
        }

        // --- Test helpers ---

        /// <summary>
        /// Add a file to the in-memory file system.
        /// </summary>
        public MockAgentContext WithFile(string path, string content)
        {
            _files[path] = content;
            return this;
        }

        /// <summary>
        /// Get all files in the in-memory file system.
        /// </summary>
        public IReadOnlyDictionary<string, string> Files => _files;
    }
}
