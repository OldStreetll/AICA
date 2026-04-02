# AICA vs OpenCode 对比分析报告

> 日期: 2026-03-30 | 更新: 2026-04-01（v2.5 全部 4 项优化实施完成 + E2E 验证通过）
> 分析基线: AICA v2.0（commit `686c9de`）→ v2.1（`58ab099`）→ v2.2（`de521e7`）→ v2.3+v2.4（`7524761`）→ **v2.5 优化完成**
> 分析方法: Agent 并行探索两个项目源码，逐维度深度对比
> 用途: 识别 AICA 的不足与改进方向，跟踪差距填补进展
>
> **进展摘要**: 第一/二梯队已全部完成（13/13 项 ✅），第三梯队剩余 4 项长期任务。v2.5 全部 4 项优化已实施并通过 E2E 验证（第十五章）。

---

## 一、项目概况对比

| 维度 | AICA | OpenCode |
|------|------|----------|
| 定位 | VS2022 内嵌 C/C++ AI 编码助手 | 开源通用 AI 编码 Agent |
| 语言 | C# (.NET) | TypeScript (Bun) |
| 代码规模 | ~5000 行（Core + VSIX） | ~37000 行（1325 个 TS/TSX 文件） |
| 前端 | VS2022 WPF 窗口 | TUI + Web + Desktop (Tauri) 三端 |
| LLM | MiniMax-M2.5（私有部署，177K token） | 20+ Provider（Claude/GPT/Gemini/Bedrock...） |
| 架构 | 单 Agent + PlanAgent 预规划 + 信任型循环 | 多 Agent（Build/Plan/General） |
| 工具数 | **16**（11 原生 + 6+ GitNexus MCP 动态注册）| 15+（bash/edit/glob/grep/patch/batch/codesearch...） |
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
| 无语义验证 | 编辑后自动调用 LSP 检测语法错误 | **✅ v2.3 POC 已实现** |
| ~~无 Write 工具~~ | ~~独立 write 工具~~ | **✅ v2.1 T2 已解决** |
| ~~无 Patch 工具~~ | ~~`apply_patch` 多文件原子操作~~ | **v2.3 统一编辑增强（合并到 edit）** |
| ~~无 Batch/MultiEdit 工具~~ | ~~并行操作 / 单文件多处编辑~~ | **v2.3 统一编辑增强（合并到 edit）** |
| 无自动格式化 | 编辑后自动运行格式化器 | 未计划 |

#### OpenCode edit.ts

**编辑机制**: 9 级级联替换策略（SimpleReplacer → LineTrimmedReplacer → BlockAnchorReplacer → WhitespaceNormalizedReplacer → IndentationFlexibleReplacer → EscapeNormalizedReplacer → TrimmedBoundaryReplacer → ContextAwareReplacer → MultiOccurrenceReplacer）

**优势**: 极高容错率，几乎任何格式偏差都能匹配。

**劣势**: 无交互式确认（直接应用）；模糊匹配可能误命中；用户不知道用了哪一级策略。

#### OpenCode apply_patch.ts

**编辑机制**: 统一 patch 格式，支持 add/update/delete/move 四种操作，多文件原子性。

**优势**: 多文件批量操作、4 级上下文匹配（exact → rstrip → trim → normalized Unicode）、事务语义。

**劣势**: 无冲突检测；patch 格式有特定标记要求。

#### 关键判断（v2.3 更新）

AICA 在**用户交互体验**上更好（确认、诊断、手动修改检测），且 v2.3 已补齐多文件编辑 + LSP 语义验证。OpenCode 在**模糊匹配深度**上仍更强（9 级 vs 3 级）。整体差距已大幅缩小。

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

### 2.5 工具系统总结（v2.3 更新）

| 工具类别 | AICA 现有 | OpenCode 对应 | 差距 | 最新状态 |
|----------|----------|--------------|------|-----------|
| 文件读取 | read_file（+FileTimeTracker） | read | 基本对等 | **✅ +冲突检测** |
| 文件编辑 | edit（3 级匹配 + 三模式: 单/edits/files） | edit + apply_patch + batch | ~~缺 patch/batch~~ | **✅ v2.3 统一编辑增强** |
| 文件写入 | write_file | write | 已对齐 | **✅** |
| 文件发现 | glob + list_dir | glob | 已对齐 | **✅** |
| 代码搜索 | grep_search（ripgrep 双路） | grep (ripgrep) + codesearch | 基本对齐 | **✅** |
| **语义验证** | **validate_file**（VS2022 Error List 轮询） | （无独立工具） | **AICA 领先** | **✅ v2.3 新增** |
| Shell | run_command | bash (PTY) | 无交互/流式 | 暂不改进 |
| 计划管理 | ~~update_plan~~ → PlanAgent 预规划 | (内置于 Agent) | 已对齐 | **✅ v2.2** |
| 交互问答 | ask_followup_question | (内置于 Agent) | 基本对等 | |
| MCP 桥接 | McpBridgeTool（动态注册） | MCP 原生集成 | 已对齐 | **✅** |
| 工具名容错 | O11 别名映射 | 原生命名一致 | — | **✅** |
| 工具描述 | 负边界 + 互相引导 | 负边界 + 互相引导 | 已对齐 | **✅** |

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
| ~~无结构化错误分类~~ | ~~区分 context_overflow / api_error / retryable~~ | **✅ v2.3 LLMErrorKind 7 种分类 + Classify()** |
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
| Agent 类型 | AgentExecutor + **PlanAgent（v2.2 只读工具 mini loop）** | Build(全权限) / Plan(只读) / General(复杂) |
| 权限隔离 | **v2.3: PermissionRuleEngine（glob+action+三级控制）** | 每个 Agent 独立权限规则 |
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

