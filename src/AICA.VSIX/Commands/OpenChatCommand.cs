using AICA.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Threading.Tasks;

namespace AICA.Commands
{
    [Command(PackageGuids.CommandSetString, PackageIds.OpenChatCommand)]
    internal sealed class OpenChatCommand : BaseCommand<OpenChatCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await Package.FindToolWindowAsync(
                typeof(ChatToolWindow),
                0,
                true,
                Package.DisposalToken);

            if (window?.Frame is IVsWindowFrame windowFrame)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
        }
    }

    [Command(PackageGuids.CommandSetString, PackageIds.OpenChatWindowMenuCommand)]
    internal sealed class OpenChatWindowMenuCommand : BaseCommand<OpenChatWindowMenuCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await Package.FindToolWindowAsync(
                typeof(ChatToolWindow),
                0,
                true,
                Package.DisposalToken);

            if (window?.Frame is IVsWindowFrame windowFrame)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
        }
    }
}
