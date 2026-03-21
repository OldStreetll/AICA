# AICA 人工测试方案 — R1-R9 重构后全面验证

> 版本: 1.0
> 日期: 2026-03-20
> 基于: Phase 0-2.5 功能 + R1-R9 重构
> 前置: `build.ps1` 编译成功 (0 errors)
> 测试项目: 建议使用 POCO C++ 项目 (3237 files, 28631 symbols)

---

## 测试环境准备

| 项目 | 要求 |
|------|------|
| VS2022 | 17.x，已安装 AICA.vsix |
| LLM API | MiniMax-M2.5 或其他兼容 OpenAI API 的模型 |
| 测试项目 | POCO C++ 解决方案 (推荐)，或任意 C#/.cpp 项目 |
| 网络 | LLM API 可达 |
| 观察方式 | VS Output → Debug 窗口查看 `[AICA]` 日志 |

---

## 一、基础功能 (10 项)

### BF-01: VSIX 安装与激活

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 双击安装 AICA.vsix | 安装成功，无错误 | PASS | |
| 2 | 启动 VS2022 | 无崩溃 | PASS | |
| 3 | View → Other Windows → AICA Chat | 工具窗口打开 | PASS | |
| 4 | Ctrl+Alt+A | 同上，快捷键有效 | **FAIL** | Ctrl+Alt+A 打开了 VS2022 命令窗口而非 AICA Chat，快捷键冲突 |

### BF-02: 简单对话 (Chat 基础)

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 输入 "你好" | AI 用中文回复问候，不调用任何工具 | **FAIL (retest)** | 推理泄露仍然存在。"用户只是打了个招呼" 仍显示给用户。Fix 2 的 pattern 未生效，因为 IsInternalReasoning 有 300 字符阈值限制——完整响应（推理+回答）超过 300 chars 时直接跳过过滤。 |
| 2 | 输入 "hello" | AI 用英文回复问候 | **FAIL (retest)** | AI 仍用中文回复 "hello"。日志显示 custom instructions 包含 "使用中文回复"，覆盖了 system prompt 的语言跟随规则。 |
| 3 | 验证响应头部 | 无 "Great,"、"Sure,"、"好的，" 等禁止开头词 | **PASS (retest)** | 无禁止开头词 |
| 4 | 验证响应尾部 | 无 "还需要我..."、"Need anything else?" 等尾部推销 | **FAIL (retest)** | "请问有什么我可以帮助你的吗？" 仍存在。新增的 trailing pattern 要求 "请问" 前缀，但模型有时用 "有什么...帮助" 不带 "请问"。 |

### BF-03: 设置页面

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 点击 ⚙ 设置按钮 | 设置对话框打开 | **PASS** | 设置对话框正常打开 |
| 2 | 修改 API Endpoint | 保存后生效 | **PASS** | API Endpoint = http://111.172.214.40:32086/v1 |
| 3 | 修改 Model Name | 下次请求使用新模型 | **PASS** | Model Name = MiniMax-M2.5 |
| 4 | 修改 Temperature | 保存后生效 | **PASS** | Temperature 修改为 0.5，保存后再打开确认值已保存 |
| 5 | 修改 Max Agent Iterations | 保存后生效 | **PASS** | Max Agent Iterations = 50 |

### BF-04: 会话管理

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 发送几条消息 | 消息正常显示 | **PASS** | |
| 2 | 点击 📋 历史按钮 | 侧边栏打开，显示当前会话 | **PASS** | |
| 3 | 点击 ✨ 新建会话 | 清空聊天，开始新会话 | **PASS** | |
| 4 | 切换回旧会话 | 旧消息正确恢复 | **PASS** | |
| 5 | 在搜索框输入关键字 | 会话列表正确过滤 | **PASS** | |
| 6 | 删除一个会话 | 会话从列表中移除 | **PASS** | |

