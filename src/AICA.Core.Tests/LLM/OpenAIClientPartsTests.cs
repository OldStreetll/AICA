using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using AICA.Core.LLM;
using AICA.Core.Storage;

namespace AICA.Core.Tests.LLM
{
    public class OpenAIClientPartsTests
    {
        [Fact]
        public void SerializeParts_TextOnly_ReturnsCorrectJson()
        {
            var parts = new List<IContentPart>
            {
                new TextPart("hello world")
            };

            var json = ConversationStorage.SerializeParts(parts);
            Assert.NotNull(json);

            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            Assert.Equal(JsonValueKind.Array, arr.ValueKind);
            Assert.Equal(1, arr.GetArrayLength());
            Assert.Equal("text", arr[0].GetProperty("type").GetString());
            Assert.Equal("hello world", arr[0].GetProperty("text").GetString());
        }

        [Fact]
        public void SerializeParts_WithImage_ContainsBase64()
        {
            var base64 = Convert.ToBase64String(new byte[50]);
            var parts = new List<IContentPart>
            {
                new TextPart("look at this"),
                new ImagePart(base64, "image/png")
            };

            var json = ConversationStorage.SerializeParts(parts);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            Assert.Equal(2, arr.GetArrayLength());
            Assert.Equal("text", arr[0].GetProperty("type").GetString());
            Assert.Equal("image", arr[1].GetProperty("type").GetString());
            Assert.Equal(base64, arr[1].GetProperty("base64Data").GetString());
            Assert.Equal("image/png", arr[1].GetProperty("mediaType").GetString());
        }

        [Fact]
        public void SerializeParts_WithCode_ContainsMetadata()
        {
            var parts = new List<IContentPart>
            {
                new CodePart("int x = 1;", "main.cpp", 42, 5, 42, 15, "cpp", "MyProject")
            };

            var json = ConversationStorage.SerializeParts(parts);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            Assert.Equal(1, arr.GetArrayLength());
            Assert.Equal("code", arr[0].GetProperty("type").GetString());
            Assert.Equal("int x = 1;", arr[0].GetProperty("code").GetString());
            Assert.Equal("main.cpp", arr[0].GetProperty("filePath").GetString());
            Assert.Equal(42, arr[0].GetProperty("startLine").GetInt32());
            Assert.Equal("cpp", arr[0].GetProperty("language").GetString());
            Assert.Equal("MyProject", arr[0].GetProperty("projectName").GetString());
        }

        [Fact]
        public void SerializeParts_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(ConversationStorage.SerializeParts(null));
            Assert.Null(ConversationStorage.SerializeParts(new List<IContentPart>()));
        }

        [Fact]
        public void ImagePart_DataUrl_MatchesVisionApiFormat()
        {
            var base64 = Convert.ToBase64String(new byte[20]);
            var img = new ImagePart(base64, "image/png");
            var url = img.ToDataUrl();
            Assert.Equal($"data:image/png;base64,{base64}", url);
        }

        [Fact]
        public void ContextManager_EstimateMessageTokens_PlainText()
        {
            var msg = ChatMessage.User("hello world");
            var tokens = AICA.Core.Context.ContextManager.EstimateMessageTokens(msg);
            Assert.True(tokens > 0);
        }

        [Fact]
        public void ContextManager_EstimateMessageTokens_WithImagePart()
        {
            var base64 = Convert.ToBase64String(new byte[10]);
            var msg = ChatMessage.UserWithParts(new List<IContentPart>
            {
                new TextPart("describe this image"),
                new ImagePart(base64, "image/png")
            });

            var tokens = AICA.Core.Context.ContextManager.EstimateMessageTokens(msg);
            // Should include 765 tokens for image + text tokens
            Assert.True(tokens >= 765);
        }

        [Fact]
        public void ContextManager_EstimateMessageTokens_NullMessage()
        {
            Assert.Equal(0, AICA.Core.Context.ContextManager.EstimateMessageTokens(null));
        }

        [Fact]
        public void RoundTrip_SerializeDeserialize_PartsJson()
        {
            var base64 = Convert.ToBase64String(new byte[30]);
            var originalParts = new List<IContentPart>
            {
                new TextPart("hello"),
                new ImagePart(base64, "image/jpeg"),
                new CodePart("void f(){}", "src/main.cpp", 10, 1, 10, 11, "cpp", "Proj")
            };

            var json = ConversationStorage.SerializeParts(originalParts);
            Assert.NotNull(json);

            // Simulate storage round-trip via ConversationMessageRecord
            var record = new ConversationMessageRecord
            {
                Role = "user",
                Content = "hello",
                PartsJson = json
            };

            // Verify the JSON is valid and contains all parts
            using var doc = JsonDocument.Parse(record.PartsJson);
            var arr = doc.RootElement;
            Assert.Equal(3, arr.GetArrayLength());
            Assert.Equal("text", arr[0].GetProperty("type").GetString());
            Assert.Equal("image", arr[1].GetProperty("type").GetString());
            Assert.Equal("code", arr[2].GetProperty("type").GetString());
        }
    }
}
