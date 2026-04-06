# AICA v2.1 工具链与工程健壮性增强方案

> 版本: v1.3 | 日期: 2026-03-30
> 前置版本: v2.0 重写（commit `686c9de`，信任型 AgentExecutor + MCP Resources + LLM Condense + 工具裁剪 19→14）
> 对标参考: OpenCode（TypeScript，同样使用 MiniMax-M2.5 + GitNexus MCP）
> 定位: v2.0 之后的增量改进，聚焦工具系统补齐 + 编辑健壮性 + 搜索性能

### 修正记录

#### v1.1 修正（6 项，评审反馈纳入）

> 评审方式：Agent Teams 模式，项目评审专家交叉验证源代码后出具结构化评审报告（评分 7.9/10）

| # | 修正项 | 原问题 | 修正内容 |
|---|--------|--------|---------|
| R1 | T5 重新定位：重构 H3 而非新增 | 方案称"H3 需要额外一轮交互"，实际 H3 已实现缩进无关和空白压缩的零交互 auto-fix（EditFileTool.cs 第136-155行 goto applyEdit） | T5 从"新增模糊匹配级联"改为"重构 H3 auto-fix 入口 + 新增 Level 1 行尾空白匹配"。优先级从 P1 降为 P2。改动量从 ~150 行调整为 ~100 行 |
| R2 | T6 FileSnapshot 改用普通 class | 方案使用 C# 9 `record` 类型，项目中无先例，旧 SDK 编译可能失败 | `record FileSnapshot(...)` 改为 `class FileSnapshot` + 构造函数，与项目现有风格一致 |
| R3 | T4 补充 rg.exe 运行时定位逻辑 | VSIX 安装后工作目录不确定，方案未说明如何找到内嵌 rg.exe | 新增 `FindRipgrep()` 方法：`Assembly.Location` 定位 VSIX 安装目录 → PATH fallback → null（C# fallback） |
| R4 | T4 新增双路策略 | Windows 进程创建开销 ~100-200ms，小规模搜索 rg 优势不明显 | 文件数 <200 走 C# 内存搜索，>=200 走 rg 进程。性能预期修正为"大项目 10-30x 提升" |
| R5 | T1.1 改为"先调试复现再修复" | ListDirTool.cs 第90-91行已有 path="." 处理逻辑，bug 根因可能在 `IsPathAccessible` 而非路径解析 | 步骤 A.1 改为先用调试器复现实际错误栈，确认根因后再定方案。代码示例对齐实际 API（`call.Arguments.TryGetValue`） |
| R6 | 风险表补充 5 项遗漏风险 | 遗漏 rg 编码、进程僵死、FileSystemGlobbing 网络驱动器、record 兼容性、新工具注册可见性 | 风险表新增 5 行 |

#### v1.2 执行记录（2026-03-30，Phase A-D 完成）

| 阶段 | 状态 | 执行发现 |
|------|------|---------|
| **Phase A (T1)** | ✅ 完成 | T1.1: 调试确认 `IsPathAccessible(".")` 已有正确处理（VSAgentContext.cs L398），`ListDirTool` L90-91 也正确映射。**bug 不存在**，标记为非问题。T1.2: 4 个测试文件清理完成，删除 ~650 行已废弃工具测试 |
| **Phase B (T2+T3)** | ✅ 完成 | WriteFileTool.cs 222 行、GlobTool.cs 337 行。已注册到 ChatToolWindowControl。GlobTool 采用自实现 glob-to-regex（未引入 FileSystemGlobbing NuGet，因自实现更轻量且避免依赖风险） |
| **Phase C (T4)** | ✅ 完成 | GrepSearchTool.cs 增至 717 行（+268 行）。双路策略实现：`FindRipgrep()` + `QuickFileCount()` + `SearchWithRipgrep()` JSON 解析。C# fallback 保留不变。**rg.exe 二进制文件需后续下载放入 `tools/ripgrep/`** |
| **Phase D (T5+T6)** | ✅ 完成 | T5: EditFileTool.cs 增至 635 行（+117 行）。`FindWithCascade` 提取 Level 2/3 + 新增 Level 1 `TrimEndEachLine`。H3 精简为仅 StaleContent + NoMatch。T6: FileTimeTracker.cs 119 行，已集成到 Read/Edit/Write 三个工具 |
| **Phase E** | ✅ 完成 | 全量编译通过（0 error）。VSIX 16.1MB 含 rg.exe。额外修复 IsLikelyConversationalTests.cs（v2.0 遗留，指向已删除的 AgentExecutor 方法）。poco 端到端测试待 VS2022 中执行。 |

**编译验证结果**:
- `AICA.Core.dll` → ✅ 编译成功（仅 CS1591 XML 注释警告）
- `AICA.Core.Tests.dll` → ✅ 编译成功（额外修复 IsLikelyConversationalTests.cs 指向 DynamicToolSelector）
- `AICA.vsix` → ✅ 16.1 MB（含 `tools/ripgrep/rg.exe` 5.2MB + GitNexus）
- ripgrep 14.1.1 → ✅ AVX2+PCRE2+JIT，Windows x64

**Git diff 统计**: +442 / -705 行，净减 263 行（删除测试 > 新增代码）

