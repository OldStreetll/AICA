# AICA v2.1 工具集系统性优化方案

> 版本: v2.1 | 日期: 2026-03-30
> 前置: v2.1 工具链增强完成后（16 个工具，含 write_file + glob）
> 方法: 逐工具审视定位、参数、描述，对标 OpenCode 设计原则
> 原则: 每个工具做一件事，描述明确边界，减少 LLM 选择歧义

### 修正记录

#### v1.1 修正（2 项，用户机器测试发现的运行时问题）

| # | 修正项 | 发现方式 | 修正内容 |
|---|--------|---------|---------|
| F1 | LLM 调用不存在的工具名（`grep`、`bash`） | 用户测试截图：Unknown tool: grep / Unknown tool: bash | 新增 O11（工具名别名映射），新增 O12（read_file 去重逻辑修复） |
| F2 | read_file 分块读取被误判为 Duplicate call | 用户测试截图：不同 offset/limit 被拦截 | 新增 O12，修复 GetToolCallSignature 签名计算 |

#### v2.0 修正（Agent Teams 讨论共识，planner + reviewer 双方验证）

> 讨论方式：planner 深入源码制定详细方案，reviewer 独立审查交叉验证，两方就分歧点逐条讨论达成共识。

| # | 共识项 | planner 立场 | reviewer 立场 | 最终决定 |
|---|--------|-------------|-------------|---------|
| C1 | O8 推迟到 v2.2 | 发现 SystemPromptBuilder 需新增注入逻辑 | 改动量 ~200+ 行（非 ~30 行），跨 5+ 文件，与本次定位不匹配 | **推迟**。O8 从本次 Phase 3 移除 |
| C2 | O10 双路径覆盖问题 | 发现 F4：MCP Phase 2 原生描述覆盖精简版 | P10-1：token 节省不会实现 | **对 cypher 始终使用硬编码精简版，Phase 2 不覆盖**。截断阈值 600 chars（保留 WHEN TO USE + schema 节点/边类型，删除 EXAMPLES） |
| C3 | O11 HandlePartialAsync 遗漏 | 发现 F5：提取 ResolveTool() 私有方法 | P11-4：两处查找需一致处理 | **提取 ResolveTool() 共用两处**。~20 行（非 ~15 行） |
| C4 | O12 TruncatedFiles 死代码 | 同意 EditedFiles 分支仍有效 | TruncatedFiles 分支变死代码 | **加注释标注，不清理**。保持最小改动 |
| C5 | F3 find_by_name 引用错误 | 发现 RunCommandTool.cs:106 引用不存在工具 | 同意 Phase 0 修复 | **Phase 0 一并修复**，改为 glob |
| C6 | O3 额外修复 | 发现 RunCommandTool.cs:99-109 Unix 命令拦截引用 find_by_name | — | **O3 描述优化时一并修复拦截消息中的工具名** |

#### v2.1 执行记录（2026-03-30，Phase 0-4 全部完成，用户测试全部通过）

**编译结果**: AICA.Core.dll + AICA.Core.Tests.dll + AICA.vsix 全部 0 error

**Git diff**: 17 files changed, +580 / -768（净减 188 行）

**用户机器测试结果**（全部通过）:

| 测试项 | 结果 |
|--------|------|
| O11 别名映射 | ✅ `grep`→`grep_search` 别名解析生效，不再报 Unknown tool |
| O12 分块读取 | ✅ 不同 offset 的 read_file 不再被误判 Duplicate call |
| O1 full_replace 容错 | ✅ LLM 被引导使用 write_file |
| O3 负边界 | ✅ LLM 避免使用 cat/grep shell 命令 |
| O4-O7 工具引导 | ✅ LLM 按描述引导选择正确工具 |
| write_file 创建 | ✅ 调用 write_file 而非 edit |
| O10 cypher 不覆盖 | ✅ 日志显示 "using trimmed hardcoded desc" |
| T6 冲突检测回归 | ✅ 收到"文件已被外部修改"警告 |

**改动文件清单**:

