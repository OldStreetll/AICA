# AICA POCO 测试问题汇总与优化建议

> 版本: 1.0
> 日期: 2026-03-17
> 基于: ManualTestPlan_Poco.md (POCO 项目测试)
> 前序参考: testCalc/OptimizationPlan.md (Calculator 项目优化方案)
> 状态: 持续更新中

---

## 一、问题全景

| 级别 | 总数 | 新发现 | Calculator 遗留 | LLM 行为 | 工具问题 |
|------|------|--------|----------------|----------|----------|
| P0 | 0 | 0 | 0 (全部已修复 ✅) | 0 | 0 |
| P1 | 13 | 10 | 3 (P1-009 数字一致性 + P1-013 condense) | 9 | 4 |
| P2 | 16 | 16 | 0 | 13 | 3 |
| **合计** | **29** | **26** | **3** | **22** | **7** |

### Calculator P0 回归验证结果

| Calculator 问题 | POCO 验证结果 | 状态 |
|----------------|---------------|------|
| P0-002 路径解析错误 (src/src/) | EX-001: 文件创建路径正确, 无多余层级 | **已修复 ✅** |
| P1-005 write_file 未触发确认框 | EX-001: 确认对话框正常弹出 | **已修复 ✅** |

---

## 二、问题详细列表

### P1 级别问题

---

#### P1-POCO-001: 日志级别归属错误 (TC-A01)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01: 读取 Logger.h |
| **描述** | AICA 声称 Logger.h 定义了 PRIO_FATAL ~ PRIO_TRACE 日志级别常量, 实际这些常量定义在 **Message.h** 中, Logger.h 仅引用 |
| **类型** | LLM 行为 — 错误归属 |
| **影响** | 开发者可能误认为日志级别定义在 Logger.h 中, 修改时找不到定义 |
| **建议** | Prompt 强化: "列出文件内容时, 区分'本文件定义的'和'从其他文件引用的'。对于引用的类型/常量, 标注来源文件" |

---

#### P1-POCO-002: #include 遗漏标准库头文件 (TC-A01)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01: 读取 Logger.h |
| **描述** | AICA 仅列出 5 个 Poco 库 #include, 遗漏了 4 个标准库头文件: `<map>`, `<vector>`, `<cstddef>`, `<memory>` |
| **类型** | LLM 行为 — 选择性遗漏 |
| **影响** | 开发者无法完整了解文件的外部依赖 |
| **建议** | Prompt 强化: "#include 列表必须包含标准库头文件, 不能仅列出项目内部头文件" |

---

#### P1-POCO-003: #include 列表正文与 completion 自相矛盾 (TC-A02)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A02: 读取 NamedTuple.h |
| **描述** | 正文列 4 个 #include (遗漏 Foundation.h), completion 列 5 个 (正确) — 同一响应内数字不一致 |
| **类型** | LLM 行为 — 数字一致性 (与 Calculator P1-009 同类) |
| **影响** | 用户无法判断哪个数字是正确的 |
| **关联** | Calculator P1-009 (方法计数矛盾), 说明 Phase 2 Step 2.1 数字一致性规则仍需强化 |
| **建议** | 见优化建议 OPT-001 |

---

#### P1-POCO-004: list_dir 文件计数不完整 (TC-A05)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A05: 列出 Foundation/include/Poco/ 目录 |
| **描述** | 实际 326 个 .h 文件, AICA 报告 283 个, 遗漏 43 个 (13.2%) |
| **类型** | 工具问题 — list_dir 截断 |
| **影响** | 开发者得到不完整的文件列表, 可能遗漏需要关注的文件 |
| **关联** | Calculator P2-016 (list_dir 截断); EX-001 同目录仅报 249 个, 说明截断不稳定 |
| **建议** | 见优化建议 OPT-002 |

---

