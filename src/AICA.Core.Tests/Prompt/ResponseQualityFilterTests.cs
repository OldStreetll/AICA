using System.Collections.Generic;
using AICA.Core.LLM;
using AICA.Core.Prompt;
using Xunit;

namespace AICA.Core.Tests.Prompt
{
    public class ResponseQualityFilterTests
    {
        #region ExtractThinking Tests

        [Fact]
        public void ExtractThinking_WithThinkingTags_SeparatesContent()
        {
            var input = "<thinking>I need to analyze this code</thinking>Here is the result.";
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(input);

            Assert.Equal("I need to analyze this code", thinking);
            Assert.Equal("Here is the result.", userFacing);
        }

        [Fact]
        public void ExtractThinking_WithoutThinkingTags_ReturnsOriginal()
        {
            var input = "Here is the result with no thinking.";
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(input);

            Assert.Null(thinking);
            Assert.Equal(input, userFacing);
        }

        [Fact]
        public void ExtractThinking_WithMultipleBlocks_ExtractsAll()
        {
            var input = "<thinking>First thought</thinking>Some text<thinking>Second thought</thinking>More text";
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(input);

            Assert.Contains("First thought", thinking);
            Assert.Contains("Second thought", thinking);
            Assert.Equal("Some textMore text", userFacing);
        }

        [Fact]
        public void ExtractThinking_WithEmptyThinking_ReturnsCleanText()
        {
            var input = "<thinking></thinking>Clean output.";
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(input);

            Assert.Equal("Clean output.", userFacing);
        }

        [Fact]
        public void ExtractThinking_NullInput_ReturnsNulls()
        {
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(null);

            Assert.Null(thinking);
            Assert.Null(userFacing);
        }

        [Fact]
        public void ExtractThinking_MultilineThinking_ExtractsCorrectly()
        {
            var input = "<thinking>\nLine 1\nLine 2\n</thinking>\nUser sees this.";
            var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(input);

            Assert.Contains("Line 1", thinking);
            Assert.Contains("Line 2", thinking);
            Assert.Contains("User sees this.", userFacing);
        }

        #endregion

        #region StripForbiddenOpeners Tests

