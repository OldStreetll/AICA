using System.Collections.Generic;
using AICA.Core.Agent;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Context passed through the edit pipeline.
    /// Populated by EditFileTool before invoking the pipeline;
    /// consumed by IEditStep implementations to decide whether and how to act.
    /// </summary>
    public class EditContext
    {
        /// <summary>Absolute path of the file being edited</summary>
        public string FilePath { get; set; }

        /// <summary>File content before the edit was applied</summary>
        public string OriginalContent { get; set; }

        /// <summary>File content after the edit was applied</summary>
        public string NewContent { get; set; }

        /// <summary>Unified diff text (for display / diagnostics)</summary>
        public string Diff { get; set; }

        /// <summary>Current agent session identifier</summary>
        public string SessionId { get; set; }

        /// <summary>Sequential step index within the session (for snapshot naming)</summary>
        public int StepIndex { get; set; }

        /// <summary>Agent context for file operations, path checks, diagnostics, etc.</summary>
        public IAgentContext AgentContext { get; set; }

        /// <summary>The ToolResult produced by the edit itself (before pipeline processing)</summary>
        public ToolResult InitialResult { get; set; }

        // ── Fields added per review C2 / Pane3 architecture review ──

        /// <summary>Which editing mode produced this context</summary>
        public EditMode EditMode { get; set; }

        /// <summary>Detected language of the file (e.g., "C++", "C#"). Null if unknown.</summary>
        public string Language { get; set; }

        /// <summary>Current agent intent classification (e.g., "refactor", "bug_fix"). Used by S4 ImpactStep trigger.</summary>
        public string Intent { get; set; }

        /// <summary>Symbols whose signatures changed in this edit. Null/empty if not detected yet.</summary>
        public List<SymbolChange> EditedSymbols { get; set; }

        /// <summary>
        /// True when this is the last file in a MultiFile batch.
        /// S2 BuildStep uses this for debounce — only trigger build on the last file.
        /// Always true for Single and MultiEdit modes.
        /// </summary>
        public bool IsLastFileInBatch { get; set; } = true;

        /// <summary>
        /// v2.1 T1: Telemetry logger for structured event recording.
        /// Null when telemetry is disabled or not yet wired.
        /// </summary>
        public Logging.TelemetryLogger TelemetryLogger { get; set; }
    }
}
