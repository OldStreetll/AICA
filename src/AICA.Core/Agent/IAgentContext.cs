using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context interface providing access to the workspace and environment
    /// </summary>
    public interface IAgentContext
    {
        /// <summary>
        /// Current working directory
        /// </summary>
        string WorkingDirectory { get; }

        /// <summary>
        /// Get list of files that can be accessed
        /// </summary>
        Task<IEnumerable<string>> GetAccessibleFilesAsync(CancellationToken ct = default);

        /// <summary>
        /// Check if a file path is accessible
        /// </summary>
        bool IsPathAccessible(string path);

        /// <summary>
        /// Read file content
        /// </summary>
        Task<string> ReadFileAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Write file content
        /// </summary>
        Task WriteFileAsync(string path, string content, CancellationToken ct = default);

        /// <summary>
        /// Check if file exists
        /// </summary>
        Task<bool> FileExistsAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Get the current task plan
        /// </summary>
        TaskPlan CurrentPlan { get; }

        /// <summary>
        /// Update the task plan
        /// </summary>
        void UpdatePlan(TaskPlan plan);

        /// <summary>
        /// Request user confirmation for an operation
        /// </summary>
        Task<bool> RequestConfirmationAsync(string operation, string details, CancellationToken ct = default);

        /// <summary>
        /// Show diff preview for file changes and ask user to confirm.
        /// Returns true if user accepts the changes.
        /// </summary>
        Task<bool> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default);
    }

    /// <summary>
    /// Task plan for tracking progress
    /// </summary>
    public class TaskPlan
    {
        public List<PlanStep> Steps { get; set; } = new List<PlanStep>();
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Individual step in a task plan
    /// </summary>
    public class PlanStep
    {
        public string Description { get; set; }
        public PlanStepStatus Status { get; set; }
    }

    /// <summary>
    /// Status of a plan step
    /// </summary>
    public enum PlanStepStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed
    }
}