| 文件 | Phase | 改动 |
|------|-------|------|
| ToolDispatcher.cs | 0 | +57 行（别名表 + ResolveTool + 两处共用） |
| ToolCallProcessor.cs | 0 | 签名修复（删 1 行 + 注释） |
| AgentExecutor.cs | 0 | +3 行（TruncatedFiles 死代码注释） |
| RunCommandTool.cs | 0+1 | F3 修复 + 描述优化 |
| WriteFileTool.cs | 1 | 描述优化 |
| ReadFileTool.cs | 1 | 描述优化 |
| ListDirTool.cs | 1 | 描述优化 |
| GlobTool.cs | 1 | 描述优化 |
| GrepSearchTool.cs | 1 | 描述优化 |
| UpdatePlanTool.cs | 1 | 描述优化 |
| EditFileTool.cs | 2 | 删 full_replace + 容错引导 + 描述优化 |
| McpBridgeTool.cs | 3 | cypher 精简 + Phase 2 不覆盖 |

---

## 一、当前工具集现状

### 16 个工具清单（debug 日志确认）

| # | 工具名 | 类别 | 行数 | 描述长度 | 参数数 |
|---|--------|------|------|---------|--------|
| 1 | read_file | FileRead | 160 | 153 chars | 3 (path*, offset, limit) |
| 2 | edit | FileWrite | 635 | 197 chars | 5 (file_path*, new_string*, old_string, replace_all, full_replace) |
| 3 | write_file | FileWrite | 222 | 247 chars | 3 (file_path*, content*, overwrite) |
| 4 | list_dir | DirectoryOps | 316 | ~180 chars | 3 (path*, recursive, max_depth) |
| 5 | glob | DirectoryOps | 337 | 166 chars | 3 (pattern*, path, max_results) |
| 6 | grep_search | Search | 717 | 153 chars | 3 (pattern*, path, include) |
| 7 | run_command | Command | 304 | 213 chars | 4 (command*, description*, cwd, timeout_seconds) |
| 8 | list_projects | Analysis | 181 | 221 chars | 2 (project_name, show_files) |
| 9 | ask_followup_question | Interaction | 245 | 250 chars | 3 (question*, options*, allow_custom_input) |
| 10 | update_plan | Interaction | 181 | 79 chars | 2 (plan*, explanation) |
| 11 | gitnexus_context | Analysis/MCP | — | 914 chars | 2 (name*, repo) |
| 12 | gitnexus_impact | Analysis/MCP | — | 1159 chars | 3 (target*, scope, repo) |
| 13 | gitnexus_query | Search/MCP | — | 796 chars | 2 (query*, repo) |
| 14 | gitnexus_detect_changes | Analysis/MCP | — | 459 chars | 1 (repo) |
| 15 | gitnexus_rename | FileWrite/MCP | — | 569 chars | 3 (symbol_name*, new_name*, repo) |
| 16 | gitnexus_cypher | Analysis/MCP | — | 2762 chars | 2 (query*, repo) |

**总描述 token**: ~7200 chars ≈ ~1800 tokens（占 177K 上下文窗口 ~1%）

---

## 二、OpenCode 工具设计原则提炼

从 OpenCode 19 个工具的设计中提炼出以下原则：

### P1: 每个工具做且只做一件事（Single Responsibility）

| OpenCode 实践 | AICA 违反点 |
|--------------|------------|
| `write` 只写文件，`edit` 不能创建新文件 | AICA `edit` 的 `full_replace=true` 可以创建新文件，与 `write_file` 重叠 |
| `bash` 描述明确说"不替代 read/write/edit/glob/grep" | AICA `run_command` 无此边界声明 |

### P2: 描述中明确"不做什么"（Negative Boundaries）

OpenCode 每个工具描述中都有显式的 "Does NOT" 列表，防止 LLM 误选。AICA 工具描述全部只说"做什么"，不说"不做什么"。

### P3: 工具间互相引导（Cross-Reference）

| OpenCode 做法 | 示例 |
|--------------|------|
| write 描述说 "prefer edit tool for modifying existing files" | 引导 LLM 选对工具 |
| edit 描述说 "use write tool to create new files" | 反向引导 |
| bash 描述说 "use read/write/edit/glob/grep instead of bash for file operations" | 防止 LLM 用 bash 替代专用工具 |

AICA 只有 `write_file` 描述引导了 `edit`，其他工具间无互相引导。

### P4: 参数最少化

OpenCode `grep` 仅 3 个参数（pattern, path, include），AICA `grep_search` 也已精简到 3 个（v2.0 从 6→3）。但 `edit` 仍有 5 个参数，其中 `full_replace` 应删除。

### P5: 描述简洁+精准

OpenCode 工具描述平均 ~100 chars，AICA 原生工具平均 ~180 chars。GitNexus MCP 工具描述过长（914-2762 chars），尤其 `gitnexus_cypher` 2762 chars 包含完整 schema 示例。

