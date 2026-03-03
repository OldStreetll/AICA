using System;
using Xunit;
using AICA.Core.Security;

namespace AICA.Tests.Security
{
    public class AutoApproveManagerTests
    {
        [Fact]
        public void ShouldAutoApprove_ReadOperations_WhenEnabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileRead = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("read_file", "test.txt"));
            Assert.True(manager.ShouldAutoApprove("list_dir", "/some/path"));
            Assert.True(manager.ShouldAutoApprove("grep_search", "pattern"));
            Assert.True(manager.ShouldAutoApprove("find_by_name", "*.cs"));
        }

        [Fact]
        public void ShouldNotAutoApprove_ReadOperations_WhenDisabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileRead = false
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.False(manager.ShouldAutoApprove("read_file", "test.txt"));
            Assert.False(manager.ShouldAutoApprove("list_dir", "/some/path"));
            Assert.False(manager.ShouldAutoApprove("grep_search", "pattern"));
            Assert.False(manager.ShouldAutoApprove("find_by_name", "*.cs"));
        }

        [Fact]
        public void ShouldAutoApprove_FileCreate_WhenEnabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileCreate = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("Create File", "newfile.txt"));
        }

        [Fact]
        public void ShouldNotAutoApprove_FileCreate_WhenDisabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileCreate = false
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.False(manager.ShouldAutoApprove("Create File", "newfile.txt"));
        }

        [Fact]
        public void ShouldAutoApprove_FileEdit_WhenEnabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileEdit = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("Edit File", "existingfile.txt"));
        }

        [Fact]
        public void ShouldNotAutoApprove_FileEdit_WhenDisabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileEdit = false
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.False(manager.ShouldAutoApprove("Edit File", "existingfile.txt"));
        }

        [Fact]
        public void ShouldAutoApprove_SafeCommands_WhenEnabled()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveSafeCommands = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("Run Command", "dotnet build"));
            Assert.True(manager.ShouldAutoApprove("Run Command", "npm install"));
            Assert.True(manager.ShouldAutoApprove("Run Command", "git status"));
            Assert.True(manager.ShouldAutoApprove("Run Command", "nuget restore"));
        }

        [Fact]
        public void ShouldNotAutoApprove_UnsafeCommands()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveSafeCommands = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.False(manager.ShouldAutoApprove("Run Command", "rm -rf /"));
            Assert.False(manager.ShouldAutoApprove("Run Command", "del /s /q C:\\"));
            Assert.False(manager.ShouldAutoApprove("Run Command", "format C:"));
        }

        [Fact]
        public void ShouldAutoApprove_CustomRule()
        {
            // Arrange
            var options = new AutoApproveOptions();
            var manager = new AutoApproveManager(options);

            manager.AddRule(new AutoApproveRule
            {
                OperationType = "Custom Operation",
                Condition = (op, details) => details.Contains("test")
            });

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("Custom Operation", "test file"));
            Assert.False(manager.ShouldAutoApprove("Custom Operation", "production file"));
        }

        [Fact]
        public void ShouldNotAutoApprove_FileDelete_ByDefault()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileDelete = false
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.False(manager.ShouldAutoApprove("Delete File", "somefile.txt"));
        }

        [Fact]
        public void ShouldAutoApprove_CaseInsensitive()
        {
            // Arrange
            var options = new AutoApproveOptions
            {
                AutoApproveFileRead = true
            };
            var manager = new AutoApproveManager(options);

            // Act & Assert
            Assert.True(manager.ShouldAutoApprove("READ_FILE", "test.txt"));
            Assert.True(manager.ShouldAutoApprove("Read_File", "test.txt"));
            Assert.True(manager.ShouldAutoApprove("read_file", "test.txt"));
        }
    }
}
