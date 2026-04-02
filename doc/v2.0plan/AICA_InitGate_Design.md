# AICA 初始化门控 + 进度条 — 详细设计方案

> 版本: v1.1 | 日期: 2026-04-02
> 方案制定: planner agent + reviewer agent（双向评审，一轮达成一致）
> 基线版本: v2.10.0 (current main)
> 状态: **APPROVED — 双方达成一致，8 项修复已合并**

---

## 一、需求

### 问题
当前解决方案打开时，AICA 在后台执行多项初始化任务（规则目录创建、符号索引、GitNexus npm install + analyze），但用户看不到进度。npm install (~104s) 完全静默，用户不知道系统在做什么。用户可以在初始化未完成时发送消息，导致工具链不完整。

### 目标
1. **初始化门控**: 初始化期间用进度 UI 替代主聊天界面，阻止用户发送消息
2. **进度 UI**: 简洁的进度条 + 步骤清单（类似安装向导），每步显示状态
3. **npm install 可见性**: 将 GitNexus npm install 从静默改为可见控制台窗口
4. **触发时机**: 解决方案打开时若 AICA 窗口可见则切换到进度视图；用户中途打开窗口则按当前状态显示
5. **失败处理**: 失败步骤显示警告但不阻塞，所有步骤尝试完成后用户可进入主 UI
6. **并行感知**: npm install (~104s) 和符号索引 (~22s) 并行运行，进度 UI 同时显示两者

---

## 二、当前初始化流程分析

### 入口: `SolutionEventListener.OnAfterOpenSolutionAsync` (Events/SolutionEventListener.cs:61)

```
OnAfterOpenSolutionAsync(solutionPath)
  │
  ├─ await _initializer.InitializeAsync(projectRoot)     // Step 1: 规则目录 (~0.1s)
  │     └─ 内含 IsCppProject 扫描 (148/200 files)        //         + C++ 检测 (~0.1s)
  │
  ├─ _ = IndexProjectAsync(solutionPath)                  // Step 2: 符号索引 (fire-and-forget)
  │     └─ ProjectIndexer.IndexDirectoryAsync              //         TreeSitter解析 (~22s)
  │         └─ Parse() 首次调用触发 PreloadNativeDlls      //         DLL预加载 (~0.1s)
  │
  └─ _ = Task.Run(() => GitNexusProcessManager             // Step 3+4: GitNexus (fire-and-forget)
        .Instance.TriggerIndexAsync(solutionPath))
          ├─ ResolveGitNexusPath()                         // Step 3: npm install (~104s, 仅首次)
          │     └─ 同步执行 Process.Start + WaitForExit     //         CreateNoWindow=true (静默!)
          └─ Process.Start("cmd.exe", analyze...)           // Step 4: analyze (~5s)
                                                            //         CreateNoWindow=false (可见)
```

**关键发现:**
- Steps 2 和 3+4 是并行的（fire-and-forget）
- npm install 在 `ResolveGitNexusPath()` 内同步阻塞，不是独立步骤
- 当前没有任何机制通知 UI 进度
- `ChatToolWindowControl` 已订阅 `SolutionEvents.Opened` 事件（独立于 SolutionEventListener）

### 涉及文件

| 文件 | 角色 |
|------|------|
| `AICA.VSIX/Events/SolutionEventListener.cs` | 初始化编排入口 |
| `AICA.Core/Rules/RulesDirectoryInitializer.cs` | 规则目录 + C++ 检测 |
| `AICA.Core/Knowledge/ProjectIndexer.cs` | TreeSitter 符号索引 |
| `AICA.Core/Knowledge/TreeSitterSymbolParser.cs` | DLL 预加载 + AST 解析 |
| `AICA.Core/Agent/GitNexusProcessManager.cs` | npm install + analyze |
| `AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml(.cs)` | 主 UI |
| `AICA.VSIX/ToolWindows/ChatToolWindow.cs` | ToolWindowPane 容器 |

---

## 三、架构设计

### 核心思路

引入 `InitializationManager`（AICA.Core）作为中央协调器，将 `SolutionEventListener` 中的 fire-and-forget 初始化改为有状态、可观察的流程。UI 层（AICA.VSIX）通过事件订阅进度更新。

```
┌─────────────────────┐         ┌──────────────────────┐
│  SolutionEventListener │───────►│  InitializationManager │
│  (触发初始化)          │         │  (协调 + 状态跟踪)      │
└─────────────────────┘         │                      │
                                │  Steps:              │
                                │  1. RulesInit         │
                                │  2. SymbolIndexing    │
                                │  3. GitNexusInstall   │
                                │  4. GitNexusAnalyze   │
                                │                      │
                                │  Events:             │
                                │  - StepChanged        │
                                │  - InitCompleted      │
                                └──────────┬───────────┘
                                           │ events
                                           ▼
                                ┌──────────────────────┐
                                │  ChatToolWindowControl │
                                │  ┌────────────────┐   │
                                │  │InitProgressOverlay│  │
                                │  │(WPF UserControl) │  │
                                │  └────────────────┘   │
                                └──────────────────────┘
```