#### P1-POCO-005: list_dir 结果不稳定 (TC-A05 + EX-001)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A05 + EX-001: 同一目录两次查询 |
| **描述** | 对 Foundation/include/Poco/ 同一目录, 第一次返回 249 个文件 (EX-001), 第二次返回 283 个文件 (TC-A05), 实际为 326 个 |
| **类型** | 工具问题 — 不确定性行为 |
| **影响** | 工具结果不可靠, 开发者无法信任计数 |
| **建议** | 见优化建议 OPT-002 |

---

#### P1-POCO-006: find_by_name 数字多处自相矛盾 (TC-A06)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A06: 查找 "Channel" 文件 |
| **描述** | 三处数字矛盾: ① Foundation 声称 "28个" 实际小计 36; ② 测试文件声称 "8个" 实际列出 10 个; ③ Completion 说 "8个头文件" 但列出 13 个 |
| **类型** | LLM 行为 — 数字一致性 (与 Calculator P1-009 同类) |
| **影响** | 数字混乱降低回答可信度 |
| **关联** | P1-POCO-003 (同类问题); Calculator P1-009 |
| **建议** | 见优化建议 OPT-001 |

---

#### P1-POCO-007: 多请求合并执行 (EX-001)

| 项目 | 内容 |
|------|------|
| **来源** | EX-001: 连续发送 A03 + A05 输入 |
| **描述** | 用户连续发送两条独立请求 (write_file + list_dir), AICA 将其合并为一个响应同时执行 |
| **类型** | Agent 行为 — 请求边界识别 |
| **影响** | 用户可能只想执行后一个请求, 但前一个也被执行了 |
| **建议** | 考虑在检测到多个独立任务时, 询问用户是否同时执行或分别处理 |

---

#### P1-POCO-008: "继承"与"引用"混淆 — grep 结果语义误归类 (TC-A07)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A07: 搜索 "RefCountedObject" |
| **描述** | Data 模块 5 个类 (AbstractBinding, AbstractExtraction, AbstractPreparator, RowFormatter, StatementImpl) 仅 `#include "RefCountedObject.h"` 但未继承, 被错误列为"继承 RefCountedObject 的类" |
| **类型** | LLM 行为 — 语义误归类 |
| **影响** | 开发者可能误以为这些类使用引用计数, 影响资源管理决策 |
| **关联** | P1-POCO-001 (定义 vs 引用混淆), OPT-004 |
| **建议** | 见优化建议 OPT-004; grep_search 结果展示时应区分 "inherits from X" vs "references X" |

---

### P2 级别问题

---

#### P2-POCO-001: 遗漏 protected/private 区域 (TC-A01)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01: 读取 Logger.h |
| **描述** | 12 个 protected 方法、6 个 private 方法、5 个私有成员变量均未提及 |
| **类型** | LLM 行为 — 默认只关注 public |
| **建议** | 非关键问题, read_file 摘要默认聚焦 public API 是合理的; 但用户明确要求"完整内容"时应包含 |

#### P2-POCO-002: 遗漏已废弃宏和条件编译 (TC-A01)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01: 读取 Logger.h |
| **描述** | poco_*_f1/f2/f3/f4 废弃宏 (24 个) 未提及; poco_debug/poco_trace 受 _DEBUG/POCO_LOG_DEBUG 条件编译控制未提及 |
| **类型** | LLM 行为 — 信息过滤 |

#### P2-POCO-003: 未提及命名空间 (TC-A01, TC-A02)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01 (Logger.h), TC-A02 (NamedTuple.h) |
| **描述** | 两个文件均未说明所在的 `namespace Poco` |
| **类型** | LLM 行为 — 遗漏基本结构信息 |
| **建议** | Prompt 强化: "文件摘要必须包含命名空间信息" |

#### P2-POCO-004: 未提及文件行数/规模 (TC-A01, TC-A02)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01 (Logger.h, 945 行), TC-A02 (NamedTuple.h, 12081 行) |
| **描述** | 未告知文件行数, 开发者无法感知文件规模 |
| **类型** | LLM 行为 — 遗漏元信息 |
| **建议** | Prompt 强化: "read_file 摘要应在开头标注文件总行数" |

