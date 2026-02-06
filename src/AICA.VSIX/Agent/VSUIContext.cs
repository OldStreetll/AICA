using System;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AICA.Agent
{
    /// <summary>
    /// Visual Studio specific implementation of IUIContext
    /// </summary>
    public class VSUIContext : IUIContext
    {
        private readonly Action<string> _streamingContentUpdater;
        private readonly Func<string, string, CancellationToken, Task<bool>> _confirmationHandler;
        private readonly Func<string, string, string, CancellationToken, Task<bool>> _diffPreviewHandler;
        private IVsThreadedWaitDialog2 _waitDialog;

        public VSUIContext(
            Action<string> streamingContentUpdater = null,
            Func<string, string, CancellationToken, Task<bool>> confirmationHandler = null,
            Func<string, string, string, CancellationToken, Task<bool>> diffPreviewHandler = null)
        {
            _streamingContentUpdater = streamingContentUpdater;
            _confirmationHandler = confirmationHandler;
            _diffPreviewHandler = diffPreviewHandler;
        }

        public async Task ShowMessageAsync(string message, CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                "AICA",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        public Task UpdateStreamingContentAsync(string content, CancellationToken ct = default)
        {
            _streamingContentUpdater?.Invoke(content);
            return Task.CompletedTask;
        }

        public async Task ShowProgressAsync(string message, int? percentComplete = null, CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            try
            {
                if (_waitDialog == null)
                {
                    var dialogFactory = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
                    if (dialogFactory != null)
                    {
                        dialogFactory.CreateInstance(out _waitDialog);
                    }
                }

                if (_waitDialog != null)
                {
                    _waitDialog.StartWaitDialogWithPercentageProgress(
                        "AICA",
                        message,
                        null,
                        null,
                        null,
                        true,
                        0,
                        percentComplete ?? 0,
                        100);
                }
            }
            catch
            {
                // Ignore errors with wait dialog
            }
        }

        public async Task HideProgressAsync(CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            try
            {
                if (_waitDialog != null)
                {
                    int cancelled;
                    _waitDialog.EndWaitDialog(out cancelled);
                    _waitDialog = null;
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken ct = default)
        {
            if (_confirmationHandler != null)
            {
                return await _confirmationHandler(title, message, ct);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var result = VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                title,
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            return result == 6; // IDYES = 6
        }

        public async Task<bool> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            if (_diffPreviewHandler != null)
            {
                return await _diffPreviewHandler(filePath, originalContent, newContent, ct);
            }

            // Default: show confirmation dialog with summary
            var changesSummary = GetChangesSummary(originalContent, newContent);
            return await ShowConfirmationAsync(
                $"Confirm changes to {System.IO.Path.GetFileName(filePath)}",
                changesSummary,
                ct);
        }

        private string GetChangesSummary(string original, string modified)
        {
            var originalLines = (original ?? string.Empty).Split('\n').Length;
            var modifiedLines = (modified ?? string.Empty).Split('\n').Length;
            var diff = modifiedLines - originalLines;

            var diffText = diff > 0 ? $"+{diff}" : diff.ToString();
            return $"Lines: {originalLines} → {modifiedLines} ({diffText})\n\nApply these changes?";
        }
    }
}
