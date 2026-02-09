using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool that allows the Agent to condense/summarize the conversation history
    /// to free up token space for longer tasks. The Agent provides a summary of
    /// what has been accomplished so far, which replaces the middle of the conversation.
    /// </summary>
    public class CondenseTool : IAgentTool
    {
        public string Name => "condense";
        public string Description => "Condense the conversation history by providing a summary of work done so far. Use this when the conversation is getting long and you need more context space. The summary will replace earlier messages in the conversation.";

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
                        ["summary"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "A comprehensive summary of all work done so far, including files read/modified, tools used, decisions made, and current progress. This replaces the earlier conversation history."
                        }
                    },
                    Required = new[] { "summary" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            if (!call.Arguments.TryGetValue("summary", out var summaryObj) || summaryObj == null)
                return Task.FromResult(ToolResult.Fail("Missing required parameter: summary"));

            var summary = summaryObj.ToString();
            if (string.IsNullOrWhiteSpace(summary))
                return Task.FromResult(ToolResult.Fail("Summary cannot be empty"));

            // Return the summary with a special prefix so AgentExecutor can detect it
            // and perform the actual conversation condensation
            return Task.FromResult(ToolResult.Ok("CONDENSE:" + summary));
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
