using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AICA.Core.SK.Adapters
{
    /// <summary>
    /// Adapts AICA IAgentTool instances into SK KernelPlugin.
    /// Execution is routed through ToolDispatcher to preserve the full middleware pipeline
    /// (logging, timeout, permission, monitoring).
    /// </summary>
    public static class AgentToolPluginAdapter
    {
        private const string PluginName = "AICATools";

        /// <summary>
        /// Create a KernelPlugin from all tools registered in a ToolDispatcher.
        /// Each IAgentTool becomes a KernelFunction within the plugin.
        /// </summary>
        public static KernelPlugin CreatePlugin(
            ToolDispatcher dispatcher,
            IAgentContext context,
            IUIContext uiContext,
            ILogger logger = null)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));

            var functions = new List<KernelFunction>();

            foreach (var toolName in dispatcher.GetToolNames())
            {
                var tool = dispatcher.GetTool(toolName);
                if (tool == null) continue;

                var function = CreateKernelFunction(tool, dispatcher, context, uiContext, logger);
                functions.Add(function);
            }

            return KernelPluginFactory.CreateFromFunctions(PluginName, functions);
        }

        /// <summary>
        /// Create a KernelFunction from a single IAgentTool.
        /// The function delegates execution to ToolDispatcher.ExecuteAsync(),
        /// preserving the entire middleware pipeline.
        /// </summary>
        private static KernelFunction CreateKernelFunction(
            IAgentTool tool,
            ToolDispatcher dispatcher,
            IAgentContext context,
            IUIContext uiContext,
            ILogger logger)
        {
            var definition = tool.GetDefinition();
            var parameters = BuildParameterMetadata(definition);

            return KernelFunctionFactory.CreateFromMethod(
                async (KernelArguments arguments, CancellationToken ct) =>
                {
                    var toolCall = new ToolCall
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = tool.Name,
                        Arguments = ConvertArguments(arguments)
                    };

                    logger?.LogDebug("SK → ToolDispatcher: executing {ToolName}", tool.Name);

                    var result = await dispatcher.ExecuteAsync(toolCall, context, uiContext, ct)
                        .ConfigureAwait(false);

                    if (result.Success)
                    {
                        return result.Content ?? string.Empty;
                    }

                    // Return error as string so SK can pass it back to the LLM
                    return $"[ERROR] {result.Error ?? "Tool execution failed"}";
                },
                functionName: tool.Name,
                description: tool.Description,
                parameters: parameters);
        }

        /// <summary>
        /// Build SK parameter metadata from AICA ToolDefinition.
        /// </summary>
        private static IEnumerable<KernelParameterMetadata> BuildParameterMetadata(ToolDefinition definition)
        {
            if (definition?.Parameters?.Properties == null)
                return Enumerable.Empty<KernelParameterMetadata>();

            var requiredSet = new HashSet<string>(
                definition.Parameters.Required ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // KernelParameterMetadata properties are init-only in SK 1.54+,
            // which is incompatible with netstandard2.0 callers at runtime.
            // Use name-only constructor — SK will still route arguments correctly
            // since function calling is based on parameter names, not metadata.
            return definition.Parameters.Properties.Select(kvp =>
                new KernelParameterMetadata(kvp.Key));
        }

        /// <summary>
        /// Convert SK KernelArguments back to the Dictionary format AICA tools expect.
        /// </summary>
        private static Dictionary<string, object> ConvertArguments(KernelArguments arguments)
        {
            if (arguments == null) return new Dictionary<string, object>();

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in arguments)
            {
                if (kvp.Value == null) continue;

                // SK may pass JsonElement values — unwrap them
                if (kvp.Value is JsonElement jsonElement)
                {
                    result[kvp.Key] = UnwrapJsonElement(jsonElement);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        private static object UnwrapJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var longVal))
                        return longVal;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                default:
                    return element.GetRawText();
            }
        }

        private static Type MapJsonType(string jsonType)
        {
            switch (jsonType?.ToLowerInvariant())
            {
                case "integer": return typeof(int);
                case "number": return typeof(double);
                case "boolean": return typeof(bool);
                case "array": return typeof(string); // Serialized as JSON string
                case "object": return typeof(string); // Serialized as JSON string
                default: return typeof(string);
            }
        }
    }
}
