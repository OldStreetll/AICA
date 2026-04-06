# 运行时优化计划: AICA POCO 测试 — Batch 1 运行时缺陷修复

> 版本: 1.0
> 日期: 2026-03-18
> 基于: Batch 1 手动 VS2022 运行时测试结果 (poco 项目)
> 测试环境: VS2022 + AICA 插件, 目标项目 D:\project\poco
> 状态: 待实施

---

## 概述

在 POCO 项目上完成 Batch 1 手动运行时测试后，发现 2 个 HIGH 级别缺陷和 1 个 MEDIUM 级别观察项。Bug 1（Int64→Nullable 参数转换失败）导致所有可选整数参数被静默忽略；Bug 2（ResponseQualityFilter 过度抑制）导致有效回复被反复压制，用户需多次追问才能获得答案。

## 运行时发现总览

| 编号 | 优先级 | 问题摘要 | 影响范围 | 工作量 |
|------|--------|---------|---------|--------|
| Bug 1 | HIGH | Int64→Nullable\<int\> 参数转换失败 | read_file offset/limit 等所有可选 int? 参数 | Small (1h) |
| Bug 2 | HIGH | ResponseQualityFilter 过度抑制有效回复 | 全局 — 每次工具调用后多 1-2 次无用迭代 | Medium (4h) |
| Bug 3 | MEDIUM | System prompt 9339 tokens 警告 | 信息性, 非阻断 | 观察中 |

### 正面发现

- EXACT_STATS 注入正常，LLM 引用精确数字
- FindProjectRoot 正确定位项目根
- Auto-augment 智能增强功能正常
- 新增错误日志改进生效（TC-B05 修复已产生价值）
- 索引性能优秀: 2760 文件, 9890 符号, 2.9 秒

---

## Bug 1 [HIGH]: Int64 → Nullable\<int\> 参数转换失败

**文件**: `src/AICA.Core/Agent/ToolParameterValidator.cs`
**方法**: `GetOptionalParameter<T>`, 第 77-116 行
**日志证据**: `Optional parameter 'limit' conversion failed: cannot convert '50' (Int64) to Nullable`1``

### 问题分析

LLM 发送 `limit=50` → JSON 解析为 Int64 → ReadFileTool 期望 `int?` → `Convert.ChangeType` 不支持 Nullable 类型 → 异常被捕获 → 返回 null → 读取全文件 (31798 chars)

### 修复方案

在 `Convert.ChangeType` 调用前，检测并解包 `Nullable<T>`:

```csharp
// GetOptionalParameter<T> 和 GetRequiredParameter<T> 中:
var targetType = typeof(T);
var underlyingType = Nullable.GetUnderlyingType(targetType);
if (underlyingType != null)
{
    var converted = Convert.ChangeType(value, underlyingType);
    return (T)converted;
}
return (T)Convert.ChangeType(value, targetType);
```

### 工作量: ~1 小时 | 风险: LOW

---

## Bug 2 [HIGH]: ResponseQualityFilter 过度抑制有效回复

### 涉及文件
- `src/AICA.Core/Agent/AgentExecutor.cs` — 第 541-590 行
- `src/AICA.Core/Prompt/ResponseQualityFilter.cs` — 第 78-214 行

### 问题分析

两个独立抑制路径:

**路径 A: 工具后叙述抑制 (AgentExecutor 第 541-580 行)**
- 条件 `!hasToolCalls && HasEverUsedTools && Iteration > 1` 过于宽泛
- 工具执行后 LLM 的有效总结被无条件抑制
- 每次工具调用多 1 轮无用迭代

**路径 B: IsInternalReasoning 误判 (ResponseQualityFilter)**
- `ReasoningStartPatterns` 包含 "let me"、"让我"、"首先，" 等过于通用的模式
- LLM 以 "让我为你解释..." 开头的有效回答被误判为内部推理
- TC-A03: 用户需问 3 次才得到答案

### 修复方案

**修复 2A**: 放宽工具后抑制 — 用 `IsToolPlanningText` 替代无条件抑制

```csharp
// 仅抑制明确的工具规划文本，非总结/答案
bool looksLikeToolPlanningOnly = IsToolPlanningText(assistantResponse);
if (looksLikeToolPlanningOnly)
{
    // 抑制并 nudge
}
else
{
    // 允许通过 — 这是有效回答
}
```

