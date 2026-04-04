# AICA v2.1 改进分析：基于 OpenCode 对比（修正版）

> 分析日期：2026-04-04（修正版 v2）
> 对比项目：AICA v2.12.0 (C#/VS2022) vs OpenCode (TypeScript/Bun)
> 约束条件：VS2022 扩展、公司内网环境、MiniMax-M2.5 单模型
> 方法：双 Agent 协作（分析师 + 审查员），两轮交叉验证
> 参考文档：[AICA_vs_OpenCode_Gap_Analysis.md](../v2.0plan/AICA_vs_OpenCode_Gap_Analysis.md)（v2.0~v2.12.0 完整进展记录）

---

## 一、AICA 已有优势（无需改进）

审查员独立验证后，以下领域 AICA 已优于或等价于 OpenCode：

| 功能 | AICA 实现 | OpenCode 对比 |
|------|----------|--------------|
| **Doom Loop 检测** | 3 连续同签名 → 强制 `ask_followup_question`（`AgentExecutor.cs`） | 源码中未找到等价实现 |
| **跨会话项目记忆** | `MemoryBank`（`.aica/memory/*.md`，注入系统提示，4000 字符限制） | 无对应功能 |
| **任务断点续做** | `TaskProgressStore`（`.aica/progress/latest.json`，含编辑文件列表/计划状态/关键发现） | 无显式断点机制 |
| **编辑失败诊断** | `DiagnoseEditFailure()`：陈旧内容检测 + 首行锚点提示 | 无 |
| **外部修改检测** | `FileTimeTracker`：编辑前检查文件是否被外部修改 | 无 |
| **路径安全** | `SafetyGuard` + `.aicaignore` + 硬编码保护路径 + 命令黑白名单 | 仅 permission rules |
| **VS 诊断集成** | 编辑后轮询 Error List + `validate_file` 独立工具（v2.3） | 无 IDE 集成（CLI 工具） |
| **按意图过滤工具集** | `DynamicToolSelector` + `ToolGroup` flags（v2.5，节省 2-4K tokens/请求） | 无 |
| **Token 精确计量** | 流式 `stream_options.include_usage` + EMA 校准 + ratio clamp [0.3, 3.0]（v2.5） | 类似（从 response.usage 读取） |
| **编辑交互体验** | Diff 预览 + 用户手动修改检测 + 三模式（单/多编辑/多文件） | 无交互确认，直接应用 |

### 两者各有所长的领域

| 功能 | AICA | OpenCode | 评价 |
|------|------|----------|------|
| **编辑模糊匹配** | 6 级（精确→行尾→缩进→空白→Unicode→注释剥离） | **9 级**（+BlockAnchor/EscapeNormalized/TrimmedBoundary/ContextAware/MultiOccurrence） | OpenCode 更深，但 AICA 的交互确认弥补了匹配精度不足 |
| **计划 Agent** | `PlanAgent`（只读工具 mini loop，16K budget，60s 超时，失败静默降级） | `Plan Agent`（完整 Agent 配置，独立权限规则，deny 所有编辑工具） | 功能等价，实现方式不同 |
| **对话压缩** | LLM 摘要 + 程序化兜底（`BuildAutoCondenseSummary`） + 任务边界感知（`[TASK_BOUNDARY]`） | LLM 摘要 + 重放/合成消息兜底 + 独立 prune() 修剪旧工具输出 | AICA 更 resilient（双兜底），OpenCode 修剪策略更精细（独立 prune） |

---

## 二、确认的真实差距（按优先级排列）

### 高优先级

#### H1. 工具输出持久化（截断存文件）

**差距性质**：架构级差距 — AICA 截断后丢弃完整数据，Agent 无法后续访问

**OpenCode 实现**（`packages/opencode/src/tool/truncate.ts`）：
- 集中式 `Truncate` 服务，所有工具输出统一经过
- 阈值：2000 行或 50KB
- 超限时：完整输出写入 `.opencode/truncations/tool_{timestamp}.txt`
- 对话中保留预览 + 文件路径 + 访问提示（"use read_file with offset/limit"）
- 根据 Agent 权限动态调整提示（有 task 权限则建议用 Task 工具）
- 7 天自动清理

**AICA 现状**：
- 各工具独立截断：ReadFile 500→200 行、Grep 200 条、ListDir 800 项、RunCommand 6KB stdout/3KB stderr
- 截断后完整输出**直接丢弃**，Agent 后续轮次无法获取
- 无集中截断服务，调整阈值需逐个工具修改

**对 MiniMax-M2.5 环境的价值**：**极高**
- MiniMax 上下文窗口有限（177K），截断不可避免
- Grep 返回 200 条后截断，第 201 条可能是关键信息 → Agent 卡住
- 存文件 + 引用路径让 Agent 可以用 `read_file` 按需查看，实现"分解式输出处理"

**实现建议**：
```
新增 ToolOutputPersistenceManager (集中式服务)
├─ 存储位置：~/.AICA/truncations/
├─ 命名：tool_{yyyyMMddHHmmssfff}.txt
├─ 各工具截断时调用 PersistAndTruncate() 而非直接丢弃
├─ 返回：preview + 文件路径 + 访问提示
├─ 后台清理：7 天过期
└─ 随此引入集中截断阈值配置（替代各工具硬编码）
```

**工作量估计**：2-3 周（含各工具接入 + 集中式阈值配置）

---

#### H2. 文件级快照与回滚（Snapshot & Revert）

**差距性质**：安全保障级差距 — **代码验证确认 AICA 无任何快照/回滚实现**

**OpenCode 实现**（`packages/opencode/src/snapshot/index.ts`）：
- 基于 git tree objects 的文件快照
- 存储：`~/.opencode/data/snapshot/{projectId}/{worktreeHash}/`（独立 git 仓库）
- 每个 Agent 步骤前 `track()` 记录当前树 hash
- 步骤后 `patch(hash)` 获取变更文件列表
- `restore(snapshot)` 回滚到任意快照点
- `revert(patches)` 逐文件选择性回滚
- 2MB 文件大小限制，自动排除大文件
- 并发控制：Semaphore 保护 git 操作
- 7 天自动清理（`git gc`）

**AICA 现状**：
- `FileTimeTracker`：仅追踪时间戳 + 文件大小，不保存文件内容
- `TaskProgressStore`：保存任务进度元数据（编辑文件列表、计划状态），不保存文件快照
- EditFileTool：diff 预览阶段有临时 `.backup` 文件，确认后**立即删除**
- **无任意消息点的文件状态回滚能力**（已通过 grep snapshot/rollback/revert 确认）

**对 MiniMax-M2.5 环境的价值**：**极高**
- MiniMax-M2.5 相比前沿模型更容易产生错误编辑
- Agent 连续修改 5 个文件后发现第 2 个改错了 → 目前只能手动 `git checkout`
- 快照是 Agent 编辑操作的"安全网"
- 与现有 `TaskProgressStore` 结合可实现"恢复到上次断点状态后继续"

**实现建议**：
```
方案 A（轻量级，推荐）：
├─ 编辑前：将原始文件内容复制到 ~/.AICA/snapshots/{sessionId}/{stepIndex}/{relativePath}
├─ 回滚 API：SnapshotManager.RestoreAsync(sessionId, stepIndex)
├─ UI：VS 工具栏按钮"回滚到步骤 N"
├─ 清理：会话结束后保留 7 天
└─ 优点：不依赖 git，简单可靠，零新依赖

方案 B（git-based，功能更强，后续升级路径）：
├─ 在 ~/.AICA/snapshots/ 初始化独立 git 仓库
├─ 每步 git add + git write-tree
├─ 回滚：git read-tree + checkout-index
└─ 优点：支持 diff 查看、空间效率高（git 去重）
```

**工作量估计**：方案 A 2-3 周，方案 B 3-4 周

---

#### H3. 权限拒绝反馈注入 + 决策持久化

**差距性质**：用户体验 + Agent 智能

**OpenCode 实现**（`packages/opencode/src/permission/index.ts`）：
- `CorrectedError`：用户拒绝时可附带反馈文字
  ```typescript
  class CorrectedError extends Error {
    constructor(public feedback: string) {
      super(`The user rejected this tool call with feedback: ${feedback}`);
    }
  }
  ```
- 反馈直接注入 Agent 对话上下文，LLM 能理解为什么被拒绝并调整策略
- `approved` 数组持久存储在 SQLite 数据库，跨会话保留
- 支持"始终允许"和"始终拒绝"的持久化决策

**AICA 现状**：
- `SafetyGuard` 返回 `PathAccessResult.Reason`（拒绝原因字符串）
- 权限规则引擎已完善（v2.3 PermissionRuleEngine：glob + action + 三级控制）
- **缺失 1**：用户拒绝时无反馈输入通道 → LLM 收到的只是 "Permission denied"
- **缺失 2**：用户的"始终允许/拒绝"决策不跨会话保存，每次重启重置

**对 MiniMax-M2.5 环境的价值**：**中-高**
- MiniMax 推理能力有限 → 明确告知"为什么被拒绝"比让它自己猜有效得多
- 内网环境用户固定，权限偏好稳定 → 持久化减少重复确认

**实现建议**：
```
1. 拒绝反馈注入（1 周）：
   ├─ ToolExecutionPipeline：用户拒绝时弹出反馈输入框（可选，可留空）
   ├─ 反馈包装为 ToolResult.Error("Permission denied. User feedback: {feedback}")
   └─ 自然进入 Agent 对话上下文

2. 权限决策持久化（1 周）：
   ├─ 存储位置：~/.AICA/permissions.json
   ├─ 数据：{ tool, pattern, decision: "always_allow"|"always_deny", timestamp }
   └─ SafetyGuard 启动时加载，优先于默认规则
```

**工作量估计**：2 周

---

### 中优先级

#### M1. Prune 时机前移（修剪作为压缩前置步骤）

**差距性质**：压缩策略精细度 — 功能已有，执行时机需调整

**OpenCode 实现**（`packages/opencode/src/session/compaction.ts`）：
- `prune()` 和 `process()` 是**两个独立机制**：
  - `prune()`：低成本，仅删除旧 tool outputs（保护最近 2 个 user turns + skill 调用）
  - `process()`：高成本，调用 LLM 生成摘要
- 溢出时**先 prune，不够再 compact**（分层策略）

**AICA 现状**（`AgentExecutor.cs`）：
- `PruneOldToolOutputs` **已存在**（protectRecentTurns=2, protectTokens=40K, minPruneTokens=20K）
- 但执行时机在**主循环结束后**（"Runs AFTER the loop — LLM never sees pruned results during active work"）
- 压缩触发时直接走 LLM 摘要，没有"先修剪再判断是否仍需摘要"的分层策略

**对 MiniMax-M2.5 环境的价值**：**中**
- 修剪旧工具输出是零成本操作（不消耗 LLM 调用）
- 在 MiniMax 177K 上下文下，先修剪可能就够了，避免不必要的 LLM 压缩
- 减少 LLM 压缩频率 = 减少延迟和 token 消耗

**实现建议**：
```
AgentExecutor 压缩触发逻辑中（仅调整调用顺序）：
1. 压缩条件达到时，先调用 PruneOldToolOutputs（已有函数）
2. 修剪后重新估算 token
3. 如果仍超阈值，再调用 ConversationCompactor
改动量极小：~15 行调用顺序调整
```

**工作量估计**：0.5 周

---

#### M2. 会话分叉（Session Fork）

**差距性质**：探索性工作流

**OpenCode 实现**：
- 可在任意消息点 fork 出子会话
- 子会话继承父会话的上下文到 fork 点

**AICA 现状**：
- 会话持久化完整（`ConversationStorage` + `ConversationIndexCache`），但只有线性历史

**对 MiniMax-M2.5 环境的价值**：**中**
- 单模型环境下试错成本高（上下文宝贵）
- Fork 让用户"保存当前进度，探索另一个方向，不行就切回来"

**实现建议**：
```
ConversationStorage.ForkAsync(conversationId, forkAtMessageIndex):
├─ 复制 ConversationRecord 到 forkAtMessageIndex
├─ 生成新 ID，标记 ParentId + ForkIndex
└─ UI 侧边栏显示 fork 关系
```

**工作量估计**：2 周

---

#### M3. 编辑后自动格式化

**差距性质**：代码质量保障

**OpenCode 实现**（`packages/opencode/src/tool/edit.ts`）：
- 编辑后自动运行格式化器

**AICA 现状**：
- 编辑后有 VS Error List 诊断，但**无自动格式化**
- 旧文档标记为"未计划"

**对 MiniMax-M2.5 环境的价值**：**中**
- MiniMax 输出的代码缩进/格式可能不一致
- 调用 VS 的格式化命令可弥补模型输出质量不足
- AICA 已在 VS2022 中运行，调用 DTE 格式化 API 成本低

**实现建议**：
```
EditFileTool 编辑成功后：
├─ 调用 DTE.ExecuteCommand("Edit.FormatDocument") 或等价 API
├─ 可配置开关（config.json: tools.autoFormatAfterEdit: true/false）
└─ 仅对支持的语言生效（C/C++/C# 等有格式化器的语言）
```

**工作量估计**：1 周

---

### 低优先级

#### L1. 工作树隔离（Worktree Isolation）

**OpenCode** 为每个会话创建隔离 git worktree。
**价值**：单用户场景下并发风险低。优先实现 H2 快照系统，worktree 作为后续增强。

#### L2. 多模型提供商抽象

**现状**：AICA 紧耦合 MiniMax-M2.5。
**价值**：当前低，内网新增模型时变为必需。预留 `IModelProvider` 接口即可。

#### L3. 子 Agent 体系扩展（ExploreAgent 等）

**现状**：仅 PlanAgent。可用不同 system prompt + 工具子集创建轻量子 Agent。
**价值**：低。核心功能稳定后考虑。

#### L4. 编辑模糊匹配增强（6 级→更多）

**现状**：AICA 6 级 vs OpenCode 9 级。差距存在但 AICA 的交互确认弥补了匹配精度不足。
**价值**：低。旧文档标记为"v2.2 观察匹配分布后评估"，可通过 telemetry 数据驱动决策。

---

## 三、实施路线图

### Phase 1：核心安全网（4-5 周）

| 编号 | 任务 | 依赖 | 工作量 |
|------|------|------|--------|
| H1 | 工具输出持久化（截断存文件 + 集中式服务） | 无 | 2-3 周 |
| H3 | 权限拒绝反馈注入 + 决策持久化 | 无 | 2 周 |

> H1 和 H3 可并行开发。

### Phase 2：文件安全保障（3-4 周）

| 编号 | 任务 | 依赖 | 工作量 |
|------|------|------|--------|
| H2 | 文件快照与回滚系统（方案 A） | 无 | 2-3 周 |
| M1 | 独立 Prune 机制 | 无 | 1 周 |

> H2 和 M1 可并行。

### Phase 3：体验优化（3 周）

| 编号 | 任务 | 依赖 | 工作量 |
|------|------|------|--------|
| M2 | 会话分叉 | 无 | 2 周 |
| M3 | 编辑后自动格式化 | 无 | 1 周 |

### Phase 4：可选增强（按需）

| 编号 | 任务 | 触发条件 |
|------|------|---------|
| L1 | 工作树隔离 | H2 完成后 |
| L2 | 多模型抽象 | 内网新增模型时 |
| L4 | 匹配级别扩展 | telemetry 数据显示 6 级不够时 |

---

## 四、关键决策记录

1. **不引入数据库**：JSON + `ConversationIndexCache` 在单机 VS 场景足够（v2.2 步骤 7 已验证）。

2. **快照选方案 A**（文件复制）：不引入 libgit2 依赖，简单可靠。git-based 方案作为后续升级路径。

3. **不扩展匹配级别**：AICA 6 级 + 交互确认 vs OpenCode 9 级无确认 — 用户体验不同但各有权衡。等 telemetry 显示匹配失败分布后再决策。

4. **截断阈值保持现状**：AICA 各工具的截断值（200 行/200 条/800 项）在 MiniMax 177K 上下文下合理。OpenCode 的 2000 行/50KB 阈值更大但其目标模型上下文也更大。关键改进是**截断后存文件**而非调阈值。

5. **压缩策略小幅增强**：不重写压缩系统（已优于 OpenCode），仅增加独立 prune 步骤作为前置过滤。

6. **Token 追踪不再列为改进项**：v2.5 已实现流式 usage 读取 + EMA 校准。当前系统已具备精确 token 感知能力。

---

## 五、纠错记录（两轮审查）

### 第一轮审查纠正（分析师 vs 审查员）

| 原始判断 | 纠正 | 原因 |
|----------|------|------|
| "AICA 无 LSP/诊断集成" | 已有 VS Error List 轮询 + `validate_file` 工具 | `VSAgentContext.GetDiagnosticsAsync()` |
| "AICA 权限系统粗粒度" | 已有 Allow/Ask/Deny 三级 + glob 匹配 + 命令黑白名单 | `SafetyGuard.cs` + `PermissionRuleEngine` |
| "AICA 对话压缩简单" | 已有任务边界感知、重压缩间隔保护、程序化兜底 | `TokenBudgetManager.cs` |
| "需要迁移到数据库" | JSON + IndexCache 在单机场景足够 | `ConversationIndexCache.cs` |

### 第二轮交叉验证纠正（对照旧文档 + 源码）

| 原始判断 | 纠正 | 验证来源 |
|----------|------|---------|
| "OpenCode 编辑仅两阶段 diff 匹配" | **9 级级联匹配**（Simple→LineTrimmed→BlockAnchor→WhitespaceNormalized→IndentationFlexible→EscapeNormalized→TrimmedBoundary→ContextAware→MultiOccurrence） | `opencode/src/tool/edit.ts` 第 196-646 行 |
| "M1 Token 追踪未实现" | **v2.5 已实现**：`OpenAIClient.cs` 读取 `stream_options.include_usage` + `ContextManager.CalibrateFromUsage()` EMA 校准 | 旧文档第十五章 + 源码确认 |
| "OpenCode 无等价规划阶段" | **有 Plan Agent**（只读权限，deny 所有编辑工具） | `opencode/src/agent/agent.ts` 第 122-140 行 |
| "OpenCode 压缩失败则停止" | **有重放/合成兜底**：失败时根据 overflow 状态决定重放历史或生成 continue 消息 | `opencode/src/session/compaction.ts` 第 243-297 行 |
| "Prompt 缓存 v2.5 已完成" | **代码中不存在**（仅 static/dynamic StringBuilder 分离，无 `cache_control` 实现） | `SystemPromptBuilder.cs` 源码验证 |

---

## 六、与旧文档的关系

本文档是 [AICA_vs_OpenCode_Gap_Analysis.md](../v2.0plan/AICA_vs_OpenCode_Gap_Analysis.md) 的**补充**，聚焦于 v2.12.0 之后仍存在的差距。

旧文档中的进展状态：
- 第一梯队（v2.1）：✅ 全部完成（13/13 项）
- 第二梯队（v2.2-v2.3）：✅ 全部完成
- 第三梯队（v2.5）：✅ 全部完成
- **第四梯队剩余项**：快照回滚（📋 待办）、会话标题（搁置）、多模型适配（搁置）、Git 集成（搁置）

本文档新增的改进点：
- **H1 工具输出持久化** — 旧文档未涉及
- **H3 权限反馈注入** — 旧文档未涉及
- **M1 独立 Prune 机制** — 旧文档未涉及
- **M2 会话分叉** — 旧文档未涉及
- **M3 编辑后自动格式化** — 旧文档标记"未计划"，本文档建议提升优先级
