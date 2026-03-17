# AICA 测试后优化方案

> 版本: 1.0
> 日期: 2026-03-17
> 基于: ManualTestResults_Calculator.md (41/42 用例, 85% PASS)
> 目标: 解决所有未修复的 P1 问题, 优化 LLM 行为, 改进工具, 补充测试

---

## 一、问题全景

| 级别 | 总数 | 已修复 | 未修复/部分 | LLM 行为 | 本方案覆盖 |
|------|------|--------|------------|----------|-----------|
| P0 | 7 | **7 ✅** | 0 | 0 | — |
| P1 | 17 | 6 | **2** (P1-012 部分, P1-013 未修复) | 8 | **10** |
| P2 | 17 | 2 | 0 | 15 | **15** |
| **合计** | **41** | **15** | **2** | **23** | **25** |

---

## 二、实施阶段

### Phase 1: 关键修复 — Condense 工具调用历史保留 [CRITICAL]

**问题**: P1-013 (重测2确认未修复) — condense 后 read_file 历史完全丢失, AICA 断言 "没有使用 read_file"

**根因分析**: 当 LLM 主动调用 `condense` 工具 (Level 1, 70% token 时触发) 时, 系统使用 LLM 自己写的摘要替换对话历史。LLM 的摘要不包含工具调用历史, 因此所有 read_file/grep_search 记录丢失。`BuildAutoCondenseSummary` (Level 2, 80% token) 虽然提取了工具调用, 但可能永远不会被触发 (LLM 在 Level 1 就已响应)。

#### Step 1.1: 新增 ExtractToolCallHistory 辅助方法

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`

**变更**: 添加新方法, 从对话历史中提取所有工具调用记录:

```csharp
private static string ExtractToolCallHistory(List<ChatMessage> conversationHistory)
{
    // 遍历所有 assistant 消息的 ToolCalls
    // 提取: 工具名, 关键参数 (文件路径/搜索查询/模式), 成功/失败
    // 按工具类型分组, 保留调用顺序
    // 输出格式:
    //   ## Tool Call History
    //   ### read_file (12 calls)
    //   - src/CalcManager/Ratpack/conv.cpp
    //   - DateCalculatorViewModel.h
    //   ...
    //   ### grep_search (5 calls)
    //   - "ICalcDisplay" in workspace
    //   ...
    // 上限: 2000 字符, 超出则保留最近 20 次调用
}
```

**预期效果**: 提取所有工具调用历史的结构化文本, 供两条 condense 路径使用

#### Step 1.2: 增强 LLM 主动 condense 路径 — 注入工具历史

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs` (约 line 801-853)

**变更**: 在 LLM 主动 condense 后, 将程序化提取的工具历史附加到 LLM 摘要之后:

```csharp
var summary = result.Content.Substring("CONDENSE:".Length);

// P1-013 fix: 无论 LLM 摘要写了什么, 都附加程序化工具历史
var toolHistory = ExtractToolCallHistory(conversationHistory);
if (!string.IsNullOrEmpty(toolHistory))
{
    summary = summary + "\n\n" + toolHistory;
}
```

**原理**: LLM 无法可靠地枚举所有工具调用。程序化注入确保工具历史永不丢失。

#### Step 1.3: 增强 Auto-condense 摘要质量

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs` (约 line 970-1128)

**变更**:
- 工具调用按时间顺序记录 (改 HashSet 为有序 List + 去重)
- 包含每个文件的读取次数 ("CalculatorManager.h (3 times)")
- 添加 "Tool Call Timeline" 段 (最近 20 次调用)
- keyFindings 分配从 8 增至 12, 截断从 200 字符增至 300 字符

#### Step 1.4: 强化 Post-condense 指令

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs` (约 line 843-848 和 223-228)

**变更**: 增强两条路径的 post-condense 系统消息:

```
[Post-condense instruction] The conversation was condensed.
The 'Tool Call History' section above contains FACTUAL data about every tool call made.
When the user asks about previous work (e.g., 'what files did I read?'),
your answer MUST be based EXCLUSIVELY on the Tool Call History section.
Do NOT claim tools were not used if they appear in the history.
```

#### Step 1.5: 增强 CondenseTool 参数描述

**文件**: `src/AICA.Core/Tools/CondenseTool.cs` (约 line 29-37)

