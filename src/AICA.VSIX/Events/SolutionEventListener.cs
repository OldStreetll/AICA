using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
using AICA.Core.Agent;
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
        private bool _disposed;

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
        /// </summary>
        public async Task OnAfterOpenSolutionAsync(string solutionPath)
        {
            System.Diagnostics.Debug.WriteLine($"[AICA] OnAfterOpenSolutionAsync called with path: {solutionPath ?? "NULL"}");

            // 检查是否已释放
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Listener is disposed, returning");
                return;
            }

            // 验证路径
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                System.Diagnostics.Debug.WriteLine("[AICA] Solution path is null or empty, returning");
                return;
            }

            try
            {
                // Resolve git root for .aica-rules directory (colocate with .git)
                var projectRoot = FindGitRoot(solutionPath) ?? solutionPath;
                System.Diagnostics.Debug.WriteLine($"[AICA] Initializing rules directory for: {projectRoot} (sln dir: {solutionPath})");

                // 初始化规则目录（在项目根目录，与 .git 同级）
                var result = await _initializer.InitializeAsync(projectRoot);

                if (result.Success)
                {
                    // 记录成功信息
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] SUCCESS: Rules directory initialized at: {result.RulesPath}");
                }
                else
                {
                    // 记录错误信息（不中断插件）
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] FAILED: {result.Error}");
                }

                // v2.8: Try CodeModel first (accurate), fallback to regex
                _ = IndexProjectAsync(solutionPath);

                // 并行触发 GitNexus 索引（fire-and-forget，不阻塞其他初始化）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus: triggering index for {solutionPath}");
                        await GitNexusProcessManager.Instance.TriggerIndexAsync(solutionPath, CancellationToken.None);
                        System.Diagnostics.Debug.WriteLine("[AICA] GitNexus: index trigger completed");
                    }
                    catch (Exception gnEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] GitNexus: index trigger failed (non-fatal): {gnEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                // 捕获所有异常，防止破坏插件
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] StackTrace: {ex.StackTrace}");
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

        /// <summary>
        /// v2.8: Regex indexing on background thread.
        /// CodeModel is reserved for on-demand single-file parsing only (not bulk indexing)
        /// because DTE API requires UI thread and blocks VS on large projects.
        /// </summary>
        private async Task IndexProjectAsync(string solutionPath)
        {
            await Task.Run(async () =>
            {
                try
                {
                    var indexer = new ProjectIndexer();
                    var index = await indexer.IndexDirectoryAsync(solutionPath);
                    ProjectKnowledgeStore.Instance.SetIndex(index);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Project indexed: {index.FileCount} files, " +
                        $"{index.Symbols.Count} symbols in {index.IndexDuration.TotalSeconds:F1}s");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Indexing failed: {ex.Message}");
                }
            });
        }

        #region IVsSolutionEvents 实现

        public int OnAfterCloseSolution(object pUnkReserved)
        {
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

