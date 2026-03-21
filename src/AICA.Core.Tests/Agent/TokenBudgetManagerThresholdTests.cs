using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class TokenBudgetManagerThresholdTests
    {
        [Theory]
        [InlineData(0, 18)]       // Zero budget returns minimum
        [InlineData(8000, 18)]    // Small budget returns minimum
        [InlineData(32000, 18)]   // Old default (32K) still returns minimum floor
        [InlineData(131072, 52)]  // 128K: 131072/1500=87, 87*0.6=52
        [InlineData(177224, 70)]  // Default budget (196608-16384-3000): 177224/1500=118, 118*0.6=70
        [InlineData(192000, 76)]  // MiniMax-M2.5 full: 192000/1500=128, 128*0.6=76
        public void ComputeCondenseMessageThreshold_ScalesWithBudget(int budget, int expected)
        {
            int result = TokenBudgetManager.ComputeCondenseMessageThreshold(budget);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ComputeCondenseMessageThreshold_NeverBelowMinimum()
        {
            for (int budget = 0; budget <= 50000; budget += 1000)
            {
                int result = TokenBudgetManager.ComputeCondenseMessageThreshold(budget);
                Assert.True(result >= TokenBudgetManager.MinCondenseMessageThreshold,
                    $"Budget {budget} gave threshold {result}, below minimum {TokenBudgetManager.MinCondenseMessageThreshold}");
            }
        }

        [Theory]
        [InlineData(0, 12)]       // Zero budget returns minimum
        [InlineData(32000, 12)]   // Old default returns minimum
        [InlineData(131072, 34)]  // 128K: msgThreshold=52, 52*0.67=34
        [InlineData(177224, 46)]  // Default budget: msgThreshold=70, 70*0.67=46
        [InlineData(192000, 50)]  // MiniMax full: msgThreshold=76, 76*0.67=50
        public void ComputeCondenseCompressibleThreshold_ScalesWithBudget(int budget, int expected)
        {
            int result = TokenBudgetManager.ComputeCondenseCompressibleThreshold(budget);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CompressibleThreshold_AlwaysLessThanMessageThreshold()
        {
            int[] budgets = { 0, 8000, 32000, 64000, 131072, 192000, 256000 };
            foreach (int budget in budgets)
            {
                int msgThreshold = TokenBudgetManager.ComputeCondenseMessageThreshold(budget);
                int compThreshold = TokenBudgetManager.ComputeCondenseCompressibleThreshold(budget);
                Assert.True(compThreshold <= msgThreshold,
                    $"Budget {budget}: compressible {compThreshold} > message {msgThreshold}");
            }
        }
    }
}
