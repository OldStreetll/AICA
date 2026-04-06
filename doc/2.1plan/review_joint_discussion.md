# AICA v2.1 Unified Plan — 联合讨论与修正建议

> **参与者**：Claude Instance A（严格审查）+ Claude Instance B（可行性分析）
> **日期**：2026-04-06
> **输入**：review_strict_audit.md + review_feasibility.md
> **目标**：交叉对比发现，形成最终修正建议

---

## 一、两份报告的共识（高置信度，应采纳）

以下问题被两份报告独立发现，置信度高：

### 1. 排期不现实（两份报告一致）

| | Instance A | Instance B |
|--|-----------|-----------|
| 估算 | 实际 ~104天 = 20.8周 | 实际 105-130天 = 21-26周 |
| 建议 | 调整为 20-24 周 | 调整为 22-24 周 |

**共识**：16 周排期偏乐观约 30-50%。**建议调整为 22 周**作为目标，24 周作为上限。

### 2. Phase 4 严重过载（两份报告一致）

| | Instance A | Instance B |
|--|-----------|-----------|
| 分析 | 18-28天任务塞入15天 | "最过度打包的阶段"，需要实际4周 |
| 建议 A | 将 S3 移到 Phase 5 | — |
| 建议 B | — | 将 S3 提前（模型无关，高价值） |

**共识**：Phase 4 必须减负。但两份报告对 S3 的移动方向不同——A 建议后移，B 建议前移。

**最终建议**：**S3 前移到 Phase 2**。理由：
- S3 是程序化检测（不依赖模型），Instance B 正确识别了这是"C/C++ 杀手级功能"
- S3 依赖 ProjectIndexer（已成熟），不依赖 H1/OH5
- Phase 2 当前只有 H1（10-15天），加入 S3（3-5天）后仍在 3 周范围内
- 这样 Phase 4 减少到 H2 + OH5 = 15-23 天，接近 3 周

### 3. Phase 3 无缓冲（两份报告一致）

Instance A 识别 Phase 3 需 16.5-19.5 天，Instance B 指出 OH2 的中文分词可能消耗缓冲。

**最终建议**：**H3 拆分为两部分**：
- H3a 反馈注入（5天）→ Phase 3
- H3b 决策持久化（5天）→ Phase 4
- Phase 3 变为 OH2(5-8天) + H3a(5天) = 10-13天，留有缓冲

### 4. ReviewAgent (OH5) 价值存疑（两份报告一致）

| | Instance A | Instance B |
|--|-----------|-----------|
| 问题 | 无工具但要检查跨文件一致性；与 S3 功能重叠 | MiniMax 4K/无工具/1次迭代产出大概率是泛泛之谈 |
| 建议 A | 聚焦 S3 无法覆盖的维度；或给只读工具 | — |
| 建议 B | — | 先只做1个维度的 PoC |

**最终建议**：
1. ReviewAgent 先做**单维度 PoC**（范围控制："修改是否超出用户请求"），验证 MiniMax 在 4K 预算下的实际表现
2. 如果 PoC 有效，再扩展到其他维度
3. 从检查清单中**移除"一致性"维度**——S3 已程序化覆盖
4. OH5 工作量从 5-8 天调整为：SubAgent 泛化(3天) + ReviewAgent PoC(2天) = 5 天

### 5. T1 Telemetry 需要前置基础设施（Instance B 发现，Instance A 间接触及）

Instance B 明确指出：首个组件需要 2-3 天基础设施搭建，后续才是每组件 +0.5 天。Instance A 从 DynamicToolSelector 基线数据角度也发现了类似问题。

**最终建议**：**Phase 1 增加 T1 基础设施任务（2-3天）**。这同时解决了 SK 步骤8（DynamicToolSelector 基线数据采集）的依赖问题。

### 6. 缺乏集成测试策略（两份报告一致）

Instance A 直接指出缺乏集成测试计划。Instance B 从 EditFileTool 7项修改角度提出了相同关切。

**最终建议**：每个 Phase 完成后增加 **1-2 天集成测试**时间。特别是 EditFileTool 需要建立回归测试套件。

---

## 二、互补发现（一方识别，另一方未提及）

### Instance A 独有发现

