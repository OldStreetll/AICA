# AICA v2.5 优化方案

> 日期: 2026-04-01
> 方案制定: Agent Team（planner agent）
> 基线版本: v2.4（edit 增强、LSP 验证、权限增强、错误分类、稳定性修复已完成）
> 状态: **✅ COMPLETED**（全部 4 项实施完成 + E2E 验证通过，2026-04-01）

**执行顺序：**

```
优化项1 按意图过滤工具集 ──→ 优化项3 工具输出裁剪标记 ──→ 优化项4 Token 精确计量 ──→ 优化项2 Prompt 缓存
      [中风险/高收益]              [低风险/低收益]              [低风险/中收益]             [低风险/收益待验证]
      ~120 行改动                  ~15 行改动                   ~60 行改动                  ~80 行改动
```

---

## 优化项 1: P2 按意图过滤工具集（核心优化）

### 1.0 背景与动机

当前 `DynamicToolSelector.SelectTools()` 仅对 `conversation` 意图做过滤（只保留 `ask_followup_question`），其余所有意图均发送全部 16+ 个工具定义。每次 API 调用中工具定义占 ~8K tokens（实测 `AgentExecutor.cs:125-128` 的 `toolDefinitionTokens` 计算）。

**问题量化：**
- 16 个工具 × ~500 tokens/工具 ≈ 8000 tokens/请求
- 一次典型任务 10 轮迭代 → 80K tokens 仅用于重复发送工具定义
- 占 MiniMax-M2.5 177K context 的 ~4.5%/请求，累计占比显著

**OpenCode 对比：** OpenCode 不做工具过滤，依赖 Claude/GPT-4 的大 context window。但 AICA 使用 MiniMax-M2.5（177K context、较低的工具选择精度），工具集精简能同时节省 token 和提升选择准确率。

### 1.1 架构设计

**核心思路：** 将工具分为 4 个语义分组，根据意图 + 复杂度组合选择工具子集。

**工具分组定义：**

| 分组 | 工具 | 说明 |
|------|------|------|
| **Core（必选）** | `read_file`, `ask_followup_question`, `validate_file` | 所有任务都需要（validate_file 是只读操作） |
| **Edit** | `edit`, `write_file` | 修改/创建任务 |
| **Search** | `grep_search`, `glob`, `list_dir`, `list_code_definition_names`, `list_projects` | 搜索/分析/定位 |
| **Advanced** | `run_command`, `gitnexus_*`（动态数量） | 构建/命令/代码图谱 |

**意图 → 工具集映射：**

| 意图 | 复杂度 Simple | 复杂度 Complex |
|------|--------------|----------------|
| `conversation` | `ask_followup_question` | `ask_followup_question` |
| `read` | Core + Search | Core + Search + Advanced |
| `analyze` | Core + Search | Core + Search + Advanced |
| `modify` | Core + Edit + Search | ALL |
| `bug_fix` | Core + Edit + Search | ALL |
| `command` | Core + Advanced | Core + Search + Advanced |

**估算收益：**
- `read` 意图（Simple）: 16 → 8 工具，节省 ~4K tokens/请求
- `modify` 意图（Simple）: 16 → 11 工具，节省 ~2.5K tokens/请求
- `conversation` 意图: 已有优化（16 → 1），不变

### 1.2 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `Agent/DynamicToolSelector.cs` | 新增 `ToolGroup` 枚举 + `ToolGroupMap` + `ReadKeywords` + `GetGroupsForIntent()` + `IsToolInGroups()`，修改 `SelectTools()` 和 `ClassifyIntent()`。default fallback 改为 `"general"` → `ToolGroup.All` | +90, ~15 改动 |
| `Agent/AgentExecutor.cs` | 无改动（接口兼容） | 0 |
| `Agent/ToolDispatcher.cs` | 无改动（已有 alias fallback + ToolResult.Fail graceful 兜底） | 0 |

### 1.3 伪代码

