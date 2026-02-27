using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool that allows the Agent to ask the user a followup question with multiple choice options.
    /// This enables interactive decision-making during task execution.
    /// </summary>
    public class AskFollowupQuestionTool : IAgentTool
    {
        public string Name => "ask_followup_question";

        public string Description => "Ask the user a followup question with multiple choice options. Use this when you need the user to make a decision or provide additional information during task execution. The user can select from predefined options or provide custom input if allowed.";

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
                        ["question"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The question to ask the user. Be clear and specific."
                        },
                        ["options"] = new ToolParameterProperty
                        {
                            Type = "array",
                            Description = "Array of option objects, each with 'label' (display text), 'value' (returned value), and optional 'description' (additional context). Provide 2-5 meaningful options."
                        },
                        ["allow_custom_input"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "Whether to allow the user to provide custom text input instead of selecting a predefined option. Default is false.",
                            Default = false
                        }
                    },
                    Required = new[] { "question", "options" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            // Validate question parameter
            if (!call.Arguments.TryGetValue("question", out var questionObj) || questionObj == null)
                return ToolResult.Fail("Missing required parameter: question");

            var question = questionObj.ToString();
            if (string.IsNullOrWhiteSpace(question))
                return ToolResult.Fail("Question cannot be empty");

            // Validate options parameter
            if (!call.Arguments.TryGetValue("options", out var optionsObj) || optionsObj == null)
                return ToolResult.Fail("Missing required parameter: options");

            // Parse options array
            List<QuestionOption> options;
            try
            {
                options = ParseOptions(optionsObj);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to parse options: {ex.Message}");
            }

            if (options == null || options.Count == 0)
                return ToolResult.Fail("Options array cannot be empty");

            if (options.Count > 10)
                return ToolResult.Fail("Too many options (max 10)");

            // Validate each option
            for (int i = 0; i < options.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(options[i].Label))
                    return ToolResult.Fail($"Option {i + 1} is missing a label");
                if (string.IsNullOrWhiteSpace(options[i].Value))
                    options[i].Value = options[i].Label; // Default value to label
            }

            // Parse allow_custom_input parameter
            bool allowCustomInput = false;
            if (call.Arguments.TryGetValue("allow_custom_input", out var customInputObj) && customInputObj != null)
            {
                if (customInputObj is bool boolVal)
                    allowCustomInput = boolVal;
                else if (bool.TryParse(customInputObj.ToString(), out var parsedBool))
                    allowCustomInput = parsedBool;
            }

            // Show the followup question to the user
            var result = await uiContext.ShowFollowupQuestionAsync(question, options, allowCustomInput, ct);

            if (result.Cancelled)
            {
                return ToolResult.Fail("User cancelled the question");
            }

            // Return the user's answer
            var answerType = result.IsCustomInput ? "custom input" : "selected option";
            var responseMessage = $"User answered ({answerType}): {result.Answer}";

            return ToolResult.Ok(responseMessage);
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            // No partial handling needed for this tool
            return Task.CompletedTask;
        }

        private List<QuestionOption> ParseOptions(object optionsObj)
        {
            var options = new List<QuestionOption>();

            // Handle JsonElement (from System.Text.Json)
            if (optionsObj is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException("Options must be an array");

                foreach (var item in jsonElement.EnumerateArray())
                {
                    var option = new QuestionOption();

                    if (item.TryGetProperty("label", out var labelProp))
                        option.Label = labelProp.GetString();

                    if (item.TryGetProperty("value", out var valueProp))
                        option.Value = valueProp.GetString();

                    if (item.TryGetProperty("description", out var descProp))
                        option.Description = descProp.GetString();

                    options.Add(option);
                }
            }
            // Handle List or Array
            else if (optionsObj is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is JsonElement elem)
                    {
                        var option = new QuestionOption();

                        if (elem.TryGetProperty("label", out var labelProp))
                            option.Label = labelProp.GetString();

                        if (elem.TryGetProperty("value", out var valueProp))
                            option.Value = valueProp.GetString();

                        if (elem.TryGetProperty("description", out var descProp))
                            option.Description = descProp.GetString();

                        options.Add(option);
                    }
                    else if (item is Dictionary<string, object> dict)
                    {
                        var option = new QuestionOption
                        {
                            Label = dict.TryGetValue("label", out var label) ? label?.ToString() : null,
                            Value = dict.TryGetValue("value", out var value) ? value?.ToString() : null,
                            Description = dict.TryGetValue("description", out var desc) ? desc?.ToString() : null
                        };
                        options.Add(option);
                    }
                }
            }
            else
            {
                throw new ArgumentException("Options must be an array");
            }

            return options;
        }
    }
}
