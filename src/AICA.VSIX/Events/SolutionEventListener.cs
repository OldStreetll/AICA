using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
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
                System.Diagnostics.Debug.WriteLine($"[AICA] Initializing rules directory for: {solutionPath}");

                // 初始化规则目录
                var result = await _initializer.InitializeAsync(solutionPath);

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

        #region IVsSolutionEvents 实现

        public int OnAfterCloseSolution(object pUnkReserved)
        {
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

