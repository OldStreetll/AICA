using System;
using Xunit;
using AICA.Core.LLM;

namespace AICA.Core.Tests.LLM
{
    public class ContentPartsTests
    {
        [Fact]
        public void TextPart_StoresText()
        {
            var part = new TextPart("hello world");
            Assert.Equal(ContentPartType.Text, part.Type);
            Assert.Equal("hello world", part.Text);
        }

        [Fact]
        public void TextPart_NullTextBecomesEmpty()
        {
            var part = new TextPart(null);
            Assert.Equal(string.Empty, part.Text);
        }

        [Fact]
        public void ImagePart_ValidConstruction()
        {
            // Small base64 string (well under 2MB)
            var base64 = Convert.ToBase64String(new byte[100]);
            var part = new ImagePart(base64, "image/png");

            Assert.Equal(ContentPartType.Image, part.Type);
            Assert.Equal(base64, part.Base64Data);
            Assert.Equal("image/png", part.MediaType);
        }

        [Fact]
        public void ImagePart_ToDataUrl_FormatsCorrectly()
        {
            var base64 = Convert.ToBase64String(new byte[10]);
            var part = new ImagePart(base64, "image/jpeg");
            Assert.StartsWith("data:image/jpeg;base64,", part.ToDataUrl());
        }

        [Fact]
        public void ImagePart_ThrowsOnEmptyBase64()
        {
            Assert.Throws<ArgumentException>(() => new ImagePart("", "image/png"));
            Assert.Throws<ArgumentException>(() => new ImagePart(null, "image/png"));
        }

        [Fact]
        public void ImagePart_ThrowsOnEmptyMediaType()
        {
            var base64 = Convert.ToBase64String(new byte[10]);
            Assert.Throws<ArgumentException>(() => new ImagePart(base64, ""));
            Assert.Throws<ArgumentException>(() => new ImagePart(base64, null));
        }

        [Fact]
        public void ImagePart_ThrowsOnOversizedBase64()
        {
            // Create base64 string that exceeds 2MB when decoded
            var oversizedData = new string('A', (ImagePart.MaxBase64Bytes * 4 / 3) + 1000);
            Assert.Throws<ArgumentException>(() => new ImagePart(oversizedData, "image/png"));
        }

        [Fact]
        public void CodePart_StoresAllMetadata()
        {
            var part = new CodePart("int x = 1;", "C:/src/main.cpp", 42, 5, 42, 15, "cpp", "MyProject");

            Assert.Equal(ContentPartType.Code, part.Type);
            Assert.Equal("int x = 1;", part.Code);
            Assert.Equal("C:/src/main.cpp", part.FilePath);
            Assert.Equal(42, part.StartLine);
            Assert.Equal("cpp", part.Language);
            Assert.Equal("MyProject", part.ProjectName);
        }

        [Fact]
        public void CodePart_ToStructuredText_ContainsMetadata()
        {
            var part = new CodePart("int x = 1;", "main.cpp", 10, 1, 10, 11, "cpp", "TestProj");
            var text = part.ToStructuredText();

            Assert.Contains("main.cpp", text);
            Assert.Contains("line 10", text);
            Assert.Contains("TestProj", text);
            Assert.Contains("```cpp", text);
            Assert.Contains("int x = 1;", text);
        }

        [Fact]
        public void CodePart_ThrowsOnNullCode()
        {
            Assert.Throws<ArgumentNullException>(() => new CodePart(null, "file.cpp", 1, 1, 1, 1, "cpp"));
        }

        [Fact]
        public void ConcatTextAndCodeParts_CombinesCorrectly()
        {
            var parts = new System.Collections.Generic.List<IContentPart>
            {
                new TextPart("Look at this: "),
                new CodePart("int x;", "file.cpp", 1, 1, 1, 6, "cpp"),
                new TextPart(" What do you think?")
            };

            var result = ContentPartHelpers.ConcatTextAndCodeParts(parts);
            Assert.Contains("Look at this: ", result);
            Assert.Contains("file.cpp", result);
            Assert.Contains("int x;", result);
            Assert.Contains("What do you think?", result);
        }

        [Fact]
        public void ConcatTextAndCodeParts_SkipsImageParts()
        {
            var base64 = Convert.ToBase64String(new byte[10]);
            var parts = new System.Collections.Generic.List<IContentPart>
            {
                new TextPart("text before"),
                new ImagePart(base64, "image/png"),
                new TextPart("text after")
            };

            var result = ContentPartHelpers.ConcatTextAndCodeParts(parts);
            Assert.Contains("text before", result);
            Assert.Contains("text after", result);
            Assert.DoesNotContain("base64", result);
        }
    }
}