| # | 发现 | 采纳建议 |
|---|------|---------|
| A-1 | Skills 被动注入 vs 主动调用机制未统一 | **采纳**。明确：任务模板通过 ClassifyIntent 自动注入（被动），SkillTool 保留但不依赖弱模型主动调用 |
| A-2 | OH3 Hooks 与 4 个未激活中间件关系模糊 | **采纳**。OH3 实施前先审视未激活中间件，明确 Hooks 在管道中的位置 |
| A-3 | 依赖图中 OH5 有隐性依赖（需 H1 截断持久化后 ReviewAgent 才有效） | **采纳**。标注为"建议在 H1 之后" |
| A-4 | M3 和 H1 的 EditFileTool 插入点关系未文档化 | **采纳**。补充跨 Phase 集成的精确插入点说明 |
| A-5 | 缺乏用户反馈回路 | **采纳**。每 2 个 Phase 后插入 1 周验证窗口 |
| A-6 | MiniMax 并发约束未考虑 ReviewAgent 额外 LLM 调用 | **采纳**。评估并发影响，ReviewAgent 增加"仅用户请求时触发"开关 |

### Instance B 独有发现

| # | 发现 | 采纳建议 |
|---|------|---------|
| B-1 | M3 的 DTE 格式化需要文档在编辑器中打开，如果 EditFileTool 直接写文件则需额外处理 | **采纳**。验证 EditFileTool 写入机制，可能 +1-2 天 |
| B-2 | H1 应考虑混合方案（自动注入截断内容的关键部分）而非完全依赖模型回读 | **采纳**。优秀建议——对构建输出自动提取错误行，对其他保留手动回读 |
| B-3 | S2 后台构建应提前（提供模型无关的编译器反馈是最高价值） | **部分采纳**。S2 依赖 H1 基础设施，无法提前到 Phase 1-3，但可从 Phase 6 提前到 Phase 5 |
| B-4 | 需要 feature flags 支持增量发布和回滚 | **采纳**。在 AicaConfig 中为每个新功能添加 enable/disable 开关 |
| B-5 | 离线涉密环境的磁盘配额风险 | **采纳**。添加到风险清单，为 snapshots/truncations/telemetry 增加总量上限配置 |
| B-6 | EditFileTool 应提前抽取 PostEditPipeline | **采纳**。优秀建议——Phase 1 M3 实施时就建立 pipeline 模式 |
| B-7 | ConversationCompactor 可能假设 Prune 未运行 | **采纳**。M1 实施前验证 |

---

## 三、两份报告的分歧

### 分歧 1：S2 后台构建的优先级

- Instance B 建议 S2 大幅提前（排第4），因为编译器反馈是模型无关的
- Instance A 未特别提及 S2 的优先级

**裁决**：S2 依赖 H1 的截断基础设施（构建输出可能很长），无法提前到 Phase 1-3。但同意 Instance B 的逻辑——编译器反馈价值极高。**将 S2 从 Phase 6 移到 Phase 5**，与 PA1 并行。

### 分歧 2：OH2 工作量

- Instance A 未特别质疑 5-8 天
- Instance B 认为应为 8-12 天（中文分词复杂度）

**裁决**：同意 Instance B。文档已注明"先用简单按字分词"，但即使简单版本也需要处理 CJK/ASCII 混合、停用词、Unicode。**调整为 8-10 天**。

### 分歧 3：OH3 Hooks 工作量

- Instance A 关注架构关系（与中间件的边界）
- Instance B 认为 5-8 天低估，应为 8-12 天（shell 执行安全性 + async UI）

**裁决**：同意 Instance B 的工作量估算。OH3 涉及 shell 执行（安全敏感）+ ReviewAgent 并行 UI 更新（VSIX 异步 UI 是公认难点）。**调整为 8-10 天**。

---

## 四、最终修正方案

### 修正后的 Phase 结构（22 周 + 2 周缓冲 = 24 周上限）

