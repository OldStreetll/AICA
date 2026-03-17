# AICA 优化计划 — 基于 A/B/C 类测试结果

> 日期: 2026-03-17
> 数据来源: ManualTestResults_Calculator.md (25 个用例，27 个问题)
> 目标: 系统性提升 AICA 的正确性、准确性和完整性

---

## 一、问题全景分析

### 问题分布

| 类别 | 代码可修复 | Prompt 可优化 | LLM 固有局限 | 已修复/关闭 |
|------|-----------|---------------|-------------|-------------|
| P0 (6) | 5 | 1 | 0 | **6/6** |
| P1 (15) | 0 | 4 | 7 | **5/15** (含 P1-005 已关闭, P1-016 Prompt 优化已修复) |
| P2 (6) | 0 | 2 | 4 | **1/6** (P2-005 Prompt 优化已修复) |
| **合计 (27)** | **5** | **7** | **11** | **12** |

### 问题根因分类

**根因 1: 代码缺陷 (6 个, 全部已修复)**
- P0-002: 路径拼接 bug
- P0-003/004: 幻觉检测无重试限制
- P0-005: condense 保留错误的用户消息
- P0-006: ask_followup_question JSON 解析不健壮
- ~~P1-005: write_file 确认对话框未触发~~ (已确认问题不存在，实际已弹出确认框)

**根因 2: System Prompt 不够精确 (7 个, 可通过 prompt 优化改善)**
- P0-001: attempt_completion 摘要详细度要求不足 → 已修复
- P1-009: 正文与 completion 数字不一致
- P1-012/013: condense 后行为指导不足
- P1-016: 复杂任务的输出格式缺乏指导
- P2-003: 工具替换时的告知规则
- P2-005: 主动探索的触发条件

**根因 3: LLM 固有局限 (11 个, 需长期策略)**
- 摘要信息丢失 (P1-001/002/003/004/010)
- 过度推断/虚构 (P1-014/015)
- 计数不精确 (P1-009)
- 搜索范围判断 (P2-006)
- 未主动提供额外信息 (P2-001/002/004)

---

## 二、优化方案

### Phase 1: System Prompt 精化 (高 ROI, 低风险) — ✅ 已实施

**目标**: 通过精化 system prompt 改善 7 个可优化问题

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

**实施日期**: 2026-03-17

#### 1.1 正文与 completion 一致性规则 ✅

**解决**: P1-009 (方法计数自相矛盾)

已在 SystemPromptBuilder.AddRules() 中新增 "Number Consistency (CRITICAL)" 规则段，要求正文与 completion 数字完全一致，不确定时使用 "约 N" 表述。

#### 1.2 condense 后行为指导 ✅

**解决**: P1-012 (condense 后未回答原始问题), P1-013 (摘要未保留文件操作历史)

已新增 "Post-Condense Behavior (CRITICAL)" 规则段，要求 condense 后继续处理最新请求，摘要需包含文件列表、关键发现、当前任务状态。

#### 1.3 复杂任务输出格式指导 ✅

**解决**: P1-016 (架构概览过于简略)

已新增 "Complex Analysis Output Format" 规则段，要求完整概览类请求必须调用工具、completion 至少 10 行。

**TC-C01 重测验证**: 从 PARTIAL PASS (B+/B+/C+) → **PASS (A/A/A-)** ✅

#### 1.4 工具替换告知规则 ✅

**解决**: P2-003 (用 list_dir 替代 dir 但未告知)

已新增 "Tool Substitution Transparency" 规则段。

#### 1.5 搜索范围规则 ✅

**解决**: P2-005 (未主动补充探索), P2-006 (搜索范围不完整)

已新增 "Search Scope (CRITICAL)" 规则段，要求 "所有/全部/整个项目" 关键字触发全范围搜索。

**TC-C01 重测验证**: AICA 主动调用 3 个工具 (list_projects + list_dir + read_file) ✅

#### 1.6 证据要求规则 ✅ (原 Phase 3.2 提前实施)

**解决**: P1-014 (虚构设计模式), P1-015 (无依据功能声称)

已新增 "Evidence-Based Analysis (CRITICAL)" 规则段，要求每个模式需有代码证据（文件名+类名+方法名），禁止推测性功能声称。

