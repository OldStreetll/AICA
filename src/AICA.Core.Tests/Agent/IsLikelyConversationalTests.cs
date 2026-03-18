using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class IsLikelyConversationalTests
    {
        [Fact]
        public void ShortGreeting_ReturnsTrue()
        {
            Assert.True(AgentExecutor.IsLikelyConversational("你好"));
        }

        [Fact]
        public void ChineseQuestion_IsShiMe_ReturnsFalse()
        {
            Assert.False(AgentExecutor.IsLikelyConversational("Logger 是什么"));
        }

        [Fact]
        public void ChineseQuestion_ZenMe_ReturnsFalse()
        {
            Assert.False(AgentExecutor.IsLikelyConversational("怎么用"));
        }

        [Fact]
        public void ChineseQuestion_NaXie_ReturnsFalse()
        {
            Assert.False(AgentExecutor.IsLikelyConversational("有哪些"));
        }

        [Fact]
        public void LongMessage_ReturnsFalse()
        {
            Assert.False(AgentExecutor.IsLikelyConversational("请帮我分析一下这个项目的架构设计"));
        }

        [Fact]
        public void ShortTaskKeyword_ReturnsFalse()
        {
            Assert.False(AgentExecutor.IsLikelyConversational("查找bug"));
        }

        [Fact]
        public void EmptyMessage_ReturnsTrue()
        {
            Assert.True(AgentExecutor.IsLikelyConversational(""));
        }

        [Fact]
        public void ShortEnglishGreeting_ReturnsTrue()
        {
            Assert.True(AgentExecutor.IsLikelyConversational("hi"));
        }
    }
}
