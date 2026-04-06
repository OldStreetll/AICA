# AICA v2.1 统一实施方案

> **版本**：v1.0
> **日期**：2026-04-06
> **方法**：整合三份规划文档 + Harness Engineering 七课学习成果
> **约束**：单人开发、VS2022 VSIX、MiniMax-M2.5 单模型、涉密离线环境
> **核心策略**：弱模型 + 强系统

---

## 一、方案来源与整合说明

本方案整合以下四个来源：

| 来源 | 覆盖范围 | 本方案中的体现 |
|------|---------|-------------|
| [v2.1 改进分析](./AICA_v2.1_Improvement_Analysis.md) | H1-H3, M1-M3 补短板 | 全部纳入 |
| [v2.1 优化路线图](./AICA_v2.1_Optimization_Roadmap.md) | S1-S5 扬长板 | 全部纳入，S5 与 Skills 合并 |
| [OpenHarness 架构分析](./AICA_OpenHarness_Analysis.md) | Skills/Memory/Hooks/SubAgent/Plugin | OH1 与 S5 合并，OH4 Plugin 搁置 |
| [Harness Engineering 学习笔记](../harness-study/README.md) | Telemetry/评估标准/假设清单/PlanAgent 优化 | 融入各组件，非独立排期 |

### 关键合并决策

| 决策 | 内容 | 理由 |
|------|------|------|
| **S5 + OH1 合并** | 任务模板化通过 Skills 系统实现（.md 文件驱动），不硬编码 | 避免先硬编码再外部化的重复工作 |
| **DynamicToolSelector 折中** | 保留 `ClassifyIntent()` 意图分类，去掉工具过滤 | 意图分类用于条件触发和 telemetry；工具过滤与信任型设计矛盾 |
| **H2 采用方案 A** | 文件复制到 `~/.AICA/snapshots/`，不引入 git 依赖 | 单人开发优先降低复杂度 |
| **M3 采用方案 A** | EditFileTool 直接调用 DTE 格式化命令 | 快速落地，后续可迁移为 Hook |

### 搁置项

| 项目 | 搁置理由 | 重新评估条件 |
|------|---------|------------|
| OH4 Plugin 系统 | Skills + Memory + Hooks 尚未落地 | 前三者稳定运行后 |
| L1 工作树隔离 | 单用户并发风险低 | H2 快照完成后 |
| L2 多模型抽象 | 当前仅 MiniMax-M2.5 | 内网新增模型时 |
| L4 匹配级别扩展 | 6 级 + 交互确认已够用 | telemetry 显示匹配失败率高时 |
| M2 会话分叉 | 优先级最低的代码项，UI 工作量大 | Phase 1-5 完成后评估 |
| L3 子 Agent 体系扩展 | ExploreAgent/TestAgent/SecurityAgent 等，核心功能稳定后考虑 | SubAgent 泛化完成且稳定运行后 |

---

## 二、全量任务清单

### 18 个任务项总览

| ID | 名称 | 类型 | 工作量 | 阶段 |
|----|------|------|--------|------|
| M1 | Prune 时机前移 | 补短板 | 2-3 天 | Phase 1 |
| M3 | 编辑后自动格式化 | 补短板 | 3-5 天 | Phase 1 |
| SK | Skills + 任务模板（S5+OH1 合并） | 架构升级 | 5-8 天 | Phase 1 |
| H1 | 工具输出持久化 | 补短板 | 10-15 天 | Phase 2 |
| OH2 | 结构化记忆升级 | 架构升级 | 5-8 天 | Phase 3 |
| H3 | 权限反馈注入 + 决策持久化 | 补短板 | 10 天 | Phase 3 |
| H2 | 文件快照与回滚 | 补短板 | 10-15 天 | Phase 4 |
| OH5 | SubAgent 泛化 + ReviewAgent | 架构升级 | 5-8 天 | Phase 4 |
| S3 | 头文件同步感知 | 扬长板 | 3-5 天 | Phase 4 |
| PA1 | PlanAgent 输出优化 | Harness 新增 | 2-3 天 | Phase 5 |
| OH3 | Hooks 钩子系统 | 架构升级 | 5-8 天 | Phase 5 |
| S1 | 符号检索增强 | 扬长板 | 5-8 天 | Phase 6 |
| S4 | GitNexus 主动触发 | 扬长板 | 4-6 天 | Phase 6 |
| S2 | 编辑后自动构建 | 扬长板 | 4-5 天 | Phase 6 |
| T2 | Telemetry 会话摘要 | Harness 新增 | 2-3 天 | Phase 6 |

