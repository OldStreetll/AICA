# 第七课：AICA 实战映射——从学习到行动

> 日期：2026-04-06
> 来源：前六课知识体系 + AICA v2.1 计划 + OpenHarness 分析

---

## 六课知识体系回顾

```
第一课：Harness = 让 Agent 产出稳定的非 LLM 基础设施（5 维度 + Prompt 横切）
第二课：Generator-Evaluator 分离 → 独立上下文 + 交互验证 + 预定标准
第三课：Context 管理 → 分层策略：Prune → Compaction → Reset
第四课：Sprint 架构 → 分治：分解 + 聚焦 + 把关 + 隔离
第五课：评估标准工程 → 失败模式反推维度 + 权重反向补偿 + 迭代校准
第六课：演进与简化 → 假设清单 + telemetry + 4 阶段生命周期
```

## AICA Harness 五维度全景

### 维度 1：Agent Loop ✅ 成熟

- AgentExecutor 主循环、Doom Loop 检测、工具别名修正、finish_reason 驱动
- **行动项**：无功能变更，随组件开发加 telemetry 埋点

### 维度 2：Tool System ✅ 成熟

- ToolExecutionPipeline 6 层中间件、DynamicToolSelector
- **行动项**：H1 工具输出持久化 + Monitoring 层 telemetry

### 维度 3：Context Management ⚠️ 可优化

- 已有：Token 预算（85%/70%/emergency）、Prune（时机不对）、Compaction（双兜底）
- 潜力：TASK_BOUNDARY + TaskProgressStore = Context Reset 的基础设施（未激活）
- **行动项**：M1 Prune 前移 + H1 截断持久化

### 维度 4：Evaluation ⚠️ 缺 Agent 级语义评估

已有评估能力（不是空白）：
- 编译级：VS Error List 轮询 + DiagnoseEditFailure()
- 人类级：用户确认流程（diff 预览 + 手动修改检测）

待建：
- Agent 级语义评估：ReviewAgent + 检查清单

**行动项**：
1. SubAgent 泛化 → ReviewAgent 实例化
2. 检查清单式评估标准（5 个防守维度：一致性、安全性、范围控制、规范性、可读性）
3. S5 任务模板加入成功标准字段
4. 保留并强化用户确认流程

### 维度 5：Memory & State ⚠️ 需升级

- 已有：MemoryBank（全量拼接）、TaskProgressStore
- **行动项**：结构化记忆升级（相关性检索）+ H2 快照 + H3 权限持久化

## 横切关注点：Prompt Engineering

| 位置 | 当前 | 改进方向 |
|------|------|---------|
| System Prompt | 硬编码 guidance | Skills 系统外部化 |
| 记忆注入 | 全量拼接 | 相关性检索 + 动态 top N |
| 评估 Prompt | 不存在 | ReviewAgent 检查清单 Prompt |
| 计划注入 | 一次性注入后被淹没 | 每轮重复注入当前步骤目标 |

## 三个调整方向（融入现有 v2.1 计划）

这些调整不是独立排期的新任务，而是在现有 v2.1 路线图的节奏中融入新视角：

### 1. Telemetry 随组件自然生长

不独立排期。每做一个新组件时顺带加埋点：
- 做 H1 → 加截断文件读取率埋点
- 做 M1 → 加 Prune 释放量和避免压缩率埋点
- 做 ReviewAgent → 加审查采纳率埋点

### 2. 成功标准概念融入 S5

S5 任务模板本来就在 Phase 1。在模板中增加"成功标准"字段，即轻量版 Sprint 契约：
```
模板：bug_fix
  步骤：定位 → 修复 → 验证
  成功标准：编译通过 + 原始问题不再复现
```

### 3. PlanAgent 输出优化融入日常迭代

- 输出粗粒度目标 + 每步成功标准（而非具体实现步骤）
- 每轮迭代中重复注入当前步骤目标（对抗 lost in the middle）

## Critic 纠正

1. **Evaluation 不是"空白"**：编译级(Error List) + 人类级(用户确认) 已有。缺的是 Agent 级语义评估。评估体系是在已有基础上补充，不是从零开始。
2. **Telemetry 不必独立排期**：AICA 当前在阶段 1（搭建），组件还不够多，独立采集数据样本有限。随组件自然生长更务实。
3. **单人开发约束**：新视角应融入现有 v2.1 节奏，不作为额外任务排期。

## 核心收获

Harness Engineering 六课的知识不是要推翻 v2.1 计划，而是为现有计划注入新视角：

- **每个组件记录假设和失效信号**（第六课）
- **Telemetry 随建随加**（第六课专题）
- **评估从检查清单起步**，不追求完整 Evaluator（第五课）
- **保留人类审查作为最后保障**（第五课 Critic）
- **PlanAgent 输出"做到什么效果"而非"怎么做"**（第四课）
- **先免费手段再昂贵手段**（第三课 Context 分层原则）
- **计划每轮重复注入**对抗 lost in the middle（第四课专题）