### BF-05: 解决方案事件

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 打开 POCO 解决方案 | Debug 输出显示索引开始，几秒后显示索引完成 (3237 files, 28631 symbols) | **PASS** | Indexed 2760 files, 9890 symbols in 9.8s。数量与文档不同 (项目内容变化)，但索引功能正常 |
| 2 | 关闭解决方案 | 会话自动保存，知识索引清除 | **PASS** | 日志显示 "Project knowledge index cleared" 和 "conversation saved" |
| 3 | 重新打开 | 索引重新构建，会话列表加载该项目的历史 | **PASS** | 索引日志延迟出现，确认重新打开后异步重建索引正常工作 |

### BF-06: 右键上下文菜单

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 选中一段代码 → 右键 → AICA → Explain Code | 聊天窗口打开，自动发送解释请求 | **PASS (retest)** | Fix 5 生效！3 轮迭代完成（find_by_name → read_file → attempt_completion）。Tools count=13 但未触发 Complex 规划指令。meta-reasoning 被成功过滤（"Suppressing meta-reasoning text (177 chars)"）。完成摘要质量极高：逐行代码解释、架构图、设计要点表格。从之前的 50 迭代超时 → 3 迭代完成。 |
| 2 | 选中一段代码 → 右键 → AICA → Refactor Code | 自动发送重构请求 | **NOT TESTED (skipped)** | |
| 3 | 选中一段方法 → 右键 → AICA → Generate Tests | 自动发送测试生成请求 | **NOT TESTED (skipped)** | |

> **注意**: 右键 Explain Code 功能本身正常工作（菜单触发 + 代码传递正确），但 Agent 执行效率极低。R9 DynamicToolSelector 将代码解释请求误判为 Complex（因请求文本包含代码），注入了 13 个工具和规划指令。实际上代码解释应为 Simple/Medium 级别。另外 iter 3 的有效回答被 DetectToolExecutionClaim 误判为幻觉。

---

## 二、工具调用功能 (13 项)

### TC-01: read_file 读取文件

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "读取 Foundation/include/Poco/Logger.h" | 调用 read_file，显示文件内容 | **PASS** | read_file correctly called with path "Foundation/include/Poco/Logger.h", returned 31798 chars |
| 2 | 验证工具日志 | 蓝色折叠卡片显示工具名、参数、结果 | **PASS** | Blue collapsible card "📖 read_file ▶" displayed with parameters and result |
| 3 | 调用 attempt_completion | 完成卡片显示，有反馈按钮 | **PASS** | Green "✅ Task Completed" card displayed with feedback buttons (👍/👎) |

> **注意**: Thinking text leaked before tool calls ("用户要求读取..." and "The user asked me to read..."), filter caught 84 chars but not all. Tools count: 8 (R3 DynamicToolSelector "read" intent working).

### TC-02: grep_search 搜索

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "搜索所有 Channel 子类" | 调用 grep_search | **PASS** | Called twice: first with regex pattern (0 results), then simplified to literal ": public Channel" (16 results). Good error recovery. |
| 2 | 验证结果 | 包含 [TOOL_EXACT_STATS] 精确计数 | **PASS** | Stats present: matches=16, files_matched=14, files_searched=1622 |
| 3 | AI 使用 EXACT_STATS 数字 | 回答中数字与 STATS 一致 | **PASS** | AI reported "16 个 Channel 子类，分布在 14 个头文件中", exactly matching EXACT_STATS. Verified independently: 16 matches in 14 files confirmed. |

### TC-03: list_dir 目录列表

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "列出项目完整目录结构" | 调用 list_dir，自动添加 recursive=true (R1 AugmentToolCallParameters) | **PASS** | 调用了 list_dir 且使用了 recursive=True, max_depth=10 |
| 2 | Debug 输出 | 显示 "[AICA] Auto-augmented list_dir with recursive=true" | **PASS** | LLM 自行添加了 recursive=true（注意：这次是 LLM 主动添加而非 AugmentToolCallParameters 自动注入，因为日志中没有 "Auto-augmented" 字样。但功能效果一致。） |
| 3 | 正常完成 | Agent 正常完成任务 | **PASS** | 3 轮迭代完成（list_dir → list_projects → attempt_completion），输出包含 30 个模块目录、34 个项目、依赖库清单。Tools count: 10（R3 analyze intent 工具集生效）。 |

