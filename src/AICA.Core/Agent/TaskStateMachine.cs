using System;
using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.Agent
{
    /// <summary>
    /// 3.4 H2: Complex task state machine.
    /// Controls phase transitions for multi-step tasks using soft interception.
    /// Tools execute normally but phase suggestions are appended to results.
    /// </summary>
    public sealed class TaskStateMachine
    {
        private readonly TaskStateMachineTemplate _template;
        private int _currentPhaseIndex;

        public string CurrentPhaseName => _template.Phases[_currentPhaseIndex].Name;
        public TaskStateMachineTemplate Template => _template;

        private TaskStateMachine(TaskStateMachineTemplate template)
        {
            _template = template;
            _currentPhaseIndex = 0;
        }

        /// <summary>
        /// Try to create a state machine for the given user request.
        /// Returns null if the request doesn't match any template.
        /// Only activates for Complex tasks.
        /// </summary>
        public static TaskStateMachine TryCreate(string userRequest, TaskComplexity complexity)
        {
            if (complexity != TaskComplexity.Complex || string.IsNullOrWhiteSpace(userRequest))
                return null;

            var template = MatchTemplate(userRequest);
            if (template == null)
                return null;

            return new TaskStateMachine(template);
        }

        /// <summary>
        /// Get a phase directive based on current state and pending tool calls.
        /// Returns null if no intervention needed.
        /// </summary>
        public PhaseDirective GetDirective(TaskState state, string[] pendingToolNames)
        {
            var phase = _template.Phases[_currentPhaseIndex];

            // Phase timeout → force advance
            if (state.PhaseIterationCount >= phase.MaxIterations)
            {
                AdvancePhase();
                var newPhase = _template.Phases[_currentPhaseIndex];
                return PhaseDirective.Force(
                    $"[阶段超时] 已超过 {phase.Name} 阶段的 {phase.MaxIterations} 轮限制，进入 {newPhase.Name} 阶段。" +
                    $"{newPhase.Description}");
            }

            // Check for off-phase tools (soft interception)
            if (pendingToolNames != null && pendingToolNames.Length > 0)
            {
                var offPhaseTools = pendingToolNames
                    .Where(t => !phase.AllowedTools.Contains(t, StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                if (offPhaseTools.Length > 0)
                {
                    return PhaseDirective.Suggest(
                        $"[阶段提示] 当前处于 {phase.Name} 阶段（{phase.Description}）。" +
                        $"建议优先使用 {string.Join("/", phase.SuggestedTools)} 完成本阶段目标。");
                }
            }

            // Check transition condition
            if (phase.ShouldTransition(state))
            {
                AdvancePhase();
            }

            return null;
        }

        /// <summary>
        /// Force advance to Complete phase (used by H7 budget warning at 80%).
        /// </summary>
        public void ForceAdvance(string targetPhase)
        {
            for (int i = 0; i < _template.Phases.Count; i++)
            {
                if (string.Equals(_template.Phases[i].Name, targetPhase, StringComparison.OrdinalIgnoreCase))
                {
                    _currentPhaseIndex = i;
                    return;
                }
            }
            // If target not found, advance to last phase
            _currentPhaseIndex = _template.Phases.Count - 1;
        }

        private void AdvancePhase()
        {
            if (_currentPhaseIndex < _template.Phases.Count - 1)
                _currentPhaseIndex++;
        }

        private static TaskStateMachineTemplate MatchTemplate(string request)
        {
            var lower = request.ToLowerInvariant();

            // Priority: Rename > CrossFileEdit > Analysis > Default
            if (ContainsAny(lower, "重命名", "rename", "改名"))
                return TaskStateMachineTemplate.Rename;

            if (ContainsAny(lower, "实现", "添加", "修改", "重构", "implement", "add", "modify", "refactor")
                && ContainsAny(lower, "文件", "模块", "file", "module", "类", "class", "头文件", ".h", ".cpp"))
                return TaskStateMachineTemplate.CrossFileEdit;

            if (ContainsAny(lower, "分析", "审查", "检查", "analyze", "review", "inspect", "check")
                && !ContainsAny(lower, "实现", "修改", "重构", "implement", "modify", "refactor"))
                return TaskStateMachineTemplate.Analysis;

            // Default: no state machine for unmatched Complex tasks
            return null;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var kw in keywords)
            {
                if (text.Contains(kw))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Phase directive from the state machine.
    /// </summary>
    public sealed class PhaseDirective
    {
        public PhaseDirectiveMode Mode { get; }
        public string Message { get; }

        private PhaseDirective(PhaseDirectiveMode mode, string message)
        {
            Mode = mode;
            Message = message;
        }

        /// <summary>Soft: tool executes normally, message appended to result.</summary>
        public static PhaseDirective Suggest(string message)
            => new PhaseDirective(PhaseDirectiveMode.Soft, message);

        /// <summary>Force: injected as system message, overrides current phase.</summary>
        public static PhaseDirective Force(string message)
            => new PhaseDirective(PhaseDirectiveMode.Force, message);
    }

    public enum PhaseDirectiveMode
    {
        Soft,
        Force
    }

    /// <summary>
    /// Template defining phases for a specific task type.
    /// </summary>
    public sealed class TaskStateMachineTemplate
    {
        public string Name { get; }
        public IReadOnlyList<PhaseConfig> Phases { get; }

        private TaskStateMachineTemplate(string name, PhaseConfig[] phases)
        {
            Name = name;
            Phases = phases;
        }

        public static readonly TaskStateMachineTemplate CrossFileEdit = new TaskStateMachineTemplate(
            "CrossFileEdit",
            new[]
            {
                new PhaseConfig("Understand", "理解需求、搜索相关代码", 8,
                    new[] { "read_file", "grep_search", "list_dir", "gitnexus_context", "gitnexus_query", "gitnexus_impact", "gitnexus_cypher", "ask_followup_question" },
                    new[] { "read_file", "gitnexus_query", "gitnexus_context" },
                    state => state.HasActivePlan),

                new PhaseConfig("Plan", "制定修改计划", 2,
                    new[] { "update_plan", "ask_followup_question" },
                    new[] { "update_plan" },
                    state => state.HasActivePlan && state.PhaseIterationCount >= 1),

                new PhaseConfig("Execute", "逐文件执行修改", 30,
                    new[] { "read_file", "edit", "grep_search", "gitnexus_context", "gitnexus_impact", "run_command", "update_plan" },
                    new[] { "edit", "read_file" },
                    _ => false),

                new PhaseConfig("Verify", "验证修改无残留", 5,
                    new[] { "read_file", "grep_search", "gitnexus_detect_changes", "run_command", "update_plan", "attempt_completion" },
                    new[] { "grep_search", "gitnexus_detect_changes" },
                    _ => false),

                new PhaseConfig("Complete", "输出完成报告", 3,
                    new[] { "attempt_completion", "update_plan" },
                    new[] { "attempt_completion" },
                    _ => false)
            });

        public static readonly TaskStateMachineTemplate Rename = new TaskStateMachineTemplate(
            "Rename",
            new[]
            {
                new PhaseConfig("Search", "搜索全部引用", 5,
                    new[] { "grep_search", "read_file", "gitnexus_context", "gitnexus_query", "gitnexus_impact", "gitnexus_cypher", "ask_followup_question" },
                    new[] { "gitnexus_impact", "grep_search" },
                    state => state.HasActivePlan),

                new PhaseConfig("Plan", "制定重命名计划", 2,
                    new[] { "update_plan", "ask_followup_question" },
                    new[] { "update_plan" },
                    state => state.HasActivePlan && state.PhaseIterationCount >= 1),

                new PhaseConfig("Execute", "逐文件重命名", 40,
                    new[] { "edit", "read_file", "gitnexus_rename", "update_plan" },
                    new[] { "gitnexus_rename", "edit" },
                    _ => false),

                new PhaseConfig("Verify", "验证无残留引用", 3,
                    new[] { "grep_search", "read_file", "gitnexus_detect_changes", "attempt_completion", "update_plan" },
                    new[] { "grep_search" },
                    _ => false),

                new PhaseConfig("Complete", "输出完成报告", 3,
                    new[] { "attempt_completion", "update_plan" },
                    new[] { "attempt_completion" },
                    _ => false)
            });

        public static readonly TaskStateMachineTemplate Analysis = new TaskStateMachineTemplate(
            "Analysis",
            new[]
            {
                new PhaseConfig("Gather", "收集信息", 15,
                    new[] { "read_file", "grep_search", "list_dir", "gitnexus_context", "gitnexus_query", "gitnexus_impact", "gitnexus_cypher", "run_command" },
                    new[] { "gitnexus_query", "read_file", "grep_search" },
                    _ => false),

                new PhaseConfig("Analyze", "分析并总结", 5,
                    new[] { "read_file", "gitnexus_context", "attempt_completion", "ask_followup_question" },
                    new[] { "attempt_completion" },
                    _ => false),

                new PhaseConfig("Complete", "输出分析结果", 3,
                    new[] { "attempt_completion" },
                    new[] { "attempt_completion" },
                    _ => false)
            });
    }

    /// <summary>
    /// Configuration for a single phase in the state machine.
    /// </summary>
    public sealed class PhaseConfig
    {
        public string Name { get; }
        public string Description { get; }
        public int MaxIterations { get; }
        public IReadOnlyList<string> AllowedTools { get; }
        public IReadOnlyList<string> SuggestedTools { get; }
        private readonly Func<TaskState, bool> _transitionCondition;

        public PhaseConfig(
            string name, string description, int maxIterations,
            string[] allowedTools, string[] suggestedTools,
            Func<TaskState, bool> transitionCondition)
        {
            Name = name;
            Description = description;
            MaxIterations = maxIterations;
            AllowedTools = allowedTools;
            SuggestedTools = suggestedTools;
            _transitionCondition = transitionCondition;
        }

        public bool ShouldTransition(TaskState state) => _transitionCondition(state);
    }
}
