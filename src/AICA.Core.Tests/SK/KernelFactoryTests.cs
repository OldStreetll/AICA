using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.SK;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Xunit;

namespace AICA.Core.Tests.SK
{
    public class KernelFactoryTests
    {
        private readonly Mock<ILLMClient> _mockLlmClient;
        private readonly LLMClientOptions _options;
        private readonly ToolDispatcher _dispatcher;
        private readonly Mock<IAgentContext> _mockContext;
        private readonly Mock<IUIContext> _mockUiContext;

        public KernelFactoryTests()
        {
            _mockLlmClient = new Mock<ILLMClient>();
            _options = new LLMClientOptions { Model = "test-model" };
            _dispatcher = new ToolDispatcher();
            _mockContext = new Mock<IAgentContext>();
            _mockUiContext = new Mock<IUIContext>();
        }

        [Fact]
        public void Create_ReturnsNonNullKernel()
        {
            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            Assert.NotNull(kernel);
        }

        [Fact]
        public void Create_RegistersChatCompletionService()
        {
            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            Assert.NotNull(chatService);
            Assert.IsType<AICA.Core.SK.Adapters.LLMClientChatCompletionService>(chatService);
        }

        [Fact]
        public void Create_RegistersToolsAsPlugin()
        {
            RegisterSampleTools(_dispatcher);

            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            Assert.True(kernel.Plugins.TryGetPlugin("AICATools", out var plugin));
            Assert.NotNull(plugin);
        }

        [Fact]
        public void Create_AllRegisteredToolsBecomeKernelFunctions()
        {
            RegisterSampleTools(_dispatcher);
            var toolCount = _dispatcher.GetToolNames().Count();

            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            var plugin = kernel.Plugins["AICATools"];
            Assert.Equal(toolCount, plugin.Count());
        }

        [Fact]
        public void Create_ToolFunctionNamesMatchToolNames()
        {
            RegisterSampleTools(_dispatcher);

            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            var plugin = kernel.Plugins["AICATools"];
            var functionNames = plugin.Select(f => f.Name).ToHashSet();

            foreach (var toolName in _dispatcher.GetToolNames())
            {
                Assert.Contains(toolName, functionNames);
            }
        }

        [Fact]
        public void Create_EmptyDispatcher_CreatesPluginWithNoFunctions()
        {
            // No tools registered
            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            var plugin = kernel.Plugins["AICATools"];
            Assert.Empty(plugin);
        }

        [Fact]
        public void CreateLightweight_ReturnsKernelWithoutPlugins()
        {
            var kernel = KernelFactory.CreateLightweight(
                _mockLlmClient.Object, _options);

            Assert.NotNull(kernel);
            Assert.Empty(kernel.Plugins);
        }

        [Fact]
        public void CreateLightweight_RegistersChatCompletionService()
        {
            var kernel = KernelFactory.CreateLightweight(
                _mockLlmClient.Object, _options);

            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            Assert.NotNull(chatService);
        }

        [Fact]
        public void Create_NullLlmClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KernelFactory.Create(null, _options, _dispatcher,
                    _mockContext.Object, _mockUiContext.Object));
        }

        [Fact]
        public void Create_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KernelFactory.Create(_mockLlmClient.Object, null, _dispatcher,
                    _mockContext.Object, _mockUiContext.Object));
        }

        [Fact]
        public void Create_NullDispatcher_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KernelFactory.Create(_mockLlmClient.Object, _options, null,
                    _mockContext.Object, _mockUiContext.Object));
        }

        [Fact]
        public void CreateLightweight_NullLlmClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KernelFactory.CreateLightweight(null, _options));
        }

        [Fact]
        public void CreateLightweight_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                KernelFactory.CreateLightweight(_mockLlmClient.Object, null));
        }

        [Fact]
        public void Create_ModelIdAttribute_MatchesOptions()
        {
            var kernel = KernelFactory.Create(
                _mockLlmClient.Object, _options, _dispatcher,
                _mockContext.Object, _mockUiContext.Object);

            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            Assert.True(chatService.Attributes.ContainsKey("ModelId"));
            Assert.Equal("test-model", chatService.Attributes["ModelId"]);
        }

        /// <summary>
        /// Register a few sample tools to simulate AICA's 14 built-in tools.
        /// </summary>
        private void RegisterSampleTools(ToolDispatcher dispatcher)
        {
            dispatcher.RegisterTool(new StubTool("read_file", "Read a file",
                new[] { ("path", "string", true) }));
            dispatcher.RegisterTool(new StubTool("grep_search", "Search files",
                new[] { ("pattern", "string", true), ("path", "string", false) }));
            dispatcher.RegisterTool(new StubTool("list_dir", "List directory",
                new[] { ("path", "string", true) }));
        }

        /// <summary>
        /// Minimal IAgentTool stub for testing registration.
        /// </summary>
        private class StubTool : IAgentTool
        {
            private readonly (string name, string type, bool required)[] _params;

            public string Name { get; }
            public string Description { get; }

            public StubTool(string name, string description,
                (string name, string type, bool required)[] parameters)
            {
                Name = name;
                Description = description;
                _params = parameters;
            }

            public ToolDefinition GetDefinition()
            {
                var properties = new Dictionary<string, ToolParameterProperty>();
                var required = new List<string>();

                foreach (var p in _params)
                {
                    properties[p.name] = new ToolParameterProperty
                    {
                        Type = p.type,
                        Description = $"Parameter: {p.name}"
                    };
                    if (p.required) required.Add(p.name);
                }

                return new ToolDefinition
                {
                    Name = Name,
                    Description = Description,
                    Parameters = new ToolParameters
                    {
                        Type = "object",
                        Properties = properties,
                        Required = required.ToArray()
                    }
                };
            }

            public ToolMetadata GetMetadata() => new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.FileRead
            };

            public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context,
                IUIContext uiContext, CancellationToken ct = default)
                => Task.FromResult(ToolResult.Ok($"Stub result for {Name}"));

            public Task HandlePartialAsync(ToolCall call, IUIContext ui,
                CancellationToken ct = default)
                => Task.CompletedTask;
        }
    }
}