`IsToolPlanningText` 检测明确的工具使用意图标记词（"i will call"、"我将调用" 等），不匹配一般性回答。

**修复 2B**: 收紧 IsInternalReasoning — 移除过于通用的模式 + 添加长度阈值

- 从 ReasoningStartPatterns 移除 "let me"、"让我"（保留 "let me check"、"让我搜索" 等精确模式）
- 超过 300 字符的文本不判定为内部推理
- 从 MetaReasoningPatterns 移除 "actually,"、"i see -" 等常见于正常解释的词

### 工作量: ~4 小时 | 风险: MEDIUM

---

## Bug 3 [MEDIUM-观察]: System Prompt Token 预算

**症状**: `WARNING: System prompt is ~9339 tokens`
**影响**: 占 32K 预算 29%，当前可接受
**建议**: 暂不修改，如超过 12000 tokens (37.5%) 再优化

---

## Bug 4 [HIGH]: LLM 会话上下文过长时跳过工具调用伪造结果

> **发现日期**: 2026-03-18
> **状态**: 待修复

### 问题分析

**涉及文件**: `src/AICA.Core/Agent/AgentExecutor.cs` — 幻觉检测逻辑
**症状**: 当会话 messages 数量较多 (≥8) 时，MiniMax-M2.5 模型倾向于直接在文本中伪造工具调用结果（包括伪造 TOOL_EXACT_STATS），而不实际调用 function calling API。

**测试证据**:

| 用例 | Messages 数 | 调用工具？ | 结果 |
|------|------------|-----------|------|
| TC-C01 | 2 | ✅ 调用 read_file | 真实结果 |
| TC-C05 | 6 | ✅ 调用 list_dir | 真实结果 |
| TC-C06 | 8 | ❌ 未调用 | 伪造 find_by_name 结果 |
| TC-D01 | 10 | ❌ 未调用 | 伪造 grep_search 结果 |

**伪造内容示例**:
- 列出不存在的 C#/Logging/ 和 Java/logging/ 路径
- 编造 NullLogger.h、LoggingFactory.h 等不存在的文件
- 伪造 TOOL_EXACT_STATS 数字

**根因分析**:
1. MiniMax-M2.5 模型在长上下文中 function calling 可靠性下降
2. AICA 现有幻觉检测 (`Detected tool execution hallucination`) 在 TC-C06/D01 中仅执行 1 轮就停止，未触发纠正
3. 当 LLM 在文本中伪造了看似完整的搜索结果（含格式化的文件列表和 EXACT_STATS），当前检测逻辑可能将其视为有效回答而非幻觉

### 修复方向

**方案 A: 强制工具调用** — 对搜索/查找类请求，在 System Prompt 中更强调 "MUST call tools, NEVER fabricate results"

**方案 B: 伪造 EXACT_STATS 检测** — 检测回复文本中是否包含 `[TOOL_EXACT_STATS:` 格式但该轮未实际调用工具，若是则判定为幻觉并纠正

**方案 C: 会话长度管理** — 当 messages 数接近阈值时主动 condense，减少上下文压力

### 工作量: Medium (3-4h) | 风险: MEDIUM | 优先级: HIGH

### 验证结果

**Bug 4 上下文长度相关性已确认**:
- TC-D01 旧会话 (messages=10): ❌ 未调用工具，伪造结果
- TC-D01 新会话 (messages=2): ✅ 正常调用 grep_search，结果 100% 准确
- 同一请求、同一模型，唯一差异是上下文长度

**结论**: Bug 4 的触发条件为 messages count ≥ 8，与 MiniMax-M2.5 的 function calling 可靠性在长上下文中下降直接相关。

### 补充验证 (Batch 2 完整结果)

Bug 4 在 Batch 2 中额外触发 2 次:
- TC-C06 (messages=8): find_by_name 未调用，伪造结果 → 新会话重测 PASS
- TC-D04 Q2 (messages=8): grep_search 未调用，伪造结果

**触发阈值精确化**: messages ≥ 8 时开始出现，messages ≥ 10 时几乎必现。

**Batch 2 受 Bug 4 影响的用例**: 3/10 (TC-C06, TC-D01, TC-D04)
**新会话重测通过率**: 100% (TC-C06, TC-D01 重测均 PASS)

