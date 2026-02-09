using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for the Agent to signal task completion and present results to the user.
    /// When invoked, the Agent loop will terminate and the result will be displayed.
    /// The user can then start a new conversation to provide feedback if needed.
    /// </summary>
    public class AttemptCompletionTool : IAgentTool
    {
        public string Name => "attempt_completion";
        public string Description => "Signal that the task is complete and present the final result to the user. Use this when you have finished all work. Include a comprehensive summary of what was accomplished.";

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
                        ["result"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "A comprehensive summary of what was accomplished, including all changes made"
                        },
                        ["command"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Optional command to demonstrate the result (e.g. 'dotnet run', 'dotnet test')"
                        }
                    },
                    Required = new[] { "result" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            if (!call.Arguments.TryGetValue("result", out var resultObj) || resultObj == null)
                return Task.FromResult(ToolResult.Fail("Missing required parameter: result"));

            var result = resultObj.ToString();
            if (string.IsNullOrWhiteSpace(result))
                return Task.FromResult(ToolResult.Fail("Result cannot be empty"));

            // Parse optional command
            string command = null;
            if (call.Arguments.TryGetValue("command", out var cmdObj) && cmdObj != null)
            {
                command = cmdObj.ToString();
            }

            // Build the completion message that will be shown to the user
            var completionMessage = result;
            if (!string.IsNullOrWhiteSpace(command))
            {
                completionMessage += "\n\nSuggested command to verify:\n" + command;
            }

            // Return TASK_COMPLETED — the AgentExecutor checks for this and ends the loop
            return Task.FromResult(ToolResult.Ok("TASK_COMPLETED:" + completionMessage));
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
