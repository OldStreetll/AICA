using System.Collections.Generic;
using System.Threading;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Interface for the Agent executor.
    /// </summary>
    public interface IAgentExecutor
    {
        IAsyncEnumerable<AgentStep> ExecuteAsync(
            string userRequest,
            IAgentContext context,
            IUIContext uiContext,
            List<ChatMessage> previousMessages = null,
            CancellationToken ct = default);

        void Abort();
    }
}
