# AICA 输出优化设计文档

> **版本**: v1.1
> **日期**: 2026-04-02
> **状态**: REVISED

---

## 1. 架构概述

### 1.1 核心问题

当前渲染方式 `RenderStructuredStreaming()` 在每个 AgentStep 到达时，都重建完整 HTML 并通过 `UpdateBrowserContent()` → `log.innerHTML = innerHtml` 全量替换 DOM。这导致：

1. **性能问题**：每次 step 都重新渲染全部历史消息 + 所有 iteration blocks
2. **闪烁**：全量替换 innerHTML 导致浏览器重绘整个聊天区域
3. **Plan 面板与内联 Plan 卡片并存**：Plan 同时出现在消息流和浮动面板中，信息冗余

### 1.2 优化目标

| 目标 | 描述 |
|------|------|
| **增量 DOM 追加** | 新内容通过 `insertAdjacentHTML` / `appendChild` 追加，不重建历史消息 |
| **有序流式输出** | 思考 → 工具调用 → 观察 → 决策 的顺序，最终结果逐字流式显示 |
| **Planning 浮动面板优化** | 底部浮动面板作为唯一 Plan 展示位置，可折叠，显示进度条 + 打勾 |
| **思考默认展开** | Thinking block 默认可见（不再需要点击展开） |

### 1.3 新增/修改的类和职责

| 类 | 文件 | 变更类型 | 职责 |
|----|------|----------|------|
| `IncrementalRenderer` | `ToolWindows/IncrementalRenderer.cs` | **新增** | 管理增量 DOM 操作，跟踪当前渲染状态 |
| `ChatToolWindowControl` | `ToolWindows/ChatToolWindowControl.xaml.cs` | 修改 | 调用 IncrementalRenderer 代替 RenderStructuredStreaming |
| `HtmlRenderer` | `ToolWindows/HtmlRenderer.cs` | 修改 | 新增 thinking 默认展开的 HTML 模板 |
| `ChatModels` | `ToolWindows/ChatModels.cs` | 修改 | IterationBlock 新增 DomElementId 用于增量更新 |

---

## 2. 数据流图

```
LLM Stream Chunks
       │
       ▼
AgentExecutor.ExecuteAsync()
  yield AgentStep { Type, Text, ToolCall, Result, Plan }
       │
       ▼
ChatToolWindowControl.ExecuteAgentModeAsync()
  dispatcher.Invoke(switch step.Type)
       │
       ▼
IncrementalRenderer (NEW)
  ├─ ThinkingChunk  → AppendThinkingBlock() 或 UpdateThinkingContent()
  ├─ ActionStart    → AppendActionText()
  ├─ ToolStart      → AppendToolCallBlock()
  ├─ ToolResult     → UpdateToolCallResult()
  ├─ TextChunk      → AppendTextChunk() 或 UpdateConclusionText()
  ├─ PlanUpdate     → UpdateFloatingPlanPanel() (仅浮动面板)
  └─ Complete       → FinalizeMessage()
       │
       ▼
DOM Operations (IE11 compatible)
  ├─ insertAdjacentHTML('beforeend', html)
  ├─ getElementById(id).innerHTML = newContent
  └─ scrollTo(0, body.scrollHeight)
```

### 2.1 渲染模式切换

```
ExecuteAgentModeAsync 开始
       │
       ├─ 创建 streaming message div（id="streaming-msg"）
       │   └─ appendChild 到 #chat-log
       │
       ├─ 每个 AgentStep → IncrementalRenderer 增量操作 streaming-msg 内部
       │
       └─ Complete →
            ├─ Plan auto-complete → UpdateFloatingPlanPanel()
            ├─ FinalizeMessage()（移除 .streaming class）
            ├─ 持久化到 _conversation（ConversationMessage 不变）
            └─ RenderConversation()（DOM-data 一致性最终重建）
```

---

## 3. 各模块详细设计

### 3.1 IncrementalRenderer

