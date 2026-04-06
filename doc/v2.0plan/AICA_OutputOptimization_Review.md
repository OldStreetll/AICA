# AICA 输出优化设计方案 — 架构审查报告

> **审查人**: Architect / Reviewer
> **日期**: 2026-04-02
> **设计文档版本**: v1.0
> **审查结论**: **APPROVED** (9/9 APPROVED)

---

## 审查摘要

设计方案整体思路正确，核心的 IncrementalRenderer + insertAdjacentHTML 增量追加策略在 MSHTML/IE11 环境中可行，且已有 UpdateFloatingPlanPanel 作为先例验证。方案保持了 VSIX UI 层变更、Core 层零修改的清晰边界。

但有 2 个维度需要修订后才能通过。

---

## 逐条审查

### 1. IE11 兼容性 — APPROVED

**审查依据**：设计文档 §3.1.1, §1.2；源码 ChatToolWindowControl.xaml.cs:839-863 (UpdateBrowserContent), 1376-1445 (UpdateFloatingPlanPanel)

**结论**：所有提议的 DOM API 在 IE11/MSHTML 中完全可用。

| API | IE 支持版本 | 已有使用先例 |
|-----|-----------|------------|
| `insertAdjacentHTML('beforeend', html)` | IE4+ | 无（新引入，但 IE 最早实现此 API） |
| `getElementById(id)` | IE5.5+ | UpdateFloatingPlanPanel:1412-1414 |
| `innerHTML` (get/set) | IE5+ | UpdateBrowserContent:857, UpdateFloatingPlanPanel:1416-1417 |
| `classList` | 未使用 | N/A |
| `appendChild` | IE5+ | 设计文档提及但实际用 insertAdjacentHTML |
| `scrollTo` | IE4+ | UpdateBrowserContent:885 |

设计文档正确地避免了 IE11 不支持的 ES6 特性（template literals, arrow functions, Promise）。所有 JS 通过 `execScript` 执行，使用的是 ES3/5 语法。CSS checkbox hack 是纯 CSS2.1，IE11 完全支持。

**一个小建议**（不影响审批）：§3.1.2 BeginStreamingMessage 使用了 `Guid.NewGuid().ToString("N").Substring(0, 8)` 生成 ID。这在 C# 侧没问题，但如果后续 JS 需要引用该 ID，注意 ID 不能以数字开头（HTML4 规范）。当前设计以 `streaming-msg-` 为前缀，所以没问题。

---

### 2. 线程安全 — APPROVED

**审查依据**：源码 ChatToolWindowControl.xaml.cs:1567-1579 (dispatcher capture + Invoke), 设计文档 §6.3

**结论**：线程安全性与现有代码一致。

当前架构中，所有 DOM 操作已经通过 `dispatcher.Invoke(new Action(() => { ... }))` 在 UI 线程上串行执行（1579 行）。IncrementalRenderer 的方法将在同一个 Invoke 块内被调用，因此：

- 所有 DOM 操作在 UI 线程上串行
- 无并发 DOM 访问风险
- `_incrementalRenderer` 的内部状态（`_streamingMsgId`, `_currentIterationId`, `_thinkingCounter`）只在 Invoke 块内读写，无竞态条件

IncrementalRenderer 作为实例字段存储在 ChatToolWindowControl 上，生命周期与单次 ExecuteAgentModeAsync 调用绑定，每次新调用创建新实例——这是正确的设计。

---

### 3. 向后兼容 — APPROVED

**审查依据**：设计文档 §7; 源码 RenderConversation (753-827), 会话切换/加载逻辑, ConversationMessage 序列化

**结论**：向后兼容性完整。

