using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// A diagnostic issue reported by the IDE (e.g., IntelliSense error/warning).
    /// </summary>
    public class FileDiagnostic
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        /// <summary>"error", "warning", or "info"</summary>
        public string Severity { get; set; }
        public string Message { get; set; }
        public string Code { get; set; }
    }

    /// <summary>
    /// Context interface for file operations
    /// </summary>
    public interface IFileContext
    {
        /// <summary>
        /// Read file content
        /// </summary>
        Task<string> ReadFileAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Write file content
        /// </summary>
        Task WriteFileAsync(string path, string content, CancellationToken ct = default);

        /// <summary>
        /// Check if file exists
        /// </summary>
        Task<bool> FileExistsAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Check if a file path is accessible
        /// </summary>
        bool IsPathAccessible(string path);

        /// <summary>
        /// Show diff preview for file changes and ask user to confirm.
        /// Returns a result containing confirmation status and final content.
        /// </summary>
        Task<DiffPreviewResult> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default);

        /// <summary>
        /// Show diff view and let user apply changes.
        /// Returns a result indicating whether changes were applied.
        /// </summary>
        Task<DiffApplyResult> ShowDiffAndApplyAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default);

        /// <summary>
        /// Open a file in the editor
        /// </summary>
        Task OpenFileInEditorAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// v2.3: Get IDE diagnostics (errors/warnings) for a file.
        /// Polls the IDE's Error List until results stabilize or timeout.
        /// Returns empty list if no diagnostics or if IDE diagnostics are unavailable.
        /// </summary>
        Task<List<FileDiagnostic>> GetDiagnosticsAsync(string filePath, CancellationToken ct = default);
    }
}