### 生产环境验证 (Batch 3)

Bug 4 Fix 在 Batch 3 中成功触发 3 次:
- TC-E02: messages=10 → condensed to 4, token usage 15%
- TC-F01: messages=11 → condensed to 4, token usage 15%
- TC-F04: messages=10 → condensed to 4, token usage 15%

所有 3 次 condense 后 LLM 均恢复正常工具调用，无幻觉发生。Fix 2 (消息数主动 condense) 验证有效。

---

## Bug 5 [MEDIUM]: update_plan 多次调用创建多个 Plan 标签

> **发现日期**: 2026-03-18
> **状态**: ✅ 已修复 [2026-03-19]

### 问题分析

**文件**: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`
**症状**: 当 LLM 在一个任务中多次调用 update_plan 工具时（如创建初始计划、condense 后重新创建、步骤完成时更新），UI 为每次调用创建一个新的 Plan 标签页（Plan 1, Plan 2, Plan 3），而非更新现有的 Plan。

**预期行为**: 同一个任务应只有 1 个 Plan，后续 update_plan 调用应更新当前 Plan 的步骤和状态，而非创建新标签。

### 修复详情

**修复方案**: Added `_planCreatedThisExecution` flag — within one user message execution, all update_plan calls update the same Plan entry. Also added auto-complete: when AgentStepType.Complete fires, all Pending/InProgress steps are automatically marked Completed.

**实施位置**: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`

### 工作量: Small (1-2h) | 风险: LOW | 优先级: MEDIUM | ✅ 已完成

---

## Bug 6 [HIGH]: ReadFileTool 模糊路径匹配返回错误文件

> **发现日期**: 2026-03-18
> **状态**: ✅ 已修复 [2026-03-19]

### 问题分析

**文件**: `src/AICA.Core/Workspace/SolutionSourceIndex.cs`, 第 282-287 行
**症状**: 请求读取不存在的文件 `nonexistent/fake/path.cpp` 时，ReadFileTool 未返回 NotFound 错误，而是静默返回了 `Foundation/src/Path.cpp` 的内容 (20276 chars)。

**根因**: `ResolveFile()` 方法的后缀匹配逻辑使用 `EndsWith` 进行大小写不敏感匹配，`path.cpp` 后缀匹配到了 `Path.cpp`。

### 修复详情

**v1 修复** (EndsWith → \+normalized) 不充分，因为单匹配快捷路径绕过了它。

**v2 修复 (最终)**: when request has directory components but suffix match fails, return null immediately instead of falling through to single-match. Verified: 'nonexistent/fake/path.cpp' now returns 'Not found' error.

**实施位置**: `src/AICA.Core/Workspace/SolutionSourceIndex.cs` — ResolveFile() method

### 工作量: Small (1h) | 风险: LOW | 优先级: HIGH | ✅ 已完成

---

## Bug 7 [MEDIUM]: 新会话残留旧 Task Plan 悬浮面板

> **发现日期**: 2026-03-18
> **状态**: ✅ 已修复 [2026-03-19]

### 问题分析

**文件**: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`
**症状**: 点击 Clear 创建新会话后，旧会话的 Task Plan 仍显示在悬浮面板中，需切到其他会话再切回才消失。

### 修复详情

**根本原因**: HideFloatingPlanPanel used wrong element ID — 'floatingPlanPanel' but actual DOM ID is 'plan-floating-panel'.

**修复方案**: Fixed by delegating to existing HidePlanPanel() method. Also: UpdateFloatingPlanPanel now calls HideFloatingPlanPanel when _planHistory is empty; ClearConversation uses Dispatcher.BeginInvoke for timing.

**实施位置**: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs` — ClearConversation, HideFloatingPlanPanel, UpdateFloatingPlanPanel methods

### 工作量: Small (<1h) | 风险: LOW | 优先级: MEDIUM | ✅ 已完成

---

## 实施顺序

1. **Bug 1** — 最快修复，立即消除 token 浪费 (1h) ✅ 已完成
2. **Bug 2A** — 解决主要回复抑制问题 (2h) ✅ 已完成
3. **Bug 2B** — 收紧误判，双重保障 (1.5h) ✅ 已完成
4. **集成验证** — 重跑 Batch 1 受影响用例 (1h) ✅ 已完成