#### P2-POCO-005: 大文件未标注分块/截断 (TC-A02)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A02: 读取 NamedTuple.h (12081 行) |
| **描述** | 未说明是否分块读取、是否存在截断 |
| **类型** | LLM 行为 — 遗漏处理说明 |

#### P2-POCO-006: 遗漏私有成员 (TC-A02)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A02: 读取 NamedTuple.h |
| **描述** | _pNames 成员和 init() 私有方法未提及 |
| **类型** | LLM 行为 — 默认只关注 public |

#### P2-POCO-007: 方法重载大量遗漏 (TC-A01)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A01: 读取 Logger.h |
| **描述** | 每个日志级别有 3 种重载 (string, string+file+line, template format), AICA 仅提及方法名而未展示重载 |
| **类型** | LLM 行为 — 摘要过度简化 |

#### P2-POCO-008: list_dir 截断未标注 (TC-A05)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A05: 列出 Foundation/include/Poco/ |
| **描述** | 326 个文件仅返回 283 个, 但未说明结果被截断 |
| **类型** | 工具问题 — 截断无提示 |
| **关联** | Calculator P2-016 (同类问题) |

#### P2-POCO-009: find_by_name 小计与总计不匹配 (TC-A06)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A06: 查找 "Channel" 文件 |
| **描述** | 各模块小计之和 (28+4+2+2=36) ≠ 总计 44 |
| **类型** | LLM 行为 — 算术错误 |

#### P2-POCO-010: grep_search 继承类数量不精确 (TC-A07)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A07: 搜索 "RefCountedObject" |
| **描述** | completion 声称 "约30+个类" 继承 RefCountedObject, 但未给出精确数字; 正文列出约 24 个, 与 "30+" 不一致 |
| **类型** | LLM 行为 — 模糊计数 |
| **关联** | OPT-001 (数字一致性) |

#### P2-POCO-011: 正则搜索跨模块覆盖不足 (TC-A08)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A08: 正则搜索 Channel 子类 |
| **描述** | Foundation 模块 16/16 = 100%, 但 Net (RemoteSyslogChannel, SMTPChannel)、Data (SQLChannel)、ApacheConnector (ApacheChannel) 共 4 个跨模块子类未发现。根因: 这些模块使用完全限定名 `Poco::Channel` 而非 `Channel`, 初始正则 `class\s+\w+\s*:\s*public\s+Channel` 未覆盖 |
| **类型** | LLM 行为 — 正则模式不够全面 |
| **正面** | AICA 在初始正则仅匹配 1 个结果时, 主动放宽模式重新搜索, 展示了良好的自适应能力 |
| **建议** | 见优化建议 OPT-005 |

#### P2-POCO-012: #include 未提及 (TC-A10)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A10: 列出 AutoPtr.h 定义 |
| **描述** | 4 个 #include (Foundation.h, Exception.h, `<algorithm>`, `<cstddef>`) 未列出 |
| **类型** | LLM 行为 — 遗漏依赖信息 |
| **关联** | P1-POCO-002 (同类), OPT-003 |

#### P2-POCO-013: 文件行数未提及 (TC-A10)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A10: 列出 AutoPtr.h 定义 (407 行) |
| **描述** | 未告知文件行数 |
| **类型** | LLM 行为 — 遗漏元信息 |
| **关联** | P2-POCO-004 (同类), OPT-003 |

#### P2-POCO-014: run_command 输出库路径不准确 (TC-A11)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A11: 执行 cmake 编译 Foundation |
| **描述** | AICA 声称输出为 `Foundation.dir\Debug\Foundation.lib`, 实际输出为 `cmake-build/lib/PocoFoundationd.lib` (CMake 生成的 POCO 库使用 "Poco" 前缀 + "d" Debug 后缀) |
| **类型** | LLM 行为 — 输出路径推断错误 |
| **影响** | 开发者按声称路径查找库文件会找不到 |
| **建议** | LLM 应从编译输出日志中提取实际输出路径, 而非推断; 或在摘要后注明 "请在 cmake-build/lib/ 下查找实际输出" |

