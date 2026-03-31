using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Context;
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
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly List<ToolDefinition> _tools = new List<ToolDefinition>();

        public SystemPromptBuilder()
        {
            AddBasePrompt();
        }

        private void AddBasePrompt()
        {
            _builder.AppendLine("You are AICA (AI Coding Assistant), an intelligent programming assistant running inside Visual Studio 2022.");
            _builder.AppendLine("You help developers with code generation, editing, refactoring, testing, debugging, and code understanding.");
            _builder.AppendLine("You operate in an offline/private environment. Do not assume internet access.");
            _builder.AppendLine();
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
            _builder.AppendLine("## Rules");
            _builder.AppendLine("- Call tools immediately via the function calling API. Do NOT describe what you will do — just call the tool.");
            _builder.AppendLine("- When multiple independent pieces of information are needed, call multiple tools in parallel in a single response.");
            _builder.AppendLine("- Read files before editing. The old_string must exactly match file content.");
            _builder.AppendLine("- Do NOT add code comments unless explicitly asked.");
            _builder.AppendLine("- Windows environment — use built-in tools, not shell commands (grep/find/cat/head/tail).");
            _builder.AppendLine("- Be direct. Minimize output. Respond in the user's language.");
            _builder.AppendLine("- Focus on the current request. If it's a new topic, switch immediately.");
            _builder.AppendLine();

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

        public SystemPromptBuilder AddBugFixGuidance(string intent, Agent.ProjectLanguage language)
        {
            if (intent != "bug_fix")
                return this;

            _builder.AppendLine("## Bug Localization");
            _builder.AppendLine("Search for error keywords → Read matching code → Analyze root cause → Suggest fix.");
            _builder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddQtTemplateGuidance(string intent)
        {
            if (!IsQtRelated(intent))
                return this;

            _builder.AppendLine("## Qt Code Generation");
            _builder.AppendLine("Follow .aica-rules/cpp-qt-specific.md. Generate .h + .cpp pairs with Q_OBJECT, new-style connect, tr() macros.");
            _builder.AppendLine();

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

            _builder.AppendLine("## 项目记忆（跨会话）");
            _builder.AppendLine(memoryContent);
            _builder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddResumeContext(Storage.TaskProgress progress)
        {
            if (progress == null || string.IsNullOrEmpty(progress.OriginalUserRequest))
                return this;

            _builder.AppendLine("## 断点续做 — 上次会话的进度");
            _builder.AppendLine($"原始请求：{progress.OriginalUserRequest}");
            if (progress.EditedFiles?.Count > 0)
                _builder.AppendLine($"已编辑文件：{string.Join(", ", progress.EditedFiles)}");
            if (progress.EditDetails?.Count > 0)
            {
                _builder.AppendLine("编辑详情：");
                foreach (var detail in progress.EditDetails)
                    _builder.AppendLine($"  - {detail}");
            }
            if (!string.IsNullOrEmpty(progress.PlanState))
                _builder.AppendLine($"计划状态：{progress.PlanState}");
            if (!string.IsNullOrEmpty(progress.CurrentPhase))
                _builder.AppendLine($"当前阶段：{progress.CurrentPhase}");
            if (progress.KeyDiscoveries?.Count > 0)
            {
                _builder.AppendLine("关键发现：");
                foreach (var disc in progress.KeyDiscoveries)
                    _builder.AppendLine($"  - {disc}");
            }
            _builder.AppendLine("请基于以上进度继续完成任务。已编辑的文件不需要重新编辑。");
            _builder.AppendLine();

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

            _builder.AppendLine(resourceContent);
            _builder.AppendLine();

            return this;
        }

        public SystemPromptBuilder AddWorkspaceContext(
            string workingDirectory,
            IEnumerable<string> sourceRoots = null,
            IEnumerable<string> recentFiles = null)
        {
            _builder.AppendLine("## Workspace");
            _builder.AppendLine($"Working Directory: {workingDirectory}");

            if (sourceRoots != null)
            {
                var rootList = sourceRoots.ToList();
                if (rootList.Count > 0)
                {
                    _builder.AppendLine();
                    _builder.AppendLine("### Source Roots");
                    _builder.AppendLine("The following directories contain source files referenced by the solution's project files.");
                    foreach (var root in rootList)
                        _builder.AppendLine($"- {root}");
                    _builder.AppendLine();
                    _builder.AppendLine("File paths are automatically resolved across working directory and source roots.");
                }
            }

            if (recentFiles != null)
            {
                var fileList = recentFiles.ToList();
                if (fileList.Count > 0)
                {
                    _builder.AppendLine();
                    _builder.AppendLine("Recently accessed files:");
                    foreach (var file in fileList.Take(20))
                        _builder.AppendLine($"- {file}");
                    if (fileList.Count > 20)
                        _builder.AppendLine($"  ... and {fileList.Count - 20} more");
                }
            }
            _builder.AppendLine();
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

            _builder.AppendLine("## Solution Structure");
            _builder.AppendLine($"The solution contains {projects.Count} project(s):");
            _builder.AppendLine();

            foreach (var kvp in projects)
            {
                var p = kvp.Value;
                _builder.AppendLine($"### {p.Name ?? kvp.Key}");
                if (!string.IsNullOrEmpty(p.ProjectType))
                    _builder.AppendLine($"- Type: {p.ProjectType}");
                if (!string.IsNullOrEmpty(p.ProjectDirectory))
                    _builder.AppendLine($"- Directory: {p.ProjectDirectory}");

                if (p.Filters != null && p.Filters.Count > 0)
                {
                    _builder.AppendLine("- Filters:");
                    foreach (var filter in p.Filters)
                    {
                        _builder.AppendLine($"  - {filter.Key}: {filter.Value.Count} file(s)");
                    }
                }

                if (p.Dependencies != null && p.Dependencies.Count > 0)
                {
                    _builder.AppendLine($"- Dependencies: {string.Join(", ", p.Dependencies.Take(10))}");
                    if (p.Dependencies.Count > 10)
                        _builder.Append($" ... and {p.Dependencies.Count - 10} more");
                }

                var fileCount = p.SourceFiles?.Count ?? 0;
                if (fileCount > 0)
                    _builder.AppendLine($"- Source files: {fileCount}");

                _builder.AppendLine();
            }

            _builder.AppendLine("Use this structure to understand the codebase. " +
                                "You can still call list_projects with show_files=true to see individual file lists.");
            _builder.AppendLine();
            return this;
        }

        public SystemPromptBuilder AddCustomInstructions(string instructions)
        {
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                _builder.AppendLine("## Custom Instructions");
                _builder.AppendLine(instructions);
                _builder.AppendLine();
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

                _builder.AppendLine("## Project Rules");
                _builder.AppendLine();
                foreach (var rule in activatedRules)
                {
                    if (!string.IsNullOrWhiteSpace(rule.Content))
                    {
                        _builder.AppendLine(rule.Content);
                        _builder.AppendLine();
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

                _builder.AppendLine();
                _builder.AppendLine(text);
            }
            return this;
        }

        public string Build()
        {
            var result = _builder.ToString();
            var estimatedTokens = result.Length / 4;
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