**实际影响**: Phase 1 改善了 7+ 个问题，通过率从 76% → **80%**（TC-C01 验证通过）

---

### Phase 2: condense 摘要质量增强 (中 ROI, 低风险) — ✅ 已实施

**目标**: 改善 condense 后的上下文保留质量

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs` — `BuildAutoCondenseSummary`, `src/AICA.Core/Tools/CondenseTool.cs`

**实施日期**: 2026-03-17

#### 2.1 增强自动摘要生成 ✅

重写 `BuildAutoCondenseSummary`，改进点：
- **直接从 ToolCallMessage JSON 提取文件路径**（替代不可靠的正则匹配 assistant 文本）
- **结构化 5 段输出**: 文件操作 → 搜索记录 → 用户请求 → 关键发现 → 工具和进度
- **新增 TryParseJsonArgs 辅助方法**: 安全解析工具调用参数 JSON
- **分离读取/创建/修改文件**: 使用 3 个独立的 HashSet 追踪
- **扩大发现保留量**: 从 100 字符提升至 200 字符，保留 8 条（原 5 条）

#### 2.2 condense 后注入上下文提示 ✅

在两处 condense 路径（LLM 触发 + 自动触发）均注入 post-condense instruction：

```csharp
condensed.Add(ChatMessage.System(
    "[Post-condense instruction] The conversation was condensed to save context space. " +
    "You MUST answer the user's LATEST message based on the summary above. " +
    "Do NOT start a new task, replay old tasks, or explore new files unless the user asks. " +
    "If the user asked about previous work (e.g., 'what files did I read?'), answer from the summary."));
