using System.Collections.Generic;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolCallProcessorSecurityTests
    {
        #region NormalizeSecurityPath

        [Fact]
        public void NormalizeSecurityPath_ForwardSlash_Normalized()
        {
            var result = ToolCallProcessor.NormalizeSecurityPath(".git/config");
            Assert.DoesNotContain("\\", result);
            Assert.Contains(".git/config", result);
        }

        [Fact]
        public void NormalizeSecurityPath_BackSlash_Normalized()
        {
            var result = ToolCallProcessor.NormalizeSecurityPath(".git\\config");
            Assert.DoesNotContain("\\", result);
            Assert.Contains(".git/config", result);
        }

        [Fact]
        public void NormalizeSecurityPath_MixedCase_Lowered()
        {
            var a = ToolCallProcessor.NormalizeSecurityPath(".git/config");
            var b = ToolCallProcessor.NormalizeSecurityPath(".GIT/CONFIG");
            Assert.Equal(a, b);
        }

        [Fact]
        public void NormalizeSecurityPath_DotSegments_Resolved()
        {
            var a = ToolCallProcessor.NormalizeSecurityPath(".git/config");
            var b = ToolCallProcessor.NormalizeSecurityPath("./././.git/config");
            Assert.Equal(a, b);
        }

        [Fact]
        public void NormalizeSecurityPath_ParentTraversal_Resolved()
        {
            // src/../.git/config should resolve to same as .git/config
            var a = ToolCallProcessor.NormalizeSecurityPath(".git/config");
            var b = ToolCallProcessor.NormalizeSecurityPath("src/../.git/config");
            Assert.Equal(a, b);
        }

        [Fact]
        public void NormalizeSecurityPath_Empty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, ToolCallProcessor.NormalizeSecurityPath(""));
            Assert.Equal(string.Empty, ToolCallProcessor.NormalizeSecurityPath(null));
        }

        #endregion

        #region Security Blacklist

        [Fact]
        public void IsPathBlacklisted_BlacklistedPath_ReturnsTrue()
        {
            var blacklist = new HashSet<string>();
            var call = MakeToolCall("read_file", "path", ".git/config");

            ToolCallProcessor.AddToSecurityBlacklist(call, blacklist);
            Assert.True(ToolCallProcessor.IsPathBlacklisted(call, blacklist));
        }

        [Fact]
        public void IsPathBlacklisted_VariantPath_ReturnsTrue()
        {
            var blacklist = new HashSet<string>();

            // Block .git/config
            var original = MakeToolCall("read_file", "path", ".git/config");
            ToolCallProcessor.AddToSecurityBlacklist(original, blacklist);

            // Try with different case
            var upperCase = MakeToolCall("read_file", "path", ".GIT/CONFIG");
            Assert.True(ToolCallProcessor.IsPathBlacklisted(upperCase, blacklist));

            // Try with backslash
            var backSlash = MakeToolCall("read_file", "path", ".git\\config");
            Assert.True(ToolCallProcessor.IsPathBlacklisted(backSlash, blacklist));
        }

        [Fact]
        public void IsPathBlacklisted_DifferentPath_ReturnsFalse()
        {
            var blacklist = new HashSet<string>();
            var blocked = MakeToolCall("read_file", "path", ".git/config");
            ToolCallProcessor.AddToSecurityBlacklist(blocked, blacklist);

            var different = MakeToolCall("read_file", "path", "src/main.cs");
            Assert.False(ToolCallProcessor.IsPathBlacklisted(different, blacklist));
        }

        [Fact]
        public void IsPathBlacklisted_EmptyBlacklist_ReturnsFalse()
        {
            var blacklist = new HashSet<string>();
            var call = MakeToolCall("read_file", "path", ".git/config");
            Assert.False(ToolCallProcessor.IsPathBlacklisted(call, blacklist));
        }

        [Fact]
        public void IsPathBlacklisted_NoPathArg_ReturnsFalse()
        {
            var blacklist = new HashSet<string> { "/some/path" };
            var call = MakeToolCall("grep_search", "query", "test");
            Assert.False(ToolCallProcessor.IsPathBlacklisted(call, blacklist));
        }

        [Fact]
        public void AddToSecurityBlacklist_NullBlacklist_NoException()
        {
            var call = MakeToolCall("read_file", "path", ".git/config");
            ToolCallProcessor.AddToSecurityBlacklist(call, null);
            // No exception thrown
        }

        #endregion

        private static ToolCall MakeToolCall(string name, string argKey, string argValue)
        {
            return new ToolCall
            {
                Id = "test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = name,
                Arguments = new Dictionary<string, object> { { argKey, argValue } }
            };
        }
    }
}
