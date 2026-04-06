using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Context;
using AICA.Core.Logging;
using AICA.Core.Rules;
using AICA.Core.Rules.Models;
using Microsoft.Extensions.Logging;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Builder for system prompts — trust-based design.
    /// Tool descriptions rely 100% on the function calling API schema.
    /// Zero explicit tool name mentions to avoid selection bias.
    /// </summary>
    public class SystemPromptBuilder
    {
        private readonly StringBuilder _staticBuilder = new StringBuilder();
        private readonly StringBuilder _dynamicBuilder = new StringBuilder();
        private readonly List<ToolDefinition> _tools = new List<ToolDefinition>();

        /// <summary>Optional telemetry logger for skill injection events (T1 埋点).</summary>
        public TelemetryLogger Telemetry { get; set; }

        /// <summary>Session ID for telemetry correlation.</summary>
        public string SessionId { get; set; }

        public SystemPromptBuilder()
        {
            AddBasePrompt();
        }

        private void AddBasePrompt()
        {
            _staticBuilder.AppendLine("You are AICA (AI Coding Assistant), an intelligent programming assistant running inside Visual Studio 2022.");
            _staticBuilder.AppendLine("You help developers with code generation, editing, refactoring, testing, debugging, and code understanding.");
            _staticBuilder.AppendLine("You operate in an offline/private environment. Do not assume internet access.");
            _staticBuilder.AppendLine();
        }

        public SystemPromptBuilder AddTools(IEnumerable<ToolDefinition> tools)
        {
            _tools.AddRange(tools);
            return this;
        }

        /// <summary>
        /// Add minimal behavioral rules. Tool-name-neutral to avoid selection bias.
        /// All tool descriptions are in the function calling schema — not duplicated here.
        /// </summary>
        public SystemPromptBuilder AddRules()
        {
            _staticBuilder.AppendLine("## Rules");
            _staticBuilder.AppendLine("- Call tools immediately via the function calling API. Do NOT describe what you will do — just call the tool.");
            _staticBuilder.AppendLine("- When multiple independent pieces of information are needed, call multiple tools in parallel in a single response.");
            _staticBuilder.AppendLine("- Read files before editing. The old_string must exactly match file content.");
            _staticBuilder.AppendLine("- Do NOT add code comments unless explicitly asked.");
            _staticBuilder.AppendLine("- Windows environment — use built-in tools, not shell commands (grep/find/cat/head/tail).");
            _staticBuilder.AppendLine("- Be direct. Minimize output. Respond in the user's language.");
            _staticBuilder.AppendLine("- Focus on the current request. If it's a new topic, switch immediately.");
            _staticBuilder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddComplexityGuidance(TaskComplexity complexity)
        {
            AddRules();
            return this;
        }

        [Obsolete("Phase B: tool descriptions handled by function calling API")]
        public SystemPromptBuilder AddToolDescriptions()
        {
            return this;
        }

        [Obsolete("v2.1 SK: Replaced by skill-bug-fix.md injected via AddSkillsByIntent. Kept for backward compatibility.")]
        public SystemPromptBuilder AddBugFixGuidance(string intent, Agent.ProjectLanguage language)
        {
            if (intent != "bug_fix")
                return this;

            _dynamicBuilder.AppendLine("## Bug Localization");
            _dynamicBuilder.AppendLine("Search for error keywords → Read matching code → Analyze root cause → Suggest fix.");
            _dynamicBuilder.AppendLine();

            return this;
        }

        [Obsolete("v2.1 SK: Replaced by .aica-rules/cpp-qt-specific.md injected via AddRulesFromFilesAsync. Kept for backward compatibility.")]
        public SystemPromptBuilder AddQtTemplateGuidance(string intent)
        {
            if (!IsQtRelated(intent))
                return this;

            _dynamicBuilder.AppendLine("## Qt Code Generation");
            _dynamicBuilder.AppendLine("Follow .aica-rules/cpp-qt-specific.md. Generate .h + .cpp pairs with Q_OBJECT, new-style connect, tr() macros.");
            _dynamicBuilder.AppendLine();

            return this;
        }

        /// <summary>
        /// v2.1 SK: Inject task template skills by intent (passive injection).
        /// Loads rules with type="skill" and matching intent from .aica-rules/.
        /// Conservative matching: only exact intent match triggers injection.
        /// Feature flag: AicaConfig.Current.Features.TaskTemplatesEnabled.
        /// </summary>
        public async Task<SystemPromptBuilder> AddSkillsByIntent(
            string intent,
            string workspacePath,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(intent) || string.IsNullOrEmpty(workspacePath))
                return this;

            if (!Config.AicaConfig.Current.Features.TaskTemplatesEnabled)
                return this;

            try
            {
                var ruleLoader = new RuleLoader();
                var allRules = await ruleLoader.LoadAllRulesAsync(workspacePath, ct);

                // Filter: type="skill" + exact intent match (conservative, per横切规则 #1)
                var matchedSkills = allRules
                    .Where(r => r.Enabled
                        && string.Equals(r.Metadata?.Type, "skill", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Metadata?.Intent, intent, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.Priority)
                    .ToList();

                var skillNames = matchedSkills.Select(s => s.Name ?? s.Id).ToList();

                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] AddSkillsByIntent: intent={intent}, skills_matched_count={matchedSkills.Count}");

                // T1 telemetry: skill_injection summary
                Telemetry?.LogEvent(SessionId, "skill_injection", new Dictionary<string, object>
                {
                    ["intent"] = intent,
                    ["skills_matched_count"] = matchedSkills.Count,
                    ["skill_names"] = string.Join(",", skillNames)
                });

                if (matchedSkills.Count == 0)
                    return this;

                _dynamicBuilder.AppendLine("## Task Template (auto-injected by intent)");
                _dynamicBuilder.AppendLine();

                foreach (var skill in matchedSkills)
                {
                    if (!string.IsNullOrWhiteSpace(skill.Content))
                    {
                        _dynamicBuilder.AppendLine(skill.Content);
                        _dynamicBuilder.AppendLine();

                        var name = skill.Name ?? skill.Id;
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] skill_injected: {name} (intent={intent})");

                        // T1 telemetry: per-skill injection
                        Telemetry?.LogEvent(SessionId, "skill_injected", new Dictionary<string, object>
                        {
                            ["skill_name"] = name,
                            ["intent"] = intent
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail-open: continue without skills
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] AddSkillsByIntent failed (non-fatal): {ex.Message}");
            }

            return this;
        }

        private static bool IsQtRelated(string intent)
        {
            if (string.IsNullOrEmpty(intent)) return false;
            var lower = intent.ToLowerInvariant();
            var qtKeywords = new[]
            {
                "对话框", "dialog", "widget", "界面", "signal", "slot", "信号", "槽",
                "qwidget", "qdialog", "qpushbutton", "qlabel", "qspinbox",
                ".ui", ".qss", "样式", "布局", "layout", "qt"
            };
            return qtKeywords.Any(kw => lower.Contains(kw));
        }

        public SystemPromptBuilder AddMemoryContext(string memoryContent)
        {
            if (string.IsNullOrWhiteSpace(memoryContent))
                return this;

            _dynamicBuilder.AppendLine("## 项目记忆（跨会话）");
            _dynamicBuilder.AppendLine(memoryContent);
            _dynamicBuilder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddResumeContext(Storage.TaskProgress progress)
        {
            if (progress == null || string.IsNullOrEmpty(progress.OriginalUserRequest))
                return this;

            _dynamicBuilder.AppendLine("## 断点续做 — 上次会话的进度");
            _dynamicBuilder.AppendLine($"原始请求：{progress.OriginalUserRequest}");
            if (progress.EditedFiles?.Count > 0)
                _dynamicBuilder.AppendLine($"已编辑文件：{string.Join(", ", progress.EditedFiles)}");
            if (progress.EditDetails?.Count > 0)
            {
                _dynamicBuilder.AppendLine("编辑详情：");
                foreach (var detail in progress.EditDetails)
                    _dynamicBuilder.AppendLine($"  - {detail}");
            }
            if (!string.IsNullOrEmpty(progress.PlanState))
                _dynamicBuilder.AppendLine($"计划状态：{progress.PlanState}");
            if (!string.IsNullOrEmpty(progress.CurrentPhase))
                _dynamicBuilder.AppendLine($"当前阶段：{progress.CurrentPhase}");
            if (progress.KeyDiscoveries?.Count > 0)
            {
                _dynamicBuilder.AppendLine("关键发现：");
                foreach (var disc in progress.KeyDiscoveries)
                    _dynamicBuilder.AppendLine($"  - {disc}");
            }
            _dynamicBuilder.AppendLine("请基于以上进度继续完成任务。已编辑的文件不需要重新编辑。");
            _dynamicBuilder.AppendLine();

            return this;
        }

        /// <summary>
        /// No-op — replaced by AddMcpResourceContext which injects GitNexus setup + context resources.
        /// </summary>
        public SystemPromptBuilder AddGitNexusGuidance(string repoName)
        {
            return this;
        }

        /// <summary>
        /// Inject MCP resource content (e.g., GitNexus setup instructions and codebase context).
        /// This is the key mechanism that tells the LLM how and when to use MCP tools.
        /// OpenCode reads these resources; without them the LLM defaults to native tools.
        /// </summary>
        public SystemPromptBuilder AddMcpResourceContext(string resourceContent)
        {
            if (string.IsNullOrWhiteSpace(resourceContent))
                return this;

            _staticBuilder.AppendLine(resourceContent);
            _staticBuilder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddWorkspaceContext(
            string workingDirectory,
            IEnumerable<string> sourceRoots = null,
            IEnumerable<string> recentFiles = null)
        {
            _staticBuilder.AppendLine("## Workspace");
            _staticBuilder.AppendLine($"Working Directory: {workingDirectory}");

            if (sourceRoots != null)
            {
                var rootList = sourceRoots.ToList();
                if (rootList.Count > 0)
                {
                    _staticBuilder.AppendLine();
                    _staticBuilder.AppendLine("### Source Roots");
                    _staticBuilder.AppendLine("The following directories contain source files referenced by the solution's project files.");
                    foreach (var root in rootList)
                        _staticBuilder.AppendLine($"- {root}");
                    _staticBuilder.AppendLine();
                    _staticBuilder.AppendLine("File paths are automatically resolved across working directory and source roots.");
                }
            }

            if (recentFiles != null)
            {
                var fileList = recentFiles.ToList();
                if (fileList.Count > 0)
                {
                    _staticBuilder.AppendLine();
                    _staticBuilder.AppendLine("Recently accessed files:");
                    foreach (var file in fileList.Take(20))
                        _staticBuilder.AppendLine($"- {file}");
                    if (fileList.Count > 20)
                        _staticBuilder.AppendLine($"  ... and {fileList.Count - 20} more");
                }
            }
            _staticBuilder.AppendLine();
            return this;
        }

        /// <summary>
        /// v2.3: Inject solution project structure into system prompt.
        /// This replaces the need for LLM to call list_projects tool on every session.
        /// </summary>
        public SystemPromptBuilder AddProjectStructure(Dictionary<string, Workspace.ProjectInfo> projects)
        {
            if (projects == null || projects.Count == 0)
                return this;

            _staticBuilder.AppendLine("## Solution Structure");
            _staticBuilder.AppendLine($"The solution contains {projects.Count} project(s):");
            _staticBuilder.AppendLine();

            foreach (var kvp in projects)
            {
                var p = kvp.Value;
                _staticBuilder.AppendLine($"### {p.Name ?? kvp.Key}");
                if (!string.IsNullOrEmpty(p.ProjectType))
                    _staticBuilder.AppendLine($"- Type: {p.ProjectType}");
                if (!string.IsNullOrEmpty(p.ProjectDirectory))
                    _staticBuilder.AppendLine($"- Directory: {p.ProjectDirectory}");

                if (p.Filters != null && p.Filters.Count > 0)
                {
                    _staticBuilder.AppendLine("- Filters:");
                    foreach (var filter in p.Filters)
                    {
                        _staticBuilder.AppendLine($"  - {filter.Key}: {filter.Value.Count} file(s)");
                    }
                }

                if (p.Dependencies != null && p.Dependencies.Count > 0)
                {
                    _staticBuilder.AppendLine($"- Dependencies: {string.Join(", ", p.Dependencies.Take(10))}");
                    if (p.Dependencies.Count > 10)
                        _staticBuilder.Append($" ... and {p.Dependencies.Count - 10} more");
                }

                var fileCount = p.SourceFiles?.Count ?? 0;
                if (fileCount > 0)
                    _staticBuilder.AppendLine($"- Source files: {fileCount}");

                _staticBuilder.AppendLine();
            }

            _staticBuilder.AppendLine("Use this structure to understand the codebase. " +
                                "You can still call list_projects with show_files=true to see individual file lists.");
            _staticBuilder.AppendLine();
            return this;
        }

        public SystemPromptBuilder AddCustomInstructions(string instructions)
        {
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                _dynamicBuilder.AppendLine("## Custom Instructions");
                _dynamicBuilder.AppendLine(instructions);
                _dynamicBuilder.AppendLine();
            }
            return this;
        }

        /// <summary>
        /// Load and integrate rules from .aica-rules directory.
        /// </summary>
        public async Task<SystemPromptBuilder> AddRulesFromFilesAsync(
            string workspacePath,
            RuleContext context = null,
            CancellationToken ct = default)
        {
            try
            {
                var ruleLoader = new RuleLoader();
                var ruleEvaluator = new RuleEvaluator();
                var allRules = await ruleLoader.LoadAllRulesAsync(workspacePath, ct);

                if (allRules.Count == 0) return this;

                var activatedRules = context != null
                    ? ruleEvaluator.EvaluateRules(allRules, context)
                    : allRules;

                if (activatedRules.Count == 0) return this;

                _dynamicBuilder.AppendLine("## Project Rules");
                _dynamicBuilder.AppendLine();
                foreach (var rule in activatedRules)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Content))
                    {
                        _dynamicBuilder.AppendLine(rule.Content);
                        _dynamicBuilder.AppendLine();
                    }
                }
            }
            catch (Exception)
            {
                // Fail-open: continue without rules
            }

            return this;
        }

        private const int DefaultKnowledgeMaxTokens = 6000;

        public SystemPromptBuilder AddKnowledgeContext(string knowledgeContext, int maxTokens = DefaultKnowledgeMaxTokens)
        {
            if (!string.IsNullOrWhiteSpace(knowledgeContext))
            {
                var maxChars = maxTokens * 4;
                var text = knowledgeContext.Length > maxChars
                    ? knowledgeContext.Substring(0, maxChars) + "\n... (truncated)"
                    : knowledgeContext;

                _dynamicBuilder.AppendLine();
                _dynamicBuilder.AppendLine(text);
            }
            return this;
        }

        public string Build()
        {
            var staticPart = _staticBuilder.ToString();
            var dynamicPart = _dynamicBuilder.ToString();
            var result = string.IsNullOrEmpty(dynamicPart)
                ? staticPart
                : staticPart + "\n" + dynamicPart;

            var estimatedTokens = ContextManager.EstimateTokens(result);
            if (estimatedTokens > 16000)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] WARNING: System prompt is ~{estimatedTokens} tokens.");
            }
            return result;
        }

        /// <summary>
        /// Build system prompt sections with priority tags for token-pressure management.
        /// </summary>
        public List<PromptSection> BuildSections(
            string workingDirectory,
            IEnumerable<string> sourceRoots = null,
            string customInstructions = null)
        {
            var sections = new List<PromptSection>();

            var baseSb = new StringBuilder();
            baseSb.AppendLine("You are AICA (AI Coding Assistant), an intelligent programming assistant running inside Visual Studio 2022.");
            baseSb.AppendLine("You help developers with code generation, editing, refactoring, testing, debugging, and code understanding.");
            baseSb.AppendLine("You operate in an offline/private environment. Do not assume internet access.");
            sections.Add(new PromptSection("base_role", baseSb.ToString(), ContextPriority.Critical, 0));

            var toolSb = new StringBuilder();
            toolSb.AppendLine("## Tool Usage");
            toolSb.AppendLine("Use the function calling API to invoke tools. Call tools immediately — never describe what you plan to do.");
            toolSb.AppendLine("Choose the best tool by reading each tool's description in the function calling schema.");
            sections.Add(new PromptSection("tool_usage", toolSb.ToString(), ContextPriority.Critical, 1));

            var wsSb = new StringBuilder();
            wsSb.AppendLine("## Workspace");
            wsSb.AppendLine($"Working Directory: {workingDirectory}");
            if (sourceRoots != null)
            {
                foreach (var root in sourceRoots)
                    wsSb.AppendLine($"- {root}");
            }
            sections.Add(new PromptSection("workspace", wsSb.ToString(), ContextPriority.High, 2));

            var knowledgeStore = Knowledge.ProjectKnowledgeStore.Instance;
            if (knowledgeStore.HasIndex)
            {
                var provider = knowledgeStore.CreateProvider();
                if (provider != null)
                {
                    var summary = provider.GetIndexSummary();
                    sections.Add(new PromptSection("project_knowledge",
                        "## Project Knowledge\n" + summary, ContextPriority.Normal, 3));
                }
            }

            if (!string.IsNullOrWhiteSpace(customInstructions))
            {
                sections.Add(new PromptSection("custom_instructions",
                    "## Custom Instructions\n" + customInstructions, ContextPriority.High, 4));
            }

            return sections;
        }

        public static string BuildWithBudget(
            string workingDirectory,
            IEnumerable<ToolDefinition> tools,
            int tokenBudget,
            string customInstructions = null,
            IEnumerable<string> sourceRoots = null)
        {
            var fullPrompt = GetDefaultPrompt(workingDirectory, tools, customInstructions, sourceRoots);
            if (ContextManager.EstimateTokens(fullPrompt) <= tokenBudget)
                return fullPrompt;

            var builder = new SystemPromptBuilder();
            builder.AddTools(tools);
            var sections = builder.BuildSections(workingDirectory, sourceRoots, customInstructions);

            var cm = new ContextManager(tokenBudget);
            foreach (var section in sections)
                cm.AddItem(section.Key, section.Content, section.Priority);

            var items = cm.GetContextWithinBudget();
            var ordered = items.OrderBy(i =>
            {
                var match = sections.FirstOrDefault(s => s.Key == i.Key);
                return match?.Order ?? 99;
            });

            return string.Join("\n\n", ordered.Select(i => i.Content));
        }

        public static string GetDefaultPrompt(
            string workingDirectory,
            IEnumerable<ToolDefinition> tools,
            string customInstructions = null,
            IEnumerable<string> sourceRoots = null)
        {
            return new SystemPromptBuilder()
                .AddTools(tools)
                .AddRules()
                .AddWorkspaceContext(workingDirectory, sourceRoots)
                .AddCustomInstructions(customInstructions)
                .Build();
        }

        // Kept for backward compatibility but no-op
        public SystemPromptBuilder AddGuidelines() => this;
    }

    public class PromptSection
    {
        public string Key { get; }
        public string Content { get; }
        public ContextPriority Priority { get; }
        public int Order { get; }

        public PromptSection(string key, string content, ContextPriority priority, int order)
        {
            Key = key;
            Content = content;
            Priority = priority;
            Order = order;
        }
    }
}
