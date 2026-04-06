using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context interface for task management
    /// </summary>
    public interface ITaskContext
    {
        /// <summary>
        /// Request user confirmation for an operation
        /// </summary>
        Task<bool> RequestConfirmationAsync(string operation, string details, CancellationToken ct = default);

        /// <summary>
        /// H6: Files edited during the current session. Used by H3 (Edit diagnosis) and H6 (Condense protected zones).
        /// </summary>
        IReadOnlyCollection<string> EditedFilesInSession { get; }
    }
}
