using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class ToolStatsFooterTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<IAgentContext> _context;
        private readonly Mock<IUIContext> _uiContext;

        public ToolStatsFooterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _context = new Mock<IAgentContext>();
            _context.Setup(c => c.WorkingDirectory).Returns(_tempDir);
            _context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(true);
            _context.Setup(c => c.ResolveDirectoryPath(It.IsAny<string>()))
                .Returns((string p) => Path.Combine(_tempDir, p));
            _context.Setup(c => c.ResolveFilePath(It.IsAny<string>()))
                .Returns((string p) => Path.Combine(_tempDir, p));

            _uiContext = new Mock<IUIContext>();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public async Task GrepSearch_Result_ContainsStats()
        {
            // Create files with searchable content
            File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "hello world\nhello again\n");
            File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "hello there\n");

            var tool = new GrepSearchTool();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>
                {
                    ["query"] = "hello",
                    ["path"] = "."
                }
            };

            var result = await tool.ExecuteAsync(call, _context.Object, _uiContext.Object);

            Assert.True(result.Success);
            Assert.Contains("[TOOL_EXACT_STATS:", result.Content);
            Assert.Contains("matches=", result.Content);
            Assert.Contains("files_matched=", result.Content);
            Assert.Contains("files_searched=", result.Content);
        }

        [Fact]
        public async Task GrepSearch_NoMatch_ContainsZeroStats()
        {
            File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "nothing here\n");

            var tool = new GrepSearchTool();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>
                {
                    ["query"] = "xyznonexistent",
                    ["path"] = "."
                }
            };

            var result = await tool.ExecuteAsync(call, _context.Object, _uiContext.Object);

            Assert.True(result.Success);
            Assert.Contains("[TOOL_EXACT_STATS: matches=0", result.Content);
        }

        // v2.0: FindByName_Result_ContainsStats and ListCodeDefs_ContainsStats removed (tools deleted)

        [Fact]
        public async Task ListDir_NonRecursive_ContainsStats()
        {
            // Create some files and dirs
            File.WriteAllText(Path.Combine(_tempDir, "file1.txt"), "");
            File.WriteAllText(Path.Combine(_tempDir, "file2.txt"), "");
            Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

            var tool = new ListDirTool();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_dir",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "."
                }
            };

            var result = await tool.ExecuteAsync(call, _context.Object, _uiContext.Object);

            Assert.True(result.Success);
            Assert.Contains("[TOOL_EXACT_STATS:", result.Content);
            Assert.Contains("directories=", result.Content);
            Assert.Contains("files=", result.Content);
            Assert.Contains("total=", result.Content);
        }
    }
}