> v2.2 已实现 PlanAgent（只读工具 mini loop），v2.3 已实现 PermissionRuleEngine。架构具备扩展基础。

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
| LSP 集成 | **v2.3: VS2022 Error List 轮询 + validate_file 工具** | 完整 LSP server（66KB），跳转/补全/诊断 |
| 代码解析 | **✅ v2.8: TreeSitter.DotNet AST 解析 + RegexSymbolParser fallback** | Tree-sitter 语法树解析 |
| 代码智能 | **✅ v2.8: AST 级符号提取（含函数签名、成员变量、访问修饰符）** | 语义级理解 |
| 符号提取 | **✅ v2.8: 类/结构体/枚举/函数(签名)/成员变量/命名空间/typedef/宏 + Qt .ui 控件** | 完整语法树 |
| C++ 复杂语法 | **✅ v2.8: Tree-sitter 完整支持（模板/嵌套命名空间/条件编译）** | Tree-sitter 完整支持 |

**v2.8 代码解析增强（2026-04-01 完成）**:
- TreeSitter.DotNet 1.3.0 NuGet 包，C/C++ AST 解析（后台线程，不阻塞 UI）
- ISymbolParser 接口抽象，tree-sitter 优先 → regex fallback
- kernel32 LoadLibrary 预加载 native DLL + 熔断机制
- SymbolRecord 增强: StartLine/EndLine/Signature/AccessModifier
- E2E: 4705 files, **101335 symbols**（比纯 regex 68230 提升 48%），21s
- Qt .ui 文件解析（widget class + objectName）
- 索引范围: .h/.hpp/.hxx/.cpp/.cxx/.cc/.c/.cppm/.ui（移除 .cs）

**已排除方案**: VS2022 CodeModel (DTE FileCodeModel API) — E2E 证明不可行，DTE API 必须 UI 线程执行，4705 文件直接卡死 VS。

**下一步**: Tree-sitter 增量索引（DocumentSaved 事件 → 单文件重解析 → 局部更新索引，<100ms）

### v2.3 LSP 语义验证 POC 实现

**实现内容**（2026-03-31 完成）:
- **FileDiagnostic 类型**: 封装编译诊断（位置、等级、信息）
- **IFileContext.GetDiagnosticsAsync 接口**: 异步诊断获取
- **VSAgentContext 轮询**: VS2022 DTE ErrorList（500ms 间隔，连续2次稳定，最长5秒）
- **EditFileTool 自动诊断**: 模式 A/B 编辑成功后自动追加诊断结果到 LLM 上下文
- **ValidateFileTool**: 新增独立验证工具（`validate_file`），参数 `file_path`，触发即时诊断
- **工具集扩展**: 15 → 16 工具

**集成效果**:
- 编辑后立即获得 VS2022 原生诊断，LLM 可在同一轮直接修复
- 无需额外编译步骤，诊断延迟 < 5 秒
- 支持多诊断聚合，优先级排序（Error > Warning > Info）

---

## 八、权限系统

| 维度 | AICA | OpenCode |
|------|------|----------|
| 粒度 | ~~白名单/黑名单二元~~ **v2.3: glob + action(read/write/execute) + 三级(allow/ask/deny)** | glob 模式 + action 分类 + 三级 |
| 动态检查 | ~~启动时静态配置~~ **v2.3: PermissionRuleEngine 运行时评估** | 运行时按规则引擎评估 |
| 每 Agent 独立 | 否 | 是 |
| 路径安全 | SafetyGuard（受保护路径 + 命令黑名单） | assertExternalDirectory + 权限规则 |
| .ignore 支持 | .aicaignore | .gitignore + 自定义 |

**✅ v2.3 已实现**: PermissionRule + PermissionRuleEngine（glob→regex + action 分类 + 三级控制 allow/ask/deny）+ SafetyGuard.CheckPathAccessWithRules() 集成。权限系统已基本对齐 OpenCode。

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

### v2.3/v2.4 新发现的问题

| # | 问题 | 严重度 | 状态 |
|---|------|--------|------|
| 16 | MiniMax 不生成嵌套 JSON 参数（edits/files 数组），模式 B/C 退化为多次单调用 | 中 | ⚠️ MiniMax 模型限制，功能无损 |
| 17 | 工具定义 token 未计入 condense 预算（~8K tokens/请求） | 高 | **✅ v2.4 P1 已修复** |
| 18 | ParseArguments JSON 解析失败静默返回空 dict | 中 | **✅ v2.4 P0 已修复（添加日志）** |
| 19 | 溢出检测遗漏 MiniMax 中文错误消息 | 中 | **✅ v2.4 P3 已修复（+6 模式）** |
| 20 | 每次 API 调用发送全部 16 个工具定义（无意图过滤） | 中 | v2.5 方案 APPROVED（第十五章） |

---

## 十一、优先级建议（v2.3 更新）

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

| 改进项 | 理由 | v2.2 状态 |
|--------|------|-----------|
| **上下文管理重建（步骤1-3）** | Compaction 增强 → 清理 update_plan → Plan Agent 分离 | **✅ 已完成（第十二章）** |
| **基础设施补充（步骤5-7）** | 统一配置层 → 可观测性 → 接口抽取 + index.json | **✅ 已完成（第十三章）** |
| **LSP 语义验证 POC** | 编辑后自动调用 VS2022 诊断 API 检测语法错误，LLM 立即修复。**从第三梯队提升** | **✅ v2.3 已实现** |
| **edit 工具统一增强（multiedit + apply_patch 合并）** | 增强现有 edit 工具，支持同文件多处编辑 + 多文件编辑，保留 diff 预览 + 用户确认流程。不新增工具，向后兼容。详见第十四章 | **✅ v2.3 已实现（第十四章）** |
| **list_projects 降级为 prompt 注入** | 启动时注入 Solution 结构到 System Prompt，减少一轮工具调用。该信息是 VS DTE API 独有能力，GitNexus 无法替代 | **✅ v2.3 已实现** |
| 会话持久化（SQLite） | 当前规模不需要，IConversationStorage 接口已预留 | 暂缓 |
| 权限系统增强 | glob + action 分类 + 三级控制，现有 diff 确认机制已覆盖大部分需求 | **✅ v2.3 已实现** |
| LLM 结构化错误分类 | 上层 AgentExecutor 可做差异化处理 | **✅ v2.3 已实现** |
| 步骤4: Token 追踪 | SharpToken NuGet，私有部署 LLM 不重要 | **暂缓（用户确认）** |

