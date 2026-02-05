using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for updating the task plan
    /// </summary>
    public class UpdatePlanTool : IAgentTool
    {
        public string Name => "update_plan";
        public string Description => "Update the task plan with current progress. Use this to track multi-step tasks.";

        public ToolDefinition GetDefinition()
        {
            return new ToolDefinition
            {
                Name = Name,
                Description = Description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["explanation"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Brief explanation of the plan update"
                        },
                        ["plan"] = new ToolParameterProperty
                        {
                            Type = "array",
                            Description = "Array of plan steps, each with 'step' (description) and 'status' (pending/in_progress/completed)"
                        }
                    },
                    Required = new[] { "plan" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            string explanation = null;
            if (call.Arguments.TryGetValue("explanation", out var explObj) && explObj != null)
            {
                explanation = explObj.ToString();
            }

            if (!call.Arguments.TryGetValue("plan", out var planObj) || planObj == null)
            {
                return Task.FromResult(ToolResult.Fail("Missing required parameter: plan"));
            }

            var taskPlan = new TaskPlan
            {
                Explanation = explanation,
                Steps = new List<PlanStep>()
            };

            // Parse plan array
            try
            {
                var planJson = planObj.ToString();
                var steps = JsonSerializer.Deserialize<List<PlanStepInput>>(planJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (steps != null)
                {
                    foreach (var step in steps)
                    {
                        var status = ParseStatus(step.Status);
                        taskPlan.Steps.Add(new PlanStep
                        {
                            Description = step.Step,
                            Status = status
                        });
                    }
                }
            }
            catch (JsonException ex)
            {
                return Task.FromResult(ToolResult.Fail($"Invalid plan format: {ex.Message}"));
            }

            // Update the context
            context.UpdatePlan(taskPlan);

            // Format response
            var response = new System.Text.StringBuilder();
            response.AppendLine("Plan updated:");
            if (!string.IsNullOrEmpty(explanation))
            {
                response.AppendLine($"  {explanation}");
            }
            foreach (var step in taskPlan.Steps)
            {
                var statusIcon = GetStatusIcon(step.Status);
                response.AppendLine($"  {statusIcon} {step.Description}");
            }

            return Task.FromResult(ToolResult.Ok(response.ToString()));
        }

        private PlanStepStatus ParseStatus(string status)
        {
            return status?.ToLowerInvariant() switch
            {
                "in_progress" => PlanStepStatus.InProgress,
                "completed" => PlanStepStatus.Completed,
                "failed" => PlanStepStatus.Failed,
                _ => PlanStepStatus.Pending
            };
        }

        private string GetStatusIcon(PlanStepStatus status)
        {
            return status switch
            {
                PlanStepStatus.Pending => "⏳",
                PlanStepStatus.InProgress => "🔄",
                PlanStepStatus.Completed => "✅",
                PlanStepStatus.Failed => "❌",
                _ => "○"
            };
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        private class PlanStepInput
        {
            public string Step { get; set; }
            public string Status { get; set; }
        }
    }
}
