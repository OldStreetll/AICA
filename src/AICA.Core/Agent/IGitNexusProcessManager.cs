using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// State of the GitNexus MCP server process.
    /// </summary>
    public enum GitNexusState
    {
        NotStarted,
        Starting,
        Ready,
        Failed,
        Disposed
    }

    /// <summary>
    /// Manages the GitNexus MCP server process lifecycle.
    /// Abstracted for testability (McpBridgeTool depends on this interface).
    /// </summary>
    public interface IGitNexusProcessManager
    {
        /// <summary>
        /// Current process state.
        /// </summary>
        GitNexusState State { get; }

        /// <summary>
        /// MCP client for communicating with the server. Null if not Ready.
        /// </summary>
        McpClient Client { get; }

        /// <summary>
        /// Ensure the MCP server is running. Attempts a single restart if failed.
        /// Returns true if server is ready for tool calls.
        /// </summary>
        Task<bool> EnsureRunningAsync(CancellationToken ct);
    }
}