#### P1-POCO-011: ask_followup_question 工具未实际调用 (TC-A12)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A12: 模糊请求 "帮我修复这个 bug" |
| **描述** | AICA 正确识别了信息不足并生成了追问文本, 但 **未实际调用 ask_followup_question 工具**, 导致无交互式对话框弹出。底部出现提示: "AI 描述了要执行的操作但未实际调用工具" |
| **类型** | 工具调用问题 — function calling 配置或模型兼容性问题 |
| **影响** | 用户无法通过选项快速回复, 交互体验降级为纯文本; 也影响 grep_search (TC-A07 测试1) |
| **根因 (已确认)** | **Phase 6 诊断日志确认: Debug 输出 `[AICA] WARNING: LLM intended to call a tool but no tool_calls`** → LLM 在响应中描述了工具调用意图, 但 HTTP 响应的 tool_calls 字段为空。确认是 **LLM 服务器/模型层面的 function calling 问题**, AICA 工具代码本身无问题 |
| **确认的排查方向** | 1. 检查 LLM 服务器是否启用 `--enable-auto-tool-choice` 参数 2. 确认模型是否支持 OpenAI 格式的 function calling (部分本地模型不支持) 3. 检查 AICA Options 中 'Enable Tool Calling' 是否已启用 4. 如使用 vLLM/Ollama 等本地推理框架, 确认 tool use 功能已开启 |
| **建议修复** | 短期: 在 AICA 中添加 fallback 机制 — 当检测到工具意图但无 tool_calls 时, 自动解析文本中的工具调用并执行; 长期: 确保推荐的 LLM 配置文档中包含 function calling 启用说明 |

#### P1-POCO-012: read_file 历史不完整 — condense 后部分丢失 (TC-A14)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A14: 10+ 轮对话后询问 "读取了哪些文件?" |
| **描述** | 实际 read_file 调用 2 次 (AutoPtr.h + Object.h), AICA 仅记住 1 次 (Object.h), 遗漏 AutoPtr.h。回忆率 50% |
| **类型** | LLM 行为 — condense 后工具调用历史部分丢失 |
| **关联** | Calculator P1-013 (condense 历史丢失, 回忆率 0%) |
| **改善程度** | Calculator 0% → POCO 50%, Phase 1 condense 优化有一定效果但仍不充分 |
| **建议** | Phase 1 Step 1.2 的工具历史注入可能仅保留了最近的调用; 需确保 **所有** read_file 路径均被程序化提取, 而非仅最近 N 次 |

#### P1-POCO-013: read_file 误归类为 list_code_definition_names (TC-A14)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A14: 工具历史回忆 |
| **描述** | AutoPtr.h 的读取实际使用了 read_file 工具 (日志显示 📖 read_file), 但 AICA 在回忆中将其归类为 "list_code_definition_names" 调用 |
| **类型** | LLM 行为 — 工具类型记忆混淆 |
| **建议** | condense 摘要中应明确标注每次调用的工具名称, 避免 LLM 推断错误 |

#### P2-POCO-015: LLM API 连接错误需重试 (TC-A14)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A14: 第一次询问触发 API 错误 |
| **描述** | 首次发送 "我之前读取了哪些文件?" 时触发 "❌ Agent Error: LLM communication error: Failed to connect to LLM API", 需用户重新发送 |
| **类型** | 工具/基础设施问题 — API 连接不稳定 |
| **建议** | 添加自动重试机制 (最多 2-3 次), 避免用户手动重发 |

---

## 附: P1 补充 (TC-A10)

#### P1-POCO-009: 比较运算符计数三重矛盾 (TC-A10)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A10: 列出 AutoPtr.h 的类和方法定义 |
| **描述** | 正文称 "16个" 比较运算符, completion 称 "18个", 实际为 **20 个**。同一响应中出现三个不同数字, 是 P1-009 同类问题的最严重表现 |
| **类型** | LLM 行为 — 数字一致性 (P1-009 同类) |
| **关联** | P1-POCO-003, P1-POCO-006, OPT-001 |