---

## 三、逐工具优化方案

### O1: `edit` — 移除 full_replace，明确边界

**问题**:
- `full_replace=true` 与 `write_file` 功能重叠
- LLM 可能纠结选哪个创建新文件
- 5 个参数，LLM 参数选择负担重

**优化**:

| 项目 | 当前 | 优化后 |
|------|------|--------|
| 参数 | file_path*, new_string*, old_string, replace_all, full_replace | file_path*, old_string*, new_string*, replace_all |
| Required | file_path, new_string | file_path, old_string, new_string |
| Description | "Edit a file by replacing text, or create a new file with full_replace=true..." | "Replace specific text in an existing file. old_string must match exactly and be unique. Use read_file first to see exact content. Do NOT use this to create new files — use write_file instead." |

**影响**: 删除 `full_replace` 参数和相关逻辑 (~30 行)。`old_string` 从 optional 变 required。

**风险**: LLM 旧会话可能仍尝试 `full_replace=true`。容错方案：`ExecuteAsync` 中检测到 `full_replace` 参数时返回引导信息"请使用 write_file 工具创建新文件"。

---

### O2: `write_file` — 强化描述边界

**问题**: 描述中未明确"不做什么"。

**优化**:

```
当前: "Create a new file or completely overwrite an existing file with the provided content.
       Use this for creating new files. For modifying specific parts of existing files, prefer 'edit'.
       Parent directories are created automatically if they don't exist."

优化: "Create a new file with the provided content. Parent directories are created automatically.
       Do NOT use this to modify parts of an existing file — use 'edit' instead.
       If the file already exists, set overwrite=true (requires user confirmation)."
```

**改动**: 仅描述文本，~5 行。

---

### O3: `run_command` — 添加负边界，防止替代专用工具

**问题**: LLM 经常用 `run_command` + `cat/grep/find` 替代 `read_file/grep_search/glob`，浪费迭代。

**优化**:

```
当前: "Execute a terminal/shell command (e.g., 'dotnet build', 'git status', 'npm install').
       Returns stdout, stderr, and exit code. Commands require user approval.
       Use timeout_seconds parameter for long-running commands."

优化: "Execute a shell command and return stdout, stderr, and exit code. Requires user approval.
       Use ONLY for build, test, git, and system commands.
       Do NOT use for file operations — use read_file, edit, write_file, grep_search, glob, list_dir instead.
       Do NOT use cat, grep, find, ls, or similar shell commands when dedicated tools exist."
```

**改动**: 仅描述文本，~5 行。

---

### O4: `read_file` — 添加互相引导

**问题**: 描述过于简单，未引导 LLM 编辑前先读取。

**优化**:

```
当前: "Read the contents of a file. Use this to view file content before making changes.
       Supports reading specific line ranges with offset and limit parameters."

优化: "Read file contents with optional line range (offset/limit).
       Always read a file before using 'edit' to ensure old_string matches exactly.
       For finding files by name, use 'glob'. For searching file contents, use 'grep_search'."
```

**改动**: 仅描述文本。

---

### O5: `list_dir` — 区分与 glob 的边界

**问题**: LLM 可能用 `list_dir` recursive 替代 `glob`。

**优化**:

```
当前: "List files and directories in the specified path.
       Use recursive=true when user asks for full/complete structure, directory tree, 完整结构, 目录树.
       For large projects, set max_depth=2 or 3 to avoid excessive output."

优化: "List files and directories in a single path, showing sizes and item counts.
       Use for browsing a directory's contents.
       Do NOT use for finding files by pattern — use 'glob' instead.
       Use recursive=true only for directory tree overview, with max_depth=2-3 for large projects."
```

**改动**: 仅描述文本。

---

### O6: `glob` — 添加反向引导

**优化**:

```
当前: "Find files by name pattern using glob syntax.
       Fast file discovery without reading file content.
       Supports patterns like '**/*.cpp', 'src/**/*.h', '*.cs'.
       Results are sorted by modification time (most recent first)."

优化: "Find files by name pattern using glob syntax (e.g., '**/*.cpp', 'src/**/*.h').
       Returns matching file paths sorted by modification time.
       Do NOT use for searching file contents — use 'grep_search' instead.
       Do NOT use for browsing a single directory — use 'list_dir' instead."
```

**改动**: 仅描述文本。