**横切项**（融入各任务，非独立排期）：

| ID | 名称 | 额外工作量 | 说明 |
|----|------|----------|------|
| T1 | Telemetry 结构化日志 | 每组件 +0.5 天 | 在 Monitoring 中间件和各组件中埋点 |
| RC1 | ReviewAgent 检查清单设计 | 含在 OH5 内 | 5 个防守维度的 Prompt 设计 |
| AS1 | 假设记录 | 每组件 +0.25 天 | 文档级，记录假设/验证方式/失效信号 |

---

## 三、依赖关系与 EditFileTool 修改排序

### 依赖图

```
无依赖（可独立开始）：
  M1, M3, SK(S5+OH1), H1, H2, H3, OH2, OH5, S3

有依赖：
  OH3 Agent Hook → OH5 SubAgent（需要 ReviewAgent）
  PA1 PlanAgent 优化 → OH5 SubAgent（需要 SubAgent 基类重构后再改 PlanAgent）
  S1 符号检索 → SK（需要意图分类）
  S4 Impact 分析 → SK（需要意图分类）
  S2 后台构建 → H1（复用截断基础设施）
  T2 会话摘要 → T1 结构化日志（需要日志数据）

接口约束：
  S1 不应破坏 S3 已使用的符号数据接口（SymbolRecord 的 FilePath/Signature/Namespace/Name）
```

### EditFileTool 修改排序（7 项）

EditFileTool 是修改最密集的文件（7 项触及），必须严格排序以避免冲突：

```
EditFileTool 执行流程中的位置：

  ┌─ [编辑前] ─────────────────────────┐
  │  ① H2 快照：复制原始文件到快照目录    │  ← Phase 4
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑应用] ──────────────────────┐
  │  （现有逻辑：diff 预览→用户确认→写入）│
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑后，同步链] ────────────────────┐
  │  ② M3 格式化：DTE.FormatDocument       │  ← Phase 1
  │  ③ S3 头文件同步：检测签名变化→追加警告 │  ← Phase 4
  │  ④ S4 Impact：条件触发→追加预警        │  ← Phase 6
  │  ⑤ H1 截断持久化：输出超限→存文件      │  ← Phase 2
  │  （现有逻辑：AppendDiagnosticsAsync）   │
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑后，异步] ─────────────────────┐
  │  ⑥ S2 后台构建：fire-and-forget       │  ← Phase 6
  └────────────────────────────────────┘
           ↓
  ┌─ [贯穿] ────────────────────────────┐
  │  ⑦ T1 Telemetry：日志记录            │  ← 随各项加入
  └────────────────────────────────────┘
```

**排序原则**：
- M3（格式化）必须在 S3（头文件同步）之前——同步检测应基于格式化后的代码
- S3、S4 追加到 ToolResult，在 H1 截断判断之前——先生成完整输出，再决定是否截断
- S2 是异步的，不影响同步返回
- H2（快照）是唯一的编辑前操作，与其他项无冲突

**⚠️ 跨 Phase 集成提醒**：
- H1 在 Phase 2 先上线，此时截断判断在 EditFileTool 末尾。当 S3（Phase 4）和 S4（Phase 6）上线时，**必须将 S3/S4 的追加逻辑插入到 H1 截断判断之前**，确保先生成完整输出（含同步警告和 impact 预警），再决定是否截断。

---

## 四、分阶段实施计划

### Phase 1：快速收益（第 1-2 周）

**目标**：用最小改动获得立竿见影的效果。

#### M1：Prune 时机前移（2-3 天）

| 项 | 内容 |
|----|------|
| **改什么** | `AgentExecutor.cs`，压缩触发逻辑中 ~15 行调用顺序调整 |
| **怎么改** | 压缩条件达到 → 先调 `PruneOldToolOutputs`（已有函数）→ 重估 token → 仍超阈值才调 `ConversationCompactor` |
| **假设** | 修剪旧工具输出能释放足够 token，避免部分 LLM 压缩 |
| **失效信号** | Prune 后仍需 Compaction 的比率 >90%（说明工具输出占比太小，Prune 无效） |
| **Telemetry 埋点** | 记录 prune_tokens_freed、compaction_avoided（bool） |
| **验收标准** | 压缩触发时先 Prune 后判断，日志可见 Prune 释放量 |

