using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AICA.Core.Rules.Parsers
{
    /// <summary>
    /// Matches file paths against glob patterns.
    /// Supports patterns like: src/**, *.ts, src/**/test.cs, etc.
    /// </summary>
    public class PathMatcher
    {
        /// <summary>
        /// Check if any of the given paths match any of the glob patterns.
        /// </summary>
        public bool MatchAny(List<string> patterns, List<string> paths)
        {
            if (patterns == null || patterns.Count == 0)
                return false;

            if (paths == null || paths.Count == 0)
                return false;

            foreach (var pattern in patterns)
            {
                foreach (var path in paths)
                {
                    if (Match(pattern, path))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a path matches a glob pattern.
        /// </summary>
        public bool Match(string pattern, string path)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(path))
                return false;

            // Normalize paths to forward slashes
            pattern = pattern.Replace("\\", "/");
            path = path.Replace("\\", "/");

            // Convert glob pattern to regex
            var regex = GlobToRegex(pattern);
            return Regex.IsMatch(path, regex, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Extract potential file paths from text.
        /// Uses heuristics to identify relative paths and filenames.
        /// </summary>
        public List<string> ExtractPathCandidates(string text)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(text))
                return new List<string>();

            // Pattern 1: Relative paths with slashes (src/index.ts, apps/web/src/App.tsx)
            var relativePathPattern = @"(?:^|\s|[""'`])([a-zA-Z0-9._\-/]+/[a-zA-Z0-9._\-/]*)(?:\s|[""'`]|$)";
            foreach (Match match in Regex.Matches(text, relativePathPattern))
            {
                var path = match.Groups[1].Value.Trim();
                if (IsValidPath(path))
                    candidates.Add(path);
            }

            // Pattern 2: File names with extensions (index.ts, App.tsx, README.md)
            var fileNamePattern = @"(?:^|\s|[""'`])([a-zA-Z0-9._\-]+\.[a-zA-Z0-9]+)(?:\s|[""'`]|$)";
            foreach (Match match in Regex.Matches(text, fileNamePattern))
            {
                var path = match.Groups[1].Value.Trim();
                if (IsValidPath(path))
                    candidates.Add(path);
            }

            return candidates.ToList();
        }

        /// <summary>
        /// Check if a string looks like a valid file path.
        /// </summary>
        private bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Exclude common non-path patterns
            var excludePatterns = new[]
            {
                "http://", "https://", "ftp://",  // URLs
                "mailto:",                         // Email
                "javascript:",                     // JS protocol
                "data:",                           // Data URI
            };

            foreach (var pattern in excludePatterns)
            {
                if (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Must contain at least one valid path character
            return path.Any(c => c == '/' || c == '\\' || c == '.');
        }

        /// <summary>
        /// Convert a glob pattern to a regex pattern.
        /// </summary>
        private string GlobToRegex(string glob)
        {
            var regex = new System.Text.StringBuilder("^");
            int i = 0;

            while (i < glob.Length)
            {
                var ch = glob[i];

                switch (ch)
                {
                    case '*':
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            // ** matches any number of directories
                            if (i + 2 < glob.Length && glob[i + 2] == '/')
                            {
                                regex.Append("(?:.*/)?");
                                i += 3;
                            }
                            else if (i + 2 == glob.Length)
                            {
                                regex.Append(".*");
                                i += 2;
                            }
                            else
                            {
                                regex.Append("\\*\\*");
                                i += 2;
                            }
                        }
                        else
                        {
                            // * matches anything except /
                            regex.Append("[^/]*");
                            i++;
                        }
                        break;

                    case '?':
                        // ? matches any single character except /
                        regex.Append("[^/]");
                        i++;
                        break;

                    case '.':
                    case '+':
                    case '^':
                    case '$':
                    case '(':
                    case ')':
                    case '[':
                    case ']':
                    case '{':
                    case '}':
                    case '|':
                    case '\\':
                        // Escape regex special characters
                        regex.Append("\\").Append(ch);
                        i++;
                        break;

                    default:
                        regex.Append(ch);
                        i++;
                        break;
                }
            }

            regex.Append("$");
            return regex.ToString();
        }
    }
}
