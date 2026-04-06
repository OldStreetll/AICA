# AICA v2.1 Unified Plan v2.0 — Second-Round Feasibility Review

> **Reviewer**: Claude Opus 4.6 (automated review)
> **Date**: 2026-04-06
> **Scope**: v2.0 修订版逐项复查，聚焦 5 个重点验证项
> **Method**: Plan v2.0 vs v1.0 差异分析 + 源码二次验证

---

## Executive Summary

v2.0 对 v1.0 审查反馈的采纳质量很高，大部分修订都是正确方向。**22 周目标基本可行但无余量，24 周上限更现实。** 主要发现：

1. **Phase 7 存在工作量溢出**：S4(4-6d) + T2(2-3d) = 6-9 天压入 1 周，需要调整
2. **S3 前移到 Phase 2 无新依赖冲突**，但与 H1 并行开发需要注意 PostEditPipeline 注册顺序
3. **S2 前移到 Phase 5 依赖链正确**，但它是代码库中首个 fire-and-forget 异步模式，需额外设计
4. **PostEditPipeline 与 EditFileTool 兼容性良好**，但需处理 AppendDiagnosticsAsync 的迁移
5. **横切项实际总开销约 17-19 天**，v2.0 声称"已包含在各任务工作量中"，但部分任务的工作量未上调

---

## 一、排期逐 Phase 重新计算

### 方法论

对每个 Phase 计算：**任务本体工作量 + 横切项开销 + 集成测试 + 缓冲**。横切项按以下公式：
- T1 埋点：每个新组件 +0.5 天
- AS1 假设记录：每个新组件 +0.25 天
- FF1 Feature Flag：每个新组件 +0.25 天
- **合计：每个新组件 +1.0 天**

### Phase 0（第 1 周）：Telemetry 基础设施

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| T1-infra | 2-3 天 | 2-3 天 | 合理。JSONL + 文件轮转 + MonitoringMiddleware 接入 |
| M1 Prune 前移 | 2-3 天 | 2-3 天 | 合理。源码验证 ConversationCompactor 无 Prune 前置假设 |
| M1 横切项 | (含在内) | +1 天 | T1 埋点 + AS1 + FF1 |
| **Phase 0 合计** | 4-6 天 | **5-7 天** | |

**评估**：1 周 (5 工作日) **紧凑但可行**。T1-infra 和 M1 可并行（T1-infra 前 2 天独立，M1 在 T1-infra 完成后开始埋点）。

**源码验证 (NEW)**：
- ConversationCompactor.cs 检测 `"[Conversation condensed]"` 标记，而 PruneOldToolOutputs 使用 `"[compacted at {timestamp}...]"` 标记 → **两者标记不同，不会冲突**。M1 的前置验证项已确认安全。
- MonitoringMiddleware.cs 已实现（180 行，含 metrics 收集和错误清洗）但**未注册在默认中间件链中**。T1-infra 需要在 ChatToolWindowControl 中注册它，这增加 ~0.5 天的集成工作。

**风险**：LOW

---

### Phase 1（第 2-3 周）：快速收益

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| M3 + PostEditPipeline | 4-6 天 | 5-7 天 | PostEditPipeline 骨架 (~80行) + FormatStep + DTE 验证 |
| SK Skills+模板 | 5-8 天 | 5-8 天 | 步骤 8 延后，降低了风险 |
| 横切项 (M3+SK) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| **Phase 1 合计** | 9-14 天 | **12-17 天** | |

**评估**：计划 2 周 (10 工作日)。按最小估计 12 天 → 需要 2.4 周。**建议计划 2.5 周**（含半周缓冲）。

**源码验证 (NEW)**：
- EditFileTool 通过 DTE TextBuffer 写入（经 `context.ShowDiffAndApplyAsync`），**非直接文件系统写入**。因此 `DTE.ExecuteCommand("Edit.FormatDocument")` 可以直接工作，无需先 OpenFile。v2.0 中 M3 增加的 +1 天验证时间可能过度谨慎 — 但保留作为安全边际是合理的。
- PostEditPipeline 抽取点明确：当前 AppendDiagnosticsAsync 在编辑成功后作为最后一步调用（EditFileTool.cs line 319）。PostEditPipeline 应在此处替换，将 AppendDiagnosticsAsync 也迁移为一个 IPostEditStep（作为默认 step，Order=900）。**v2.0 遗漏了 AppendDiagnosticsAsync 的迁移说明**。

