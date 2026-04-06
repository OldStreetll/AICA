# Harness Engineering 学习笔记

> 学习日期：2026-04-05 ~ 2026-04-06
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA v2.1 对标分析 + OpenHarness 架构分析
> 方法：三角色教学（主讲 Prof + 助教 TA + 纠错员 Critic）

## 课程索引

| # | 课程 | 核心概念 | 文件 |
|---|------|---------|------|
| 1 | [Harness 是什么](lesson-01-what-is-harness.md) | 5 维度 + Prompt 横切 + 天花板/地板模型 | lesson-01 |
| 2 | [Generator-Evaluator 分离](lesson-02-generator-evaluator.md) | 独立上下文 + 交互验证 + Sprint 契约 | lesson-02 |
| 3 | [Context 管理](lesson-03-context-management.md) | Prune → Compaction → Reset 分层策略 | lesson-03 |
| 4 | [Sprint 架构与任务分解](lesson-04-sprint-architecture.md) | 分治四要素 + 计划执行偏离分析 | lesson-04 |
| 5 | [评估标准工程](lesson-05-criteria-engineering.md) | 失败反推维度 + 权重反向补偿 + 校准 | lesson-05 |
| 6 | [演进与简化](lesson-06-evolution-simplification.md) | 假设清单 + 4 阶段生命周期 + Telemetry | lesson-06 |
| 7 | [AICA 实战映射](lesson-07-aica-mapping.md) | 五维度全景 + 行动计划 + v2.1 调整 | lesson-07 |

## 一句话总结

**Harness Engineering = 一切让 Agent 产出趋于稳定、保障输出质量的非 LLM 手段。模型决定天花板，Harness 决定地板。**
