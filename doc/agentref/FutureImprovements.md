# AICA 未来提升方向记录

> 基于人工测试过程中发现的问题和优化机会
> 持续更新

---

## BF-01: 快捷键冲突

**问题**: Ctrl+Alt+A 与 VS2022 内置命令窗口冲突，无法打开 AICA Chat。
**建议**:
- 在 Options 页面提供自定义快捷键配置
- 或改用不冲突的组合键（如 Ctrl+Shift+Alt+A）

---

## BF-02: 对话质量

### 内部推理泄露 (严重)
**问题**: MiniMax-M2.5 不使用 `<thinking>` 标签，直接在回复中输出推理过程（如 "用户只是简单地打了个招呼..."）。`ResponseQualityFilter.IsInternalReasoning()` 未拦截。
**建议**: `ReasoningStartPatterns` 增加 "用户只是"、"用户用"、"用户在" 等模式。

### 语言未跟随用户 (中等)
**问题**: 用户说 "hello"，AI 回复中文。
**建议**: System Prompt 中语言跟随指令需要加强，或增加后处理检测（检测用户输入语言 vs 回复语言是否一致）。

### 尾部推销未过滤 (中等)
**问题**: "请问你今天需要什么帮助？" 未被 `TrailingOfferPatterns` 拦截。
**建议**: 增加 "请问.*帮助"、"请问.*需要" 等中文尾部模式。

### 对话场景虚假警告 (轻微)
**问题**: 问候回复触发了 "AI 描述了要执行的操作但未实际调用工具" 的 ⚠️ 警告。
**建议**: 对话式回复（IsLikelyConversational=true）不应触发该警告。

---

## BF-05: 索引体验

**问题**: 索引是后台异步执行的，用户无法感知进度。
**建议**: 在 UI 上显示索引进度条或状态提示（如 "正在索引项目... 2760 files"），让用户知道索引正在进行。

---

## TC-01: 工具调用与思考泄露

### 思考文本部分泄露 (中等)
**问题**: `ResponseQualityFilter` 拦截了 84 chars 的思考文本，但仍有部分泄露到用户界面（如 "用户要求读取..." 和 "The user asked me to read..."）。
**建议**:
- 在 `IsInternalReasoning` 中增加 "用户要求"、"The user asked" 等模式
- 考虑对 MiniMax-M2.5 模型的 attempt_completion 前文本做更积极的过滤

### 完成摘要质量 (亮点)
**观察**: attempt_completion 的摘要质量高，结构化输出包含 [文件结构]、[主要方法]、[宏定义]、[成员变量] 分段，覆盖率良好。

---

## TC-02: 搜索与错误恢复

### 搜索恢复能力 (亮点)
**观察**: 第一次用复杂正则 `class\s+\w+\s*:\s*(?:public\s+)?Channel` 搜索返回 0 结果后，AI 自动简化为字面量 `: public Channel` 并成功找到 16 个匹配。3 轮迭代完成，展示了良好的搜索策略调整能力。

### TOOL_EXACT_STATS 数字一致性 (亮点)
**观察**: AI 完全使用了 TOOL_EXACT_STATS 提供的精确数字（16 matches, 14 files），未手动计数或估算。完成摘要以结构化表格列出所有 16 个子类，覆盖率 100%。

### 搜索覆盖率限制 (轻微)
**问题**: 搜索仅覆盖 `Foundation` 模块中直接继承 `Channel` 的类，未搜索间接继承（如继承 `SplitterChannel` 的类）或 `Poco::` 命名空间限定形式。
**建议**: 对复杂继承分析任务，可考虑多轮搜索策略：先搜直接继承，再搜间接继承链。

---

## TC-13: 复杂任务规划与上下文管理

