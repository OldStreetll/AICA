using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Storage;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// v2.1 H2-2: Captures a file snapshot before editing (PreEdit phase).
    /// FailureIsFatal=true — if snapshot capture fails, the edit is aborted (fail-close).
    /// Feature-flag gated: when FileSnapshots is disabled, skips with telemetry.
    /// </summary>
    public class SnapshotStep : IEditStep
    {
        public string Name => "Snapshot";
        public bool IsEnabled => true; // Always enabled; feature flag checked in RunAsync for telemetry
        public int Order => 100;
        public EditPhase Phase => EditPhase.PreEdit;
        public bool FailureIsFatal => true;

        public bool ShouldRun(EditContext ctx)
        {
            return !string.IsNullOrEmpty(ctx.FilePath)
                && !string.IsNullOrEmpty(ctx.SessionId);
        }

        public async Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AICA][H2-2] SnapshotStep.RunAsync ENTERED — " +
                $"FileSnapshots={AicaConfig.Current.Features.FileSnapshots}, " +
                $"SessionId={ctx.SessionId ?? "NULL"}, StepIndex={ctx.StepIndex}, " +
                $"FilePath={ctx.FilePath ?? "NULL"}");

            // Feature flag gate — skip with telemetry when disabled
            if (!AicaConfig.Current.Features.FileSnapshots)
            {
                ctx.TelemetryLogger?.LogEvent(ctx.SessionId, "snapshot_step_skipped",
                    new Dictionary<string, object>
                    {
                        ["reason"] = "feature_flag_disabled",
                        ["file"] = ctx.FilePath
                    });
                return current;
            }

            var result = await SnapshotManager.Instance.CaptureAsync(
                ctx.SessionId, ctx.StepIndex, ctx.FilePath).ConfigureAwait(false);

            switch (result)
            {
                case CaptureResult.Captured:
                    return current;

                case CaptureResult.Skipped:
                    return current;

                case CaptureResult.Failed:
                    throw new InvalidOperationException(
                        $"Snapshot capture failed for '{ctx.FilePath}' " +
                        $"(session={ctx.SessionId}, step={ctx.StepIndex}). Edit aborted.");

                default:
                    return current;
            }
        }
    }
}
