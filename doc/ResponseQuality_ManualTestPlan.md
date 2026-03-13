# AICA 响应质量优化 — 人工测试方案

> 版本: 1.0
> 日期: 2026-03-13
> 涉及改动: `ResponseQualityFilter.cs`, `SystemPromptBuilder.cs`, `AgentExecutor.cs`, `AttemptCompletionTool.cs`

---

## 一、测试环境准备

1. 使用 `build.ps1` 编译 AICA 解决方案，确认 0 error
2. 在 Visual Studio 2022 中加载 AICA VSIX 扩展（调试模式 F5 启动实验实例）
3. 打开 AICA 聊天窗口
4. 打开 VS 的 **Output → Debug** 窗口，筛选 `[AICA]` 前缀日志，用于观察后处理行为

---

## 二、测试用例

### TC-01: 禁止开头短语过滤（中文）

| 项目 | 内容 |
|------|------|
| **目标** | 验证 LLM 回复不以 "好的"、"当然"、"没问题" 等开头 |
| **输入** | `你好` |
| **预期** | 回复直接给出内容，不以 "好的，"、"当然，"、"没问题，" 开头。如果 LLM 原始输出包含这些短语，Debug 日志中不会有特别提示（过滤是静默的），但用户看到的文本不应包含这些开头 |
| **验证方法** | 观察聊天窗口中的回复首句 |
| **通过标准** | 回复首字不是 "好的"/"当然"/"没问题"/"当然可以" |

### TC-02: 禁止开头短语过滤（英文）

| 项目 | 内容 |
|------|------|
| **目标** | 验证英文回复不以 "Great,", "Certainly,", "Sure," 等开头 |
| **输入** | `Hello, can you help me?` |
| **预期** | 回复直接给出内容，不以 "Great,", "Certainly,", "Sure,", "Of course," 等开头 |
| **验证方法** | 观察聊天窗口中的回复首句 |
| **通过标准** | 回复首词不是 Great/Certainly/Sure/Okay/Of course/Absolutely/No problem |

### TC-03: 禁止结尾反问过滤（中文）

| 项目 | 内容 |
|------|------|
| **目标** | 验证回复不以 "还需要我..."、"需要其他帮助吗" 等结尾 |
| **输入** | `读取 README.md` |
| **预期** | 回复在展示文件内容后直接调用 `attempt_completion`，不追加 "还需要我做其他的吗？" 或 "需要其他帮助吗？" |
| **验证方法** | 观察回复最后一句 |
| **通过标准** | 回复末尾不包含反问句或主动提供帮助的句式 |

### TC-04: 禁止结尾反问过滤（英文）

| 项目 | 内容 |
|------|------|
| **目标** | 验证英文回复不以 "Do you want me to...", "Let me know if..." 等结尾 |
| **输入** | `Read the file AICA.Core.csproj` |
| **预期** | 回复展示文件内容后直接完成，不追加 "Would you like me to..." 或 "Let me know if you need anything else" |
| **验证方法** | 观察回复最后一句 |
| **通过标准** | 回复末尾不包含 offer 句式 |

### TC-05: 思维链标签提取（`<thinking>` 不泄漏）

| 项目 | 内容 |
|------|------|
| **目标** | 验证 `<thinking>` 标签内容不展示给用户 |
| **输入** | `列出当前项目的结构` |
| **预期** | 用户看到的回复中不包含 `<thinking>` 或 `</thinking>` 标签，也不包含明显的内部推理文本（如 "我需要先调用 list_dir"） |
| **验证方法** | 1. 观察聊天窗口，搜索 `<thinking>` 关键字 2. 检查 Debug 日志是否有 `[AICA] Extracted thinking (N chars)` |
| **通过标准** | 用户可见文本中无 thinking 标签；如果 LLM 使用了 thinking，Debug 日志应有提取记录 |

### TC-06: 内部推理抑制（工具调用前的叙述）

| 项目 | 内容 |
|------|------|
| **目标** | 验证工具调用前的 "我将调用..."、"Let me check..." 等叙述被抑制 |
| **输入** | `搜索项目中所有包含 TODO 的文件` |
| **预期** | AICA 直接调用 `grep_search` 工具，不先输出 "我将调用 grep_search 来搜索..." 或 "让我搜索一下..." |
| **验证方法** | 1. 观察工具调用前是否有叙述文本 2. 检查 Debug 日志是否有 `[AICA] Suppressing pre-tool text` 或 `[AICA] Suppressing meta-reasoning text` |
| **通过标准** | 工具调用前无叙述性文本，或 Debug 日志显示已抑制 |

