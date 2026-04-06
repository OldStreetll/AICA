using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Rules.Models;
using AICA.Core.Rules.Parsers;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Rules
{
    /// <summary>
    /// Loads rules from the file system (local workspace and global directories).
    /// </summary>
    public class RuleLoader
    {
        private readonly ILogger<RuleLoader> _logger;
        private readonly YamlFrontmatterParser _parser;

        // Default rule directories
        private const string LocalRulesDirectory = ".aica-rules";
        private const string GlobalRulesDirectory = ".aica";
        private const string GlobalRulesSubdirectory = "rules";

        public RuleLoader(ILogger<RuleLoader> logger = null)
        {
            _logger = logger;
            _parser = new YamlFrontmatterParser();
        }

        /// <summary>
        /// Load rules from the local workspace .aica-rules directory.
        /// </summary>
        public async Task<List<Rule>> LoadLocalRulesAsync(
            string workspacePath,
            CancellationToken ct = default)
        {
            var rulesPath = Path.Combine(workspacePath, LocalRulesDirectory);
            return await LoadRulesFromDirectoryAsync(rulesPath, RuleSource.Workspace, ct);
        }

        /// <summary>
        /// Load rules from the global ~/.aica/rules directory.
        /// </summary>
        public async Task<List<Rule>> LoadGlobalRulesAsync(CancellationToken ct = default)
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rulesPath = Path.Combine(homeDir, GlobalRulesDirectory, GlobalRulesSubdirectory);
            return await LoadRulesFromDirectoryAsync(rulesPath, RuleSource.Global, ct);
        }

        /// <summary>
        /// Load all rules from a directory (recursively).
        /// </summary>
        private async Task<List<Rule>> LoadRulesFromDirectoryAsync(
            string directoryPath,
            RuleSource source,
            CancellationToken ct = default)
        {
            var rules = new List<Rule>();

            if (!Directory.Exists(directoryPath))
            {
                _logger?.LogDebug($"Rules directory not found: {directoryPath}");
                return rules;
            }

            try
            {
                var files = Directory.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories);
                _logger?.LogDebug($"Found {files.Length} rule files in {directoryPath}");

                foreach (var filePath in files)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var rule = await LoadRuleFileAsync(filePath, source, ct);
                        if (rule != null)
                        {
                            rules.Add(rule);
                            _logger?.LogDebug($"Loaded rule: {rule.Id} from {filePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"Failed to load rule file {filePath}: {ex.Message}");
                        // Continue loading other rules
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error loading rules from {directoryPath}: {ex.Message}");
            }

            return rules;
        }

        /// <summary>
        /// Load a single rule file.
        /// </summary>
        private async Task<Rule> LoadRuleFileAsync(
            string filePath,
            RuleSource source,
            CancellationToken ct = default)
        {
            string content;
            using (var reader = new StreamReader(filePath))
            {
                content = await reader.ReadToEndAsync();
            }
            var parseResult = _parser.Parse(content);

            var rule = new Rule
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                Name = Path.GetFileNameWithoutExtension(filePath),
                Content = parseResult.Body,
                FilePath = filePath,
                Source = source,
                Priority = (int)source,
                LoadedAt = DateTime.UtcNow
            };

            // Extract metadata from frontmatter
            if (parseResult.HadFrontmatter && parseResult.Data != null)
            {
                // Extract paths
                if (parseResult.Data.TryGetValue("paths", out var pathsObj))
                {
                    if (pathsObj is List<object> pathsList)
                    {
                        rule.Metadata.Paths = pathsList
                            .OfType<string>()
                            .ToList();
                    }
                }

                // Extract priority override
                if (parseResult.Data.TryGetValue("priority", out var priorityObj))
                {
                    if (priorityObj is int priority)
                    {
                        rule.Priority = priority;
                    }
                    else if (int.TryParse(priorityObj?.ToString(), out var parsedPriority))
                    {
                        rule.Priority = parsedPriority;
                    }
                }

                // Extract enabled flag
                if (parseResult.Data.TryGetValue("enabled", out var enabledObj))
                {
                    if (enabledObj is bool enabled)
                    {
                        rule.Enabled = enabled;
                    }
                    else if (bool.TryParse(enabledObj?.ToString(), out var parsedEnabled))
                    {
                        rule.Enabled = parsedEnabled;
                    }
                }

                // v2.1 SK: Extract description
                if (parseResult.Data.TryGetValue("description", out var descObj) && descObj is string desc)
                    rule.Metadata.Description = desc;

                // v2.1 SK: Extract type (skill / rule)
                if (parseResult.Data.TryGetValue("type", out var typeObj) && typeObj is string type)
                    rule.Metadata.Type = type;

                // v2.1 SK: Extract intent (bug_fix, modify, refactor, test_write, etc.)
                if (parseResult.Data.TryGetValue("intent", out var intentObj) && intentObj is string intent)
                    rule.Metadata.Intent = intent;

                // Override rule.Name from frontmatter "name" field if present
                if (parseResult.Data.TryGetValue("name", out var nameObj) && nameObj is string name
                    && !string.IsNullOrEmpty(name))
                    rule.Name = name;

                // Store remaining custom metadata
                var knownKeys = new HashSet<string> { "paths", "priority", "enabled", "description", "type", "intent", "name" };
                foreach (var kvp in parseResult.Data)
                {
                    if (!knownKeys.Contains(kvp.Key))
                    {
                        rule.Metadata.Custom[kvp.Key] = kvp.Value;
                    }
                }
            }

            return rule;
        }

        /// <summary>
        /// Load all available rules (local + global).
        /// </summary>
        public async Task<List<Rule>> LoadAllRulesAsync(
            string workspacePath,
            CancellationToken ct = default)
        {
            var rules = new List<Rule>();

            // Load local rules first (higher priority)
            var localRules = await LoadLocalRulesAsync(workspacePath, ct);
            rules.AddRange(localRules);

            // Load global rules
            var globalRules = await LoadGlobalRulesAsync(ct);
            rules.AddRange(globalRules);

            _logger?.LogDebug($"Loaded {rules.Count} total rules");
            return rules;
        }
    }
}
