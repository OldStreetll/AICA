using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Result of a truncation-and-persist operation.
    /// </summary>
    public class TruncationResult
    {
        /// <summary>Preview text suitable for injection into the LLM context.</summary>
        public string PreviewText { get; set; }

        /// <summary>Absolute path to the persisted full output file (null if not truncated).</summary>
        public string FullOutputPath { get; set; }

        /// <summary>Whether the output was actually truncated.</summary>
        public bool WasTruncated { get; set; }

        /// <summary>Original output length in characters.</summary>
        public int OriginalLength { get; set; }
    }

    /// <summary>
    /// v2.1 H1: Centralized service for persisting truncated tool outputs.
    ///
    /// When a tool output exceeds the preview limit the full text is saved to disk
    /// and a shortened preview (with a retrieval hint) is returned for the LLM context.
    ///
    /// Storage: ~/.AICA/truncations/tool_{timestamp}_{toolName}.txt
    /// Retention: configurable (default 7 days), disk quota (default 200 MB).
    /// Thread-safe: all disk I/O serialized via lock; maintenance runs at most once/hour.
    /// </summary>
    public sealed class ToolOutputPersistenceManager
    {
        private static volatile ToolOutputPersistenceManager _instance;
        private static readonly object _instanceLock = new object();

        /// <summary>
        /// Singleton instance, lazy-initialized with default config.
        /// </summary>
        public static ToolOutputPersistenceManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                            _instance = new ToolOutputPersistenceManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>Replace the singleton (for testing or to inject a TelemetryLogger).</summary>
        public static void SetInstance(ToolOutputPersistenceManager instance) => _instance = instance;

        /// <summary>Reset the singleton (for testing).</summary>
        public static void ResetInstance() => _instance = null;

        private readonly string _baseDirectory;
        private readonly int _maxTotalSizeMB;
        private readonly int _retentionDays;
        private readonly int _defaultPreviewChars;
        private readonly Dictionary<string, int> _toolPreviewChars;
        private readonly Logging.TelemetryLogger _telemetryLogger;
        private readonly object _writeLock = new object();

        private DateTime _lastMaintenanceUtc = DateTime.MinValue;
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Creates a new ToolOutputPersistenceManager.
        /// </summary>
        /// <param name="baseDirectory">Override storage path (default: ~/.AICA/truncations/)</param>
        /// <param name="telemetryLogger">Optional telemetry logger for event recording.</param>
        public ToolOutputPersistenceManager(
            string baseDirectory = null,
            Logging.TelemetryLogger telemetryLogger = null)
        {
            var cfg = Config.AicaConfig.Current.Truncation;
            _baseDirectory = baseDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".AICA",
                    "truncations");
            _maxTotalSizeMB = cfg.MaxTotalSizeMB;
            _retentionDays = cfg.RetentionDays;
            _defaultPreviewChars = cfg.DefaultPreviewChars;
            _toolPreviewChars = cfg.ToolPreviewChars ?? new Dictionary<string, int>();
            _telemetryLogger = telemetryLogger;
        }

        /// <summary>Base directory for truncated output files (exposed for testing).</summary>
        public string BaseDirectory => _baseDirectory;

        /// <summary>
        /// Evaluate the output: if it exceeds the preview limit, persist the full
        /// output to disk and return a truncated preview with a retrieval hint.
        /// If under the limit, the output is returned as-is.
        /// Thread-safe.
        /// </summary>
        /// <param name="toolName">Name of the tool that produced the output.</param>
        /// <param name="fullOutput">Complete tool output text.</param>
        /// <param name="previewCharLimit">
        /// Override preview size. Pass 0 or negative to use per-tool / default config value.
        /// </param>
        /// <param name="sessionId">Optional session id for telemetry.</param>
        /// <returns>A <see cref="TruncationResult"/> with the preview and file path.</returns>
        public TruncationResult PersistAndTruncate(
            string toolName,
            string fullOutput,
            int previewCharLimit = 0,
            string sessionId = null)
        {
            if (string.IsNullOrEmpty(fullOutput))
            {
                return new TruncationResult
                {
                    PreviewText = fullOutput ?? string.Empty,
                    WasTruncated = false,
                    OriginalLength = 0
                };
            }

            int limit = ResolvePreviewLimit(toolName, previewCharLimit);

            // Under limit — pass through
            if (fullOutput.Length <= limit)
            {
                return new TruncationResult
                {
                    PreviewText = fullOutput,
                    WasTruncated = false,
                    OriginalLength = fullOutput.Length
                };
            }

            // Over limit — persist and truncate
            try
            {
                var filePath = PersistFullOutput(toolName, fullOutput);

                var preview = fullOutput.Substring(0, limit) +
                    $"\n\n[Output truncated ({fullOutput.Length} chars total). " +
                    $"Full output saved to: {filePath}]\n" +
                    "[Use read_file to view the complete output]";

                // Telemetry
                _telemetryLogger?.LogEvent(sessionId, "truncation_persisted",
                    new Dictionary<string, object>
                    {
                        ["tool_name"] = toolName,
                        ["original_size"] = fullOutput.Length,
                        ["preview_size"] = limit,
                        ["file_path"] = filePath
                    });

                // Piggyback maintenance
                RunMaintenanceIfNeeded();

                return new TruncationResult
                {
                    PreviewText = preview,
                    FullOutputPath = filePath,
                    WasTruncated = true,
                    OriginalLength = fullOutput.Length
                };
            }
            catch (Exception ex)
            {
                // Fail-open: return the full output if persistence fails
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] ToolOutputPersistenceManager.PersistAndTruncate failed: {ex.Message}");

                return new TruncationResult
                {
                    PreviewText = fullOutput,
                    WasTruncated = false,
                    OriginalLength = fullOutput.Length
                };
            }
        }

        #region File I/O

        /// <summary>
        /// Write the full output to a timestamped file. Thread-safe.
        /// </summary>
        private string PersistFullOutput(string toolName, string fullOutput)
        {
            var safeName = SanitizeToolName(toolName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var fileName = $"tool_{timestamp}_{safeName}.txt";

            lock (_writeLock)
            {
                Directory.CreateDirectory(_baseDirectory);
                var filePath = Path.Combine(_baseDirectory, fileName);
                File.WriteAllText(filePath, fullOutput);
                return filePath;
            }
        }

        private static string SanitizeToolName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = new char[name.Length];
            for (int i = 0; i < name.Length; i++)
                chars[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
            return new string(chars);
        }

        #endregion

        #region Preview limit resolution

        private int ResolvePreviewLimit(string toolName, int explicitLimit)
        {
            if (explicitLimit > 0)
                return explicitLimit;

            // Per-tool config override (case-insensitive lookup)
            if (!string.IsNullOrEmpty(toolName) && _toolPreviewChars.Count > 0)
            {
                foreach (var kvp in _toolPreviewChars)
                {
                    if (string.Equals(kvp.Key, toolName, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
                }
            }

            return _defaultPreviewChars;
        }

        #endregion

        #region Maintenance: retention + disk quota

        private void RunMaintenanceIfNeeded()
        {
            var now = DateTime.UtcNow;
            if (now - _lastMaintenanceUtc < MaintenanceInterval) return;
            _lastMaintenanceUtc = now;

            try
            {
                DeleteExpiredFiles();
                EnforceDiskQuota();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] ToolOutputPersistenceManager maintenance failed: {ex.Message}");
            }
        }

        private void DeleteExpiredFiles()
        {
            if (!Directory.Exists(_baseDirectory)) return;
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

            foreach (var file in Directory.GetFiles(_baseDirectory, "tool_*.txt"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff)
                        info.Delete();
                }
                catch { /* skip individual failures */ }
            }
        }

        private void EnforceDiskQuota()
        {
            if (!Directory.Exists(_baseDirectory)) return;
            var maxBytes = (long)_maxTotalSizeMB * 1024 * 1024;

            var files = Directory.GetFiles(_baseDirectory, "tool_*.txt")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            var totalSize = files.Sum(f => f.Length);
            int idx = 0;
            while (totalSize > maxBytes && idx < files.Count)
            {
                try
                {
                    totalSize -= files[idx].Length;
                    files[idx].Delete();
                }
                catch { /* skip */ }
                idx++;
            }
        }

        #endregion
    }
}
