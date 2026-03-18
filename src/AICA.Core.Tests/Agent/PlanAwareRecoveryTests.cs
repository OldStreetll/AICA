using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Tests for plan-aware recovery behavior.
    /// The actual recovery message injection happens inside AgentExecutor.ExecuteAsync,
    /// which is gated by TaskState.HasActivePlan. These tests verify the state tracking
    /// that controls recovery path selection.
    /// </summary>
    public class PlanAwareRecoveryTests
    {
        [Fact]
        public void Recovery_WithActivePlan_HasActivePlanIsTrue()
        {
            // Arrange: simulate reaching blocking failure threshold with an active plan
            var taskState = new TaskState
            {
                MaxConsecutiveMistakes = 3,
                MaxRecoveryPrompts = 2,
                HasActivePlan = true
            };

            // Simulate 3 consecutive blocking failures
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);

            // Assert: threshold reached and recovery available
            Assert.True(taskState.HasReachedBlockingFailureThreshold());
            Assert.True(taskState.CanPromptRecovery());
            Assert.True(taskState.HasActivePlan);
            // When HasActivePlan is true, AgentExecutor injects "update_plan" recovery
            // instead of "ask_followup_question" recovery
        }

        [Fact]
        public void Recovery_WithoutPlan_HasActivePlanIsFalse()
        {
            // Arrange: simulate reaching blocking failure threshold without a plan
            var taskState = new TaskState
            {
                MaxConsecutiveMistakes = 3,
                MaxRecoveryPrompts = 2,
                HasActivePlan = false
            };

            // Simulate 3 consecutive blocking failures
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);

            // Assert: threshold reached, recovery available, but no active plan
            Assert.True(taskState.HasReachedBlockingFailureThreshold());
            Assert.True(taskState.CanPromptRecovery());
            Assert.False(taskState.HasActivePlan);
            // When HasActivePlan is false, AgentExecutor injects "ask_followup_question" recovery
        }
    }
}