### 第三梯队 — v2.5 优化（中等价值，方案已 APPROVED）

| 改进项 | 价值 | 状态 |
|--------|------|------|
| **P2 按意图过滤工具集** | 高（节省 2-4K tokens/请求） | v2.5 APPROVED（第十五章） |
| **工具输出裁剪时间戳标记** | 低（防御性改进） | v2.5 搭便车实施 |
| **Token 精确计量（校准因子）** | 中（改善 condense 触发精度） | v2.5 APPROVED |
| **Prompt 缓存（static/dynamic 分离）** | 低（MiniMax 未确认支持） | v2.5 推迟或最后 |

### 第四梯队 — 长期路线图（高价值高成本）

| 改进项 | 理由 |
|--------|------|
| ~~apply_patch（多文件原子操作）~~ | **已完成** — 合并到 edit 统一增强（第十四章） |
| 会话持久化（SQLite）+ 会话标题 | ⏸ 搁置 — JSON 方案当前够用 |
| 多模型适配层 | 解绑 MiniMax，支持 Claude/GPT（模式 B/C 在强模型上可用） |
| Git 集成（status/diff/commit 工具） | 开发体验闭环 |
| ~~Tree-sitter 代码解析~~ | **✅ v2.8 已完成** — TreeSitter.DotNet + ISymbolParser + regex fallback |
| Diff 可视化增强 | 提升代码审查体验 |
| ~~消息 Part 化~~ | **✅ v2.6 已完成** — ImagePart + CodePart(四维坐标) + 附件标签 UX |
| Tree-sitter 增量索引 | **下一任务** — DocumentSaved 事件触发单文件重解析，<100ms |

---

## 十二、上下文管理系统性重建方案（v2.2）

> 日期: 2026-03-31
> 方案制定: Agent Team（planner + architect reviewer 双向评审，两轮达成一致）
> 状态: **APPROVED**（全部步骤通过评审）

**执行顺序：**

```
步骤1 Compaction 增强 ──→ 步骤2 清理 update_plan ──→ 步骤3 Plan Agent 分离 ──→ 步骤4 Token 追踪(可选)
        [低风险]                 [低风险]                  [中低风险]                 [低风险]
        ~65 行新增              ~364 行删除               ~345-400 行新增             ~100 行新增
```

### 12.0 背景与动机

对比 OpenCode，AICA 上下文管理存在以下关键差距：

| 差距 | 用户痛感 | 根因 |
|------|---------|------|
| Compaction 单次限制 | 长会话质量断崖、context overflow | `HasAutoCondensed` 布尔值阻止第二次触发 |
| 无 Plan Agent | 复杂任务 doom loop 率高 | 单 AgentExecutor，规划与执行不分离 |
| `update_plan` 工具无用 | LLM 不主动调用，工具列表噪声 | 设计定位尴尬（进度记录器 vs 规划手段） |
| Token 估算精度 ±20% | 偶尔提前/延迟触发 condense | 字符级粗估，无 tokenizer |

### 12.1 步骤1: Compaction 增强（多次触发 + Checkpoint）

**优先级**: 最高 | **风险**: 低 | **新增 ~65 行，改动 4 文件**

#### 目标

- 解除单次 compaction 限制，支持长会话中多次压缩
- 引入 checkpoint 机制记录每次压缩点
- 保持 emergency condense 作为兜底

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `TaskState.cs` | `HasAutoCondensed: bool` → `CondenseCount: int` + `LastCondenseAtMessageCount: int`，新增 `CanCondenseAgain(currentCount)` 方法 | +15, -2 |
| `AgentExecutor.cs` | 替换 `!_taskState.HasAutoCondensed` 为 `_taskState.CanCondenseAgain(history.Count)`；emergency condense 后重置 `LastCondenseAtMessageCount`；`HandleCondense()` (line 646) 同步改为 `RecordCondense()`；增加 condense 间隔保护。**注意：HasAutoCondensed 共 3 处设置（line 133/173/646），全部替换** | ~25 行改动 |
| `ConversationCompactor.cs` | `BuildCondensedHistory` 支持累积式摘要（合并而非嵌套旧 summary），摘要上限 3000 tokens | +20 |
| `TokenBudgetManager.cs` | 新增 `ComputeReCondenseGap(maxTokenBudget)` — 两次 condense 之间最少积累的新消息数 | +10 |

#### 设计细节

**Condense 触发逻辑（基于 token 估算，对齐 OpenCode 思路）：**

```
estimatedTokens = history.Sum(m => EstimateTokens(m.Content))
condenseThreshold = conversationBudget * 0.70

条件: _taskState.CanCondenseAgain(history.Count, reCondenseGap)
  AND estimatedTokens >= condenseThreshold
  AND history.Count >= 6  (安全下限)
```

> **v2.2 实施改进**: 原方案基于消息条数阈值（`budget/1500*0.6`），177K context 下需 70 条消息才触发。
> 测试发现实际场景中 10 次工具调用（含大文件读取）就可能接近 context limit，但消息数远不到 70。
> 改为基于累计 token 估算后，condense 自适应实际内容量，不再受消息条数限制。

`CanCondenseAgain(currentCount, reCondenseGap)` 逻辑：
- `CondenseCount == 0` → 允许（首次）
- `currentCount - LastCondenseAtMessageCount >= ReCondenseGap` → 允许（间隔足够）
- 否则 → 不允许（避免频繁压缩）

**累积式摘要（ConversationCompactor.BuildCondensedHistory）：**
- 如果 history 中已有 `[Conversation condensed]` 系统消息，提取内容作为 `## Previous Summary`
- Prompt 指示 LLM "合并而非嵌套"旧 summary
- Previous Summary 超过 3000 tokens (~12000 chars) 时截断
- 新 summary 追加为 `## Current Summary`

