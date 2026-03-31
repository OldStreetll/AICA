# AICA vs OpenCode 对比分析报告

> 日期: 2026-03-30 | 更新: 2026-03-31（v2.1 执行成果标注）
> 分析基线: AICA v2.0（commit `686c9de`）→ **v2.1（commit `58ab099`）已完成部分差距填补**
> 分析方法: Agent 并行探索两个项目源码，逐维度深度对比
> 用途: 识别 AICA 的不足与改进方向，跟踪差距填补进展

---

## 一、项目概况对比

| 维度 | AICA | OpenCode |
|------|------|----------|
| 定位 | VS2022 内嵌 C/C++ AI 编码助手 | 开源通用 AI 编码 Agent |
| 语言 | C# (.NET) | TypeScript (Bun) |
| 代码规模 | ~5000 行（Core + VSIX） | ~37000 行（1325 个 TS/TSX 文件） |
| 前端 | VS2022 WPF 窗口 | TUI + Web + Desktop (Tauri) 三端 |
| LLM | MiniMax-M2.5（私有部署，177K token） | 20+ Provider（Claude/GPT/Gemini/Bedrock...） |
| 架构 | 单 Agent + 信任型循环 | 多 Agent（Build/Plan/General） |
| 工具数 | ~~14~~ → **16+**（10 原生 + 6+ GitNexus MCP 动态注册）| 15+（bash/edit/glob/grep/patch/batch/codesearch...） |
| 持久化 | 文件存储 | SQLite + Drizzle ORM |
| 扩展性 | MCP 桥接（**v2.1: 动态注册所有 MCP 工具**） | 原生插件系统 + MCP |

---

## 二、工具系统对比

### 2.1 文件编辑

#### AICA EditFileTool（~~518~~ → **635 行**，v2.1 增强）

**编辑机制**: old_string/new_string 字符串替换，~~支持 `full_replace`~~ **v2.1 O1 已移除 full_replace，新建文件用 write_file**。

**优势**:
| 优势 | 说明 |
|------|------|
| 交互式确认 | 编辑前展示 diff，等待用户批准，用户可手动修改后再应用 |
| H3 诊断路由 | old_string 匹配失败时，自动尝试修复缩进/空白差异（零交互 auto-fix） |
| **v2.1 T5: FindWithCascade 3 级模糊匹配** | 精确→行尾空白→缩进无关→空白压缩，从 H3 提取统一入口 |
| 会话感知 | 跟踪 `EditedFilesInSession`，检测到文件已被编辑过时提示用户重新 read |
| **v2.1 T6: 文件时间戳冲突检测** | FileTimeTracker 记录 mtime+size，编辑前检测外部修改 |
| CRLF 保持 | 检测原始换行符，编辑后还原（D-09 修复） |
| 用户修改检测 | 检测用户是否在 diff 对话框中手动改了内容 |
| **v2.1 O2-O7: 工具描述负边界 + 互相引导** | 每个工具声明"不做什么"，引导 LLM 选对工具 |

**劣势（剩余差距）**:
| 劣势 | OpenCode 如何做 | 状态 |
|------|----------------|------|
| 3 级模糊匹配（vs OpenCode 9 级） | Levenshtein/锚点/转义/上下文感知等 6 级 | v2.2 观察匹配分布后评估 |
| 无文件锁 | `FileTime.withLock()` 信号量序列化 | 未计划 |
| ~~无时间戳冲突检测~~ | ~~记录 mtime/ctime/size~~ | **✅ v2.1 T6 已解决** |
| 无语义验证 | 编辑后自动调用 LSP 检测语法错误 | v2.2+ 评估 |
| ~~无 Write 工具~~ | ~~独立 write 工具~~ | **✅ v2.1 T2 已解决** |
| 无 Patch 工具 | `apply_patch` 多文件原子操作 | v2.2 评估 |
| 无 Batch/MultiEdit 工具 | 并行操作 / 单文件多处编辑 | v2.2 评估 |
| 无自动格式化 | 编辑后自动运行格式化器 | 未计划 |

#### OpenCode edit.ts