### Condense 导致灾难性上下文丢失 (严重)
**问题**: 第 3 轮迭代时 message count 达到 11 触发 proactive condense。压缩后 LLM 完全丢失了原始任务目标（"分析 Logger 系统完整架构"），退化为反复询问用户想做什么。浪费了 3 轮 ask_followup_question 迭代。
**根因**: Condense 摘要未保留原始用户请求的完整信息，或 condense 阈值（message count 11）过于激进。
**建议**:
- R2 的 CondenseSummary 需确保 `UserRequests` 字段始终保留完整的原始请求
- 提高 proactive condense 的 message count 阈值（如 15-20）
- Condense 后注入一条系统消息提醒 LLM 继续原始任务

### 任务范围缩窄 (严重)
**问题**: 原始请求要求分析 Channel + Formatter + Message 三个维度，最终结果仅覆盖 Channel。5 步原始计划被替换为 4 步窄计划。
**建议**: Plan 状态应在 condense 时被保留并重新注入，防止 LLM 丢失任务目标。

### 不必要的 followup 追问 (中等)
**问题**: 任务目标已在用户消息中明确说明，condense 后 LLM 仍连续 3 次调用 ask_followup_question 询问用户想做什么。
**建议**: 在 condense 摘要中显式包含 "用户最新请求: [原文]"，并在 system prompt 中增加 "condense 后不要重新询问已明确的任务"。

### 重复工具调用被正确拦截 (亮点)
**观察**: 迭代 9-11 的 3 次重复 read_file Channel.h 调用被去重机制正确拦截，证明 Phase 2.5 的工具去重功能正常。

---

## SEC-01: 安全机制一致性

### R5 去重放宽对安全拒绝的副作用 (严重)
**问题**: R5 改动让失败的工具调用可以重试，但未区分"安全拒绝"和"临时失败"。对 `.git/config` 的 read_file 被安全机制拒绝后，签名从去重集移除，导致 LLM 在迭代 1、2、6 反复重试同一受保护路径。
**建议**: 在去重放宽逻辑中增加判断：如果错误消息包含 "Access denied" 或 "path traversal"，则**不**从去重集移除签名（安全拒绝不应允许重试）。

### list_dir 未保护 .git 目录 (中等)
**问题**: `read_file .git/config` 被拦截，但 `list_dir .git` 成功返回了 `.git/` 目录的完整内容（hooks, refs, config 文件大小等）。安全策略不一致。
**建议**: 在 SafetyGuard 中对 list_dir 也应用 .git 路径保护，或至少过滤掉 .git 目录的内容。

### 错误后恢复效率低 (轻微)
**问题**: 安全拒绝后 LLM 花了 8 轮迭代尝试各种方式绕过（list_dir, find_by_name, grep_search），未及时放弃。
**建议**: 在安全拒绝的错误消息中增加更明确的提示，如 "This path is in a protected directory (.git). Do NOT attempt to access it through other tools."

---

## B-1 复测: ResponseQualityFilter 架构限制

### 300 字符阈值导致过滤失效 (严重)
**问题**: `IsInternalReasoning` 方法在文本超过 300 字符时直接返回 false，跳过所有 pattern 检查。MiniMax 模型将推理文本和实际回答混在一个文本块中输出（不用 `<thinking>` 标签），总长度 >300 chars，导致新增的 pattern 完全不被检查。
**根因**: 过滤器假设推理文本是短的独立片段，但 MiniMax 把推理和回答混在一起。
**建议**:
- 方案 A: 移除 300 字符阈值，改为只检查文本**开头部分**（如前 200 字符）是否匹配 reasoning pattern
- 方案 B: 对整个文本做分段处理——如果开头匹配 reasoning pattern，截取到第一个空行，只保留后半部分
- 方案 C: 在 System Prompt 中更强烈地指示模型使用 `<thinking>` 标签

### Custom Instructions 覆盖语言规则 (中等)
**问题**: 用户在 Options 中设置了 "使用中文回复" custom instruction，导致 system prompt 的 "respond in the same language as the user's request" 规则被覆盖。
**建议**: 检查 custom instructions 是否包含语言指令，如有冲突则在 system prompt 中明确标注优先级。

