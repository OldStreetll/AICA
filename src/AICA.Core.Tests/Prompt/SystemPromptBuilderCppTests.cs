using AICA.Core.Agent;
using AICA.Core.Prompt;
using AICA.Core.Rules;
using Xunit;

namespace AICA.Core.Tests.Prompt
{
    public class SystemPromptBuilderCppTests
    {
        [Fact]
        public void CppRuleTemplates_ContainsAllRequiredRules()
        {
            var templates = CppRuleTemplates.GetAll();

            Assert.Equal(6, templates.Count);

            // Verify all expected file names exist
            var fileNames = new System.Collections.Generic.HashSet<string>();
            foreach (var (fileName, _) in templates)
            {
                fileNames.Add(fileName);
            }
            Assert.Contains("cpp-code-style.md", fileNames);
            Assert.Contains("cpp-reliability.md", fileNames);
            Assert.Contains("cpp-file-io.md", fileNames);
            Assert.Contains("cpp-qt-specific.md", fileNames);
            Assert.Contains("cpp-comment-template.md", fileNames);
            Assert.Contains("cpp-aica-guidance.md", fileNames);
        }

        [Fact]
        public void CppRuleTemplates_CodeStyle_ContainsKeyRules()
        {
            Assert.Contains("Allman", CppRuleTemplates.CodeStyle);
            Assert.Contains("m_", CppRuleTemplates.CodeStyle);
            Assert.Contains("Bit32", CppRuleTemplates.CodeStyle);
            Assert.Contains("4 个空格", CppRuleTemplates.CodeStyle);
        }

        [Fact]
        public void CppRuleTemplates_Reliability_ContainsMemorySafety()
        {
            Assert.Contains("malloc/free", CppRuleTemplates.Reliability);
            Assert.Contains("snprintf", CppRuleTemplates.Reliability);
            Assert.Contains("NULL", CppRuleTemplates.Reliability);
        }

        [Fact]
        public void CppRuleTemplates_AicaGuidance_ContainsTestAndExplanation()
        {
            Assert.Contains("Google Test", CppRuleTemplates.AicaGuidance);
            Assert.Contains("调用链", CppRuleTemplates.AicaGuidance);
            Assert.Contains("模块", CppRuleTemplates.AicaGuidance);
        }

        [Fact]
        public void CppRuleTemplates_CommentTemplate_ContainsDoxygen()
        {
            Assert.Contains("doxygen", CppRuleTemplates.CommentTemplate);
            Assert.Contains("@brief", CppRuleTemplates.CommentTemplate);
            Assert.Contains("@param", CppRuleTemplates.CommentTemplate);
        }

        [Fact]
        public void CppRuleTemplates_AllHaveFrontmatter()
        {
            foreach (var (fileName, content) in CppRuleTemplates.GetAll())
            {
                Assert.True(content.TrimStart().StartsWith("---"), $"{fileName} missing frontmatter");
                Assert.True(content.Contains("paths:"), $"{fileName} missing paths");
                Assert.True(content.Contains("**/*.cpp"), $"{fileName} missing cpp glob");
                Assert.True(content.Contains("enabled: true"), $"{fileName} not enabled");
            }
        }

        [Fact]
        public void AddBugFixGuidance_BugFixIntent_ContainsSteps()
        {
            var builder = new SystemPromptBuilder();
            builder.AddBugFixGuidance("bug_fix", ProjectLanguage.Unknown);
            var prompt = builder.Build();

            Assert.Contains("grep_search", prompt);
            Assert.Contains("read_file", prompt);
            Assert.Contains("修复建议", prompt);
        }

        [Fact]
        public void AddBugFixGuidance_BugFixCpp_ContainsCppChecks()
        {
            var builder = new SystemPromptBuilder();
            builder.AddBugFixGuidance("bug_fix", ProjectLanguage.CppC);
            var prompt = builder.Build();

            Assert.Contains("malloc/free", prompt);
            Assert.Contains("fopen/fclose", prompt);
        }

        [Fact]
        public void AddBugFixGuidance_NonBugIntent_NoGuidance()
        {
            var builder = new SystemPromptBuilder();
            builder.AddBugFixGuidance("read", ProjectLanguage.CppC);
            var prompt = builder.Build();

            Assert.DoesNotContain("Bug 定位", prompt);
        }
    }
}
