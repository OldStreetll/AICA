# Phase 3 开发计划：记忆升级 + 权限反馈

> **版本**: v1.1 reviewed
> **日期**: 2026-04-08
> **状态**: Pane 5 审核通过（PASS WITH CONDITIONS），7 项修正已应用
> **前置**: Phase 2 R2 已完成 (commit `2194697`)
> **权威依据**: `AICA_v2.1_Unified_Plan_v2.1.md` Phase 3 章节 + 四实例审查终稿

---

## 一、代码现状分析

### OH2 相关文件

| 文件 | 路径 | 行数 | 现状 |
|------|------|------|------|
| MemoryBank.cs | `src/AICA.Core/Storage/MemoryBank.cs` | 102 | 静态类，`LoadAsync` 全量拼接 `.md`，4000 字符硬截断，无 YAML 解析 |
| YamlFrontmatterParser.cs | `src/AICA.Core/Rules/Parsers/YamlFrontmatterParser.cs` | 257 | 已有完整实现，fail-open 策略，返回 `FrontmatterParseResult`（Data/Body/HadFrontmatter/ParseError），可直接复用 |
| FeaturesConfig.cs | `src/AICA.Core/Config/FeaturesConfig.cs` | 58 | `StructuredMemory` (line 27) 已定义，默认 `true` |
| AgentExecutor.cs | `src/AICA.Core/Agent/AgentExecutor.cs` | ~600+ | Line 554-559：`BuildConversationHistory()` 中调用 `MemoryBank.LoadAsync`，结果传给 `SystemPromptBuilder.AddMemoryContext()` |
| SystemPromptBuilder.cs | `src/AICA.Core/Agent/SystemPromptBuilder.cs` | ~300+ | Line 201-211：`AddMemoryContext(string)` 将记忆追加到动态 prompt 区域，header 为 `## 项目记忆（跨会话）` |
| 记忆文件 | `AICA/mem/*.md` | 5 个文件 | 纯 markdown，无 frontmatter，当前由 Claude Code 记忆系统使用（非 AICA 运行时记忆） |

**注意**: AICA 运行时记忆文件路径为 `{WorkingDirectory}/.aica/memory/*.md`，与 `AICA/mem/` (开发者记忆) 不同。

### H3a 相关文件

| 文件 | 路径 | 行数 | 现状 |
|------|------|------|------|
| ToolExecutionPipeline.cs | `src/AICA.Core/Agent/ToolExecutionPipeline.cs` | 212 | 中间件管道，`ExecuteAsync` 构建链式调用 |
| PermissionCheckMiddleware.cs | `src/AICA.Core/Agent/Middleware/PermissionCheckMiddleware.cs` | ~86 | 拒绝时用 `ToolResult.Fail()`（Transient），未用 `SecurityDenied` |
| IUIContext.cs | `src/AICA.Core/Agent/IUIContext.cs` | ~55 | 已有 `ShowConfirmationAsync()`、`ShowFollowupQuestionAsync()` 等方法 |
| IAgentTool.cs (ToolResult) | `src/AICA.Core/Agent/IAgentTool.cs` | ~128 | `ToolResult.SecurityDenied(error)` 工厂方法已存在 |
| IPermissionHandler.cs | `src/AICA.Core/Agent/IPermissionHandler.cs` | ~28 | `RequestApprovalAsync()`、`RequestConfirmationAsync()` |
| FeaturesConfig.cs | 同上 | - | `PermissionFeedback` (line 30) 已定义，默认 `true` |

### 公共基础设施

| 组件 | 状态 |
|------|------|
| Feature Flags | Phase 3 两个 flag 均已定义 |
| Telemetry (AgentTelemetry) | Session 级 JSONL 写入，`SessionRecordBuilder` 可扩展 |
| Telemetry (TelemetryLogger) | 事件级后台写入，`LogEvent()` / `LogToolExecution()` 可直接使用 |

---

## 二、任务分解与 Pane 分工

### Pane 1：OH2 核心 — MemoryBank 重写 + 相关性检索

**涉及文件**（修改）:
- `src/AICA.Core/Storage/MemoryBank.cs` — 核心重写

**涉及文件**（新建）:
- `src/AICA.Core/Storage/MemoryEntry.cs` — 记忆条目数据模型
- `src/AICA.Core/Storage/RelevanceScorer.cs` — 相关性评分逻辑

**详细步骤**:

