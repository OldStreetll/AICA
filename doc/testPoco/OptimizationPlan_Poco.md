# AICA 优化方案 — 基于 POCO A 类测试发现

> 版本: 1.1
> 日期: 2026-03-18
> 基于: IssuesAndOptimizations_Poco.md (29 个问题, 14 用例)
> 前序: testCalc/OptimizationPlan.md (Calculator 优化方案)
> 目标: 解决 A 类测试中发现的所有 P1 问题, 降低 P2 问题密度
> **状态: 全部 7 个 Phase 已实施, BUILD SUCCEEDED (2026-03-18)**

---

## 一、问题优先级矩阵

| 优化项 | 影响用例数 | 问题级别 | 实施类型 | 预期收益 | 实施难度 | 状态 |
|--------|-----------|----------|----------|----------|----------|------|
| Phase 1: 数字一致性 | 5/14 (36%) | P1 | Prompt | 高 — 最顽固问题 | 低 | **已实施 ✅** |
| Phase 2: read_file 摘要模板 | 4/14 (29%) | P1+P2 | Prompt | 高 — 完整性从 35% → 70%+ | 低 | **已实施 ✅** |
| Phase 3: list_dir 稳定性 | 2/14 (14%) | P1 | 代码 | 中 — 修复唯一 B- 工具 | 中 | **已实施 ✅** |
| Phase 4: 定义 vs 引用区分 | 2/14 (14%) | P1 | Prompt | 中 — 语义准确性 | 低 | **已实施 ✅** |
| Phase 5: 正则搜索命名空间 | 1/14 (7%) | P2 | Prompt | 中 — 跨模块覆盖 | 低 | **已实施 ✅** |
| Phase 6: ask_followup_question | 1/14 (7%) | P1 | 代码排查 | 中 — 交互体验 | 中 | **已实施 ✅** (诊断日志) |
| Phase 7: condense 历史强化 | 1/14 (7%) | P1 | 代码 | 中 — 回忆率 50% → 90%+ | 中 | **已实施 ✅** |

---

## 二、Phase 1: 数字一致性 Prompt 强化 [HIGH — 最紧迫]

### 问题

Calculator Phase 2 Step 2.1 已添加数字一致性规则, 但 POCO 测试中仍有 **5/14 用例** (36%) 出现数字矛盾:
- TC-A01: #include 正文 4 个 vs completion 5 个
- TC-A02: #include 正文 4 个 vs completion 5 个
- TC-A06: Foundation "28个" 实际 36; 测试 "8个" 实际 10
- TC-A07: completion "约30+个" 不精确
- TC-A10: 比较运算符正文 16、completion 18、实际 20 (三重矛盾)

### 现有规则

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs` (line 232-233)

```
### Number Consistency (CRITICAL)
- Numbers mentioned in your analysis text MUST exactly match numbers in `attempt_completion` result.
```

### 问题分析

现有规则仅要求"正文和 completion 数字一致", 但未覆盖:
1. **正文内部矛盾** (如标题说 "28个" 但表格列出 36 行)
2. **小计 vs 总计** (模块小计之和 ≠ 总计)
3. **列出的项目数 vs 声称的数量** (列出 10 个但说 "8个")
4. **三重矛盾** (正文 vs completion vs 表格, 各不相同)

### 具体变更

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`
**位置**: line 232 附近, 替换现有 Number Consistency 规则

**替换内容**:
```
### Number Consistency (CRITICAL)
- Numbers mentioned in your analysis text MUST exactly match numbers in `attempt_completion` result.
```