**风险**：LOW-MEDIUM（PostEditPipeline 骨架本身简单，但需处理现有诊断逻辑迁移）

---

### 验证窗口 1（第 4 周）

| 项目 | 计划工作量 | 实际评估 |
|------|-----------|---------|
| SK 收尾 + 用户反馈 | 1 周 | 1 周 |

**评估**：合理。利用验证窗口消化 Phase 1 的溢出也是好策略。

---

### Phase 2（第 5-7 周）：截断持久化 + 头文件同步

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| H1 截断持久化 | 10-15 天 | 12-15 天 | 混合截断策略增加了复杂度 |
| S3 头文件同步 | 3-5 天 | 3-5 天 | 源码确认 SymbolParser 支持 .h/.cpp 签名提取 |
| 横切项 (H1+S3) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| DynamicToolSelector 评估 | 0.5 天 | 0.5 天 | 数据分析 + 决策 |
| **Phase 2 合计** | 13-20 天 | **17.5-22.5 天** | |

**评估**：计划 3 周 (15 工作日)。按中位估计 20 天 → 需要 4 周。**Phase 2 可能需要 3.5-4 周**。

**关键验证**：
- **S3 并行开发可行性**：计划中第 6 周 S3 与 H1 并行。两者都注册到 PostEditPipeline（S3 Order=200，H1 TruncationStep Order=400），且修改不同文件（S3: HeaderSyncDetector.cs 新建，H1: ToolOutputPersistenceManager.cs 新建 + 各工具接入）。**无代码冲突**。但需注意：
  - S3 和 H1 的 TruncationStep 都在 PostEditPipeline 注册，注册时需要 PostEditPipeline 的 Register API 已就绪（Phase 1 交付）→ **依赖满足** ✅
  - S3 追加的警告信息会进入 ToolResult，然后 H1 的 TruncationStep 判断是否需要截断。顺序正确：先 S3 生成内容 → 再 H1 决定截断 ✅

- **混合截断策略**：v2.0 新增"构建输出自动提取错误/警告行"，这增加了 H1 的复杂度。需要识别不同工具输出中的错误模式（编译器输出格式、grep 匹配格式等），估计增加 2 天。

**风险**：MEDIUM（Phase 2 是工作量最大的 Phase，3 周偏紧）

---

### Phase 3（第 8-10 周）：记忆升级 + 权限反馈

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| OH2 结构化记忆 | 8-10 天 | 8-10 天 | v2.0 已上调，合理 |
| H3a 权限反馈注入 | 5 天 | 5-6 天 | VS UI 弹出框略有不确定性 |
| 横切项 (OH2+H3a) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| 集成测试 | 1 天(计划中) | 1 天 | |
| **Phase 3 合计** | 13-15 天 | **16-19 天** | |

**评估**：计划 3 周 (15 工作日)。按中位估计 17.5 天 → 需要 3.5 周。**3 周可行但无缓冲**。

**风险**：LOW-MEDIUM

---

### 验证窗口 2（第 11 周）

**评估**：位置合理。Phase 2-3 的 4 个主要功能（H1/S3/OH2/H3a）需要用户验证。建议此窗口也用于消化 Phase 2-3 的溢出。

---

### Phase 4（第 12-14 周）：安全网 + 权限持久化

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| H2 文件快照 | 10-15 天 | 12-15 天 | VS UI 回滚按钮是主要不确定性 |
| H3b 权限持久化 | 5 天 | 5-6 天 | SafetyGuard 集成需要仔细测试 |
| 横切项 (H2+H3b) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| **Phase 4 合计** | 15-20 天 | **19-23 天** | |

**评估**：计划 3 周 (15 工作日)。按中位估计 21 天 → 需要 4.2 周。**Phase 4 明显超出 3 周**。

**问题详解**：
- 周计划中第 14 周安排 "H2 收尾 (2d) + H3b 权限决策持久化 (3d)"，第 15 周安排 "H3b 收尾 (2d)"。即 H3b 实际跨越了 Phase 4 和 Phase 5 边界。
- 但第 15 周还安排了 "OH5 SubAgent 泛化 (3d)"，意味着 Phase 5 第一周就有 5 天满载。
- **这导致 Phase 4→Phase 5 过渡处无缓冲**。

**建议**：将 Phase 4 延长至 3.5 周（第 12-14.5 周），或接受 H3b 溢出到 Phase 5 第一周的现状。

