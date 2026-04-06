using AICA.Core.LLM;
using AICA.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Linq;
using System.Threading.Tasks;

namespace AICA.Commands
{
    [Command(PackageGuids.CommandSetString, PackageIds.SendCodeToAicaCommand)]
    internal sealed class SendCodeToAicaCommand : BaseCommand<SendCodeToAicaCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[AICA] SendCodeToAicaCommand: ExecuteAsync ENTERED");

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Diagnostics.Debug.WriteLine("[AICA] SendCodeToAicaCommand: on main thread");

                var docView = await VS.Documents.GetActiveDocumentViewAsync();
                System.Diagnostics.Debug.WriteLine($"[AICA] SendCodeToAicaCommand: docView={docView != null}, textView={docView?.TextView != null}");

                if (docView?.TextView == null)
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] SendCodeToAicaCommand: no active document");
                    await VS.MessageBox.ShowWarningAsync("AICA", "未找到活动文档。");
                    return;
                }

                var selection = docView.TextView.Selection.SelectedSpans.FirstOrDefault();
                var selectedText = selection.Snapshot != null ? selection.GetText() : string.Empty;
                System.Diagnostics.Debug.WriteLine($"[AICA] SendCodeToAicaCommand: selectedText length={selectedText?.Length ?? 0}");

                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] SendCodeToAicaCommand: no selection");
                    await VS.MessageBox.ShowWarningAsync("AICA", "请先选择需要发送的代码。");
                    return;
                }

                // Capture editor context
                var filePath = docView.FilePath ?? "unknown";
                var contentType = docView.TextView.TextDataModel.ContentType.DisplayName;
                var ext = docView.FilePath != null
                    ? System.IO.Path.GetExtension(docView.FilePath).ToLowerInvariant()
                    : "";

                var isCpp = contentType.Contains("C++") || contentType.Contains("C/C++")
                    || ext == ".cpp" || ext == ".h" || ext == ".c" || ext == ".cc" || ext == ".cxx"
                    || ext == ".hpp" || ext == ".hxx";
                var language = isCpp ? "cpp" : contentType.ToLowerInvariant();

                // Four-dimensional selection coordinates
                int startLine = 1, startColumn = 1, endLine = 1, endColumn = 1;
                if (selection.Snapshot != null)
                {
                    var startSnapshotLine = selection.Snapshot.GetLineFromPosition(selection.Start.Position);
                    startLine = startSnapshotLine.LineNumber + 1;
                    startColumn = selection.Start.Position - startSnapshotLine.Start.Position + 1;

                    var endSnapshotLine = selection.Snapshot.GetLineFromPosition(selection.End.Position);
                    endLine = endSnapshotLine.LineNumber + 1;
                    endColumn = selection.End.Position - endSnapshotLine.Start.Position + 1;
                }

                string projectName = null;
                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
                if (dte?.ActiveDocument?.ProjectItem?.ContainingProject != null)
                {
                    projectName = dte.ActiveDocument.ProjectItem.ContainingProject.Name;
                }

                System.Diagnostics.Debug.WriteLine($"[AICA] SendCodeToAicaCommand: file={filePath}, L{startLine}:C{startColumn}-L{endLine}:C{endColumn}, lang={language}, proj={projectName}");

                var codePart = new CodePart(selectedText, filePath, startLine, startColumn, endLine, endColumn, language, projectName);

                var window = await Package.FindToolWindowAsync(
                    typeof(ChatToolWindow),
                    0,
                    true,
                    Package.DisposalToken) as ChatToolWindow;

                System.Diagnostics.Debug.WriteLine($"[AICA] SendCodeToAicaCommand: window={window != null}");

                if (window?.Frame is IVsWindowFrame windowFrame)
                {
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
                }

                if (window != null)
                {
                    window.AttachCodePart(codePart);
                    System.Diagnostics.Debug.WriteLine("[AICA] SendCodeToAicaCommand: AttachCodePart called successfully");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] SendCodeToAicaCommand EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AICA] StackTrace: {ex.StackTrace}");
            }
        }
    }
}