**Emergency Condense 处理：**
- Emergency condense 后重置 `LastCondenseAtMessageCount = history.Count`
- 不受 ReCondenseGap 间隔限制（emergency 始终允许触发）

**MiniMax-M2.5 考量：**
- 177K context window → `ComputeCondenseMessageThreshold` 约 70 条消息才首次触发
- ReCondenseGap 建议设为 threshold 的 40%（约 28 条）

**Checkpoint 记录：**
- `TaskState.CondenseHistory: List<CondenseCheckpoint>` 记录 `{MessageCount, Timestamp, SummaryLength}`
- 用于 telemetry 和调试，不影响运行时逻辑

#### 验证策略

- 单元测试：`CanCondenseAgain` 边界条件（首次/间隔不足/间隔足够/emergency 后重置）
- 集成测试：含大文件读取的 10-15 轮工具调用会话，验证 token 累计触发 condense
- 回归测试：emergency condense 仍在 `context_length_exceeded` 时触发

#### 回滚策略

`CondenseCount` 改回 `HasAutoCondensed`（`Count > 0` 等价 `true`），一个 commit 可回退。

#### 成功标准

- [ ] 含大文件读取的会话中 token 累计达到 70% budget 时自动触发 condense，支持多次
- [ ] 累积式摘要不超过 3000 tokens
- [ ] Emergency condense 不受间隔限制

---

### 12.2 步骤2: 删除 update_plan + PlanManager 清理

**优先级**: 中（清理死代码） | **风险**: 低 | **删除 ~364 行，改动 12 文件**

#### 目标

- 删除无用的 `update_plan` 工具链
- 清理 `PlanManager` 死代码（已确认无外部调用者）
- 保留 `TaskPlan` / `PlanStep` / `PlanStepStatus` 类型（定义在 `IAgentContext.cs`，步骤3 复用）

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `UpdatePlanTool.cs` | **删除整个文件** | -184 |
| `PlanManager.cs` | **删除整个文件** | -66 |
| `ITaskContext.cs` | 删除 `CurrentPlan` 属性和 `UpdatePlan` 方法（`TaskPlan`/`PlanStep`/`PlanStepStatus` 类型定义在 `IAgentContext.cs`，不受影响） | -8 |
| `TaskState.cs` | 删除 `HasActivePlan` 属性 | -1 |
| `AgentExecutor.cs` | 删除 plan update 跟踪代码（line 260 跳过、line 319-323 step） | -8 |
| `ToolCallProcessor.cs` | 删除 update_plan 的 action description | -1 |
| `ChatToolWindowControl.xaml.cs` | 删除 `RegisterTool(new UpdatePlanTool())` | -1 |
| `VSAgentContext.cs` | 删除 `UpdatePlan` 方法实现 | -10 |
| `InteractionToolsTests.cs` | 删除 UpdatePlanTool 测试 | -70 |
| `MockAgentContext.cs` | 删除 `UpdatePlan` mock | -5 |
| `PlanAwareRecoveryTests.cs` | **删除整个文件** | -6+ |
| `ToolCallOptimizationTests.cs` | 清理工具名列表中的 `update_plan`（连同已废弃的 `condense`、`find_by_name`、`list_code_definition_names`） | ~-4 |

#### 验证策略

- 编译通过（所有引用已清理）
- 现有测试通过（删除相关测试，其余不受影响）
- 工具列表确认：`ToolDispatcher.GetToolNames()` 不含 `update_plan`

#### 回滚策略

`git revert` 单个 commit，所有删除的文件在 git history 中可恢复。

#### 成功标准

- [ ] 编译通过，全部测试通过
- [ ] 工具列表从 16 → 15，减少工具选择噪声
- [ ] `TaskPlan`/`PlanStep`/`PlanStepStatus` 类型保留可用（位于 `IAgentContext.cs`）

---

### 12.3 步骤3: Plan Agent 分离（AgentExecutor 内部集成）

**优先级**: 高 | **风险**: 中低 | **新增 ~345-400 行，改动 5 文件（UI 层零改动）**

#### 目标

- 将复杂任务的规划能力分离为独立的 `PlanAgent`
- PlanAgent 使用精简只读工具集，输出结构化 JSON plan
- 在 `AgentExecutor` 内部集成（无独立 Orchestrator 层）
- 失败时静默降级到无 plan 执行

#### 架构设计

```
用户请求 → AgentExecutor.ExecuteAsync
              ├─ complexity != Complex → 直接进入主循环（现有路径，零开销）
              └─ complexity == Complex → PlanAgent.GeneratePlanAsync（内部调用）
                                          ├─ Success → plan 注入到 history → 进入主循环
                                          └─ Failure → 静默 fallback → 进入主循环（无 plan）
```

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `PlanAgent.cs`（新建） | 独立 Plan Agent：精简工具集、JSON schema 输出、10 次迭代上限、超时控制、Markdown 渲染 | +250-300 |
| `PlanPromptBuilder.cs`（新建） | PlanAgent 专用极简 prompt + JSON schema 定义 | +60 |
| `AgentExecutor.cs` | 主循环前插入 PlanAgent 调用（~15 行内联） | +15 |
| `TaskComplexityAnalyzer.cs` | 新增 `RequiresPlanning` 属性（Complex + 多文件/重构意图 + 用户显式要求） | +15 |
| `ILLMClient.cs` | 扩展 `StreamChatAsync` 支持 `response_format` 参数（可选） | +5-10 |

#### PlanAgent 核心设计

**工具集（只读，LLM 请求层面过滤）：**

```csharp
PlanningToolNames = { "read_file", "grep_search", "list_dir", "glob" }

var planTools = allToolDefinitions
    .Where(t => PlanningToolNames.Contains(t.Name))
    .ToList();
```

不修改 ToolDispatcher 注册状态，仅在 LLM 请求中传入精简 toolDefinitions。

**输出格式（JSON Schema 约束）：**

