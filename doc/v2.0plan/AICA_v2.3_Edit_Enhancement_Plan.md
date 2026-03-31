# AICA v2.3 Edit 工具统一增强方案

> 日期: 2026-03-31
> 方案制定: Agent Team（planner + architect reviewer 双向评审，两轮达成一致）
> 状态: **✅ IMPLEMENTED**（全部 CRITICAL/HIGH 问题已修复，全量编译通过）
> 基线: AICA v2.2（commit `de521e7`）EditFileTool.cs 635 行

---

## 0. 背景与动机

### 现状

当前 `edit` 工具支持单文件单处替换（`file_path` + `old_string` + `new_string`），每次调用只能修改一个位置。LLM 需要多文件重构时必须逐个调用 edit，导致：

| 痛点 | 表现 | 影响 |
|------|------|------|
| 工具调用爆炸 | 10 处修改 = 10 次 edit 调用 | token 消耗大，延迟高 |
| 用户确认疲劳 | 每次 edit 弹出 diff 确认对话框 | 10 次确认严重影响体验 |
| 上下文偏移 | 同文件第 2 次 edit 的 old_string 基于第 1 次编辑后的内容 | LLM 需 read_file 刷新，或 doom loop |
| 无多文件原子性 | 多文件修改各自独立 | 部分应用后代码可能不一致 |

### 对标 OpenCode

| 能力 | AICA 现状 | OpenCode | 本方案目标 |
|------|-----------|----------|-----------|
| 单文件单处编辑 | ✅ edit | ✅ edit | ✅ 保持不变 |
| 同文件多处编辑 | ❌ | ✅ edit（multi-occurrence） | ✅ edits 数组 |
| 多文件编辑 | ❌ | ✅ apply_patch | ✅ files 数组 |
| 交互式确认 | ✅ diff 对话框 | ❌（直接应用） | ✅ 保持优势 |
| 模糊匹配 | ✅ 3 级 cascade | ✅ 9 级 cascade | 不变（v2.4 评估） |

### 核心约束

1. **所有编辑必须用户确认后才应用**（ShowDiffAndApplyAsync）
2. **100% 向后兼容** — 现有 `{file_path, old_string, new_string}` 调用方式不变
3. **不新增工具** — 一切增强在 `edit` 工具内完成
4. **同文件多处编辑聚合为一个 diff 预览**（减少确认次数）
5. **多文件编辑逐文件各一次 diff 预览**（每文件独立确认）

---

## 1. 架构设计

### 1.1 参数 Schema 设计（三种调用模式）

LLM 通过参数组合自动选择模式，无需显式 mode 字段：

**模式 A: 单编辑（现有，向后兼容）**
```json
{
  "file_path": "src/foo.cpp",
  "old_string": "int x = 1;",
  "new_string": "int x = 2;"
}
```

**模式 B: 同文件多处编辑**
```json
{
  "file_path": "src/foo.cpp",
  "edits": [
    { "old_string": "int x = 1;", "new_string": "int x = 2;" },
    { "old_string": "int y = 3;", "new_string": "int y = 4;" }
  ]
}
```

**模式 C: 多文件编辑**
```json
{
  "files": [
    {
      "file_path": "src/foo.cpp",
      "edits": [
        { "old_string": "int x = 1;", "new_string": "int x = 2;" }
      ]
    },
    {
      "file_path": "src/bar.cpp",
      "edits": [
        { "old_string": "void bar()", "new_string": "void bar(int n)" }
      ]
    }
  ]
}
```

**模式检测逻辑：**
```
if (args.has("files"))         → 模式 C（多文件）
else if (args.has("edits"))    → 模式 B（同文件多处）
else                           → 模式 A（单编辑，现有路径）
```

### 1.2 执行流程

```
ExecuteAsync(call, context, uiContext, ct)
  │
  ├─ 检测模式 ─→ A: 直接走现有逻辑（零改动）
  │             │
  │             ├─ B: ExecuteMultiEditAsync(file_path, edits[], context, ct)
  │             │     1. ReadFile
  │             │     2. 对每个 edit: FindWithCascade → 记录 (matchIndex, matchLength, newString)
  │             │     3. 按 matchIndex 降序排序
  │             │     4. 从后向前逐个替换（避免偏移漂移）
  │             │     5. ShowDiffAndApplyAsync(path, original, aggregatedNew) — 一次确认
  │             │     6. 返回结果
  │             │
  │             └─ C: ExecuteMultiFileAsync(files[], context, ct)
  │                   for each file in files:
  │                     1. ExecuteMultiEditAsync(file.path, file.edits, context, ct)
  │                     2. 收集结果（成功/失败/取消）
  │                   3. 汇总返回
```

