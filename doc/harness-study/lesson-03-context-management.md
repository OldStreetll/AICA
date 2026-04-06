# 第三课：Context 管理——压缩 vs 重置

> 日期：2026-04-05
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA v2.1 计划

---

## 为什么 Context 管理是核心战场

上下文窗口有限，长时间 Agent 任务中对话内容快速膨胀（工具调用、多轮迭代、代码内容）。不做管理：
- API 报错超长
- 或"暗中降质"——早期信息被挤出注意力范围

**Context 管理决定了 Agent 能不能跑完任务。**

## 两种核心策略

### Compaction（压缩）

用 LLM 将历史对话总结为更短的摘要，替换原始内容。

- 优点：保留上下文连续性
- 缺点：每次压缩有信息损失，多次压缩后"摘要的摘要"累积失真，且压缩本身消耗 LLM 调用

### Context Reset（上下文重置）

清空整个上下文，启动全新会话，传入结构化的任务状态。

- 优点：上下文完全干净，无累积噪声
- 缺点：依赖"任务状态提取"的质量，丢失对话中的微妙上下文

### 关系

**两者是分层配合，不是二选一。** Anthropic 实际做法：
- Sprint 内部：Compaction 管理上下文
- Sprint 切换时：Reset 清空上下文

**最优策略取决于模型能力**：弱模型更需要 Reset，强模型可以靠 Compaction 撑住。
- Sonnet 4.5：Reset proved essential
- Opus 4.6：Sprint 结构被移除，Compaction 够用

## AICA 分层策略（4 层）

```
第一层：Token 预算管理（预防）
  └─ 85% budget 预警 → 70% condense 触发 → emergency overflow 兜底

第二层：Prune（低成本修剪）
  └─ PruneOldToolOutputs：删除旧工具输出，保护最近 2 轮
  └─ 零 LLM 开销，纯程序化
  └─ v2.1 M1：从循环结束后前移到压缩触发时（先免费手段，再昂贵手段）

第三层：Compaction（LLM 压缩）
  └─ LLM 生成摘要 + 程序化兜底（BuildAutoCondenseSummary）
  └─ 任务边界感知（[TASK_BOUNDARY] 标记）
  └─ 重压缩间隔保护（防短时间内反复压缩）

第四层：工具输出持久化（v2.1 H1 新增）
  └─ 截断不丢弃，存文件后给引用路径
  └─ Agent 可按需 read_file 查看完整输出
```

## 对比

| 策略 | Anthropic | OpenCode | AICA |
|------|----------|---------|------|
| Compaction | 有 | LLM 摘要 + 重放兜底 | LLM 摘要 + 程序化兜底 + 任务边界感知 |
| Context Reset | 核心策略 | 无 | 无（但基础设施已就绪） |
| Prune | 未提及 | 先 prune 再 compact | 有，v2.1 前移时机 |
| 截断持久化 | 未提及 | 有 | v2.1 H1 计划中 |
| 重压缩保护 | 未提及 | 未提及 | 有 |

## Critic 纠正

1. **AICA 已具备 Context Reset 基础设施**：`[TASK_BOUNDARY]` 标记 = Sprint 分界，`TaskProgressStore` 保存编辑文件列表/计划状态/关键发现 = Reset 时需要传递的结构化状态。只是尚未在压缩策略中利用。未来 MiniMax 长上下文表现不佳时，升级为 Reset 是自然的演进方向。

2. **M1 效果需要 telemetry 验证**：Prune 能释放多少 token 取决于工具输出在总 token 中的占比。建议实现 M1 时加 telemetry：记录 Prune 释放量与是否成功避免了后续的 LLM 压缩。

## 术语释义

### Sprint（冲刺）

借用敏捷开发（Scrum）术语。在 Agent Harness 中指**将大任务切分成的一个个小工作周期**，每个 Sprint 有：
- 明确的交付物（做什么）
- 成功标准（做到什么程度算完）
- 独立的上下文（Sprint 切换时可以 Reset）

核心价值：把模型无法一口气完成的大任务，切成能在单次上下文内搞定的小任务。

### Prune（修剪）

英文原意"修剪树枝"。在 Context 管理中指**程序化地删除对话中已经没用的内容**，不调用 LLM。

| | Prune | Compaction |
|---|-------|-----------|
| 做什么 | 直接删除旧的工具输出 | 用 LLM 总结整段对话 |
| 成本 | 零（纯程序操作） | 一次 LLM 调用 |
| 信息损失 | 有（被删的就没了） | 有（摘要不完美） |
| 精度 | 粗粒度（整块删除） | 细粒度（可以保留关键点） |

Prune 的哲学：第 1 轮 grep 出来的 200 行结果，到了第 10 轮大概率没用了——直接删掉，腾出空间。

**Prune + H1 持久化 = 放心剪，随时捡回来。** 删掉的工具输出存在文件里，Agent 需要时可以用 read_file 找回。

## 关键洞察

- Context 管理的核心原则：**先免费手段（Prune），再昂贵手段（Compaction），最后重手段（Reset）**
- AICA 从 Compaction → Reset 的演进路径已就绪（TASK_BOUNDARY + TaskProgressStore）
- 分层策略比单一策略更 resilient
