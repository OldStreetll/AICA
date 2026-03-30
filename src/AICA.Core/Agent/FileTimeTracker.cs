using System;
using System.Collections.Concurrent;
using System.IO;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Tracks file read/edit timestamps to detect external modifications.
    /// Session-scoped, in-memory only — no persistence needed.
    /// </summary>
    public class FileTimeTracker
    {
        /// <summary>
        /// Session-scoped singleton instance. Reset when a new session starts.
        /// </summary>
        public static FileTimeTracker Instance { get; } = new FileTimeTracker();

        private readonly ConcurrentDictionary<string, FileSnapshot> _snapshots
            = new ConcurrentDictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Snapshot of a file's state at a point in time.
        /// Uses plain class (not C# 9 record) for netstandard2.0 / older SDK compatibility.
        /// </summary>
        public class FileSnapshot
        {
            public DateTime LastWriteTimeUtc { get; }
            public long FileSize { get; }
            public string OperationType { get; }

            public FileSnapshot(DateTime lastWriteTimeUtc, long fileSize, string operationType)
            {
                LastWriteTimeUtc = lastWriteTimeUtc;
                FileSize = fileSize;
                OperationType = operationType;
            }
        }

        /// <summary>
        /// Record file state after a successful read operation.
        /// </summary>
        public void RecordRead(string filePath)
        {
            RecordSnapshot(filePath, "read");
        }

        /// <summary>
        /// Record file state after a successful edit/write operation.
        /// </summary>
        public void RecordEdit(string filePath)
        {
            RecordSnapshot(filePath, "edit");
        }

        /// <summary>
        /// Check if a file has been modified externally since the last recorded operation.
        /// Returns false if the file was never tracked (no false positives for untracked files).
        /// </summary>
        public bool HasExternalModification(string filePath)
        {
            var key = NormalizePath(filePath);
            if (!_snapshots.TryGetValue(key, out var snapshot))
                return false; // Never tracked — don't flag

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    return true; // File was deleted

                return info.LastWriteTimeUtc != snapshot.LastWriteTimeUtc
                    || info.Length != snapshot.FileSize;
            }
            catch
            {
                return false; // Can't check — don't flag
            }
        }

        /// <summary>
        /// Get the last recorded snapshot for a file, or null if never tracked.
        /// </summary>
        public FileSnapshot GetSnapshot(string filePath)
        {
            var key = NormalizePath(filePath);
            _snapshots.TryGetValue(key, out var snapshot);
            return snapshot;
        }

        private void RecordSnapshot(string filePath, string operationType)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) return;

                var key = NormalizePath(filePath);
                _snapshots[key] = new FileSnapshot(info.LastWriteTimeUtc, info.Length, operationType);
            }
            catch
            {
                // Silently ignore — tracking is best-effort
            }
        }

        private static string NormalizePath(string path)
        {
            // Normalize to full path with consistent casing for dictionary lookup
            try
            {
                return Path.GetFullPath(path).Replace('/', '\\');
            }
            catch
            {
                return path;
            }
        }
    }
}
