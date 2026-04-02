using System;
using System.IO;
using System.Threading.Tasks;
using AICA.Core.Knowledge;

namespace AICA.VSIX.Events
{
    /// <summary>
    /// Listens for document save events via DTE.Events.DocumentEvents.
    /// On save of a supported C/C++ file, triggers incremental re-indexing
    /// of that single file using tree-sitter, then updates the in-memory symbol store.
    /// </summary>
    public sealed class DocumentSaveListener : IDisposable
    {
        private EnvDTE.DocumentEvents _documentEvents;
        private readonly SolutionEventListener _solutionListener;
        private bool _disposed;

        public DocumentSaveListener(
            EnvDTE.DTE dte,
            SolutionEventListener solutionListener)
        {
            if (dte == null) throw new ArgumentNullException(nameof(dte));
            _solutionListener = solutionListener ?? throw new ArgumentNullException(nameof(solutionListener));

            // Store a strong reference to prevent GC of the COM event sink
            _documentEvents = dte.Events.DocumentEvents;
            _documentEvents.DocumentSaved += OnDocumentSaved;

            System.Diagnostics.Debug.WriteLine("[AICA] DocumentSaveListener: subscribed to DTE.DocumentEvents");
        }

        private void OnDocumentSaved(EnvDTE.Document document)
        {
            try
            {
                var filePath = document?.FullName;
                if (string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] IncrIndex: skip — empty file path");
                    return;
                }

                var projectRoot = _solutionListener.ProjectRootPath;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] IncrIndex: skip — no project root");
                    return;
                }

                if (!ProjectKnowledgeStore.Instance.HasIndex)
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] IncrIndex: skip — no index yet");
                    return;
                }

                var ext = Path.GetExtension(filePath);
                if (!ProjectIndexer.IsSupportedExtension(ext))
                    return; // Non-C/C++ files are silently skipped (too noisy to log)

                // Compute relative path using the same root as ProjectIndexer
                var basePath = projectRoot.EndsWith("\\") || projectRoot.EndsWith("/")
                    ? projectRoot
                    : projectRoot + Path.DirectorySeparatorChar;

                string relativePath;
                if (filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    relativePath = filePath.Substring(basePath.Length).Replace('\\', '/');
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] IncrIndex: skip — file outside project root " +
                        $"(file={filePath}, root={projectRoot})");
                    return;
                }

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] DocumentSaveListener.OnDocumentSaved error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_documentEvents != null)
                {
                    try
                    {
                        _documentEvents.DocumentSaved -= OnDocumentSaved;
                        System.Diagnostics.Debug.WriteLine("[AICA] DocumentSaveListener: unsubscribed");
                    }
                    catch
                    {
                        // Best-effort cleanup during disposal
                    }
                    _documentEvents = null;
                }
            }
        }
    }
}
