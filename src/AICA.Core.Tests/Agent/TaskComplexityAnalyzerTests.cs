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

        [Fact]
        public void ContextMenu_ExplainCode_NotComplex()
        {
            var request = "请用中文详细解释以下来自文件 `SocketNotifier.cpp` 的 C/C++ 代码，包括其功能、逻辑和关键细节：\n\n```c++\nvoid foo() { delete ptr; }\n```";
            Assert.NotEqual(TaskComplexity.Complex, TaskComplexityAnalyzer.AnalyzeComplexity(request));
        }

        [Fact]
        public void ContextMenu_RefactorCode_NotComplex()
        {
            var request = "请用中文重构以下来自文件 `test.cpp` 的 C/C++ 代码，以提高可读性、性能和可维护性：\n\n```c++\nint x = read(fd); write(fd, buf);\n```";
            Assert.NotEqual(TaskComplexity.Complex, TaskComplexityAnalyzer.AnalyzeComplexity(request));
        }

        [Fact]
        public void ContextMenu_GenerateTest_NotComplex()
        {
            var request = "请用中文为以下来自文件 `test.cpp` 的 C/C++ 代码生成全面的单元测试：\n\n```c++\nvoid test() {}\n```";
            Assert.NotEqual(TaskComplexity.Complex, TaskComplexityAnalyzer.AnalyzeComplexity(request));
        }

        [Fact]
        public void DirectRefactorRequest_StillComplex()
        {
            // User directly typing "重构这个类" should still be Complex
            Assert.True(TaskComplexityAnalyzer.IsComplexRequest("重构这个类"));
        }

        [Fact]
        public void CodeBlockStripped_ShortRequestNotInflated()
        {
            // A short request with a long code block should not score as Complex
            var request = "解释这段代码\n\n```c++\n" + new string('x', 500) + "\n```";
            Assert.NotEqual(TaskComplexity.Complex, TaskComplexityAnalyzer.AnalyzeComplexity(request));
        }
    }
}