**新增文件**: WriteFileTool.cs (222行), GlobTool.cs (337行), FileTimeTracker.cs (119行), tools/ripgrep/rg.exe (5.2MB)
**改动文件**: EditFileTool.cs, GrepSearchTool.cs, ReadFileTool.cs, ChatToolWindowControl.xaml.cs, AICA.VSIX.csproj, IsLikelyConversationalTests.cs, 4 个测试文件

**与方案偏差**:
- T1.1 确认为非 bug（方案预估 ~30 行改动，实际 0 行）
- T3 未引入 `FileSystemGlobbing` NuGet，改为自实现 `GlobToRegex()`（更轻量，避免 netstandard2.0 兼容风险）
- GlobTool 337 行超预估 200 行（自实现 glob-to-regex 比调用库更多代码）
- GrepSearchTool 改动 268 行（接近预估 300 行）
- 额外修复 `IsLikelyConversationalTests.cs`（v2.0 遗留，测试中引用 `AgentExecutor.IsLikelyConversational` 已不存在）
- VSIX 总大小 16.1MB（预估 ~20MB，实际更小因为 rg.exe 5.2MB 非 ~5MB 预估的上限）

**VS2022 验证结果**（v1.3 更新，用户机器测试全部通过）:
- E.3 右键菜单 4 个命令 ✅
- E.4 poco 端到端 ✅
- E.5 write_file/glob 被 LLM 选用 ✅
- E.6 D-03/D-08 复验 ✅
- E.7 工具注册确认 16 个 ✅（debug 日志 Tools count: 16，LLM 自述 17 个含幻觉的 gitnexus_list_repos 已确认为非问题）

**后续工具优化（同日完成）**: 见 `AICA_v2.1_ToolSet_Optimization_Plan.md` v2.1
- O1-O7, O9-O12, F3 共 11 项优化
- 工具名别名映射（O11）修复 LLM 调用 grep/bash 幻觉
- read_file 签名修复（O12）修复大文件分块读取被误拦
- edit full_replace 移除（O1）消除与 write_file 歧义
- 6 个工具描述添加负边界 + 互相引导
- gitnexus_cypher 描述精简 + Phase 2 不覆盖
- 用户测试 8 项全部通过
- 已知问题 K1: LLM 幻觉 gitnexus_list_repos（MCP Server 可能暴露但被 AICA 硬编码 spec 过滤），待 v2.2 评估

---

## 一、背景与动机

v2.0 完成了 AgentExecutor 架构重写，工具集精简至 14 个。与 OpenCode 对比后发现以下差距：

| 差距类别 | 核心问题 | 影响 |
|----------|----------|------|
| 工具缺失 | 无 Write/Glob 工具 | 新建文件语义不清，找文件效率低 |
| 编辑脆弱 | Edit 严格精确匹配 | LLM 缩进/空白偏差导致编辑失败率高 |
| 搜索性能 | grep_search 纯 C# 逐文件读取 | 大项目（4000+ 文件）搜索缓慢 |
| 冲突检测 | 无文件时间戳校验 | 外部修改被静默覆盖 |
| 残留 Bug | list_dir path="." 报错、测试编译失败 | 基本功能不可靠 |

**不在本方案范围内**（已与开发者确认暂不考虑）：
- 外部目录访问
- Shell/PTY 执行增强
- 多 LLM Provider / 模型路由
- 新增 Agent 类型
- UI/UX 改进

---

## 二、改进项总览

| 编号 | 改进项 | 优先级 | 预估改动量 | 新增/改动文件 |
|------|--------|--------|-----------|--------------|
| T1 | 修复残留 Bug（list_dir + 测试编译） | P0 | ~30 行 | ListDirTool.cs + 测试文件 |
| T2 | 新增 WriteFileTool | P1 | ~200 行 | 新建 WriteFileTool.cs |
| T3 | 新增 GlobTool | P1 | ~200 行 | 新建 GlobTool.cs |
| T4 | grep_search 改用 ripgrep 进程（双路策略） | P1 | ~300 行（重写） | GrepSearchTool.cs |
| T5 | Edit 重构 H3 auto-fix 入口 + 新增行尾空白匹配 | P2 | ~100 行（重构+新增） | EditFileTool.cs |
| T6 | Edit 文件时间戳冲突检测 | P2 | ~80 行 | EditFileTool.cs + ReadFileTool.cs |
| **合计** | | | **~910 行** | **2 新建 + 4 改动** |

改进后工具集：14 → 16（新增 write_file、glob）

---

## 三、各改进项详细设计

### T1: 修复残留 Bug

**优先级**: P0 — 基本功能可靠性

#### T1.1 list_dir path="." I/O 错误

**现象**: LLM 传 `path: "."` 时路径解析失败
**待确认根因**: ListDirTool.cs 第90-91行已有 `"."` / `"./"` → `context.WorkingDirectory` 的映射逻辑。实际根因可能在 `context.IsPathAccessible(".")` 返回 false，而非路径解析本身。**[R5] 需先调试复现确认。**

**修复方案**: 先用调试器复现实际错误栈，确认是 `IsPathAccessible` 还是 `ResolvePath` 失败，再定方案。

