using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Storage;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for executing terminal commands with safety checks
    /// </summary>
    public class RunCommandTool : IAgentTool
    {
        private readonly Security.ICommandSandbox _sandbox;

        public string Name => "run_command";
        public string Description =>
            "Execute a shell command and return stdout, stderr, and exit code. Requires user approval. " +
            "Use ONLY for build, test, git, and system commands. " +
            "Do NOT use for file operations — use read_file, edit, write_file, grep_search, glob, list_dir instead. " +
            "Do NOT use cat, grep, find, ls, or similar shell commands when dedicated tools exist.";

        /// <summary>
        /// Optional external command safety checker (injected by VS layer)
        /// </summary>
        public Func<string, CommandSafetyInfo> CommandSafetyChecker { get; set; }

        /// <summary>
        /// Create RunCommandTool with optional sandbox for isolated execution.
        /// </summary>
        public RunCommandTool(Security.ICommandSandbox sandbox = null)
        {
            _sandbox = sandbox;
        }

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
                        },
                        ["description"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Brief description of what this command does and why you are running it"
                        }
                    },
                    Required = new[] { "command", "description" }
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
                            "- Use 'glob' instead of 'find'\n" +
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

                var timeoutSeconds = ToolParameterValidator.GetOptionalParameter<int>(call.Arguments, "timeout_seconds", Config.AicaConfig.Current.Tools.CommandDefaultTimeoutSeconds);
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

                // Execute the command — use sandbox if available, otherwise direct process
                if (_sandbox != null)
                {
                    var sandboxResult = await _sandbox.ExecuteAsync(command, cwd, timeoutSeconds * 1000, ct);
                    if (sandboxResult.TimedOut)
                        return ToolResult.Fail($"Command timed out after {timeoutSeconds} seconds.");

                    var output = FormatCommandOutput(sandboxResult.ExitCode, sandboxResult.Stdout, sandboxResult.Stderr);
                    return sandboxResult.Success ? ToolResult.Ok(output) : ToolResult.Fail(output);
                }

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
            // Use shell wrapper so redirections (>, |, &&) work correctly.
            // SECURITY: CommandSafetyChecker (injected by VS layer) and user confirmation
            // in ExecuteAsync are the safety gates — they run BEFORE this method is called.
            string executable;
            string arguments;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                executable = "cmd.exe";
                arguments = $"/c {command}";
            }
            else
            {
                executable = "/bin/bash";
                arguments = $"-c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
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
                    var partialStdout = TruncateStdout(stdoutBuilder.ToString(), 2000);
                    return ToolResult.Fail($"Command timed out after {timeoutSeconds} seconds.\n\nPartial stdout:\n{partialStdout}\n\nPartial stderr:\n{Truncate(stderrBuilder.ToString(), 1000)}");
                }

                var exitCode = process.ExitCode;
                var stdout = stdoutBuilder.ToString();
                var stderr = stderrBuilder.ToString();

                var resultBuilder = new StringBuilder();
                resultBuilder.AppendLine($"Exit code: {exitCode}");

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    resultBuilder.AppendLine("\nstdout:");
                    resultBuilder.AppendLine(TruncateStdout(stdout, 6000));
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

        /// <summary>
        /// v2.1 H1: Truncate stdout with optional persistence of full output.
        /// When feature flag is on and output exceeds limit, full output is saved to disk.
        /// </summary>
        private string TruncateStdout(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;

            if (AicaConfig.Current.Features.TruncationPersistence)
            {
                var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
                    "run_command", text, maxLength);
                if (tr.WasTruncated)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] run_command stdout truncation persisted: {tr.FullOutputPath} ({text.Length} chars)");
                    return tr.PreviewText +
                        $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
                        "Use read_file with the above path to see the complete output.";
                }
                return tr.PreviewText;
            }

            return Truncate(text, maxLength);
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "\n... (truncated)";
        }

        private string FormatCommandOutput(int exitCode, string stdout, string stderr)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Exit code: {exitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                sb.AppendLine("\nstdout:");
                sb.AppendLine(TruncateStdout(stdout, 6000));
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine("\nstderr:");
                sb.AppendLine(Truncate(stderr, 3000));
            }
            return sb.ToString();
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
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
