using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AICA.Core.Config;

namespace AICA.Core.Security
{
    /// <summary>
    /// Safety guard for file and command operations
    /// </summary>
    public class SafetyGuard
    {
        private readonly HashSet<string> _protectedPaths;
        private readonly HashSet<string> _commandWhitelist;
        private readonly HashSet<string> _commandBlacklist;
        private readonly List<Regex> _ignorePatterns;
        private readonly string _workingDirectory;
        private readonly HashSet<string> _sourceRoots;
        private readonly PermissionRuleEngine _ruleEngine;
        private readonly PermissionDecisionStore _decisionStore;

        /// <summary>
        /// Tools that are never eligible for "always allow" persistent decisions.
        /// RunCommand is entirely blocked; other dangerous tools can only persist "always deny".
        /// This is the single source of truth — PermissionDecisionStore references it.
        /// </summary>
        public static readonly HashSet<string> DangerousTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RunCommand"
        };

        /// <summary>v2.3: Permission rule engine for glob+action+level checks</summary>
        public PermissionRuleEngine RuleEngine => _ruleEngine;

        /// <summary>v2.1 H3b: Persistent permission decision store</summary>
        public PermissionDecisionStore DecisionStore => _decisionStore;

        /// <summary>Current working directory for this guard instance</summary>
        public string WorkingDirectory => _workingDirectory;

        public SafetyGuard(SafetyGuardOptions options)
        {
            _workingDirectory = options?.WorkingDirectory ?? Environment.CurrentDirectory;
            _ruleEngine = new PermissionRuleEngine(options?.PermissionRules);
            _sourceRoots = new HashSet<string>(
                (options?.SourceRoots ?? Array.Empty<string>())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Select(r => Path.GetFullPath(r).TrimEnd('\\', '/')),
                StringComparer.OrdinalIgnoreCase);
            _protectedPaths = new HashSet<string>(
                options?.ProtectedPaths ?? new[] { ".git", ".vs", "node_modules", "bin", "obj" },
                StringComparer.OrdinalIgnoreCase);
            _commandWhitelist = new HashSet<string>(
                options?.CommandWhitelist ?? new[] { "dotnet", "npm", "git", "nuget" },
                StringComparer.OrdinalIgnoreCase);
            _commandBlacklist = new HashSet<string>(
                options?.CommandBlacklist ?? new[]
                {
                    "rm", "del", "format", "shutdown", "restart",
                    "rmdir", "rd",
                    "Remove-Item", "Stop-Process", "Stop-Service"
                },
                StringComparer.OrdinalIgnoreCase);
            _ignorePatterns = new List<Regex>();

            LoadIgnorePatterns(options?.AicaIgnorePath);

            // v2.1 H3b: Load persistent permission decisions if feature enabled
            if (Config.AicaConfig.Current.Features.PermissionPersistence)
            {
                _decisionStore = options?.DecisionStore ?? new PermissionDecisionStore();
                _decisionStore.Load();
                PermissionDecisionStore.Current = _decisionStore;
            }
        }