```csharp
namespace AICA.ToolWindows
{
    /// <summary>
    /// 增量 DOM 渲染器。在流式输出期间只追加/更新变化的 DOM 节点，
    /// 不重建历史消息。通过 MSHTML COM 接口操作 IE11 WebBrowser DOM。
    /// </summary>
    internal class IncrementalRenderer
    {
        private readonly Func<dynamic> _getDocument;    // () => ChatBrowser.Document
        private readonly HtmlRenderer _htmlRenderer;
        private readonly MarkdownPipeline _markdownPipeline;

        // 当前 streaming 消息的 DOM element ID
        private string _streamingMsgId;
        // 当前 iteration 的 DOM element ID（用于追加 conclusion text）
        private string _currentIterationId;
        // 当前 thinking block 的 DOM element ID（用于追加流式 thinking）
        private int _thinkingCounter;
        private int _toolCallCounter;

        // ── 初始化 ──

        /// <summary>
        /// 在 #chat-log 末尾创建一个新的 streaming message div。
        /// 在 ExecuteAgentModeAsync 开始时调用一次。
        /// </summary>
        public void BeginStreamingMessage();

        // ── 增量追加方法 ──

        /// <summary>
        /// 追加一个新的 thinking block（默认展开）。
        /// 返回 DOM element ID 以便后续更新 thinking content。
        /// </summary>
        public string AppendThinkingBlock(string thinkingContent, int iterationId);

        /// <summary>
        /// 更新已有 thinking block 的内容（如果 thinking 是分多个 chunk 到达的）。
        /// </summary>
        public void UpdateThinkingContent(string elementId, string newContent);

        /// <summary>
        /// 在当前 iteration 后追加 action text。
        /// </summary>
        public void AppendActionText(string actionText, int iterationId);

        /// <summary>
        /// 追加一个新的工具调用 block（pending 状态，无 result）。
        /// </summary>
        public string AppendToolCallBlock(string toolName,
            Dictionary<string, object> arguments, int toolCallId);

        /// <summary>
        /// 更新已有工具调用 block 的 result 部分。
        /// </summary>
        public void UpdateToolCallResult(string toolCallElementId,
            string resultHtml);

        /// <summary>
        /// 追加或更新 conclusion text（工具执行后 LLM 的文本决策）。
        /// </summary>
        public void AppendOrUpdateConclusionText(string iterationId,
            string markdownContent);

        /// <summary>
        /// 追加独立的 streaming text（不属于任何 iteration 的文本）。
        /// </summary>
        public void AppendStreamingText(string markdownContent);

        /// <summary>
        /// 替换独立 streaming text 区域的内容（逐字流式时持续更新）。
        /// </summary>
        public void UpdateStreamingText(string markdownContent);

        /// <summary>
        /// 完成当前 streaming 消息：移除 .streaming class，
        /// 清除内部状态。不涉及 _conversation 持久化。
        /// </summary>
        public void FinalizeMessage();

        /// <summary>
        /// 滚动到底部。
        /// </summary>
        public void ScrollToBottom();

        // ── DOM 操作辅助（IE11 兼容） ──

        private void InsertHtmlAtEnd(string parentId, string html);
        private void SetInnerHtml(string elementId, string html);
        private dynamic GetElement(string id);
    }
}
```

#### 3.1.1 DOM 操作实现细节

所有 DOM 操作通过 `dynamic doc = ChatBrowser.Document` 的 MSHTML COM 接口完成：

```csharp
// 追加 HTML 到元素末尾（IE11 兼容的 insertAdjacentHTML）
private void InsertHtmlAtEnd(string parentId, string html)
{
    dynamic doc = _getDocument();
    dynamic parent = doc?.getElementById(parentId);
    if (parent != null)
    {
        parent.insertAdjacentHTML("beforeend", html);
    }
}

// 替换元素内容
private void SetInnerHtml(string elementId, string html)
{
    dynamic doc = _getDocument();
    dynamic el = doc?.getElementById(elementId);
    if (el != null)
    {
        el.innerHTML = html;
    }
}
```

> **IE11 兼容性说明**：`insertAdjacentHTML` 是 IE 最早实现的 API 之一（IE4+），在 IE11 中完全可用。这是增量渲染的核心 API。

#### 3.1.2 BeginStreamingMessage 实现