**风险**：MEDIUM

---

### Phase 5（第 15-17 周）：评估基础 + PlanAgent + 构建

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| OH5 SubAgent + ReviewAgent PoC | 5 天 | 5-6 天 | v2.0 缩减范围至单维度 PoC，合理 |
| PA1 PlanAgent 优化 | 2-3 天 | 2-3 天 | 合理 |
| S2 后台构建 | 4-5 天 | 5-7 天 | **首个 fire-and-forget 模式，需额外设计** |
| 横切项 (OH5+PA1+S2) | (含在内) | +3 天 | 3 个新组件 × 1.0 天 |
| **Phase 5 合计** | 11-13 天 | **15-19 天** | |

**评估**：计划 3 周 (15 工作日)。按中位估计 17 天 → 需要 3.4 周。**Phase 5 偏紧**。

**S2 关键发现 (NEW)**：
- **S2 是整个代码库中首个 fire-and-forget 异步模式**。当前所有工具的异步操作都是 `await Task.Run()`（完全等待）。S2 需要：
  1. 设计 fire-and-forget 任务的生命周期管理（谁持有 Task 引用？异常如何捕获？）
  2. 处理 CS4014 编译器警告（未等待的 Task）
  3. BuildResultCache 需要线程安全（ConcurrentDictionary 或 lock）
  4. AgentExecutor 注入构建结果时的时序问题（构建可能在下一轮迭代开始后才完成）
- 这些设计问题增加 1-2 天工作量，将 S2 从 4-5 天推到 5-7 天。

**S2 前移依赖验证**：
- S2 依赖 H1（"复用截断基础设施"）。H1 在 Phase 2 完成，S2 在 Phase 5。**依赖满足** ✅
- S2 注册到 PostEditPipeline（Order=500，异步）。PostEditPipeline 在 Phase 1 建立。**依赖满足** ✅
- S2 需要 VSAgentContext 提供 build API。这在 VSIX 层已有基础（VSAgentContext.cs 存在）。需验证 VS2022 增量构建 API 可用性。**低风险** ✅

**风险**：MEDIUM

---

### 验证窗口 3（第 18 周）

**评估**：位置关键。ReviewAgent PoC 决策点放在这里是正确的，直接影响 Phase 6 OH3 的范围。

---

### Phase 6（第 19-21 周）：Hooks + 知识增强

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| OH3 Hooks | 8-10 天 | 8-12 天 | 包含中间件审视 + Command Hook + 条件性 Agent Hook |
| S1 符号检索 | 5-8 天 | 5-8 天 | 合理 |
| 横切项 (OH3+S1) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| **Phase 6 合计** | 13-18 天 | **15-22 天** | |

**评估**：计划 3 周 (15 工作日)。最坏情况 22 天 → 需要 4.4 周。

**关键不确定性**：OH3 的范围取决于验证窗口 3 的 ReviewAgent 决策。如果跳过 Agent Hook（采纳率 <20%），OH3 缩减为 5-7 天（仅 Command Hook），Phase 6 变为 12-17 天，3 周可行。如果保留 Agent Hook，Phase 6 偏紧。

**源码验证 (NEW)**：
- ToolExecutionPipeline 的中间件注册使用 `Use()` 方法，**反向执行**（首个注册 = 最后执行）。Hook 系统需要在中间件链的合适位置插入。当前只注册了 PreValidationMiddleware 和 VerificationMiddleware。
- OH3 计划中"审视 4 个未激活中间件"：确认 MonitoringMiddleware 已实现(180行)但未注册。LoggingMiddleware、TimeoutMiddleware 也存在但未注册。**如果 T1-infra 在 Phase 0 已激活 MonitoringMiddleware，OH3 只需审视剩余 2-3 个**，节省约 0.5 天。

**风险**：MEDIUM（取决于验证窗口 3 决策）

---

### Phase 7（第 22 周）：Impact + 收尾

| 项目 | 计划工作量 | 实际评估 | 说明 |
|------|-----------|---------|------|
| S4 Impact 分析 | 4-6 天 | 4-6 天 | 合理 |
| T2 会话摘要 | 2-3 天 | 2-3 天 | 合理 |
| 横切项 (S4+T2) | (含在内) | +2 天 | 2 个新组件 × 1.0 天 |
| **Phase 7 合计** | 6-9 天 | **8-11 天** | |

**评估**：计划 1 周 (5 工作日)。**即使最小估计 8 天也溢出 60%。Phase 7 必须扩展。**

