# AICA Planning 面板重设计 + LLM 自动标记

> **版本**: v1.0
> **日期**: 2026-04-02
> **状态**: DRAFT
> **前置**: AICA_OutputOptimization_Design.md v1.1（本文档为其补充，不修改 v1.1 已有内容）

---

## 1. 概述

本文档补充 v1.1 设计中遗漏的两部分：

| 部分 | 描述 |
|------|------|
| **Part A** | Planning 浮动面板 UI 重设计 — 可折叠/展开，进度徽章 + 迷你进度条 + 步骤状态列表 |
| **Part B** | LLM 自动标记步骤完成 — AgentExecutor 每轮工具调用后用轻量 LLM 判断步骤完成状态 |

---

## 2. Part A：Planning 浮动面板 UI 重设计

### 2.1 设计目标

将当前简单的 plan-card 面板重设计为一个信息密度更高、交互更清晰的浮动面板：

| 当前问题 | 目标 |
|----------|------|
| 标题栏只有"📋 Task Plan (N)" | 标题栏 = 图标 + 标题 + 进度徽章(completed/total) + 迷你进度条 + 折叠按钮 |
| 步骤列表用 emoji 区分状态但没有视觉层次 | 已完成=删除线灰色，进行中=蓝色高亮+动画，待执行=灰色 |
| 折叠依赖 JS toggleClass | CSS checkbox hack（IE11 兼容，零 JS 事件） |
| 颜色主题是红色(#e06c75) | 重设计为 VS2022 暗色主题色系 |

### 2.2 配色方案

```
面板背景:   #2a2a3c
标题栏背景: #252536
蓝色主题:   #89b4fa（进行中、进度条、边框）
绿色完成:   #a6e3a1（完成徽章、完成图标）
灰色待执行: #6c7086
灰色文字:   #9399b2（已完成步骤描述）
白色文字:   #cdd6f4（标题、进行中步骤描述）
```

### 2.3 面板结构 — HTML

替换当前 `BuildPageHtml` 行 3456-3465 的 `#plan-floating-panel` HTML：

```html
<!-- CSS checkbox hack: 隐藏 checkbox 控制折叠 -->
<input type="checkbox" id="plan-collapse-toggle" style="display:none" checked />

<div id="plan-floating-panel" style="display:none">
    <!-- 标题栏 -->
    <label for="plan-collapse-toggle" id="plan-title-bar">
        <span id="plan-title-icon">📋</span>
        <span id="plan-title-text">执行计划</span>
        <span id="plan-badge">0/0</span>
        <span id="plan-mini-progress">
            <span id="plan-mini-progress-fill"></span>
        </span>
        <span id="plan-collapse-arrow">▼</span>
    </label>

    <!-- 可折叠主体 -->
    <div id="plan-panel-body">
        <div id="plan-tabs-container"></div>
        <div id="plan-steps-list"></div>
    </div>
</div>
```

**关键点**：
- `<label for="plan-collapse-toggle">` 绑定隐藏的 checkbox，点击 label 自动切换 checkbox 状态
- checkbox `checked` = 折叠（只显示标题栏），`unchecked` = 展开
- 初始状态 `checked`（折叠），`UpdateFloatingPlanPanel` 首次显示时通过 DOM 移除 checked 来展开
- 不需要任何 JS 事件绑定 — 纯 CSS 驱动

### 2.4 面板结构 — CSS

替换当前 `#plan-floating-panel` 及相关 CSS（行 3303-3351）：

```css
/* ── Planning 浮动面板（VS2022 暗色主题） ── */
#plan-floating-panel {
    position: fixed;
    bottom: 0; left: 0; right: 0;
    z-index: 1000;
    background: #2a2a3c;
    border-top: 2px solid #89b4fa;
    font-family: 'Segoe UI', sans-serif;
}

/* ── 标题栏 ── */
#plan-title-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 12px;
    background: #252536;
    cursor: pointer;
    user-select: none;
}
#plan-title-bar:hover { background: #2d2d48; }

#plan-title-icon { font-size: 14px; }
#plan-title-text {
    font-size: 12px; font-weight: 600;
    color: #cdd6f4;
}
#plan-badge {
    font-size: 11px; font-weight: 600;
    color: #a6e3a1;
    background: rgba(166, 227, 161, 0.15);
    padding: 1px 6px;
    border-radius: 8px;
}
#plan-mini-progress {
    flex: 1;
    max-width: 120px;
    height: 4px;
    background: #45475a;
    border-radius: 2px;
    overflow: hidden;
}
#plan-mini-progress-fill {
    display: block;
    height: 100%;
    width: 0%;
    background: #89b4fa;
    border-radius: 2px;
    transition: width 0.3s ease;
}
#plan-collapse-arrow {
    color: #6c7086;
    font-size: 10px;
    transition: transform 0.2s ease;
    margin-left: auto;
}

/* ── CSS Checkbox Hack 折叠逻辑 ──
   checked = 折叠（只显示标题栏）
   unchecked = 展开（显示步骤列表） */
#plan-collapse-toggle:checked ~ #plan-floating-panel #plan-panel-body {
    display: none;
}
#plan-collapse-toggle:checked ~ #plan-floating-panel #plan-collapse-arrow {
    transform: rotate(180deg);
}
/* unchecked = 展开 */
#plan-panel-body {
    max-height: 40vh;
    overflow-y: auto;
    background: #2a2a3c;
}

/* ── 步骤列表 ── */
#plan-steps-list {
    padding: 4px 0 8px 0;
}
.pp-step {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    padding: 4px 14px;
    font-size: 13px;
    line-height: 1.4;
}
.pp-step-icon {
    flex-shrink: 0;
    width: 18px;
    text-align: center;
    font-size: 13px;
}

/* 待执行 */
.pp-step.pp-pending .pp-step-icon { color: #6c7086; }
.pp-step.pp-pending .pp-step-desc { color: #6c7086; }

/* 进行中 — 蓝色高亮 */
.pp-step.pp-inprogress {
    background: rgba(137, 180, 250, 0.08);
}
.pp-step.pp-inprogress .pp-step-icon { color: #89b4fa; }
.pp-step.pp-inprogress .pp-step-desc {
    color: #cdd6f4;
    font-weight: 600;
}

/* 已完成 — 删除线灰色 */
.pp-step.pp-completed .pp-step-icon { color: #a6e3a1; }
.pp-step.pp-completed .pp-step-desc {
    color: #9399b2;
    text-decoration: line-through;
}

/* 失败 — 红色 */
.pp-step.pp-failed .pp-step-icon { color: #f38ba8; }
.pp-step.pp-failed .pp-step-desc {
    color: #f38ba8;
    text-decoration: line-through;
}

/* ── 多 Plan Tab 栏 ── */
.plan-tabs { display: flex; gap: 0; border-bottom: 1px solid #45475a; }
.plan-tab {
    padding: 6px 14px; cursor: pointer;
    font-size: 12px; color: #6c7086;
    border-bottom: 2px solid transparent;
    background: transparent;
    border-top: none; border-left: none; border-right: none;
}
.plan-tab:hover { color: #cdd6f4; }
.plan-tab.active { color: #89b4fa; border-bottom-color: #89b4fa; }
```

### 2.5 面板结构 — CSS Checkbox Hack 兼容性说明

**IE11 兼容性分析**：

CSS checkbox hack 依赖 `~`（通用兄弟选择器），IE7+ 完全支持。但本设计中 checkbox 和 panel 是**兄弟元素**（都在 `<body>` 下），而非嵌套关系，因此 `~` 选择器可以正确工作：

```
<body>
  <div id="chat-log">...</div>
  <input type="checkbox" id="plan-collapse-toggle" />  ← 兄弟
  <div id="plan-floating-panel">                        ← ~ 选中
      <div id="plan-panel-body">                        ← 后代
```

选择器 `#plan-collapse-toggle:checked ~ #plan-floating-panel #plan-panel-body` 在 IE11 中完全有效。

**备选方案**（如果兄弟选择器距离过远导致 IE11 问题）：

退回到 C# DOM 操作方式 — 在 `UpdateFloatingPlanPanel` 中直接设置 `panelBody.style.display`。当前代码已经在这样做（行 1420-1424 的 `panelEl.className = ""`），所以这是零风险的退路。

### 2.6 C# 端修改 — BuildPlanStepsHtml

当前 `BuildPlanCardHtml`（行 1279-1326）生成的 HTML 用于消息流内的 plan-card 和浮动面板。重设计后，浮动面板不再使用 plan-card HTML，而是直接渲染步骤列表：

```csharp
/// <summary>
/// Build the steps-only HTML for the floating plan panel (new design).
/// No plan-card wrapper — the floating panel provides its own chrome.
/// </summary>
private string BuildPlanStepsHtml(TaskPlan plan)
{
    if (plan?.Steps == null || plan.Steps.Count == 0)
        return string.Empty;

    var sb = new StringBuilder();
    foreach (var step in plan.Steps)
    {
        string icon;
        string cssClass;
        switch (step.Status)
        {
            case PlanStepStatus.InProgress:
                icon = "&#9654;";   // ▶ (solid right triangle)
                cssClass = "pp-inprogress";
                break;
            case PlanStepStatus.Completed:
                icon = "&#10003;";  // ✓ (checkmark)
                cssClass = "pp-completed";
                break;
            case PlanStepStatus.Failed:
                icon = "&#10007;";  // ✗ (X mark)
                cssClass = "pp-failed";
                break;
            default: // Pending
                icon = "&#9675;";   // ○ (circle)
                cssClass = "pp-pending";
                break;
        }
        var desc = System.Net.WebUtility.HtmlEncode(step.Description ?? "");
        sb.Append("<div class=\"pp-step ")
          .Append(cssClass)
          .Append("\"><span class=\"pp-step-icon\">")
          .Append(icon)
          .Append("</span><span class=\"pp-step-desc\">")
          .Append(desc)
          .Append("</span></div>");
    }
    return sb.ToString();
}
```

**注意**：使用 HTML 实体（`&#10003;`）而非 emoji，确保 IE11 渲染一致性。

### 2.7 C# 端修改 — UpdateFloatingPlanPanel

替换当前实现（行 1376-1445）：

```csharp
private void UpdateFloatingPlanPanel()
{
    if (_planHistory.Count == 0)
    {
        HideFloatingPlanPanel();
        return;
    }

    try
    {
        if (!_isBrowserReady || ChatBrowser.Document == null) return;
        dynamic doc = ChatBrowser.Document;

        // ── 计算进度 ──
        var completedCount = _lastPlan?.Steps?.Count(
            s => s.Status == PlanStepStatus.Completed) ?? 0;
        var totalCount = _lastPlan?.Steps?.Count ?? 0;
        var progressPercent = totalCount > 0
            ? (int)((completedCount * 100.0) / totalCount) : 0;

        // ── 更新标题栏元素 ──
        dynamic badgeEl = doc.getElementById("plan-badge");
        if (badgeEl != null)
            badgeEl.innerHTML = completedCount + "/" + totalCount;

        dynamic progressFill = doc.getElementById("plan-mini-progress-fill");
        if (progressFill != null)
            progressFill.style.width = progressPercent + "%";

        // ── Tabs（多 plan 时） ──
        dynamic tabsEl = doc.getElementById("plan-tabs-container");
        if (tabsEl != null)
        {
            if (_planHistory.Count > 1)
            {
                var tabsHtml = new StringBuilder();
                tabsHtml.Append("<div class='plan-tabs'>");
                for (int i = 0; i < _planHistory.Count; i++)
                {
                    var active = i == _planHistory.Count - 1 ? "active" : "";
                    tabsHtml.Append("<div class='plan-tab ")
                            .Append(active)
                            .Append("' onclick='showPlan(")
                            .Append(i)
                            .Append(")'>Plan ")
                            .Append(i + 1)
                            .Append("</div>");
                }
                tabsHtml.Append("</div>");
                tabsEl.innerHTML = tabsHtml.ToString();
            }
            else
            {
                tabsEl.innerHTML = "";
            }
        }

        // ── 步骤列表 ──
        dynamic stepsEl = doc.getElementById("plan-steps-list");
        if (stepsEl != null)
        {
            stepsEl.innerHTML = BuildPlanStepsHtml(_lastPlan);
        }

        // ── 显示面板 + 展开 ──
        dynamic panelEl = doc.getElementById("plan-floating-panel");
        if (panelEl != null)
            panelEl.style.display = "block";

        // 展开：取消 checkbox 的 checked 状态
        dynamic toggleCb = doc.getElementById("plan-collapse-toggle");
        if (toggleCb != null)
            toggleCb.checked = false;

        // ── 调整聊天区域 padding ──
        dynamic chatLog = doc.getElementById("chat-log");
        if (chatLog != null)
            chatLog.style.paddingBottom = "200px";
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[AICA] Error updating floating plan panel: {ex.Message}");
    }
}
```

### 2.8 C# 端修改 — CollapsePlanPanel / HidePlanPanel

```csharp
private void CollapsePlanPanel()
{
    try
    {
        if (!_isBrowserReady || ChatBrowser.Document == null) return;
        dynamic doc = ChatBrowser.Document;
        // 折叠：设置 checkbox checked
        dynamic toggleCb = doc.getElementById("plan-collapse-toggle");
        if (toggleCb != null) toggleCb.checked = true;
        dynamic chatLog = doc.getElementById("chat-log");
        if (chatLog != null) chatLog.style.paddingBottom = "48px";
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[AICA] Error collapsing plan panel: {ex.Message}");
    }
}

// HidePlanPanel 保持不变（隐藏整个面板 display:none）
```

### 2.9 adjustChatPadding JS 更新

替换当前 `adjustChatPadding`（行 3408-3421），适配 checkbox hack：

```javascript
function adjustChatPadding() {
    var panel = document.getElementById('plan-floating-panel');
    var container = document.getElementById('chat-log');
    var toggle = document.getElementById('plan-collapse-toggle');
    if (!container) return;
    if (!panel || panel.style.display === 'none') {
        container.style.paddingBottom = '20px';
    } else if (toggle && toggle.checked) {
        // collapsed
        container.style.paddingBottom = '48px';
    } else {
        setTimeout(function() {
            if (panel) container.style.paddingBottom = (panel.offsetHeight + 8) + 'px';
        }, 50);
    }
}
```

**注意**：`togglePlanPanel` 函数可以删除 — label + checkbox 已替代其功能。但如果需要 C# 端程序化控制折叠（如 Complete 时自动折叠），保留 `CollapsePlanPanel` 通过 DOM 操作 checkbox.checked 即可。

### 2.10 _planHistory 存储变更

当前 `_planHistory` 存储的是 `BuildPlanCardHtml` 生成的完整 HTML 字符串。重设计后改为存储 `BuildPlanStepsHtml` 的输出（纯步骤列表 HTML）。

需要修改的位置：
- 行 1709: `var planHtml = BuildPlanCardHtml(...)` → `var planHtml = BuildPlanStepsHtml(step.Plan)`
- 行 1743: `var completedPlanHtml = BuildPlanCardHtml(...)` → `var completedPlanHtml = BuildPlanStepsHtml(_lastPlan)`
- `showPlan(index)` JS 函数中 `#plan-content > div` → `#plan-steps-list` 内切换

### 2.11 视觉效果对比

| 元素 | 当前 | 重设计 |
|------|------|--------|
| 标题栏 | `📋 Task Plan (N)` 白色文字 | `📋 执行计划` + `2/4` 绿色徽章 + 迷你进度条 + `▼` |
| 边框色 | #e06c75 (红色) | #89b4fa (蓝色) |
| 面板背景 | #1e1e1e | #2a2a3c (略偏紫的深灰) |
| 折叠机制 | JS toggleClass | CSS checkbox hack |
| 步骤图标 | emoji (⏳🔄✅❌) | HTML 实体 (○▶✓✗) |
| 进行中步骤 | 无高亮 | 蓝色背景 + 粗体 |
| 已完成步骤 | 灰色文字 | 删除线 + 灰色 |

---

## 3. Part B：LLM 自动标记步骤完成

### 3.1 设计目标

在 AgentExecutor 的主执行循环中，每轮工具调用完成后，用一次轻量 LLM 调用判断当前执行进度，自动标记 plan 步骤状态，通过 `AgentStep.PlanUpdated` 通知 UI 更新浮动面板。

### 3.2 架构位置

```
AgentExecutor.ExecuteAsync() 主循环
    │
    ├─ ... 工具执行 (行 269-388) ...
    │
    ├─ yield AgentStep.WithToolResult(...)
    │
    ├─ ◆◆◆ 新增插入点 ◆◆◆
    │  └─ PlanProgressTracker.EvaluateProgressAsync(plan, recentToolResults)
    │      ├─ 轻量 LLM 调用（无 tools，纯文本判断）
    │      ├─ 解析 JSON 输出 → 更新 PlanStep.Status
    │      └─ 如果有状态变更 → yield AgentStep.PlanUpdated(updatedPlan)
    │
    └─ continue → 下一轮迭代
```

### 3.3 新增类：PlanProgressTracker

```csharp
namespace AICA.Core.Agent
{
    /// <summary>
    /// Lightweight LLM-based tracker that evaluates which plan steps
    /// have been completed after each tool execution round.
    /// Runs as a fire-and-forget side-channel — failures never block
    /// the main execution loop.
    /// </summary>
    public class PlanProgressTracker
    {
        private readonly ILLMClient _llmClient;
        private const int MaxPromptTokens = 1500;  // 硬上限
        private const int TimeoutSeconds = 10;

        public PlanProgressTracker(ILLMClient llmClient)
        {
            _llmClient = llmClient
                ?? throw new ArgumentNullException(nameof(llmClient));
        }

        /// <summary>
        /// Evaluate progress and return an updated plan if any steps changed.
        /// Returns null if no changes or on any error.
        /// </summary>
        public async Task<TaskPlan> EvaluateProgressAsync(
            TaskPlan currentPlan,
            List<ToolExecutionSummary> recentTools,
            CancellationToken ct)
        {
            if (currentPlan?.Steps == null || currentPlan.Steps.Count == 0)
                return null;
            if (recentTools == null || recentTools.Count == 0)
                return null;

            // Skip if all steps already completed/failed
            if (currentPlan.Steps.All(s =>
                s.Status == PlanStepStatus.Completed ||
                s.Status == PlanStepStatus.Failed))
                return null;

            try
            {
                using (var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                    return await RunEvaluationAsync(
                        currentPlan, recentTools, cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] PlanProgressTracker failed: {ex.Message}");
                return null; // 静默失败，不影响主流程
            }
        }

        private async Task<TaskPlan> RunEvaluationAsync(
            TaskPlan currentPlan,
            List<ToolExecutionSummary> recentTools,
            CancellationToken ct)
        {
            var prompt = BuildEvaluationPrompt(currentPlan, recentTools);

            var messages = new List<ChatMessage>
            {
                ChatMessage.System(EvaluationSystemPrompt),
                ChatMessage.User(prompt)
            };

            // 无 tools 调用 — 纯文本输出
            var responseText = new StringBuilder();
            await foreach (var chunk in _llmClient
                .StreamChatAsync(messages, null, ct)
                .ConfigureAwait(false))
            {
                if (chunk.Type == LLMChunkType.Text && chunk.Text != null)
                    responseText.Append(chunk.Text);
            }

            return ParseEvaluationResponse(
                currentPlan, responseText.ToString());
        }

        // ── Prompt ──

        private const string EvaluationSystemPrompt =
            "You are a progress tracker. Given a plan and recent tool execution results, " +
            "determine which steps have been completed or are in progress. " +
            "Respond with ONLY a JSON array of step status updates. " +
            "Each element: {\"step\": <1-based index>, \"status\": \"completed\"|\"in_progress\"|\"failed\"}. " +
            "Only include steps whose status has CHANGED. " +
            "If no steps changed, respond with an empty array: []";

        private string BuildEvaluationPrompt(
            TaskPlan plan,
            List<ToolExecutionSummary> recentTools)
        {
            var sb = new StringBuilder();

            // Plan steps with current status
            sb.AppendLine("## Current Plan");
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                sb.Append(i + 1).Append(". [")
                  .Append(plan.Steps[i].Status.ToString().ToUpper())
                  .Append("] ")
                  .AppendLine(plan.Steps[i].Description);
            }

            // Recent tool executions (truncated summaries)
            sb.AppendLine();
            sb.AppendLine("## Recent Tool Executions");
            foreach (var tool in recentTools)
            {
                sb.Append("- ").Append(tool.ToolName);
                if (tool.Success)
                    sb.Append(" ✓");
                else
                    sb.Append(" ✗");
                if (!string.IsNullOrEmpty(tool.Summary))
                    sb.Append(": ").Append(tool.Summary);
                sb.AppendLine();
            }

            // Token 安全：如果 prompt 超长则截断工具摘要
            var result = sb.ToString();
            if (result.Length > MaxPromptTokens * 4) // ~4 chars/token
                result = result.Substring(0, MaxPromptTokens * 4);

            return result;
        }

        // ── Response 解析 ──

        private TaskPlan ParseEvaluationResponse(
            TaskPlan currentPlan, string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            // 提取 JSON 数组（可能被 markdown 代码块包裹）
            var json = response.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('[');
                var end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                    json = json.Substring(start, end - start + 1);
            }

            List<StepStatusUpdate> updates;
            try
            {
                updates = System.Text.Json.JsonSerializer
                    .Deserialize<List<StepStatusUpdate>>(json);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] PlanProgressTracker: failed to parse: {json}");
                return null;
            }

            if (updates == null || updates.Count == 0)
                return null;

            // 创建新的 plan（不可变模式 — 不修改原 plan）
            var newSteps = currentPlan.Steps
                .Select(s => new PlanStep
                {
                    Description = s.Description,
                    Status = s.Status
                })
                .ToList();

            bool anyChanged = false;
            foreach (var update in updates)
            {
                int idx = update.Step - 1; // 1-based → 0-based
                if (idx < 0 || idx >= newSteps.Count) continue;

                var newStatus = ParseStatus(update.Status);
                if (newStatus == null) continue;

                // 防止降级：已完成的不能回到 pending/in_progress
                if (newSteps[idx].Status == PlanStepStatus.Completed)
                    continue;
                if (newSteps[idx].Status == PlanStepStatus.Failed)
                    continue;

                if (newSteps[idx].Status != newStatus.Value)
                {
                    newSteps[idx].Status = newStatus.Value;
                    anyChanged = true;
                }
            }

            if (!anyChanged) return null;

            return new TaskPlan
            {
                Steps = newSteps,
                Explanation = currentPlan.Explanation
            };
        }

        private PlanStepStatus? ParseStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return null;
            switch (status.ToLowerInvariant())
            {
                case "completed": return PlanStepStatus.Completed;
                case "in_progress": return PlanStepStatus.InProgress;
                case "failed": return PlanStepStatus.Failed;
                default: return null;
            }
        }

        // ── 内部 DTO ──

        private class StepStatusUpdate
        {
            [System.Text.Json.Serialization.JsonPropertyName("step")]
            public int Step { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("status")]
            public string Status { get; set; }
        }
    }

    /// <summary>
    /// Lightweight summary of a tool execution for plan progress evaluation.
    /// </summary>
    public class ToolExecutionSummary
    {
        public string ToolName { get; set; }
        public bool Success { get; set; }
        public string Summary { get; set; } // ≤200 chars
    }
}
```

### 3.4 AgentExecutor 集成

在 `AgentExecutor.cs` 主循环中，工具执行完成后（行 388 `}` 闭合 `foreach` 之后）插入进度评估：

```csharp
// ── 字段新增 ──
private PlanProgressTracker _planTracker;
private TaskPlan _currentPlan;
private List<ToolExecutionSummary> _recentToolSummaries
    = new List<ToolExecutionSummary>();

// ── 在 ExecuteAsync 开头，PlanAgent 成功后 ──
if (planResult.Success)
{
    // ... 现有代码 ...
    _currentPlan = ParsePlanFromText(planResult.PlanText);
    if (_currentPlan != null)
    {
        _planTracker = new PlanProgressTracker(_llmClient);
        yield return AgentStep.PlanUpdated(_currentPlan);
    }
}

// ── 在工具执行 foreach 内部，每个工具完成后 ──
// (行 367 yield return AgentStep.WithToolResult 之后)
_recentToolSummaries.Add(new ToolExecutionSummary
{
    ToolName = toolCall.Name,
    Success = result.Success,
    Summary = TruncateForSummary(
        result.Success ? result.Content : result.Error, 200)
});

// ── 在工具 foreach 之后（行 388 之后），每轮迭代结束前 ──
if (_planTracker != null && _currentPlan != null
    && _recentToolSummaries.Count > 0)
{
    var updatedPlan = await _planTracker.EvaluateProgressAsync(
        _currentPlan, _recentToolSummaries, ct)
        .ConfigureAwait(false);

    if (updatedPlan != null)
    {
        _currentPlan = updatedPlan;
        yield return AgentStep.PlanUpdated(updatedPlan);
    }

    _recentToolSummaries.Clear();
}

// ── 辅助方法 ──
private static string TruncateForSummary(string text, int maxLen)
{
    if (string.IsNullOrEmpty(text)) return "";
    return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
}
```

### 3.5 ParsePlanFromText — 从 PlanAgent 文本输出解析 TaskPlan

当前 PlanAgent 输出 markdown 文本（`## Steps\n1. ...\n2. ...`），需要解析为 `TaskPlan`：

```csharp
/// <summary>
/// Parse the structured markdown plan text from PlanAgent into a TaskPlan.
/// Extracts numbered steps from the "## Steps" section.
/// </summary>
private static TaskPlan ParsePlanFromText(string planText)
{
    if (string.IsNullOrWhiteSpace(planText)) return null;

    var plan = new TaskPlan { Explanation = planText };
    var lines = planText.Split('\n');
    bool inSteps = false;

    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();

        if (line.StartsWith("## Steps", StringComparison.OrdinalIgnoreCase))
        {
            inSteps = true;
            continue;
        }

        if (inSteps)
        {
            // Stop at next heading
            if (line.StartsWith("##"))
                break;

            // Match numbered list: "1. description" or "- description"
            var match = System.Text.RegularExpressions
                .Regex.Match(line, @"^(?:\d+[\.\)]\s*|-\s+)(.+)$");
            if (match.Success)
            {
                plan.Steps.Add(new PlanStep
                {
                    Description = match.Groups[1].Value.Trim(),
                    Status = PlanStepStatus.Pending
                });
            }
        }
    }

    return plan.Steps.Count > 0 ? plan : null;
}
```

### 3.6 Token 开销估算

| 组件 | 输入 tokens | 输出 tokens | 总计/次 |
|------|------------|------------|---------|
| System prompt | ~80 | — | 80 |
| Plan steps (6 步典型) | ~120 | — | 120 |
| Tool summaries (3 个工具/轮) | ~200 | — | 200 |
| JSON response | — | ~50 | 50 |
| **合计/每轮** | **~400** | **~50** | **~450** |

对于一个 10 轮迭代的复杂任务：
- 10 轮 × 450 tokens = **~4,500 tokens** 总额外开销
- 占主对话总 token 预算（128K 典型）的 **~3.5%**
- 每轮额外延迟：~1-2s（无 tools，短 prompt，快速返回）

### 3.7 频率控制策略

为避免频繁的进度评估 LLM 调用，添加以下控制：

```csharp
// PlanProgressTracker 内部
private int _evaluationCount;
private int _minToolCallsBetweenEvaluations = 2;

public async Task<TaskPlan> EvaluateProgressAsync(...)
{
    // 至少累积 N 个工具调用才评估一次
    if (recentTools.Count < _minToolCallsBetweenEvaluations)
        return null;

    // 上限：最多评估 N 次（对应 N 轮迭代）
    if (_evaluationCount >= 15)
        return null;

    _evaluationCount++;
    // ... 正常评估 ...
}
```

### 3.8 错误回退

| 失败场景 | 行为 |
|----------|------|
| LLM 调用超时（10s） | 返回 null，跳过此轮评估 |
| LLM 返回非 JSON | 返回 null，Debug.WriteLine 记录 |
| LLM 返回无效步骤索引 | 跳过该条更新 |
| LLM 尝试降级状态（completed→pending） | 防降级检查，跳过 |
| 整个 EvaluateProgressAsync 抛异常 | 外层 catch 返回 null |
| _currentPlan 为 null（非复杂任务没有 plan） | 不创建 tracker，跳过所有评估 |

**关键原则**：进度评估是纯 UI 增强，任何失败都不影响 AgentExecutor 的主执行流。

### 3.9 数据流完整路径

```
PlanAgent.GeneratePlanAsync() → PlanResult.PlanText (markdown)
    │
    ▼
ParsePlanFromText() → TaskPlan { Steps: [...], Explanation: "..." }
    │
    ▼
AgentStep.PlanUpdated(plan) → UI: UpdateFloatingPlanPanel()
    │                                ├─ BuildPlanStepsHtml(plan)
    │                                ├─ 更新 badge (2/4)
    │                                └─ 更新 progress bar (50%)
    │
    ▼ (每轮工具执行后)
PlanProgressTracker.EvaluateProgressAsync(plan, recentTools)
    ├─ LLM 调用 → JSON: [{"step":1,"status":"completed"}]
    ├─ 解析 → 新 TaskPlan（不可变）
    └─ yield AgentStep.PlanUpdated(updatedPlan) → UI 更新
    │
    ▼ (Complete 时)
Auto-complete all remaining steps → UI 最终更新
```

---

## 4. 集成点总结

| 位置 | 行号 | 变更类型 | 描述 |
|------|------|----------|------|
| `ChatToolWindowControl.xaml.cs` BuildPlanCardHtml | 1279-1326 | 保留 | 仍用于消息流内 plan-card（如果需要） |
| `ChatToolWindowControl.xaml.cs` 新增 BuildPlanStepsHtml | — | **新增** | 浮动面板专用步骤列表 HTML |
| `ChatToolWindowControl.xaml.cs` UpdateFloatingPlanPanel | 1376-1445 | **替换** | 新的标题栏/徽章/进度条更新逻辑 |
| `ChatToolWindowControl.xaml.cs` CollapsePlanPanel | 1447-1462 | **修改** | 改为操作 checkbox.checked |
| `ChatToolWindowControl.xaml.cs` BuildPageHtml CSS | 3303-3351 | **替换** | 新的 VS2022 暗色主题 CSS |
| `ChatToolWindowControl.xaml.cs` BuildPageHtml HTML | 3456-3465 | **替换** | 新的 checkbox hack HTML 结构 |
| `ChatToolWindowControl.xaml.cs` BuildPageHtml JS | 3401-3434 | **修改** | adjustChatPadding 适配 checkbox |
| `ChatToolWindowControl.xaml.cs` PlanUpdate case | 1708-1731 | **修改** | 改用 BuildPlanStepsHtml |
| `AgentExecutor.cs` 字段 | ~30 | **新增** | _planTracker, _currentPlan, _recentToolSummaries |
| `AgentExecutor.cs` PlanAgent 成功后 | ~84 | **新增** | ParsePlanFromText + PlanUpdated |
| `AgentExecutor.cs` 工具执行后 | ~370 | **新增** | 收集 ToolExecutionSummary |
| `AgentExecutor.cs` 迭代末尾 | ~390 | **新增** | EvaluateProgressAsync 调用 |
| `PlanProgressTracker.cs` | — | **新增** | 完整新文件 ~200 行 |

---

## 5. 实现阶段

| 阶段 | 范围 | 估计行数 |
|------|------|---------|
| 1: 浮动面板 UI 重设计 | CSS + HTML + BuildPlanStepsHtml + UpdateFloatingPlanPanel | ~150 修改 |
| 2: PlanProgressTracker | 新类 + ToolExecutionSummary | ~200 新增 |
| 3: AgentExecutor 集成 | ParsePlanFromText + 工具循环集成 | ~60 新增 |
| 4: 测试 | 面板渲染 + 进度跟踪 | ~50 |
| **总计** | | **~460 行** |

---

## 6. 风险评估

| 风险 | 严重度 | 缓解 |
|------|--------|------|
| CSS checkbox hack 在 IE11 中兄弟选择器距离过远 | MEDIUM | 备选：C# DOM 操作 fallback（2.5 节已说明） |
| LLM 进度评估返回错误状态 | LOW | 防降级 + 静默失败 + 手动 Complete 保底 |
| 额外 LLM 调用增加延迟 | LOW | 10s 超时 + 最少 2 次工具调用间隔 + 15 次上限 |
| 额外 token 开销 | LOW | ~4500 tokens/任务（<4%预算） |
| ParsePlanFromText 解析失败 | LOW | 返回 null → 不启用 tracker，退化为当前行为 |
| HTML 实体在 IE11 中渲染异常 | LOW | &#10003; 等基础实体 IE11 完全支持 |
