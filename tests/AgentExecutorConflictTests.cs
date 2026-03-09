using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using AICA.Core.Agent;
using AICA.Core.Context;
using AICA.Core.LLM;
using AICA.Core.Tools;

namespace AICA.Tests.Agent
{
    public class AgentExecutorConflictTests
    {
        private readonly Mock<ILLMClient> _mockLLMClient;
        private readonly Mock<IAgentContext> _mockContext;
        private readonly Mock<IUIContext> _mockUIContext;
        private readonly ToolDispatcher _toolDispatcher;

        public AgentExecutorConflictTests()
        {
            _mockLLMClient = new Mock<ILLMClient>();
            _mockContext = new Mock<IAgentContext>();
            _mockUIContext = new Mock<IUIContext>();
            _toolDispatcher = new ToolDispatcher();
            _toolDispatcher.RegisterTool(new AskFollowupQuestionTool());
            _toolDispatcher.RegisterTool(new EditFileTool());

            _mockContext.SetupGet(x => x.WorkingDirectory).Returns("D:\\Project\\AIConsProject\\AIHelper");
            _mockContext.SetupGet(x => x.SourceRoots).Returns(Array.Empty<string>());
            _mockContext.Setup(x => x.IsPathAccessible(It.IsAny<string>())).Returns(true);
            _mockContext.Setup(x => x.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mockContext.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("current file content");
            _mockContext.Setup(x => x.ShowDiffAndApplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiffApplyResult { Applied = true });
        }

        [Fact]
        public async Task ExecuteAsync_ModificationRequestWithAlreadyCompliant_ShouldForceFollowupQuestion()
        {
            var userRequest = "Refactor ReadFileTool and WriteFileTool to use ToolResult.Fail()";

            var responses = new Queue<(string Text, List<ToolCall> ToolCalls, string FinishReason)>(new[]
            {
                (
                    "I've analyzed the files. Both ReadFileTool and WriteFileTool already use ToolResult.Fail() for error handling. No changes are needed.",
                    new List<ToolCall>(),
                    "stop"
                ),
                (
                    "I understand there's a conflict.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_1",
                            Name = "ask_followup_question",
                            Arguments = new Dictionary<string, object>
                            {
                                ["question"] = "The files already use ToolResult.Fail(). What would you like to do?",
                                ["options"] = new[]
                                {
                                    new { label = "Keep as is", value = "keep" },
                                    new { label = "Modify anyway", value = "modify" }
                                }
                            }
                        }
                    },
                    "tool_calls"
                )
            });

            var callCount = 0;
            _mockLLMClient.Setup(x => x.StreamChatAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<IEnumerable<ToolDefinition>>(),
                It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (callCount >= responses.Count)
                        throw new InvalidOperationException("Unexpected LLM call");
                    var response = responses.ElementAt(callCount++);
                    return CreateChunks(response.Text, response.ToolCalls, response.FinishReason);
                });

            _mockUIContext.Setup(x => x.AskFollowupQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<QuestionOption>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FollowupQuestionResult { SelectedValue = "keep", Cancelled = false });

            var executor = new AgentExecutor(_mockLLMClient.Object, _toolDispatcher, _mockContext.Object, _mockUIContext.Object);
            var steps = new List<AgentStep>();

            await foreach (var step in executor.ExecuteAsync(userRequest, CancellationToken.None))
            {
                steps.Add(step);
            }

