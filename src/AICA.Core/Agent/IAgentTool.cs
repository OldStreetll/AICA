using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Interface for Agent tools that can be invoked by the LLM
    /// </summary>
    public interface IAgentTool
    {
        /// <summary>
        /// Tool name that matches the LLM function call name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Tool description for the LLM
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Get the JSON schema definition for this tool's parameters
        /// </summary>
        ToolDefinition GetDefinition();

        /// <summary>
        /// Get metadata about this tool (category, requirements, timeout, etc.)
        /// </summary>
        ToolMetadata GetMetadata();

        /// <summary>
        /// Execute the tool with the given parameters
        /// </summary>
        Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default);

        /// <summary>
        /// Handle partial streaming updates (optional)
        /// </summary>
        Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default);
    }

    /// <summary>
    /// Tool definition for LLM function calling
    /// </summary>
    public class ToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public ToolParameters Parameters { get; set; }

        /// <summary>
        /// Raw JSON schema from MCP server. When set, serialized as-is instead of Parameters.
        /// This preserves additionalProperties, nested schemas, etc. that ToolParameters loses.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Text.Json.JsonElement? RawParametersJson { get; set; }
    }

    /// <summary>
    /// Tool parameters schema
    /// </summary>
    public class ToolParameters
    {
        public string Type { get; set; } = "object";
        public System.Collections.Generic.Dictionary<string, ToolParameterProperty> Properties { get; set; }
        public string[] Required { get; set; }
    }

    /// <summary>
    /// Individual parameter property definition
    /// </summary>
    public class ToolParameterProperty
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public string[] Enum { get; set; }
        public object Default { get; set; }

        /// <summary>v2.3: Schema for array items (when Type="array")</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public ToolParameterProperty Items { get; set; }

        /// <summary>v2.3: Properties for nested objects (when Type="object")</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public System.Collections.Generic.Dictionary<string, ToolParameterProperty> Properties { get; set; }

        /// <summary>v2.3: Required fields for nested objects</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string[] Required { get; set; }
    }

    /// <summary>
    /// Tool call request from LLM
    /// </summary>
    public class ToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<string, object> Arguments { get; set; }
        public bool IsPartial { get; set; }
    }

    /// <summary>
    /// Result from tool execution
    /// </summary>
    public class ToolResult
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string Error { get; set; }
        public bool RequiresConfirmation { get; set; }

        /// <summary>
        /// Type of failure. Used by dedup logic to distinguish retryable vs permanent failures.
        /// </summary>
        public ToolResultFailureKind FailureKind { get; set; }

        /// <summary>
        /// Optional metadata for telemetry (e.g., fuzzy match level from EditFileTool).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }

        public static ToolResult Ok(string content) => new ToolResult { Success = true, Content = content };
        public static ToolResult Fail(string error) => new ToolResult { Success = false, Error = error, FailureKind = ToolResultFailureKind.Transient };
        public static ToolResult SecurityDenied(string error) => new ToolResult { Success = false, Error = error, FailureKind = ToolResultFailureKind.SecurityDenied };
        public static ToolResult NeedsConfirm(string content) => new ToolResult { Success = true, Content = content, RequiresConfirmation = true };
    }

    /// <summary>
    /// Classifies tool failure type for dedup and retry decisions.
    /// </summary>
    public enum ToolResultFailureKind
    {
        /// <summary>No failure (success)</summary>
        None,
        /// <summary>Transient failure — retry may succeed (network, timeout)</summary>
        Transient,
        /// <summary>Security denial — retry will never succeed (access denied, protected path)</summary>
        SecurityDenied,
        /// <summary>Permanent failure — resource doesn't exist</summary>
        NotFound
    }
}
