# Bug 修复计划：TelemetryLogger 注入 + 中间件注册缺失

> **版本**: v2.0 — 5 Pane 讨论后收敛版
> **日期**: 2026-04-08
> **状态**: 已收敛，待开发
> **触发**: Phase 3 E2E 测试发现 telemetry 不写入 + H3a 权限反馈不生效

---

## 一、问题全貌

### 根因：VSIX 层未创建 TelemetryLogger 实例

`ChatToolWindowControl.xaml.cs` 中从未 `new TelemetryLogger()`，导致所有依赖它的组件都收不到 telemetry 实例。

### 影响清单

| # | 组件 | 问题 | 影响 |
|---|------|------|------|
| 1 | TelemetryLogger | 从未创建实例 | 根因 |
| 2 | AgentExecutor | 构造时未传 telemetryLogger | 所有 telemetry 埋点不写入 |
| 3 | PermissionCheckMiddleware | 未注册到 pipeline | H3a 权限反馈不生效 |
| 4 | MonitoringMiddleware | 未注册到 pipeline | 工具执行监控不生效 |
| 5 | ToolOutputPersistenceManager | Singleton 未传 telemetryLogger | 截断 telemetry 不记录 |

---

## 二、讨论收敛结论（5 Pane 共识）

### 结论 1：PermissionCheckMiddleware 只处理 RequiresApproval

**共识**：注册 PermissionCheckMiddleware，但**修改它跳过 RequiresConfirmation 分支**，仅保留 RequiresApproval 分支。

**理由**：
- EditFileTool/WriteFileTool 已有 ShowDiffAndApplyAsync（比通用 MessageBox 更好）
- RunCommandTool 已有 RequestConfirmationAsync
- 注册 Middleware 且保留 RequiresConfirmation → 双重确认
- H3a 反馈收集的核心入口是 RequiresApproval（当前只有 AskFollowupQuestionTool 使用）
- 工具内部特化确认 > Middleware 通用确认

### 结论 2：中间件执行顺序

```
PreValidation → Monitoring → Permission → Verification → [Core]
```

- Monitoring 在 Permission 之前，可记录被拒绝的调用
- PreValidation 最前，参数无效的调用无需进入后续链路

### 结论 3：sessionId 使用唯一 ID

每次对话生成唯一 sessionId：`Guid.NewGuid().ToString("N").Substring(0, 8)`
不硬编码 "agent"。

### 结论 4：TelemetryLogger 存为字段并 Dispose

TelemetryLogger 存为 `_telemetryLogger` 字段，在 `ChatToolWindowControl.Dispose` 中调用 `_telemetryLogger?.Dispose()` 做 final flush。

---

## 三、修复方案（收敛版）

### Pane 1 任务：修改 PermissionCheckMiddleware — 跳过 RequiresConfirmation

**文件**: `src/AICA.Core/Agent/Middleware/PermissionCheckMiddleware.cs`

- 删除或跳过 `RequiresConfirmation` 分支（原 line 62-78 附近）
- 仅保留 `RequiresApproval` 分支
- 不论有无反馈功能，RequiresApproval 拒绝时都走 SecurityDenied + H3a 反馈
- RequiresConfirmation 的工具继续由工具内部处理确认

### Pane 2 任务：新建 VSPermissionHandler + 修改 ChatToolWindowControl

**文件**（新建）: `src/AICA.VSIX/Agent/VSPermissionHandler.cs`

```csharp
internal class VSPermissionHandler : IPermissionHandler
{
    public async Task<bool> RequestApprovalAsync(IAgentTool tool, ToolCall call, IUIContext uiContext, CancellationToken ct)
    {
        return await uiContext.ShowConfirmationAsync(
            "工具调用审批: " + tool.Name,
            "AI 请求执行 " + tool.Name + "，是否允许？",
            ct);
    }

    public async Task<bool> RequestConfirmationAsync(IAgentTool tool, ToolCall call, IUIContext uiContext, CancellationToken ct)
    {
        // 由工具内部处理，middleware 不再调用此方法
        return true;
    }
}
```

**文件**（修改）: `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`

