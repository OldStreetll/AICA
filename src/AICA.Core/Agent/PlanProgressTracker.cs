using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Lightweight summary of a tool execution for plan progress evaluation.
    /// </summary>
    public class ToolExecutionSummary
    {
        public string ToolName { get; set; }
        public bool Success { get; set; }
        /// <summary>Truncated to ≤200 chars.</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// Lightweight LLM-based tracker that evaluates which plan steps
    /// have been completed after each tool execution round.
    /// Failures never block the main execution loop.
    /// </summary>
    public class PlanProgressTracker
    {
        private readonly ILLMClient _llmClient;
        private const int MaxPromptTokens = 1500;
        private const int TimeoutSeconds = 10;

        private int _evaluationCount;
        private readonly int _minToolCallsBetweenEvaluations = 2;
        private readonly int _maxEvaluations = 15;

        public PlanProgressTracker(ILLMClient llmClient)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        }

        /// <summary>
        /// Evaluate progress and return an updated plan if any steps changed.
        /// Returns null if no changes or on any error.
        /// </summary>
        public async Task<TaskPlan> EvaluateProgressAsync(
            TaskPlan currentPlan,
            List<ToolExecutionSummary> recentTools,
            CancellationToken ct)
        {
            if (currentPlan?.Steps == null || currentPlan.Steps.Count == 0)
                return null;
            if (recentTools == null || recentTools.Count == 0)
                return null;

            // Frequency control: require minimum tool calls between evaluations
            if (recentTools.Count < _minToolCallsBetweenEvaluations)
                return null;

            // Hard cap on total evaluations
            if (_evaluationCount >= _maxEvaluations)
                return null;

            // Skip if all steps already terminal
            if (currentPlan.Steps.All(s =>
                s.Status == PlanStepStatus.Completed ||
                s.Status == PlanStepStatus.Failed))
                return null;

            try
            {
                _evaluationCount++;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                    return await RunEvaluationAsync(currentPlan, recentTools, cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICA] PlanProgressTracker failed: {ex.Message}");
                return null;
            }
        }

        private async Task<TaskPlan> RunEvaluationAsync(
            TaskPlan currentPlan,
            List<ToolExecutionSummary> recentTools,
            CancellationToken ct)
        {
            var prompt = BuildEvaluationPrompt(currentPlan, recentTools);

            var messages = new List<ChatMessage>
            {
                ChatMessage.System(EvaluationSystemPrompt),
                ChatMessage.User(prompt)
            };

            var responseText = new StringBuilder();
            await foreach (var chunk in _llmClient.StreamChatAsync(messages, null, ct)
                .ConfigureAwait(false))
            {
                if (chunk.Type == LLMChunkType.Text && chunk.Text != null)
                    responseText.Append(chunk.Text);
            }

            return ParseEvaluationResponse(currentPlan, responseText.ToString());
        }

        private const string EvaluationSystemPrompt =
            "You are a progress tracker. Given a plan and recent tool execution results, " +
            "determine which steps have been completed or are in progress. " +
            "Respond with ONLY a JSON array of step status updates. " +
            "Each element: {\"step\": <1-based index>, \"status\": \"completed\"|\"in_progress\"|\"failed\"}. " +
            "Only include steps whose status has CHANGED. " +
            "If no steps changed, respond with an empty array: []";

        private string BuildEvaluationPrompt(
            TaskPlan plan,
            List<ToolExecutionSummary> recentTools)
        {
            var sb = new StringBuilder();

            sb.AppendLine("## Current Plan");
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                sb.Append(i + 1).Append(". [")
                  .Append(plan.Steps[i].Status.ToString().ToUpper())
                  .Append("] ")
                  .AppendLine(plan.Steps[i].Description);
            }

            sb.AppendLine();
            sb.AppendLine("## Recent Tool Executions");
            foreach (var tool in recentTools)
            {
                sb.Append("- ").Append(tool.ToolName);
                sb.Append(tool.Success ? " \u2713" : " \u2717");
                if (!string.IsNullOrEmpty(tool.Summary))
                    sb.Append(": ").Append(tool.Summary);
                sb.AppendLine();
            }

            var result = sb.ToString();
            if (result.Length > MaxPromptTokens * 4)
                result = result.Substring(0, MaxPromptTokens * 4);

            return result;
        }

        private TaskPlan ParseEvaluationResponse(TaskPlan currentPlan, string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            var json = response.Trim();
            // Extract JSON array (may be wrapped in markdown code block)
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('[');
                var end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
            }
            else if (!json.StartsWith("["))
            {
                // Try to find JSON array in response
                var start = json.IndexOf('[');
                var end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
                else
                    return null;
            }

            List<StepStatusUpdate> updates;
            try
            {
                updates = System.Text.Json.JsonSerializer
                    .Deserialize<List<StepStatusUpdate>>(json);
            }
            catch
            {
                Debug.WriteLine($"[AICA] PlanProgressTracker: failed to parse: {json}");
                return null;
            }

            if (updates == null || updates.Count == 0)
                return null;

            // Create new plan (immutable pattern)
            var newSteps = currentPlan.Steps
                .Select(s => new PlanStep
                {
                    Description = s.Description,
                    Status = s.Status
                })
                .ToList();

            bool anyChanged = false;
            foreach (var update in updates)
            {
                int idx = update.Step - 1;
                if (idx < 0 || idx >= newSteps.Count) continue;

                var newStatus = ParseStatus(update.Status);
                if (newStatus == null) continue;

                // Prevent demotion: completed/failed cannot be reverted
                if (newSteps[idx].Status == PlanStepStatus.Completed) continue;
                if (newSteps[idx].Status == PlanStepStatus.Failed) continue;

                if (newSteps[idx].Status != newStatus.Value)
                {
                    newSteps[idx].Status = newStatus.Value;
                    anyChanged = true;
                }
            }

            if (!anyChanged) return null;

            return new TaskPlan
            {
                Steps = newSteps,
                Explanation = currentPlan.Explanation
            };
        }

        private static PlanStepStatus? ParseStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return null;
            switch (status.ToLowerInvariant())
            {
                case "completed": return PlanStepStatus.Completed;
                case "in_progress": return PlanStepStatus.InProgress;
                case "failed": return PlanStepStatus.Failed;
                default: return null;
            }
        }

        private class StepStatusUpdate
        {
            [System.Text.Json.Serialization.JsonPropertyName("step")]
            public int Step { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("status")]
            public string Status { get; set; }
        }
    }
}
