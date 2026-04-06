using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// A single step in the edit pipeline.
    /// Steps are registered once and executed in Order for each edit operation.
    /// PreEdit steps run before the file write; PostEdit steps run after.
    /// </summary>
    public interface IEditStep
    {
        /// <summary>Human-readable step name (for logging and telemetry)</summary>
        string Name { get; }

        /// <summary>Whether this step is currently enabled (checked via Feature Flag)</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Execution order within its phase. Lower runs first.
        /// Convention: 100=Format, 200=HeaderSync, 300=Impact, 400=Truncation, 500=Build, 900=Diagnostics.
        /// </summary>
        int Order { get; }

        /// <summary>Which phase this step belongs to (PreEdit or PostEdit)</summary>
        EditPhase Phase { get; }

        /// <summary>
        /// If true, a failure in this step aborts the entire edit operation (fail-close).
        /// Default should be false (fail-open) for all steps except H2 SnapshotStep.
        /// </summary>
        bool FailureIsFatal { get; }

        /// <summary>
        /// Determine whether this step should execute for the given context.
        /// Called before RunAsync; return false to skip without error.
        /// </summary>
        bool ShouldRun(EditContext ctx);

        /// <summary>
        /// Execute the step. Receives the current ToolResult and returns a
        /// (possibly enriched) ToolResult. For PreEdit steps, the current
        /// result is the InitialResult; for PostEdit steps, it is the result
        /// accumulated from all previous PostEdit steps.
        /// </summary>
        Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct);
    }
}
