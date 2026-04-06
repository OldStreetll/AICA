# 第一课：Harness 是什么？为什么需要它？

> 日期：2026-04-05
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA 对标分析

---

## 核心定义

**Harness 工程 = 一切让 Agent 产出趋于稳定、保障输出质量的非 LLM 手段。**

## 五大维度

| # | 维度 | 说明 | AICA 现状 |
|---|------|------|----------|
| 1 | **Agent Loop（循环）** | 驱动 LLM 反复思考-行动的控制循环 | AgentExecutor, finish_reason 驱动，成熟 |
| 2 | **Tool System（工具设计与调用）** | 工具集定义、执行管道、安全保障 | ToolExecutionPipeline 6 层中间件 + 工具别名修正，成熟 |
| 3 | **Context Management（上下文窗口）** | token 预算、压缩、重置策略 | 85% budget / 70% condense / Prune，成熟但可优化 |
| 4 | **Evaluation（评估系统）** | 对 LLM 产出的质量检查机制 | 空白（v2.1 计划中 SubAgent/ReviewAgent 待建） |
| 5 | **Memory & State（记忆）** | 跨轮次、跨会话的信息持久化 | MemoryBank 已有，但需升级（全量拼接→相关性检索） |

## 核心公式

```
产出质量 = f(模型能力 × Harness 质量)
模型 → 决定天花板
Harness → 决定地板
```

## 实验证据（Anthropic）

| 方式 | 时间 | 成本 | 结果 |
|------|------|------|------|
| Opus 4.5 裸跑 | 20 min | $9 | 界面好看，核心逻辑全坏 |
| Opus 4.5 + Harness | 6 h | $200 | 完整可用的游戏编辑器 |

注意：Harness 不是免费提升，本质是用更多计算换更好的组织。

## 两个根本瓶颈（单靠模型无解）

1. **Context Degradation** — 对话越长，模型越乱。部分模型出现 context anxiety（快到上下文极限时草草收工，Sonnet 4.5 上观察到，是否为普遍现象需验证）
2. **Self-Evaluation Bias** — Agent 评价自己的产出总是过度自信，尤其在主观任务上

## AICA 定位

**弱模型（MiniMax-M2.5）+ 强系统 = 可用产品**

- 优势：Loop / Tool / Safety 成熟
- 短板：Evaluation 维度空白，Memory 需从全量拼接升级为相关性检索
- 约束：20 并发 + 高延迟 + 离线环境 → 不能照搬 Anthropic 的高成本多轮方案

## 横切关注点：Prompt Engineering

**Prompt Engineering 不是 Harness 的第六个维度，而是贯穿五大维度的横切关注点。**

它在任何模块被调用时都可以按需注入到 Agent 中，覆盖三个时态：

| 时态 | 说明 | 示例 |
|------|------|------|
| 设计时 | 静态定义，编码阶段确定 | 工具描述（name/description/参数说明）、评估标准定义 |
| 启动时 | 会话开始时组装 System Prompt | AICA `SystemPromptBuilder` 组装角色定义、规则、工具集 |
| 运行时 | Agent 循环中按需动态注入 | Memory 按相关性注入、诊断追加到 ToolResult、任务模板条件注入 |

**关键洞察**：Harness 把 Prompt Engineering 从一项模糊的技巧，升级为有明确目标、可迭代、可验证的工程实践。在 Harness 语境下，Prompt Engineering 被具体化为"设计评估标准"、"校准评估者"、"定义 Sprint 契约"等具体工程活动。

## 关键术语

| 术语 | 含义 |
|------|------|
| Harness | LLM 外围的系统基础设施总称 |
| Context Degradation | 上下文变长后模型连贯性下降 |
| Context Anxiety | 模型感知上下文将满时提前草草收工（模型特异性） |
| Self-Evaluation Bias | Agent 对自身输出过度自信 |
| Compaction | 对话压缩（LLM 总结历史） |
| Context Reset | 清空上下文重启，传入结构化摘要 |
