using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Config;
using AICA.Core.Rules.Parsers;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Result of memory loading with metrics for telemetry.
    /// </summary>
    public struct MemoryLoadResult
    {
        public string Content;
        public int MemoriesTotal;
        public int MemoriesInjected;
        public int MemoryTokensUsed;
    }

    /// <summary>
    /// 3.8: Cross-session memory bank.
    /// Stores project background information in .aica/memory/ as Markdown files.
    /// Loaded into the system prompt at session start.
    /// v2.1 OH2: Structured retrieval with relevance scoring.
    /// </summary>
    public static class MemoryBank
    {
        private const string MemoryDir = ".aica/memory";
        private const int MaxMemoryTokens = 2000;
        private const int LegacyMaxTotalChars = 4000;

        private static readonly HashSet<string> _migratedDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _migrateLock = new object();
        private static readonly YamlFrontmatterParser _parser = new YamlFrontmatterParser();
        private static readonly RelevanceScorer _scorer = new RelevanceScorer();

        /// <summary>
        /// Load memory entries (legacy overload, no query — full load).
        /// </summary>
        public static Task<string> LoadAsync(string workingDirectory, CancellationToken ct = default)
        {
            return LoadAsync(workingDirectory, null, ct);
        }

        /// <summary>
        /// Load memory entries with optional relevance-based retrieval.
        /// When query is provided and StructuredMemory is enabled, entries are scored and
        /// top-N selected within a token budget. Otherwise falls back to legacy full-load.
        /// </summary>
        public static async Task<string> LoadAsync(string workingDirectory, string query, CancellationToken ct = default)
        {
            var result = await LoadWithMetricsAsync(workingDirectory, query, ct).ConfigureAwait(false);
            return result.Content;
        }

        /// <summary>
        /// Load memory entries and return metrics for telemetry.
        /// v2.1 OH2: Used by AgentExecutor for proper TelemetryLogger integration.
        /// </summary>
        public static async Task<MemoryLoadResult> LoadWithMetricsAsync(string workingDirectory, string query, CancellationToken ct = default)
        {
            var result = new MemoryLoadResult();

            if (string.IsNullOrEmpty(workingDirectory))
                return result;

            var dir = Path.Combine(workingDirectory, MemoryDir);
            if (!Directory.Exists(dir))
                return result;

            try
            {
                // Migration protection — per-directory, run once per dir
                bool needsMigration;
                lock (_migrateLock) { needsMigration = _migratedDirs.Add(dir); }
                if (needsMigration)
                {
                    try
                    {
                        var migrator = new MemoryMigrator();
                        await migrator.MigrateIfNeededAsync(dir, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        lock (_migrateLock) { _migratedDirs.Remove(dir); }
                        // fail-safe: 迁移失败不阻塞加载，但允许下次重试
                    }
                }

                var files = Directory.GetFiles(dir, "*.md")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                    return result;

                // Feature flag check
                if (!AicaConfig.Current.Features.StructuredMemory)
                {
                    result.Content = LoadLegacy(files, ct);
                    return result;
                }

                // Structured path: parse all entries
                var entries = new List<MemoryEntry>();
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    var parsed = _parser.Parse(content);
                    var entry = new MemoryEntry();
                    entry.FilePath = file;
                    entry.Body = (parsed.Body ?? "").Trim();

                    if (parsed.HadFrontmatter && parsed.Data != null)
                    {
                        entry.Name = GetStringValue(parsed.Data, "name")
                            ?? Path.GetFileNameWithoutExtension(file);
                        entry.Description = GetStringValue(parsed.Data, "description") ?? "";
                        entry.Type = GetStringValue(parsed.Data, "type") ?? "project";
                    }
                    else
                    {
                        entry.Name = Path.GetFileNameWithoutExtension(file);
                        entry.Description = "";
                        entry.Type = "project";
                    }

                    entries.Add(entry);
                }

                if (entries.Count == 0)
                    return result;

                result.MemoriesTotal = entries.Count;

                // Select entries
                List<MemoryEntry> selected;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    selected = _scorer.SelectTopN(entries, query, MaxMemoryTokens);
                }
                else
                {
                    // No query — select by token budget in file order
                    selected = new List<MemoryEntry>();
                    int usedTokens = 0;
                    foreach (var e in entries)
                    {
                        int tokens = RelevanceScorer.EstimateTokens(e);
                        if (usedTokens + tokens > MaxMemoryTokens && selected.Count > 0)
                            break;
                        selected.Add(e);
                        usedTokens += tokens;
                    }
                }

                if (selected.Count == 0)
                    return result;

                // Format output
                var parts = new List<string>();
                int totalTokensUsed = 0;
                foreach (var entry in selected)
                {
                    var formatted = FormatEntry(entry);
                    parts.Add(formatted);
                    totalTokensUsed += RelevanceScorer.EstimateTokens(entry);
                }

                result.MemoriesInjected = selected.Count;
                result.MemoryTokensUsed = totalTokensUsed;

                // Debug telemetry
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[AICA] memory_loaded: total={0}, injected={1}, tokens_used={2}",
                        entries.Count, selected.Count, totalTokensUsed));

                result.Content = string.Join("\n\n", parts);
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AICA] MemoryBank.Load failed: " + ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Legacy load path: full concatenation with 4000 char truncation.
        /// </summary>
        private static string LoadLegacy(string[] files, CancellationToken ct)
        {
            var entries = new List<string>();
            int totalChars = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var content = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                if (totalChars + content.Length > LegacyMaxTotalChars)
                    break;

                var name = Path.GetFileNameWithoutExtension(file);
                entries.Add("### " + name + "\n" + content.Trim());
                totalChars += content.Length;
            }

            return entries.Count > 0 ? string.Join("\n\n", entries) : null;
        }

        /// <summary>
        /// Format a memory entry for system prompt injection.
        /// </summary>
        private static string FormatEntry(MemoryEntry entry)
        {
            var type = entry.Type ?? "project";
            var name = entry.Name ?? "unknown";
            var desc = entry.Description;
            var body = entry.Body ?? "";

            string header = "### [" + type + "] " + name;
            if (!string.IsNullOrEmpty(desc))
            {
                header += "\ndescription: " + desc;
            }
            header += "\n---";

            if (!string.IsNullOrEmpty(body))
            {
                return header + "\n" + body;
            }
            return header;
        }

        private static string GetStringValue(Dictionary<string, object> data, string key)
        {
            object val;
            if (data.TryGetValue(key, out val) && val != null)
            {
                var s = val.ToString().Trim();
                return s.Length > 0 ? s : null;
            }
            return null;
        }

        /// <summary>
        /// Save a memory entry to the project's memory bank.
        /// </summary>
        public static async Task SaveAsync(string workingDirectory, string key, string content, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(key))
                return;

            try
            {
                var dir = Path.Combine(workingDirectory, MemoryDir);
                Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, SanitizeFileName(key) + ".md");
                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AICA] MemoryBank.Save failed: " + ex.Message);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
            }
            return new string(sanitized);
        }
    }
}
