using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class TaskComplexityAnalyzerTests
    {
        [Fact]
        public void SimpleGreeting_ReturnsFalse()
        {
            Assert.False(TaskComplexityAnalyzer.IsComplexRequest("你好"));
        }

        [Fact]
        public void SimpleRead_ReturnsFalse()
        {
            Assert.False(TaskComplexityAnalyzer.IsComplexRequest("read file.cs"));
        }

        [Fact]
        public void ChineseArchitectureAnalysis_ReturnsTrue()
        {
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest("分析日志系统架构"));
        }

        [Fact]
        public void EnglishMultiStep_ReturnsTrue()
        {
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest("implement feature and write tests"));
        }

        [Fact]
        public void LongWithMultipleVerbs_ReturnsTrue()
        {
            var longRequest = "Please read the configuration file, then analyze the logging module for performance issues, and finally create a summary report with optimization suggestions for the team";
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest(longRequest));
        }

        [Fact]
        public void NumberedSteps_ReturnsTrue()
        {
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest("1. read file 2. modify 3. test"));
        }

        [Fact]
        public void RefactorKeyword_ReturnsTrue()
        {
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest("重构这个类"));
        }

        [Fact]
        public void EmptyString_ReturnsFalse()
        {
            Assert.False(TaskComplexityAnalyzer.IsComplexRequest(""));
        }

        [Fact]
        public void Null_ReturnsFalse()
        {
            Assert.False(TaskComplexityAnalyzer.IsComplexRequest(null));
        }
    }
}
