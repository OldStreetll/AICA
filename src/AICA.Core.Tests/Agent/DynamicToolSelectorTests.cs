using System.Collections.Generic;
using System.Linq;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class DynamicToolSelectorTests
    {
        #region ClassifyIntent tests

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

        [Theory]
        [InlineData("show me the file content", "read")]
        [InlineData("查看这个函数", "read")]
        [InlineData("look at main.cpp", "read")]
        public void ClassifyIntent_ReadKeywords_ReturnsRead(string input, string expected)
        {
            Assert.Equal(expected, DynamicToolSelector.ClassifyIntent(input));
        }

        [Theory]
        [InlineData("这个项目怎么用", "general")]
        [InlineData("explain this concept", "general")]
        [InlineData("what does this mean", "general")]
        public void ClassifyIntent_NoKeywordMatch_ReturnsGeneral(string input, string expected)
        {
            Assert.Equal(expected, DynamicToolSelector.ClassifyIntent(input));
        }

        [Fact]
        public void ClassifyIntent_EmptyOrNull_ReturnsConversation()
        {
            Assert.Equal("conversation", DynamicToolSelector.ClassifyIntent(null));
            Assert.Equal("conversation", DynamicToolSelector.ClassifyIntent(""));
            Assert.Equal("conversation", DynamicToolSelector.ClassifyIntent("   "));
        }

        #endregion

        #region ToolGroup mapping tests

        [Theory]
        [InlineData("read_file", ToolGroup.Core)]
        [InlineData("ask_followup_question", ToolGroup.Core)]
        [InlineData("validate_file", ToolGroup.Core)]
        [InlineData("edit", ToolGroup.Edit)]
        [InlineData("write_file", ToolGroup.Edit)]
        [InlineData("grep_search", ToolGroup.Search)]
        [InlineData("glob", ToolGroup.Search)]
        [InlineData("list_dir", ToolGroup.Search)]
        [InlineData("list_code_definition_names", ToolGroup.Search)]
        [InlineData("list_projects", ToolGroup.Search)]
        [InlineData("run_command", ToolGroup.Advanced)]
        public void IsToolInGroups_MappedTools_MatchCorrectGroup(string toolName, ToolGroup expectedGroup)
        {
            Assert.True(DynamicToolSelector.IsToolInGroups(toolName, expectedGroup));
        }

        [Theory]
        [InlineData("gitnexus_context")]
        [InlineData("gitnexus_impact")]
        [InlineData("gitnexus_query")]
        [InlineData("some_unknown_mcp_tool")]
        public void IsToolInGroups_UnmappedTools_DefaultToAdvanced(string toolName)
        {
            Assert.True(DynamicToolSelector.IsToolInGroups(toolName, ToolGroup.Advanced));
            Assert.False(DynamicToolSelector.IsToolInGroups(toolName, ToolGroup.Core));
            Assert.False(DynamicToolSelector.IsToolInGroups(toolName, ToolGroup.Edit));
            Assert.False(DynamicToolSelector.IsToolInGroups(toolName, ToolGroup.Search));
        }

        #endregion

        #region GetGroupsForIntent tests

        [Theory]
        [InlineData("read", TaskComplexity.Simple, ToolGroup.Core | ToolGroup.Search)]
        [InlineData("read", TaskComplexity.Complex, ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced)]
        [InlineData("analyze", TaskComplexity.Simple, ToolGroup.Core | ToolGroup.Search)]
        [InlineData("analyze", TaskComplexity.Complex, ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced)]
        [InlineData("modify", TaskComplexity.Simple, ToolGroup.Core | ToolGroup.Edit | ToolGroup.Search)]
        [InlineData("modify", TaskComplexity.Complex, ToolGroup.All)]
        [InlineData("bug_fix", TaskComplexity.Simple, ToolGroup.Core | ToolGroup.Edit | ToolGroup.Search)]
        [InlineData("bug_fix", TaskComplexity.Complex, ToolGroup.All)]
        [InlineData("command", TaskComplexity.Simple, ToolGroup.Core | ToolGroup.Advanced)]
        [InlineData("command", TaskComplexity.Complex, ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced)]
        [InlineData("general", TaskComplexity.Simple, ToolGroup.All)]
        [InlineData("general", TaskComplexity.Complex, ToolGroup.All)]
        public void GetGroupsForIntent_ReturnsExpectedGroups(string intent, TaskComplexity complexity, ToolGroup expected)
        {
            Assert.Equal(expected, DynamicToolSelector.GetGroupsForIntent(intent, complexity));
        }

        [Fact]
        public void GetGroupsForIntent_UnknownIntent_ReturnsAll()
        {
            Assert.Equal(ToolGroup.All, DynamicToolSelector.GetGroupsForIntent("unknown_intent", TaskComplexity.Simple));
            Assert.Equal(ToolGroup.All, DynamicToolSelector.GetGroupsForIntent("xyz", TaskComplexity.Complex));
        }

        #endregion

        #region SelectTools integration tests

        private static IReadOnlyList<ToolDefinition> CreateAllTools()
        {
            var names = new[]
            {
                "read_file", "ask_followup_question", "validate_file",
                "edit", "write_file",
                "grep_search", "glob", "list_dir", "list_code_definition_names", "list_projects",
                "run_command",
                "gitnexus_context", "gitnexus_impact", "gitnexus_query"
            };
            return names.Select(n => new ToolDefinition { Name = n, Description = n }).ToList();
        }

        [Fact]
        public void SelectTools_Conversation_ReturnsOnlyAskFollowup()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("你好", TaskComplexity.Simple, all);
            Assert.Single(result);
            Assert.Equal("ask_followup_question", result[0].Name);
        }

        [Fact]
        public void SelectTools_ReadSimple_ReturnsCoreAndSearch()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("show me the code", TaskComplexity.Simple, all);
            var names = result.Select(t => t.Name).ToHashSet();

            // Core tools present
            Assert.Contains("read_file", names);
            Assert.Contains("ask_followup_question", names);
            Assert.Contains("validate_file", names);
            // Search tools present
            Assert.Contains("grep_search", names);
            Assert.Contains("glob", names);
            // Edit tools absent
            Assert.DoesNotContain("edit", names);
            Assert.DoesNotContain("write_file", names);
            // Advanced tools absent
            Assert.DoesNotContain("run_command", names);
            Assert.DoesNotContain("gitnexus_context", names);
        }

        [Fact]
        public void SelectTools_ModifySimple_ReturnsCoreEditSearch()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("帮我修改这个函数", TaskComplexity.Simple, all);
            var names = result.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("edit", names);
            Assert.Contains("write_file", names);
            Assert.Contains("grep_search", names);
            Assert.DoesNotContain("run_command", names);
            Assert.DoesNotContain("gitnexus_context", names);
        }

        [Fact]
        public void SelectTools_ModifyComplex_ReturnsAll()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("帮我修改这个函数", TaskComplexity.Complex, all);
            Assert.Equal(all.Count, result.Count);
        }

        [Fact]
        public void SelectTools_GeneralIntent_ReturnsAll()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("这个项目怎么用", TaskComplexity.Simple, all);
            Assert.Equal(all.Count, result.Count);
        }

        [Fact]
        public void SelectTools_NullOrEmpty_ReturnsInput()
        {
            Assert.Null(DynamicToolSelector.SelectTools("test", TaskComplexity.Simple, null));
            var empty = new List<ToolDefinition>();
            Assert.Same(empty, DynamicToolSelector.SelectTools("test", TaskComplexity.Simple, empty));
        }

        [Fact]
        public void SelectTools_CommandSimple_ReturnsCoreAndAdvanced()
        {
            var all = CreateAllTools();
            var result = DynamicToolSelector.SelectTools("运行 dotnet build", TaskComplexity.Simple, all);
            var names = result.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("run_command", names);
            Assert.Contains("gitnexus_context", names); // unmapped → Advanced
            Assert.DoesNotContain("edit", names);
            Assert.DoesNotContain("grep_search", names);
        }

        #endregion
    }
}