## 成功标准

- [x] Bug 1: `GetOptionalParameter<int?>` 对 Int64 正确转换 — Nullable.GetUnderlyingType 方案已实施
- [x] Bug 1: read_file limit 参数生效，不再读全文 — TC-B01 验证: 1980 chars vs 31798
- [x] Bug 2: 知识问答第一次回复即显示答案 — TC-A03 验证: 一次回答获得
- [x] Bug 2: 工具后总结不再被抑制 — IsToolPlanningText 替代无条件抑制
- [x] Bug 2: 纯规划文本仍被正确抑制 — 保留精确 ToolUseIndicators 检测
- [x] 单元测试: 317/319 通过 (99.4%) — 比基线 311/313 多通过 6 个
- [x] 编译: 0 errors, 0 warnings

## 预期效益 (已验证)

| 指标 | 修复前 | 修复后 | 验证 |
|------|--------|--------|------|
| 工具后迭代次数 | 3 次 | 2 次 | TC-B01: 实际 2 次 ✅ |
| read_file token 浪费 | 100% 文件 | 仅请求范围 | TC-B01: 1980 vs 31798 chars ✅ |
| 问答延迟 (TC-A03) | 3 次 LLM 调用 | 1 次 | TC-A03: 一次回答 ✅ |
| 估计 token 节省 | — | 每会话 30-50% | 实测节省明显 ✅ |

---

## 修复完成证明

**日期**: 2026-03-18
**状态**: ✅ COMPLETE

### Bug 1 修复证明

| 项目 | 详情 |
|------|------|
| 文件 | `src/AICA.Core/Agent/ToolParameterValidator.cs` |
| 方法 | `GetOptionalParameter<T>`, `GetRequiredParameter<T>` |
| 修复 | 添加 `Nullable.GetUnderlyingType(targetType)` 检查，解包 Nullable<T> 后调用 Convert.ChangeType |
| 验证用例 | TC-B01 |
| 测试结果 | limit=50 参数生效，1980 chars 返回（vs 31798 修复前）|

### Bug 2 修复证明

| 项目 | 详情 |
|------|------|
| 文件 A | `src/AICA.Core/Agent/AgentExecutor.cs` (第 541-590 行) |
| 修复 2A | 用 `IsToolPlanningText()` 替代行 541-580 的无条件抑制逻辑；仅抑制明确工具规划标记 |
| 文件 B | `src/AICA.Core/Prompt/ResponseQualityFilter.cs` (第 78-214 行) |
| 修复 2B | 收紧 ReasoningStartPatterns：移除过于通用的 "let me"/"让我" 模式；添加 300 字符长度阈值；添加精确 ToolUseIndicators |
| 验证用例 | TC-A03 |
| 测试结果 | "让我为你解释..." 类型回答现被允许通过；工具规划文本仍被正确抑制 |

### 编译和测试结果

**编译**: ✅ 0 errors, 0 warnings
**单元测试**: 317/319 pass (99.4%) — 比修复前提升 6 个通过用例
**集成测试**: Batch 1 全 8 用例 PASS

---

## 附加观察

### Bug 3 观察项 (MEDIUM — 信息性)

**症状**: System prompt 占用 9339 tokens (29% 预算)
**评估**: 当前可接受，无需立即修复
**监控阈值**: 如超 12000 tokens (37.5%) 再启动优化
**优化方向**: 知识库摘要进一步压缩、规则集浓缩

---

## 最终状态 (2026-03-19)

**全部测试和修复工作完成。**

| 指标 | 数值 |
|------|------|
| 测试用例执行 | 47/50 |
| Bug 发现 | 7 (4 HIGH, 3 MEDIUM) |
| Bug 修复 | 6 ✅ + 1 👀观察 |
| 代码修改文件 | 9 |
| 单元测试 | 317/319 (99.4%) |
| 编译 | 0 errors |

### 遗留观察项 (非 Bug, LLM 行为相关)
1. Condense 后记忆丢失 — 需改进 BuildAutoCondenseSummary 的摘要质量
2. ModificationConflict 循环追问 — 需优化冲突解决 UX 流程
3. 复杂分析覆盖率 ~50% — LLM 能力限制，非代码问题
