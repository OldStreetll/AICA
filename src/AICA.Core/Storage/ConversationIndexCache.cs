using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Manages an index.json cache of ConversationSummary entries.
    /// Speeds up ListConversations from O(n) full-file reads to O(1) index read.
    /// Auto-rebuilds on corruption or missing file.
    /// </summary>
    public class ConversationIndexCache
    {
        private readonly string _indexPath;
        private readonly string _storageDir;
        private List<ConversationSummary> _cache;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public ConversationIndexCache(string storageDir)
        {
            _storageDir = storageDir;
            _indexPath = Path.Combine(storageDir, "index.json");
        }

        public List<ConversationSummary> Load()
        {
            if (_cache != null) return _cache;

            if (File.Exists(_indexPath))
            {
                try
                {
                    var json = File.ReadAllText(_indexPath);
                    _cache = JsonSerializer.Deserialize<List<ConversationSummary>>(json, JsonOptions);
                    if (_cache != null) return _cache;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Index cache corrupt, rebuilding: {ex.Message}");
                }
            }

            _cache = Rebuild();
            return _cache;
        }

        public void Update(ConversationSummary summary)
        {
            var entries = Load();
            var existing = entries.FindIndex(e => e.Id == summary.Id);
            if (existing >= 0)
                entries[existing] = summary;
            else
                entries.Add(summary);
            Save(entries);
        }

        public void Remove(string id)
        {
            var entries = Load();
            entries.RemoveAll(e => e.Id == id);
            Save(entries);
        }

        public List<ConversationSummary> Rebuild()
        {
            var entries = new List<ConversationSummary>();

            if (!Directory.Exists(_storageDir))
            {
                _cache = entries;
                return entries;
            }

            var readOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            foreach (var file in Directory.GetFiles(_storageDir, "*.json"))
            {
                if (Path.GetFileName(file) == "index.json") continue;
                try
                {
                    var json = File.ReadAllText(file);
                    var record = JsonSerializer.Deserialize<ConversationRecord>(json, readOptions);
                    if (record != null)
                    {
                        entries.Add(new ConversationSummary
                        {
                            Id = record.Id,
                            Title = record.Title,
                            CreatedAt = record.CreatedAt,
                            UpdatedAt = record.UpdatedAt,
                            MessageCount = record.Messages?.Count ?? 0,
                            WorkingDirectory = record.WorkingDirectory,
                            ProjectPath = record.ProjectPath,
                            ProjectName = record.ProjectName,
                            SolutionPath = record.SolutionPath
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Failed to index {file}: {ex.Message}");
                }
            }

            _cache = entries;
            Save(entries);
            System.Diagnostics.Debug.WriteLine($"[AICA] Rebuilt conversation index: {entries.Count} entries");
            return entries;
        }

        private void Save(List<ConversationSummary> entries)
        {
            _cache = entries;
            try
            {
                var json = JsonSerializer.Serialize(entries, JsonOptions);
                var tempPath = _indexPath + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(_indexPath)) File.Delete(_indexPath);
                File.Move(tempPath, _indexPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Failed to save index: {ex.Message}");
            }
        }
    }
}