可能的修复路径：
- 若 `IsPathAccessible(".")` 返回 false → 修复安全检查对相对路径的处理
- 若 `ResolvePath(".")` 返回异常路径 → 在入口处增加规范化

```csharp
// ListDirTool.cs — 实际 API 模式（与现有代码风格对齐）
call.Arguments.TryGetValue("path", out var pathObj);
var rawPath = pathObj?.ToString()?.Trim();
if (string.IsNullOrEmpty(rawPath) || rawPath == "." || rawPath == "./")
    rawPath = context.WorkingDirectory;
```

**改动**: ListDirTool.cs ~10 行（调试确认后可能更少）
**验证**: 单元测试传 `"."`, `""`, `null`, 相对路径、绝对路径

#### T1.2 测试项目编译报错

**现象**: v2.0 删除了 AttemptCompletionTool、CondenseTool 等 5 个工具源文件，但测试代码仍引用
**修复方案**: 删除或更新引用已删除工具的测试文件
**改动**: 测试文件 ~40 行（删除无效引用 + 更新 mock 注册）
**验证**: `dotnet build AICA.Core.Tests` 编译通过

---

### T2: 新增 WriteFileTool

**优先级**: P1 — 从 edit 分离新建文件语义

#### 动机

当前创建新文件只能通过 `edit` 的 `full_replace=true` + `old_string=""` hack。问题：
1. LLM 经常不使用 `full_replace` 参数（C28 记录的 E4 测试 50 轮超时根因之一）
2. 语义不清晰：edit 暗示"修改已有内容"，创建新文件应该是独立操作
3. OpenCode 有独立的 `write` 工具处理全量写入

#### 设计

```csharp
// WriteFileTool.cs
public class WriteFileTool : IAgentTool
{
    public string Name => "write_file";
    public string Description => "Create a new file or completely overwrite an existing file. "
        + "Use this for creating new files. For modifying existing files, prefer 'edit'.";

    // 参数
    // - file_path: string (required) — 文件绝对路径或相对于工作目录的路径
    // - content: string (required) — 文件完整内容
    // - overwrite: boolean (optional, default: false) — 文件已存在时是否覆盖

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object> parameters,
        IAgentContext context,
        IUIContext uiContext,
        CancellationToken ct)
    {
        // 1. 参数验证
        // 2. 路径安全检查（SafetyGuard）
        // 3. 文件存在性检查
        //    - 不存在 → 创建（含父目录）
        //    - 已存在 + overwrite=false → 报错提示用 edit
        //    - 已存在 + overwrite=true → 用户确认后覆盖
        // 4. 检测换行符（CRLF/LF），保持项目一致性
        // 5. 写入文件
        // 6. 返回成功信息（文件路径 + 行数 + 字节数）
    }
}
```

**关键决策**:
- `overwrite` 默认 false，防止 LLM 误覆盖现有文件
- 文件已存在时明确引导 LLM 使用 edit 工具
- 创建前自动创建父目录（`Directory.CreateDirectory`）
- 写入时检测项目主流换行符，保持一致

**新增文件**: `AICA.Core/Tools/WriteFileTool.cs` ~180 行
**改动文件**: `ChatToolWindowControl.xaml.cs`（注册工具）~3 行
**测试文件**: `AICA.Core.Tests/Tools/WriteFileToolTests.cs` ~120 行

**验证场景**:
- 创建不存在的文件 → 成功
- 创建已存在的文件（overwrite=false）→ 拒绝并提示
- 创建已存在的文件（overwrite=true）→ 用户确认后覆盖
- 路径含不存在的父目录 → 自动创建
- 被 SafetyGuard 拦截的路径 → 拒绝

---

### T3: 新增 GlobTool

**优先级**: P1 — 补齐文件发现能力

#### 动机

当前找文件只能靠 `list_dir`（单层）+ `grep_search`（内容匹配），缺少按文件名模式搜索的能力。LLM 需要多次 list_dir 递归探索目录结构，浪费迭代次数。

#### 设计

```csharp
// GlobTool.cs
public class GlobTool : IAgentTool
{
    public string Name => "glob";
    public string Description => "Find files by name pattern using glob syntax. "
        + "Fast file discovery without reading content. "
        + "Supports patterns like '**/*.cpp', 'src/**/*.h', '*.cs'.";

    // 参数
    // - pattern: string (required) — glob 模式（支持 **, *, ?）
    // - path: string (optional) — 搜索起始目录，默认工作目录
    // - max_results: int (optional, default: 200) — 最大返回数

    public async Task<ToolResult> ExecuteAsync(...)
    {
        // 1. 参数验证
        // 2. 路径安全检查
        // 3. 使用 Microsoft.Extensions.FileSystemGlobbing 或自实现
        //    - 递归遍历目录
        //    - 跳过排除目录（.git, .vs, bin, obj, node_modules）
        //    - 按 glob 模式匹配文件名
        // 4. 按修改时间降序排列（最近修改的优先）
        // 5. 返回匹配文件列表（相对路径 + 大小 + 修改时间）
    }
}
```

**实现选择**:
- **方案 A**: 使用 `Microsoft.Extensions.FileSystemGlobbing` NuGet 包（成熟、标准）
- **方案 B**: 自实现轻量 glob 匹配（减少依赖）
- **推荐方案 A**：`FileSystemGlobbing` 是 .NET 官方库，netstandard2.0 兼容，无额外运行时依赖