**替换为**:
```
### Number Consistency (CRITICAL — HIGHEST PRIORITY RULE)

BEFORE outputting ANY number, apply this 3-step verification:

1. COUNT-WHAT-YOU-LIST: If you list N items in a table or bullet list,
   the number you state MUST be N. Do NOT write a different number.
   BAD: "Found 8 test files:" followed by 10 bullet points
   GOOD: "Found 10 test files:" followed by 10 bullet points

2. SUBTOTALS-MUST-SUM: Module subtotals MUST add up to the grand total.
   BAD: "Foundation (28), Net (4), Data (2) = 44 total" (28+4+2=34≠44)
   GOOD: "Foundation (36), Net (4), Data (2), Apache (2) = 44 total"

3. BODY-COMPLETION-MATCH: Every number in attempt_completion MUST match
   the corresponding number in the body text. If they differ, you made
   an error — fix it before calling attempt_completion.

4. NEVER USE APPROXIMATE COUNTS when exact counts are available from tools.
   BAD: "approximately 30+ classes"
   GOOD: "32 classes" (count them from your list)

SELF-CHECK: Before calling attempt_completion, re-read your response and
verify every number. This takes 2 seconds and prevents the #1 user complaint.
```

### 验证方法

重测 TC-A06 和 TC-A10, 检查数字矛盾是否消除。

### 预期效果

P1-009 类问题: 5/14 用例 → ≤ 1/14 用例

---

## 三、Phase 2: read_file 摘要结构化模板 [HIGH]

### 问题

read_file 摘要平均覆盖率仅 35-40% (TC-A01: C+, TC-A02: B+), 遗漏:
- 命名空间 (TC-A01, A02 均未提及)
- 文件行数 (全部 read_file 用例均未提及)
- 标准库 #include (TC-A01 遗漏 4 个)
- protected/private 成员 (TC-A01 完全遗漏)

### 现有规则

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs` (line 165)

```
- `read_file`: **CRITICAL: If you read a file with offset/limit and the content appears
  truncated, continue reading... Do NOT tell the user 'the file was truncated' and stop**
```

当前规则仅关注截断处理, 未规定摘要格式。

### 具体变更

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`
**位置**: line 165 之后插入

**新增内容**:
```
- `read_file` SUMMARY FORMAT: After reading a file, your summary MUST start with:
  [File] path/to/file.h | Lines: N | Namespace: X
  [Includes] ALL #include directives (both project and standard library headers)

  Then provide:
  [Public API] List ALL public classes, methods, enums (complete, not "and others...")
  [Protected/Private] State counts: "N protected methods, M private methods, K member variables"
  [Macros] List any #define macros if present
  [Notes] Conditional compilation, deprecated items, platform-specific code

  Coverage target: ≥70% of structural elements.
  NEVER say "and other methods" — list ALL or give exact count.
```

### 验证方法

重测 TC-A01 (Logger.h), 检查摘要是否包含:
- `[File] Foundation/include/Poco/Logger.h | Lines: 945 | Namespace: Poco`
- 全部 9 个 #include (含标准库)
- protected/private 计数

### 预期效果

TC-A01 完整性: C+ → B+; TC-A02 完整性: B+ → A-

---

## 四、Phase 3: list_dir 稳定性修复 [HIGH — 代码级]

### 问题

同一目录 (Foundation/include/Poco/, 326 文件) 两次查询返回不同数量:
- EX-001: 249 个
- TC-A05: 283 个
- 实际: 326 个

### 现有代码分析

**文件**: `src/AICA.Core/Tools/ListDirTool.cs`

```csharp
// Line 115: 硬限制
const int maxItems = 800;

// Line 174-175: 排序
System.Array.Sort(dirs);
System.Array.Sort(files);

// Line 122-129: 截断报告
if (itemCount_total >= maxItems) {
    int totalEstimate = CountTotalItemsRecursive(fullPath, maxDepth);
    int remaining = System.Math.Max(0, totalEstimate - maxItems);
    sb.AppendLine($"... (output truncated at {maxItems} items, approximately {remaining} more items not shown)");
}
```

### 问题分析

