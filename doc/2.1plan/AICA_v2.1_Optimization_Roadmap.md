# AICA v2.1 优化路线图

> 日期：2026-04-04
> 方法：双 Agent 协作（规划 + 审批），三轮交叉验证，每条结论有代码依据
> 核心策略：**"弱模型 + 强系统"** — 用系统智能弥补 MiniMax-M2.5 的不足
> 参考文档：[AICA_v2.1_Improvement_Analysis.md](./AICA_v2.1_Improvement_Analysis.md)

---

## 一、两大优化方向

### 补短板：填补对标 OpenCode 的真实差距

已在 [Improvement Analysis](./AICA_v2.1_Improvement_Analysis.md) 中详细分析，本文档不重复。核心 3 项：
- **H1** 工具输出持久化（截断存文件）
- **H2** 文件快照与回滚
- **H3** 权限反馈注入 + 决策持久化

### 扬长板：深化 AICA 的不可替代优势

AICA 运行在 VS2022 内部，拥有 CLI 工具（OpenCode/Claude Code）永远无法获得的能力：
- 本地符号索引（Tree-sitter 解析 + TF-IDF 检索）
- VS 解决方案结构感知（项目依赖、编译配置）
- GitNexus 代码知识图谱（调用链、影响分析）
- 任务意图分析（复杂度分级 + 动态工具选择）

本文档聚焦于如何**将这些优势转化为对 MiniMax-M2.5 的系统级补偿**。

---

## 二、扬长板改进项（5 项）

### S5: 任务模板化（Task Template Injection）

**审批状态**：✅ 批准 | **优先级**：P0 | **风险**：低 | **工作量**：2-3 天

**目标**：为 bug_fix / feature_add / refactor 等常见意图注入结构化工作流指导，给 MiniMax 一个"脚手架"。

**代码基础**：
- `TaskComplexityAnalyzer` 已实现 Simple/Medium/Complex 三级分类
- `DynamicToolSelector.ClassifyIntent()` 已被 `AgentExecutor` 调用
- `SystemPromptBuilder` 已有 `AddBugFixGuidance()` 和 `AddQtTemplateGuidance()` 两个特定意图处理——**框架已就绪，只需扩展**

**接入点**：`SystemPromptBuilder` 中现有的 Add*Guidance 方法集

**实现方案**：
```
1. 扩展 Add*Guidance 方法系列：
   - AddFeatureGuidance()：搜索相关代码 → 规划 → 实现 → 构建验证
   - AddRefactorGuidance()：分析影响范围 → 分步重构 → 逐步验证
   - AddTestGuidance()：分析被测代码 → 编写测试 → 运行验证

2. 条件化注入（控制 token 开销）：
   - complexity >= Medium 时才注入
   - 每次只注入 1-2 个匹配的模板（~300-600 tokens/个）
   - 通过 DynamicToolSelector 已有的 intent 分类驱动
```

**审批 Agent 注意事项**：
- System Prompt 已接近预算上限（~16K tokens），需条件化注入
- 建议同时优化现有 prompt 中的冗余内容，腾出 ~1000 tokens 空间

**预期收益**：MiniMax 收到明确的任务框架后，工具调用序列更有针对性，减少"漫无目的"的探索。

---

### S3: C/C++ 头文件同步感知（Header Sync Awareness）

**审批状态**：✅ 批准 | **优先级**：P1 | **风险**：低 | **工作量**：3-5 天

**目标**：编辑 `.cpp` 中的函数签名时，自动检测对应 `.h` 是否需要同步修改。

**代码基础**：
- `SymbolRecord` 已包含完整的元数据：`FilePath`、`Signature`（v2.8）、`StartLine/EndLine`、`Namespace`、`Name`
- `ProjectKnowledgeStore.UpdateFileSymbols()` 按文件路径精确维护符号
- `RegexSymbolParser` 同时处理 `.h` 和 `.cpp`，输出统一的 `SymbolRecord`
- 所有判断 .h/.cpp 对应关系所需的信息**已具备**

