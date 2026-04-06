# AICA v2.1 统一实施方案（修订版）

> **版本**：v2.0
> **日期**：2026-04-06
> **基于**：v1.0 方案 + 双实例联合审查（严格审查 + 可行性分析）
> **约束**：单人开发、VS2022 VSIX、MiniMax-M2.5 单模型、涉密离线环境
> **核心策略**：弱模型 + 强系统
> **排期**：22 周目标 / 24 周上限（原 v1.0 为 16 周）

---

## 一、v2.0 修订说明

本版本基于两个独立 Claude 实例对 v1.0 方案的联合审查，审查产出：
- `review_strict_audit.md` — 逻辑一致性、架构设计、排期、遗漏风险（12 个问题）
- `review_feasibility.md` — 15 个任务逐一可行性验证 + 源码交叉验证
- `review_joint_discussion.md` — 交叉对比与统一修正建议

### 主要修订

| 修订 | v1.0 | v2.0 | 理由 |
|------|------|------|------|
| 排期 | 16 周 / 6 Phase | 22-24 周 / 8 Phase + 3 验证窗口 | 两份报告独立估算均为 21-26 周 |
| S3 头文件同步 | Phase 4 | **Phase 2** | 模型无关、高价值、减轻 Phase 4 |
| S2 后台构建 | Phase 6 | **Phase 5** | 编译器反馈=最高价值的模型无关信号 |
| H3 权限 | Phase 3 整体 | **拆分**：H3a→Phase 3, H3b→Phase 4 | Phase 3 无缓冲 |
| OH2 工作量 | 5-8 天 | **8-10 天** | 中文分词复杂度被低估 |
| OH3 工作量 | 5-8 天 | **8-10 天** | shell 安全 + async UI 复杂度 |
| OH5 ReviewAgent | 5 维度检查清单 | **单维度 PoC**（范围控制） | MiniMax 4K/无工具/1次迭代 → 先验证 |
| 新增 Phase 0 | 无 | **T1 基础设施 (2-3天)** | Telemetry 是多个任务的前提 |
| 新增 | 无 | **PostEditPipeline 抽取**（Phase 1） | 管理 7 项 EditFileTool 修改 |
| 新增 | 无 | **Feature flags**（每个新功能） | 增量发布与回滚 |
| 新增 | 无 | **3 个验证窗口**（各 1 周） | 用户反馈回路 |
| Skills 机制 | 未明确 | **任务模板=被动注入**；SkillTool=备用 | 弱模型不可靠调用元工具 |
| SK 步骤 8 | Phase 1 直接执行 | **延后到基线数据就绪** | 需要 T1 Telemetry 前提 |

### 保持不变

以下 v1.0 内容经审查确认准确，保持原样：
- 方案来源与整合说明（第一节）
- 搁置项及重新评估条件
- EditFileTool 7 项修改的执行流程排序（新增 PostEditPipeline 管理）
- 各任务的假设/失效信号/Telemetry 埋点/验收标准
- 对 MiniMax-M2.5 的系统级补偿矩阵

---

## 二、全量任务清单（修订版）

### 18 个任务项总览

| ID | 名称 | 类型 | 工作量 | 阶段 | v2.0 变更 |
|----|------|------|--------|------|-----------|
| T1-infra | Telemetry 基础设施 | 基础设施 | 2-3 天 | Phase 0 | **新增** |
| M1 | Prune 时机前移 | 补短板 | 2-3 天 | Phase 1 | 不变 |
| M3 | 编辑后自动格式化 | 补短板 | 4-6 天 | Phase 1 | +1天（验证 DTE 写入机制）；含 PostEditPipeline 抽取 |
| SK | Skills + 任务模板（S5+OH1 合并） | 架构升级 | 5-8 天 | Phase 1 | 明确被动注入机制；步骤 8 延后 |
| H1 | 工具输出持久化 | 补短板 | 10-15 天 | Phase 2 | 增加混合截断策略 |
| S3 | 头文件同步感知 | 扬长板 | 3-5 天 | Phase 2 | **从 Phase 4 前移** |
| OH2 | 结构化记忆升级 | 架构升级 | 8-10 天 | Phase 3 | 工作量上调 |
| H3a | 权限反馈注入 | 补短板 | 5 天 | Phase 3 | **H3 拆分 Part 1** |
| H2 | 文件快照与回滚 | 补短板 | 10-15 天 | Phase 4 | 不变 |
| H3b | 权限决策持久化 | 补短板 | 5 天 | Phase 4 | **H3 拆分 Part 2** |
| OH5 | SubAgent 泛化 + ReviewAgent PoC | 架构升级 | 5 天 | Phase 5 | 范围缩减为单维度 PoC |
| PA1 | PlanAgent 输出优化 | Harness 新增 | 2-3 天 | Phase 5 | 不变 |
| S2 | 编辑后自动构建 | 扬长板 | 4-5 天 | Phase 5 | **从 Phase 6 前移** |
| OH3 | Hooks 钩子系统 | 架构升级 | 8-10 天 | Phase 6 | 工作量上调；实施前审视未激活中间件 |
| S1 | 符号检索增强 | 扬长板 | 5-8 天 | Phase 6 | 不变 |
| S4 | GitNexus 主动触发 | 扬长板 | 4-6 天 | Phase 7 | 从 Phase 6 后移 |
| T2 | Telemetry 会话摘要 | Harness 新增 | 2-3 天 | Phase 7 | 从 Phase 6 后移 |

**横切项**（融入各任务，非独立排期）：