详见下方第五节专项分析。

---

### 收尾（第 23-24 周）

| 项目 | 计划工作量 | 实际评估 |
|------|-----------|---------|
| 集成测试 | 2 周 | 2 周（EditFileTool 7 项回归测试是主要工作） |
| Bug 修复 | (含在内) | 至少 3-5 天 |
| 文档更新 | (含在内) | 1-2 天 |
| 最终验收 | (含在内) | 1-2 天 |

**评估**：2 周合理，但前提是 Phase 7 不溢出到此窗口。

---

### 总体排期评估

| Phase | 计划周数 | 实际评估周数 | 差异 |
|-------|---------|------------|------|
| Phase 0 | 1 | 1 | = |
| Phase 1 | 2 | 2.5 | +0.5 |
| 验证窗口 1 | 1 | 1 | = |
| Phase 2 | 3 | 3.5 | +0.5 |
| Phase 3 | 3 | 3 | = |
| 验证窗口 2 | 1 | 1 | = |
| Phase 4 | 3 | 3.5 | +0.5 |
| Phase 5 | 3 | 3 | = |
| 验证窗口 3 | 1 | 1 | = |
| Phase 6 | 3 | 3 | = (假设跳过 Agent Hook) |
| Phase 7 | 1 | **2** | **+1** |
| 收尾 | 2 | 2 | = |
| **总计** | **24** | **~26** | **+2** |

**结论**：
- **22 周目标不可行**（至少缺 4 周缓冲）
- **24 周上限偏紧**（约差 2 周）
- **26 周（6.5 个月）是更安全的上限**
- 但如果验证窗口能有效吸收前序 Phase 的溢出（这是合理的设计），**24 周仍然可以作为拉伸目标**

**建议**：保持 "22 周目标 / 24 周上限" 表述，但内部计划按 24 周安排，并明确：如果 Phase 2 或 Phase 4 溢出 >3 天，Phase 7 的 S4 降级为 Phase 8（超出 24 周则作为后续迭代）。

---

## 二、S3 前移到 Phase 2 的依赖分析

### 依赖链验证

```
S3 依赖：
  ├─ PostEditPipeline（Phase 1 建立） ✅ 满足
  ├─ ProjectIndex + SymbolParser（已有） ✅ 满足
  │   └─ TreeSitterSymbolParser 支持 .h/.cpp 签名提取 ✅ 已验证
  ├─ EditFileTool 集成点（AppendDiagnosticsAsync 之前） ✅ 通过 PostEditPipeline Order=200
  └─ 与 Phase 2 其他任务的冲突：
      └─ H1 TruncationStep (Order=400) — 无冲突，S3 在 H1 之前执行 ✅
```

### 新引入的风险

| 风险 | 严重性 | 说明 |
|------|--------|------|
| S3 与 H1 并行开发时的 PostEditPipeline 注册竞争 | LOW | 两者注册不同的 step，Order 不同，无代码冲突 |
| S3 警告输出影响 H1 截断判断 | LOW | 设计上正确：S3 先生成内容，H1 再判断是否截断 |
| S3 在 Phase 2 上线但 S4 在 Phase 7 → 中间有 5 个 Phase 间隔 | INFO | 无技术问题，但 S3+S4 的组合效果要到 Phase 7 才能完整体现 |

### 结论

**S3 前移到 Phase 2 无新依赖冲突。** 前移是正确决策：
1. 依赖全部满足（PostEditPipeline Phase 1、SymbolParser 已有）
2. 与 H1 并行无代码冲突
3. 作为模型无关的 C/C++ 杀手级功能，早期上线能快速产生价值
4. 减轻了原 Phase 4 的过载问题

**唯一建议**：v2.0 的 PostEditPipeline step 注册表中，S3 HeaderSyncStep (Order=200) 在 H1 TruncationStep (Order=400) 之前，但中间缺少一个 Order=300 的预留位置给 S4 ImpactStep。v2.0 已在第 183 行标注 S4 ImpactStep Order=300 — **确认正确，间距合理**。

---

## 三、S2 前移到 Phase 5 的依赖分析

### 依赖链验证

```
S2 依赖：
  ├─ H1 截断基础设施（Phase 2） ✅ 满足（Phase 5 > Phase 2）
  ├─ PostEditPipeline（Phase 1） ✅ 满足
  ├─ VSAgentContext build API ✅ VSIX 层已有基础
  └─ AgentExecutor 注入构建结果 ✅ AgentExecutor 每轮迭代开始处可注入
```