326 < 800, 不应触发截断。可能原因:
1. **递归深度 (maxDepth=3)**: 默认递归 3 层, 但用户请求的是单层目录, 递归可能消耗 budget 在子目录上
2. **breadth-first 策略 (line 177-211)**: 两阶段枚举可能在子目录递归时消耗过多 budget
3. **LLM 输出截断**: 非 list_dir 工具问题, 而是 LLM 在格式化大量结果时自行截断
4. **不稳定性根因**: 可能是 LLM 上下文窗口或 token 限制导致不同次输出不同长度

### 具体变更

**文件**: `src/AICA.Core/Tools/ListDirTool.cs`

#### Step 3.1: 始终附加总数信息 (即使未截断)

**位置**: line 122-129 之后, 添加 else 分支

```csharp
// 现有代码 (line 122-129): 截断时报告
if (itemCount_total >= maxItems)
{
    // ... existing truncation message ...
}
// NEW: 即使未截断, 也附加总数
else
{
    sb.AppendLine($"\n[Total: {itemCount_total} items listed]");
}
```

#### Step 3.2: 非递归模式优化

**位置**: 在处理 `recursive=false` 或 `maxDepth=1` 时, 跳过预算限制

```csharp
// 当用户请求非递归或单层时, 不限制项目数
bool isShallowListing = !recursive || maxDepth <= 1;
int effectiveMaxItems = isShallowListing ? int.MaxValue : maxItems;
```

#### Step 3.3: 验证排序确定性

确认 `System.Array.Sort(dirs)` 使用的是 Ordinal 比较 (已确认 line 174), 排除排序不确定性。

### 验证方法

对 Foundation/include/Poco/ 连续执行 3 次 list_dir, 确认:
1. 三次结果数量相同
2. 结果末尾附有 `[Total: 326 items listed]`

### 预期效果

TC-A05: B- → A-; 结果稳定性: 不一致 → 一致

---

## 五、Phase 4: 定义 vs 引用区分 [MEDIUM — Prompt 级]

### 问题

- TC-A01: Logger.h 引用的 `Message::PRIO_*` 被归属为 Logger.h 定义的内容
- TC-A07: 5 个 Data 类仅 `#include RefCountedObject.h` 但被误报为 "继承 RefCountedObject"

### 具体变更

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`
**位置**: line 219 (Evidence-Based Analysis) 之后插入

```
### Definition vs Reference Distinction (IMPORTANT)
When describing file contents or search results, distinguish:
- DEFINED HERE: Classes, methods, enums, macros declared/implemented in this file
- REFERENCED: Types, constants used but defined in other files

For inheritance analysis from grep_search results:
- "class Foo: public Bar" → Foo INHERITS Bar (direct relationship)
- "#include Bar.h" without ": public Bar" → Foo REFERENCES Bar (not inheritance)
Do NOT list classes as "inheriting X" if they only #include X's header.

For file summaries:
- If a constant like PRIO_FATAL is defined in Message.h but used in Logger.h,
  say "uses Message::PRIO_FATAL (defined in Message.h)", NOT "defines PRIO_FATAL"
```

### 预期效果

P1-POCO-001 (日志级别归属) 和 P1-POCO-008 (继承 vs 引用) 消除

---

## 六、Phase 5: 正则搜索命名空间补充 [MEDIUM — Prompt 级]

### 问题

TC-A08: 正则 `class\s+\w+\s*:\s*public\s+Channel` 仅匹配非限定名, 遗漏了 Net/Data/ApacheConnector 中使用 `Poco::Channel` 的 4 个子类。

### 具体变更

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`
**位置**: grep_search 工具说明附近 (约 line 163)

```
- `grep_search` with inheritance patterns: When searching for class inheritance
  (e.g., "public Channel"), ALWAYS perform TWO searches:
  1. Unqualified: `public Channel` or `public\s+Channel`
  2. Fully-qualified: `public Poco::Channel` or `public\s+Poco::Channel`
  This catches cross-module code that uses namespace-qualified names.
  Report results grouped by module: "Found N in Foundation, M in Net, K in other modules"
```