### TC-07: 内部推理抑制（attempt_completion 前）

| 项目 | 内容 |
|------|------|
| **目标** | 验证 `attempt_completion` 调用前的所有文本被抑制 |
| **输入** | `创建一个名为 test_hello.txt 的文件，内容为 Hello World` |
| **预期** | 创建文件后，AICA 调用 `attempt_completion`，在 completion 卡片之前不应有额外的总结文本 |
| **验证方法** | 观察 completion 卡片出现前是否有多余文本 |
| **通过标准** | completion 卡片前无 "我已经创建了文件..." 等重复总结 |

### TC-08: 无重复信息

| 项目 | 内容 |
|------|------|
| **目标** | 验证回复不重复已展示的信息 |
| **输入** | 连续两步操作：先 `读取 AICA.Core.csproj`，然后 `这个项目用了什么框架？` |
| **预期** | 第二次回复直接基于已读取的内容回答，不重新输出文件全文 |
| **验证方法** | 观察第二次回复是否包含完整文件内容的重复 |
| **通过标准** | 第二次回复简洁，仅引用关键信息，不重复全文 |

### TC-09: 简洁的 attempt_completion 结果

| 项目 | 内容 |
|------|------|
| **目标** | 验证 completion 摘要为 1-5 句话，聚焦结果而非过程 |
| **输入** | `在 src/AICA.Core/Prompt/ 目录下搜索所有包含 "CRITICAL" 的文件` |
| **预期** | completion 卡片中的摘要简洁（1-5 句），说明找到了什么，不描述 "我先调用了 grep_search，然后分析了结果..." |
| **验证方法** | 观察 completion 卡片内容 |
| **通过标准** | 摘要 ≤ 5 句，聚焦结果（找到 N 个文件/N 处匹配），不描述过程 |

### TC-10: 知识问答直接输出（无工具叙述）

| 项目 | 内容 |
|------|------|
| **目标** | 验证纯知识问答直接给出技术内容，无多余开头/结尾 |
| **输入** | `解释 SOLID 原则` |
| **预期** | 直接输出 SOLID 五个原则的技术解释，使用 Markdown 格式，无 "好的，我来解释" 开头，无 "还需要了解其他内容吗？" 结尾 |
| **验证方法** | 观察回复的开头和结尾 |
| **通过标准** | 开头直接进入技术内容；结尾是技术内容的自然结束，无反问 |

### TC-11: 微压缩（多轮对话上下文管理）

| 项目 | 内容 |
|------|------|
| **目标** | 验证多轮对话中旧的工具结果被压缩，不导致上下文溢出 |
| **输入** | 连续执行 6+ 次文件读取操作（每次读取不同文件） |
| **预期** | 1. 不出现上下文溢出错误 2. Debug 日志中可观察到微压缩行为 3. 后续回复仍然准确 |
| **验证方法** | 1. 连续发送：`读取 file1`, `读取 file2`, ... `读取 file6` 2. 最后问 `刚才读取的第一个文件是什么？` 3. 检查 Debug 日志中 `[AICA] Agent iteration` 附近是否有微压缩相关日志 |
| **通过标准** | 不崩溃，不报上下文溢出，后续回复基本准确 |

### TC-12: 系统提示中的结构化思考规则

| 项目 | 内容 |
|------|------|
| **目标** | 验证系统提示包含 Structured Thinking 和 Response Quality 规则 |
| **输入** | 在 Debug 模式下，断点或日志捕获 `SystemPromptBuilder.Build()` 的输出 |
| **预期** | 系统提示中包含以下 section：`### Structured Thinking`、`### Response Quality (CRITICAL)` |
| **验证方法** | 在 `AgentExecutor.ExecuteAsync` 的 `var systemPrompt = builder.Build();` 处设断点，检查 systemPrompt 内容 |
| **通过标准** | 包含 "Structured Thinking" section 和 "Response Quality (CRITICAL)" section，且 "FORBIDDEN openers" 规则存在 |

### TC-13: 中英文语言自适应

