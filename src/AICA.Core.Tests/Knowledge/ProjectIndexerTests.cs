using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Knowledge;
using Xunit;

namespace AICA.Core.Tests.Knowledge
{
    public class ProjectIndexerTests
    {
        [Fact]
        public async Task IndexDirectoryAsync_WithTestFiles_ReturnsSymbols()
        {
            // Create a temp directory with test files
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a C++ header file
                File.WriteAllText(Path.Combine(tempDir, "Logger.h"), @"
namespace Poco {
class Logger : public Channel
{
public:
    void log(const std::string& msg);
    void debug(const std::string& msg);
    static Logger& get(const std::string& name);
};
}");

                // Create a C# file
                File.WriteAllText(Path.Combine(tempDir, "Agent.cs"), @"
namespace AICA.Core.Agent
{
    public class AgentExecutor
    {
        public void Execute() { }
    }
}");

                var indexer = new ProjectIndexer();
                var index = await indexer.IndexDirectoryAsync(tempDir);

                Assert.True(index.FileCount >= 2);
                Assert.True(index.Symbols.Count >= 2);
                Assert.Contains(index.Symbols, s => s.Name == "Logger");
                Assert.Contains(index.Symbols, s => s.Name == "AgentExecutor");
                Assert.True(index.IndexDuration.TotalSeconds < 5);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task IndexDirectoryAsync_SkipsBuildDirectories()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create files in directories that should be skipped
                var buildDir = Path.Combine(tempDir, "build");
                Directory.CreateDirectory(buildDir);
                File.WriteAllText(Path.Combine(buildDir, "Generated.h"), "class Generated {};");

                var gitDir = Path.Combine(tempDir, ".git");
                Directory.CreateDirectory(gitDir);
                File.WriteAllText(Path.Combine(gitDir, "config.h"), "class Config {};");

                // Create a file that should be indexed
                File.WriteAllText(Path.Combine(tempDir, "Real.h"), "class Real {};");

                var indexer = new ProjectIndexer();
                var index = await indexer.IndexDirectoryAsync(tempDir);

                Assert.Contains(index.Symbols, s => s.Name == "Real");
                Assert.DoesNotContain(index.Symbols, s => s.Name == "Generated");
                Assert.DoesNotContain(index.Symbols, s => s.Name == "Config");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task IndexDirectoryAsync_EmptyDirectory_ReturnsEmptyIndex()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var indexer = new ProjectIndexer();
                var index = await indexer.IndexDirectoryAsync(tempDir);

                Assert.Empty(index.Symbols);
                Assert.Equal(0, index.FileCount);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task IndexDirectoryAsync_NullPath_ThrowsArgumentNull()
        {
            var indexer = new ProjectIndexer();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => indexer.IndexDirectoryAsync(null));
        }

        [Fact]
        public async Task IndexDirectoryAsync_NonexistentPath_ThrowsDirectoryNotFound()
        {
            var indexer = new ProjectIndexer();
            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => indexer.IndexDirectoryAsync("/nonexistent/path/xyz"));
        }

        [Fact]
        public async Task IndexFileAsync_SingleFile_ReturnsSymbols()
        {
            var tempFile = Path.GetTempFileName();
            var hFile = Path.ChangeExtension(tempFile, ".h");
            File.Move(tempFile, hFile);

            try
            {
                File.WriteAllText(hFile, @"
class TestClass
{
public:
    void method();
};
enum TestEnum { A, B, C };");

                var indexer = new ProjectIndexer();
                var symbols = await indexer.IndexFileAsync(hFile);

                Assert.Contains(symbols, s => s.Name == "TestClass");
                Assert.Contains(symbols, s => s.Name == "TestEnum");
            }
            finally
            {
                File.Delete(hFile);
            }
        }

        [Fact]
        public async Task IndexFileAsync_NonexistentFile_ReturnsEmpty()
        {
            var indexer = new ProjectIndexer();
            var symbols = await indexer.IndexFileAsync("/nonexistent/file.h");

            Assert.Empty(symbols);
        }

        [Fact]
        public async Task IndexDirectoryAsync_BuildSubdir_FindsProjectRoot()
        {
            // Simulate: project root has .git and source files,
            // but the sln is in a "build" subdirectory
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create .git marker at project root
                Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

                // Create source file at project root
                Directory.CreateDirectory(Path.Combine(tempDir, "include"));
                File.WriteAllText(Path.Combine(tempDir, "include", "Logger.h"),
                    "namespace Poco {\nclass Logger\n{\n};\n}");

                // Create build subdirectory (where sln lives)
                var buildDir = Path.Combine(tempDir, "build");
                Directory.CreateDirectory(buildDir);

                // Index from the build directory — should walk up to project root
                var indexer = new ProjectIndexer();
                var index = await indexer.IndexDirectoryAsync(buildDir);

                Assert.Contains(index.Symbols, s => s.Name == "Logger");
                Assert.True(index.FileCount >= 1);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindProjectRoot_WithGitDir_ReturnsParent()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            var buildDir = Path.Combine(tempDir, "build");
            Directory.CreateDirectory(buildDir);
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

            try
            {
                var root = ProjectIndexer.FindProjectRoot(buildDir);
                Assert.Equal(tempDir, root);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindProjectRoot_NoMarkers_ReturnsSameDir()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var root = ProjectIndexer.FindProjectRoot(tempDir);
                Assert.Equal(tempDir, root);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task IndexDirectoryAsync_CancellationToken_Respects()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create some files
                for (int i = 0; i < 10; i++)
                    File.WriteAllText(Path.Combine(tempDir, $"File{i}.h"), $"class Class{i} {{}};");

                var cts = new CancellationTokenSource();
                cts.Cancel(); // Cancel immediately

                var indexer = new ProjectIndexer();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => indexer.IndexDirectoryAsync(tempDir, cts.Token));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