```csharp
public void BeginStreamingMessage()
{
    _streamingMsgId = "streaming-msg-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    _thinkingCounter = 0;
    _toolCallCounter = 0;
    _currentIterationId = null;

    var html = "<div id=\"" + _streamingMsgId + "\" class=\"message assistant streaming\">"
             + "<div class=\"role\">AI</div>"
             + "<div id=\"" + _streamingMsgId + "-content\" class=\"content\"></div>"
             + "</div>";

    InsertHtmlAtEnd("chat-log", html);
    ScrollToBottom();
}
```

#### 3.1.3 Thinking Block（默认展开）

当前设计使用 CSS `checkbox` hack 来切换 thinking body 的显示/隐藏。为实现"默认展开"：

**方案**：反转 checkbox 逻辑 — 默认未选中 = 展开，选中 = 折叠。

新 CSS：
```css
.thinking-body {
    display: block;           /* 默认展开 */
}
.thinking-toggle:checked ~ .thinking-body {
    display: none;            /* checked 时折叠 */
}
.thinking-expand {
    transform: rotate(90deg);  /* 默认展开箭头朝下 */
}
.thinking-toggle:checked ~ .thinking-header .thinking-expand {
    transform: rotate(0deg);   /* 折叠后箭头回正 */
}
```

### 3.2 ChatToolWindowControl 修改

#### 3.2.1 ExecuteAgentModeAsync 中的 switch 块重构

**当前代码**（行 1577-1874）：每个 case 都调用 `RenderStructuredStreaming()` 全量重建。

**新代码**：每个 case 调用 `IncrementalRenderer` 的对应方法。

