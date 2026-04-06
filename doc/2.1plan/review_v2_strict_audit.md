# AICA v2.1 Unified Plan v2.0 — 第二轮严格审查

> **审查人**：Claude Instance A（Session 1, Pane 0.0）
> **日期**：2026-04-06
> **目标**：验证 v1.0 问题是否收敛，v2.0 是否引入新矛盾
> **方法**：逐项对照 v1.0 审查意见 + 内部一致性交叉验证

---

## 一、v1.0 问题收敛检查

### 问题 1：排期不现实 → ✅ 已解决

v1.0 估算 16 周。v2.0 调整为 22-24 周。

**验证**：重新计算 v2.0 工作量（取中值）：
- Phase 0: 2.5 天
- Phase 1: 2.5 + 5 + 6.5 = 14 天
- 验证窗口1: 2天实际工作（SK收尾）+ 3天反馈 = 5天
- Phase 2: 12.5 + 4 = 16.5 天
- Phase 3: 9 + 5 = 14 天
- 验证窗口2: 5天
- Phase 4: 12.5 + 5 = 17.5 天
- Phase 5: 5 + 2.5 + 4.5 = 12 天
- 验证窗口3: 5天
- Phase 6: 9 + 6.5 = 15.5 天
- Phase 7: 5 + 2.5 = 7.5 天
- 收尾: 10天

**小计**: 2.5 + 14 + 5 + 16.5 + 14 + 5 + 17.5 + 12 + 5 + 15.5 + 7.5 + 10 = **124.5 天 = 24.9 周**

横切项：17个任务 × 1天(T1+AS1+FF1) = **17 天**

**总计 ≈ 141.5 天 = 28.3 周**

**问题**：v2.0 声称 22-24 周，但精确计算是 **约 28 周**。原因：
1. 验证窗口 3 个 × 5 天 = 15 天，占 3 周
2. 收尾 2 周
3. 横切项 17 天未被充分吸收

**结论**：排期改善了方向（不再是不现实的 16 周），但 22 周仍然偏乐观。**更准确的估算是 24-26 周**。建议将"22 周目标 / 24 周上限"调整为"**24 周目标 / 28 周上限**"。

不过，这里有一个重要细微之处：文档声称"每周额外隐含工作 T1+AS1+FF1 已包含在各任务工作量中"。如果横切项确实已计入各任务的工作量估算（而非额外叠加），则实际为 124.5 天 = 24.9 周，与 24 周上限基本吻合。**需要明确**：各任务的工作量估算是否已包含横切项？如果是，24 周上限可行；如果否，需调整为 28 周。

### 问题 2：Phase 4 过载 → ✅ 已解决

v1.0 Phase 4: H2(10-15) + OH5(5-8) + S3(3-5) = 18-28天 / 15天可用

v2.0 Phase 4: H2(10-15) + H3b(5) = 15-20天 / 15天可用

- S3 前移到 Phase 2 ✅
- H3b 分入减轻了负担 ✅
- 取中值 17.5 天 vs 15 天可用，仍有 2.5 天溢出，但在可控范围内

**结论**：基本解决。H2 取下限(10天) + H3b(5天) = 15天刚好。取中值有轻微溢出但不严重。

### 问题 3：Phase 3 无缓冲 → ✅ 已解决

v1.0 Phase 3: OH2(5-8) + H3(10) = 15-18天 / 15天可用

v2.0 Phase 3: OH2(8-10) + H3a(5) = 13-15天 / 15天可用

OH2 工作量虽然上调了，但 H3 拆分后只留 H3a(5天)。13-15天 / 15天，有 0-2 天缓冲。

**结论**：已解决。缓冲虽小但存在。

### 问题 4：ReviewAgent 价值存疑 → ✅ 已解决

- 缩减为单维度 PoC ✅
- 移除与 S3 重叠的"一致性"维度 ✅
- 默认手动触发（并发保护）✅
- 明确的 PoC 验证策略和退出条件 ✅

