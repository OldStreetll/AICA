using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Abstraction for IDE document formatting.
    /// AICA.Core defines the interface; AICA.VSIX provides the implementation
    /// that calls DTE.ExecuteCommand("Edit.FormatDocument").
    ///
    /// Design note (v2.1 M3 / review R5):
    /// - FormatDocument requires the file to be open in the VS editor.
    /// - The implementation must use JoinableTaskFactory to marshal to the UI thread.
    /// - If no formatter is available for the file type, FormatAsync should return false
    ///   (not throw), so FormatStep can record format_changed=false silently.
    /// </summary>
    public interface IFormatService
    {
        /// <summary>
        /// Format the specified file using the IDE's built-in formatter.
        /// The file must already be open in the editor (caller ensures this via OpenFileInEditorAsync).
        /// Returns true if the formatter ran and changed the file content; false if
        /// no formatter is available, the file was already well-formatted, or formatting
        /// had no effect.
        /// Implementations must not throw on missing formatter — return false instead.
        /// </summary>
        Task<bool> FormatAsync(string filePath, CancellationToken ct = default);
    }
}
