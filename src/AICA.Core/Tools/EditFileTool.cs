using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for making precise edits to existing files
    /// </summary>
    public class EditFileTool : IAgentTool
    {
        private string _lastMatchLevel;

        public string Name => "edit";
        public string Description =>
            "Replace specific text in an existing file. old_string must match exactly and be unique. " +
            "Use read_file first to see exact content. " +
            "Do NOT use this to create new files — use write_file instead.";

        public ToolMetadata GetMetadata()
        {
            return new ToolMetadata
            {
                Name = Name,
                Description = Description,
                Category = ToolCategory.FileWrite,
                RequiresConfirmation = true,
                RequiresApproval = false,
                TimeoutSeconds = 15,
                IsModifying = true,
                Tags = new[] { "file", "edit", "modify" }
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
                        ["file_path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The path to the file to edit"
                        },
                        ["old_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The exact text to replace. MUST be unique in the file."
                        },
                        ["new_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "The new text to replace old_string with"
                        },
                        ["replace_all"] = new ToolParameterProperty
                        {
                            Type = "boolean",
                            Description = "If true, replace all occurrences. Default is false.",
                            Default = false
                        }
                        // v2.1 O1: full_replace removed — use write_file for creating new files
                    },
                    Required = new[] { "file_path", "old_string", "new_string" }
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                // Validate required parameters
                var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "file_path");
                var newString = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "new_string");
                var replaceAll = ToolParameterValidator.GetOptionalParameter<bool>(call.Arguments, "replace_all", false);

                // v2.1 O1: full_replace removed — redirect LLM to write_file
                var fullReplace = ToolParameterValidator.GetOptionalParameter<bool>(call.Arguments, "full_replace", false);
                if (fullReplace)
                {
                    return ToolResult.Fail(
                        "full_replace is no longer supported by 'edit'. " +
                        "To create a new file or overwrite an entire file, use the 'write_file' tool instead.");
                }

                var oldString = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "old_string");

                // Validate path access
                if (!context.IsPathAccessible(path))
                    return ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path));

                // Check file exists
                if (!await context.FileExistsAsync(path, ct))
                {
                    return ToolErrorHandler.HandleError(ToolErrorHandler.NotFound(path));
                }

                // v2.1 T6: Check for external modifications before editing
                if (FileTimeTracker.Instance.HasExternalModification(path))
                {
                    return ToolResult.Fail(
                        $"⚠️ File '{path}' has been modified externally since last read.\n" +
                        "Use read_file to get the latest content before editing.");
                }

                // Read current content
                var content = await context.ReadFileAsync(path, ct);

                string newContent;

                {
                    // Normal edit mode: require old_string matching

                    // Detect original line ending style to preserve it after edit [D-09]
                    var originalLineEnding = content.Contains("\r\n") ? "\r\n" : "\n";

                    // Normalize line endings for matching (compare in \n space)
                    var normalizedContent = NormalizeLineEndings(content);
                    var normalizedOldString = NormalizeLineEndings(oldString);
                    var normalizedNewString = NormalizeLineEndings(newString);

                    // v2.1 T5: Cascading fuzzy match (Level 0-3) before H3 diagnostic fallback
                    var cascadeResult = FindWithCascade(normalizedContent, normalizedOldString);
                    if (cascadeResult == null)
                    {
                        // All levels failed → fall back to H3 diagnostic (StaleContent + NoMatch hints)
                        var diagnosis = DiagnoseEditFailure(
                            normalizedContent, normalizedOldString, path, context);
                        return ToolResult.Fail(diagnosis.Message);
                    }

                    // Record match level for telemetry (fuzzy match distribution)
                    _lastMatchLevel = cascadeResult.Value.Level.ToString();
                    if (cascadeResult.Value.Level != MatchLevel.Exact)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] EditFileTool: fuzzy match Level={cascadeResult.Value.Level} for {path}");
                    }

                    if (cascadeResult.Value.Level == MatchLevel.Exact)
                    {
                        // Exact match — use standard Contains path (handles replace_all + uniqueness check)
                        // fall through to existing logic below
                    }
                    else
                    {
                        // Fuzzy match — replace the actual matched segment
                        var actualSegment = normalizedContent.Substring(
                            cascadeResult.Value.MatchIndex, cascadeResult.Value.MatchLength);
                        newContent = replaceAll
                            ? normalizedContent.Replace(actualSegment, normalizedNewString)
                            : ReplaceFirst(normalizedContent, actualSegment, normalizedNewString);
                        // Restore original line endings [D-09]
                        if (originalLineEnding == "\r\n")
                            newContent = newContent.Replace("\n", "\r\n");
                        goto applyEdit;
                    }

                    // Level 0 exact match: check old_string exists
                    if (!normalizedContent.Contains(normalizedOldString))
                    {
                        // Should not reach here (FindWithCascade Level 0 would have caught it)
                        return ToolResult.Fail("old_string not found in file. Use read_file to see exact content.");
                    }

                    // Check uniqueness (unless replace_all)
                    if (!replaceAll)
                    {
                        var firstIndex = normalizedContent.IndexOf(normalizedOldString);
                        var lastIndex = normalizedContent.LastIndexOf(normalizedOldString);
                        if (firstIndex != lastIndex)
                        {
                            return ToolResult.Fail("old_string is not unique in the file. Provide more context to make it unique, or use replace_all=true.");
                        }
                    }

                    // Check if old_string equals new_string
                    if (NormalizeLineEndings(oldString) == NormalizeLineEndings(newString))
                        return ToolResult.Fail("old_string and new_string are identical. This is a no-op.");

                    // Apply the edit in normalized space
                    newContent = replaceAll
                        ? normalizedContent.Replace(normalizedOldString, normalizedNewString)
                        : ReplaceFirst(normalizedContent, normalizedOldString, normalizedNewString);

                    // Restore original line endings [D-09]
                    if (originalLineEnding == "\r\n")
                        newContent = newContent.Replace("\n", "\r\n");
                }

                // Show diff and let user apply changes
                applyEdit:
                var result = await context.ShowDiffAndApplyAsync(path, content, newContent, ct);

                if (!result.Applied)
                {
                    var currentContent = await context.ReadFileAsync(path, ct);
                    return ToolResult.Ok(
                        $"EDIT CANCELLED BY USER - NO CHANGES WERE APPLIED\n\n" +
                        $"File: {path}\n\n" +
                        $"The user chose not to apply the proposed edit. Respect this decision and do NOT retry the same edit automatically unless the user explicitly asks you to try again.\n\n" +
                        $"CURRENT FILE CONTENT (unchanged after cancellation):\n{currentContent}\n\n" +
                        $"Next step: Explain that the edit was cancelled, analyze the current file state if helpful, and continue the task based on the unchanged file content."
                    );
                }

                // v2.1 T6: Record edit for conflict detection
                FileTimeTracker.Instance.RecordEdit(path);

                // Read the actual saved content (user may have modified it in the diff view)
                var finalContent = await context.ReadFileAsync(path, ct);

                // Check if user modified the content
                bool wasModifiedByUser = finalContent != newContent;

                if (wasModifiedByUser)
                {
                    // Calculate actual changes
                    var originalLines = content.Split('\n').Length;
                    var finalLines = finalContent.Split('\n').Length;
                    var lineDiff = finalLines - originalLines;
                    var diffText = lineDiff > 0 ? $"+{lineDiff}" : lineDiff < 0 ? $"{lineDiff}" : "0";

                    // Include the actual final content in the result so AI can see what was actually applied
                    return ToolResult.Ok($"⚠️ USER MANUALLY EDITED THE FILE - YOUR SUGGESTION WAS NOT USED ⚠️\n\nFile: {path}\nOriginal: {originalLines} lines → User's version: {finalLines} lines ({diffText})\n\n📄 ACTUAL FILE CONTENT (as saved by user):\n{finalContent}\n\n⚠️ CRITICAL: You MUST read and analyze the actual content above. Do NOT describe your original suggestion. Describe what the user actually saved.");
                }
                else
                {
                    var occurrences = replaceAll ? CountOccurrences(content, oldString) : 1;
                    var editResult = ToolResult.Ok($"File edited: {path} ({occurrences} replacement(s) made)");
                    if (!string.IsNullOrEmpty(_lastMatchLevel))
                        editResult.Metadata = new Dictionary<string, string> { ["fuzzy_match_level"] = _lastMatchLevel };
                    return editResult;
                }
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "edit");
                return ToolErrorHandler.HandleError(error);
            }
        }

        /// <summary>
        /// Normalize line endings to Unix format (\n) to handle cross-platform differences
        /// </summary>
        private string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Convert all line endings to \n
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private string ReplaceFirst(string text, string oldValue, string newValue)
        {
            var index = text.IndexOf(oldValue);
            if (index < 0) return text;
            return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        #region v2.1 T5: Cascading Fuzzy Match

        /// <summary>
        /// Match level used by FindWithCascade.
        /// </summary>
        private enum MatchLevel
        {
            Exact,                  // Level 0: exact string match
            LineTrimmed,            // Level 1: line-end whitespace ignored (NEW in v2.1)
            IndentationFlexible,    // Level 2: leading whitespace ignored (extracted from H3)
            WhitespaceNormalized    // Level 3: all whitespace compressed (extracted from H3)
        }

        private struct CascadeMatch
        {
            public int MatchIndex;
            public int MatchLength;
            public MatchLevel Level;
        }

        /// <summary>
        /// Try to find old_string in content using cascading match strategies.
        /// Returns null if all levels fail — caller should fall back to H3 diagnostic.
        /// All levels require a unique match (reject if multiple candidates).
        /// </summary>
        private CascadeMatch? FindWithCascade(string content, string oldString)
        {
            // Level 0: Exact match
            var exactIndex = content.IndexOf(oldString, StringComparison.Ordinal);
            if (exactIndex >= 0)
            {
                return new CascadeMatch { MatchIndex = exactIndex, MatchLength = oldString.Length, Level = MatchLevel.Exact };
            }

            // Level 1: Line-end whitespace ignored (TrimEnd per line)
            var level1Result = FindLineTrimmed(content, oldString);
            if (level1Result != null)
                return level1Result;

            // Level 2: Indentation-flexible (TrimStart per line, from H3)
            var trimmedOld = TrimEachLine(oldString);
            var trimmedContent = TrimEachLine(content);
            var trimmedIndex = trimmedContent.IndexOf(trimmedOld);
            if (trimmedIndex >= 0 && trimmedOld.Length > 0)
            {
                var segment = LocateOriginalSegment(content, trimmedContent, trimmedIndex, trimmedOld);
                if (segment != null)
                {
                    var segIndex = content.IndexOf(segment, StringComparison.Ordinal);
                    if (segIndex >= 0)
                        return new CascadeMatch { MatchIndex = segIndex, MatchLength = segment.Length, Level = MatchLevel.IndentationFlexible };
                }
            }

            // Level 3: Whitespace-normalized (compress runs, from H3)
            var compOld = CompressWhitespace(oldString);
            var compContent = CompressWhitespace(content);
            var compIndex = compContent.IndexOf(compOld);
            if (compIndex >= 0 && compOld.Length > 1)
            {
                var segment = LocateSegmentByCompressed(content, compContent, compIndex, compOld);
                if (segment != null)
                {
                    var segIndex = content.IndexOf(segment, StringComparison.Ordinal);
                    if (segIndex >= 0)
                        return new CascadeMatch { MatchIndex = segIndex, MatchLength = segment.Length, Level = MatchLevel.WhitespaceNormalized };
                }
            }

            return null; // All levels failed
        }

        /// <summary>
        /// Level 1: Match after trimming trailing whitespace from each line.
        /// New in v2.1 — covers LLM-generated old_string with trailing spaces.
        /// </summary>
        private CascadeMatch? FindLineTrimmed(string content, string oldString)
        {
            var trimmedOld = TrimEndEachLine(oldString);
            var trimmedContent = TrimEndEachLine(content);
            var index = trimmedContent.IndexOf(trimmedOld, StringComparison.Ordinal);
            if (index < 0 || trimmedOld.Length == 0)
                return null;

            // Check uniqueness
            var lastIndex = trimmedContent.LastIndexOf(trimmedOld, StringComparison.Ordinal);
            if (index != lastIndex)
                return null; // Multiple matches — reject

            // Map back to original content using line positions
            var segment = LocateOriginalSegment(content, trimmedContent, index, trimmedOld);
            if (segment == null) return null;

            var segIndex = content.IndexOf(segment, StringComparison.Ordinal);
            if (segIndex < 0) return null;

            return new CascadeMatch { MatchIndex = segIndex, MatchLength = segment.Length, Level = MatchLevel.LineTrimmed };
        }

        /// <summary>
        /// TrimEnd each line (preserve leading whitespace, remove trailing spaces/tabs).
        /// </summary>
        private static string TrimEndEachLine(string text)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimEnd(' ', '\t');
            return string.Join("\n", lines);
        }

        #endregion

        #region H3 Edit Diagnostic Routing (StaleContent + NoMatch only)

        /// <summary>
        /// Diagnose why old_string was not found. Called only after FindWithCascade fails all levels.
        /// v2.1: IndentationMismatch and WhitespaceMismatch auto-fix moved to FindWithCascade.
        /// </summary>
        private EditDiagnosis DiagnoseEditFailure(
            string content, string normalizedOld, string filePath, IAgentContext context)
        {
            // 1. StaleContent: file was edited earlier in this session
            if (context.EditedFilesInSession.Contains(filePath))
            {
                var firstLine = normalizedOld.Split('\n')[0].Trim();
                if (firstLine.Length > 5)
                {
                    var anchorIndex = content.IndexOf(firstLine);
                    if (anchorIndex >= 0)
                    {
                        var snippet = ExtractSnippet(content, anchorIndex,
                            normalizedOld.Split('\n').Length + 4);
                        return EditDiagnosis.Stale(
                            "该文件已在本会话中被编辑过，old_string 可能基于旧版本。\n" +
                            "以下是当前文件中与 old_string 首行匹配位置的实际内容：\n\n" +
                            snippet + "\n\n" +
                            "请基于以上最新内容重新构造 old_string。");
                    }
                }
            }

            // 2. NoMatch: try to find the first line as a hint
            var searchLine = normalizedOld.Split('\n')[0].Trim();
            if (searchLine.Length > 5)
            {
                var nearIndex = content.IndexOf(searchLine);
                if (nearIndex >= 0)
                {
                    var snippet = ExtractSnippet(content, nearIndex,
                        normalizedOld.Split('\n').Length + 4);
                    return EditDiagnosis.NoMatch(
                        "old_string 未找到精确匹配。\n" +
                        "找到首行相似位置的实际内容：\n\n" +
                        snippet + "\n\n" +
                        "请基于以上内容重新构造 old_string。");
                }
            }

            // Complete miss — generic error
            return EditDiagnosis.NoMatch(
                "old_string not found in file.\n" +
                "Use read_file to see the exact content, then try again with the correct string.");
        }

        /// <summary>
        /// Join each line after TrimStart for indentation-insensitive comparison.
        /// </summary>
        private static string TrimEachLine(string text)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimStart();
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Compress runs of whitespace (spaces/tabs) into a single space for fuzzy matching.
        /// Preserves newlines as-is.
        /// </summary>
        private static string CompressWhitespace(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool inSpace = false;
            foreach (var c in text)
            {
                if (c == ' ' || c == '\t')
                {
                    if (!inSpace) { sb.Append(' '); inSpace = true; }
                }
                else
                {
                    sb.Append(c);
                    inSpace = false;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Given a match index in the trimmed version, locate the corresponding segment
        /// in the original content by mapping line-by-line.
        /// </summary>
        private static string LocateOriginalSegment(
            string original, string trimmed, int trimmedMatchIndex, string trimmedOld)
        {
            // Count which line in the trimmed content the match starts at
            int trimmedLineStart = 0;
            for (int i = 0; i < trimmedMatchIndex; i++)
            {
                if (trimmed[i] == '\n') trimmedLineStart++;
            }

            // Count how many lines the trimmed old_string spans
            int oldLineCount = 1;
            foreach (var c in trimmedOld)
            {
                if (c == '\n') oldLineCount++;
            }

            // Extract the same line range from the original content
            var originalLines = original.Split('\n');
            if (trimmedLineStart + oldLineCount > originalLines.Length)
                return null;

            var segment = string.Join("\n",
                originalLines, trimmedLineStart, oldLineCount);
            return segment;
        }

        /// <summary>
        /// Locate original segment from a match in the compressed version.
        /// Uses character position mapping between compressed and original.
        /// </summary>
        private static string LocateSegmentByCompressed(
            string original, string compressed, int compMatchIndex, string compOld)
        {
            // Map compressed char index back to original char index
            int origStart = MapCompressedToOriginal(original, compMatchIndex);
            int origEnd = MapCompressedToOriginal(original, compMatchIndex + compOld.Length);

            if (origStart < 0 || origEnd < 0 || origEnd > original.Length)
                return null;

            return original.Substring(origStart, origEnd - origStart);
        }

        /// <summary>
        /// Map a character index in the compressed string back to the original string.
        /// </summary>
        private static int MapCompressedToOriginal(string original, int compressedIndex)
        {
            int compIdx = 0;
            bool inSpace = false;
            for (int i = 0; i < original.Length; i++)
            {
                if (compIdx >= compressedIndex)
                    return i;

                var c = original[i];
                if (c == ' ' || c == '\t')
                {
                    if (!inSpace) { compIdx++; inSpace = true; }
                }
                else
                {
                    compIdx++;
                    inSpace = false;
                }
            }
            // End of original
            return compIdx >= compressedIndex ? original.Length : -1;
        }

        /// <summary>
        /// Extract N lines of context starting from a character index.
        /// </summary>
        private static string ExtractSnippet(string content, int charIndex, int lineCount)
        {
            // Find the start of the line containing charIndex
            int lineStart = content.LastIndexOf('\n', Math.Max(0, charIndex - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            // Collect lineCount lines from lineStart
            int pos = lineStart;
            int linesFound = 0;
            while (pos < content.Length && linesFound < lineCount)
            {
                var nextNewline = content.IndexOf('\n', pos);
                if (nextNewline < 0)
                {
                    linesFound++;
                    break;
                }
                pos = nextNewline + 1;
                linesFound++;
            }

            var end = Math.Min(pos, content.Length);
            return content.Substring(lineStart, end - lineStart).TrimEnd('\n');
        }

        private enum EditDiagnosisKind
        {
            StaleContent,
            NoMatch
            // v2.1: IndentationMismatch and WhitespaceMismatch moved to FindWithCascade
        }

        private sealed class EditDiagnosis
        {
            public EditDiagnosisKind Kind { get; }
            public string Message { get; }
            public string ActualSegment { get; }

            private EditDiagnosis(EditDiagnosisKind kind, string message, string actualSegment = null)
            {
                Kind = kind;
                Message = message;
                ActualSegment = actualSegment;
            }

            public static EditDiagnosis Stale(string message)
                => new EditDiagnosis(EditDiagnosisKind.StaleContent, message);

            public static EditDiagnosis NoMatch(string message)
                => new EditDiagnosis(EditDiagnosisKind.NoMatch, message);
        }

        #endregion
    }
}