### 1.3 偏移漂移处理算法（核心）

同文件多处编辑的关键挑战：当第一处替换改变了文件长度，后续替换的 matchIndex 就会失效。

**解决方案：从后向前应用（Reverse-Order Apply）**

```
edits = [edit1(matchIndex=10), edit2(matchIndex=50), edit3(matchIndex=100)]

排序后: [edit3(100), edit2(50), edit1(10)]

应用 edit3: 替换 index=100 处 → index<100 的内容不受影响
应用 edit2: 替换 index=50 处  → index<50 的内容不受影响
应用 edit1: 替换 index=10 处  → 无后续编辑

结果: 所有 matchIndex 在应用时都是有效的
```

**实现伪代码：**
```csharp
// 1. 先收集所有匹配位置（在原始内容上）
var matches = new List<(int Index, int Length, string NewText, int EditIndex)>();
foreach (var (edit, i) in edits.Select((e, i) => (e, i)))
{
    var cascade = FindWithCascade(normalizedContent, NormalizeLineEndings(edit.OldString));
    if (cascade == null)
        return ToolResult.Fail($"Edit #{i+1}: old_string not found...");
    matches.Add((cascade.Value.MatchIndex, cascade.Value.MatchLength,
                 NormalizeLineEndings(edit.NewString), i));
}

// 2. 检查重叠（排序后相邻区间不应重叠）
matches.Sort((a, b) => a.Index.CompareTo(b.Index));
for (int i = 1; i < matches.Count; i++)
{
    if (matches[i].Index < matches[i-1].Index + matches[i-1].Length)
        return ToolResult.Fail($"Edits #{matches[i-1].EditIndex+1} and #{matches[i].EditIndex+1} overlap.");
}

// 3. 从后向前替换（降序）
var sb = new StringBuilder(normalizedContent);
for (int i = matches.Count - 1; i >= 0; i--)
{
    sb.Remove(matches[i].Index, matches[i].Length);
    sb.Insert(matches[i].Index, matches[i].NewText);
}
var newContent = sb.ToString();
```

### 1.4 聚合 Diff 预览策略

**同文件多处编辑（模式 B）：**
- 所有编辑在内存中应用到文件副本
- 调用 `ShowDiffAndApplyAsync(path, originalContent, aggregatedNewContent)` — 一次确认
- 用户在 diff 视图中看到所有修改的合并效果
- 用户可在 diff 右侧面板进一步手动编辑

**多文件编辑（模式 C）：**
- 逐文件调用 `ShowDiffAndApplyAsync` — 每文件一次确认
- 用户逐文件审查和确认
- 任一文件取消不影响已确认的文件（非事务性，符合 IDE 交互习惯）

---

## 2. 实现步骤

### 执行顺序

```
Phase 0 基础设施 ──→ Phase 1 同文件多处编辑 ──→ Phase 2 多文件编辑
   [低风险]              [中风险]                    [低风险]
   ~70 行新增            ~250 行新增                 ~130 行新增
   4 文件                2 文件                      2 文件
```

---

### Phase 0: 基础设施补充（ToolParameterProperty 支持 array/object）

**优先级**: 最高（前置依赖） | **风险**: 低 | **新增 ~70 行，改动 4 文件**

#### 目标

`ToolParameterProperty` 当前只支持 `Type/Description/Enum/Default`，无法表达 `edits` 数组的子 schema（`items`）和嵌套对象（`properties`）。需要扩展以支持 OpenAI function calling 的标准 JSON Schema。

同时，`OpenAIClient.ConvertJsonElement` 已将 JSON 数组转为 `List<object>`，每个对象元素转为 `Dictionary<string, object>`。因此参数提取方法需要适配此运行时类型（而非 `JsonElement`）。

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `AICA.Core/Agent/IAgentTool.cs` | `ToolParameterProperty` 新增 `Items`/`Properties`/`Required` 字段，带 `[JsonIgnore(WhenWritingNull)]` | +10 |
| `AICA.Core/Agent/ToolParameterValidator.cs` | 新增 `GetListOfDicts` 方法，从 `List<object>` 提取 `List<Dictionary<string, object>>` | +25 |
| `AICA.Core.Tests/Agent/ToolParameterValidatorTests.cs` | GetListOfDicts 测试（正常/null/空数组/非数组类型） | +10 |
| `AICA.Core.Tests/LLM/ToolDefinitionSerializationTests.cs`（新建） | 嵌套 3 层 JSON Schema 序列化断言 | +30 |

#### 设计细节

