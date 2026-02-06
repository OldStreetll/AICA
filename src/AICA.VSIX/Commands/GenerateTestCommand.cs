using AICA.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using System.Linq;
using System.Threading.Tasks;

namespace AICA.Commands
{
    [Command(PackageGuids.CommandSetString, PackageIds.GenerateTestCommand)]
    internal sealed class GenerateTestCommand : BaseCommand<GenerateTestCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                await VS.MessageBox.ShowWarningAsync("AICA", "No active document found.");
                return;
            }

            var selection = docView.TextView.Selection.SelectedSpans.FirstOrDefault();
            var selectedText = selection.Snapshot != null ? selection.GetText() : string.Empty;

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                await VS.MessageBox.ShowWarningAsync("AICA", "Please select a method or class to generate tests for.");
                return;
            }

            // Open chat window and send test generation request
            var window = await Package.FindToolWindowAsync(
                typeof(ChatToolWindow),
                0,
                true,
                Package.DisposalToken) as ChatToolWindow;

            if (window?.Frame is IVsWindowFrame windowFrame)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }

            if (window != null)
            {
                var contentType = docView.TextView.TextDataModel.ContentType.DisplayName;
                var fileName = docView.FilePath != null ? System.IO.Path.GetFileName(docView.FilePath) : "unknown";
                var prompt = $"Please generate comprehensive unit tests for the following {contentType} code from file `{fileName}` using xUnit. Include edge cases and use Arrange/Act/Assert pattern:\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
                
                await window.SendProgrammaticMessageAsync(prompt);
            }
        }
    }
}
