# AICA v2.1 四实例联合审查报告（终稿）

> **日期**：2026-04-06
> **审查方式**：4个 Claude Code Opus 4.6 实例并行审查 + 交叉验证（2轮） + 汇总后各实例确认审查（第3轮）
> **审查范围**：技术可行性（Pane1）、Harness原则合规（Pane2）、架构风险（Pane3）、综合协调（Pane0）
> **状态**：**四实例全部 APPROVE**（第4轮终审通过），下方已整合全部4轮审查中的补充修正

---

## 一、总体结论

| 维度 | 评级 | 说明 |
|------|------|------|
| 技术可行性 | ✅ 全部可行 | 18个任务无架构性阻塞，现有扩展点充分 |
| 工作量估算 | ⚠️ 偏乐观10-15% | 8个准确、7个偏紧、2个需+缓冲（OH5/H2） |
| Harness原则符合度 | ✅ ~85%（修正后） | 修正前 75-80%；经 Context Reset(+3-4%)、SubAgent 走 Pipeline(+2-3%)、Intent 匹配缓解(+1%) 后提升。剩余差距主要来自 Generator-Evaluator 不完整(~8-10%) |
| 架构风险 | ⚠️ 可控 | 4个高风险点已识别，均有缓解方案 |
| 排期 | ✅ 可行 | 内部按26-28周规划，对外承诺24-26周，3个验证窗口是关键安全网 |

---

## 二、四实例共识（全部同意）

### 2.1 必须修改的设计缺陷

**C1: PostEditPipeline 需要 Pre/Post 分离（P0）**

- **问题**：H2 快照在"编辑前"执行，但 PostEditPipeline 是"编辑后"管道。计划中写"pipeline之外"调用快照，导致两套独立机制。
- **共识方案**：将 pipeline 泛化为 `EditPipeline`，含 `PreEdit`/`PostEdit` 两阶段。H2 SnapshotStep 注册为 PreEdit step。接口加一个 Phase 枚举，改动量小但必须在 Phase 1 骨架中一次到位。
- **影响**：Phase 1 骨架设计需修订，+0.5天。

**C2: PostEditContext 缺少关键字段（P0）**

- **问题**：当前定义缺少 `EditMode`（单文件/多编辑/多文件）、`Language`（文件语言）、`EditedSymbols`（被修改符号列表）、`BuildRequired`（是否需触发构建）。
- **共识方案**：Phase 1 骨架中一次性定义完整字段，避免后续每加一个 step 都要改 context 类。
- **影响**：无额外工时，属于设计完善。

**C3: PostEditPipeline 骨架应与 M3 解耦（P0）**

- **问题**：如果骨架和 M3 一起做，出问题时无法区分是骨架 bug 还是格式化 bug。
- **共识方案**：
  1. 先建 EditPipeline 骨架 + 迁移 `AppendDiagnosticsAsync` 为 `DiagnosticsStep`（~1天，零功能变更，可回归验证）
  2. 再在已验证的骨架上实现 `FormatStep`（M3 主体工作）
- **影响**：降低 Phase 1 风险，无额外工时。

**C4: 缺少 Context Reset 机制（P0）**

- **问题**：方案只有 Prune→Compaction 两层渐进压缩，缺少第三层"硬重置"。Anthropic 博文明确指出 compaction ≠ clean slate。对 MiniMax-M2.5 弱模型，累积的上下文漂移靠压缩修不好——压缩后的摘要本身可能带偏。
- **共识方案**：作为 M1 附带项在 Phase 1 加入 `ResetToClean()` 路径（~50-80行，+1-2天）。触发条件：连续 3 次 doom loop 检测 或 compaction 后 token 仍超阈值。保留 system prompt + 记忆 + 当前 plan context，丢弃全部对话历史。
- **关键细节**：`_taskState`（编辑过的文件列表、doom loop 签名等）必须跨 reset 保留，但 `executedToolSignatures` 去重集合应清空。
- **结构化交接摘要（Pane2 第3轮补充）**：ResetToClean 执行时，用轻量模板从 `_taskState`（已编辑文件列表、当前步骤等）自动生成一段 structured handoff text 注入 reset 后的首轮 user message。这不需要额外 LLM 调用，纯模板化即可（+20-30行代码）。没有此摘要，reset 后 Agent 只有 plan context 但不知道执行进度，可能重复已完成的工作。
- **影响**：M1 工作量从 2-3天调整为 3-5天。

**C5: SubAgent 必须走 Pipeline（P1）**