**ToolParameterProperty 扩展：**
```csharp
public class ToolParameterProperty
{
    public string Type { get; set; }
    public string Description { get; set; }
    public string[] Enum { get; set; }
    public object Default { get; set; }

    // v2.3: Support array items and nested object schemas
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ToolParameterProperty Items { get; set; }               // for type="array"

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ToolParameterProperty> Properties { get; set; }  // for type="object"

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[] Required { get; set; }                         // for type="object"
}
```

> **序列化说明**: `OpenAIClient.BuildRequest` 使用 `JsonSerializer.Serialize(request, _jsonOptions)` 序列化整个请求。`_jsonOptions` 已配置 `CamelCase` 命名和 `WhenWritingNull` 忽略。`System.Text.Json` 在序列化 `object` 类型属性时使用运行时类型，因此 `ToolParameters` 和嵌套的 `ToolParameterProperty` 的属性会被正确序列化。新增字段的 `[JsonIgnore(WhenWritingNull)]` 确保现有工具（未使用这些字段）的序列化输出不变。

**GetListOfDicts 方法（适配 ConvertJsonElement 的运行时类型）：**

> **关键发现**: `OpenAIClient.ParseArguments` → `ConvertJsonElement` 已将 `JsonValueKind.Array` 转为 `List<object>`，`JsonValueKind.Object` 转为 `Dictionary<string, object>`。因此 `call.Arguments["edits"]` 的运行时类型是 `List<object>`，**不是** `JsonElement`。

```csharp
/// <summary>
/// Extract an array-of-objects parameter from Arguments.
/// After OpenAIClient.ConvertJsonElement, arrays are List&lt;object&gt;
/// and objects are Dictionary&lt;string, object&gt;.
/// Returns null if parameter not present (distinguishes from empty list).
/// </summary>
public static List<Dictionary<string, object>> GetListOfDicts(
    Dictionary<string, object> arguments,
    string paramName)
{
    if (arguments == null)
        throw new ArgumentNullException(nameof(arguments));

    if (!arguments.TryGetValue(paramName, out var value) || value == null)
        return null;

    if (value is List<object> list)
    {
        var result = new List<Dictionary<string, object>>(list.Count);
        foreach (var item in list)
        {
            if (item is Dictionary<string, object> dict)
                result.Add(dict);
            else
                throw new ToolParameterException(
                    $"Parameter '{paramName}' must be an array of objects, " +
                    $"but element is {item?.GetType().Name ?? "null"}");
        }
        return result;
    }

    throw new ToolParameterException(
        $"Parameter '{paramName}' must be an array, but got {value.GetType().Name}");
}
```

**嵌套 JSON Schema 序列化集成测试：**
```csharp
[Fact]
public void EditTool_NestedSchema_SerializesCorrectly()
{
    var editTool = new EditFileTool();
    var def = editTool.GetDefinition();

    var json = JsonSerializer.Serialize(def.Parameters, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    // Verify 3-level nesting: files → items → edits → items → old_string
    var doc = JsonDocument.Parse(json);
    var filesItems = doc.RootElement
        .GetProperty("properties")
        .GetProperty("files")
        .GetProperty("items");

    Assert.Equal("object", filesItems.GetProperty("type").GetString());

    var editsItems = filesItems
        .GetProperty("properties")
        .GetProperty("edits")
        .GetProperty("items");

    Assert.Equal("object", editsItems.GetProperty("type").GetString());
    Assert.True(editsItems.GetProperty("properties").TryGetProperty("old_string", out _));
    Assert.True(editsItems.GetProperty("properties").TryGetProperty("new_string", out _));

    var editsRequired = editsItems.GetProperty("required");
    Assert.Contains("old_string", editsRequired.EnumerateArray().Select(e => e.GetString()));
}
```

#### 验证策略

- 单元测试：GetListOfDicts 正常解析 / null 返回 / 空数组 / 非数组类型抛异常 / 非对象元素抛异常
- 集成测试：构造含 `edits` 和 `files` 参数的 ToolDefinition → 序列化为 JSON → 断言嵌套 3 层 schema 正确
- 回归测试：现有工具的 `GetDefinition()` 序列化输出不变（新字段为 null 被忽略）

#### 回滚策略

新增字段均为 nullable + `[JsonIgnore(WhenWritingNull)]`，不影响现有工具。删除 3 个字段 + 1 个方法 + 1 个测试文件即可回退。

#### 成功标准

- [ ] `ToolParameterProperty.Items` 和 `.Properties` 在 LLM 请求 JSON 中正确序列化为标准 JSON Schema
- [ ] 嵌套 3 层深度（files→edits→old_string）序列化输出通过集成测试断言
- [ ] `GetListOfDicts` 正确处理 `List<object>` 运行时类型
- [ ] 现有工具定义和参数提取行为不变