| 场景 | 影响分析 |
|------|---------|
| **会话切换/加载** | RenderConversation 保持不变，历史会话通过全量重建渲染。DomElementId 是运行时属性，不参与 ConversationStorage 序列化。 |
| **暂停/恢复** | 设计文档 §6.4 提到 FinalizeMessage + pause indicator。源码 1877-1908 的 pause 路径在 Complete 前中断，只需确保 IncrementalRenderer.FinalizeMessage() 在 pause catch 块中也被调用。 |
| **错误处理** | 源码 1837-1872 的 Error case 目前调用 RenderConversation。设计方案应同样在 Error 路径调用 FinalizeMessage。 |
| **Chat Mode** | 使用 ExecuteChatModeAsync (1912+)，完全独立的代码路径，不受影响。 |
| **ConversationMessage** | DomElementId 不在 ConversationMessage 上，在 IterationBlock/ToolCallBlock 上，且不参与序列化。 |

**备注**：Error 和 Pause 路径的 FinalizeMessage 调用在设计文档 §3.2.1 的代码示例中未明确展示，但 §6.4 和 §6.1 的 fallback 策略覆盖了这些场景。建议实现时在 Error/Pause catch 块中显式调用 `_incrementalRenderer?.FinalizeMessage()`。

---

### 4. 数据一致性 — APPROVED

**审查依据**：设计文档 §3.2.1 Complete case; 源码 1733-1835 (Complete handler)

**结论**：增量 DOM 与 `_conversation` 列表的同步策略可靠。

关键设计决策：
- **流式期间**：DOM 由 IncrementalRenderer 增量管理，`iterationBlocks` / `toolCallBlocks` / `responseBuilder` 等内存数据结构继续维护（与现有代码相同）
- **Complete 时**：`_conversation.Add(new ConversationMessage { ... })` 的逻辑完全不变，finalContent 的构建使用的是内存中的 `responseBuilder` / `iterationBlocks` / `preToolContent`，不依赖 DOM 状态
- **FinalizeMessage 后**：streaming div 标记为完成，后续如果用户切换会话，RenderConversation 从 `_conversation` 全量重建，不依赖 IncrementalRenderer 状态

这种"DOM 是视图层，_conversation 是数据层"的分离是正确的。DOM 丢失（浏览器重载、fallback）不会导致数据丢失。

---

### 5. 性能 — NEEDS_REVISION

**审查依据**：设计文档 §1.1, §8; 源码 RenderStructuredStreaming (1991-2079)

**问题**：设计文档缺少定量的性能对比分析。

当前 `RenderStructuredStreaming` 的性能瓶颈清晰：每次 step 都重建 `_conversation` 全部历史（1996-2038 行的 for 循环）+ 所有 iteration blocks（2046-2066 行）。对于长对话（50+ 消息, 每条有多个工具调用），每次 step 可能重建数 KB HTML。

**但设计文档需要补充**：

1. **定量估算**：一个典型的 50 工具调用对话，当前 RenderStructuredStreaming 每次 step 重建的 HTML 大小是多少？增量方案每次 step 的 DOM 操作量是多少？至少给出数量级估算。
2. **innerHTML vs insertAdjacentHTML 在 MSHTML 中的性能特征**：insertAdjacentHTML 不会触发已有 DOM 节点的重解析，但 MSHTML 的实现可能有 quirks。是否需要一个简单的 benchmark？
3. **syntax highlighting 成本**：当前 `execScript` 在每次 UpdateBrowserContent 后对 ALL code blocks 运行 hljs（863 行）。设计文档 §6.6 提到 "HighlightNewCodeBlocks after each DOM append"，但没有具体实现。需要说明如何只高亮新增的 code blocks 而不重新高亮已有的。

**修改要求**：
- 添加一个"性能预期"小节，包含 (a) 典型场景的 HTML 重建量对比估算（当前 vs 增量），(b) HighlightNewCodeBlocks 的具体实现策略（建议：对新追加的 DOM 子树用 `querySelectorAll('pre code')` 过滤未标记 `hljs` class 的节点，与当前 863 行的逻辑类似但范围限定在新元素上）。

---

### 6. 边界条件 — APPROVED

**审查依据**：设计文档 §6; 源码各相关路径