- **问题**：PlanAgent 直接调用 ToolDispatcher，绕过 ToolExecutionPipeline 的中间件链。导致：PreToolUse Hook 对 SubAgent 无效、Permission 检查被跳过、Telemetry 中间件也被绕过。
- **共识方案**：OH5 SubAgent 重构时，让 SubAgent 走 Pipeline（可用受限的 UIContext）。这是架构级决策，必须在 OH5 设计阶段确定。
- **偏离的 Harness 原则**：原则六（多层安全治理）+ 原则七（可扩展性/Hook 生命周期）。
- **Pipeline 权限兼容（Pane1 第3轮补充）**：SubAgent 走 Pipeline 后，PermissionCheckMiddleware 对 PlanAgent 的只读工具必须 auto-approve（当前 PlanAgent 用 NullUIContext 绕过，重构后需确保 AutoApproveManager 默认 read 操作 auto-approve），否则 PlanAgent 每次 read_file 都弹框会破坏用户体验。OH5 验收中必须显式验证此行为。
- **OH5 受限 UIContext 的 UI 交互（Pane2 第3轮补充）**：SubAgent 走 Pipeline 意味着要处理"受限 UIContext"下权限对话框如何呈现的问题，这个 UI 交互细节可能被低估，需在 OH5 任务描述中显式提及。
- **影响**：OH5 复杂度上升，工作量建议 5天→7-8天。

### 2.2 达成共识的风险缓解措施

**R1: OH5 是关键路径瓶颈**

- OH5 影响 PA1 和 OH3 Agent Hook 两个下游任务
- 缓解：OH5 工作量 5天→7-8天；PA1 的 prompt 优化部分可提前独立实施；OH3 Agent Hook 已有条件跳过机制
- 额外建议：提前定义 PoC 最小可行标准——如果时间不够，优先保 SubAgent 基类重构，ReviewAgent PoC 可精简

**R2: H1 散弹枪手术**

- 需改约 7 个工具文件（非 11 个），以及 ContextManager.SmartTruncateToolResult（Agent 循环层截断）
- 缓解：先改 ReadFileTool + RunCommandTool 作为 pilot（截断最频繁的两个），验证接口设计后再批量推进
- 工作量 12-15天不变（5天核心 + 5天逐工具 + 3天回归）

**R3: DynamicToolSelector 意图匹配脆弱**

- SK 被动注入和 DynamicToolSelector 都依赖关键词匹配，两个关键路径上的组件都用脆弱的字符串匹配做决策
- **区分两套 intent 逻辑（Pane1 第3轮补充）**：DynamicToolSelector 的工具过滤已稳定运行（风险低），风险主要在 SK 新增的模板注入 intent 匹配（风险中）。这是两个不同组件用的两套逻辑，风险级别不同
- 缓解：SK 增加置信度阈值和 fallback——匹配置信度低时不注入模板（宁缺勿错）；Phase 1 初期用保守匹配（精确 intent 完全匹配而非包含匹配），验证窗口 1 后根据 Telemetry 放宽

**R4: S2 多文件模式 debounce**

- 多文件编辑（ExecuteMultiFileAsync）为每个文件独立调用 pipeline，S2 BuildStep 会被触发多次
- 缓解：BuildStep 实现 debounce 或 batch 机制（仅在最后一个文件编辑完成后触发构建）

**R5: S2 VS 线程亲和性**

- SolutionBuild.Build() 需在 UI 线程调用，fire-and-forget 模式必须用 `JoinableTaskFactory.RunAsync` 而非普通 `Task.Run`
- 缓解：S2 实施时通过 `JoinableTaskFactory.SwitchToMainThreadAsync()` 回到 UI 线程调用 Build，然后立即切回后台线程

**R6: OH2 旧格式兼容**

- 旧 .md 文件没有 YAML frontmatter，迁移路径不明
- 缓解：OH2 实现兼容性迁移——检测到无 frontmatter 的 .md 文件时，自动归类为 `type: project`，同时从文件内容首行或文件名推导生成 `description` 字段（Pane1 第3轮补充：若 description 为空，新的相关性检索中这些旧记忆的 description 权重为零，等于"迁移了但找不到"）
- 额外保障：迁移前备份原文件到 `.aica/memory_backup/`

**R7: H2 磁盘预检**

- fail-close 语义下，磁盘满/权限问题会导致所有编辑被阻塞
- 缓解：快照前检查目标目录可写 + 剩余空间；空间不足时降级为 fail-open（跳过快照但记录 Telemetry 警告）