#### P1-POCO-010: 赋值方法计数矛盾 (TC-A10)

| 项目 | 内容 |
|------|------|
| **来源** | TC-A10: 列出 AutoPtr.h 的类和方法定义 |
| **描述** | 正文称 "7个" 赋值方法, completion 称 "5个", 表格实际列出 8 个 (assign×4 + operator=×4) |
| **类型** | LLM 行为 — 数字一致性 (P1-009 同类) |
| **关联** | P1-POCO-009 (同用例), OPT-001 |

---

## 三、优化建议

### OPT-001: 强化数字一致性规则 [HIGH — 跨多用例]

**影响用例**: TC-A01, TC-A02, TC-A06, TC-A07, **TC-A10** (以及 Calculator 5+ 用例)

**问题根因**: Calculator Phase 2 Step 2.1 的数字一致性 Prompt 规则效果不足。在 POCO 测试中, P1-009 同类问题仍在 3 个用例中出现。

**当前规则** (Phase 2 Step 2.1):
```
Before calling attempt_completion, verify all numbers match tool output.
ALWAYS prefer tool-reported counts over manual counting.
```

**建议强化**:
```
CRITICAL NUMBER CONSISTENCY:
1. Before outputting ANY count, verify it matches the items you actually listed.
2. If you list N items, the count MUST say N, not a different number.
3. Subtotals MUST sum to the grand total.
4. If body says "X个" and completion says "Y个", they MUST be the same number.
5. SELF-CHECK: After writing your response, scan for all numbers and verify each one.

COMMON MISTAKES:
- Saying "28 files" but listing 36 → Count what you listed
- Saying "8 test files" but listing 10 → Count each .h and .cpp separately
- Body says "4 includes" but completion says "5 includes" → Pick the correct one
```

**预期效果**: P1-009 类问题从 3/6 用例 → ≤ 1/6 用例

---

### OPT-002: list_dir 截断机制改进 [HIGH — 工具级]

**影响用例**: TC-A05, EX-001

**问题根因**: list_dir 在大目录 (326+ 文件) 下截断行为不稳定 (同一目录两次返回不同数量), 且未标注截断。

**Calculator Phase 3 Step 3.1 现状**: 上限从 500 提升至 800, 添加截断消息。但 326 < 800, 不应触发截断, 暗示问题可能不在上限设置。

**建议排查**:
1. 检查 `ListDirTool.cs` 是否存在随机排序导致不同文件被截断
2. 检查是否有文件权限/隐藏文件过滤导致不稳定
3. 添加确定性排序 (字母序) 确保结果稳定
4. 在结果末尾附加: `[Listed X of Y total items]` (即使未截断也标注总数)

**建议代码变更方向**:
```csharp
// ListDirTool.cs
var allItems = directory.GetFileSystemInfos().OrderBy(f => f.Name).ToList();
int totalCount = allItems.Count;
var items = allItems.Take(maxItems).ToList();

// 始终附加总数信息
result += $"\n[{items.Count} of {totalCount} items shown]";
```

**预期效果**: 同一目录多次查询结果一致; 开发者始终知道列表是否完整

---

### OPT-003: read_file 摘要结构化模板 [MEDIUM — Prompt 级]

**影响用例**: TC-A01, TC-A02

**问题根因**: read_file 摘要遗漏命名空间、行数、protected/private 等结构信息。

**建议 Prompt 补充**:
```
When summarizing a file after read_file, ALWAYS include this header:
  [文件] path/to/file.h (N lines)
  [命名空间] Poco::JSON (or "global namespace")
  [依赖] #include list (ALL includes, both library and standard)

Then provide structured content:
  [公共 API] classes, methods, enums (COMPLETE list)
  [受保护/私有] summary with counts (e.g., "3 private methods, 5 member variables")
  [宏/条件编译] if any macros defined, list them
```

**预期效果**: P2-POCO-003/004 自动消除; TC-A01 完整性从 C+ → B+