        /// <summary>
        /// v2.1 H3b: Check if a tool has a persistent "always allow" decision for the current project.
        /// Returns true if allowed, false if denied, null if no decision stored.
        /// Respects DangerousTools constraints.
        /// </summary>
        public bool? CheckPersistentDecision(string toolName)
        {
            if (_decisionStore == null || string.IsNullOrEmpty(toolName))
                return null;

            var decision = _decisionStore.Query(toolName, _workingDirectory);
            if (decision == null)
                return null;

            if (string.Equals(decision, "allow", StringComparison.OrdinalIgnoreCase))
            {
                // Safety: DangerousTools can never have "always allow"
                if (DangerousTools.Contains(toolName))
                    return null;
                return true;
            }

            if (string.Equals(decision, "deny", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private void LoadIgnorePatterns(string aicaIgnorePath)
        {
            // Empty string = explicitly disabled (RespectAicaIgnore=false)
            if (aicaIgnorePath != null && aicaIgnorePath.Length == 0) return;

            var ignoreFile = aicaIgnorePath ?? Path.Combine(_workingDirectory, ".aicaignore");
            if (!File.Exists(ignoreFile)) return;

            try
            {
                var lines = File.ReadAllLines(ignoreFile);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    var pattern = ConvertGlobToRegex(trimmed);
                    _ignorePatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
            }
            catch { }
        }

        private string ConvertGlobToRegex(string glob)
        {
            // Normalize directory separators to match NormalizePath behavior
            var normalized = glob.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar);

            // Trailing separator = directory pattern (match dir and everything inside, like .gitignore)
            var sep = Path.DirectorySeparatorChar;
            if (normalized.EndsWith(sep.ToString()))
            {
                normalized = normalized.TrimEnd(sep);
                var escaped = Regex.Escape(normalized);
                var sepEscaped = sep == '\\' ? @"\\" : "/";
                return $"^{escaped}({sepEscaped}.*)?$";
            }

            // Build negated character class for single * (must not cross directory boundaries)
            var sepE = sep == '\\' ? @"\\" : "/";
            var regex = "^" + Regex.Escape(normalized)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", $"[^{sepE}]*")
                .Replace("\\?", ".") + "$";
            return regex;
        }

        /// <summary>
        /// Check if a file path is accessible
        /// </summary>
        public PathAccessResult CheckPathAccess(string path)
        {
            if (string.IsNullOrEmpty(path))
                return PathAccessResult.Denied("Path is empty");

            var normalizedPath = NormalizePath(path);

            // Check protected paths
            foreach (var protectedPath in _protectedPaths)
            {
                if (normalizedPath.Contains(Path.DirectorySeparatorChar + protectedPath + Path.DirectorySeparatorChar) ||
                    normalizedPath.EndsWith(Path.DirectorySeparatorChar + protectedPath) ||
                    normalizedPath.StartsWith(protectedPath + Path.DirectorySeparatorChar))
                {
                    return PathAccessResult.Denied($"Path is protected: {protectedPath}");
                }
            }

            // Check ignore patterns (match against relative path from working directory)
            if (_ignorePatterns.Count > 0)
            {
                var pathToCheck = normalizedPath;

                // Convert absolute path to relative for pattern matching
                var workDirPrefix = _workingDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (normalizedPath.StartsWith(workDirPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    pathToCheck = normalizedPath.Substring(workDirPrefix.Length);
                }

                foreach (var pattern in _ignorePatterns)
                {
                    if (pattern.IsMatch(pathToCheck))
                    {
                        return PathAccessResult.Denied("Path matches .aicaignore pattern");
                    }
                }
            }

            // Check if path is within working directory or source roots
            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(_workingDirectory, path));
            }
            catch
            {
                return PathAccessResult.Denied("Invalid path");
            }

            // Normalize for directory-boundary-safe comparisons
            var normalizedFull = fullPath.TrimEnd('\\', '/');
            var normalizedFullSlash = normalizedFull + "\\";
            var workDirNorm = _workingDirectory.TrimEnd('\\', '/');
            var workDirSlash = workDirNorm + "\\";

            // Check if path is the working directory or within it
            if (normalizedFull.Equals(workDirNorm, StringComparison.OrdinalIgnoreCase) ||
                normalizedFullSlash.StartsWith(workDirSlash, StringComparison.OrdinalIgnoreCase))
                return PathAccessResult.Allowed();

            // Check SourceRoots: exact match, child path, or parent path
            foreach (var root in _sourceRoots)
            {
                var rootNorm = root.TrimEnd('\\', '/');
                var rootSlash = rootNorm + "\\";

                // Exact match: path IS a source root
                if (normalizedFull.Equals(rootNorm, StringComparison.OrdinalIgnoreCase))
                    return PathAccessResult.AllowedExternal(root);

                // Child: path is within a source root
                if (normalizedFullSlash.StartsWith(rootSlash, StringComparison.OrdinalIgnoreCase))
                    return PathAccessResult.AllowedExternal(root);

                // Parent: path is an ancestor of a source root (needed for navigation)
                if (rootNorm.StartsWith(normalizedFullSlash, StringComparison.OrdinalIgnoreCase))
                    return PathAccessResult.AllowedExternal(root);
            }

            return PathAccessResult.Denied("Path is outside working directory and source roots");
        }

        /// <summary>
        /// Check if a command is safe to execute
        /// </summary>
        public CommandCheckResult CheckCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return CommandCheckResult.Denied("Command is empty");

            var parts = command.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return CommandCheckResult.Denied("Command is empty");

            var executable = Path.GetFileNameWithoutExtension(parts[0]).ToLowerInvariant();

            // Check blacklist first
            if (_commandBlacklist.Contains(executable))
            {
                return CommandCheckResult.Denied($"Command '{executable}' is blacklisted");
            }

            // Check for dangerous argument patterns in the full command
            if (IsDangerousCommandPattern(command))
            {
                return CommandCheckResult.Denied($"Command contains dangerous pattern: '{command}'");
            }

            // Check whitelist
            if (_commandWhitelist.Contains(executable))
            {
                return CommandCheckResult.Safe($"Command '{executable}' is whitelisted");
            }

            // Unknown command - requires approval
            return CommandCheckResult.RequiresApproval($"Command '{executable}' requires user approval");
        }

