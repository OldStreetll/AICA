using System.Collections.Generic;

namespace AICA.Core.LLM
{
    /// <summary>
    /// Chat message for LLM conversation
    /// </summary>
    public class ChatMessage
    {
        public ChatRole Role { get; set; }
        public string Content { get; set; }
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
