using AICA.Core.Security;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading.Tasks;

namespace AICA.Commands
{
    [Command(PackageGuids.CommandSetString, PackageIds.ResetPermissionsCommand)]
    internal sealed class ResetPermissionsCommand : BaseCommand<ResetPermissionsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Use the live singleton so both file and in-memory state are cleared together.
            // Falls back to a disposable instance if feature was disabled or no session active.
            var store = PermissionDecisionStore.Current;
            if (store == null)
            {
                store = new PermissionDecisionStore();
                store.Load();
            }

            // Ask user: reset for current project only, or all projects?
            var choice = await VS.MessageBox.ShowAsync(
                "AICA: Reset Permission Decisions",
                "Reset persistent permission decisions?\n\n" +
                "Click 'Yes' to reset for the current project only.\n" +
                "Click 'No' to reset for ALL projects.\n" +
                "Click 'Cancel' to abort.",
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL);

            if (choice == VSConstants.MessageBoxResult.IDCANCEL)
                return;

            int removed;
            if (choice == VSConstants.MessageBoxResult.IDYES)
            {
                // Current project only — get solution path from DTE
                string projectPath = null;
                try
                {
                    var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider
                        .GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                    if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                        projectPath = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
                }
                catch { }

                if (string.IsNullOrEmpty(projectPath))
                {
                    await VS.MessageBox.ShowWarningAsync(
                        "AICA",
                        "No solution is currently open. Cannot determine project path.");
                    return;
                }

                removed = store.Reset(projectPath);
                await VS.StatusBar.ShowMessageAsync(
                    $"AICA: Reset {removed} permission decision(s) for current project.");
            }
            else
            {
                // All projects
                removed = store.Reset();
                await VS.StatusBar.ShowMessageAsync(
                    $"AICA: Reset {removed} permission decision(s) for all projects.");
            }
        }
    }
}