**接入点**：`EditFileTool` 编辑成功后、诊断追加前（现有 `AppendDiagnosticsAsync` 相邻位置）

**实现方案**：
```
新增 HeaderSyncDetector 类（~100 行）：
1. 编辑前：从 ProjectIndex 记录当前文件中被修改符号的 Signature
2. 编辑后：重新解析符号，比较 Signature 是否变化
3. 若签名变化：
   ├─ 从 ProjectIndex 中按 Namespace + Name 查找其他文件中的同名符号
   ├─ 过滤出 .h/.hpp 文件中的声明
   └─ 追加警告到 ToolResult：
      "⚠️ HEADER SYNC: You modified `CAxis::SetPosition` signature in foo.cpp.
       The declaration in foo.h:42 may need updating."
4. 仅在签名变化时触发，函数体修改不触发
```

**匹配规则**：Namespace 相同 + Name 相同 + 参数模式差异 → 触发警告

**预期收益**：C/C++ 开发中"声明-定义不同步"是高频问题，特别在重构时。自动检测可减少约 70% 的遗漏。

---

### S1: 符号检索增强（Relation-Enriched Retrieval）

**审批状态**：⚠️ 有条件批准 | **优先级**：P2 | **风险**：中 | **工作量**：5-8 天

**目标**：在 TF-IDF 关键词检索基础上，通过 GitNexus 关系图扩展"虽然不含关键词但有调用关系"的相关符号。

**代码基础**：
- `KnowledgeContextProvider.RetrieveContext()` 是当前的检索入口，纯 TF-IDF 评分
- `McpBridgeTool` 可调用 `gitnexus_context` 获取符号的调用者/被调用者

**审批 Agent 发现的关键阻塞**：
- `KnowledgeContextProvider.RetrieveContext()` 是**同步方法**
- `McpBridgeTool.ExecuteAsync()` 是**异步方法**，且需要先 `EnsureRunningAsync()` 启动 GitNexus 进程
- 在 SystemPromptBuilder（同步）中调用 GitNexus（异步）**不可行**

**解决方案**：
```
方案：在 AgentExecutor.ExecuteAsync() 的早期阶段异步预热

AgentExecutor.ExecuteAsync() 开始时（现有 PlanAgent 调用之前）：
1. 异步调用 GitNexus context，获取用户查询相关的关系图数据
2. 将结果传入 KnowledgeContextProvider 作为"关系补充"
3. TF-IDF 检索 Top-10 后，用关系图扩展到 Top-15-20
4. 超时保护：3 秒未返回则跳过关系扩展，仅用 TF-IDF 结果

不修改 SystemPromptBuilder 的同步签名，在调用 Build() 之前已准备好数据。
```

**预期收益**：用户问"修改 Foo 类会影响什么"时，不仅找到 Foo，还能找到调用 Foo 的 Bar 和 Baz。

---

### S4: GitNexus 主动触发（Proactive Impact Analysis）

**审批状态**：⚠️ 有条件批准 | **优先级**：P2 | **风险**：中 | **工作量**：4-6 天

**目标**：Agent 执行修改类工具前，系统自动调用 `gitnexus_impact` 预警影响范围。

**审批 Agent 发现的关键问题**：
- `gitnexus_impact` 超时 30 秒，无条件调用会严重拖慢 Agent 循环
- Impact 返回数据可能很大（d=1/d=2/d=3 深度），无截断机制
- Token 预算（~27K）不足以每次都容纳 impact 分析

**解决方案（条件触发 + 截断）**：
```
触发条件（仅在以下情况调用）：
- 意图为 refactor 或 bug_fix（通过 DynamicToolSelector.ClassifyIntent 判断）
- 编辑目标是公共 API（.h 文件或 public 方法）
- 同一文件首次编辑（后续编辑跳过）

截断策略：
- 仅返回 d=1（直接依赖），忽略 d=2/d=3
- 结果限制在 1000 tokens 以内
- 格式：简洁列表 "⚠️ Impact: FooBar.cpp:SetPosition() and 3 other callers may be affected"

注入方式：
- 追加到 edit 工具的 ToolResult 中（与诊断追加类似的模式）
- 非阻塞：异步启动，超时 5 秒后跳过
```