| ID | 名称 | 额外工作量 | 说明 | v2.0 变更 |
|----|------|----------|------|-----------|
| T1 | Telemetry 结构化日志 | 每组件 +0.5 天 | 在 Monitoring 中间件和各组件中埋点 | 基础设施前置到 Phase 0 |
| RC1 | ReviewAgent 检查清单设计 | 含在 OH5 内 | **单维度**：范围控制（移除"一致性"，S3 已覆盖） | 范围缩减 |
| AS1 | 假设记录 | 每组件 +0.25 天 | 文档级，记录假设/验证方式/失效信号 | 不变 |
| FF1 | Feature Flags | 每组件 +0.25 天 | AicaConfig 中为每个新功能添加 enable/disable 开关 | **新增** |

---

## 三、依赖关系（修订版）

### 依赖图

```
无依赖（可独立开始）：
  T1-infra, M1, M3, SK, H1, H2, H3a, H3b, OH2, S3

有依赖：
  OH5 SubAgent → 建议在 H1 之后（ReviewAgent 需要截断持久化支持完整输出审查）
  OH3 Agent Hook → OH5 SubAgent（需要 ReviewAgent）
  PA1 PlanAgent 优化 → OH5 SubAgent（需要 SubAgent 基类重构后再改 PlanAgent）
  S1 符号检索 → SK（需要意图分类）
  S4 Impact 分析 → SK（需要意图分类）
  S2 后台构建 → H1（复用截断基础设施）
  T2 会话摘要 → T1 结构化日志（需要日志数据）
  SK 步骤 8（去工具过滤）→ T1-infra（需要基线数据采集）

接口约束：
  S1 不应破坏 S3 已使用的符号数据接口（SymbolRecord 的 FilePath/Signature/Namespace/Name）
```

### EditFileTool 修改排序（7 项，不变）

```
EditFileTool 执行流程中的位置：

  ┌─ [编辑前] ─────────────────────────┐
  │  ① H2 快照：复制原始文件到快照目录    │  ← Phase 4
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑应用] ──────────────────────┐
  │  （现有逻辑：diff 预览→用户确认→写入）│
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑后，PostEditPipeline] ──────────┐  ← Phase 1 建立 pipeline 骨架
  │  ② M3 格式化：DTE.FormatDocument       │  ← Phase 1
  │  ③ S3 头文件同步：检测签名变化→追加警告 │  ← Phase 2
  │  ④ S4 Impact：条件触发→追加预警        │  ← Phase 7
  │  ⑤ H1 截断持久化：输出超限→存文件      │  ← Phase 2
  │  （现有逻辑：AppendDiagnosticsAsync）   │
  └────────────────────────────────────┘
           ↓
  ┌─ [编辑后，异步] ─────────────────────┐
  │  ⑥ S2 后台构建：fire-and-forget       │  ← Phase 5
  └────────────────────────────────────┘
           ↓
  ┌─ [贯穿] ────────────────────────────┐
  │  ⑦ T1 Telemetry：日志记录            │  ← 随各项加入
  └────────────────────────────────────┘
```

**v2.0 新增：PostEditPipeline 设计**

Phase 1 实施 M3 时，同步建立 `PostEditPipeline` 类（链式责任模式）：

```csharp
// PostEditPipeline.cs — Phase 1 建立骨架
public class PostEditPipeline
{
    private readonly List<IPostEditStep> _steps = new();

    public void Register(IPostEditStep step) => _steps.Add(step);

    public async Task<ToolResult> ExecuteAsync(PostEditContext ctx, CancellationToken ct)
    {
        var result = ctx.InitialResult;
        foreach (var step in _steps)
        {
            if (step.IsEnabled && step.ShouldRun(ctx))
                result = await step.RunAsync(ctx, result, ct);
        }
        return result;
    }
}

public interface IPostEditStep
{
    string Name { get; }
    bool IsEnabled { get; }  // Feature flag
    int Order { get; }       // 排序保证
    bool ShouldRun(PostEditContext ctx);
    Task<ToolResult> RunAsync(PostEditContext ctx, ToolResult current, CancellationToken ct);
}
```

**错误处理原则**（v2.0 新增）：
- 每个 step 独立可失败（fail-open），不阻塞后续 step
- 唯一例外：H2 快照（fail-close）—— 快照失败则不应继续编辑
- 每个 step 的失败记录到 T1 Telemetry

**跨 Phase 集成插入点**（v2.0 新增，解决 v1.0 文档化缺失）：

| Phase | 加入的 step | 插入位置 | 操作 |
|-------|-----------|---------|------|
| Phase 1 | M3 FormatStep | pipeline 首位 (Order=100) | 新建 pipeline + 注册 M3 |
| Phase 2 | S3 HeaderSyncStep | M3 之后 (Order=200) | 注册新 step |
| Phase 2 | H1 TruncationStep | S3 之后 (Order=400) | 注册新 step |
| Phase 4 | H2 SnapshotStep | 编辑前（pipeline 之外） | EditFileTool 编辑前调用 |
| Phase 5 | S2 BuildStep | pipeline 末尾异步 (Order=500) | 注册新 step |
| Phase 7 | S4 ImpactStep | S3 之后、H1 之前 (Order=300) | 注册新 step |

---

## 四、分阶段实施计划（修订版）

### Phase 0：基础设施（第 1 周）

**目标**：建立 Telemetry 基础设施和基线数据采集，为后续所有任务提供观测能力。

#### T1-infra：Telemetry 基础设施（2-3 天）