---

## B-3 复测 TC-13: Condense 修复验证

### Fix 4 核心目标达成 (亮点)
**观察**: Condense 后 LLM **不再询问用户想做什么**。原始请求被保留在压缩后的历史中，LLM 在 condense 后立即继续分析任务。最终结果覆盖了 Channel + Formatter + Message 三个维度（修复前仅覆盖 Channel）。

### 对比改善

| 指标 | 修复前 | 修复后 |
|------|--------|--------|
| Condense 触发 | msg=11 (iter 3) | msg=19 (iter 9) |
| ask_followup 追问 | 3 次 | 0 次 |
| 任务覆盖率 | 1/3 (Channel only) | 3/3 (全部) |
| 总迭代 | 17 | 46 |

### 迭代效率低 (严重 — Phase 3 级别问题)
**问题**: 46 次迭代中大量重复搜索和被拦截的 duplicate calls。Condense 后 LLM 重新搜索了已有的信息（因为 condense 摘要不够详细）。HasAutoCondensed=true 阻止了第二次 condense，导致 message count 持续增长到 79。
**根因**: 单 Agent 串行 + 一次性 condense 限制 + condense 摘要丢失工具结果细节
**建议**: 这是 Phase 3（多 Agent 并行）和 Phase 4（分层上下文）要解决的核心问题。当前阶段已无法通过简单参数调整解决。

---

## BF-06: 右键上下文菜单 — 代码解释

### R9 复杂度误分类 (严重)
**问题**: "请用中文详细解释以下来自文件 SocketNotifier.cpp 的 C/C++ 代码" 被 R9/R3 的 TaskComplexityAnalyzer 判定为 Complex（可能因为包含代码片段或"详细"关键词），导致注入全部 13 个工具和 "[System: Task Planning Required]" 指令。代码解释本应是 Simple 任务，2-3 轮迭代即可完成。
**建议**: TaskComplexityAnalyzer 需要识别"解释代码"类请求为 Simple，即使请求文本较长（因为包含了代码片段）。

### DetectToolExecutionClaim 误判 (中等)
**问题**: Iteration 3 中 LLM 输出了完整的代码解释（3602 chars），但被 DetectToolExecutionClaim 误判为幻觉（因为回答中包含表格 `|...|---` 格式），触发了纠正消息，导致 Agent 从头开始搜索。
**建议**: DetectToolExecutionClaim 的 "Markdown table" 检测条件过于宽泛。应排除在 read_file 之后的响应（因为用户要求解释已读取的代码，包含表格是合理的输出格式）。

### 迭代 22-50 全部浪费在重复 read_file (严重)
**问题**: Condense 后 LLM 反复尝试 read_file 同一文件（Channel.h、Message.h 等），全部被去重拦截，但 LLM 不学习，继续尝试。28 轮迭代全部浪费。
**建议**: 连续 N 次（如 5 次）去重拦截后应强制触发 attempt_completion，而非让 LLM 继续无效尝试。

---

## STB-04: 设置修改未立即生效

### Options 保存后 LLM Client 未重新初始化 (中等)
**问题**: 修改 API Endpoint（或可能其他 LLM 相关设置如 Model Name、Temperature 等）后点击确认保存，旧会话仍使用修改前的配置。必须新建会话才能使新设置生效。
**根因推测**: `ChatToolWindowControl.InitializeAgentComponents()` 中创建的 `OpenAIClient` 实例在会话生命周期内不会更新。Options 页面的保存仅写入 VS Settings 存储，但不会通知已打开的 ChatToolWindow 重新读取配置。
**影响范围**: 所有 LLM 相关设置（API Endpoint、Model、Temperature、MaxTokens、ApiKey、TopP、TopK、Timeout、BypassProxy）和 Agent 设置（EnableToolCalling、MaxAgentIterations、CustomInstructions）可能都受影响。
**建议**:
- 方案 A: 在 Options 保存时发布事件，ChatToolWindowControl 监听并调用 `InitializeAgentComponents()` 重建 LLM client
- 方案 B: 每次 `SendMessageAsync` 时检查 Options 是否有变化，有变化则重建 client
- 方案 C: 在 UI 上显示提示 "设置已保存，新建会话后生效"
**优先级**: 待日后排查