### 层分离

- **AICA.Core** (`InitializationManager`, `InitStep`, `InitStepState`): 纯逻辑，无 WPF 依赖，netstandard2.0 兼容
- **AICA.VSIX** (`InitProgressOverlay`): WPF UI，通过事件订阅 + Dispatcher 更新

---

## 四、数据模型

### 新建 `AICA.Core/Initialization/InitStep.cs` (~60行)

```csharp
namespace AICA.Core.Initialization
{
    /// <summary>
    /// Initialization step identifiers.
    /// </summary>
    public enum InitStepId
    {
        RulesInit,        // 规则目录创建 + C++ 检测
        SymbolIndexing,   // TreeSitter 符号索引
        GitNexusInstall,  // npm install (仅首次)
        GitNexusAnalyze   // GitNexus analyze
    }

    /// <summary>
    /// Step execution status.
    /// </summary>
    public enum InitStepStatus
    {
        Pending,    // 等待执行
        Running,    // 正在执行
        Completed,  // 成功完成
        Failed,     // 执行失败（非阻塞）
        Skipped     // 跳过（如无 .git 目录跳过 GitNexus）
    }

    /// <summary>
    /// Immutable snapshot of a single initialization step's state.
    /// </summary>
    public class InitStepState
    {
        public InitStepId Id { get; }
        public string DisplayName { get; }
        public InitStepStatus Status { get; }
        public string StatusMessage { get; }  // 如 "5317 files, 113451 symbols"
        public double? ProgressPercent { get; }  // null = indeterminate

        public InitStepState(
            InitStepId id,
            string displayName,
            InitStepStatus status,
            string statusMessage = null,
            double? progressPercent = null)
        {
            Id = id;
            DisplayName = displayName;
            Status = status;
            StatusMessage = statusMessage;
            ProgressPercent = progressPercent;
        }

        /// <summary>
        /// Create a new state with updated fields (immutable pattern).
        /// </summary>
        public InitStepState With(
            InitStepStatus? status = null,
            string statusMessage = null,
            double? progressPercent = null)
        {
            return new InitStepState(
                Id,
                DisplayName,
                status ?? Status,
                statusMessage ?? StatusMessage,
                progressPercent ?? ProgressPercent);
        }
    }
}
```

