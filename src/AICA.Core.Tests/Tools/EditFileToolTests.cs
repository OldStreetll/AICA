using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tests.Agent.Mocks;
using AICA.Core.Tools;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class EditFileToolTests
    {
        private readonly EditFileTool _tool = new EditFileTool();
        private readonly MockUIContext _ui = new MockUIContext();

        private ToolCall MakeCall(Dictionary<string, object> args)
        {
            return new ToolCall { Id = "test-1", Name = "edit", Arguments = args };
        }

        #region Mode A: Single edit (regression)

        [Fact]
        public async Task SingleEdit_ReplacesText()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "int x = 1;\nint y = 2;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["old_string"] = "int x = 1;",
                ["new_string"] = "int x = 42;"
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.True(result.Success);
            Assert.Equal("int x = 42;\nint y = 2;\n", ctx.Files["test.cpp"]);
        }

        #endregion

        #region Mode B: Multi-edit (same file)

        [Fact]
        public async Task MultiEdit_TwoEdits_AppliedCorrectly()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp",
                "int x = 1;\nint y = 2;\nint z = 3;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "int x = 1;", ["new_string"] = "int x = 10;" },
                    new Dictionary<string, object> { ["old_string"] = "int z = 3;", ["new_string"] = "int z = 30;" }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.True(result.Success);
            Assert.Contains("2 edits applied", result.Content);
            Assert.Equal("int x = 10;\nint y = 2;\nint z = 30;\n", ctx.Files["test.cpp"]);
        }

        [Fact]
        public async Task MultiEdit_OffsetDrift_HandledCorrectly()
        {
            // First edit changes length, second edit should still work
            var ctx = new MockAgentContext().WithFile("test.cpp",
                "AAA\nBBB\nCCC\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "AAA", ["new_string"] = "AAAAAA" }, // +3 chars
                    new Dictionary<string, object> { ["old_string"] = "CCC", ["new_string"] = "C" }       // -2 chars
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.True(result.Success);
            Assert.Equal("AAAAAA\nBBB\nC\n", ctx.Files["test.cpp"]);
        }

        [Fact]
        public async Task MultiEdit_OverlappingEdits_ReturnsError()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "ABCDEF\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "ABCD", ["new_string"] = "XXXX" },
                    new Dictionary<string, object> { ["old_string"] = "CDEF", ["new_string"] = "YYYY" }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("overlapping", result.Content?.ToLower() ?? result.Error?.ToLower() ?? "");
        }

        [Fact]
        public async Task MultiEdit_NonUniqueOldString_ReturnsError()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "int x = 1;\nint x = 1;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "int x = 1;", ["new_string"] = "int x = 2;" }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("multiple times", result.Content?.ToLower() ?? result.Error?.ToLower() ?? "");
        }

        [Fact]
        public async Task MultiEdit_IdenticalOldNew_ReturnsError()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "int x = 1;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "int x = 1;", ["new_string"] = "int x = 1;" }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("identical", result.Content?.ToLower() ?? result.Error?.ToLower() ?? "");
        }

        [Fact]
        public async Task MultiEdit_EmptyEdits_ReturnsError()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "content\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>()
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("empty", result.Content?.ToLower() ?? result.Error?.ToLower() ?? "");
        }

        [Fact]
        public async Task MultiEdit_OldStringNotFound_ReturnsError()
        {
            var ctx = new MockAgentContext().WithFile("test.cpp", "int x = 1;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["file_path"] = "test.cpp",
                ["edits"] = new List<object>
                {
                    new Dictionary<string, object> { ["old_string"] = "nonexistent", ["new_string"] = "replacement" }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("Edit #1", result.Content ?? result.Error ?? "");
        }

        #endregion

        #region Mode C: Multi-file

        [Fact]
        public async Task MultiFile_TwoFiles_BothApplied()
        {
            var ctx = new MockAgentContext()
                .WithFile("a.cpp", "int a = 1;\n")
                .WithFile("b.cpp", "int b = 2;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["files"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["file_path"] = "a.cpp",
                        ["edits"] = new List<object>
                        {
                            new Dictionary<string, object> { ["old_string"] = "int a = 1;", ["new_string"] = "int a = 10;" }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["file_path"] = "b.cpp",
                        ["edits"] = new List<object>
                        {
                            new Dictionary<string, object> { ["old_string"] = "int b = 2;", ["new_string"] = "int b = 20;" }
                        }
                    }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.True(result.Success);
            Assert.Contains("2 applied", result.Content);
            Assert.Equal("int a = 10;\n", ctx.Files["a.cpp"]);
            Assert.Equal("int b = 20;\n", ctx.Files["b.cpp"]);
        }

        [Fact]
        public async Task MultiFile_EmptyFiles_ReturnsError()
        {
            var ctx = new MockAgentContext();
            var call = MakeCall(new Dictionary<string, object>
            {
                ["files"] = new List<object>()
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.False(result.Success);
            Assert.Contains("empty", result.Content?.ToLower() ?? result.Error?.ToLower() ?? "");
        }

        [Fact]
        public async Task MultiFile_OneFailsOneContinues()
        {
            // First file has a valid edit, second file doesn't exist
            var ctx = new MockAgentContext()
                .WithFile("a.cpp", "int a = 1;\n");
            var call = MakeCall(new Dictionary<string, object>
            {
                ["files"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["file_path"] = "a.cpp",
                        ["edits"] = new List<object>
                        {
                            new Dictionary<string, object> { ["old_string"] = "int a = 1;", ["new_string"] = "int a = 10;" }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["file_path"] = "nonexistent.cpp",
                        ["edits"] = new List<object>
                        {
                            new Dictionary<string, object> { ["old_string"] = "x", ["new_string"] = "y" }
                        }
                    }
                }
            });

            var result = await _tool.ExecuteAsync(call, ctx, _ui);

            Assert.True(result.Success); // At least one file applied
            Assert.Contains("1 applied", result.Content);
            Assert.Contains("1 failed", result.Content);
            Assert.Equal("int a = 10;\n", ctx.Files["a.cpp"]);
        }

        #endregion
    }
}
