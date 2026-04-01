using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;

namespace AICA.Core.Agent
{
    /// <summary>
    /// LLM-based conversation compaction (OpenCode style).
    /// Generates a structured summary via LLM, with programmatic fallback on failure.
    /// </summary>
    public static class ConversationCompactor
    {
        private const string CompactionPrompt =
            "Provide a detailed summary for continuing this conversation. " +
            "Use this template:\n\n" +
            "## Goal\n" +
            "[What is the user trying to accomplish?]\n\n" +
            "## Instructions\n" +
            "- [Important instructions the user gave]\n" +
            "- [If there is a plan, include it]\n\n" +
            "## Discoveries\n" +
            "[Notable things learned during this conversation]\n\n" +
            "## Accomplished\n" +
            "[What work has been completed, what is in progress, what is left?]\n\n" +
            "## Relevant files\n" +
            "[Files that have been read, edited, or created]";

        /// <summary>
        /// Generate a structured summary of the conversation using LLM.
        /// Falls back to programmatic extraction if LLM call fails.
        /// </summary>
        public static async Task<string> GenerateSummaryAsync(
            ILLMClient llmClient,
            List<ChatMessage> history,
            CancellationToken ct)
        {
            try
            {
                // Build a compact request: system instruction + conversation history (no tools)
                var compactMessages = new List<ChatMessage>
                {
                    ChatMessage.System("You are a summarization assistant. Your ONLY job is to summarize the conversation below.")
                };

                // Add conversation messages (skip system prompt, keep user/assistant/tool)
                // Downgrade ImageParts to text placeholders before sending to compaction LLM
                foreach (var msg in history)
                {
                    if (msg.Role == ChatRole.System) continue;
                    compactMessages.Add(DowngradeImageParts(msg));
                }

                // If previous summary exists from earlier condense, include merge instruction
                var prevCondensed = history.FirstOrDefault(m =>
                    m.Role == ChatRole.System && m.Content != null
                    && m.Content.StartsWith("[Conversation condensed]"));
                if (prevCondensed != null)
                {
                    var prevSummary = prevCondensed.Content;
                    if (prevSummary.Length > 12000)
                        prevSummary = prevSummary.Substring(0, 12000) + "\n... (earlier context truncated)";
                    compactMessages.Add(ChatMessage.User(
                        "IMPORTANT: A previous summary exists from an earlier condensation. " +
                        "Merge its key points into your new summary — do NOT nest it verbatim.\n\n" + prevSummary));
                }

                // Add the compaction prompt as the final user message
                compactMessages.Add(ChatMessage.User(CompactionPrompt));

                // Call LLM without tools (text-only response)
                var sb = new StringBuilder();
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30s timeout for summary

                    await foreach (var chunk in llmClient.StreamChatAsync(compactMessages, null, cts.Token).ConfigureAwait(false))
                    {
                        if (chunk.Type == LLMChunkType.Text && chunk.Text != null)
                        {
                            sb.Append(chunk.Text);
                        }
                    }
                }

                var llmSummary = sb.ToString().Trim();
                if (llmSummary.Length > 100)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] LLM compaction summary generated ({llmSummary.Length} chars)");

                    // Append programmatic tool history as factual supplement
                    var toolHistory = TokenBudgetManager.ExtractToolCallHistory(history);
                    if (!string.IsNullOrEmpty(toolHistory))
                    {
                        llmSummary += "\n\n## Tool Call History (auto-extracted)\n" + toolHistory;
                    }

                    return llmSummary;
                }

                // LLM returned too short — fall through to programmatic
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] LLM compaction too short ({llmSummary.Length} chars), falling back to programmatic");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] LLM compaction failed, falling back to programmatic: {ex.Message}");
            }

            // Fallback: programmatic extraction (existing logic)
            return TokenBudgetManager.BuildAutoCondenseSummary(history);
        }

        /// <summary>
        /// Build condensed conversation history from a summary.
        /// Keeps system prompt + summary + replays last user message (OpenCode style).
        /// The replay ensures the LLM re-processes the user's request with fresh context.
        /// </summary>
        public static List<ChatMessage> BuildCondensedHistory(
            List<ChatMessage> history, string summary)
        {
            var condensed = new List<ChatMessage>();

            // Keep system prompt
            if (history.Count > 0 && history[0].Role == ChatRole.System)
            {
                condensed.Add(history[0]);
            }

            // Add summary as system context
            condensed.Add(ChatMessage.System(
                "[Conversation condensed] The following is a summary of all previous work:\n" + summary));

            // Replay last user message (OpenCode style: re-send as a fresh message so LLM re-processes it)
            string lastUserContent = null;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == ChatRole.User)
                {
                    lastUserContent = history[i].Content;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(lastUserContent))
            {
                condensed.Add(ChatMessage.User(lastUserContent));
            }

            // Safety: ensure at least one user message exists
            if (!condensed.Any(m => m.Role == ChatRole.User))
            {
                condensed.Add(ChatMessage.User(
                    "Context was condensed. Please continue with the current task based on the summary above."));
            }

            return condensed;
        }

        /// <summary>
        /// Downgrade ImageParts to text placeholders for compaction.
        /// Returns the original message if no ImageParts are present.
        /// </summary>
        private static ChatMessage DowngradeImageParts(ChatMessage msg)
        {
            if (!msg.HasMultimodalParts || msg.Parts == null)
                return msg;

            int imageCount = 0;
            var downgradedParts = new List<IContentPart>();
            foreach (var part in msg.Parts)
            {
                if (part is ImagePart)
                {
                    imageCount++;
                }
                else
                {
                    downgradedParts.Add(part);
                }
            }

            if (imageCount == 0)
                return msg;

            downgradedParts.Add(new TextPart($"[{imageCount} image(s) omitted for summary]"));
            return ChatMessage.UserWithParts(downgradedParts);
        }
    }
}
