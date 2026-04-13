using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICA.Core.Security
{
    /// <summary>
    /// Persistent decision for a tool operation (allow/deny), scoped to a project.
    /// </summary>
    public class PermissionDecision
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("tool")]
        public string Tool { get; set; }

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; }

        [JsonPropertyName("decision")]
        public string Decision { get; set; }

        [JsonPropertyName("projectPath")]
        public string ProjectPath { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// Root object for ~/.AICA/permissions.json
    /// </summary>
    public class PermissionDecisionFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("decisions")]
        public List<PermissionDecision> Decisions { get; set; } = new List<PermissionDecision>();
    }

    /// <summary>
    /// H3b: Cross-session permission decision persistence.
    /// Stores user decisions (always allow / always deny) per project in ~/.AICA/permissions.json.
    /// Thread-safe via lock; atomic writes via temp-file-then-rename.
    /// </summary>
    public class PermissionDecisionStore
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private PermissionDecisionFile _data;

        private static volatile PermissionDecisionStore _current;

        /// <summary>
        /// The live singleton instance created by SafetyGuard at startup.
        /// Used by ResetPermissionsCommand to sync in-memory state after clearing the file.
        /// Null if PermissionPersistence feature is disabled.
        /// </summary>
        public static PermissionDecisionStore Current
        {
            get => _current;
            internal set => _current = value;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new store. Call <see cref="Load"/> to read persisted decisions.
        /// </summary>
        /// <param name="filePath">Override path (default: ~/.AICA/permissions.json)</param>
        public PermissionDecisionStore(string filePath = null)
        {
            _filePath = filePath
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".AICA", "permissions.json");
            _data = new PermissionDecisionFile();
        }

        /// <summary>
        /// Number of decisions currently loaded (across all projects).
        /// </summary>
        public int Count
        {
            get { lock (_lock) return _data.Decisions.Count; }
        }

        /// <summary>
        /// Load decisions from disk. Skips entries whose projectPath directory no longer exists.
        /// Safe to call multiple times (replaces in-memory state).
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                _data = new PermissionDecisionFile();

                if (!File.Exists(_filePath))
                    return;

                try
                {
                    var json = File.ReadAllText(_filePath);
                    var parsed = JsonSerializer.Deserialize<PermissionDecisionFile>(json, JsonOptions);
                    if (parsed?.Decisions == null)
                        return;

                    // Filter out decisions whose project directory no longer exists
                    _data.Version = parsed.Version;
                    _data.Decisions = parsed.Decisions
                        .Where(d => !string.IsNullOrEmpty(d.ProjectPath) && Directory.Exists(d.ProjectPath))
                        .ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] PermissionDecisionStore.Load failed: {ex.Message}");
                    _data = new PermissionDecisionFile();
                }
            }
        }

        /// <summary>
        /// Query a persistent decision for a given tool and project.
        /// Returns "allow", "deny", or null if no decision stored.
        /// </summary>
        /// <param name="tool">Tool name (e.g. "EditFile")</param>
        /// <param name="projectPath">WorkingDirectory absolute path (exact match)</param>
        /// <param name="pattern">Optional glob pattern for finer match; null matches any pattern for the tool.</param>
        public string Query(string tool, string projectPath, string pattern = null)
        {
            if (string.IsNullOrEmpty(tool) || string.IsNullOrEmpty(projectPath))
                return null;

            lock (_lock)
            {
                // Most-specific first: match tool + project + pattern
                foreach (var d in _data.Decisions)
                {
                    if (!string.Equals(d.Tool, tool, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.Equals(d.ProjectPath, projectPath, StringComparison.Ordinal))
                        continue;

                    // If caller specifies a pattern, require exact match
                    if (pattern != null)
                    {
                        if (string.Equals(d.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                            return d.Decision;
                    }
                    else
                    {
                        // No pattern filter — tool-level decision (pattern may be null or "*")
                        if (string.IsNullOrEmpty(d.Pattern) || d.Pattern == "*")
                            return d.Decision;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Store a persistent decision. Overwrites any existing decision for the same tool+project+pattern.
        /// Automatically persists to disk (atomic write).
        /// </summary>
        public void Store(string tool, string projectPath, string decision, string pattern = null)
        {
            if (string.IsNullOrEmpty(tool) || string.IsNullOrEmpty(projectPath) || string.IsNullOrEmpty(decision))
                return;

            lock (_lock)
            {
                // Remove existing matching entry
                _data.Decisions.RemoveAll(d =>
                    string.Equals(d.Tool, tool, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.ProjectPath, projectPath, StringComparison.Ordinal) &&
                    string.Equals(d.Pattern ?? "", pattern ?? "", StringComparison.OrdinalIgnoreCase));

                _data.Decisions.Add(new PermissionDecision
                {
                    Id = Guid.NewGuid().ToString(),
                    Tool = tool,
                    Pattern = pattern,
                    Decision = decision,
                    ProjectPath = projectPath,
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

                Save();
            }
        }

        /// <summary>
        /// Remove all decisions for a given project. Pass null to clear all projects.
        /// </summary>
        public int Reset(string projectPath = null)
        {
            lock (_lock)
            {
                int removed;
                if (projectPath == null)
                {
                    removed = _data.Decisions.Count;
                    _data.Decisions.Clear();
                }
                else
                {
                    removed = _data.Decisions.RemoveAll(d =>
                        string.Equals(d.ProjectPath, projectPath, StringComparison.Ordinal));
                }

                Save();
                return removed;
            }
        }

        /// <summary>
        /// Get all decisions for a specific project (for UI display / telemetry).
        /// </summary>
        public List<PermissionDecision> GetDecisions(string projectPath)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(projectPath))
                    return new List<PermissionDecision>(_data.Decisions);

                return _data.Decisions
                    .Where(d => string.Equals(d.ProjectPath, projectPath, StringComparison.Ordinal))
                    .ToList();
            }
        }

        /// <summary>
        /// Atomic write: serialize → temp file → rename over original.
        /// On failure: attempts recovery from .tmp if original was already deleted.
        /// Always cleans up residual .tmp to prevent stale files.
        /// Must be called under _lock.
        /// </summary>
        private void Save()
        {
            var tmpPath = _filePath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_data, JsonOptions);
                File.WriteAllText(tmpPath, json);

                // netstandard2.0: File.Move doesn't have overwrite param; delete first
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(tmpPath, _filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] PermissionDecisionStore.Save failed: {ex.Message}");

                // Recovery: if original was deleted but rename failed, restore from .tmp
                if (!File.Exists(_filePath) && File.Exists(tmpPath))
                {
                    try
                    {
                        File.Move(tmpPath, _filePath);
                    }
                    catch (Exception rex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] PermissionDecisionStore.Save recovery also failed: {rex.Message}");
                    }
                }
            }
            finally
            {
                // Clean up residual .tmp to avoid confusion on next Load
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch { }
            }
        }
    }
}
