using System;
using System.Collections.Generic;
using Xunit;
using AICA.Core.LLM;

namespace AICA.Core.Tests.LLM
{
    public class ChatMessagePartsTests
    {
        [Fact]
        public void Content_Getter_ReturnsPlainText_WhenNoParts()
        {
            var msg = ChatMessage.User("hello");
            Assert.Equal("hello", msg.Content);
            Assert.Null(msg.Parts);
            Assert.False(msg.HasMultimodalParts);
        }

        [Fact]
        public void Content_Setter_ClearsParts()
        {
            var msg = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("text"),
                new ImagePart(Convert.ToBase64String(new byte[10]), "image/png")
            });

            Assert.True(msg.HasMultimodalParts);

            msg.Content = "plain text";
            Assert.Null(msg.Parts);
            Assert.Equal("plain text", msg.Content);
            Assert.False(msg.HasMultimodalParts);
        }

        [Fact]
        public void Parts_Setter_ClearsContent()
        {
            var msg = ChatMessage.User("hello");
            msg.Parts = new List<IContentPart> { new TextPart("from parts") };

            Assert.Equal("from parts", msg.Content);
        }

        [Fact]
        public void Content_Getter_ConcatenatesTextAndCodeParts()
        {
            var msg = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("Review this: "),
                new CodePart("int x;", "file.cpp", 1, 1, 1, 6, "cpp")
            });

            var content = msg.Content;
            Assert.Contains("Review this: ", content);
            Assert.Contains("file.cpp", content);
            Assert.Contains("int x;", content);
        }

        [Fact]
        public void HasMultimodalParts_TrueOnlyWithImagePart()
        {
            // Text + Code only → not multimodal
            var msg1 = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("text"),
                new CodePart("code", "f.cpp", 1, 1, 1, 5, "cpp")
            });
            Assert.False(msg1.HasMultimodalParts);

            // With ImagePart → multimodal
            var msg2 = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("look at this"),
                new ImagePart(Convert.ToBase64String(new byte[10]), "image/png")
            });
            Assert.True(msg2.HasMultimodalParts);
        }

        [Fact]
        public void UserWithParts_SetsRoleToUser()
        {
            var msg = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("hello")
            });
            Assert.Equal(ChatRole.User, msg.Role);
            Assert.NotNull(msg.Parts);
        }

        [Fact]
        public void BackwardCompatibility_ExistingFactoryMethods_StillWork()
        {
            var sys = ChatMessage.System("sys");
            Assert.Equal(ChatRole.System, sys.Role);
            Assert.Equal("sys", sys.Content);

            var user = ChatMessage.User("usr");
            Assert.Equal(ChatRole.User, user.Role);
            Assert.Equal("usr", user.Content);

            var asst = ChatMessage.Assistant("asst");
            Assert.Equal(ChatRole.Assistant, asst.Role);
            Assert.Equal("asst", asst.Content);

            var tool = ChatMessage.ToolResult("id1", "result");
            Assert.Equal(ChatRole.Tool, tool.Role);
            Assert.Equal("result", tool.Content);
            Assert.Equal("id1", tool.ToolCallId);
        }

        [Fact]
        public void EmptyParts_FallsBackToContent()
        {
            var msg = new ChatMessage { Role = ChatRole.User };
            msg.Parts = new List<IContentPart>();
            // Empty parts list should fall back to _content (which is null)
            Assert.Null(msg.Content);
        }
    }
}