| 项 | 内容 |
|----|------|
| **新增** | `TelemetryLogger.cs`（结构化日志服务） |
| **内容** | 1. 日志格式定义：JSONL，字段 `{timestamp, session_id, event_type, tool_name, duration_ms, metadata}` |
|  | 2. 存储：`~/.AICA/telemetry/YYYY-MM-DD.jsonl`，自动按日分文件 |
|  | 3. 文件轮转：保留 30 天，单文件上限 10MB |
|  | 4. 磁盘总量上限：配置项 `telemetry.maxTotalSizeMB: 100`（涉密环境磁盘配额保护） |
|  | 5. 在 MonitoringMiddleware（现有但未激活）中接入 TelemetryLogger |
|  | 6. 启动基线数据采集：记录当前工具调用模式（为 SK 步骤 8 提供对比基线） |
| **验收标准** | 工具调用产生结构化日志；日志文件可读 |

#### Phase 0 交付物

- Telemetry 基础设施上线
- 基线数据开始采集（DynamicToolSelector 对比用）

---

### Phase 1：快速收益（第 2-3 周）

**目标**：用最小改动获得立竿见影的效果 + 建立 PostEditPipeline 模式。

#### M1：Prune 时机前移（2-3 天）

| 项 | 内容 |
|----|------|
| **改什么** | `AgentExecutor.cs`，压缩触发逻辑中 ~15 行调用顺序调整 |
| **怎么改** | 压缩条件达到 → 先调 `PruneOldToolOutputs`（已有函数）→ 重估 token → 仍超阈值才调 `ConversationCompactor` |
| **前置验证（v2.0 新增）** | 实施前确认 `ConversationCompactor` 不依赖"Prune 未运行"的假设 |
| **Feature flag** | `features.pruneBeforeCompaction: true` |
| **假设** | 修剪旧工具输出能释放足够 token，避免部分 LLM 压缩 |
| **失效信号** | Prune 后仍需 Compaction 的比率 >90% |
| **Telemetry 埋点** | `prune_tokens_freed`、`compaction_avoided (bool)` |
| **验收标准** | 压缩触发时先 Prune 后判断，日志可见 Prune 释放量 |

#### M3：编辑后自动格式化（4-6 天）

| 项 | 内容 |
|----|------|
| **改什么** | `EditFileTool.cs`，编辑成功后新增格式化调用 |
| **怎么改** | 1. **建立 PostEditPipeline 骨架**（v2.0 新增，~80 行） |
|  | 2. 实现 `FormatStep` 作为首个 IPostEditStep |
|  | 3. 调用 `DTE.ExecuteCommand("Edit.FormatDocument")` |
|  | 4. **前置验证（v2.0 新增）**：确认 EditFileTool 写入机制——如果直接写磁盘（非 DTE TextBuffer），需先 `DTE.ItemOperations.OpenFile()` 使文档在编辑器中打开 |
|  | 5. 加配置开关 `features.autoFormatAfterEdit: true/false` |
|  | 6. 仅对有格式化器的语言生效 |
| **Feature flag** | `features.autoFormatAfterEdit: true` |
| **假设** | MiniMax 输出格式经常不一致，VS 格式化器能修复 |
| **失效信号** | 格式化前后 diff 为空的比率 >90% |
| **Telemetry 埋点** | `format_changed (bool)`、`format_duration_ms` |
| **验收标准** | C/C++ 文件编辑后自动格式化；PostEditPipeline 骨架可用；可通过配置关闭 |

#### SK：Skills + 任务模板（5-8 天）

| 项 | 内容 |
|----|------|
| **改什么** | `Rule.cs`、`RuleLoader.cs`、`RuleEvaluator.cs`、`SystemPromptBuilder.cs`、`DynamicToolSelector.cs`；新增 `SkillTool.cs`、`.aica-rules/*.md` |
| **步骤** | 1. `Rule.cs` 增加 `Description` 字段 |
|  | 2. `RuleLoader.cs` 显式提取 frontmatter 中的 name/description |
|  | 3. `RuleEvaluator.cs` 增加 paths glob 匹配逻辑 |
|  | 4. 新增 `SkillTool.cs`（~60-80 行，按 name 查找 .md 返回内容） |
|  | 5. 将 `AddBugFixGuidance()`、`AddQtTemplateGuidance()` 外部化为 .md 文件 |
|  | 6. 新增 4 个任务模板技能文件：`bug-fix.md`、`feature-add.md`、`refactor.md`、`test-write.md` |
|  | 7. 每个模板包含**成功标准**字段（轻量版 Sprint 契约） |
|  | 8. ~~`DynamicToolSelector.cs` 去掉工具过滤~~ **延后**：等 T1 基线数据采集 2 周后（约 Phase 2 开始时）再评估是否去掉过滤，对比幻觉率变化后决定 |
| **技能激活机制（v2.0 明确）** | |

| 机制 | 说明 | 适用场景 |
|------|------|---------|
| **被动注入**（主要） | `ClassifyIntent()` 结果匹配 → 自动注入对应模板到 System Prompt | 任务模板（bug-fix/feature-add/refactor/test-write） |
| **主动调用**（备用） | LLM 调用 SkillTool → 返回 .md 内容 | 用户明确要求"用 bug-fix 流程"时 |
| **优先级** | 被动注入优先；弱模型不依赖主动调用可靠性 | |