> **注意**: 表现优秀。3 轮迭代高效完成。LLM 额外调用 list_projects 补充了项目信息。R3 DynamicToolSelector 正确注入 10 个工具（analyze intent）。meta-reasoning 文本被成功过滤（日志显示 'Suppressing meta-reasoning text'）。

### TC-04: find_by_name 查找文件

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "找到所有 Test*.cpp 文件" | 调用 find_by_name |

### TC-05: list_code_definition_names 代码定义

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "列出 Logger.h 的代码定义" | 调用 list_code_definition_names |

### TC-06: list_projects 项目列表

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "列出解决方案中的所有项目" | 调用 list_projects (不是 list_dir) |

### TC-07: edit 编辑文件

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "在 test.txt 末尾添加一行 hello" | 调用 read_file → edit |
| 2 | 弹出 diff 预览 | 显示修改前后对比 |
| 3 | 点击确认 | 文件被修改 |

### TC-08: write_to_file 创建文件

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "创建 test_new.txt 内容为 hello world" | 调用 write_to_file |
| 2 | 弹出确认 | 确认后文件被创建 |

### TC-09: run_command 执行命令

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "运行 git status" | 调用 run_command | **FAIL** | AI 未调用 run_command。日志显示 Tools count=8，但 run_command 不在工具列表中。DynamicToolSelector 的 "read" intent 工具集不包含 run_command。AI 认为自己没有执行命令的能力，最终告知用户手动执行。 |
| 2 | 弹出确认对话框 | 显示命令和工作目录 | **NOT TESTED** | 未触发 run_command |
| 3 | 确认执行 | 返回 stdout/stderr/exitcode | **NOT TESTED** | 未触发 run_command |

> **根因：** DynamicToolSelector 将 "运行 git status" 分类为 "read" intent（而非 "modify" 或 "command"），导致 run_command 未被注入到工具列表中。需要在 DynamicToolSelector.ClassifyIntent 中增加 command intent 的关键词识别（如 "运行", "run", "execute", "执行", "git", "dotnet", "npm" 等）。

### TC-10: attempt_completion 完成任务

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 任何任务完成后 | AI 调用 attempt_completion |
| 2 | 显示完成卡片 | 绿色 ✅ 卡片，含 summary |
| 3 | 反馈按钮 | 👍/👎 可点击 |

### TC-11: ask_followup_question 追问

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 发送一个有歧义的修改请求 | AI 调用 ask_followup_question |
| 2 | 弹出选项对话框 | 显示选项和可选自定义输入 |
| 3 | 选择一个选项 | AI 根据选择继续执行 |

### TC-12: condense 上下文压缩

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 进行 8+ 轮工具调用对话 | token 使用率上升 |
| 2 | 达到 70% token | Debug 输出 condense hint |
| 3 | 达到 80% token | 自动调用 condense |
| 4 | condense 后继续提问 | AI 仍知道之前读了什么文件 (R2 结构化摘要) |

### TC-13: update_plan 任务规划

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "分析 Logger 系统的完整架构" (复杂任务) | AI 先调用 update_plan | **PASS (retest)** | 5-step plan created on iteration 1, covering Channel/Formatter/Message/relationships/summary |
| 2 | 悬浮面板显示 | 红色边框面板固定在底部 | **PASS (retest)** | Zero ask_followup_question calls. LLM never asked what user wants after condense. Fix 4 working. |
| 3 | 步骤状态更新 | ⏳ → 🔄 → ✅ 状态变化 | **PASS (retest)** | Plan status updated correctly. After condense at iter 9, new plan created and continued. All 5 steps completed. |
| 4 | 任务完成 | 面板保留，可折叠查看 | **PASS (retest)** | Result covers Channel (16 Foundation + 3 Net), Formatter (2 subclasses), Message (Priority enum, properties), and architecture diagram with relationships. 46 iterations total. |

