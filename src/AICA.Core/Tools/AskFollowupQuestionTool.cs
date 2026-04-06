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
            (List<QuestionOption> options, string error) = ParseOptions(optionsObj);
            if (error != null)
            {
                return ToolResult.Fail(error);
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

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.Interaction,
                RequiresConfirmation = false,
                RequiresApproval = true,
                TimeoutSeconds = null,
                Tags = new[] { "interaction", "question", "user", "input" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }

        private (List<QuestionOption> Options, string Error) ParseOptions(object optionsObj)
        {
            var options = new List<QuestionOption>();

            // Handle JsonElement (from System.Text.Json)
            if (optionsObj is JsonElement jsonElement)
            {
                // If the LLM passed options as a JSON string instead of an array,
                // try to parse the string as JSON first
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var str = jsonElement.GetString();
                    if (!string.IsNullOrEmpty(str))
                    {
                        try
                        {
                            using var parsed = JsonDocument.Parse(str);
                            if (parsed.RootElement.ValueKind == JsonValueKind.Array)
                                return ParseJsonArray(parsed.RootElement);
                        }
                        catch
                        {
                            // Not valid JSON string, fall through
                        }
                    }
                    return (null, "Options must be an array, got a string");
                }

                if (jsonElement.ValueKind != JsonValueKind.Array)
                    return (null, "Options must be an array");

                return ParseJsonArray(jsonElement);
            }
            // Handle string (LLM may pass serialized JSON array as a raw string)
            else if (optionsObj is string optionsStr)
            {
                if (!string.IsNullOrEmpty(optionsStr))
                {
                    try
                    {
                        using var parsed = JsonDocument.Parse(optionsStr);
                        if (parsed.RootElement.ValueKind == JsonValueKind.Array)
                            return ParseJsonArray(parsed.RootElement);
                    }
                    catch
                    {
                        // Not valid JSON
                    }
                }
                return (null, "Options string could not be parsed as a JSON array");
            }
            // Handle List or Array
            else if (optionsObj is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is JsonElement elem)
                    {
                        options.Add(ParseSingleOption(elem));
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
                return (null, "Options must be an array");
            }

            return (options, null);
        }

        private static (List<QuestionOption> Options, string Error) ParseJsonArray(JsonElement arrayElement)
        {
            var options = new List<QuestionOption>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                options.Add(ParseSingleOption(item));
            }
            return (options, null);
        }

        private static QuestionOption ParseSingleOption(JsonElement item)
        {
            var option = new QuestionOption();

            // Try both camelCase and PascalCase property names
            if (item.TryGetProperty("label", out var labelProp) || item.TryGetProperty("Label", out labelProp))
                option.Label = labelProp.GetString();

            if (item.TryGetProperty("value", out var valueProp) || item.TryGetProperty("Value", out valueProp))
                option.Value = valueProp.GetString();

            if (item.TryGetProperty("description", out var descProp) || item.TryGetProperty("Description", out descProp))
                option.Description = descProp.GetString();

            return option;
        }
    }
}