**结论**：完全解决。PoC 策略务实。

### 问题 5：T1 Telemetry 需前置 → ✅ 已解决

新增 Phase 0 (T1-infra, 2-3天) ✅

**结论**：已解决。

### 问题 6：缺乏集成测试策略 → ⚠️ 部分解决

v2.0 在收尾阶段（第23-24周）提到"全量集成测试"，第10周也有"集成测试(1d)"。但：
- 没有明确 EditFileTool 回归测试套件的建立时间点
- 只有 Phase 3 显式安排了集成测试时间，其他 Phase 没有

**结论**：方向正确，但需要在每个 Phase 末尾显式标注集成测试时间。

### 问题 7：Skills 激活机制未定义 → ✅ 已解决

明确了被动注入（主要）+ 主动调用（备用）✅

### 问题 8：DynamicToolSelector 缺少 Telemetry 前提 → ✅ 已解决

SK 步骤 8 延后到 T1 基线数据就绪后 ✅
Phase 2 开始时设置评估点 ✅

### 问题 9：缺乏用户反馈回路 → ✅ 已解决

3 个验证窗口（第4/11/18周）✅
每个窗口有明确的评估指标和决策点 ✅

### 问题 10：MiniMax 并发约束 → ✅ 已解决

ReviewAgent 默认手动触发 ✅

### 问题 11：Hooks 与中间件关系 → ✅ 已解决

明确了 Hook 在管道中的位置（中间件之后）✅
前置任务：审视未激活中间件 ✅

### 问题 12：EditFileTool 集成点文档化 → ✅ 已解决

PostEditPipeline + Order 值 + 跨Phase插入点表格 ✅

**v1.0 问题收敛总结**：12 个问题中 10 个完全解决，1 个基本解决（排期需微调），1 个部分解决（集成测试需加强）。

---

## 二、v2.0 内部一致性验证

### 检查 1：依赖图 vs Phase 排列

| 依赖关系 | Phase 排列 | 是否满足？ |
|----------|-----------|-----------|
| OH5 → 建议在 H1 之后 | H1=Phase 2, OH5=Phase 5 | ✅ |
| OH3 Agent Hook → OH5 | OH5=Phase 5, OH3=Phase 6 | ✅ |
| PA1 → OH5 SubAgent | OH5=Phase 5, PA1=Phase 5（同Phase） | ⚠️ 需确认顺序 |
| S1 → SK 意图分类 | SK=Phase 1, S1=Phase 6 | ✅ |
| S4 → SK 意图分类 | SK=Phase 1, S4=Phase 7 | ✅ |
| S2 → H1 截断基础设施 | H1=Phase 2, S2=Phase 5 | ✅ |
| T2 → T1 日志数据 | T1=Phase 0, T2=Phase 7 | ✅ |
| SK步骤8 → T1-infra | T1-infra=Phase 0, SK步骤8=Phase 2评估点 | ✅ |

**问题发现**：PA1 和 OH5 都在 Phase 5。PA1 依赖 OH5（需要 SubAgent 基类重构后再改 PlanAgent）。周计划中：
- 第15周：OH5 SubAgent 泛化 (3d)
- 第16周：OH5 ReviewAgent PoC (2d) + PA1 (3d)

PA1 在第16周开始，OH5 SubAgent 基类泛化在第15周完成。**顺序正确**，PA1 在 SubAgent 泛化之后。但 PA1 和 ReviewAgent PoC 在第16周并行，这意味着 PlanAgent 改造和 ReviewAgent 开发同时进行——两者都修改 SubAgent 相关代码，可能产生冲突。

**建议**：将第16周改为先完成 ReviewAgent PoC(2d)，再开始 PA1(3d)，避免并行修改 SubAgent 相关文件。

### 检查 2：PostEditPipeline Order 值 vs 排序逻辑

