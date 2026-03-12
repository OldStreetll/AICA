using System;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Categorizes different types of tool errors
    /// </summary>
    public enum ToolErrorType
    {
        /// <summary>
        /// Invalid or missing parameters
        /// </summary>
        ParameterError,

        /// <summary>
        /// Access to resource was denied
        /// </summary>
        AccessDenied,

        /// <summary>
        /// Resource not found
        /// </summary>
        NotFound,

        /// <summary>
        /// Tool execution timed out
        /// </summary>
        Timeout,

        /// <summary>
        /// Tool execution failed
        /// </summary>
        ExecutionFailed,

        /// <summary>
        /// Tool execution was cancelled
        /// </summary>
        Cancelled,

        /// <summary>
        /// Unknown or unclassified error
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Represents an error that occurred during tool execution
    /// </summary>
    public class ToolError
    {
        /// <summary>
        /// The type of error
        /// </summary>
        public ToolErrorType Type { get; set; }

        /// <summary>
        /// User-friendly error message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Detailed error information for logging
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Suggestion for how to resolve the error
        /// </summary>
        public string Suggestion { get; set; }

        /// <summary>
        /// The underlying exception, if any
        /// </summary>
        public Exception InnerException { get; set; }

        /// <summary>
        /// Create a new ToolError
        /// </summary>
        public ToolError(ToolErrorType type, string message, string details = null, string suggestion = null, Exception innerException = null)
        {
            Type = type;
            Message = message ?? "An error occurred";
            Details = details;
            Suggestion = suggestion;
            InnerException = innerException;
        }
    }

    /// <summary>
    /// Utility class for handling and classifying tool errors
    /// </summary>
    public static class ToolErrorHandler
    {
        /// <summary>
        /// Convert a ToolError to a ToolResult
        /// </summary>
        public static ToolResult HandleError(ToolError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            var message = error.Message;

            // Add suggestion if available
            if (!string.IsNullOrEmpty(error.Suggestion))
                message += $"\n\nSuggestion: {error.Suggestion}";

            // Add details for debugging
            if (!string.IsNullOrEmpty(error.Details))
                message += $"\n\nDetails: {error.Details}";

            return ToolResult.Fail(message);
        }

        /// <summary>
        /// Classify an exception and create a ToolError
        /// </summary>
        public static ToolError ClassifyException(Exception ex, string context = null)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            var contextInfo = string.IsNullOrEmpty(context) ? "" : $" ({context})";

            // Check for specific exception types
            if (ex is ToolParameterException paramEx)
            {
                return new ToolError(
                    ToolErrorType.ParameterError,
                    $"Invalid parameter{contextInfo}: {ex.Message}",
                    ex.ToString(),
                    "Check the tool documentation and ensure all parameters are correct.",
                    ex);
            }

            if (ex is OperationCanceledException)
            {
                return new ToolError(
                    ToolErrorType.Cancelled,
                    $"Operation was cancelled{contextInfo}",
                    ex.ToString(),
                    "The operation was interrupted. You can retry if needed.",
                    ex);
            }

            if (ex is TimeoutException)
            {
                return new ToolError(
                    ToolErrorType.Timeout,
                    $"Operation timed out{contextInfo}",
                    ex.ToString(),
                    "The operation took too long. Try again or use a simpler operation.",
                    ex);
            }

            if (ex is UnauthorizedAccessException)
            {
                return new ToolError(
                    ToolErrorType.AccessDenied,
                    $"Access denied{contextInfo}",
                    ex.ToString(),
                    "You don't have permission to access this resource.",
                    ex);
            }

            if (ex is System.IO.FileNotFoundException)
            {
                return new ToolError(
                    ToolErrorType.NotFound,
                    $"File not found{contextInfo}",
                    ex.ToString(),
                    "Check that the file path is correct and the file exists.",
                    ex);
            }

            if (ex is System.IO.DirectoryNotFoundException)
            {
                return new ToolError(
                    ToolErrorType.NotFound,
                    $"Directory not found{contextInfo}",
                    ex.ToString(),
                    "Check that the directory path is correct and the directory exists.",
                    ex);
            }

            if (ex is System.IO.IOException ioEx)
            {
                return new ToolError(
                    ToolErrorType.ExecutionFailed,
                    $"I/O error{contextInfo}: {ex.Message}",
                    ex.ToString(),
                    "There was a problem reading or writing files. Try again or check disk space.",
                    ex);
            }

            // Default to ExecutionFailed for unknown exceptions
            return new ToolError(
                ToolErrorType.ExecutionFailed,
                $"Tool execution failed{contextInfo}: {ex.Message}",
                ex.ToString(),
                "An unexpected error occurred. Check the details and try again.",
                ex);
        }

        /// <summary>
        /// Create a parameter error
        /// </summary>
        public static ToolError ParameterError(string message, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.ParameterError,
                message,
                suggestion: suggestion);
        }

        /// <summary>
        /// Create an access denied error
        /// </summary>
        public static ToolError AccessDenied(string resource, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.AccessDenied,
                $"Access denied: {resource}",
                suggestion: suggestion ?? "Check that you have permission to access this resource.");
        }

        /// <summary>
        /// Create a not found error
        /// </summary>
        public static ToolError NotFound(string resource, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.NotFound,
                $"Not found: {resource}",
                suggestion: suggestion ?? "Check that the resource exists and the path is correct.");
        }

        /// <summary>
        /// Create a timeout error
        /// </summary>
        public static ToolError Timeout(string operation, int timeoutSeconds, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.Timeout,
                $"Operation timed out after {timeoutSeconds} seconds: {operation}",
                suggestion: suggestion ?? "Try again or use a simpler operation.");
        }

        /// <summary>
        /// Create an execution failed error
        /// </summary>
        public static ToolError ExecutionFailed(string message, string details = null, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.ExecutionFailed,
                message,
                details,
                suggestion);
        }

        /// <summary>
        /// Create a cancelled error
        /// </summary>
        public static ToolError Cancelled(string operation, string suggestion = null)
        {
            return new ToolError(
                ToolErrorType.Cancelled,
                $"Operation was cancelled: {operation}",
                suggestion: suggestion ?? "You can retry if needed.");
        }
    }
}
