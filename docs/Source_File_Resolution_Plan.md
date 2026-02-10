# AICA 源文件路径解析功能实现计划

## 问题背景

### 现象
在 Visual Studio 2022 中打开 CMake 生成的 out-of-source 项目（如 FreeCAD）时：
- `.sln` 文件位于 `build/` 目录（工作区目录）
- 源文件实际存放在 `source/` 目录（工作区目录之外）
- VS 解决方案资源管理器通过解析 `.vcxproj` 中的绝对路径正确显示文件
- AICA 的所有工具仅在 `WorkingDirectory`（= `.sln` 所在目录）内操作
- 导致 AICA **无法找到、读取、搜索、编辑**任何真正的源文件

### 根因分析

```
用户请求: "读取 src/App/Application.h 并分析"

AICA 路径解析:
  WorkingDirectory = D:\...\FreeCAD0191\build\
  → build\src\App\Application.h  ← 只有 .vcxproj、CMakeFiles 等构建产物
  → 文件不存在 → 工具返回错误 → LLM 可能幻觉

实际源文件位置:
  D:\...\FreeCAD0191\FreeCAD-0.19.1\src\App\Application.h  ← 在 build/ 外部
```

### VS 的文件管理机制

| 文件 | 作用 |
|------|------|
| `.sln` | 解决方案入口，列出所有项目的 `.vcxproj` 路径 |
| `.vcxproj` | 项目文件，`<ClCompile Include="绝对路径">` 和 `<ClInclude Include="绝对路径">` 定义编译和头文件 |
| `.vcxproj.filters` | 虚拟目录定义，控制解决方案资源管理器中的分组显示 |

关键：`.vcxproj` 中的 `Include` 属性使用**绝对路径**指向源码树中的文件。

---

## 目标

使 AICA 能够：
1. **自动发现**解决方案中所有项目引用的源文件路径
2. **正确解析** LLM 发出的文件路径请求（相对路径 → 实际源文件绝对路径）
3. **安全访问**工作区外的源文件（仅限 `.vcxproj` 声明的路径）
4. **搜索覆盖**所有源文件目录（而非仅限 `build/`）
5. **告知 LLM** 源码根目录信息，使其能发出正确的路径

---

## 架构设计

### 整体架构

```
┌─────────────────────────────────────────────────────┐
│                  AgentExecutor                       │
│   System Prompt 包含 SourceRoots 信息               │
└────────┬────────────────────────┬───────────────────┘
         │                        │
    ┌────▼─────┐           ┌─────▼──────────┐
    │ LLM 请求  │           │ Tool 执行      │
    │ path=     │           │ 路径解析       │
    │ "src/App/ │           │ ↓              │
    │  App.h"   │           │ PathResolver   │
    └──────────┘           └──┬──────────────┘
                              │
              ┌───────────────▼────────────────┐
              │         PathResolver            │
              │  1. 工作区内查找                │
              │  2. SourceIndex 映射查找        │
              │  3. SourceRoots 目录查找        │
              │  4. 返回绝对路径               │
              └───────────────┬────────────────┘
                              │
              ┌───────────────▼────────────────┐
              │    SolutionSourceIndex          │
              │  解析 .sln → .vcxproj           │
              │  提取 ClCompile/ClInclude       │
              │  建立 文件名→绝对路径 映射      │
              │  计算 SourceRoots（公共父目录）  │
              └────────────────────────────────┘
```

### 新增组件

| 组件 | 位置 | 职责 |
|------|------|------|
| `SolutionSourceIndex` | `AICA.Core/Workspace/` | 解析 .sln/.vcxproj，建立源文件索引 |
| `PathResolver` | `AICA.Core/Workspace/` | 统一路径解析（工作区 + 索引 + 源码根） |
| `IWorkspaceProvider` | `AICA.Core/Workspace/` | 抽象接口，支持 VS SDK 或文件解析两种模式 |

### 修改组件

| 组件 | 修改内容 |
|------|---------|
| `IAgentContext` | 新增 `SourceRoots` 属性和 `ResolveSourcePath()` 方法 |
| `VSAgentContext` | 集成 `SolutionSourceIndex`，实现路径解析 |
| `SafetyGuard` | 扩展路径访问检查，允许 SourceRoots 下的路径 |
| `SystemPromptBuilder` | 在 Workspace 段落中添加 SourceRoots 信息 |
| `ReadFileTool` | 使用 `ResolveSourcePath()` 替代直接拼接 |
| `EditFileTool` | 同上 |
| `GrepSearchTool` | 搜索范围扩展到 SourceRoots |
| `ListDirTool` | 支持列出 SourceRoots 下的目录 |
| `ListCodeDefinitionsTool` | 扫描范围扩展到 SourceRoots |
| `FindByNameTool`（如有） | 搜索范围扩展到 SourceRoots |