| **Feature flag** | `features.skillsEnabled: true`、`features.taskTemplatesEnabled: true` |
| **假设** | 模板指导能改善 MiniMax 的工具调用序列 |
| **失效信号** | 注入模板前后任务完成率无显著差异 |
| **Telemetry 埋点** | `skill_injected (name)`、`skills_matched_count`、`skill_tool_called (bool)` |
| **验收标准** | `.aica-rules/` 中的 .md 文件可被自动加载注入；SkillTool 可被 LLM 调用；intent 匹配时自动注入模板 |

#### Phase 1 交付物

- Telemetry 基础设施上线 + 基线数据采集中
- Prune 前移到压缩触发前（先免费后昂贵）
- PostEditPipeline 骨架建立
- 编辑后自动格式化（可配置开关）
- Skills 系统上线 + 4 个任务模板 + SkillTool
- 硬编码 guidance 外部化为 .md 文件
- DynamicToolSelector 工具过滤**暂保留**（等基线数据）

---

### [验证窗口 1：第 4 周]

- 收集 6 名试用用户对 Phase 0-1 改动的反馈
- 检查 Telemetry 数据：Prune 释放量、格式化命中率、模板注入频率
- 评估基线数据：DynamicToolSelector 去过滤的风险
- 方向调整：如果 Skills 对 MiniMax 无效，调整后续 Phase 策略

---

### Phase 2：截断持久化 + 头文件同步（第 5-7 周）

**目标**：解决工具输出截断数据丢失 + 上线 C/C++ 杀手级功能。

#### H1：工具输出持久化（10-15 天）

| 项 | 内容 |
|----|------|
| **新增** | `ToolOutputPersistenceManager.cs`（集中式服务） |
| **改什么** | 所有工具文件的截断逻辑（ReadFile、Grep、ListDir、RunCommand、EditFileTool 等） |
| **步骤** | 1. 新增 `ToolOutputPersistenceManager`：存储位置 `~/.AICA/truncations/`，命名 `tool_{yyyyMMddHHmmssfff}.txt` |
|  | 2. 新增 `PersistAndTruncate()` 方法：判断是否超限 → 超限则存完整输出到文件 → 返回预览 + 文件路径 + 访问提示 |
|  | 3. 逐个工具接入：替换各工具独立的截断逻辑为 `PersistAndTruncate()` 调用 |
|  | 4. **混合截断策略（v2.0 新增）**：构建输出自动提取错误/警告行注入 ToolResult（不依赖模型回读）；其他工具输出保留"use read_file"提示 |
|  | 5. 后台清理：7 天过期自动删除 |
|  | 6. 磁盘总量上限：配置项 `truncations.maxTotalSizeMB: 200` |
|  | 7. 集中式阈值配置：替代各工具硬编码的截断值 |
| **Feature flag** | `features.truncationPersistence: true` |
| **假设** | Agent 会回头查看被截断的完整输出（混合策略降低对此假设的依赖） |
| **失效信号** | Agent 对截断文件的 `read_file` 调用频率接近 0 **且** 自动提取的错误行未被利用 |
| **Telemetry 埋点** | `truncation_persisted (bool)`、`truncation_file_size`、`truncation_file_read_count`、`auto_extract_error_count` |
| **验收标准** | 所有工具截断后完整输出可通过 read_file 访问；构建输出自动提取错误行；7 天自动清理 |

#### S3：头文件同步感知（3-5 天）← 从 Phase 4 前移

| 项 | 内容 |
|----|------|
| **新增** | `HeaderSyncDetector.cs`（~100 行） |
| **改什么** | `EditFileTool.cs`（通过 PostEditPipeline 注册 HeaderSyncStep，Order=200） |
| **逻辑** | 1. 编辑前：从 ProjectIndex 记录被修改符号的 Signature |
|  | 2. 编辑后：重新解析符号，比较 Signature 变化 |
|  | 3. 签名变化 → 按 Namespace + Name 查找 .h/.hpp 中的声明 → 追加警告 |
|  | 4. 仅签名变化时触发，函数体修改不触发 |
| **前移理由（v2.0）** | 程序化检测，不依赖模型推理；C/C++ 最常见错误之一；减轻原 Phase 4 过载 |
| **Feature flag** | `features.headerSyncDetection: true` |
| **假设** | MiniMax 看到同步警告后会修改头文件 |
| **失效信号** | 警告后实际修改率 <30% |
| **Telemetry 埋点** | `header_sync_warning_triggered`、`header_sync_acted_on` |
| **验收标准** | 编辑 .cpp 中函数签名时自动检测 .h 是否需要同步 |

#### DynamicToolSelector 评估点

Phase 2 开始时（约第 5 周），T1 基线数据已采集约 3 周。此时评估：
- 当前工具调用模式分布
- 幻觉调用不相关工具的频率
- 决策：是否执行 SK 步骤 8（去工具过滤）

#### Phase 2 交付物

- 集中式截断服务，所有工具统一接入
- 混合截断策略（构建输出自动提取 + 手动回读）
- 头文件同步检测（C/C++ 杀手级功能）
- DynamicToolSelector 去过滤决策

---

### Phase 3：记忆升级 + 权限反馈（第 8-10 周）

**目标**：精准注入相关记忆，权限交互更智能。

#### OH2：结构化记忆升级（8-10 天）← 工作量上调

