using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.Prompt;
using AICA.Core.Tests.Agent.Mocks;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    /// <summary>
    /// Regression tests for ToolCall Optimization Plan (Phase A + B).
    /// Validates the migration from control-type to trust-type tool selection.
    /// </summary>
    public class ToolCallOptimizationTests
    {
        #region Phase A — Direction 1: DynamicToolSelector trust-based selection

        [Theory]
        [InlineData("读取这个文件", "read")]
        [InlineData("帮我修改这段代码", "modify")]
        [InlineData("分析一下架构", "analyze")]
        [InlineData("这段代码报错了", "bug_fix")]
        [InlineData("运行 dotnet build", "command")]
        public void SelectTools_NonConversation_ReturnsAllTools(string request, string expectedIntent)
        {
            // Arrange: create a full tool set
            var allTools = CreateFullToolSet();
            var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(request);

            // Verify intent classification is as expected
            Assert.Equal(expectedIntent, DynamicToolSelector.ClassifyIntent(request));

            // Act
            var selected = DynamicToolSelector.SelectTools(request, complexity, allTools);

            // Assert: all tools should be returned (no filtering)
            Assert.Equal(allTools.Count, selected.Count);
        }

        [Theory]
        [InlineData("你好")]
        [InlineData("hello")]
        [InlineData("hi")]
        public void SelectTools_Conversation_ReturnsCoreToolsOnly(string request)
        {
            // Arrange
            var allTools = CreateFullToolSet();
            var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(request);

            // Verify it's classified as conversation
            Assert.Equal("conversation", DynamicToolSelector.ClassifyIntent(request));

            // Act
            var selected = DynamicToolSelector.SelectTools(request, complexity, allTools);

            // Assert: only CoreTools (attempt_completion, ask_followup_question)
            Assert.True(selected.Count <= 2, $"Conversation should have ≤2 tools, got {selected.Count}");
            Assert.All(selected, t =>
                Assert.True(t.Name == "attempt_completion" || t.Name == "ask_followup_question",
                    $"Unexpected tool in conversation: {t.Name}"));
        }

        [Fact]
        public void SelectTools_Complex_ReturnsAllTools()
        {
            // Complex tasks should always get all tools (unchanged behavior)
            var allTools = CreateFullToolSet();

            var selected = DynamicToolSelector.SelectTools(
                "帮我重构整个模块的架构，需要分析依赖关系并逐步修改",
                TaskComplexity.Complex,
                allTools);

            Assert.Equal(allTools.Count, selected.Count);
        }

        [Fact]
        public void SelectTools_ReadIntent_NoLongerFiltersWriteTools()
        {
            // Before: read intent would filter out edit, run_command
            // After: all tools visible — LLM decides
            var allTools = CreateFullToolSet();

            var selected = DynamicToolSelector.SelectTools(
                "这个函数是做什么的",
                TaskComplexity.Simple,
                allTools);

            Assert.Contains(selected, t => t.Name == "edit");
            Assert.Contains(selected, t => t.Name == "run_command");
        }

        [Fact]
        public void SelectTools_AnalyzeIntent_IncludesAllTools()
        {
            // Before: analyze intent excluded write tools
            // After: all tools visible
            var allTools = CreateFullToolSet();

            var selected = DynamicToolSelector.SelectTools(
                "分析一下这个模块的架构",
                TaskComplexity.Medium,
                allTools);

            Assert.Equal(allTools.Count, selected.Count);
        }

        #endregion

        #region Phase A — Direction 3: No GitNexus priority bias

        [Fact]
        public void AddGitNexusGuidance_NoLongerContainsPriorityStrategy()
        {
            // The "首选/次选/避免" priority ordering should be removed
            var builder = new SystemPromptBuilder();
            builder.AddGitNexusGuidance("TestRepo");
            var prompt = builder.Build();

            Assert.DoesNotContain("优先策略", prompt);
            Assert.DoesNotContain("首选", prompt);
            Assert.DoesNotContain("次选", prompt);
            Assert.DoesNotContain("避免", prompt);
        }

        [Fact]
        public void AddGitNexusGuidance_StillContainsFewShotExamples()
        {
            // P1/P2/P3 few-shot examples should be preserved
            var builder = new SystemPromptBuilder();
            builder.AddGitNexusGuidance("TestRepo");
            var prompt = builder.Build();

            // P1: repo parameter
            Assert.Contains("repo", prompt);
            Assert.Contains("TestRepo", prompt);

            // P2: simple symbol names
            Assert.Contains("gitnexus_context", prompt);
            Assert.Contains("gitnexus_impact", prompt);

            // P3: Cypher schema (CodeRelation, r.type)
            Assert.Contains("CodeRelation", prompt);
            Assert.Contains("r.type", prompt);
        }

        [Fact]
        public void AddGitNexusGuidance_EmptyRepoName_NoGuidanceAdded()
        {
            var builder = new SystemPromptBuilder();
            builder.AddGitNexusGuidance("");
            var prompt = builder.Build();

            Assert.DoesNotContain("GitNexus", prompt);
        }

        #endregion

        #region Phase B — Direction 2: No duplicate tool descriptions in System Prompt

        [Fact]
        public void AddToolDescriptions_DoesNotListIndividualToolNames()
        {
            // After Phase B: AddToolDescriptions should NOT enumerate tool names/params
            var tools = CreateFullToolSet();
            var builder = new SystemPromptBuilder();
            builder.AddTools(tools);
            builder.AddToolDescriptions();
            var prompt = builder.Build();

            // Should contain generic tool usage principles
            Assert.Contains("function calling", prompt);
            Assert.Contains("tool_calls", prompt);

            // Should NOT contain per-tool sections (### tool_name format)
            Assert.DoesNotContain("### read_file", prompt);
            Assert.DoesNotContain("### edit", prompt);
            Assert.DoesNotContain("### grep_search", prompt);
            Assert.DoesNotContain("### gitnexus_context", prompt);
        }

        [Fact]
        public void AddToolDescriptions_ContainsGenericPrinciples()
        {
            var builder = new SystemPromptBuilder();
            builder.AddToolDescriptions();
            var prompt = builder.Build();

            Assert.Contains("Tool Usage", prompt);
            Assert.Contains("IMMEDIATELY", prompt);
            Assert.Contains("function calling", prompt);
        }

        #endregion

        #region Phase B — Direction 4: MCP upgrade gate

        [Fact]
        public async Task WaitForMcpUpgrade_NoUpgradePending_ReturnsImmediately()
        {
            var dispatcher = new ToolDispatcher();

            // Should return instantly — no BeginMcpUpgrade called
            await dispatcher.WaitForMcpUpgradeAsync(5000);
            // No exception = pass
        }

        [Fact]
        public async Task WaitForMcpUpgrade_SignaledBeforeWait_ReturnsImmediately()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();
            dispatcher.SignalMcpUpgradeComplete();

            await dispatcher.WaitForMcpUpgradeAsync(5000);
            // No timeout = pass
        }

        [Fact]
        public async Task WaitForMcpUpgrade_SignaledDuringWait_ReturnsWhenSignaled()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();

            // Signal after a short delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                dispatcher.SignalMcpUpgradeComplete();
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await dispatcher.WaitForMcpUpgradeAsync(5000);
            sw.Stop();

            // Should complete well before the 5s timeout
            Assert.True(sw.ElapsedMilliseconds < 2000,
                $"Should complete quickly after signal, took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task WaitForMcpUpgrade_NeverSignaled_TimesOut()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();
            // Never call SignalMcpUpgradeComplete

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await dispatcher.WaitForMcpUpgradeAsync(200); // short timeout
            sw.Stop();

            // Should complete at or near the timeout
            Assert.True(sw.ElapsedMilliseconds >= 150,
                $"Should wait near timeout, only waited {sw.ElapsedMilliseconds}ms");
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Should not wait much longer than timeout, waited {sw.ElapsedMilliseconds}ms");
        }

        #endregion

        #region Integration: AgentExecutor passes all tools to LLM

        [Fact]
        public async Task AgentExecutor_ReadRequest_PassesAllToolsToLLM()
        {
            // Before: read intent would filter tools
            // After: all registered tools should be visible to LLM
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });

            // Register several tools of different categories
            harness.WithTool("read_file", "Read a file");
            harness.WithTool("edit", "Edit a file");
            harness.WithTool("grep_search", "Search for pattern");
            harness.WithTool("run_command", "Run shell command");

            // Act: "read" intent request
            await harness.RunAsync("这个函数是做什么的");

            // Assert: LLM should see ALL tools (including attempt_completion from harness)
            var tools = harness.LlmClient.ReceivedTools[0];
            Assert.Contains(tools, t => t.Name == "read_file");
            Assert.Contains(tools, t => t.Name == "edit");
            Assert.Contains(tools, t => t.Name == "grep_search");
            Assert.Contains(tools, t => t.Name == "run_command");
            Assert.Contains(tools, t => t.Name == "attempt_completion");
        }

        [Fact]
        public async Task AgentExecutor_ConversationRequest_PassesMinimalToolsToLLM()
        {
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("你好！有什么可以帮你的？")
            });

            // Register many tools
            harness.WithTool("read_file", "Read a file");
            harness.WithTool("edit", "Edit a file");
            harness.WithTool("grep_search", "Search for pattern");

            // Act: conversation intent
            await harness.RunAsync("你好");

            // Assert: only CoreTools should be visible
            var tools = harness.LlmClient.ReceivedTools[0];
            Assert.Contains(tools, t => t.Name == "attempt_completion");
            Assert.DoesNotContain(tools, t => t.Name == "read_file");
            Assert.DoesNotContain(tools, t => t.Name == "edit");
            Assert.DoesNotContain(tools, t => t.Name == "grep_search");
        }

        [Fact]
        public async Task AgentExecutor_SystemPrompt_DoesNotContainToolNames()
        {
            // Verify the system prompt sent to LLM doesn't enumerate tool names
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });
            harness.WithTool("read_file", "Read a file");
            harness.WithTool("grep_search", "Search for pattern");

            await harness.RunAsync("读取文件");

            var messages = harness.LlmClient.ReceivedMessages[0];
            var systemPrompt = messages.First(m => m.Role == ChatRole.System).Content;

            // Should NOT contain per-tool sections
            Assert.DoesNotContain("### read_file", systemPrompt);
            Assert.DoesNotContain("### grep_search", systemPrompt);

            // Should contain generic tool usage section
            Assert.Contains("Tool Usage", systemPrompt);
        }

        #endregion

        #region Helpers

        private static IReadOnlyList<ToolDefinition> CreateFullToolSet()
        {
            var toolNames = new[]
            {
                "attempt_completion", "ask_followup_question",
                "read_file", "list_dir", "list_code_definition_names",
                "grep_search", "find_by_name", "list_projects",
                "edit", "run_command",
                "condense", "update_plan",
                "gitnexus_context", "gitnexus_query", "gitnexus_impact"
            };

            return toolNames.Select(name => new ToolDefinition
            {
                Name = name,
                Description = $"Tool: {name}",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>(),
                    Required = new string[0]
                }
            }).ToList();
        }

        #endregion
    }
}
