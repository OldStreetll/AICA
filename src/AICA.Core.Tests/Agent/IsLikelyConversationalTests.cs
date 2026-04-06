using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Tests for conversation intent classification.
    /// v2.0: IsLikelyConversational moved from AgentExecutor to DynamicToolSelector.ClassifyIntent.
    /// </summary>
    public class IsLikelyConversationalTests
    {
        private static bool IsConversational(string input)
            => DynamicToolSelector.ClassifyIntent(input) == "conversation";

        [Fact]
        public void ShortGreeting_ReturnsTrue()
        {
            Assert.True(IsConversational("你好"));
        }

        [Fact]
        public void ChineseQuestion_IsShiMe_ReturnsFalse()
        {
            Assert.False(IsConversational("Logger 是什么"));
        }

        [Fact]
        public void ChineseQuestion_ZenMe_ReturnsFalse()
        {
            Assert.False(IsConversational("怎么用"));
        }

        [Fact]
        public void ChineseQuestion_NaXie_ReturnsFalse()
        {
            Assert.False(IsConversational("有哪些"));
        }

        [Fact]
        public void LongMessage_ReturnsFalse()
        {
            Assert.False(IsConversational("请帮我分析一下这个项目的架构设计"));
        }

        [Fact]
        public void ShortTaskKeyword_ReturnsFalse()
        {
            Assert.False(IsConversational("查找bug"));
        }

        [Fact]
        public void EmptyMessage_ReturnsTrue()
        {
            Assert.True(IsConversational(""));
        }

        [Fact]
        public void ShortEnglishGreeting_ReturnsTrue()
        {
            Assert.True(IsConversational("hi"));
        }
    }
}