---

## UI-05: 流式渲染闪烁

### 渲染过程中有闪烁/跳动 (中等)
**问题**: 流式文本输出过程中，UI 出现闪烁或跳动现象。
**根因推测**: WPF WebBrowser 控件每次更新 innerHTML 时会触发重绘，高频更新导致视觉闪烁。
**建议**:
- 方案 A: 降低 UI 更新频率（如每 100ms 或每 N 个 token 批量更新一次，而非每个 token 都更新）
- 方案 B: 使用 CSS `content-visibility: auto` 或 `will-change` 优化渲染性能
- 方案 C: 使用 JavaScript 增量追加文本（appendChild）而非每次替换整个 innerHTML

---

## STB-07: 解决方案直接切换时会话列表不更新

### 直接切换解决方案时会话历史不刷新 (中等)
**问题**: 通过 VS2022 菜单 文件→打开→项目/解决方案 直接切换到新项目时，AICA 的会话历史列表不会自动切换到新项目，仍显示旧项目的会话。需要手动操作才能看到新项目的会话。
**根因推测**: `SolutionEventListener.OnAfterOpenSolution` 事件可能在直接切换时未正确触发，或触发后 `ChatToolWindowControl` 未刷新侧边栏会话列表。
**建议**: 检查 `OnAfterOpenSolution` 在直接切换场景下是否触发，如果触发则确保会话列表刷新逻辑被调用。

---

## TC-10: 反馈按钮脚本错误

### 👍/👎 点击后弹出脚本错误 (轻微)
**问题**: attempt_completion 完成卡片的 👍/👎 反馈按钮点击后弹出 JavaScript 脚本错误对话框，功能无法正常使用。
**根因推测**: WPF WebBrowser (IE/Trident) 的 JS 引擎对按钮点击事件处理代码存在兼容性问题。
**优先级**: 低，暂不处理。

---

## UI-04: 多计划切换问题与增强

### Plan 切换后旧 Plan 内容丢失 (中等)
**问题**: 当产生 Plan 2 后切换回 Plan 1 标签，Plan 1 的步骤内容全部消失，只剩空白标签。Plan 2 内容正常显示。
**根因推测**: 前端切换 Plan 标签时可能未正确缓存已完成 Plan 的步骤数据，或 JavaScript 渲染逻辑在切换时清除了旧 Plan 的 DOM 元素。
**建议**: 切换标签时保留每个 Plan 的完整步骤内容，可使用 display:none/block 切换而非销毁重建 DOM。

### Plan 标签增加来源索引（需求）
**需求**: 在 Plan 1 / Plan 2 标签底部增加一行索引文字，显示触发该 Plan 的用户输入（如 "分析 Logger 系统的完整架构"）。点击索引后，主聊天窗口自动滚动到该用户提问的位置。
**实现思路**:
- update_plan 调用时记录触发该 Plan 的原始用户消息 ID 或位置
- 标签下方渲染可点击的索引文字
- 点击时调用 JavaScript scrollIntoView 跳转到对应消息元素

---

## ~~上下文预算与 MaxTokens 调优~~ [已修复 2026-03-21]

