using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Security
{
    /// <summary>
    /// Interface for sandboxed command execution.
    /// Abstracts command execution behind a consistent interface,
    /// enabling different isolation levels (process, Docker, cloud).
    /// </summary>
    public interface ICommandSandbox
    {
        /// <summary>
        /// Execute a command in the sandbox.
        /// </summary>
        Task<CommandExecutionResult> ExecuteAsync(
            string command,
            string workingDirectory,
            int timeoutMs,
            CancellationToken ct = default);

        /// <summary>
        /// Capabilities of this sandbox implementation.
        /// </summary>
        SandboxCapabilities Capabilities { get; }
    }

    /// <summary>
    /// Result of a sandboxed command execution.
    /// </summary>
    public class CommandExecutionResult
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public bool TimedOut { get; set; }
        public bool Success => ExitCode == 0 && !TimedOut;

        public static CommandExecutionResult FromOutput(int exitCode, string stdout, string stderr)
        {
            return new CommandExecutionResult
            {
                ExitCode = exitCode,
                Stdout = stdout,
                Stderr = stderr
            };
        }

        public static CommandExecutionResult Timeout()
        {
            return new CommandExecutionResult { TimedOut = true, ExitCode = -1 };
        }

        public static CommandExecutionResult Error(string error)
        {
            return new CommandExecutionResult { ExitCode = -1, Stderr = error };
        }
    }

    /// <summary>
    /// Describes the isolation capabilities of a sandbox implementation.
    /// </summary>
    public class SandboxCapabilities
    {
        /// <summary>
        /// Whether the sandbox provides file system isolation.
        /// </summary>
        public bool SupportsFileIsolation { get; set; }

        /// <summary>
        /// Whether the sandbox provides network isolation.
        /// </summary>
        public bool SupportsNetworkIsolation { get; set; }

        /// <summary>
        /// Whether the sandbox supports resource limits (CPU, memory).
        /// </summary>
        public bool SupportsResourceLimits { get; set; }

        /// <summary>
        /// Human-readable name of the sandbox type.
        /// </summary>
        public string Name { get; set; }

        public static SandboxCapabilities LocalProcess()
        {
            return new SandboxCapabilities
            {
                Name = "LocalProcess",
                SupportsFileIsolation = false,
                SupportsNetworkIsolation = false,
                SupportsResourceLimits = false
            };
        }
    }
}
