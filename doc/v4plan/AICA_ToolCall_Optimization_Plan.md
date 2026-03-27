# AICA 工具调用优化方案 — 对标 OpenCode

> 版本: v1.1 | 日期: 2026-03-27
> 背景: D-01 修复过程中发现 AICA 与 OpenCode 在工具选择机制上存在根本性差异。本文档分析差异并提出优化方向。
> 前置文档: [MCP 工具透传 + System Prompt 重构计划](AICA_MCP_SystemPrompt_Refactor_Plan.md)

---

## 零、核心设计哲学差异

### 控制型 vs 信任型

| | AICA（控制型） | OpenCode（信任型） |
|---|---|---|
| **设计假设** | LLM 不够聪明，需要人为预判和限制 | LLM 足够聪明，给好的工具描述就能自行决策 |
| **意图分类** | 开发者写规则分类（ClassifyIntent: read/analyze/modify...） | 不分类，让 LLM 自己理解用户意图 |
| **工具过滤** | 按意图只暴露工具子集 | 所有工具始终可见 |
| **工具引导** | System Prompt 告诉 LLM "用 X 工具做 Y 事" | 工具描述自带 WHEN TO USE，LLM 自行判断 |
| **System Prompt 角色** | 工具使用手册（大量工具名 + 场景映射） | 风格和方法论指引（零工具名） |

### 为什么信任型更优

用户的请求是**隐式的** — 说"这个函数是做什么的"，不会说"请用 gitnexus_context 工具搜索"。在用户意图和工具选择之间存在一个**推理步骤**：

```
用户意图（隐式）→ [推理：需要什么信息？] → 工具选择
```

AICA 的控制型设计试图用**关键词规则**（ClassifyIntent）来完成这个推理步骤。但关键词规则是脆弱的：

- "这个函数是做什么的" → 被分为 `read`（正确是 `analyze`）
- "哪些文件用了 mutex" → 被分为 `read`（正确是 `search`，但 gitnexus_query 也适用）
- "为什么编译出错" → 被分为 `bug_fix`（正确，但可能也需要 gitnexus_impact）

**用关键词规则去做 LLM 该做的事，注定不如 LLM 自己判断准确。** 这就是控制型设计的根本缺陷。

OpenCode 的信任型设计把推理步骤**完全交给 LLM**：
1. 工具描述写清楚"我能做什么、什么时候该用我"（WHEN TO USE）
2. LLM 读所有工具描述，理解用户意图，自行决定用哪个工具
3. 不需要人为的意图分类、工具过滤、优先级排序

这不仅适用于搜索场景，**适用于所有场景**：

| 用户说 | 控制型（AICA）的问题 | 信任型（OpenCode）的做法 |
|--------|---------------------|------------------------|
| "这个函数是做什么的" | 分类为 read，可能过滤掉 impact 工具 | LLM 自己判断需要 context + impact |
| "哪些文件用了 mutex" | 分类为 read，偏向 grep_search | LLM 比较 grep_search 和 gitnexus_query 描述，选更合适的 |
| "帮我重构这段代码" | 分类为 modify，暴露所有工具 | LLM 先用 context 理解，再用 edit 修改 |
| "为什么编译出错" | 分类为 bug_fix | LLM 自己判断先搜索错误信息还是先看函数上下文 |
| "这个模块的架构是什么" | 分类为 analyze | LLM 自己选择 query（语义搜索）还是 list_dir + read_file |

### 信任型设计的前提条件

信任型设计不是无条件信任，它有两个前提：

1. **工具描述必须自足** — 每个工具的 description 必须包含足够信息让 LLM 判断何时使用。GitNexus MCP 原生描述做到了这一点（含 WHEN TO USE 章节）
2. **工具描述必须准确到达 LLM** — 通过 function calling API 传递，不被过滤、不被覆盖、不被降级

AICA 当前的 MCP 透传（Part 1）已满足条件 2。条件 1 由 GitNexus MCP server 的原生描述保证。

---

## 一、现状对比