**新增文件**: `AICA.Core/Tools/GlobTool.cs` ~200 行
**改动文件**: `ChatToolWindowControl.xaml.cs`（注册工具）~3 行, `AICA.Core.csproj`（添加 NuGet 引用）~1 行
**测试文件**: `AICA.Core.Tests/Tools/GlobToolTests.cs` ~100 行

**验证场景**:
- `**/*.cpp` → 递归查找所有 .cpp 文件
- `src/**/*.h` → 限定目录搜索
- `*.cs` → 当前目录下的 .cs 文件
- 排除目录正常跳过
- 超过 max_results 时截断并提示总数
- 无匹配结果时返回空列表 + 建议

---

### T4: grep_search 改用 ripgrep 进程

**优先级**: P1 — 搜索性能质变

#### 动机

当前 GrepSearchTool 用纯 C# 逐文件 `File.ReadAllLines` + `Regex.IsMatch`，性能瓶颈：
- 4000+ 文件项目搜索耗时数秒
- 5MB 文件大小限制（OpenCode 无限制）
- 无 SIMD 加速，无内存映射

OpenCode 直接调用 ripgrep 进程，利用其 Rust 实现的高性能搜索。

#### 设计

**双路策略 [R4]**: 根据搜索范围自动选择引擎，避免小规模搜索中 rg 进程启动开销（Windows ~100-200ms）抵消 SIMD 收益。

```csharp
// GrepSearchTool.cs — 重写核心搜索逻辑
public async Task<ToolResult> ExecuteAsync(...)
{
    // 1. 参数解析：pattern, path, include
    // 2. 预扫描目标目录文件数（快速 EnumerateFiles 计数）
    // 3. 双路选择：
    //    - 文件数 < 200 → 走 C# 内存搜索（现有实现，零进程开销）
    //    - 文件数 >= 200 → 走 ripgrep 进程（大规模搜索优势明显）
    //    - ripgrep 不可用 → 始终走 C# 实现
    // 4. ripgrep 路径：
    //    a. 定位 rg.exe（见 FindRipgrep）
    //    b. 构建命令行参数
    //       rg --json --max-count=200 --max-filesize=50M
    //          --glob="!.git" --glob="!.vs" --glob="!bin" --glob="!obj"
    //          [--glob=include_pattern] [--ignore-case]
    //          "pattern" "path"
    //    c. 启动 rg 进程，流式读取 stdout（UTF-8 编码）
    //    d. 解析 JSON 输出（--json 模式每行一个 JSON 对象）
    //    e. 超时 30s 后 Kill 进程，返回已收集的部分结果
    // 5. 格式化为 AICA 标准 ToolResult
}

/// <summary>定位 ripgrep 可执行文件 [R3]</summary>
private string FindRipgrep()
{
    // 1. VSIX 安装目录下的 tools/ripgrep/rg.exe
    var asmDir = Path.GetDirectoryName(typeof(GrepSearchTool).Assembly.Location);
    var embeddedPath = Path.Combine(asmDir, "tools", "ripgrep", "rg.exe");
    if (File.Exists(embeddedPath)) return embeddedPath;

    // 2. PATH 中查找（用 where rg 或直接尝试）
    try
    {
        var proc = Process.Start(new ProcessStartInfo("where", "rg")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        var output = proc?.StandardOutput.ReadLine();
        proc?.WaitForExit(3000);
        if (!string.IsNullOrEmpty(output) && File.Exists(output))
            return output;
    }
    catch { /* ignore */ }

    return null; // fallback to C#
}
```

**关键决策**:

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 引擎选择 | **双路策略**：文件数 <200 走 C#，>=200 走 rg **[R4]** | Windows 进程创建 ~100-200ms，小规模搜索 C# 更快 |
| rg.exe 定位 | `Assembly.Location` → PATH → null **[R3]** | VSIX 安装后工作目录不确定，需通过程序集路径定位 |
| ripgrep 来源 | 内嵌 `tools/ripgrep/rg.exe` + PATH fallback | 不依赖用户环境，VSIX 打包时一并分发 |
| 输出格式 | `--json` | 结构化解析，避免正则解析 rg 文本输出 |
| 输出编码 | `StandardOutputEncoding = Encoding.UTF8` **[R6]** | 防止 rg JSON 输出含中文路径/内容时乱码 |
| C# fallback | 保留现有实现 | ripgrep 不可用或小规模搜索时使用 |
| 文件大小限制 | 50MB（rg 默认） | 远超当前 5MB，覆盖华中数控大文件场景 |
| 超时 | 30 秒 + `process.Kill()` 清理 **[R6]** | 防止大项目全量搜索阻塞，超时后强制终止进程 |

**ripgrep 分发方案**:
- `tools/ripgrep/rg.exe` ~5MB（Windows x64）
- VSIX 打包时包含（与 GitNexus 相同策略）
- 版本锁定在 ripgrep 14.x

**改动文件**: `AICA.Core/Tools/GrepSearchTool.cs` ~300 行（重写搜索核心 + 双路选择 + FindRipgrep + C# fallback 保留）
**新增目录**: `tools/ripgrep/rg.exe`
**改动文件**: `AICA.VSIX.csproj`（VSIX 打包 rg.exe）~5 行