---

## 详细实现步骤

### 阶段 1：SolutionSourceIndex（核心索引构建）

**目标**：从 `.sln` 和 `.vcxproj` 文件中提取所有源文件的绝对路径，建立索引。

#### 1.1 创建 `AICA.Core/Workspace/SolutionSourceIndex.cs`

```csharp
namespace AICA.Core.Workspace
{
    /// <summary>
    /// 解析 VS 解决方案文件，建立源文件路径索引
    /// </summary>
    public class SolutionSourceIndex
    {
        // 文件名 → 绝对路径列表（可能有同名文件）
        public Dictionary<string, List<string>> FileNameIndex { get; }
        
        // 相对路径 → 绝对路径（以源码根为基准的相对路径）
        public Dictionary<string, string> RelativePathIndex { get; }
        
        // 所有源文件的公共父目录（源码根目录列表）
        public List<string> SourceRoots { get; }
        
        // 所有已索引的绝对路径集合（用于安全检查）
        public HashSet<string> AllIndexedPaths { get; }
        
        /// <summary>
        /// 从 .sln 文件构建索引
        /// </summary>
        public static SolutionSourceIndex BuildFromSolution(string slnPath);
        
        /// <summary>
        /// 从单个 .vcxproj 文件提取源文件路径
        /// </summary>
        private static List<string> ParseVcxproj(string vcxprojPath);
        
        /// <summary>
        /// 从 .csproj 文件提取源文件路径（支持 C# 项目）
        /// </summary>
        private static List<string> ParseCsproj(string csprojPath);
        
        /// <summary>
        /// 计算公共父目录作为 SourceRoots
        /// </summary>
        private static List<string> ComputeSourceRoots(IEnumerable<string> allPaths);
        
        /// <summary>
        /// 根据文件名或相对路径查找绝对路径
        /// </summary>
        public string Resolve(string requestedPath);
    }
}
```

#### 1.2 .sln 解析逻辑

```
输入: FreeCAD.sln
  ↓
正则提取: Project("{GUID}") = "ProjectName", "相对路径.vcxproj", "{GUID}"
  ↓
对每个 .vcxproj:
  解析 XML:
    <ClCompile Include="D:\...\src\App\Application.cpp" />
    <ClInclude Include="D:\...\src\App\Application.h" />
  ↓
收集所有绝对路径 → 建立索引
```

#### 1.3 索引结构

```
FileNameIndex:
  "Application.cpp" → ["D:\...\FreeCAD-0.19.1\src\App\Application.cpp"]
  "Application.h"   → ["D:\...\FreeCAD-0.19.1\src\App\Application.h",
                        "D:\...\FreeCAD-0.19.1\src\Gui\Application.h"]  ← 可能有同名

RelativePathIndex (以 SourceRoot 为基准):
  "src\App\Application.cpp" → "D:\...\FreeCAD-0.19.1\src\App\Application.cpp"
  "src\Gui\Application.cpp" → "D:\...\FreeCAD-0.19.1\src\Gui\Application.cpp"

SourceRoots:
  ["D:\...\FreeCAD-0.19.1\"]  ← 所有源文件的最大公共前缀

AllIndexedPaths:
  { "D:\...\src\App\Application.cpp", "D:\...\src\App\Application.h", ... }
```

#### 1.4 性能考虑

- **懒加载**：首次需要时构建索引，缓存结果
- **文件数量上限**：索引最多 50000 个文件路径（超大项目保护）
- **异步构建**：不阻塞 UI 线程
- **变更检测**：监听 `.sln` 修改时间，变更时重建索引

**预计工时**：4-5 小时

---

### 阶段 2：PathResolver（统一路径解析）

**目标**：提供统一的路径解析逻辑，替代各工具中分散的 `Path.Combine(WorkingDirectory, path)`。

#### 2.1 创建 `AICA.Core/Workspace/PathResolver.cs`

