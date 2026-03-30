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
        public string Description =>
            "Track progress of multi-step tasks. Update step status (pending/in_progress/completed) " +
            "and add explanation of current progress. " +
            "Use this proactively during complex tasks to show progress to the user.";

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

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
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

            // Parse plan array — handle both pre-deserialized List<object> (from ConvertJsonElement)
            // and raw JSON string representations
            try
            {
                if (planObj is List<object> planList)
                {
                    // Already deserialized by ConvertJsonElement: List<object> of Dictionary<string,object>
                    foreach (var item in planList)
                    {
                        if (item is Dictionary<string, object> stepDict)
                        {
                            var stepDesc = stepDict.TryGetValue("step", out var s) ? s?.ToString() : null;
                            var statusStr = stepDict.TryGetValue("status", out var st) ? st?.ToString() : null;
                            taskPlan.Steps.Add(new PlanStep
                            {
                                Description = stepDesc ?? "",
                                Status = ParseStatus(statusStr)
                            });
                        }
                    }
                }
                else
                {
                    // Fallback: try to parse as JSON string
                    var planJson = planObj is JsonElement je ? je.GetRawText() : planObj.ToString();
                    var steps = JsonSerializer.Deserialize<List<PlanStepInput>>(planJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (steps != null)
                    {
                        foreach (var step in steps)
                        {
                            taskPlan.Steps.Add(new PlanStep
                            {
                                Description = step.Step,
                                Status = ParseStatus(step.Status)
                            });
                        }
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

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.Interaction,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 10,
                Tags = new[] { "plan", "progress", "tracking" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }

        private class PlanStepInput
        {
            public string Step { get; set; }
            public string Status { get; set; }
        }
    }
}