```csharp
// 字段新增
private IncrementalRenderer _incrementalRenderer;

// 在 ExecuteAgentModeAsync 开始时
_incrementalRenderer = new IncrementalRenderer(
    () => ChatBrowser.Document,
    _htmlRenderer,
    _markdownPipeline
);
_incrementalRenderer.BeginStreamingMessage();

// switch 块改写示例
case AgentStepType.ThinkingChunk:
    currentIteration = new IterationBlock
    {
        ThinkingContent = step.Text,
        IterationId = _globalIterationCounter++
    };
    iterationBlocks.Add(currentIteration);
    currentIteration.DomElementId =
        _incrementalRenderer.AppendThinkingBlock(
            step.Text, currentIteration.IterationId);
    break;

case AgentStepType.ToolStart:
    // P0-007 fix preserved
    if (!hasToolCalls && responseBuilder.Length > 0)
    {
        preToolContent.Append(responseBuilder);
        responseBuilder.Clear();
        _incrementalRenderer.UpdateStreamingText("");
    }
    hasToolCalls = true;
    if (step.ToolCall.Name != "attempt_completion")
    {
        var toolId = _globalToolCallCounter++;
        pendingToolCalls[step.ToolCall.Id] = (step.ToolCall, toolId);
        var toolElementId = _incrementalRenderer.AppendToolCallBlock(
            step.ToolCall.Name, step.ToolCall.Arguments, toolId);
        currentBlock = new ToolCallBlock
        {
            ToolId = toolId,
            ToolCallId = step.ToolCall.Id,
            DomElementId = toolElementId
        };
        toolCallBlocks.Add(currentBlock);
        if (currentIteration != null)
            currentIteration.ToolBlock = currentBlock;
    }
    break;

case AgentStepType.ToolResult:
    if (step.ToolCall?.Name == "attempt_completion") break;
    if (pendingToolCalls.TryGetValue(step.ToolCall.Id, out var toolInfo))
    {
        var resultText = step.Result.Success ? step.Result.Content : step.Result.Error;
        var block = toolCallBlocks.FirstOrDefault(b => b.ToolCallId == step.ToolCall.Id);
        if (block != null)
        {
            var resultHtml = BuildToolResultHtml(step.Result.Success, resultText, toolInfo.id);
            _incrementalRenderer.UpdateToolCallResult(block.DomElementId, resultHtml);
            block.ToolHtml = _htmlRenderer.BuildToolCallHtml(
                toolInfo.call.Name, toolInfo.call.Arguments,
                resultText, step.Result.Success, toolInfo.id);
        }
    }
    currentIteration = null;
    break;

case AgentStepType.PlanUpdate:
    // Plan ONLY in floating panel, NOT in message stream
    var planHtml = BuildPlanCardHtml(step.Plan, _planHistory.Count);
    // ... (existing plan dedup logic unchanged)
    UpdateFloatingPlanPanel();
    // NO RenderStructuredStreaming call
    break;

case AgentStepType.Complete:
    // 1. Auto-complete all plan steps (lines 1735-1746 preserved exactly)
    if (_lastPlan != null && _planHistory.Count > 0)
    {
        foreach (var ps in _lastPlan.Steps)
        {
            if (ps.Status != AICA.Core.Agent.PlanStepStatus.Completed
                && ps.Status != AICA.Core.Agent.PlanStepStatus.Failed)
                ps.Status = AICA.Core.Agent.PlanStepStatus.Completed;
        }
        var completedPlanHtml = BuildPlanCardHtml(_lastPlan, _planHistory.Count - 1);
        _planHistory[_planHistory.Count - 1] = completedPlanHtml;
        UpdateFloatingPlanPanel();
    }

    // 2. Finalize incremental streaming div (remove .streaming class)
    _incrementalRenderer.FinalizeMessage();

    // 3. Build final content and persist to _conversation
    //    (lines 1748-1832 logic preserved exactly — CompletionResult
    //    deserialization, finalContent/finalToolLogs assembly,
    //    P0-007 preToolContent restore, tool intent warning)
    CompletionResult completionResult = null;
    if (!string.IsNullOrEmpty(step.Text) && step.Text.StartsWith("TASK_COMPLETED:"))
        completionResult = CompletionResult.Deserialize(step.Text);

    string finalContent = null;
    string finalToolLogs = null;
    // ... (existing finalContent/finalToolLogs assembly logic, lines 1757-1817)

    if (!string.IsNullOrWhiteSpace(finalContent) || completionResult != null
        || !string.IsNullOrWhiteSpace(finalToolLogs))
    {
        var message = new ConversationMessage
        {
            Role = "assistant",
            Content = finalContent,
            ToolLogsHtml = finalToolLogs,
            CompletionData = completionResult != null ? step.Text : null,
            IterationBlocks = iterationBlocks.Count > 0
                ? new List<IterationBlock>(iterationBlocks) : null,
            ToolCallBlocks = toolCallBlocks.Count > 0
                ? new List<ToolCallBlock>(toolCallBlocks) : null
        };
        _conversation.Add(message);
        _llmHistory.Add(ChatMessage.Assistant(
            completionResult?.Summary ?? finalContent));
    }

    // 4. Full re-render to ensure DOM-data consistency
    //    The streaming div served during live output; now RenderConversation
    //    rebuilds the final layout from _conversation (canonical source).
    //    This guarantees the persisted ConversationMessage and DOM are in sync.
    RenderConversation();
    break;
```

#### 3.2.2 RenderConversation preserved for history replay

`RenderConversation()` (line 753-827) stays unchanged — used only for loading historical conversations and final Complete rendering.

#### 3.2.3 RenderStructuredStreaming marked Obsolete

```csharp
[Obsolete("Replaced by IncrementalRenderer. Kept as fallback.")]
private void RenderStructuredStreaming(...) { /* unchanged */ }
```

### 3.3 HtmlRenderer modification

**File**: `AICA/src/AICA.VSIX/ToolWindows/HtmlRenderer.cs`, lines 34-62

Only change: CSS logic reversal for default-expanded thinking (see section 3.1.3). The `BuildThinkingBlockHtml` HTML template stays the same — the checkbox is unchecked by default, and the CSS now treats unchecked = expanded.

### 3.4 ChatModels modification

**File**: `AICA/src/AICA.VSIX/ToolWindows/ChatModels.cs`

Add `DomElementId` property to both `IterationBlock` and `ToolCallBlock`:

```csharp
internal class IterationBlock
{
    // ... existing fields ...
    public string DomElementId { get; set; }  // NEW
}

internal class ToolCallBlock
{
    // ... existing fields ...
    public string DomElementId { get; set; }  // NEW
}
```