---

### OPT-004: 区分"定义"与"引用" [LOW — Prompt 级]

**影响用例**: TC-A01, TC-A07

**问题根因**: AICA 将引用/包含的内容误归为定义/继承。TC-A01: Logger.h 引用的 Message::PRIO_* 被归属为 Logger.h 内容; TC-A07: 仅 #include RefCountedObject.h 的类被误报为继承。

**建议 Prompt 补充**:
```
When describing file contents, distinguish between:
- DEFINED in this file: classes, methods, enums, macros declared here
- REFERENCED from other files: types, constants used but defined elsewhere
Mark referenced items with their source: "uses Message::PRIO_FATAL (from Message.h)"
```

### OPT-005: 正则搜索自动补充完全限定名 [MEDIUM — Prompt 级]

**影响用例**: TC-A08

**问题根因**: 用户提供的正则 `class\s+\w+\s*:\s*public\s+Channel` 仅匹配非限定名。跨模块代码使用 `Poco::Channel` 完全限定名, 导致遗漏。AICA 虽主动放宽了模式, 但仍未考虑命名空间前缀。

**建议 Prompt 补充**:
```
When searching for class inheritance patterns:
1. Always search for BOTH unqualified and fully-qualified names.
   Example: If user searches "public Channel", also search "public Poco::Channel"
2. If initial regex yields few results, consider namespace variants automatically.
3. Report scope: "Found N in Foundation, M in Net, K in other modules"
```

**预期效果**: TC-A08 覆盖率从 80% → 95%+

---

## 四、问题趋势对比 (Calculator vs POCO)

| 问题类别 | Calculator | POCO 优化前 | POCO 优化后 (重测) | 趋势 |
|----------|-----------|-----------|-------------------|------|
| 路径解析 (P0-002) | 严重 | **已修复 ✅** | 已修复 ✅ | 改善, 稳定 |
| 工具调用幻觉 (P0-003) | 存在 | **未复现 ✅** | 未复现 ✅ | 改善, 稳定 |
| 确认对话框 (P1-005) | 缺失 | **已修复 ✅** | 已修复 ✅ | 改善, 稳定 |
| 数字一致性 (P1-009) | 5+ 用例 | 5/14 (36%) | **部分改善 ⚠️** (三重→二重, 差值缩小) | Phase1 部分有效 |
| list_dir 截断 (P2-016) | 存在+不稳定 | 249/283/326 | **99.7% (327/326)** | **Phase3 显著改善 ✅** |
| read_file 完整性 | D→B+ | C+ (35-40%) | **B+ (70%)** | **Phase2 显著改善 ✅** |
| 继承 vs 引用区分 | 未测 | body 混淆 | **completion 区分 ⚠️** (body 仍混淆) | Phase4 部分有效 |
| 跨模块搜索覆盖 | 有限 | 80% | **80% (未改善)** | Phase5 未生效 ❌ |
| ask_followup_question | 正常 | 工具未调用 | **工具未调用, 根因确认** (LLM 无 tool_calls) | Phase6 诊断生效 ✅ |
| condense 历史丢失 (P1-013) | 0% 回忆率 | 50% 回忆率 | **100% 回忆率** | **Phase7 完全修复 ✅** |

---

## 五、优先修复建议

| 优先级 | 优化项 | 预期收益 | 实施难度 |
|--------|--------|----------|----------|
| 优先级 | 优化项 | 实施前 | 实施后 | 状态 |
|--------|--------|--------|--------|------|
| **1** | OPT-001: 数字一致性 Prompt | 5/14 (36%) 三重矛盾 | 二重矛盾, 差值缩小 | **部分有效 ⚠️** — LLM 计数天花板 |
| **2** | OPT-002: list_dir 稳定性 | 249/283 不稳定 | 327/326 (99.7%) | **验证通过 ✅** |
| **3** | OPT-003: read_file 摘要模板 | C+ (35-40%) | B+ (70%) | **验证通过 ✅** |
| **4** | OPT-004: 定义 vs 引用区分 | body 混淆 | completion 区分 | **部分有效 ⚠️** |
| **5** | OPT-005: 正则搜索命名空间 | 80% 覆盖 | 80% (未改善) | **未生效 ❌** |
| **6** | OPT-006: ask_followup 诊断 | 无诊断信息 | Debug 日志确认根因 | **诊断生效 ✅** |
| **7** | OPT-007: condense 历史强化 | 50% 回忆率 | 100% 回忆率 | **验证通过 ✅** |