```
Phase 0 (Week 1):      T1 基础设施 (2-3天) + 基线数据采集启动
Phase 1 (Week 2-3):    M1 Prune前移 (2-3天) + M3 自动格式化 (3-5天) + SK Skills+模板 (5-8天)
                        ↳ M3 实施时建立 PostEditPipeline 模式
                        ↳ SK 步骤8（去工具过滤）延后到基线数据采集完成后
  [验证窗口 Week 4]:    用户验证 Phase 0-1 + 方向调整

Phase 2 (Week 5-7):    H1 截断持久化 (10-15天) + S3 头文件同步 (3-5天，从 Phase 4 前移)
Phase 3 (Week 8-10):   OH2 结构化记忆 (8-10天) + H3a 权限反馈注入 (5天)
  [验证窗口 Week 11]:   用户验证 Phase 2-3 + 方向调整

Phase 4 (Week 12-14):  H2 文件快照 (10-15天) + H3b 权限决策持久化 (5天)
Phase 5 (Week 15-17):  OH5 SubAgent+ReviewAgent PoC (5天) + PA1 PlanAgent优化 (2-3天) + S2 后台构建 (4-5天，从 Phase 6 前移)
  [验证窗口 Week 18]:   用户验证 Phase 4-5 + 方向调整

Phase 6 (Week 19-21):  OH3 Hooks (8-10天) + S1 符号检索 (5-8天)
Phase 7 (Week 22):     S4 Impact分析 (4-6天) + T2 会话摘要 (2-3天)
  [收尾 Week 23-24]:    集成测试 + bug修复 + 文档
```

### 修正后的任务变更清单

| 变更 | 原方案 | 修正 | 理由 |
|------|--------|------|------|
| 新增 Phase 0 | 无 | T1 基础设施 2-3 天 | Telemetry 是多个任务的前提 |
| S3 前移 | Phase 4 | Phase 2 | 模型无关、高价值、减轻 Phase 4 |
| S2 前移 | Phase 6 | Phase 5 | 编译器反馈是模型无关的高价值信号 |
| H3 拆分 | Phase 3 整体 10天 | H3a Phase 3 (5天) + H3b Phase 4 (5天) | Phase 3 无缓冲 |
| OH2 工作量 | 5-8 天 | 8-10 天 | 中文分词复杂度 |
| OH3 工作量 | 5-8 天 | 8-10 天 | shell 安全 + async UI |
| OH5 范围 | 5维度检查清单 | 单维度 PoC + SubAgent 泛化 | 验证弱模型可行性 |
| SK 步骤8 | Phase 1 | 延后到基线数据就绪 | 需要 Telemetry 前提 |
| Skills 机制 | 未明确 | 任务模板=被动注入(ClassifyIntent)；SkillTool=备用 | 弱模型不可靠调用元工具 |
| 新增 | 无 | PostEditPipeline 抽取（Phase 1 M3 时） | 管理 7 项 EditFileTool 修改 |
| 新增 | 无 | Feature flags（每个新功能） | 支持增量发布和回滚 |
| 新增 | 无 | 3 个验证窗口（各 1 周） | 收集用户反馈，调整方向 |
| ReviewAgent | 一致性/安全/范围/规范/可读 5维度 | 移除"一致性"维度（S3覆盖）；先做"范围控制"PoC | 避免重叠，验证可行性 |
| 排期 | 16 周 | 22 周目标 / 24 周上限 | 工作量+横切项+测试+缓冲 |

### 需补充到风险清单的新风险

| 风险 | 来源 | 缓解 |
|------|------|------|
| DTE 格式化需文档在编辑器中打开 | B-1 | M3 实施前验证 EditFileTool 写入机制 |
| 离线环境磁盘配额限制 | B-5 | snapshots/truncations/telemetry 增加总量上限 |
| ConversationCompactor 可能假设 Prune 未运行 | B-7 | M1 实施前验证 |
| ReviewAgent 增加 LLM 并发压力 | A-6 | 默认"仅用户请求时触发" |
| 4个未激活中间件与 Hooks 功能重叠 | A-2 | OH3 前审视并清理 |
| 缺乏 EditFileTool 回归测试 | A-10/B | Phase 1 建立测试套件 |

---

## 五、结论

原方案在架构设计和组件分析上是**优秀的**。"假设+失效信号+Telemetry"的模式、搁置项的选择、EditFileTool 排序分析都是高质量的工程实践。

核心修正集中在三个方面：
1. **排期现实化**：16周 → 22-24周
2. **优先级调整**：模型无关的系统级功能（S3/S2）前移，模型依赖的功能（OH5 ReviewAgent）降低初始范围
3. **工程保障**：T1 基础设施前置、PostEditPipeline 抽取、Feature flags、验证窗口、集成测试

这些修正不改变方案的战略方向（"弱模型+强系统"），而是让执行更稳健。
