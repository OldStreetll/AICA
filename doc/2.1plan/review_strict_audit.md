# AICA v2.1 Unified Plan — 严格审查报告

> **审查人**：Claude Code 实例 A（Session 1, Pane 0.0）
> **审查日期**：2026-04-06
> **审查方法**：完整阅读 Unified Plan + 源码交叉验证 + 逻辑一致性分析

---

## 一、代码引用准确性验证

对文档中所有提到的代码位置和函数名进行了源码交叉验证。

### 准确的引用（11/12）

| 任务 | 引用 | 验证结果 |
|------|------|---------|
| M1 | `PruneOldToolOutputs` + `ConversationCompactor` | ✅ 均存在于 AgentExecutor.cs |
| M3 | `EditFileTool.cs` + `AppendDiagnosticsAsync` | ✅ 存在，line 401-426 |
| SK | `Rule.cs`/`RuleLoader.cs`/`RuleEvaluator.cs`/`ClassifyIntent()`/`AddBugFixGuidance()` | ✅ 全部存在 |
| H1 | 各工具独立截断逻辑 | ✅ ReadFile 500行/Grep 200条/ListDir 800项/RunCommand 6000字符 |
| OH2 | `MemoryBank.cs` 全量拼接 + 4000字符硬截断 | ✅ line 43: `const int maxTotalChars = 4000` |
| H3 | `ToolExecutionPipeline.cs` + `SafetyGuard.cs` | ✅ 均存在 |
| H2 | EditFileTool diff→确认→写入流程 | ✅ `ShowDiffAndApplyAsync` |
| OH5 | PlanAgent 配置 10次/60s/16K | ✅ lines 56-58 |
| S3 | SymbolRecord 有 FilePath/Signature/Namespace/Name | ✅ 验证通过 |
| S1 | `KnowledgeContextProvider.cs` TF-IDF | ✅ 存在 |
| H3 | `SafetyGuard.cs` 权限引擎 | ✅ 存在 |

### 有问题的引用（1/12）

| 任务 | 问题 | 严重性 |
|------|------|--------|
| **OH3** | 文档声称"6层中间件"，但源码中只有 **2个中间件被实际注册**（PreValidation + Verification）。其余4个文件存在但未激活。文档对现状描述有误导性——这不是"6层"而是"2层 + 4个未用" | 中 |
| **S2** | 文档提到 `VSAgentContext.TriggerBackgroundBuildAsync()`，但当前 VSAgentContext 中 **不存在任何构建集成**。这是新增功能而非改动，工作量可能被低估 | 中 |

---

## 二、逻辑一致性问题

### 问题 1：依赖图声称"无依赖"的项实际有隐含依赖

文档第三节声称 `M1, M3, SK, H1, H2, H3, OH2, OH5, S3` 均"可独立开始"，但：

- **OH5（SubAgent泛化）声称无依赖，但 Phase 4 才做**。如果 OH5 真的无依赖，为什么不提前到 Phase 2-3？实际上 OH5 需要稳定的 Agent Loop 和工具系统作为前提（H1 截断持久化完成后 ReviewAgent 才能有效工作），这属于**隐性依赖**。
- **S3（头文件同步）声称无依赖**，但它需要 ProjectIndexer 的符号数据。当前 ProjectIndexer 是否足够稳定？如果 S1（符号检索增强）会修改 SymbolRecord 接口，S3 就有对 S1 的**逆向依赖**风险。文档虽提到了"S1 不应破坏 S3 接口"约束，但这个约束没有工程手段保障（如接口冻结或抽象层）。

**建议**：依赖图应区分"技术可行"和"实际有效"两种依赖。OH5 虽然技术上可独立开始，但 ReviewAgent 需要截断持久化（H1）来审查完整输出，应标注为"建议在 H1 之后"。

### 问题 2：EditFileTool 排序与实际 Phase 排期矛盾

排序图中 ② M3 格式化（Phase 1）在 ⑤ H1 截断持久化（Phase 2）之前。这意味着：
- Phase 1 完成时，M3 格式化逻辑加入 EditFileTool 末尾
- Phase 2 加入 H1 截断判断时，必须将截断逻辑放在 M3 **之后**

