# Pane 2 输出：MCP 内容结构化吸收 — 排期与依赖分析

> **角色**：Pane 2（排期与依赖分析）
> **日期**：2026-04-07
> **状态**：分析完成

---

## 一、依赖分析

### 1.1 MCP 子任务与现有18任务的依赖关系

| MCP 子任务 | 前置依赖 | 后续受益任务 | 说明 |
|-----------|---------|-------------|------|
| **MCP-A 冗余文件清理** | 无（仅改 GitNexusProcessManager 参数） | 无直接依赖 | 独立修复，用户痛点 |
| **MCP-B McpRuleAdapter** | SK ✅（Rule模型+RuleEvaluator+YamlFrontmatterParser 已就绪） | OH2（记忆升级可复用条件注入思路）、S1（符号检索 intent 匹配已就绪） | 核心组件，将 AGENTS.md 解析为 Rule 对象 |
| **MCP-C McpResourceResolver** | MCP-B（需要 McpRuleAdapter 的结构化解析结果来判断何时加载额外资源） | S1（按需加载 clusters/processes 可丰富符号检索上下文）、S4（Impact 分析可利用 MCP 关系数据） | 增强资源利用 |
| **MCP-D McpServerDescriptor** | MCP-B（需要 McpRuleAdapter 作为参考实现） | 长期扩展性（未来新 MCP 服务器接入） | 可选，当前只有 GitNexus 一个 MCP 服务器 |

### 1.2 前置条件状态

| 前置条件 | 状态 | 说明 |
|---------|------|------|
| SK Skills 系统 | ✅ 已完成 | Rule.cs、RuleLoader.cs、RuleEvaluator.cs、SystemPromptBuilder.cs 全部就绪 |
| RuleEvaluator path glob + intent 匹配 | ✅ 已就绪 | McpRuleAdapter 产出的 Rule 可直接进入现有评估流程 |
| YamlFrontmatterParser | ✅ 已就绪 | 可复用于解析 AGENTS.md 结构化内容 |
| AgentExecutor.InjectMcpResources | ✅ 存在 | 需重构的目标代码已定位（line 1016-1080） |

### 1.3 后续任务受益分析

| 后续任务 | 受益方式 | 受益程度 |
|---------|---------|---------|
| **OH2 记忆升级（Phase 3）** | MCP 内容结构化后，prompt token 优化为 OH2 的记忆 token 分配腾出空间 | 中（间接受益，节省 ~1400 tokens 可用于更多记忆注入） |
| **S1 符号检索（Phase 6）** | McpResourceResolver 按需加载 GitNexus 的 clusters/processes 数据可丰富检索上下文 | 中（MCP 额外资源目前未使用，结构化后可按需加载） |
| **S4 Impact 分析（Phase 7）** | 类似 S1，可利用按需加载的 MCP 关系数据 | 低（S4 主要依赖 GitNexus context tool 而非 resource） |

---

## 二、排期方案

### 方案 A：利用验证窗口弹性时间（推荐）

将 MCP 任务拆散放入验证窗口的弹性时间：

| 子任务 | 放置位置 | 工作量 | 理由 |
|--------|---------|--------|------|
| MCP-A 冗余文件清理 | **立即执行**（Phase 2 R2 间隙） | 0.5 天 | 无依赖、低风险、用户直接痛点 |
| MCP-B McpRuleAdapter | **验证窗口 2（第 11 周）后 3 天** | 2-3 天 | Phase 2 H1 全部完成后，弹性时间刚好可容纳；SK 已完成是充分前置 |
| MCP-C McpResourceResolver | **验证窗口 3（第 18 周）前 2 天** | 1-2 天 | Phase 5 完成后，MCP-B 已就绪 |
| MCP-D McpServerDescriptor | **不排入计划** | — | 当前仅 GitNexus 一个 MCP 服务器，属过度设计，搁置 |

**对总排期影响**：+0 周（利用弹性时间吸收，但弹性缓冲减少约 4-5.5 天）
**风险**：验证窗口弹性时间本用于消化前序 Phase 溢出，如果 Phase 2 或 Phase 4-5 溢出严重，MCP-B/C 会被挤出

### 方案 B：融入 Phase 3 OH2 记忆升级

将 MCP-B 与 OH2 合并实施：

| 子任务 | 放置位置 | 工作量 | 理由 |
|--------|---------|--------|------|
| MCP-A | 立即执行 | 0.5 天 | 同方案 A |
| MCP-B McpRuleAdapter | **Phase 3（第 8-10 周）与 OH2 一起** | +2-3 天（OH2 从 8-10天→10-13天） | 两者都涉及 prompt token 优化，概念相近 |
| MCP-C McpResourceResolver | **Phase 3 尾部** | +1-2 天 | OH2 完成后顺带实现 |
| MCP-D | 不排入 | — | 同方案 A |

**对总排期影响**：+0.5~1 周（Phase 3 从 3 周膨胀到 3.5-4 周，可能挤压验证窗口 2）
**风险**：OH2 已是高风险高工作量任务（8-10天），加码 MCP 增加单 Phase 失败概率；Phase 3 无缓冲

### 方案 C：独立新增为 Phase 2.5

在验证窗口 1（第 4 周）和 Phase 2 之间插入：

| 子任务 | 放置位置 | 工作量 | 理由 |
|--------|---------|--------|------|
| MCP-A | 立即执行 | 0.5 天 | 同方案 A |
| MCP-B + MCP-C | **第 4 周后半段 + 第 5 周前半段** | 3-5 天 | 验证窗口 1 结束后、H1 启动前 |
| MCP-D | 不排入 | — | 同方案 A |