**编辑机制**: 9 级级联替换策略（SimpleReplacer → LineTrimmedReplacer → BlockAnchorReplacer → WhitespaceNormalizedReplacer → IndentationFlexibleReplacer → EscapeNormalizedReplacer → TrimmedBoundaryReplacer → ContextAwareReplacer → MultiOccurrenceReplacer）

**优势**: 极高容错率，几乎任何格式偏差都能匹配。

**劣势**: 无交互式确认（直接应用）；模糊匹配可能误命中；用户不知道用了哪一级策略。

#### OpenCode apply_patch.ts

**编辑机制**: 统一 patch 格式，支持 add/update/delete/move 四种操作，多文件原子性。

**优势**: 多文件批量操作、4 级上下文匹配（exact → rstrip → trim → normalized Unicode）、事务语义。

**劣势**: 无冲突检测；patch 格式有特定标记要求。

#### 关键判断

AICA 在**用户交互体验**上更好（确认、诊断、手动修改检测），OpenCode 在**工程健壮性**上更强（模糊匹配、冲突检测、语义验证、多文件操作）。

---

### 2.2 代码搜索（**v2.1 T4 已大幅改善**）

| 维度 | AICA GrepSearchTool | OpenCode grep | v2.1 状态 |
|------|---------------------|---------------|-----------|
| 实现 | ~~纯 C#~~ **双路策略：>=200 文件用 ripgrep，<200 用 C#** | 调用 ripgrep 进程 | **✅ 已对齐** |
| 性能 | ~~4000 文件 ~3-5s~~ **大项目 ~0.2-0.5s** | 4000 文件 ~0.1-0.3s | **✅ 差距缩小到 2x 内** |
| 文件大小限制 | ~~5MB~~ **50MB（rg 路径）** | 无限制（rg 默认 50MB） | **✅ 已对齐** |
| 加速 | **ripgrep SIMD + 内存映射** | SIMD + 内存映射 | **✅ 已对齐** |
| 输出 | **rg --json 结构化输出** | `--json` 结构化输出 | **✅ 已对齐** |
| 排除目录 | C# 硬编码排除列表 | .gitignore + rg 内置规则 | 剩余差距 |
| 编码处理 | **UTF-8（rg 路径）** | 自动检测 UTF-8/UTF-16 | 基本对齐 |

**差距**: ~~性能差 10-50x~~ → 大项目基本对齐，小项目 C# 路径无 .gitignore 支持。

---

### 2.3 文件发现（**v2.1 T3 已解决**）

| 维度 | AICA | OpenCode | v2.1 状态 |
|------|------|----------|-----------|
| 按名称查找 | ~~无专用工具~~ **glob 工具，自实现 glob-to-regex** | `glob` 工具 | **✅ 已对齐** |
| 语义搜索 | 无（GitNexus MCP 覆盖部分语义搜索） | `codesearch` 语义代码搜索 | GitNexus 替代 |
| 效率 | ~~多次 list_dir 递归~~ **一次 glob 调用** | 一次 glob 调用 | **✅ 已对齐** |

---

### 2.4 Shell 执行

| 维度 | AICA RunCommandTool | OpenCode bash |
|------|---------------------|---------------|
| 实现 | `Process.Start` 简单进程调用 | PTY 伪终端 + 流式输出 |
| 交互性 | 无（等待进程结束后返回全部输出） | 支持交互式命令 |
| 信号处理 | 无 | Windows 信号处理支持 |
| 流式输出 | 无（一次性返回） | 实时流式返回 |

> 注：AICA 暂不考虑 Shell 增强。

---

### 2.5 工具系统总结（v2.1 更新）