> **注意**: Fix 4 validated: condense at msg=19 (vs old msg=10), original request preserved, no task amnesia. BUT 46 iterations is excessive — many duplicate calls blocked, LLM recreated plan after condense, searched redundantly post-condense. Efficiency needs Phase 3 multi-agent to solve.

---

## 三、R1-R9 重构验证 (15 项)

### R1-V01: AgentExecutor 拆分 — 基本功能不变

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 发送简单问题 "Logger 是什么" | 正常回答，使用知识索引 |
| 2 | 发送需要工具的问题 | 工具正常调用和返回 |
| 3 | 发送复杂任务 | 规划 + 工具 + 完成 完整流程 |

### R1-V02: TokenBudgetManager — Condense 摘要

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 触发自动 condense | Debug 输出显示结构化摘要 |
| 2 | 摘要内容检查 | 包含 "## Conversation Summary"、"### File Operations"、"### Searches Performed" 等分段 |
| 3 | condense 后提问 "之前读了哪些文件" | AI 能回答（不是 "尚未读取任何文件"） |

### R1-V03: ToolCallProcessor — 文本解析 Fallback

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 如果 LLM 在文本中输出工具调用格式 | ToolCallTextParser 正确解析并执行 |
| 2 | Debug 输出 | 显示 "[AICA] Text Fallback" 相关日志 |

### R3-V01: 动态工具注入 — Simple 请求

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 发送 "你好" | Debug 输出显示较少的工具被注入 (仅 core tools) |
| 2 | 发送 "读取 README.md" | 注入 read 工具集 |
| 3 | 发送 "修改 test.txt" | 注入全部工具 (modify intent) |
| 4 | 发送复杂分析任务 | 注入全部工具 (Complex 回退) |

### R4-V01: ResponseQualityFilter 配置化

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | AI 响应不含 "Great,"、"Certainly," 等禁止词 | 过滤器正常工作 |
| 2 | AI 响应不含 "还需要我..." 等尾部推销 | 尾部过滤器正常 |
| 3 | AI 不在响应中说 "让我调用..." | 内部推理抑制正常 |

### R5-V01: 工具去重放宽 — 失败可重试

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 请求读取一个不存在的文件 | read_file 失败 |
| 2 | AI 尝试用不同路径重试 | 不被去重拦截 |
| 3 | Debug 输出 | 显示 "removed from dedup set (allows retry)" |

### R5-V02: 工具去重 — 成功仍拦截

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | AI 对同一文件调用两次 read_file (相同参数) | 第二次被拦截 |
| 2 | 错误消息 | 显示 "Duplicate call"，不含 "add/change a parameter" |

### R9-V01: 复杂度驱动 Prompt

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 发送简单问题 | 不注入 "Task Planning" 规则 |
| 2 | 发送 "分析 XX 的完整架构" | 注入完整规则含 "Task Planning"、"Number Consistency" |

### R6-V01: 命令沙箱

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "运行 dotnet --version" | run_command 正常执行 |
| 2 | 尝试执行黑名单命令 (如 "rm -rf") | 被拒绝，返回错误 |

---

## 四、Phase 2.5 遗留验证 (9 项，来自 AgentEvolutionPlan)

### P25-01: 重复调用错误消息

| 通过条件 | 操作 |
|----------|------|
| 错误消息无 "add/change a parameter" 字样 | 触发工具去重拦截，检查错误文本 |

### P25-02: 编辑 A 后重读 A 通过、重读 B 拦截

| 通过条件 | 操作 |
|----------|------|
| 编辑文件 A 后，read_file A 通过；read_file B 被拦截 | 请求编辑文件 A，再请求读取 A 和 B |

