using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Handles permission checks and user approvals for tool execution
    /// </summary>
    public interface IPermissionHandler
    {
        /// <summary>
        /// Request approval from the user for a tool execution
        /// </summary>
        Task<bool> RequestApprovalAsync(
            IAgentTool tool,
            ToolCall call,
            IUIContext uiContext,
            CancellationToken ct = default);

        /// <summary>
        /// Request confirmation from the user for a tool execution
        /// </summary>
        Task<bool> RequestConfirmationAsync(
            IAgentTool tool,
            ToolCall call,
            IUIContext uiContext,
            CancellationToken ct = default);
    }
}