```json
{
  "type": "object",
  "properties": {
    "goal": { "type": "string" },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "description": { "type": "string" },
          "files": { "type": "array", "items": { "type": "string" } },
          "action": { "type": "string", "enum": ["read", "search", "edit", "create", "test"] }
        },
        "required": ["description"]
      }
    },
    "key_context": { "type": "string" }
  },
  "required": ["goal", "steps"]
}
```

**失败降级：**

```csharp
public class PlanResult
{
    public bool Success { get; set; }
    public string PlanText { get; set; }      // Markdown（注入到 executor）
    public List<PlanStep> Steps { get; set; }  // 结构化（从 JSON 解析）
    public string FailureReason { get; set; }  // Telemetry only
}
```

所有失败（超时、LLM 错误、JSON 解析失败、steps 为空）→ `Success = false` → AgentExecutor 静默跳过 plan 注入，正常执行。

**Token Budget 隔离：**

| 组件 | 预算 |
|------|------|
| PlanAgent system prompt | ~800 tokens |
| 工具调用结果（截断） | ≤10K tokens |
| LLM plan 输出 | ≤2K tokens |
| **PlanAgent 总计** | **~13K tokens（独立，不从主 executor 扣除）** |
| 传递到 executor 的 PlanText | 500-1500 tokens |

PlanAgent 的 conversation history 在 `GeneratePlanAsync` 返回后丢弃，只有精简 PlanText 注入主 executor。

**MiniMax-M2.5 适配（三层防御）：**

1. `response_format=json_schema` 强制 JSON 输出
2. Prompt 极度精简：`"You are a task planner. Use tools to explore, then output JSON plan."`
3. Fallback：若 MiniMax 不支持 `response_format`，用 regex 提取第一个 `{...}` JSON 块

**Plan 注入方式（AgentExecutor.ExecuteAsync 内部）：**

```csharp
if (complexity == TaskComplexity.Complex)
{
    var planAgent = new PlanAgent(_llmClient, _toolDispatcher);
    var planResult = await planAgent.GeneratePlanAsync(
        userRequest, context, toolDefinitions, ct);

    if (planResult.Success)
    {
        history.Add(ChatMessage.System(
            "[Task Plan — generated by planning phase]\n" + planResult.PlanText));
        yield return AgentStep.TextChunk($"\n📋 **Plan:**\n{planResult.PlanText}\n\n---\n");
    }
    // Failure: fall through to normal execution
}
```

#### 验证策略

- 单元测试：AgentExecutor 路由逻辑（简单 → 直接，复杂 → plan first）
- 单元测试：PlanAgent JSON 解析（valid/invalid/empty steps）
- 集成测试：PlanAgent 对多文件重构请求生成合理 plan
- E2E 测试：完整流程 "重构所有 DAO 类" → plan → 执行

#### 回滚策略

删除 `PlanAgent.cs`、`PlanPromptBuilder.cs`，移除 AgentExecutor 中 ~15 行内联代码。UI 层无需回滚。

#### 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| PlanAgent 超时/失败 | 静默 fallback 到无 plan 执行，用户无感知 |
| Plan 质量差 | JSON schema 约束 + steps 非空验证 |
| Token 消耗过多 | 独立 budget，只传精简 PlanText |
| MiniMax 不支持 response_format | Fallback: prompt 约束 + regex JSON 提取 |
| 复杂度误判 | RequiresPlanning 门槛高于 Complex |

#### 成功标准

- [ ] Complex 任务自动生成结构化 plan（JSON 可解析）
- [ ] PlanAgent 失败时静默降级，用户无感知
- [ ] PlanAgent token 消耗不超过 15K，传递到 executor 不超过 2K
- [ ] 简单/Medium 任务零开销（不触发 PlanAgent）
- [ ] UI 层零改动

---

### 12.4 步骤4: Token 追踪增强（可选）

**优先级**: 低 | **风险**: 低 | **新增 ~100 行，改动 4 文件**

#### 目标

提升 token 估算精度（从字符级 ±20% 到 tokenizer 级 ±5%）。

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `TokenEstimator.cs`（新建） | 基于 SharpToken（tiktoken C# 移植）的 token 计数器 | +80 |
| `ContextManager.cs` | `EstimateTokens` 切换到 TokenEstimator | ~5 |
| `AgentExecutor.cs` | 使用精确 token 计数替代 `content.Length / 4` | ~10 |
| `TokenBudgetManager.cs` | threshold 计算使用精确 token | ~5 |

#### 注意事项

- SharpToken NuGet 包约 2MB（含词表文件）
- MiniMax-M2.5 使用非标准 tokenizer，SharpToken 只能提供更好的近似值
- 建议等步骤1-3 稳定后再实施

---

### 12.5 延期: 消息 Part 化（推迟到 v3.0）

**优先级**: 低 | **风险**: 高 | **新增 ~160 行，影响 10+ 文件**

将 `ChatMessage.Content: string` 扩展为 `List<ContentPart>` 结构（Text/ToolUse/ToolResult），支持精细化 compaction。改动面广（ChatMessage 是核心模型），不在本轮实施。

---

### 12.6 总改动量与执行计划

| 步骤 | 内容 | 新增行 | 删除行 | 文件数 | 风险 | 前置依赖 |
|------|------|--------|--------|--------|------|----------|
| **步骤1** | Compaction 增强 | ~65 | ~5 | 4 | 低 | 无 |
| **步骤2** | 清理 update_plan | ~0 | ~364 | 12 | 低 | 步骤1 完成（TaskState 字段变更） |
| **步骤5** | 统一配置层 | ~170 | ~40 | 2+7 | 低 | 步骤2 完成（AgentExecutor 清理后基础更干净）**→ 详见第十三章** |
| **步骤6** | 可观测性增强 | ~70 | ~25 | 0+3 | 低 | 步骤5（telemetry 开关）**→ 详见第十三章** |
| **步骤3** | Plan Agent 分离 | ~345-400 | ~0 | 5 | 中低 | 步骤2 完成（死代码已清理） |
| **步骤7** | 接口抽取 + index.json | ~80 | ~15 | 2+2 | 低 | 无硬依赖，可穿插 **→ 详见第十三章** |
| **步骤4** | Token 追踪（可选） | ~100 | ~5 | 4 | 低 | 步骤1-3 稳定后 |
| 延期 | 消息 Part 化 | ~160 | ~20 | 10+ | 高 | v3.0 |

