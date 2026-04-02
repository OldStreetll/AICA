using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
using AICA.Core.Agent;
using AICA.Core.Initialization;
using AICA.Core.Knowledge;
using AICA.Core.Rules;

namespace AICA.VSIX.Events
{
    /// <summary>
    /// 解决方案事件监听器
    /// 监听解决方案打开事件，并自动初始化规则目录
    /// </summary>
    public class SolutionEventListener : IVsSolutionEvents, IDisposable
    {
        private readonly RulesDirectoryInitializer _initializer;
        private readonly InitializationManager _initManager = new InitializationManager();
        private bool _disposed;

        /// <summary>
        /// Initialization manager for tracking startup progress.
        /// Exposed for AICAPackage → ChatToolWindowControl binding.
        /// </summary>
        public InitializationManager InitManager => _initManager;

        /// <summary>
        /// The solution directory path, set on solution open, cleared on close.
        /// </summary>
        public string SolutionPath { get; private set; }

        /// <summary>
        /// The project root path (git root), used by DocumentSaveListener for relative paths.
        /// Matches the root that ProjectIndexer.FindProjectRoot uses for indexing.
        /// Volatile for cross-thread visibility (UI thread writes, background reads).
        /// </summary>
        private volatile string _projectRootPath;
        public string ProjectRootPath
        {
            get => _projectRootPath;
            private set => _projectRootPath = value;
        }

        public SolutionEventListener(RulesDirectoryInitializer initializer = null)
        {
            _initializer = initializer ?? new RulesDirectoryInitializer();
            _disposed = false;
        }

        /// <summary>
        /// 解决方案打开后触发
        /// </summary>
        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            System.Diagnostics.Debug.WriteLine("[AICA] OnAfterOpenSolution event triggered");

            var solutionPath = GetSolutionPath();
            System.Diagnostics.Debug.WriteLine($"[AICA] Solution path from event: {solutionPath ?? "NULL"}");

            // 在后台线程中处理，不阻塞 UI
            _ = OnAfterOpenSolutionAsync(solutionPath);
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        /// <summary>
        /// 处理解决方案打开事件（异步）
        /// v2.11: Coordinated through InitializationManager for progress tracking.
        /// </summary>
        public async Task OnAfterOpenSolutionAsync(string solutionPath)
        {
            System.Diagnostics.Debug.WriteLine($"[AICA] OnAfterOpenSolutionAsync called with path: {solutionPath ?? "NULL"}");

            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Listener is disposed, returning");
                return;
            }

            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Solution path is null or empty, returning");
                return;
            }

            SolutionPath = solutionPath;
            _initManager.Start();

            try
            {
                var projectRoot = FindGitRoot(solutionPath) ?? solutionPath;
                ProjectRootPath = projectRoot;
                System.Diagnostics.Debug.WriteLine($"[AICA] Initializing rules directory for: {projectRoot} (sln dir: {solutionPath})");

                // Step 1: Rules directory initialization (synchronous, fast)
                _initManager.UpdateStep(InitStepId.RulesInit, InitStepStatus.Running);
                var result = await _initializer.InitializeAsync(projectRoot);
                if (result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] SUCCESS: Rules directory initialized at: {result.RulesPath}");
                    _initManager.UpdateStep(InitStepId.RulesInit, InitStepStatus.Completed, result.RulesPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] FAILED: {result.Error}");
                    _initManager.UpdateStep(InitStepId.RulesInit, InitStepStatus.Failed, result.Error);
                }

                // Steps 2 + 3+4: Run symbol indexing and GitNexus in parallel
                var indexingTask = RunSymbolIndexingAsync(solutionPath);
                var gitNexusTask = RunGitNexusAsync(solutionPath);

                // Wait for both parallel tasks (doesn't block UI — already on background thread)
                await Task.WhenAll(indexingTask, gitNexusTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Step 2: Symbol indexing with progress reporting.
        /// </summary>
        private async Task RunSymbolIndexingAsync(string solutionPath)
        {
            _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Running);
            try
            {
                await Task.Run(async () =>
                {
                    var indexer = new ProjectIndexer();
                    var index = await indexer.IndexDirectoryAsync(solutionPath);
                    ProjectKnowledgeStore.Instance.SetIndex(index);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Project indexed: {index.FileCount} files, " +
                        $"{index.Symbols.Count} symbols in {index.IndexDuration.TotalSeconds:F1}s");
                    _initManager.UpdateStep(
                        InitStepId.SymbolIndexing,
                        InitStepStatus.Completed,
                        $"{index.FileCount} files, {index.Symbols.Count} symbols ({index.IndexDuration.TotalSeconds:F1}s)");
                });
            }
            catch (OperationCanceledException)
            {
                _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Skipped, "Cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Indexing failed: {ex.Message}");
                _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Steps 3+4: GitNexus npm install + analyze with progress reporting.
        /// </summary>
        private async Task RunGitNexusAsync(string solutionPath)
        {
            _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Running);
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus: triggering index for {solutionPath}");
                await Task.Run(async () =>
                {
                    await GitNexusProcessManager.Instance.TriggerIndexWithProgressAsync(
                        solutionPath, _initManager, _initManager.Token);
                });
                System.Diagnostics.Debug.WriteLine("[AICA] GitNexus: index trigger completed");
            }
            catch (OperationCanceledException)
            {
                _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Skipped, "Cancelled");
                _initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus: index trigger failed (non-fatal): {ex.Message}");
                // Determine which step failed
                var steps = _initManager.GetSteps();
                var installStep = System.Linq.Enumerable.FirstOrDefault(steps, s => s.Id == InitStepId.GitNexusInstall);
                if (installStep?.Status == InitStepStatus.Running)
                {
                    _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Failed, ex.Message);
                    _initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Install failed");
                }
                else
                {
                    _initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Failed, ex.Message);
                }
            }
        }

        /// <summary>
        /// 获取解决方案路径
        /// </summary>
        private string GetSolutionPath()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Getting solution path...");

                var dte = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider
                    .GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;

                System.Diagnostics.Debug.WriteLine($"[AICA] DTE: {(dte != null ? "OK" : "NULL")}");
                System.Diagnostics.Debug.WriteLine($"[AICA] Solution: {(dte?.Solution != null ? "OK" : "NULL")}");
                System.Diagnostics.Debug.WriteLine($"[AICA] Solution.FullName: {dte?.Solution?.FullName ?? "NULL"}");

                if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    // 返回解决方案所在的目录
                    var path = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
                    System.Diagnostics.Debug.WriteLine($"[AICA] Solution path: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Error getting solution path: {ex.GetType().Name}: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("[AICA] Solution path is NULL");
            return null;
        }

        #region IVsSolutionEvents 实现

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            _initManager.Cancel();
            SolutionPath = null;
            ProjectRootPath = null;
            ProjectKnowledgeStore.Instance.Clear();
            System.Diagnostics.Debug.WriteLine("[AICA] Project knowledge index cleared (solution closed)");

            // Note: GitNexusProcessManager is NOT disposed here — it's a singleton that
            // must survive solution close/reopen cycles within the same VS session.
            // Final cleanup happens in AICAPackage.Dispose when VS exits.
            // The MCP process will idle until the next solution open triggers re-indexing.

            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeOpenSolution(string pszSolutionFilename)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        #endregion

        /// <summary>
        /// Walk up from a directory to find the nearest .git directory (repository root).
        /// </summary>
        private static string FindGitRoot(string startDir)
        {
            var dir = startDir;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, ".git")))
                    return dir;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}

