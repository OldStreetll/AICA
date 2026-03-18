using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.Knowledge;
using Xunit;

namespace AICA.Core.Tests.Knowledge
{
    public class KnowledgeContextProviderTests
    {
        private static ProjectIndex CreateTestIndex()
        {
            var symbols = new List<SymbolRecord>
            {
                new SymbolRecord(
                    "Logger.h:Logger", "Logger", SymbolKind.Class,
                    "Foundation/include/Poco/Logger.h", "Poco",
                    "class Logger : Channel (28 methods)",
                    new List<string> { "logger", "poco", "channel", "log", "class" }),

                new SymbolRecord(
                    "Channel.h:Channel", "Channel", SymbolKind.Class,
                    "Foundation/include/Poco/Channel.h", "Poco",
                    "class Channel (5 methods)",
                    new List<string> { "channel", "poco", "class", "base" }),

                new SymbolRecord(
                    "HTTPRequest.h:HTTPRequest", "HTTPRequest", SymbolKind.Class,
                    "Net/include/Poco/Net/HTTPRequest.h", "Poco::Net",
                    "class HTTPRequest : HTTPMessage (12 methods)",
                    new List<string> { "httprequest", "http", "request", "poco", "net", "class", "message" }),

                new SymbolRecord(
                    "LogLevel.h:LogLevel", "LogLevel", SymbolKind.Enum,
                    "Foundation/include/Poco/LogLevel.h", "Poco",
                    "enum LogLevel",
                    new List<string> { "loglevel", "log", "level", "poco", "enum" }),

                new SymbolRecord(
                    "Socket.h:Socket", "Socket", SymbolKind.Class,
                    "Net/include/Poco/Net/Socket.h", "Poco::Net",
                    "class Socket (8 methods)",
                    new List<string> { "socket", "poco", "net", "class", "network" }),
            };

            return new ProjectIndex(symbols, DateTime.UtcNow, 100, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void RetrieveContext_QueryLogger_ReturnsLoggerFirst()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            var context = provider.RetrieveContext("Logger");

            Assert.Contains("Logger", context);
            Assert.Contains("Project Knowledge", context);
            // Logger should appear before HTTPRequest
            var loggerPos = context.IndexOf("Logger");
            var httpPos = context.IndexOf("HTTPRequest");
            if (httpPos >= 0)
                Assert.True(loggerPos < httpPos, "Logger should rank higher than HTTPRequest for query 'Logger'");
        }

        [Fact]
        public void RetrieveContext_QueryHTTP_ReturnsHTTPRequest()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            var context = provider.RetrieveContext("HTTP request");

            Assert.Contains("HTTPRequest", context);
        }

        [Fact]
        public void RetrieveContext_EmptyQuery_ReturnsEmpty()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            Assert.Equal("", provider.RetrieveContext(""));
            Assert.Equal("", provider.RetrieveContext(null));
            Assert.Equal("", provider.RetrieveContext("   "));
        }

        [Fact]
        public void RetrieveContext_NoMatch_ReturnsEmpty()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            var context = provider.RetrieveContext("xyzzy_nonexistent_symbol");

            Assert.Equal("", context);
        }

        [Fact]
        public void RetrieveContext_RespectsTokenBudget()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            // Very small token budget
            var context = provider.RetrieveContext("Logger", maxTokens: 50);

            // Should be truncated but not empty — header includes guidance text (~400 chars)
            Assert.True(context.Length <= 50 * 4 + 500);
        }

        [Fact]
        public void GetIndexSummary_ReturnsStats()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            var summary = provider.GetIndexSummary();

            Assert.Contains("100 files", summary);
            Assert.Contains("5 symbols", summary);
            Assert.Contains("classs", summary); // "4 classs" - plural
        }

        [Fact]
        public void GetIndexSummary_EmptyIndex_ReturnsNoSymbols()
        {
            var emptyIndex = new ProjectIndex(
                new List<SymbolRecord>(), DateTime.UtcNow, 0, TimeSpan.Zero);
            var provider = new KnowledgeContextProvider(emptyIndex);

            Assert.Equal("No symbols indexed.", provider.GetIndexSummary());
        }

        [Fact]
        public void Tokenize_SplitsCamelCase()
        {
            var tokens = KnowledgeContextProvider.Tokenize("HTTPRequest");

            Assert.Contains("httprequest", tokens);
            Assert.Contains("http", tokens);
        }

        [Fact]
        public void Tokenize_RemovesStopWords()
        {
            var tokens = KnowledgeContextProvider.Tokenize("what is the Logger");

            Assert.Contains("logger", tokens);
            Assert.DoesNotContain("what", tokens);
            Assert.DoesNotContain("is", tokens);
            Assert.DoesNotContain("the", tokens);
        }

        [Fact]
        public void Tokenize_HandlesMultipleWords()
        {
            var tokens = KnowledgeContextProvider.Tokenize("Logger Channel Socket");

            Assert.Contains("logger", tokens);
            Assert.Contains("channel", tokens);
            Assert.Contains("socket", tokens);
        }

        [Fact]
        public void RetrieveContext_QueryLog_ReturnsLogRelated()
        {
            var provider = new KnowledgeContextProvider(CreateTestIndex());

            var context = provider.RetrieveContext("log");

            // Should match Logger and LogLevel (both have "log" keyword)
            Assert.Contains("Logger", context);
            Assert.Contains("LogLevel", context);
        }

        [Fact]
        public void Constructor_NullIndex_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new KnowledgeContextProvider(null));
        }
    }
}