| Order | Step | 预期位置 |
|-------|------|---------|
| 100 | M3 FormatStep | 首位 |
| 200 | S3 HeaderSyncStep | M3 之后 |
| 300 | S4 ImpactStep | S3 之后 |
| 400 | H1 TruncationStep | S4 之后 |
| 500 | S2 BuildStep | 末尾（异步） |

与排序图对照：
```
② M3 格式化 → ③ S3 头文件同步 → ④ S4 Impact → ⑤ H1 截断持久化 → ⑥ S2 后台构建
```

**一致**。Order 值正确反映了排序逻辑。✅

但注意：排序图中⑤位置的描述是"输出超限→存文件"，而 H1 的 TruncationStep 应该是**判断+截断**所有前序 step 追加到 ToolResult 的内容。S3 和 S4 追加的警告信息也应纳入截断判断范围。这在 v1.0 的"跨 Phase 集成提醒"中已提到，v2.0 通过 PostEditPipeline 的 Order 机制自然保证了。✅

### 检查 3：周计划 vs 任务工作量

| 周 | 安排 | 计划天数 | 任务估算（中值） | 差异 |
|----|------|---------|----------------|------|
| 1 | T1-infra + M1 | 5d | 2.5 + 2.5 = 5d | ✅ 吻合 |
| 2 | M3 + PostEditPipeline | 5d | 5d | ✅ 吻合 |
| 3 | SK | 5d | 6.5d（中值） | ⚠️ SK中值6.5天需溢出到第4周 |
| 4 | SK收尾 + 验证窗口 | 5d | 1.5d + 3d反馈 | ✅ 合理 |
| 5-7 | H1 + S3 | 15d | 12.5 + 4 = 16.5d | ⚠️ 溢出1.5天 |
| 8-10 | OH2 + H3a | 15d | 9 + 5 = 14d | ✅ 有缓冲 |
| 11 | 验证窗口2 | 5d | 反馈收集 | ✅ |
| 12-14 | H2 + H3b | 15d | 12.5 + 5 = 17.5d | ⚠️ 溢出2.5天 |
| 15-17 | OH5 + PA1 + S2 | 15d | 5 + 2.5 + 4.5 = 12d | ✅ 有缓冲 |
| 18 | 验证窗口3 | 5d | 反馈收集 | ✅ |
| 19-21 | OH3 + S1 | 15d | 9 + 6.5 = 15.5d | ⚠️ 勉强 |
| 22 | S4 + T2 | 5d | 5 + 2.5 = 7.5d | ⚠️ 溢出2.5天 |
| 23-24 | 收尾 | 10d | 集成测试+修复 | ✅ |

**问题**：
1. **第22周（Phase 7）溢出**：S4(4-6天中值5天) + T2(2-3天中值2.5天) = 7.5天，但只有5个工作日。这意味着 Phase 7 实际需要 1.5 周而非 1 周。
2. **多个 Phase 取中值均有轻微溢出**（Phase 2 溢出1.5天，Phase 4 溢出2.5天，Phase 6 勉强持平）

**建议**：Phase 7 应扩展为 1.5-2 周（第22-23周），收尾延后到第24-25周。或者将 T2 移到收尾阶段（因为 T2 是独立的，不影响其他功能）。

### 检查 4：Feature Flags 一致性

文档中列出的 Feature flags：

| Flag | 任务 | 存在？ |
|------|------|--------|
| features.pruneBeforeCompaction | M1 | ✅ |
| features.autoFormatAfterEdit | M3 | ✅ |
| features.skillsEnabled | SK | ✅ |
| features.taskTemplatesEnabled | SK | ✅ |
| features.truncationPersistence | H1 | ✅ |
| features.headerSyncDetection | S3 | ✅ |
| features.structuredMemory | OH2 | ✅ |
| features.permissionFeedback | H3a | ✅ |
| features.fileSnapshots | H2 | ✅ |
| features.permissionPersistence | H3b | ✅ |
| features.reviewAgent | OH5 | ✅ |
| features.reviewAgentAutoTrigger | OH5 | ✅ |
| features.planAgentOptimized | PA1 | ✅ |
| features.autoBackgroundBuild | S2 | ✅ |
| features.commandHooks | OH3 | ✅ |
| features.agentHooks | OH3 | ✅ |
| features.symbolGraphExpansion | S1 | ✅ |
| features.proactiveImpactAnalysis | S4 | ✅ |

