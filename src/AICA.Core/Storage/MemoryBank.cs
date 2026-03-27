using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Storage
{
    /// <summary>
    /// 3.8: Cross-session memory bank.
    /// Stores project background information in .aica/memory/ as Markdown files.
    /// Loaded into the system prompt at session start.
    /// </summary>
    public static class MemoryBank
    {
        private const string MemoryDir = ".aica/memory";

        /// <summary>
        /// Load all memory entries from the project's .aica/memory/ directory.
        /// Returns a combined string suitable for system prompt injection.
        /// </summary>
        public static async Task<string> LoadAsync(string workingDirectory, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            var dir = Path.Combine(workingDirectory, MemoryDir);
            if (!Directory.Exists(dir))
                return null;

            try
            {
                var files = Directory.GetFiles(dir, "*.md")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                    return null;

                var entries = new List<string>();
                int totalChars = 0;
                const int maxTotalChars = 4000; // Limit memory injection to ~1000 tokens

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    if (totalChars + content.Length > maxTotalChars)
                        break;

                    var name = Path.GetFileNameWithoutExtension(file);
                    entries.Add($"### {name}\n{content.Trim()}");
                    totalChars += content.Length;
                }

                return entries.Count > 0 ? string.Join("\n\n", entries) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] MemoryBank.Load failed: {ex.Message}");
                return null;
            }
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
                System.Diagnostics.Debug.WriteLine($"[AICA] MemoryBank.Save failed: {ex.Message}");
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