---

### Phase 1: 同文件多处编辑（模式 B）

**优先级**: 高 | **风险**: 中 | **新增 ~250 行，改动 2 文件**

#### 目标

- 支持 `edits` 数组参数实现同文件多处编辑
- 所有编辑聚合为一个 diff 预览，一次确认
- 处理偏移漂移和编辑区间重叠检测
- 唯一性校验（每个 edit 的 old_string 必须唯一匹配，不支持 replace_all）
- 用户手动编辑检测（与模式 A 对齐）
- 与现有 FindWithCascade 模糊匹配完全复用

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `AICA.Core/Tools/EditFileTool.cs` | `GetDefinition()` 新增 `edits` 参数 + Required 改空数组；模式检测 + `ExecuteMultiEditAsync` + `FindWithCascadeUnique` + `MultiEditOutcome/MultiEditResult` + `ParseEditsFromDicts` | +200 |
| `AICA.Core.Tests/Tools/EditFileToolTests.cs` | 多处编辑测试（偏移漂移、重叠、唯一性、no-op、空 edits、用户取消、用户手动编辑） | +50 |

#### 设计细节

**GetDefinition() 新增参数：**
```csharp
["edits"] = new ToolParameterProperty
{
    Type = "array",
    Description = "Array of edits for multi-edit mode. Each edit has old_string and new_string. " +
                  "When provided, all edits are applied to the same file and shown as a single diff preview.",
    Items = new ToolParameterProperty
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["old_string"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "The exact text to replace"
            },
            ["new_string"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "The replacement text"
            }
        },
        Required = new[] { "old_string", "new_string" }
    }
}
```

**Required 字段更新：** 三种模式各自需要不同的必填参数。JSON Schema 不支持条件 required，因此 `Required` 设为空数组，完全依赖运行时验证 + Description 自然语言引导。

```csharp
Required = Array.Empty<string>()  // 运行时根据模式动态验证
```

**模式检测逻辑（ExecuteAsync 入口，适配 ConvertJsonElement 运行时类型）：**
```csharp
// 检测调用模式（Arguments 中数组已被 ConvertJsonElement 转为 List<object>）
var filesDicts = ToolParameterValidator.GetListOfDicts(call.Arguments, "files");
var editsDicts = ToolParameterValidator.GetListOfDicts(call.Arguments, "edits");

if (filesDicts != null)
{
    var files = filesDicts.Select(d => new FileEditEntry
    {
        FilePath = ToolParameterValidator.GetRequiredParameter<string>(d, "file_path"),
        Edits = ParseEditsFromDicts(
            ToolParameterValidator.GetListOfDicts(d, "edits")
            ?? throw new ToolParameterException("Each file entry must have 'edits' array"))
    }).ToList();
    return (await ExecuteMultiFileAsync(files, context, ct)).ToolResult;
}
else if (editsDicts != null)
{
    var path = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "file_path");
    var edits = ParseEditsFromDicts(editsDicts);
    return (await ExecuteMultiEditAsync(path, edits, context, ct)).ToolResult;
}
else
{
    // 模式 A: 现有单编辑路径（零改动）
    ...
}
```

**辅助解析方法：**
```csharp
private static List<EditEntry> ParseEditsFromDicts(List<Dictionary<string, object>> dicts)
{
    return dicts.Select(d => new EditEntry
    {
        OldString = ToolParameterValidator.GetRequiredParameter<string>(d, "old_string"),
        NewString = ToolParameterValidator.GetRequiredParameter<string>(d, "new_string")
    }).ToList();
}
```

**内部结构化结果类型（消除字符串匹配判断取消的脆弱模式）：**
```csharp
private enum MultiEditOutcome
{
    Applied,
    Cancelled,
    Failed
}

private sealed class MultiEditResult
{
    public MultiEditOutcome Outcome { get; set; }
    public ToolResult ToolResult { get; set; }
}
```

**FindWithCascadeUnique（唯一性校验，模式 B 中 replace_all 不可用）：**

> **评审修复 C1**: 模式 B 中每个 edit 的 old_string 必须唯一匹配。如果出现多次，返回错误引导 LLM 提供更多上下文。