### 新引入的风险

| 风险 | 严重性 | 说明 |
|------|--------|------|
| 首个 fire-and-forget 模式的设计成本 | MEDIUM | 代码库中无先例，需建立模式（Task 生命周期、异常捕获、线程安全） |
| BuildResultCache 与 AgentExecutor 的时序竞争 | MEDIUM | 构建可能在下一轮迭代已开始后才完成 → 需要设计"下下轮注入"的 fallback |
| VS 增量构建在大型解决方案上的延迟 | LOW-MEDIUM | 如果增量构建 >10 秒，可能跨越多个 Agent 迭代才返回 |
| S2 前移后 Phase 5 工作量增加 | MEDIUM | Phase 5 现在有 3 个任务 (OH5+PA1+S2)，总量 11-15 天，3 周尚可 |

### S2 对 H1 的实际依赖程度

v2.0 标注 "S2 → H1（复用截断基础设施）"。验证具体依赖点：

- S2 的构建输出可能很长（编译器错误列表），需要截断 → 可以复用 H1 的 `PersistAndTruncate()` 方法
- 但 S2 的核心功能（触发构建 + 缓存结果 + 下一轮注入）不需要 H1
- **依赖是"便利性复用"而非"功能必需"**。如果需要，S2 可以实现自己的简单截断（但没必要，因为 H1 在 Phase 2 已完成）

### 结论

**S2 前移到 Phase 5 依赖正确，无冲突。** 但需注意：
1. **增加 1-2 天用于 fire-and-forget 模式设计**（v2.0 未计入）
2. BuildResultCache 需要 `ConcurrentDictionary` 或等效线程安全容器
3. 建议在 PostEditPipeline 的 BuildStep 中使用 `_ = Task.Run(async () => { ... })` 模式，并在外部捕获异常记录到 T1 Telemetry

---

## 四、PostEditPipeline 设计与 EditFileTool 兼容性分析

### 当前 EditFileTool 后编辑流程（源码验证）

```
EditFileTool.ExecuteAsync()
  └─ 编辑成功后（line ~293-319）：
      1. FileTimeTracker.Instance.RecordEdit(path)       // line 293
      2. context.ReadFileAsync(path, ct)                  // line 296（重读确认）
      3. 用户手动编辑检测（lines 299-311）                  // diff view 中用户修改
      4. AppendDiagnosticsAsync(result, filePath, ct)     // line 319（最后一步）
      └─ return result
```

### PostEditPipeline 集成点分析

v2.0 的 PostEditPipeline 设计在**第 4 步（AppendDiagnosticsAsync）处替换**，将其纳入 pipeline：

```
EditFileTool.ExecuteAsync()
  └─ 编辑成功后：
      1. FileTimeTracker.Instance.RecordEdit(path)       // 保持
      2. context.ReadFileAsync(path, ct)                  // 保持
      3. 用户手动编辑检测                                   // 保持
      4. postEditPipeline.ExecuteAsync(ctx, ct)           // 替换 AppendDiagnosticsAsync
          ├─ FormatStep (Order=100) ← M3
          ├─ HeaderSyncStep (Order=200) ← S3
          ├─ ImpactStep (Order=300) ← S4
          ├─ TruncationStep (Order=400) ← H1
          ├─ BuildStep (Order=500, async) ← S2
          └─ DiagnosticsStep (Order=900) ← 现有 AppendDiagnosticsAsync 迁移
      └─ return result
```

### 兼容性问题清单

