using AICA.Core.Agent;
using AICA.Core.Prompt;
using Xunit;

namespace AICA.Core.Tests.Prompt
{
    public class SystemPromptBuilderCppTests
    {
        [Fact]
        public void AddCppSpecialization_CppLanguage_ContainsGoogleTest()
        {
            var builder = new SystemPromptBuilder();
            builder.AddCppSpecialization(ProjectLanguage.CppC);
            var prompt = builder.Build();

            Assert.Contains("Google Test", prompt);
            Assert.Contains("Allman", prompt);
            Assert.Contains("m_", prompt);
            Assert.Contains("Bit32", prompt);
            Assert.Contains("doxygen", prompt);
        }

        [Fact]
        public void AddCppSpecialization_CSharp_NoGoogleTest()
        {
            var builder = new SystemPromptBuilder();
            builder.AddCppSpecialization(ProjectLanguage.CSharp);
            var prompt = builder.Build();

            Assert.DoesNotContain("Google Test", prompt);
            Assert.DoesNotContain("C/C++ 专业化", prompt);
        }

        [Fact]
        public void AddCppSpecialization_Unknown_NoSpecialization()
        {
            var builder = new SystemPromptBuilder();
            builder.AddCppSpecialization(ProjectLanguage.Unknown);
            var prompt = builder.Build();

            Assert.DoesNotContain("C/C++ 专业化", prompt);
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