#### M3：编辑后自动格式化（3-5 天）

| 项 | 内容 |
|----|------|
| **改什么** | `EditFileTool.cs`，编辑成功后新增格式化调用 |
| **怎么改** | 编辑写入后调 `DTE.ExecuteCommand("Edit.FormatDocument")`；加配置开关 `tools.autoFormatAfterEdit: true/false`；仅对有格式化器的语言生效 |
| **假设** | MiniMax 输出格式经常不一致，VS 格式化器能修复 |
| **失效信号** | 格式化前后 diff 为空的比率 >90%（说明模型输出格式已足够好） |
| **Telemetry 埋点** | 记录 format_changed（bool）、format_duration_ms |
| **验收标准** | C/C++ 文件编辑后自动格式化，可通过配置关闭 |

#### SK：Skills + 任务模板（5-8 天）

| 项 | 内容 |
|----|------|
| **改什么** | `Rule.cs`、`RuleLoader.cs`、`RuleEvaluator.cs`、`SystemPromptBuilder.cs`、`DynamicToolSelector.cs`；新增 `SkillTool.cs`、`.aica-rules/*.md` |
| **步骤** | 1. `Rule.cs` 增加 `Description` 字段 |
|  | 2. `RuleLoader.cs` 显式提取 frontmatter 中的 name/description |
|  | 3. `RuleEvaluator.cs` 增加 paths glob 匹配逻辑 |
|  | 4. 新增 `SkillTool.cs`（~60-80 行，按 name 查找 .md 返回内容） |
|  | 5. 将 `AddBugFixGuidance()`、`AddQtTemplateGuidance()` 外部化为 .md 文件 |
|  | 6. 新增 4 个任务模板技能文件：`bug-fix.md`、`feature-add.md`、`refactor.md`、`test-write.md` |
|  | 7. 每个模板包含**成功标准**字段（轻量版 Sprint 契约） |
|  | 8. `DynamicToolSelector.cs` 去掉工具过滤，保留 `ClassifyIntent()`。**注意：此步骤前先在 telemetry 中记录一段"去掉过滤前"的工具调用基线数据（至少 1-2 周），去掉过滤后对比幻觉率变化。如果幻觉调用不相关工具的频率显著上升，回退此改动。** |
| **技能文件格式** | 见下方 |
| **假设** | 模板指导能改善 MiniMax 的工具调用序列 |
| **失效信号** | 注入模板前后任务完成率无显著差异 |
| **Telemetry 埋点** | 记录 skill_injected（name）、skills_matched_count |
| **验收标准** | `.aica-rules/` 中的 .md 文件可被自动加载注入；SkillTool 可被 LLM 调用 |

**技能文件格式示例**：

```markdown
---
name: bug-fix
description: Bug修复的结构化工作流，含诊断、修复和验证步骤
paths:
  - "*.cpp"
  - "*.h"
  - "*.c"
priority: 20
---

## Bug 修复工作流

### 步骤
1. **诊断**：使用 grep/read_file 定位问题代码，理解上下文
2. **修复**：编辑文件修复问题，每次只改一个文件
3. **验证**：确认编译通过，检查是否引入新问题

### 成功标准
- [ ] 编译通过（零新增错误）
- [ ] 原始问题不再复现
- [ ] 未修改与问题无关的代码
```

#### Phase 1 交付物

- Prune 前移到压缩触发前（先免费后昂贵）
- 编辑后自动格式化（可配置开关）
- Skills 系统上线 + 4 个任务模板 + SkillTool
- 硬编码 guidance 外部化为 .md 文件
- DynamicToolSelector 工具过滤移除

---

### Phase 2：截断持久化（第 3-5 周）

**目标**：解决工具输出截断后数据丢失的问题。

#### H1：工具输出持久化（10-15 天）