| 项 | 内容 |
|----|------|
| **改什么** | `MemoryBank.cs` 核心重写 |
| **步骤** | 1. 记忆文件加 YAML frontmatter（name/description/type，复用 `YamlFrontmatterParser`） |
|  | 2. 分 4 类：user / feedback / project / reference |
|  | 3. 实现相关性检索逻辑（~100-150 行）：英文按单词（3+字符）+ 中文按单字（去停用词）；description 命中 2x 权重 + body 命中 1x；取 top N |
|  | 4. 中文分词：第一版按字分词 + 基础停用词表（~50 个常见停用词） |
|  | 5. 替换全量拼接 + 4000 字符硬截断 |
| **Feature flag** | `features.structuredMemory: true`（false 时回退到全量拼接） |
| **假设** | 相关性检索注入的记忆比全量拼接更精准、更省 token |
| **失效信号** | 用户反馈"需要的记忆没被注入"的频率上升 |
| **Telemetry 埋点** | `memories_total`、`memories_injected`、`memory_tokens_used` |
| **验收标准** | 记忆按相关性检索注入；消除 4000 字符硬截断；节省 ~300-500 tokens/请求 |

#### H3a：权限反馈注入（5 天）← H3 拆分 Part 1

| 项 | 内容 |
|----|------|
| **改什么** | `ToolExecutionPipeline.cs`、VS UI |
| **步骤** | 1. 用户拒绝工具调用时弹出可选反馈输入框 |
|  | 2. 反馈包装为 `ToolResult.Error("Permission denied. User feedback: {feedback}")` |
|  | 3. 自然进入 Agent 对话上下文，LLM 理解拒绝原因 |
| **Feature flag** | `features.permissionFeedback: true` |
| **假设** | 明确告知拒绝原因比让弱模型猜更有效 |
| **失效信号** | 反馈注入后同类工具的重试失败率未下降 |
| **Telemetry 埋点** | `permission_denied_with_feedback (bool)` |
| **验收标准** | 拒绝时可附带反馈；反馈进入 Agent 上下文 |

#### Phase 3 交付物

- 按相关性检索的记忆注入
- 权限拒绝反馈注入 Agent 上下文

---

### [验证窗口 2：第 11 周]

- 收集用户对 Phase 2-3 的反馈（特别是头文件同步、记忆注入效果）
- 检查 Telemetry：S3 警告触发率和用户响应率、OH2 记忆命中率
- 评估 H1 混合截断策略效果
- 方向调整：如果头文件同步响应率 <30%，考虑改为自动修复而非警告

---

### Phase 4：安全网 + 权限持久化（第 12-14 周）

**目标**：建立文件安全保障，完成权限体系。

#### H2：文件快照与回滚（10-15 天）

| 项 | 内容 |
|----|------|
| **新增** | `SnapshotManager.cs` |
| **改什么** | `EditFileTool.cs`（编辑前调用快照，PostEditPipeline 外）、VS 工具栏 UI |
| **步骤** | 1. 编辑前：复制原始文件到 `~/.AICA/snapshots/{sessionId}/{stepIndex}/{relativePath}` |
|  | 2. 回滚 API：`SnapshotManager.RestoreAsync(sessionId, stepIndex)` |
|  | 3. VS 工具栏按钮"回滚到步骤 N" |
|  | 4. 2MB 文件大小限制，自动排除大文件 |
|  | 5. 清理：会话结束后保留 7 天 |
|  | 6. 磁盘总量上限：配置项 `snapshots.maxTotalSizeMB: 500` |
| **错误处理（v2.0 新增）** | 快照失败 = fail-close（不继续编辑），这是唯一的 fail-close step |
| **Feature flag** | `features.fileSnapshots: true` |
| **假设** | MiniMax 会产生需要回滚的错误编辑 |
| **失效信号** | 回滚功能使用频率接近 0 |
| **Telemetry 埋点** | `snapshot_created`、`snapshot_restored`、`snapshot_size_bytes` |
| **验收标准** | 任意编辑步骤可回滚；UI 可视化快照点 |

#### H3b：权限决策持久化（5 天）← H3 拆分 Part 2

| 项 | 内容 |
|----|------|
| **改什么** | `SafetyGuard.cs`、VS UI |
| **步骤** | 1. 存储：`~/.AICA/permissions.json`，格式 `{ tool, pattern, decision, timestamp }` |
|  | 2. `SafetyGuard` 启动时加载，优先于默认规则 |
|  | 3. VS UI 支持"始终允许"/"始终拒绝"选项 |
|  | 4. 避免安全陷阱：危险操作（rm、format 等）不允许"始终允许" |
| **Feature flag** | `features.permissionPersistence: true` |
| **假设** | 内网用户权限偏好稳定 |
| **失效信号** | 持久化决策后同一权限的重复确认率未下降 |
| **Telemetry 埋点** | `persistent_decisions_count`、`persistent_decision_used` |
| **验收标准** | 权限决策跨会话保留；危险操作不可自动批准 |

#### Phase 4 交付物

- 文件快照与回滚系统
- 权限决策跨会话持久化

---

### Phase 5：评估基础 + PlanAgent + 构建（第 15-17 周）

**目标**：建立 Evaluation 基础，修复计划偏离，上线编译器反馈。

#### OH5：SubAgent 泛化 + ReviewAgent PoC（5 天）← 范围缩减

| 项 | 内容 |
|----|------|
| **新增** | `SubAgent.cs` 基类、`ReviewAgent.cs` |
| **重构** | `PlanAgent.cs` 改为 SubAgent 实例化配置 |
| **SubAgent 基类设计** | 同 v1.0（SystemPrompt, AllowedTools, MaxIterations, TimeoutSeconds, TokenBudget） |