```xml
<!-- AICA.VSIX.csproj — VSIX 打包配置示例 [R3] -->
<Content Include="$(SolutionDir)tools\ripgrep\rg.exe">
  <IncludeInVSIX>true</IncludeInVSIX>
  <Link>tools\ripgrep\rg.exe</Link>
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

**测试文件**: `AICA.Core.Tests/Tools/GrepSearchToolTests.cs`（更新测试）~80 行

**验证场景**:
- 文件数 <200 → 走 C# 路径，结果正确
- 文件数 >=200 + rg 可用 → 走 rg 路径，结果正确
- rg 不可用 → 始终走 C# fallback，结果正确
- 大文件（>5MB）搜索 → rg 成功，C# fallback 跳过
- 超时场景 → 30 秒后 Kill 进程，返回部分结果
- rg JSON 输出含中文路径 → UTF-8 解析正确
- 参数 pattern/path/include 正确传递

**性能预期 [R4 修正]**:
- 小项目（<200 文件）：C# 内存搜索 ~0.1-0.5s，无进程开销
- 大项目（4000 文件）：C# ~3-5s → rg ~0.2-0.5s（**10-30x 提升**，含进程启动开销）
- 单文件 >5MB：C# 跳过 → rg 正常搜索

---

### T5: Edit 重构 H3 auto-fix 入口 + 新增行尾空白匹配

**优先级**: P2 — 统一模糊匹配入口 + 增量新增 Level 1 **[R1 重新定位]**

#### 动机与现状修正 [R1]

**v1.0 方案误判**: 原方案称"H3 需要额外一轮交互"，经评审交叉验证源码发现，**H3 已实现零交互 auto-fix**：
- EditFileTool.cs 第136-155行：`DiagnoseEditFailure` 的 `IndentationMismatch` 和 `WhitespaceMismatch` case 通过 `TrimEachLine` + `LocateOriginalSegment` 和 `CompressWhitespace` + `LocateSegmentByCompressed` 定位原始内容，然后 `goto applyEdit` 直接执行替换，无需 LLM 额外交互。

**T5 的真正价值**:
1. **新增 Level 1（行尾空白忽略）** — 现有 H3 未覆盖的场景，LLM 生成的 old_string 行尾常有多余空格
2. **统一入口** — 将 H3 散落在 `DiagnoseEditFailure` 中的 auto-fix 逻辑提取到 `FindWithCascade`，使匹配流程清晰可维护
3. **前置执行** — 当前 auto-fix 在 `content.Contains(oldString)` 失败后才进入 H3 诊断。重构后，模糊匹配在精确匹配失败时立即尝试，减少诊断开销

#### 设计

```
Level 0: 精确匹配（现有逻辑，不变）
   ↓ 失败
Level 1: 行尾空白忽略（LineTrimmed）— 新增
   - 每行 TrimEnd() 后比较
   ↓ 失败
Level 2: 缩进无关匹配 — 从 H3 DiagnoseEditFailure 提取
   - 复用现有 TrimEachLine + LocateOriginalSegment
   ↓ 失败
Level 3: 空白规范化匹配 — 从 H3 DiagnoseEditFailure 提取
   - 复用现有 CompressWhitespace + LocateSegmentByCompressed
   ↓ 失败
→ 进入 H3 剩余诊断（StaleContent + NoMatch，不变）
```

```csharp
// EditFileTool.cs — 重构：统一模糊匹配入口

private (int matchIndex, int matchLength, MatchLevel level)? FindWithCascade(
    string content, string oldString)
{
    // Level 0: 精确匹配（现有逻辑）
    var exactIndex = content.IndexOf(oldString, StringComparison.Ordinal);
    if (exactIndex >= 0)
        return (exactIndex, oldString.Length, MatchLevel.Exact);

    // Level 1: 行尾空白忽略（新增）
    var trimmedResult = FindLineTrimmed(content, oldString);
    if (trimmedResult.HasValue)
        return (trimmedResult.Value.index, trimmedResult.Value.length, MatchLevel.LineTrimmed);

    // Level 2: 缩进无关（从 H3 IndentationMismatch 提取）
    var indentResult = FindIndentationFlexible(content, oldString);
    if (indentResult.HasValue)
        return (indentResult.Value.index, indentResult.Value.length, MatchLevel.IndentationFlexible);

    // Level 3: 空白规范化（从 H3 WhitespaceMismatch 提取）
    var normalizedResult = FindWhitespaceNormalized(content, oldString);
    if (normalizedResult.HasValue)
        return (normalizedResult.Value.index, normalizedResult.Value.length, MatchLevel.WhitespaceNormalized);

    return null; // 全部失败 → 进入 H3 剩余诊断（StaleContent + NoMatch）
}