**预期收益**：Agent 在修改公共 API 前收到影响预警，避免"改了一个函数，破坏了十个调用者"。

---

### S2: 编辑后自动构建 + 诊断回注（Post-Edit Build）

**审批状态**：⚠️ 降级为后台异步 | **优先级**：P3 | **风险**：中 | **工作量**：4-5 天

**审批 Agent 的反对意见**（已采纳）：
- VSAgentContext 当前**没有**构建触发 API
- C++ 增量构建 10-60 秒，同步等待会阻塞 Agent 循环
- 在用户确认流程中插入构建等待会降低体验

**降级后的方案（后台异步构建）**：
```
不阻塞 Agent 循环的实现方式：

1. 用户确认编辑 → 文件写入成功 → 后台异步触发 VS 增量构建
2. Agent 继续下一轮迭代（不等待构建完成）
3. 构建完成时：
   ├─ 收集编译错误（结构化：文件/行号/错误代码/描述）
   └─ 缓存到 BuildResultCache
4. 下一轮 Agent 迭代开始时：
   ├─ 检查 BuildResultCache 是否有新结果
   └─ 如有：注入为系统消息 "[Build completed: 3 errors found]"

关键约束：
- 用户确认流程完全不变
- Agent 不等待构建
- 构建结果在"最终可用时"才注入，而非强制等待
```

**实现前提**：需要新增 `VSAgentContext.TriggerBackgroundBuildAsync()`，调用 `DTE.Solution.SolutionBuild.BuildProject()`

**预期收益**：相比现有的 Error List 轮询（只看当前文件的语法错误），增量构建能捕获**跨文件的链接错误和类型不匹配**。但因为是异步的，收益延迟一轮才体现。

---

## 三、补短板改进项（简要，详见 Improvement Analysis）

| 编号 | 改进项 | 优先级 | 工作量 |
|------|--------|-------|--------|
| H1 | 工具输出持久化（截断存文件） | P0 | 2-3 周 |
| H2 | 文件快照与回滚 | P0 | 2-3 周 |
| H3 | 权限反馈注入 + 决策持久化 | P1 | 2 周 |
| M1 | Prune 时机前移 | P1 | 0.5 周 |
| M2 | 会话分叉 | P2 | 2 周 |
| M3 | 编辑后自动格式化 | P2 | 1 周 |

---

## 四、分阶段路线图

### Phase 1：快速收益 + 安全网（3-4 周）

| 项目 | 类型 | 工作量 | 可并行 |
|------|------|--------|--------|
| **S5** 任务模板化 | 扬长板 | 2-3 天 | ✓ |
| **M1** Prune 时机前移 | 补短板 | 2-3 天 | ✓ |
| **M3** 编辑后自动格式化 | 补短板 | 2-3 天 | ✓ |
| **H1** 工具输出持久化 | 补短板 | 2-3 周 | ✓ |
| **H3** 权限反馈注入 | 补短板 | 2 周 | ✓ |

> S5/M1/M3 可在 1 周内完成，与 H1/H3 并行。

**Phase 1 交付物**：
- 4 种意图模板（bug_fix/feature/refactor/test）条件注入
- Prune 前移到压缩触发前
- 编辑后自动格式化（可配置开关）
- 截断输出存文件 + 集中式截断服务
- 权限拒绝反馈 + 决策持久化

### Phase 2：文件安全 + C++ 深化（3-4 周）

| 项目 | 类型 | 工作量 | 可并行 |
|------|------|--------|--------|
| **H2** 文件快照与回滚 | 补短板 | 2-3 周 | ✓ |
| **S3** 头文件同步感知 | 扬长板 | 3-5 天 | ✓ |

> S3 的代码基础完善（SymbolRecord 已有 Signature/FilePath/Namespace），可快速实现。

