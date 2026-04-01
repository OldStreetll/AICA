using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AICA.ToolWindows
{
    /// <summary>
    /// Represents a single message in the UI conversation history.
    /// Stores both content and rendering artifacts.
    /// </summary>
    internal class ConversationMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public string ToolLogsHtml { get; set; }
        public string CompletionData { get; set; }
        public List<IterationBlock> IterationBlocks { get; set; }
        public List<ToolCallBlock> ToolCallBlocks { get; set; }
        /// <summary>v2.6: Serialized content parts JSON for multimodal messages (null for plain text).</summary>
        public string PartsJson { get; set; }
    }

    /// <summary>
    /// Represents a single tool call HTML block with trailing text.
    /// </summary>
    internal class ToolCallBlock
    {
        public string ToolHtml { get; set; }
        public StringBuilder TextAfter { get; set; } = new StringBuilder();
        public int ToolId { get; set; }
        public string ToolCallId { get; set; }
    }

    /// <summary>
    /// Represents one iteration of the agent loop for structured rendering:
    /// [thinking + action] -> [tool call] -> [conclusion text]
    /// </summary>
    internal class IterationBlock
    {
        public string ThinkingContent { get; set; }
        public string ActionText { get; set; }
        public ToolCallBlock ToolBlock { get; set; }
        public StringBuilder ConclusionText { get; set; } = new StringBuilder();
        public int IterationId { get; set; }
    }

    /// <summary>
    /// ViewModel for conversation list in sidebar.
    /// </summary>
    internal class ConversationViewModel : INotifyPropertyChanged
    {
        private string _title;

        public string Id { get; set; }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayInfo)));
                }
            }
        }

        public string TimeAgo { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string ProjectName { get; set; }

        public string DisplayInfo
        {
            get
            {
                if (!string.IsNullOrEmpty(ProjectName))
                {
                    return $"{ProjectName} | {TimeAgo}";
                }
                return TimeAgo;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
