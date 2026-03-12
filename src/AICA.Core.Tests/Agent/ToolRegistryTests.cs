using System.Linq;
using AICA.Core.Agent;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolRegistryTests
    {
        private Mock<IAgentTool> CreateMockTool(string name, ToolCategory category, string[] tags = null)
        {
            var mock = new Mock<IAgentTool>();
            mock.Setup(t => t.Name).Returns(name);
            mock.Setup(t => t.Description).Returns($"Description for {name}");
            mock.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = name,
                Description = $"Description for {name}",
                Category = category,
                Tags = tags ?? new string[] { }
            });
            return mock;
        }

        [Fact]
        public void Register_WithValidTool_StoresToolAndMetadata()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool = CreateMockTool("test_tool", ToolCategory.FileRead).Object;

            // Act
            registry.Register(tool);

            // Assert
            Assert.NotNull(registry.GetTool("test_tool"));
            Assert.NotNull(registry.GetMetadata("test_tool"));
        }

        [Fact]
        public void Register_WithNullTool_ThrowsException()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() => registry.Register(null));
        }

        [Fact]
        public void GetTool_WithRegisteredTool_ReturnsTool()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool = CreateMockTool("test_tool", ToolCategory.FileRead).Object;
            registry.Register(tool);

            // Act
            var result = registry.GetTool("test_tool");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test_tool", result.Name);
        }

        [Fact]
        public void GetTool_WithNonExistentTool_ReturnsNull()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.GetTool("nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetTool_WithNullName_ReturnsNull()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.GetTool(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetMetadata_WithRegisteredTool_ReturnsMetadata()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool = CreateMockTool("test_tool", ToolCategory.FileRead).Object;
            registry.Register(tool);

            // Act
            var result = registry.GetMetadata("test_tool");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test_tool", result.Name);
        }

        [Fact]
        public void GetMetadata_WithNonExistentTool_ReturnsNull()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.GetMetadata("nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetByCategory_ReturnsToolsInCategory()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("read_file", ToolCategory.FileRead).Object);
            registry.Register(CreateMockTool("write_file", ToolCategory.FileWrite).Object);
            registry.Register(CreateMockTool("read_file2", ToolCategory.FileRead).Object);

            // Act
            var fileReadTools = registry.GetByCategory(ToolCategory.FileRead).ToList();

            // Assert
            Assert.Equal(2, fileReadTools.Count);
            Assert.All(fileReadTools, t => Assert.Equal(ToolCategory.FileRead, registry.GetMetadata(t.Name).Category));
        }

        [Fact]
        public void GetByTag_ReturnsToolsWithTag()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead, new[] { "file", "read" }).Object);
            registry.Register(CreateMockTool("tool2", ToolCategory.FileWrite, new[] { "file", "write" }).Object);
            registry.Register(CreateMockTool("tool3", ToolCategory.Search, new[] { "search" }).Object);

            // Act
            var fileTools = registry.GetByTag("file").ToList();

            // Assert
            Assert.Equal(2, fileTools.Count);
        }

        [Fact]
        public void GetByTag_WithNullTag_ReturnsEmpty()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead, new[] { "file" }).Object);

            // Act
            var result = registry.GetByTag(null).ToList();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetRequiringConfirmation_ReturnsConfirmationTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool1 = new Mock<IAgentTool>();
            tool1.Setup(t => t.Name).Returns("tool1");
            tool1.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = "tool1",
                Category = ToolCategory.FileRead,
                RequiresConfirmation = false
            });

            var tool2 = new Mock<IAgentTool>();
            tool2.Setup(t => t.Name).Returns("tool2");
            tool2.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = "tool2",
                Category = ToolCategory.FileDelete,
                RequiresConfirmation = true
            });

            registry.Register(tool1.Object);
            registry.Register(tool2.Object);

            // Act
            var confirmTools = registry.GetRequiringConfirmation().ToList();

            // Assert
            Assert.Single(confirmTools);
            Assert.Equal("tool2", confirmTools[0].Name);
        }

        [Fact]
        public void GetRequiringApproval_ReturnsApprovalTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool1 = new Mock<IAgentTool>();
            tool1.Setup(t => t.Name).Returns("tool1");
            tool1.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = "tool1",
                Category = ToolCategory.Command,
                RequiresApproval = true
            });

            registry.Register(tool1.Object);

            // Act
            var approvalTools = registry.GetRequiringApproval().ToList();

            // Assert
            Assert.Single(approvalTools);
        }

        [Fact]
        public void GetModifyingTools_ReturnsModifyingTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("read_file", ToolCategory.FileRead).Object);

            var writeTool = new Mock<IAgentTool>();
            writeTool.Setup(t => t.Name).Returns("write_file");
            writeTool.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = "write_file",
                Category = ToolCategory.FileWrite,
                IsModifying = true
            });
            registry.Register(writeTool.Object);

            // Act
            var modifyingTools = registry.GetModifyingTools().ToList();

            // Assert
            Assert.Single(modifyingTools);
            Assert.Equal("write_file", modifyingTools[0].Name);
        }

        [Fact]
        public void GetAll_ReturnsAllTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead).Object);
            registry.Register(CreateMockTool("tool2", ToolCategory.FileWrite).Object);

            // Act
            var allTools = registry.GetAll().ToList();

            // Assert
            Assert.Equal(2, allTools.Count);
        }

        [Fact]
        public void GetToolNames_ReturnsAllToolNames()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead).Object);
            registry.Register(CreateMockTool("tool2", ToolCategory.FileWrite).Object);

            // Act
            var names = registry.GetToolNames().ToList();

            // Assert
            Assert.Equal(2, names.Count);
            Assert.Contains("tool1", names);
            Assert.Contains("tool2", names);
        }

        [Fact]
        public void Contains_WithRegisteredTool_ReturnsTrue()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("test_tool", ToolCategory.FileRead).Object);

            // Act
            var result = registry.Contains("test_tool");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Contains_WithNonExistentTool_ReturnsFalse()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.Contains("nonexistent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Count_ReturnsNumberOfTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead).Object);
            registry.Register(CreateMockTool("tool2", ToolCategory.FileWrite).Object);

            // Act
            var count = registry.Count;

            // Assert
            Assert.Equal(2, count);
        }

        [Fact]
        public void Clear_RemovesAllTools()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.Register(CreateMockTool("tool1", ToolCategory.FileRead).Object);

            // Act
            registry.Clear();

            // Assert
            Assert.Equal(0, registry.Count);
            Assert.Null(registry.GetTool("tool1"));
        }
    }
}
