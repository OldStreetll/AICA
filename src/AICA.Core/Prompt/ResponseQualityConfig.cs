using System.Collections.Generic;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Configuration for ResponseQualityFilter patterns.
    /// Can be loaded from JSON to support per-model customization.
    /// </summary>
    public class ResponseQualityConfig
    {
        /// <summary>
        /// Phrases that should be stripped from the start of responses.
        /// Each entry specifies the phrase and whether matching is case-sensitive.
        /// </summary>
        public List<ForbiddenOpenerEntry> ForbiddenOpeners { get; set; } = new List<ForbiddenOpenerEntry>();

        /// <summary>
        /// Regex patterns for trailing offers to strip (e.g., "Need anything else?").
        /// </summary>
        public List<string> TrailingOfferPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Phrases that indicate internal reasoning (tool planning narration).
        /// Matched case-insensitively at the start of text.
        /// </summary>
        public List<string> ReasoningStartPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Phrases that indicate meta-reasoning about the user's intent.
        /// Matched case-insensitively anywhere in text.
        /// </summary>
        public List<string> MetaReasoningPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Minimum text length before suppression is considered.
        /// Text longer than this threshold is never suppressed.
        /// </summary>
        public int SuppressionLengthThreshold { get; set; } = 300;

        /// <summary>
        /// Load the default configuration matching the current hardcoded patterns.
        /// </summary>
        public static ResponseQualityConfig LoadDefault()
        {
            var config = new ResponseQualityConfig();

            // Forbidden openers
            config.ForbiddenOpeners.AddRange(new[]
            {
                new ForbiddenOpenerEntry("Great, ", false),
                new ForbiddenOpenerEntry("Great! ", false),
                new ForbiddenOpenerEntry("Certainly, ", false),
                new ForbiddenOpenerEntry("Certainly! ", false),
                new ForbiddenOpenerEntry("Okay, ", false),
                new ForbiddenOpenerEntry("Sure, ", false),
                new ForbiddenOpenerEntry("Sure! ", false),
                new ForbiddenOpenerEntry("Of course, ", false),
                new ForbiddenOpenerEntry("Of course! ", false),
                new ForbiddenOpenerEntry("Absolutely, ", false),
                new ForbiddenOpenerEntry("Absolutely! ", false),
                new ForbiddenOpenerEntry("No problem, ", false),
                new ForbiddenOpenerEntry("No problem! ", false),
                new ForbiddenOpenerEntry("Happy to help, ", false),
                new ForbiddenOpenerEntry("Happy to help! ", false),
                new ForbiddenOpenerEntry("I'd be happy to ", false),
                // Chinese
                new ForbiddenOpenerEntry("好的，", true),
                new ForbiddenOpenerEntry("好的！", true),
                new ForbiddenOpenerEntry("当然，", true),
                new ForbiddenOpenerEntry("当然！", true),
                new ForbiddenOpenerEntry("没问题，", true),
                new ForbiddenOpenerEntry("没问题！", true),
                new ForbiddenOpenerEntry("当然可以，", true),
                new ForbiddenOpenerEntry("当然可以！", true),
                new ForbiddenOpenerEntry("很高兴", true),
            });

            // Trailing offer patterns (regex strings)
            config.TrailingOfferPatterns.AddRange(new[]
            {
                @"(?<=[.!?。！？])\s*Let me know if you need\b.*$",
                @"(?<=[.!?。！？])\s*Do you want me to\b.*[?？]?\s*$",
                @"(?<=[.!?。！？])\s*Would you like me to\b.*[?？]?\s*$",
                @"(?<=[.!?。！？])\s*Need help with anything else\b.*[?？]?\s*$",
                @"(?<=[.!?。！？])\s*Need anything else\b.*[?？]?\s*$",
                @"(?<=[.!?。！？])\s*Is there anything else\b.*[?？]?\s*$",
                @"(?<=[.!?。！？])\s*Would you like\b.*[?？]?\s*$",
                // Chinese
                @"(?<=[。！？])\s*如果你需要.*[。.]?\s*$",
                @"(?<=[。！？])\s*如果需要.*请告诉我.*[。.]?\s*$",
                @"(?<=[。！？])\s*还需要我.*[?？]?\s*$",
                @"(?<=[。！？])\s*要我继续.*[?？]?\s*$",
                @"(?<=[。！？])\s*需要其他帮助.*[?？]?\s*$",
                @"\s*请问.*帮助.*[?？]?\s*$",
                @"\s*请问.*需要.*[?？]?\s*$",
                @"\s*请问有什么.*[?？]?\s*$",
            });

            // Reasoning start patterns
            config.ReasoningStartPatterns.AddRange(new[]
            {
                "i need to check", "i need to search", "i need to read", "i need to look",
                "i should check", "i should search", "i should read", "i should look",
                "i will call", "i will use the", "i'm going to use",
                "let me check", "let me search", "let me look at",
                "let me read the", "let me find", "let me analyze the",
                "i'll use the", "i'll check", "i'll search",
                "first, i need to", "first, i should", "first, let me check",
                "next, i need to", "next, i should",
                // Chinese
                "我需要查看", "我需要搜索", "我需要读取", "我需要检查",
                "我需要使用", "我需要调用",
                "我应该查看", "我应该搜索",
                "我将调用", "我将使用工具",
                "让我查看", "让我搜索", "让我检查", "让我读取",
                "首先，我需要", "首先，让我查",
                "接下来，我需要", "接下来，让我",
                "用户要求", "用户只是", "用户用", "用户在",
                "用户想要", "用户希望",
            });

            // Meta-reasoning patterns
            config.MetaReasoningPatterns.AddRange(new[]
            {
                "the user is asking me to", "the user wants me to", "the user is requesting",
                "the user might want", "the user may want",
                "looking at the instructions",
                "i think the user wants me to", "let me re-read the",
                // Chinese
                "用户想要我", "用户在请求我", "用户可能想让我",
                "看起来用户想",
                "let me re-read", "let me reconsider", "let me think about",
                "according to the rules", "based on the instructions",
                "根据规则", "让我重新阅读", "让我重新考虑",
                "the user asked me to read", "the user asked me to search",
                "the user asked me to analyze", "the user asked me to check",
                "the user is asking me to",
                "我不需要调用", "不需要执行", "不需要调用任何工具",
                "由于这是一个简单", "这是一个简单的问候",
                "根据自定义指令", "根据我的指导",
                "i should respond", "i don't need to call", "i should not call",
                "since this is a simple", "no tools are needed",
            });

            return config;
        }

        /// <summary>
        /// Load configuration from a JSON string.
        /// </summary>
        public static ResponseQualityConfig LoadFromJson(string json)
        {
            return System.Text.Json.JsonSerializer.Deserialize<ResponseQualityConfig>(json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        /// <summary>
        /// Serialize to JSON string.
        /// </summary>
        public string ToJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
        }
    }

    /// <summary>
    /// Entry for a forbidden opener phrase.
    /// </summary>
    public class ForbiddenOpenerEntry
    {
        public string Phrase { get; set; }
        public bool IsCaseSensitive { get; set; }

        public ForbiddenOpenerEntry() { }

        public ForbiddenOpenerEntry(string phrase, bool isCaseSensitive)
        {
            Phrase = phrase;
            IsCaseSensitive = isCaseSensitive;
        }
    }
}