| 预定义实例 | 配置 |
|-----------|------|
| PlanAgent | 规划 prompt + 只读工具 + 10 次迭代 + 60s + 16K |
| ReviewAgent | **范围控制**检查清单 + 无工具 + 1 次迭代 + 15s + 4K |

**RC1 ReviewAgent 检查清单（v2.0 修订）**：

| 维度 | 状态 | 说明 |
|------|------|------|
| ~~一致性~~ | **移除** | S3 头文件同步已程序化覆盖 |
| 安全性 | **待定** | PoC 验证后决定是否扩展 |
| **范围控制** | **PoC** | 修改是否超出用户请求的范围？ |
| 规范性 | **待定** | M3 自动格式化已部分覆盖 |
| 可读性 | **待定** | PoC 验证后决定是否扩展 |

**PoC 策略（v2.0 新增）**：
1. 先只实现"范围控制"单维度
2. 运行 2 周，收集 Telemetry：`review_triggered`、`review_useful (user_feedback)`
3. 用户采纳率 >30% → 扩展到安全性维度
4. 用户采纳率 <20% → 简化或移除

**并发保护（v2.0 新增）**：
- ReviewAgent 默认**仅在用户请求时触发**（`features.reviewAgentAutoTrigger: false`）
- 避免增加 MiniMax-M2.5 并发压力（20 并发约束）
- 用户可配置为自动触发

| **Feature flag** | `features.reviewAgent: true`、`features.reviewAgentAutoTrigger: false` |
| **假设** | MiniMax 能基于检查清单给出有用审查 |
| **失效信号** | 审查意见的用户采纳率 <20% |
| **验收标准** | SubAgent 基类可用；PlanAgent 重构通过回归测试；ReviewAgent 范围控制 PoC 可运行 |

#### PA1：PlanAgent 输出优化（2-3 天）

| 项 | 内容 |
|----|------|
| **改什么** | `PlanAgent.cs`（Prompt 重写）、`AgentExecutor.cs`（重复注入逻辑） |
| **三个改进** | 粗粒度目标 + 成功标准 + 每轮重复注入 |
| **Feature flag** | `features.planAgentOptimized: true` |
| **假设** | 粗粒度目标 + 重复注入能减少计划执行偏离 |
| **失效信号** | 计划完成率未改善 |
| **Telemetry 埋点** | `plan_steps_total`、`plan_steps_completed`、`plan_abandoned` |
| **验收标准** | PlanAgent 输出含成功标准；Agent 日志可见每轮步骤目标注入 |

#### S2：编辑后自动构建（4-5 天）← 从 Phase 6 前移

| 项 | 内容 |
|----|------|
| **新增** | `VSAgentContext.TriggerBackgroundBuildAsync()`、`BuildResultCache.cs` |
| **改什么** | `EditFileTool.cs`（通过 PostEditPipeline 注册 BuildStep，Order=500，异步）、`AgentExecutor.cs`（注入构建结果） |
| **流程** | 编辑确认 → 后台触发 VS 增量构建（不阻塞）→ 构建完成缓存结果 → 下一轮 Agent 迭代开始时检查并注入 |
| **前移理由（v2.0）** | 编译器错误是模型无关的 ground truth 信号，价值极高 |
| **Feature flag** | `features.autoBackgroundBuild: true` |
| **假设** | 增量构建能捕获跨文件链接错误和类型不匹配 |
| **失效信号** | 构建结果中有价值的错误（Agent 据此修复了问题）比率 <10% |
| **Telemetry 埋点** | `build_triggered`、`build_errors_found`、`build_error_acted_on` |
| **验收标准** | 编辑后自动后台构建；构建错误在下一轮 Agent 迭代中可见 |

#### Phase 5 交付物

- SubAgent 基类 + PlanAgent 重构 + ReviewAgent PoC（单维度）
- PlanAgent 粗粒度输出 + 成功标准 + 每轮重复注入
- 后台异步构建 + 编译错误注入

---

### [验证窗口 3：第 18 周]

- 收集用户对 Phase 4-5 的反馈（快照回滚使用率、ReviewAgent PoC 价值、构建反馈效果）
- 关键决策点：
  - ReviewAgent 采纳率 >30% → Phase 6 扩展维度
  - ReviewAgent 采纳率 <20% → 简化 OH3 中的 Agent Hook 部分
  - S2 构建反馈效果 → 决定 Phase 7 中 S4 Impact 的优先级
- 评估是否需要调整 Phase 6-7 范围

---

### Phase 6：Hooks + 知识增强（第 19-21 周）

**目标**：建立可配置扩展机制，深化符号检索。

#### OH3：Hooks 钩子系统（8-10 天）← 工作量上调

| 项 | 内容 |
|----|------|
| **前置任务（v2.0 新增）** | 审视 4 个未激活中间件（Logging/Monitoring/Permission/Timeout），决定激活或清理 |
| **新增** | Hook 配置加载器、`CommandHookExecutor.cs`、`AgentHookExecutor.cs` |
| **改什么** | `ToolExecutionPipeline.cs`（Hook 触发点，明确在中间件管道之后） |
| **Hook 在管道中的位置（v2.0 明确）** | 中间件管道执行完毕 → 返回结果前 → 触发 PostToolUse Hook；中间件管道执行前 → 触发 PreToolUse Hook |
| **两种 Hook 类型** | Command Hook（Shell 命令）+ Agent Hook（调用 ReviewAgent，依赖 OH5） |
| **Agent Hook 范围（v2.0 调整）** | 如果验证窗口 3 中 ReviewAgent 采纳率 <20%，**跳过 Agent Hook**，只实现 Command Hook（可节省 3-4 天） |
| **Feature flag** | `features.commandHooks: true`、`features.agentHooks: false`（默认关闭，验证后开启） |
| **假设** | 可配置的 Hook 能减少硬编码扩展需求 |
| **失效信号** | 用户从不配置自定义 Hook |
| **验收标准** | Command Hook 可通过 JSON 配置启用；Agent Hook 根据验证窗口 3 决策 |