改动内容：
1. 新增字段 `private AICA.Core.Logging.TelemetryLogger _telemetryLogger;`
2. 在 `_llmClient = new OpenAIClient(...)` 之后创建实例：
   ```csharp
   _telemetryLogger = new AICA.Core.Logging.TelemetryLogger();
   var sessionId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
   ```
3. 初始化 ToolOutputPersistenceManager：
   ```csharp
   AICA.Core.Storage.ToolOutputPersistenceManager.Initialize(_telemetryLogger);
   ```
4. 注册中间件（在现有 PreValidationMiddleware 之后，按顺序）：
   ```csharp
   // Monitoring — wraps everything to capture denied calls too
   _toolDispatcher.UseMiddleware(new AICA.Core.Agent.Middleware.MonitoringMiddleware(
       telemetryLogger: _telemetryLogger, sessionId: sessionId));
   // Permission — H3a feedback on RequiresApproval only
   var permissionHandler = new AICA.VSIX.Agent.VSPermissionHandler();
   _toolDispatcher.UseMiddleware(new AICA.Core.Agent.Middleware.PermissionCheckMiddleware(
       permissionHandler, telemetryLogger: _telemetryLogger, sessionId: sessionId));
   ```
5. AgentExecutor 传入 telemetryLogger：
   ```csharp
   _agentExecutor = new AgentExecutor(
       _llmClient, _toolDispatcher,
       maxIterations: options.MaxAgentIterations,
       maxTokenBudget: tokenBudget,
       customInstructions: options.CustomInstructions,
       telemetryLogger: _telemetryLogger);
   ```
6. Dispose 中清理：
   ```csharp
   _telemetryLogger?.Dispose();
   ```

### Pane 3 任务：ToolOutputPersistenceManager 新增 Initialize 方法

**文件**: `src/AICA.Core/Storage/ToolOutputPersistenceManager.cs`

新增静态方法：
```csharp
public static void Initialize(TelemetryLogger telemetryLogger = null)
{
    lock (_instanceLock)
    {
        _instance = new ToolOutputPersistenceManager(telemetryLogger: telemetryLogger);
    }
}
```

---

## 四、文件冲突矩阵

| 文件 | Pane 1 | Pane 2 | Pane 3 |
|------|--------|--------|--------|
| PermissionCheckMiddleware.cs | ✏️ 修改 | | |
| VSPermissionHandler.cs | | ✏️ 新建 | |
| ChatToolWindowControl.xaml.cs | | ✏️ 修改 | |
| ToolOutputPersistenceManager.cs | | | ✏️ 修改 |

**冲突**: 无。

## 五、执行顺序

```
Round 1（并行）:
  Pane 1: 修改 PermissionCheckMiddleware — 跳过 RequiresConfirmation
  Pane 3: ToolOutputPersistenceManager 新增 Initialize

Round 2（依赖 Pane 1/3 完成）:
  Pane 2: 新建 VSPermissionHandler + 修改 ChatToolWindowControl（全部注入+注册）

Round 3:
  Pane 5: 代码审核
```

---

## 六、遗留问题（方向 A 选择后）

### H3a 覆盖范围极窄

**现状**: PermissionCheckMiddleware 跳过 RequiresConfirmation 后，H3a 反馈仅在 RequiresApproval 拒绝时触发。当前只有 `AskFollowupQuestionTool` 标记了 RequiresApproval = true。

**不覆盖的场景**（用户最常见的拒绝操作）：
- EditFileTool: 用户在 diff 预览中取消 → 不触发反馈
- WriteFileTool: 用户在 diff 预览中取消 → 不触发反馈
- RunCommandTool: 用户在命令确认中取消 → 不触发反馈

**根因**: 这三个工具的确认由工具内部特化 UI 处理（diff 预览/命令预览），不经过 Middleware。

**后续解决方案**: 统一权限架构改造
1. 将工具内部的确认逻辑迁移到 Middleware 层统一管理
2. 将 diff 预览作为 Middleware 的 UI 扩展
3. PermissionCheckMiddleware 恢复 RequiresConfirmation 分支
4. 所有拒绝场景统一触发 H3a 反馈收集

**预计时机**: 可在后续 Phase 中作为权限系统统一改造的一部分实施。