```csharp
// DynamicToolSelector.cs

[Flags]
public enum ToolGroup
{
    Core     = 1,
    Edit     = 2,
    Search   = 4,
    Advanced = 8,
    All      = Core | Edit | Search | Advanced
}

private static readonly Dictionary<string, ToolGroup> ToolGroupMap =
    new Dictionary<string, ToolGroup>(StringComparer.OrdinalIgnoreCase)
    {
        ["read_file"] = ToolGroup.Core,
        ["ask_followup_question"] = ToolGroup.Core,
        ["edit"] = ToolGroup.Edit,
        ["write_file"] = ToolGroup.Edit,
        ["validate_file"] = ToolGroup.Core,  // 只读操作，所有场景可用
        ["grep_search"] = ToolGroup.Search,
        ["glob"] = ToolGroup.Search,
        ["list_dir"] = ToolGroup.Search,
        ["list_code_definition_names"] = ToolGroup.Search,
        ["list_projects"] = ToolGroup.Search,
        ["run_command"] = ToolGroup.Advanced,
        // gitnexus_* tools: 默认 Advanced（动态注册的 MCP 工具归入 Advanced）
    };

public static IReadOnlyList<ToolDefinition> SelectTools(
    string userRequest, TaskComplexity complexity,
    IReadOnlyList<ToolDefinition> allTools, bool gitNexusAvailable)
{
    var intent = ClassifyIntent(userRequest);
    if (intent == "conversation")
        return allTools.Where(t => t.Name == "ask_followup_question").ToList();

    var groups = GetGroupsForIntent(intent, complexity);
    return allTools.Where(t => IsToolInGroups(t.Name, groups)).ToList();
}

private static ToolGroup GetGroupsForIntent(string intent, TaskComplexity complexity)
{
    bool isComplex = complexity == TaskComplexity.Complex;
    switch (intent)
    {
        case "read":
        case "analyze":
            return isComplex
                ? ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced
                : ToolGroup.Core | ToolGroup.Search;
        case "modify":
        case "bug_fix":
            return isComplex
                ? ToolGroup.All
                : ToolGroup.Core | ToolGroup.Edit | ToolGroup.Search;
        case "command":
            return isComplex
                ? ToolGroup.Core | ToolGroup.Search | ToolGroup.Advanced
                : ToolGroup.Core | ToolGroup.Advanced;
        case "general": // 未分类请求不过滤（评审修复：从 read 改为 general）
        default:
            return ToolGroup.All;
    }
}

private static bool IsToolInGroups(string toolName, ToolGroup groups)
{
    if (ToolGroupMap.TryGetValue(toolName, out var group))
        return (groups & group) != 0;
    // 未在映射中的工具（MCP 动态注册）归入 Advanced
    return (groups & ToolGroup.Advanced) != 0;
}
```

**Graceful fallback（ToolDispatcher 侧）：**

当 LLM 请求了未在当前子集中注册的工具时，`ToolDispatcher.ResolveTool()` 已返回 null → `ToolResult.Fail("Unknown tool: xxx")`。此行为天然提供了 graceful fallback：LLM 收到错误后会尝试子集内的替代工具。无需额外改动。

### 1.4 验证策略

- **单元测试**: 扩展 `DynamicToolSelectorTests.cs`，覆盖每种意图×复杂度组合，验证返回工具列表正确
- **Token 对比测试**: 修改前后各运行 5 个典型场景（read/modify/bug_fix），记录 `toolDefinitionTokens` 差异
- **回归测试**: E2E 运行 "读取文件" + "修改文件" + "搜索代码" 场景，确认未缺失必要工具

### 1.5 回滚策略

- `DynamicToolSelector.SelectTools()` 恢复为 `return allTools;`（一行改动）
- `ToolDispatcher.ResolveTool()` 新增的日志行可保留（无害）

### 1.6 风险评估

| 风险 | 级别 | 缓解 |
|------|------|------|
| 意图分类错误导致缺失关键工具 | 中 | 未知意图返回 ALL；`ToolDispatcher` alias fallback 兜底 |
| MCP 工具（gitnexus_*）被意外过滤 | 低 | 未映射工具默认归入 Advanced，Complex 任务始终包含 Advanced |
| LLM 在子集内找不到预期工具、反复重试 | 低 | `ToolResult.Fail("Unknown tool")` 已有效引导 LLM 切换 |

### 1.7 真实价值评估

