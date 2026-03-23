using System;
using System.IO;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ProjectLanguageDetectorTests : IDisposable
    {
        private readonly string _tempDir;

        public ProjectLanguageDetectorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "aica_langdetect_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            ProjectLanguageDetector.ClearCache();
        }

        public void Dispose()
        {
            ProjectLanguageDetector.ClearCache();
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public void CppProject_Detected()
        {
            CreateFiles("main.cpp", "utils.h", "algo.c", "helper.cpp", "readme.txt");
            Assert.Equal(ProjectLanguage.CppC, ProjectLanguageDetector.DetectLanguage(_tempDir));
        }

        [Fact]
        public void CSharpProject_Detected()
        {
            CreateFiles("Program.cs", "Startup.cs", "Model.cs", "Service.cs", "readme.md");
            Assert.Equal(ProjectLanguage.CSharp, ProjectLanguageDetector.DetectLanguage(_tempDir));
        }

        [Fact]
        public void EmptyDir_ReturnsUnknown()
        {
            Assert.Equal(ProjectLanguage.Unknown, ProjectLanguageDetector.DetectLanguage(_tempDir));
        }

        [Fact]
        public void NullDir_ReturnsUnknown()
        {
            Assert.Equal(ProjectLanguage.Unknown, ProjectLanguageDetector.DetectLanguage(null));
        }

        [Fact]
        public void NonexistentDir_ReturnsUnknown()
        {
            Assert.Equal(ProjectLanguage.Unknown, ProjectLanguageDetector.DetectLanguage("/nonexistent/dir"));
        }

        [Theory]
        [InlineData(".cpp", ProjectLanguage.CppC)]
        [InlineData(".h", ProjectLanguage.CppC)]
        [InlineData(".c", ProjectLanguage.CppC)]
        [InlineData(".cc", ProjectLanguage.CppC)]
        [InlineData(".cs", ProjectLanguage.CSharp)]
        [InlineData(".py", ProjectLanguage.Python)]
        [InlineData(".ts", ProjectLanguage.TypeScript)]
        [InlineData(".txt", ProjectLanguage.Unknown)]
        [InlineData("", ProjectLanguage.Unknown)]
        public void DetectFromFile_Extensions(string ext, ProjectLanguage expected)
        {
            var path = string.IsNullOrEmpty(ext) ? "" : $"test{ext}";
            Assert.Equal(expected, ProjectLanguageDetector.DetectFromFile(path));
        }

        [Fact]
        public void ExcludedDirs_NotCounted()
        {
            // C# files in main dir, C++ files only in bin/ (excluded)
            CreateFiles("Program.cs", "Startup.cs");
            var binDir = Path.Combine(_tempDir, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "output.cpp"), "");
            File.WriteAllText(Path.Combine(binDir, "output.h"), "");
            File.WriteAllText(Path.Combine(binDir, "output2.cpp"), "");

            Assert.Equal(ProjectLanguage.CSharp, ProjectLanguageDetector.DetectLanguage(_tempDir));
        }

        [Fact]
        public void CacheWorks_SecondCallSameResult()
        {
            CreateFiles("main.cpp", "utils.h");
            var first = ProjectLanguageDetector.DetectLanguage(_tempDir);
            var second = ProjectLanguageDetector.DetectLanguage(_tempDir);
            Assert.Equal(first, second);
        }

        private void CreateFiles(params string[] names)
        {
            foreach (var name in names)
            {
                File.WriteAllText(Path.Combine(_tempDir, name), "");
            }
        }
    }
}