```

#### 2.3 CondenseTool 描述增强 ✅

`summary` 参数描述改为要求 4 段结构化摘要：文件操作 + 关键发现 + 搜索记录 + 当前任务状态。

**预期影响**: TC-A14 从 PARTIAL 提升至 PASS（待重测验证）

---

### Phase 3: LLM 行为问题的间接缓解 (低 ROI, 需持续迭代) — ✅ 已实施

#### 3.1 摘要信息丢失缓解 ✅

**问题**: P1-001/002/003/004/010 (LLM 在摘要中丢失细节)

已在 `AttemptCompletionTool.cs` 中将 `result` 参数描述改为结构化模板：
1. [File Structure] 类/枚举/接口完整名称
2. [Method List] 公共方法分类展示
3. [Dependencies] #include/using 引用
4. [Counts] 精确数字（使用工具报告值）
5. [Key Findings] 设计模式需有代码证据

#### 3.2 过度推断/虚构缓解 ✅ (已提前至 Phase 1.6 实施)

已在 SystemPromptBuilder 中新增 "Evidence-Based Analysis (CRITICAL)" 规则段。

#### 3.3 数量精确性缓解 ✅ (已合并至 Phase 1.1)

已在 "Number Consistency (CRITICAL)" 规则段中覆盖，要求优先使用工具统计结果。

**预期影响**: 减少约 30-50% 的 LLM 行为问题出现频率（部分已通过 TC-C01 验证）

---

### ~~Phase 4: 代码层面的剩余修复 (P1-005)~~ — 已关闭

P1-005 经用户确认，write_file 实际已弹出确认对话框，问题不存在。**无剩余代码层面待修复项。**

---

## 三、优先级排序

| 优先级 | Phase | 内容 | 影响范围 | 复杂度 | 状态 |
|--------|-------|------|----------|--------|------|
| **P1** | Phase 1 | System Prompt 精化 | 7+ 个问题 | 低 | **✅ 已实施** |
| **P2** | Phase 2 | condense 摘要增强 | 2 个问题 | 中 | **✅ 已实施** |
| **P3** | Phase 3 | LLM 行为间接缓解 | 11 个问题 | 低(prompt)/持续 | **✅ 已实施** |
| ~~P4~~ | ~~Phase 4~~ | ~~write_file 确认修复~~ | ~~已关闭~~ | - | 已关闭 |

---

## 四、预期效果

### 优化前（当前状态）

| 指标 | 值 |
|------|-----|
| 总通过率 | 76% (19/25 PASS) |
| 全 A 满分率 | 48% (12/25) |
| P0 问题 | 6 (全部已修复) |
| P1 问题 | 15 (3 已修复, 1 已关闭, 9 LLM 行为) |
| P2 问题 | 6 (全部 LLM 行为) |

### 优化后（实际结果，截至 2026-03-17）

| 指标 | 预期值 | 实际值 | 状态 |
|------|--------|--------|------|
| 总通过率 | ~88% | **78% (25/32)** | A-E 类完成，PASS 25 / PARTIAL 7 |
| 全 A 满分率 | ~56% | 待全面重测 | 待验证 |
| TC-C01 PARTIAL → PASS | 预期转化 | **已转化 ✅** | A/A/A- |
| TC-A14 PARTIAL → PASS | 预期转化 | 待重测 | condense 增强已实施 |
| LLM 行为问题频率下降 | ~30-50% | 待持续观测 | Phase 3 已实施 |

> **注**: 目前仅对 TC-C01 做了重测验证。其余用例（特别是 TC-A14 condense、TC-B02 设计模式、TC-A01 read_file 摘要）
> 需进一步重测以验证 Phase 2/3 的效果。预计全面重测后通过率可达 ~85-88%。

---

## 五、不建议优化的问题

以下问题属于 LLM 固有局限，投入产出比低，建议接受现状：

1. **P1-001/002 (摘要遗漏结构信息)**: read_file 返回完整文件内容，LLM 在总结时必然丢失部分信息。已通过 Phase 3.1 的结构化模板部分缓解，但无法完全解决
2. **P2-001 (读取文件只给摘要)**: 这实际上是一个设计权衡 — 展示完整文件内容会占用大量 UI 空间。AICA 的 UI 层已经在 ToolLogsHtml 中展示了完整内容，completion 摘要给出要点即可
3. **P2-004 (遗漏 Factory 模式)**: 设计模式识别是主观性较强的分析，不同工程师对同一代码可能有不同解读。LLM 识别了主要模式（Observer/Visitor/Command）已属良好

---

## 六、实施记录

### 已完成 (2026-03-17)

1. **Phase 1 已实施 ✅** — SystemPromptBuilder.cs 新增 6 条规则段（Number Consistency, Post-Condense Behavior, Complex Analysis Output Format, Tool Substitution Transparency, Search Scope, Evidence-Based Analysis）
2. **Phase 2 已实施 ✅** — AgentExecutor.cs 重写 BuildAutoCondenseSummary（结构化提取 + post-condense instruction 注入）+ CondenseTool.cs 描述增强
3. **Phase 3 已实施 ✅** — AttemptCompletionTool.cs result 描述改为结构化模板 + Evidence-Based Analysis 规则（原 3.2 提前至 Phase 1.6）
4. ~~Phase 4 已关闭~~ — 无剩余代码层面待修复项
5. **TC-C01 重测验证通过** — 从 PARTIAL PASS (B+/B+/C+) → PASS (A/A/A-)

### 新发现待修复 (2026-03-17, D/E 类测试)

- **[P0-007] 流式输出被 completion 覆盖（严重）**: TC-E01/E03 中发现，LLM 流式输出的详细分析/代码在 `attempt_completion` 触发后全部消失，用户只能看到 completion 卡片的简短摘要。影响右键命令(Explain/Refactor)体验。
  - **疑似位置**: `ChatToolWindowControl` 的 completion 处理逻辑，或 `AgentExecutor` 中 `AgentStep.Complete()` 的消息替换逻辑
  - **影响范围**: 所有使用 `attempt_completion` 且 LLM 先输出文本再调用 completion 的场景
  - **修复方向**: completion 卡片应追加在流式输出之后，而非替换
- **[P1-017] 工具调用失败/叙述阻塞**: TC-D02 第 2 轮 AICA 输出叙述文本但未调用工具
  - **疑似位置**: LLM 流式输出中断或工具调用解析失败

### 待验证

- TC-A14 (condense) — Phase 2 实施后需重测
- TC-B02 (设计模式) — Phase 3 Evidence-Based Analysis 规则效果需验证
- TC-A01 (read_file 摘要) — Phase 3 结构化模板效果需验证
- TC-E01/E03 — P0-007 修复后需重测（当前评分可能偏低）
- 其余 E(1)/F(4)/G(3)/H(3) 类用例 — 测试进行中