**高价值。** 这是 4 项优化中 ROI 最高的。原因：
1. 每次 API 请求节省 2-4K tokens，累积效果显著（10 轮 = 20-40K tokens）
2. 更小的工具集降低 MiniMax-M2.5 的工具选择混淆概率
3. 实现简单（核心逻辑 ~80 行），回滚成本低

---

## 优化项 2: Prompt 缓存

### 2.0 背景与动机

`SystemPromptBuilder.Build()` 每次请求生成完整的 system prompt。其中约 70% 是静态内容（角色定义、规则、工具使用规范、workspace 信息），30% 是动态内容（memory、resume context、plan context、knowledge context、rules from files）。

**OpenAI API 的 Prompt Caching 机制：** OpenAI 及兼容 API（如 DeepSeek）支持 prefix caching — 只要请求的 system prompt 前 N 个 token 与上一次请求完全相同（字节级一致），API 服务端会自动缓存并跳过这部分的 encoding/computation，显著降低首 token 延迟和计费。

**问题：** 当前 `SystemPromptBuilder` 的构建顺序会导致动态内容（memory、resume）插在中间，破坏前缀一致性。

### 2.1 MiniMax 兼容性评估

**关键发现：MiniMax API 是否支持 prefix prompt caching 目前未知。**

MiniMax 的 OpenAI 兼容 API 并未在其公开文档中明确提及 prompt caching 特性。OpenAI 的 prompt caching 是在 2024 年引入的服务端优化，MiniMax 未必实现了这一特性。

**结论：**
- 如果 AICA 未来切换到 OpenAI/DeepSeek/Anthropic 等支持 prompt caching 的 provider，此优化会自动生效
- 对当前 MiniMax 后端，**收益可能为零**
- 但代码改动本身有次要收益：prompt 组装逻辑更清晰、便于调试

### 2.2 架构设计

**目标：** 将 system prompt 的构建拆分为 static header + dynamic tail，确保 static header 在同一会话的多次请求间保持字节级一致。

**Static Header（不变部分，按顺序）：**
1. Base role prompt（"You are AICA..."）
2. Rules（"## Rules"...）
3. Workspace context（"## Workspace"...）
4. Tool usage guidelines
5. Project structure（solution structure）
6. Bug fix guidance / Qt template guidance（按意图条件附加）

**Dynamic Tail（每次可能变化）：**
1. Memory context
2. Resume context
3. MCP resource context
4. Knowledge context
5. Rules from files
6. Custom instructions

### 2.3 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `Prompt/SystemPromptBuilder.cs` | 新增 `BuildStatic()` + `BuildDynamic()` 方法，`Build()` 改为 `BuildStatic() + "\n\n" + BuildDynamic()`。需要将 `_builder` 拆分为 `_staticBuilder` + `_dynamicBuilder`，并调整各 `Add*` 方法写入正确的 builder | +40, ~30 改动 |
| `Agent/AgentExecutor.cs:419-512` | `BuildConversationHistory()` 无需改动 — 调用顺序决定了 `Add*` 的顺序，只需确保 static 部分在前 | ~5 注释 |

### 2.4 伪代码

```csharp
// SystemPromptBuilder.cs

private readonly StringBuilder _staticBuilder = new StringBuilder();
private readonly StringBuilder _dynamicBuilder = new StringBuilder();

private void AddBasePrompt()
{
    _staticBuilder.AppendLine("You are AICA...");
    // ...
}

public SystemPromptBuilder AddRules()
{
    _staticBuilder.AppendLine("## Rules");
    // ... static rules
    return this;
}

public SystemPromptBuilder AddWorkspaceContext(...)
{
    _staticBuilder.AppendLine("## Workspace");
    // ... static workspace info
    return this;
}

// Dynamic methods write to _dynamicBuilder
public SystemPromptBuilder AddMemoryContext(string memoryContent)
{
    _dynamicBuilder.AppendLine("## 项目记忆（跨会话）");
    _dynamicBuilder.AppendLine(memoryContent);
    return this;
}

public string Build()
{
    var result = _staticBuilder.ToString();
    var dynamic = _dynamicBuilder.ToString();
    if (!string.IsNullOrEmpty(dynamic))
        result += "\n" + dynamic;
    return result;
}
```

