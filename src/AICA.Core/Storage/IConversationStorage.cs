using System.Collections.Generic;
using System.Threading.Tasks;

namespace AICA.Core.Storage
{
    /// <summary>
    /// Interface for conversation storage, enabling future storage engine swaps.
    /// Current implementation: file-based JSON (ConversationStorage).
    /// </summary>
    public interface IConversationStorage
    {
        Task SaveConversationAsync(ConversationRecord record);
        Task<ConversationRecord> LoadConversationAsync(string id);
        Task<List<ConversationSummary>> ListConversationsAsync(int limit = 50);
        Task<List<ConversationSummary>> ListConversationsForProjectAsync(string projectPath, int limit = 50);
        Task<List<ConversationSummary>> ListAllConversationsAsync(int limit = 100);
        Task<bool> DeleteConversationAsync(string id);
        Task<string> ExportAsMarkdownAsync(string id);
        Task<int> CleanupOldConversationsAsync(int keepCount = 100);
        Task<List<ConversationRecord>> SearchConversationsAsync(string keyword, string projectPath = null, int limit = 50);
    }
}
