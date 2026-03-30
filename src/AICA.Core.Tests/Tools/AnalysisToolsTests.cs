using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using AICA.Core.Workspace;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class AnalysisToolsTests
    {
        private Mock<IAgentContext> CreateMockContext()
        {
            var context = new Mock<IAgentContext>();
            context.Setup(c => c.WorkingDirectory).Returns("/workspace");
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(true);
            context.Setup(c => c.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            return context;
        }

        private Mock<IUIContext> CreateMockUIContext()
        {
            return new Mock<IUIContext>();
        }

        // v2.0: LogAnalysisTool tests removed (tool deleted, grep_search + read_file covers this)

        #region ListProjectsTool Tests

        [Fact]
        public async Task ListProjectsTool_WithNoProjects_ReturnsMessage()
        {
            // Arrange
            var tool = new ListProjectsTool();
            var context = CreateMockContext();
            context.Setup(c => c.GetProjects()).Returns(new Dictionary<string, ProjectInfo>());
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_projects",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("No projects found", result.Content);
        }

        [Fact]
        public async Task ListProjectsTool_WithProjects_ListsAll()
        {
            // Arrange
            var tool = new ListProjectsTool();
            var context = CreateMockContext();
            var projects = new Dictionary<string, ProjectInfo>
            {
                ["Project1"] = new ProjectInfo
                {
                    Name = "Project1",
                    ProjectType = "Library",
                    ProjectFilePath = "/workspace/Project1/Project1.csproj",
                    ProjectDirectory = "/workspace/Project1",
                    SourceFiles = new List<string> { "File1.cs", "File2.cs" },
                    Filters = new Dictionary<string, List<string>>(),
                    Dependencies = new List<string>()
                },
                ["Project2"] = new ProjectInfo
                {
                    Name = "Project2",
                    ProjectType = "Console",
                    ProjectFilePath = "/workspace/Project2/Project2.csproj",
                    ProjectDirectory = "/workspace/Project2",
                    SourceFiles = new List<string> { "Program.cs" },
                    Filters = new Dictionary<string, List<string>>(),
                    Dependencies = new List<string> { "Project1" }
                }
            };
            context.Setup(c => c.GetProjects()).Returns(projects);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_projects",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Project1", result.Content);
            Assert.Contains("Project2", result.Content);
            Assert.Contains("2 project(s)", result.Content);
        }

        [Fact]
        public async Task ListProjectsTool_WithSpecificProject_ShowsDetails()
        {
            // Arrange
            var tool = new ListProjectsTool();
            var context = CreateMockContext();
            var projects = new Dictionary<string, ProjectInfo>
            {
                ["Project1"] = new ProjectInfo
                {
                    Name = "Project1",
                    ProjectType = "Library",
                    ProjectFilePath = "/workspace/Project1/Project1.csproj",
                    ProjectDirectory = "/workspace/Project1",
                    SourceFiles = new List<string> { "File1.cs", "File2.cs" },
                    Filters = new Dictionary<string, List<string>>
                    {
                        ["Source Files"] = new List<string> { "File1.cs", "File2.cs" }
                    },
                    Dependencies = new List<string> { "Newtonsoft.Json" }
                }
            };
            context.Setup(c => c.GetProjects()).Returns(projects);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_projects",
                Arguments = new Dictionary<string, object>
                {
                    ["project_name"] = "Project1"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Project: Project1", result.Content);
            Assert.Contains("Type: Library", result.Content);
            Assert.Contains("Total Files: 2", result.Content);
            Assert.Contains("Dependencies: Newtonsoft.Json", result.Content);
        }

        [Fact]
        public async Task ListProjectsTool_WithNonexistentProject_ShowsAvailable()
        {
            // Arrange
            var tool = new ListProjectsTool();
            var context = CreateMockContext();
            var projects = new Dictionary<string, ProjectInfo>
            {
                ["Project1"] = new ProjectInfo
                {
                    Name = "Project1",
                    ProjectType = "Library",
                    ProjectFilePath = "/workspace/Project1/Project1.csproj",
                    ProjectDirectory = "/workspace/Project1",
                    SourceFiles = new List<string>(),
                    Filters = new Dictionary<string, List<string>>(),
                    Dependencies = new List<string>()
                }
            };
            context.Setup(c => c.GetProjects()).Returns(projects);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "list_projects",
                Arguments = new Dictionary<string, object>
                {
                    ["project_name"] = "NonexistentProject"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("not found", result.Content);
            Assert.Contains("Available projects", result.Content);
            Assert.Contains("Project1", result.Content);
        }

        [Fact]
        public void ListProjectsTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new ListProjectsTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Analysis, metadata.Category);
            Assert.Equal("list_projects", metadata.Name);
        }

        #endregion
    }
}