```csharp
/// <summary>
/// FindWithCascade + uniqueness enforcement for multi-edit mode.
/// </summary>
private (CascadeMatch? Match, string Error) FindWithCascadeUnique(string content, string oldString)
{
    var match = FindWithCascade(content, oldString);
    if (match == null)
        return (null, null); // no match — caller handles diagnostic

    // Verify uniqueness at Level 0 (Exact)
    if (match.Value.Level == MatchLevel.Exact)
    {
        var first = content.IndexOf(oldString, StringComparison.Ordinal);
        var second = content.IndexOf(oldString, first + 1, StringComparison.Ordinal);
        if (second >= 0)
            return (null, "old_string appears multiple times in the file. " +
                          "Provide more surrounding context to make it unique. " +
                          "replace_all is not supported in multi-edit mode.");
    }
    // Levels 1-3: internal implementation already enforces uniqueness

    return (match, null);
}
```

**ExecuteMultiEditAsync 核心流程（返回 MultiEditResult）：**

```csharp
private async Task<MultiEditResult> ExecuteMultiEditAsync(
    string path, List<EditEntry> edits, IAgentContext context, CancellationToken ct)
{
    // 1. 验证
    if (edits.Count == 0)
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolResult.Fail("edits array is empty") };
    if (edits.Count > 50)
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolResult.Fail("Too many edits (max 50 per call)") };

    // 2. 路径检查 + 读取文件
    if (!context.IsPathAccessible(path))
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolErrorHandler.HandleError(ToolErrorHandler.AccessDenied(path)) };
    if (!await context.FileExistsAsync(path, ct))
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolErrorHandler.HandleError(ToolErrorHandler.NotFound(path)) };
    if (FileTimeTracker.Instance.HasExternalModification(path))
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolResult.Fail($"File '{path}' modified externally. Use read_file first.") };

    var content = await context.ReadFileAsync(path, ct);
    var originalLineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
    var normalized = NormalizeLineEndings(content);

    // 3. 收集所有匹配位置（唯一性校验 + no-op 检测）
    var matches = new List<(int Index, int Length, string NewText, int EditIndex)>();
    for (int i = 0; i < edits.Count; i++)
    {
        var normOld = NormalizeLineEndings(edits[i].OldString);
        var normNew = NormalizeLineEndings(edits[i].NewString);

        // No-op 检测（评审修复 M1）
        if (normOld == normNew)
            return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                ToolResult = ToolResult.Fail($"Edit #{i+1}: old_string and new_string are identical.") };

        // 唯一性校验（评审修复 C1）
        var (cascade, uniqueError) = FindWithCascadeUnique(normalized, normOld);
        if (uniqueError != null)
            return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                ToolResult = ToolResult.Fail($"Edit #{i+1}: {uniqueError}") };
        if (cascade == null)
        {
            var diagnosis = DiagnoseEditFailure(normalized, normOld, path, context);
            return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                ToolResult = ToolResult.Fail($"Edit #{i+1} failed: {diagnosis.Message}") };
        }
        matches.Add((cascade.Value.MatchIndex, cascade.Value.MatchLength, normNew, i));
    }

    // 4. 按位置排序 + 重叠检测
    matches.Sort((a, b) => a.Index.CompareTo(b.Index));
    for (int i = 1; i < matches.Count; i++)
    {
        if (matches[i].Index < matches[i-1].Index + matches[i-1].Length)
            return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
                ToolResult = ToolResult.Fail(
                    $"Edits #{matches[i-1].EditIndex+1} and #{matches[i].EditIndex+1} overlap.") };
    }

    // 5. 从后向前替换
    var sb = new System.Text.StringBuilder(normalized);
    for (int i = matches.Count - 1; i >= 0; i--)
    {
        sb.Remove(matches[i].Index, matches[i].Length);
        sb.Insert(matches[i].Index, matches[i].NewText);
    }
    var newContent = sb.ToString();

    // 6. 还原行尾
    if (originalLineEnding == "\r\n")
        newContent = newContent.Replace("\n", "\r\n");

    // 7. 一次 diff 确认
    var diffResult = await context.ShowDiffAndApplyAsync(path, content, newContent, ct);
    if (!diffResult.Applied)
    {
        return new MultiEditResult
        {
            Outcome = MultiEditOutcome.Cancelled,
            ToolResult = ToolResult.Ok(
                $"MULTI-EDIT CANCELLED BY USER — {edits.Count} edits NOT applied to {path}\n\n" +
                "The user chose not to apply the proposed edits. Respect this decision.")
        };
    }

    // 8. 冲突检测记录
    FileTimeTracker.Instance.RecordEdit(path);
    context.RecordFileEdit(path);

    // 9. 检测用户手动编辑（评审修复 C2，与模式 A 对齐）
    var finalContent = await context.ReadFileAsync(path, ct);
    bool wasModifiedByUser = finalContent != newContent;

    if (wasModifiedByUser)
    {
        var originalLines = content.Split('\n').Length;
        var finalLines = finalContent.Split('\n').Length;
        var lineDiff = finalLines - originalLines;
        var diffText = lineDiff > 0 ? $"+{lineDiff}" : lineDiff < 0 ? $"{lineDiff}" : "0";

        return new MultiEditResult
        {
            Outcome = MultiEditOutcome.Applied,
            ToolResult = ToolResult.Ok(
                $"⚠️ USER MANUALLY EDITED THE FILE — YOUR SUGGESTION WAS NOT USED ⚠️\n\n" +
                $"File: {path}\n" +
                $"Original: {originalLines} lines → User's version: {finalLines} lines ({diffText})\n" +
                $"Attempted edits: {edits.Count}\n\n" +
                $"📄 ACTUAL FILE CONTENT (as saved by user):\n{finalContent}\n\n" +
                $"⚠️ CRITICAL: You MUST read and analyze the actual content above.")
        };
    }

    return new MultiEditResult
    {
        Outcome = MultiEditOutcome.Applied,
        ToolResult = ToolResult.Ok($"File edited: {path} ({edits.Count} edits applied in one diff)")
    };
}
```