```csharp
namespace AICA.Core.Workspace
{
    public class PathResolver
    {
        private readonly string _workingDirectory;
        private readonly SolutionSourceIndex _sourceIndex;
        
        /// <summary>
        /// 解析用户/LLM 请求的路径为实际文件的绝对路径
        /// </summary>
        /// <param name="requestedPath">请求的路径（可能是相对路径、文件名、或绝对路径）</param>
        /// <returns>解析后的绝对路径，null 表示未找到</returns>
        public string ResolveFile(string requestedPath);
        
        /// <summary>
        /// 解析目录路径
        /// </summary>
        public string ResolveDirectory(string requestedPath);
        
        /// <summary>
        /// 检查路径是否可访问（工作区内 或 源文件索引中）
        /// </summary>
        public bool IsAccessible(string path);
    }
}
```

#### 2.2 解析策略（优先级从高到低）

```
ResolveFile("src/App/Application.h"):

1. 绝对路径？ → 直接返回（如果存在且可访问）
2. 工作区相对路径？ → Path.Combine(WorkingDirectory, path)
   → build/src/App/Application.h → 检查存在 → 存在则返回
3. 源码索引相对路径？ → RelativePathIndex["src\App\Application.h"]
   → D:\...\FreeCAD-0.19.1\src\App\Application.h → 存在则返回
4. 源码根相对路径？ → 对每个 SourceRoot 尝试 Path.Combine(root, path)
   → D:\...\FreeCAD-0.19.1\src\App\Application.h → 存在则返回
5. 文件名查找？ → FileNameIndex["Application.h"]
   → 唯一匹配则返回；多个匹配则返回消歧列表
6. 均未找到 → 返回 null
```

#### 2.3 消歧处理

当文件名索引返回多个匹配时：

```
工具返回:
"Found multiple files matching 'Application.h':
  1. src/App/Application.h
  2. src/Gui/Application.h
Please specify the full relative path."
```

**预计工时**：2-3 小时

---

### 阶段 3：安全机制扩展

**目标**：扩展 `SafetyGuard`，允许访问 SourceRoots 下的路径。

#### 3.1 修改 `SafetyGuard.cs`

```csharp
// 新增字段
private readonly HashSet<string> _allowedSourceRoots;

// 修改 CheckPathAccess
public PathAccessResult CheckPathAccess(string path)
{
    // ... 现有检查 ...
    
    // 新增：检查是否在 SourceRoots 内
    var fullPath = Path.GetFullPath(path);
    if (_allowedSourceRoots != null)
    {
        foreach (var root in _allowedSourceRoots)
        {
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return PathAccessResult.Allowed();
        }
    }
    
    return PathAccessResult.Denied("Path is outside working directory and source roots");
}
```

#### 3.2 修改 `SafetyGuardOptions`

```csharp
public class SafetyGuardOptions
{
    public string WorkingDirectory { get; set; }
    public string[] SourceRoots { get; set; }  // 新增
    // ...
}
```

#### 3.3 安全约束

- 仅允许**读取**和**搜索** SourceRoots 下的文件
- **写入/编辑** SourceRoots 下的文件**需要额外确认**（System Prompt 提示 + 用户确认）
- SourceRoots 仅来自 `.vcxproj` 解析，不可由 LLM 自行指定

**预计工时**：1-2 小时

---

### 阶段 4：IAgentContext 接口扩展

**目标**：在接口层暴露源文件解析能力。

#### 4.1 修改 `IAgentContext.cs`

```csharp
public interface IAgentContext
{
    // 现有接口...
    string WorkingDirectory { get; }
    
    // 新增
    /// <summary>
    /// 解决方案中的源码根目录列表
    /// </summary>
    IReadOnlyList<string> SourceRoots { get; }
    
    /// <summary>
    /// 解析文件路径（支持工作区内和源码索引）
    /// </summary>
    string ResolveFilePath(string requestedPath);
    
    /// <summary>
    /// 解析目录路径
    /// </summary>
    string ResolveDirectoryPath(string requestedPath);
}
```

#### 4.2 修改 `VSAgentContext.cs`

```csharp
public class VSAgentContext : IAgentContext
{
    private readonly PathResolver _pathResolver;
    private SolutionSourceIndex _sourceIndex;
    
    public IReadOnlyList<string> SourceRoots => 
        _sourceIndex?.SourceRoots ?? Array.Empty<string>();
    
    // 构造函数中初始化索引
    public VSAgentContext(DTE2 dte, ...)
    {
        // ... 现有逻辑 ...
        
        // 异步构建源文件索引
        _ = Task.Run(() => InitializeSourceIndex());
    }
    
    private void InitializeSourceIndex()
    {
        var slnPath = _dte?.Solution?.FullName;
        if (!string.IsNullOrEmpty(slnPath) && File.Exists(slnPath))
        {
            _sourceIndex = SolutionSourceIndex.BuildFromSolution(slnPath);
            _pathResolver = new PathResolver(WorkingDirectory, _sourceIndex);
        }
    }
    
    public string ResolveFilePath(string path) => 
        _pathResolver?.ResolveFile(path) ?? GetFullPath(path);
}
```

