# Phase 2 R2 开发计划

> **日期**：2026-04-07
> **范围**：H1 剩余 5 工具截断持久化接入 + MCP-A 冗余文件清理
> **参照**：Phase 2 R1 (commit `8c6287f`，ReadFile + RunCommand)
> **权威文档**：`AICA_v2.1_Unified_Plan_v2.1.md` + `AICA_v2.1_Four_Instance_Review.md`

---

## 一、R1 Pilot 模式回顾（已完成）

R1 建立了截断持久化的标准接入模式：

```csharp
// 标准模式：Feature flag → PersistAndTruncate → 格式化结果
if (AicaConfig.Current.Features.TruncationPersistence)
{
    var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
        "tool_name", fullOutput, previewCharLimit);
    if (tr.WasTruncated)
    {
        return tr.PreviewText +
            $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
            "Use read_file with the above path to see the complete output.";
    }
    return tr.PreviewText;
}
// else: 保留原有截断逻辑作为 fallback
```

**核心组件**（已就绪，无需修改）：
- `ToolOutputPersistenceManager.cs` — 单例，PersistAndTruncate()
- `AicaConfig.Current.Features.TruncationPersistence` — Feature flag
- `AicaConfig.Current.Truncation.DefaultPreviewChars` — 默认 4000 字符

---

## 二、R2 任务分解（6 个子任务）

### T1: GrepSearchTool 截断持久化 [Pane 1]

**文件**: `src/AICA.Core/Tools/GrepSearchTool.cs`
**工作量**: 0.5 天

**需要添加的 using**:
```csharp
using AICA.Core.Config;
using AICA.Core.Storage;
```

**修改点 1 — C# 搜索路径** (line ~301-303):

当前代码：
```csharp
var output = summary.ToString() + results.ToString();
output += $"\n[TOOL_EXACT_STATS: ...]";
return ToolResult.Ok(output);
```

改为（output 组装完成后、return 前）：
```csharp
var output = summary.ToString() + results.ToString();
output += $"\n[TOOL_EXACT_STATS: ...]";

// v2.1 H1: Truncation persistence
if (AicaConfig.Current.Features.TruncationPersistence)
{
    var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
        "grep_search", output, AicaConfig.Current.Truncation.DefaultPreviewChars);
    if (tr.WasTruncated)
    {
        output = tr.PreviewText +
            $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
            "Use read_file with the above path to see the complete output.";
    }
    else
    {
        output = tr.PreviewText;
    }
}

return ToolResult.Ok(output);
```

**修改点 2 — Ripgrep 路径** (line ~655-658):

同样模式，在 `return ToolResult.Ok(output);` 之前插入截断持久化逻辑。

**注意**：空结果路径（"No matches found"）不需要截断，仅在有匹配结果时处理。

---

### T2: ListDirTool 截断持久化 [Pane 1]

**文件**: `src/AICA.Core/Tools/ListDirTool.cs`
**工作量**: 0.5 天

**需要添加的 using**:
```csharp
using AICA.Core.Config;
using AICA.Core.Storage;
```

**修改点** (line ~176):

当前代码：
```csharp
return Task.FromResult(ToolResult.Ok(sb.ToString()));
```

改为：
```csharp
var output = sb.ToString();

// v2.1 H1: Truncation persistence
if (AicaConfig.Current.Features.TruncationPersistence)
{
    var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
        "list_dir", output, AicaConfig.Current.Truncation.DefaultPreviewChars);
    if (tr.WasTruncated)
    {
        output = tr.PreviewText +
            $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
            "Use read_file with the above path to see the complete output.";
    }
    else
    {
        output = tr.PreviewText;
    }
}

return Task.FromResult(ToolResult.Ok(output));
```

**注意**：ListDirTool 已有 800 条目截断（maxItems=800），H1 截断是字符级别的额外保护层。

---

### T3: GlobTool 截断持久化 [Pane 2]

**文件**: `src/AICA.Core/Tools/GlobTool.cs`
**工作量**: 0.5 天

**需要添加的 using**:
```csharp
using AICA.Core.Config;
using AICA.Core.Storage;
```

**修改点** (line ~161):

当前代码：
```csharp
return Task.FromResult(ToolResult.Ok(sb.ToString()));
```