**辅助类：**
```csharp
private sealed class EditEntry
{
    public string OldString { get; set; }
    public string NewString { get; set; }
}
```

#### 验证策略

- 单元测试：2 处编辑正确聚合、偏移漂移处理正确（第 1 处替换长度变化不影响第 2 处）
- 单元测试：重叠检测（两个 old_string 区域有重叠 → 报错）
- 单元测试：空 edits 数组 → 报错
- 集成测试：模式 B 调用触发一次 ShowDiffAndApplyAsync，用户确认后文件内容正确
- 回归测试：模式 A 单编辑行为完全不变

#### 回滚策略

删除 `edits` 参数定义 + `ExecuteMultiEditAsync` 方法 + 模式检测分支。现有 else 分支（模式 A）天然恢复为唯一路径。

#### 成功标准

- [ ] 同文件 3 处编辑 → 1 次 diff 确认 → 文件正确修改
- [ ] 偏移漂移：替换后长度变化不影响其他编辑
- [ ] 重叠编辑被检测并报错
- [ ] 现有单编辑测试全部通过

---

### Phase 2: 多文件编辑（模式 C）

**优先级**: 中 | **风险**: 低 | **新增 ~130 行，改动 2 文件**

#### 目标

- 支持 `files` 数组参数实现多文件编辑
- 逐文件调用 ExecuteMultiEditAsync，每文件一次 diff 确认
- 部分失败处理：已确认文件保留，失败/取消文件报告

#### 前置依赖

Phase 1 完成（复用 ExecuteMultiEditAsync）

#### 改动文件

| 文件 | 改动 | 估算行数 |
|------|------|----------|
| `AICA.Core/Tools/EditFileTool.cs` | `GetDefinition()` 新增 `files` 参数定义；新增 `ExecuteMultiFileAsync`（使用 MultiEditResult 结构化判断） | +100 |
| `AICA.Core.Tests/Tools/EditFileToolTests.cs` | 多文件测试（2 文件成功、部分取消、全部取消、空 files） | +30 |

#### 设计细节

**GetDefinition() 新增参数：**
```csharp
["files"] = new ToolParameterProperty
{
    Type = "array",
    Description = "Array of file edits for multi-file mode. Each entry has file_path and edits array. " +
                  "Each file is shown as a separate diff preview for independent confirmation. " +
                  "When provided, file_path/old_string/new_string/edits top-level params are ignored.",
    Items = new ToolParameterProperty
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["file_path"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Path to the file"
            },
            ["edits"] = new ToolParameterProperty
            {
                Type = "array",
                Description = "Edits to apply to this file",
                Items = new ToolParameterProperty
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["old_string"] = new ToolParameterProperty { Type = "string", Description = "Text to replace" },
                        ["new_string"] = new ToolParameterProperty { Type = "string", Description = "Replacement text" }
                    },
                    Required = new[] { "old_string", "new_string" }
                }
            }
        },
        Required = new[] { "file_path", "edits" }
    }
}
```

