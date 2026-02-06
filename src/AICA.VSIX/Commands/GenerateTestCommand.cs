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
                await VS.MessageBox.ShowWarningAsync("AICA", "未找到活动文档。");
                return;
            }

            var selection = docView.TextView.Selection.SelectedSpans.FirstOrDefault();
            var selectedText = selection.Snapshot != null ? selection.GetText() : string.Empty;

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                await VS.MessageBox.ShowWarningAsync("AICA", "请先选择需要生成测试的方法或类。");
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
                var prompt = $"请用中文为以下来自文件 `{fileName}` 的 {contentType} 代码生成全面的单元测试，使用 xUnit 框架。请包含边界情况并使用 Arrange/Act/Assert 模式，并用中文注释说明每个测试的目的：\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
                
                await window.SendProgrammaticMessageAsync(prompt);
            }
        }
    }
}