---

## 4. Integration Points

### 4.1 Precise file and line number mapping

| Location | Lines | Change |
|----------|-------|--------|
| `ChatToolWindowControl.xaml.cs` fields | 27-50 | Add `_incrementalRenderer` field |
| `ExecuteAgentModeAsync` switch block | 1577-1874 | Replace with IncrementalRenderer calls |
| `RenderStructuredStreaming` | 1991-2079 | Mark `[Obsolete]`, keep as fallback |
| `UpdateBrowserContent` | 829-893 | **No change** |
| `BuildPageHtml` CSS | 3035-3095 | Reverse thinking CSS logic |
| `BuildPageHtml` plan panel HTML | 3456-3465 | **No change** |
| `UpdateFloatingPlanPanel` | 1376-1445 | **No change** |
| `HtmlRenderer.BuildThinkingBlockHtml` | 34-62 | CSS reversal only |
| `ChatModels.cs` | 39-46, 27-33 | Add `DomElementId` property |

### 4.2 AgentExecutor — No changes

`AgentExecutor.cs` yields the same `AgentStep` sequence. All changes are in VSIX UI layer only.

### 4.3 PlanAgent — No changes

---

## 5. CSS Changes

### 5.1 Thinking block default expanded

In `BuildPageHtml` style section (lines 3035-3095):

```css
/* BEFORE */
.thinking-body { display: none; }
.thinking-toggle:checked ~ .thinking-body { display: block; }

/* AFTER */
.thinking-body { display: block; }
.thinking-toggle:checked ~ .thinking-body { display: none; }
```

### 5.2 New streaming-text class

```css
.streaming-text { padding: 4px 0; }
.streaming-text p { margin: 0 0 0.75em 0; }
```

---

## 6. Error Handling and Edge Cases

### 6.1 DOM element not found → Fallback to RenderStructuredStreaming

```csharp
private void InsertHtmlAtEnd(string parentId, string html)
{
    try
    {
        dynamic parent = GetElement(parentId);
        if (parent == null)
        {
            Debug.WriteLine("[AICA-IR] Element not found, falling back");
            _fallbackAction?.Invoke();
            return;
        }
        parent.insertAdjacentHTML("beforeend", html);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[AICA-IR] DOM op failed: {ex.Message}");
        _fallbackAction?.Invoke();
    }
}
```

### 6.2 Browser not ready → defer to RenderConversation on Complete
### 6.3 Rapid successive steps → already serialized by WPF Dispatcher.Invoke
### 6.4 Pause/Cancel → FinalizeMessage() + append pause indicator
### 6.5 Incomplete Markdown → UpdateStreamingText replaces single div innerHTML (small scope)
### 6.6 Syntax Highlighting — Scoped to New DOM Subtree

**Current problem** (line 863 of ChatToolWindowControl.xaml.cs): `UpdateBrowserContent` re-highlights ALL `<pre><code>` blocks globally via `document.querySelectorAll('pre code')`. While it filters already-highlighted blocks (checking for `hljs` class), the querySelectorAll still traverses the entire DOM on every update.

**Incremental approach**: Scope highlighting to the newly appended parent element only.

```csharp
/// <summary>
/// Highlight only unhighlighted code blocks within a specific parent element.
/// Avoids global DOM traversal — O(new blocks) instead of O(all blocks).
/// </summary>
private void HighlightNewCodeBlocks(string parentElementId)
{
    try
    {
        dynamic doc = _getDocument();
        dynamic window = doc?.parentWindow;
        // Scope querySelectorAll to the specific parent element
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
```

**Call sites**: Invoked after each DOM-mutating operation that may contain code blocks:
- `AppendThinkingBlock` → thinking content may contain inline code
- `UpdateToolCallResult` → tool results (especially `read_file`) contain code
- `AppendOrUpdateConclusionText` → LLM conclusion may contain code snippets
- `UpdateStreamingText` → streaming response may contain code blocks

**Not called for**: `AppendActionText` (plain text, no code blocks possible).

### 6.7 Performance Expectations