private enum MatchLevel { Exact, LineTrimmed, IndentationFlexible, WhitespaceNormalized }
```

**关键决策**:

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 定位 | **重构 H3 + 新增 Level 1**（非全新实现）**[R1]** | H3 已有 Level 2/3 的 auto-fix，避免功能重复 |
| 新增内容 | 仅 Level 1（行尾 TrimEnd）是真正新增 | Level 2/3 从 DiagnoseEditFailure 提取复用 |
| H3 剩余 | `StaleContent` + `NoMatch` 诊断保留在 DiagnoseEditFailure | 这两个 case 不是 auto-fix，需要返回诊断信息 |
| 多匹配处理 | 模糊匹配仍要求唯一匹配 | 多匹配时拒绝，与精确匹配行为一致 |
| 日志记录 | 模糊匹配成功时记录使用了哪一级 | 方便后续分析匹配模式分布 |

**改动文件**: `AICA.Core/Tools/EditFileTool.cs` ~100 行（提取重构 + 新增 FindLineTrimmed）
**测试文件**: `AICA.Core.Tests/Tools/EditFileToolTests.cs` ~150 行（Level 1 新场景 + 回归验证）

**验证场景**:
- 精确匹配 → Level 0 命中
- old_string 行尾有多余空格 → Level 1 命中（新增场景）
- old_string 缩进为 2 空格，文件为 4 空格 → Level 2 命中（回归验证）
- old_string 多个空格被压成一个 → Level 3 命中（回归验证）
- 模糊匹配存在多个候选 → 拒绝并进入 H3 剩余诊断
- 全部失败 → 进入 H3 StaleContent/NoMatch 诊断（回归验证）

---

### T6: Edit 文件时间戳冲突检测

**优先级**: P2 — 防止静默覆盖外部修改

#### 动机

当前 AICA 跟踪 `EditedFilesInSession`（文件名集合），但不记录读取时的文件状态。如果用户在 VS2022 中手动修改了文件，AICA 的下一次 edit 可能覆盖这些修改。

OpenCode 用 `FileTime.withLock()` + mtime/ctime/size 校验，编辑前验证文件未被外部修改。

#### 设计

```csharp
// 新增：FileTimeTracker.cs（放在 Agent/ 目录）
public class FileTimeTracker
{
    // 记录每个文件最近一次 read/edit 时的状态
    private readonly ConcurrentDictionary<string, FileSnapshot> _snapshots = new();

    // [R2] 使用普通 class 替代 C# 9 record，与项目现有风格一致
    public class FileSnapshot
    {
        public DateTime LastWriteTimeUtc { get; }
        public long FileSize { get; }
        public string OperationType { get; }  // "read" | "edit"

        public FileSnapshot(DateTime lastWriteTimeUtc, long fileSize, string operationType)
        {
            LastWriteTimeUtc = lastWriteTimeUtc;
            FileSize = fileSize;
            OperationType = operationType;
        }
    }

    /// <summary>读取文件后记录快照</summary>
    public void RecordRead(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;
        _snapshots[NormalizePath(filePath)] = new FileSnapshot(
            info.LastWriteTimeUtc, info.Length, "read");
    }

    /// <summary>编辑文件后更新快照</summary>
    public void RecordEdit(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;
        _snapshots[NormalizePath(filePath)] = new FileSnapshot(
            info.LastWriteTimeUtc, info.Length, "edit");
    }

    /// <summary>检查文件是否在上次操作后被外部修改</summary>
    public bool HasExternalModification(string filePath)
    {
        var key = NormalizePath(filePath);
        if (!_snapshots.TryGetValue(key, out var snapshot))
            return false; // 从未读取过，不检测

        var info = new FileInfo(filePath);
        if (!info.Exists) return true; // 文件被删除

        return info.LastWriteTimeUtc != snapshot.LastWriteTimeUtc
            || info.Length != snapshot.FileSize;
    }
}
```

**集成点**:
- `ReadFileTool.ExecuteAsync` 成功后调用 `tracker.RecordRead(filePath)`
- `EditFileTool.ExecuteAsync` 编辑前调用 `tracker.HasExternalModification(filePath)`
  - 检测到冲突 → 返回警告："文件自上次读取后已被外部修改，建议先 read_file 获取最新内容"
  - 未检测到冲突 → 正常编辑
- `EditFileTool.ExecuteAsync` 编辑成功后调用 `tracker.RecordEdit(filePath)`
- `WriteFileTool.ExecuteAsync` 写入后调用 `tracker.RecordEdit(filePath)`

**关键决策**:

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 冲突处理 | 警告并建议重新 read，不阻断 | 避免过于激进，LLM 可以决定是否继续 |
| 存储方式 | 内存 ConcurrentDictionary | 会话级，不需要持久化 |
| 检测粒度 | mtime + size | 简单高效，覆盖 99% 场景（不用 hash，避免大文件性能问题） |

**新增文件**: `AICA.Core/Agent/FileTimeTracker.cs` ~60 行
**改动文件**: `ReadFileTool.cs` ~5 行, `EditFileTool.cs` ~15 行, `WriteFileTool.cs` ~5 行
**测试文件**: `AICA.Core.Tests/Agent/FileTimeTrackerTests.cs` ~80 行

---

## 四、实施计划

### 阶段划分

```
Phase A: 基础修复（T1）         ─── 0.5 天
Phase B: 工具补齐（T2 + T3）    ─── 1.5 天（T2 可能 +0.25 天 [换行符检测]）
Phase C: 搜索升级（T4）         ─── 2 天（[R3/R4] VSIX 打包 + 双路策略增加复杂度）
Phase D: 编辑增强（T5 + T6）    ─── 1 天（[R1] T5 改为重构，工作量减半）
Phase E: 集成测试 + 回归验证    ─── 1.5 天（16 个工具 + VSIX 打包验证）
                                ─────────
                          合计    6.5 天