文档的"跨 Phase 集成提醒"已注意到 S3/S4 需要插入到 H1 之前，但**没有提到 M3 和 H1 的排列关系**。M3 在 Phase 1 加入时，它是末尾操作；H1 在 Phase 2 加入时，必须将自己放在 M3 之后。这个排列关系需要显式文档化。

**建议**：在排序图旁增加"Phase 集成时的代码插入点说明"，明确每个 Phase 加入新逻辑时，在已有逻辑链中的精确位置。

### 问题 3：DynamicToolSelector 改动的风险控制不够严密

文档要求"先采集 1-2 周基线数据，去过滤后对比"。但：
- 基线数据采集需要 T1 Telemetry，而 T1 是"横切项，随各组件加入"
- SK 在 Phase 1（第2周），T1 的工具调用日志此时是否已就绪？
- 如果没有 Telemetry，基线数据怎么采集？

**建议**：明确 SK 任务的第8步（去工具过滤）应**延后到 T1 telemetry 基础就绪后**，或者在 SK 中先单独实现一个轻量级工具调用计数器，不依赖完整 T1。

### 问题 4：Phase 3 工作量分配不均

- OH2（结构化记忆）：5-8天
- H3（权限反馈+持久化）：10天
- 合计：15-18天，分配在第6-8周（15个工作日）

即使取下限也要 15 天填满 15 个工作日，没有任何缓冲。加上 T1 埋点（+1天）和 AS1 假设记录（+0.5天），Phase 3 实际需要 **16.5-19.5 天**，超出 3 周（15天）。

**建议**：Phase 3 应预留 1 周缓冲，或将 H3 拆分为 Part 1（反馈注入，5天，Phase 3）和 Part 2（决策持久化，5天，Phase 4）。

---

## 三、架构设计问题

### 问题 5：ReviewAgent 的实际价值存疑

文档声称 ReviewAgent 用"检查清单式（模式匹配）"来降低对弱模型推理能力的依赖。但：

1. **MiniMax-M2.5 即使做模式匹配也需要理解代码语义**。"修改了函数签名？→ 对应头文件是否同步？"这个检查项需要：理解什么是签名 → 比较前后差异 → 定位头文件 → 判断是否同步。这不是简单的模式匹配。
2. **ReviewAgent 无工具（1次迭代/15s/4K）**。没有工具意味着它只能审查传入的 diff 文本，无法读取关联文件。那么"头文件是否同步"这个检查项，ReviewAgent 根本无法执行——它看不到头文件。
3. **S3 头文件同步检测是程序化实现的**。如果 S3 已经程序化检测了头文件同步，ReviewAgent 的"一致性"维度就与 S3 功能重叠。

**建议**：
- ReviewAgent 应聚焦于 S3/S4 **无法覆盖**的维度（范围控制、可读性）
- 或者给 ReviewAgent 有限的只读工具访问（read_file），代价是增加 token 预算到 8K
- 明确 ReviewAgent 和 S3/S4 的分工边界

### 问题 6：Hooks 系统（OH3）与现有中间件体系的关系模糊

文档说 Hooks 是"中间件之上的可配置扩展"，但：

1. 6 个中间件中只有 2 个被激活，剩余 4 个（Logging/Monitoring/Permission/Timeout）为什么不先激活？
2. OH3 新增 Command Hook 和 Agent Hook，它们与未激活的中间件（如 LoggingMiddleware）是否功能重叠？
3. Hook 的触发点（PreToolUse/PostToolUse）与中间件的执行管道是什么关系？是在管道内还是管道外？

**建议**：先审视现有 4 个未激活中间件的价值，决定是激活还是删除。然后明确 Hooks 在管道中的精确位置（中间件之前？之后？还是作为特定中间件的实现？）。

### 问题 7：Skills 系统与现有 Rules 系统的边界模糊

SK 任务将 Skills 建立在 Rules 基础上（复用 Rule.cs、RuleLoader、RuleEvaluator），同时新增 SkillTool。但：

1. **Rule 是被动注入的**（系统启动时加载到 System Prompt），**Skill 是主动调用的**（LLM 调用 SkillTool）。这是两种截然不同的机制，共用一套 Rule 基础设施是否合适？
2. 任务模板（bug-fix.md）应该是被动注入还是主动调用？文档没有明确。如果是被动注入（根据 intent 自动注入），那不需要 SkillTool；如果是主动调用，弱模型是否能可靠地决定何时调用 SkillTool？

