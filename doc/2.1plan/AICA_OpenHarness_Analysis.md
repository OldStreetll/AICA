# AICA 对标 OpenHarness 架构分析与借鉴方案

> **版本：** v1.0  
> **日期：** 2026-04-05  
> **作者：** AICA 项目组  
> **状态：** 初稿

---

## 修订记录

| 版本 | 日期 | 修订内容 | 修订人 |
|------|------|---------|--------|
| v1.0 | 2026-04-05 | 初稿，整理五站对标分析结论 | — |
| v1.1 | 2026-04-05 | 审查修正：2 CRITICAL + 5 MAJOR + 5 MINOR + 2 NIT 项 | — |

---

## 目录

1. [摘要](#摘要)
2. [背景与核心策略](#背景与核心策略)
3. [AICA 约束条件](#aica-约束条件)
4. [AICA 已有优势](#aica-已有优势)
5. [第一站：Skills 技能系统](#第一站skills-技能系统极高-roi)
6. [第二站：结构化记忆升级](#第二站结构化记忆升级高-roi)
7. [第三站：Hooks 钩子系统](#第三站hooks-钩子系统高-roi)
8. [第四站：Plugin 插件系统](#第四站plugin-插件系统暂且搁置)
9. [第五站：多 Agent 协调](#第五站多-agent-协调架构投资)
10. [实施优先级总览](#实施优先级总览)
11. [与 v2.1 改进计划的关系](#七与-v21-改进计划的关系)
12. [参考文档](#参考文档)

---

## 摘要

本文档整理了 AICA 项目对 OpenHarness（OH）架构的五站对标分析结论。核心策略为 **"弱模型 + 强系统"**——通过增强 Harness（系统基础设施）来弥补单一弱模型的能力不足。分析涵盖 Skills 技能系统、结构化记忆、Hooks 钩子、Plugin 插件、多 Agent 协调五个维度，给出了各维度的借鉴方案、改动范围和实施优先级。

关键结论：

- Skills 系统无需新建，基于已有 Rules 系统升级即可，ROI 极高
- 记忆系统从全量拼接升级为按相关性检索，精准注入省 token
- Hooks 系统与已有中间件互补，支持 Command Hook 和 Agent Hook 两种类型
- Plugin 系统暂且搁置，待前三项落地后再评估
- 多 Agent 方面不引入 OH Swarm，而是将 PlanAgent 泛化为 SubAgent 基类

---

## 背景与核心策略

### 项目概况

| 项目 | 定位 | 技术栈 |
|------|------|--------|
| **AICA** | 企业内部 C/C++ AI 编程助手 | VS2022 VSIX 插件，MiniMax-M2.5 单一私有化弱模型 |
| **OpenHarness** | 开源 Claude Code Python 复刻版 | HKUDS 开发，支持多模型 |

### 核心策略："弱模型 + 强系统"

OH 的核心命题：**Agent 的智能不只来自模型，更来自 Harness。**

- 不管模型多弱，Harness 保证产出稳定可控
- **模型决定天花板，Harness 决定地板**
- 用 Harness（系统基础设施）弥补模型能力不足

---

## AICA 约束条件

| # | 约束 | 影响 |
|---|------|------|
| 1 | MiniMax-M2.5 弱模型 | 指令遵从弱、复杂推理差、容易幻觉工具名 |
| 2 | VS2022 VSIX + .NET Framework 4.8 | C# 实现，Windows 平台限定 |
| 3 | 涉密离线环境 | 无法联网，排除云端依赖方案 |
| 4 | 20 并发 + 高延迟 | 每次 LLM 调用都"贵"，必须精打细算 |

---

## AICA 已有优势

以下能力已经成熟，不需要改动：

| 能力 | 说明 |
|------|------|
| **Agent Loop** | AgentExecutor ~1200 行，finish_reason 驱动，成熟可靠 |
| **工具中间件管道** | ToolExecutionPipeline，6 层中间件，优于 OH |
| **工具别名/幻觉修正** | 弱模型刚需，OH 没有此能力 |
| **Doom Loop 检测** | 同工具+参数 3 次触发用户确认，防止死循环 |
| **Token 预算管理** | 85% budget + 70% condense + emergency overflow，完善 |
| **MCP 集成架构** | GitNexus + 分阶段注册，架构合理 |

---

## 第一站：Skills 技能系统（极高 ROI）

### 现状分析

AICA 已有 Rules 系统（RuleLoader + RuleEvaluator + YamlFrontmatterParser），具备技能系统的骨架。同时存在 `DynamicToolSelector.ClassifyIntent()` 关键词匹配的控制型设计，以及 `SystemPromptBuilder` 中硬编码的 `AddBugFixGuidance()` / `AddQtTemplateGuidance()` 等方法。（注：`AddToolDescriptions()` 和 `AddGitNexusGuidance()` 已标记 `[Obsolete]` 且为 no-op，无需外部化。）

### OH 做法

OH 使用 Skills 文件（Markdown + YAML frontmatter），通过信任型设计让 LLM 自主判断何时使用技能。

### AICA 借鉴方案

**核心结论：不需要新建 Skills 系统。已有的 Rules 系统就是 Skills 的骨架，只需升级。**

#### 设计哲学：信任型

- 去掉 `DynamicToolSelector.ClassifyIntent()` 的工具过滤功能（仅保留 conversation 意图分支），消除控制型设计与信任型哲学的矛盾
- `AgentExecutor.cs` 中的 ClassifyIntent 调用链路需要同步调整
- 让 LLM 自主判断何时使用技能
- 参考文档：`D:\project\AICA\doc\v4plan\AICA_ToolCall_Optimization_Plan.md` 中已分析了从控制型到信任型的迁移路径

#### 三层触发机制（方案 E）

三层是 **叠加关系** 而非互斥：

| 层 | 触发方式 | 信号来源 | 确定性 |
|----|---------|---------|--------|
| 第一层 | 用户显式命令 `/review`、`/debug` | 用户输入 | 100% |
| 第二层 | paths glob 匹配 | 当前操作文件（VS2022 上下文） | 100%（不依赖 LLM） |
| 第三层 | SkillTool（LLM 自主调用） | LLM 判断 | 不稳定但无害，模型升级后自动变强 |

#### SkillTool 设计

- **只有 1 个工具**（不是每个 skill 一个工具）
- 接受 `name` 参数，从注册表查找对应 `.md` 返回内容
- 增加 100 个 skill 也只占 1 个工具位的 token 开销
- 加入成本极低，无下行风险，有上行空间

#### 技能文件格式

扩展已有 Rule 的 YAML frontmatter：

```markdown
---
name: debug-cpp
description: C/C++段错误和内存问题的调试流程
paths:
  - "*.cpp"
  - "*.h"
  - "*.c"
priority: 20
---
具体技能内容...
```

> **注意：** 当前 `YamlFrontmatterParser` 仅支持 YAML 标准列表格式，如需支持 JSON 内联列表需升级 parser。

### 改动范围

| 操作 | 目标 | 说明 |
|------|------|------|
| 修改 | `Rule.cs` | 增加 `Description` 字段，当前 frontmatter 中的 name/description 存入 `Metadata.Custom` 字典，`Rule.Name` 来自文件名而非 frontmatter |
| 修改 | `RuleLoader.cs` | 显式提取 frontmatter 中的 name/description 并赋值到 Rule 对应属性 |
| 修改 | `RuleEvaluator.cs` | 增加 paths 匹配逻辑与技能加载 |
| 修改 | `SystemPromptBuilder.cs` | 用规则文件替代硬编码的 guidance 方法 |
| 新增 | `SkillTool` | ~60-80 行（需实现 `IAgentTool` 接口），通用工具，按名称查找 .md 文件返回内容 |
| 新增 | `.aica-rules/*.md` | 若干技能文件 |
| 外部化 | `AddBugFixGuidance()` / `AddQtTemplateGuidance()` | 硬编码方法内容外部化为 .md 文件（注：`AddToolDescriptions()` 和 `AddGitNexusGuidance()` 已标记 `[Obsolete]` 且为 no-op，无需处理） |

> 技能由 AICA 开发者维护，不需要用户自服务 UI。

### 预期效果

- 系统提示词从硬编码转为数据驱动，可维护性大幅提升
- 新增 C/C++ 场景能力只需添加 .md 文件，零代码改动
- SkillTool 为模型能力升级预留了上行空间

---

## 第二站：结构化记忆升级（高 ROI）

### 现状分析

`MemoryBank.cs` 存在以下问题：

- **全量拼接**：加载所有 `.aica/memory/*.md`，按文件名排序
- **硬截断**：4000 字符一刀切
- **缺元数据**：无分类、无描述
- **缺检索**：无法按相关性筛选

**后果**：浪费 token 注入无关记忆，真正需要的记忆可能被截断。

### OH 做法

OH 使用结构化记忆文件（带 YAML frontmatter），按 4 类分类（user / feedback / project / reference），通过 `find_relevant_memories` 按相关性检索。

### AICA 借鉴方案

#### 1. 记忆文件加 YAML frontmatter

```markdown
---
name: Qt编码规范
description: Qt 5.15 项目编码规范摘要，含信号槽、Widget命名、QSS样式约定
type: project
---
具体记忆内容...
```

分 4 类：`user` / `feedback` / `project` / `reference`（参考 OH）。复用已有的 `YamlFrontmatterParser`。

#### 2. 按相关性检索替代全量拼接

| 维度 | 方案 |
|------|------|
| 检索输入 | 用户原始消息（主） |
| 分词策略 | 英文按单词（3+ 字符）+ 中文按单字（去停用词） |
| 打分规则 | description 命中 2x 权重 + body 命中 1x 权重 |
| 注入策略 | 取 top N（N 根据 token 预算动态决定） |
| 设计一致性 | 不使用意图标签作为检索输入（与信任型设计一致） |

### 改动范围

| 操作 | 目标 | 说明 |
|------|------|------|
| 修改 | `MemoryBank.cs` | 替换全量拼接为相关性检索 |
| 复用 | `YamlFrontmatterParser` | 解析记忆文件的 frontmatter |
| 新增 | 相关性检索逻辑 | ~100-150 行 C#，参考 OH 的 `find_relevant_memories`（中文分词 + 停用词过滤 + 加权打分 + 动态 top N + MemoryBank 重构，中文分词复杂度较高） |

### 预期效果

- 精准注入相关记忆，减少无关 token 消耗。预计节省 ~300-500 tokens/请求。主要价值不在 token 节省，而在提升记忆注入的精准度和消除重要记忆被截断的风险
- 消除 4000 字符硬截断导致的重要记忆丢失
- 记忆文件自带描述，便于管理和调试

---

## 第三站：Hooks 钩子系统（高 ROI）

### 现状分析

AICA 已有 6 层中间件管道（ToolExecutionPipeline）：PreValidation → Permission → Timeout → [Tool] → Verification → Monitoring → Logging。这是内核级确定性保障，成熟可靠。但缺乏可配置的扩展点，新增行为必须改代码。

### OH 做法

OH 支持 Hooks（PreToolUse / PostToolUse），通过配置文件声明，在工具执行前后插入自定义行为。

### AICA 借鉴方案

#### 与中间件的关系：互补而非替代

```
Hooks 层（可配置，JSON 声明）
  ├── PRE_TOOL_USE → 工具执行前检查
  └── POST_TOOL_USE → 工具执行后操作
Middleware 层（硬编码，C# 编译时确定）
  └── PreValidation → Permission → Timeout → [Tool] → Verification → Monitoring → Logging
```

> **注意：** 实际执行顺序取决于中间件注册顺序（`Use()` 调用顺序）。

- **中间件管道** = 内核级确定性保障，不动
- **Hooks** = 可配置的扩展点，在中间件之上，不改代码即可插入行为

#### 支持两种 Hook 类型

##### 1. Command Hook：执行 Shell 命令

- **执行环境**：Windows（cmd.exe / powershell.exe）
- **适用场景**：clang-format 自动格式化、危险命令拦截、编译检查、审计日志

示例配置：

```json
{
  "PostToolUse": [{
    "matcher": "edit",
    "command": "clang-format -i \"${file_path}\"",
    "timeout_seconds": 10,
    "block_on_failure": false
  }]
}
```

##### 2. Agent Hook：调用 LLM 进行语义验证

- **定位**：不是自动纠错器，是辅助用户审查的 AI 第二意见
- **仅挂载在**：`edit` 和 `write_file`（高影响不可逆操作）
- **成本**：一次 agent 循环中可能触发 2-5 次额外 LLM 调用

**Agent Hook 执行流程：**

1. LLM 调用 edit → 编辑应用（预览态）
2. Agent Hook 并行验证 → 产出审查意见
3. 用户看到：diff + AI 审查意见
4. 用户做最终决策：确认 / 修改 / 取消
5. 干净的结果回主 LLM（不含 Hook 反馈，不触发纠错循环）

**关键设计原则：** 验证结果呈现给用户辅助审查，**不反馈给 LLM 做自动纠错**，避免与现有用户确认流程冲突。

#### 不实现的 Hook 类型

| 类型 | 原因 |
|------|------|
| HTTP Hook | 涉密离线环境不适用 |
| Prompt Hook | 被 Agent Hook 覆盖 |

### 改动范围

| 操作 | 目标 | 说明 |
|------|------|------|
| 新增 | Hook 配置加载器 | 读取 JSON 配置文件 |
| 新增 | Command Hook 执行器 | 调用 Shell 命令，处理超时和失败 |
| 新增 | Agent Hook 执行器 | 基于 SubAgent（见第五站）实现 |
| 修改 | ToolExecutionPipeline | 在中间件管道外层接入 Hook 触发点 |

### 预期效果

- 不改代码即可新增工具执行前后的检查和操作
- clang-format 等 C/C++ 工具链自动集成
- Agent Hook 提供 AI 辅助代码审查，提升编辑质量

---

## 第四站：Plugin 插件系统（暂且搁置）

### 现状分析

AICA 暂无插件系统，各能力（Skills、Hooks、MCP）独立管理。

### OH 做法

OH 的 Plugin 本质是 Skills + Hooks + MCP 的打包容器（plugin.json manifest + 目录结构）。

### AICA 借鉴方案

**结论：先搁置，Skills + Memory + Hooks 落地后再评估。**

#### 潜在价值

| 场景 | 说明 |
|------|------|
| A：项目组隔离 | 不同项目组（平台组 vs 界面组）需要不同的技能+钩子组合，Plugin 提供隔离 |
| B：快速开关 | config 一行 `"misra-safety": false` 即可开关某套能力 |
| C：版本管理 | 插件目录独立部署，便于分发和版本控制 |

#### 预估实现量

~50 行新增代码（PluginLoader 扫描目录 + 读 manifest + 分发给各子系统）。此为最小 MVP 估算，不含错误处理和验证逻辑。

> **待补充：** 用户将在后续补充具体细节和需求。

### 预期效果

- 为多项目组场景提供能力隔离和快速切换
- 具体效果待需求细化后评估

---

## 第五站：多 Agent 协调（架构投资）

### 现状分析

AICA 已有 PlanAgent：独立 system prompt + 独立工具集（只读）+ 独立 token 预算（16K）+ 独立超时（60s）。形态正确但不可复用。

### OH 做法

OH 使用 Swarm 框架，面向"多模型、多终端、高并发"设计（subprocess / tmux / iTerm2 后端）。

### AICA 借鉴方案

**结论：不需要 OH 的 Swarm 框架，需要的是把 PlanAgent 泛化为 SubAgent 基类。**

#### 为什么不需要 OH Swarm

- OH Swarm 面向多模型、多终端、高并发场景
- AICA：单模型、单窗格 VS2022 UI、20 并发限制、Windows 环境
- 轻量级 SubAgent 完全满足需求，不需要 IPC / 信箱 / 额外并发

#### SubAgent 基类设计

借鉴的是 **"用配置定义 Agent 角色"** 的思想：

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

#### 预定义实例

| Agent | 配置 | 用途 |
|-------|------|------|
| PlanAgent | 规划 prompt + 只读工具 + 10 次迭代 + 60s + 16K | 任务规划 |
| ReviewAgent | 审查 prompt + 无工具 + 1 次迭代 + 15s + 4K | Agent Hook 的执行器 |
| 未来扩展 | TestAgent、SecurityAgent 等 | 按需增加 |

### 改动范围

| 操作 | 目标 | 说明 |
|------|------|------|
| 新增 | `SubAgent` 基类 | 抽象 PlanAgent 的通用逻辑 |
| 重构 | `PlanAgent` | 改为 SubAgent 的实例化配置 |
| 新增 | `ReviewAgent` | SubAgent 实例，作为 Agent Hook 的执行器 |

### 预期效果

- PlanAgent 逻辑可复用，新增 Agent 角色只需配置
- ReviewAgent 支撑 Agent Hook 的 AI 审查能力
- 为未来 TestAgent、SecurityAgent 等预留扩展点

---

## 实施优先级总览

```
优先级  模块                ROI       说明
─────────────────────────────────────────────────────────────
  1     Skills 技能系统     ████████████  极高 — 最少代码，最大能力提升
  2     结构化记忆升级      █████████░░░  高 — 已有基础，精准注入省 token
  3     Hooks 钩子系统      ████████░░░░  高 — Command Hook 部分；Agent Hook 与 SubAgent 合并评估，建议并行实施
  4     SubAgent 泛化       ██████░░░░░░  架构投资 — PlanAgent 泛化 + ReviewAgent
  5     Plugin 插件框架     ████░░░░░░░░  暂且搁置 — 待用户补充细节
```

### 依赖关系

- Skills（第一站）和 Memory（第二站）相互独立，可并行推进
- Hooks 的 Agent Hook（第三站）依赖 SubAgent 泛化（第五站）中的 ReviewAgent
- Plugin（第四站）依赖前三站落地

### 建议实施顺序

1. **第一批（并行）**：Skills 技能系统 + 结构化记忆升级
2. **第二批（并行）**：SubAgent 泛化 + Hooks 钩子系统（Command Hook 先行，Agent Hook 在 SubAgent 就绪后接入）
3. **第三批（待评估）**：Plugin 插件框架

---

## 七、与 v2.1 改进计划的关系

本文档方案与 v2.1 改进计划（`AICA_v2.1_Improvement_Analysis.md`）面向不同维度：

- **v2.1 改进计划**：面向功能差距补齐（H1-H3 高优先级 + M1-M3 中优先级 + S1-S5 扬长板）
- **本文档**：面向架构范式借鉴（Skills / Memory / Hooks / SubAgent / Plugin）

两份计划共享同一个开发资源，建议如下协调排期：

1. **v2.1 Phase 1（H1+H3）与本文档第一批（Skills+Memory）可穿插执行**，共享同一套 Rule/Memory 基础设施改造。具体来说，Rule.cs 增加 Description 字段、RuleLoader 显式提取 frontmatter 等改动同时服务于两个计划。
2. **v2.1 Phase 2（M1-M3）与本文档第二批（Hooks+SubAgent）串行或交替推进**，避免同时修改 AgentExecutor 和 ToolExecutionPipeline 导致冲突。
3. **v2.1 扬长板（S1-S5）可作为穿插任务**，在等待架构改造稳定期间推进。

---

## 参考文档

| 文档 | 路径 / 来源 |
|------|------------|
| AICA ToolCall 优化计划 | `D:\project\AICA\doc\v4plan\AICA_ToolCall_Optimization_Plan.md` |
| AICA v2.1 改进分析 | `D:\project\AICA\doc\2.1plan\AICA_v2.1_Improvement_Analysis.md` |
| AICA v2.1 优化路线图 | `D:\project\AICA\doc\2.1plan\AICA_v2.1_Optimization_Roadmap.md` |
| OpenHarness 源码 | `D:\project\OpenHarness` |
| OH find_relevant_memories | `D:\project\OpenHarness\src\openharness\memory\search.py` |
| OH Skills / Hooks / Plugin 机制 | OpenHarness 架构参考 |