1. **定义 `MemoryEntry` 数据模型**
   ```
   class MemoryEntry {
       string Name          // 从 frontmatter 或文件名
       string Description   // 从 frontmatter 或首行推导
       string Type          // user / feedback / project / reference
       string Body          // frontmatter 之后的正文
       string FilePath      // 源文件路径
   }
   ```

2. **重写 `MemoryBank.LoadAsync` 签名**
   ```
   // 旧签名
   Task<string> LoadAsync(string workingDirectory, CancellationToken ct)

   // 新签名（增加 query 参数供相关性检索）
   Task<string> LoadAsync(string workingDirectory, string query, CancellationToken ct)
   ```
   - 保留旧签名作为重载（query = null 时全量加载，兼容回退）

3. **加载流程改造**
   - 读取 `.aica/memory/*.md`
   - 用 `YamlFrontmatterParser` 解析每个文件
   - 构建 `List<MemoryEntry>`
   - Feature flag 检查：`StructuredMemory = false` → 走旧的全量拼接 + 4000 截断逻辑
   - `StructuredMemory = true` → 调用 `RelevanceScorer` 评分 → 取 top N

4. **实现 `RelevanceScorer`（~100-150 行）**
   ```
   class RelevanceScorer {
       double Score(MemoryEntry entry, string query)
       List<MemoryEntry> SelectTopN(List<MemoryEntry> entries, string query, int maxTokens)
   }
   ```
   - 英文分词：按空格/标点分割，过滤 < 3 字符的词
   - 中文分词：按单字分割，去停用词（调用 Pane 2 提供的停用词表）
   - 评分规则：description 命中 ×2 + body 命中 ×1
   - `SelectTopN`：按分数降序，累加 token 估算直到超限
   - **Token 预算**：`const int MaxMemoryTokens = 2000`（定义在 MemoryBank 中）
   - **Token 估算公式**：`length / 3`（适配中英混合场景）

5. **Telemetry 埋点**
   - 通过 `TelemetryLogger.LogEvent()` 记录：
     - `memories_total` — 加载的记忆条目总数
     - `memories_injected` — 实际注入的条目数
     - `memory_tokens_used` — 注入的 token 估算值

**依赖**: Pane 2（停用词表）、Pane 4（调用方适配）

---

### Pane 2：OH2 辅助 — 停用词 + 兼容迁移

**涉及文件**（新建）:
- `src/AICA.Core/Storage/ChineseStopwords.cs` — 中文停用词表
- `src/AICA.Core/Storage/MemoryMigrator.cs` — 旧格式兼容迁移

**详细步骤**:

1. **中文停用词表 `ChineseStopwords.cs`**
   ```
   static class ChineseStopwords {
       static readonly HashSet<char> Stopwords = new HashSet<char> { '的', '了', '在', '是', '我', ... };
       static bool IsStopword(char c)
   }
   ```
   - ~50 个常见中文停用词（的、了、在、是、我、他、她、你、们、这、那、和、与、或、但、而、也、都、就、会、要、有、不、没、很、把、被、让、从、到、为、以、及、于、上、下、中、大、小、多、少、能、可、已、又、还、才、只、更、最、每）
   - 单字符粒度，HashSet 查询 O(1)

2. **兼容迁移 `MemoryMigrator.cs`**
   ```
   class MemoryMigrator {
       // 检测并迁移旧格式记忆文件
       Task<int> MigrateIfNeededAsync(string memoryDir, CancellationToken ct)
   }
   ```
   - 扫描 `.aica/memory/*.md`
   - 检测无 YAML frontmatter 的文件（`!result.HadFrontmatter`）
   - 自动处理：
     - 归类为 `type: project`
     - 从文件首行提取 description（如果首行是 `#` 标题则用标题文本；否则取前 80 字符）
     - 从文件名推导 name（去扩展名，`_` → 空格）
   - 迁移前备份原文件到 `.aica/memory_backup/`（创建目录如不存在）
   - 采用"写入临时文件 → 原子替换"模式：先写 `{file}.tmp`，成功后 `File.Move` 替换原文件；失败时从备份恢复
   - 返回迁移文件数

3. **调用时机**
   - `MemoryBank.LoadAsync` 开头调用 `MigrateIfNeededAsync`（仅首次）
   - **线程安全**：使用 `Interlocked.CompareExchange` 保护，确保并发场景下只执行一次：
     ```csharp
     private static int _migrated = 0;
     if (Interlocked.CompareExchange(ref _migrated, 1, 0) == 0)
     {
         await migrator.MigrateIfNeededAsync(...);
     }
     ```

