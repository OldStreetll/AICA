using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tests.Agent.Mocks
{
    /// <summary>
    /// Mock IUIContext that auto-approves confirmations and records interactions.
    /// </summary>
    public class MockUIContext : IUIContext
    {
        private readonly List<string> _messages = new List<string>();
        private readonly List<string> _streamingUpdates = new List<string>();
        private readonly List<string> _progressMessages = new List<string>();
        private readonly List<(string title, string message)> _confirmations = new List<(string, string)>();
        private readonly List<string> _followupQuestions = new List<string>();

        /// <summary>
        /// Whether ShowConfirmationAsync returns true (default) or false.
        /// </summary>
        public bool AutoApprove { get; set; } = true;

        /// <summary>
        /// All messages shown via ShowMessageAsync.
        /// </summary>
        public IReadOnlyList<string> Messages => _messages;

        /// <summary>
        /// All streaming content updates.
        /// </summary>
        public IReadOnlyList<string> StreamingUpdates => _streamingUpdates;

        /// <summary>
        /// All progress messages shown.
        /// </summary>
        public IReadOnlyList<string> ProgressMessages => _progressMessages;

        /// <summary>
        /// All confirmation requests received.
        /// </summary>
        public IReadOnlyList<(string title, string message)> Confirmations => _confirmations;

        /// <summary>
        /// All followup questions asked.
        /// </summary>
        public IReadOnlyList<string> FollowupQuestions => _followupQuestions;

        public Task ShowMessageAsync(string message, CancellationToken ct = default)
        {
            _messages.Add(message);
            return Task.CompletedTask;
        }

        public Task UpdateStreamingContentAsync(string content, CancellationToken ct = default)
        {
            _streamingUpdates.Add(content);
            return Task.CompletedTask;
        }

        public Task ShowProgressAsync(string message, int? percentComplete = null, CancellationToken ct = default)
        {
            _progressMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task HideProgressAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken ct = default)
        {
            _confirmations.Add((title, message));
            return Task.FromResult(AutoApprove);
        }

        public Task<DiffPreviewResult> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
        {
            return Task.FromResult(DiffPreviewResult.Approved(newContent));
        }

        public Task<FollowupQuestionResult> ShowFollowupQuestionAsync(
            string question,
            List<QuestionOption> options,
            bool allowCustomInput = false,
            CancellationToken ct = default)
        {
            _followupQuestions.Add(question);
            // Return first option by default
            var answer = options != null && options.Count > 0 ? options[0].Value : "yes";
            return Task.FromResult(new FollowupQuestionResult { Answer = answer });
        }
    }
}
