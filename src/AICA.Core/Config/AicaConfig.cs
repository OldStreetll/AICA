using System.Collections.Generic;

namespace AICA.Core.Config
{
    /// <summary>
    /// Central configuration for AICA. Loaded once at startup from ~/.AICA/config.json.
    /// Users only need to specify fields they want to override; unspecified fields keep defaults.
    /// </summary>
    public class AicaConfig
    {
        private static AicaConfig _current;

        /// <summary>
        /// Singleton config instance. Lazy-loaded on first access.
        /// </summary>
        public static AicaConfig Current => _current ?? (_current = AicaConfigLoader.Load());

        /// <summary>
        /// Reset config (for testing or reload).
        /// </summary>
        public static void Reset() => _current = null;

        public AgentConfig Agent { get; set; } = new AgentConfig();
        public CondenseConfig Condense { get; set; } = new CondenseConfig();
        public ToolConfig Tools { get; set; } = new ToolConfig();
        public TelemetryConfig Telemetry { get; set; } = new TelemetryConfig();
        public FeaturesConfig Features { get; set; } = new FeaturesConfig();
        public TruncationConfig Truncation { get; set; } = new TruncationConfig();
        public SnapshotsConfig Snapshots { get; set; } = new SnapshotsConfig();
    }

    public class AgentConfig
    {
        public int DoomLoopThreshold { get; set; } = 3;
        public int MaxRetries { get; set; } = 2;
        public int MaxUserCancellations { get; set; } = 3;
    }

    public class CondenseConfig
    {
        public int MinMessageThreshold { get; set; } = 18;
        public int MinCompressibleThreshold { get; set; } = 12;
    }

    public class ToolConfig
    {
        public int GrepRipgrepThreshold { get; set; } = 200;
        public int GrepTimeoutSeconds { get; set; } = 30;
        public int CommandDefaultTimeoutSeconds { get; set; } = 30;
        public int GitNexusStartTimeoutMs { get; set; } = 15000;

        /// <summary>Timeout in milliseconds for GitNexus npm install (first-time only). Default: 5 minutes.</summary>
        public int NpmInstallTimeoutMs { get; set; } = 300000;

        public List<string> ExcludeDirectories { get; set; } = new List<string>
        {
            ".git", ".vs", "bin", "obj", "node_modules", "packages", ".nuget", "TestResults",
            "Debug", "Release", "RelWithDebInfo", "MinSizeRel", "x64", "x86"
        };

        public List<string> ExcludeExtensions { get; set; } = new List<string>
        {
            ".exe", ".dll", ".pdb", ".obj", ".o", ".lib", ".so", ".a",
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
            ".zip", ".tar", ".gz", ".rar", ".7z", ".bz2",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx",
            ".vsix", ".nupkg", ".snk",
            ".tlog", ".log", ".cache", ".ilk", ".idb", ".ipch", ".sdf", ".suo",
            ".pch", ".ncb", ".opensdf", ".res", ".lastbuildstate"
        };
    }

    public class TelemetryConfig
    {
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// v2.1 H1: Configuration for tool output truncation persistence.
    /// </summary>
    public class TruncationConfig
    {
        /// <summary>Total disk quota for truncated outputs in MB.</summary>
        public int MaxTotalSizeMB { get; set; } = 200;

        /// <summary>Days to retain truncated output files before auto-cleanup.</summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>Default number of characters to include in the preview when truncating.</summary>
        public int DefaultPreviewChars { get; set; } = 4000;

        /// <summary>
        /// Per-tool preview character overrides. Key = tool name (case-insensitive at lookup).
        /// Tools not listed here fall back to DefaultPreviewChars.
        /// </summary>
        public Dictionary<string, int> ToolPreviewChars { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// v2.1 H2: Configuration for file snapshot and rollback.
    /// </summary>
    public class SnapshotsConfig
    {
        /// <summary>Total disk quota for snapshots in MB.</summary>
        public int MaxTotalSizeMB { get; set; } = 500;

        /// <summary>Days to retain snapshot sessions before auto-cleanup.</summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>Maximum file size in bytes that can be snapshotted. Files larger are skipped.</summary>
        public long MaxFileSizeBytes { get; set; } = 2 * 1024 * 1024; // 2MB
    }
}