**依赖**: 无（被 Pane 1 依赖）

---

### Pane 3：H3a — 权限反馈注入

**涉及文件**（修改）:
- `src/AICA.Core/Agent/Middleware/PermissionCheckMiddleware.cs` — 添加反馈逻辑
- `src/AICA.Core/Agent/IUIContext.cs` — 新增反馈输入方法声明
- `src/AICA.Core/Agent/IPermissionHandler.cs` — 扩展接口（可选）

**涉及文件**（修改，VS UI 层）:
- `src/AICA.VSIX/Agent/VSUIContext.cs` — 实现反馈输入框 UI

**详细步骤**:

1. **扩展 `IUIContext` 接口**
   ```csharp
   // 新增方法
   Task<string> RequestDenialFeedbackAsync(
       string toolName,
       string operationDescription,
       CancellationToken ct);
   // 返回: 用户输入的反馈文本，null/empty 表示用户跳过
   ```

2. **修改 `PermissionCheckMiddleware.ProcessAsync`**
   ```
   原逻辑: 拒绝 → return ToolResult.Fail("Tool execution denied: {toolName}")

   新逻辑:
   if (!approved) {
       if (AicaConfig.Current.Features.PermissionFeedback) {
           var feedback = await context.UIContext.RequestDenialFeedbackAsync(toolName, description, ct);
           if (!string.IsNullOrEmpty(feedback)) {
               feedback = feedback.Substring(0, Math.Min(feedback.Length, 500)); // S1: 长度限制
               telemetry.LogEvent("permission_denied_with_feedback", metadata: { with_feedback = true });
               return ToolResult.SecurityDenied($"Permission denied. User feedback: {feedback}");
           }
           telemetry.LogEvent("permission_denied_with_feedback", metadata: { with_feedback = false });
       }
       return ToolResult.SecurityDenied($"Tool execution denied: {toolName}");
   }
   ```
   - **关键改动**: `Fail` → `SecurityDenied`（不论有无反馈）
   - **偏差备注**：Unified Plan 原文使用 `ToolResult.Error`，此处改用 `SecurityDenied` 系现有 API 更精确的语义表达，同时修正现有 `Fail` → `SecurityDenied` 的 bug
   - Feature flag 控制是否弹出反馈框
   - 无 flag 或用户跳过时，仍返回 SecurityDenied（修正现有 bug）

3. **VS UI 实现**
   - `RequestDenialFeedbackAsync` 在 VS UI 层实现为简单的文本输入对话框
   - 包含"跳过"按钮（返回 null）
   - 对话框标题: "工具调用被拒绝 - 可选反馈"
   - 提示文本: "您拒绝了 {toolName} 的执行。如有原因可在此说明，AI 将据此调整策略："

4. **Telemetry 埋点**
   - `permission_denied_with_feedback`: bool — 是否附带了反馈

**依赖**: 无

---

### Pane 4：集成层 — 调用方适配

**涉及文件**（修改）:
- `src/AICA.Core/Agent/AgentExecutor.cs` — 传递 query 参数
- `src/AICA.Core/Agent/SystemPromptBuilder.cs` — 适配新格式（可选）

**详细步骤**:

1. **`AgentExecutor.BuildConversationHistory` 适配**
   ```
   原代码 (line 554-559):
     var memoryContent = await MemoryBank.LoadAsync(context.WorkingDirectory, ct);

   新代码:
     // 提取当前用户消息作为 query（供相关性检索）
     var query = ExtractLatestUserMessage(messages);
     var memoryContent = await MemoryBank.LoadAsync(context.WorkingDirectory, query, ct);
   ```
   - 新增私有方法 `ExtractLatestUserMessage`：从消息列表中取最后一条 user message 的文本

2. **`SystemPromptBuilder.AddMemoryContext` 适配**
   - 当前签名 `AddMemoryContext(string memoryContent)` 不变
   - MemoryBank 输出格式从旧的 `### filename\ncontent` 改为带类型标注：
     ```
     ### [project] memory_name
     description: ...
     ---
     body content
     ```
   - SystemPromptBuilder 无需改动，只是接收格式化后的字符串

