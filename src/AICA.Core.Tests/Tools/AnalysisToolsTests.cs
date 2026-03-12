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

        #region LogAnalysisTool Tests

        [Fact]
        public async Task LogAnalysisTool_WithValidPath_ReturnsAnalysis()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            var logContent = @"2024-01-01 10:00:00 INFO Application started
2024-01-01 10:00:01 ERROR Failed to connect to database
2024-01-01 10:00:02 WARN Retrying connection
2024-01-01 10:00:03 INFO Connection established";

            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(logContent);

            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "app.log"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Log Analysis Results", result.Content);
            Assert.Contains("Log Statistics", result.Content);
        }

        [Fact]
        public async Task LogAnalysisTool_WithLevelFilter_FiltersCorrectly()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            var logContent = @"2024-01-01 10:00:00 INFO Application started
2024-01-01 10:00:01 ERROR Failed to connect
2024-01-01 10:00:02 WARN Warning message
2024-01-01 10:00:03 ERROR Another error";

            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(logContent);

            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "app.log",
                    ["level"] = "ERROR"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Level Filter: ERROR", result.Content);
        }

        [Fact]
        public async Task LogAnalysisTool_WithSearchPattern_FiltersCorrectly()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            var logContent = @"2024-01-01 10:00:00 INFO Application started
2024-01-01 10:00:01 ERROR Database connection failed
2024-01-01 10:00:02 WARN Network timeout
2024-01-01 10:00:03 ERROR Database query failed";

            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(logContent);

            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "app.log",
                    ["search"] = "Database"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Search Pattern: Database", result.Content);
        }

        [Fact]
        public async Task LogAnalysisTool_WithMissingPath_ReturnsFail()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter: path", result.Error);
        }

        [Fact]
        public async Task LogAnalysisTool_WithAccessDenied_ReturnsFail()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(false);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "restricted.log"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Access denied", result.Error);
        }

        [Fact]
        public async Task LogAnalysisTool_WithFileNotFound_ReturnsFail()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            context.Setup(c => c.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "nonexistent.log"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("File not found", result.Error);
        }

        [Fact]
        public async Task LogAnalysisTool_WithLimit_RespectsLimit()
        {
            // Arrange
            var tool = new LogAnalysisTool();
            var context = CreateMockContext();
            var logLines = new List<string>();
            for (int i = 0; i < 200; i++)
            {
                logLines.Add($"2024-01-01 10:00:{i:D2} INFO Message {i}");
            }
            var logContent = string.Join("\n", logLines);

            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(logContent);

            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "log_analysis",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "app.log",
                    ["limit"] = 50
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Showing: 50", result.Content);
        }

        [Fact]
        public void LogAnalysisTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new LogAnalysisTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Analysis, metadata.Category);
            Assert.Equal("log_analysis", metadata.Name);
        }

        #endregion

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
