global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;
using AICA.Options;
using AICA.ToolWindows;

namespace AICA
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.AICAPackageString)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptionsPage), "AICA", "General", 0, 0, true, SupportsProfiles = true)]
    [ProvideOptionPage(typeof(OptionsProvider.SecurityOptionsPage), "AICA", "Security", 1, 1, true, SupportsProfiles = true)]
    [ProvideToolWindow(typeof(ChatToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    public sealed class AICAPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            await this.RegisterCommandsAsync();
            
            await VS.StatusBar.ShowMessageAsync("AICA - AI Coding Assistant loaded");
        }
    }

    public static class PackageGuids
    {
        public const string AICAPackageString = "D4E5F6A7-B8C9-0123-ABCD-456789ABCDEF";
        public static readonly Guid AICAPackage = new Guid(AICAPackageString);
        
        public const string CommandSetString = "E5F6A7B8-C9D0-1234-ABCD-56789ABCDEF0";
        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }
}