**对总排期影响**：+0.5~1 周（H1 启动延后约 1 周，后续全部顺延）
**风险**：直接推迟 Phase 2 核心任务 H1，H1 是当前最高优先级（Phase 2 R2 进行中）；可能导致总排期从 24-26 周膨胀到 25-27 周

---

## 三、风险评估

### 3.1 对 24-26 周总排期的影响

| 方案 | 排期影响 | 风险等级 |
|------|---------|---------|
| 方案 A（验证窗口） | +0 周（名义上），但弹性缓冲减少 4-5.5 天 | **低** |
| 方案 B（融入 OH2） | +0.5~1 周 | **中** |
| 方案 C（独立 Phase） | +0.5~1 周，且推迟核心任务 | **高** |

### 3.2 单人开发约束下的可行性

- MCP-A（0.5天）：完全可行，改动极小
- MCP-B（2-3天）：可行，SK 基础设施已完备，主要是 AGENTS.md 解析逻辑 + AgentExecutor.InjectMcpResources 重构
- MCP-C（1-2天）：可行但优先级较低，当前 6 个额外 MCP 资源未使用不是紧急痛点
- MCP-D（1天）：不建议，单 MCP 服务器场景下过度设计

**总工作量**：MCP-A + MCP-B + MCP-C = 3.5-5.5 天（不含 MCP-D）
**占总排期比例**：约 3-4%（在 24-26 周中占比小）

### 3.3 不做的风险

| 风险 | 影响 | 紧迫度 |
|------|------|--------|
| 架构分裂持续 | 两套并行 prompt 注入机制（精细 Rules vs 粗暴 MCP dump）长期共存，增加维护成本 | **中**（不影响功能，但违反 SK 设计意图） |
| Token 浪费累积 | 每次请求白白消耗 ~1400 tokens（1950-540），在 MiniMax 4K 窗口下尤其痛 | **中高**（MiniMax 上下文窗口极其珍贵） |
| 冗余文件持续污染 | 用户每次打开项目都看到 AGENTS.md/CLAUDE.md/.claude/ | **高**（用户直接痛点，但 MCP-A 可单独解决） |
| 后续 OH2/S1 无法受益 | OH2 记忆升级时仍需与粗暴的 MCP 注入竞争 token 预算 | **低**（OH2 可独立实施） |

---

## 四、推荐方案

### 推荐：方案 A（利用验证窗口弹性时间），MCP-D 搁置

**具体排期**：

| 时间 | 任务 | 工作量 |
|------|------|--------|
| **本周内**（Phase 2 R2 间隙） | MCP-A 冗余文件清理 | 0.5 天 |
| **第 11 周**（验证窗口 2 后 3 天） | MCP-B McpRuleAdapter | 2-3 天 |
| **第 18 周**（验证窗口 3 弹性时间） | MCP-C McpResourceResolver | 1-2 天 |
| **搁置** | MCP-D McpServerDescriptor | — |

### 推荐理由

1. **不影响主线排期**：MCP 任务全部在弹性时间内完成，不推迟任何现有 18 个任务
2. **MCP-A 应立即做**：0.5 天工作量、无依赖、解决用户直接痛点，没有理由等待
3. **MCP-B 放验证窗口 2 合理**：
   - 此时 Phase 2 H1 全部完成，SK 已完成 3 个多月，前置条件充分成熟
   - 验证窗口 2 本身要评估 H1 截断策略效果和 OH2 方向，MCP-B 的 token 优化数据可同时收集
   - 即使弹性时间被前序溢出占用，MCP-B 可顺延到 Phase 3 启动周的前 2 天
4. **MCP-C 不急**：额外 MCP 资源（clusters/processes）未使用不是当前痛点，放到验证窗口 3 作为低优先级填充
5. **MCP-D 搁置正确**：单 MCP 服务器场景下通用化是 YAGNI（You Aren't Gonna Need It），等实际有第二个 MCP 服务器时再做

### 应急预案

如果验证窗口 2 弹性时间被前序溢出全部占用：
- **MCP-B 顺延到 Phase 3 第 8 周前 2 天**（OH2 启动前），此时仍不影响 OH2 排期
- 最差情况：MCP-B 推迟到验证窗口 3，MCP-C 则推迟到收尾阶段（第 23-24 周）

---

## 五、关于讨论文档中待讨论问题的回应

| # | 问题 | 排期视角回应 |
|---|------|-------------|
| 1 | MCP 是否应优先于 Phase 2 R2（H1 剩余5工具）？ | **否**。H1 是 v2.1 核心任务，截断持久化影响所有工具，优先级远高于 token 优化。MCP-A 可并行做（0.5天间隙），MCP-B/C 应等 H1 完成后 |
| 2 | 方案选择？ | 方案 A（验证窗口弹性时间），理由见第四节 |
| 3 | MCP-A 可否立即执行？ | **是**。0.5天、无依赖、无风险，建议本周 Phase 2 R2 工作间隙完成 |
| 4 | McpRuleAdapter 的 RuleSource 设计？ | 建议新增 `RuleSource.Mcp`（Priority=5，低于 Builtin:0 → 高于无，低于 Workspace:20），使 MCP 规则可被用户 .aica-rules/ 覆盖 |
| 5 | 新增任务是否影响总排期？ | 方案 A 下名义上 +0 周，但消耗 4-5.5 天弹性缓冲。可接受 |
| 6 | McpServerDescriptor 是否过度设计？ | **是**。单 MCP 服务器场景下，搁置是正确决策 |