### ~~tokenBudget 远小于模型实际上下文窗口~~ [已修复]
**已解决**: 采用方案 C — 新增 `ContextWindowSize` 配置项，与 MaxTokens 完全解耦。
**修复内容**:
- `ContextWindowSize` 新属性: 默认 196,608（MiniMax-M2.5 实际上下文窗口）
- `MaxTokens` 默认值: 4096 → 16,384（MiniMax-M2.5 输出上限 65K，取保守值）
- `tokenBudget` 公式: `MaxTokens * 8` → `ContextWindowSize - MaxTokens - 3000` = 177,224
- 消息数 condense 阈值: 固定 18 → 动态计算 `ComputeCondenseMessageThreshold(budget)` = 70
- 用户可在 VS Options 中按模型调整 ContextWindowSize
**改动文件**: LLMClientOptions.cs, GeneralOptions.cs, ChatToolWindowControl.xaml.cs, TokenBudgetManager.cs, AgentExecutor.cs, ContextManager.cs
**测试**: 13 个新单元测试 (TokenBudgetManagerThresholdTests.cs)

---

## TC-11: 歧义请求未优先追问

### LLM 不主动调用 ask_followup_question 澄清歧义 (严重)
**问题**: 用户发送歧义请求"帮我修改一下那个配置文件"时，LLM 没有调用 `ask_followup_question` 询问是哪个文件，而是自行搜索了 4 种配置文件格式（*.ini, *.json, *.conf, *.config）。随后 condense 触发，LLM 丢失上下文，转而执行 git status（3 次被取消）。`ask_followup_question` 最终仅作为错误恢复被触发，问的是 git status 问题而非配置文件澄清。
**根因**: System Prompt 缺少对歧义请求的处理指引。LLM 倾向于"自己先搜索"而非"先问用户"。
**建议**:
- 在 System Prompt 工具使用指引中增加规则：当用户请求中包含"那个"、"这个"等指示代词但未指定具体目标时，应优先调用 `ask_followup_question` 澄清
- 或在 DynamicToolSelector 中检测歧义请求，自动注入 `ask_followup_question` 并提高其优先级

---

## TC-04: find_by_name 默认结果数偏小

### LLM 多次递增 max_results 浪费迭代 (轻微)
**问题**: "找到所有 Test*.cpp 文件" 时，LLM 以 max_results=100 起步，发现不够后递增到 200、300，共 3 次调用才获取全部 277 个结果，浪费 2 个额外迭代。
**建议**: 在 System Prompt 的 find_by_name 工具说明中建议 LLM 对大型项目使用较大的 max_results（如 500），或在 FindByNameTool 中将默认值从 100 提高到 500。

---

## TC-07/TC-08: 统一文件创建与编辑流程

### 删除 write_to_file 工具，统一为 run_command + edit 两步流程 (中等)
**问题**: 当前 `write_to_file` 和 `edit` 是两个独立工具，LLM 有时对空文件选错工具（TC-07 中先尝试 write_to_file 失败才改用 edit）。且 write_to_file 不提供 diff 预览，用户无法在写入前确认内容。
**改进方案**:
1. **删除 `WriteFileTool` 类**及其在 system prompt、DynamicToolSelector 中的所有引用
2. **新文件创建流程**: LLM 调用 `run_command`（如 `touch filename` 或 `New-Item filename`）创建空文件，用户通过命令确认对话框决定是否创建
3. **文件内容编辑**: 创建后 LLM 调用 `edit` 工具写入内容，用户通过 diff 预览确认
4. **System Prompt 更新**: 在工具说明中指导 LLM 新建文件的两步流程

**优势**:
- 每一步都有明确的用户确认（创建确认 + 内容确认）
- 用户始终能通过 diff 预览看到将要写入的内容
- 减少一个工具，降低 LLM 工具选择的复杂度

**涉及文件**:
- 删除: `AICA.Core/Tools/WriteFileTool.cs`
- 修改: `SystemPromptBuilder.cs`（移除 write_to_file 工具定义）
- 修改: `DynamicToolSelector.cs`（移除 write_to_file 引用）
- 修改: `AgentExecutor.cs`（移除 WriteFileTool 注册）
- 修改: System Prompt 工具说明（增加两步创建指引）
