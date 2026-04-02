using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            "Replace text in files. Three modes:\n" +
            "1. Single edit: file_path + old_string + new_string (one replacement)\n" +
            "2. Multi-edit: file_path + edits array (multiple replacements in one file, shown as single diff)\n" +
            "3. Multi-file: files array with per-file edits (each file shown as separate diff)\n" +
            "old_string must match uniquely. Use read_file first to see exact content.\n" +
            "Limits: max 50 edits per file, max 20 files per call.\n" +
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
                            Description = "If true, replace all occurrences (single edit mode only). Default is false.",
                            Default = false
                        },
                        // v2.3: Multi-edit mode (same file, multiple replacements)
                        ["edits"] = new ToolParameterProperty
                        {
                            Type = "array",
                            Description = "Array of edits for multi-edit mode. Each edit has old_string and new_string. " +
                                          "When provided, all edits are applied to the same file and shown as a single diff preview. (max 50)",
                            Items = new ToolParameterProperty
                            {
                                Type = "object",
                                Properties = new Dictionary<string, ToolParameterProperty>
                                {
                                    ["old_string"] = new ToolParameterProperty { Type = "string", Description = "The exact text to replace" },
                                    ["new_string"] = new ToolParameterProperty { Type = "string", Description = "The replacement text" }
                                },
                                Required = new[] { "old_string", "new_string" }
                            }
                        },
                        // v2.3: Multi-file mode (multiple files, each with edits)
                        ["files"] = new ToolParameterProperty
                        {
                            Type = "array",
                            Description = "Array of file edits for multi-file mode. Each entry has file_path and edits array. " +
                                          "Each file is shown as a separate diff preview for independent confirmation. (max 20)",
                            Items = new ToolParameterProperty
                            {
                                Type = "object",
                                Properties = new Dictionary<string, ToolParameterProperty>
                                {
                                    ["file_path"] = new ToolParameterProperty { Type = "string", Description = "Path to the file" },
                                    ["edits"] = new ToolParameterProperty
                                    {
                                        Type = "array",
                                        Description = "Edits to apply to this file",
                                        Items = new ToolParameterProperty
                                        {
                                            Type = "object",
                                            Properties = new Dictionary<string, ToolParameterProperty>
                                            {
                                                ["old_string"] = new ToolParameterProperty { Type = "string", Description = "Text to replace" },
                                                ["new_string"] = new ToolParameterProperty { Type = "string", Description = "Replacement text" }
                                            },
                                            Required = new[] { "old_string", "new_string" }
                                        }
                                    }
                                },
                                Required = new[] { "file_path", "edits" }
                            }
                        }
                    },
                    // v2.3: Required is empty — validated at runtime based on mode
                    Required = Array.Empty<string>()
                }
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            try
            {
                // v2.3: Detect call mode (files → multi-file, edits → multi-edit, else → single edit)
                var filesDicts = ToolParameterValidator.GetListOfDicts(call.Arguments, "files");
                var editsDicts = ToolParameterValidator.GetListOfDicts(call.Arguments, "edits");

                if (filesDicts != null)
                {
                    var files = filesDicts.Select(d => new FileEditEntry
                    {
                        FilePath = ToolParameterValidator.GetRequiredParameter<string>(d, "file_path"),
                        Edits = ParseEditsFromDicts(
                            ToolParameterValidator.GetListOfDicts(d, "edits")
                            ?? throw new ToolParameterException("Each file entry must have an 'edits' array"))
                    }).ToList();
                    return (await ExecuteMultiFileAsync(files, context, ct)).ToolResult;
                }

                if (editsDicts != null)
                {
                    var path2 = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "file_path");
                    var edits = ParseEditsFromDicts(editsDicts);
                    return (await ExecuteMultiEditAsync(path2, edits, context, ct)).ToolResult;
                }

                // Mode A: Single edit (existing path — zero changes below)
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
                    // v2.3: Append diagnostics after successful edit
                    return await AppendDiagnosticsAsync(editResult, path, context, ct);
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

        #region v2.3: Multi-edit and Multi-file support

        private sealed class EditEntry
        {
            public string OldString { get; set; }
            public string NewString { get; set; }
        }

        private sealed class FileEditEntry
        {
            public string FilePath { get; set; }
            public List<EditEntry> Edits { get; set; }
        }

        private enum MultiEditOutcome
        {
            Applied,
            Cancelled,
            Failed
        }

        private sealed class MultiEditResult
        {
            public MultiEditOutcome Outcome { get; set; }
            public ToolResult ToolResult { get; set; }
        }

        /// <summary>
        /// v2.3: Append IDE diagnostics to a successful edit result.
        /// Non-fatal — if diagnostics retrieval fails, the original result is returned unchanged.
        /// </summary>
        private static async Task<ToolResult> AppendDiagnosticsAsync(
            ToolResult result, string filePath, IAgentContext context, CancellationToken ct)
        {
            if (!result.Success)
                return result;

            try
            {
                var diagnostics = await context.GetDiagnosticsAsync(filePath, ct);
                if (diagnostics != null && diagnostics.Count > 0)
                {
                    var formatted = string.Join("\n", diagnostics.Select(d =>
                        $"  Line {d.Line}, Col {d.Column}: [{d.Severity}] {d.Message}" +
                        (string.IsNullOrEmpty(d.Code) ? "" : $" ({d.Code})")));
                    result.Content += $"\n\n⚠️ DIAGNOSTICS ({diagnostics.Count} issue(s) detected after edit):\n{formatted}\n" +
                                      "Fix these issues before proceeding.";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Non-fatal: diagnostics unavailable, return original result
            }

            return result;
        }

        private static List<EditEntry> ParseEditsFromDicts(List<Dictionary<string, object>> dicts)
        {
            return dicts.Select(d => new EditEntry
            {
                OldString = ToolParameterValidator.GetRequiredParameter<string>(d, "old_string"),
                NewString = ToolParameterValidator.GetRequiredParameter<string>(d, "new_string")
            }).ToList();
        }

        /// <summary>
        /// FindWithCascade + uniqueness enforcement for multi-edit mode.
        /// Returns (match, error). If error is non-null, the edit should be rejected.
        /// </summary>
        private (CascadeMatch? Match, string Error) FindWithCascadeUnique(string content, string oldString)
        {
            var match = FindWithCascade(content, oldString);
            if (match == null)
                return (null, null); // no match — caller handles diagnostic

            // Verify uniqueness at Level 0 (Exact): check for second occurrence
            if (match.Value.Level == MatchLevel.Exact)
            {
                var first = content.IndexOf(oldString, StringComparison.Ordinal);
                var second = content.IndexOf(oldString, first + 1, StringComparison.Ordinal);
                if (second >= 0)
                    return (null, "old_string appears multiple times in the file. " +
                                  "Provide more surrounding context to make it unique. " +
                                  "replace_all is not supported in multi-edit mode.");
            }
            // Levels 1-5: internal implementation already enforces uniqueness

            return (match, null);
        }

        /// <summary>
        /// v2.3: Execute multiple edits on a single file. All edits are aggregated into
        /// one diff preview for a single user confirmation.
        /// </summary>
        private async Task<MultiEditResult> ExecuteMultiEditAsync(
            string path, List<EditEntry> edits, IAgentContext context, CancellationToken ct)
        {
            // 1. Validate
            if (edits.Count == 0)
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolResult.Fail("edits array is empty") };
            if (edits.Count > 50)
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolResult.Fail("Too many edits (max 50 per call)") };

            // 2. Path checks + read file
            if (!context.IsPathAccessible(path))
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path)) };
            if (!await context.FileExistsAsync(path, ct))
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolErrorHandler.HandleError(ToolErrorHandler.NotFound(path)) };
            if (FileTimeTracker.Instance.HasExternalModification(path))
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolResult.Fail($"⚠️ File '{path}' has been modified externally. Use read_file first.") };

            var content = await context.ReadFileAsync(path, ct);
            var originalLineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
            var normalized = NormalizeLineEndings(content);

            // 3. Collect all match positions (with no-op and uniqueness checks)
            var matches = new List<(int Index, int Length, string NewText, int EditIndex)>();
            for (int i = 0; i < edits.Count; i++)
            {
                var normOld = NormalizeLineEndings(edits[i].OldString);
                var normNew = NormalizeLineEndings(edits[i].NewString);

                // No-op detection
                if (normOld == normNew)
                    return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                        ToolResult = ToolResult.Fail($"Edit #{i + 1}: old_string and new_string are identical.") };

                // Uniqueness-enforced fuzzy match
                var (cascade, uniqueError) = FindWithCascadeUnique(normalized, normOld);
                if (uniqueError != null)
                    return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                        ToolResult = ToolResult.Fail($"Edit #{i + 1}: {uniqueError}") };
                if (cascade == null)
                {
                    var diagnosis = DiagnoseEditFailure(normalized, normOld, path, context);
                    return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                        ToolResult = ToolResult.Fail($"Edit #{i + 1} failed: {diagnosis.Message}") };
                }
                matches.Add((cascade.Value.MatchIndex, cascade.Value.MatchLength, normNew, i));
            }

            // 4. Sort by position + overlap detection
            matches.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (int i = 1; i < matches.Count; i++)
            {
                if (matches[i].Index < matches[i - 1].Index + matches[i - 1].Length)
                    return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                        ToolResult = ToolResult.Fail(
                            $"Edits #{matches[i - 1].EditIndex + 1} and #{matches[i].EditIndex + 1} have overlapping regions.") };
            }

            // 5. Apply edits in reverse order (avoids offset drift)
            var sb = new StringBuilder(normalized);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                sb.Remove(matches[i].Index, matches[i].Length);
                sb.Insert(matches[i].Index, matches[i].NewText);
            }
            var newContent = sb.ToString();

            // 6. Restore original line endings
            if (originalLineEnding == "\r\n")
                newContent = newContent.Replace("\n", "\r\n");

            // 7. Single diff confirmation
            var diffResult = await context.ShowDiffAndApplyAsync(path, content, newContent, ct);
            if (!diffResult.Applied)
            {
                return new MultiEditResult
                {
                    Outcome = MultiEditOutcome.Cancelled,
                    ToolResult = ToolResult.Ok(
                        $"MULTI-EDIT CANCELLED BY USER — {edits.Count} edits NOT applied to {path}\n\n" +
                        "The user chose not to apply the proposed edits. Respect this decision and do NOT retry " +
                        "the same edits automatically unless the user explicitly asks.")
                };
            }

            // 8. Record edit for conflict detection
            FileTimeTracker.Instance.RecordEdit(path);

            // 9. User manual edit detection (aligned with Mode A)
            var finalContent = await context.ReadFileAsync(path, ct);
            bool wasModifiedByUser = finalContent != newContent;

            if (wasModifiedByUser)
            {
                var originalLines = content.Split('\n').Length;
                var finalLines = finalContent.Split('\n').Length;
                var lineDiff = finalLines - originalLines;
                var diffText = lineDiff > 0 ? $"+{lineDiff}" : lineDiff < 0 ? $"{lineDiff}" : "0";

                return new MultiEditResult
                {
                    Outcome = MultiEditOutcome.Applied,
                    ToolResult = ToolResult.Ok(
                        $"⚠️ USER MANUALLY EDITED THE FILE — YOUR SUGGESTION WAS NOT USED ⚠️\n\n" +
                        $"File: {path}\n" +
                        $"Original: {originalLines} lines → User's version: {finalLines} lines ({diffText})\n" +
                        $"Attempted edits: {edits.Count}\n\n" +
                        $"📄 ACTUAL FILE CONTENT (as saved by user):\n{finalContent}\n\n" +
                        $"⚠️ CRITICAL: You MUST read and analyze the actual content above.")
                };
            }

            var editResult = ToolResult.Ok($"File edited: {path} ({edits.Count} edits applied in one diff)");
            editResult.Metadata = new Dictionary<string, string>
            {
                ["edit_mode"] = "multi_edit",
                ["edit_count"] = edits.Count.ToString()
            };
            // v2.3: Append diagnostics after successful multi-edit
            editResult = await AppendDiagnosticsAsync(editResult, path, context, ct);
            return new MultiEditResult { Outcome = MultiEditOutcome.Applied, ToolResult = editResult };
        }

        /// <summary>
        /// v2.3: Execute edits across multiple files. Each file gets its own
        /// diff preview for independent confirmation.
        /// </summary>
        private async Task<MultiEditResult> ExecuteMultiFileAsync(
            List<FileEditEntry> files, IAgentContext context, CancellationToken ct)
        {
            if (files.Count == 0)
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolResult.Fail("files array is empty") };
            if (files.Count > 20)
                return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                    ToolResult = ToolResult.Fail("Too many files (max 20 per call)") };

            var results = new List<string>();
            int applied = 0, cancelled = 0, failed = 0;

            foreach (var file in files)
            {
                var mer = await ExecuteMultiEditAsync(file.FilePath, file.Edits, context, ct);
                switch (mer.Outcome)
                {
                    case MultiEditOutcome.Applied:
                        applied++;
                        results.Add($"✅ {file.FilePath}: {file.Edits.Count} edit(s) applied");
                        break;
                    case MultiEditOutcome.Cancelled:
                        cancelled++;
                        results.Add($"⏭️ {file.FilePath}: skipped by user");
                        break;
                    case MultiEditOutcome.Failed:
                        failed++;
                        results.Add($"❌ {file.FilePath}: {mer.ToolResult.Error ?? mer.ToolResult.Content ?? "failed"}");
                        break;
                }
            }

            var summary = $"Multi-file edit: {applied} applied, {cancelled} skipped, {failed} failed\n" +
                          string.Join("\n", results);

            var outcome = applied > 0 ? MultiEditOutcome.Applied
                        : cancelled > 0 ? MultiEditOutcome.Cancelled
                        : MultiEditOutcome.Failed;

            var toolResult = outcome != MultiEditOutcome.Failed
                ? ToolResult.Ok(summary)
                : ToolResult.Fail(summary);

            if (outcome == MultiEditOutcome.Applied)
            {
                toolResult.Metadata = new Dictionary<string, string>
                {
                    ["edit_mode"] = "multi_file",
                    ["file_count"] = files.Count.ToString(),
                    ["applied"] = applied.ToString(),
                    ["cancelled"] = cancelled.ToString(),
                    ["failed"] = failed.ToString()
                };
            }

            return new MultiEditResult { Outcome = outcome, ToolResult = toolResult };
        }

        #endregion

        #region v2.1 T5: Cascading Fuzzy Match

        /// <summary>
        /// Match level used by FindWithCascade.
        /// </summary>
        private enum MatchLevel
        {
            Exact,                  // Level 0: exact string match
            LineTrimmed,            // Level 1: line-end whitespace ignored (NEW in v2.1)
            IndentationFlexible,    // Level 2: leading whitespace ignored (extracted from H3)
            WhitespaceNormalized,   // Level 3: all whitespace compressed (extracted from H3)
            UnicodeNormalized,      // Level 4: fullwidth/smart-quote/dash/NBSP/ZWS normalization
            CommentStripped         // Level 5: C++ line comments (//) stripped before matching
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

            // Level 4: Unicode normalization (fullwidth → halfwidth, smart quotes, dashes, NBSP, ZWS)
            var normalizedUnicodeContent = NormalizeUnicode(content);
            var normalizedUnicodeOld = NormalizeUnicode(oldString);
            if (normalizedUnicodeContent != content || normalizedUnicodeOld != oldString)
            {
                int idx4 = normalizedUnicodeContent.IndexOf(normalizedUnicodeOld, StringComparison.Ordinal);
                if (idx4 >= 0)
                {
                    var second4 = normalizedUnicodeContent.IndexOf(normalizedUnicodeOld, idx4 + 1, StringComparison.Ordinal);
                    if (second4 < 0)
                        return new CascadeMatch { MatchIndex = idx4, MatchLength = normalizedUnicodeOld.Length, Level = MatchLevel.UnicodeNormalized };
                }
            }

            // Level 5: Comment stripping (strip C++ // line comments before matching)
            var strippedContent = StripLineComments(content);
            var strippedOld = StripLineComments(oldString);
            if (strippedContent != content || strippedOld != oldString)
            {
                int idx5 = strippedContent.IndexOf(strippedOld, StringComparison.Ordinal);
                if (idx5 >= 0)
                {
                    var second5 = strippedContent.IndexOf(strippedOld, idx5 + 1, StringComparison.Ordinal);
                    if (second5 < 0)
                    {
                        // Map start/end positions from stripped space back to original content
                        int origStart5 = MapStrippedToOriginal(content, strippedContent, idx5);
                        int origEnd5 = MapStrippedToOriginal(content, strippedContent, idx5 + strippedOld.Length);
                        if (origStart5 >= 0 && origEnd5 >= 0 && origEnd5 <= content.Length)
                            return new CascadeMatch { MatchIndex = origStart5, MatchLength = origEnd5 - origStart5, Level = MatchLevel.CommentStripped };
                    }
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
        /// Level 4: Normalize Unicode characters — fullwidth ASCII → halfwidth, smart quotes,
        /// em/en-dash → hyphen, non-breaking space → space, zero-width chars removed.
        /// </summary>
        private static string NormalizeUnicode(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var ch in text)
            {
                // Fullwidth ASCII → halfwidth (！→!, Ａ→A, etc.)
                if (ch >= '\uFF01' && ch <= '\uFF5E')
                    sb.Append((char)(ch - 0xFEE0));
                // Smart quotes → straight quotes
                else if (ch == '\u201C' || ch == '\u201D') sb.Append('"');
                else if (ch == '\u2018' || ch == '\u2019') sb.Append('\'');
                // Em-dash / En-dash → hyphen
                else if (ch == '\u2014' || ch == '\u2013') sb.Append('-');
                // Non-breaking space → space
                else if (ch == '\u00A0') sb.Append(' ');
                // Zero-width characters → skip
                else if (ch == '\u200B' || ch == '\uFEFF') continue;
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Level 5: Strip C++ // line comments from each line before matching.
        /// Uses a simple heuristic: ignores // inside string/char literals.
        /// </summary>
        private static string StripLineComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split('\n');
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Find // that's not inside a string literal (simple heuristic)
                bool inString = false;
                bool inChar = false;
                int commentStart = -1;
                for (int j = 0; j < line.Length - 1; j++)
                {
                    var c = line[j];
                    if (c == '"' && !inChar) inString = !inString;
                    else if (c == '\'' && !inString) inChar = !inChar;
                    else if (c == '/' && line[j + 1] == '/' && !inString && !inChar)
                    {
                        commentStart = j;
                        break;
                    }
                }
                if (commentStart >= 0)
                    sb.Append(line.Substring(0, commentStart).TrimEnd());
                else
                    sb.Append(line);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Map a character index in the stripped (comment-removed) string back to the original string.
        /// Comment stripping only removes characters from within lines (trailing portions),
        /// so start-of-line positions are preserved. We scan both strings in parallel.
        /// </summary>
        private static int MapStrippedToOriginal(string original, string stripped, int strippedIndex)
        {
            int origIdx = 0;
            int stripIdx = 0;
            while (origIdx < original.Length && stripIdx < strippedIndex)
            {
                // Skip original characters that were removed by stripping
                // Both strings share newlines at the same relative positions
                if (origIdx < original.Length && stripIdx < stripped.Length &&
                    original[origIdx] == stripped[stripIdx])
                {
                    origIdx++;
                    stripIdx++;
                }
                else
                {
                    // Original has extra chars (the stripped comment) — advance original only
                    origIdx++;
                }
            }
            return origIdx <= original.Length ? origIdx : -1;
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