### 2.3 AgentExecutor 上帝类处理（有分歧但达成折中）

- **Pane3 建议**：Phase 0/1 做最小拆分（抽取 ConversationHistoryBuilder / LLMStreamExecutor / ToolExecutionLoop）
- **Pane1 反驳**：先拆再改等于改两次，Phase 0 拆分引入额外回归风险
- **Pane2 补充**：符合 Harness 负载承载分析原则——让每个组件的假设可独立验证
- **最终共识**：**渐进式提取**。不在 Phase 0 做专门拆分，而是各 Phase 改动时顺带提取所触碰的关注点。具体时间点：
  - Phase 1 M1：提取压缩协调为 `CompactionCoordinator`（~80行）
  - Phase 1 SK：提取 prompt 构建为 `SystemPromptOrchestrator` 的方法
  - Phase 5 PA1/S2：提取构建结果注入和 plan 注入逻辑
  - 目标：AgentExecutor 从 1101 行瘦身到 ~600 行
- **提取安全网（Pane3 第3轮补充）**：每次提取前必须先补回归测试。如果 AICA.Core.Tests/Agent/ 下缺少 AgentExecutor 的直接测试覆盖，渐进式提取就缺乏安全网。建议作为横切约束：**提取前先补回归测试**。

---

## 三、Harness 原则偏离与修正方案

### 已修正的偏离（通过上述共识解决）

| 偏离项 | 原严重度 | 修正方案 | 修正后 |
|--------|---------|---------|--------|
| 缺少 Context Reset | 高 | M1 附带 ResetToClean() | ✅ 解决 |
| SubAgent 绕过 Pipeline | 高 | OH5 重构时让 SubAgent 走 Pipeline | ✅ 解决 |
| PostEditPipeline 位置矛盾 | 中 | 泛化为 Pre/Post 两阶段 | ✅ 解决 |
| Intent 匹配脆弱 | 中 | 增加置信度阈值 + 保守匹配 | ✅ 缓解 |

### 接受的偏离（当前阶段合理）

| 偏离项 | 严重度 | 接受理由 |
|--------|--------|---------|
| Generator-Evaluator 对抗循环不完整 | 中→高 | MiniMax-M2.5 的 4K/无工具/1次迭代限制下，自动对抗循环不可行。PoC 策略 + 程序化检测（S3/S2/S4）是务实替代。**这是当前最大的 harness 能力天花板，模型升级后应作为第一优先重评项**（Pane2 第3轮强调：不是被动"重新评估"，而是主动列为升级后首要议题）。 |
| Sprint 分解缺失 | 中 | PA1 的"粗粒度目标 + 成功标准 + 每轮重复注入"是轻量版 Sprint Contract。完整 Sprint 分解的价值取决于 PA1 效果，验证窗口 3 后评估。 |
| ReviewAgent 评估器未迭代调优 | 中 | 在 OH5 验收标准中补充：定义 2-3 个 golden test case（已知应触发/不应触发范围控制警告），作为 prompt 迭代的回归基线。不需要完整调优框架。**验证窗口 3 检查项中明确加入"ReviewAgent 判断偏差分析"**（Pane2 第3轮补充：golden test case 是回归基线，真正的调优需要观察与人类判断的偏差）。 |
| Harness 退化计划缺失 | 低 | Feature Flags 已提供开关能力。补充一项横切规则：每个验证窗口中增加"当前 Feature Flags 哪些应该移除"的检查点。 |
| OH3 缺少 Prompt Hook | 低 | 涉密离线环境 + 弱模型。Command + Agent 两种 Hook 覆盖主要场景。 |
| H2 快照粒度（单文件 vs worktree）| 低 | VS2022 VSIX 约束下的合理折中。跨多文件回滚可通过按 stepIndex 批量回滚部分缓解。 |

---

## 四、修订后的排期与变更总结

### 工作量调整

| 任务 | 原估算 | 修订估算 | 调整原因 |
|------|--------|---------|---------|
| M1 Prune前移 | 2-3天 | **3-5天** | +Context Reset（ResetToClean）附带实现 |
| M3 自动格式化 | 4-6天 | 4-6天 | 不变（骨架解耦降低了风险） |
| OH5 SubAgent+ReviewAgent | 5天 | **7-8天** | SubAgent 走 Pipeline + PlanAgent 回归测试 + ReviewAgent golden test cases |
| H2 文件快照 | 10-15天 | **12-15天** | 磁盘预检 + fail-close 降级逻辑 |
| 其余14个任务 | — | 不变 | — |

