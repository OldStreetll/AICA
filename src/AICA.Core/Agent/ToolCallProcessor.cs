using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Handles tool call parsing (text fallback), parameter augmentation,
    /// dedup signature generation, and action descriptions.
    /// Extracted from AgentExecutor to separate tool-call processing concerns.
    /// </summary>
    public static class ToolCallProcessor
    {
        /// <summary>
        /// Auto-add missing parameters when user intent is clear but LLM omitted them.
        /// For example, auto-add recursive=true to list_dir when user asks for "完整结构".
        /// </summary>
        public static void AugmentToolCallParameters(List<ToolCall> toolCalls, string userRequest)
        {
            if (string.IsNullOrEmpty(userRequest)) return;

            var lower = userRequest.ToLowerInvariant();

            foreach (var tc in toolCalls)
            {
                if (tc.Name == "list_dir" && tc.Arguments != null)
                {
                    bool hasRecursive = tc.Arguments.ContainsKey("recursive");
                    if (!hasRecursive)
                    {
                        var recursiveKeywords = new[] { "完整", "全部", "递归", "目录树", "结构", "树形", "所有",
                            "full", "complete", "recursive", "tree", "entire", "all" };

                        foreach (var kw in recursiveKeywords)
                        {
                            if (lower.Contains(kw))
                            {
                                tc.Arguments["recursive"] = "true";
                                System.Diagnostics.Debug.WriteLine($"[AICA] Auto-augmented list_dir with recursive=true (matched '{kw}')");
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Parse tool calls from LLM text output (fallback when function calling fails).
        /// Supports 3 patterns: function tags, minimax format, and JSON blocks.
        /// </summary>
        public static List<ToolCall> TryParseTextToolCalls(string text)
        {
            var result = new List<ToolCall>();

            // Pattern 1: <function=NAME> <parameter=KEY> VALUE ... </tool_call>
            var funcPattern = new Regex(
                @"<function=(\w+)>(.*?)</tool_call>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in funcPattern.Matches(text))
            {
                var funcName = match.Groups[1].Value.Trim();
                var body = match.Groups[2].Value.Trim();

                var args = new Dictionary<string, object>();
                var paramPattern = new Regex(
                    @"<parameter=(\w+)>\s*(.*?)(?=<parameter=|$)",
                    RegexOptions.Singleline);

                foreach (Match paramMatch in paramPattern.Matches(body))
                {
                    var key = paramMatch.Groups[1].Value.Trim();
                    var value = SanitizeParameterValue(paramMatch.Groups[2].Value);
                    args[key] = value;
                }

                if (!string.IsNullOrEmpty(funcName) && args.Count > 0)
                {
                    result.Add(new ToolCall
                    {
                        Id = "text_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = funcName,
                        Arguments = args
                    });
                }
            }

            // Pattern 2: minimax:tool_call format
            if (result.Count == 0)
            {
                var minimaxPattern = new Regex(
                    @"minimax:tool_call\s+(.*?)\s*</minimax:tool_call>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match match in minimaxPattern.Matches(text))
                {
                    var content = match.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) continue;

                    string toolName = null;
                    var args = new Dictionary<string, object>();
                    var firstLine = lines[0].Trim();

                    // Infer tool name from content
                    if (firstLine.StartsWith("dotnet ") || firstLine.StartsWith("msbuild ") ||
                        firstLine.StartsWith("git ") || firstLine.StartsWith("npm ") ||
                        firstLine.StartsWith("make ") || firstLine.Contains(".exe") ||
                        firstLine.Contains("&&") || firstLine.Contains("|"))
                    {
                        toolName = "run_command";
                        args["command"] = firstLine;
                        if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out var timeout))
                            args["timeout"] = timeout;
                    }
                    else if (firstLine.Contains("/") || firstLine.Contains("\\") || firstLine.Contains("."))
                    {
                        if (firstLine.EndsWith(".cs") || firstLine.EndsWith(".txt") ||
                            firstLine.EndsWith(".json") || firstLine.EndsWith(".xml") ||
                            firstLine.EndsWith(".md") || firstLine.EndsWith(".cpp") ||
                            firstLine.EndsWith(".h") || firstLine.EndsWith(".py") ||
                            firstLine.EndsWith(".js") || firstLine.EndsWith(".ts") ||
                            firstLine.EndsWith(".java") || firstLine.EndsWith(".go"))
                        {
                            toolName = "read_file";
                            args["path"] = firstLine;
                            if (lines.Length > 1 && int.TryParse(lines[1].Trim(), out var offset))
                                args["offset"] = offset;
                            if (lines.Length > 2 && int.TryParse(lines[2].Trim(), out var limit))
                                args["limit"] = limit;
                        }
                        else
                        {
                            toolName = "list_dir";
                            args["path"] = firstLine;
                        }
                    }
                    else
                    {
                        toolName = "grep_search";
                        args["query"] = firstLine;
                        if (lines.Length > 1) args["path"] = lines[1].Trim();
                        if (lines.Length > 2) args["includes"] = lines[2].Trim();
                    }

                    if (!string.IsNullOrEmpty(toolName) && args.Count > 0)
                    {
                        result.Add(new ToolCall
                        {
                            Id = "text_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            Name = toolName,
                            Arguments = args
                        });
                    }
                }
            }

            // Pattern 3: JSON-style tool calls in text
            if (result.Count == 0)
            {
                var jsonBlocks = ExtractBalancedJsonBlocks(text);
                foreach (var block in jsonBlocks)
                {
                    try
                    {
                        using (var doc = System.Text.Json.JsonDocument.Parse(block))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("name", out var nameProp))
                            {
                                var name = nameProp.GetString();
                                var args = new Dictionary<string, object>();

                                if (root.TryGetProperty("arguments", out var argsProp))
                                {
                                    args = System.Text.Json.JsonSerializer
                                        .Deserialize<Dictionary<string, object>>(
                                            argsProp.GetRawText());
                                }

                                if (!string.IsNullOrEmpty(name))
                                {
                                    result.Add(new ToolCall
                                    {
                                        Id = "text_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                        Name = name,
                                        Arguments = args ?? new Dictionary<string, object>()
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Failed to parse JSON tool call block: {ex.Message}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Extract top-level balanced JSON objects from text.
        /// </summary>
        public static List<string> ExtractBalancedJsonBlocks(string text)
        {
            var blocks = new List<string>();
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == '{')
                {
                    var depth = 0;
                    var start = i;
                    var inString = false;
                    var escape = false;

                    for (; i < text.Length; i++)
                    {
                        if (escape) { escape = false; continue; }
                        if (text[i] == '\\' && inString) { escape = true; continue; }
                        if (text[i] == '"') { inString = !inString; continue; }
                        if (inString) continue;
                        if (text[i] == '{') depth++;
                        else if (text[i] == '}') { depth--; if (depth == 0) break; }
                    }

                    if (depth == 0 && i < text.Length)
                    {
                        var block = text.Substring(start, i - start + 1);
                        if (block.Contains("\"name\"") && block.Contains("\"arguments\""))
                            blocks.Add(block);
                    }
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return blocks;
        }

        /// <summary>
        /// Remove text-based tool call syntax from response.
        /// </summary>
        public static string RemoveTextToolCallSyntax(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = Regex.Replace(text, @"<function=\w+>.*?</tool_call>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"minimax:tool_call\s+.*?\s*</minimax:tool_call>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<minimax:tool_call>.*?</minimax:tool_call>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\n{3,}", "\n\n", RegexOptions.None);
            text = text.Trim();

            return text;
        }

        /// <summary>
        /// Clean up parameter values extracted from text-based tool calls.
        /// </summary>
        public static string SanitizeParameterValue(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var cleaned = Regex.Replace(raw, @"<[^>]+>", " ");
            cleaned = Regex.Replace(cleaned, @"[\x00-\x1F\x7F]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
            cleaned = cleaned.Trim();

            if (cleaned.Length >= 2 &&
                ((cleaned[0] == '"' && cleaned[cleaned.Length - 1] == '"') ||
                 (cleaned[0] == '\'' && cleaned[cleaned.Length - 1] == '\'')))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }

            return cleaned;
        }

        /// <summary>
        /// Determine whether a failed tool call should be removed from the dedup set (allowing retry).
        /// Security denials are permanent and should NOT be retried.
        /// </summary>
        public static bool ShouldAllowRetry(ToolResult result)
        {
            if (result.Success) return false; // not a failure
            return result.FailureKind != ToolResultFailureKind.SecurityDenied;
        }

        /// <summary>
        /// SEC-01: Normalize a file path for security blacklist comparison.
        /// Resolves relative segments (./ ../) and normalizes case + separators.
        /// </summary>
        public static string NormalizeSecurityPath(string path, string basePath = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                // Resolve relative path against basePath if provided
                var fullPath = string.IsNullOrEmpty(basePath)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(basePath, path));

                // Normalize to forward slashes and lowercase for consistent comparison
                return fullPath.Replace('\\', '/').ToLowerInvariant();
            }
            catch
            {
                // Fallback: basic normalization without filesystem resolution
                return path.Replace('\\', '/').ToLowerInvariant().TrimEnd('/');
            }
        }

        /// <summary>
        /// SEC-01: Check if a tool call targets a blacklisted (security-denied) path.
        /// </summary>
        public static bool IsPathBlacklisted(ToolCall call, HashSet<string> securityBlacklist, string basePath = null)
        {
            if (securityBlacklist == null || securityBlacklist.Count == 0)
                return false;

            var path = ExtractPathFromToolCall(call);
            if (string.IsNullOrEmpty(path))
                return false;

            var normalized = NormalizeSecurityPath(path, basePath);
            return securityBlacklist.Contains(normalized);
        }

        /// <summary>
        /// SEC-01: Add the path from a security-denied tool call to the blacklist.
        /// </summary>
        public static void AddToSecurityBlacklist(ToolCall call, HashSet<string> securityBlacklist, string basePath = null)
        {
            if (securityBlacklist == null) return;

            var path = ExtractPathFromToolCall(call);
            if (string.IsNullOrEmpty(path)) return;

            var normalized = NormalizeSecurityPath(path, basePath);
            if (!string.IsNullOrEmpty(normalized))
            {
                securityBlacklist.Add(normalized);
                System.Diagnostics.Debug.WriteLine($"[AICA] SEC-01: Added to security blacklist: {normalized}");
            }
        }

        /// <summary>
        /// Generate a stable signature for a tool call (name + sorted args) for dedup.
        /// </summary>
        public static string GetToolCallSignature(ToolCall call)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(call.Name ?? "");
            if (call.Arguments != null)
            {
                var sortedKeys = new List<string>(call.Arguments.Keys);
                sortedKeys.Sort(StringComparer.Ordinal);
                foreach (var key in sortedKeys)
                {
                    // v2.1 O12: offset/limit now included in signature so different chunks produce different signatures
                    // (was: if read_file, skip offset/limit → all chunks had same signature → false "Duplicate call")
                    if (call.Name == "grep_search" && key == "max_results") continue;

                    sb.Append('|').Append(key).Append('=');
                    var val = call.Arguments[key];
                    var strVal = val?.ToString() ?? "";
                    if (key == "path" || key == "file_path" || key == "directory")
                        strVal = strVal.TrimEnd('/', '\\').ToLowerInvariant();
                    else if (key == "query" || key == "pattern" || key == "name" || key == "command")
                        strVal = strVal.Trim().ToLowerInvariant();
                    sb.Append(strVal);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extract the file path from a tool call's arguments.
        /// </summary>
        public static string ExtractPathFromToolCall(ToolCall toolCall)
        {
            if (toolCall.Arguments == null) return null;
            if (toolCall.Arguments.TryGetValue("path", out var path)) return path?.ToString();
            if (toolCall.Arguments.TryGetValue("file_path", out var filePath)) return filePath?.ToString();
            return null;
        }

        /// <summary>
        /// Build a human-readable action description from a tool call for UI display.
        /// </summary>
        public static string BuildActionDescription(ToolCall toolCall)
        {
            var name = toolCall.Name?.ToLowerInvariant() ?? "";
            var args = toolCall.Arguments;

            string target = GetFirstArgValue(args, "path", "file_path", "directory");
            string query = GetFirstArgValue(args, "pattern", "query", "search_term", "name");
            string command = GetFirstArgValue(args, "command");

            if (name.Contains("read")) return $"📖 正在读取 {Shorten(target)}...";
            if (name.Contains("write") || name.Contains("create")) return $"✏️ 正在写入 {Shorten(target)}...";
            if (name.Contains("edit")) return $"📝 正在编辑 {Shorten(target)}...";
            if (name.Contains("grep") || name.Contains("search")) return $"🔍 正在搜索 {Shorten(query)}...";
            if (name.Contains("list_dir")) return $"📂 正在列出 {Shorten(target ?? ".")} 目录...";
            if (name.Contains("find")) return $"🔍 正在查找 {Shorten(query)}...";
            if (name.Contains("command") || name.Contains("run")) return $"⚡ 正在执行命令 {Shorten(command)}...";
            if (name.Contains("list_project")) return "📁 正在列出项目信息...";
            if (name.Contains("list_code")) return "📋 正在分析代码定义...";
            if (name.Contains("condense")) return "📝 正在压缩上下文...";
            if (name.Contains("attempt_completion")) return "✅ 正在完成任务...";
            if (name.Contains("ask_followup")) return "❓ 正在向用户提问...";

            if (name.Contains("log_analysis")) return "📊 正在分析日志...";
            return $"🔧 正在执行 {toolCall.Name}...";
        }

        /// <summary>
        /// Get first non-null argument value from a set of candidate keys.
        /// </summary>
        public static string GetFirstArgValue(Dictionary<string, object> args, params string[] keys)
        {
            if (args == null) return null;
            foreach (var key in keys)
            {
                if (args.TryGetValue(key, out var val) && val != null)
                {
                    var s = val.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return null;
        }

        /// <summary>
        /// Shorten text for display purposes.
        /// </summary>
        public static string Shorten(string text, int maxLen = 60)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", "").Replace("\n", " ").Trim();
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