**Phase 2 交付物**：
- 轻量级文件快照系统（方案 A：文件复制）
- HeaderSyncDetector：编辑 .cpp 签名时自动检测 .h 是否需要同步

### Phase 3：知识图谱增强（2-3 周）

| 项目 | 类型 | 工作量 | 前置条件 |
|------|------|--------|---------|
| **S1** 符号检索增强 | 扬长板 | 5-8 天 | 需解决异步注入问题 |
| **S4** GitNexus 主动触发 | 扬长板 | 4-6 天 | 需实现条件触发 + 截断 |

> S1 和 S4 都依赖 GitNexus，可共享异步调用基础设施。

**Phase 3 交付物**：
- TF-IDF + 关系图混合检索（Top-10 → Top-15-20）
- 修改公共 API 前的 impact 预警（条件触发、d=1 深度、5s 超时）

### Phase 4：体验完善（2-3 周）

| 项目 | 类型 | 工作量 |
|------|------|--------|
| **S2** 后台异步构建 | 扬长板 | 4-5 天 |
| **M2** 会话分叉 | 补短板 | 2 周 |

**Phase 4 交付物**：
- 编辑确认后后台触发构建，下一轮注入编译错误
- 会话分叉 UI + API

---

## 五、审批记录

| 建议 | 规划 Agent | 审批 Agent | 最终决策 |
|------|----------|----------|---------|
| S1 符号检索增强 | 可行，~80-120 行 | ⚠️ SystemPromptBuilder 同步阻塞 | **有条件批准**：在 AgentExecutor 中异步预热 |
| S2 编辑后自动构建 | 可行，~60-100 行 | ❌ 构建延迟阻塞、无现成 API | **降级**：改为后台异步，不阻塞 Agent |
| S3 头文件同步感知 | 可行，~120-150 行 | ✅ 符号元数据完备，逻辑清晰 | **批准** |
| S4 GitNexus 主动触发 | 可行，~60-80 行 | ⚠️ 延迟 30s、token 膨胀 | **有条件批准**：条件触发 + d=1 截断 + 5s 超时 |
| S5 任务模板化 | 可行，~100-150 行 | ✅ 框架已就绪，低风险 | **批准** |

### 关键审批条件

| 条件 | 适用于 | 解决方案 |
|------|--------|---------|
| SystemPromptBuilder 同步 vs GitNexus 异步 | S1 | 在 AgentExecutor 早期异步预热，Build() 前数据就绪 |
| GitNexus 调用延迟 | S1, S4 | 3-5 秒超时保护 + fallback |
| 构建延迟阻塞 | S2 | 降级为后台异步，不等待 |
| System Prompt token 膨胀 | S5 | 条件化注入（complexity >= Medium）+ 优化现有冗余 |
| Impact 数据过大 | S4 | 仅 d=1 深度 + 1000 tokens 上限 |

---

## 六、预期收益总结

### 对 MiniMax-M2.5 的系统级补偿效果

| 模型弱点 | 系统补偿 | 来源 |
|----------|---------|------|
| 探索效率低（工具调用浪费轮次） | 任务模板指导 + 符号预注入 | S5 + S1 |
| 容易遗漏跨文件影响 | 头文件同步检测 + Impact 预警 | S3 + S4 |
| 编译错误反馈不完整 | 后台构建 + 结构化诊断 | S2 |
| 上下文窗口有限 | Prune 前移 + 截断存文件 | M1 + H1 |
| 幻觉导致错误编辑 | 快照回滚 + 格式化 | H2 + M3 |
| 不理解拒绝原因 | 反馈注入对话 | H3 |

### 量化预期

| 指标 | 改善来源 | 预估提升 |
|------|---------|---------|
| 首次编辑编译通过率 | S2 + S3 | +25-40% |
| 任务完成一致性 | S5 | +20-35% |
| 相关符号发现率 | S1 | +30% |
| 修改安全性 | S4 | +20% |
| 头文件一致性维护 | S3 | +70%（遗漏率降低） |