| 项 | 内容 |
|----|------|
| **新增** | `ToolOutputPersistenceManager.cs`（集中式服务） |
| **改什么** | 所有工具文件的截断逻辑（ReadFile、Grep、ListDir、RunCommand、EditFileTool 等） |
| **步骤** | 1. 新增 `ToolOutputPersistenceManager`：存储位置 `~/.AICA/truncations/`，命名 `tool_{yyyyMMddHHmmssfff}.txt` |
|  | 2. 新增 `PersistAndTruncate()` 方法：判断是否超限（集中式阈值配置）→ 超限则存完整输出到文件 → 返回预览 + 文件路径 + 访问提示 |
|  | 3. 逐个工具接入：替换各工具独立的截断逻辑为 `PersistAndTruncate()` 调用 |
|  | 4. 访问提示根据 Agent 能力动态生成（"use read_file with offset/limit to see full output"） |
|  | 5. 后台清理：7 天过期自动删除 |
|  | 6. 集中式阈值配置：替代各工具硬编码的截断值 |
| **假设** | Agent 会回头查看被截断的完整输出 |
| **失效信号** | Agent 对截断文件的 `read_file` 调用频率接近 0 |
| **Telemetry 埋点** | 记录 truncation_persisted（bool）、truncation_file_size、truncation_file_read_count |
| **验收标准** | 所有工具截断后完整输出可通过 read_file 访问；7 天自动清理 |

#### Phase 2 交付物

- 集中式截断服务，所有工具统一接入
- 截断后存文件 + 引用路径 + 访问提示
- 集中式截断阈值配置
- 与 M1 Prune 形成组合：Prune 删除的旧工具输出 + H1 持久化 = 放心剪，随时捡回来

---

### Phase 3：记忆升级 + 权限体系（第 6-8 周）

**目标**：精准注入相关记忆，权限交互更智能。

#### OH2：结构化记忆升级（5-8 天）

| 项 | 内容 |
|----|------|
| **改什么** | `MemoryBank.cs` 核心重写 |
| **步骤** | 1. 记忆文件加 YAML frontmatter（name/description/type，复用 `YamlFrontmatterParser`） |
|  | 2. 分 4 类：user / feedback / project / reference |
|  | 3. 实现相关性检索逻辑（~100-150 行）：英文按单词（3+字符）+ 中文按单字（去停用词）；description 命中 2x 权重 + body 命中 1x；取 top N（N 根据 token 预算动态决定）。**第一版用按字分词 + 基础停用词表，后续迭代优化分词精度。** |
|  | 4. 替换全量拼接 + 4000 字符硬截断 |
| **假设** | 相关性检索注入的记忆比全量拼接更精准、更省 token |
| **失效信号** | 用户反馈"需要的记忆没被注入"的频率上升 |
| **Telemetry 埋点** | 记录 memories_total、memories_injected、memory_tokens_used |
| **验收标准** | 记忆按相关性检索注入；消除 4000 字符硬截断；节省 ~300-500 tokens/请求 |

#### H3：权限反馈注入 + 决策持久化（10 天）

| 项 | 内容 |
|----|------|
| **改什么** | `ToolExecutionPipeline.cs`、`SafetyGuard.cs`、VS UI |
| **步骤** | **Part 1 反馈注入（5 天）**：|
|  | 1. 用户拒绝工具调用时弹出可选反馈输入框 |
|  | 2. 反馈包装为 `ToolResult.Error("Permission denied. User feedback: {feedback}")` |
|  | 3. 自然进入 Agent 对话上下文，LLM 理解拒绝原因 |
|  | **Part 2 决策持久化（5 天）**： |
|  | 4. 存储：`~/.AICA/permissions.json`，格式 `{ tool, pattern, decision: "always_allow"\|"always_deny", timestamp }` |
|  | 5. `SafetyGuard` 启动时加载，优先于默认规则 |
|  | 6. VS UI 支持"始终允许"/"始终拒绝"选项 |
| **假设** | 明确告知拒绝原因比让弱模型猜更有效；内网用户权限偏好稳定 |
| **失效信号** | 持久化决策后同一权限的重复确认率未下降 |
| **Telemetry 埋点** | 记录 permission_denied_with_feedback（bool）、persistent_decisions_count |
| **验收标准** | 拒绝时可附带反馈；决策跨会话保留 |

#### Phase 3 交付物

- 按相关性检索的记忆注入
- 权限拒绝反馈注入 Agent 上下文
- 权限决策跨会话持久化

