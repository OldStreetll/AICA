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
                var ext = docView.FilePath != null ? System.IO.Path.GetExtension(docView.FilePath).ToLowerInvariant() : "";
                var isCpp = contentType.Contains("C++") || contentType.Contains("C/C++")
                    || ext == ".cpp" || ext == ".h" || ext == ".c" || ext == ".cc" || ext == ".cxx"
                    || ext == ".hpp" || ext == ".hxx";

                var codeBlockLang = isCpp ? "cpp" : contentType.ToLowerInvariant();
                var framework = isCpp
                    ? "Google Test 框架（使用 TEST_F、EXPECT_EQ、ASSERT_NE 等宏）"
                    : "xUnit 框架";
                var pattern = isCpp
                    ? "请包含边界情况，并用中文注释说明每个测试的目的"
                    : "请包含边界情况并使用 Arrange/Act/Assert 模式，并用中文注释说明每个测试的目的";

                var prompt = $"请用中文为以下来自文件 `{fileName}` 的 {contentType} 代码生成全面的单元测试，使用 {framework}。{pattern}：\n\n```{codeBlockLang}\n{selectedText}\n```";

                await window.SendProgrammaticMessageAsync(prompt);
            }
        }
    }
}