### 2.5 验证策略

- **单元测试**: 验证同一 session 内多次调用 `Build()` 时，static 前缀部分字节级一致
- **Token 对比**: 记录 static / dynamic 各自的 token 数，确认 static 占 70%+
- **回归测试**: 对比改动前后 system prompt 内容（应完全一致，只是内部构建方式不同）

### 2.6 回滚策略

- 合并 `_staticBuilder` 和 `_dynamicBuilder` 回 `_builder`（恢复原始代码）

### 2.7 真实价值评估

**低价值（对当前 MiniMax 后端）。** 原因：
1. MiniMax 未确认支持 prompt prefix caching
2. 即使支持，同会话内 system prompt 已相同（只在首轮不同），节省有限
3. 跨会话无状态，无法利用缓存

**潜在价值（未来）：** 如果 AICA 切换到 OpenAI/DeepSeek/Anthropic，此优化立即生效，节省 ~70% system prompt 的 encoding 成本。

**建议：优先级最低，作为代码质量改进在其他优化项完成后实施。**

---

## 优化项 3: 工具输出裁剪时间戳标记

### 3.0 背景与动机

`AgentExecutor.PruneOldToolOutputs()` (line 843-896) 在主循环结束后裁剪旧工具输出。被裁剪的消息内容替换为：

```csharp
history[idx] = ChatMessage.ToolResult(history[idx].ToolCallId, "[output pruned for context space]");
```

**问题：**
- 裁剪标记不含时间信息，LLM 无法判断结果的新旧程度
- 裁剪标记不含原始大小信息，LLM 无法评估丢失了多少上下文
- 在跨轮（下一次用户请求复用 history）场景下，LLM 可能尝试重新调用已裁剪的工具

**注意：** `PruneOldToolOutputs` 在主循环 **之后** 运行（line 394），LLM 在当前会话的活跃工作中**不会看到**裁剪结果。裁剪仅影响 `previousMessages` 被传入下一次 `ExecuteAsync` 的场景。

### 3.1 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `Agent/AgentExecutor.cs:889-891` | 替换裁剪消息模板，加入时间戳和原始 token 估算 | ~8 行改动 |

### 3.2 伪代码

```csharp
// AgentExecutor.cs, PruneOldToolOutputs method, line ~889

foreach (var idx in toPrune)
{
    int originalTokens = ContextManager.EstimateTokens(history[idx].Content);
    var timestamp = DateTime.Now.ToString("HH:mm");
    history[idx] = ChatMessage.ToolResult(
        history[idx].ToolCallId,
        $"[compacted at {timestamp}, original ~{originalTokens} tokens — re-read the file if you need this content]");
}
```

### 3.3 验证策略

- **单元测试**: 验证裁剪后消息包含 `compacted at` 前缀和 token 估算
- **手动验证**: 运行长会话，确认 Debug output 中的 pruning 信息包含时间戳

### 3.4 回滚策略

- 还原为 `"[output pruned for context space]"`（一行改动）

### 3.5 真实价值评估

**低价值。** 原因：
1. `PruneOldToolOutputs` 仅在主循环**结束后**运行，LLM 在活跃工作中不受影响
2. 跨轮传递 `previousMessages` 时 LLM 已有 condense summary，很少依赖旧工具输出
3. 但改动极小（~8 行），不引入任何风险，作为防御性改进值得实施

---

## 优化项 4: Token 精确计量

### 4.0 背景与动机

当前 `ContextManager.EstimateTokens()` (line 293-316) 使用字符级统计：

```csharp
tokens = ceil(cjkCount * 1.5 + codeSymbolCount * 0.5 + otherCount * 0.25)
```

**v2.2 已从 `content.Length / 4` 改进到混合公式**，但仍存在精度问题：
- CJK 1.5 tokens/char 是经验值，实际因模型 tokenizer 而异（MiniMax 的 tokenizer 未公开）
- 代码中混合中文注释 + 英文关键字时，边界 tokenization 误差累积
- 估算误差影响 condense 触发时机（`AgentExecutor.cs:158-159` 的 `estimatedTokens >= condenseThreshold`）

