using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for analyzing log files - reading, filtering by level, and generating statistics
    /// </summary>
    public class LogAnalysisTool : IAgentTool
    {
        public string Name => "log_analysis";
        public string Description => "Analyze log files by reading content, filtering by log levels (INFO/ERROR/WARN), " +
            "and generating error statistics. Use this to debug issues and understand application behavior.";

        // Common log patterns
        private static readonly Regex LogLinePattern = new Regex(
            @"^(?<timestamp>\d{4}[-/]\d{2}[-/]\d{2}[\sT]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\s*" +
            @"(?:\[?(?<level>DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|TRACE)\]?)?\s*" +
            @"(?:\[?(?<source>[^\]]+)\]?)?\s*" +
            @"(?<message>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the log file to analyze (relative to workspace root)"
                        },
                        ["level"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Optional. Filter by log level: INFO, ERROR, WARN, DEBUG, FATAL. If not specified, returns all lines.",
                            Enum = new[] { "INFO", "ERROR", "WARN", "DEBUG", "FATAL", "ALL" }
                        },
                        ["limit"] = new ToolParameterProperty
                        {
                            Type = "integer",
                            Description = "Optional. Maximum number of log lines to return. Default: 100"
                        },
                        ["show_stats"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "Optional. If true, include error statistics summary. Default: true"
                        },
                        ["search"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Optional. Search for a specific pattern in log messages (case-insensitive)"
                        }
                    },
                    Required = new[] { "path" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            // Validate required parameter
            if (!call.Arguments.TryGetValue("path", out var pathObj) || pathObj == null)
            {
                return ToolResult.Fail("Missing required parameter: path");
            }

            var path = pathObj.ToString();

            // Resolve and validate file path
            var resolvedPath = context.ResolveFilePath(path);
            if (resolvedPath != null)
            {
                if (!context.IsPathAccessible(resolvedPath))
                    return ToolResult.SecurityDenied($"Access denied: {path}");
            }
            else
            {
                if (!context.IsPathAccessible(path))
                    return ToolResult.SecurityDenied($"Access denied: {path}");

                if (!await context.FileExistsAsync(path, ct))
                    return ToolResult.Fail($"File not found: {path}");
                
                resolvedPath = path;
            }

            // Parse optional parameters
            string filterLevel = null;
            if (call.Arguments.TryGetValue("level", out var levelObj) && levelObj != null)
            {
                filterLevel = levelObj.ToString().ToUpperInvariant();
                if (filterLevel == "ALL") filterLevel = null;
            }

            int limit = 100;
            if (call.Arguments.TryGetValue("limit", out var limitObj) && limitObj != null)
            {
                if (int.TryParse(limitObj.ToString(), out var l) && l > 0)
                    limit = l;
            }

            bool showStats = true;
            if (call.Arguments.TryGetValue("show_stats", out var statsObj) && statsObj != null)
            {
                if (statsObj is bool b) showStats = b;
                else if (statsObj is System.Text.Json.JsonElement je)
                {
                    if (je.ValueKind == System.Text.Json.JsonValueKind.True) showStats = true;
                    else if (je.ValueKind == System.Text.Json.JsonValueKind.False) showStats = false;
                }
            }

            string searchPattern = null;
            if (call.Arguments.TryGetValue("search", out var searchObj) && searchObj != null)
            {
                searchPattern = searchObj.ToString();
            }

            try
            {
                // Read log file
                var content = await context.ReadFileAsync(path, ct);
                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // Parse and filter log lines
                var parsedLines = new List<LogEntry>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var entry = ParseLogLine(line);
                    parsedLines.Add(entry);
                }

                // Apply filters
                var filteredLines = parsedLines.AsEnumerable();

                // Filter by level
                if (!string.IsNullOrEmpty(filterLevel))
                {
                    filteredLines = filteredLines.Where(e => 
                        e.Level.Equals(filterLevel, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by search pattern
                if (!string.IsNullOrEmpty(searchPattern))
                {
                    filteredLines = filteredLines.Where(e => 
                        e.Message.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                // Take limited results
                var resultLines = filteredLines.Take(limit).ToList();

                // Build output
                var output = new StringBuilder();

                // Add filtered log lines
                output.AppendLine("=== Log Analysis Results ===");
                output.AppendLine($"File: {path}");
                if (!string.IsNullOrEmpty(filterLevel))
                    output.AppendLine($"Level Filter: {filterLevel}");
                if (!string.IsNullOrEmpty(searchPattern))
                    output.AppendLine($"Search Pattern: {searchPattern}");
                output.AppendLine($"Showing: {resultLines.Count} of {filteredLines.Count()} entries");
                output.AppendLine();

                foreach (var entry in resultLines)
                {
                    output.AppendLine(entry.OriginalLine);
                }

                // Add statistics if requested
                if (showStats)
                {
                    var stats = GetStatistics(parsedLines);
                    output.AppendLine();
                    output.AppendLine("=== Log Statistics ===");
                    output.AppendLine($"Total Lines: {stats.TotalLines}");
                    output.AppendLine($"  DEBUG: {stats.DebugCount}");
                    output.AppendLine($"  INFO:  {stats.InfoCount}");
                    output.AppendLine($"  WARN:  {stats.WarnCount}");
                    output.AppendLine($"  ERROR: {stats.ErrorCount}");
                    output.AppendLine($"  FATAL: {stats.FatalCount}");
                    
                    if (stats.ErrorCount > 0)
                    {
                        output.AppendLine();
                        output.AppendLine("=== Error Summary (last 10) ===");
                        var errors = parsedLines
                            .Where(e => e.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase) || 
                                        e.Level.Equals("FATAL", StringComparison.OrdinalIgnoreCase))
                            .Take(10);
                        foreach (var error in errors)
                        {
                            output.AppendLine($"[{error.Timestamp}] {error.Message}");
                        }
                    }
                }

                return ToolResult.Ok(output.ToString());
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to analyze log file: {ex.Message}");
            }
        }

        private LogEntry ParseLogLine(string line)
        {
            var match = LogLinePattern.Match(line);
            if (match.Success)
            {
                return new LogEntry
                {
                    Timestamp = match.Groups["timestamp"].Value,
                    Level = match.Groups["level"].Success ? match.Groups["level"].Value.ToUpperInvariant() : "INFO",
                    Source = match.Groups["source"].Success ? match.Groups["source"].Value : null,
                    Message = match.Groups["message"].Value,
                    OriginalLine = line
                };
            }

            // Fallback: treat entire line as message
            return new LogEntry
            {
                Timestamp = "",
                Level = "INFO",
                Message = line,
                OriginalLine = line
            };
        }

        private LogStatistics GetStatistics(List<LogEntry> entries)
        {
            var stats = new LogStatistics { TotalLines = entries.Count };
            
            foreach (var entry in entries)
            {
                switch (entry.Level.ToUpperInvariant())
                {
                    case "DEBUG":
                    case "TRACE":
                        stats.DebugCount++;
                        break;
                    case "INFO":
                        stats.InfoCount++;
                        break;
                    case "WARN":
                    case "WARNING":
                        stats.WarnCount++;
                        break;
                    case "ERROR":
                        stats.ErrorCount++;
                        break;
                    case "FATAL":
                    case "CRITICAL":
                        stats.FatalCount++;
                        break;
                }
            }

            return stats;
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext uiContext, CancellationToken ct = default)
        {
            // No streaming support needed for log analysis
            return Task.CompletedTask;
        }

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.Analysis,
                RequiresConfirmation = false,
                RequiresApproval = false,
                TimeoutSeconds = 60,
                Tags = new[] { "log", "analysis", "debug", "error" },
                IsModifying = false,
                RequiresNetwork = false,
                IsExperimental = false
            };
        }

        private class LogEntry
        {
            public string Timestamp { get; set; }
            public string Level { get; set; }
            public string Source { get; set; }
            public string Message { get; set; }
            public string OriginalLine { get; set; }
        }

        private class LogStatistics
        {
            public int TotalLines { get; set; }
            public int DebugCount { get; set; }
            public int InfoCount { get; set; }
            public int WarnCount { get; set; }
            public int ErrorCount { get; set; }
            public int FatalCount { get; set; }
        }
    }
}