| # | 问题 | 严重性 | 解决方案 |
|---|------|--------|---------|
| 1 | **AppendDiagnosticsAsync 迁移** | MEDIUM | v2.0 未提及将现有 AppendDiagnosticsAsync 迁移为 IPostEditStep。需要在 Phase 1 创建 DiagnosticsStep (Order=900) 作为默认 step。 |
| 2 | **PostEditContext 需要的数据** | LOW | PostEditContext 需要：filePath, editedContent, originalContent, ToolResult, IAgentContext, CancellationToken。这些在 EditFileTool 调用点都可获取。 |
| 3 | **多文件编辑模式** | MEDIUM | EditFileTool 有三种模式：单文件(line 156)、多编辑点(line 465)、多文件(line 597)。PostEditPipeline 需要在**每种模式**的成功路径后调用。v2.0 仅描述了单文件场景。 |
| 4 | **H2 快照在 pipeline 之外** | LOW | v2.0 正确标注 H2 SnapshotStep 在"编辑前"（pipeline 之外），不影响 PostEditPipeline。但 EditFileTool 中需要在 `ShowDiffAndApplyAsync` 调用之前插入快照逻辑。 |
| 5 | **FormatStep 对非 C/C++ 文件** | LOW | FormatStep 需要检查文件类型，仅对有格式化器的语言执行。IPostEditStep.ShouldRun(ctx) 可处理。 |
| 6 | **错误传播** | LOW | v2.0 的 fail-open 设计正确。但需确保每个 step 的异常不会腐蚀 ToolResult — 建议在 pipeline 中 try-catch 每个 step，失败时保留上一个 step 的 result。v2.0 的 ExecuteAsync 设计已隐含此模式。 |

### 关键遗漏：多文件编辑模式

EditFileTool 的多编辑点模式（line 465-590）和多文件模式（line 597-653）各自有独立的 AppendDiagnosticsAsync 调用点。PostEditPipeline 需要在这三个代码路径中都被调用：

```
单文件模式：  line 319 → pipeline
多编辑点模式：line 589 → pipeline  
多文件模式：  line 647 → pipeline（每个文件调用一次）
```

**v2.0 应明确：PostEditPipeline 在 EditFileTool 的所有 3 个编辑成功路径中调用。** 这增加约 0.5 天的集成工作。

### 结论

**PostEditPipeline 设计与 EditFileTool 兼容**，但有两个遗漏需补充：
1. AppendDiagnosticsAsync 必须迁移为 DiagnosticsStep（Order=900）
2. 三种编辑模式都需要调用 PostEditPipeline

设计本身（IPostEditStep 接口、Order 排序、fail-open 语义）是正确的，与现有代码架构兼容。

---

## 五、Phase 7 工作量溢出分析

### 问题

Phase 7 计划 1 周（5 工作日），包含：
- S4 GitNexus 主动触发：4-6 天
- T2 Telemetry 会话摘要：2-3 天
- 横切项：+2 天（2 组件 × 1.0 天）

**总计：8-11 天 vs 5 天可用。溢出 60-120%。**

### 周计划验证

```
第 22 周 ──── S4 Impact 分析 (4d) + T2 会话摘要 (1d)
```

计划将 T2 压缩到 1 天，但 T2 的任务描述标注 2-3 天。即使 T2 只用 1 天（仅骨架），S4 仍需 4-6 天。加上横切项 +2 天 → 7-9 天。

### 溢出方案选项

| 方案 | 描述 | 影响 |
|------|------|------|
| A. Phase 7 扩展到 2 周 | 第 22-23 周为 Phase 7，第 24-25 周收尾 | 总排期变 25 周（超 24 周上限 1 周） |
| B. T2 降级到收尾期 | Phase 7 只做 S4（1 周可完成），T2 放入第 23-24 周收尾期 | 收尾期压力增加，但 T2 不阻塞其他功能 |
| C. S4 降级为可选 | 如果 Phase 6 溢出，S4 推后到 v2.2 | 24 周可保，但 S4 Impact 是高价值功能 |
| D. S4 与 S1 合并到 Phase 6 | Phase 6 变成 4 周（第 19-22 周），S4 在 S1 之后 | Phase 6 压力大但逻辑上合理（都是知识图谱增强） |

**推荐方案 B**：
- T2（会话摘要）是"nice to have"，不阻塞核心功能
- T2 的开发内容（JSON 聚合）相对独立，可在收尾期间编写
- S4 Impact 分析是高价值功能，应确保在主开发期完成
- 保持 24 周上限不变

### 修改建议

```
第 22 周 ──── S4 Impact 分析 (4d) + 横切项 (1d)
第 23-24 周 ── T2 会话摘要 (2d) + 集成测试 + Bug 修复 + 文档 + 最终验收
```

---

## 六、横切项总工作量核算

### v2.0 的声明

> "每周额外隐含工作：T1 telemetry 埋点（~0.5d）+ AS1 假设记录（~0.25d）+ FF1 Feature flag（~0.25d），已包含在各任务工作量中。"

### 逐项验证

**新组件数量统计**（需要横切项的独立组件）：

