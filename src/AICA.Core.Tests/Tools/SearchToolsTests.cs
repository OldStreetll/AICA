using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class SearchToolsTests
    {
        private Mock<IAgentContext> CreateMockContext()
        {
            var context = new Mock<IAgentContext>();
            context.Setup(c => c.WorkingDirectory).Returns("/workspace");
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(true);
            return context;
        }

        private Mock<IUIContext> CreateMockUIContext()
        {
            return new Mock<IUIContext>();
        }

        #region GrepSearchTool Tests

        [Fact]
        public async Task GrepSearchTool_WithValidQuery_ReturnsMatches()
        {
            // Arrange
            var tool = new GrepSearchTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>
                {
                    ["query"] = "test"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.NotNull(result);
            // Note: Actual search results depend on file system, so we just verify it doesn't throw
        }

        [Fact]
        public async Task GrepSearchTool_WithMissingQuery_ReturnsFail()
        {
            // Arrange
            var tool = new GrepSearchTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter", result.Error);
        }

        [Fact]
        public async Task GrepSearchTool_WithEmptyQuery_ReturnsFail()
        {
            // Arrange
            var tool = new GrepSearchTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>
                {
                    ["query"] = ""
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("cannot be empty", result.Error);
        }

        [Fact]
        public async Task GrepSearchTool_WithInvalidRegex_ReturnsFail()
        {
            // Arrange
            var tool = new GrepSearchTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "grep_search",
                Arguments = new Dictionary<string, object>
                {
                    ["query"] = "[invalid(regex"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Invalid regex", result.Error);
        }

        [Fact]
        public void GrepSearchTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new GrepSearchTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Search, metadata.Category);
            Assert.Equal("grep_search", metadata.Name);
        }

        #endregion

        #region FindByNameTool Tests

        [Fact]
        public async Task FindByNameTool_WithValidPattern_ReturnsMatches()
        {
            // Arrange
            var tool = new FindByNameTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "find_by_name",
                Arguments = new Dictionary<string, object>
                {
                    ["pattern"] = "*.cs"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.NotNull(result);
            // Note: Actual results depend on file system
        }

        [Fact]
        public async Task FindByNameTool_WithMissingPattern_ReturnsFail()
        {
            // Arrange
            var tool = new FindByNameTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "find_by_name",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter", result.Error);
        }

        [Fact]
        public async Task FindByNameTool_WithEmptyPattern_ReturnsFail()
        {
            // Arrange
            var tool = new FindByNameTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "find_by_name",
                Arguments = new Dictionary<string, object>
                {
                    ["pattern"] = ""
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("cannot be empty", result.Error);
        }

        [Fact]
        public void FindByNameTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new FindByNameTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Search, metadata.Category);
            Assert.Equal("find_by_name", metadata.Name);
        }

        #endregion

        #region ListDirTool Tests

        [Fact]
        public async Task ListDirTool_WithValidPath_ReturnsListing()
        {
            // Arrange
            var tool = new ListDirTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_dir",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "."
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.NotNull(result);
            // Note: Actual results depend on file system
        }

        [Fact]
        public async Task ListDirTool_WithMissingPath_UsesDefault()
        {
            // Arrange
            var tool = new ListDirTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_dir",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.NotNull(result);
            // Should use working directory as default
        }

        [Fact]
        public void ListDirTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new ListDirTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.DirectoryOps, metadata.Category);
            Assert.Equal("list_dir", metadata.Name);
        }

        #endregion

        #region ListCodeDefinitionsTool Tests

        [Fact]
        public async Task ListCodeDefinitionsTool_WithValidPath_ReturnsDefinitions()
        {
            // Arrange
            var tool = new ListCodeDefinitionsTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_code_definition_names",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "."
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ListCodeDefinitionsTool_WithAccessDenied_ReturnsFail()
        {
            // Arrange
            var tool = new ListCodeDefinitionsTool();
            var context = CreateMockContext();
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(false);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_code_definition_names",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "restricted"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Access denied", result.Error);
        }

        [Fact]
        public void ListCodeDefinitionsTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new ListCodeDefinitionsTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Analysis, metadata.Category);
            Assert.Equal("list_code_definition_names", metadata.Name);
        }

        #endregion
    }
}