**共 18 个 flags**。T1-infra 和 T2 没有 feature flag——合理，因为 telemetry 是基础设施，应始终启用。

**一致性检查通过**。✅

### 检查 5：S3 前移到 Phase 2 的兼容性

S3 在 PostEditPipeline 中注册为 HeaderSyncStep (Order=200)。H1 在同 Phase 注册为 TruncationStep (Order=400)。

问题：S3 和 H1 都在 Phase 2，且 S3 的 Order < H1 的 Order。这意味着 S3 的输出（头文件同步警告）会追加到 ToolResult，然后 H1 的截断判断会考虑这个警告。**这正是期望的行为**——先生成完整输出，再判断截断。

但实施顺序需要注意：周计划中 H1 先开始（第5周），S3 后开始（第6周并行）。这意味着：
1. 第5周 H1 开始，此时 PostEditPipeline 中只有 M3(Order=100)
2. 第6周 S3 开始，H1 正在开发中
3. H1 的 TruncationStep(Order=400) 加入时，S3 的 HeaderSyncStep(Order=200) 可能还没完成

**风险**：如果 H1 先完成（第7周前半）、S3 后完成（第7周后半），H1 加入时不知道 S3 会在 Order=200 位置插入。H1 的截断逻辑需要处理"其前面的 step 可能追加了额外内容"的情况。

**这在 PostEditPipeline 架构下是自然处理的**——TruncationStep 只看最终 ToolResult 的总长度，不关心内容来自哪个 step。所以即使 S3 后加入，H1 的截断逻辑仍然正确。✅

### 检查 6：H1 混合截断策略的描述

v2.0 新增"混合截断策略"：构建输出自动提取错误/警告行注入 ToolResult。

但 S2（后台构建）在 Phase 5，而 H1 在 Phase 2。**Phase 2 实施 H1 时，S2 还不存在**。那么 H1 的"构建输出自动提取"应该如何实现？

两种理解：
1. H1 的混合策略是一个通用框架，Phase 2 实现基础设施，S2 上线后自动适配
2. H1 需要在 Phase 2 就实现构建输出提取逻辑，但 S2 还不存在

**结论**：这是一个**描述模糊**问题。H1 的混合截断策略应该分为：
- Phase 2：通用 PersistAndTruncate 基础设施 + "use read_file" 提示
- Phase 5（S2 上线后）：为 RunCommandTool 的构建输出增加自动错误提取逻辑

**建议**：在 H1 描述中明确——Phase 2 实现通用截断持久化；构建输出的智能提取在 S2 上线时一并实现。

---

## 三、v2.0 新增内容审查

### 审查 1：PostEditPipeline 设计

**优点**：
- 链式责任模式优雅地解决了 7 项修改的管理问题
- Order 值提供了明确的排序保证
- IsEnabled 直接对接 Feature flags
- fail-open 默认 + H2 fail-close 例外是合理的

**潜在问题**：
1. **PostEditContext 未定义**：文档给出了 IPostEditStep 接口，但 PostEditContext 的内容未说明。它至少需要：原始文件路径、编辑前内容、编辑后内容、编辑 diff、会话上下文。这个设计在 Phase 1 需要前瞻性地考虑后续 step 的需求。
2. **异步 step 的处理**：S2 BuildStep 是 fire-and-forget，但 pipeline 是顺序执行的 `foreach`。应在接口中区分同步和异步 step，或者让 BuildStep 的 RunAsync 立即返回（只是触发后台任务）。