### 新建 `AICA.Core/Initialization/InitializationManager.cs` (~200行)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Initialization
{
    /// <summary>
    /// Event args for step state changes.
    /// </summary>
    public class InitStepChangedEventArgs : EventArgs
    {
        public InitStepState Step { get; }
        public InitStepChangedEventArgs(InitStepState step) => Step = step;
    }

    /// <summary>
    /// Event args for initialization completion.
    /// </summary>
    public class InitCompletedEventArgs : EventArgs
    {
        public bool AllSucceeded { get; }
        public IReadOnlyList<InitStepState> Steps { get; }

        public InitCompletedEventArgs(bool allSucceeded, IReadOnlyList<InitStepState> steps)
        {
            AllSucceeded = allSucceeded;
            Steps = steps;
        }
    }

    /// <summary>
    /// Central coordinator for AICA initialization steps.
    /// Tracks step progress, fires events for UI consumption.
    /// Thread-safe: all state access is lock-protected.
    /// </summary>
    public class InitializationManager
    {
        private readonly object _lock = new object();
        private readonly Dictionary<InitStepId, InitStepState> _steps;
        private volatile bool _isRunning;
        private CancellationTokenSource _cts;

        /// <summary>Fired when initialization starts. Thread: caller (usually background).</summary>
        public event EventHandler InitStarted;

        /// <summary>Fired when any step's state changes. Thread: background.</summary>
        public event EventHandler<InitStepChangedEventArgs> StepChanged;

        /// <summary>Fired when all steps have completed/failed/skipped. Thread: background.</summary>
        public event EventHandler<InitCompletedEventArgs> InitCompleted;

        /// <summary>Whether initialization is currently running.</summary>
        public bool IsRunning => _isRunning;

        public InitializationManager()
        {
            _steps = new Dictionary<InitStepId, InitStepState>
            {
                [InitStepId.RulesInit] = new InitStepState(
                    InitStepId.RulesInit, "规则目录初始化", InitStepStatus.Pending),
                [InitStepId.SymbolIndexing] = new InitStepState(
                    InitStepId.SymbolIndexing, "符号索引", InitStepStatus.Pending),
                [InitStepId.GitNexusInstall] = new InitStepState(
                    InitStepId.GitNexusInstall, "GitNexus 依赖安装", InitStepStatus.Pending),
                [InitStepId.GitNexusAnalyze] = new InitStepState(
                    InitStepId.GitNexusAnalyze, "GitNexus 代码分析", InitStepStatus.Pending),
            };
        }

        /// <summary>
        /// Get a snapshot of all step states.
        /// </summary>
        public IReadOnlyList<InitStepState> GetSteps()
        {
            lock (_lock)
            {
                return _steps.Values.ToList();
            }
        }

        /// <summary>
        /// Update a step's state and fire StepChanged event.
        /// </summary>
        public void UpdateStep(InitStepId id, InitStepStatus status,
            string message = null, double? progress = null)
        {
            InitStepState newState;
            lock (_lock)
            {
                if (!_steps.TryGetValue(id, out var current)) return;
                newState = current.With(status: status,
                    statusMessage: message, progressPercent: progress);
                _steps[id] = newState;
            }

            StepChanged?.Invoke(this, new InitStepChangedEventArgs(newState));
            CheckCompletion();
        }

        /// <summary>
        /// Start initialization sequence. Called by SolutionEventListener.
        /// Cancels any previous run's background tasks first.
        /// </summary>
        public void Start()
        {
            _cts?.Cancel();   // Cancel previous run's background tasks
            _cts?.Dispose();

            _isRunning = true;
            _cts = new CancellationTokenSource();

            // Reset all steps to Pending
            lock (_lock)
            {
                var ids = _steps.Keys.ToList();
                foreach (var id in ids)
                {
                    _steps[id] = new InitStepState(
                        id, _steps[id].DisplayName, InitStepStatus.Pending);
                }
            }

            InitStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Cancel all running initialization (e.g., solution close).
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
            _isRunning = false;
        }

        public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

        private void CheckCompletion()
        {
            bool shouldComplete = false;
            IReadOnlyList<InitStepState> snapshot;

            lock (_lock)
            {
                snapshot = _steps.Values.ToList();
                var allDone = snapshot.All(s =>
                    s.Status == InitStepStatus.Completed ||
                    s.Status == InitStepStatus.Failed ||
                    s.Status == InitStepStatus.Skipped);

                if (allDone && _isRunning)
                {
                    _isRunning = false;  // 在 lock 内设置，防止 double-fire
                    shouldComplete = true;
                }
            }

            if (shouldComplete)
            {
                var allSucceeded = snapshot.All(s =>
                    s.Status == InitStepStatus.Completed ||
                    s.Status == InitStepStatus.Skipped);

                InitCompleted?.Invoke(this,
                    new InitCompletedEventArgs(allSucceeded, snapshot));
            }
        }
    }
}
```

---

## 五、SolutionEventListener 改造

### 修改 `AICA.VSIX/Events/SolutionEventListener.cs`

**变更概述:** 将 fire-and-forget 初始化改为通过 `InitializationManager` 协调。

```csharp
// 新增字段
private readonly InitializationManager _initManager = new InitializationManager();
public InitializationManager InitManager => _initManager;

// OnAfterOpenSolutionAsync 重写:
public async Task OnAfterOpenSolutionAsync(string solutionPath)
{
    // ... 现有的 disposed/path 检查 ...

    SolutionPath = solutionPath;
    _initManager.Start();

    try
    {
        var projectRoot = FindGitRoot(solutionPath) ?? solutionPath;
        ProjectRootPath = projectRoot;

        // Step 1: 规则目录初始化 (同步，快速)
        _initManager.UpdateStep(InitStepId.RulesInit, InitStepStatus.Running);
        var result = await _initializer.InitializeAsync(projectRoot);
        _initManager.UpdateStep(
            InitStepId.RulesInit,
            result.Success ? InitStepStatus.Completed : InitStepStatus.Failed,
            result.Success ? result.RulesPath : result.Error);

        // Step 2: 符号索引 (并行)
        var indexingTask = RunSymbolIndexingAsync(solutionPath);

        // Steps 3+4: GitNexus (并行于 Step 2)
        var gitNexusTask = RunGitNexusAsync(solutionPath);

        // 等待两个并行任务完成 — 不阻塞 UI 因为本方法已在后台线程
        await Task.WhenAll(indexingTask, gitNexusTask);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[AICA] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
    }
}

private async Task RunSymbolIndexingAsync(string solutionPath)
{
    _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Running);
    try
    {
        var indexer = new ProjectIndexer();
        var index = await Task.Run(() =>
            indexer.IndexDirectoryAsync(solutionPath, _initManager.Token));
        ProjectKnowledgeStore.Instance.SetIndex(index);
        _initManager.UpdateStep(
            InitStepId.SymbolIndexing,
            InitStepStatus.Completed,
            $"{index.FileCount} files, {index.Symbols.Count} symbols ({index.IndexDuration.TotalSeconds:F1}s)");
    }
    catch (OperationCanceledException)
    {
        _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Skipped, "Cancelled");
    }
    catch (Exception ex)
    {
        _initManager.UpdateStep(InitStepId.SymbolIndexing, InitStepStatus.Failed, ex.Message);
    }
}

