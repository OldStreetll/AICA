using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Manages the GitNexus MCP server Node.js process lifecycle.
    /// Singleton — one process per VS session.
    /// </summary>
    public sealed class GitNexusProcessManager : IGitNexusProcessManager, IDisposable
    {
        private static readonly Lazy<GitNexusProcessManager> _lazy =
            new Lazy<GitNexusProcessManager>(() => new GitNexusProcessManager());

        public static GitNexusProcessManager Instance => _lazy.Value;

        private Process _process;
        private McpClient _client;
        private readonly object _lock = new object();
        private volatile GitNexusState _state = GitNexusState.NotStarted;

        private const int StartTimeoutMs = 15000;
        private const int IndexTimeoutMs = 60000;
        private const string MCP_COMMAND = "npx";
        private const string MCP_ARGS = "-y gitnexus@latest mcp";
        private const string ANALYZE_ARGS = "-y gitnexus@latest analyze";
        private const string STARTUP_SIGNAL = "MCP server starting";

        public GitNexusState State => _state;
        public McpClient Client => _state == GitNexusState.Ready ? _client : null;

        private GitNexusProcessManager() { }

        /// <summary>
        /// Start the MCP server process. Waits for startup signal on stderr.
        /// </summary>
        public async Task<bool> StartAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                if (_state == GitNexusState.Disposed) return false;
                if (_state == GitNexusState.Ready && _process != null && !_process.HasExited)
                    return true;

                _state = GitNexusState.Starting;
            }

            CleanupProcess();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = MCP_COMMAND,
                    Arguments = MCP_ARGS,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var startupTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus stderr: {e.Data}");
                    if (e.Data.Contains(STARTUP_SIGNAL))
                    {
                        startupTcs.TrySetResult(true);
                    }
                };

                process.Exited += (sender, e) =>
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] GitNexus process exited");
                    _state = GitNexusState.Failed;
                    startupTcs.TrySetResult(false);
                };

                if (!process.Start())
                {
                    _state = GitNexusState.Failed;
                    return false;
                }

                process.BeginErrorReadLine();

                // Wait for startup signal with timeout
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(StartTimeoutMs);
                    using (timeoutCts.Token.Register(() => startupTcs.TrySetResult(false)))
                    {
                        var started = await startupTcs.Task.ConfigureAwait(false);
                        if (!started || process.HasExited)
                        {
                            System.Diagnostics.Debug.WriteLine("[AICA] GitNexus startup failed or timed out");
                            TryKillProcess(process);
                            _state = GitNexusState.Failed;
                            return false;
                        }
                    }
                }

                // Create MCP client and initialize
                var client = new McpClient(
                    process.StandardInput.BaseStream,
                    process.StandardOutput.BaseStream);

                await client.InitializeAsync(ct).ConfigureAwait(false);

                lock (_lock)
                {
                    _process = process;
                    _client = client;
                    _state = GitNexusState.Ready;
                }

                System.Diagnostics.Debug.WriteLine("[AICA] GitNexus MCP server ready");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus start error: {ex.Message}");
                _state = GitNexusState.Failed;
                return false;
            }
        }

        /// <summary>
        /// Ensure the MCP server is running. Attempts a single restart if not.
        /// </summary>
        public async Task<bool> EnsureRunningAsync(CancellationToken ct)
        {
            if (_state == GitNexusState.Disposed) return false;

            if (_state == GitNexusState.Ready && _process != null && !_process.HasExited)
                return true;

            // Process died or never started — attempt single restart
            System.Diagnostics.Debug.WriteLine("[AICA] GitNexus not running, attempting restart");
            return await StartAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Trigger GitNexus indexing for a solution directory (separate process, fire-and-forget).
        /// </summary>
        public async Task TriggerIndexAsync(string solutionDirectory, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(solutionDirectory)) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = MCP_COMMAND,
                    Arguments = ANALYZE_ARGS,
                    WorkingDirectory = solutionDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = psi })
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(IndexTimeoutMs);

                    process.Start();

                    var exitTask = Task.Run(() =>
                    {
                        process.WaitForExit();
                        return process.ExitCode;
                    }, timeoutCts.Token);

                    var exitCode = await exitTask.ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] GitNexus analyze completed: exit={exitCode}, dir={solutionDirectory}");
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[AICA] GitNexus analyze timed out");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus analyze error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_state == GitNexusState.Disposed) return;
            _state = GitNexusState.Disposed;

            CleanupProcess();
            System.Diagnostics.Debug.WriteLine("[AICA] GitNexus process manager disposed");
        }

        private void CleanupProcess()
        {
            var client = _client;
            var process = _process;
            _client = null;
            _process = null;

            try { client?.Dispose(); } catch { }
            TryKillProcess(process);
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }
            try { process.Dispose(); } catch { }
        }
    }
}