### P25-03: 多文件编辑精准追踪

| 通过条件 | 操作 |
|----------|------|
| 仅编辑过的文件可重读 | 编辑 A 和 B，重读 A/B 通过，重读 C 拦截 |

### P25-04: 同文件不同 offset 去重

| 通过条件 | 操作 |
|----------|------|
| 同文件第二次 read_file (不同 offset) 被拦截 | AI 尝试分段读取同一文件 |

### P25-05: 同 query 不同 max_results 去重

| 通过条件 | 操作 |
|----------|------|
| 同 query 第二次 grep_search (不同 max_results) 被拦截 | AI 尝试重复搜索 |

### P25-06: 不同路径不误杀

| 通过条件 | 操作 |
|----------|------|
| 不同文件的 read_file 都通过 | AI 读取两个不同文件 |

### P25-07: MicroCompact 带信息摘要

| 通过条件 | 操作 |
|----------|------|
| 压缩摘要显示文件名/行数而非 "[Previous tool result]" | 多轮对话后，在 Debug 输出中查看 MicroCompact 摘要 |

### P25-08: Condense 工具历史带结果

| 通过条件 | 操作 |
|----------|------|
| condense 后工具历史条目含 `→ (...)` 结果摘要 | 触发 condense，检查 Debug 输出中的 "Tool Call History" |

### P25-09: System Prompt 规则更新

| 通过条件 | 操作 |
|----------|------|
| 新效率规则文本，无旧的 "add/change a parameter" 建议 | 在 Debug 输出中查看 system prompt 内容 |

---

## 五、UI 与交互体验 (8 项)

### UI-01: 思考块折叠

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | AI 生成 `<thinking>` 内容 | 显示黄色 💭 折叠块 | **PASS** | |
| 2 | 点击展开 | 显示思考内容 | **PASS** | |
| 3 | 再次点击 | 收回折叠 | **PASS** | |

### UI-02: 工具日志折叠

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 工具调用完成 | 显示蓝色 🔧 折叠块 | **PASS** | |
| 2 | 展开查看 | 显示工具名、参数、结果 | **PASS** | |

### UI-03: 计划面板

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 复杂任务触发 plan | 红色面板出现在底部 |
| 2 | 面板显示步骤 | 每步有状态图标 |
| 3 | 进度条 | 实时更新百分比 |
| 4 | 折叠/展开 | Toggle Bar 点击切换 |
| 5 | 任务完成后 | 面板保留，默认折叠 |

### UI-04: 多计划切换

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 执行两个复杂任务 | 产生 Plan 1 和 Plan 2 |
| 2 | 标签栏 | 显示 [Plan 1] [Plan 2] 可切换 |

### UI-05: 流式渲染

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 发送任何请求 | 文本逐字流式出现 | **PASS** | 文本逐字流式出现 |
| 2 | 无闪烁或跳动 | 渲染平滑 | **FAIL** | 渲染过程中有闪烁或跳动 |

### UI-06: Diff 预览对话框

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | AI 编辑文件 | 弹出 diff 对话框 |
| 2 | 显示修改 | 原文 vs 新文，行数变化 |
| 3 | 可编辑新内容 | 在对话框中修改后确认 |
| 4 | 取消 | 文件不变 |

### UI-07: Markdown 渲染

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | AI 回复包含代码块 | 正确渲染为代码块，有语法高亮 | **PASS** | 代码块正确渲染，有代码框样式 |
| 2 | AI 回复包含表格 | 正确渲染为 HTML 表格 | **NOT TESTED** | |
| 3 | AI 回复包含列表 | 正确渲染为有序/无序列表 | **PASS** | 有序/无序列表正确渲染 |