---

## 六、A 类测试总结

### 整体评估

A 类 (工具正确性) 14 个用例全部完成并经优化重测验证。

| 阶段 | PASS 率 | PARTIAL PASS | FAIL |
|------|---------|-------------|------|
| 优化前 | 71% (10/14) | 4 (A01/A05/A12/A14) | 0 |
| **优化后 (重测)** | **86% (12/14)** | **2 (A06/A12)** | **0** |

3 个用例从 PARTIAL PASS 升级为 PASS: TC-A01, TC-A05, TC-A14

### 优化效果总结

| Phase | 优化内容 | 效果 | 评级 |
|-------|----------|------|------|
| Phase 1 | 数字一致性 Prompt | 三重矛盾→二重, 差值缩小, 但未完全消除 | ⚠️ 部分有效 |
| Phase 2 | read_file 摘要模板 | 覆盖率 35% → 70%, 命名空间/#include/Protected 全出现 | ✅ **显著改善** |
| Phase 3 | list_dir 稳定性 | 覆盖率 76-87% → 99.7% | ✅ **显著改善** |
| Phase 4 | 定义 vs 引用 | completion 区分生效, body 未一致体现 | ⚠️ 部分有效 |
| Phase 5 | 正则命名空间 | 搜索范围被缩小, 未生效 | ❌ 未生效 |
| Phase 6 | ask_followup 诊断 | Debug 日志确认根因: LLM 无 tool_calls 字段 | ✅ 诊断生效 |
| Phase 7 | condense 历史强化 | 回忆率 50% → 100%, 工具名误归修复 | ✅ **完全修复** |

### 问题分布 (A 类, 优化后)

| 问题来源 | 优化前 | 优化后 | 变化 |
|----------|--------|--------|------|
| LLM 行为 (Prompt) | 22 | ~14 (数字一致性改善, 摘要/区分改善) | -36% |
| 工具问题 (代码) | 7 | ~3 (list_dir/condense 修复, ask_followup 根因确认) | -57% |

### 与 Calculator A 类对比 (含优化后)

| 维度 | Calculator A类 | POCO 优化前 | POCO 优化后 |
|------|---------------|-----------|-----------|
| PASS 率 | 64%→86% (修复后) | 71% | **86%** |
| FAIL 率 | 21%→7% | 0% | **0%** |
| P0 问题 | 6→0 | 0 | **0** |
| 零虚构 | 大部分 | 全部 | **全部** |
| condense 回忆率 | 0% | 50% | **100%** |

### 遗留问题与 B 类测试建议

**已解决/改善**: P0-002/003, P1-005/013, read_file 完整性, list_dir 稳定性

**遗留待解决**:
1. **数字一致性 (P1-009)**: LLM 固有计数弱点, Prompt 已接近天花板; 可能需要工具侧辅助 (自动附加精确计数)
2. **ask_followup_question**: 根因确认为 LLM function calling 配置; 需短期 fallback + 长期配置修复
3. **跨模块正则搜索 (OPT-005)**: Prompt 未被遵守; 可能需要工具侧实现 (自动扩展搜索范围)

**B 类测试重点关注**:
1. **数字一致性**: B 类涉及大量计数, 预计 P1-009 仍会出现
2. **继承 vs 引用区分**: B01 (类继承) 和 B02 (设计模式) 直接测试 Phase 4
3. **跨模块理解**: B03 (跨文件依赖) 验证 OPT-005
4. **模板理解**: B05 (模板参数) 是 POCO 特有测试