private async Task RunGitNexusAsync(string solutionPath)
{
    // Step 3: npm install (inside ResolveGitNexusPath)
    _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Running);
    try
    {
        await Task.Run(async () =>
        {
            await GitNexusProcessManager.Instance.TriggerIndexWithProgressAsync(
                solutionPath, _initManager, _initManager.Token);
        });
    }
    catch (OperationCanceledException)
    {
        _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Skipped, "Cancelled");
        _initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Cancelled");
    }
    catch (Exception ex)
    {
        _initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Failed, ex.Message);
        _initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Install failed");
    }
}
```

### OnAfterCloseSolution 变更

```csharp
public int OnAfterCloseSolution(object pUnkReserved)
{
    _initManager.Cancel();  // 取消正在运行的初始化
    SolutionPath = null;
    ProjectRootPath = null;
    ProjectKnowledgeStore.Instance.Clear();
    return Microsoft.VisualStudio.VSConstants.S_OK;
}
```

---

## 六、GitNexusProcessManager 改造

### 修改 `AICA.Core/Agent/GitNexusProcessManager.cs`

**变更 1: npm install 可见性** — 第 83 行 `CreateNoWindow = true` 改为 `false`

```csharp
// 第 75-84 行: npm install ProcessStartInfo
var npmPsi = new ProcessStartInfo
{
    FileName = npmCmd,
    Arguments = npmArgs,
    WorkingDirectory = gitnexusDir,
    UseShellExecute = false,
    RedirectStandardOutput = false,
    RedirectStandardError = false,
    CreateNoWindow = false  // 改为 false: 让用户看到 npm install 进度
};
```

**变更 2: 新增 `TriggerIndexWithProgressAsync` 方法** — 支持进度报告

```csharp
/// <summary>
/// TriggerIndexAsync with progress reporting to InitializationManager.
/// Splits npm install and analyze into separate reportable steps.
/// </summary>
public async Task TriggerIndexWithProgressAsync(
    string solutionDirectory,
    InitializationManager initManager,
    CancellationToken ct)
{
    if (string.IsNullOrEmpty(solutionDirectory)) return;

    var repoRoot = FindGitRoot(solutionDirectory);
    if (repoRoot == null)
    {
        initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Skipped, "No .git directory");
        initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "No .git directory");
        return;
    }

    try
    {
        // ResolveGitNexusPath may trigger npm install synchronously
        // The progress is already reported by the caller setting Running status
        var (cmd, _, analyzeArgs) = ResolveGitNexusPath();

        // npm install finished (it's synchronous inside ResolveGitNexusPath)
        initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Completed);

        // Step 4: analyze
        initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Running);

        var repoName = Path.GetFileName(repoRoot);
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c title [AICA] GitNexus Indexing: {repoName} & echo. & echo   [AICA] 正在索引代码库... & echo   Repository: {repoRoot} & echo. & {cmd} {analyzeArgs} \"{repoRoot}\" & echo. & echo   [AICA] 索引完成! & timeout /t 3 /nobreak >nul",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
        {
            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (s, e) =>
            {
                try { exitTcs.TrySetResult(process.ExitCode); }
                catch { exitTcs.TrySetResult(-1); }
            };

            process.Start();

            using (ct.Register(() =>
            {
                exitTcs.TrySetCanceled();
                try { if (!process.HasExited) process.Kill(); } catch { }
            }))
            {
                var exitCode = await exitTcs.Task.ConfigureAwait(false);
                initManager.UpdateStep(
                    InitStepId.GitNexusAnalyze,
                    exitCode == 0 ? InitStepStatus.Completed : InitStepStatus.Failed,
                    exitCode == 0 ? "Indexing complete" : $"Exit code: {exitCode}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Cancelled");
        throw;
    }
    catch (Exception ex)
    {
        // Determine which step failed based on current state
        var steps = initManager.GetSteps();
        var installStep = steps.FirstOrDefault(s => s.Id == InitStepId.GitNexusInstall);
        if (installStep?.Status == InitStepStatus.Running)
        {
            initManager.UpdateStep(InitStepId.GitNexusInstall, InitStepStatus.Failed, ex.Message);
            initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Skipped, "Install failed");
        }
        else
        {
            initManager.UpdateStep(InitStepId.GitNexusAnalyze, InitStepStatus.Failed, ex.Message);
        }
    }
}
```

**注意:** 原 `TriggerIndexAsync` 保留不变，用于非 InitGate 场景（如手动重新索引）。

---

## 七、进度 UI 设计

### 新建 `AICA.VSIX/ToolWindows/InitProgressOverlay.xaml` (~80行)

```xml
<UserControl x:Class="AICA.ToolWindows.InitProgressOverlay"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vsshell="clr-namespace:Microsoft.VisualStudio.Shell;assembly=Microsoft.VisualStudio.Shell.15.0"
             Background="{DynamicResource {x:Static vsshell:VsBrushes.ToolWindowBackgroundKey}}"
             Foreground="{DynamicResource {x:Static vsshell:VsBrushes.ToolWindowTextKey}}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="20" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="20" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" Text="AICA 正在初始化..."
                   FontSize="16" FontWeight="SemiBold"
                   HorizontalAlignment="Center" />

        <!-- Overall Progress Bar -->
        <ProgressBar x:Name="OverallProgress" Grid.Row="2"
                     Height="8" Minimum="0" Maximum="100"
                     Foreground="#0078D4" />

        <!-- Step List -->
        <ItemsControl x:Name="StepList" Grid.Row="4">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="24" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <!-- Status Icon -->
                        <TextBlock Grid.Column="0" Text="{Binding StatusIcon}"
                                   FontFamily="Segoe UI, Segoe UI Symbol, Arial Unicode MS"
                                   FontSize="14" VerticalAlignment="Center" />

                        <!-- Step Name -->
                        <TextBlock Grid.Column="1" Text="{Binding DisplayName}"
                                   FontSize="13" VerticalAlignment="Center"
                                   Foreground="{DynamicResource {x:Static vsshell:VsBrushes.ToolWindowTextKey}}" />

                        <!-- Status Message -->
                        <TextBlock Grid.Column="2" Text="{Binding StatusMessage}"
                                   FontSize="11" VerticalAlignment="Center"
                                   Foreground="{DynamicResource {x:Static vsshell:VsBrushes.GrayTextKey}}"
                                   Margin="8,0,0,0" />
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- Bottom hint -->
        <TextBlock x:Name="HintText" Grid.Row="6"
                   Text="初始化完成后即可开始对话"
                   FontSize="11" HorizontalAlignment="Center"
                   Foreground="{DynamicResource {x:Static vsshell:VsBrushes.GrayTextKey}}" />
    </Grid>
