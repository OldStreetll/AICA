using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Agent
{
    /// <summary>
    /// VS implementation of IPermissionHandler.
    /// Approval: delegates to UIContext confirmation dialog.
    /// Confirmation: returns true (handled internally by each tool via ShowDiffAndApplyAsync etc.)
    /// </summary>
    internal class VSPermissionHandler : IPermissionHandler
    {
        public async Task<bool> RequestApprovalAsync(
            IAgentTool tool, ToolCall call, IUIContext uiContext, CancellationToken ct)
        {
            return await uiContext.ShowConfirmationAsync(
                "工具调用审批: " + tool.Name,
                "AI 请求执行 " + tool.Name + "，是否允许？",
                ct);
        }

        public Task<bool> RequestConfirmationAsync(
            IAgentTool tool, ToolCall call, IUIContext uiContext, CancellationToken ct)
        {
            // Confirmation is handled by each tool internally (e.g. ShowDiffAndApplyAsync)
            // Returning true here avoids double-confirmation
            return Task.FromResult(true);
        }
    }
}