---

### Phase 4：安全网 + 评估基础（第 9-11 周）

**目标**：建立文件安全保障，为 Evaluation 体系打基础。

#### H2：文件快照与回滚（10-15 天）

| 项 | 内容 |
|----|------|
| **新增** | `SnapshotManager.cs` |
| **改什么** | `EditFileTool.cs`（编辑前调用快照）、VS 工具栏 UI |
| **步骤** | 1. 编辑前：将原始文件复制到 `~/.AICA/snapshots/{sessionId}/{stepIndex}/{relativePath}` |
|  | 2. 回滚 API：`SnapshotManager.RestoreAsync(sessionId, stepIndex)` |
|  | 3. VS 工具栏按钮"回滚到步骤 N" |
|  | 4. 2MB 文件大小限制，自动排除大文件 |
|  | 5. 清理：会话结束后保留 7 天 |
| **假设** | MiniMax 会产生需要回滚的错误编辑 |
| **失效信号** | 回滚功能使用频率接近 0 |
| **Telemetry 埋点** | 记录 snapshot_created、snapshot_restored、snapshot_size_bytes |
| **验收标准** | 任意编辑步骤可回滚；UI 可视化快照点 |

#### OH5：SubAgent 泛化 + ReviewAgent（5-8 天）

| 项 | 内容 |
|----|------|
| **新增** | `SubAgent.cs` 基类、`ReviewAgent.cs` |
| **重构** | `PlanAgent.cs` 改为 SubAgent 实例化配置 |
| **SubAgent 基类设计** | |

```csharp
public class SubAgent
{
    string SystemPrompt;
    HashSet<string> AllowedTools;  // null = 无工具（纯推理）
    int MaxIterations;
    int TimeoutSeconds;
    int TokenBudget;

    Task<SubAgentResult> RunAsync(string task, IAgentContext ctx, CancellationToken ct);
}
```

| 预定义实例 | 配置 |
|-----------|------|
| PlanAgent | 规划 prompt + 只读工具 + 10 次迭代 + 60s + 16K |
| ReviewAgent | 检查清单 prompt + 无工具 + 1 次迭代 + 15s + 4K |

**RC1 ReviewAgent 检查清单**（5 个防守维度）：

| 维度 | 权重 | 检查项示例 |
|------|------|----------|
| 一致性 | 高 | 修改了函数签名？→ 对应头文件是否同步？ |
| 安全性 | 高 | 编辑后是否有编译错误？是否引入空指针风险？ |
| 范围控制 | 中 | 修改是否超出了用户请求的范围？ |
| 规范性 | 中 | 代码风格是否一致？（有 M3 兜底） |
| 可读性 | 低 | 命名是否清晰？结构是否合理？ |

**关键设计原则**：
- ReviewAgent 是**辅助用户审查的 AI 第二意见**，不是自动纠错器
- 检查清单式（模式匹配）而非开放式判断，弱模型更可靠
- 结果呈现给用户，**不反馈给 LLM 做自动纠错**
- 假设：MiniMax 能基于检查清单给出有用审查
- 失效信号：审查意见的用户采纳率 <20%

#### S3：头文件同步感知（3-5 天）

| 项 | 内容 |
|----|------|
| **新增** | `HeaderSyncDetector.cs`（~100 行） |
| **改什么** | `EditFileTool.cs`（编辑后、格式化后、诊断前） |
| **逻辑** | 1. 编辑前：从 ProjectIndex 记录被修改符号的 Signature |
|  | 2. 编辑后：重新解析符号，比较 Signature 变化 |
|  | 3. 签名变化 → 按 Namespace + Name 查找 .h/.hpp 中的声明 → 追加警告 |
|  | 4. 仅签名变化时触发，函数体修改不触发 |
| **假设** | MiniMax 看到同步警告后会修改头文件 |
| **失效信号** | 警告后实际修改率 <30% |
| **Telemetry 埋点** | 记录 header_sync_warning_triggered、header_sync_acted_on |
| **验收标准** | 编辑 .cpp 中函数签名时自动检测 .h 是否需要同步 |

#### Phase 4 交付物

- 文件快照与回滚系统
- SubAgent 基类 + PlanAgent 重构 + ReviewAgent（检查清单式）
- 头文件同步检测

---

