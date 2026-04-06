using System;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent.Middleware
{
    /// <summary>
    /// 3.3 H1: Post-edit verification middleware.
    /// After a successful edit, verifies the modification was correctly applied
    /// using two high-confidence checks (no false positives on C++ template syntax).
    /// </summary>
    public class VerificationMiddleware : IToolExecutionMiddleware
    {
        public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Execute the tool first
            var result = await context.Next(ct).ConfigureAwait(false);

            // Only verify successful edit operations
            if (context.Tool?.Name != "edit" || !result.Success)
                return result;

            // Skip verification for full_replace (no old_string to check)
            var args = context.Call?.Arguments;
            if (args == null)
                return result;

            var fullReplace = false;
            if (args.TryGetValue("full_replace", out var frVal))
            {
                fullReplace = frVal is bool b && b;
            }
            if (fullReplace)
                return result;

            // Get parameters for verification
            string filePath = null;
            string newString = null;
            string oldString = null;

            if (args.TryGetValue("file_path", out var fpVal))
                filePath = fpVal?.ToString();
            if (args.TryGetValue("new_string", out var nsVal))
                newString = nsVal?.ToString();
            if (args.TryGetValue("old_string", out var osVal))
                oldString = osVal?.ToString();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(newString))
                return result;

            // Skip if result indicates user cancelled or manually edited
            if (result.Content != null &&
                (result.Content.Contains("CANCELLED") || result.Content.Contains("USER MANUALLY EDITED")))
                return result;

            // Verification 1: Content existence check
            // Confirm new_string's first 3 non-empty lines exist in the file
            try
            {
                var fileContent = await context.AgentContext.ReadFileAsync(filePath, ct).ConfigureAwait(false);
                var checkLines = newString.Split('\n');
                string firstCheckLine = null;
                foreach (var line in checkLines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 10)
                    {
                        firstCheckLine = trimmed;
                        break;
                    }
                }

                if (firstCheckLine != null && !fileContent.Contains(firstCheckLine))
                {
                    return ToolResult.Ok(
                        result.Content +
                        "\n\n⚠️ [验证] 修改已提交，但 new_string 的首个有效行未在文件中找到。" +
                        "建议用 read_file 确认修改是否正确应用。");
                }

                // Verification 2: Line count anomaly check
                if (!string.IsNullOrEmpty(oldString))
                {
                    var oldLines = oldString.Split('\n').Length;
                    var newLines = newString.Split('\n').Length;
                    var diff = Math.Abs(newLines - oldLines);
                    if (diff > 50 && diff > oldLines * 2)
                    {
                        return ToolResult.Ok(
                            result.Content +
                            $"\n\n⚠️ [验证] 修改行数差异较大（原 {oldLines} 行 → 新 {newLines} 行）。" +
                            "建议用 read_file 确认修改范围是否符合预期。");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Verification failure should not break the edit
            }

            return result;
        }
    }
}
