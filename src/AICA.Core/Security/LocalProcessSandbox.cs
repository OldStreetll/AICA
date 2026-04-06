using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Security
{
    /// <summary>
    /// Layer 1 sandbox: executes commands via System.Diagnostics.Process.
    /// Integrates with SafetyGuard for whitelist/blacklist validation.
    /// No file system or network isolation — relies on OS-level process security.
    /// </summary>
    public class LocalProcessSandbox : ICommandSandbox
    {
        private readonly SafetyGuard _safetyGuard;

        public LocalProcessSandbox(SafetyGuard safetyGuard = null)
        {
            _safetyGuard = safetyGuard;
        }

        public SandboxCapabilities Capabilities => SandboxCapabilities.LocalProcess();

        public async Task<CommandExecutionResult> ExecuteAsync(
            string command,
            string workingDirectory,
            int timeoutMs,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command))
                return CommandExecutionResult.Error("Command cannot be empty");

            // Validate command safety if SafetyGuard is available
            if (_safetyGuard != null)
            {
                var safetyCheck = _safetyGuard.CheckCommand(command);
                if (safetyCheck.Level == CommandSafetyLevel.Denied)
                    return CommandExecutionResult.Error($"Command denied: {safetyCheck.Reason}");
            }

            try
            {
                // Determine shell based on platform
                string shell, shellArgs;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    shell = "cmd.exe";
                    shellArgs = $"/c {command}";
                }
                else
                {
                    shell = "/bin/bash";
                    shellArgs = $"-c \"{command.Replace("\"", "\\\"")}\"";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = shellArgs,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi })
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    linkedCts.CancelAfter(timeoutMs);

                    var stdoutBuilder = new StringBuilder();
                    var stderrBuilder = new StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) stderrBuilder.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    try
                    {
                        await WaitForExitAsync(process, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        try { process.Kill(); } catch { /* ignore */ }
                        return CommandExecutionResult.Timeout();
                    }

                    return CommandExecutionResult.FromOutput(
                        process.ExitCode,
                        stdoutBuilder.ToString(),
                        stderrBuilder.ToString());
                }
            }
            catch (Exception ex)
            {
                return CommandExecutionResult.Error($"Process execution error: {ex.Message}");
            }
        }

        private static Task WaitForExitAsync(Process process, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            ct.Register(() =>
            {
                tcs.TrySetCanceled();
            });

            if (process.HasExited)
                tcs.TrySetResult(true);

            return tcs.Task;
        }
    }
}