| 工具类别 | AICA 现有 | OpenCode 对应 | 差距 | v2.1 状态 |
|----------|----------|--------------|------|-----------|
| 文件读取 | read_file（+FileTimeTracker） | read | 基本对等 | **✅ +冲突检测** |
| 文件编辑 | edit（FindWithCascade 3 级） | edit + apply_patch + batch | 缺 patch/batch | **↑ 模糊匹配改善** |
| 文件写入 | **write_file** | write | ~~语义不清晰~~ | **✅ 已对齐** |
| 文件发现 | **glob** + list_dir | glob | ~~缺 glob~~ | **✅ 已对齐** |
| 代码搜索 | grep_search（**ripgrep 双路**） | grep (ripgrep) + codesearch | ~~性能差距大~~ | **✅ 基本对齐** |
| Shell | run_command | bash (PTY) | 无交互/流式 | 暂不改进 |
| 计划管理 | update_plan | (内置于 Agent) | 基本对等 | |
| 交互问答 | ask_followup_question | (内置于 Agent) | 基本对等 | |
| MCP 桥接 | McpBridgeTool（**动态注册**） | MCP 原生集成 | ~~静默丢弃~~ | **✅ 自动注册** |
| **工具名容错** | **O11 别名映射** | 原生命名一致 | — | **✅ 新增** |
| **工具描述** | **负边界 + 互相引导** | 负边界 + 互相引导 | ~~无引导~~ | **✅ 已对齐** |

---

## 三、多模型/多 Provider 支持

| 维度 | AICA | OpenCode |
|------|------|----------|
| LLM Provider 数 | 1（OpenAI-compatible） | 20+（Claude/GPT/Gemini/Bedrock/Groq/Mistral...） |
| 模型路由 | 无（固定 MiniMax-M2.5） | 按 Agent 类型选不同模型 |
| Provider SDK | 自写 OpenAIClient (~300 行) | Vercel AI SDK 统一抽象层 |
| 模型元数据 | 无 | models.dev 动态获取（context window/cost/token） |
| 消息规范化 | 无 | ProviderTransform 处理各 provider 怪癖 |

> 注：AICA 暂不考虑多模型接入。

---

## 四、Provider SDK 对比

### AICA OpenAIClient

**优势**:
| 优势 | 说明 |
|------|------|
| 流式恢复健壮 | 针对 MiniMax P1-017 修复：流中断后 fallback 恢复，部分 tool call 也能发出 |
| 连接错误精细分类 | 区分 IOException/SocketException 瞬态错误 vs 永久错误 |
| MCP Raw Schema 透传 | `RawParametersJson` 100% 保留 MCP 原始 schema，不做有损转换 |
| 简洁可控 | 单文件，行为完全透明，无隐藏抽象层 |
| 中文错误识别 | context overflow 检测包含"上下文长度""令牌限制"等中文模式 |

**劣势**:
| 劣势 | OpenCode 如何做 |
|------|----------------|
| 无结构化错误分类 | 区分 `context_overflow` / `api_error` / `retryable` |
| tool_choice 硬编码 "auto" | 支持动态 tool_choice 映射 |
| 无 token 元数据 | 从 models.dev 获取每个模型的 context window、cost/token |
| 无消息规范化 | `ProviderTransform` 处理空内容过滤、ID 格式化、消息排序修复 |
| 无 HTML 网关错误检测 | 检测 401/403 HTML 响应（反向代理/网关返回的非 JSON 错误） |

### 关键判断

AICA 在**单模型场景的实战健壮性**上很好（流恢复、MiniMax 特殊处理）。劣势主要体现在多模型扩展性上。

---

## 五、多 Agent 架构

| 维度 | AICA | OpenCode |
|------|------|----------|
| Agent 类型 | 单一 AgentExecutor | Build(全权限) / Plan(只读) / General(复杂) |
| 权限隔离 | 统一权限，无隔离 | 每个 Agent 独立权限规则 |
| Agent 发现 | 硬编码 | 可配置、可扩展 |
| 工具集隔离 | 所有工具共享一个 ToolDispatcher | 每个 Agent 独立工具集 |

### 多 Agent 可行性评估

经源码分析，AICA 当前架构**可以支持多 Agent**，改动量不大：

| 组件 | 现状 | 多 Agent 就绪？ |
|------|------|----------------|
| AgentExecutor | 非单例，每次 new 一个实例 | 天然支持多实例 |
| SystemPromptBuilder | Fluent builder，可动态组装 | 不同 role 传不同 prompt |
| ToolDispatcher | 实例级，但当前 UI 共享一个 | 需创建多个（Plan 只注册只读工具） |
| ILLMClient | 依赖注入，可共享 | 多 Agent 共享同一端点 |
| previousMessages | ExecuteAsync 接受外部传入 | Plan 输出可作为 Build 输入 |

