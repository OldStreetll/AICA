using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Workspace;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context interface providing access to the workspace and environment
    /// Combines file operations, workspace management, and task tracking
    /// </summary>
    public interface IAgentContext : IFileContext, IWorkspaceContext, ITaskContext
    {
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
