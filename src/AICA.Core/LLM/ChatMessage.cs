using System.Collections.Generic;
using System.Linq;

namespace AICA.Core.LLM
{
    /// <summary>
    /// Chat message for LLM conversation.
    /// Supports dual-channel: plain text (_content) or structured parts (_parts).
    /// Content getter is backward-compatible — returns concatenated text from parts when parts are active.
    /// </summary>
    public class ChatMessage
    {
        private string _content;
        private List<IContentPart> _parts;

        public ChatRole Role { get; set; }

        public string Content
        {
            get => (_parts != null && _parts.Count > 0)
                ? ContentPartHelpers.ConcatTextAndCodeParts(_parts)
                : _content;
            set { _content = value; _parts = null; }
        }

        public List<IContentPart> Parts
        {
            get => _parts;
            set { _parts = value; _content = null; }
        }

        public bool HasMultimodalParts =>
            _parts != null && _parts.Any(p => p.Type == ContentPartType.Image);

        public string Name { get; set; }
        public string ToolCallId { get; set; }
        public List<ToolCallMessage> ToolCalls { get; set; }

        public static ChatMessage System(string content) => new ChatMessage { Role = ChatRole.System, Content = content };
        public static ChatMessage User(string content) => new ChatMessage { Role = ChatRole.User, Content = content };
        public static ChatMessage Assistant(string content) => new ChatMessage { Role = ChatRole.Assistant, Content = content };
        public static ChatMessage ToolResult(string toolCallId, string content) => new ChatMessage
        {
            Role = ChatRole.Tool,
            Content = content,
            ToolCallId = toolCallId
        };

        public static ChatMessage UserWithParts(List<IContentPart> parts) => new ChatMessage
        {
            Role = ChatRole.User,
            Parts = parts
        };
    }

    public enum ChatRole
    {
        System,
        User,
        Assistant,
        Tool
    }

    /// <summary>
    /// Tool call in assistant message
    /// </summary>
    public class ToolCallMessage
    {
        public string Id { get; set; }
        public string Type { get; set; } = "function";
        public FunctionCall Function { get; set; }
    }

    public class FunctionCall
    {
        public string Name { get; set; }
        public string Arguments { get; set; }
    }
}