**完整执行序：**

```
步骤1(Compaction) → 步骤2(清理) → 步骤5(配置层) → 步骤6(可观测性) → 步骤3(Plan Agent) → 步骤7(接口抽取) → 步骤4(Token)
   [1天]             [0.5天]         [1天]             [0.5天]            [2天]                [0.5天]           [0.5天]
```

**执行序理由**：步骤1 直接解决长会话质量断崖（最高优先级）→ 步骤2 低风险清理 → 步骤5 在清理后的干净代码上抽取配置 → 步骤6 基于配置层采集 telemetry → 步骤3 Plan Agent 分离 → 步骤7 存储接口抽取（独立可穿插）→ 步骤4 锦上添花。

### 12.7 全局成功标准

- [ ] 步骤1: token 累计达 70% budget 时自动触发 condense，支持多次，质量不断崖
- [ ] 步骤2: 工具列表中无 update_plan，编译通过，全部测试通过
- [ ] 步骤5: 修改 config.json 后对应组件使用新值；无 config.json 时行为不变
- [ ] 步骤6: telemetry JSONL 中包含 condenseEvents / tokenUsage / fuzzyMatchDistribution
- [ ] 步骤3: 多文件重构任务的 doom loop 率从 ~30% 降至 <10%
- [ ] 步骤7: 50 会话 ListConversationsAsync <10ms；index.json 损坏时自动重建
- [ ] 每个步骤通过现有测试套件回归
- [ ] MiniMax-M2.5 下的行为与方案设计一致
- [ ] VSIX 打包大小增量 = 0（零新 NuGet 依赖）

---

## 十三、基础设施补充方案（v2.2）

> 日期: 2026-03-31
> 方案制定: Agent Team（planner + architect reviewer 双向评审，两轮达成一致）
> 状态: **APPROVED**（全部步骤通过评审）
> 与第十二章关系: 步骤5-7 在步骤1-2 之后执行，与步骤3-4 无冲突

### 13.0 背景与动机

第十二章方案在上下文管理维度内是系统性的，但全局分析发现三个底层基础设施缺失：

| 基础设施 | 影响 | 现状 |
|----------|------|------|
| 统一配置层 | condense 阈值、排除目录、工具超时等大量硬编码，用户无法覆盖 | 文档未提及（遗漏） |
| 可观测性/Telemetry | 步骤1 需要"观察 condense 效果"，模糊匹配需要"观察命中分布" | 仅有基础 SessionRecord（遗漏） |
| 会话存储扩展性 | 当前 JSON 存储无接口抽象，list 操作 O(n) 全量反序列化 | 仅一行"SQLite"描述 |

---

### 13.1 步骤5: 统一配置层（AicaConfig）

**优先级**: 高 | **风险**: 低 | **新增 ~170 行，改动 7 文件** | **前置**: 步骤2 完成

#### 目标

- 将散落在各文件的硬编码常量收拢到可序列化配置类
- 支持 `~/.AICA/config.json` 用户覆盖，缺省值等于当前硬编码值（零行为变更）
- 纯静态加载（`AicaConfig.Current`），启动时读一次，不引入 DI，不做热加载

#### 当前硬编码清单（源码审计）

| 文件 | 硬编码 | 当前值 |
|------|--------|--------|
| `ContextManager.cs:18` | 默认 maxTokenBudget | 177224 |
| `TokenBudgetManager.cs:105-110` | MinCondenseMessageThreshold / MinCondenseCompressibleThreshold | 18 / 12 |
| `AgentExecutor.cs:33-35` | DoomLoopThreshold / MaxRetries / RetryDelaysMs | 3 / 2 / [1000,3000] |
| `AgentExecutor.cs:47` | 默认 maxIterations / maxTokenBudget | 50 / 32000 |
| `TaskState.cs:37` | MaxUserCancellations | 3 |
| `GrepSearchTool.cs:28-31` | RipgrepThreshold / RipgrepTimeoutSeconds | 200 / 30 |
| `GrepSearchTool.cs:399-409` | 排除目录列表 | .git,.vs,bin,obj,node_modules... |
| `GrepSearchTool.cs:433-444` | 排除文件扩展名列表 | .exe,.dll,.pdb... |
| `RunCommandTool.cs:127` | 默认 timeout_seconds | 30 |
| `RunCommandTool.cs:214` | stdout/stderr 缓冲区上限 | 16000/8000 |
| `ConversationStorage.cs:272` | 保留会话数量 keepCount | 100 |
| `GitNexusProcessManager.cs:26` | StartTimeoutMs | 15000 |

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `Config/AicaConfig.cs`（新建） | 顶层配置类，嵌套 AgentConfig / CondenseConfig / ToolConfig / StorageConfig / TelemetryConfig。静态 `AicaConfig.Current` + `Load(path?)` | +130 |
| `Config/AicaConfigLoader.cs`（新建） | 加载 `~/.AICA/config.json` → 反序列化 → 与默认值合并。缺文件/畸形 JSON 回退全默认 | +40 |
| `AgentExecutor.cs` | DoomLoopThreshold / MaxRetries 从配置读取 | ~10 |
| `TaskState.cs` | MaxUserCancellations 从配置读取 | ~3 |
| `TokenBudgetManager.cs` | condense 阈值从配置读取 | ~8 |
| `GrepSearchTool.cs` | RipgrepThreshold / 排除目录从配置读取 | ~12 |
| `RunCommandTool.cs` | 默认 timeout 从配置读取 | ~5 |
| `GitNexusProcessManager.cs` | StartTimeoutMs 从配置读取 | ~3 |

