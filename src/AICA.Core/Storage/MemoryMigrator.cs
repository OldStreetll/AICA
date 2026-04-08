using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Rules.Parsers;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Migrates legacy memory files (plain markdown without YAML frontmatter)
    /// to the structured format required by OH2.
    /// Safety: backs up originals before migration, uses atomic file replacement.
    /// </summary>
    internal class MemoryMigrator
    {
        private readonly YamlFrontmatterParser _parser = new YamlFrontmatterParser();

        /// <summary>
        /// Scan memory directory for files without YAML frontmatter and migrate them.
        /// Returns the number of files migrated.
        /// </summary>
        public Task<int> MigrateIfNeededAsync(string memoryDir, CancellationToken ct)
        {
            int migrated = 0;

            if (string.IsNullOrEmpty(memoryDir) || !Directory.Exists(memoryDir))
                return Task.FromResult(0);

            var mdFiles = Directory.GetFiles(memoryDir, "*.md");
            if (mdFiles.Length == 0)
                return Task.FromResult(0);

            // Prepare backup directory
            string backupDir = memoryDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_backup";

            foreach (var filePath in mdFiles)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var content = File.ReadAllText(filePath, Encoding.UTF8);
                    var result = _parser.Parse(content);

                    if (result.HadFrontmatter)
                        continue;

                    // Needs migration — ensure backup directory exists
                    if (!Directory.Exists(backupDir))
                    {
                        Directory.CreateDirectory(backupDir);
                    }

                    // Backup original file
                    var fileName = Path.GetFileName(filePath);
                    var backupPath = Path.Combine(backupDir, fileName);
                    File.Copy(filePath, backupPath, true);

                    // Derive metadata
                    var name = DeriveName(fileName);
                    var description = ExtractDescription(content);

                    // Build migrated content
                    var sb = new StringBuilder();
                    sb.AppendLine("---");
                    sb.AppendLine("name: " + name);
                    sb.AppendLine("description: " + description);
                    sb.AppendLine("type: project");
                    sb.AppendLine("---");
                    sb.AppendLine();
                    sb.Append(content);

                    var migratedContent = sb.ToString();

                    // Atomic replacement: write to .tmp then move
                    var tmpPath = filePath + ".tmp";
                    try
                    {
                        File.WriteAllText(tmpPath, migratedContent, Encoding.UTF8);
                        // File.Move does not overwrite on .NET Framework — delete first
                        File.Delete(filePath);
                        File.Move(tmpPath, filePath);
                        migrated++;
                    }
                    catch
                    {
                        // Cleanup temp file if it exists
                        if (File.Exists(tmpPath))
                        {
                            try { File.Delete(tmpPath); }
                            catch { /* best effort */ }
                        }

                        // Restore from backup
                        if (File.Exists(backupPath))
                        {
                            try { File.Copy(backupPath, filePath, true); }
                            catch { /* best effort */ }
                        }

                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Log and continue with next file — don't let one failure block all migrations
                    System.Diagnostics.Debug.WriteLine(
                        "[AICA] MemoryMigrator: failed to migrate " + Path.GetFileName(filePath) + ": " + ex.Message);
                }
            }

            return Task.FromResult(migrated);
        }

        /// <summary>
        /// Derive a human-readable name from the filename.
        /// "my_project_notes.md" → "my project notes"
        /// </summary>
        private static string DeriveName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return name.Replace('_', ' ').Replace('-', ' ');
        }

        /// <summary>
        /// Extract a description from the file content.
        /// If first line is a markdown heading, use the heading text.
        /// Otherwise, take the first 80 characters of the first non-empty line.
        /// </summary>
        private static string ExtractDescription(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "migrated memory";

            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Markdown heading
                if (trimmed.StartsWith("#"))
                {
                    var headingText = trimmed.TrimStart('#').Trim();
                    if (!string.IsNullOrEmpty(headingText))
                        return headingText;
                }

                // First non-empty line — take up to 80 chars
                if (trimmed.Length <= 80)
                    return trimmed;
                return trimmed.Substring(0, 80);
            }

            return "migrated memory";
        }
    }
}