        /// <summary>
        /// v2.3: Check path access with permission rule overlay.
        /// First evaluates glob+action rules; falls back to standard CheckPathAccess if no rule matches.
        /// </summary>
        public PathAccessResult CheckPathAccessWithRules(string path, PermissionAction action)
        {
            // 1. Standard path checks (protected paths, .aicaignore, directory boundaries)
            var baseResult = CheckPathAccess(path);
            if (!baseResult.IsAllowed)
                return baseResult;

            // 2. Permission rule evaluation (glob + action + level)
            var relativePath = path;
            var workDirPrefix = _workingDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            try
            {
                var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_workingDirectory, path));
                if (fullPath.StartsWith(workDirPrefix, StringComparison.OrdinalIgnoreCase))
                    relativePath = fullPath.Substring(workDirPrefix.Length);
            }
            catch { }

            var ruleResult = _ruleEngine.Evaluate(relativePath, action);
            if (ruleResult != null)
            {
                switch (ruleResult.Level)
                {
                    case PermissionLevel.Deny:
                        return PathAccessResult.Denied(
                            ruleResult.Reason ?? $"Denied by permission rule: {ruleResult.MatchedPattern}");
                    case PermissionLevel.Ask:
                        // Allowed but flagged for confirmation
                        var askResult = baseResult;
                        askResult.Reason = ruleResult.Reason ?? $"Requires confirmation (rule: {ruleResult.MatchedPattern})";
                        return askResult;
                    case PermissionLevel.Allow:
                        return baseResult; // Explicitly allowed
                }
            }

            return baseResult;
        }

        private static bool IsDangerousCommandPattern(string command)
        {
            var lower = command.ToLowerInvariant();
            if (lower.Contains("remove-item") && lower.Contains("-recurse"))
                return true;
            if (lower.Contains("rmdir") && lower.Contains("/s"))
                return true;
            if ((lower.Contains(" rd ") || lower.StartsWith("rd ")) && lower.Contains("/s"))
                return true;
            if (Regex.IsMatch(lower, @"format\s+[a-z]:"))
                return true;
            return false;
        }

        private string NormalizePath(string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar)
                       .Replace('\\', Path.DirectorySeparatorChar)
                       .TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    public class SafetyGuardOptions
    {
        public string WorkingDirectory { get; set; }
        public string[] SourceRoots { get; set; }
        public string[] ProtectedPaths { get; set; }
        public string[] CommandWhitelist { get; set; }
        public string[] CommandBlacklist { get; set; }
        public string AicaIgnorePath { get; set; }
        /// <summary>v2.3: Permission rules (glob + action + level)</summary>
        public List<PermissionRule> PermissionRules { get; set; }
        /// <summary>v2.1 H3b: Pre-built decision store (for testing). If null, auto-created.</summary>
        public PermissionDecisionStore DecisionStore { get; set; }
    }

    #region v2.3: Permission Rule System (glob + action + three-level control)

    /// <summary>
    /// Action category for permission rules.
    /// </summary>
    public enum PermissionAction
    {
        Read,
        Write,
        Execute
    }

    /// <summary>
    /// Permission level (three-level control).
    /// </summary>
    public enum PermissionLevel
    {
        Allow,
        Ask,
        Deny
    }

    /// <summary>
    /// A permission rule matching glob pattern + action to a permission level.
    /// Rules are evaluated in order; first match wins.
    /// </summary>
    public class PermissionRule
    {
        /// <summary>Glob pattern (e.g., "*.cpp", "src/**", ".env*")</summary>
        public string Pattern { get; set; }
        /// <summary>Action this rule applies to</summary>
        public PermissionAction Action { get; set; }
        /// <summary>Permission level when matched</summary>
        public PermissionLevel Level { get; set; }
        /// <summary>Optional human-readable reason</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// v2.3: Evaluates permission rules (glob + action + three-level control).
    /// First-match-wins ordering. Falls back to default behavior if no rule matches.
    /// </summary>
    public class PermissionRuleEngine
    {
        private readonly List<(Regex Pattern, PermissionAction Action, PermissionLevel Level, string Reason)> _compiledRules;

        public PermissionRuleEngine(IEnumerable<PermissionRule> rules)
        {
            _compiledRules = new List<(Regex, PermissionAction, PermissionLevel, string)>();

            if (rules == null) return;

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
                var regex = GlobToRegex(rule.Pattern);
                _compiledRules.Add((
                    new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    rule.Action, rule.Level, rule.Reason));
            }
        }

        /// <summary>
        /// Evaluate a path + action against the rule set.
        /// Returns null if no rule matches (caller uses default behavior).
        /// </summary>
        public PermissionEvalResult Evaluate(string relativePath, PermissionAction action)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            var normalized = relativePath.Replace('\\', '/');

            foreach (var rule in _compiledRules)
            {
                if (rule.Action != action)
                    continue;

                if (rule.Pattern.IsMatch(normalized))
                {
                    return new PermissionEvalResult
                    {
                        Level = rule.Level,
                        MatchedPattern = rule.Pattern.ToString(),
                        Reason = rule.Reason
                    };
                }
            }

            return null; // No rule matched — use default
        }

        private static string GlobToRegex(string glob)
        {
            var normalized = glob.Replace('\\', '/');
            var regex = "^" + Regex.Escape(normalized)
                .Replace("\\*\\*\\/", "(.*/)?")
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", "[^/]") + "$";
            return regex;
        }
    }

    public class PermissionEvalResult
    {
        public PermissionLevel Level { get; set; }
        public string MatchedPattern { get; set; }
        public string Reason { get; set; }
    }

    #endregion

    public class PathAccessResult
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; }

        /// <summary>
        /// Whether this path is external (in SourceRoots, not working directory).
        /// Write operations on external paths should require extra confirmation.
        /// </summary>
        public bool IsExternal { get; set; }

        /// <summary>
        /// The source root that contains this path (if external).
        /// </summary>
        public string SourceRoot { get; set; }

        public static PathAccessResult Allowed() => new PathAccessResult { IsAllowed = true };
        public static PathAccessResult AllowedExternal(string sourceRoot) => new PathAccessResult { IsAllowed = true, IsExternal = true, SourceRoot = sourceRoot };
        public static PathAccessResult Denied(string reason) => new PathAccessResult { IsAllowed = false, Reason = reason };
    }

    public class CommandCheckResult
    {
        public CommandSafetyLevel Level { get; set; }
        public string Reason { get; set; }

        public static CommandCheckResult Safe(string reason) => new CommandCheckResult { Level = CommandSafetyLevel.Safe, Reason = reason };
        public static CommandCheckResult RequiresApproval(string reason) => new CommandCheckResult { Level = CommandSafetyLevel.RequiresApproval, Reason = reason };
        public static CommandCheckResult Denied(string reason) => new CommandCheckResult { Level = CommandSafetyLevel.Denied, Reason = reason };
    }

    public enum CommandSafetyLevel
    {
        Safe,
        RequiresApproval,
        Denied
    }
}
