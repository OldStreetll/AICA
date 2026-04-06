using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Appends IDE diagnostics (errors/warnings) to the edit result.
    /// Migrated from EditFileTool.AppendDiagnosticsAsync — behavior is identical.
    ///
    /// Order=900: runs last in the PostEdit phase so all other steps
    /// (format, header sync, truncation, build) have already enriched the result.
    /// Always enabled, fail-open (diagnostics unavailable should never block an edit).
    /// </summary>
    public class DiagnosticsStep : IEditStep
    {
        public string Name => "Diagnostics";
        public bool IsEnabled => true;
        public int Order => 900;
        public EditPhase Phase => EditPhase.PostEdit;
        public bool FailureIsFatal => false;

        public bool ShouldRun(EditContext ctx)
        {
            // Only run for successful edits — failed edits don't need diagnostics
            return ctx.InitialResult != null && ctx.InitialResult.Success;
        }

        public async Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            if (!current.Success)
                return current;

            var sw = Stopwatch.StartNew();
            var diagnostics = await ctx.AgentContext.GetDiagnosticsAsync(ctx.FilePath, ct);
            sw.Stop();

            var count = diagnostics?.Count ?? 0;
            if (count > 0)
            {
                var formatted = string.Join("\n", diagnostics.Select(d =>
                    $"  Line {d.Line}, Col {d.Column}: [{d.Severity}] {d.Message}" +
                    (string.IsNullOrEmpty(d.Code) ? "" : $" ({d.Code})")));
                current.Content += $"\n\n⚠️ DIAGNOSTICS ({count} issue(s) detected after edit):\n{formatted}\n" +
                                   "Fix these issues before proceeding.";
            }

            // v2.1 T1: Persist structured telemetry event
            ctx.TelemetryLogger?.LogEvent(ctx.SessionId, "diagnostics_step",
                new Dictionary<string, object>
                {
                    ["diagnostics_count"] = count,
                    ["duration_ms"] = sw.ElapsedMilliseconds
                });

            return current;
        }
    }
}