#### 设计细节

**config.json 格式：**

```json
{
  "agent": { "maxIterations": 50, "maxTokenBudget": 32000, "doomLoopThreshold": 3, "maxRetries": 2, "maxUserCancellations": 3 },
  "condense": { "minMessageThreshold": 18, "minCompressibleThreshold": 12 },
  "tools": { "grepRipgrepThreshold": 200, "grepTimeoutSeconds": 30, "commandDefaultTimeoutSeconds": 30, "gitNexusStartTimeoutMs": 15000, "excludeDirectories": [...], "excludeExtensions": [...] },
  "storage": { "conversationRetentionCount": 100 },
  "telemetry": { "enabled": true }
}
```

用户只需写想覆盖的字段，未写字段保持默认。

#### 验证与回滚

- 无 config.json → 行为完全不变（零回归）
- 畸形 JSON → 回退全默认 + Debug.WriteLine 警告
- 回滚：删除 2 个新文件 + 还原 7 文件 const 改动

---

### 13.2 步骤6: 可观测性增强（SessionRecordBuilder 扩展）

**优先级**: 中 | **风险**: 低 | **新增 ~70 行，改动 3 文件** | **前置**: 步骤5（telemetry 开关）

#### 目标

- 在现有 SessionRecordBuilder 上直接扩展 condense 事件、token 使用量、模糊匹配命中分布
- **不修改 IAgentContext 接口**（避免与步骤2/3 冲突）
- 支持 telemetry 开关（`AicaConfig.Current.Telemetry.Enabled`）

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `AgentTelemetry.cs` | SessionRecord 新增字段（condenseEvents / totalPromptTokens / totalCompletionTokens / fuzzyMatchDistribution）+ CondenseEvent 值对象 + SessionRecordBuilder 新增 RecordCondenseEvent() / RecordTokenUsage() / RecordFuzzyMatchLevel() | +70 |
| `AgentExecutor.cs` | condense 后调用 RecordCondenseEvent()；WriteTelemetry 加 enabled 检查 | ~10 |
| `EditFileTool.cs` | FindWithCascade 命中时通过 ToolResult.Metadata 传递匹配级别，AgentExecutor 转发给 telemetryBuilder | ~15 |

#### 数据传递路径（不修改 IAgentContext）

```
EditFileTool.FindWithCascade → ToolResult.Metadata["fuzzy_match_level"] = "indent_flexible"
    → AgentExecutor 工具调用后检查 result.Metadata
    → telemetryBuilder.RecordFuzzyMatchLevel("indent_flexible")
```

#### JSONL 输出示例

```json
{
  "sessionId": "abc123", "iterations": 15, "toolCalls": 23,
  "condenseEvents": [{"messagesBefore": 42, "messagesAfter": 6, "summaryTokenEstimate": 1200, "triggerReason": "proactive"}],
  "totalPromptTokens": 45000, "totalCompletionTokens": 8000,
  "fuzzyMatchDistribution": {"exact": 5, "line_trimmed": 2, "indent_flexible": 1},
  "outcome": "completed", "durationMs": 120000
}
```

#### 验证与回滚

- 完整会话后 JSONL 新增字段有值
- `telemetry.enabled: false` 时不写入
- 旧版 JSONL 格式向后兼容（新字段缺失时为默认值）
- 回滚：删除新增字段，还原 3 文件改动

---

### 13.3 步骤7: 会话存储接口抽取 + index.json 缓存

**优先级**: 低 | **风险**: 低 | **新增 ~80 行，改动 2 文件** | **前置**: 无（可穿插）

#### 目标

- 抽取 `IConversationStorage` 接口，为未来存储引擎切换预留扩展点
- 新增 index.json 缓存，list 操作从 O(n) 全量反序列化降到 O(1) 读索引
- 零新 NuGet 依赖

#### 为什么不用 SQLite

- `netstandard2.0` + `.NET Framework 4.8` 宿主下，`Microsoft.Data.Sqlite` 需原生 `e_sqlite3.dll` (~1.5MB)，VSIX 打包器不自动处理
- 单人开发者单进程场景，JSON + SSD 下 list 50 会话 <100ms，SQLite 事务/并发优势无意义
- 未来如需 SQLite：建议单表 + JSON blob（`conversations` 表 + `data JSON` 列），而非多表 ORM

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `IConversationStorage.cs`（新建） | 接口定义：SaveConversationAsync / LoadConversationAsync / ListConversationsAsync / ListConversationsForProjectAsync / DeleteConversationAsync / ExportAsMarkdownAsync / CleanupOldConversationsAsync | +30 |
| `ConversationIndexCache.cs`（新建） | 管理 `~/.AICA/conversations/index.json`：Load / Update / Remove / Rebuild。write-to-temp-then-rename 原子写入 | +50 |
| `ConversationStorage.cs` | 实现 IConversationStorage；Save/Delete 后同步更新 index.json；首次无 index.json 时自动 Rebuild | ~15 |

#### index.json 格式

```json
[
  {"id": "abc123", "title": "Fix build error", "projectPath": "D:\\Project\\MyApp", "updatedAt": "...", "messageCount": 15},
  ...
]
```

#### 验证与回滚

- 无 index.json → 自动 Rebuild，后续 <10ms
- 损坏 index.json → 自动 Rebuild
- 回滚：删除 2 新文件 + 还原 ConversationStorage + 删除 index.json（自动重建保证安全）

---

### 13.4 总改动量

| 步骤 | 内容 | 新增行 | 改动行 | 新文件 | 风险 | 前置依赖 |
|------|------|--------|--------|--------|------|----------|
| **步骤5** | 统一配置层 | ~170 | ~40 | 2 | 低 | 步骤2 完成 |
| **步骤6** | 可观测性增强 | ~70 | ~25 | 0 | 低 | 步骤5 |
| **步骤7** | 接口抽取 + index.json | ~80 | ~15 | 2 | 低 | 无 |
| **合计** | | **~320** | **~80** | **4** | | |