**变更**: 在 summary 参数描述中明确要求包含工具调用日志:

```
MUST include: 1) Tool Call Log — list EVERY read_file path, grep_search query,
edit/write_to_file target. 2) Key Findings. 3) Current Task Status.
CRITICAL: The Tool Call Log is the ONLY record. Anything omitted is PERMANENTLY LOST.
```

**Phase 1 预期**: TC-A14 重测3 PASS (read_file 历史 0% 丢失 → ~90%+ 保留)

---

### Phase 2: LLM 行为优化 — Prompt 工程 [HIGH]

所有 "LLM 行为" 类问题通过 SystemPromptBuilder.AddRules() 优化。

**文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

#### Step 2.1: 强化数字一致性规则 (P1-009, 跨 5+ 用例)

```
Before calling attempt_completion, verify all numbers match tool output.
ALWAYS prefer tool-reported counts over manual counting.
If you must count manually, use 'approximately' qualifier.
BAD: Analysis says "48 methods" but completion says "44 methods"
GOOD: Both say "approximately 45 methods"
```

#### Step 2.2: 强化证据基础分析 (P1-014 虚构模式, P1-015 无依据声称)

```
Common FALSE patterns to avoid claiming without evidence:
- "Singleton" — only if private constructor + static instance + GetInstance()
- "Template Method" — only if abstract base with template method calling abstract steps
- "Supports undo/redo" — only if actual undo stack/memento

Citation format: "**[Pattern]** — Evidence: `Class::Method` in `file.h` (line ~N)"
If you cannot fill evidence fields, do NOT claim the pattern.
```

#### Step 2.3: 文件读取去重意识 (P2-014, 37 次迭代中 10 次重复)

```
CRITICAL: Do NOT re-read files you have already read in this conversation.
Before calling read_file, check if you already have the file contents from a previous call.
```

#### Step 2.4: 文件读取完整性 (P1-001/002/003/004, P2-001)

```
For file reading tasks, summary MUST cover ALL major sections:
- ALL #include directives, ALL class/struct/enum definitions
- ALL public methods (list every one), ALL private methods (enumerate)
- Coverage target: 70%+ of structural elements
- NEVER say "and other methods" — list them ALL or give exact count
```

#### Step 2.5: 代码风格感知 (P2-010, ratpak C 风格)

```
Before suggesting refactoring (RAII, namespaces, modern C++), check surrounding style.
If project uses C-style code, suggest improvements within existing paradigm.
```

#### Step 2.6: 路径拒绝后引导 (P2-013)

```
When path access is denied, explain security reason and suggest valid alternatives:
"This path is outside the workspace boundary. Try 'src/...' instead."
```

#### Step 2.7: 搜索范围验证 (P2-006)

```
After grep_search/find_by_name, check: did results come from ALL relevant subdirectories?
If user asked for "all .h files" but results only show CalcManager, search again without path filter.
```

#### Step 2.8: 代码生成质量 (P2-011 语法错误, P2-012 大小写)

```
Syntax correctness is MANDATORY. Common errors:
- Mismatched parentheses: TEST_METHOD(Name)() not TEST_METHOD Name)()
- Header casing: use EXACT filename. If unsure, use find_by_name to verify.
```

#### Step 2.9: 叙述抑制强化 (P1-017)

```
If you need information, CALL THE TOOL — do not narrate.
FORBIDDEN: "让我读取一些关键文件..." (then no tool call)
If you write "让我"/"Let me"/"I'll", STOP and call the tool instead.
```

#### Step 2.10: 统计精度 (P2-007/015/017)

```
ALWAYS use tool-reported counts. If output is truncated ("showing 200 of 343"),
report 343 as total, not the visible line count.
```

**Phase 2 预期**: P1-009 影响用例从 5 降至 ≤2; P1-014/015 重测消除虚构模式

---

### Phase 3: 工具改进 [MEDIUM]

#### Step 3.1: list_dir 截断增强 (P2-016)

**文件**: `src/AICA.Core/Tools/ListDirTool.cs`

**变更**:
- 截断消息包含剩余项数 ("truncated at 500, ~300 more items")
- 添加 `CountTotalItems` 快速计数辅助方法
- 上限从 500 提升至 800 (Calculator 项目约 600 项)
- 添加 5 秒超时保护

