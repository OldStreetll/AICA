using System;
using System.Linq;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolMetadataTests
    {
        [Fact]
        public void ToolMetadata_WithValidData_CreatesSuccessfully()
        {
            // Act
            var metadata = new ToolMetadata
            {
                Name = "test_tool",
                Description = "Test tool",
                Category = ToolCategory.FileRead,
                RequiresConfirmation = false,
                TimeoutSeconds = 10
            };

            // Assert
            Assert.Equal("test_tool", metadata.Name);
            Assert.Equal(ToolCategory.FileRead, metadata.Category);
        }

        [Fact]
        public void ToolMetadataRegistry_Register_StoresMetadata()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            var metadata = new ToolMetadata
            {
                Name = "test_tool",
                Description = "Test",
                Category = ToolCategory.FileRead
            };

            // Act
            ToolMetadataRegistry.Register(metadata);

            // Assert
            var retrieved = ToolMetadataRegistry.Get("test_tool");
            Assert.NotNull(retrieved);
            Assert.Equal("test_tool", retrieved.Name);
        }

        [Fact]
        public void ToolMetadataRegistry_Register_WithNullMetadata_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                ToolMetadataRegistry.Register(null));
        }

        [Fact]
        public void ToolMetadataRegistry_Register_WithEmptyName_ThrowsException()
        {
            // Arrange
            var metadata = new ToolMetadata { Name = "", Description = "Test" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                ToolMetadataRegistry.Register(metadata));
        }

        [Fact]
        public void ToolMetadataRegistry_Get_WithNonExistentTool_ReturnsNull()
        {
            // Arrange
            ToolMetadataRegistry.Clear();

            // Act
            var result = ToolMetadataRegistry.Get("nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ToolMetadataRegistry_GetByCategory_ReturnsToolsInCategory()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "read_file",
                Description = "Read",
                Category = ToolCategory.FileRead
            });
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "write_file",
                Description = "Write",
                Category = ToolCategory.FileWrite
            });

            // Act
            var fileReadTools = ToolMetadataRegistry.GetByCategory(ToolCategory.FileRead).ToList();

            // Assert
            Assert.Single(fileReadTools);
            Assert.Equal("read_file", fileReadTools[0].Name);
        }

        [Fact]
        public void ToolMetadataRegistry_GetByTag_ReturnsToolsWithTag()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool1",
                Description = "Tool 1",
                Category = ToolCategory.FileRead,
                Tags = new[] { "file", "read" }
            });
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool2",
                Description = "Tool 2",
                Category = ToolCategory.FileWrite,
                Tags = new[] { "file", "write" }
            });

            // Act
            var fileTools = ToolMetadataRegistry.GetByTag("file").ToList();

            // Assert
            Assert.Equal(2, fileTools.Count);
        }

        [Fact]
        public void ToolMetadataRegistry_GetByTag_WithNullTag_ReturnsEmpty()
        {
            // Act
            var result = ToolMetadataRegistry.GetByTag(null).ToList();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void ToolMetadataRegistry_GetRequiringConfirmation_ReturnsConfirmationTools()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "read_file",
                Description = "Read",
                Category = ToolCategory.FileRead,
                RequiresConfirmation = false
            });
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "delete_file",
                Description = "Delete",
                Category = ToolCategory.FileDelete,
                RequiresConfirmation = true
            });

            // Act
            var confirmTools = ToolMetadataRegistry.GetRequiringConfirmation().ToList();

            // Assert
            Assert.Single(confirmTools);
            Assert.Equal("delete_file", confirmTools[0].Name);
        }

        [Fact]
        public void ToolMetadataRegistry_GetRequiringApproval_ReturnsApprovalTools()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool1",
                Description = "Tool 1",
                Category = ToolCategory.Command,
                RequiresApproval = true
            });

            // Act
            var approvalTools = ToolMetadataRegistry.GetRequiringApproval().ToList();

            // Assert
            Assert.Single(approvalTools);
        }

        [Fact]
        public void ToolMetadataRegistry_GetModifyingTools_ReturnsModifyingTools()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "read_file",
                Description = "Read",
                Category = ToolCategory.FileRead,
                IsModifying = false
            });
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "write_file",
                Description = "Write",
                Category = ToolCategory.FileWrite,
                IsModifying = true
            });

            // Act
            var modifyingTools = ToolMetadataRegistry.GetModifyingTools().ToList();

            // Assert
            Assert.Single(modifyingTools);
            Assert.Equal("write_file", modifyingTools[0].Name);
        }

        [Fact]
        public void ToolMetadataRegistry_GetAll_ReturnsAllTools()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool1",
                Description = "Tool 1",
                Category = ToolCategory.FileRead
            });
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool2",
                Description = "Tool 2",
                Category = ToolCategory.FileWrite
            });

            // Act
            var allTools = ToolMetadataRegistry.GetAll().ToList();

            // Assert
            Assert.Equal(2, allTools.Count);
        }

        [Fact]
        public void ToolMetadataRegistry_Clear_RemovesAllTools()
        {
            // Arrange
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "tool1",
                Description = "Tool 1",
                Category = ToolCategory.FileRead
            });

            // Act
            ToolMetadataRegistry.Clear();

            // Assert
            Assert.Null(ToolMetadataRegistry.Get("tool1"));
        }

        [Fact]
        public void ToolMetadataRegistry_Contains_ReturnsTrueForRegisteredTool()
        {
            // Arrange
            ToolMetadataRegistry.Clear();
            ToolMetadataRegistry.Register(new ToolMetadata
            {
                Name = "test_tool",
                Description = "Test",
                Category = ToolCategory.FileRead
            });

            // Act
            var exists = ToolMetadataRegistry.Contains("test_tool");

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void ToolMetadataRegistry_Contains_ReturnsFalseForNonExistentTool()
        {
            // Arrange
            ToolMetadataRegistry.Clear();

            // Act
            var exists = ToolMetadataRegistry.Contains("nonexistent");

            // Assert
            Assert.False(exists);
        }
    }
}