| Phase | 组件 | 数量 |
|-------|------|------|
| Phase 0 | T1-infra, M1 | 2 (M1 需要 T1/AS1/FF1) |
| Phase 1 | M3(+PostEditPipeline), SK | 2 |
| Phase 2 | H1, S3 | 2 |
| Phase 3 | OH2, H3a | 2 |
| Phase 4 | H2, H3b | 2 |
| Phase 5 | OH5, PA1, S2 | 3 |
| Phase 6 | OH3, S1 | 2 |
| Phase 7 | S4, T2 | 2 (T2 本身是 telemetry 组件，但仍需 AS1/FF1) |
| **总计** | | **17 个组件** |

### 横切项总工作量

| 横切项 | 单位成本 | 组件数 | 总量 |
|--------|---------|--------|------|
| T1 Telemetry 埋点 | 0.5 天 | 17 | 8.5 天 |
| AS1 假设记录 | 0.25 天 | 17 | 4.25 天 |
| FF1 Feature Flag | 0.25 天 | 17 | 4.25 天 |
| T1-infra（一次性） | 2-3 天 | 1 | 2-3 天 |
| **总计** | | | **19-20 天** |

### 问题：横切项是否真的"已包含在各任务工作量中"？

逐任务检查 v2.0 是否在工作量估计中上调了横切项：

| 任务 | v1.0 工作量 | v2.0 工作量 | 差异 | 横切项(1d) 是否计入？ |
|------|-----------|-----------|------|---------------------|
| M1 | 2-3 天 | 2-3 天 | 不变 | ❌ 未计入 |
| M3 | 3-5 天 | 4-6 天 | +1 天 | ⚠️ +1 天用于 DTE 验证和 PostEditPipeline，非横切项 |
| SK | 5-8 天 | 5-8 天 | 不变 | ❌ 未计入 |
| H1 | 10-15 天 | 10-15 天 | 不变 | ❌ 未计入 |
| S3 | 3-5 天 | 3-5 天 | 不变 | ❌ 未计入 |
| OH2 | 5-8 天 | 8-10 天 | +3 天 | ⚠️ 上调用于中文分词，非横切项 |
| H3a | 5 天 | 5 天 | 不变 | ❌ 未计入 |
| H2 | 10-15 天 | 10-15 天 | 不变 | ❌ 未计入 |
| H3b | 5 天 | 5 天 | 不变 | ❌ 未计入 |
| OH5 | 5-8 天 | 5 天 | -3 天 | 范围缩减，横切项更不可能计入 |
| PA1 | 2-3 天 | 2-3 天 | 不变 | ❌ 未计入 |
| S2 | 4-5 天 | 4-5 天 | 不变 | ❌ 未计入 |
| OH3 | 5-8 天 | 8-10 天 | +3 天 | ⚠️ 上调用于 shell 安全/async UI，非横切项 |
| S1 | 5-8 天 | 5-8 天 | 不变 | ❌ 未计入 |
| S4 | 4-6 天 | 4-6 天 | 不变 | ❌ 未计入 |
| T2 | 2-3 天 | 2-3 天 | 不变 | ❌ 未计入 |

**结论：15/17 个组件的横切项工作量未在任务估计中体现。**

### 实际影响

横切项总量 ~19 天，分布在 22 周内 ≈ **每周约 0.86 天**（接近 v2.0 声称的 "每周 1 天"）。

问题不在于每周分布，而在于**某些 Phase 已经满载**：
- Phase 2（15 天计划 vs 3 周）如果再加 2 天横切项 → 更紧
- Phase 5（11-13 天计划 vs 3 周）如果再加 3 天横切项 → 溢出

### 建议

v2.0 的声明"已包含在各任务工作量中"**在任务级别不成立**（工作量未上调），但在 **Phase 级别基本成立**（每个 Phase 都有 1-3 天的缓冲天数可以吸收）。这种隐式缓冲是可以接受的，前提是：

1. **Phase 2 和 Phase 5 是高风险 Phase**，横切项最容易造成溢出
2. 验证窗口应作为溢出吸收机制（v2.0 已隐含此设计）
3. 建议在周计划中**显式标注横切项时间**，而非隐含。例如：`第 5 周 ──── H1 截断持久化 启动 (4d) + T1/AS1/FF1 (1d)`

---

## 七、其他发现

### 7.1 v2.0 解决了 v1.0 的大部分问题

