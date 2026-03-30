using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Tests for TaskState tracking that controls recovery path selection.
    /// </summary>
    public class PlanAwareRecoveryTests
    {
        [Fact]
        public void Recovery_WithActivePlan_HasActivePlanIsTrue()
        {
            var taskState = new TaskState { HasActivePlan = true };

            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);

            Assert.Equal(3, taskState.ConsecutiveBlockingFailureCount);
            Assert.True(taskState.HasActivePlan);
        }

        [Fact]
        public void Recovery_WithoutPlan_HasActivePlanIsFalse()
        {
            var taskState = new TaskState { HasActivePlan = false };

            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);

            Assert.Equal(3, taskState.ConsecutiveBlockingFailureCount);
            Assert.False(taskState.HasActivePlan);
        }

        [Fact]
        public void ResetFailureCounts_ClearsConsecutiveFailures()
        {
            var taskState = new TaskState();
            taskState.RecordToolFailure(ToolFailureKind.Blocking);
            taskState.RecordToolFailure(ToolFailureKind.Blocking);

            taskState.ResetFailureCounts();

            Assert.Equal(0, taskState.ConsecutiveBlockingFailureCount);
        }
    }
}
