using Markdig;
using System;
using System.Diagnostics;
using System.Text;

namespace AICA.ToolWindows
{
    /// <summary>
    /// Incremental DOM renderer for streaming output.
    /// During live streaming, only appends/updates changed DOM nodes
    /// via insertAdjacentHTML/innerHTML on specific elements.
    /// Never rebuilds historical messages. IE11/MSHTML compatible.
    /// </summary>
    internal class IncrementalRenderer
    {
        private readonly Func<dynamic> _getDocument;
        private readonly HtmlRenderer _htmlRenderer;
        private readonly MarkdownPipeline _markdownPipeline;
        private readonly Action _fallbackAction;

        private string _streamingMsgId;
        private string _contentDivId;
        private string _currentStreamingTextId;
        private int _thinkingCounter;
        private int _toolCallCounter;
        private int _streamingTextCounter;
        private bool _isActive;

        public IncrementalRenderer(
            Func<dynamic> getDocument,
            HtmlRenderer htmlRenderer,
            MarkdownPipeline markdownPipeline,
            Action fallbackAction = null)
        {
            _getDocument = getDocument ?? throw new ArgumentNullException(nameof(getDocument));
            _htmlRenderer = htmlRenderer ?? throw new ArgumentNullException(nameof(htmlRenderer));
            _markdownPipeline = markdownPipeline ?? throw new ArgumentNullException(nameof(markdownPipeline));
            _fallbackAction = fallbackAction;
        }

        /// <summary>
        /// Whether the renderer is in an active streaming session.
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// Create a new streaming message div at the end of #chat-log.
        /// Call once at the start of ExecuteAgentModeAsync.
        /// </summary>
        public void BeginStreamingMessage()
        {
            _streamingMsgId = "streaming-msg-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _contentDivId = _streamingMsgId + "-content";
            _thinkingCounter = 0;
            _toolCallCounter = 0;
            _streamingTextCounter = 0;
            _currentStreamingTextId = null;
            _isActive = true;

            var html = "<div id=\"" + _streamingMsgId + "\" class=\"message assistant streaming\">"
                     + "<div class=\"role\">AI</div>"
                     + "<div id=\"" + _contentDivId + "\" class=\"content\"></div>"
                     + "</div>";

            InsertHtmlAtEnd("chat-log", html);
            ScrollToBottom();
        }

        /// <summary>
        /// Append a new thinking block (default expanded).
        /// Returns the DOM element ID for later updates.
        /// </summary>
        public string AppendThinkingBlock(string thinkingContent, string actionText, int iterationId)
        {
            var thinkingId = _streamingMsgId + "-think-" + (_thinkingCounter++);

            var html = new StringBuilder();
            html.Append("<div id=\"").Append(thinkingId).Append("\">");
            html.Append(_htmlRenderer.BuildThinkingBlockHtml(thinkingContent, actionText, iterationId));
            html.Append("</div>");

            InsertHtmlAtEnd(_contentDivId, html.ToString());
            HighlightNewCodeBlocks(thinkingId);
            ScrollToBottom();
            return thinkingId;
        }

        /// <summary>
        /// Update existing thinking block content (for multi-chunk thinking).
        /// </summary>
        public void UpdateThinkingContent(string elementId, string thinkingContent, string actionText, int iterationId)
        {
            var html = _htmlRenderer.BuildThinkingBlockHtml(thinkingContent, actionText, iterationId);
            SetInnerHtml(elementId, html);
            HighlightNewCodeBlocks(elementId);
            ScrollToBottom();
        }

        /// <summary>
        /// Append a new tool call block (pending state, no result yet).
        /// Returns DOM element ID for later result update.
        /// </summary>
        public string AppendToolCallBlock(string toolName,
            System.Collections.Generic.Dictionary<string, object> arguments, int toolCallId)
        {
            var elemId = _streamingMsgId + "-tool-" + (_toolCallCounter++);

            var toolHtml = _htmlRenderer.BuildToolCallHtml(toolName, arguments, null, true, toolCallId);
            var html = "<div id=\"" + elemId + "\">" + toolHtml + "</div>";

            // Clear any active streaming text div before tool block
            _currentStreamingTextId = null;

            InsertHtmlAtEnd(_contentDivId, html);
            ScrollToBottom();
            return elemId;
        }

        /// <summary>
        /// Update an existing tool call block with its result.
        /// </summary>
        public void UpdateToolCallResult(string toolElementId, string fullToolHtml)
        {
            SetInnerHtml(toolElementId, fullToolHtml);
            HighlightNewCodeBlocks(toolElementId);
            ScrollToBottom();
        }