---

### O7: `grep_search` — 添加反向引导

**优化**:

```
当前: "Fast content search tool that works with any codebase size.
       Searches file contents using regular expressions.
       Returns matching lines with file paths and line numbers."

优化: "Search file contents using regex patterns. Returns matching lines with file paths and line numbers.
       Do NOT use for finding files by name — use 'glob' instead.
       Do NOT use for reading entire files — use 'read_file' instead."
```

**改动**: 仅描述文本。

---

### O8: `list_projects` — 评估存留价值

**问题**:
- 功能与 `list_dir` + VS2022 解决方案资源管理器重叠
- 对于多项目解决方案有价值，但 LLM 使用率可能很低
- OpenCode 无此工具（项目信息内置于 agent 上下文）

**方案选择**:

| 方案 | 做法 | 优点 | 缺点 |
|------|------|------|------|
| A. 保留 | 不变 | 零改动 | 工具数不减 |
| B. 降级为 System Prompt 注入 | 在 SystemPromptBuilder 中注入项目列表，删除工具 | 减少 1 个工具 | 信息不实时 |
| C. 合并到 list_dir | `list_dir` 新增 `show_projects=true` 参数 | 减少 1 个工具 | list_dir 职责不清 |

**推荐方案 B**: 项目列表是静态信息（VS 解决方案结构不会在对话中变化），注入 System Prompt 比暴露为工具更合理。工具集 16 → 15。

---

### O9: `update_plan` — 评估存留价值

**问题**:
- 描述过短（79 chars），LLM 不太理解何时使用
- v2.0 PlanManager 已精简，update_plan 主要用于多步骤任务进度追踪
- OpenCode 有 `todowrite`（类似但更丰富）

**方案选择**:

| 方案 | 做法 |
|------|------|
| A. 保留并优化描述 | 补充"何时使用"引导 |
| B. 删除，进度追踪内化到 AgentExecutor | 减少 1 个工具 |

**推荐方案 A**: 保留但优化描述：

```
优化: "Track progress of multi-step tasks. Update step status (pending/in_progress/completed)
       and add explanation of current progress.
       Use this proactively during complex tasks to show progress to the user."
```

---

### O10: `gitnexus_cypher` — 描述过长，评估精简

**问题**: 描述 2762 chars（~690 tokens），包含完整 Cypher schema 示例。占所有工具描述 token 的 ~38%。

**方案选择**:

| 方案 | 做法 | 描述长度 | token 节省 |
|------|------|---------|-----------|
| A. 保留 | 不变 | 2762 chars | 0 |
| B. 精简描述，schema 移到 MCP Resources | 描述保留摘要（~300 chars），完整 schema 通过 `gitnexus://setup` 资源注入 | ~300 chars | ~615 tokens |
| C. 移除工具 | 如果 LLM 使用率极低 | 0 chars | ~690 tokens |

**推荐方案 B**: MCP Resources 已有 `gitnexus://setup` 注入 AGENTS.md，可将 schema 放在那里。工具描述精简为：

```
"Execute Cypher query against the code knowledge graph.
 Use for advanced structural queries when gitnexus_query is insufficient.
 Refer to the GitNexus schema documentation for available node/relationship types."
```

---

### O11: 工具名别名映射 — 防止 LLM 工具名幻觉 [F1]

**问题**:

用户机器测试发现 MiniMax-M2.5 会调用不存在的工具名：
- 调用 `grep`（正确名称 `grep_search`）→ "Unknown tool: grep"
- 调用 `bash`（正确名称 `run_command`）→ "Unknown tool: bash"
- 失败后不会自动纠正，继续用错误名称重试 → 浪费 2+ 轮迭代

**根因分析**:

```
MiniMax-M2.5 训练数据偏见
  ├─ Claude Code 使用 Grep/Bash/Read/Write
  ├─ OpenCode 使用 grep/bash/read/write/edit
  └─ 行业惯例：grep, bash, cat, find, ls
       ↓
AICA 工具名: grep_search, run_command（非行业惯例）
       ↓
System Prompt 零工具名策略（v2.0 C88）— 不提具体工具名
       ↓
LLM 在 function calling API 中看到 grep_search/run_command
但训练数据偏见 > API 定义 → 回退到"记忆中的"名字
       ↓
ToolDispatcher 严格按名字查找 → Unknown tool
```

**方案选择**:

| 方案 | 做法 | 优点 | 缺点 |
|------|------|------|------|
| A. 别名映射 | ToolDispatcher 中添加 `grep`→`grep_search` 等映射 | 零风险，向后兼容，15 行改动 | 不解决根因（名字不符惯例） |
| B. 工具改名 | `grep_search`→`grep`, `run_command`→`bash` | 根治，符合行业惯例 | 影响面大：测试、文档、prompt、已有对话记忆 |
| C. A+B 组合 | 改名 + 保留旧名别名 | 根治 + 向后兼容 | 改动较大 |

**推荐方案 A**（当前阶段）: 别名映射。最小改动，立即生效。后续可评估方案 B。

**别名表**:

```csharp
// ToolDispatcher.cs — 新增静态别名映射
private static readonly Dictionary<string, string> ToolAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // LLM 训练数据中的常见工具名 → AICA 实际工具名
    ["grep"] = "grep_search",
    ["bash"] = "run_command",
    ["shell"] = "run_command",
    ["search"] = "grep_search",
    ["find"] = "glob",
    ["cat"] = "read_file",
    ["read"] = "read_file",
    ["write"] = "write_file",
    ["ls"] = "list_dir",
    ["list"] = "list_dir",
};
```

**集成点**: `ToolDispatcher.DispatchAsync()` 入口处，在查找工具之前做别名解析：

```csharp
// 在查找工具之前
if (!_tools.ContainsKey(call.Name) && ToolAliases.TryGetValue(call.Name, out var realName))
{
    Debug.WriteLine($"[AICA] Tool alias: {call.Name} → {realName}");
    call = new ToolCall { Id = call.Id, Name = realName, Arguments = call.Arguments };
}
```

**改动**: ToolDispatcher.cs ~15 行
**风险**: 极低。只在工具名查找失败时才尝试别名，不影响正常调用。

---

### O12: read_file 去重签名修复 — 允许分块读取 [F2]

**问题**:

用户机器测试发现 LLM 分块读取大文件（decoder.c ~16000 行）时，不同 offset/limit 的调用被误判为 "Duplicate call"。

**根因分析**:

```
ToolCallProcessor.GetToolCallSignature() 第 374 行：
  if (call.Name == "read_file" && (key == "offset" || key == "limit")) continue;
       ↓
  签名计算故意忽略 offset 和 limit
       ↓
  read_file("decoder.c", offset=15800, limit=250) 的签名
  = read_file("decoder.c", offset=16250, limit=100) 的签名
  = read_file("decoder.c") 的签名
  （三者签名完全相同）
       ↓
  executedToolSignatures.Add(signature) 第二次调用时返回 false
       ↓
  ShouldAllowDuplicate() 检查：
    1. 文件在 EditedFiles 中？→ 否（没编辑过）
    2. 文件在 TruncatedFiles 中 且 有 offset/limit？→ 不一定
       ┌─ 如果第一次 read 触发了 AUTO_TRUNCATED → TruncatedFiles 有记录 → 放行 ✓
       └─ 如果 LLM 一开始就用 offset 参数读（未触发 AUTO_TRUNCATED）→ TruncatedFiles 为空 → 不放行 ✗
       ↓
  第二次及后续分块读取被拦截 → "Duplicate call"
```

**设计缺陷**: 签名忽略 offset/limit 的原意是"同一文件不需要完全重复读取"，但这假设了 LLM 只会"先完整读，再按需读"。实际上 LLM 可能一开始就用 offset 分块读取（知道文件很大时）。

**修复方案**:

删除 `GetToolCallSignature` 中忽略 offset/limit 的特殊逻辑，让签名包含完整参数：

```csharp
// ToolCallProcessor.cs GetToolCallSignature()
// 删除这一行:
// if (call.Name == "read_file" && (key == "offset" || key == "limit")) continue;
```

修复后的行为：

| 调用 | 签名 | 是否去重 |
|------|------|---------|
| `read_file("decoder.c")` | `read_file\|path=decoder.c` | 首次放行 |
| `read_file("decoder.c")` | `read_file\|path=decoder.c` | 重复 → 拦截 ✓ |
| `read_file("decoder.c", offset=15800)` | `read_file\|offset=15800\|path=decoder.c` | 不同签名 → 放行 ✓ |
| `read_file("decoder.c", offset=16250)` | `read_file\|offset=16250\|path=decoder.c` | 不同签名 → 放行 ✓ |