**净增加**：约 4-6天，被3个验证窗口（各1周弹性）吸收。**排期建议（Pane1 第3轮修正）**：内部按 **26-28周** 规划（含偏乐观10-15%的缓冲），对外承诺仍可保持 24-26周。

### Phase 1 执行顺序修订

原方案 Phase 1 是并行执行 M1/M3/SK，修订为有序执行关键路径：

```
Phase 1 第1天：EditPipeline 骨架 + DiagnosticsStep 迁移（验证骨架）
Phase 1 第2-4天：M1 Prune前移 + ResetToClean 实现
Phase 1 第5-8天：M3 FormatStep（在已验证的骨架上）
Phase 1 第9-14天：SK Skills + 任务模板（可与 M3 尾部并行）
```

### 新增横切规则

1. **SK 保守匹配**：Phase 1 初期 intent 匹配只对精确完全匹配（bug_fix/modify）注入模板（非 Contains 包含匹配），验证窗口 1 后根据 Telemetry 放宽
2. **S2 debounce**：多文件编辑时 BuildStep 只在最后一个文件完成后触发
3. **S2 线程安全**：必须使用 `JoinableTaskFactory.RunAsync`
4. **S2 跨层通信（Pane3 第3轮补充）**：S2 BuildStep.RunAsync 仅负责触发构建，构建结果通过 BuildResultCache 异步传递，AgentExecutor 每轮迭代开头检查缓存并注入。此跨层通信模式需在 Phase 1 骨架文档中明确记录。
5. **H1 pilot 策略**：先改 ReadFileTool + RunCommandTool 验证接口，再批量推进
6. **H1 依赖澄清（Pane1 第3轮补充）**：S2→H1 是软依赖而非硬依赖。S2 复用 H1 的截断是便利而非必须，S2 可自带简单截断。如果 H1 延期，S2 不应被阻塞。
7. **OH2 兼容迁移**：无 frontmatter 的旧 .md 自动归类为 `type: project`，同时从首行/文件名推导 description
8. **验证窗口增项**：每个验证窗口增加"Feature Flags 退化检查" + 验证窗口 3 增加"ReviewAgent 判断偏差分析"
9. **OH5 golden tests**：ReviewAgent 验收标准中包含 2-3 个基准测试 case
10. **AgentExecutor 提取约束**：渐进式提取前必须先补回归测试

### PostEditPipeline 接口修订

```csharp
// 泛化为 EditPipeline，支持 Pre/Post 两阶段
public enum EditPhase { PreEdit, PostEdit }

public interface IEditStep  // 原 IPostEditStep
{
    string Name { get; }
    bool IsEnabled { get; }
    int Order { get; }
    EditPhase Phase { get; }  // 新增：标识 Pre 或 Post
    bool FailureIsFatal { get; }  // 第4轮补充：默认 false (fail-open)，H2 SnapshotStep 设为 true (fail-close)
    bool ShouldRun(EditContext ctx);
    Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct);
}

public class EditContext  // 原 PostEditContext，补充字段
{
    public string FilePath { get; init; }
    public string OriginalContent { get; init; }
    public string NewContent { get; init; }
    public string Diff { get; init; }
    public string SessionId { get; init; }
    public int StepIndex { get; init; }
    public IAgentContext AgentContext { get; init; }
    public ToolResult InitialResult { get; init; }
    // 新增
    public EditMode EditMode { get; init; }      // Single/MultiEdit/MultiFile
    public string Language { get; init; }          // C++/C#/etc
    public string Intent { get; init; }            // 第4轮补充：当前 Agent 意图分类（S4 ImpactStep 触发条件用）
    public List<SymbolChange> EditedSymbols { get; init; }  // S3 用
    public bool IsLastFileInBatch { get; init; }   // S2 debounce 用
}

public enum EditMode { Single, MultiEdit, MultiFile }
```

---

## 五、风险登记簿（按优先级排序）