#### Step 3.2: 重复读取警告注入 (P2-014)

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs` (约 line 718-736)

**变更**: 重复工具调用被跳过时, 注入明确提示:

```csharp
var skipMessage = $"[Duplicate call skipped] You already called {toolCall.Name} " +
    "with these arguments. Use the existing result.";
conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, skipMessage));
```

**Phase 3 预期**: 重复读取减少 50%+; 大项目 list_dir 结果更完整

---

### Phase 4: 测试补充 [HIGH]

#### Step 4.1: TC-G03 长对话稳定性测试

15+ 轮连续操作 (读文件/搜索/分析/编辑混合), 验证:
- 无崩溃/挂起
- Condense 正常触发
- Condense 后工具历史保留 (验证 Phase 1)
- 响应质量不下降

#### Step 4.2: TC-A14 第三次重测

Phase 1 实施后, 重测:
1. 12+ 轮多种工具调用
2. 提问 "我之前读取了哪些文件？"
3. 验证所有 read_file 路径被正确列出
4. 通过标准: 正确性 ≥ B+, 准确性 ≥ B+, 完整性 ≥ B

#### Step 4.3: 回归测试

- TC-D02: P1-017 叙述抑制效果
- TC-B02: P1-014/015 证据基础分析效果
- TC-C01: 确认 Prompt 优化未引入回归

---

## 三、文件变更总览

| 文件 | Phase | 变更类型 | 预估工作量 |
|------|-------|---------|-----------|
| `src/AICA.Core/Agent/AgentExecutor.cs` | 1, 3 | 新增 ExtractToolCallHistory; 增强 condense 路径; 改进 BuildAutoCondenseSummary; 重复调用警告 | 4-6h |
| `src/AICA.Core/Prompt/SystemPromptBuilder.cs` | 2 | 10 条 Prompt 规则新增/强化 | 2-3h |
| `src/AICA.Core/Tools/ListDirTool.cs` | 3 | 截断增强, CountTotalItems, 上限提升 | 1-2h |
| `src/AICA.Core/Tools/CondenseTool.cs` | 3 | summary 参数描述增强 | 15min |
| **合计** | | | **8-12h** |

---

## 四、风险评估

| 风险 | 级别 | 缓解措施 |
|------|------|---------|
| 增强的 condense 摘要超出 token 预算 | 中 | ExtractToolCallHistory 输出上限 2000 字符, 超出保留最近 20 次调用 |
| LLM 仍忽略程序化工具历史 | 中 | Step 1.4 强化指令; 若仍无效, 考虑将工具历史注入为 user message (更高注意力权重) |
| Phase 2 Prompt 新增导致系统提示超预算 | 低 | 现有 BuildWithBudget 机制自动裁剪低优先级段; 新增规则标记为 Medium 优先级 |
| list_dir 上限提升导致大目录超时 | 低 | 添加 5 秒超时保护, 超时时截断并注明 |
| P2 LLM 行为问题仅部分改善 | 预期内 | 接受 ~50% 改善率, 持续跟踪并考虑模型特定调优 |

---

## 五、成功标准

| 指标 | 当前 | 目标 |
|------|------|------|
| TC-A14 read_file 历史保留率 | 0% (12 次全丢) | ≥ 90% |
| P1-009 影响用例数 | 5/41 | ≤ 2/41 |
| P1-014/015 虚构模式 | B02 存在虚构 | B02 重测无虚构 |
| 重复 read_file 调用比例 | ~27% (10/37) | ≤ 13% |
| 整体 PASS 率 | 85% (35/41) | ≥ 90% (37/41) |
| TC-G03 | 未测 | PASS |

---

## 六、实施优先级

```
Phase 1 (CRITICAL) ──→ Phase 4.2 (验证) ──→ Phase 2 (HIGH) ──→ Phase 3 (MEDIUM) ──→ Phase 4.1/4.3
      ↓                                           ↓
  condense 修复                              prompt 优化
  (P1-013 根因)                             (23 个 LLM 行为问题)
```

**建议执行顺序**: 1.1 → 1.2 → 1.3 → 1.4 → 1.5 → 4.2(验证) → 2.1 → 2.2 → 2.9 → 2.3 → 2.4 → 其余 Phase 2 → 3.1 → 3.2 → 3.3 → 4.1 → 4.3
