using System.Collections.Generic;

namespace AICA.Core.Workspace
{
    /// <summary>
    /// Represents metadata about a project in the solution
    /// </summary>
    public class ProjectInfo
    {
        /// <summary>
        /// Project name (e.g., "Project1")
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Absolute path to the project file (.vcxproj, .csproj, etc.)
        /// </summary>
        public string ProjectFilePath { get; set; }

        /// <summary>
        /// Project type (vcxproj, csproj, etc.)
        /// </summary>
        public string ProjectType { get; set; }

        /// <summary>
        /// Project directory (parent directory of the project file)
        /// </summary>
        public string ProjectDirectory { get; set; }

        /// <summary>
        /// List of source files in this project (absolute paths)
        /// </summary>
        public List<string> SourceFiles { get; set; } = new List<string>();

        /// <summary>
        /// Filter structure for C++ projects (from .vcxproj.filters)
        /// Key: Filter path (e.g., "Source Files\Utilities")
        /// Value: List of file paths in that filter
        /// </summary>
        public Dictionary<string, List<string>> Filters { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Project dependencies (names of other projects this project depends on)
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// Project GUID from solution file
        /// </summary>
        public string ProjectGuid { get; set; }

        /// <summary>
        /// Get a summary of the project structure
        /// </summary>
        public string GetSummary()
        {
            var summary = $"Project: {Name}\n";
            summary += $"Type: {ProjectType}\n";
            summary += $"Files: {SourceFiles.Count}\n";

            if (Filters.Count > 0)
            {
                summary += $"Filters:\n";
                foreach (var filter in Filters.Keys)
                {
                    var fileCount = Filters[filter].Count;
                    summary += $"  - {filter} ({fileCount} files)\n";
                }
            }

            if (Dependencies.Count > 0)
            {
                summary += $"Dependencies: {string.Join(", ", Dependencies)}\n";
            }

            return summary;
        }
    }
}
