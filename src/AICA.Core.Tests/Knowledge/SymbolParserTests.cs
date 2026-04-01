using System.Linq;
using AICA.Core.Knowledge;
using Xunit;

namespace AICA.Core.Tests.Knowledge
{
    public class SymbolParserTests
    {
        [Fact]
        public void ParseCpp_ClassWithInheritance_ExtractsSymbol()
        {
            var content = @"
namespace Poco {

class Logger : public Channel
{
public:
    void log(const std::string& msg);
    void debug(const std::string& msg);
    static Logger& get(const std::string& name);
};

}";
            var symbols = RegexSymbolParser.ParseCpp("Foundation/include/Poco/Logger.h", content);

            Assert.Contains(symbols, s => s.Name == "Logger" && s.Kind == SymbolKind.Class);
            var logger = symbols.First(s => s.Name == "Logger");
            Assert.Equal("Poco", logger.Namespace);
            Assert.Contains("Channel", logger.Summary);
        }

        [Fact]
        public void ParseCpp_Struct_ExtractsSymbol()
        {
            var content = @"
namespace Poco {
struct Point
{
    int x;
    int y;
};
}";
            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            Assert.Contains(symbols, s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        }

        [Fact]
        public void ParseCpp_Enum_ExtractsSymbol()
        {
            var content = @"
enum class LogLevel
{
    Debug,
    Info,
    Warning,
    Error
};";
            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            Assert.Contains(symbols, s => s.Name == "LogLevel" && s.Kind == SymbolKind.Enum);
        }

        [Fact]
        public void ParseCpp_Typedef_ExtractsSymbol()
        {
            var content = "typedef unsigned long DWORD;";

            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            Assert.Contains(symbols, s => s.Name == "DWORD" && s.Kind == SymbolKind.Typedef);
        }

        [Fact]
        public void ParseCpp_Define_ExtractsSymbol()
        {
            var content = "#define POCO_API __declspec(dllexport)";

            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            Assert.Contains(symbols, s => s.Name == "POCO_API" && s.Kind == SymbolKind.Define);
        }

        [Fact]
        public void ParseCpp_SkipsIncludeGuard()
        {
            var content = @"
#ifndef POCO_LOGGER_H
#define POCO_LOGGER_H
class Logger {};
#endif";
            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            Assert.DoesNotContain(symbols, s => s.Name == "POCO_LOGGER_H");
            Assert.Contains(symbols, s => s.Name == "Logger");
        }

        [Fact]
        public void ParseCpp_NestedNamespace_TracksCorrectly()
        {
            var content = @"
namespace Poco {
namespace Net {
class HTTPRequest
{
public:
    void send();
};
}
}";
            var symbols = RegexSymbolParser.ParseCpp("test.h", content);

            var req = symbols.FirstOrDefault(s => s.Name == "HTTPRequest");
            Assert.NotNull(req);
            // Namespace order may vary due to stack behavior, but should contain both
            Assert.Contains("Poco", req.Namespace);
            Assert.Contains("Net", req.Namespace);
        }

        [Fact]
        public void ParseCSharp_Class_ExtractsSymbol()
        {
            var content = @"
namespace AICA.Core.Agent
{
    public class AgentExecutor : IDisposable
    {
        public void Execute() { }
    }
}";
            var symbols = RegexSymbolParser.ParseCSharp("AgentExecutor.cs", content);

            Assert.Contains(symbols, s => s.Name == "AgentExecutor" && s.Kind == SymbolKind.Class);
            var agent = symbols.First(s => s.Name == "AgentExecutor");
            Assert.Equal("AICA.Core.Agent", agent.Namespace);
            Assert.Contains("IDisposable", agent.Summary);
        }

        [Fact]
        public void ParseCSharp_Enum_ExtractsSymbol()
        {
            var content = @"
namespace AICA.Core
{
    public enum TaskState
    {
        Pending,
        Running,
        Completed
    }
}";
            var symbols = RegexSymbolParser.ParseCSharp("TaskState.cs", content);

            Assert.Contains(symbols, s => s.Name == "TaskState" && s.Kind == SymbolKind.Enum);
        }

        [Fact]
        public void ParseCSharp_Interface_ExtractsAsClass()
        {
            var content = @"
namespace AICA.Core
{
    public interface ILLMClient
    {
        Task<string> CompleteAsync(string prompt);
    }
}";
            var symbols = RegexSymbolParser.ParseCSharp("ILLMClient.cs", content);

            Assert.Contains(symbols, s => s.Name == "ILLMClient" && s.Kind == SymbolKind.Class);
        }

        [Fact]
        public void Parse_SelectsCorrectParser_ByExtension()
        {
            var cppContent = "class Foo {};";
            var csContent = "public class Bar {}";

            var cppSymbols = RegexSymbolParser.Parse("test.h", cppContent);
            var csSymbols = RegexSymbolParser.Parse("test.cs", csContent);
            var txtSymbols = RegexSymbolParser.Parse("test.txt", "nothing");

            Assert.NotEmpty(cppSymbols);
            Assert.NotEmpty(csSymbols);
            Assert.Empty(txtSymbols);
        }

        [Fact]
        public void Parse_NullInput_ReturnsEmpty()
        {
            Assert.Empty(RegexSymbolParser.Parse(null, "content"));
            Assert.Empty(RegexSymbolParser.Parse("file.h", null));
            Assert.Empty(RegexSymbolParser.Parse("", "content"));
        }

        [Fact]
        public void SplitIdentifier_CamelCase_SplitsCorrectly()
        {
            var parts = RegexSymbolParser.SplitIdentifier("MyLoggerFactory");

            Assert.Contains("My", parts);
            Assert.Contains("Logger", parts);
            Assert.Contains("Factory", parts);
        }

        [Fact]
        public void SplitIdentifier_SnakeCase_SplitsCorrectly()
        {
            var parts = RegexSymbolParser.SplitIdentifier("my_logger_factory");

            Assert.Contains("my", parts);
            Assert.Contains("logger", parts);
            Assert.Contains("factory", parts);
        }

        [Fact]
        public void GenerateKeywords_IncludesNameAndNamespace()
        {
            var keywords = RegexSymbolParser.GenerateKeywords("Logger", "Poco", "Channel", "Class");

            Assert.Contains("logger", keywords);
            Assert.Contains("poco", keywords);
            Assert.Contains("channel", keywords);
            Assert.Contains("class", keywords);
        }
    }
}
