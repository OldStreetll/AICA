using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Knowledge;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// S3: PostEdit step that detects C/C++ function signature changes and warns
    /// about header files that may need synchronization.
    ///
    /// Order=200 — runs after FormatStep (100), before ImpactStep (300).
    /// Fail-open: detection failures are logged but do not block the edit result.
    ///
    /// v2.1 S3 — Phase 2 (moved from Phase 4 per review)
    /// </summary>
    public class HeaderSyncStep : IEditStep
    {
        private readonly HeaderSyncDetector _detector;

        public HeaderSyncStep(HeaderSyncDetector detector = null)
        {
            _detector = detector ?? new HeaderSyncDetector();
        }

        public string Name => "HeaderSync";
        public bool IsEnabled => AicaConfig.Current.Features.HeaderSyncDetection;
        public int Order => 200;
        public EditPhase Phase => EditPhase.PostEdit;
        public bool FailureIsFatal => false;

        private static readonly HashSet<string> CppLanguages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "C", "C++", "cpp", "c", "cc", "cxx" };

        private static readonly HashSet<string> CppSourceExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cpp", ".cxx", ".cc", ".c" };

        public bool ShouldRun(EditContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.FilePath))
                return false;

            // Must have detected symbol changes
            if (ctx.EditedSymbols == null || ctx.EditedSymbols.Count == 0)
                return false;

            // Check language
            if (!string.IsNullOrEmpty(ctx.Language) && CppLanguages.Contains(ctx.Language))
                return true;

            // Fallback: check file extension
            var ext = System.IO.Path.GetExtension(ctx.FilePath);
            return CppSourceExtensions.Contains(ext);
        }

        public async Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            try
            {
                var index = ProjectKnowledgeStore.Instance.GetIndex();
                if (index == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[AICA] HeaderSyncStep: no project index available, skipping");
                    return current;
                }

                // Convert SymbolChange list to before/after SymbolRecord lists for the detector
                var symbolsBefore = BuildSymbolRecords(ctx.EditedSymbols, useBefore: true, ctx.FilePath);
                var symbolsAfter = BuildSymbolRecords(ctx.EditedSymbols, useBefore: false, ctx.FilePath);

                var warnings = _detector.Detect(ctx.FilePath, symbolsBefore, symbolsAfter, index);

                if (warnings.Count == 0)
                    return current;

                // Build warning message
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine($"⚠️ 函数签名已变化，以下头文件可能需要同步更新：");
                foreach (var w in warnings)
                {
                    sb.AppendLine($"- {w.HeaderFilePath}: {w.OldSignature} → {w.NewSignature}");
                }
                sb.AppendLine("请检查并更新对应的头文件声明。");

                if (current.Success)
                    current.Content += sb.ToString();

                // Telemetry
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] header_sync_warning_triggered: {warnings.Count} header(s) for {ctx.FilePath}");

                ctx.TelemetryLogger?.LogEvent(ctx.SessionId, "header_sync_warning",
                    new Dictionary<string, object>
                    {
                        ["header_sync_warning_triggered"] = true,
                        ["warning_count"] = warnings.Count,
                        ["file_path"] = ctx.FilePath,
                        ["headers"] = string.Join(", ", warnings.Select(w => w.HeaderFilePath))
                    });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] HeaderSyncStep failed (non-fatal): {ex.Message}");
            }

            return current;
        }

        /// <summary>
        /// Build minimal SymbolRecord list from SymbolChange entries.
        /// When useBefore=true, uses OldSignature; otherwise uses NewSignature.
        /// </summary>
        private static List<SymbolRecord> BuildSymbolRecords(
            List<SymbolChange> changes, bool useBefore, string filePath)
        {
            var records = new List<SymbolRecord>();

            foreach (var change in changes)
            {
                // Parse namespace and name from the fully qualified name (e.g., "NS::Class::Method")
                string ns = "";
                string name = change.Name ?? "";
                var lastSep = name.LastIndexOf("::", StringComparison.Ordinal);
                if (lastSep >= 0)
                {
                    ns = name.Substring(0, lastSep);
                    name = name.Substring(lastSep + 2);
                }

                var sig = useBefore ? change.OldSignature : change.NewSignature;
                if (string.IsNullOrEmpty(sig))
                    continue;

                records.Add(new SymbolRecord(
                    id: $"{filePath}:{name}",
                    name: name,
                    kind: SymbolKind.Function,
                    filePath: change.FilePath ?? filePath,
                    ns: ns,
                    summary: "",
                    keywords: null,
                    signature: sig));
            }

            return records;
        }
    }
}