| # | 风险 | 级别 | 缓解措施 | 责任Phase |
|---|------|------|---------|-----------|
| 1 | OH5 关键路径瓶颈 | 🔴 高 | +2-3天缓冲；PA1 prompt 可提前；OH3 AgentHook 可跳过 | Phase 5 |
| 2 | H1 散弹枪手术遗漏 | 🟡 中 | Pilot 策略（先改2个验证接口，再批量推进）；绘制截断调用图 | Phase 2 |
| 3 | EditPipeline 跨Phase集成 | 🟡 中 | Order编号 + fail-open + 每个 step 加入时回归测试已有 step | Phase 1-7 |
| 4 | OH2 中文分词效果 | 🟡 中 | 最小可行方案（按字+停用词）+ Feature Flag 可回退全量拼接 | Phase 3 |
| 5 | SK 模板注入 intent 匹配 | 🟡 中 | 保守精确匹配 + 置信度阈值 + Telemetry 验证（注：与 DynamicToolSelector 工具过滤是两套逻辑，后者风险低） | Phase 1 |
| 6 | GitNexus 子进程稳定性 | 🟡 中 | 优雅降级回 TF-IDF only + 超时保护 | Phase 6-7 |
| 7 | VS DTE 线程亲和性 | 🟡 中 | JoinableTaskFactory 强制使用 | Phase 5 |
| 8 | 涉密环境磁盘配额 | 🟢 低 | H1/H2/T1 各有容量上限 + 自动清理 + 预检 | 贯穿 |
| 9 | OH3 未激活中间件冲突（Pane3 第3轮补充） | 🟡 中 | OH3 实施前审视4个已实现未激活中间件（Logging/Monitoring/Permission/Timeout），与 Hook 功能对比后决定激活或清理 | Phase 6 |
| 10 | OH2 旧格式迁移失败（Pane3 第3轮补充） | 🟢 低 | 自动归类 + 从首行推导 description + 迁移前备份原文件 | Phase 3 |

---

## 六、审查者签名

### 第4轮终审结果（最终审批）

- **Pane 1（技术可行性）**：✅ **APPROVE 附条件**。发现2个接口遗漏：IEditStep 缺 `FailureIsFatal`（H2 fail-close 无法表达）、EditContext 缺 `Intent`（S4 触发条件无法满足）。已补充入接口定义。另发现 Phase 1→2 推迟约1周（被26-28周吸收）和 Context Reset 与 DoomLoop 优先级关系需 M1 实施时澄清（低风险）。横切规则 #4 和 #6 有微妙张力但非矛盾（H1 上线后 S2 应切换统一截断）。**条件已满足，正式 APPROVE。**
- **Pane 2（Harness原则）**：✅ **APPROVE 附条件**。Harness 符合度应从 75-80% 更新为 ~85%（修正后提升 +6-8%）。已更新总体结论表。BuildStep 旁路输出模式应在设计文档中标注为设计意图。排期 26-28 周符合成本意识原则。6项接受的偏离全部确认合理，无需重新分类。**条件已满足，正式 APPROVE。**
- **Pane 3（架构风险）**：✅ **APPROVE 无异议**。全部反馈准确整合。EditPipeline 接口完整解决4个设计问题。风险登记簿10项覆盖完整。Phase 1 执行顺序与代码依赖一致（经源码行号交叉验证）。渐进式提取可执行，建议 CompactionCoordinator 提取加 escape hatch（补测试超 1.5天则推迟到验证窗口1）。
- **Pane 0（综合协调）**：✅ **APPROVE**。Pane1 的2个接口补充和 Pane2 的符合度更新已整合入终稿。Pane3 的 escape hatch 建议记录在案。四实例经4轮审查全部 APPROVE，文档定稿。

### 第3轮确认审查结果

- **Pane 1（技术可行性）**：✅ 确认通过。2处小遗漏和2处补充已整合。排期建议内部 26-28 周已采纳。
- **Pane 2（Harness原则）**：✅ 确认通过。Context Reset 结构化交接摘要已补充。Generator-Evaluator 升级为第一优先重评项已修正。
- **Pane 3（架构风险）**：✅ 确认通过。BuildResultCache 跨层通信约束、提取前补回归测试、风险 #9/#10 均已整合。
- **Pane 0（综合协调）**：✅ 确认通过。第3轮反馈全部整合，共12处修正。

### 总计审查工作量

| 轮次 | 内容 | 参与实例 |
|------|------|---------|
| 第1轮 | 独立分析（可行性/Harness原则/架构风险） | Pane1/2/3 并行 |
| 第2轮 | 交叉验证（各实例审查其他实例发现） | Pane1/2/3 并行 |
| 第3轮 | 汇总确认（审查 Pane0 的综合报告） | Pane1/2/3 并行 |
| 终稿整合 | 整合第3轮所有反馈，更新文档 | Pane0 |
| 第4轮 | **终审审批**（重新完整阅读终稿+源码交叉验证） | Pane0/1/2/3 并行 |
| 定稿 | 整合第4轮条件修正，文档定稿 | Pane0 |
