namespace AICA.Core.Initialization
{
    /// <summary>
    /// Initialization step identifiers.
    /// </summary>
    public enum InitStepId
    {
        RulesInit,        // 规则目录创建 + C++ 检测
        SymbolIndexing,   // TreeSitter 符号索引
        GitNexusInstall,  // npm install (仅首次)
        GitNexusAnalyze   // GitNexus analyze
    }

    /// <summary>
    /// Step execution status.
    /// </summary>
    public enum InitStepStatus
    {
        Pending,    // 等待执行
        Running,    // 正在执行
        Completed,  // 成功完成
        Failed,     // 执行失败（非阻塞）
        Skipped     // 跳过（如无 .git 目录跳过 GitNexus）
    }

    /// <summary>
    /// Immutable snapshot of a single initialization step's state.
    /// </summary>
    public class InitStepState
    {
        public InitStepId Id { get; }
        public string DisplayName { get; }
        public InitStepStatus Status { get; }
        public string StatusMessage { get; }
        public double? ProgressPercent { get; }

        public InitStepState(
            InitStepId id,
            string displayName,
            InitStepStatus status,
            string statusMessage = null,
            double? progressPercent = null)
        {
            Id = id;
            DisplayName = displayName;
            Status = status;
            StatusMessage = statusMessage;
            ProgressPercent = progressPercent;
        }

        /// <summary>
        /// Create a new state with updated fields (immutable pattern).
        /// </summary>
        public InitStepState With(
            InitStepStatus? status = null,
            string statusMessage = null,
            double? progressPercent = null)
        {
            return new InitStepState(
                Id,
                DisplayName,
                status ?? Status,
                statusMessage ?? StatusMessage,
                progressPercent ?? ProgressPercent);
        }
    }
}