</UserControl>
```

### 新建 `AICA.VSIX/ToolWindows/InitProgressOverlay.xaml.cs` (~120行)

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using AICA.Core.Initialization;
using Microsoft.VisualStudio.Shell;

namespace AICA.ToolWindows
{
    /// <summary>
    /// ViewModel for a single init step in the progress overlay.
    /// </summary>
    public class InitStepViewModel
    {
        public string DisplayName { get; }
        public string StatusIcon { get; }
        public string StatusMessage { get; }

        public InitStepViewModel(InitStepState state)
        {
            DisplayName = state.DisplayName;
            StatusMessage = state.StatusMessage ?? "";
            switch (state.Status)
            {
                case InitStepStatus.Pending:   StatusIcon = "○"; break;
                case InitStepStatus.Running:   StatusIcon = "◌"; break;
                case InitStepStatus.Completed: StatusIcon = "✓"; break;
                case InitStepStatus.Failed:    StatusIcon = "⚠"; break;
                case InitStepStatus.Skipped:   StatusIcon = "—"; break;
                default:                       StatusIcon = "?"; break;
            }
        }
    }

    public partial class InitProgressOverlay : UserControl
    {
        private InitializationManager _manager;

        public InitProgressOverlay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Bind to an InitializationManager and start tracking progress.
        /// Must be called on UI thread.
        /// </summary>
        public void Bind(InitializationManager manager)
        {
            // Unbind previous
            if (_manager != null)
            {
                _manager.StepChanged -= OnStepChanged;
                _manager.InitCompleted -= OnInitCompleted;
            }

            _manager = manager;
            _manager.StepChanged += OnStepChanged;
            _manager.InitCompleted += OnInitCompleted;

            // Show initial state
            RefreshUI();
        }

        public void Unbind()
        {
            if (_manager != null)
            {
                _manager.StepChanged -= OnStepChanged;
                _manager.InitCompleted -= OnInitCompleted;
                _manager = null;
            }
        }

        private void OnStepChanged(object sender, InitStepChangedEventArgs e)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(new Action(RefreshUI));
        }

        private void OnInitCompleted(object sender, InitCompletedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshUI();
                HintText.Text = e.AllSucceeded
                    ? "初始化完成!"
                    : "初始化完成 (部分步骤失败，不影响基本功能)";
            }));
        }

        private void RefreshUI()
        {
            if (_manager == null) return;

            var steps = _manager.GetSteps();
            StepList.ItemsSource = steps.Select(s => new InitStepViewModel(s)).ToList();

            // Calculate overall progress: completed/total * 100
            var total = steps.Count;
            var done = steps.Count(s =>
                s.Status == InitStepStatus.Completed ||
                s.Status == InitStepStatus.Failed ||
                s.Status == InitStepStatus.Skipped);

            if (done < total && steps.Any(s => s.Status == InitStepStatus.Running))
            {
                OverallProgress.IsIndeterminate = true;
            }
            else
            {
                OverallProgress.IsIndeterminate = false;
                OverallProgress.Value = total > 0 ? (done * 100.0 / total) : 0;
            }
        }
    }
}
```

