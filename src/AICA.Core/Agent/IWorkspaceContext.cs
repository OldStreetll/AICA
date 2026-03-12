using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Workspace;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Context interface for workspace operations
    /// </summary>
    public interface IWorkspaceContext
    {
        /// <summary>
        /// Current working directory
        /// </summary>
        string WorkingDirectory { get; }

        /// <summary>
        /// Source root directories discovered from solution/project files.
        /// Contains directories outside WorkingDirectory that hold source files
        /// referenced by .vcxproj/.csproj projects.
        /// </summary>
        IReadOnlyList<string> SourceRoots { get; }

        /// <summary>
        /// Get list of files that can be accessed
        /// </summary>
        Task<IEnumerable<string>> GetAccessibleFilesAsync(CancellationToken ct = default);

        /// <summary>
        /// Resolve a file path across working directory and source roots.
        /// Returns the absolute path if found, null otherwise.
        /// </summary>
        string ResolveFilePath(string requestedPath);

        /// <summary>
        /// Resolve a directory path across working directory and source roots.
        /// Returns the absolute path if found, null otherwise.
        /// </summary>
        string ResolveDirectoryPath(string requestedPath);

        /// <summary>
        /// Get all projects in the solution.
        /// Returns a dictionary: project name → ProjectInfo.
        /// </summary>
        Dictionary<string, ProjectInfo> GetProjects();

        /// <summary>
        /// If non-null, contains a warning message indicating that the project
        /// was opened from a different location than where it was originally built.
        /// The UI should display this to the user.
        /// </summary>
        string PathMismatchWarning { get; }
    }
}
