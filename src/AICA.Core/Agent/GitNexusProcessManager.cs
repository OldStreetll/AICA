using System;
using System.Diagnostics;
using System.IO;
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
        private const string STARTUP_SIGNAL = "MCP server starting";

        // Bundled GitNexus entry point (relative to tools/gitnexus/ in AICA project)
        private const string BUNDLED_ENTRY = "dist/cli/index.js";

        // Fallback to npx if bundled version not found
        private const string NPX_COMMAND = "npx";
        private const string NPX_MCP_ARGS = "-y gitnexus@latest mcp";
        private const string NPX_ANALYZE_ARGS = "-y gitnexus@latest analyze";

        /// <summary>
        /// Resolve GitNexus launch command. Prefers bundled version, falls back to npx.
        /// </summary>
        private static (string fileName, string mcpArgs, string analyzeArgs) ResolveGitNexusPath()
        {
            // [D-06] Use assembly location (VSIX install dir) instead of BaseDirectory (VS IDE dir)
            var assemblyDir = Path.GetDirectoryName(typeof(GitNexusProcessManager).Assembly.Location) ?? "";
            var candidates = new[]
            {
                // Relative to VSIX install directory (assembly location) — primary for deployed VSIX
                Path.Combine(assemblyDir, "tools", "gitnexus", BUNDLED_ENTRY),
                // Relative to AICA project root (development: assembly in bin/Debug/netstandard2.0/)
                Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "tools", "gitnexus", BUNDLED_ENTRY),
                // Legacy: BaseDirectory fallback
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "gitnexus", BUNDLED_ENTRY),
                // Environment variable override
                Environment.GetEnvironmentVariable("AICA_GITNEXUS_PATH") ?? ""
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    var fullPath = Path.GetFullPath(candidate);
                    var gitnexusDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath), "..", ".."));
                    var nodeModulesDir = Path.Combine(gitnexusDir, "node_modules");

                    // [D-06] Auto-install dependencies if node_modules missing
                    if (!Directory.Exists(nodeModulesDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus: node_modules not found, running npm install in {gitnexusDir}");
                        try
                        {
                            var npmCmd = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "npm";
                            var npmArgs = Environment.OSVersion.Platform == PlatformID.Win32NT
                                ? "/c npm install --omit=dev"
                                : "install --omit=dev";
                            var npmPsi = new ProcessStartInfo
                            {
                                FileName = npmCmd,
                                Arguments = npmArgs,
                                WorkingDirectory = gitnexusDir,
                                UseShellExecute = false,
                                RedirectStandardOutput = false,
                                RedirectStandardError = false,
                                CreateNoWindow = true
                            };
                            using (var npmProc = Process.Start(npmPsi))
                            {
                                npmProc.WaitForExit(120000);
                                System.Diagnostics.Debug.WriteLine(
                                    $"[AICA] GitNexus: npm install exit={npmProc.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] GitNexus: npm install failed: {ex.Message}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus: using bundled version at {fullPath}");
                    return ("node", $"\"{fullPath}\" mcp", $"\"{fullPath}\" analyze");
                }
            }

            System.Diagnostics.Debug.WriteLine("[AICA] GitNexus: bundled version not found, falling back to npx");
            return (NPX_COMMAND, NPX_MCP_ARGS, NPX_ANALYZE_ARGS);
        }

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
                var (cmd, mcpArgs, _) = ResolveGitNexusPath();
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = mcpArgs,
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
        /// Resolves git repository root from the solution directory before indexing.
        /// </summary>
        public async Task TriggerIndexAsync(string solutionDirectory, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(solutionDirectory)) return;

            // Resolve git repo root (walk up from solution dir to find .git)
            var repoRoot = FindGitRoot(solutionDirectory);
            if (repoRoot == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] GitNexus: no .git found above {solutionDirectory}, skipping index");
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[AICA] GitNexus: resolved repo root {repoRoot} (from {solutionDirectory})");

            try
            {
                var (cmd, _, analyzeArgs) = ResolveGitNexusPath();
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = $"{analyzeArgs} \"{repoRoot}\"",
                    WorkingDirectory = repoRoot,
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
                        $"[AICA] GitNexus analyze completed: exit={exitCode}, repo={repoRoot}");
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

        /// <summary>
        /// Walk up from a directory to find the nearest .git directory (repository root).
        /// Returns null if no .git found within 10 levels.
        /// </summary>
        private static string FindGitRoot(string startDir)
        {
            var dir = startDir;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
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