### 预期效果

TC-A08 跨模块覆盖率: 80% → 95%+

---

## 七、Phase 6: ask_followup_question 排查 [MEDIUM — 代码排查]

### 问题

TC-A12: AICA 生成了追问文本但未实际调用 ask_followup_question 工具。底部显示 "AI 描述了要执行的操作但未实际调用工具"。

### 代码分析

**文件**: `src/AICA.Core/Tools/AskFollowupQuestionTool.cs`
- Line 129: `RequiresApproval = true` — 需要 UI 审批
- Line 101: `uiContext.ShowFollowupQuestionAsync()` — 依赖 UI 上下文

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`
- Line 471: `bool hasOnlyFollowup = toolCalls.All(tc => tc.Name == "ask_followup_question");`
- Line 465: ask_followup_question 有特殊处理, 文本不被抑制

### 排查方向

1. **检查 LLM 是否返回了 tool_calls**: 在 `AgentExecutor.cs` 的工具调用解析处 (约 line 440-480) 添加日志, 记录 LLM 返回的原始 JSON 是否包含 `tool_calls` 字段
2. **检查工具解析器**: 某些 LLM (尤其是本地模型) 可能不支持 OpenAI function calling 格式, 导致 tool_calls 被忽略
3. **检查 AICA Options**: 确认 "Enable Tool Calling" 选项是否启用

### 具体变更

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`
**位置**: 工具调用解析处 (约 line 440)

```csharp
// 添加诊断日志
if (assistantMessage.ToolCalls == null || !assistantMessage.ToolCalls.Any())
{
    // 检查文本中是否包含工具调用意图
    if (assistantMessage.Content?.Contains("ask_followup_question") == true
        || assistantMessage.Content?.Contains("让我使用") == true)
    {
        _logger?.LogWarning("[AICA] LLM intended to call a tool but no tool_calls in response. " +
            "Check: 1) Function calling enabled? 2) Model supports tool use? " +
            "Content preview: {Preview}",
            assistantMessage.Content?.Substring(0, Math.Min(200, assistantMessage.Content.Length)));
    }
}
```

### 预期效果

提供清晰的诊断信息, 帮助定位是 LLM 配置问题还是解析问题。

---

## 八、Phase 7: condense 工具历史强化 [MEDIUM — 代码级]

### 问题

TC-A14: 2 次 read_file 仅记住 1 次 (50% 回忆率)。AutoPtr.h (第 6 轮) 被遗漏, Object.h (第 9 轮) 被记住 — 暗示较早的调用在 condense 中丢失。

### 现有代码分析

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`

```csharp
// Line 1231: 2000 字符上限
// Line 1235: 去重参数

// Line 813: 手动 condense 时注入工具历史
var toolHistory = ExtractToolCallHistory(conversationHistory);
```

### 问题分析

`ExtractToolCallHistory` (line 1155-1256) 有 2000 字符上限 (line 1231)。但 TC-A14 仅 2 个 read_file 路径, 远低于 2000 字符, 不应触发截断。

可能原因:
1. **去重逻辑** (line 1235) 可能误将 AutoPtr.h 视为已处理
2. **condense 触发时机**: 可能在 AutoPtr.h 调用之前就已 condense, 导致该调用不在提取范围内
3. **Level 1 vs Level 2**: LLM 主动 condense (Level 1, 70%) 时, 可能在 AutoPtr.h 之后但 Object.h 之前触发, 此时 LLM 的手写摘要遗漏了 AutoPtr.h, 程序化注入来不及覆盖

### 具体变更

#### Step 7.1: 增强 ExtractToolCallHistory 日志

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`
**位置**: line 1155 方法入口