**ShouldAllowDuplicate 是否还需要**: 仍需保留，用于 edit 后重新读取的场景（文件内容变了，相同参数的 read 应该放行）。

**改动**: ToolCallProcessor.cs 删除 1 行
**风险**: 极低。真正的重复调用（完全相同参数）仍被拦截。Doom loop 检测（连续 3 次完全相同）独立于去重，不受影响。

---

### O13: 缺失工具评估 — 是否需要补齐

| OpenCode 工具 | 功能 | AICA 是否需要 | 理由 |
|--------------|------|--------------|------|
| `batch` | 并行执行多个工具调用 | ❌ 暂不需要 | MiniMax-M2.5 已支持并行 tool_call，Agent 层面处理 |
| `apply_patch` | 多文件原子编辑 | ⚠️ 后续评估 | 复杂重构场景有价值，但实现复杂度高 |
| `multiedit` | 单文件多处编辑 | ⚠️ 后续评估 | 减少多次 edit 的迭代次数，中等价值 |
| `codesearch` | 外部语义代码搜索 | ❌ 暂不需要 | GitNexus 已覆盖语义搜索 |
| `lsp` | 语言服务协议 | ❌ 暂不需要 | 需要 VS LSP 集成，复杂度高 |
| `webfetch` / `websearch` | 网络搜索 | ❌ 暂不需要 | AICA 定位内网环境 |

---

## 四、优化总览

| 编号 | 改动 | 类型 | 工具数变化 | 改动量 |
|------|------|------|-----------|--------|
| O1 | edit 移除 full_replace | 参数删除 | 0 | ~35 行 |
| O2 | write_file 描述优化 | 描述 | 0 | ~5 行 |
| O3 | run_command 负边界 + 拦截消息修复 [C6] | 描述+修复 | 0 | ~10 行 |
| O4 | read_file 互相引导 | 描述 | 0 | ~5 行 |
| O5 | list_dir 边界声明 | 描述 | 0 | ~5 行 |
| O6 | glob 反向引导 | 描述 | 0 | ~5 行 |
| O7 | grep_search 反向引导 | 描述 | 0 | ~5 行 |
| ~~O8~~ | ~~list_projects 降级为 prompt 注入~~ | ~~工具删除~~ | ~~-1~~ | **[C1] 推迟 v2.2**（改动量 ~200+ 行，跨 5+ 文件，与本次定位不匹配） |
| O9 | update_plan 描述优化 | 描述 | 0 | ~5 行 |
| O10 | gitnexus_cypher 精简 + Phase 2 不覆盖 [C2] | 描述+逻辑 | 0 | ~15 行 |
| **O11** | **工具名别名映射 + ResolveTool 共用** [F1][C3] | **Bug 修复** | 0 | **~20 行** |
| **O12** | **read_file 去重签名修复 + 死代码注释** [F2][C4] | **Bug 修复** | 0 | **~3 行** |
| **F3** | **RunCommandTool find_by_name→glob** [C5] | **Bug 修复** | 0 | **1 行** |
| | | | **16 不变** | **~115 行** |

### 预期效果

| 指标 | 优化前 | 优化后 |
|------|--------|--------|
| 工具数 | 16 | 16（O8 推迟，不减少） |
| 描述总 token | ~1800 | ~1450（省 ~350 tokens/轮，cypher 精简贡献最大） |
| edit + write_file 歧义 | 有（两种方式创建文件） | 无（write_file 专职创建） |
| LLM 用 run_command 替代专用工具 | 可能 | 描述明确禁止 + 拦截消息修复 |
| 工具间引导 | 仅 write→edit | 6 对互相引导 |
| gitnexus_cypher 占比 | 38% 描述 token | ~10%（600 chars 截断，保留 schema 要素） |
| **LLM 工具名幻觉** [F1] | `grep`/`bash` → Unknown tool，浪费 2+ 轮 | 别名自动解析，0 轮浪费 |
| **大文件分块读取** [F2] | offset 不同仍被判 Duplicate，分块读取不可用 | 不同 offset 签名不同，正常分块 |
| **find_by_name 引导错误** [F3] | 拦截消息引导 LLM 调用不存在的工具 | 修正为 glob |

---

## 五、实施顺序（v2.0 共识版）

