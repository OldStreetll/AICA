using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Storage;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// v2.1 H1: Truncation persistence for edit tool output.
    /// When the accumulated ToolResult content exceeds the preview limit,
    /// persist the full output to disk and replace with a truncated preview.
    ///
    /// Order=400 (after HeaderSync=200, before Diagnostics=900).
    /// Feature flag: features.truncationPersistence
    /// Fail-open: truncation failure does not block the edit result.
    /// </summary>
    public class TruncationStep : IEditStep
    {
        public string Name => "Truncation";
        public bool IsEnabled => AicaConfig.Current.Features.TruncationPersistence;
        public int Order => 400;
        public EditPhase Phase => EditPhase.PostEdit;
        public bool FailureIsFatal => false;

        public bool ShouldRun(EditContext ctx)
        {
            // Always run when enabled — PersistAndTruncate is a no-op when under limit
            return true;
        }

        public Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            if (current == null || string.IsNullOrEmpty(current.Content))
                return Task.FromResult(current);

            var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
                "edit", current.Content, AicaConfig.Current.Truncation.DefaultPreviewChars,
                ctx.SessionId);

            if (tr.WasTruncated)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] edit truncation persisted: {tr.FullOutputPath} ({current.Content.Length} chars)");
                current.Content = tr.PreviewText +
                    $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
                    "Use read_file with the above path to see the complete output.";
            }
            else
            {
                current.Content = tr.PreviewText;
            }

            return Task.FromResult(current);
        }
    }
}