### Phase 5：PlanAgent 优化 + Hooks（第 12-13 周）

**目标**：修复计划执行偏离问题，建立可配置扩展机制。

#### PA1：PlanAgent 输出优化（2-3 天）

| 项 | 内容 |
|----|------|
| **改什么** | `PlanAgent.cs`（Prompt 重写）、`AgentExecutor.cs`（重复注入逻辑） |
| **三个改进** | |

| 改进 | 说明 |
|------|------|
| 粗粒度目标 | PlanAgent 输出"做到什么效果"而非"修改 foo.cpp 第 42 行" |
| 成功标准 | 每步附带成功标准（如"编译通过 + 所有调用者已更新"） |
| 每轮重复注入 | 当前步骤目标在每轮 Agent 迭代中重复注入 System Prompt，对抗 lost in the middle |

| 项 | 内容 |
|----|------|
| **假设** | 粗粒度目标 + 重复注入能减少计划执行偏离 |
| **失效信号** | 计划完成率未改善 |
| **Telemetry 埋点** | 记录 plan_steps_total、plan_steps_completed、plan_abandoned |
| **验收标准** | PlanAgent 输出含成功标准；Agent 日志可见每轮步骤目标注入 |

#### OH3：Hooks 钩子系统（5-8 天）

| 项 | 内容 |
|----|------|
| **新增** | Hook 配置加载器、`CommandHookExecutor.cs`、`AgentHookExecutor.cs` |
| **改什么** | `ToolExecutionPipeline.cs`（Hook 触发点） |

**两种 Hook 类型**：

| 类型 | 执行方式 | 适用场景 |
|------|---------|---------|
| Command Hook | Shell 命令（cmd.exe / powershell） | clang-format、编译检查、审计日志 |
| Agent Hook | 调用 ReviewAgent（SubAgent） | AI 辅助代码审查 |

**配置格式**：

```json
{
  "hooks": {
    "PostToolUse": [{
      "matcher": "edit",
      "type": "command",
      "command": "clang-format -i \"${file_path}\"",
      "timeout_seconds": 10,
      "block_on_failure": false
    }],
    "PreToolUse": [{
      "matcher": "run_command",
      "type": "command",
      "command": "echo ${command} >> ~/.AICA/audit.log",
      "timeout_seconds": 5,
      "block_on_failure": false
    }]
  }
}
```

**Agent Hook 流程（与现有编辑确认流程集成）**：

现有编辑流程：Agent 调用 edit → diff 预览 → 用户确认/修改/取消 → 根据实际结果继续任务

Agent Hook 插入时序：
```
1. Agent 调用 edit → diff 预览呈现给用户
2. Agent Hook 并行启动 ReviewAgent（15s 超时）
   └─ ReviewAgent 基于 diff 内容进行检查清单审查
3. 用户看到：diff + AI 审查意见（ReviewAgent 结果到达后追加显示）
4. 用户做最终决策：
   ├─ 直接确认 → 干净结果回主 LLM
   ├─ 用户手动修改 → ReviewAgent 针对用户修改后的版本给出可实施的建议
   │   （建议呈现给用户参考，不触发 Agent 自动修改）
   └─ 取消 → Agent 收到取消通知
5. 无论哪种情况，ReviewAgent 的反馈不注入主 LLM 上下文
```

**关键原则**：
- 用户手动编辑时，ReviewAgent 应给出**可实施的建议**供用户参考，而非让 Agent 继续自动修改
- ReviewAgent 超时（15s）未返回时，不阻塞用户操作，用户可直接确认
- 审查结果仅呈现给用户辅助决策，不反馈给主 LLM

| 项 | 内容 |
|----|------|
| **假设** | 可配置的 Hook 能减少硬编码扩展需求 |
| **失效信号** | 用户从不配置自定义 Hook |
| **验收标准** | Command Hook 和 Agent Hook 均可通过 JSON 配置启用 |

#### Phase 5 交付物

- PlanAgent 粗粒度输出 + 成功标准 + 每轮重复注入
- Hooks 系统（Command + Agent）
- M3 自动格式化可迁移为 Hook（可选）

---

### Phase 6：知识图谱增强（第 14-16 周）

**目标**：深化 AICA 在 C/C++ 场景的独特优势。

#### S1：符号检索增强（5-8 天）