**预计工时**：2 小时

---

### 阶段 5：工具层适配

**目标**：修改所有文件操作工具，使用 `ResolveFilePath()` 替代硬编码路径拼接。

#### 5.1 ReadFileTool.cs

```csharp
// 之前:
var path = pathObj.ToString();
if (!await context.FileExistsAsync(path, ct))
    return ToolResult.Fail($"File not found: {path}");

// 之后:
var path = pathObj.ToString();
var resolvedPath = context.ResolveFilePath(path);
if (resolvedPath == null || !File.Exists(resolvedPath))
    return ToolResult.Fail($"File not found: {path}");
```

#### 5.2 EditFileTool.cs — 同样使用 `ResolveFilePath()`

#### 5.3 GrepSearchTool.cs

```csharp
// 搜索范围扩展
// 之前: 只搜索 WorkingDirectory
// 之后: 搜索 WorkingDirectory + SourceRoots

var searchPaths = new List<string> { fullPath };
if (context.SourceRoots != null)
{
    // 如果搜索路径是工作区根目录，也搜索源码根
    if (fullPath == context.WorkingDirectory)
    {
        searchPaths.AddRange(context.SourceRoots);
    }
}
```

#### 5.4 ListDirTool.cs

```csharp
// 当列出工作区根目录时，额外显示源码根信息
if (relativePath == "." || relativePath == "./")
{
    // 正常列出 WorkingDirectory
    // + 附加提示: "Source roots (from .vcxproj): ..."
}
```

#### 5.5 ListCodeDefinitionsTool.cs — 搜索范围扩展到 SourceRoots

#### 5.6 FindByNameTool（如有）— 搜索范围扩展到 SourceRoots

**预计工时**：3-4 小时

---

### 阶段 6：System Prompt 增强

**目标**：告知 LLM 源码根目录信息，使其能发出正确的文件路径请求。

#### 6.1 修改 `SystemPromptBuilder.AddWorkspaceContext()`

```csharp
public SystemPromptBuilder AddWorkspaceContext(
    string workingDirectory, 
    IReadOnlyList<string> sourceRoots = null,  // 新增
    IEnumerable<string> recentFiles = null)
{
    _builder.AppendLine("## Workspace");
    _builder.AppendLine($"Working Directory: {workingDirectory}");
    
    if (sourceRoots != null && sourceRoots.Count > 0)
    {
        _builder.AppendLine();
        _builder.AppendLine("### Source Roots");
        _builder.AppendLine("The following directories contain source files referenced by the solution's project files (.vcxproj/.csproj):");
        foreach (var root in sourceRoots)
        {
            _builder.AppendLine($"- {root}");
        }
        _builder.AppendLine("You can use paths relative to these roots when accessing source files.");
        _builder.AppendLine("For example, if a source root is 'D:\\Project\\FreeCAD-0.19.1\\' and you need 'src/App/Application.h',");
        _builder.AppendLine("you can use 'src/App/Application.h' directly — the path resolver will find the correct file.");
    }
    
    // ... 现有 recentFiles 逻辑 ...
}
```

#### 6.2 新增路径规则

```
### Path Resolution
- File paths are automatically resolved across the working directory AND source roots.
- If a file is not found in the working directory, the system will search source roots.
- When multiple files match the same name, use the full relative path to disambiguate.
- Source root files can be read and searched. Write operations on source files require explicit confirmation.
```

**预计工时**：1 小时

---

### 阶段 7：VS SDK 增强（可选，P2）

**目标**：通过 VS SDK 动态获取项目信息，而非静态解析 `.vcxproj` 文件。

#### 7.1 方案对比

| 方案 | 优点 | 缺点 |
|------|------|------|
| **A. 文件解析**（阶段1-6 采用） | 简单可靠、无 VS SDK 依赖 | 不支持 MSBuild 属性展开、不响应实时变更 |
| **B. VS SDK IVsSolution** | 实时准确、支持动态项目 | 必须在 UI 线程、API 复杂 |
| **C. MSBuild API** | 支持属性展开、条件编译 | 需要额外 NuGet 包、初始化慢 |

#### 7.2 推荐路径

- **Sprint 4 先用方案 A**（文件解析），覆盖 90% 场景
- **后续迭代**可切换为方案 B（VS SDK），获取更精确的项目结构

