namespace AICA.Core.Agent
{
    /// <summary>
    /// Represents a step in the Agent execution.
    /// Yielded by AgentExecutor.ExecuteAsync as an async enumerable.
    /// </summary>
    public class AgentStep
    {
        public AgentStepType Type { get; set; }
        public string Text { get; set; }
        public ToolCall ToolCall { get; set; }
        public ToolResult Result { get; set; }
        public string ErrorMessage { get; set; }
        public TaskPlan Plan { get; set; }

        public static AgentStep TextChunk(string text) => new AgentStep { Type = AgentStepType.TextChunk, Text = text };
        public static AgentStep ThinkingChunk(string text) => new AgentStep { Type = AgentStepType.ThinkingChunk, Text = text };
        public static AgentStep ActionStart(string text) => new AgentStep { Type = AgentStepType.ActionStart, Text = text };
        public static AgentStep ToolStart(ToolCall call) => new AgentStep { Type = AgentStepType.ToolStart, ToolCall = call };
        public static AgentStep WithToolResult(ToolCall call, ToolResult result) => new AgentStep { Type = AgentStepType.ToolResult, ToolCall = call, Result = result };
        public static AgentStep PlanUpdated(TaskPlan plan) => new AgentStep { Type = AgentStepType.PlanUpdate, Plan = plan };
        public static AgentStep Complete(string finalText) => new AgentStep { Type = AgentStepType.Complete, Text = finalText };
        public static AgentStep WithError(string error) => new AgentStep { Type = AgentStepType.Error, ErrorMessage = error };
    }

    public enum AgentStepType
    {
        TextChunk,
        ThinkingChunk,
        ActionStart,
        ToolStart,
        ToolResult,
        PlanUpdate,
        Complete,
        Error
    }
}
