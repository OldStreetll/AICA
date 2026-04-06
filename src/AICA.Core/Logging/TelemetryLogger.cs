using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace AICA.Core.Logging
{
    /// <summary>
    /// T1-infra: Structured telemetry event for JSONL logging.
    /// Each event is one line in the daily JSONL file.
    /// </summary>
    public class TelemetryEvent
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; }

        [JsonPropertyName("event_type")]
        public string EventType { get; set; }

        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; }

        [JsonPropertyName("duration_ms")]
        public long? DurationMs { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object> Metadata { get; set; }
    }

    /// <summary>
    /// T1-infra: Structured JSONL telemetry logger.
    /// Stores events to ~/.AICA/telemetry/YYYY-MM-DD.jsonl with:
    /// - ConcurrentQueue + background timer flush (thread-safe, non-blocking)
    /// - Daily file rotation, 30-day retention
    /// - 10MB per-file cap (rolls to next suffix)
    /// - Configurable total disk cap (default 100MB)
    ///
    /// Telemetry failures never crash the host — all exceptions are swallowed
    /// and reported via Debug.WriteLine.
    /// </summary>
    public sealed class TelemetryLogger : IDisposable
    {
        private readonly string _baseDirectory;
        private readonly int _maxTotalSizeMB;
        private readonly int _retentionDays;
        private readonly long _maxFileSizeBytes;
        private readonly ConcurrentQueue<TelemetryEvent> _queue;
        private readonly Timer _flushTimer;
        private readonly object _writeLock = new object();
        private volatile bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new TelemetryLogger.
        /// </summary>
        /// <param name="baseDirectory">Override storage path (default: ~/.AICA/telemetry/)</param>
        /// <param name="maxTotalSizeMB">Disk quota in MB (default 100, for classified-env disk protection)</param>
        /// <param name="retentionDays">Days to keep log files (default 30)</param>
        /// <param name="maxFileSizeMB">Per-file size cap in MB (default 10)</param>
        /// <param name="flushIntervalMs">Background flush interval in ms (default 2000)</param>
        public TelemetryLogger(
            string baseDirectory = null,
            int maxTotalSizeMB = 100,
            int retentionDays = 30,
            int maxFileSizeMB = 10,
            int flushIntervalMs = 2000)
        {
            _baseDirectory = baseDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".AICA",
                    "telemetry");
            _maxTotalSizeMB = maxTotalSizeMB;
            _retentionDays = retentionDays;
            _maxFileSizeBytes = (long)maxFileSizeMB * 1024 * 1024;
            _queue = new ConcurrentQueue<TelemetryEvent>();

            // Background flush timer — first tick after flushIntervalMs, then repeating
            _flushTimer = new Timer(FlushCallback, null, flushIntervalMs, flushIntervalMs);
        }

        /// <summary>
        /// Base directory for telemetry files (exposed for testing).
        /// </summary>
        public string BaseDirectory => _baseDirectory;

        /// <summary>
        /// Enqueue a telemetry event for background writing.
        /// Non-blocking, thread-safe.
        /// </summary>
        public void Log(TelemetryEvent evt)
        {
            if (_disposed || evt == null) return;

            // Stamp timestamp if not set
            if (string.IsNullOrEmpty(evt.Timestamp))
                evt.Timestamp = DateTime.UtcNow.ToString("o");

            _queue.Enqueue(evt);
        }

        /// <summary>
        /// Convenience: log a tool execution event.
        /// </summary>
        public void LogToolExecution(
            string sessionId,
            string toolName,
            long durationMs,
            bool success,
            Dictionary<string, object> metadata = null)
        {
            var meta = metadata ?? new Dictionary<string, object>();
            meta["success"] = success;

            Log(new TelemetryEvent
            {
                SessionId = sessionId,
                EventType = "tool_execution",
                ToolName = toolName,
                DurationMs = durationMs,
                Metadata = meta
            });
        }

        /// <summary>
        /// Convenience: log a generic event (condensation, reset, plan, etc.).
        /// </summary>
        public void LogEvent(
            string sessionId,
            string eventType,
            Dictionary<string, object> metadata = null)
        {
            Log(new TelemetryEvent
            {
                SessionId = sessionId,
                EventType = eventType,
                Metadata = metadata
            });
        }

        /// <summary>
        /// Force-flush all queued events to disk. Called automatically by the timer,
        /// but can be called manually (e.g. on session end).
        /// Thread-safe via lock.
        /// </summary>
        public void Flush()
        {
            if (_disposed) return;

            var batch = new List<TelemetryEvent>();
            while (_queue.TryDequeue(out var evt))
                batch.Add(evt);

            if (batch.Count == 0) return;

            try
            {
                Directory.CreateDirectory(_baseDirectory);

                var filePath = GetCurrentFilePath();
                var lines = new List<string>(batch.Count);
                foreach (var evt in batch)
                {
                    lines.Add(JsonSerializer.Serialize(evt, JsonOptions));
                }

                lock (_writeLock)
                {
                    // Re-check file size before write — may have rolled since path was computed
                    var actualPath = filePath;
                    if (File.Exists(actualPath))
                    {
                        var info = new FileInfo(actualPath);
                        if (info.Length >= _maxFileSizeBytes)
                            actualPath = GetRolledFilePath();
                    }

                    using (var stream = new FileStream(actualPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream))
                    {
                        foreach (var line in lines)
                            writer.WriteLine(line);
                    }
                }

                // Maintenance: run cleanup periodically (piggyback on flush)
                RunMaintenanceIfNeeded();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] TelemetryLogger.Flush failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispose: flush remaining events and stop the timer.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer.Dispose();

            // Final flush
            try { Flush(); }
            catch { /* swallow */ }
        }

        #region File path management

        private string GetCurrentFilePath()
        {
            var fileName = $"{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
            return Path.Combine(_baseDirectory, fileName);
        }

        /// <summary>
        /// When the daily file exceeds the size cap, roll to a suffixed file.
        /// Pattern: YYYY-MM-DD_001.jsonl, YYYY-MM-DD_002.jsonl, ...
        /// </summary>
        private string GetRolledFilePath()
        {
            var datePrefix = DateTime.UtcNow.ToString("yyyy-MM-dd");

            for (int i = 1; i <= 999; i++)
            {
                var candidate = Path.Combine(_baseDirectory, $"{datePrefix}_{i:D3}.jsonl");
                if (!File.Exists(candidate))
                    return candidate;

                var info = new FileInfo(candidate);
                if (info.Length < _maxFileSizeBytes)
                    return candidate;
            }

            // Fallback: overwrite the last suffix (extremely unlikely — 999 × 10MB = ~10GB)
            return Path.Combine(_baseDirectory, $"{datePrefix}_999.jsonl");
        }

        #endregion

        #region Maintenance: retention + disk quota

        // Run maintenance at most once per hour
        private DateTime _lastMaintenanceUtc = DateTime.MinValue;
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

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
                System.Diagnostics.Debug.WriteLine($"[AICA] TelemetryLogger maintenance failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete .jsonl files older than retention period.
        /// </summary>
        private void DeleteExpiredFiles()
        {
            if (!Directory.Exists(_baseDirectory)) return;

            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            var files = Directory.GetFiles(_baseDirectory, "*.jsonl");

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff)
                        info.Delete();
                }
                catch { /* skip individual file failures */ }
            }
        }

        /// <summary>
        /// Enforce total disk quota by deleting oldest files first.
        /// </summary>
        private void EnforceDiskQuota()
        {
            if (!Directory.Exists(_baseDirectory)) return;

            var maxBytes = (long)_maxTotalSizeMB * 1024 * 1024;
            var files = Directory.GetFiles(_baseDirectory, "*.jsonl")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            var totalSize = files.Sum(f => f.Length);

            // Delete oldest files until under quota
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

        #region Timer callback

        private void FlushCallback(object state)
        {
            try { Flush(); }
            catch { /* swallow — timer callback must not throw */ }
        }

        #endregion
    }
}