| 项 | 内容 |
|----|------|
| **改什么** | `AgentExecutor.cs`（早期异步预热）、`KnowledgeContextProvider.cs` |
| **方案** | AgentExecutor 开始时异步调用 GitNexus context → 将关系图数据传入 KnowledgeContextProvider → TF-IDF Top-10 后用关系图扩展到 Top-15-20 → 3 秒超时保护 |
| **假设** | 关系图扩展能帮助发现"虽不含关键词但有调用关系"的相关符号 |
| **失效信号** | 关系图扩展的命中率（被 Agent 实际使用的比率）<10% |
| **验收标准** | 用户问"修改 Foo 影响什么"时，能找到调用 Foo 的 Bar 和 Baz |

#### S4：GitNexus 主动触发（4-6 天）

| 项 | 内容 |
|----|------|
| **改什么** | `EditFileTool.cs`（条件触发 impact 分析） |
| **触发条件** | 意图为 refactor 或 bug_fix（ClassifyIntent）+ 编辑目标为公共 API（.h 或 public 方法）+ 同一文件首次编辑 |
| **截断策略** | 仅 d=1（直接依赖），1000 tokens 上限，5 秒超时 |
| **输出格式** | 追加到 ToolResult：`"⚠️ Impact: FooBar.cpp:SetPosition() and 3 other callers may be affected"` |
| **假设** | Agent 收到 impact 预警后会注意保护受影响的调用者 |
| **失效信号** | 预警后 Agent 仍然破坏调用者的比率未下降 |
| **验收标准** | 修改公共 API 时自动显示影响范围（d=1），不阻塞 Agent 循环 |

#### S2：编辑后自动构建（4-5 天）

| 项 | 内容 |
|----|------|
| **新增** | `VSAgentContext.TriggerBackgroundBuildAsync()`、`BuildResultCache.cs` |
| **改什么** | `EditFileTool.cs`（触发异步构建）、`AgentExecutor.cs`（注入构建结果） |
| **流程** | 编辑确认 → 后台触发 VS 增量构建（不阻塞）→ 构建完成缓存结果 → 下一轮 Agent 迭代开始时检查并注入 |
| **假设** | 增量构建能捕获跨文件链接错误和类型不匹配 |
| **失效信号** | 构建结果中有价值的错误（Agent 据此修复了问题）比率 <10% |
| **验收标准** | 编辑后自动后台构建；构建错误在下一轮 Agent 迭代中可见 |

#### T2：Telemetry 会话摘要（2-3 天）

| 项 | 内容 |
|----|------|
| **新增** | 会话摘要生成器 |
| **改什么** | `AgentExecutor.cs`（会话结束钩子） |
| **输出** | `~/.AICA/telemetry/sessions/{id}.json`，包含：总迭代数、工具调用统计、各组件触发频率、计划完成率、token 总量 |
| **验收标准** | 每个会话结束时自动生成聚合摘要 |

#### Phase 6 交付物

- TF-IDF + 关系图混合检索
- 修改公共 API 前的 impact 预警
- 后台异步构建 + 编译错误注入
- 会话级 telemetry 摘要

---

## 五、周计划总览

```
第 1 周 ──── M1 Prune 前移 (2d) + M3 自动格式化 (3d)
第 2 周 ──── SK Skills+模板 前半 (5d)
第 3 周 ──── SK Skills+模板 后半 (3d) + H1 截断持久化 启动 (2d)
第 4 周 ──── H1 截断持久化 核心服务 + 首批工具接入 (5d)
第 5 周 ──── H1 截断持久化 剩余工具接入 + 清理机制 (5d)
第 6 周 ──── OH2 结构化记忆升级 (5d)
第 7 周 ──── OH2 收尾 (2d) + H3 权限反馈注入 (3d)
第 8 周 ──── H3 权限决策持久化 (5d)
第 9 周 ──── H2 文件快照 核心 (5d)
第 10 周 ─── H2 文件快照 回滚API + UI (5d)
第 11 周 ─── OH5 SubAgent+ReviewAgent (3d) + S3 头文件同步 (2d)
第 12 周 ─── PA1 PlanAgent 优化 (2d) + OH3 Hooks 启动 (3d)
第 13 周 ─── OH3 Hooks 完成 (5d)
第 14 周 ─── S1 符号检索增强 (5d)
第 15 周 ─── S4 Impact 分析 (4d) + T2 会话摘要 (1d)
第 16 周 ─── S2 后台构建 (5d)
```