改为（同 T2 模式）：
```csharp
var output = sb.ToString();

// v2.1 H1: Truncation persistence
if (AicaConfig.Current.Features.TruncationPersistence)
{
    var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
        "glob", output, AicaConfig.Current.Truncation.DefaultPreviewChars);
    if (tr.WasTruncated)
    {
        output = tr.PreviewText +
            $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
            "Use read_file with the above path to see the complete output.";
    }
    else
    {
        output = tr.PreviewText;
    }
}

return Task.FromResult(ToolResult.Ok(output));
```

---

### T4: WriteFileTool 截断持久化 [Pane 2]

**文件**: `src/AICA.Core/Tools/WriteFileTool.cs`
**工作量**: 0.25 天

**分析**：WriteFileTool 返回的是小型成功消息（"File created: path (X lines, Y bytes)"），正常情况下不会触发截断。但统一计划要求"所有工具统一接入"，所以添加保护性集成以确保一致性。

**需要添加的 using**:
```csharp
using AICA.Core.Config;
using AICA.Core.Storage;
```

**方案**：抽取一个私有辅助方法 `ApplyTruncationIfNeeded()`，在两个 `return ToolResult.Ok(...)` 之前调用：

```csharp
/// <summary>
/// v2.1 H1: Apply truncation persistence if output exceeds limit.
/// </summary>
private string ApplyTruncationIfNeeded(string output)
{
    if (!AicaConfig.Current.Features.TruncationPersistence)
        return output;

    var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
        "write_file", output, AicaConfig.Current.Truncation.DefaultPreviewChars);
    if (tr.WasTruncated)
    {
        return tr.PreviewText +
            $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
            "Use read_file with the above path to see the complete output.";
    }
    return tr.PreviewText;
}
```

在 line ~110 和 line ~133 的 `return ToolResult.Ok(...)` 中使用此方法包装输出。

---

### T5: EditFileTool — TruncationStep [Pane 3]

**新文件**: `src/AICA.Core/Tools/Pipeline/TruncationStep.cs`
**修改文件**: `src/AICA.Core/Tools/EditFileTool.cs`（注册 step）
**工作量**: 1 天

**Plan 规定**：TruncationStep 在 PostEditPipeline 中 Order=400（S3 HeaderSyncStep=200 之后，DiagnosticsStep=900 之前）

**TruncationStep.cs** 实现：

```csharp
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Storage;

namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// v2.1 H1: Truncation persistence for edit tool output.
    /// When the accumulated ToolResult content exceeds the preview limit,
    /// persist the full output to disk and replace with a truncated preview.
    ///
    /// Order=400 (after HeaderSync=200, before Diagnostics=900).
    /// Feature flag: features.truncationPersistence
    /// Fail-open: truncation failure does not block the edit result.
    /// </summary>
    public class TruncationStep : IEditStep
    {
        public string Name => "Truncation";
        public bool IsEnabled => AicaConfig.Current.Features.TruncationPersistence;
        public int Order => 400;
        public EditPhase Phase => EditPhase.PostEdit;
        public bool FailureIsFatal => false;

        public bool ShouldRun(EditContext ctx)
        {
            // Always run when enabled — the PersistAndTruncate method
            // is a no-op when content is under the limit
            return true;
        }

        public Task<ToolResult> RunAsync(EditContext ctx, ToolResult current, CancellationToken ct)
        {
            if (current == null || string.IsNullOrEmpty(current.Content))
                return Task.FromResult(current);

            var tr = ToolOutputPersistenceManager.Instance.PersistAndTruncate(
                "edit", current.Content, AicaConfig.Current.Truncation.DefaultPreviewChars,
                ctx.SessionId);

            if (tr.WasTruncated)
            {
                current.Content = tr.PreviewText +
                    $"\n\n[Full output saved to: {tr.FullOutputPath}]\n" +
                    "Use read_file with the above path to see the complete output.";
            }
            else
            {
                current.Content = tr.PreviewText;
            }

            return Task.FromResult(current);
        }
    }
}
```

**EditFileTool.cs 注册** (构造函数 line ~29):

当前：
```csharp
_pipeline.Register(new HeaderSyncStep());   // Order=200
_pipeline.Register(new DiagnosticsStep());  // Order=900
```

改为：
```csharp
_pipeline.Register(new HeaderSyncStep());    // Order=200, PostEdit — S3 头文件同步
_pipeline.Register(new TruncationStep());    // Order=400, PostEdit — H1 截断持久化
_pipeline.Register(new DiagnosticsStep());   // Order=900, PostEdit — 诊断信息
```

