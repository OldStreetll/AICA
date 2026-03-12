using System;
using System.IO;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolErrorHandlerTests
    {
        [Fact]
        public void HandleError_WithParameterError_ReturnsFailResult()
        {
            // Arrange
            var error = new ToolError(ToolErrorType.ParameterError, "Invalid parameter");

            // Act
            var result = ToolErrorHandler.HandleError(error);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Invalid parameter", result.Error);
        }

        [Fact]
        public void HandleError_WithSuggestion_IncludesSuggestionInMessage()
        {
            // Arrange
            var error = new ToolError(
                ToolErrorType.ParameterError,
                "Invalid parameter",
                suggestion: "Check the documentation");

            // Act
            var result = ToolErrorHandler.HandleError(error);

            // Assert
            Assert.Contains("Check the documentation", result.Error);
        }

        [Fact]
        public void HandleError_WithDetails_IncludesDetailsInMessage()
        {
            // Arrange
            var error = new ToolError(
                ToolErrorType.ExecutionFailed,
                "Operation failed",
                details: "Detailed error information");

            // Act
            var result = ToolErrorHandler.HandleError(error);

            // Assert
            Assert.Contains("Detailed error information", result.Error);
        }

        [Fact]
        public void ClassifyException_WithToolParameterException_ReturnsParameterError()
        {
            // Arrange
            var ex = new ToolParameterException("Missing parameter");

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.ParameterError, error.Type);
        }

        [Fact]
        public void ClassifyException_WithOperationCanceledException_ReturnsCancelledError()
        {
            // Arrange
            var ex = new OperationCanceledException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.Cancelled, error.Type);
        }

        [Fact]
        public void ClassifyException_WithTimeoutException_ReturnsTimeoutError()
        {
            // Arrange
            var ex = new TimeoutException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.Timeout, error.Type);
        }

        [Fact]
        public void ClassifyException_WithUnauthorizedAccessException_ReturnsAccessDeniedError()
        {
            // Arrange
            var ex = new UnauthorizedAccessException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.AccessDenied, error.Type);
        }

        [Fact]
        public void ClassifyException_WithFileNotFoundException_ReturnsNotFoundError()
        {
            // Arrange
            var ex = new FileNotFoundException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.NotFound, error.Type);
        }

        [Fact]
        public void ClassifyException_WithDirectoryNotFoundException_ReturnsNotFoundError()
        {
            // Arrange
            var ex = new DirectoryNotFoundException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.NotFound, error.Type);
        }

        [Fact]
        public void ClassifyException_WithIOException_ReturnsExecutionFailedError()
        {
            // Arrange
            var ex = new IOException();

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.ExecutionFailed, error.Type);
        }

        [Fact]
        public void ClassifyException_WithGenericException_ReturnsExecutionFailedError()
        {
            // Arrange
            var ex = new Exception("Generic error");

            // Act
            var error = ToolErrorHandler.ClassifyException(ex);

            // Assert
            Assert.Equal(ToolErrorType.ExecutionFailed, error.Type);
        }

        [Fact]
        public void ClassifyException_WithContext_IncludesContextInMessage()
        {
            // Arrange
            var ex = new Exception("Error");

            // Act
            var error = ToolErrorHandler.ClassifyException(ex, "my_tool");

            // Assert
            Assert.Contains("my_tool", error.Message);
        }

        [Fact]
        public void ParameterError_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.ParameterError("Invalid value", "Use a valid value");

            // Assert
            Assert.Equal(ToolErrorType.ParameterError, error.Type);
            Assert.Equal("Invalid value", error.Message);
            Assert.Equal("Use a valid value", error.Suggestion);
        }

        [Fact]
        public void AccessDenied_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.AccessDenied("file.txt");

            // Assert
            Assert.Equal(ToolErrorType.AccessDenied, error.Type);
            Assert.Contains("file.txt", error.Message);
        }

        [Fact]
        public void NotFound_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.NotFound("file.txt");

            // Assert
            Assert.Equal(ToolErrorType.NotFound, error.Type);
            Assert.Contains("file.txt", error.Message);
        }

        [Fact]
        public void Timeout_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.Timeout("operation", 30);

            // Assert
            Assert.Equal(ToolErrorType.Timeout, error.Type);
            Assert.Contains("30", error.Message);
        }

        [Fact]
        public void ExecutionFailed_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.ExecutionFailed("Operation failed", "Details", "Try again");

            // Assert
            Assert.Equal(ToolErrorType.ExecutionFailed, error.Type);
            Assert.Equal("Operation failed", error.Message);
            Assert.Equal("Details", error.Details);
            Assert.Equal("Try again", error.Suggestion);
        }

        [Fact]
        public void Cancelled_CreatesCorrectError()
        {
            // Act
            var error = ToolErrorHandler.Cancelled("operation");

            // Assert
            Assert.Equal(ToolErrorType.Cancelled, error.Type);
            Assert.Contains("operation", error.Message);
        }
    }
}