**ExecuteMultiFileAsync 核心流程（使用结构化 MultiEditResult 判断，评审修复 H2）：**
```csharp
private async Task<MultiEditResult> ExecuteMultiFileAsync(
    List<FileEditEntry> files, IAgentContext context, CancellationToken ct)
{
    if (files.Count == 0)
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolResult.Fail("files array is empty") };
    if (files.Count > 20)
        return new MultiEditResult { Outcome = MultiEditOutcome.Failed,
            ToolResult = ToolResult.Fail("Too many files (max 20 per call)") };

    var results = new List<string>();
    int applied = 0, cancelled = 0, failed = 0;

    foreach (var file in files)
    {
        var mer = await ExecuteMultiEditAsync(file.FilePath, file.Edits, context, ct);
        switch (mer.Outcome)
        {
            case MultiEditOutcome.Applied:
                applied++;
                results.Add($"✅ {file.FilePath}: {file.Edits.Count} edit(s) applied");
                break;
            case MultiEditOutcome.Cancelled:
                cancelled++;
                results.Add($"⏭️ {file.FilePath}: skipped by user");
                break;
            case MultiEditOutcome.Failed:
                failed++;
                results.Add($"❌ {file.FilePath}: {mer.ToolResult.Error ?? "failed"}");
                break;
        }
    }

    var summary = $"Multi-file edit: {applied} applied, {cancelled} skipped, {failed} failed\n" +
                  string.Join("\n", results);

    var outcome = applied > 0 ? MultiEditOutcome.Applied
                : cancelled > 0 ? MultiEditOutcome.Cancelled
                : MultiEditOutcome.Failed;

    return new MultiEditResult
    {
        Outcome = outcome,
        ToolResult = outcome != MultiEditOutcome.Failed
            ? ToolResult.Ok(summary)
            : ToolResult.Fail(summary)
    };
}
```

**FileEditEntry 辅助类：**
```csharp
private sealed class FileEditEntry
{
    public string FilePath { get; set; }
    public List<EditEntry> Edits { get; set; }
}
```

#### 部分失败语义

- **已确认文件保留** — 用户已审查并确认的文件修改不会回滚
- **取消的文件跳过** — 继续处理后续文件
- **失败的文件报告** — 记录错误但继续处理后续文件
- **全部取消** — 返回成功（内容说明所有文件被跳过）
- **全部失败** — 返回失败

这符合 IDE 交互习惯（类似 VS "跳过/重试/取消"模式），不同于数据库事务语义。

#### 验证策略

- 单元测试：2 文件均成功 → summary 正确
- 单元测试：第 1 文件成功、第 2 文件用户取消 → 第 1 文件保留、第 2 文件跳过
- 单元测试：空 files → 报错
- 回归测试：模式 A、B 行为不变

#### 回滚策略

删除 `files` 参数定义 + `ExecuteMultiFileAsync` 方法。模式检测代码中删除 files 分支。

#### 成功标准

- [ ] 2 文件各 2 处编辑 → 2 次 diff 确认（每文件一次）→ 文件正确修改
- [ ] 用户取消某文件不影响其他文件
- [ ] 部分失败时 summary 正确反映每文件状态
- [ ] 现有单编辑和多编辑测试全部通过

---

## 3. Tool Description 设计

更新后的 `Description` 属性（用于 LLM 工具选择）：

```csharp
public string Description =>
    "Replace text in files. Three modes:\n" +
    "1. Single edit: file_path + old_string + new_string (one replacement)\n" +
    "2. Multi-edit: file_path + edits array (multiple replacements in one file, shown as single diff)\n" +
    "3. Multi-file: files array with per-file edits (each file shown as separate diff)\n" +
    "old_string must match uniquely. Use read_file first to see exact content.\n" +
    "Limits: max 50 edits per file, max 20 files per call.\n" +
    "Do NOT use this to create new files — use write_file instead.";
```

> **Token 开销注意**: 新 Description + `edits`/`files` 参数 JSON Schema 预计增加 ~300 tokens/call 的工具定义开销。对 MiniMax-M2.5 的 177K context 来说可接受（<0.2%）。

---

## 4. 总改动量与执行计划

| Phase | 内容 | 新增行 | 删除行 | 文件数 | 风险 | 前置依赖 |
|-------|------|--------|--------|--------|------|----------|
| **Phase 0** | 基础设施（ToolParameterProperty 扩展 + GetListOfDicts + 序列化测试） | ~70 | 0 | 4 | 低 | 无 |
| **Phase 1** | 同文件多处编辑（含唯一性校验 + 用户编辑检测 + MultiEditResult） | ~250 | ~10 | 2 | 中 | Phase 0 |
| **Phase 2** | 多文件编辑（结构化判断） | ~130 | 0 | 2 | 低 | Phase 1 |
| **合计** | | **~450** | **~10** | **7** | | |

**完整执行序：**
```
Phase 0（基础设施）→ Phase 1（multiedit）→ Phase 2（多文件）
   [0.5 天]            [1.5 天]               [0.5 天]
```

