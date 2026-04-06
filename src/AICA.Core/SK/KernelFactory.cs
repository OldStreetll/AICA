using System;
using AICA.Core.Agent;
using AICA.Core.LLM;
using AICA.Core.SK.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AICA.Core.SK
{
    /// <summary>
    /// Factory for creating configured SK Kernel instances.
    /// Registers the AICA LLM adapter as the chat completion service
    /// and all AICA tools as a KernelPlugin.
    /// </summary>
    public static class KernelFactory
    {
        /// <summary>
        /// Create a Kernel with AICA's LLM client and tool dispatcher wired in.
        /// The Kernel is fully configured and ready for SK agent use.
        /// </summary>
        /// <param name="llmClient">AICA LLM client (reused for all LLM calls)</param>
        /// <param name="options">LLM client options (model name, etc.)</param>
        /// <param name="toolDispatcher">Tool dispatcher with registered tools and middleware</param>
        /// <param name="context">Agent context for workspace operations</param>
        /// <param name="uiContext">UI context for user interactions</param>
        /// <param name="logger">Optional logger</param>
        public static Kernel Create(
            ILLMClient llmClient,
            LLMClientOptions options,
            ToolDispatcher toolDispatcher,
            IAgentContext context,
            IUIContext uiContext,
            ILogger logger = null)
        {
            if (llmClient == null) throw new ArgumentNullException(nameof(llmClient));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (toolDispatcher == null) throw new ArgumentNullException(nameof(toolDispatcher));

            var builder = Kernel.CreateBuilder();

            // Register the AICA LLM adapter as SK's chat completion service
            var chatService = new LLMClientChatCompletionService(llmClient, options, logger);
            builder.Services.AddSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(chatService);

            // Add logging if available
            if (logger != null)
            {
                builder.Services.AddSingleton(logger);
            }

            var kernel = builder.Build();

            // Register all AICA tools as a KernelPlugin
            var plugin = AgentToolPluginAdapter.CreatePlugin(toolDispatcher, context, uiContext, logger);
            kernel.Plugins.Add(plugin);

            return kernel;
        }

        /// <summary>
        /// Create a lightweight Kernel with only the LLM service (no tools).
        /// Useful for summarization, analysis, and other non-tool tasks.
        /// </summary>
        public static Kernel CreateLightweight(
            ILLMClient llmClient,
            LLMClientOptions options,
            ILogger logger = null)
        {
            if (llmClient == null) throw new ArgumentNullException(nameof(llmClient));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var builder = Kernel.CreateBuilder();

            var chatService = new LLMClientChatCompletionService(llmClient, options, logger);
            builder.Services.AddSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(chatService);

            if (logger != null)
            {
                builder.Services.AddSingleton(logger);
            }

            return builder.Build();
        }
    }
}
