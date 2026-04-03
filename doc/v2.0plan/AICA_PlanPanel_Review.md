# AICA Planning 面板设计审查

> **审查人**: architect (reviewer)
> **日期**: 2026-04-02
> **文档**: AICA_PlanPanel_Design.md v1.0
> **结果**: **APPROVED** (全部 8 维度通过，含 2 条建议)

---

## 1. IE11 兼容性 — APPROVED

**分析**：
- CSS checkbox hack 使用 `~`（通用兄弟选择器）+ `:checked` 伪类，IE9+ 完全支持，IE11 无问题。
- 文档 2.5 节明确说明了 checkbox 和 panel 是兄弟元素的布局要求，且 `#plan-collapse-toggle:checked ~ #plan-floating-panel #plan-panel-body` 选择器链在 IE11 中有效。
- HTML 实体 `&#10003;` `&#9675;` `&#9654;` `&#10007;` 替代 emoji，避免了 IE11 emoji 渲染不一致问题。这是一个很好的改进。
- `transition: width 0.3s ease` 用于进度条动画 — IE10+ 支持 CSS transitions，IE11 完全兼容。
- 备选方案（2.5 节末尾）指出如果兄弟选择器距离过远可退回 C# DOM 操作，零风险退路合理。

**验证**：当前代码（行 3365-3378）已有 IE-safe class helpers（`hasClass`/`addClass`/`removeClass`），新设计不依赖 `classList`，兼容性无回归。

---

## 2. LLM 标记可靠性 — APPROVED

**分析**：
- Prompt 设计简洁精准：system prompt 约 80 tokens，明确要求 JSON 数组输出，only changed steps。
- 解析容错好：
  - 支持 markdown 代码块包裹的 JSON（提取 `[` 到 `]` 区间）
  - JSON 反序列化失败 → `return null`
  - 无效步骤索引（out of range）→ `continue` 跳过
  - 空数组 `[]` → `return null`（无变更）
- **防降级保护**是关键亮点：`Completed` 和 `Failed` 状态不可被 LLM 回退，防止幻觉导致状态抖动。
- 不可变模式（`Select` 创建新 `PlanStep` 列表）符合项目编码规范。

**潜在风险及缓解**：LLM 可能返回非预期格式（如自然语言解释+JSON混合），但 `Substring(start, end - start + 1)` 提取逻辑可以处理这种情况。10s 超时保底。

---

## 3. Token 开销 — APPROVED

**分析**：
- 文档 3.6 节估算：每轮 ~450 tokens（400 input + 50 output），10 轮任务总计 ~4,500 tokens。
- 对比主对话预算（128K 典型），占比 ~3.5%，完全可接受。
- 频率控制（3.7 节）进一步限制：
  - `_minToolCallsBetweenEvaluations = 2`：至少累积 2 个工具调用才评估
  - `_evaluationCount >= 15`：最多 15 次评估，硬上限 ~6,750 tokens
- `MaxPromptTokens = 1500` 硬上限 + prompt 截断逻辑（`result.Length > MaxPromptTokens * 4`），防止极端场景。

**结论**：token 开销在可接受范围内，频率控制策略合理。

---

## 4. 性能影响 — APPROVED

**分析**：
- 每轮额外 LLM 调用延迟 ~1-2s（无 tools，短 prompt），对于本身就需要 5-15s 工具执行的迭代轮次来说影响较小。
- 10s 超时保护，超时即跳过，不阻塞主循环。
- 频率控制确保不会每个工具调用都触发评估。

**建议（非阻塞）**：当前同步 await 设计是正确的——fire-and-forget 异步无法在正确时机 yield PlanUpdated step。

---

## 5. 与 v1.1 集成 — APPROVED

**分析**：
- v1.1 `IncrementalRenderer` 方案中，`PlanUpdate` 步骤处理方式是 `UpdateFloatingPlanPanel()`（不进入增量渲染流），本设计完全兼容这一路径。
- 本设计重写了 `UpdateFloatingPlanPanel` 的内部实现但保持了相同的调用接口。
- `PlanUpdate` case 中仍调用 `UpdateFloatingPlanPanel()`，但不再调用 `RenderStructuredStreaming`，与 v1.1 设计一致。
- `_planHistory` 存储内容从 `BuildPlanCardHtml` 输出改为 `BuildPlanStepsHtml` 输出，需确保 `showPlan(index)` JS 函数适配。文档已提及此点。

---

## 6. 面板 UX — APPROVED

**分析**：
- 信息密度提升明显：标题栏同时展示图标 + 标题 + 进度徽章 + 迷你进度条 + 折叠箭头。
- 配色方案从红色改为蓝色更符合 VS2022 暗色主题。
- 步骤状态视觉区分合理（进行中=蓝色高亮，已完成=删除线灰色，待执行=灰色，失败=红色）。
- `adjustChatPadding` JS 适配了 checkbox hack，动态调整合理。

---

## 7. 边界条件 — APPROVED

**分析**：

| 边界条件 | 设计处理 | 评估 |
|----------|----------|------|
| 无计划 | 不创建 tracker | OK |
| 计划失败 | 退化为当前行为 | OK |
| 步骤数 = 0 | ParsePlanFromText 返回 null | OK |
| 步骤数 = 1 | 正常工作 | OK |
| 步骤数 20+ | max-height: 40vh + overflow-y: auto | OK |
| LLM 超时 | 10s cancel → return null | OK |
| LLM 返回非 JSON | catch → return null | OK |
| Complete 时未全部标记 | auto-complete 保底 | OK |
| 新对话切换 | HideFloatingPlanPanel 隐藏 | OK |
| 多 Plan tab | tab 逻辑保持 | OK |

**建议（非阻塞）**：`_recentToolSummaries.Clear()` 应仅在评估实际执行后清空，避免频率控制跳过时丢弃已积累的工具摘要。

---

## 8. 代码复杂度 — APPROVED

**分析**：
- 总新增/修改约 460 行，分布合理。
- `PlanProgressTracker` 独立类，单一职责。
- AgentExecutor 改动量合理（3 字段 + 辅助方法 + 评估调用），不改变主循环结构。
- 不可变模式贯穿设计。

---

## 总结

| 维度 | 结果 |
|------|------|
| 1. IE11 兼容性 | **APPROVED** |
| 2. LLM 标记可靠性 | **APPROVED** |
| 3. Token 开销 | **APPROVED** |
| 4. 性能影响 | **APPROVED** |
| 5. 与 v1.1 集成 | **APPROVED** |
| 6. 面板 UX | **APPROVED** |
| 7. 边界条件 | **APPROVED** |
| 8. 代码复杂度 | **APPROVED** |

**最终结论：APPROVED — 可以进入实现阶段。**

### 建议（非阻塞，可在实现时处理）

1. **`_recentToolSummaries.Clear()` 时机**：建议仅在评估实际执行后清空，避免频率控制跳过评估时丢弃已积累的工具摘要。
2. **showPlan(index) JS 函数选择器更新**：实现时需确保从 `#plan-content > div` 改为适配新 `#plan-steps-list` 结构。
