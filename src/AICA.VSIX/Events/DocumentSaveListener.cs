using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using AICA.Core.Knowledge;

namespace AICA.VSIX.Events
{
    /// <summary>
    /// Listens for document save events via IVsRunningDocTableEvents3.
    /// On save of a supported C/C++ file, triggers incremental re-indexing
    /// of that single file using tree-sitter, then updates the in-memory symbol store.
    /// </summary>
    public sealed class DocumentSaveListener : IVsRunningDocTableEvents3, IDisposable
    {
        private readonly IVsRunningDocumentTable _rdt;
        private readonly SolutionEventListener _solutionListener;
        private uint _cookie;
        private bool _disposed;

        public DocumentSaveListener(
            IVsRunningDocumentTable rdt,
            SolutionEventListener solutionListener)
        {
            _rdt = rdt ?? throw new ArgumentNullException(nameof(rdt));
            _solutionListener = solutionListener ?? throw new ArgumentNullException(nameof(solutionListener));
        }

        /// <summary>
        /// Start listening for document save events.
        /// Must be called from the UI thread.
        /// </summary>
        public void Advise()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _rdt.AdviseRunningDocTableEvents(this, out _cookie);
            System.Diagnostics.Debug.WriteLine("[AICA] DocumentSaveListener: advised");
        }

        /// <summary>
        /// Stop listening for document save events.
        /// </summary>
        public void Unadvise()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0)
            {
                _rdt.UnadviseRunningDocTableEvents(_cookie);
                _cookie = 0;
                System.Diagnostics.Debug.WriteLine("[AICA] DocumentSaveListener: unadvised");
            }
        }

        // --- IVsRunningDocTableEvents3 ---

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var solutionPath = _solutionListener.SolutionPath;
            if (string.IsNullOrEmpty(solutionPath))
                return VSConstants.S_OK;

            if (!ProjectKnowledgeStore.Instance.HasIndex)
                return VSConstants.S_OK;

            // Get the file path from the document cookie
            _rdt.GetDocumentInfo(
                docCookie,
                out uint flags,
                out uint readLocks,
                out uint editLocks,
                out string filePath,
                out IVsHierarchy hierarchy,
                out uint itemId,
                out IntPtr docData);

            if (string.IsNullOrEmpty(filePath))
                return VSConstants.S_OK;

            var ext = Path.GetExtension(filePath);
            if (!ProjectIndexer.IsSupportedExtension(ext))
                return VSConstants.S_OK;

            // Compute relative path (same logic as ProjectIndexer.GetRelativePath)
            var basePath = solutionPath.EndsWith("\\") || solutionPath.EndsWith("/")
                ? solutionPath
                : solutionPath + Path.DirectorySeparatorChar;

            string relativePath;
            if (filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                relativePath = filePath.Substring(basePath.Length).Replace('\\', '/');
            else
                return VSConstants.S_OK; // File is outside solution — skip

            // Fire-and-forget: re-index on background thread
            var absPath = filePath;
            var relPath = relativePath;
            _ = Task.Run(async () =>
            {
                try
                {
                    var indexer = new ProjectIndexer();
                    var symbols = await indexer.IndexFileAsync(absPath, relPath);
                    ProjectKnowledgeStore.Instance.UpdateFileSymbols(relPath, symbols);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Incremental index: {relPath} -> {symbols.Count} symbols");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Incremental index failed for {relPath}: {ex.Message}");
                }
            });

            return VSConstants.S_OK;
        }

        // --- Unused events (required by interface) ---

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType,
            uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            => VSConstants.S_OK;

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType,
            uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            => VSConstants.S_OK;

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
            => VSConstants.S_OK;

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
            => VSConstants.S_OK;

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
            => VSConstants.S_OK;

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs,
            IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld,
            IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
            => VSConstants.S_OK;

        public int OnBeforeSave(uint docCookie)
            => VSConstants.S_OK;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    Unadvise();
                }
                catch
                {
                    // Best-effort cleanup during disposal
                }
            }
        }
    }
}
