using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolIntegrationTests
    {
        private Mock<IAgentContext> CreateMockContext()
        {
            var context = new Mock<IAgentContext>();
            context.Setup(c => c.WorkingDirectory).Returns("/workspace");
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(true);
            context.Setup(c => c.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file content");
            context.Setup(c => c.RequestConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.Setup(c => c.EditedFilesInSession)
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return context;
        }

        private Mock<IUIContext> CreateMockUIContext()
        {
            return new Mock<IUIContext>();
        }

        [Fact]
        public async Task ReadFileTool_WithValidPath_ReturnsContent()
        {
            // Arrange
            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("file content", result.Content);
        }

        [Fact]
        public async Task ReadFileTool_WithMissingPath_ReturnsFail()
        {
            // Arrange
            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter", result.Error);
        }

        [Fact]
        public async Task ReadFileTool_WithOffset_ReturnsLimitedContent()
        {
            // Arrange
            var tool = new ReadFileTool();
            var context = CreateMockContext();
            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("line1\nline2\nline3\nline4\nline5");
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "test.txt",
                    ["offset"] = 2,
                    ["limit"] = 2
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("line2", result.Content);
            Assert.Contains("line3", result.Content);
        }

        [Fact]
        public async Task EditFileTool_WithValidEdit_ShowsDiff()
        {
            // Arrange
            var tool = new EditFileTool();
            var context = CreateMockContext();
            context.Setup(c => c.ShowDiffAndApplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiffApplyResult { Applied = true });
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "edit",
                Arguments = new Dictionary<string, object>
                {
                    ["file_path"] = "test.txt",
                    ["old_string"] = "old content",
                    ["new_string"] = "new content"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
        }

        [Fact]
        public async Task EditFileTool_WithMissingOldString_ReturnsFail()
        {
            // Arrange
            var tool = new EditFileTool();
            var context = CreateMockContext();
            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file content");
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "edit",
                Arguments = new Dictionary<string, object>
                {
                    ["file_path"] = "test.txt",
                    ["old_string"] = "nonexistent",
                    ["new_string"] = "new content"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("not found", result.Error.ToLower());
        }

        // WriteFileTool tests removed — tool deleted in favor of run_command + edit flow

        [Fact]
        public async Task ToolDispatcher_WithRegisteredTools_ExecutesTool()
        {
            // Arrange
            var dispatcher = new ToolDispatcher();
            dispatcher.RegisterTool(new ReadFileTool());

            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            var result = await dispatcher.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ToolDispatcher_WithUnknownTool_ReturnsFail()
        {
            // Arrange
            var dispatcher = new ToolDispatcher();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "unknown_tool",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await dispatcher.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Unknown tool", result.Error);
        }

        [Fact]
        public void ToolDispatcher_GetToolDefinitions_ReturnsDefinitions()
        {
            // Arrange
            var dispatcher = new ToolDispatcher();
            dispatcher.RegisterTool(new ReadFileTool());
            dispatcher.RegisterTool(new EditFileTool());

            // Act
            var definitions = dispatcher.GetToolDefinitions();

            // Assert
            Assert.NotNull(definitions);
            var defList = new List<ToolDefinition>(definitions);
            Assert.Equal(2, defList.Count);
        }

        [Fact]
        public void ToolRegistry_WithMultipleTools_SupportsQueries()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(new ReadFileTool());
            registry.Register(new EditFileTool());
            registry.Register(new RunCommandTool());

            // Act
            var fileReadTools = registry.GetByCategory(ToolCategory.FileRead);
            var fileWriteTools = registry.GetByCategory(ToolCategory.FileWrite);
            var confirmTools = registry.GetRequiringConfirmation();

            // Assert
            Assert.NotEmpty(fileReadTools);
            Assert.NotEmpty(fileWriteTools);
            Assert.NotEmpty(confirmTools);
        }

        [Fact]
        public async Task ToolExecutionPipeline_WithMiddleware_ProcessesRequest()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var middlewareCalled = false;

            var middleware = new TestMiddleware(() => middlewareCalled = true);
            pipeline.Use(middleware);

            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.True(middlewareCalled);
        }

        private class TestMiddleware : IToolExecutionMiddleware
        {
            private readonly Action _callback;

            public TestMiddleware(Action callback)
            {
                _callback = callback;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                _callback();
                return await context.Next(ct);
            }
        }
    }
}