| 项目 | 内容 |
|------|------|
| **目标** | 验证回复语言与用户输入语言一致 |
| **输入** | 先发 `列出项目文件`，再发 `List project files` |
| **预期** | 第一次回复为中文，第二次回复为英文 |
| **验证方法** | 观察两次回复的语言 |
| **通过标准** | 语言与输入一致 |

### TC-14: 代码块中的推理模式不被误判

| 项目 | 内容 |
|------|------|
| **目标** | 验证代码块中包含 "I need to" 等模式时不被错误抑制 |
| **输入** | `解释这段代码的作用：\`\`\`csharp\n// I need to initialize the variable\nvar x = 10;\n\`\`\`` |
| **预期** | 回复正常展示，代码块内容不被抑制 |
| **验证方法** | 观察回复是否完整包含代码解释 |
| **通过标准** | 回复完整，代码块内容未被过滤 |

---

## 三、回归测试

以下场景确保原有功能未被破坏：

| 编号 | 场景 | 输入 | 预期 |
|------|------|------|------|
| RT-01 | 文件读取 | `读取 src/AICA.Core/AICA.Core.csproj` | 正常返回文件内容 |
| RT-02 | 文件编辑 | 对一个文件执行 edit 操作 | diff 预览正常，确认后文件被修改 |
| RT-03 | 目录列表 | `列出 src 目录结构` | 正常返回目录树 |
| RT-04 | 搜索 | `搜索 AgentExecutor 类的定义` | grep_search 正常返回结果 |
| RT-05 | 冲突检测 | 要求修改一个已经符合要求的文件 | 触发 ask_followup_question |
| RT-06 | 幻觉检测 | 观察是否有 LLM 声称执行了工具但实际未调用的情况 | 如有，应触发纠正消息 |
| RT-07 | 上下文压缩 | 长对话（10+ 轮）后继续操作 | 不崩溃，condense 机制正常触发 |
| RT-08 | 任务完成 | 任何文件操作后 | 必须调用 attempt_completion |

---

## 四、Debug 日志关键字速查

在 VS Output → Debug 窗口中搜索以下关键字来验证各机制是否生效：

| 关键字 | 含义 |
|--------|------|
| `[AICA] Extracted thinking` | thinking 标签被提取，内容未泄漏给用户 |
| `[AICA] Suppressing pre-tool text` | 工具调用前的叙述文本被抑制 |
| `[AICA] Suppressing meta-reasoning text` | 元推理文本被抑制 |
| `[AICA] Agent iteration` | Agent 循环迭代（微压缩在每次迭代开始时执行） |
| `[AICA] Token usage at` | 上下文压力检测 |
| `[AICA] Auto-condense complete` | 自动压缩完成 |
| `[AICA] Skipping duplicate tool call` | 重复工具调用被跳过 |
| `[AICA] Detected tool execution hallucination` | 幻觉检测触发 |

---

## 五、测试结果记录模板

| 用例 | 通过/失败 | 实际行为 | 备注 |
|------|-----------|----------|------|
| TC-01 | | | |
| TC-02 | | | |
| TC-03 | | | |
| TC-04 | | | |
| TC-05 | | | |
| TC-06 | | | |
| TC-07 | | | |
| TC-08 | | | |
| TC-09 | | | |
| TC-10 | | | |
| TC-11 | | | |
| TC-12 | | | |
| TC-13 | | | |
| TC-14 | | | |
| RT-01 | | | |
| RT-02 | | | |
| RT-03 | | | |
| RT-04 | | | |
| RT-05 | | | |
| RT-06 | | | |
| RT-07 | | | |
| RT-08 | | | |

---

## 六、已知限制

1. **过滤是尽力而为** — 如果 LLM 原始输出不包含禁止短语，过滤器不会改变任何内容。过滤器只在 LLM 输出包含这些模式时才生效。
2. **thinking 标签依赖 LLM 遵守** — 系统提示鼓励使用 `<thinking>` 标签，但不能强制。如果 LLM 不使用 thinking 标签而直接输出推理内容，`ExtractThinking` 不会生效，但 `IsInternalReasoning` 仍会尝试检测并抑制。
3. **微压缩阈值** — 短于 12 字符的工具结果不会被压缩（保留错误消息等短结果的可读性）。
4. **2 个预存在的测试失败** — `EditFileTool_WithValidEdit_ShowsDiff` 和 `TimeoutMiddleware_EnforcesTimeout` 是本次改动之前就存在的失败，与响应质量优化无关。