**建议**：
- 在 PostEditContext 中至少包含：`FilePath`, `OriginalContent`, `NewContent`, `Diff`, `SessionId`, `StepIndex`, `AgentContext`
- BuildStep 的 RunAsync 应立即返回（fire-and-forget 内部实现），不阻塞 pipeline

### 审查 2：验证窗口设计

3 个验证窗口各 1 周（第4/11/18周）。

**优点**：自然的反馈回路，每个窗口有明确的评估指标和决策点。

**潜在问题**：
- 第4周的验证窗口中包含"SK 收尾(2d)"——这意味着验证窗口不完全是闲置的，仍有开发工作。这是合理的。
- 但第11周和第18周的验证窗口是纯反馈收集，没有开发工作。对单人开发者来说，1 周纯反馈可能太长——如果用户反馈很快（1-2天内），剩余时间浪费。

**建议**：验证窗口改为"弹性周"——前 2 天收集反馈+做决策，后 3 天可用于：消化前 Phase 溢出的工作 / 补充集成测试 / 提前启动下一 Phase 准备工作。这样验证窗口既提供了反馈回路，又不浪费时间。

### 审查 3：Feature Flags 实现开销

FF1 横切项声称每组件 +0.25 天。18 个 flags × 0.25 = 4.5 天。

但 Feature flag 的实际开销包括：
1. 首次实现 flag 框架（在 AicaConfig 中添加 features section + 读取逻辑）：1-2 天
2. 每个 flag 的条件判断代码：确实约 0.25 天

**总计**：1-2 天框架 + 4.5 天 = 5.5-6.5 天。

**建议**：FF1 框架搭建应明确安排在 Phase 0 或 Phase 1 初期（与 T1-infra 一起）。

---

## 四、发现总结

### 需要修正的问题（按优先级排序）

| # | 问题 | 严重性 | 修正建议 |
|---|------|--------|---------|
| 1 | Phase 7 工作量溢出（7.5天/5天可用） | 中 | T2 移到收尾阶段，或 Phase 7 扩展为 1.5 周 |
| 2 | H1 混合截断策略描述模糊（构建输出提取在 S2 之前无法实现） | 中 | 明确：Phase 2 实现通用截断；智能提取随 S2 上线 |
| 3 | 横切项是否已计入工作量估算不清晰 | 中 | 明确声明：各任务工作量估算已包含横切项 |
| 4 | PostEditContext 未定义 | 低 | 补充 PostEditContext 最小字段列表 |
| 5 | 验证窗口可能浪费时间 | 低 | 改为"弹性周"，前2天反馈+后3天可用 |
| 6 | Phase 5 中 PA1 和 ReviewAgent PoC 并行可能冲突 | 低 | 周计划中明确先 PoC 后 PA1 |
| 7 | FF1 框架搭建时间点未明确 | 低 | 安排在 Phase 0 |
| 8 | 集成测试仅 Phase 3 显式安排 | 低 | 每个 Phase 末尾预留 0.5-1 天 |

### 无需修正的确认

- 依赖图与 Phase 排列一致 ✅
- PostEditPipeline Order 值与排序逻辑一致 ✅
- Feature flags 完整覆盖所有功能 ✅
- S3 前移到 Phase 2 与 H1 无冲突 ✅
- v1.0 的 12 个问题中 10 个完全解决 ✅

---

## 五、结论

v2.0 相比 v1.0 有**显著改善**。12 个 v1.0 问题中 10 个完全解决，问题在收敛。

v2.0 新引入的问题均为**低-中严重性**，主要是描述清晰度和排期微调，没有架构级别的新矛盾。

**最关键的修正**：
1. 排期微调：22周目标可能仍偏乐观，建议 24 周目标 / 26 周上限
2. Phase 7 溢出需处理
3. H1 混合截断策略的时序需澄清

方案整体已达到**可执行状态**，上述修正可在执行过程中细化，不阻塞 Phase 0/1 的启动。

===V2_REVIEW_COMPLETE===