**预计工时（方案 B）**：5-6 小时（如需实现）

---

## 实现优先级与排期

| 阶段 | 内容 | 工时 | 优先级 | 依赖 |
|------|------|------|--------|------|
| 1 | SolutionSourceIndex | 4-5h | P0 | 无 |
| 2 | PathResolver | 2-3h | P0 | 阶段1 |
| 3 | SafetyGuard 扩展 | 1-2h | P0 | 阶段1 |
| 4 | IAgentContext 扩展 | 2h | P0 | 阶段1-3 |
| 5 | 工具层适配 | 3-4h | P0 | 阶段4 |
| 6 | System Prompt 增强 | 1h | P1 | 阶段1 |
| 7 | VS SDK 增强 | 5-6h | P2 | 阶段1-6 |
| **总计** | | **18-23h** | | |

### 建议实施顺序

```
第一轮（核心能力）: 阶段 1 → 2 → 3 → 4 → 5
  → 交付物: AICA 能正确找到和读取 .vcxproj 中引用的所有源文件
  → 测试: 在 FreeCAD 项目上验证 read_file/grep_search/list_dir

第二轮（体验优化）: 阶段 6
  → 交付物: LLM 知道源码目录的存在，能主动使用正确路径
  → 测试: 直接问"读取 Application.h"能否自动找到

第三轮（可选增强）: 阶段 7
  → 交付物: 实时项目感知，支持动态加载/卸载项目
```

---

## 测试计划

### 测试环境
- 项目: FreeCAD 0.19.1（CMake out-of-source build）
- .sln 位置: `D:\Project\AIConsProject\FreeCAD0191\build\FreeCAD.sln`
- 源码位置: `D:\Project\AIConsProject\FreeCAD0191\FreeCAD-0.19.1\src\`

### 测试用例

| # | 测试描述 | 输入 | 期望行为 |
|---|---------|------|---------|
| T1 | 索引构建 | 打开 FreeCAD.sln | SourceIndex 包含所有 .vcxproj 中的源文件路径 |
| T2 | 读取源文件 | "读取 src/App/Application.h" | 正确解析到 FreeCAD-0.19.1/src/App/Application.h |
| T3 | 同名消歧 | "读取 Application.h" | 返回多个候选，提示用户指定完整路径 |
| T4 | 搜索源码 | "搜索 BRep_Builder" | 搜索范围包含源码目录，找到匹配文件 |
| T5 | 列出源码目录 | "列出 src/App 目录" | 正确列出 FreeCAD-0.19.1/src/App/ 的内容 |
| T6 | 安全限制 | "读取 C:\Windows\System32\..." | 拒绝访问（不在 WorkingDirectory 和 SourceRoots 内） |
| T7 | 编辑源文件 | "修改 Application.cpp 中的..." | 弹出额外确认（源码在工作区外） |
| T8 | Prompt 感知 | "这个项目的源码在哪里？" | LLM 能基于 System Prompt 中的 SourceRoots 信息回答 |

### 验收标准

- [ ] FreeCAD 项目中 `read_file("src/App/Application.h")` 成功返回内容
- [ ] `grep_search("BRep_Builder")` 能搜索到源码目录中的文件
- [ ] 工作区外的源文件写入操作需要额外确认
- [ ] 非源码路径（如 C:\Windows）仍然被拒绝
- [ ] 索引构建时间 < 3 秒（FreeCAD 规模项目）
- [ ] 无回归：AICA 自身项目（C# 项目，源码在工作区内）行为不变

---

## 风险与应对

| 风险 | 影响 | 应对措施 |
|------|------|---------|
| `.vcxproj` 中使用 MSBuild 变量（如 `$(SrcDir)`） | 无法解析绝对路径 | 阶段1: 跳过含变量的路径；阶段7: 用 MSBuild API 展开 |
| 同名文件过多导致消歧困难 | LLM 可能选错文件 | 返回带路径的候选列表，让 LLM/用户选择 |
| 超大解决方案（>1000 个 .vcxproj） | 索引构建缓慢 | 设置超时+文件数上限+进度条 |
| 源码路径变更（CMake 重新配置） | 索引过期 | 监听 .sln 修改时间，变更时重建 |
| 路径中包含中文或特殊字符 | 路径解析出错 | 使用 `Path.GetFullPath()` 标准化 |

---

## 版本规划

- 实现完成后 VSIX 版本升级至 **v1.9.0**
- 每个阶段完成后构建并测试
- 全部完成后在 FreeCAD 项目上做完整回归测试