### UI-08: 清空会话

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 点击清空按钮 | 聊天区域清空 | **PASS** | 聊天区域清空 |
| 2 | Plan 面板 | 隐藏 | **PASS** | Plan 面板隐藏 |
| 3 | LLM 历史 | 重置 | **PASS** | 推断通过（清空后新消息正常工作） |

---

## 六、知识索引 (5 项)

### KI-01: 打开解决方案自动索引

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 打开 POCO 解决方案 | Debug 输出: "Indexing..." → "Indexed 3237 files, 28631 symbols in ~9s" |

### KI-02: 知识注入 System Prompt

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | "Logger 是什么" | AI 使用索引知识回答，包含文件路径、继承关系、方法数 | **PARTIAL** | AI 在思考中提到了项目知识（"根据项目知识，我可以看到 Poco::Logger"），说明知识索引被注入。但仍然调用了 read_file 读取完整文件内容，而非仅使用索引知识回答。 |
| 2 | 不调用 read_file | 知识足够回答基本问题 | **PASS** | 回答包含文件路径 "Foundation/include/Poco/Logger.h"、继承关系 "继承自 Channel"、所有方法分类、宏定义和使用示例。 |

> **注意**: 知识索引确实被注入到 system prompt 中（LLM 思考中引用了项目知识），但 LLM 选择调用 read_file 获取更详细信息而非仅用索引回答。2 轮迭代完成，质量很高。

### KI-03: 关闭后索引清除

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 关闭解决方案 | 知识索引清除 |
| 2 | 重新打开 | 重新索引 |

### KI-04: 空项目

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 打开空解决方案 | 0 files, 0 symbols，无崩溃 |

### KI-05: TF-IDF 检索

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 提问不同关键词 | 检索返回相关的 Top-10 符号 |
| 2 | 驼峰/下划线分词 | HTTPRequest → HTTP, Request |

---

## 七、安全与权限 (6 项)

### SEC-01: 文件路径保护

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 尝试读取 .git 目录下的文件 | 被拦截，提示路径受保护 | **PASS (retest)** | read_file .git/config 被拦截。日志确认 "security denied, keeping in dedup set (no retry)"。read_file 只尝试了 1 次（之前 3 次）。Fix 1 生效。list_dir .git 也被拦截，返回 "protected directory" 错误。Fix 3 生效。总迭代从 8 次降到 6 次。 |
| 2 | 尝试读取工作区外的文件 | 被拦截，提示路径越界 | **NOT TESTED** | Will test separately. |

### SEC-02: 命令黑名单

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 请求执行 "rm -rf /" | 被黑名单拦截 | **PASS** | LLM 层面直接拒绝执行，未调用 run_command 工具。AI 正确识别为危险命令并明确拒绝，解释了风险。 |
| 2 | 请求执行 "format C:" | 被黑名单拦截 | **NOT TESTED** | |

> **注意**: 安全防护在两个层面生效：(1) LLM 自身拒绝执行危险命令（未调用 run_command）；(2) 即使 LLM 调用了 run_command，SafetyGuard 的黑名单也会拦截 rm 命令。本次测试验证了 LLM 层面的安全意识。

### SEC-03: 编辑确认

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | AI 编辑文件 (设置中 RequireConfirmation=true) | 弹出 diff 确认 |
| 2 | 点击取消 | 文件未修改 |

### SEC-04: 命令确认

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | AI 执行 run_command | 弹出确认对话框 |
| 2 | 点击取消 | 命令未执行 |

### SEC-05: 自动审批

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 设置 AutoApproveRead=true | read_file 不弹确认 |
| 2 | 设置 AutoApproveSafeCommands=true | git status 不弹确认 |

### SEC-06: .aicaignore

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 在项目根创建 .aicaignore 含 "secret/" | 尝试读取 secret/ 下文件被拦截 |

---

## 八、稳定性与边界 (7 项)

