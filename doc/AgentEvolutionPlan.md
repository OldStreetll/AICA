# AICA Agent 演进计划 — 从工具调用者到真正的 Agent

> 版本: 1.0
> 日期: 2026-03-18
> 状态: 待确认
> 基于: POCO A 类测试发现 + AICA 源码深度分析
> 目标: 将 AICA 从 "单轮工具调用者" 演进为 "自主任务完成者"

---

## 一、现状分析

### 当前架构

```
用户请求 → AgentExecutor (单循环) → LLM → 工具调用 → 返回结果
                ↓
        32K token 上下文
        14 个工具
        单线程串行执行
        每次会话从零开始
```

### 关键文件与规模

| 组件 | 文件 | 行数 | 职责 |
|------|------|------|------|
| AgentExecutor | Agent/AgentExecutor.cs | 1849 | 主循环, condense, 工具调度 |
| SystemPromptBuilder | Prompt/SystemPromptBuilder.cs | 628 | 系统 Prompt 构建 |
| ToolDispatcher | Agent/ToolDispatcher.cs | 133 | 工具路由 |
| ToolRegistry | Agent/ToolRegistry.cs | 173 | 工具注册 |
| OpenAIClient | LLM/OpenAIClient.cs | 80+ | LLM HTTP 通信 |
| 14 个工具 | Tools/*.cs | 3644 | 文件/搜索/命令/交互 |
| VSAgentContext | VSIX/Agent/VSAgentContext.cs | — | VS 工作区上下文 |
| VSUIContext | VSIX/Agent/VSUIContext.cs | — | VS UI 交互 |

### POCO 测试暴露的核心局限

| 局限 | 测试证据 | 根因 |
|------|----------|------|
| 上下文窗口小 (32K) | TC-A14: condense 后信息丢失 | maxTokenBudget = 32000 |
| 单 Agent 串行 | 复杂任务需多轮才能完成 | AgentExecutor 单循环 |
| function calling 不可靠 | TC-A12/A07: tool_calls 为空 | LLM 模型兼容性 |
| 无项目记忆 | 每次会话重新理解项目 | 无持久化知识库 |
| LLM 计数弱 | 5/14 用例数字矛盾 | LLM 固有弱点 |
| 被动响应 | 用户问一步做一步 | 无任务分解能力 |

---

## 二、目标架构

```
用户请求 → TaskPlanner (任务分解)
              ↓
        AgentOrchestrator (编排)
        ├── MainAgent (对话 + 决策)
        ├── ResearchAgent (并行搜索/读文件)
        ├── CodeAgent (代码生成/修改)
        └── ReviewAgent (审查/验证)
              ↓
        ProjectKnowledgeBase (RAG)
        ├── 文件索引 (结构/符号)
        ├── 依赖图 (模块/类/继承)
        └── 会话记忆 (跨会话持久化)
              ↓
        ToolCallFallback (可靠执行)
        ├── function calling (优先)
        ├── 文本解析 fallback
        └── 工具侧计数注入
```

---

## 三、演进路线 (6 个 Phase, 渐进式)

### Phase 0: 快速收益修复 [1-2 天]

> 不改架构, 修复测试中发现的具体问题, 立即提升用户体验

#### 0.1 Tool Call Fallback — 解决 function calling 失败

**问题**: TC-A12/A07 中 LLM 返回无 tool_calls 字段, 工具未执行

**文件**: `src/AICA.Core/Agent/AgentExecutor.cs`
**位置**: 约 line 430 (LLM 响应处理后)

**方案**: 当检测到工具意图但无 tool_calls 时, 从文本中解析工具调用

```csharp
// 新增: ToolCallTextParser.cs
public class ToolCallTextParser
{
    /// <summary>
    /// 当 LLM 返回文本中包含工具调用意图但 tool_calls 为空时,
    /// 尝试从文本中解析工具名称和参数
    /// </summary>
    public static List<ToolCall> TryParseFromText(string text, IEnumerable<string> knownTools)
    {
        // 策略 1: 检测 JSON 格式的工具调用
        // 策略 2: 检测 "我将调用 X 工具" / "让我使用 X" 等模式
        // 策略 3: 检测参数提取 (path: "...", query: "..." 等)
        // 仅在 tool_calls 为空时触发, 不影响正常流程
    }
}
```

**AgentExecutor 集成**:
```csharp
if (!hasToolCalls && !string.IsNullOrWhiteSpace(assistantResponse))
{
    // 现有诊断日志 (Phase 6)
    // 新增: 尝试从文本解析工具调用
    var parsedCalls = ToolCallTextParser.TryParseFromText(
        assistantResponse, _toolDispatcher.GetToolNames());
    if (parsedCalls.Any())
    {
        _logger?.LogInformation("[AICA] Recovered {Count} tool calls from text fallback", parsedCalls.Count);
        toolCalls = parsedCalls;
        hasToolCalls = true;
    }
}
```

**预期**: TC-A12 ask_followup_question 从 PARTIAL PASS → PASS

#### 0.2 工具侧计数注入 — 解决数字一致性

**问题**: P1-009 是 LLM 固有弱点, Prompt 无法完全解决

**方案**: 每个搜索/列表工具在返回结果末尾自动附加精确统计

**文件修改**:

`GrepSearchTool.cs` 返回结果末尾追加:
```
[TOOL_EXACT_STATS: 142 matches in 67 files]
```

`FindByNameTool.cs` 返回结果末尾追加:
```
[TOOL_EXACT_STATS: 44 files found]
```

`ListCodeDefinitionsTool.cs` 返回结果末尾追加:
```
[TOOL_EXACT_STATS: 6 constructors, 20 public methods, 5 private members]
```

**Prompt 补充** (SystemPromptBuilder.cs):
```
When tool output contains [TOOL_EXACT_STATS: ...], you MUST use these exact numbers.
Do NOT count manually — the tool-provided numbers are authoritative.
```

**预期**: P1-009 从 5/14 用例 → ≤ 1/14 用例

#### 0.3 搜索范围自动扩展

**问题**: TC-A08 Phase 5 Prompt 未被遵守, 搜索限于 Foundation

**方案**: 在 GrepSearchTool.cs 中, 当搜索继承模式且结果少时, 自动扩展

```csharp
// GrepSearchTool.cs: 在返回少量结果时建议扩展
if (isInheritancePattern && matchCount < 5 && !string.IsNullOrEmpty(searchPath))
{
    // 自动追加提示
    result += "\n[TOOL_HINT: Only found " + matchCount + " matches in " + searchPath +
        ". Consider searching the entire project or with fully-qualified name (e.g., Poco::Channel)]";
}
```

---

### Phase 1: 项目知识库 [1-2 周]

> 让 AICA "记住" 项目, 不再每次从零开始

#### 1.1 项目索引器 (ProjectIndexer)

**新文件**: `src/AICA.Core/Knowledge/ProjectIndexer.cs`

**功能**: 首次打开解决方案时, 后台扫描建立索引

```csharp
public class ProjectIndexer
{
    /// <summary>
    /// 扫描项目生成结构化索引:
    /// - 文件列表 (路径, 大小, 类型)
    /// - 类/接口/枚举定义 (名称, 文件, 行号, 继承关系)
    /// - 命名空间层次
    /// - 模块依赖图 (基于 #include / using)
    /// - 测试文件与被测文件的映射
    /// </summary>
    public async Task<ProjectIndex> BuildIndexAsync(string solutionPath, CancellationToken ct);

    /// <summary>
    /// 增量更新: 仅重新索引变更文件
    /// </summary>
    public async Task<ProjectIndex> UpdateIndexAsync(ProjectIndex existing, IEnumerable<string> changedFiles);
}
```

**索引存储**: `.aica/index.json` (项目根目录)

```json
{
  "version": "1.0",
  "projectPath": "D:\\project\\poco",
  "indexedAt": "2026-03-18T...",
  "stats": { "files": 4194, "classes": 850, "namespaces": 25 },
  "modules": [
    {
      "name": "Foundation",
      "path": "Foundation/",
      "dependencies": [],
      "classes": 320,
      "testDir": "Foundation/testsuite/"
    }
  ],
  "symbols": [
    {
      "name": "Logger",
      "type": "class",
      "file": "Foundation/include/Poco/Logger.h",
      "line": 38,
      "namespace": "Poco",
      "inherits": ["Channel"],
      "methods": 45
    }
  ]
}
```

#### 1.2 知识库上下文注入 (KnowledgeContextProvider)

**新文件**: `src/AICA.Core/Knowledge/KnowledgeContextProvider.cs`

**功能**: 根据用户请求, 从索引中提取相关上下文注入到系统 Prompt

```csharp
public class KnowledgeContextProvider
{
    /// <summary>
    /// 分析用户请求, 从索引中提取相关信息:
    /// - 提到的类名 → 注入类定义摘要
    /// - 提到的文件名 → 注入文件元信息
    /// - 提到的模块名 → 注入模块概览
    /// 控制注入量: ≤ 2000 tokens
    /// </summary>
    public string GetRelevantContext(string userRequest, ProjectIndex index);
}
```

**集成点**: AgentExecutor.ExecuteAsync 中, 在构建系统 Prompt 时注入

```csharp
var knowledgeContext = _knowledgeProvider.GetRelevantContext(userRequest, _projectIndex);
systemPrompt += "\n\n## Project Knowledge (auto-indexed)\n" + knowledgeContext;
```

**预期效果**:
- 用户问 "Logger 类有什么方法" → 系统已知 Logger 在 Foundation/include/Poco/Logger.h, 继承 Channel, 45 个方法
- 减少不必要的 read_file 调用, 降低 token 消耗
- 项目整体架构理解无需多轮探索

#### 1.3 增量更新与 VS 集成

**文件**: `src/AICA.VSIX/Events/SolutionEventListener.cs`

```csharp
// 解决方案打开时: 加载或构建索引
private async void OnSolutionOpened(object sender, EventArgs e)
{
    var indexPath = Path.Combine(solutionDir, ".aica", "index.json");
    if (File.Exists(indexPath))
        _projectIndex = await ProjectIndex.LoadAsync(indexPath);
    else
        _projectIndex = await _indexer.BuildIndexAsync(solutionPath, ct);
}

// 文件保存时: 增量更新
private async void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
{
    _projectIndex = await _indexer.UpdateIndexAsync(_projectIndex, new[] { e.FilePath });
}
```

---

### Phase 2: 任务规划系统 [1-2 周]

> 让 AICA 能自主分解和执行多步任务

#### 2.1 TaskPlanner

**新文件**: `src/AICA.Core/Agent/TaskPlanner.cs`

```csharp
public class TaskPlanner
{
    /// <summary>
    /// 分析用户请求复杂度, 决定是否需要分解
    /// 简单请求 (读文件, 搜索) → 直接执行
    /// 复杂请求 (重构, 架构分析, bug 修复) → 生成执行计划
    /// </summary>
    public TaskPlan AnalyzeAndPlan(string userRequest, ProjectIndex index);
}

public class TaskPlan
{
    public TaskComplexity Complexity { get; set; } // Simple, Medium, Complex
    public List<TaskStep> Steps { get; set; }
    public List<string> RequiredTools { get; set; }
    public bool RequiresUserConfirmation { get; set; }
}

public class TaskStep
{
    public int Order { get; set; }
    public string Description { get; set; }
    public string Tool { get; set; }       // 预期使用的工具
    public string[] Dependencies { get; set; }  // 前置步骤
    public TaskStepStatus Status { get; set; }
}
```

#### 2.2 执行计划 UI

**文件**: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml`

在聊天窗口中显示可折叠的执行计划:

```
📋 执行计划 (3 步)
  ✅ 1. 读取 Logger.h 了解类结构
  🔄 2. 搜索所有 Channel 子类
  ⬜ 3. 生成架构分析报告
```

用户可以:
- 查看每步详情
- 跳过某步
- 修改计划
- 取消执行

#### 2.3 自主重试与策略调整

**增强 AgentExecutor**: 当工具调用失败时, 不是简单重试, 而是调整策略

```csharp
// 现有: 失败 → 重试同一调用 (或放弃)
// 新增: 失败 → 分析原因 → 选择替代策略

private async Task<ToolResult> ExecuteWithRecovery(ToolCall call, IAgentContext context)
{
    var result = await _toolDispatcher.ExecuteAsync(call, context);
    if (!result.Success)
    {
        var recovery = _recoveryStrategies.GetStrategy(call.Name, result.Error);
        // 例如: grep_search 在 Foundation 无结果 → 自动扩展到全项目
        // 例如: read_file 文件不存在 → find_by_name 搜索类似文件
        // 例如: edit 匹配失败 → read_file 重新获取内容
        if (recovery != null)
            return await recovery.ExecuteAsync(call, context);
    }
    return result;
}
```

---

### Phase 3: 多 Agent 协作 [2-3 周]

> 突破单 Agent 串行瓶颈

#### 3.1 AgentOrchestrator

**新文件**: `src/AICA.Core/Agent/AgentOrchestrator.cs`

```csharp
public class AgentOrchestrator
{
    private readonly ILLMClient _llmClient;
    private readonly ToolDispatcher _toolDispatcher;

    /// <summary>
    /// 编排多个子 Agent 协同完成任务
    /// </summary>
    public async IAsyncEnumerable<AgentStep> OrchestrateAsync(
        TaskPlan plan,
        IAgentContext context,
        IUIContext uiContext,
        CancellationToken ct)
    {
        // 1. 识别可并行的步骤
        var parallelGroups = plan.GetParallelGroups();

        foreach (var group in parallelGroups)
        {
            if (group.Steps.Count == 1)
            {
                // 串行执行
                await foreach (var step in ExecuteStepAsync(group.Steps[0], context, uiContext, ct))
                    yield return step;
            }
            else
            {
                // 并行执行
                var tasks = group.Steps.Select(s =>
                    ExecuteStepCollectAsync(s, context, ct));
                var results = await Task.WhenAll(tasks);

                // 汇总结果
                yield return AgentStep.TextChunk(MergeResults(results));
            }
        }
    }
}
```

#### 3.2 子 Agent 类型

```csharp
public enum SubAgentType
{
    Research,   // 搜索 + 读文件, 只读操作
    Code,       // 代码生成 + 编辑, 需要确认
    Review,     // 代码审查 + 测试验证
    Build       // 编译 + 运行测试
}
```

**ResearchAgent**: 只使用 read_file/grep_search/find_by_name/list_dir, 不需要用户确认
**CodeAgent**: 使用 edit/write_to_file, 需要 diff 确认
**ReviewAgent**: 分析代码质量, 生成报告
**BuildAgent**: 执行 cmake/msbuild, 报告结果

#### 3.3 共享上下文

子 Agent 之间通过 `SharedContext` 共享发现:

```csharp
public class SharedContext
{
    // 线程安全的共享状态
    private readonly ConcurrentDictionary<string, object> _findings;

    public void AddFinding(string key, object value);
    public T GetFinding<T>(string key);
    public string GetSummary(); // 汇总所有发现
}
```

**使用场景**:
```
用户: "分析 POCO 的日志系统架构"

Orchestrator 创建计划:
├── ResearchAgent 1: 读取 Logger.h, Channel.h → 发现类层次
├── ResearchAgent 2: grep_search "Channel" → 发现所有子类
├── ResearchAgent 3: 读取 CppUnit 测试 → 发现测试覆盖
│
└── MainAgent: 汇总三个 Research 结果, 生成架构分析

(并行执行, 耗时从 ~30s 降至 ~10s)
```

---

### Phase 4: 上下文窗口突破 [2-3 周]

> 从 32K 限制到 "无限" 有效上下文

#### 4.1 分层上下文架构

```
┌─────────────────────────────────────┐
│ 热上下文 (Hot Context)              │ ≤ 16K tokens
│ 当前任务的直接相关内容               │
│ - 系统 Prompt + 工具定义             │
│ - 最近 2-3 轮对话                    │
│ - 当前步骤的工具输出                 │
└─────────────────────────────────────┘
┌─────────────────────────────────────┐
│ 温上下文 (Warm Context)             │ ≤ 8K tokens
│ 本会话摘要                           │
│ - condense 摘要 (工具历史)           │
│ - 关键发现列表                       │
│ - 用户偏好和纠正                     │
└─────────────────────────────────────┘
┌─────────────────────────────────────┐
│ 冷上下文 (Cold Context)             │ 无限 (向量索引)
│ 项目知识库 + 历史会话                │
│ - ProjectIndex 符号索引              │
│ - 历史会话的关键发现                 │
│ - 文件内容缓存                       │
└─────────────────────────────────────┘
```

#### 4.2 智能上下文选择

**新文件**: `src/AICA.Core/Context/SmartContextSelector.cs`

```csharp
public class SmartContextSelector
{
    /// <summary>
    /// 根据当前请求, 从三层上下文中选择最相关的内容
    /// 总量控制在 maxTokenBudget 内
    /// </summary>
    public ContextSelection SelectContext(
        string currentRequest,
        HotContext hot,
        WarmContext warm,
        ProjectIndex cold,
        int maxTokenBudget)
    {
        // 1. 热上下文始终包含 (优先级最高)
        // 2. 从冷上下文中检索与请求相关的符号/文件
        // 3. 温上下文中选择与当前请求相关的历史发现
        // 4. 按相关性排序, 控制总量
    }
}
```

#### 4.3 向量索引 (可选, 高级)

如果项目极大 (>10000 文件), 可引入轻量向量索引:

**依赖**: 无外部服务, 使用本地 ONNX 模型生成嵌入

```csharp
public class LocalEmbeddingIndex
{
    // 使用 ONNX Runtime 运行小型嵌入模型 (all-MiniLM-L6-v2, ~80MB)
    // 为每个文件/类/方法生成 384 维嵌入向量
    // 存储在 .aica/embeddings.bin (本地文件)
    // 查询时通过余弦相似度检索 top-K 相关内容
}
```

---

### Phase 5: 跨会话记忆 [1 周]

> 让 AICA 记住用户偏好和项目理解

#### 5.1 记忆类型

```csharp
public enum MemoryType
{
    UserPreference,   // 用户偏好: "不要用 GTest, 用 CppUnit"
    ProjectFact,      // 项目事实: "POCO 使用 CppUnit 测试框架"
    Correction,       // 纠正记录: "Channel 不是单例模式"
    WorkHistory       // 工作历史: "上次重构了 Logger 的线程安全"
}
```

#### 5.2 记忆存储

**文件**: `.aica/memory.json` (项目级)

```json
{
  "memories": [
    {
      "type": "ProjectFact",
      "content": "POCO uses CppUnit (self-contained in CppUnit/ directory), not GTest or Catch2",
      "confidence": 0.95,
      "source": "TC-E04 test session",
      "createdAt": "2026-03-18"
    },
    {
      "type": "UserPreference",
      "content": "User prefers Chinese responses with technical terms in English",
      "confidence": 0.9,
      "source": "conversation pattern"
    }
  ]
}
```

#### 5.3 记忆注入

在 SystemPromptBuilder 中注入相关记忆:

```csharp
var memories = _memoryStore.GetRelevant(userRequest, maxTokens: 500);
if (memories.Any())
{
    systemPrompt += "\n\n## Project Memory (persistent)\n";
    foreach (var m in memories)
        systemPrompt += $"- [{m.Type}] {m.Content}\n";
}
```

---

### Phase 6: 高级 Agent 行为 [2-3 周]

> 让 AICA 表现得像真正的 AI 编程助手

#### 6.1 主动建议

当 AICA 在执行任务中发现问题时, 主动提醒:

```csharp
public class ProactiveAdvisor
{
    // 场景 1: 编辑文件时发现潜在 bug
    // "我注意到这个方法没有 null 检查, 建议添加"

    // 场景 2: 代码审查时发现安全问题
    // "这里有潜在的缓冲区溢出风险"

    // 场景 3: 发现测试覆盖不足
    // "这个新方法还没有对应的测试用例"
}
```

#### 6.2 学习与自我改进

记录每次会话的工具使用模式, 优化未来行为:

```csharp
public class UsagePatternTracker
{
    // 统计: 哪些工具组合最常用
    // 统计: 哪些搜索模式最有效
    // 统计: 用户最常修改哪些文件
    // 用于: 预测用户意图, 预加载相关上下文
}
```

#### 6.3 对话质量自评

在 attempt_completion 之前, 自动检查:

```csharp
public class ResponseValidator
{
    public ValidationResult Validate(string response, List<ToolResult> toolOutputs)
    {
        // 检查 1: 数字一致性 (从工具输出提取数字, 与回答对比)
        // 检查 2: 文件路径存在性 (提到的文件是否在索引中)
        // 检查 3: 类名正确性 (提到的类是否在索引中)
        // 检查 4: 无虚构检测 (声称的事实是否有工具输出支持)
    }
}
```

---

## 四、技术栈选择

| 组件 | 技术 | 理由 |
|------|------|------|
| 项目索引 | System.Text.Json + 自定义解析 | 无外部依赖, .NET Standard 2.0 兼容 |
| 符号提取 | 正则 + Tree-sitter (可选) | 正则覆盖 80% 场景, Tree-sitter 更精确 |
| 向量索引 | ONNX Runtime (可选) | 本地运行, 无需外部服务 |
| 并行执行 | Task.WhenAll + CancellationToken | .NET 原生 |
| 记忆存储 | JSON 文件 (.aica/ 目录) | 简单, 版本可控 |
| UI 更新 | 现有 VSUIContext 扩展 | 复用现有基础设施 |

---

## 五、实施路线图

```
Phase 0 (1-2 天)          Phase 1 (1-2 周)        Phase 2 (1-2 周)
┌────────────────┐     ┌──────────────────┐    ┌──────────────────┐
│ Tool Fallback  │     │ ProjectIndexer   │    │ TaskPlanner      │
│ 计数注入       │────→│ KnowledgeContext │───→│ 执行计划 UI      │
│ 搜索范围扩展   │     │ VS 集成          │    │ 自主重试策略     │
└────────────────┘     └──────────────────┘    └──────────────────┘
                                                       │
Phase 5 (1 周)           Phase 4 (2-3 周)       Phase 3 (2-3 周)
┌────────────────┐     ┌──────────────────┐    ┌──────────────────┐
│ 跨会话记忆     │←────│ 分层上下文       │←───│ AgentOrchestrator│
│ 记忆注入       │     │ 智能选择         │    │ 子 Agent 类型    │
│ 纠正学习       │     │ 向量索引 (可选)  │    │ 共享上下文       │
└────────────────┘     └──────────────────┘    └──────────────────┘
                                │
                       Phase 6 (2-3 周)
                       ┌──────────────────┐
                       │ 主动建议         │
                       │ 使用模式学习     │
                       │ 响应自动验证     │
                       └──────────────────┘
```

**总计**: 约 8-14 周 (2-3.5 个月)

---

## 六、每 Phase 交付物与验收标准

| Phase | 交付物 | 验收标准 |
|-------|--------|----------|
| **0** | ToolCallTextParser, 工具计数注入, 搜索扩展 | TC-A12 PASS; P1-009 ≤ 1/14; TC-A08 跨模块 95% |
| **1** | ProjectIndexer, KnowledgeContextProvider, .aica/index.json | 打开 POCO 项目后 10s 内索引完成; "Logger 是什么" 无需 read_file 即可回答基本信息 |
| **2** | TaskPlanner, 执行计划 UI, RecoveryStrategies | "分析日志系统架构" 自动拆解为 3+ 步; 工具失败时自动切换策略 |
| **3** | AgentOrchestrator, ResearchAgent, SharedContext | "分析 5 个模块" 并行执行, 耗时降低 50%+ |
| **4** | SmartContextSelector, 分层上下文, 向量索引 | 50 轮对话后仍能准确回忆第 1 轮的发现; condense 零信息丢失 |
| **5** | MemoryStore, 记忆注入, 纠正学习 | 新会话自动知道 "POCO 用 CppUnit"; 用户纠正一次后不再犯同样错误 |
| **6** | ProactiveAdvisor, UsagePatternTracker, ResponseValidator | 编辑代码时主动发现 bug; 数字一致性自动校验 |

---

## 七、风险与缓解

| 风险 | 级别 | 缓解措施 |
|------|------|----------|
| 项目索引耗时过长 (大项目) | 中 | 后台异步 + 增量更新; 首次仅索引 .h/.cs 文件 |
| 多 Agent 并行的 token 成本 | 高 | 子 Agent 使用更小的上下文; Research Agent 只携带必要 Prompt |
| 向量索引模型体积 (~80MB) | 低 | 作为可选功能; 默认使用关键词匹配 |
| .NET Standard 2.0 限制 | 中 | 避免使用 .NET 6+ API; 必要时 polyfill |
| LLM 模型差异 | 高 | Tool Fallback 作为安全网; 测试多种模型兼容性 |
| VS 扩展性能 | 中 | 索引和搜索异步执行; UI 线程不阻塞 |

---

## 八、与现有测试体系的关系

每个 Phase 完成后, 使用 POCO 测试方案 (ManualTestPlan_Poco.md) 中的相关用例验证:

| Phase | 相关测试类别 |
|-------|-------------|
| Phase 0 | A 类重测 (A06/A08/A12) |
| Phase 1 | B 类 (代码理解), C 类 (分析完整性) |
| Phase 2 | I 类 (真实开发者工作流) |
| Phase 3 | G 类 (性能), I 类 (复杂任务) |
| Phase 4 | D 类 (多轮对话), G03 (长对话稳定性) |
| Phase 5 | D 类 (纠错能力), 跨会话测试 |
| Phase 6 | F 类 (幻觉检测), H 类 (响应质量) |

---

**等待确认**: 是否按此计划执行? 可以调整优先级、跳过某些 Phase、或修改技术方案。
