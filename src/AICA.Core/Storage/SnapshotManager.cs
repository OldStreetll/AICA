using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Storage
{
    // ── Result types ──────────────────────────────────────────────

    public enum CaptureResult
    {
        Captured,   // snapshot saved
        Skipped,    // intentionally skipped (file too large, already captured, etc.)
        Failed      // real failure (IO error, etc.)
    }

    public class RestoreFileResult
    {
        public string FilePath { get; set; }
        public bool Success { get; set; }
        public string FailureReason { get; set; }  // null if success
    }

    public class RestoreResult
    {
        public bool Success { get; set; }
        public List<RestoreFileResult> Files { get; set; } = new List<RestoreFileResult>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class SnapshotInfo
    {
        public string SessionId { get; set; }
        public int StepIndex { get; set; }
        public string OriginalFilePath { get; set; }
        public string SnapshotFilePath { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedUtc { get; set; }
        /// <summary>
        /// True if this snapshot represents a newly created file (rollback = delete).
        /// </summary>
        public bool IsNewFile { get; set; }
    }

    // ── Interface ─────────────────────────────────────────────────

    public interface ISnapshotManager
    {
        Task<CaptureResult> CaptureAsync(string sessionId, int stepIndex, string filePath, bool isNewFile = false);
        Task<RestoreResult> RestoreAsync(string sessionId, int stepIndex);
        Task<List<SnapshotInfo>> GetSnapshotsAsync(string sessionId);
        Task CleanupExpiredAsync();
        Task<long> GetTotalSnapshotSizeBytesAsync();
    }

    // ── Implementation ────────────────────────────────────────────

    /// <summary>
    /// v2.1 H2: Captures file snapshots before edits and supports rollback.
    ///
    /// Storage layout: ~/.AICA/snapshots/{sessionId}/{stepIndex}/{relativePath}
    /// Thread-safe: all disk I/O serialized via SemaphoreSlim.
    /// Cleanup granularity: per-session (entire session directory removed together).
    /// Retention: configurable (default 7 days by creation time), disk quota (default 500 MB).
    /// </summary>
    public sealed class SnapshotManager : ISnapshotManager
    {
        private static volatile SnapshotManager _instance;
        private static readonly object _instanceLock = new object();

        public static SnapshotManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                            _instance = new SnapshotManager();
                    }
                }
                return _instance;
            }
        }

        public static void SetInstance(SnapshotManager instance) => _instance = instance;

        public static void Initialize(Logging.TelemetryLogger telemetryLogger = null)
        {
            lock (_instanceLock)
            {
                _instance = new SnapshotManager(telemetryLogger: telemetryLogger);
            }
        }

        public static void ResetInstance() => _instance = null;

        private readonly string _baseDirectory;
        private readonly long _maxFileSizeBytes;
        private readonly int _maxTotalSizeMB;
        private readonly int _retentionDays;
        private readonly Logging.TelemetryLogger _telemetryLogger;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        private DateTime _lastMaintenanceUtc = DateTime.MinValue;
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Phase 4 T10: Fired after CleanupExpiredAsync completes so UI can invalidate snapshot caches.
        /// </summary>
        public event Action SnapshotsCleanedUp;

        public SnapshotManager(
            string baseDirectory = null,
            Logging.TelemetryLogger telemetryLogger = null)
        {
            var cfg = Config.AicaConfig.Current.Snapshots;
            _baseDirectory = baseDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".AICA",
                    "snapshots");
            _maxFileSizeBytes = cfg.MaxFileSizeBytes;
            _maxTotalSizeMB = cfg.MaxTotalSizeMB;
            _retentionDays = cfg.RetentionDays;
            _telemetryLogger = telemetryLogger;
        }

        public string BaseDirectory => _baseDirectory;

        // ── CaptureAsync ──────────────────────────────────────────

        private const string NewFileSuffix = ".aica-newfile";

        public async Task<CaptureResult> CaptureAsync(string sessionId, int stepIndex, string filePath, bool isNewFile = false)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AICA][H2-2] SnapshotManager.CaptureAsync ENTERED — " +
                $"sessionId={sessionId ?? "NULL"}, stepIndex={stepIndex}, " +
                $"filePath={filePath ?? "NULL"}, isNewFile={isNewFile}, baseDir={_baseDirectory}");

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(filePath))
                return CaptureResult.Failed;

            try
            {
                // Resolve to absolute path
                var fullPath = Path.GetFullPath(filePath);

                if (isNewFile)
                {
                    // New file marker: write an empty sentinel file with .aica-newfile suffix
                    var relativePath = GetRelativePath(fullPath);
                    var markerPath = Path.Combine(
                        _baseDirectory, sessionId, stepIndex.ToString(), relativePath + NewFileSuffix);

                    await _ioLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (File.Exists(markerPath))
                            return CaptureResult.Skipped;

                        var dir = Path.GetDirectoryName(markerPath);
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(markerPath, Array.Empty<byte>());
                    }
                    finally
                    {
                        _ioLock.Release();
                    }

                    _telemetryLogger?.LogEvent(sessionId, "snapshot_created",
                        new Dictionary<string, object>
                        {
                            ["file_path"] = filePath,
                            ["step_index"] = stepIndex,
                            ["is_new_file"] = true,
                            ["snapshot_size_bytes"] = 0
                        });

                    await RunMaintenanceIfNeededAsync().ConfigureAwait(false);
                    return CaptureResult.Captured;
                }

                if (!File.Exists(fullPath))
                    return CaptureResult.Skipped; // file doesn't exist and not marked as new

                // Check file size limit
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > _maxFileSizeBytes)
                    return CaptureResult.Skipped;

                // Build snapshot destination path
                var snapshotRelativePath = GetRelativePath(fullPath);
                var snapshotPath = Path.Combine(
                    _baseDirectory, sessionId, stepIndex.ToString(), snapshotRelativePath);
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA][H2-2] SnapshotManager.CaptureAsync — " +
                    $"fullPath={fullPath}, snapshotPath={snapshotPath}, fileSize={fileInfo.Length}");

                await _ioLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Idempotency: if already captured for this step, skip
                    if (File.Exists(snapshotPath))
                        return CaptureResult.Skipped;

                    var dir = Path.GetDirectoryName(snapshotPath);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA][H2-2] SnapshotManager — CreateDirectory={dir}, Copy {fullPath} → {snapshotPath}");
                    Directory.CreateDirectory(dir);
                    File.Copy(fullPath, snapshotPath, overwrite: false);
                }
                finally
                {
                    _ioLock.Release();
                }

                // Telemetry
                var snapshotSize = new FileInfo(snapshotPath).Length;
                _telemetryLogger?.LogEvent(sessionId, "snapshot_created",
                    new Dictionary<string, object>
                    {
                        ["file_path"] = filePath,
                        ["step_index"] = stepIndex,
                        ["snapshot_size_bytes"] = snapshotSize
                    });
                _telemetryLogger?.LogEvent(sessionId, "snapshot_size_bytes",
                    new Dictionary<string, object>
                    {
                        ["size_bytes"] = snapshotSize
                    });

                // Piggyback maintenance
                await RunMaintenanceIfNeededAsync().ConfigureAwait(false);

                return CaptureResult.Captured;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] SnapshotManager.CaptureAsync failed: {ex.Message}");
                return CaptureResult.Failed;
            }
        }

        // ── RestoreAsync ──────────────────────────────────────────

        public async Task<RestoreResult> RestoreAsync(string sessionId, int stepIndex)
        {
            var result = new RestoreResult { Success = true };

            var stepDir = Path.Combine(_baseDirectory, sessionId, stepIndex.ToString());
            if (!Directory.Exists(stepDir))
            {
                result.Warnings.Add($"No snapshots found for session={sessionId}, step={stepIndex}");
                return result;
            }

            var snapshotFiles = Directory.GetFiles(stepDir, "*", SearchOption.AllDirectories);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var snapshotFile in snapshotFiles)
                {
                    var fileResult = new RestoreFileResult();
                    try
                    {
                        // Reconstruct the original path from the relative path stored under stepDir
                        var relativeToStep = snapshotFile
                            .Substring(stepDir.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var originalPath = ReconstructOriginalPath(relativeToStep);
                        fileResult.FilePath = originalPath;

                        // Check if the original file was modified after the snapshot
                        if (File.Exists(originalPath))
                        {
                            var origInfo = new FileInfo(originalPath);
                            var snapInfo = new FileInfo(snapshotFile);
                            if (origInfo.LastWriteTimeUtc > snapInfo.CreationTimeUtc)
                            {
                                result.Warnings.Add(
                                    $"File modified after snapshot: {originalPath}");
                            }
                        }
                        else
                        {
                            // File was deleted after snapshot — will be re-created
                            result.Warnings.Add(
                                $"File deleted after snapshot, will re-create: {originalPath}");
                        }

                        var dir = Path.GetDirectoryName(originalPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        File.Copy(snapshotFile, originalPath, overwrite: true);
                        fileResult.Success = true;
                    }
                    catch (Exception ex)
                    {
                        fileResult.Success = false;
                        fileResult.FailureReason = ex.Message;
                        result.Success = false;
                    }
                    result.Files.Add(fileResult);
                }
            }
            finally
            {
                _ioLock.Release();
            }

            // Telemetry
            var successCount = result.Files.Count(f => f.Success);
            var failedCount = result.Files.Count(f => !f.Success);
            if (result.Success)
            {
                _telemetryLogger?.LogEvent(sessionId, "snapshot_restored",
                    new Dictionary<string, object>
                    {
                        ["step_index"] = stepIndex,
                        ["files_restored"] = successCount,
                        ["warnings_count"] = result.Warnings.Count
                    });
            }
            else
            {
                _telemetryLogger?.LogEvent(sessionId, "snapshot_restore_failed",
                    new Dictionary<string, object>
                    {
                        ["step_index"] = stepIndex,
                        ["files_restored"] = successCount,
                        ["files_failed"] = failedCount,
                        ["failed_files"] = result.Files
                            .Where(f => !f.Success)
                            .Select(f => f.FilePath)
                            .ToList()
                    });
            }

            return result;
        }

        // ── GetSnapshotsAsync ─────────────────────────────────────

        public async Task<List<SnapshotInfo>> GetSnapshotsAsync(string sessionId)
        {
            var snapshots = new List<SnapshotInfo>();
            var sessionDir = Path.Combine(_baseDirectory, sessionId);

            if (!Directory.Exists(sessionDir))
                return snapshots;

            // Read-only enumeration — no lock needed; concurrent CaptureAsync
            // may add files mid-enumeration but that is harmless (snapshot appears
            // in the next call). Directory enumeration is atomic per entry on NTFS/ext4.
            foreach (var stepDir in Directory.GetDirectories(sessionDir))
            {
                var stepName = Path.GetFileName(stepDir);
                if (!int.TryParse(stepName, out int stepIndex))
                    continue;

                foreach (var file in Directory.GetFiles(stepDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var relativeToStep = file
                            .Substring(stepDir.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        // Detect new-file marker by suffix
                        bool isNewFile = relativeToStep.EndsWith(NewFileSuffix, StringComparison.OrdinalIgnoreCase);
                        var effectiveRelative = isNewFile
                            ? relativeToStep.Substring(0, relativeToStep.Length - NewFileSuffix.Length)
                            : relativeToStep;

                        snapshots.Add(new SnapshotInfo
                        {
                            SessionId = sessionId,
                            StepIndex = stepIndex,
                            OriginalFilePath = ReconstructOriginalPath(effectiveRelative),
                            SnapshotFilePath = file,
                            SizeBytes = info.Length,
                            CreatedUtc = info.CreationTimeUtc,
                            IsNewFile = isNewFile
                        });
                    }
                    catch { /* file may have been cleaned up concurrently — skip */ }
                }
            }

            return snapshots;
        }

        // ── GetAllSessionIdsAsync ─────────────────────────────────

        /// <summary>
        /// Returns all session IDs that have snapshot data, ordered by most recent first.
        /// </summary>
        public async Task<List<string>> GetAllSessionIdsAsync()
        {
            if (!Directory.Exists(_baseDirectory))
                return new List<string>();

            // Read-only — no lock needed (see GetSnapshotsAsync comment)
            return Directory.GetDirectories(_baseDirectory)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.CreationTimeUtc)
                .Select(d => d.Name)
                .ToList();
        }

        // ── CleanupExpiredAsync ───────────────────────────────────

        public async Task CleanupExpiredAsync()
        {
            if (!Directory.Exists(_baseDirectory))
                return;

            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Cleanup unit is per-session directory
                foreach (var sessionDir in Directory.GetDirectories(_baseDirectory))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(sessionDir);
                        if (dirInfo.CreationTimeUtc < cutoff)
                        {
                            dirInfo.Delete(recursive: true);
                        }
                    }
                    catch { /* skip individual failures */ }
                }

                EnforceDiskQuota();
            }
            finally
            {
                _ioLock.Release();
            }

            // Phase 4 T10: notify subscribers to invalidate snapshot caches
            SnapshotsCleanedUp?.Invoke();
        }

        // ── GetTotalSnapshotSizeBytesAsync ────────────────────────

        public async Task<long> GetTotalSnapshotSizeBytesAsync()
        {
            if (!Directory.Exists(_baseDirectory))
                return 0;

            // Read-only — no lock needed (see GetSnapshotsAsync comment).
            // Sum may be slightly stale if a concurrent capture/cleanup is in progress;
            // this is acceptable for quota display purposes.
            return Directory.GetFiles(_baseDirectory, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; } // file may vanish during enumeration
                });
        }

        // ── Path helpers ──────────────────────────────────────────

        /// <summary>
        /// Convert an absolute file path to a relative form suitable for snapshot storage.
        /// On Linux/WSL: strips leading '/'.
        /// On Windows: converts 'C:\foo\bar' to 'C/foo/bar'.
        /// Note: UNC paths (\\server\share) are NOT supported. AICA only operates on
        /// local files within the solution directory, so UNC paths are out of scope.
        /// </summary>
        private static string GetRelativePath(string absolutePath)
        {
            // Normalize separators to forward slash for consistent storage
            var normalized = absolutePath.Replace('\\', '/');

            // Strip leading slash(es)
            normalized = normalized.TrimStart('/');

            // Handle Windows drive letter (e.g. "C:/foo" -> "C/foo")
            if (normalized.Length >= 2 && normalized[1] == ':')
            {
                normalized = normalized[0] + normalized.Substring(2);
            }

            return normalized;
        }

        /// <summary>
        /// Reconstruct the original absolute path from a snapshot-relative path.
        /// </summary>
        private static string ReconstructOriginalPath(string relativePath)
        {
            // Normalize to OS separator
            var normalized = relativePath.Replace('\\', '/').TrimStart('/');

            // On Linux/WSL, prepend '/'
            if (Environment.OSVersion.Platform == PlatformID.Unix ||
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                return "/" + normalized;
            }

            // On Windows, restore drive letter (e.g. "C/foo/bar" -> "C:/foo/bar")
            if (normalized.Length >= 2 && normalized[1] == '/')
            {
                return normalized[0] + ":/" + normalized.Substring(2);
            }

            return normalized;
        }

        // ── Maintenance ───────────────────────────────────────────

        private async Task RunMaintenanceIfNeededAsync()
        {
            var now = DateTime.UtcNow;
            if (now - _lastMaintenanceUtc < MaintenanceInterval) return;
            _lastMaintenanceUtc = now;

            try
            {
                await CleanupExpiredAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] SnapshotManager maintenance failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Enforce disk quota by removing oldest sessions first.
        /// Must be called under _ioLock.
        /// </summary>
        private void EnforceDiskQuota()
        {
            if (!Directory.Exists(_baseDirectory)) return;
            var maxBytes = (long)_maxTotalSizeMB * 1024 * 1024;

            // Gather session directories sorted by creation time (oldest first)
            var sessionDirs = Directory.GetDirectories(_baseDirectory)
                .Select(d => new DirectoryInfo(d))
                .OrderBy(d => d.CreationTimeUtc)
                .ToList();

            var totalSize = GetDirectorySize(_baseDirectory);

            int idx = 0;
            while (totalSize > maxBytes && idx < sessionDirs.Count)
            {
                try
                {
                    var dirSize = GetDirectorySize(sessionDirs[idx].FullName);
                    sessionDirs[idx].Delete(recursive: true);
                    totalSize -= dirSize;
                }
                catch { /* skip */ }
                idx++;
            }
        }

        private static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
    }
}