**API Usage 字段分析：**
- `OpenAIClient.StreamChunk` (line 646-649) **不包含 `usage` 字段** — 流式响应中无法获取 token 计数
- `OpenAIClient.ChatCompletionResponse` (line 604-608) **包含 `Usage`** — 非流式响应可获取精确 token 数
- `LLMChunk.Done(UsageInfo)` (line 39) 已预留 `Usage` 字段，但流式场景从未填充

**MiniMax 流式 usage 支持：**
OpenAI 在 2024 年为流式响应添加了 `stream_options: {"include_usage": true}` 参数，流式最后一个 chunk 会包含完整 usage。MiniMax 是否支持此参数未知。

### 4.1 架构设计

**两层策略：**

1. **Layer A: 流式 usage 捕获（尝试性）**
   - 在 `OpenAIClient.BuildRequest()` 中添加 `stream_options: {"include_usage": true}`
   - 在 `ProcessStreamResponseAsync` 中解析最后一个 chunk 的 `usage` 字段
   - 如果 MiniMax 不支持，此字段会被忽略（不影响功能）

2. **Layer B: 校准估算因子（持久价值）**
   - 当 Layer A 获取到真实 usage 时，计算 `calibrationFactor = actual / estimated`
   - 用滑动平均维护校准因子，修正后续 `EstimateTokens()` 的结果
   - 当 Layer A 不可用时（MiniMax 不返回 usage），使用改进的静态公式

### 4.2 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `LLM/OpenAIClient.cs:361-374` | `BuildRequest()` 添加 `StreamOptions` 字段 | +5 |
| `LLM/OpenAIClient.cs:646-649` | `StreamChunk` 类添加 `Usage` 属性 | +3 |
| `LLM/OpenAIClient.cs:212-294` | `ProcessStreamResponseAsync` 中解析 usage，通过 `LLMChunk.Done(usage)` 传出 | +10 |
| `Context/ContextManager.cs:293-316` | `EstimateTokens()` 添加校准因子支持 | +20 |
| `Agent/AgentExecutor.cs:158-176` | condense 触发前，如有 usage 数据则用真实值替代估算 | +15 |
| `LLM/ILLMClient.cs` | `LLMChunk` 已有 `Usage` 字段，无需改动 | 0 |

### 4.3 伪代码

**Layer A: 流式 usage 捕获**

```csharp
// OpenAIClient.cs — BuildRequest()
if (_options.Stream)
{
    request.StreamOptions = new { include_usage = true };
}

// OpenAIClient.cs — StreamChunk
private class StreamChunk
{
    public List<StreamChoice> Choices { get; set; }
    public ResponseUsage Usage { get; set; }  // 新增
}

// OpenAIClient.cs — ProcessStreamResponseAsync, [DONE] 之前
if (chunk.Usage != null)
{
    lastUsage = new UsageInfo
    {
        PromptTokens = chunk.Usage.PromptTokens,
        CompletionTokens = chunk.Usage.CompletionTokens
    };
}
// 在 yield return LLMChunk.Done() 时传入 lastUsage
```

**Layer B: 校准估算因子**

```csharp
// ContextManager.cs

private static double _calibrationFactor = 1.0;
private static int _calibrationSamples = 0;
private const int MaxCalibrationSamples = 20;

public static void CalibrateFromUsage(string text, int actualTokens)
{
    int estimated = EstimateTokensRaw(text);
    if (estimated <= 0 || actualTokens <= 0) return;

    double ratio = (double)actualTokens / estimated;
    // Exponential moving average
    if (_calibrationSamples == 0)
        _calibrationFactor = ratio;
    else
        _calibrationFactor = _calibrationFactor * 0.8 + ratio * 0.2;

    _calibrationSamples = Math.Min(_calibrationSamples + 1, MaxCalibrationSamples);
}

public static int EstimateTokens(string text)
{
    int raw = EstimateTokensRaw(text);
    return (int)Math.Ceiling(raw * _calibrationFactor);
}

// EstimateTokensRaw = 当前的 EstimateTokens 逻辑
```

**AgentExecutor 侧集成：**