---

## 八、ChatToolWindowControl 集成

### 修改 `AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml`

在 Row 2 (ChatBrowser) 位置添加 InitProgressOverlay 覆盖层:

```xml
<!-- 在 ChatBrowser 之后、同一 Grid.Row="2" 添加 -->
<local:InitProgressOverlay x:Name="InitOverlay" Grid.Row="2"
                            Visibility="Collapsed" />
```

需要添加 XAML namespace:
```xml
xmlns:local="clr-namespace:AICA.ToolWindows"
```

### 修改 `AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`

**新增字段:**
```csharp
private InitializationManager _initManager;
```

**修改 `OnSolutionOpened()`:**
```csharp
private void OnSolutionOpened()
{
    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // Save current conversation before switching
        await SaveConversationAsync();
        ClearConversation();
        _lastProjectPath = GetCurrentProjectPath();

        // Bind to InitializationManager from SolutionEventListener
        BindToInitManager();

        if (_isSidebarOpen)
        {
            await LoadConversationListAsync();
        }
    }).FireAndForget();
}

private void BindToInitManager()
{
    var initManager = AICAPackage.CurrentInitManager;
    if (initManager == null) return;

    _initManager = initManager;

    if (initManager.IsRunning)
    {
        // Show progress overlay, hide chat + input
        InitOverlay.Visibility = Visibility.Visible;
        ChatBrowser.Visibility = Visibility.Collapsed;
        InputTextBox.IsEnabled = false;
        SendButton.IsEnabled = false;

        InitOverlay.Bind(initManager);
        initManager.InitCompleted += OnInitManagerCompleted;
    }
}

private void OnInitManagerCompleted(object sender, InitCompletedEventArgs e)
{
    Dispatcher.BeginInvoke(new Action(() =>
    {
        // Short delay so user can see the final state
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        timer.Tick += (s, args) =>
        {
            timer.Stop();
            TransitionToChat();
        };
        timer.Start();
    }));
}

private void TransitionToChat()
{
    InitOverlay.Unbind();
    InitOverlay.Visibility = Visibility.Collapsed;
    ChatBrowser.Visibility = Visibility.Visible;
    InputTextBox.IsEnabled = true;
    SendButton.IsEnabled = true;
    InputTextBox.Focus();

    if (_initManager != null)
    {
        _initManager.InitCompleted -= OnInitManagerCompleted;
    }
}
```

**修改 `ChatToolWindowControl_Loaded`:**
```csharp
private void ChatToolWindowControl_Loaded(object sender, RoutedEventArgs e)
{
    // Subscribe to InitStarted event — eliminates DTE vs IVsSolutionEvents timing race
    // InitStarted fires from InitializationManager.Start(), regardless of DTE event order
    var initManager = AICAPackage.CurrentInitManager;
    if (initManager != null)
    {
        initManager.InitStarted += OnInitStarted;
        // Also check if already running (user opened window mid-init)
        if (initManager.IsRunning)
            BindToInitManager();
    }

    // ... 现有的 path mismatch check ...
}

private void OnInitStarted(object sender, EventArgs e)
{
    Dispatcher.BeginInvoke(new Action(BindToInitManager));
}
```

**修改 `OnSolutionClosed()`:**
```csharp
private void OnSolutionClosed()
{
    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // Cancel init if running
        TransitionToChat();  // 隐藏进度 overlay

        await SaveConversationAsync();
        ClearConversation();
        _lastProjectPath = null;
        // ... 现有代码 ...
    }).FireAndForget();
}
```

**修改 `SendMessageAsync()` — 添加初始化门控:**
```csharp
private async System.Threading.Tasks.Task SendMessageAsync()
{
    var userMessage = InputTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(userMessage)) return;
    if (_isSending) return;

    // 初始化门控: 如果初始化还在运行，阻止发送
    if (_initManager?.IsRunning == true)
    {
        AppendMessage("assistant", "⏳ 正在初始化中，请等待初始化完成后再发送消息...");
        return;
    }

    // ... 现有的 SendMessageAsync 逻辑 ...
}
```

---

## 九、AICAPackage — 暴露 InitializationManager

### 修改 `AICA.VSIX/AICAPackage.cs`

```csharp
// 新增静态引用
private static AICAPackage _instance;

internal static InitializationManager CurrentInitManager =>
    _instance?._solutionEventListener?.InitManager;

protected override async Task InitializeAsync(...)
{
    _instance = this;
    // ... 现有代码 ...
}
```

---

## 十、状态机