| 边界条件 | 设计覆盖 | 验证 |
|---------|---------|------|
| **无工具调用** | TextChunk → AppendStreamingText / UpdateStreamingText | Complete 时 `hasToolCalls=false`，finalContent 来自 responseBuilder。IncrementalRenderer 的 streaming text div 已存在。OK |
| **无思考内容** | ActionStart 创建无 thinking 的 iteration | 源码 1600-1609 已有此路径，设计文档 §3.2.1 保持一致。OK |
| **计划失败** | PlanAgent 返回 Fail → 无 PlanUpdate step | PlanAgent.cs:107-113，fallback 到直接执行。IncrementalRenderer 不受影响。OK |
| **超时取消** | OperationCanceledException (non-pause) | 源码中未显式处理（目前也未处理）。这是现有行为，不是本次设计引入的。OK |
| **暂停恢复** | §6.4 FinalizeMessage + pause indicator | 见"向后兼容"部分的备注。OK |
| **单工具调用** | 与多工具调用路径相同 | OK |
| **多次 PlanUpdate** | 浮动面板 dedup 逻辑不变 (1711-1727) | OK |

---

### 7. LLM 计划标记 — APPROVED

**审查依据**：设计文档 §3.2.1 PlanUpdate case; 源码 AgentExecutor.cs:73-91, PlanAgent.cs

**结论**：设计方案不改变 LLM 计划相关逻辑。

- PlanAgent 在 Core 层运行，不受本次 UI 层变更影响
- AgentStep.PlanUpdate 的 yield 逻辑不变
- 浮动面板的更新逻辑（UpdateFloatingPlanPanel）不变
- 唯一变化是 PlanUpdate case 不再调用 RenderStructuredStreaming，而是仅更新浮动面板——这是正确的，因为 Plan 信息已经通过浮动面板展示

Token 开销方面：无新增 prompt、无额外 LLM 调用。

---

### 8. 代码复杂度 — APPROVED

**审查依据**：设计文档 §1.3, §8; IncrementalRenderer API surface

**结论**：复杂度适当，未过度设计。

- IncrementalRenderer 约 150-250 行，职责单一（管理一个 streaming message div 的增量 DOM 操作）
- API 方法与 AgentStepType 枚举一一对应，语义清晰
- Fallback 策略简单直接：DOM 操作失败 → 调用现有 RenderStructuredStreaming
- 保留 RenderStructuredStreaming 作为 Obsolete fallback 是务实的选择——可以在后续版本移除
- DomElementId 作为运行时属性添加到 IterationBlock/ToolCallBlock，不影响序列化，开销极小

没有更简单的实现路径能同时满足"增量追加"和"fallback 安全"这两个需求。当前设计是恰当的。

---

### 9. Planning 面板 — NEEDS_REVISION

**审查依据**：设计文档 §2.1, §3.2.1 PlanUpdate case; 源码 3456-3465 (plan panel HTML), 1376-1445 (UpdateFloatingPlanPanel), 3340-3400 (plan panel CSS)

**问题**：设计文档 §1.2 声称"Plan 只在浮动面板中展示"，但缺少对当前 Plan 在消息流中的渲染路径的清理说明。

当前实现中，Plan 卡片有两个展示位置：
1. 浮动面板（UpdateFloatingPlanPanel）— 通过 DOM 直接操作
2. 消息流中的 RenderStructuredStreaming 不直接渲染 Plan，但 PlanUpdate case（1708-1731）在调用 UpdateFloatingPlanPanel 之后还调用了 `RenderStructuredStreaming`

设计文档 §3.2.1 的 PlanUpdate case 正确移除了 RenderStructuredStreaming 调用。**但遗漏了以下情况**：

1. **Complete 时的 Plan 自动完成**（1733-1746）：当前代码在 Complete case 中修改 `_lastPlan` 的步骤状态并更新浮动面板。增量模式下，Complete 调用 FinalizeMessage 而非 RenderConversation（设计文档 §3.2.1 第 345-347 行）。但设计文档的 Complete 示例代码只写了 `_incrementalRenderer.FinalizeMessage()`，**没有展示 Plan 自动完成逻辑是否保留**。需要明确：Plan 自动完成 + UpdateFloatingPlanPanel 调用仍然在 Complete case 中执行，FinalizeMessage 在其之后调用。

