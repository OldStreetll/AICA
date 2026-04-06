using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Storage
{
    /// <summary>
    /// 3.7: Persists and loads TaskProgress for checkpoint resume.
    /// Storage location: {workingDirectory}/.aica/progress/latest.json
    /// </summary>
    public static class TaskProgressStore
    {
        private const string ProgressDir = ".aica/progress";
        private const string ProgressFile = "latest.json";

        /// <summary>
        /// Save task progress to disk.
        /// </summary>
        public static async Task SaveAsync(string workingDirectory, TaskProgress progress, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(workingDirectory) || progress == null)
                return;

            try
            {
                var dir = Path.Combine(workingDirectory, ProgressDir);
                Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, ProgressFile);
                var json = JsonSerializer.Serialize(progress, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] TaskProgressStore.Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Load task progress from disk. Returns null if not found.
        /// </summary>
        public static async Task<TaskProgress> LoadAsync(string workingDirectory, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            try
            {
                var filePath = Path.Combine(workingDirectory, ProgressDir, ProgressFile);
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<TaskProgress>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] TaskProgressStore.Load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Delete saved progress (after successful completion).
        /// </summary>
        public static void Delete(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return;

            try
            {
                var filePath = Path.Combine(workingDirectory, ProgressDir, ProgressFile);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] TaskProgressStore.Delete failed: {ex.Message}");
            }
        }
    }
}