**每周额外隐含工作**：T1 telemetry 埋点（~0.5d）+ AS1 假设记录（~0.25d），已包含在各任务工作量中。

---

## 六、Harness 五维度预期效果

完成全部 Phase 后，AICA Harness 五维度的状态：

| 维度 | 实施前 | 实施后 |
|------|--------|--------|
| **Agent Loop** | ✅ 成熟 | ✅ 成熟 + telemetry 可观测 |
| **Tool System** | ✅ 成熟，截断有缺陷 | ✅ 成熟 + 截断持久化 + Hooks 可扩展 + 自动格式化 |
| **Context Mgmt** | ⚠️ Prune 时机不对 | ✅ 分层策略完整（Prune→Compaction→Reset 就绪） |
| **Evaluation** | ❌ 缺 Agent 级 | ⚠️ ReviewAgent 检查清单 + Impact 预警 + 后台构建（三层评估） |
| **Memory** | ⚠️ 全量拼接 | ✅ 相关性检索 + Skills 数据驱动 + 快照安全网 |

### 横切关注点

| 项 | 实施前 | 实施后 |
|----|--------|--------|
| **Prompt Engineering** | 硬编码 guidance | Skills .md 文件驱动 + 任务模板 + 成功标准 |
| **Telemetry** | 无 | L1 结构化日志 + L2 会话摘要 + 各组件专项埋点 |
| **假设管理** | 无 | 每组件记录假设/验证方式/失效信号 |

### 对 MiniMax-M2.5 的系统级补偿

| 模型弱点 | 补偿手段 | 来源 |
|----------|---------|------|
| 探索效率低 | 任务模板指导 + 符号预注入 | SK + S1 |
| 容易遗漏跨文件影响 | 头文件同步检测 + Impact 预警 | S3 + S4 |
| 编译错误反馈不完整 | 后台构建 + 结构化诊断 | S2 |
| 上下文窗口有限 | Prune 前移 + 截断存文件 | M1 + H1 |
| 幻觉导致错误编辑 | 快照回滚 + 格式化 | H2 + M3 |
| 不理解拒绝原因 | 反馈注入对话 | H3 |
| 输出格式不一致 | 自动格式化 | M3 |
| 记忆注入不精准 | 相关性检索 | OH2 |
| 计划执行偏离 | 粗粒度目标 + 成功标准 + 每轮重复注入 | PA1 |
| 自评不可靠 | ReviewAgent 检查清单（独立上下文） | OH5 |

---

## 七、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| EditFileTool 7 项修改冲突 | 合并困难、回归 bug | 严格按排序实施，每项完成后充分测试 |
| 中文分词复杂度（OH2） | 记忆检索效果差 | 先用简单按字分词，后续迭代优化 |
| S1 异步预热死锁 | AgentExecutor 卡住 | 3 秒超时保护 + fallback 到纯 TF-IDF |
| GitNexus 延迟（S4） | 拖慢 Agent 循环 | 条件触发 + 5 秒超时 + 非阻塞 |
| ReviewAgent 对弱模型无效 | 审查意见无价值 | 检查清单式（模式匹配）降低推理依赖；telemetry 追踪采纳率，<20% 则简化或移除 |
| 单人开发排期压力 | 延期 | 每 Phase 独立交付价值，可在任意 Phase 后暂停 |
| DynamicToolSelector 去过滤后回归 | MiniMax 幻觉调用不相关工具频率上升 | 先采集基线数据，去过滤后对比，幻觉率上升则回退 |

---

## 八、参考文档索引

| 文档 | 路径 |
|------|------|
| v2.1 改进分析 | `doc/2.1plan/AICA_v2.1_Improvement_Analysis.md` |
| v2.1 优化路线图 | `doc/2.1plan/AICA_v2.1_Optimization_Roadmap.md` |
| OpenHarness 架构分析 | `doc/2.1plan/AICA_OpenHarness_Analysis.md` |
| Harness 学习笔记 | `doc/harness-study/README.md` |
| Anthropic 原文 | https://www.anthropic.com/engineering/harness-design-long-running-apps |
