# 第六课：演进与简化——假设的保质期

> 日期：2026-04-05
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA 现状分析

---

## 核心原则

> "Every component encodes assumptions worth stress-testing."

好的 Harness 不是越复杂越好，而是**恰好匹配当前模型能力和任务需求的最简系统**。

## Anthropic 的实证

模型从 Sonnet 4.5 → Opus 4.6，逐个移除 Harness 组件的结果：

| 组件 | 编码的假设 | Sonnet 4.5 | Opus 4.6 |
|------|----------|-----------|---------|
| Sprint 分解 | 模型无法在长上下文中保持连贯 | 必要 | 可移除 |
| Context Reset | 压缩后质量不可接受 | 必要 | 可简化 |
| 多轮评估迭代 | 单轮评估不够 | 必要（5-15 轮） | 可减少 |
| Evaluator 本身 | 自评不可靠 | 必要 | **仍然必要** |

**Evaluation 是 Harness 中最持久的价值——最后一个被保留的组件。**

## 假设清单思维

每加入一个 Harness 组件，同时记录三件事：

```
组件名称：XXX
编码的假设：模型会/不会做 YYY
验证方式：统计 ZZZ / 关闭组件对比
失效信号：指标降到/升到 NNN
```

失效信号出现时 → 不立即拆除，而是进入压力测试（关闭组件运行一段时间，确认无回归）。

## Harness 生命周期（4 阶段）

```
阶段 1：搭建（Building）
  模型能力弱 → 每个弱点对应一个 Harness 组件
  风险：过度工程化

阶段 2：运行（Operating）
  通过 telemetry 收集各组件的触发频率和效果数据
  积累"哪些组件真正有用"的证据

阶段 3：简化（Simplifying）
  模型升级 → 部分假设失效 → 逐步移除
  风险：过早移除、过晚移除

阶段 4：重组（Restructuring）
  不只是拆除，是把刚性结构替换为弹性策略
  例：固定 Sprint 分解 → 动态决定是否需要分解
  例：固定 3 次 Doom Loop → 根据重复率动态调整阈值
```

大多数团队只做阶段 1。正确做法：**搭建时就记录假设，为未来的简化和重组做数据准备。**

## AICA 已有组件的假设清单

| 组件 | 编码的假设 | 验证方式 | 失效信号 |
|------|----------|---------|---------|
| 工具别名修正 | MiniMax 幻觉工具名 | 统计修正触发率 | 触发率 <1% |
| Doom Loop 检测 | MiniMax 陷入重复调用 | 统计触发频率 | 频率接近 0 |
| DynamicToolSelector | 按意图过滤工具集可节省 token | 关闭过滤后对比任务完成率 | 完成率无差异 |
| 6 级模糊匹配 | 6 级足够覆盖 MiniMax 编辑输出 | 统计各级匹配命中分布 | 大量落到第 6 级仍失败 |
| PlanAgent 16K budget | 16K 足以规划大多数任务 | 统计规划被截断的频率 | 截断率 >20% |

## v2.1 新组件应记录的假设

| 新组件 | 编码的假设 | 失效信号 |
|--------|----------|---------|
| S5 任务模板化 | 模板指导能改善工具调用序列 | 注入模板前后任务完成率无差异 |
| H1 截断持久化 | Agent 会回头查看截断输出 | read_file 对截断文件的调用频率接近 0 |
| S3 头文件同步检测 | MiniMax 看到警告后会修改头文件 | 警告后实际修改率 <30% |
| ReviewAgent | MiniMax 能基于检查清单给出有用审查 | 审查意见的采纳率 <20% |

## Critic 纠正

1. **不只是简化，还有重组**（刚性结构 → 弹性策略）。这是第四阶段，不能归入简化。
2. **AICA 当前首要任务是搭建**，简化思维不应阻碍搭建进度。正确做法是搭建时记录假设，不是现在就开始拆。
3. **假设验证依赖 telemetry 基础设施**。AICA Token 层有 EMA 校准，但工具调用层的 telemetry（调用频次、别名修正触发率、匹配命中分布）是否就绪需要评估。没有 telemetry，假设清单就只是文档。

---

## 专题：AICA Telemetry 方案

假设清单思维的落地依赖数据采集。AICA 的约束（涉密离线、VS2022 VSIX）要求零外部依赖的方案。

### 三层架构

#### L1：结构化日志（最小成本）

利用已有的 ToolExecutionPipeline Monitoring 中间件，每次工具调用记录一条结构化记录：

```json
{
  "timestamp": "2026-04-06T10:23:45",
  "session_id": "abc123",
  "tool_requested": "edti_file",
  "tool_resolved": "edit_file",
  "alias_corrected": true,
  "fuzzy_match_level": 0,
  "iteration": 7,
  "token_before": 45000,
  "token_after": 48200,
  "result": "success",
  "duration_ms": 1200
}
```

- 存储：`~/.AICA/telemetry/YYYY-MM-DD.jsonl`（逐行追加）
- 轮转：保留最近 30 天详细日志，更早只保留会话摘要

#### L2：会话级聚合摘要

每个会话结束时程序化生成：

```json
{
  "session_id": "abc123",
  "date": "2026-04-06",
  "total_iterations": 12,
  "tool_calls": 34,
  "alias_corrections": 3,
  "doom_loop_triggers": 0,
  "fuzzy_match_distribution": { "L1": 28, "L2": 4, "L3": 1, "L4": 1, "L5": 0, "L6": 0 },
  "prune_events": 1,
  "prune_tokens_freed": 8200,
  "compaction_events": 0,
  "plan_created": true,
  "plan_steps_total": 5,
  "plan_steps_completed": 3,
  "plan_abandoned": true,
  "total_tokens_in": 120000,
  "total_tokens_out": 35000
}
```

- 存储：`~/.AICA/telemetry/sessions/{id}.json`

#### L3：周期性分析报告（离线脚本，可选）

读取 L1+L2 数据，生成假设验证周报，直接回答"哪些 Harness 组件在真正起作用"。

### 两层采集点

| 层 | 位置 | 采集内容 |
|----|------|---------|
| 通用采集 | Monitoring 中间件 | 工具调用、别名修正、匹配级别、token、耗时 |
| 专项埋点 | 各功能代码中 | 截断文件读取率、审查意见采纳率、头文件警告响应率 |

### 量化数据的局限

量化数据是必要但不充分的。有些维度难以量化：
- 用户满意度
- 计划质量（完成率低可能是任务太难而非计划差）
- 逻辑正确性（编译通过 ≠ 逻辑正确）

建议：会话摘要中加可选的用户主观评分（1-5）作为补充。

### Telemetry 自身的假设

Telemetry 系统也是一个 Harness 组件，编码的假设是"量化数据能指导 Harness 演进"。需要同样的审视态度。

## 关键收获

- Harness Engineering = 搭建 + 修剪，不只是往上加东西
- 每个组件都有保质期，由它编码的假设决定
- 搭建时记录假设 → 运行时收集数据 → 假设失效时简化或重组
- Evaluation 是最持久的 Harness 组件
- 数据采集方案：利用已有中间件 + JSONL 本地存储 + 会话聚合 + 离线分析