#### S1：符号检索增强（5-8 天）

| 项 | 内容 |
|----|------|
| **改什么** | `AgentExecutor.cs`（早期异步预热）、`KnowledgeContextProvider.cs` |
| **方案** | AgentExecutor 开始时异步调用 GitNexus context → 关系图数据 → TF-IDF Top-10 后用关系图扩展到 Top-15-20 → 3 秒超时保护 |
| **Feature flag** | `features.symbolGraphExpansion: true` |
| **假设** | 关系图扩展能帮助发现"虽不含关键词但有调用关系"的相关符号 |
| **失效信号** | 关系图扩展的命中率 <10% |
| **验收标准** | 用户问"修改 Foo 影响什么"时，能找到调用 Foo 的 Bar 和 Baz |

#### Phase 6 交付物

- 未激活中间件的审视与清理
- Hooks 系统（Command + 条件性 Agent Hook）
- TF-IDF + 关系图混合检索

---

### Phase 7：Impact + 收尾（第 22 周）

**目标**：完成最后的增强功能。

#### S4：GitNexus 主动触发（4-6 天）

| 项 | 内容 |
|----|------|
| **改什么** | `EditFileTool.cs`（通过 PostEditPipeline 注册 ImpactStep，Order=300） |
| **触发条件** | 意图为 refactor 或 bug_fix + 编辑目标为公共 API + 同一文件首次编辑 |
| **截断策略** | 仅 d=1（直接依赖），1000 tokens 上限，5 秒超时 |
| **Feature flag** | `features.proactiveImpactAnalysis: true` |
| **假设** | Agent 收到 impact 预警后会注意保护受影响的调用者 |
| **失效信号** | 预警后 Agent 仍然破坏调用者的比率未下降 |
| **验收标准** | 修改公共 API 时自动显示影响范围，不阻塞 Agent 循环 |

#### T2：Telemetry 会话摘要（2-3 天）

| 项 | 内容 |
|----|------|
| **新增** | 会话摘要生成器 |
| **改什么** | `AgentExecutor.cs`（会话结束钩子） |
| **输出** | `~/.AICA/telemetry/sessions/{id}.json`，包含：总迭代数、工具调用统计、各组件触发频率、计划完成率、token 总量 |
| **验收标准** | 每个会话结束时自动生成聚合摘要 |

#### Phase 7 交付物

- 修改公共 API 前的 impact 预警
- 会话级 telemetry 摘要

---

### [收尾：第 23-24 周]

- 全量集成测试（特别是 EditFileTool 7 项修改的回归测试）
- Bug 修复与性能优化
- 文档更新
- 6 名试用用户的最终验收反馈

---

## 五、周计划总览（修订版）

```
第 1 周  ──── T1-infra Telemetry 基础设施 (2d) + 基线采集启动 + M1 Prune 前移 (2d)
第 2 周  ──── M3 自动格式化 + PostEditPipeline 骨架 (5d)
第 3 周  ──── SK Skills+模板 (5d)
第 4 周  ──── [验证窗口 1] SK 收尾 (2d) + 用户反馈收集 + 方向调整
第 5 周  ──── H1 截断持久化 启动 (5d)
第 6 周  ──── H1 截断持久化 工具接入 (5d) + S3 头文件同步 启动 (并行)
第 7 周  ──── H1 收尾 (3d) + S3 收尾 (2d) + DynamicToolSelector 评估
第 8 周  ──── OH2 结构化记忆 启动 (5d)
第 9 周  ──── OH2 收尾 (4d) + H3a 权限反馈注入 启动 (1d)
第 10 周 ──── H3a 权限反馈注入 (4d) + 集成测试 (1d)
第 11 周 ──── [验证窗口 2] 用户反馈收集 + 方向调整
第 12 周 ──── H2 文件快照 核心 (5d)
第 13 周 ──── H2 文件快照 回滚API + UI (5d)
第 14 周 ──── H2 收尾 (2d) + H3b 权限决策持久化 (3d)
第 15 周 ──── H3b 收尾 (2d) + OH5 SubAgent 泛化 (3d)
第 16 周 ──── OH5 ReviewAgent PoC (2d) + PA1 PlanAgent 优化 (3d)
第 17 周 ──── S2 后台构建 (5d)
第 18 周 ──── [验证窗口 3] 用户反馈收集 + ReviewAgent 决策 + 方向调整
第 19 周 ──── OH3 Hooks 启动 (5d) — 先审视未激活中间件
第 20 周 ──── OH3 Hooks 完成 (5d) + S1 符号检索 启动 (并行)
第 21 周 ──── S1 符号检索 完成 (5d)
第 22 周 ──── S4 Impact 分析 (4d) + T2 会话摘要 (1d)
第 23-24 周 ── 集成测试 + Bug 修复 + 文档 + 最终验收
```

**每周额外隐含工作**：T1 telemetry 埋点（~0.5d）+ AS1 假设记录（~0.25d）+ FF1 Feature flag（~0.25d），已包含在各任务工作量中。

---