---

## 5. 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| ToolParameterProperty 新字段影响现有工具序列化 | 新字段均为 nullable + `[JsonIgnore(WhenWritingNull)]`，H1 集成测试验证 |
| LLM 不理解新参数格式 | Description 中明确描述三种模式和限制；JSON Schema 约束嵌套结构 |
| edits 数组中某个 old_string 匹配失败 | 整个调用失败，返回 `Edit #N failed` + DiagnoseEditFailure 诊断，不做部分应用 |
| edits 数组中 old_string 出现多次 | FindWithCascadeUnique 强制唯一匹配，错误引导 LLM 提供更多上下文 |
| 偏移漂移算法 bug | 重叠检测 + 排序验证 + 从后向前替换是成熟算法模式 |
| 多文件编辑中途 CancellationToken 取消 | 已确认文件保留（已写入磁盘），未处理文件跳过 |
| MiniMax-M2.5 不生成嵌套 JSON 参数 | 三种模式均可独立工作；模式 A（现有）不变，模式 B/C 需 E2E 验证 |
| Token 开销增加 | 工具定义增加 ~300 tokens/call，占 177K context 的 <0.2%，可接受 |

---

## 6. 全局成功标准

- [x] Phase 0: 嵌套 3 层 JSON Schema 序列化集成测试通过（files→edits→old_string）
- [x] Phase 0: GetListOfDicts 正确处理 List<object> 运行时类型
- [x] Phase 1: 同文件 3 处编辑聚合为 1 次 diff 确认，偏移漂移处理正确
- [x] Phase 1: FindWithCascadeUnique 对多次出现的 old_string 返回唯一性错误
- [x] Phase 1: 用户手动编辑 diff 后返回 USER MANUALLY EDITED 消息（与模式 A 对齐）
- [x] Phase 2: 2 文件编辑逐文件确认，部分取消不影响已确认文件
- [x] Phase 2: MultiEditResult 结构化判断替代字符串匹配
- [x] 向后兼容：现有 `{file_path, old_string, new_string}` 调用行为 100% 不变
- [x] 工具数量不变（仍为 edit）
- [x] 现有 EditFileTool 测试套件全部通过
- [x] MiniMax-M2.5 下模式 A 行为不变（模式 B/C 需 E2E 验证 LLM 是否正确生成嵌套参数）
- [x] VSIX 打包大小增量 = 0（零新 NuGet 依赖）

---

## 7. 评审记录

> 评审人: architect agent | 评审轮次: 2 | 结论: APPROVED（全部问题已修复）

| ID | 级别 | 问题 | 修复 |
|----|------|------|------|
| C1 | CRITICAL | 模式 B 中 replace_all 和唯一性检查语义未定义 | 新增 FindWithCascadeUnique，模式 B 强制唯一匹配 |
| C2 | CRITICAL | ExecuteMultiEditAsync 缺少用户手动编辑检测 | 补充 finalContent 比对 + USER MANUALLY EDITED 消息 + RecordFileEdit |
| C3 | CRITICAL | call.Arguments 数组类型是 List\<object\> 不是 JsonElement | 废弃 GetArrayParameter\<JsonElement\>，改为 GetListOfDicts |
| H1 | HIGH | 缺少嵌套 JSON Schema 序列化集成测试 | 新增 ToolDefinitionSerializationTests.cs |
| H2 | HIGH | 字符串匹配 "CANCELLED" 判断取消太脆弱 | 引入 MultiEditOutcome/MultiEditResult 结构化枚举 |
| H3 | HIGH | Required 字段多模式冲突 | Required 改为空数组，运行时动态验证 |
| M1 | MEDIUM | 缺少 old_string == new_string 检测 | 匹配阶段添加 no-op 检测 |
| M2 | MEDIUM | 估算行数偏低 | 修正为 Phase 0: ~70, Phase 1: ~250, Phase 2: ~130, 合计 ~450 |
| L1 | LOW | Token 开销未记录 | 风险表和 Description 节补充说明 |
| L2 | LOW | 数组限制未告知 LLM | Description 中添加 "max 50 edits, max 20 files" |

---

## 8. 未来考虑（不在本方案范围内）

- **事务性多文件编辑**: 全部成功或全部回滚（需要 backup + rollback 机制，v3.0 评估）
- **add/delete/move 操作**: OpenCode apply_patch 的全功能集（当前仅支持 update/replace）
- **自动格式化**: 编辑后调用 clang-format（需要与 VS 格式化器集成）
- **LSP 语义验证**: 编辑后自动检测语法错误（需要 LSP client 就绪）
