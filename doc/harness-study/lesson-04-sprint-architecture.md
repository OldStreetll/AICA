# 第四课：Sprint 架构与任务分解

> 日期：2026-04-05
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA v2.1 计划

---

## 核心问题

复杂任务超出 LLM 单次规划的能力边界。人类工程师不会一口气从第一行写到最后一行——Sprint 架构把这种自然工作方式编码进了 Harness。

## Anthropic 的 Sprint 架构

### 完整流程

```
用户需求（1-4 句话）
     │
  Planner → 产品规格 + Sprint 列表（有雄心但不指定实现）
     │
  Sprint 1：
    1. Generator + Evaluator 协商 Sprint 契约（成功标准）
    2. Generator 实现
    3. Generator 自评 → 提交
    4. Evaluator 交互式测试 → 通过/打回
    5. 通过 → git commit
     │
  Context Reset
     │
  Sprint 2 ...（干净上下文，同样流程）
```

### Sprint 契约

在 Generator 动手**之前**协商的成功标准，解决两个问题：

| 问题 | 说明 |
|------|------|
| **Goldplating（镀金）** | Generator 不停加功能做超出范围的事。契约划定"做到这里就停" |
| **Moving Goalposts（移动球门柱）** | Evaluator 用越来越高的标准评判。契约把标准固定在开始时刻 |

### Planner 的设计哲学

Planner 应该让**产品愿景**更有雄心，但**不指定实现细节**（"without over-specifying implementation"）。LLM 在有明确目标时表现最好，有雄心的明确目标比保守的模糊目标更能激发全部能力。

注意：这一设计哲学依赖强模型的规划能力。弱模型的 Planner 应保守聚焦于不出错。

## Sprint 的本质：分治

```
复杂度管理 = 任务分解（Planner）
           + 聚焦执行（Generator per Sprint）
           + 质量把关（Evaluator per Sprint）
           + 状态隔离（Context Reset between Sprints）
```

- 弱模型：四要素都需要
- 强模型：可逐步简化（Opus 4.6 移除了 Sprint 结构和 Context Reset）
- **Evaluation 是最后被移除的组件**——始终有价值

关键原则："Every component encodes assumptions worth stress-testing." 每个组件都编码了某个假设，假设失效时组件就该被移除。

## 模型演进对 Sprint 的影响

| 模型 | Sprint 是否必要 | 原因 |
|------|---------------|------|
| Sonnet 4.5 | 必要 | 长上下文连贯性差，需要频繁 Reset |
| Opus 4.6 | 可移除 | 能在更长上下文中保持连贯 |

## AICA 映射

| Sprint 要素 | AICA 现状 | 差距与建议 |
|------------|----------|----------|
| 任务分解 | PlanAgent（只读工具、16K、60s） | 输出粒度偏细（指定实现步骤），应改为输出"效果目标" |
| 聚焦执行 | AgentExecutor 主循环 | 已有 |
| 质量把关 | VS Error List（编译级） | 无语义级评估，待 ReviewAgent 补充 |
| 状态隔离 | TASK_BOUNDARY + TaskProgressStore | 基础设施已有，未激活为 Reset |
| Sprint 契约 | 无 | S5 任务模板化是轻量替代；建议 PlanAgent 输出加入每步成功标准 |

### AICA 约束

- 20 并发限制 → 多 Sprint 的 Generator-Evaluator 多轮迭代太贵
- 高延迟 → Sprint 切换的 Reset + 重建上下文开销大
- 弱模型 → PlanAgent 规划本身可能出错，应保守而非有雄心

### 可借鉴的思想

Sprint 的思想可以轻量化借鉴：让 PlanAgent 不只输出"计划步骤"，还输出每步的**成功标准**（轻量版 Sprint 契约），无需引入完整的多 Sprint 循环。

## Critic 纠正

1. Planner 提升雄心的是产品愿景，不是实现细节。PlanAgent 应输出"做到什么效果"而非"怎么做"
2. 四要素不是永恒缺一不可。模型越强需要的脚手架越少，但 Evaluation 始终有价值
3. 弱模型的 PlanAgent 应保守聚焦于不出错，而非追求有雄心

---

## 专题：AICA 计划执行偏离问题分析

AICA 的 PlanAgent 能制定计划，但主 Agent 往往执行到某一步后发现需要准备而退出计划。

### 6 个原因

#### 模型侧

| # | 原因 | 说明 |
|---|------|------|
| 1 | 弱模型规划缺少真实依赖分析 | MiniMax 推理深度不足以在只读信息基础上预判所有依赖，计划是"看起来合理的幻觉" |
| 2 | 近因偏差 + lost in the middle | 新信息权重压过计划文本；计划位于上下文早期位置，被多轮工具输出淹没后注意力下降 |

#### 系统侧

| # | 原因 | 说明 |
|---|------|------|
| 3 | 计划只是文本注入，无系统级执行保障 | 没有检测偏离和强制回归的机制，对比 Anthropic Sprint 契约绑定 Evaluator |
| 4 | 计划粒度太细，前提脆弱 | 指定"修改 foo.cpp 第 42 行"级别的步骤，一个假设不成立全盘崩塌 |
| 5 | 发现意外后缺乏"拉回"机制 | Agent 进入自由探索模式，计划被遗忘 |
| 6 | PlanAgent 只读工具集无法验证可行性 | 计划中某些步骤在制定时就不可行，执行时才暴露 |

### 潜在对策方向

| 对策 | 针对原因 | 说明 |
|------|---------|------|
| 每轮重复注入当前步骤目标 | 2 | 在 System Prompt 或工具返回中重复当前步骤，对抗 lost in the middle |
| 分层计划 | 1, 4 | 粗粒度目标（不容易失效）+ 每步开始前细粒度探查（基于真实代码状态） |
| 计划偏离检测 + 拉回机制 | 3, 5 | 系统级检测当前操作是否偏离计划，偏离时注入提醒 |
| 接受规划不完美 | 1, 6 | 弱模型规划必然有盲区，系统设计应容忍计划修正而非假设计划完美 |