        /// <summary>
        /// Append or update conclusion text after a tool result within an iteration.
        /// </summary>
        public void AppendOrUpdateConclusionText(string iterationElementId, string markdownContent)
        {
            var conclusionId = iterationElementId + "-conclusion";
            var conclusionHtml = Markdig.Markdown.ToHtml(markdownContent, _markdownPipeline);

            var el = GetElement(conclusionId);
            if (el != null)
            {
                SetInnerHtml(conclusionId, conclusionHtml);
            }
            else
            {
                var html = "<div id=\"" + conclusionId + "\" class=\"conclusion-text\">" + conclusionHtml + "</div>";
                InsertHtmlAtEnd(_contentDivId, html);
            }
            HighlightNewCodeBlocks(conclusionId);
            ScrollToBottom();
        }

        /// <summary>
        /// Append independent streaming text (not inside any iteration block).
        /// Creates a streaming-text div on first call.
        /// </summary>
        public void AppendStreamingText(string markdownContent)
        {
            if (_currentStreamingTextId == null)
            {
                _currentStreamingTextId = _streamingMsgId + "-stext-" + (_streamingTextCounter++);
                var html = "<div id=\"" + _currentStreamingTextId + "\" class=\"streaming-text\"></div>";
                InsertHtmlAtEnd(_contentDivId, html);
            }
            UpdateStreamingText(markdownContent);
        }

        /// <summary>
        /// Replace the content of the streaming text div (for progressive updates).
        /// Pass empty/null to remove the div from DOM entirely.
        /// </summary>
        public void UpdateStreamingText(string markdownContent)
        {
            if (_currentStreamingTextId == null) return;
            if (string.IsNullOrEmpty(markdownContent))
            {
                // Remove the div from DOM entirely to avoid ID conflicts
                RemoveElement(_currentStreamingTextId);
                _currentStreamingTextId = null;
                return;
            }

            var html = Markdig.Markdown.ToHtml(markdownContent, _markdownPipeline);
            SetInnerHtml(_currentStreamingTextId, html);
            HighlightNewCodeBlocks(_currentStreamingTextId);
            ScrollToBottom();
        }

        /// <summary>
        /// Finalize the streaming message: remove .streaming class, reset state.
        /// Does NOT handle _conversation persistence — caller does that.
        /// </summary>
        public void FinalizeMessage()
        {
            if (!_isActive) return;

            try
            {
                dynamic doc = _getDocument();
                dynamic el = doc?.getElementById(_streamingMsgId);
                if (el != null)
                {
                    // Remove 'streaming' class using IE-safe string manipulation
                    string className = el.className ?? "";
                    el.className = className.Replace("streaming", "").Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICA-IR] FinalizeMessage failed: {ex.Message}");
            }

            _isActive = false;
            _streamingMsgId = null;
            _contentDivId = null;
            _currentStreamingTextId = null;
        }

        /// <summary>
        /// Scroll the page to the bottom.
        /// </summary>
        public void ScrollToBottom()
        {
            try
            {
                dynamic doc = _getDocument();
                dynamic window = doc?.parentWindow;
                window?.scrollTo(0, doc?.body?.scrollHeight ?? 0);
            }
            catch { }
        }

        // ── DOM Operation Helpers (IE11 compatible) ──

        private void InsertHtmlAtEnd(string parentId, string html)
        {
            try
            {
                dynamic parent = GetElement(parentId);
                if (parent == null)
                {
                    Debug.WriteLine($"[AICA-IR] Parent element '{parentId}' not found");
                    _fallbackAction?.Invoke();
                    return;
                }
                parent.insertAdjacentHTML("beforeend", html);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICA-IR] InsertHtmlAtEnd failed: {ex.Message}");
                _fallbackAction?.Invoke();
            }
        }

        private void SetInnerHtml(string elementId, string html)
        {
            try
            {
                dynamic el = GetElement(elementId);
                if (el != null)
                {
                    el.innerHTML = html;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICA-IR] SetInnerHtml failed for '{elementId}': {ex.Message}");
            }
        }

        private void RemoveElement(string elementId)
        {
            try
            {
                dynamic el = GetElement(elementId);
                if (el != null && el.parentNode != null)
                {
                    el.parentNode.removeChild(el);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICA-IR] RemoveElement failed for '{elementId}': {ex.Message}");
            }
        }

        private dynamic GetElement(string id)
        {
            try
            {
                dynamic doc = _getDocument();
                return doc?.getElementById(id);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Highlight only unhighlighted code blocks within a specific parent element.
        /// Scoped querySelectorAll — O(new blocks) instead of O(all blocks).
        /// </summary>
        private void HighlightNewCodeBlocks(string parentElementId)
        {
            try
            {
                dynamic doc = _getDocument();
                dynamic window = doc?.parentWindow;
                window?.execScript(
                    "var p=document.getElementById('" + parentElementId + "');" +
                    "if(p&&typeof hljs!=='undefined'){" +
                    "var bs=p.querySelectorAll('pre code');" +
                    "for(var i=0;i<bs.length;i++){" +
                    "if((' '+bs[i].className+' ').indexOf(' hljs ')<0){" +
                    "hljs.highlightBlock(bs[i]);" +
                    "}}}");
            }
            catch { }
        }
    }
}