### OpenCode 工具调用流程

```
用户请求
    ↓
所有已注册工具（MCP 原生定义）直接传入 function calling API
    ↓
System Prompt: 零工具名，106 行，纯风格/方法论指引
    ↓
LLM 根据工具描述自行选择 → 调用 → 返回
```

### AICA 当前工具调用流程

```
用户请求
    ↓
TaskComplexityAnalyzer 分析复杂度
    ↓
DynamicToolSelector.ClassifyIntent → 分类为 conversation/read/analyze/modify 等
    ↓
DynamicToolSelector.SelectTools → 按意图过滤工具子集
    ↓
SystemPromptBuilder:
    ├── AddToolDescriptions()  ← 在 System Prompt 中再写一遍工具描述（与 function calling 重复）
    ├── AddGitNexusGuidance()  ← GitNexus 优先策略 + few-shot
    ├── AddComplexityGuidance() ← 按复杂度分层加载规则
    └── AddRules()              ← 行为规则
    ↓
OpenAIClient.BuildRequest → 将过滤后的工具发送到 function calling API
    ↓
LLM 选择工具 → ToolDispatcher 执行 → 返回
```

### 关键差异

| 维度 | OpenCode | AICA |
|------|----------|------|
| **工具过滤** | 无（所有工具始终可见） | DynamicToolSelector 按意图过滤 |
| **工具描述位置** | 仅 function calling API | function calling API **+ System Prompt**（双重） |
| **System Prompt 工具名** | 零提及 | AddToolDescriptions 列出所有工具 + AddGitNexusGuidance 优先策略 |
| **System Prompt 长度** | ~106 行 | ~800 行 |
| **意图分类** | 无 | 6 种意图（conversation/read/command/analyze/bug_fix/modify） |
| **工具描述来源** | MCP 原生透传 | 硬编码 → 后台异步升级为原生（有竞态） |

---

## 二、优化方向

### 方向 1：去除 DynamicToolSelector 工具过滤

**现状问题：**
- DynamicToolSelector 将请求分为 6 种意图，每种意图只暴露一个工具子集
- 意图分类依赖关键词匹配（`ClassifyIntent`），误分类会导致 LLM 看不到需要的工具
- 例如：用户问"哪些文件用了 mutex"，可能被归类为 `read` 而非 `analyze`，但 `read` 意图下 GitNexus 工具虽然可见，LLM 仍可能因为 System Prompt 偏见不选它

**OpenCode 做法：** 不过滤，所有工具始终可见。LLM 自行决定。

**优化方案：**
- 去掉 `SelectTools` 过滤，始终传入全部工具
- 仅保留 `conversation` 意图的极简工具集（纯对话不需要 15 个工具）

**可行性：⭐⭐⭐⭐（高）**
- 改动小（DynamicToolSelector 一处条件修改）
- 风险低（更多工具可见 ≠ 误调用，LLM 有工具描述指引）
- MiniMax-M2.5 的 function calling 支持 15 个工具无问题（DebugView 确认 `Tools count: 15`）
- 潜在副作用：token 消耗略增（多 6 个工具定义 ~500 tokens），可忽略

---

### 方向 2：移除 AddToolDescriptions（消除双重描述）

**现状问题：**
- 工具描述同时出现在两个位置：
  1. System Prompt 中的 `## Available Tools` 章节（AddToolDescriptions 生成）
  2. Function calling API 的 `tools[]` 参数（OpenAIClient.BuildRequest 生成）
- 两处描述来源不同：System Prompt 用 `ToolDefinition.Description`，function calling 也用 `ToolDefinition.Description`
- 重复浪费 ~2000-3000 tokens
- 更严重的是：System Prompt 中的工具列表强化了 LLM 对列出工具的偏好

**OpenCode 做法：** 工具描述只在 function calling API 中，System Prompt 不提及。

**优化方案：**
- 删除 `AddToolDescriptions()` 调用
- 工具描述完全由 function calling API 承载
- System Prompt 保留通用的工具使用原则（"选择最佳工具"/"读文件后再编辑"等泛化规则）

