using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// H5: Pre-validates tool call parameters before execution.
    /// Intercepts obvious parameter errors (missing files, empty strings, no-ops)
    /// to avoid wasting LLM iterations on doomed tool calls.
    /// Pipeline position: PermissionCheck → PreValidation → Timeout → Logging → Monitoring → Tool
    /// </summary>
    public class PreValidationMiddleware : IToolExecutionMiddleware
    {
        private readonly ILogger<PreValidationMiddleware> _logger;

        public PreValidationMiddleware(ILogger<PreValidationMiddleware> logger = null)
        {
            _logger = logger;
        }

        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var call = context.Call;
            if (call == null)
                return await context.Next(ct).ConfigureAwait(false);

            var validationError = await ValidateAsync(call, context, ct).ConfigureAwait(false);
            if (validationError != null)
            {
                _logger?.LogDebug("H5 pre-validation blocked tool {ToolName}: {Error}", call.Name, validationError);
                return ToolResult.Fail(validationError);
            }

            return await context.Next(ct).ConfigureAwait(false);
        }

        private async Task<string> ValidateAsync(ToolCall call, ToolExecutionContext context, CancellationToken ct)
        {
            var toolName = call.Name?.ToLowerInvariant() ?? "";

            switch (toolName)
            {
                case "edit":
                    return await ValidateEditAsync(call, context, ct).ConfigureAwait(false);

                case "read_file":
                    return await ValidateReadFileAsync(call, context, ct).ConfigureAwait(false);

                case "grep_search":
                    return ValidateGrepSearch(call);

                default:
                    return null; // No pre-validation for other tools
            }
        }

        private async Task<string> ValidateEditAsync(ToolCall call, ToolExecutionContext context, CancellationToken ct)
        {
            var path = GetArg(call, "path") ?? GetArg(call, "file_path");
            var oldString = GetArg(call, "old_string");
            var newString = GetArg(call, "new_string");

            // Fix 1: full_replace is a boolean parameter, not mode="full_replace"
            var fullReplaceArg = GetArg(call, "full_replace");
            var isFullReplace = string.Equals(fullReplaceArg, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullReplaceArg, "True", StringComparison.OrdinalIgnoreCase);

            // Skip validation for full_replace mode (creates new files)
            if (isFullReplace)
                return null;

            // Check file existence
            if (!string.IsNullOrEmpty(path) && context.AgentContext != null)
            {
                var exists = await context.AgentContext.FileExistsAsync(path, ct).ConfigureAwait(false);
                if (!exists)
                {
                    // Fix 2: If file doesn't exist but new_string is provided,
                    // this is likely a "create new file" intent. Let EditFileTool handle it
                    // (it has its own user confirmation for file creation).
                    if (!string.IsNullOrWhiteSpace(newString))
                        return null;
                    return $"文件 {path} 不存在。请用 find_by_name 确认路径后重试。";
                }
            }

            // Check old_string is not empty (only for existing files)
            if (string.IsNullOrWhiteSpace(oldString))
                return "old_string 不能为空。请先用 read_file 查看文件内容。";

            // Check no-op (old_string == new_string)
            if (oldString != null && newString != null && oldString == newString)
                return "old_string 和 new_string 相同，无需编辑。";

            return null;
        }

        private async Task<string> ValidateReadFileAsync(ToolCall call, ToolExecutionContext context, CancellationToken ct)
        {
            var path = GetArg(call, "path") ?? GetArg(call, "file_path");
            if (string.IsNullOrEmpty(path))
                return null; // Let the tool itself handle missing path

            if (context.AgentContext != null)
            {
                var exists = await context.AgentContext.FileExistsAsync(path, ct).ConfigureAwait(false);
                if (!exists)
                    return $"文件 {path} 不存在。";
            }

            return null;
        }

        private string ValidateGrepSearch(ToolCall call)
        {
            // Support both "pattern" (new) and "query" (legacy) parameter names
            var query = GetArg(call, "pattern") ?? GetArg(call, "query");
            if (string.IsNullOrWhiteSpace(query))
                return "搜索关键词不能为空。";

            return null;
        }

        private static string GetArg(ToolCall call, string key)
        {
            if (call.Arguments == null) return null;
            return call.Arguments.TryGetValue(key, out var val) ? val?.ToString() : null;
        }
    }
}