3. **集成验证清单**
   - [ ] Feature flag `StructuredMemory=false` → 完全回退旧逻辑
   - [ ] Feature flag `PermissionFeedback=false` → 不弹反馈框，但 Fail→SecurityDenied 修正保留
   - [ ] 无记忆文件 → 返回 null，不报错
   - [ ] 旧格式记忆文件 → 自动迁移后正常加载
   - [ ] query 为 null/empty → 全量加载（退化为旧行为）

**依赖**: Pane 1（新 LoadAsync 签名）

---

### Pane 5：代码审核

**审核要点**:
1. OH2 相关性算法正确性（权重计算、中文分词、停用词覆盖）
2. 兼容迁移安全性（备份→迁移→验证，失败不破坏原文件）
3. H3a 安全性（反馈内容是否需要 sanitize、长度限制）
4. Feature flag 回退路径完整性
5. Telemetry 埋点是否覆盖验收标准所需指标
6. 线程安全（MemoryBank 是 static class，MigrateIfNeededAsync 的并发保护）
7. 现有 `Fail` → `SecurityDenied` 修正的影响面

---

## 三、文件冲突矩阵

| 文件 | Pane 1 | Pane 2 | Pane 3 | Pane 4 |
|------|--------|--------|--------|--------|
| MemoryBank.cs | ✏️ 重写 | | | |
| MemoryEntry.cs | ✏️ 新建 | | | |
| RelevanceScorer.cs | ✏️ 新建 | | | |
| ChineseStopwords.cs | | ✏️ 新建 | | |
| MemoryMigrator.cs | | ✏️ 新建 | | |
| PermissionCheckMiddleware.cs | | | ✏️ 修改 | |
| IUIContext.cs | | | ✏️ 修改 | |
| VSUIContext.cs | | | ✏️ 修改 | |
| AgentExecutor.cs | | | | ✏️ 修改 |
| SystemPromptBuilder.cs | | | | ✏️ 可选 |

**冲突**: 无。四个 Pane 操作完全不同的文件。

---

## 四、执行顺序

```
Round 1（并行）:
  Pane 2: ChineseStopwords + MemoryMigrator
  Pane 3: H3a 权限反馈注入（全部）

Round 2（并行，依赖 Pane 2 完成）:
  Pane 1: MemoryBank 重写 + RelevanceScorer（引用 Pane 2 的停用词）
  Pane 3: 继续或已完成

Round 3（依赖 Pane 1 完成）:
  Pane 4: AgentExecutor 集成适配

Round 4:
  Pane 5: 全量审核
```

---

## 五、验收标准（摘自 Unified Plan）

### OH2
- [ ] 记忆按相关性检索注入（非全量拼接）
- [ ] 消除 4000 字符硬截断
- [ ] 节省 ~300-500 tokens/请求
- [ ] Feature flag 可回退全量拼接
- [ ] Telemetry: memories_total / memories_injected / memory_tokens_used

### H3a
- [ ] 拒绝工具调用时可附带反馈
- [ ] 反馈进入 Agent 对话上下文
- [ ] Feature flag 控制反馈框显示
- [ ] Telemetry: permission_denied_with_feedback

---

## 六、Pane 5 审核记录

**审核时间**: 2026-04-08
**审核结论**: PASS WITH CONDITIONS
**Must-fix**: 0 项

### Should-fix（5 项，全部已应用）

| # | 问题 | 处理 |
|---|------|------|
| S1 | H3a 反馈内容无长度限制 | ✅ 已在 Pane 3 步骤 2 增加 `Substring(0, 500)` |
| S2 | VS UI 实现类路径未指定 | ✅ 已补充为 `src/AICA.VSIX/Agent/VSUIContext.cs` |
| S3 | SelectTopN maxTokens 来源和 token 估算未明确 | ✅ 已在 Pane 1 步骤 4 添加：`MaxMemoryTokens = 2000`，估算 `length / 3` |
| S4 | MigrateIfNeededAsync 线程安全方案不具体 | ✅ 已在 Pane 2 步骤 3 改用 `Interlocked.CompareExchange` |
| S5 | SecurityDenied 改进偏差需标注 | ✅ 已在 Pane 3 步骤 2 添加偏差备注 |

### Nice-to-have（2 项，全部已应用）

| # | 问题 | 处理 |
|---|------|------|
| N1 | MemoryMigrator 失败可能留半修改文件 | ✅ 已在 Pane 2 步骤 2 改用"写临时文件 → 原子替换"模式 |
| N2 | Pane 2 标题含"测试"但无测试步骤 | ✅ 已从标题去掉"测试" |