**可行性：⭐⭐⭐（中高）**
- 改动小（删除一个方法调用）
- 节省 ~2000-3000 tokens
- 风险：之前 Part 2 尝试删除后复测发现 LLM 行为退化（任务重复 + 幻觉增加）。但当时是同时做了规则精简，退化可能来自规则精简而非 AddToolDescriptions 删除
- **建议：单独测试**，只删 AddToolDescriptions，不动其他规则，隔离验证

---

### 方向 3：移除 AddGitNexusGuidance 优先策略

**现状问题：**
- "首选 gitnexus → 次选 grep_search → 避免反复搜索"创造了偏见
- 偏见范围太窄：只覆盖"代码理解类任务"，搜索类任务不触发
- OpenCode 没有任何优先策略

**优化方案：**
- 删除"优先策略"章节（"首选/次选/避免"排序）
- 保留 few-shot 使用示例（repo 参数、简单符号名、Cypher schema 语法）—— 这些是参数正确性指导，不是偏见
- 让 function calling API 中的原生工具描述（含 WHEN TO USE）驱动选择

**可行性：⭐⭐⭐⭐（高）**
- 改动极小（删除 5 行）
- 当前复测已证明：去掉工具名偏见后，LLM 能自主正确选择 GitNexus
- 保留 few-shot 确保参数正确性（P1/P2/P3 问题不会复发）

---

### 方向 4：确保原生 MCP 描述优先生效（消除竞态）

**现状问题：**
- 工具注册分两阶段：同步注册硬编码定义 → 后台异步升级为原生 MCP 定义
- 如果用户在升级完成前发送消息，LLM 看到的是硬编码的短描述（缺少 WHEN TO USE）
- 竞态窗口：从打开聊天窗口到 MCP server ready + ListToolsAsync 完成，约 3-5 秒

**OpenCode 做法：** 工具注册是同步的，MCP server 启动后才开始接受请求。

**优化方案（两个子方案）：**

A. **阻塞式注册**：ChatToolWindowControl 初始化时 await CreateAllToolsAsync，完成后才允许用户发消息
- 优点：保证原生描述始终可用
- 缺点：首次打开聊天窗口可能延迟 3-5 秒

B. **延迟首次请求**：在 AgentExecutor.ExecuteAsync 开头检查升级是否完成，未完成则等待（加超时）
- 优点：不阻塞 UI 初始化
- 缺点：首次请求可能延迟

**可行性：⭐⭐⭐（中高）**
- 方案 A 改动小但影响 UI 响应性
- 方案 B 更优雅但需要新增状态管理
- 当前竞态窗口较短（3-5s），用户通常不会立即发消息，实际影响有限

---

### 方向 5：精简 System Prompt（OpenCode 风格）

**现状问题：**
- AICA System Prompt ~800 行，OpenCode ~106 行
- 大量规则是为了补偿 MiniMax-M2.5 的弱点（anti-hallucination, number consistency, evidence-based analysis 等）
- 但过多规则也消耗 token、增加 LLM 处理负担

**OpenCode 做法：** ~106 行，聚焦风格和方法论，不包含工具特定规则。

**优化方案：**
- 分阶段精简：先去掉已被工具描述覆盖的规则 → 再合并重复规则 → 最后评估每条规则的 ROI
- 保留 MiniMax-M2.5 必需的规则（attempt_completion 调用、anti-hallucination）
- 目标：从 ~800 行降至 ~300 行

**可行性：⭐⭐（中）**
- 风险最高的方向 — Part 2 尝试已证明激进精简会导致行为退化
- 需要逐条规则 A/B 测试，工作量大
- 建议作为长期优化，不急于实施
- 前提条件：方向 1-4 完成后，System Prompt 中的工具相关内容已大幅减少，再评估剩余规则

---

## 三、实施优先级

