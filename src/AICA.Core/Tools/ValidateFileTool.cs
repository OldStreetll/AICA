using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// v2.3: Tool for retrieving IDE diagnostics (errors/warnings) for a file.
    /// Polls the VS2022 Error List until results stabilize.
    /// </summary>
    public class ValidateFileTool : IAgentTool
    {
        public string Name => "validate_file";
        public string Description =>
            "Get IDE diagnostics (errors/warnings) for a file from the VS2022 Error List. " +
            "Use after editing to check for syntax or semantic errors introduced by changes. " +
            "Returns empty if no issues found.";

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.FileRead,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 10,
                IsModifying = false,
                Tags = new[] { "file", "validate", "diagnostics", "lint" }
            };
        }

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
                        ["file_path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the file to validate"
                        }
                    },
                    Required = new[] { "file_path" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "file_path");

                if (!context.IsPathAccessible(path))
                    return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));

                if (!await context.FileExistsAsync(path, ct))
                    return ToolErrorHandler.HandleError(ToolErrorHandler.NotFound(path));

                var diagnostics = await context.GetDiagnosticsAsync(path, ct);

                if (diagnostics == null || diagnostics.Count == 0)
                    return ToolResult.Ok($"✅ No diagnostics found for {path}. File appears clean.");

                var formatted = string.Join("\n", diagnostics.Select(d =>
                    $"  Line {d.Line}, Col {d.Column}: [{d.Severity}] {d.Message}" +
                    (string.IsNullOrEmpty(d.Code) ? "" : $" ({d.Code})")));

                var errors = diagnostics.Count(d => d.Severity == "error");
                var warnings = diagnostics.Count(d => d.Severity == "warning");

                return ToolResult.Ok(
                    $"⚠️ {diagnostics.Count} diagnostic(s) found for {path}" +
                    (errors > 0 ? $" ({errors} error(s))" : "") +
                    (warnings > 0 ? $" ({warnings} warning(s))" : "") +
                    $":\n{formatted}\n\nFix these issues before proceeding.");
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (System.Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "validate_file");
                return ToolErrorHandler.HandleError(error);
            }
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
