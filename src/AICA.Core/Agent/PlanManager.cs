using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Manages task planning: complexity-based directive injection and plan-aware recovery.
    /// Extracted from AgentExecutor to separate planning concerns.
    /// </summary>
    public static class PlanManager
    {
        /// <summary>
        /// Build the planning directive message injected after the user's request
        /// for complex tasks.
        /// </summary>
        public static ChatMessage BuildPlanningDirective(bool isMultiFile = false)
        {
            var baseDirective =
                "[System: Task Planning Required] This is a complex multi-step task. " +
                "Before doing anything else, you MUST first call the `update_plan` tool to create a plan with 3+ concrete steps (all status 'pending'). " +
                "Do NOT call any other tool before `update_plan`. " +
                "After creating the plan, execute each step and call `update_plan` again to update step status (in_progress → completed/failed) as you progress.";

            if (isMultiFile)
            {
                baseDirective +=
                    "\n\n[跨文件重构五步法] " +
                    "1. 搜索全部引用（grep_search 或 gitnexus_impact）→ " +
                    "2. 制定计划（update_plan）→ " +
                    "3. 逐文件执行修改 → " +
                    "4. 验证无残留（grep_search 确认旧名称/旧代码不存在）→ " +
                    "5. 完成报告（attempt_completion）";
            }

            return ChatMessage.User(baseDirective);
        }

        /// <summary>
        /// Build a plan-aware recovery message that helps the agent recover
        /// from consecutive failures by referencing the active plan.
        /// </summary>
        public static ChatMessage BuildPlanAwareRecoveryMessage(TaskState taskState, string userRequest)
        {
            if (taskState.HasActivePlan)
            {
                return ChatMessage.User(
                    "[System: Recovery with Active Plan] You have encountered multiple consecutive failures. " +
                    "Review your active plan and identify which step failed. " +
                    "Consider: (1) re-reading the relevant file to refresh context, " +
                    "(2) changing your approach for the failed step, " +
                    "(3) marking the failed step as 'failed' and moving to the next step, or " +
                    "(4) using ask_followup_question if you need user input to proceed. " +
                    "Do NOT repeat the same failing action.");
            }
            else
            {
                return ChatMessage.User(
                    "[System: Recovery Required] You have encountered multiple consecutive failures. " +
                    "STOP and reassess your approach. " +
                    "Consider: (1) re-reading the relevant file to refresh context, " +
                    "(2) using a different tool or different parameters, " +
                    "(3) using ask_followup_question if you need user clarification. " +
                    "Do NOT repeat the same failing action.");
            }
        }
    }
}
