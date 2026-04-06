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
    /// Regression tests for ToolCall Optimization (Phase A+B → v2.5 intent-based filtering).
    /// Validates intent-based tool group selection and trust-type tool descriptions.
    /// </summary>
    public class ToolCallOptimizationTests
    {
        #region v2.5 — Intent-based tool filtering

        [Fact]
        public void SelectTools_ModifySimple_ReturnsCoreEditSearch()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools("帮我修改这段代码", TaskComplexity.Simple, allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            // Core + Edit + Search present
            Assert.Contains("read_file", names);
            Assert.Contains("edit", names);
            Assert.Contains("write_file", names);
            Assert.Contains("grep_search", names);
            // Advanced absent for Simple modify
            Assert.DoesNotContain("run_command", names);
            Assert.DoesNotContain("gitnexus_context", names);
        }

        [Fact]
        public void SelectTools_ModifyComplex_ReturnsAll()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools(
                "帮我重构整个模块的架构，需要分析依赖关系并逐步修改",
                TaskComplexity.Complex,
                allTools);
            Assert.Equal(allTools.Count, selected.Count);
        }

        [Fact]
        public void SelectTools_ReadSimple_ReturnsCoreAndSearch()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools("读取这个文件", TaskComplexity.Simple, allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("grep_search", names);
            Assert.DoesNotContain("edit", names);
            Assert.DoesNotContain("run_command", names);
        }

        [Fact]
        public void SelectTools_AnalyzeSimple_ReturnsCoreAndSearch()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools(
                "分析一下这个模块的架构",
                TaskComplexity.Simple,
                allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("grep_search", names);
            Assert.DoesNotContain("edit", names);
            Assert.DoesNotContain("run_command", names);
        }

        [Fact]
        public void SelectTools_AnalyzeComplex_IncludesAdvanced()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools(
                "分析一下这个模块的架构",
                TaskComplexity.Complex,
                allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            Assert.Contains("run_command", names);
            Assert.Contains("gitnexus_context", names);
        }

        [Fact]
        public void SelectTools_CommandSimple_ReturnsCoreAndAdvanced()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools("运行 dotnet build", TaskComplexity.Simple, allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("run_command", names);
            Assert.DoesNotContain("edit", names);
            Assert.DoesNotContain("grep_search", names);
        }

        [Fact]
        public void SelectTools_BugFixSimple_ReturnsCoreEditSearch()
        {
            var allTools = CreateFullToolSet();
            var selected = DynamicToolSelector.SelectTools("这段代码报错了", TaskComplexity.Simple, allTools);
            var names = selected.Select(t => t.Name).ToHashSet();

            Assert.Contains("read_file", names);
            Assert.Contains("edit", names);
            Assert.Contains("grep_search", names);
            Assert.DoesNotContain("run_command", names);
        }

        [Fact]
        public void SelectTools_GeneralIntent_ReturnsAll()
        {
            var allTools = CreateFullToolSet();
            // "这个函数是做什么的" has no keyword matches → general → All
            var selected = DynamicToolSelector.SelectTools("这个函数是做什么的", TaskComplexity.Simple, allTools);
            Assert.Equal(allTools.Count, selected.Count);
        }

        #endregion

        #region Conversation intent

        [Theory]
        [InlineData("你好")]
        [InlineData("hello")]
        [InlineData("hi")]
        public void SelectTools_Conversation_ReturnsCoreToolsOnly(string request)
        {
            var allTools = CreateFullToolSet();
            var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(request);
            Assert.Equal("conversation", DynamicToolSelector.ClassifyIntent(request));

            var selected = DynamicToolSelector.SelectTools(request, complexity, allTools);
            Assert.True(selected.Count <= 2, $"Conversation should have ≤2 tools, got {selected.Count}");
            Assert.All(selected, t =>
                Assert.True(t.Name == "ask_followup_question",
                    $"Unexpected tool in conversation: {t.Name}"));
        }

        #endregion

        #region Phase A — Direction 3: No GitNexus priority bias

        [Fact]
        public void AddGitNexusGuidance_NoLongerContainsPriorityStrategy()
        {
            var builder = new SystemPromptBuilder();
            builder.AddGitNexusGuidance("TestRepo");
            var prompt = builder.Build();

            Assert.DoesNotContain("优先策略", prompt);
            Assert.DoesNotContain("首选", prompt);
            Assert.DoesNotContain("次选", prompt);
            Assert.DoesNotContain("避免", prompt);
        }

        [Fact]
        public void AddGitNexusGuidance_IsNoOp()
        {
            // v2.3+: GitNexus guidance is injected via MCP resources, not AddGitNexusGuidance
            var builder = new SystemPromptBuilder();
            builder.AddGitNexusGuidance("TestRepo");
            var prompt = builder.Build();

            // Should only contain base prompt (no GitNexus specifics added by this method)
            Assert.DoesNotContain("gitnexus_context", prompt);
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
            var tools = CreateFullToolSet();
            var builder = new SystemPromptBuilder();
            builder.AddTools(tools);
#pragma warning disable CS0618
            builder.AddToolDescriptions();
#pragma warning restore CS0618
            var prompt = builder.Build();

            // Should NOT contain per-tool sections (### tool_name format)
            Assert.DoesNotContain("### read_file", prompt);
            Assert.DoesNotContain("### edit", prompt);
            Assert.DoesNotContain("### grep_search", prompt);
            Assert.DoesNotContain("### gitnexus_context", prompt);
        }

        [Fact]
        public void AddToolDescriptions_IsNoOp()
        {
            // AddToolDescriptions is obsolete and no-op — tool descriptions come from function calling API
            var builder = new SystemPromptBuilder();
#pragma warning disable CS0618
            builder.AddToolDescriptions();
#pragma warning restore CS0618
            var prompt = builder.Build();

            // Should only contain base prompt
            Assert.Contains("AICA", prompt);
            Assert.DoesNotContain("Tool Usage", prompt); // No-op doesn't add Tool Usage
        }

        #endregion

        #region Phase B — Direction 4: MCP upgrade gate

        [Fact]
        public async Task WaitForMcpUpgrade_NoUpgradePending_ReturnsImmediately()
        {
            var dispatcher = new ToolDispatcher();
            await dispatcher.WaitForMcpUpgradeAsync(5000);
        }

        [Fact]
        public async Task WaitForMcpUpgrade_SignaledBeforeWait_ReturnsImmediately()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();
            dispatcher.SignalMcpUpgradeComplete();
            await dispatcher.WaitForMcpUpgradeAsync(5000);
        }

        [Fact]
        public async Task WaitForMcpUpgrade_SignaledDuringWait_ReturnsWhenSignaled()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();

            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                dispatcher.SignalMcpUpgradeComplete();
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await dispatcher.WaitForMcpUpgradeAsync(5000);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 2000,
                $"Should complete quickly after signal, took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task WaitForMcpUpgrade_NeverSignaled_TimesOut()
        {
            var dispatcher = new ToolDispatcher();
            dispatcher.BeginMcpUpgrade();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await dispatcher.WaitForMcpUpgradeAsync(200);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= 150,
                $"Should wait near timeout, only waited {sw.ElapsedMilliseconds}ms");
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Should not wait much longer than timeout, waited {sw.ElapsedMilliseconds}ms");
        }

        #endregion

        #region Integration: AgentExecutor tool passing

        [Fact]
        public async Task AgentExecutor_ReadRequest_PassesFilteredToolsToLLM()
        {
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });

            harness.WithTool("read_file", "Read a file");
            harness.WithTool("edit", "Edit a file");
            harness.WithTool("grep_search", "Search for pattern");
            harness.WithTool("run_command", "Run shell command");

            await harness.RunAsync("查看这个函数");

            var tools = harness.LlmClient.ReceivedTools[0];
            var names = tools.Select(t => t.Name).ToHashSet();

            // Read+Simple → Core + Search (no Edit, no Advanced)
            Assert.Contains("read_file", names);
            Assert.Contains("grep_search", names);
        }

        [Fact]
        public async Task AgentExecutor_ConversationRequest_PassesMinimalToolsToLLM()
        {
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("你好！有什么可以帮你的？")
            });

            harness.WithTool("read_file", "Read a file");
            harness.WithTool("edit", "Edit a file");
            harness.WithTool("grep_search", "Search for pattern");

            await harness.RunAsync("你好");

            var tools = harness.LlmClient.ReceivedTools[0];
            Assert.DoesNotContain(tools, t => t.Name == "read_file");
            Assert.DoesNotContain(tools, t => t.Name == "edit");
            Assert.DoesNotContain(tools, t => t.Name == "grep_search");
        }

        [Fact]
        public async Task AgentExecutor_SystemPrompt_DoesNotContainToolNames()
        {
            var harness = new AgentEvalHarness(new[]
            {
                MockLlmResponse.Completion("Done.")
            });
            harness.WithTool("read_file", "Read a file");
            harness.WithTool("grep_search", "Search for pattern");

            await harness.RunAsync("查看文件");

            var messages = harness.LlmClient.ReceivedMessages[0];
            var systemPrompt = messages.First(m => m.Role == ChatRole.System).Content;

            // Should NOT contain per-tool sections
            Assert.DoesNotContain("### read_file", systemPrompt);
            Assert.DoesNotContain("### grep_search", systemPrompt);

            // Should contain rules section
            Assert.Contains("Rules", systemPrompt);
        }

        #endregion

        #region Helpers

        private static IReadOnlyList<ToolDefinition> CreateFullToolSet()
        {
            var toolNames = new[]
            {
                "ask_followup_question",
                "read_file", "write_file", "list_dir", "glob",
                "grep_search", "list_projects",
                "edit", "run_command",
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