**Current cost per step** (`RenderStructuredStreaming`, line 1991-2079):

For a conversation with N historical messages and K iteration blocks in the current streaming turn:
1. Rebuilds ALL N historical messages as HTML (StringBuilder concatenation + Markdig rendering)
2. Rebuilds ALL K iteration blocks (thinking + tool + conclusion HTML)
3. Assigns result to `log.innerHTML` — browser parses and renders entire DOM tree
4. Re-runs hljs on ALL `<pre><code>` blocks globally

In a typical 50-tool-call conversation:
- ~50 historical messages × ~500 chars avg = ~25KB of historical HTML rebuilt per step
- ~50 iteration blocks × ~800 chars avg = ~40KB of streaming HTML rebuilt per step
- Total: **~65KB of HTML rebuilt and re-parsed on EVERY AgentStep** (50+ times per conversation)
- hljs re-scans ~100+ code blocks globally each time

**Incremental cost per step** (`IncrementalRenderer`):

1. Historical messages: **0 bytes** — never touched after initial render
2. New content only: single `insertAdjacentHTML` call with ~200-800 bytes of new HTML
3. `innerHTML` replacement only for `UpdateStreamingText` and `UpdateToolCallResult` — scoped to a single `<div>`, not the entire chat log
4. hljs scoped to parent element: scans only 0-2 new code blocks

In the same 50-tool-call conversation:
- Per-step HTML: **~500 bytes** (one thinking block or one tool result)
- Per-step DOM operation: **1 insertAdjacentHTML** (append) or **1 scoped innerHTML** (update)
- hljs: scans **0-2 blocks** within the new element only

**Expected improvement**:
| Metric | Current (full rebuild) | Incremental | Improvement |
|--------|----------------------|-------------|-------------|
| HTML generated per step | ~65KB | ~0.5KB | **~130x less** |
| DOM nodes parsed per step | ~all | ~1-3 new | **O(1) vs O(N)** |
| hljs blocks scanned | ~100+ global | ~0-2 scoped | **~50x less** |
| Visible flicker | Yes (full repaint) | No (append only) | **Eliminated** |

> Note: These are estimates based on typical conversation patterns. Actual numbers depend on tool result sizes and message complexity. The key insight is that cost-per-step changes from O(conversation_length) to O(1).

---

## 7. Backward Compatibility

| Area | Impact |
|------|--------|
| ConversationMessage serialization | None — DomElementId is runtime-only |
| Historical conversation replay | None — RenderConversation unchanged |
| Chat Mode (non-Agent) | None — uses RenderConversation as before |
| Plan floating panel | None — same HTML/JS/C# code |
| ConversationStorage | None — no schema change |

> **Plan panel session-switching note**: When the user switches between conversations via the sidebar, the existing `HideFloatingPlanPanel()` call in the conversation-loading code path clears the panel. This behavior is unchanged — `_planHistory` is cleared on new conversation load (line 372), and `UpdateFloatingPlanPanel` hides the panel when `_planHistory` is empty (line 1378-1381). No additional session-switching logic is needed.

---

## 8. Implementation Phases

| Phase | Scope | Est. Lines |
|-------|-------|-----------|
| 1: Core incremental renderer | IncrementalRenderer class, BeginStreamingMessage, InsertHtmlAtEnd, thinking/tool append | ~150 |
| 2: Full streaming integration | TextChunk, ActionStart, Complete, Error, fallback | ~100 |
| 3: Thinking CSS + Plan optimization | CSS reversal, remove Plan from message stream | ~30 |
| 4: Cleanup and testing | Obsolete marker, logging, E2E test | ~20 |
| **Total** | | **~300 new + ~190 modified** |

---

## 9. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|-----------|
| IE11 MSHTML COM exceptions | HIGH | Fallback to RenderStructuredStreaming |
| insertAdjacentHTML XSS | MEDIUM | All content HtmlEncoded or Markdig-processed |
| Scroll position after append | LOW | ScrollToBottom after each append |
| CSS reversal affects old conversations | LOW | Global CSS change, works for all |
| hljs not triggered on new DOM | LOW | HighlightNewCodeBlocks after each append |
