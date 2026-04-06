using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Agent.Middleware;
using AICA.Core.Tests.Agent.Mocks;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class PreValidationMiddlewareTests
    {
        private readonly PreValidationMiddleware _middleware = new PreValidationMiddleware();

        #region Edit validation

        [Fact]
        public async Task Edit_FileNotExists_WithNewString_PassesThrough()
        {
            // Fix 2: File doesn't exist + new_string provided → likely create intent → pass through
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/nonexistent/file.cs" },
                { "old_string", "old" },
                { "new_string", "new content" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task Edit_FileNotExists_WithoutNewString_Blocked()
        {
            // File doesn't exist + no new_string → genuinely wrong path → block
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/nonexistent/file.cs" },
                { "old_string", "old" },
                { "new_string", "" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("不存在", result.Error);
        }

        [Fact]
        public async Task Edit_OldStringEmpty_Blocked()
        {
            var agentContext = new MockAgentContext().WithFile("/src/file.cs", "content");
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/src/file.cs" },
                { "old_string", "" },
                { "new_string", "replacement" }
            }, agentContext);

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("old_string 不能为空", result.Error);
        }

        [Fact]
        public async Task Edit_NoOp_Blocked()
        {
            var agentContext = new MockAgentContext().WithFile("/src/file.cs", "content");
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/src/file.cs" },
                { "old_string", "same text" },
                { "new_string", "same text" }
            }, agentContext);

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("相同", result.Error);
        }

        [Fact]
        public async Task Edit_ValidCall_PassesThrough()
        {
            var agentContext = new MockAgentContext().WithFile("/src/file.cs", "old content");
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/src/file.cs" },
                { "old_string", "old" },
                { "new_string", "new" }
            }, agentContext);

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("passed_through", result.Content);
        }

        [Fact]
        public async Task Edit_FullReplaceMode_SkipsValidation()
        {
            // Fix 1: full_replace is a boolean parameter, not mode="full_replace"
            var context = MakeContext("edit", new Dictionary<string, object>
            {
                { "path", "/new/file.cs" },
                { "new_string", "new content" },
                { "full_replace", "true" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
        }

        #endregion

        #region ReadFile validation

        [Fact]
        public async Task ReadFile_FileNotExists_Blocked()
        {
            var context = MakeContext("read_file", new Dictionary<string, object>
            {
                { "path", "/nonexistent/file.cs" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("不存在", result.Error);
        }

        [Fact]
        public async Task ReadFile_FileExists_PassesThrough()
        {
            var agentContext = new MockAgentContext().WithFile("/src/file.cs", "content");
            var context = MakeContext("read_file", new Dictionary<string, object>
            {
                { "path", "/src/file.cs" }
            }, agentContext);

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
        }

        #endregion

        #region GrepSearch validation

        [Fact]
        public async Task GrepSearch_EmptyQuery_Blocked()
        {
            var context = MakeContext("grep_search", new Dictionary<string, object>
            {
                { "query", "" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("搜索关键词不能为空", result.Error);
        }

        [Fact]
        public async Task GrepSearch_ValidQuery_PassesThrough()
        {
            var context = MakeContext("grep_search", new Dictionary<string, object>
            {
                { "query", "searchTerm" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
        }

        #endregion

        #region Other tools

        [Fact]
        public async Task UnknownTool_AlwaysPassesThrough()
        {
            var context = MakeContext("list_dir", new Dictionary<string, object>
            {
                { "path", "/some/dir" }
            });

            var result = await _middleware.ProcessAsync(context, CancellationToken.None);

            Assert.True(result.Success);
        }

        #endregion

        private ToolExecutionContext MakeContext(
            string toolName,
            Dictionary<string, object> args,
            MockAgentContext agentContext = null)
        {
            agentContext ??= new MockAgentContext();

            var call = new ToolCall
            {
                Id = "test_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = toolName,
                Arguments = args
            };

            return new ToolExecutionContext
            {
                Call = call,
                AgentContext = agentContext,
                StartTime = DateTime.UtcNow,
                Next = (ct) => Task.FromResult(new ToolResult { Success = true, Content = "passed_through" })
            };
        }
    }
}