**Plan-first 不需要多模型** — 同一模型 + 不同 prompt + 不同工具集 = 不同行为。

> 注：AICA 暂不添加新 Agent 类型，但架构已具备扩展基础。

---

## 六、会话与持久化

| 维度 | AICA | OpenCode |
|------|------|----------|
| 存储引擎 | 简单文件存储（ConversationStorage） | SQLite + Drizzle ORM |
| 会话管理 | 单会话，重启丢失 | 多会话并行、附加、导入/导出 |
| 快照 | 无 | Session snapshots，可回滚 |
| 消息格式 | 自定义 ChatMessage | Message V2 标准化 |
| 会话标题 | 无 | 自动/手动标题 |
| 历史检索 | 不可检索 | 按标题/时间/内容搜索 |

**改进方向**: SQLite 持久化 → 多会话管理 → 会话标题自动生成。

---

## 七、LSP 与 IDE 能力

| 维度 | AICA | OpenCode |
|------|------|----------|
| LSP 集成 | 无 | 完整 LSP server（66KB），跳转/补全/诊断 |
| 代码解析 | 正则 SymbolParser | Tree-sitter 语法树解析 |
| 代码智能 | 仅文本匹配 | 语义级理解 |
| 符号提取 | 类/结构体/枚举/函数/命名空间/typedef | 完整语法树 |
| C++ 复杂语法 | 模板/宏/嵌套命名空间处理不好 | Tree-sitter 完整支持 |

**AICA SymbolParser 已知缺陷**:
- `template<class T>` 模板参数解析不完整
- `#define` 宏展开无法跟踪
- `namespace a::b::c` 嵌套命名空间匹配失败
- 条件编译 `#ifdef` 内的符号可能遗漏

**改进方向**: 可借助 VS2022 内置的 LSP 能力（Roslyn for C#, vcpkg/clangd for C++），无需自建 LSP server。

---

## 八、权限系统

| 维度 | AICA | OpenCode |
|------|------|----------|
| 粒度 | 白名单/黑名单二元 | glob 模式 + action 分类(read/write/execute) + 三级(allow/ask/deny) |
| 动态检查 | 启动时静态配置 | 运行时按规则引擎评估 |
| 每 Agent 独立 | 否 | 是 |
| 路径安全 | SafetyGuard（受保护路径 + 命令黑名单） | assertExternalDirectory + 权限规则 |
| .ignore 支持 | .aicaignore | .gitignore + 自定义 |

**改进方向**: glob 模式 + action 分类 + 三级控制。

---

## 九、UI/UX

| 维度 | AICA | OpenCode |
|------|------|----------|
| 前端 | 仅 VS2022 WPF 窗口 | TUI + Web + Desktop (Tauri) 三端 |
| Markdown 渲染 | Markdig 基础渲染 | Shiki 语法高亮 + Pierre diff 可视化 |
| Diff 展示 | 简单 DiffEditorDialog | 专用 diff 可视化组件 |
| 主题 | 无 | 暗/亮主题系统 |
| 键绑定 | 无自定义 | 完整键绑定系统 |
| 终端集成 | 无 | ghostty-web 终端模拟器 |

> 注：UI/UX 改进方向待后续讨论。

---

## 十、AICA 自身存在的问题（v2.1 更新）

| # | 问题 | 严重度 | 状态 |
|---|------|--------|------|
| 1 | ~~list_dir 对 `path: .` 的 I/O 错误~~ | ~~中~~ | **✅ v2.1 确认非 bug**（IsPathAccessible 已正确处理） |
| 2 | ~~测试项目编译报错（引用已删除的工具）~~ | ~~中~~ | **✅ v2.1 T1.2 清理完成** |
| 3 | D-03 流式输出被覆盖 | 中 | **✅ v2.1 测试通过** |
| 4 | D-08 长会话上下文混淆 | 中 | **✅ v2.1 测试通过** |
| 5 | ~~GitNexus 工具选择率~~ | ~~低~~ | **✅ v2.0 C88 零偏见 + v2.1 O10 cypher 精简** |
| 6 | ~~创建新文件靠 edit full_replace hack~~ | ~~中~~ | **✅ v2.1 T2 WriteFileTool + O1 移除 full_replace** |
| 7 | ~~grep_search 大项目性能差~~ | ~~高~~ | **✅ v2.1 T4 ripgrep 双路策略** |
| 8 | 无 Git/GitHub 集成 | 低 | 后续评估 |
| 9 | ~~无插件系统（扩展完全依赖 MCP）~~ | ~~低~~ | **↑ v2.1 MCP 动态注册改善扩展性** |
| 10 | 无会话标题（用户难以找到历史对话） | 低 | 后续评估 |