| v1.0 问题 | v2.0 修正 | 状态 |
|-----------|----------|------|
| 16 周不现实 | 22-24 周 | ✅ 方向正确，24 周仍偏紧 |
| EditFileTool 7 项冲突 | PostEditPipeline | ✅ 正确设计 |
| T1 基础设施缺失 | Phase 0 前置 | ✅ |
| ReviewAgent 5 维度过于乐观 | 单维度 PoC | ✅ |
| Skills 依赖模型主动调用 | 被动注入为主 | ✅ 关键改进 |
| DynamicToolSelector 无基线 | 延后到基线就绪 | ✅ |
| OH2/OH3 工作量低估 | 上调 | ✅ |
| 磁盘配额风险 | maxTotalSizeMB 配置 | ✅ |
| 无 feature flags | 每组件添加 | ✅ |
| 无验证窗口 | 3 个验证窗口 | ✅ |

### 7.2 v2.0 引入的新遗漏

| # | 遗漏 | 影响 | 建议 |
|---|------|------|------|
| 1 | AppendDiagnosticsAsync 未迁移为 PostEditPipeline step | MEDIUM | Phase 1 M3 中添加 DiagnosticsStep (Order=900) |
| 2 | 多文件编辑模式的 PostEditPipeline 调用未覆盖 | MEDIUM | 明确三种编辑模式都需调用 |
| 3 | S2 是首个 fire-and-forget 模式，未计入额外设计时间 | LOW-MEDIUM | S2 工作量调整为 5-7 天 |
| 4 | Phase 7 溢出 | HIGH | 方案 B：T2 降级到收尾期 |
| 5 | 横切项声称"已计入"但任务工作量未上调 | LOW | Phase 级别可吸收，但应显式标注 |
| 6 | SK 步骤 8（去工具过滤）的最终执行时间未确定 | LOW | 可能永远不执行（如果基线数据显示过滤有效），需在计划中标为"条件性" |
| 7 | Phase 0 MonitoringMiddleware 注册需要修改 ChatToolWindowControl | LOW | T1-infra 工作量中应包含此项 |

### 7.3 验证窗口设计评价

v2.0 的 3 个验证窗口是**最重要的结构性改进**：

- 验证窗口 1（第 4 周）：Phase 0-1 验证 + Phase 1 溢出吸收 → **位置正确**
- 验证窗口 2（第 11 周）：Phase 2-3 验证 → **位置正确**
- 验证窗口 3（第 18 周）：ReviewAgent 决策点 + Phase 4-5 验证 → **关键决策点，位置正确**

验证窗口的双重功能（用户反馈 + 溢出吸收）是优秀的设计，使 24 周目标成为可能。

### 7.4 H3 拆分评价

将 H3 拆分为 H3a（Phase 3，反馈注入）和 H3b（Phase 4，决策持久化）：
- **正确决策**：解耦了 VS UI 工作（H3b 需要"始终允许/拒绝"按钮）与核心逻辑（H3a 的反馈注入）
- **依赖正确**：H3b 的 SafetyGuard 集成不依赖 H3a（两者独立）
- **但**：第 14-15 周 H3b 跨越了 Phase 4→Phase 5 边界，增加了过渡期压力

---

## 八、最终结论

### v2.0 质量评价

v2.0 是一份**高质量的修订文档**，对 v1.0 审查反馈的采纳率约 90%。主要改进（Phase 0 前置、PostEditPipeline、验证窗口、被动注入、单维度 PoC、H3 拆分）都是正确方向。

### 需要修正的 5 项

| 优先级 | 修正 |
|--------|------|
| **P0** | Phase 7 溢出：T2 降级到收尾期，或 Phase 7 扩展到 2 周 |
| **P1** | PostEditPipeline 补充：添加 DiagnosticsStep (Order=900) 迁移说明 + 三种编辑模式调用覆盖 |
| **P1** | S2 工作量上调至 5-7 天（首个 fire-and-forget 模式的设计成本） |
| **P2** | 横切项在周计划中显式标注（当前隐含在"已包含"中但未上调工作量） |
| **P2** | SK 步骤 8 标注为"条件性执行"（可能永远不执行） |

### 排期最终建议

- **乐观路径**：24 周（验证窗口有效吸收溢出、OH3 跳过 Agent Hook、无重大技术障碍）
- **现实路径**：25-26 周
- **保守路径**：26-28 周（包含所有横切项显式计入 + 所有可选项实施）

**v2.0 的 "22 周目标 / 24 周上限" 建议修改为 "24 周目标 / 26 周上限"。**

===V2_ANALYSIS_COMPLETE===
