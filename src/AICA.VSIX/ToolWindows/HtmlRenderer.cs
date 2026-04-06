using Markdig;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AICA.ToolWindows
{
    /// <summary>
    /// Responsible for building HTML fragments used in the chat UI.
    /// Extracted from ChatToolWindowControl to keep rendering logic separate.
    /// </summary>
    internal class HtmlRenderer
    {
        private readonly MarkdownPipeline _markdownPipeline;

        public HtmlRenderer(MarkdownPipeline markdownPipeline)
        {
            _markdownPipeline = markdownPipeline;
        }

        /// <summary>
        /// Truncate text for display, replacing newlines and appending ellipsis.
        /// </summary>
        public string TruncateForDisplay(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            if (text.Length <= maxLength) return text.Replace("\n", " ").Replace("\r", "");
            return text.Substring(0, maxLength).Replace("\n", " ").Replace("\r", "") + "...";
        }

        /// <summary>
        /// Build HTML for thinking block with collapsible content
        /// </summary>
        public string BuildThinkingBlockHtml(string thinkingContent, string actionText, int iterationId)
        {
            var html = new StringBuilder();

            // Only render the collapsible thinking block if there's actual thinking content
            if (!string.IsNullOrEmpty(thinkingContent))
            {
                html.AppendLine("<div class=\"thinking-container\">");
                html.AppendLine($"  <input type=\"checkbox\" id=\"thinking-toggle-{iterationId}\" class=\"thinking-toggle\" />");
                html.AppendLine($"  <label for=\"thinking-toggle-{iterationId}\" class=\"thinking-header\">");
                html.AppendLine("    <span class=\"thinking-icon\">\U0001F4AD</span>");
                html.AppendLine("    <span class=\"thinking-label\">Thought</span>");
                html.AppendLine("    <span class=\"thinking-expand\">\u25B6</span>");
                html.AppendLine("  </label>");
                html.AppendLine("  <div class=\"thinking-body\">");
                var thinkingHtml = Markdig.Markdown.ToHtml(thinkingContent, _markdownPipeline);
                html.AppendLine($"    {thinkingHtml}");
                html.AppendLine("  </div>");
                html.AppendLine("</div>");
            }

            // Action text always visible below thinking block
            if (!string.IsNullOrEmpty(actionText))
            {
                html.AppendLine($"<div class=\"action-text\">{System.Web.HttpUtility.HtmlEncode(actionText)}</div>");
            }

            return html.ToString();
        }

        /// <summary>
        /// Build final HTML from interleaved tool call blocks (for persisting to conversation)
        /// </summary>
        public string BuildInterleavedToolLogsHtml(List<ToolCallBlock> toolCallBlocks)
        {
            var html = new StringBuilder();

            foreach (var block in toolCallBlocks)
            {
                // Add tool call HTML
                html.AppendLine(block.ToolHtml);

                // Add text that came after this tool call
                if (block.TextAfter.Length > 0)
                {
                    var textHtml = Markdig.Markdown.ToHtml(block.TextAfter.ToString(), _markdownPipeline);
                    html.AppendLine(textHtml);
                }
            }

            return html.ToString();
        }

        /// <summary>
        /// Build final HTML from structured iteration blocks for persistence
        /// </summary>
        public string BuildStructuredToolLogsHtml(List<IterationBlock> iterationBlocks, List<ToolCallBlock> toolCallBlocks)
        {
            var html = new StringBuilder();

            foreach (var iteration in iterationBlocks)
            {
                // 1. Thinking + Action
                if (!string.IsNullOrEmpty(iteration.ThinkingContent) || !string.IsNullOrEmpty(iteration.ActionText))
                {
                    html.AppendLine(BuildThinkingBlockHtml(iteration.ThinkingContent, iteration.ActionText, iteration.IterationId));
                }

                // 2. Tool call
                if (iteration.ToolBlock != null)
                {
                    html.AppendLine(iteration.ToolBlock.ToolHtml);
                }

                // 3. Conclusion text
                if (iteration.ConclusionText.Length > 0)
                {
                    var conclusionHtml = Markdig.Markdown.ToHtml(iteration.ConclusionText.ToString(), _markdownPipeline);
                    html.AppendLine($"<div class=\"conclusion-text\">{conclusionHtml}</div>");
                }
            }

            return html.ToString();
        }

        /// <summary>
        /// Build enhanced HTML for tool call visualization
        /// </summary>
        public string BuildToolCallHtml(string toolName, Dictionary<string, object> arguments, string result, bool success, int toolCallId)
        {
            var html = new StringBuilder();
            var toolIcon = GetToolIcon(toolName);

            html.AppendLine($"<div class=\"tool-call-container\">");
            html.AppendLine($"  <input type=\"checkbox\" id=\"tool-call-toggle-{toolCallId}\" class=\"tool-call-toggle\" />");
            html.AppendLine($"  <label for=\"tool-call-toggle-{toolCallId}\" class=\"tool-call-header\">");
            html.AppendLine($"    <span class=\"tool-call-icon\">{toolIcon}</span>");
            html.AppendLine($"    <span class=\"tool-call-name\">{System.Web.HttpUtility.HtmlEncode(toolName)}</span>");
            html.AppendLine($"    <span class=\"tool-call-expand\">\u25B6</span>");
            html.AppendLine($"  </label>");
            html.AppendLine($"  <div class=\"tool-call-body\">");

            // Parameters section
            if (arguments != null && arguments.Count > 0)
            {
                html.AppendLine("    <div class=\"tool-call-params\">");
                foreach (var arg in arguments.Take(5)) // Limit to 5 params for display
                {
                    var valueStr = arg.Value?.ToString() ?? "(null)";
                    var displayValue = TruncateForDisplay(valueStr, 100);
                    html.AppendLine("      <div class=\"tool-call-param\">");
                    html.AppendLine($"        <span class=\"tool-call-param-name\">{System.Web.HttpUtility.HtmlEncode(arg.Key)}:</span>");
                    html.AppendLine($"        <span class=\"tool-call-param-value\">{System.Web.HttpUtility.HtmlEncode(displayValue)}</span>");
                    html.AppendLine("      </div>");
                }
                if (arguments.Count > 5)
                {
                    html.AppendLine($"      <div class=\"tool-call-param\" style=\"color: #9ca3af; font-size: 11px;\">... and {arguments.Count - 5} more parameters</div>");
                }
                html.AppendLine("    </div>");
            }

            // Result section
            if (!string.IsNullOrEmpty(result))
            {
                var resultClass = success ? "success" : "error";
                var resultIcon = success ? "\u2705" : "\u274C";
                var resultLabel = success ? "Result" : "Error";

                html.AppendLine($"    <div class=\"tool-call-result {(success ? "" : "error")}\">");
                html.AppendLine($"      <div class=\"tool-call-result-header {resultClass}\">");
                html.AppendLine($"        <span>{resultIcon}</span>");
                html.AppendLine($"        <span>{resultLabel}</span>");
                html.AppendLine("      </div>");
                html.AppendLine($"      <div class=\"tool-call-result-content\">{System.Web.HttpUtility.HtmlEncode(TruncateForDisplay(result, 500))}</div>");
                html.AppendLine("    </div>");
            }

            html.AppendLine("  </div>");
            html.AppendLine("</div>");

            return html.ToString();
        }

        /// <summary>
        /// Get icon for tool based on tool name
        /// </summary>
        public string GetToolIcon(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return "\U0001F527";

            var lowerName = toolName.ToLowerInvariant();
            if (lowerName.Contains("read") || lowerName.Contains("list")) return "\U0001F4D6";
            if (lowerName.Contains("write") || lowerName.Contains("create")) return "\u270F\uFE0F";
            if (lowerName.Contains("edit") || lowerName.Contains("modify")) return "\U0001F4DD";
            if (lowerName.Contains("delete") || lowerName.Contains("remove")) return "\U0001F5D1\uFE0F";
            if (lowerName.Contains("search") || lowerName.Contains("find") || lowerName.Contains("grep")) return "\U0001F50D";
            if (lowerName.Contains("command") || lowerName.Contains("run") || lowerName.Contains("execute")) return "\u26A1";
            if (lowerName.Contains("git")) return "\U0001F500";
            if (lowerName.Contains("project")) return "\U0001F4C1";

            return "\U0001F527";
        }

        /// <summary>
        /// Build HTML for a completion card with feedback buttons
        /// </summary>
        public string BuildCompletionCardHtml(string summary, string command, int messageIndex)
        {
            var html = new StringBuilder();
            html.AppendLine("<div class=\"completion-card\">");
            html.AppendLine("  <div class=\"completion-header\">");
            html.AppendLine("    <span class=\"completion-icon\">\u2705</span>");
            html.AppendLine("    <span>Task Completed</span>");
            html.AppendLine("  </div>");
            html.AppendLine($"  <div class=\"content\">{Markdown.ToHtml(summary, _markdownPipeline)}</div>");

            if (!string.IsNullOrWhiteSpace(command))
            {
                html.AppendLine("  <div class=\"completion-command\">");
                html.AppendLine($"    <strong>Suggested command:</strong> <code>{System.Web.HttpUtility.HtmlEncode(command)}</code>");
                html.AppendLine("  </div>");
            }

            html.AppendLine("  <div class=\"feedback-section\">");
            html.AppendLine("    <div class=\"feedback-label\">Was this helpful?</div>");
            html.AppendLine("    <div class=\"feedback-buttons\">");
            html.AppendLine($"      <button class=\"feedback-btn\" data-message-id=\"{messageIndex}\" data-feedback=\"satisfied\" onclick=\"provideFeedback({messageIndex}, 'satisfied')\">\U0001F44D Yes</button>");
            html.AppendLine($"      <button class=\"feedback-btn unsatisfied\" data-message-id=\"{messageIndex}\" data-feedback=\"unsatisfied\" onclick=\"provideFeedback({messageIndex}, 'unsatisfied')\">\U0001F44E No</button>");
            html.AppendLine("    </div>");
            html.AppendLine("  </div>");
            html.AppendLine("</div>");

            return html.ToString();
        }
    }
}
