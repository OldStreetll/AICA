using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for executing terminal commands with safety checks
    /// </summary>
    public class RunCommandTool : IAgentTool
    {
        public string Name => "run_command";
        public string Description => "Execute a terminal/shell command (e.g., 'dotnet build', 'git status', 'npm install'). Returns stdout, stderr, and exit code. Commands require user approval. Use timeout_seconds parameter for long-running commands.";

        /// <summary>
        /// Optional external command safety checker (injected by VS layer)
        /// </summary>
        public Func<string, CommandSafetyInfo> CommandSafetyChecker { get; set; }

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.Command,
                RequiresConfirmation = true,
                RequiresApproval = false,
                TimeoutSeconds = 60,
                IsModifying = false,
                RequiresNetwork = false,
                Tags = new[] { "command", "shell", "execute" }
            };
        }

        public ToolDefinition GetDefinition()
        {
            return new ToolDefinition
            {
                Name = Name,
                Description = Description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["command"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The command to execute (e.g. 'dotnet build', 'git status')"
                        },
                        ["cwd"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Working directory for the command (relative to workspace root). Defaults to workspace root."
                        },
                        ["timeout_seconds"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Maximum time to wait for command completion in seconds. Default is 30."
                        }
                    },
                    Required = new[] { "command" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                // Validate required parameters
                var command = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "command");
                ToolParameterValidator.ValidateNotEmpty(command, "command");

                // Detect common Unix commands on Windows and provide helpful error
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    var unixCommands = new[] { "head", "tail", "grep", "find", "cat", "ls", "rm", "cp", "mv", "chmod", "chown" };
                    var firstWord = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();

                    if (firstWord != null && unixCommands.Contains(firstWord))
                    {
                        return ToolResult.Fail($"Unix command '{firstWord}' is not available on Windows. Please use the appropriate built-in tool instead:\n" +
                            "- Use 'grep_search' instead of 'grep'\n" +
                            "- Use 'find_by_name' instead of 'find'\n" +
                            "- Use 'read_file' instead of 'cat', 'head', or 'tail'\n" +
                            "- Use 'list_dir' instead of 'ls'");
                    }
                }

                // Parse optional parameters
                var cwd = context.WorkingDirectory;
                var cwdParam = ToolParameterValidator.GetOptionalParameter<string>(call.Arguments, "cwd");
                if (!string.IsNullOrWhiteSpace(cwdParam))
                {
                    if (System.IO.Path.IsPathRooted(cwdParam))
                        cwd = cwdParam;
                    else
                        cwd = System.IO.Path.Combine(context.WorkingDirectory, cwdParam);
                }

                var timeoutSeconds = ToolParameterValidator.GetOptionalParameter<int>(call.Arguments, "timeout_seconds", 30);
                ToolParameterValidator.ValidateRange(timeoutSeconds, 1, 300, "timeout_seconds");

                // Safety check via injected checker
                if (CommandSafetyChecker != null)
                {
                    var safety = CommandSafetyChecker(command);
                    if (safety.IsDenied)
                        return ToolResult.Fail($"Command denied: {safety.Reason}");
                }

                // Request user confirmation
                var confirmed = await context.RequestConfirmationAsync(
                    "Run Command",
                    $"Execute command:\n```\n{command}\n```\nIn directory: {cwd}",
                    ct);

                if (!confirmed)
                    return ToolResult.Fail("Command execution cancelled by user.");

                // Execute the command
                var result = await ExecuteCommandAsync(command, cwd, timeoutSeconds, ct);
                return result;
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.Cancelled("run_command"));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "run_command");
                return ToolErrorHandler.HandleError(error);
            }
        }

        private async Task<ToolResult> ExecuteCommandAsync(string command, string workingDirectory, int timeoutSeconds, CancellationToken ct)
        {
            // Parse command safely to prevent injection
            var (executable, arguments) = ParseCommandSafely(command);

            if (string.IsNullOrEmpty(executable))
            {
                return ToolResult.Fail("Invalid command: executable not found");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null && stdoutBuilder.Length < 16000)
                        stdoutBuilder.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null && stderrBuilder.Length < 8000)
                        stderrBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait with timeout and cancellation
                var timeoutMs = timeoutSeconds * 1000;
                var completed = await Task.Run(() => process.WaitForExit(timeoutMs), ct).ConfigureAwait(false);

                if (!completed)
                {
                    try { process.Kill(); } catch { }
                    return ToolResult.Fail($"Command timed out after {timeoutSeconds} seconds.\n\nPartial stdout:\n{Truncate(stdoutBuilder.ToString(), 2000)}\n\nPartial stderr:\n{Truncate(stderrBuilder.ToString(), 1000)}");
                }

                var exitCode = process.ExitCode;
                var stdout = stdoutBuilder.ToString();
                var stderr = stderrBuilder.ToString();

                var resultBuilder = new StringBuilder();
                resultBuilder.AppendLine($"Exit code: {exitCode}");

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    resultBuilder.AppendLine("\nstdout:");
                    resultBuilder.AppendLine(Truncate(stdout, 6000));
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    resultBuilder.AppendLine("\nstderr:");
                    resultBuilder.AppendLine(Truncate(stderr, 3000));
                }

                if (exitCode == 0)
                    return ToolResult.Ok(resultBuilder.ToString());
                else
                    return ToolResult.Fail(resultBuilder.ToString());
            }
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "\n... (truncated)";
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Safely parse command to prevent injection attacks
        /// </summary>
        private (string executable, string arguments) ParseCommandSafely(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return (null, null);

            command = command.Trim();

            // Split command into executable and arguments
            var parts = SplitCommandLine(command);
            if (parts.Length == 0)
                return (null, null);

            var executable = parts[0];
            var arguments = parts.Length > 1 ? string.Join(" ", parts.Skip(1).Select(EscapeArgument)) : "";

            // Validate executable name (no path traversal, no shell metacharacters)
            if (!IsValidExecutableName(executable))
                return (null, null);

            return (executable, arguments);
        }

        /// <summary>
        /// Split command line respecting quotes
        /// </summary>
        private string[] SplitCommandLine(string commandLine)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool escapeNext = false;

            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];

                if (escapeNext)
                {
                    current.Append(c);
                    escapeNext = false;
                }
                else if (c == '\\')
                {
                    escapeNext = true;
                }
                else if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result.ToArray();
        }

        /// <summary>
        /// Validate executable name to prevent injection
        /// </summary>
        private bool IsValidExecutableName(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                return false;

            // Prevent path traversal
            if (executable.Contains("..") || executable.Contains("/") || executable.Contains("\\"))
                return false;

            // Prevent shell metacharacters
            var dangerousChars = new[] { '|', '&', ';', '>', '<', '`', '$', '(', ')', '{', '}', '[', ']', '*', '?' };
            if (executable.Any(c => dangerousChars.Contains(c)))
                return false;

            return true;
        }

        /// <summary>
        /// Properly escape command line arguments
        /// </summary>
        private string EscapeArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";

            // If argument contains spaces or special characters, quote it
            if (argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                return "\"" + argument.Replace("\"", "\\\"") + "\"";
            }

            return argument;
        }
    }

    /// <summary>
    /// Command safety check result (used by VS layer to inject SafetyGuard checks)
    /// </summary>
    public class CommandSafetyInfo
    {
        public bool IsDenied { get; set; }
        public bool RequiresApproval { get; set; }
        public string Reason { get; set; }

        public static CommandSafetyInfo Allow() => new CommandSafetyInfo();
        public static CommandSafetyInfo Deny(string reason) => new CommandSafetyInfo { IsDenied = true, Reason = reason };
        public static CommandSafetyInfo NeedApproval(string reason) => new CommandSafetyInfo { RequiresApproval = true, Reason = reason };
    }
}