```csharp
private static string ExtractToolCallHistory(List<ChatMessage> conversationHistory)
{
    // NEW: 添加提取前日志
    int totalMessages = conversationHistory.Count;
    int toolCallMessages = conversationHistory
        .Count(m => m.Role == "assistant" && m.ToolCalls?.Any() == true);
    // Log: "Extracting tool history from {totalMessages} messages, {toolCallMessages} with tool calls"

    // ... existing code ...
}
```

#### Step 7.2: 确保 Level 1 condense 也注入完整历史

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`
**位置**: line 808 附近 (手动 condense 处理)

当前代码 (line 813):
```csharp
var toolHistory = ExtractToolCallHistory(conversationHistory);
```

确认此行在 `conversationHistory` 被裁剪之前执行。如果 conversationHistory 已被裁剪, 则需要在裁剪前先提取:

```csharp
// MUST extract BEFORE any conversation trimming
var toolHistory = ExtractToolCallHistory(conversationHistory);
// ... then trim conversation ...
var summary = result.Content.Substring("CONDENSE:".Length);
if (!string.IsNullOrEmpty(toolHistory))
{
    summary = summary + "\n\n" + toolHistory;
}
```

#### Step 7.3: 强化 post-condense 指令中的工具历史引用

**位置**: line 223-231 (post-condense instruction)

```
[Post-condense instruction]
The 'Tool Call History' section above is AUTO-EXTRACTED and FACTUAL.
When the user asks about previous work:
- Your answer MUST be based on the Tool Call History section
- If read_file appears with 2 paths, tell the user BOTH paths
- Do NOT claim "only 1 file was read" if the history shows 2
- Do NOT reclassify tool names (read_file stays read_file, not list_code_definition_names)
```

### 预期效果

TC-A14 read_file 回忆率: 50% → 90%+

---

## 九、实施顺序与验证计划

| 阶段 | 实施内容 | 验证用例 | 通过标准 | 实施状态 |
|------|----------|----------|----------|----------|
| **Phase 1** | 数字一致性 Prompt | 重测 TC-A06, TC-A10 | 正文/completion/表格数字一致 | **已实施 ✅ → 部分有效 ⚠️** (三重矛盾→二重; 头文件差5→差1; 但小计求和仍不一致; LLM 计数天花板) |
| **Phase 2** | read_file 摘要模板 | 重测 TC-A01 | 命名空间+行数+全部#include 出现 | **已实施 ✅ → 验证通过 ✅** (命名空间/9个include/Protected/Private全出现; 覆盖率35%→70%) |
| **Phase 3** | list_dir 稳定性 | 重测 TC-A05 ×3 次 | 三次结果数量相同, 附总数 | **已实施 ✅ → 验证通过 ✅** (327/326=99.7%, 从249/283提升) |
| **Phase 4** | 定义 vs 引用 | 重测 TC-A07 | Data 类标注为 "引用" 非 "继承" | **已实施 ✅ → 部分有效 ⚠️** (completion 中区分生效; body 中未体现; 另发现 grep_search 也有工具未调用问题) |
| **Phase 5** | 正则命名空间 | 重测 TC-A08 | Net/Data/Apache 子类出现 | **已实施 ✅ → 未生效 ❌** (搜索范围被缩小到Foundation; 未搜索Poco::Channel; 结果与优化前相同80%) |
| **Phase 6** | ask_followup 排查 | 重测 TC-A12 | Debug 日志输出诊断信息 | **已实施 ✅ → 诊断生效 ✅** (Debug日志确认: LLM intended to call tool but no tool_calls → **根因确认: LLM返回无tool_calls字段, 是function calling配置或模型兼容性问题**; 工具本身无问题) |
| **Phase 7** | condense 强化 | 重测 TC-A14 | 全部 read_file 路径被列出 | **已实施 ✅ → 验证通过 ✅** (回忆率50%→100%; 工具名误归类修复; 正确区分read_file vs其他工具) |

### 实施记录

**实施日期**: 2026-03-18
**实施方式**: 全部 7 个 Phase 一次性实施, 编译通过

**Batch 1 — Prompt 修改 (SystemPromptBuilder.cs)**:
- Phase 1: 数字一致性规则从 6 行 → 12 行, 新增 COUNT-WHAT-YOU-LIST / SUBTOTALS-MUST-SUM / SELF-CHECK
- Phase 2: read_file 工具说明新增 4 行摘要格式模板 ([File]/[Lines]/[Namespace]/[Includes]/[Public]/[Private])
- Phase 4: 新增 "Definition vs Reference Distinction" 规则段 (8 行)
- Phase 5: grep_search 说明增加命名空间搜索提示 (搜索 unqualified + fully-qualified)

**Batch 2 — 代码修复**:
- Phase 3 (ListDirTool.cs): 递归模式添加 `[Total: N items listed]`, 非递归模式添加 `[Total: N directories, M files]`
- Phase 6 (AgentExecutor.cs:461): 添加工具意图诊断日志 (检测 LLM 想调用工具但 tool_calls 为空)
- Phase 7 (AgentExecutor.cs:870): condense post-instruction 从 4 条 → 6 条, 新增 "list ALL N paths" 和 "不要重新分类工具名"

**编译结果**: BUILD SUCCEEDED, 0 errors, VSIX 3394.3 KB

### 待验证

所有 Phase 均需通过重测验证。建议重测顺序:
1. TC-A05 (list_dir 总数显示) — 验证 Phase 3
2. TC-A01 (read_file 摘要格式) — 验证 Phase 2
3. TC-A06/A10 (数字一致性) — 验证 Phase 1
4. TC-A07 (继承 vs 引用) — 验证 Phase 4
5. TC-A08 (跨模块正则) — 验证 Phase 5
6. TC-A12 (ask_followup_question) — 验证 Phase 6
7. TC-A14 (condense 回忆) — 验证 Phase 7

---

## 十、效果总结 (重测验证完成)

| 指标 | 优化前 | 预期 | **实际 (重测)** | 达标 |
|------|--------|------|----------------|------|
| PASS 率 | 71% (10/14) | 86%+ (12/14) | **86% (12/14)** | **✅ 达标** |
| PARTIAL PASS | 4 (A01/A05/A12/A14) | ≤ 2 | **2 (A06/A12)** | **✅ 达标** |
| P1-009 数字矛盾 | 5/14 (36%) | ≤ 1/14 (7%) | **改善但未消除** (三重→二重) | **⚠️ 部分达标** |
| read_file 完整性 | C+ (35-40%) | B+ (70%+) | **B+ (~70%)** | **✅ 达标** |
| list_dir 覆盖率 | 76-87% 不稳定 | 稳定 + 附总数 | **99.7% (327/326)** | **✅ 超预期** |
| condense 回忆率 | 50% | 90%+ | **100% (2/2)** | **✅ 超预期** |

### 升级的用例

| 用例 | 优化前 | 优化后 | 关键改善 |
|------|--------|--------|----------|
| TC-A01 | PARTIAL PASS (C+完整性) | **PASS** (B+完整性) | Phase 2: 命名空间/#include/Protected 出现 |
| TC-A05 | PARTIAL PASS (B-完整性) | **PASS** (A-完整性) | Phase 3: 覆盖率 87% → 99.7% |
| TC-A14 | PARTIAL PASS (C正确性) | **PASS** (A正确性, 三维全A) | Phase 7: 回忆率 50% → 100% |

### 仍需迭代的问题

1. **数字一致性 (Phase 1)**: LLM 计数天花板, Prompt 已接近极限 → 建议工具侧辅助 (自动附加计数)
2. **跨模块正则 (Phase 5)**: Prompt 未被遵守 → 建议工具侧实现 (自动扩展搜索范围)
3. **ask_followup_question (Phase 6)**: 根因确认为 LLM function calling → 需配置修复或 fallback 机制