```
          ┌──────────────────────────────────────────────┐
          │            ChatToolWindowControl              │
          │                                              │
          │  ┌──────────┐  init starts  ┌────────────┐  │
          │  │  Normal   │─────────────►│  Progress   │  │
          │  │  (Chat)   │◄─────────────│  (Overlay)  │  │
          │  │           │  init done   │             │  │
          │  │ Visible:  │  (1.5s delay)│ Visible:    │  │
          │  │ Browser ✓ │              │ Overlay ✓   │  │
          │  │ Input ✓   │              │ Browser ✗   │  │
          │  │ Overlay ✗ │              │ Input ✗     │  │
          │  └──────────┘              └────────────┘  │
          │       ▲                         │           │
          │       │    solution close        │           │
          │       └─────────────────────────┘           │
          └──────────────────────────────────────────────┘
```

### 转换触发条件

| 转换 | 触发 | 动作 |
|------|------|------|
| Normal → Progress | `OnSolutionOpened` + `InitManager.IsRunning` | 显示 Overlay, 隐藏 Browser, 禁用 Input |
| Progress → Normal | `InitCompleted` event + 1.5s delay | 隐藏 Overlay, 显示 Browser, 启用 Input |
| Progress → Normal | `OnSolutionClosed` | 立即隐藏 Overlay, 取消 InitManager |
| (窗口打开时) | `Loaded` + `InitManager.IsRunning` | 直接显示 Progress |
| (窗口打开时) | `Loaded` + `!InitManager.IsRunning` | 直接显示 Normal |

---

## 十一、线程安全

| 组件 | 线程模型 | 安全机制 |
|------|----------|----------|
| `InitializationManager` | 多线程写入 (background tasks) | `lock(_lock)` 保护 `_steps` 字典 |
| `InitializationManager.Events` | 从 background 线程触发 | 订阅者负责 marshal 到 UI 线程 |
| `InitProgressOverlay` | UI 线程操作 | `Dispatcher.BeginInvoke` marshal |
| `ChatToolWindowControl` | UI 线程操作 | `ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync` |
| `GitNexusProcessManager` | background 线程 | 现有 `lock(_lock)` 保护 |

**关键原则:** `InitializationManager` 的事件可能从任意线程触发。所有 UI 更新必须通过 `Dispatcher.BeginInvoke` 或 `SwitchToMainThreadAsync` 回到 UI 线程。

---

## 十二、边缘情况

### 1. 解决方案关闭/重开
- `OnAfterCloseSolution` 调用 `InitManager.Cancel()`，取消所有运行中的步骤
- `ChatToolWindowControl.OnSolutionClosed` 立即调用 `TransitionToChat()` 隐藏 overlay
- 新解决方案打开时，`InitializationManager.Start()` 重置所有步骤为 Pending

### 2. 无解决方案打开
- `InitializationManager` 不会被 `Start()`，`IsRunning = false`
- ChatToolWindowControl 正常显示聊天界面
- 用户可以发送消息（工具链不完整但不阻塞）

### 3. GitNexus 未安装 / Node.js 不可用
- `ResolveGitNexusPath()` 找不到 bundled 版本时 fallback 到 npx
- 如果 npx 也失败，`TriggerIndexWithProgressAsync` 捕获异常
- GitNexusInstall 和 GitNexusAnalyze 标记为 Failed
- 其他步骤不受影响，InitCompleted 仍然触发（AllSucceeded = false）
- 用户看到警告但可以正常使用（降级为无 GitNexus 模式）

### 4. 用户中途打开 AICA 窗口
- `ChatToolWindowControl_Loaded` 检查 `InitManager.IsRunning`
- 如果正在初始化，显示 Progress overlay 并 Bind 到当前状态
- 如果已完成，直接显示 Normal chat

### 5. 快速切换解决方案
- Cancel 前一次 init → Start 新 init → 前一次的 background tasks 收到 CancellationToken
- Steps 标记为 Skipped，CheckCompletion 触发 InitCompleted
- 新 init 立即开始，UI 切换到新的 Progress overlay

### 6. InitProgressOverlay 性能
- `StepChanged` 事件在每个步骤更新时触发（最多 ~6 次）
- `RefreshUI()` 每次创建新的 ViewModel 列表（4 个元素），性能可忽略
- 不需要虚拟化或增量更新

---

## 十三、改动清单

| # | 文件 | 类型 | 行数 |
|---|------|------|------|
| 1 | `Core/Initialization/InitStep.cs` | 新建 | ~60 |
| 2 | `Core/Initialization/InitializationManager.cs` | 新建 | ~200 |
| 3 | `VSIX/ToolWindows/InitProgressOverlay.xaml` | 新建 | ~80 |
| 4 | `VSIX/ToolWindows/InitProgressOverlay.xaml.cs` | 新建 | ~120 |
| 5 | `VSIX/Events/SolutionEventListener.cs` | 修改 | ~80 (重写 OnAfterOpenSolutionAsync) |
| 6 | `Core/Agent/GitNexusProcessManager.cs` | 修改 | ~60 (新增 TriggerIndexWithProgressAsync + npm CreateNoWindow) |
| 7 | `VSIX/ToolWindows/ChatToolWindowControl.xaml` | 修改 | ~5 (添加 InitOverlay) |
| 8 | `VSIX/ToolWindows/ChatToolWindowControl.xaml.cs` | 修改 | ~80 (BindToInitManager, TransitionToChat, gate) |
| 9 | `VSIX/AICAPackage.cs` | 修改 | ~10 (暴露 CurrentInitManager) |
| 10-11 | 测试 (InitializationManagerTests, InitStepTests) | 新建 | ~150 |
| **合计** | | | **~845** |