```
Phase 0: Bug 修复（O11 + O12 + F3）           ─── 最高优先级，修复运行时阻塞问题
  O11: ToolDispatcher 别名映射 + ResolveTool() 共用  (~20 行) [C3]
  O12: ToolCallProcessor 签名修复 + 死代码注释       (~3 行) [C4]
  F3:  RunCommandTool find_by_name→glob              (1 行) [C5]

Phase 1: 描述优化（O2-O7, O9）                ─── 仅改文本，零逻辑风险
  O2-O7: 6 个工具描述添加负边界 + 互相引导
  O3:    额外修复拦截消息中的工具名 [C6]
  O9:    update_plan 描述增强

Phase 2: edit 移除 full_replace（O1）          ─── 参数删除 + 容错引导 (~35 行)

Phase 3: gitnexus_cypher 精简（O10）           ─── 硬编码精简 + Phase 2 不覆盖 [C2] (~15 行)

Phase 4: 编译验证 + 端到端测试
```

**[C1] O8 (list_projects 降级) 推迟到 v2.2**：改动量 ~200+ 行跨 5+ 文件，需新增 SystemPromptBuilder 注入逻辑，与本次"描述优化+bug修复"定位不匹配。

### 依赖关系

```
O11, O12, F3 无依赖 ─── Phase 0 最先做
O2 无依赖
O1 依赖 O2（write_file 描述先优化好，再删 edit 的 full_replace）
O3-O7, O9 无依赖，可并行
O10 无依赖
```

---

## 六、风险

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| 删除 full_replace 后旧提示词/会话尝试使用 | 中 | 低 | ExecuteAsync 检测到 full_replace 参数时返回引导信息 |
| 描述负边界过于严格，限制 LLM 合理使用 | 低 | 中 | "Do NOT" 语气用于明确场景，不用于模糊场景 |
| list_projects 降级后 LLM 无法获取实时项目结构 | 低 | 低 | System Prompt 在每次对话开始时注入最新项目列表 |
| gitnexus_cypher 描述精简后 LLM 写不出正确 Cypher | 中 | 中 | schema 通过 MCP Resources 注入，LLM 仍可访问 |
| 别名映射导致 LLM 学不到正确工具名 [F1] | 低 | 低 | 别名仅在查找失败时触发，不改变 API 暴露的正式名称 |
| 去重修复后 LLM 对同一文件发起大量不同 offset 的读取 | 低 | 低 | Doom loop 检测（连续 3 次完全相同）仍有效；max_iterations 兜底 |

---

## 七、已知问题

### K1: LLM 调用不存在的 gitnexus_list_repos 工具

**现象**: LLM 声称有 gitnexus_list_repos 工具（自述 17 个），但 debug 日志显示 Tools count: 16，实际调用时报 Unknown tool。

**三层原因**:

1. **LLM 命名模式推断**：看到 6 个 `gitnexus_*` 工具，推断"应该有 list_repos"
2. **MCP Resources 文档提及**：`gitnexus://setup` 注入的 AGENTS.md 可能提到 list_repos 功能，LLM 从文档中读到工具名
3. **MCP Server 暴露但被 AICA 过滤**：`BuildToolsFromNativeDefinitions` 只遍历硬编码的 6 个 toolSpecs，MCP Server 返回的第 7 个工具 `list_repos` 被静默跳过

**验证方法**:
- Output 窗口搜索 `ListToolsAsync returned` 确认 MCP server 返回工具数
- 搜索 `No native definition for` 确认是否有 list_repos 被跳过
- 检查 `gitnexus://setup` 内容是否提及 list_repos

**解决方向**（待 v2.2 评估）:
- A. 在 toolSpecs 中新增 list_repos 条目（如果有用）
- B. O11 别名映射到 list_projects（如果功能重叠）
- C. 修改 BuildToolsFromNativeDefinitions 动态注册所有 MCP 工具（根治方案）

---

## 八、后续评估（v2.2 范围）

| 工具 | 评估时机 | 条件 |
|------|---------|------|
| **O8 list_projects 降级** [C1] | v2.2 | 改动量 ~200+ 行跨 5+ 文件，需新增 SystemPromptBuilder 注入逻辑 |
| `multiedit` | v2.2 | 如果 LLM 频繁对同一文件连续调用 edit（>3 次/文件） |
| `apply_patch` | v2.2 | 如果复杂重构场景的多文件编辑失败率高 |
| `ask_followup_question` 参数精简 | v2.2 | 如果 LLM 构造 options 参数频繁出错 |
| O12 TruncatedFiles 死代码清理 [C4] | v2.2 | 已标注注释，确认无副作用后清理 |