### 13.5 全局成功标准

- [ ] 步骤5: config.json 覆盖生效；无 config.json 时零回归
- [ ] 步骤6: telemetry JSONL 包含 condenseEvents / tokenUsage / fuzzyMatchDistribution
- [ ] 步骤7: 50 会话 ListConversationsAsync <10ms；index.json 损坏自动重建
- [ ] VSIX 打包大小增量 = 0（零新 NuGet 依赖）

---

## 十四、Edit 工具统一增强方案（v2.3）

> 独立文档: [AICA_v2.3_Edit_Enhancement_Plan.md](./AICA_v2.3_Edit_Enhancement_Plan.md)
> 日期: 2026-03-31
> 方案制定: Agent Team（planner + architect reviewer 双向评审，两轮达成一致）
> 状态: **✅ 已实现**

将 multiedit（同文件多处编辑）和 apply_patch（多文件编辑）合并到现有 `edit` 工具中，不新增工具，100% 向后兼容。

**三阶段执行**: Phase 0 基础设施（~70 行）→ Phase 1 同文件多处编辑（~250 行）→ Phase 2 多文件编辑（~130 行），合计 ~450 行，7 文件，零新依赖。

**执行完成** (2026-03-31):
- Phase 0: ToolParameterProperty +Items/Properties/Required, GetListOfDicts, 序列化测试（4 文件）
- Phase 1+2: EditFileTool ~260 行（3 种调用模式、FindWithCascadeUnique、ExecuteMultiEditAsync、ExecuteMultiFileAsync、MultiEditOutcome 枚举）
- 测试: EditFileToolTests.cs ~230 行, ToolParameterValidatorTests +60 行, ToolDefinitionSerializationTests ~130 行
- 全量编译: AICA.sln Build succeeded, AICA.Core.dll + AICA.vsix 输出成功

**核心设计决策**:
- 三种调用模式通过参数组合自动检测（单编辑/edits 数组/files 数组）
- 同文件多处编辑聚合为一个 diff 预览，一次确认
- 多文件编辑逐文件各一次 diff 预览
- 偏移漂移处理：从后向前应用（Reverse-Order Apply）
- FindWithCascadeUnique 强制唯一匹配
- MultiEditOutcome 结构化枚举替代字符串匹配
- 用户手动编辑检测与模式 A 完全对齐

---

## 十五、v2.5 优化方案

> 独立文档: [AICA_v2.5_Optimization_Plan.md](./AICA_v2.5_Optimization_Plan.md)
> 日期: 2026-04-01
> 方案制定: Agent Team（planner + architect reviewer 双向评审，两轮达成一致）
> 状态: **✅ 全部完成 + E2E 验证通过**

四项优化，全部已实施：

| 优先级 | 优化项 | 实际改动 | 状态 |
|--------|--------|----------|------|
| **1** | P2 按意图过滤工具集（ToolGroup flags + 意图映射） | `DynamicToolSelector.cs` +106 行 | ✅ E2E 通过 |
| **2** | 工具输出裁剪时间戳标记 | `AgentExecutor.cs` +8 行 | ✅ 代码审查通过 |
| **3** | Token 精确计量（流式 usage + EMA 校准 + ratio clamp） | 4 文件 +92 行 | ✅ E2E 通过（graceful 降级） |
| **4** | Prompt 缓存（static/dynamic 分离） | `SystemPromptBuilder.cs` ~154 行改动 | ✅ E2E 通过 |

**评审修复落实**:
- C1: default intent fallback → `"general"` → `ToolGroup.All`（不丢工具）
- H1: `validate_file` 归 Core 组（只读操作）
- H3: `CalibrateFromUsage(int, int)` + ratio clamp [0.3, 3.0]
- H4: `StreamOptionsRequest` + `[JsonPropertyName]` + `StreamUsageEnabled` 配置开关
- M1: `PruneOldToolOutputs` 用 `ContextManager.EstimateTokens()` 替代 `content.Length/4`

**E2E 验证结果（2026-04-01）**:
| 测试 | 结果 |
|------|------|
| read 意图工具过滤 | ✅ 工具数减少，功能正常 |
| modify 意图工具过滤 | ✅ edit 工具可用 |
| general fallback（安全网） | ✅ 全部工具保留 |
| conversation 回归 | ✅ 与 v2.4 一致 |
| 裁剪时间戳标记 | ⚠️ 未触发（需长对话） |
| Token 校准 / stream_options | ✅ 不报错，graceful 降级 |
| Prompt 内容回归 | ✅ 内容完整 |
| bug_fix Complex 全工具 | ✅ ALL 工具可用 |

---

## 附录：版本演进与 Commit 记录

| 版本 | Commit | 核心内容 | 改动量 |
|------|--------|----------|--------|
| v2.0 | `686c9de` | Trust-based AgentExecutor + MCP + Condense + 工具裁剪 | 净减 2680 行 |
| v2.1 | `58ab099` | 工具链增强 + 工具集优化 | +2887/-768 |
| v2.2 | `de521e7` | 上下文管理重建 + 基础设施 | +789/-507 |
| **v2.3+v2.4** | **`7524761`** | **edit 增强 + LSP 验证 + 权限增强 + 错误分类 + 稳定性修复** | **+2430/-23** |
| **v2.5** | **`fab9edb`** | **工具过滤 + 裁剪标记 + Token 计量 + Prompt 缓存** | **+670/-236** |
| v2.5.1 | `5ca0109` | 修复 7 个 pre-existing 测试 (510/510) | ~200 |
| v2.5.2 | `4a2aa5f` | C++ Rules 审查 (Q/HNC 43 补充) | ~150 |
| **v2.6.0** | **`bdca148`** | **消息 Part 化 (ImagePart + CodePart 四维坐标)** | **+1543** |
| **v2.8.0** | **`b1123a2`** | **Tree-sitter 代码解析 + ISymbolParser + Regex 改进** | **+693/-54** |