```csharp
// AgentExecutor.cs — StreamLLMWithRetry, 在获取 streamResult 后
if (streamResult.Usage != null)
{
    // 使用真实 prompt_tokens 校准估算
    var estimatedPrompt = history.Sum(m => ContextManager.EstimateTokens(m.Content));
    ContextManager.CalibrateFromUsage(estimatedPrompt, streamResult.Usage.PromptTokens);
}
```

### 4.4 验证策略

- **单元测试**: `EstimateTokens` 校准前后精度对比（用已知 token 数的文本样本）
- **集成测试**: 验证 `stream_options` 不会导致 MiniMax API 报错（graceful 降级）
- **观测**: 在 Debug output 中记录 `estimated vs actual` 的偏差百分比，验证校准效果

### 4.5 回滚策略

- 移除 `stream_options` 字段（API 兼容性回滚）
- `_calibrationFactor` 默认 1.0，移除校准逻辑后行为等价于 v2.4

### 4.6 风险评估

| 风险 | 级别 | 缓解 |
|------|------|------|
| `stream_options` 导致 MiniMax API 400 错误 | 低 | 如果 MiniMax 返回错误，在下一版移除此字段。不影响流式功能（字段被忽略是最可能场景） |
| 校准因子震荡导致 condense 触发不稳定 | 低 | EMA 平滑 + 最小样本数限制 |
| 非流式 usage（已存在）与流式 usage 不一致 | 极低 | 使用同一 `UsageInfo` 结构 |

### 4.7 真实价值评估

**中等价值。** 原因：
1. Layer A（流式 usage）对 MiniMax 可能不生效，但不浪费成本（一次性尝试）
2. Layer B（校准）即使无真实 usage，也可通过非流式回退获取少量校准数据
3. 更精确的 token 估算能改善 condense 触发时机，间接提升长会话质量
4. v2.2 已将估算从 `length/4` 改进到混合公式，当前精度约 ±20%，进一步优化的边际收益递减

---

## 全局成功标准

| 指标 | 基线 (v2.4) | 目标 (v2.5) | 测量方式 |
|------|------------|------------|----------|
| 工具定义 token 开销 | ~8K/请求 | ~4-6K/请求（意图相关） | `toolDefinitionTokens` Debug 日志 |
| System prompt 前缀一致性 | N/A（无缓存） | static 部分字节级一致 | 单元测试 |
| 裁剪标记信息量 | 无时间/大小 | 包含时间戳 + 原始 token 数 | 代码审查 |
| Token 估算精度 | ±20-30% | ±15% 或更优（有校准数据时） | estimated vs usage 对比日志 |

## 总体优先级排序

1. **优化项 1（工具过滤）** — 高价值、中风险、~120 行 → **立即实施**
2. **优化项 3（裁剪标记）** — 低价值、极低风险、~8 行 → **搭便车实施**
3. **优化项 4（Token 计量）** — 中价值、低风险、~60 行 → **第二优先**
4. **优化项 2（Prompt 缓存）** — 低价值（当前后端）、低风险、~80 行 → **最后实施或推迟**

---

## 评审记录

> 评审人: architect agent | 评审轮次: 2 | 结论: APPROVED

| ID | 级别 | 问题 | 修复 |
|----|------|------|------|
| C1 | CRITICAL | `read` 作为 default fallback 导致未分类请求丢失工具 | 新增 ReadKeywords + default 改为 `"general"` → All |
| H1 | HIGH | validate_file 归 Edit 组但是只读操作 | 改为 Core 组 |
| H2 | HIGH | ToolDispatcher 改动行数矛盾 | 确认无需改动，移除 +15 行估算 |
| H3 | HIGH | CalibrateFromUsage 签名类型不匹配 | 改为 (int estimated, int actual) + ratio clamp |
| H4 | HIGH | stream_options 缺序列化注解和配置开关 | StreamOptionsRequest 类 + [JsonPropertyName] + Config.StreamUsageEnabled |
| M1 | MEDIUM | PruneOldToolOutputs 用 content.Length/4 | 改为 ContextManager.EstimateTokens() |
| M2 | MEDIUM | Prompt 缓存 static 含条件性内容 | BugFixGuidance/QtTemplateGuidance 移到 Dynamic Tail |