```
Phase A（立即可做，风险低）:
  方向 3: 移除优先策略偏见（5 行删除）          ← 已复测验证方向正确
  方向 1: 去除 DynamicToolSelector 工具过滤     ← 改动小，OpenCode 验证过

Phase B（需单独测试）:
  方向 2: 移除 AddToolDescriptions              ← 需隔离测试，避免重蹈 Part 2 覆辙
  方向 4: 消除原生描述竞态                       ← 改善首次请求体验

Phase C（长期优化）:
  方向 5: 精简 System Prompt                    ← 逐条测试，工作量大
```

---

## 四、预期效果

| 指标 | 当前 | Phase A 后 | Phase B 后 |
|------|------|-----------|-----------|
| System Prompt tokens | ~8000 | ~7500 | ~5000 |
| 工具选择公平性 | grep_search 偏见残留 | 零偏见 | 零偏见 |
| GitNexus 首选率（代码理解） | ~70% | ~90% | ~95% |
| GitNexus 首选率（代码搜索） | ~20% | ~60% | ~80% |
| 工具可见性 | 按意图过滤（6-15 个） | 始终 15 个（conversation 除外） | 始终 15 个 |
| 原生描述可用率 | ~95%（竞态窗口内降级） | ~95% | ~100% |

---

## 五、风险

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| 去掉工具过滤后 LLM 在简单任务调用不必要的工具 | 低 | 浪费迭代 | conversation 意图仍保留极简工具集 |
| 删除 AddToolDescriptions 后 LLM 行为退化 | 中 | 任务完成率下降 | 单独隔离测试，不同时改其他规则 |
| System Prompt 精简过度 | 中 | 幻觉增加、规范不遵循 | 逐条测试，保留 MiniMax-M2.5 必需规则 |
| 阻塞式 MCP 注册导致 UI 卡顿 | 低 | 用户体验降级 | 加超时（5s），超时后降级到硬编码 |

---

## 六、与 Phase 1 WorkPlan 的关系

- 方向 1-3 属于 D-01 修复的延伸优化，可纳入阶段 3 步骤 3.9（M1 反馈修复窗口）
- 方向 4 属于基础设施改善，可纳入阶段 3 步骤 3.10（集成测试）
- 方向 5 属于长期优化，建议在 M2 交付后作为技术债务处理

---

## 七、总结：从控制型到信任型的迁移路径

5 个方向本质上是同一个设计哲学转变的不同层面：

```
控制型 AICA                              信任型（OpenCode 风格）
┌─────────────────────┐                  ┌─────────────────────┐
│ ClassifyIntent      │  方向 1: 去除     │                     │
│ (关键词规则分类)     │ ──────────────→  │ 不分类               │
├─────────────────────┤                  ├─────────────────────┤
│ SelectTools         │  方向 1: 去除     │                     │
│ (按意图过滤工具)     │ ──────────────→  │ 所有工具始终可见      │
├─────────────────────┤                  ├─────────────────────┤
│ AddToolDescriptions │  方向 2: 移除     │                     │
│ (System Prompt 重复) │ ──────────────→  │ 仅 function calling │
├─────────────────────┤                  ├─────────────────────┤
│ AddGitNexusGuidance │  方向 3: 移除偏见 │                     │
│ (优先策略偏见)       │ ──────────────→  │ 工具描述自驱动       │
├─────────────────────┤                  ├─────────────────────┤
│ 800 行 System Prompt│  方向 5: 精简     │                     │
│ (工具规则为主)       │ ──────────────→  │ ~100 行（方法论为主） │
└─────────────────────┘                  └─────────────────────┘
```

**关键认知：用户的请求是隐式的。** 用户说"这个函数是做什么的"，不会说"请用 gitnexus_context 搜索"。在隐式意图和工具选择之间的推理，LLM 做得比关键词规则好。AICA 的优化方向就是**逐步退出这个推理环节，让 LLM 自己来**。

每一步迁移都可以独立验证、独立回滚，不需要一次性完成。MCP 透传（Part 1）已经奠定了基础 — 原生工具描述是信任型设计的核心依赖。
