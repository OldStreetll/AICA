using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.Tests.Agent.Mocks;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Baseline tests for AgentExecutor behavior.
    /// These tests freeze current behavior before R1 extraction.
    /// </summary>
    public class AgentExecutorTests
    {
        [Fact]
        public async Task SimpleConversation_ReturnsTextChunkAndComplete()
        {
            // Arrange: LLM returns text, then attempt_completion
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Text("Hello! How can I help you?"),
                MockLlmResponse.Completion("Greeted the user.")
            });

            // Act
            var steps = await harness.RunAsync("你好");

            // Assert: should have text chunks and complete
            Assert.True(steps.Any(s => s.Type == AgentStepType.TextChunk || s.Type == AgentStepType.Complete),
                "Should produce text or complete steps");
        }

        [Fact]
        public async Task ToolCallFlow_ExecutesToolAndContinues()
        {
            // Arrange: LLM calls read_file, then attempt_completion
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.WithToolCall("read_file", new Dictionary<string, object>
                {
                    ["path"] = "/workspace/test.cs"
                }),
                MockLlmResponse.Completion("Read the file successfully.")
            });
            harness.WithTool("read_file", "Read a file", "file content here");
            harness.WithFile("/workspace/test.cs", "// test file content");

            // Act
            var steps = await harness.RunAsync("读取 test.cs");

            // Assert: should have tool start, tool result, and complete
            Assert.True(AgentEvalHarness.HasToolCall(steps, "read_file"),
                "Should have read_file tool call");
            Assert.True(AgentEvalHarness.IsCompleted(steps),
                "Should complete after tool execution");
        }

        [Fact]
        public async Task IterationLimit_ForcesCompletionOrError()
        {
            // Arrange: LLM keeps returning text without completing (maxIterations = 3)
            var responses = Enumerable.Range(0, 5)
                .Select(_ => MockLlmResponse.Text("Still working..."))
                .ToArray();

            var harness = new AgentEvalHarness(responses, maxIterations: 3);

            // Act
            var steps = await harness.RunAsync("做一个复杂的任务");

            // Assert: should stop at or near iteration limit
            // Either produces an error or forces completion
            var hasEndStep = steps.Any(s =>
                s.Type == AgentStepType.Complete || s.Type == AgentStepType.Error);
            Assert.True(hasEndStep, "Should terminate at iteration limit");
        }

        [Fact]
        public async Task MultipleToolCalls_ExecutesInSequence()
        {
            // Arrange: LLM calls two tools, then completes
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.WithToolCall("grep_search", new Dictionary<string, object>
                {
                    ["query"] = "Logger",
                    ["path"] = "/workspace"
                }),
                MockLlmResponse.WithToolCall("read_file", new Dictionary<string, object>
                {
                    ["path"] = "/workspace/Logger.cs"
                }),
                MockLlmResponse.Completion("Found Logger class implementation.")
            });
            harness.WithTool("grep_search", "Search for pattern", "Logger.cs:10: class Logger");
            harness.WithTool("read_file", "Read a file", "public class Logger { }");

            // Act
            var steps = await harness.RunAsync("找到 Logger 类");

            // Assert
            Assert.True(AgentEvalHarness.HasToolCall(steps, "grep_search"));
            Assert.True(AgentEvalHarness.HasToolCall(steps, "read_file"));
            Assert.True(AgentEvalHarness.IsCompleted(steps));
        }

        [Fact]
        public async Task ToolCallFailure_ContinuesExecution()
        {
            // Arrange: First tool fails, LLM retries with different tool, then completes
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.WithToolCall("read_file", new Dictionary<string, object>
                {
                    ["path"] = "/nonexistent.cs"
                }),
                MockLlmResponse.Completion("Could not find the file.")
            });
            harness.WithFailingTool("read_file", "File not found: /nonexistent.cs");

            // Act
            var steps = await harness.RunAsync("读取 nonexistent.cs");

            // Assert: should have tool result with failure, then continue
            var toolResults = steps.Where(s => s.Type == AgentStepType.ToolResult).ToList();
            Assert.True(toolResults.Count > 0, "Should have tool results");
        }

        [Fact]
        public async Task LlmClientReceivesSystemPromptAndUserMessage()
        {
            // Arrange
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });

            // Act
            await harness.RunAsync("你好");

            // Assert: LLM should receive messages including system prompt and user message
            Assert.True(harness.LlmClient.CallCount >= 1, "LLM should be called at least once");

            var firstMessages = harness.LlmClient.ReceivedMessages[0];
            Assert.True(firstMessages.Any(m => m.Role == ChatRole.System),
                "Should include system prompt");
            Assert.True(firstMessages.Any(m => m.Role == ChatRole.User),
                "Should include user message");
        }

        [Fact]
        public async Task LlmClientReceivesToolDefinitions()
        {
            // Arrange
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });
            harness.WithTool("read_file", "Read a file");

            // Act
            await harness.RunAsync("你好");

            // Assert: LLM should receive tool definitions
            Assert.True(harness.LlmClient.CallCount >= 1);
            var tools = harness.LlmClient.ReceivedTools[0];
            Assert.True(tools.Count >= 1, "Should pass tool definitions to LLM");
            Assert.Contains(tools, t => t.Name == "attempt_completion");
        }

        [Fact]
        public async Task TaskState_TracksIteration()
        {
            // Arrange
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.WithToolCall("read_file", new Dictionary<string, object>
                {
                    ["path"] = "/test.cs"
                }),
                MockLlmResponse.Completion("Done.")
            });
            harness.WithTool("read_file", "Read a file", "content");

            // Act
            await harness.RunAsync("读取文件");

            // Assert
            Assert.True(harness.Executor.CurrentTaskState.Iteration >= 1,
                "Iteration count should be at least 1");
        }

        [Fact]
        public async Task PreviousMessages_IncludedInConversation()
        {
            // Arrange
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });

            var previousMessages = new List<ChatMessage>
            {
                ChatMessage.User("之前的问题"),
                ChatMessage.Assistant("之前的回答"),
                ChatMessage.User("新的问题")
            };

            // Act
            await harness.RunAsync("新的问题", previousMessages);

            // Assert: LLM should receive previous messages
            var firstMessages = harness.LlmClient.ReceivedMessages[0];
            Assert.True(firstMessages.Count >= 4,
                "Should include system prompt + previous messages");
        }
        [Fact]
        public async Task NoToolCalls_MetaReasoningText_NotSuppressed_D04()
        {
            // D-04: When LLM returns text that matches IsInternalReasoning patterns
            // but has NO tool calls (finish_reason: stop), the text should NOT be suppressed.
            // It IS the final answer, not meta-reasoning before tool execution.
            var reasoningLikeAnswer = "用户要求解释虚函数的概念。\n\n" +
                "虚函数是C++中实现多态的核心机制。通过在基类中声明虚函数，" +
                "派生类可以重写该函数，从而在运行时根据对象的实际类型调用正确的函数版本。";

            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Text(reasoningLikeAnswer)
            });

            // Act
            var steps = await harness.RunAsync("解释一下C++中的虚函数");

            // Assert: response should be yielded as Complete, not suppressed
            Assert.True(AgentEvalHarness.IsCompleted(steps),
                "Response should complete even when text matches meta-reasoning patterns");

            var completionText = AgentEvalHarness.GetCompletionText(steps);
            Assert.True(completionText != null && completionText.Contains("虚函数"),
                "Completion text should contain the actual answer, not be empty");
        }
    }
}