---

### T6: MCP-A 冗余文件清理 [Pane 4]

**文件**: `src/AICA.Core/Agent/GitNexusProcessManager.cs`
**参考**: `doc/nextstep2.1/AICA_MCP_Redundant_Files_Issue.md`
**工作量**: 0.5 天

**新增方法**:

```csharp
/// <summary>
/// v2.1 MCP-A: Clean up redundant files generated by GitNexus analyze.
/// Removes AGENTS.md, CLAUDE.md, and .claude/ directory from the repository root.
/// Fail-safe: cleanup failure does not affect indexing functionality.
/// </summary>
private void CleanupRedundantFiles(string repoRoot)
{
    if (string.IsNullOrEmpty(repoRoot)) return;

    var filesToDelete = new[] { "AGENTS.md", "CLAUDE.md" };
    var dirsToDelete = new[] { ".claude" };

    foreach (var file in filesToDelete)
    {
        try
        {
            var path = System.IO.Path.Combine(repoRoot, file);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] MCP-A: Deleted redundant file: {path}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AICA] MCP-A: Failed to delete {file}: {ex.Message}");
        }
    }

    foreach (var dir in dirsToDelete)
    {
        try
        {
            var path = System.IO.Path.Combine(repoRoot, dir);
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] MCP-A: Deleted redundant directory: {path}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AICA] MCP-A: Failed to delete {dir}/: {ex.Message}");
        }
    }
}
```

**调用点 1 — TriggerIndexAsync** (line ~298 后，exitCode==0 分支):
```csharp
if (exitCode == 0)
{
    CleanupRedundantFiles(repoRoot);  // v2.1 MCP-A
}
```

**调用点 2 — TriggerIndexWithProgressAsync** (line ~387-390 后，exitCode==0 分支):
```csharp
if (exitCode == 0)
{
    CleanupRedundantFiles(repoRoot);  // v2.1 MCP-A
}
```

---

## 三、团队分工

| Pane | 任务 | 工作量 | 依赖 |
|------|------|--------|------|
| Pane 1 | T1 GrepSearchTool + T2 ListDirTool | 1 天 | 无 |
| Pane 2 | T3 GlobTool + T4 WriteFileTool | 0.75 天 | 无 |
| Pane 3 | T5 EditFileTool TruncationStep | 1 天 | 无 |
| Pane 4 | T6 MCP-A GitNexus 冗余清理 | 0.5 天 | 无 |
| Pane 5 | 全部任务代码审查 | — | T1-T6 完成后 |

**所有任务互相独立，可完全并行**。

---

## 四、验收标准

### 功能验收
1. **GrepSearchTool**: 搜索结果超过 4000 字符时，完整输出持久化到 `~/.AICA/truncations/`，返回预览+路径提示
2. **ListDirTool**: 递归目录列表超限时同上
3. **GlobTool**: 大量文件匹配结果超限时同上
4. **WriteFileTool**: 统一接入保护（正常场景不触发）
5. **EditFileTool**: TruncationStep 在 Pipeline 中 Order=400 正确执行
6. **MCP-A**: GitNexus analyze 完成后，AGENTS.md/CLAUDE.md/.claude/ 被清理；清理失败不影响索引

### 代码质量
- Feature flag `TruncationPersistence` 控制所有截断行为
- 原有截断逻辑保留为 fallback（flag off 时使用）
- 所有新代码遵循 R1 已建立的模式
- 异常处理：fail-open，截断失败不影响工具正常输出

### 构建验证
- `dotnet build` 通过
- 现有单元测试全部通过

---

## 五、Pane 5 审查检查清单

- [ ] 所有 5 个工具是否都正确接入 `ToolOutputPersistenceManager.Instance.PersistAndTruncate()`
- [ ] Feature flag 检查是否一致（`AicaConfig.Current.Features.TruncationPersistence`）
- [ ] 工具名参数是否与实际工具名一致（grep_search, list_dir, glob, write_file, edit）
- [ ] 原有截断逻辑是否保留为 fallback
- [ ] TruncationStep 的 Order/Phase/FailureIsFatal 是否符合 Plan 规定
- [ ] MCP-A 是否在两个调用点都添加了清理
- [ ] MCP-A 清理失败是否被正确捕获（不影响主流程）
- [ ] 是否有多余的代码改动
- [ ] using 语句是否完整且无冗余