---

## 十四、分阶段

### Phase A: Core 基础设施 (文件 1-2, 10-11)
- InitStep 数据模型
- InitializationManager 协调器
- 单元测试
- 验收: 测试通过，状态机转换正确

### Phase B: GitNexus 改造 (文件 6)
- npm install `CreateNoWindow = false`
- 新增 `TriggerIndexWithProgressAsync`
- 验收: npm install 弹出可见窗口，进度可通过 DebugView 验证

### Phase C: UI 集成 (文件 3-4, 7-9)
- InitProgressOverlay WPF 控件
- ChatToolWindowControl 集成
- AICAPackage 暴露 InitManager
- 验收: 打开解决方案 → 看到进度步骤清单 → 完成后自动切换到聊天界面

### Phase D: SolutionEventListener 改造 (文件 5)
- 将 fire-and-forget 改为 InitializationManager 驱动
- 验收: 完整 E2E — 解决方案打开 → 4 步进度 → 自动切换 → 可发送消息

---

## 十五、风险与缓解

| 风险 | 严重度 | 缓解 |
|------|--------|------|
| npm install 从 CreateNoWindow=true 改为 false 弹出窗口可能干扰用户 | 中 | 与 analyze 窗口行为一致，用户已习惯; 窗口有 title 标识 |
| SolutionEventListener 从 fire-and-forget 改为 await 可能影响初始化时序 | 中 | Steps 2+3+4 仍然并行（Task.WhenAll），只是不再丢弃 Task |
| InitializationManager.CheckCompletion 竞态: 多个线程同时更新导致重复触发 | 低 | `_isRunning` 使用 volatile + 在 CheckCompletion 中用 lock 保护 |
| AICAPackage 静态引用: VS 可能在某些情况下延迟加载 Package | 低 | `GetInitializationManager()` null-safe，返回 null 时不显示 overlay |

---

## 十六、已知限制

- 符号索引进度是 indeterminate（无百分比）—— ProjectIndexer 没有总文件数的预估，改造代价大
- GitNexus npm install 进度是 indeterminate —— npm 输出未重定向（窗口可见即可）
- 第一次初始化（npm install ~104s）后续重开很快（无需 npm install），进度条几秒即过
- 不支持取消单个步骤，只能取消全部（解决方案关闭时）

---

## 十七、评审记录

> 评审方式: planner agent 设计 + reviewer agent 对照源码独立评审 + planner 自审
> 评审轮次: 1 轮即达成一致
> 日期: 2026-04-02

### 发现与修复

| # | 来源 | 严重度 | 问题 | 修复 |
|---|------|--------|------|------|
| 1 | Reviewer | HIGH | `CheckCompletion` 竞态: `_isRunning=false` 在 lock 外，可能 double-fire | 移入 lock 内，用 `shouldComplete` flag 在 lock 外触发事件 |
| 2 | Reviewer | HIGH | `Start()` 未 cancel 前一次 CTS，快速切换解决方案会泄漏后台任务 | `Start()` 开头加 `_cts?.Cancel(); _cts?.Dispose()` |
| 3 | Reviewer | MEDIUM | `TriggerIndexWithProgressAsync` 对 `InitializationManager` 的耦合 | 保持 intra-project 耦合，添加文档注释说明 |
| 4 | Reviewer | MEDIUM | `ProgressBar.Value` 在 `IsIndeterminate=true` 时赋值无效 | 条件分支: indeterminate 时不设 Value |
| 5 | Reviewer | MEDIUM | `AICAPackage._instance` 可能为 null（VS 延迟加载） | null-safe + 文档注释说明降级行为 |
| 6 | Reviewer | LOW | Unicode 图标 (✓/⚠/○) 可能在某些字体下不显示 | 添加 `FontFamily="Segoe UI, Segoe UI Symbol, Arial Unicode MS"` |
| 7 | Reviewer | LOW | `IndexDirectoryAsync` 当前不接受 `CancellationToken` | 确认为有意改进，增量修改 |
| 8 | Planner 自审 | MEDIUM | DTE `SolutionEvents.Opened` 与 `IVsSolutionEvents` 竞态: overlay 可能不显示 | 新增 `InitStarted` 事件，`ChatToolWindowControl_Loaded` 订阅，消除 DTE 时序依赖 |

**结论:** 8 项修复全部已合并到本文档 v1.1 版本。设计可进入实施阶段。
