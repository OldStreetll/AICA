using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Structured condense summary with typed sections.
    /// Enables downstream consumers to access individual sections.
    /// </summary>
    public class CondenseSummary
    {
        public List<string> FilesRead { get; set; } = new List<string>();
        public List<string> FilesModified { get; set; } = new List<string>();
        public List<string> FilesCreated { get; set; } = new List<string>();
        public List<string> SearchesPerformed { get; set; } = new List<string>();
        public List<string> UserRequests { get; set; } = new List<string>();
        public List<string> KeyFindings { get; set; } = new List<string>();
        public List<string> ToolsUsed { get; set; } = new List<string>();
        public string ProgressSummary { get; set; }

        /// <summary>
        /// Render as structured Markdown with explicit section headers.
        /// </summary>
        public string ToMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Conversation Summary (auto-generated)");
            sb.AppendLine();

            sb.AppendLine("### Files Read");
            if (FilesRead.Count > 0)
            {
                var deduped = FilesRead.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var f in deduped) sb.AppendLine($"- {f}");
            }
            else sb.AppendLine("- (none)");
            sb.AppendLine();

            if (FilesModified.Count > 0)
            {
                sb.AppendLine("### Files Modified");
                foreach (var f in FilesModified) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }

            if (FilesCreated.Count > 0)
            {
                sb.AppendLine("### Files Created");
                foreach (var f in FilesCreated) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }

            if (SearchesPerformed.Count > 0)
            {
                sb.AppendLine("### Searches");
                var recent = SearchesPerformed.Count > 10
                    ? SearchesPerformed.GetRange(SearchesPerformed.Count - 10, 10)
                    : SearchesPerformed;
                foreach (var s in recent) sb.AppendLine($"- {s}");
                sb.AppendLine();
            }

            if (UserRequests.Count > 0)
            {
                sb.AppendLine("### User Requests");
                for (int i = 0; i < UserRequests.Count; i++)
                    sb.AppendLine($"{i + 1}. {UserRequests[i]}");
                sb.AppendLine();
            }

            if (KeyFindings.Count > 0)
            {
                sb.AppendLine("### Key Findings");
                var recent = KeyFindings.Count > 12
                    ? KeyFindings.GetRange(KeyFindings.Count - 12, 12)
                    : KeyFindings;
                foreach (var f in recent) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }

            if (ToolsUsed.Count > 0)
                sb.AppendLine($"### Tools Used: {string.Join(", ", ToolsUsed)}");

            if (!string.IsNullOrEmpty(ProgressSummary))
                sb.AppendLine($"### Progress: {ProgressSummary}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Manages token budget, conversation history compression, and condense summaries.
    /// Extracted from AgentExecutor to separate token management concerns.
    /// </summary>
    public static class TokenBudgetManager
    {
        /// <summary>
        /// Minimum total message count before proactive condense triggers.
        /// Raised from 10 to 18 after TC-13 showed premature condense caused context loss.
        /// Kept as floor for ComputeCondenseMessageThreshold.
        /// </summary>
        public static int MinCondenseMessageThreshold => Config.AicaConfig.Current.Condense.MinMessageThreshold;

        /// <summary>
        /// Minimum non-system messages before proactive condense triggers.
        /// Kept as floor for ComputeCondenseCompressibleThreshold.
        /// </summary>
        public static int MinCondenseCompressibleThreshold => Config.AicaConfig.Current.Condense.MinCompressibleThreshold;

        /// <summary>
        /// Compute message-count threshold for proactive condense based on token budget.
        /// Assumes ~1500 tokens per message on average (mix of tool calls, results, user messages).
        /// Uses 60% of estimated message capacity as the trigger point.
        /// Floors at MinCondenseMessageThreshold to avoid premature condense on small budgets.
        /// </summary>
        public static int ComputeCondenseMessageThreshold(int maxTokenBudget)
        {
            int estimatedCapacity = maxTokenBudget / 1500;
            int threshold = (int)(estimatedCapacity * 0.60);
            return Math.Max(MinCondenseMessageThreshold, threshold);
        }

        /// <summary>
        /// Compute compressible message threshold, proportional to message threshold (~67%).
        /// </summary>
        public static int ComputeCondenseCompressibleThreshold(int maxTokenBudget)
        {
            int msgThreshold = ComputeCondenseMessageThreshold(maxTokenBudget);
            return Math.Max(MinCondenseCompressibleThreshold, (int)(msgThreshold * 0.67));
        }

        /// <summary>
        /// Minimum new messages required between consecutive condense operations.
        /// Set to 40% of message threshold to avoid excessive condensation.
        /// </summary>
        public static int ComputeReCondenseGap(int maxTokenBudget)
        {
            int msgThreshold = ComputeCondenseMessageThreshold(maxTokenBudget);
            return Math.Max(8, (int)(msgThreshold * 0.40));
        }

        /// <summary>
        /// Build a condensed conversation history that preserves the original user request.
        /// Called by AgentExecutor when proactive condense triggers.
        /// </summary>
        public static List<ChatMessage> BuildCondensedHistory(
            List<ChatMessage> conversationHistory,
            string summary)
        {
            var condensed = new List<ChatMessage>();

            // Keep system prompt
            if (conversationHistory.Count > 0)
                condensed.Add(conversationHistory[0]);

            // Add condensed summary
            condensed.Add(ChatMessage.System(
                "[Conversation auto-condensed] The following is a summary of all previous work:\n" + summary));

            // Preserve the ORIGINAL user request (first non-system user message)
            string originalUserRequest = null;
            for (int i = 1; i < conversationHistory.Count; i++)
            {
                if (conversationHistory[i].Role == ChatRole.User
                    && !conversationHistory[i].Content.StartsWith("[System"))
                {
                    originalUserRequest = conversationHistory[i].Content;
                    condensed.Add(conversationHistory[i]);
                    break;
                }
            }

            // Also preserve the LATEST user message (if different from original)
            for (int i = conversationHistory.Count - 1; i >= 0; i--)
            {
                if (conversationHistory[i].Role == ChatRole.User
                    && conversationHistory[i].Content != originalUserRequest)
                {
                    condensed.Add(conversationHistory[i]);
                    break;
                }
            }

            // Post-condense instruction
            condensed.Add(ChatMessage.System(
                "[Post-condense instruction] The conversation was condensed to save context space. " +
                "You MUST continue working on the user's ORIGINAL request shown above. " +
                "Do NOT ask what the user wants — the task is already defined. " +
                "Do NOT start a new task or replay old tasks. " +
                "Continue executing from where you left off based on the summary."));

            return condensed;
        }

        /// <summary>
        /// Build a structured condense summary object from conversation history.
        /// Returns a CondenseSummary with typed sections for programmatic access.
        /// </summary>
        public static CondenseSummary BuildStructuredSummary(List<ChatMessage> conversationHistory)
        {
            var summary = new CondenseSummary();
            var filesReadRaw = new List<string>();

            for (int i = 0; i < conversationHistory.Count; i++)
            {
                var msg = conversationHistory[i];
                if (msg.Role == ChatRole.User && msg.Content != null
                    && !msg.Content.StartsWith("[System") && !msg.Content.StartsWith("[CONTEXT_PRESSURE")
                    && !msg.Content.StartsWith("⚠️") && !msg.Content.StartsWith("Context was condensed"))
                {
                    var text = msg.Content.Length > 300 ? msg.Content.Substring(0, 300) + "..." : msg.Content;
                    summary.UserRequests.Add(text);
                }
                else if (msg.Role == ChatRole.Assistant && msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var toolName = tc.Function?.Name ?? "unknown";
                        if (!summary.ToolsUsed.Contains(toolName))
                            summary.ToolsUsed.Add(toolName);

                        var argsJson = tc.Function?.Arguments;
                        if (!string.IsNullOrEmpty(argsJson))
                        {
                            var args = TryParseJsonArgs(argsJson);
                            if (args != null)
                            {
                                string filePath = null;
                                if (args.TryGetValue("path", out var pathVal) && pathVal != null)
                                    filePath = pathVal;
                                else if (args.TryGetValue("file_path", out var fpVal) && fpVal != null)
                                    filePath = fpVal;

                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    switch (toolName)
                                    {
                                        case "read_file": filesReadRaw.Add(filePath); break;
                                        case "edit": summary.FilesModified.Add(filePath); break;
                                    }
                                }

                                if (toolName == "grep_search" && args.TryGetValue("query", out var queryVal) && queryVal != null)
                                {
                                    var searchPath = args.TryGetValue("path", out var sp) && sp != null ? sp : "workspace";
                                    summary.SearchesPerformed.Add($"grep '{queryVal}' in {searchPath}");
                                }
                                if (toolName == "find_by_name" && args.TryGetValue("pattern", out var patVal) && patVal != null)
                                    summary.SearchesPerformed.Add($"find '{patVal}'");
                            }
                        }
                    }
                }
                else if (msg.Role == ChatRole.Tool && msg.Content != null && !msg.Content.StartsWith("Error:"))
                {
                    if (msg.Content.Length > 200)
                        summary.KeyFindings.Add(msg.Content.Substring(0, 200) + "...");
                    else if (msg.Content.Length > 20)
                        summary.KeyFindings.Add(msg.Content);
                }
            }

            summary.FilesRead = filesReadRaw.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            summary.ProgressSummary = $"{conversationHistory.Count} messages processed.";
            return summary;
        }

        /// <summary>
        /// Build a structured auto-condense summary from conversation history.
        /// Extracts file operations, searches, user requests, key findings, and tools used.
        /// </summary>
        public static string BuildAutoCondenseSummary(List<ChatMessage> conversationHistory)
        {
            // H6: Task boundary awareness [C81/D-02]
            // Find last [TASK_BOUNDARY] marker; condense pre-boundary content minimally
            int boundaryIndex = -1;
            for (int i = conversationHistory.Count - 1; i >= 0; i--)
            {
                if (conversationHistory[i].Role == ChatRole.System
                    && conversationHistory[i].Content != null
                    && conversationHistory[i].Content.Contains("[TASK_BOUNDARY]"))
                {
                    boundaryIndex = i;
                    break;
                }
            }

            if (boundaryIndex > 0)
            {
                // Split: minimal summary for pre-boundary, full summary for post-boundary
                var preBoundary = conversationHistory.GetRange(0, boundaryIndex);
                var postBoundary = conversationHistory.GetRange(boundaryIndex, conversationHistory.Count - boundaryIndex);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("## Previous Tasks (condensed)");
                sb.AppendLine(BuildMinimalSummary(preBoundary));
                sb.AppendLine();
                sb.AppendLine("## Current Task");
                sb.AppendLine(BuildFullSummary(postBoundary));
                return sb.ToString();
            }

            // No boundary found — use full summary for everything
            return BuildFullSummary(conversationHistory);
        }

        /// <summary>
        /// Build a minimal summary for previous task content (pre-boundary).
        /// Only retains user requests and file lists, omits tool details. [C81/D-02]
        /// </summary>
        private static string BuildMinimalSummary(List<ChatMessage> messages)
        {
            var sb = new System.Text.StringBuilder();
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var msg in messages)
            {
                if (msg.Role == ChatRole.User && msg.Content != null
                    && !msg.Content.StartsWith("[System") && !msg.Content.StartsWith("⚠️"))
                {
                    var text = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
                    sb.AppendLine($"- Request: {text}");
                }
                else if (msg.Role == ChatRole.Assistant && msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var argsJson = tc.Function?.Arguments;
                        if (!string.IsNullOrEmpty(argsJson))
                        {
                            var args = TryParseJsonArgs(argsJson);
                            if (args != null)
                            {
                                if (args.TryGetValue("path", out var p) && p != null) files.Add(p);
                                else if (args.TryGetValue("file_path", out var fp) && fp != null) files.Add(fp);
                            }
                        }
                    }
                }
            }

            if (files.Count > 0)
                sb.AppendLine($"- Files touched: {string.Join(", ", files.Take(20))}");

            if (sb.Length == 0)
                sb.AppendLine("- (no significant operations)");

            return sb.ToString();
        }

        /// <summary>
        /// Build full structured summary (original logic, extracted as method). [C81/D-02]
        /// </summary>
        private static string BuildFullSummary(List<ChatMessage> conversationHistory)
        {
            var sb = new System.Text.StringBuilder();
            var filesRead = new List<string>();
            var filesCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filesModified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toolsUsed = new HashSet<string>();
            var userRequests = new List<string>();
            var keyFindings = new List<string>();
            var searchResults = new List<string>();

            // Pass 1: Extract structured info from tool calls and results
            for (int i = 0; i < conversationHistory.Count; i++)
            {
                var msg = conversationHistory[i];

                if (msg.Role == ChatRole.User && msg.Content != null
                    && !msg.Content.StartsWith("[System") && !msg.Content.StartsWith("[CONTEXT_PRESSURE")
                    && !msg.Content.StartsWith("⚠️") && !msg.Content.StartsWith("Context was condensed"))
                {
                    var text = msg.Content.Length > 300 ? msg.Content.Substring(0, 300) + "..." : msg.Content;
                    userRequests.Add(text);
                }
                else if (msg.Role == ChatRole.Assistant && msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var toolName = tc.Function?.Name ?? "unknown";
                        toolsUsed.Add(toolName);

                        // Extract file paths from tool call arguments (JSON string)
                        var argsJson = tc.Function?.Arguments;
                        if (!string.IsNullOrEmpty(argsJson))
                        {
                            var args = TryParseJsonArgs(argsJson);
                            if (args != null)
                            {
                                string filePath = null;
                                if (args.TryGetValue("path", out var pathVal) && pathVal != null)
                                    filePath = pathVal;
                                else if (args.TryGetValue("file_path", out var fpVal) && fpVal != null)
                                    filePath = fpVal;

                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    switch (toolName)
                                    {
                                        case "read_file":
                                            filesRead.Add(filePath);
                                            break;
                                        case "edit":
                                            filesModified.Add(filePath);
                                            break;
                                    }
                                }

                                // Extract search queries for context
                                if (toolName == "grep_search" && args.TryGetValue("query", out var queryVal) && queryVal != null)
                                {
                                    var searchPath = args.TryGetValue("path", out var sp) && sp != null ? sp : "workspace";
                                    searchResults.Add($"grep '{queryVal}' in {searchPath}");
                                }
                                if (toolName == "find_by_name" && args.TryGetValue("pattern", out var patVal) && patVal != null)
                                {
                                    searchResults.Add($"find '{patVal}'");
                                }
                            }
                        }
                    }
                }
                else if (msg.Role == ChatRole.Tool && msg.Content != null && !msg.Content.StartsWith("Error:"))
                {
                    // Extract meaningful findings from successful tool results (keep more context)
                    if (msg.Content.Length > 200)
                    {
                        keyFindings.Add(msg.Content.Substring(0, 200) + "...");
                    }
                    else if (msg.Content.Length > 20)
                    {
                        keyFindings.Add(msg.Content);
                    }
                }
            }

            // Pass 2: Also scan assistant text for file references (fallback)
            foreach (var msg in conversationHistory)
            {
                if (msg.Role != ChatRole.Assistant || msg.Content == null) continue;

                var content = msg.Content;
                var pathMatches = Regex.Matches(
                    content, @"[\w/\\.-]+\.(?:cs|ts|js|py|json|xml|md|txt|cpp|h|hpp|java|go|sln|csproj|vcxproj)");
                foreach (Match m in pathMatches)
                {
                    // Only add paths not already captured from tool calls
                    var path = m.Value;
                    if (!filesRead.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))
                        && !filesModified.Contains(path) && !filesCreated.Contains(path))
                    {
                        filesRead.Add(path);
                    }
                }
            }

            // Build structured summary
            sb.AppendLine("## Conversation Summary (auto-generated)");
            sb.AppendLine();

            // Section 1: File operations (most important for P1-013)
            sb.AppendLine("### File Operations");
            if (filesRead.Count > 0)
            {
                var dedupedFilesRead = filesRead.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                sb.AppendLine($"- **Files read ({filesRead.Count} calls, {dedupedFilesRead.Count} unique):** {string.Join(", ", dedupedFilesRead)}");
            }
            if (filesCreated.Count > 0)
                sb.AppendLine($"- **Files created ({filesCreated.Count}):** {string.Join(", ", filesCreated)}");
            if (filesModified.Count > 0)
                sb.AppendLine($"- **Files modified ({filesModified.Count}):** {string.Join(", ", filesModified)}");
            if (filesRead.Count == 0 && filesCreated.Count == 0 && filesModified.Count == 0)
                sb.AppendLine("- (no file operations recorded)");
            sb.AppendLine();

            // Section 2: Searches performed
            if (searchResults.Count > 0)
            {
                sb.AppendLine("### Searches Performed");
                foreach (var sr in searchResults.Count > 10 ? searchResults.GetRange(searchResults.Count - 10, 10) : searchResults)
                    sb.AppendLine($"- {sr}");
                sb.AppendLine();
            }

            // Section 3: User requests (all of them, for context)
            if (userRequests.Count > 0)
            {
                sb.AppendLine("### User Requests (chronological)");
                for (int i = 0; i < userRequests.Count; i++)
                    sb.AppendLine($"{i + 1}. {userRequests[i]}");
                sb.AppendLine();
            }

            // Section 4: Key findings (more generous allocation)
            if (keyFindings.Count > 0)
            {
                sb.AppendLine("### Key Tool Results");
                var recent = keyFindings.Count > 12
                    ? keyFindings.GetRange(keyFindings.Count - 12, 12)
                    : keyFindings;
                foreach (var finding in recent)
                    sb.AppendLine($"- {finding}");
                sb.AppendLine();
            }

            // Section 5: Tools and progress
            if (toolsUsed.Count > 0)
                sb.AppendLine($"### Tools Used: {string.Join(", ", toolsUsed)}");

            sb.AppendLine($"### Progress: {conversationHistory.Count} messages processed.");

            return sb.ToString();
        }

        /// <summary>
        /// Extract a structured tool call history from conversation for condense augmentation.
        /// This ensures tool call records survive condense regardless of LLM summary quality.
        /// </summary>
        public static string ExtractToolCallHistory(List<ChatMessage> conversationHistory)
        {
            var toolCalls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int msgIdx = 0; msgIdx < conversationHistory.Count; msgIdx++)
            {
                var msg = conversationHistory[msgIdx];
                if (msg.Role != ChatRole.Assistant || msg.ToolCalls == null) continue;

                foreach (var tc in msg.ToolCalls)
                {
                    var toolName = tc.Function?.Name ?? "unknown";
                    var argsJson = tc.Function?.Arguments;
                    string summary = null;

                    if (!string.IsNullOrEmpty(argsJson))
                    {
                        var args = TryParseJsonArgs(argsJson);
                        if (args != null)
                        {
                            switch (toolName)
                            {
                                case "read_file":
                                    args.TryGetValue("path", out summary);
                                    if (summary == null) args.TryGetValue("file_path", out summary);
                                    break;
                                case "grep_search":
                                    if (args.TryGetValue("query", out var q))
                                    {
                                        var searchPath = args.TryGetValue("path", out var sp) ? sp : "workspace";
                                        summary = $"\"{q}\" in {searchPath}";
                                    }
                                    break;
                                case "find_by_name":
                                    if (args.TryGetValue("pattern", out var p))
                                        summary = $"\"{p}\"";
                                    break;
                                case "list_dir":
                                    args.TryGetValue("path", out summary);
                                    if (summary == null) summary = "workspace root";
                                    break;
                                case "edit":
                                    args.TryGetValue("path", out summary);
                                    if (summary == null) args.TryGetValue("file_path", out summary);
                                    break;
                                case "list_code_definition_names":
                                    args.TryGetValue("path", out summary);
                                    break;
                                case "list_projects":
                                    summary = "(solution)";
                                    break;
                                case "run_command":
                                    args.TryGetValue("command", out summary);
                                    if (summary != null && summary.Length > 60)
                                        summary = summary.Substring(0, 60) + "...";
                                    break;
                            }
                        }
                    }

                    // Look forward for the corresponding Tool result message to get a result hint
                    string resultHint = null;
                    if (tc.Id != null)
                    {
                        for (int j = msgIdx + 1; j < conversationHistory.Count; j++)
                        {
                            if (conversationHistory[j].Role == ChatRole.Tool
                                && conversationHistory[j].ToolCallId == tc.Id)
                            {
                                var resultContent = conversationHistory[j].Content ?? "";
                                if (resultContent.Length > 100)
                                    resultHint = resultContent.Substring(0, 80) + "...";
                                else if (resultContent.Length > 0)
                                    resultHint = resultContent;
                                break;
                            }
                        }
                    }

                    if (!toolCalls.ContainsKey(toolName))
                        toolCalls[toolName] = new List<string>();

                    var entry = summary ?? "(no args)";
                    if (resultHint != null)
                        entry = $"{entry} → ({resultHint})";

                    toolCalls[toolName].Add(entry);
                }
            }

            if (toolCalls.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Tool Call History (auto-extracted, factual)");

            int totalChars = 0;
            const int maxChars = 3000;

            foreach (var kvp in toolCalls)
            {
                var dedupedArgs = kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var line = $"### {kvp.Key} ({kvp.Value.Count} calls)";
                sb.AppendLine(line);
                totalChars += line.Length;

                foreach (var arg in dedupedArgs)
                {
                    if (totalChars > maxChars)
                    {
                        sb.AppendLine("- ... (truncated for space)");
                        break;
                    }
                    var argLine = $"- {arg}";
                    sb.AppendLine(argLine);
                    totalChars += argLine.Length;
                }

                if (totalChars > maxChars) break;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Try to parse a JSON arguments string into a simple string dictionary.
        /// Used by BuildAutoCondenseSummary and ExtractToolCallHistory to extract parameters.
        /// Returns null on parse failure.
        /// </summary>
        public static Dictionary<string, string> TryParseJsonArgs(string json)
        {
            try
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            result[prop.Name] = prop.Value.GetString();
                        }
                        else if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
                        {
                            result[prop.Name] = prop.Value.GetRawText();
                        }
                    }
                }
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
