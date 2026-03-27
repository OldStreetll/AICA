using System.Collections.Generic;

namespace AICA.Core.Storage
{
    /// <summary>
    /// 3.6/3.7: Task progress data for Condense protection zones and checkpoint resume.
    /// Shared data structure captured by code (not LLM) during agent execution.
    /// </summary>
    public sealed class TaskProgress
    {
        public string OriginalUserRequest { get; set; }
        public List<string> EditedFiles { get; set; } = new List<string>();
        public List<string> EditDetails { get; set; } = new List<string>();
        public string PlanState { get; set; }
        public string CurrentPhase { get; set; }
        public List<string> KeyDiscoveries { get; set; } = new List<string>();
    }
}
