using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

        public SafetyGuard(SafetyGuardOptions options)
        {
            _workingDirectory = options?.WorkingDirectory ?? Environment.CurrentDirectory;
            _protectedPaths = new HashSet<string>(
                options?.ProtectedPaths ?? new[] { ".git", ".vs", "node_modules", "bin", "obj" },
                StringComparer.OrdinalIgnoreCase);
            _commandWhitelist = new HashSet<string>(
                options?.CommandWhitelist ?? new[] { "dotnet", "npm", "git", "nuget" },
                StringComparer.OrdinalIgnoreCase);
            _commandBlacklist = new HashSet<string>(
                options?.CommandBlacklist ?? new[] { "rm", "del", "format", "shutdown", "restart" },
                StringComparer.OrdinalIgnoreCase);
            _ignorePatterns = new List<Regex>();

            LoadIgnorePatterns(options?.AicaIgnorePath);
        }

        private void LoadIgnorePatterns(string aicaIgnorePath)
        {
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
            var regex = "^" + Regex.Escape(glob)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/\\\\]*")
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

            // Check ignore patterns
            foreach (var pattern in _ignorePatterns)
            {
                if (pattern.IsMatch(normalizedPath))
                {
                    return PathAccessResult.Denied("Path matches .aicaignore pattern");
                }
            }

            // Check if path is within working directory
            var fullPath = Path.GetFullPath(Path.Combine(_workingDirectory, path));
            if (!fullPath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return PathAccessResult.Denied("Path is outside working directory");
            }

            return PathAccessResult.Allowed();
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

            // Check whitelist
            if (_commandWhitelist.Contains(executable))
            {
                return CommandCheckResult.Safe($"Command '{executable}' is whitelisted");
            }

            // Unknown command - requires approval
            return CommandCheckResult.RequiresApproval($"Command '{executable}' requires user approval");
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
        public string[] ProtectedPaths { get; set; }
        public string[] CommandWhitelist { get; set; }
        public string[] CommandBlacklist { get; set; }
        public string AicaIgnorePath { get; set; }
    }

    public class PathAccessResult
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; }

        public static PathAccessResult Allowed() => new PathAccessResult { IsAllowed = true };
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
