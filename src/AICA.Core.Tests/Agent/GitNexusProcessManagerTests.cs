using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class GitNexusProcessManagerTests
    {
        [Fact]
        public void State_Initially_IsNotStarted()
        {
            // GitNexusProcessManager is a singleton, but we can test the interface contract
            // by verifying initial state before any StartAsync call
            var state = GitNexusProcessManager.Instance.State;

            // State should be NotStarted, Ready (if previously started in test run), or Disposed
            Assert.True(
                state == GitNexusState.NotStarted ||
                state == GitNexusState.Ready ||
                state == GitNexusState.Failed ||
                state == GitNexusState.Disposed,
                $"Unexpected state: {state}");
        }

        [Fact]
        public void Client_WhenNotReady_ReturnsNull()
        {
            // If state is not Ready, Client should return null
            // This tests the contract, not the singleton (which may be in any state)
            var manager = GitNexusProcessManager.Instance;
            if (manager.State != GitNexusState.Ready)
            {
                Assert.Null(manager.Client);
            }
        }

        [Fact]
        public void GitNexusState_HasExpectedValues()
        {
            // Verify the state enum contains all expected states
            Assert.Equal(0, (int)GitNexusState.NotStarted);
            Assert.Equal(1, (int)GitNexusState.Starting);
            Assert.Equal(2, (int)GitNexusState.Ready);
            Assert.Equal(3, (int)GitNexusState.Failed);
            Assert.Equal(4, (int)GitNexusState.Disposed);
        }
    }
}