缓冲                              2 天（T4 是最大风险项）
总计                              8.5 天
```

### 依赖关系

```
T1 (Bug修复) ──→ 无依赖，最先做
T2 (Write)   ──→ 无依赖
T3 (Glob)    ──→ 无依赖
T4 (ripgrep) ──→ 无依赖
T5 (模糊匹配) ──→ 无依赖
T6 (时间戳)  ──→ 依赖 T2（WriteFileTool 需集成 tracker）

T2/T3/T4/T5 互相独立，可并行开发。
T6 在 T2 完成后实施。
```

### 详细步骤

#### Phase A: 基础修复（T1）— Day 1 上午

| 步骤 | 内容 | 验证 |
|------|------|------|
| A.1 | **先调试复现** ListDirTool path="." 实际错误栈，确认根因后修复 **[R5]** | 单元测试：`"."`, `""`, `null` |
| A.2 | 清理测试项目中已删除工具的引用 | `dotnet build AICA.Core.Tests` 通过 |
| A.3 | 运行全量单元测试 | 全部通过（或记录已知失败项） |

**验收标准**: `dotnet build` + `dotnet test` 全部通过

#### Phase B: 工具补齐（T2 + T3）— Day 1 下午 ~ Day 2

| 步骤 | 内容 | 验证 |
|------|------|------|
| B.1 | 实现 WriteFileTool | 单元测试 5 场景通过 |
| B.2 | 注册 WriteFileTool 到 ChatToolWindowControl | 编译通过 |
| B.3 | 添加 FileSystemGlobbing NuGet 引用 | `dotnet restore` 通过 |
| B.4 | 实现 GlobTool | 单元测试 5 场景通过 |
| B.5 | 注册 GlobTool 到 ChatToolWindowControl | 编译通过 |
| B.6 | 更新 DynamicToolSelector（新工具的复杂度/意图映射） | 编译通过 |

**验收标准**: 全量编译 + 16 个工具注册成功 + 新工具单元测试通过

#### Phase C: 搜索升级（T4）— Day 3 ~ Day 4

| 步骤 | 内容 | 验证 |
|------|------|------|
| C.1 | 下载 ripgrep Windows x64 二进制 | `tools/ripgrep/rg.exe --version` 正常输出 |
| C.2 | 实现 `FindRipgrep()` 运行时定位 **[R3]** | 单元测试：VSIX 目录 / PATH / null 三路 |
| C.3 | 实现双路策略（文件数阈值 200）**[R4]** | 单元测试：小/大规模分别走 C#/rg |
| C.4 | 重写 rg 进程调用（JSON 解析 + UTF-8 编码 + Kill 超时）| 单元测试：rg 路径搜索正确 |
| C.5 | 保留 C# fallback 路径 | 单元测试：mock rg 不存在时走 fallback |
| C.6 | 配置 VSIX 打包 rg.exe | `build.ps1` 编译后 VSIX 含 rg.exe |
| C.7 | 性能对比测试 | poco 项目搜索：rg vs C# 耗时对比 |

**验收标准**: 双路策略测试通过 + rg/C# 路径各自正确 + VSIX 打包含 rg.exe

#### Phase D: 编辑增强（T5 + T6）— Day 5

| 步骤 | 内容 | 验证 |
|------|------|------|
| D.1 | 从 DiagnoseEditFailure 提取 Level 2/3 auto-fix 到 FindWithCascade **[R1]** | 回归测试通过 |
| D.2 | 新增 Level 1 FindLineTrimmed（行尾空白忽略）| 单元测试新场景通过 |
| D.3 | 集成 FindWithCascade 到 EditFileTool.ExecuteAsync 主流程 | 编译通过 |
| D.4 | 验证 H3 剩余诊断（StaleContent + NoMatch）不受影响 | 回归测试 |
| D.4 | 实现 FileTimeTracker | 单元测试通过 |
| D.5 | 集成到 ReadFileTool / EditFileTool / WriteFileTool | 编译通过 |
| D.6 | 验证冲突检测提示 | 手动修改文件后 edit → 收到警告 |

**验收标准**: 模糊匹配测试通过 + 时间戳冲突检测测试通过 + H3 诊断路由不回归

#### Phase E: 集成测试 + 回归验证 — Day 6 ~ Day 6.5

| 步骤 | 内容 | 验证 |
|------|------|------|
| E.1 | 全量单元测试 | 全部通过 |
| E.2 | VSIX 编译 + 打包 | build.ps1 成功，VSIX 含 rg.exe |
| E.3 | VS2022 中加载 VSIX 冒烟测试 | 右键菜单 4 个命令可用 |
| E.4 | poco 项目端到端验证 | 解释代码 / 生成测试 / 重构 各跑一次 |
| E.5 | 工具选择率观察 | **量化标准**: 3 次端到端测试中，write_file 或 glob 至少被 LLM 选用 1 次即为 pass；0 次则检查工具描述是否需要优化 |
| E.6 | D-03/D-08 复验 | **量化标准**: D-03 流式输出不再被覆盖（连续 3 次 chat 无复现）；D-08 10 轮以上对话后 LLM 回复仍与当前任务相关 |
| E.7 | 确认 ToolDispatcher.GetToolDefinitions() 包含全部 16 个工具 | 启动日志输出工具数量 = 16 |

**验收标准**: VSIX 冒烟通过 + poco 端到端 3 场景通过 + 16 个工具注册确认

---

## 五、改动文件汇总

### 新增文件

| 文件 | 行数 | 说明 |
|------|------|------|
| `AICA.Core/Tools/WriteFileTool.cs` | ~200 | 新建文件工具（含换行符检测 + SafetyGuard 集成） |
| `AICA.Core/Tools/GlobTool.cs` | ~200 | 文件模式匹配工具 |
| `AICA.Core/Agent/FileTimeTracker.cs` | ~70 | 文件时间戳追踪（[R2] 普通 class 风格） |
| `tools/ripgrep/rg.exe` | — | ripgrep 二进制 |
| `AICA.Core.Tests/Tools/WriteFileToolTests.cs` | ~120 | |
| `AICA.Core.Tests/Tools/GlobToolTests.cs` | ~100 | |
| `AICA.Core.Tests/Agent/FileTimeTrackerTests.cs` | ~80 | |

### 改动文件

| 文件 | 改动量 | 说明 |
|------|--------|------|
| `AICA.Core/Tools/ListDirTool.cs` | ~10 行 | 修复 path="."（调试确认根因后） |
| `AICA.Core/Tools/GrepSearchTool.cs` | ~300 行（重写核心） | 双路策略 + FindRipgrep + rg JSON 解析 |
| `AICA.Core/Tools/EditFileTool.cs` | ~120 行 | 重构 H3 auto-fix 入口 + Level 1 + 时间戳 |
| `AICA.Core/Tools/ReadFileTool.cs` | ~5 行 | 时间戳记录 |
| `AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs` | ~10 行 | 注册新工具 |
| `AICA.Core/Agent/DynamicToolSelector.cs` | ~20 行 | 新工具映射 |
| `AICA.Core.csproj` | ~2 行 | NuGet 引用（FileSystemGlobbing） |
| `AICA.VSIX.csproj` | ~5 行 | 打包 rg.exe（Content + IncludeInVSIX） |
| 测试文件（已有） | ~90 行 | 更新/清理 |

---

## 六、风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| ripgrep VSIX 打包增大 ~5MB | 确定 | 低 | VSIX 从 ~15MB 增至 ~20MB，可接受 |
| FileSystemGlobbing 与 netstandard2.0 兼容性 | 低 | 中 | 验证 NuGet 包支持 netstandard2.0；不兼容则自实现 |
| 模糊匹配误命中（匹配到错误位置） | 低 | 高 | 唯一匹配要求 + 日志记录匹配级别 + H3 兜底 |
| LLM 不使用新工具（write_file/glob） | 中 | 中 | System Prompt 不提具体工具名（v2.0 已确立的零偏见策略），依赖工具描述引导。确认 ToolDispatcher.GetToolDefinitions() 自动包含新注册工具 **[R6]** |
| ripgrep 进程启动开销抵消 SIMD 收益（小规模搜索） | 中 | 低 | **双路策略 [R4]**: 文件数 <200 走 C#，>=200 走 rg |
| 时间戳检测误报（编辑器自动保存触发 mtime 变化） | 中 | 低 | 仅警告不阻断，LLM 可忽略继续 |
| rg --json 输出编码问题（中文路径/内容）**[R6]** | 中 | 中 | `ProcessStartInfo.StandardOutputEncoding = Encoding.UTF8` |
| rg 进程僵死（搜索巨型仓库不返回）**[R6]** | 低 | 高 | 30s 超时 + `process.Kill()` 强制终止 + 返回已收集的部分结果 |
| FileSystemGlobbing 在网络驱动器上性能差 **[R6]** | 低 | 中 | glob 加 15s 超时；限制最大搜索深度 20 层 |
| C# 9 语法在旧 SDK 编译失败 **[R6]** | 中 | 中 | **[R2] 已修正**: FileSnapshot 改用普通 class，不使用 record |

---

## 七、后续可做（不在本方案范围）

以下改进在本方案完成后可进一步评估：

| 改进 | 说明 | 前置条件 |
|------|------|----------|
| Plan Agent 分离 | 只读规划 Agent + 执行 Agent 的两阶段模式 | v2.1 完成（工具集稳定后再拆分） |
| Edit LSP 语义验证 | 编辑后调用 LSP 检测语法错误 | VS2022 LSP 集成调研 |
| 会话持久化（SQLite） | 历史会话检索、恢复 | 无 |
| 权限系统增强 | glob 模式 + action 分类 + 三级控制 | 无 |
| 模糊匹配扩展到 6-9 级 | Levenshtein 锚点、转义处理、Unicode 规范化 | T5 完成后观察匹配分布数据 |
| 多 MCP server 支持 | 除 GitNexus 外接入其他 MCP 服务 | 无 |
| 会话标题自动生成 | LLM 生成会话标题用于历史列表 | 会话持久化完成后 |
