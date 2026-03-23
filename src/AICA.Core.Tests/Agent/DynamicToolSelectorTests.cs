using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class DynamicToolSelectorTests
    {
        [Theory]
        [InlineData("这段代码报错了", "bug_fix")]
        [InlineData("程序崩溃了，帮我看看", "bug_fix")]
        [InlineData("内存泄漏了，帮我找原因", "bug_fix")]
        [InlineData("this code throws an exception", "bug_fix")]
        [InlineData("getting a segfault when running", "bug_fix")]
        [InlineData("there's a bug in the parser", "bug_fix")]
        [InlineData("crash on startup", "bug_fix")]
        [InlineData("程序运行失败", "bug_fix")]
        [InlineData("这段代码在处理空数组时崩溃", "bug_fix")]
        public void ClassifyIntent_BugKeywords_ReturnsBugFix(string input, string expected)
        {
            Assert.Equal(expected, DynamicToolSelector.ClassifyIntent(input));
        }

        [Theory]
        [InlineData("帮我创建一个新函数", "modify")]
        [InlineData("分析一下架构", "analyze")]
        [InlineData("读取这个文件", "read")]
        [InlineData("你好", "conversation")]
        public void ClassifyIntent_OtherIntents_Correct(string input, string expected)
        {
            Assert.Equal(expected, DynamicToolSelector.ClassifyIntent(input));
        }
    }
}
