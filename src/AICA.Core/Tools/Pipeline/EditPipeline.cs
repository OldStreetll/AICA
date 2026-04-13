using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Executes registered IEditStep instances around file edits.
    /// Supports two phases: PreEdit (before file write) and PostEdit (after file write).
    ///
    /// Design decisions (from four-instance review C1/C3):
    /// - PreEdit and PostEdit share the same registration list, separated by EditPhase.
    /// - Steps within each phase run in Order (ascending).
    /// - PostEdit steps default to fail-open: a failing step logs + continues.
    /// - PreEdit steps with FailureIsFatal=true abort the edit (e.g., H2 SnapshotStep).
    /// - S2 BuildStep uses fire-and-forget; its result propagates via BuildResultCache,
    ///   not through this pipeline (see横切规则 #4).
    /// </summary>
    public class EditPipeline
    {
        private readonly List<IEditStep> _steps = new List<IEditStep>();
        private readonly ILogger<EditPipeline> _logger;

        /// <summary>
        /// Create a new EditPipeline with optional logger.
        /// </summary>
        public EditPipeline(ILogger<EditPipeline> logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Register a step. Steps are sorted by Phase then Order at execution time.
        /// </summary>
        public void Register(IEditStep step)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));

            _steps.Add(step);
            _logger?.LogDebug("EditPipeline: registered step {StepName} (Phase={Phase}, Order={Order})",
                step.Name, step.Phase, step.Order);
        }

        /// <summary>
        /// Execute all PreEdit steps in order.
        /// Returns a ToolResult: if a fatal step fails, returns an error result
        /// and the caller should NOT proceed with the edit.
        /// If all steps succeed (or non-fatal steps fail), returns null — proceed with edit.
        /// </summary>
        public async Task<ToolResult> ExecutePreEditAsync(EditContext ctx, CancellationToken ct)
        {
            var preSteps = GetStepsForPhase(EditPhase.PreEdit);
            System.Diagnostics.Debug.WriteLine(
                $"[AICA][H2-2] EditPipeline.ExecutePreEditAsync — PreEdit step count={preSteps.Count}, " +
                $"steps=[{string.Join(", ", preSteps.Select(s => $"{s.Name}(Enabled={s.IsEnabled},Phase={s.Phase})"))}]");

            foreach (var step in preSteps)
            {
                ct.ThrowIfCancellationRequested();

                if (!step.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA][H2-2] EditPipeline PreEdit — SKIP {step.Name} (IsEnabled=false)");
                    _logger?.LogDebug("EditPipeline: skipping disabled step {StepName}", step.Name);
                    continue;
                }

                if (!step.ShouldRun(ctx))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA][H2-2] EditPipeline PreEdit — SKIP {step.Name} (ShouldRun=false) " +
                        $"SessionId={ctx.SessionId ?? "NULL"}, FilePath={ctx.FilePath ?? "NULL"}");
                    _logger?.LogDebug("EditPipeline: step {StepName} skipped (ShouldRun=false)", step.Name);
                    continue;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[AICA][H2-2] EditPipeline PreEdit — EXECUTING {step.Name}");
                try
                {
                    _logger?.LogDebug("EditPipeline: executing PreEdit step {StepName}", step.Name);
                    await step.RunAsync(ctx, ctx.InitialResult, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "EditPipeline: PreEdit step {StepName} failed", step.Name);

                    if (step.FailureIsFatal)
                    {
                        _logger?.LogError("EditPipeline: fatal PreEdit step {StepName} failed, aborting edit", step.Name);
                        return ToolResult.Fail(
                            $"Edit aborted: {step.Name} failed — {ex.Message}");
                    }
                    // Non-fatal PreEdit failure: log and continue
                }
            }

            return null; // All OK — caller should proceed with the edit
        }

        /// <summary>
        /// Execute all PostEdit steps in order, enriching the ToolResult along the way.
        /// Each step receives the result from the previous step and may append content.
        /// Non-fatal failures are logged but do not stop the pipeline.
        /// </summary>
        public async Task<ToolResult> ExecutePostEditAsync(EditContext ctx, ToolResult result, CancellationToken ct)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var postSteps = GetStepsForPhase(EditPhase.PostEdit);

            foreach (var step in postSteps)
            {
                ct.ThrowIfCancellationRequested();

                if (!step.IsEnabled)
                {
                    _logger?.LogDebug("EditPipeline: skipping disabled step {StepName}", step.Name);
                    continue;
                }

                if (!step.ShouldRun(ctx))
                {
                    _logger?.LogDebug("EditPipeline: step {StepName} skipped (ShouldRun=false)", step.Name);
                    continue;
                }

                try
                {
                    _logger?.LogDebug("EditPipeline: executing PostEdit step {StepName}", step.Name);
                    result = await step.RunAsync(ctx, result, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "EditPipeline: PostEdit step {StepName} failed (non-fatal)", step.Name);

                    if (step.FailureIsFatal)
                    {
                        // PostEdit fatal failure: unusual but possible.
                        // Return the error but do not discard the edit (file is already written).
                        _logger?.LogError("EditPipeline: fatal PostEdit step {StepName} failed", step.Name);
                        result.Content += $"\n\n⚠️ Pipeline step '{step.Name}' failed: {ex.Message}";
                    }
                    // Non-fatal: swallow and continue with current result
                }
            }

            return result;
        }

        /// <summary>
        /// Get registered steps for a phase, sorted by Order ascending.
        /// </summary>
        private List<IEditStep> GetStepsForPhase(EditPhase phase)
        {
            return _steps
                .Where(s => s.Phase == phase)
                .OrderBy(s => s.Order)
                .ToList();
        }

        /// <summary>Number of registered steps (for testing)</summary>
        public int StepCount => _steps.Count;
    }
}
