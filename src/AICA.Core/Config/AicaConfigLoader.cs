using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICA.Core.Config
{
    /// <summary>
    /// Loads AicaConfig from ~/.AICA/config.json.
    /// Missing file or parse errors fall back to all-default config.
    /// </summary>
    public static class AicaConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static AicaConfig Load(string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                path = Path.Combine(home, ".AICA", "config.json");
            }

            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine("[AICA] No config.json found, using defaults");
                return new AicaConfig();
            }

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<AicaConfig>(json, JsonOptions);
                System.Diagnostics.Debug.WriteLine($"[AICA] Config loaded from {path}");
                return config ?? new AicaConfig();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] Failed to load config.json, using defaults: {ex.Message}");
                return new AicaConfig();
            }
        }
    }
}
