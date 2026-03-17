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
        public string Description => "**CRITICAL - MUST USE**: Call this tool when you complete ANY task. This is REQUIRED to signal task completion. Provide a summary of what was accomplished. Without calling this tool, the user won't know the task is finished.";

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
                            Description = "A structured summary of what was accomplished. Include the following sections as applicable:\n" +
                                "1. **[File Structure]** All classes, enums, interfaces, namespaces found — use COMPLETE names (with prefixes/suffixes/namespaces)\n" +
                                "2. **[Method List]** All public methods, grouped by category\n" +
                                "3. **[Dependencies]** #include / using / import references and key dependencies\n" +
                                "4. **[Counts]** Precise numbers (class count, method count, file count, match count). Use tool-reported totals, not manual counts\n" +
                                "5. **[Key Findings]** Design patterns (with code evidence), notable structures, issues found\n" +
                                "For simple tasks (file creation, single search), 1-3 sentences suffice. For analysis tasks, aim for 70%+ coverage of key information. " +
                                "Numbers MUST be accurate and consistent with your analysis text."
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

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
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

            // Create structured completion result
            var completionResult = new CompletionResult
            {
                Summary = result,
                Command = command
            };

            // Return serialized completion result
            return Task.FromResult(ToolResult.Ok(completionResult.Serialize()));
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
                RequiresApproval = true,
                TimeoutSeconds = 10,
                Tags = new[] { "completion", "finish", "result" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }
    }
}