2. **RenderConversation 中是否有 Plan 内联渲染**：检查源码 753-827，RenderConversation 不渲染 Plan（Plan 是通过 UpdateFloatingPlanPanel 独立管理的）。确认：当 Complete 调用 RenderConversation 时不会丢失 Plan。但设计文档说 Complete 不再调用 RenderConversation——那么浮动面板在会话切换后如何恢复？

3. **会话切换后 Plan 面板恢复**：当用户切换回一个有 Plan 的会话时，RenderConversation 被调用来重建聊天历史，但浮动面板的恢复依赖 `_planHistory` 和 `_lastPlan`（这些是内存状态）。如果用户切换到另一个会话再切换回来，`_planHistory` 会被重置吗？这是现有的问题，不是本次设计引入的，但设计文档应明确说明不改变此行为。

**修改要求**：
- 在 §3.2.1 的 Complete case 代码示例中，明确展示 Plan 自动完成逻辑 + UpdateFloatingPlanPanel 调用 + FinalizeMessage 的完整顺序
- 添加一段说明：Complete case 中，先执行 Plan 自动完成和面板更新（与现有逻辑一致），再调用 FinalizeMessage 标记 streaming div 完成，最后执行 _conversation 持久化和 RenderConversation（用于确保 DOM 状态与数据一致）
- 说明会话切换场景下 Plan 面板的行为不变

---

## 总结

| # | 维度 | 结论 |
|---|------|------|
| 1 | IE11 兼容性 | **APPROVED** |
| 2 | 线程安全 | **APPROVED** |
| 3 | 向后兼容 | **APPROVED** |
| 4 | 数据一致性 | **APPROVED** |
| 5 | 性能 | **NEEDS_REVISION** — 补充定量估算和 HighlightNewCodeBlocks 实现策略 |
| 6 | 边界条件 | **APPROVED** |
| 7 | LLM 计划标记 | **APPROVED** |
| 8 | 代码复杂度 | **APPROVED** |
| 9 | Planning 面板 | **NEEDS_REVISION** — Complete case 代码示例不完整，需明确 Plan 完成+FinalizeMessage+RenderConversation 顺序 |

**下一步**：~~设计师修订第 5 和第 9 条后，提交 v1.1 版本重新审查。~~ ✅ v1.1 已通过。

---

## v1.1 Re-Review (2026-04-02)

Designer addressed both NEEDS_REVISION items:

### #5 Performance — APPROVED (was NEEDS_REVISION)
- §6.7 adds quantitative estimates: ~65KB/step (current) vs ~0.5KB/step (incremental), ~130x improvement, O(N)→O(1) cost model
- §6.6 provides complete `HighlightNewCodeBlocks` implementation: scoped `querySelectorAll` on parent element, `hljs` class filter, ES3-compatible JS via `execScript`, correct call sites identified

### #9 Planning Panel — APPROVED (was NEEDS_REVISION)
- Complete case now shows full sequence: Plan auto-complete → UpdateFloatingPlanPanel → FinalizeMessage → _conversation persist → RenderConversation
- §2.1 data flow diagram updated to reflect this order
- §7 includes Plan panel session-switching note: `_planHistory` cleared on conversation load, panel hidden when empty

## Final Summary

| # | 维度 | 结论 |
|---|------|------|
| 1 | IE11 兼容性 | **APPROVED** |
| 2 | 线程安全 | **APPROVED** |
| 3 | 向后兼容 | **APPROVED** |
| 4 | 数据一致性 | **APPROVED** |
| 5 | 性能 | **APPROVED** (v1.1) |
| 6 | 边界条件 | **APPROVED** |
| 7 | LLM 计划标记 | **APPROVED** |
| 8 | 代码复杂度 | **APPROVED** |
| 9 | Planning 面板 | **APPROVED** (v1.1) |

**设计方案 v1.1 已通过全部审查，可以进入实现阶段。**