        [Theory]
        [InlineData("Great, I've updated the file.", "I've updated the file.")]
        [InlineData("Certainly, here is the code.", "here is the code.")]
        [InlineData("Okay, let me show you.", "let me show you.")]
        [InlineData("Sure, I can do that.", "I can do that.")]
        [InlineData("Of course, here it is.", "here it is.")]
        [InlineData("Absolutely, that's correct.", "that's correct.")]
        [InlineData("No problem, I'll fix it.", "I'll fix it.")]
        public void StripForbiddenOpeners_English_RemovesOpener(string input, string expected)
        {
            var result = ResponseQualityFilter.StripForbiddenOpeners(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("好的，我来读取文件。", "我来读取文件。")]
        [InlineData("当然，这是代码。", "这是代码。")]
        [InlineData("没问题，我来修改。", "我来修改。")]
        [InlineData("当然可以，我来处理。", "我来处理。")]
        public void StripForbiddenOpeners_Chinese_RemovesOpener(string input, string expected)
        {
            var result = ResponseQualityFilter.StripForbiddenOpeners(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void StripForbiddenOpeners_MidSentence_DoesNotRemove()
        {
            var input = "The result is great, no issues found.";
            var result = ResponseQualityFilter.StripForbiddenOpeners(input);
            Assert.Equal(input, result);
        }

        [Fact]
        public void StripForbiddenOpeners_NullOrEmpty_ReturnsAsIs()
        {
            Assert.Null(ResponseQualityFilter.StripForbiddenOpeners(null));
            Assert.Equal("", ResponseQualityFilter.StripForbiddenOpeners(""));
        }

        [Fact]
        public void StripForbiddenOpeners_TechnicalContent_Preserved()
        {
            var input = "I've updated the CSS to fix the layout.";
            var result = ResponseQualityFilter.StripForbiddenOpeners(input);
            Assert.Equal(input, result);
        }

        #endregion

        #region StripTrailingOffers Tests

        [Theory]
        [InlineData("Done. Do you want me to do anything else?", "Done.")]
        [InlineData("File updated. Would you like me to continue?", "File updated.")]
        [InlineData("Complete. Need help with anything else?", "Complete.")]
        [InlineData("Fixed. Let me know if you need anything else.", "Fixed.")]
        public void StripTrailingOffers_English_RemovesTrailing(string input, string expected)
        {
            var result = ResponseQualityFilter.StripTrailingOffers(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("完成了。还需要我做其他的吗？", "完成了。")]
        [InlineData("已修改。要我继续吗？", "已修改。")]
        [InlineData("搞定了。需要其他帮助吗？", "搞定了。")]
        [InlineData("好了。如果你需要其他帮助，请告诉我。", "好了。")]
        public void StripTrailingOffers_Chinese_RemovesTrailing(string input, string expected)
        {
            var result = ResponseQualityFilter.StripTrailingOffers(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void StripTrailingOffers_MidText_DoesNotRemove()
        {
            var input = "Do you want me to explain? Here is the code.";
            var result = ResponseQualityFilter.StripTrailingOffers(input);
            Assert.Equal(input, result);
        }

        [Fact]
        public void StripTrailingOffers_NullOrEmpty_ReturnsAsIs()
        {
            Assert.Null(ResponseQualityFilter.StripTrailingOffers(null));
            Assert.Equal("", ResponseQualityFilter.StripTrailingOffers(""));
        }

        #endregion

        #region IsInternalReasoning Tests

        [Theory]
        [InlineData("I need to check the file first.")]
        [InlineData("I should search the directory.")]
        [InlineData("Let me check this code.")]
        [InlineData("I'm going to use the read_file tool.")]
        [InlineData("I will call read_file now.")]
        [InlineData("First, I need to check the project structure.")]
        public void IsInternalReasoning_EnglishPatterns_ReturnsTrue(string text)
        {
            Assert.True(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Theory]
        [InlineData("我需要读取这个文件。")]
        [InlineData("我应该查看目录结构。")]
        [InlineData("让我搜索这段代码。")]
        [InlineData("我将调用 read_file。")]
        [InlineData("首先，我需要查看项目结构。")]
        [InlineData("接下来，我需要搜索函数。")]
        public void IsInternalReasoning_ChinesePatterns_ReturnsTrue(string text)
        {
            Assert.True(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Theory]
        [InlineData("The user is asking me to find the API.")]
        [InlineData("Looking at the instructions, I see...")]
        [InlineData("The user wants me to analyze this.")]
        public void IsInternalReasoning_MetaPatterns_ReturnsTrue(string text)
        {
            Assert.True(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Theory]
        [InlineData("Let me explain the Poco::Logger class. It provides...")]
        [InlineData("让我为你介绍这个类的功能。Poco::Logger 是...")]
        [InlineData("首先，Poco::Logger 继承自 Channel 接口...")]
        [InlineData("Actually, the Logger class uses the strategy pattern...")]
        [InlineData("The user is asking about the API.")]
        public void IsInternalReasoning_ValidAnswers_ReturnsFalse(string text)
        {
            Assert.False(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Fact]
        public void IsInternalReasoning_LongText_StartingWithPattern_ReturnsTrue()
        {
            // BF-02 fix: Long text starting with reasoning pattern IS internal reasoning
            // (MiniMax mixes reasoning + answer in one block)
            var longAnswer = "I need to check " + new string('x', 350);
            Assert.True(ResponseQualityFilter.IsInternalReasoning(longAnswer));
        }

        [Fact]
        public void IsInternalReasoning_LongText_NotStartingWithPattern_ReturnsFalse()
        {
            // Long text NOT starting with reasoning pattern is valid content
            var longAnswer = "The Logger class provides " + new string('x', 350);
            Assert.False(ResponseQualityFilter.IsInternalReasoning(longAnswer));
        }

        [Fact]
        public void IsInternalReasoning_UserFacingContent_ReturnsFalse()
        {
            Assert.False(ResponseQualityFilter.IsInternalReasoning(
                "The function `calculateSum` takes two parameters and returns their sum."));
        }

        [Fact]
        public void IsInternalReasoning_CodeBlock_ReturnsFalse()
        {
            var code = "```csharp\n// I need to initialize the variable\nvar x = 10;\n```";
            Assert.False(ResponseQualityFilter.IsInternalReasoning(code));
        }

        [Fact]
        public void IsInternalReasoning_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(ResponseQualityFilter.IsInternalReasoning(null));
            Assert.False(ResponseQualityFilter.IsInternalReasoning(""));
        }

        #endregion

        #region BF-02: Paragraph-level reasoning detection and stripping

        [Fact]
        public void BF02_IsInternalReasoning_MiddleParagraphReasoning_ReturnsTrue()
        {
            // MiniMax outputs normal text first, then starts reasoning in a later paragraph
            var text = "这个函数的功能是处理数据。\n\n我需要查看具体的实现代码。\n\n下面是结果。";
            Assert.True(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Fact]
        public void BF02_IsInternalReasoning_AllNormalParagraphs_ReturnsFalse()
        {
            var text = "这个函数处理数据转换。\n\n它接受两个参数并返回结果。\n\n代码质量良好。";
            Assert.False(ResponseQualityFilter.IsInternalReasoning(text));
        }

        [Fact]
        public void BF02_StripReasoningParagraphs_RemovesMiddleReasoning()
        {
            var text = "函数功能说明如下：\n\n让我查看这段代码。\n\n该函数返回整数类型。";
            var result = ResponseQualityFilter.StripReasoningParagraphs(text);
            Assert.Contains("函数功能说明如下", result);
            Assert.DoesNotContain("让我查看", result);
            Assert.Contains("该函数返回整数类型", result);
        }

        [Fact]
        public void BF02_StripReasoningParagraphs_RemovesLeadingReasoning()
        {
            // "我需要搜索" matches "我需要搜索"; "首先，我需要查看" matches "首先，我需要"
            var text = "我需要搜索这个函数。\n\n首先，我需要查看项目结构。\n\n这是一个工具函数，用于数据转换。";
            var result = ResponseQualityFilter.StripReasoningParagraphs(text);
            Assert.DoesNotContain("我需要搜索", result);
            Assert.DoesNotContain("我需要查看", result);
            Assert.Contains("这是一个工具函数", result);
        }

        [Fact]
        public void BF02_StripReasoningParagraphs_NormalContent_Preserved()
        {
            var text = "让我们看看这个函数的实现。\n\n它使用了观察者模式。\n\n性能表现很好。";
            var result = ResponseQualityFilter.StripReasoningParagraphs(text);
            Assert.Contains("让我们看看这个函数", result);
            Assert.Contains("观察者模式", result);
        }

        [Fact]
        public void BF02_StripReasoningParagraphs_AllReasoning_ReturnsFallback()
        {
            // If all paragraphs are reasoning, return the last paragraph as fallback
            var text = "我需要查看文件。\n\n让我搜索这段代码。";
            var result = ResponseQualityFilter.StripReasoningParagraphs(text);
            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        [Fact]
        public void BF02_NormalContentWithLetMe_NotFalsePositive()
        {
            // "让我们" (let us) is valid user-facing content, should not be filtered
            var text = "让我们来分析这段代码的性能特征。";
            Assert.False(ResponseQualityFilter.IsParagraphReasoning(text));
        }

        [Fact]
        public void BF02_AC1_ThinkingAndReasoningFiltered()
        {
            // Exact AC1 scenario from work plan
            var text = "好的，让我思考一下这个问题。\n\n首先我需要分析这段代码的结构。\n\n这个函数的作用是数据转换。";
            var result = ResponseQualityFilter.StripReasoningParagraphs(text);
            Assert.DoesNotContain("让我思考", result);
            Assert.DoesNotContain("首先我需要", result);
            Assert.Contains("这个函数的作用", result);
        }

        [Fact]
        public void BF02_LetMeThink_DetectedAsReasoning()
        {
            // "让我思考" should be detected as meta-reasoning
            Assert.True(ResponseQualityFilter.IsParagraphReasoning("让我思考一下这个问题的解决方案。"));
        }

        [Fact]
        public void BF02_FirstINeedTo_NoComma_DetectedAsReasoning()
        {
            // "首先我需要" (no comma) should match as reasoning start
            Assert.True(ResponseQualityFilter.IsParagraphReasoning("首先我需要查看项目的目录结构。"));
        }

        #endregion

        #region MicroCompactToolResults Tests

        [Fact]
        public void MicroCompactToolResults_FewMessages_NoChange()
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System("system prompt"),
                ChatMessage.User("hello"),
                ChatMessage.Assistant("hi"),
                ChatMessage.ToolResult("tc1", "file content here")
            };

            var result = ResponseQualityFilter.MicroCompactToolResults(messages, keepRecent: 3);

            Assert.Equal(4, result.Count);
            Assert.Equal("file content here", result[3].Content);
        }

        [Fact]
        public void MicroCompactToolResults_ManyToolResults_CompactsOld()
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System("system prompt"),
                ChatMessage.User("do something"),
                ChatMessage.ToolResult("tc1", "old result 1 with lots of content"),
                ChatMessage.ToolResult("tc2", "old result 2 with lots of content"),
                ChatMessage.ToolResult("tc3", "old result 3 with lots of content"),
                ChatMessage.ToolResult("tc4", "recent result 4"),
                ChatMessage.ToolResult("tc5", "recent result 5"),
            };

            var result = ResponseQualityFilter.MicroCompactToolResults(messages, keepRecent: 2);

            // Old results (tc1, tc2, tc3) should be compacted
            Assert.Contains("[Previous tool result", result[2].Content);
            Assert.Contains("[Previous tool result", result[3].Content);
            Assert.Contains("[Previous tool result", result[4].Content);
            // Recent results (tc4, tc5) should be preserved
            Assert.Equal("recent result 4", result[5].Content);
            Assert.Equal("recent result 5", result[6].Content);
        }

        [Fact]
        public void MicroCompactToolResults_PreservesNonToolMessages()
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System("system prompt"),
                ChatMessage.User("request"),
                ChatMessage.Assistant("response"),
                ChatMessage.ToolResult("tc1", "old tool result"),
                ChatMessage.User("another request"),
                ChatMessage.ToolResult("tc2", "recent tool result"),
            };

            var result = ResponseQualityFilter.MicroCompactToolResults(messages, keepRecent: 1);

            Assert.Equal("system prompt", result[0].Content);
            Assert.Equal("request", result[1].Content);
            Assert.Equal("response", result[2].Content);
            Assert.Contains("[Previous tool result", result[3].Content);
            Assert.Equal("another request", result[4].Content);
            Assert.Equal("recent tool result", result[5].Content);
        }

        [Fact]
        public void MicroCompactToolResults_ShortResults_NotCompacted()
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System("sys"),
                ChatMessage.ToolResult("tc1", "short"),
                ChatMessage.ToolResult("tc2", "also short"),
                ChatMessage.ToolResult("tc3", "recent"),
            };

            // Short results (< 200 chars) should not be compacted even if old
            var result = ResponseQualityFilter.MicroCompactToolResults(messages, keepRecent: 1);

            Assert.Equal("short", result[1].Content);
            Assert.Equal("also short", result[2].Content);
            Assert.Equal("recent", result[3].Content);
        }

        [Fact]
        public void MicroCompactToolResults_NullMessages_ReturnsEmpty()
        {
            var result = ResponseQualityFilter.MicroCompactToolResults(null, keepRecent: 3);
            Assert.Empty(result);
        }

        #endregion
    }
}