## 六、Harness 五维度预期效果（修订版）

完成全部 Phase 后，AICA Harness 五维度的状态：

| 维度 | 实施前 | 实施后 |
|------|--------|--------|
| **Agent Loop** | ✅ 成熟 | ✅ 成熟 + telemetry 可观测 |
| **Tool System** | ✅ 成熟，截断有缺陷 | ✅ 成熟 + 截断持久化（混合策略）+ Hooks 可扩展 + 自动格式化 |
| **Context Mgmt** | ⚠️ Prune 时机不对 | ✅ 分层策略完整（Prune→Compaction→Reset 就绪） |
| **Evaluation** | ❌ 缺 Agent 级 | ⚠️ ReviewAgent PoC + S3 头文件同步 + S4 Impact 预警 + S2 后台构建（四层评估） |
| **Memory** | ⚠️ 全量拼接 | ✅ 相关性检索 + Skills 数据驱动 + 快照安全网 |

### 对 MiniMax-M2.5 的系统级补偿（修订版，按价值排序）

| 优先级 | 模型弱点 | 补偿手段 | 依赖模型？ | 来源 |
|--------|----------|---------|-----------|------|
| **P0** | 容易遗漏跨文件影响 | 头文件同步检测 + Impact 预警 | ❌ 否 | S3 + S4 |
| **P0** | 编译错误反馈不完整 | 后台构建 + 结构化诊断 | ❌ 否 | S2 |
| **P0** | 输出格式不一致 | 自动格式化 | ❌ 否 | M3 |
| **P1** | 上下文窗口有限 | Prune 前移 + 截断存文件 | 部分（回读依赖模型） | M1 + H1 |
| **P1** | 探索效率低 | 任务模板指导 + 符号预注入 | 部分（需理解模板） | SK + S1 |
| **P1** | 幻觉导致错误编辑 | 快照回滚 + 格式化 | ❌ 否 | H2 + M3 |
| **P2** | 记忆注入不精准 | 相关性检索 | ❌ 否 | OH2 |
| **P2** | 不理解拒绝原因 | 反馈注入对话 | 部分（需理解反馈） | H3 |
| **P2** | 计划执行偏离 | 粗粒度目标 + 成功标准 + 重复注入 | 是 | PA1 |
| **P3** | 自评不可靠 | ReviewAgent PoC（待验证） | 是（MiniMax 审查能力待验证） | OH5 |

---

## 七、风险与缓解（修订版）

| 风险 | 影响 | 缓解措施 | 来源 |
|------|------|---------|------|
| EditFileTool 7 项修改冲突 | 合并困难、回归 bug | PostEditPipeline 链式模式 + 每项 fail-open + 回归测试套件 | v1.0 + B-6 |
| 中文分词复杂度（OH2） | 记忆检索效果差 | 先用简单按字分词，后续迭代优化 | v1.0 |
| S1 异步预热死锁 | AgentExecutor 卡住 | 3 秒超时 + fallback 到纯 TF-IDF | v1.0 |
| GitNexus 延迟（S4） | 拖慢 Agent 循环 | 条件触发 + 5 秒超时 + 非阻塞 | v1.0 |
| ReviewAgent 对弱模型无效 | 审查意见无价值 | 单维度 PoC 先验证；采纳率 <20% 则简化或移除 | A/B 共识 |
| 单人开发排期压力 | 延期 | 每 Phase 独立交付 + 3 个验证窗口 + 可在任意 Phase 后暂停 | v1.0 + A-5 |
| DynamicToolSelector 去过滤后回归 | MiniMax 幻觉调用不相关工具 | 先采集 3 周基线 → 对比 → 幻觉率上升则回退 | v1.0 + A-3 |
| DTE 格式化需文档在编辑器中打开 | M3 格式化无效 | 实施前验证 EditFileTool 写入机制，必要时先 OpenFile | B-1 |
| 离线涉密环境磁盘配额限制 | 存储功能不可用 | snapshots/truncations/telemetry 各设磁盘总量上限 | B-5 |
| ConversationCompactor 假设 Prune 未运行 | M1 引入隐蔽 bug | M1 实施前验证 Compactor 无此假设 | B-7 |
| ReviewAgent 增加 LLM 并发压力 | TTFT 恶化 | 默认"仅用户请求时触发"，可配置 | A-6 |
| 4 个未激活中间件与 Hooks 重叠 | 架构混乱 | OH3 实施前审视，激活或清理 | A-2 |
| VS2022 VSIX API 版本差异 | DTE 命令行为不一致 | 在目标 VS 版本上测试 | B 补充 |

---

## 八、参考文档索引

| 文档 | 路径 |
|------|------|
| v2.1 改进分析 | `doc/2.1plan/AICA_v2.1_Improvement_Analysis.md` |
| v2.1 优化路线图 | `doc/2.1plan/AICA_v2.1_Optimization_Roadmap.md` |
| OpenHarness 架构分析 | `doc/2.1plan/AICA_OpenHarness_Analysis.md` |
| Harness 学习笔记 | `doc/harness-study/README.md` |
| 严格审查报告 | `doc/2.1plan/review_strict_audit.md` |
| 可行性分析报告 | `doc/2.1plan/review_feasibility.md` |
| 联合讨论与修正 | `doc/2.1plan/review_joint_discussion.md` |
| v1.0 原始方案 | `doc/2.1plan/AICA_v2.1_Unified_Plan.md` |
| Anthropic 原文 | https://www.anthropic.com/engineering/harness-design-long-running-apps |
