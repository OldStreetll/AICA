using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context interface for task management
    /// </summary>
    public interface ITaskContext
    {
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
    }
}
