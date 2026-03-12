using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
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
    }
}
