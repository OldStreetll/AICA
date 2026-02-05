using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Interface for UI interactions during tool execution
    /// </summary>
    public interface IUIContext
    {
        /// <summary>
        /// Show a message to the user
        /// </summary>
        Task ShowMessageAsync(string message, CancellationToken ct = default);

        /// <summary>
        /// Update streaming content in the chat
        /// </summary>
        Task UpdateStreamingContentAsync(string content, CancellationToken ct = default);

        /// <summary>
        /// Show progress indicator
        /// </summary>
        Task ShowProgressAsync(string message, int? percentComplete = null, CancellationToken ct = default);

        /// <summary>
        /// Hide progress indicator
        /// </summary>
        Task HideProgressAsync(CancellationToken ct = default);

        /// <summary>
        /// Show confirmation dialog
        /// </summary>
        Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken ct = default);

        /// <summary>
        /// Show diff preview for file changes
        /// </summary>
        Task<bool> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default);
    }
}