            Assert.True(steps.Any(s => s.Type == AgentStepType.ToolCall && s.ToolName == "ask_followup_question"),
                "Should force ask_followup_question when modification request conflicts with current state");
        }

        [Fact]
        public async Task ExecuteAsync_RecoverableFailures_ShouldNotTriggerConsecutiveErrorExit()
        {
            var userRequest = "Edit the file to fix the bug";

            var responses = new Queue<(string Text, List<ToolCall> ToolCalls, string FinishReason)>(new[]
            {
                (
                    "Let me try to edit the file.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_1",
                            Name = "edit",
                            Arguments = new Dictionary<string, object>
                            {
                                ["file_path"] = "test.cs",
                                ["old_string"] = "wrong text",
                                ["new_string"] = "correct text"
                            }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Let me try with the correct old_string.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_2",
                            Name = "edit",
                            Arguments = new Dictionary<string, object>
                            {
                                ["file_path"] = "test.cs",
                                ["old_string"] = "another wrong text",
                                ["new_string"] = "correct text"
                            }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Let me read the file first to get the exact text.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_3",
                            Name = "read_file",
                            Arguments = new Dictionary<string, object>
                            {
                                ["file_path"] = "test.cs"
                            }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Now I can edit correctly.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_4",
                            Name = "attempt_completion",
                            Arguments = new Dictionary<string, object>
                            {
                                ["result"] = "Fixed the bug"
                            }
                        }
                    },
                    "tool_calls"
                )
            });

            var callCount = 0;
            _mockLLMClient.Setup(x => x.StreamChatAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<IEnumerable<ToolDefinition>>(),
                It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (callCount >= responses.Count)
                        throw new InvalidOperationException("Unexpected LLM call");
                    var response = responses.ElementAt(callCount++);
                    return CreateChunks(response.Text, response.ToolCalls, response.FinishReason);
                });

            var editCallCount = 0;
            _mockContext.Setup(x => x.ShowDiffAndApplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    editCallCount++;
                    return new DiffApplyResult { Applied = false, Error = "old_string not found in file" };
                });

            _toolDispatcher.RegisterTool(new ReadFileTool());
            _toolDispatcher.RegisterTool(new AttemptCompletionTool());

            var executor = new AgentExecutor(_mockLLMClient.Object, _toolDispatcher, _mockContext.Object, _mockUIContext.Object);
            var steps = new List<AgentStep>();

            await foreach (var step in executor.ExecuteAsync(userRequest, CancellationToken.None))
            {
                steps.Add(step);
            }

            Assert.True(steps.Any(s => s.Type == AgentStepType.Completion),
                "Should complete successfully despite recoverable failures");
            Assert.False(steps.Any(s => s.Type == AgentStepType.Error && s.Message.Contains("3 consecutive errors")),
                "Should not trigger consecutive error exit for recoverable failures");
        }

        [Fact]
        public async Task ExecuteAsync_UserCancelledFollowupQuestion_ShouldNotCountAsFatalError()
        {
            var userRequest = "Modify the configuration";

            var responses = new Queue<(string Text, List<ToolCall> ToolCalls, string FinishReason)>(new[]
            {
                (
                    "Let me ask for clarification.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_1",
                            Name = "ask_followup_question",
                            Arguments = new Dictionary<string, object>
                            {
                                ["question"] = "Which configuration file?",
                                ["options"] = new[]
                                {
                                    new { label = "Config A", value = "a" },
                                    new { label = "Config B", value = "b" }
                                }
                            }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "I understand. Let me try a different approach.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_2",
                            Name = "attempt_completion",
                            Arguments = new Dictionary<string, object>
                            {
                                ["result"] = "User cancelled, stopping task"
                            }
                        }
                    },
                    "tool_calls"
                )
            });

            var callCount = 0;
            _mockLLMClient.Setup(x => x.StreamChatAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<IEnumerable<ToolDefinition>>(),
                It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (callCount >= responses.Count)
                        throw new InvalidOperationException("Unexpected LLM call");
                    var response = responses.ElementAt(callCount++);
                    return CreateChunks(response.Text, response.ToolCalls, response.FinishReason);
                });

            _mockUIContext.Setup(x => x.AskFollowupQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<QuestionOption>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FollowupQuestionResult { Cancelled = true });

            _toolDispatcher.RegisterTool(new AttemptCompletionTool());

            var executor = new AgentExecutor(_mockLLMClient.Object, _toolDispatcher, _mockContext.Object, _mockUIContext.Object);
            var steps = new List<AgentStep>();

            await foreach (var step in executor.ExecuteAsync(userRequest, CancellationToken.None))
            {
                steps.Add(step);
            }

            Assert.False(steps.Any(s => s.Type == AgentStepType.Error && s.Message.Contains("3 consecutive errors")),
                "User cancellation should not count as fatal consecutive error");
        }

        [Fact]
        public async Task ExecuteAsync_ReachingThreshold_ShouldInjectRecoveryPromptFirst()
        {
            var userRequest = "Fix the issue";

            var responses = new Queue<(string Text, List<ToolCall> ToolCalls, string FinishReason)>(new[]
            {
                (
                    "Attempting fix 1",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_1",
                            Name = "run_command",
                            Arguments = new Dictionary<string, object> { ["command"] = "invalid1" }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Attempting fix 2",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_2",
                            Name = "run_command",
                            Arguments = new Dictionary<string, object> { ["command"] = "invalid2" }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Attempting fix 3",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_3",
                            Name = "run_command",
                            Arguments = new Dictionary<string, object> { ["command"] = "invalid3" }
                        }
                    },
                    "tool_calls"
                ),
                (
                    "Let me try a different approach after seeing the recovery prompt.",
                    new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "call_4",
                            Name = "ask_followup_question",
                            Arguments = new Dictionary<string, object>
                            {
                                ["question"] = "The command keeps failing. Should I try a different approach?",
                                ["options"] = new[]
                                {
                                    new { label = "Yes", value = "yes" },
                                    new { label = "No", value = "no" }
                                }
                            }
                        }
                    },
                    "tool_calls"
                )
            });

            var callCount = 0;
            _mockLLMClient.Setup(x => x.StreamChatAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<IEnumerable<ToolDefinition>>(),
                It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (callCount >= responses.Count)
                        throw new InvalidOperationException("Unexpected LLM call");
                    var response = responses.ElementAt(callCount++);
                    return CreateChunks(response.Text, response.ToolCalls, response.FinishReason);
                });

            _mockUIContext.Setup(x => x.AskFollowupQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<QuestionOption>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FollowupQuestionResult { SelectedValue = "yes", Cancelled = false });

            var mockRunCommandTool = new Mock<IAgentTool>();
            mockRunCommandTool.SetupGet(x => x.Name).Returns("run_command");
            mockRunCommandTool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<IAgentContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolResult.Fail("Command execution failed"));
            _toolDispatcher.RegisterTool(mockRunCommandTool.Object);

            var executor = new AgentExecutor(_mockLLMClient.Object, _toolDispatcher, _mockContext.Object, _mockUIContext.Object);
            var steps = new List<AgentStep>();

            await foreach (var step in executor.ExecuteAsync(userRequest, CancellationToken.None))
            {
                steps.Add(step);
            }

            Assert.True(steps.Any(s => s.Type == AgentStepType.ToolCall && s.ToolName == "ask_followup_question"),
                "Should inject recovery prompt and allow agent to adjust strategy");
        }

        private async IAsyncEnumerable<LLMChunk> CreateChunks(string text, List<ToolCall> toolCalls, string finishReason)
        {
            if (!string.IsNullOrEmpty(text))
            {
                yield return new LLMChunk { Delta = new LLMChunkDelta { Content = text } };
            }

            if (toolCalls != null && toolCalls.Any())
            {
                foreach (var toolCall in toolCalls)
                {
                    yield return new LLMChunk
                    {
                        Delta = new LLMChunkDelta
                        {
                            ToolCalls = new List<ToolCallDelta>
                            {
                                new ToolCallDelta
                                {
                                    Index = 0,
                                    Id = toolCall.Id,
                                    Type = "function",
                                    Function = new FunctionCallDelta
                                    {
                                        Name = toolCall.Name,
                                        Arguments = System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)
                                    }
                                }
                            }
                        }
                    };
                }
            }

            yield return new LLMChunk
            {
                Delta = new LLMChunkDelta { FinishReason = finishReason }
            };
        }
    }
}