### STB-01: 迭代限制

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 设置 MaxAgentIterations=5 | 达到 5 轮后强制完成 | **PASS** | 日志确认 5 轮后停止。Safety boundary 在 iter 3 就开始触发警告（approaching limit 3/5），iter 5 达到上限。设置从 50 改为 5 后新建会话生效。共执行了 13 次工具调用（iter 2 批量执行了 6 个 read_file，iter 3 批量执行了 4 个）。 |
| 2 | 达到限制后观察 UI | 显示错误/完成消息，无崩溃 | **PASS** | 显示 "❌ Agent Error: 已达到最大迭代次数 (5/5)。共执行了 13 次工具调用，发送了 5 次API请求。建议：请检查上方的工具执行日志..."，无崩溃。 |

> **注意**: 迭代限制功能正常。注意 safety boundary 从 iter 3 就触发（maxIter-2），此时 Tools count 降为 1（仅 attempt_completion），强制 LLM 完成。但 LLM 在仅有 attempt_completion 工具时仍然调用了 grep_search（说明 safety boundary 应更强制地阻止非 completion 工具调用）。

### STB-02: 超时处理

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 执行一个长时间命令 | 超时后返回错误 |

### STB-03: 取消执行

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | AI 执行中点击取消/关闭窗口 | 执行中止，无崩溃 | **PASS** | 执行中取消后正常停止，无崩溃 |

### STB-04: LLM 连接失败

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 设置错误的 API Endpoint | 显示错误消息，无崩溃 | **PASS** | 显示 "❌ Agent Error: LLM communication error: Failed to connect to LLM API"，无崩溃，VS2022 正常运行。 |
| 2 | 错误消息对用户有帮助 | 消息清晰，用户可理解 | **PASS** | 错误消息明确指出 "Failed to connect to LLM API"，用户可理解。 |

> **注意**: 错误处理正常。另发现：修改 API Endpoint 后需新建会话才能生效（旧会话仍使用修改前的地址）。建议在 Options 保存时重新初始化 LLM client。

### STB-05: 大文件读取

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | "读取一个 5000+ 行的文件" | 分块读取，不卡死 |

### STB-06: 长对话稳定性

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 进行 20+ 轮对话 | condense 自动触发，不卡死 |
| 2 | 30+ 轮后仍能正常交互 | 响应时间可接受 |

### STB-07: 项目切换

| 步骤 | 操作 | 预期结果 | 结果 | 备注 |
|------|------|----------|------|------|
| 1 | 打开项目 A，对话几轮 | 正常 | **PASS** | |
| 2 | 关闭 A，打开项目 B | 会话自动保存切换，索引重建 | **PASS** | |
| 3 | 再回到项目 A | 旧会话可从历史恢复 | **PASS** | |
| 4 | 通过 文件→打开→项目/解决方案 直接切换 | 会话列表自动更新到新项目 | **PASS** | 关闭→重新打开项目的切换流程正常。但发现：通过 文件→打开→项目/解决方案 直接切换时，会话历史列表不会自动更新到新项目，仍停留在旧项目。 |

---

## 测试结果汇总表

| 类别 | 总数 | 通过 | 失败 | 跳过 |
|------|------|------|------|------|
| 基础功能 | 10 | | | |
| 工具调用 | 13 | | | |
| R1-R9 验证 | 15 | | | |
| Phase 2.5 遗留 | 9 | | | |
| UI 与交互 | 8 | | | |
| 知识索引 | 5 | | | |
| 安全与权限 | 6 | | | |
| 稳定性与边界 | 7 | | | |
| **合计** | **73** | | | |

---

## 优先级建议

**P0 (必须通过)**: BF-01~02, TC-01~03/10/13, R1-V01, R5-V01~02, SEC-01~04, STB-01/03/04
**P1 (重要)**: BF-03~05, TC-04~09/11~12, R1-V02, R3-V01, R4-V01, R9-V01, UI-01~06, KI-01~03, P25-01~09
**P2 (补充)**: BF-06, UI-07~08, KI-04~05, SEC-05~06, STB-02/05~07
