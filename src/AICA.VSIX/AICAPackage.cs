global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell.Interop;
using AICA.Core.Initialization;
using AICA.Options;
using AICA.ToolWindows;
using AICA.VSIX.Events;

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
        private static AICAPackage _instance;

        /// <summary>
        /// Get the InitializationManager from the current SolutionEventListener.
        /// Returns null if package not yet loaded (acceptable degradation —
        /// user can't interact with AICA before the package loads anyway).
        /// </summary>
        internal static InitializationManager CurrentInitManager =>
            _instance?._solutionEventListener?.InitManager;

        /// <summary>
        /// 解决方案事件监听器
        /// 用于自动创建 .aica-rules 目录
        /// </summary>
        private SolutionEventListener _solutionEventListener;

        /// <summary>
        /// 解决方案事件 Cookie
        /// 用于注销事件监听器
        /// </summary>
        private uint _solutionEventsCookie;

        private DocumentSaveListener _documentSaveListener;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _instance = this;

            await this.RegisterCommandsAsync();

            // 初始化规则目录监听器
            await InitializeRulesDirectoryListenerAsync(cancellationToken);

            await VS.StatusBar.ShowMessageAsync("AICA - AI Coding Assistant loaded");
        }

        /// <summary>
        /// 初始化规则目录监听器
        /// 当解决方案打开时自动创建 .aica-rules 目录
        /// </summary>
        private async Task InitializeRulesDirectoryListenerAsync(CancellationToken cancellationToken)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Starting rules directory listener initialization");

                // 创建事件监听器
                _solutionEventListener = new SolutionEventListener();
                System.Diagnostics.Debug.WriteLine("[AICA] SolutionEventListener created");

                // 获取解决方案服务
                var solutionService = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
                System.Diagnostics.Debug.WriteLine($"[AICA] SolutionService: {(solutionService != null ? "OK" : "NULL")}");

                if (solutionService != null)
                {
                    // 注册事件监听器
                    solutionService.AdviseSolutionEvents(_solutionEventListener, out _solutionEventsCookie);
                    System.Diagnostics.Debug.WriteLine("[AICA] Event listener registered successfully");

                    // 检查是否已有打开的解决方案
                    try
                    {
                        var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider
                            .GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;

                        // Subscribe to document save events for incremental indexing
                        if (dte != null)
                        {
                            _documentSaveListener = new DocumentSaveListener(dte, _solutionEventListener);
                            System.Diagnostics.Debug.WriteLine("[AICA] DocumentSaveListener initialized");
                        }

                        System.Diagnostics.Debug.WriteLine($"[AICA] Checking for already open solution...");
                        System.Diagnostics.Debug.WriteLine($"[AICA] DTE: {(dte != null ? "OK" : "NULL")}");
                        System.Diagnostics.Debug.WriteLine($"[AICA] Solution: {(dte?.Solution != null ? "OK" : "NULL")}");
                        System.Diagnostics.Debug.WriteLine($"[AICA] Solution.FullName: {dte?.Solution?.FullName ?? "NULL"}");

                        if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                        {
                            // 手动触发初始化
                            var solutionPath = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
                            System.Diagnostics.Debug.WriteLine($"[AICA] Found already open solution: {solutionPath}");
                            System.Diagnostics.Debug.WriteLine("[AICA] Manually triggering initialization for already open solution");

                            await _solutionEventListener.OnAfterOpenSolutionAsync(solutionPath);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[AICA] No solution currently open");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Error checking for already open solution: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] ERROR: SolutionService is null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AICA] StackTrace: {ex.StackTrace}");
                // 不中断插件初始化
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unadvise solution events (cleanup advisory cookie)
                try
                {
                    if (_solutionEventsCookie != 0)
                    {
                        var solutionService = GetService(typeof(SVsSolution)) as IVsSolution;
                        solutionService?.UnadviseSolutionEvents(_solutionEventsCookie);
                        _solutionEventsCookie = 0;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Error unadvising solution events: {ex.Message}");
                }

                // Dispose solution event listener
                try
                {
                    if (_solutionEventListener != null)
                    {
                        _solutionEventListener.Dispose();
                        _solutionEventListener = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Error disposing rules directory listener: {ex.Message}");
                }

                // Dispose document save listener
                try
                {
                    if (_documentSaveListener != null)
                    {
                        _documentSaveListener.Dispose();
                        _documentSaveListener = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Error disposing document save listener: {ex.Message}");
                }

                // Cancel any running agent before disposing GitNexus (prevents mid-execution disposal)
                try
                {
                    var chatWindow = FindToolWindow(typeof(ToolWindows.ChatToolWindow), 0, false) as ToolWindows.ChatToolWindow;
                    if (chatWindow != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[AICA] Package disposing: cancelling running agent...");
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await chatWindow.CancelRunningAgentAsync(3000);
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Error cancelling agent: {ex.Message}");
                }

                // Dispose GitNexus process manager (safe now — agent is stopped)
                try
                {
                    AICA.Core.Agent.GitNexusProcessManager.Instance.Dispose();
                    System.Diagnostics.Debug.WriteLine("[AICA] GitNexus: process manager disposed (package disposing)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Error disposing GitNexus: {ex.Message}");
                }
            }

            base.Dispose(disposing);
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