### v2.1 新发现的问题

| # | 问题 | 严重度 | 状态 |
|---|------|--------|------|
| 11 | LLM 工具名幻觉（调用 grep/bash 而非 grep_search/run_command） | 高 | **✅ v2.1 O11 别名映射** |
| 12 | read_file 分块读取被误判 Duplicate call | 高 | **✅ v2.1 O12 签名修复** |
| 13 | LLM 幻觉 gitnexus_list_repos（MCP Server 暴露但被硬编码 spec 过滤） | 中 | **✅ MCP 动态注册解决** |
| 14 | RunCommandTool 拦截消息引用不存在的 find_by_name | 低 | **✅ v2.1 F3 改为 glob** |
| 15 | gitnexus_cypher 描述 2762 chars 占 38% 工具描述 token | 中 | **✅ v2.1 O10 精简到 ~600 chars** |

---

## 十一、优先级建议（v2.1 更新）

### 第一梯队 — ✅ 全部完成（v2.1，commit `58ab099`）

| 改进项 | 状态 | 实际工作量 |
|--------|------|-----------|
| 修复残留 bug | ✅ T1 确认非 bug + 测试清理 | 0.5 天 |
| 新增 Write 工具 | ✅ T2 WriteFileTool 222 行 | 0.5 天 |
| 新增 Glob 工具 | ✅ T3 GlobTool 337 行 | 0.5 天 |
| grep_search 改用 ripgrep | ✅ T4 双路策略 +268 行 | 1 天 |
| Edit 重构 H3 + 新增行尾空白匹配 | ✅ T5 FindWithCascade | 0.5 天 |
| 文件时间戳冲突检测 | ✅ T6 FileTimeTracker 119 行 | 0.3 天 |
| 工具名别名映射 | ✅ O11 ToolDispatcher +57 行 | 0.2 天 |
| read_file 去重签名修复 | ✅ O12 删 1 行 | 0.1 天 |
| 工具描述负边界 + 互相引导 | ✅ O2-O7, O9 | 0.3 天 |
| edit 移除 full_replace | ✅ O1 参数删除 + 容错引导 | 0.3 天 |
| gitnexus_cypher 描述精简 | ✅ O10 ~2762→~600 chars | 0.2 天 |
| MCP 动态工具注册 | ✅ BuildToolsFromNativeDefinitions 重写 | 0.2 天 |

### 第二梯队 — v2.2 评估（中等价值中等成本）

| 改进项 | 理由 |
|--------|------|
| Plan Agent 分离 | 减少 doom loop，提升复杂任务成功率。架构已具备（AgentExecutor 可多实例） |
| 会话持久化（SQLite） | 历史会话检索、恢复 |
| 权限系统增强 | glob + action 分类 + 三级控制 |
| LLM 结构化错误分类 | 上层 AgentExecutor 可做差异化处理 |
| list_projects 降级为 prompt 注入 | 改动量 ~200+ 行，推迟到 v2.2（O8） |
| multiedit / apply_patch | 单文件多处编辑 / 多文件原子编辑 |

### 第三梯队 — 长期路线图（高价值高成本）

| 改进项 | 理由 |
|--------|------|
| LSP 语义验证 | 编辑后自动检测语法错误（借助 VS2022 内置 LSP） |
| Tree-sitter 代码解析 | 替代正则 SymbolParser |
| 多模型适配层 | 解绑 MiniMax，支持模型切换 |
| Diff 可视化增强 | 提升代码审查体验 |
