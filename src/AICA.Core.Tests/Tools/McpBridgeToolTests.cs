using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class McpBridgeToolTests
    {
        private readonly Mock<IGitNexusProcessManager> _mockPm;
        private readonly Mock<IAgentContext> _mockContext;
        private readonly Mock<IUIContext> _mockUI;

        public McpBridgeToolTests()
        {
            _mockPm = new Mock<IGitNexusProcessManager>();
            _mockContext = new Mock<IAgentContext>();
            _mockContext.Setup(c => c.WorkingDirectory).Returns("/workspace");
            _mockUI = new Mock<IUIContext>();
        }

        #region Factory Tests

        [Fact]
        public void CreateAllTools_Returns6Tools()
        {
            _mockPm.Setup(p => p.State).Returns(GitNexusState.NotStarted);

            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            Assert.Equal(6, tools.Count);
        }

        [Fact]
        public void CreateAllTools_NullProcessManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => McpBridgeTool.CreateAllTools(null));
        }

        [Fact]
        public void CreateAllTools_ToolNamesAreCorrect()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            var names = new HashSet<string>();
            foreach (var tool in tools)
            {
                names.Add(tool.Name);
            }

            Assert.Contains("gitnexus_context", names);
            Assert.Contains("gitnexus_impact", names);
            Assert.Contains("gitnexus_query", names);
            Assert.Contains("gitnexus_detect_changes", names);
            Assert.Contains("gitnexus_rename", names);
            Assert.Contains("gitnexus_cypher", names);
        }

        [Fact]
        public void CreateAllTools_AllToolsImplementIAgentTool()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            foreach (var tool in tools)
            {
                Assert.IsAssignableFrom<IAgentTool>(tool);
                Assert.NotNull(tool.Name);
                Assert.NotNull(tool.Description);
                Assert.NotNull(tool.GetDefinition());
                Assert.NotNull(tool.GetMetadata());
            }
        }

        [Fact]
        public void CreateAllTools_RenameToolRequiresConfirmation()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            McpBridgeTool renameTool = null;
            foreach (var tool in tools)
            {
                if (tool.Name == "gitnexus_rename")
                {
                    renameTool = tool;
                    break;
                }
            }

            Assert.NotNull(renameTool);
            Assert.True(renameTool.GetMetadata().RequiresConfirmation);
            Assert.True(renameTool.GetMetadata().IsModifying);
            Assert.Equal(ToolCategory.FileWrite, renameTool.GetMetadata().Category);
        }

        [Fact]
        public void CreateAllTools_AnalysisToolsAreReadOnly()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            var readOnlyNames = new HashSet<string>
            {
                "gitnexus_context", "gitnexus_impact", "gitnexus_query",
                "gitnexus_detect_changes", "gitnexus_cypher"
            };

            foreach (var tool in tools)
            {
                if (readOnlyNames.Contains(tool.Name))
                {
                    Assert.False(tool.GetMetadata().IsModifying, $"{tool.Name} should not be modifying");
                    Assert.False(tool.GetMetadata().RequiresConfirmation, $"{tool.Name} should not require confirmation");
                }
            }
        }

        [Fact]
        public void CreateAllTools_DefinitionsHaveRequiredParams()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            foreach (var tool in tools)
            {
                var def = tool.GetDefinition();
                Assert.Equal(tool.Name, def.Name);
                Assert.NotNull(def.Parameters);
                Assert.NotNull(def.Parameters.Properties);
                Assert.True(def.Parameters.Properties.Count > 0, $"{tool.Name} should have at least one parameter");
            }
        }

        [Fact]
        public void CreateAllTools_ContextToolHasSearchTags()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);
            var contextTool = tools[0];

            Assert.Equal("gitnexus_context", contextTool.Name);
            Assert.Equal(ToolCategory.Analysis, contextTool.GetMetadata().Category);
            Assert.Contains("gitnexus", contextTool.GetMetadata().Tags);
        }

        [Fact]
        public void CreateAllTools_QueryToolIsCategorySearch()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);

            McpBridgeTool queryTool = null;
            foreach (var tool in tools)
            {
                if (tool.Name == "gitnexus_query")
                {
                    queryTool = tool;
                    break;
                }
            }

            Assert.NotNull(queryTool);
            Assert.Equal(ToolCategory.Search, queryTool.GetMetadata().Category);
        }

        #endregion

        #region ExecuteAsync Tests

        [Fact]
        public async Task ExecuteAsync_WhenServerNotReady_UsesFallback()
        {
            // Arrange
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            bool fallbackCalled = false;
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallback =
                (c, ctx, ui, ct) =>
                {
                    fallbackCalled = true;
                    return Task.FromResult(ToolResult.Ok("grep fallback result"));
                };

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object, fallback);
            var queryTool = tools[2]; // gitnexus_query

            var call = new ToolCall
            {
                Id = "test-2",
                Name = "gitnexus_query",
                Arguments = new Dictionary<string, object> { ["query"] = "CChannel" }
            };

            // Act
            var result = await queryTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.True(fallbackCalled);
            Assert.True(result.Success);
            Assert.Contains("grep fallback", result.Content);
        }

        [Fact]
        public async Task ExecuteAsync_WhenServerNotReady_NoFallback_ReturnsFail()
        {
            // Arrange — detect_changes has no fallback
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object);
            var detectTool = tools[3]; // gitnexus_detect_changes

            var call = new ToolCall
            {
                Id = "test-3",
                Name = "gitnexus_detect_changes",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await detectTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("not available", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenEnsureRunningThrows_UsesFallback()
        {
            // Arrange
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Node.js not found"));

            bool fallbackCalled = false;
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> fallback =
                (c, ctx, ui, ct) =>
                {
                    fallbackCalled = true;
                    return Task.FromResult(ToolResult.Ok("fallback"));
                };

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object, fallback);
            var contextTool = tools[0]; // gitnexus_context

            var call = new ToolCall
            {
                Id = "test-5",
                Name = "gitnexus_context",
                Arguments = new Dictionary<string, object> { ["name"] = "test" }
            };

            // Act
            var result = await contextTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.True(fallbackCalled);
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ExecuteAsync_WhenEnsureRunningThrows_NoFallback_ReturnsFail()
        {
            // Arrange — cypher has no fallback
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Node.js not found"));

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object);
            var cypherTool = tools[5]; // gitnexus_cypher

            var call = new ToolCall
            {
                Id = "test-6",
                Name = "gitnexus_cypher",
                Arguments = new Dictionary<string, object> { ["query"] = "MATCH (n) RETURN n" }
            };

            // Act
            var result = await cypherTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("not available", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenServerNotReady_ContextUsesGrepFallback()
        {
            // Arrange — context tool should use grep fallback
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            string capturedName = null;
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback =
                (c, ctx, ui, ct) =>
                {
                    capturedName = c.Name;
                    return Task.FromResult(ToolResult.Ok("grep results"));
                };

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object, grepFallback);
            var contextTool = tools[0]; // gitnexus_context

            var call = new ToolCall
            {
                Id = "test-7",
                Name = "gitnexus_context",
                Arguments = new Dictionary<string, object> { ["name"] = "CChannel" }
            };

            // Act
            var result = await contextTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("gitnexus_context", capturedName);
        }

        [Fact]
        public async Task ExecuteAsync_WhenServerNotReady_ImpactUsesGrepFallback()
        {
            // Arrange — impact tool should use grep fallback
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            bool fallbackCalled = false;
            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback =
                (c, ctx, ui, ct) =>
                {
                    fallbackCalled = true;
                    return Task.FromResult(ToolResult.Ok("grep fallback"));
                };

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object, grepFallback);
            var impactTool = tools[1]; // gitnexus_impact

            var call = new ToolCall
            {
                Id = "test-8",
                Name = "gitnexus_impact",
                Arguments = new Dictionary<string, object> { ["target"] = "CAxis" }
            };

            // Act
            var result = await impactTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert
            Assert.True(fallbackCalled);
        }

        [Fact]
        public async Task ExecuteAsync_WhenServerNotReady_RenameReturnsError()
        {
            // Arrange — rename has no fallback (always returns error)
            var mockPm = new Mock<IGitNexusProcessManager>();
            mockPm.Setup(p => p.EnsureRunningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Func<ToolCall, IAgentContext, IUIContext, CancellationToken, Task<ToolResult>> grepFallback =
                (c, ctx, ui, ct) => Task.FromResult(ToolResult.Ok("should not be called"));

            var tools = McpBridgeTool.CreateAllTools(mockPm.Object, grepFallback);
            var renameTool = tools[4]; // gitnexus_rename

            var call = new ToolCall
            {
                Id = "test-9",
                Name = "gitnexus_rename",
                Arguments = new Dictionary<string, object>
                {
                    ["old_name"] = "foo",
                    ["new_name"] = "bar"
                }
            };

            // Act
            var result = await renameTool.ExecuteAsync(call, _mockContext.Object, _mockUI.Object);

            // Assert — rename has no fallback, should fail
            Assert.False(result.Success);
            Assert.Contains("not available", result.Error);
        }

        [Fact]
        public async Task HandlePartialAsync_CompletesWithoutError()
        {
            var tools = McpBridgeTool.CreateAllTools(_mockPm.Object);
            var tool = tools[0];

            var call = new ToolCall { Id = "1", Name = "gitnexus_context", IsPartial = true };

            // Should not throw
            await tool.HandlePartialAsync(call, _mockUI.Object);
        }

        #endregion
    }
}
