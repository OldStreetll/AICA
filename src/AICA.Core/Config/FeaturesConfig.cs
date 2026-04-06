namespace AICA.Core.Config
{
    /// <summary>
    /// Feature flags for incremental rollout and rollback of v2.1 capabilities.
    /// Each flag controls a specific feature; defaults are production-ready values.
    /// Users override individual flags in ~/.AICA/config.json under the "features" section.
    /// </summary>
    public class FeaturesConfig
    {
        // Phase 1: Context management
        public bool PruneBeforeCompaction { get; set; } = true;

        // Phase 1: PostEditPipeline - auto format
        public bool AutoFormatAfterEdit { get; set; } = true;

        // Phase 1: Skills system
        public bool SkillsEnabled { get; set; } = true;
        public bool TaskTemplatesEnabled { get; set; } = true;

        // Phase 2: Tool output persistence
        public bool TruncationPersistence { get; set; } = true;

        // Phase 2: C/C++ header sync detection
        public bool HeaderSyncDetection { get; set; } = true;

        // Phase 3: Structured memory retrieval
        public bool StructuredMemory { get; set; } = true;

        // Phase 3: Permission denial feedback injection
        public bool PermissionFeedback { get; set; } = true;

        // Phase 4: File snapshot and rollback
        public bool FileSnapshots { get; set; } = true;

        // Phase 4: Cross-session permission persistence
        public bool PermissionPersistence { get; set; } = true;

        // Phase 5: ReviewAgent
        public bool ReviewAgent { get; set; } = true;
        public bool ReviewAgentAutoTrigger { get; set; } = false;

        // Phase 5: PlanAgent optimized output
        public bool PlanAgentOptimized { get; set; } = true;

        // Phase 5: Background build after edit
        public bool AutoBackgroundBuild { get; set; } = true;

        // Phase 6: Hooks system
        public bool CommandHooks { get; set; } = true;
        public bool AgentHooks { get; set; } = false;

        // Phase 6: Symbol graph expansion
        public bool SymbolGraphExpansion { get; set; } = true;

        // Phase 7: Proactive impact analysis
        public bool ProactiveImpactAnalysis { get; set; } = true;
    }
}