**建议**：明确 Skills 的激活机制——是 intent 触发自动注入（被动），还是 LLM 调用 SkillTool（主动），还是两者兼有。文档中两种模式都有暗示但没有统一。

---

## 四、排期与工作量问题

### 问题 8：16 周排期的实际可行性

工作量合计（取中值）：
- Phase 1: 2.5 + 4 + 6.5 = 13 天
- Phase 2: 12.5 天
- Phase 3: 6.5 + 10 = 16.5 天
- Phase 4: 12.5 + 6.5 + 4 = 23 天
- Phase 5: 2.5 + 6.5 = 9 天
- Phase 6: 6.5 + 5 + 4.5 + 2.5 = 18.5 天
- 横切项: 15个任务 × 0.75天 = 11.25 天

**总计 ≈ 104 天 = 20.8 周**（按每周5个工作日）

文档声称 16 周，缺口约 5 周（25%）。这还没有计入：
- 测试时间（每个任务至少 0.5-1 天）
- Bug 修复和返工
- 文档编写
- vibe coding 的沟通开销（每个任务都需要与 Claude Code 对话）

**建议**：要么将排期调整为 20-24 周，要么削减 Phase 6 的范围（S1/S4/S2 可搁置）。

### 问题 9：Phase 4 工作量过重

Phase 4（第9-11周）包含 H2（10-15天）+ OH5（5-8天）+ S3（3-5天）= 18-28天，但只有 15个工作日。

即使取最小值（18天）也超出 3 天。取中值则需要 23 天，超出 8 天。

**建议**：将 S3 移到 Phase 5（PA1 只需 2-3 天，Phase 5 有空间），或将 OH5 提前到 Phase 3 末尾。

---

## 五、遗漏风险

### 问题 10：缺乏集成测试策略

每个 Phase 都有"交付物"，但没有集成测试计划。特别是：
- EditFileTool 被 7 个不同 Phase 修改，每次修改后需要**回归测试**整个编辑流程
- H1 截断持久化需要测试所有工具的截断行为
- OH5 SubAgent 泛化后需要验证 PlanAgent 行为不回归

**建议**：每个 Phase 增加 1-2 天的集成测试时间，或建立 EditFileTool 的自动化回归测试套件。

### 问题 11：缺乏用户反馈回路

方案是 16 周的线性执行计划，但：
- 6 个试用用户的反馈何时收集？
- Phase 1 交付后是否有验证周期？
- 如果 Phase 1 的 Skills 系统对 MiniMax 无效，后续 Phase 是否需要调整？

**建议**：每 2 个 Phase 之间插入 1 周的"用户验证 + 方向调整"窗口。

### 问题 12：MiniMax-M2.5 并发约束未在方案中体现

文档提到"20并发+10K input → TTFT 23s"，但：
- ReviewAgent（OH5）需要额外的 LLM 调用
- PlanAgent 优化（PA1）的每轮重复注入会增加 token 消耗
- 这些是否会导致并发压力增加？对 TTFT 的影响？

**建议**：评估 Phase 4-5 新增的 LLM 调用对并发和延迟的影响，必要时对 ReviewAgent 增加"仅在用户请求时触发"的开关。

---

## 六、审查结论

### 方案优势
1. 整合四份文档的逻辑清晰，合并决策合理
2. "假设+失效信号+Telemetry"的组件设计模式优秀
3. 搁置项选择合理，每项都有重新评估条件
4. EditFileTool 排序分析深入，跨 Phase 集成提醒有价值

### 需要修正的关键问题
1. **排期不现实**：实际工作量约 21 周，需要调整（严重）
2. **Phase 4 过载**：18-28天任务塞入 15 天（严重）
3. **Phase 3 无缓冲**：15-18天塞入 15 天（中等）
4. **ReviewAgent 设计矛盾**：无工具但要检查跨文件一致性（中等）
5. **Skills 激活机制未定义**：被动注入 vs 主动调用未统一（中等）
6. **DynamicToolSelector 改动缺少 Telemetry 前提**（低）
7. **缺乏集成测试策略和用户反馈回路**（中等）

===REVIEW_COMPLETE===
