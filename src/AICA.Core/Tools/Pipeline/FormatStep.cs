using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// PostEdit step that auto-formats the edited file using the IDE's formatter.
    /// Order=100 — runs first in the PostEdit phase.
    ///
    /// Fail-open: if formatting fails or no formatter is available, the edit
    /// result passes through unchanged. Telemetry records whether formatting
    /// actually changed the file.
    ///
    /// v2.1 M3 — Phase 1
    /// </summary>
    public class FormatStep : IEditStep
    {
        private readonly IFormatService _formatService;

        /// <summary>
        /// Languages for which auto-format is supported.
        /// Matched case-insensitively against EditContext.Language.
        /// </summary>
        private static readonly HashSet<string> SupportedLanguages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "C", "C++", "C#", "cpp", "c", "cs", "csharp",
                "Java", "java",
                "TypeScript", "typescript", "ts",
                "JavaScript", "javascript", "js",
                "JSON", "json",
                "XML", "xml",
                "XAML", "xaml"
            };

        /// <summary>
        /// File extensions that have formatter support, used as fallback
        /// when EditContext.Language is null or empty.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hxx",
                ".cs",
                ".java",
                ".ts", ".tsx", ".js", ".jsx",
                ".json",
                ".xml", ".xaml", ".csproj", ".vcxproj"
            };

        public FormatStep(IFormatService formatService)
        {
            _formatService = formatService ?? throw new ArgumentNullException(nameof(formatService));
        }

        public string Name => "AutoFormat";

        public bool IsEnabled => AicaConfig.Current.Features.AutoFormatAfterEdit;

        public int Order => 100;

        public EditPhase Phase => EditPhase.PostEdit;

        public bool FailureIsFatal => false;

        public bool ShouldRun(EditContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.FilePath))
                return false;

            // Check by Language field first
            if (!string.IsNullOrEmpty(ctx.Language))
                return SupportedLanguages.Contains(ctx.Language);

            // Fallback: check file extension
            var ext = System.IO.Path.GetExtension(ctx.FilePath);
            return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
        }

        public async Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            bool formatChanged = false;

            try
            {
                // Ensure file is open in the editor (required for DTE.FormatDocument)
                if (ctx.AgentContext != null)
                {
                    await ctx.AgentContext.OpenFileInEditorAsync(ctx.FilePath, ct)
                        .ConfigureAwait(false);
                }

                // Run the formatter
                formatChanged = await _formatService.FormatAsync(ctx.FilePath, ct)
                    .ConfigureAwait(false);

                sw.Stop();

                if (formatChanged && current.Success)
                {
                    current.Content += "\n✅ Auto-formatted after edit.";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Fail-open: log and continue with unmodified result
                sw.Stop();
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] FormatStep failed (non-fatal): {ex.Message}");
            }

            // Telemetry: append to result metadata
            var metadata = current.Metadata ?? new Dictionary<string, string>();
            metadata["format_changed"] = formatChanged.ToString().ToLowerInvariant();
            metadata["format_duration_ms"] = sw.ElapsedMilliseconds.ToString();
            current.Metadata = metadata;

            // v2.1 T1: Persist structured telemetry event
            ctx.TelemetryLogger?.LogEvent(ctx.SessionId, "format_step",
                new Dictionary<string, object>
                {
                    ["format_changed"] = formatChanged,
                    ["format_duration_ms"] = sw.ElapsedMilliseconds,
                    ["file_path"] = ctx.FilePath,
                    ["language"] = ctx.Language
                });

            return current;
        }
    }
}
