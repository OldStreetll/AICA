using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Tools
{
    public class InteractionToolsTests
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

        #region AskFollowupQuestionTool Tests

        [Fact]
        public async Task AskFollowupQuestionTool_WithValidQuestion_ReturnsAnswer()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();

            var expectedAnswer = new FollowupQuestionResult
            {
                Answer = "Option 1",
                Cancelled = false,
                IsCustomInput = false
            };

            uiContext.Setup(u => u.ShowFollowupQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<QuestionOption>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedAnswer);

            var options = new[]
            {
                new { label = "Option 1", value = "opt1", description = "First option" },
                new { label = "Option 2", value = "opt2", description = "Second option" }
            };

            var call = new ToolCall
            {
                Id = "1",
                Name = "ask_followup_question",
                Arguments = new Dictionary<string, object>
                {
                    ["question"] = "Which option do you prefer?",
                    ["options"] = JsonSerializer.SerializeToElement(options)
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Option 1", result.Content);
        }

        [Fact]
        public async Task AskFollowupQuestionTool_WithMissingQuestion_ReturnsFail()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "ask_followup_question",
                Arguments = new Dictionary<string, object>
                {
                    ["options"] = JsonSerializer.SerializeToElement(new[] { new { label = "Yes", value = "yes" } })
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter: question", result.Error);
        }

        [Fact]
        public async Task AskFollowupQuestionTool_WithEmptyQuestion_ReturnsFail()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "ask_followup_question",
                Arguments = new Dictionary<string, object>
                {
                    ["question"] = "",
                    ["options"] = JsonSerializer.SerializeToElement(new[] { new { label = "Yes", value = "yes" } })
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("cannot be empty", result.Error);
        }

        [Fact]
        public async Task AskFollowupQuestionTool_WithMissingOptions_ReturnsFail()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "ask_followup_question",
                Arguments = new Dictionary<string, object>
                {
                    ["question"] = "What do you think?"
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Missing required parameter: options", result.Error);
        }

        [Fact]
        public async Task AskFollowupQuestionTool_WithUserCancellation_ReturnsFail()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();

            var cancelledResult = new FollowupQuestionResult
            {
                Cancelled = true
            };

            uiContext.Setup(u => u.ShowFollowupQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<QuestionOption>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(cancelledResult);

            var call = new ToolCall
            {
                Id = "1",
                Name = "ask_followup_question",
                Arguments = new Dictionary<string, object>
                {
                    ["question"] = "Continue?",
                    ["options"] = JsonSerializer.SerializeToElement(new[] { new { label = "Yes", value = "yes" } })
                }
            };

            // Act
            var result = await tool.ExecuteAsync(call, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("cancelled", result.Error.ToLower());
        }

        [Fact]
        public void AskFollowupQuestionTool_GetMetadata_ReturnsCorrectCategory()
        {
            // Arrange
            var tool = new AskFollowupQuestionTool();

            // Act
            var metadata = tool.GetMetadata();

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(ToolCategory.Interaction, metadata.Category);
            Assert.Equal("ask_followup_question", metadata.Name);
            Assert.True(metadata.RequiresApproval);
        }

        #endregion

        // v2.0: AttemptCompletionTool and CondenseTool tests removed (tools deleted in v2.0 rewrite)
        // v2.2: UpdatePlanTool tests removed (tool deleted in step 2)
    }
}
