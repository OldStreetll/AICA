# AICA C/C++ 专家化技术方案

> 版本: v1.0 | 日期: 2026-03-22
> 目标: 将 AICA 从通用编程助手转型为华中数控 C/C++ 专家级编程助手

---

## 一、方案概述

### 1.1 当前状态

AICA 的 System Prompt 是**语言无关的通用编程助手**：

- `SystemPromptBuilder.AddBasePrompt()` 定义的角色是 "help developers with code generation, editing, refactoring, testing, debugging"
- 三个右键菜单命令（解释/重构/生成测试）使用通用 prompt，测试框架硬编码为 "xUnit"
- `.aica-rules/general.md` 只有通用的代码质量规则（DRY、函数 <50 行）
- `SymbolParser` 已支持 C/C++ 解析（class/struct/enum/namespace/typedef/#define/function）
- `KnowledgeContextProvider` 的 TF-IDF 检索对 C/C++ 符号已可用

### 1.2 目标状态

AICA 生成的所有 C/C++ 代码**自动遵循**华中数控 Qt-C++ 编码规范和 MISRA C 标准，在4个核心场景（代码生成、Bug修复、测试生成、代码重构）中表现出 C/C++ 领域专家水平。

### 1.3 三阶段划分

| Phase | 名称 | 改动范围 | 改代码? | 预期效果 |
|-------|------|----------|---------|----------|
| Phase 1 | 规范注入 | `.aica-rules/` 文件 | 否 | 生成代码自动遵循公司规范 |
| Phase 2 | 场景 Prompt 升级 | 3个 Command + SystemPromptBuilder | 是 | 4个场景输出质量显著提升 |
| Phase 3 | 知识引擎强化 | SymbolParser + KnowledgeContextProvider | 是 | C/C++ 项目理解能力提升 |

---

## 二、Phase 1：规范注入（零代码修改）

### 2.1 原理

AICA 已有完整的规则加载管线：

```
.aica-rules/*.md → RuleLoader.LoadAllRulesAsync() → RuleEvaluator → SystemPromptBuilder.AddRulesFromFilesAsync()
```

规则文件通过 YAML frontmatter 控制优先级和路径匹配：

```yaml
---
priority: 15          # 0-20, Workspace 默认 20
enabled: true
paths:                # 可选: glob 模式匹配触发
  - "**/*.cpp"
  - "**/*.h"
---
```

规则内容作为 Markdown 注入到 System Prompt 的 `## Project Rules` 段落。

### 2.2 Token 预算分析

当前 System Prompt 各段落的 token 占用估算：

| 段落 | 估算 tokens | 说明 |
|------|-------------|------|
| Base Role | ~150 | 固定 |
| Tool Descriptions | ~800-1500 | 取决于工具数量 |
| Rules (Core) | ~600 | 固定 |
| Rules (Advanced) | ~800 | Medium/Complex |
| Rules (Complex) | ~700 | Complex only |
| Workspace Context | ~200-400 | 取决于 source roots |
| Project Rules (.aica-rules) | **~0 → 2000** | 当前几乎为空 |
| Knowledge Context | ~1500 (max 6000) | 动态 |
| **总计** | **~4000-7000** | Build() 在 >8000 时警告 |

**结论：** 有约 1000-3000 tokens 的空间用于 C/C++ 规范注入。需要**精简提炼**，不能把整本规范塞进去。

### 2.3 规则文件设计

**设计原则：**
- 每个文件聚焦一个主题，便于独立启用/禁用
- 总 token 预算控制在 **1500 tokens 以内**（约 6000 字符）
- 只包含 LLM 可执行的规则（"生成代码时做X"），不包含理念性描述
- 使用 `paths` 过滤，C/C++ 规则只在操作 `.h/.cpp/.c` 文件时激活

#### 文件 1: `.aica-rules/cpp-code-style.md`（~400 tokens）

```markdown
---
priority: 15
enabled: true
paths:
  - "**/*.h"
  - "**/*.hpp"
  - "**/*.cpp"
  - "**/*.c"
---

# C/C++ 代码风格（华中数控规范）

生成或修改 C/C++ 代码时，严格遵循以下规则：

## 花括号与缩进
- 花括号 `{` 和 `}` 独占一行，与引用语句左对齐（Allman 风格）
- 例外：do-while/struct/union 其后有 `;` 的除外，头文件中仅一行的函数定义除外
- 缩进使用 4 个空格
- 代码行最大长度 80 字符，长表达式在低优先级操作符处拆分

## 命名规范
- 类/结构体/枚举：`UpperCamelCase`（如 `WgFileList`, `BackGroundColor`）
- 函数：`UpperCamelCase`（如 `AddNode`, `GetSelPath`）
- 变量和参数：`lowerCamelCase`（如 `posVal`, `toolNumPath`）
- 常量和宏：`ALL_CAPS_WITH_UNDERSCORES`（如 `MAX_LENGTH`, `CODE_NUM`）
- 静态变量：加 `s_` 前缀（如 `s_path`）
- 类成员变量：`m_` 前缀，整型 `m_n`，浮点 `m_f`，字符串 `m_s`，bool `m_b`，指针 `m_p`
- 文件名：小写字母+数字，Widget 界面用 `wg` 开头，对话框用 `dlg` 开头

## 指针与操作符
- 修饰符 `*` 和 `&` 紧靠变量名：`Bit8 *name = &value;`
- 二元操作符前后加空格，一元操作符不加空格
- 函数名后不留空格，紧跟 `(`

## 数据类型
- 使用项目自定义类型：`Bit8`, `Bit16`, `Bit32`, `Bit64`, `uBit32`, `fBit32`, `fBit64`
- 不要使用 `#define` 重新定义基本类型，使用 `typedef`
- 在分布式/跨 CPU 通信的数据结构中，注意字节对齐（使用 `#pragma pack`）
- 避免使用 `long` 型变量（不同平台长度不同）
```

#### 文件 2: `.aica-rules/cpp-reliability.md`（~350 tokens）

```markdown
---
priority: 18
enabled: true
paths:
  - "**/*.h"
  - "**/*.hpp"
  - "**/*.cpp"
  - "**/*.c"
---

# C/C++ 可靠性规则（华中数控规范 + MISRA C）

## 内存安全（最高优先级）
- 禁止在运行时动态分配内存（malloc/new），仅允许在初始化和退出时使用
- 禁止使用 `strcpy`、`sprintf`，使用 `strlcpy`、`snprintf` 替代
- 数组访问前必须进行下标合法性检查和指针非 NULL 检查
- `free` 后立即将指针置 NULL
- 函数内不要定义超过 4096 字节的数组，使用 static 或全局数组
- 缓冲区参数必须同时传入长度参数

## 类型安全
- 禁止隐式类型转换，必须显式转换
- 整型用 `==` 或 `!=` 与 0 比较，不要用 `if(value)`
- 指针用 `==` 或 `!=` 与 NULL 比较，不要用 `if(p)`
- 浮点数禁止用 `==` 比较，使用 `HNC_DoubleCompare` 接口
- bool 变量用 `==` 或 `!=` 与 true/false 比较

## 控制流
- 禁止使用 `goto` 语句
- 所有 `if/for/while/do` 必须使用 `{}` 包裹，即使只有一条语句
- 避免使用 `?:` 三元运算符，使用 `if` 语句替代
- 宏定义中的参数和整体必须加括号：`#define MIN(a, b) ((a) < (b) ? (a) : (b))`
- `switch` 的每个 `case` 必须有 `break`，多 case 时在 break 后加注释

## 函数规范
- 函数不超过 200 行（不含注释和空行）
- 入口处对所有指针参数进行 NULL 检查
- 返回指针的函数用 NULL 表示失败
- 参数顺序：输入在前，输出在后
- const 修饰所有不修改的输入指针参数
```

#### 文件 3: `.aica-rules/cpp-file-io.md`（~200 tokens）

```markdown
---
priority: 16
enabled: true
paths:
  - "**/*.cpp"
  - "**/*.c"
---

# C/C++ 文件 I/O 规则

- `fopen` 前用 `stat()` 检查路径有效性（Linux 下 fopen 文件夹也能成功）
- 检查所有文件操作的返回值：`fopen`、`fclose`、`fread`、`fwrite`、`fseek`
- 写文件后必须 `fflush` + `fsync` 确保数据落盘
- `fopen` 和 `fclose` 必须配对，所有错误路径都要关闭文件
- `fclose` 后将文件指针置 NULL
- 新增数据文件必须有文件头、校验码（写入时计算，读取时验证）
- 写文件前调用 `HNC_FileioCheckFreeSpace` 检测磁盘剩余空间
- 文件路径使用 `QDir::toNativeSeparators` 或 `DIR_SEPARATOR` 分隔符
- 路径缓冲区使用 `PATH_NAME_LEN` 常量，禁止硬编码长度
```

#### 文件 4: `.aica-rules/cpp-qt-specific.md`（~300 tokens）

```markdown
---
priority: 15
enabled: true
paths:
  - "**/*.h"
  - "**/*.hpp"
  - "**/*.cpp"
---

# Qt-C++ 专属规则

## 头文件规范
- 使用 `#ifndef/#define/#endif` 防止重复引用
- include 分类顺序：标准库 → Qt 库 → API → APP（→ 自身头文件，仅 .cpp）
- 禁止 `#include <QtGui>`、`#include <QtCore>` 等模块级引用，使用具体头文件
- 仅使用指针的类，用前向声明放在 `QT_BEGIN_NAMESPACE`/`QT_END_NAMESPACE` 之间

## 类成员排序
头文件中类成员按以下顺序排列：
`public` → `public slots` → `signals` → `protected` → `protected slots` → `private` → `private slots`
同一访问类别中：成员变量在前，成员函数在后

## Qt 编码规则
- 禁止在代码中直接设置颜色值，使用 `.qss` 文件配置
- 组合按键使用 `QKeyEvent::modifiers()` 获取，按位处理
- 功能快捷键在 `hotkeycfg.xml` 中配置，禁止代码中直接设置
- 翻译文本：`wg*.cpp`/`dlg*.cpp` 中优先使用 `TR`，其他文件使用 `QObject::TR`
- 禁止在 `.h` 文件中写需要翻译的文本
- 浮点数转字符串必须指定精度：`QString::number(value, 'f', prec)`
- 使用通道号时不要硬编码 0，用 `ActiveChan()` 或从事件数据获取

## 平台可移植性
- 用 `#ifdef _LINUX` 区分不同 OS 环境的代码
- 包含头文件不使用绝对路径
- 新增代码和配置文件使用 UTF-8 编码
```

#### 文件 5: `.aica-rules/cpp-comment-template.md`（~250 tokens）

```markdown
---
priority: 12
enabled: true
paths:
  - "**/*.h"
  - "**/*.hpp"
  - "**/*.cpp"
  - "**/*.c"
---

# C/C++ 注释规范

## 文件头注释（必需）
每个新建文件必须包含以下格式的文件头注释：
```cpp
/*!
 * @file filename.cpp
 * @brief 简要描述
 * @note 详细说明
 *
 * @version V1.00
 * @date YYYY/MM/DD
 * @author HNC-8 Team
 * @copyright 武汉华中数控股份有限公司软件开发部
 */
```

## 函数注释（doxygen 格式，必需）
```cpp
/**
 * @brief 函数功能简述
 * @param [in] paramName：参数说明
 * @param [out] outParam：输出参数说明
 * @return 返回值说明
 * @attention 注意事项
 */
```

## 注释规则
- 注释率目标：程序总行数的 20%~30%
- 注释放在代码上方或右方，不放下方
- 行末注释使用 `//`
- 大量代码注释使用 `#if 0 ... #endif`，不使用 `/* */` 或 `//`
- 多重嵌套时在段落结束处加注释
- `/*`、`//` 后留空格，`*/` 前留空格
- 修改代码时同步修改注释
```

### 2.4 部署方式

将上述 5 个文件放入用户 C/C++ 项目的 `.aica-rules/` 目录即可。AICA 下次会话时 `RuleLoader` 自动加载。

**关键点：** 这些规则文件应随用户项目分发，不是放在 AICA 源码中。但 AICA 可以提供一个"初始化 C/C++ 规范"的命令来自动创建这些文件。

### 2.5 验证方法

1. 在 AICA 中打开一个 C/C++ 项目
2. 让 AICA 生成一个新的 C++ 类
3. 检查生成的代码是否符合：
   - 花括号 Allman 风格
   - 成员变量 `m_` 前缀
   - 文件头 doxygen 注释
   - 函数入口 NULL 检查
   - 使用 `Bit32` 而非 `int`

---

## 三、Phase 2：场景 Prompt 升级（改代码）

### 3.1 改动清单

| 文件 | 改动类型 | 说明 |
|------|----------|------|
| `Commands/RefactorCommand.cs:49` | 修改 prompt 模板 | C/C++ 专属重构指令 |
| `Commands/GenerateTestCommand.cs:49` | 修改 prompt 模板 | 替换 xUnit 为 C/C++ 测试框架 |
| `Commands/ExplainCodeCommand.cs:49` | 修改 prompt 模板 | 加入 CNC 领域上下文 |
| `Prompt/SystemPromptBuilder.cs` | 新增方法 | `AddLanguageSpecialization()` |
| `Agent/AgentExecutor.cs:91-97` | 插入调用 | 调用语言专属方法 |
| `Agent/TaskComplexityAnalyzer.cs` | 增强关键词 | C/C++ 复杂任务识别 |

### 3.2 实现细节

#### 3.2.1 语言检测机制

在 `AgentExecutor` 中加入项目语言检测，用于决定是否注入 C/C++ 专属 Prompt：

**新增文件: `Agent/ProjectLanguageDetector.cs`**

```csharp
namespace AICA.Core.Agent
{
    /// <summary>
    /// Detects the primary programming language of the current project
    /// based on file extensions in the workspace.
    /// </summary>
    public static class ProjectLanguageDetector
    {
        public enum ProjectLanguage
        {
            Unknown,
            CSharp,
            CppQt,     // C/C++ with Qt
            Cpp,       // C/C++ without Qt
            Mixed
        }

        /// <summary>
        /// Detect project language from workspace directory.
        /// Uses file extension distribution and Qt-specific markers.
        /// </summary>
        public static ProjectLanguage Detect(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory) ||
                !System.IO.Directory.Exists(workingDirectory))
            {
                return ProjectLanguage.Unknown;
            }

            int cppCount = 0;
            int csCount = 0;
            bool hasQtMarkers = false;

            // Sample files (max 500 to avoid slow scan)
            var files = System.IO.Directory.EnumerateFiles(
                workingDirectory, "*.*",
                System.IO.SearchOption.AllDirectories);

            int scanned = 0;
            foreach (var file in files)
            {
                if (scanned++ > 500) break;

                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                switch (ext)
                {
                    case ".h":
                    case ".hpp":
                    case ".hxx":
                    case ".cpp":
                    case ".cxx":
                    case ".c":
                        cppCount++;
                        break;
                    case ".cs":
                        csCount++;
                        break;
                    case ".ui":
                    case ".qss":
                    case ".qrc":
                    case ".pro":
                    case ".pri":
                        hasQtMarkers = true;
                        break;
                }
            }

            // Also check for .vcxproj (C++ project in VS)
            if (cppCount == 0)
            {
                var vcxprojFiles = System.IO.Directory.GetFiles(
                    workingDirectory, "*.vcxproj",
                    System.IO.SearchOption.TopDirectoryOnly);
                if (vcxprojFiles.Length > 0) cppCount += 10;
            }

            if (cppCount == 0 && csCount == 0)
                return ProjectLanguage.Unknown;

            if (cppCount > csCount * 2)
                return hasQtMarkers
                    ? ProjectLanguage.CppQt
                    : ProjectLanguage.Cpp;

            if (csCount > cppCount * 2)
                return ProjectLanguage.CSharp;

            return ProjectLanguage.Mixed;
        }

        /// <summary>
        /// Detect language from a single file path (for context menu commands).
        /// </summary>
        public static ProjectLanguage DetectFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return ProjectLanguage.Unknown;

            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".h":
                case ".hpp":
                case ".hxx":
                case ".cpp":
                case ".cxx":
                case ".c":
                    return ProjectLanguage.Cpp;
                case ".cs":
                    return ProjectLanguage.CSharp;
                default:
                    return ProjectLanguage.Unknown;
            }
        }
    }
}
```

**设计决策：**
- 轻量级检测：只扫描文件扩展名，不读文件内容，最多 500 个文件
- Qt 检测：通过 `.ui`、`.qss`、`.qrc`、`.pro` 文件判断
- 结果可缓存：项目语言不会在会话中变化，首次检测后缓存即可
- 不可变：返回枚举值，不修改任何状态

#### 3.2.2 SystemPromptBuilder 新增 C/C++ 专属方法

**修改文件: `Prompt/SystemPromptBuilder.cs`**

在 `AddCustomInstructions()` 之后、`AddKnowledgeContext()` 之前，新增方法：

```csharp
/// <summary>
/// Add C/C++ language-specific expertise to the system prompt.
/// Called when project is detected as C/C++ (with or without Qt).
/// </summary>
public SystemPromptBuilder AddCppSpecialization(bool isQt = false)
{
    _builder.AppendLine("## C/C++ Expert Mode");
    _builder.AppendLine();
    _builder.AppendLine("You are operating in C/C++ expert mode. You have deep expertise in:");
    _builder.AppendLine("- Memory management (RAII, smart pointers, manual allocation patterns)");
    _builder.AppendLine("- Template metaprogramming and SFINAE");
    _builder.AppendLine("- Preprocessor macros and conditional compilation");
    _builder.AppendLine("- Header/source file organization and include dependencies");
    _builder.AppendLine("- Build systems (CMake, MSBuild/vcxproj)");
    _builder.AppendLine("- Debugging with gdb (attach, breakpoints, backtrace, core dumps)");
    _builder.AppendLine();
    _builder.AppendLine("When generating or reviewing C/C++ code:");
    _builder.AppendLine("- Always consider memory ownership and lifecycle");
    _builder.AppendLine("- Check for buffer overflows, null pointer dereference, use-after-free");
    _builder.AppendLine("- Prefer stack allocation over heap allocation");
    _builder.AppendLine("- Use const correctness throughout");
    _builder.AppendLine("- Consider platform portability (Windows/Linux differences)");
    _builder.AppendLine();

    if (isQt)
    {
        _builder.AppendLine("### Qt Framework Expertise");
        _builder.AppendLine("- Signal/slot connections (type safety, lifetime management)");
        _builder.AppendLine("- Qt property system and meta-object compiler (MOC)");
        _builder.AppendLine("- Widget lifecycle and parent-child ownership");
        _builder.AppendLine("- Qt container classes vs STL containers");
        _builder.AppendLine("- Internationalization (TR/QObject::TR)");
        _builder.AppendLine();
    }

    _builder.AppendLine("### CNC Domain Context");
    _builder.AppendLine("This codebase is for CNC (Computer Numerical Control) industrial automation software.");
    _builder.AppendLine("Key domain concepts:");
    _builder.AppendLine("- Channels (通道): Independent machining control units");
    _builder.AppendLine("- Axes (轴): Motor-driven movement axes (feed axes, spindles)");
    _builder.AppendLine("- PLC: Programmable Logic Controller interface");
    _builder.AppendLine("- G-code: CNC programming language for machine tool control");
    _builder.AppendLine("- Parameters (参数): System/channel/axis configuration values");
    _builder.AppendLine("- HNC API: Internal API prefixed with HNC_ (e.g., HNC_SysCtrlGetConfig)");
    _builder.AppendLine();

    return this;
}
```

**Token 成本：** 约 350 tokens（Qt 模式）/ 250 tokens（纯 C++）

#### 3.2.3 AgentExecutor 集成

**修改文件: `Agent/AgentExecutor.cs` 第 91-114 行区域**

在构建 SystemPromptBuilder 后、Build() 前插入语言检测和专属 Prompt 注入：

```csharp
// 现有代码: line 91-97
var builder = new SystemPromptBuilder()
    .AddTools(toolDefinitions)
    .AddToolDescriptions()
    .AddComplexityGuidance(complexity)
    .AddWorkspaceContext(
        context?.WorkingDirectory ?? Environment.CurrentDirectory,
        context?.SourceRoots);

// 现有代码: line 99-111 (rules from files)
if (context?.WorkingDirectory != null)
{
    var ruleContext = new RuleContext(context.WorkingDirectory)
    {
        CandidatePaths = ExtractPathCandidates(userRequest)
    };

    await builder.AddRulesFromFilesAsync(
        context.WorkingDirectory,
        ruleContext,
        ct);
}

// === NEW: C/C++ 专属化注入 ===
if (context?.WorkingDirectory != null)
{
    var projectLang = ProjectLanguageDetector.Detect(context.WorkingDirectory);
    if (projectLang == ProjectLanguageDetector.ProjectLanguage.CppQt)
    {
        builder.AddCppSpecialization(isQt: true);
    }
    else if (projectLang == ProjectLanguageDetector.ProjectLanguage.Cpp)
    {
        builder.AddCppSpecialization(isQt: false);
    }
}
// === END NEW ===

// 现有代码: line 113-114
builder.AddCustomInstructions(_customInstructions);
```

**关键设计：**
- 语言检测在 rules 之后、custom instructions 之前
- custom instructions 优先级最高（用户可覆盖自动规则）
- 检测结果应缓存到 `_cachedProjectLanguage` 字段，避免每次请求重新扫描

**缓存实现（AgentExecutor 类级别）：**

```csharp
private ProjectLanguageDetector.ProjectLanguage? _cachedProjectLanguage;

private ProjectLanguageDetector.ProjectLanguage GetProjectLanguage(string workingDirectory)
{
    if (_cachedProjectLanguage == null)
    {
        _cachedProjectLanguage = ProjectLanguageDetector.Detect(workingDirectory);
    }
    return _cachedProjectLanguage.Value;
}
```

#### 3.2.4 右键菜单命令升级

**修改文件: `Commands/RefactorCommand.cs` 第 49 行**

将当前的通用重构 prompt：
```csharp
var prompt = $"请用中文重构以下来自文件 `{fileName}` 的 {contentType} 代码，以提高可读性、性能和可维护性。请展示改进后的代码并解释修改内容：\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
```

改为语言感知的 prompt：

```csharp
var lang = ProjectLanguageDetector.DetectFromFile(docView.FilePath);
string prompt;
if (lang == ProjectLanguageDetector.ProjectLanguage.Cpp ||
    lang == ProjectLanguageDetector.ProjectLanguage.CppQt)
{
    prompt = $"请用中文重构以下来自文件 `{fileName}` 的 C/C++ 代码。\n\n" +
             "重构要求：\n" +
             "1. 遵循华中数控 Qt-C++ 编码规范（花括号 Allman 风格、命名规范、成员变量 m_ 前缀）\n" +
             "2. 检查并修复内存安全问题（缓冲区溢出、空指针、资源泄漏）\n" +
             "3. 应用 const 正确性\n" +
             "4. 函数入口添加参数有效性检查\n" +
             "5. 提高可读性和可维护性\n\n" +
             "请展示改进后的完整代码，并用中文逐条解释每处修改的原因。\n\n" +
             $"```cpp\n{selectedText}\n```";
}
else
{
    // 保留原有通用 prompt
    prompt = $"请用中文重构以下来自文件 `{fileName}` 的 {contentType} 代码，以提高可读性、性能和可维护性。请展示改进后的代码并解释修改内容：\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
}
```

**修改文件: `Commands/GenerateTestCommand.cs` 第 49 行**

```csharp
var lang = ProjectLanguageDetector.DetectFromFile(docView.FilePath);
string prompt;
if (lang == ProjectLanguageDetector.ProjectLanguage.Cpp ||
    lang == ProjectLanguageDetector.ProjectLanguage.CppQt)
{
    prompt = $"请用中文为以下来自文件 `{fileName}` 的 C/C++ 代码生成单元测试。\n\n" +
             "测试要求：\n" +
             "1. 使用项目现有的测试框架（如 Google Test、CppUnit 或项目自定义框架）\n" +
             "2. 覆盖以下场景：\n" +
             "   - 正常输入的功能验证\n" +
             "   - 边界值（0、最大值、负数、空指针 NULL）\n" +
             "   - 内存安全（缓冲区边界、空指针传入）\n" +
             "   - 错误路径和返回值检查\n" +
             "3. 每个测试用中文注释说明测试目的\n" +
             "4. 使用 Arrange/Act/Assert 模式\n\n" +
             $"```cpp\n{selectedText}\n```";
}
else
{
    prompt = $"请用中文为以下来自文件 `{fileName}` 的 {contentType} 代码生成全面的单元测试，使用 xUnit 框架。请包含边界情况并使用 Arrange/Act/Assert 模式，并用中文注释说明每个测试的目的：\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
}
```

**修改文件: `Commands/ExplainCodeCommand.cs` 第 49 行**

```csharp
var lang = ProjectLanguageDetector.DetectFromFile(docView.FilePath);
string prompt;
if (lang == ProjectLanguageDetector.ProjectLanguage.Cpp ||
    lang == ProjectLanguageDetector.ProjectLanguage.CppQt)
{
    prompt = $"请用中文详细解释以下来自文件 `{fileName}` 的 C/C++ 代码。\n\n" +
             "请覆盖以下方面：\n" +
             "1. 整体功能和设计意图\n" +
             "2. 关键数据结构和算法逻辑\n" +
             "3. 内存管理方式（栈/堆、所有权、生命周期）\n" +
             "4. 可能的安全风险（缓冲区溢出、空指针、类型转换）\n" +
             "5. 如果涉及 HNC_ API 调用，说明其 CNC 领域含义\n\n" +
             $"```cpp\n{selectedText}\n```";
}
else
{
    prompt = $"请用中文详细解释以下来自文件 `{fileName}` 的 {contentType} 代码，包括其功能、逻辑和关键细节：\n\n```{contentType.ToLowerInvariant()}\n{selectedText}\n```";
}
```

#### 3.2.5 TaskComplexityAnalyzer 增强

**修改文件: `Agent/TaskComplexityAnalyzer.cs`**

在现有的 Chinese/English strong signals 正则中增加 C/C++ 特有的复杂任务关键词：

```csharp
// 现有 strong signal 中文模式:
@"分析.*架构|重构|实现.*并.*测试|添加.*并|比较.*和|迁移|优化.*性能"

// 增强为:
@"分析.*架构|重构|实现.*并.*测试|添加.*并|比较.*和|迁移|优化.*性能|内存泄漏|模板特化|多线程.*安全|跨平台.*兼容|头文件.*依赖"

// 现有 strong signal 英文模式:
@"analyze.*architecture|refactor|implement.*and.*test|add.*and|compare.*and|migrate"

// 增强为:
@"analyze.*architecture|refactor|implement.*and.*test|add.*and|compare.*and|migrate|memory leak|template.*specializ|thread.*safe|cross.*platform|include.*depend"
```

**理由：** C/C++ 中"内存泄漏分析"、"模板特化"、"多线程安全"等场景天然是 Complex 级别任务，需要 planning 和完整的 rules 注入。

---

## 四、Phase 3：知识引擎强化（改代码）

### 4.1 当前 SymbolParser 的 C/C++ 能力评估

`SymbolParser.ParseCpp()` 已能提取：
- ✅ class/struct（含模板、继承）
- ✅ enum / enum class
- ✅ namespace
- ✅ typedef
- ✅ #define（过滤 include guard）
- ✅ 顶层 function

**缺失项：**

| 缺失符号 | 重要性 | 对 CNC 代码的影响 |
|----------|--------|-------------------|
| Qt signals/slots | 高 | Qt 项目的核心通信机制 |
| Qt Q_PROPERTY | 中 | Qt 属性系统 |
| HNC_ 枚举值 | 高 | CNC API 常量（如 HNC_CHAN_AXES_MASK） |
| 成员函数 | 中 | 当前只提取顶层函数，class 内方法被忽略 |
| 全局变量 | 低 | 部分 CNC 模块用全局状态 |

### 4.2 SymbolParser 增强方案

#### 4.2.1 新增 Qt 信号槽解析

**修改文件: `Knowledge/SymbolParser.cs`**

在 C/C++ 正则定义区新增：

```csharp
// Qt-specific patterns
private static readonly Regex QtSignalSlotRegex = new Regex(
    @"^\s*(?:signals|public\s+slots|protected\s+slots|private\s+slots)\s*:",
    RegexOptions.Compiled);

private static readonly Regex QtPropertyRegex = new Regex(
    @"^\s*Q_PROPERTY\s*\(\s*(\w[\w<>:]*)\s+(\w+)\s",
    RegexOptions.Compiled);
```

在 `ParseCpp()` 方法中，增加对 `signals:` 和 `slots:` 段落内函数的识别。当检测到 `signals:` 标记时，后续的函数声明标记为 `SymbolKind.Signal`；检测到 `*slots:` 标记时，标记为 `SymbolKind.Slot`。

#### 4.2.2 新增 HNC 枚举值提取

CNC 代码中大量使用枚举常量（如 `HNC_CHAN_AXES_MASK`），当前只提取枚举类型名，不提取枚举值。

```csharp
// 在 enum 解析逻辑后，增加枚举值提取（仅限 HNC_ 前缀的重要枚举）
private static readonly Regex HncEnumValueRegex = new Regex(
    @"^\s*(HNC_\w+)\s*(?:=\s*\d+)?\s*,?\s*(?:/\*.*?\*/|//.*)?$",
    RegexOptions.Compiled);
```

**设计权衡：** 不是所有枚举值都值得索引。只提取 `HNC_` 前缀的枚举值（这些是 API 常量），避免索引膨胀。

#### 4.2.3 成员函数提取（类内方法）

当前 `ParseCpp()` 只提取顶层函数，class 内的方法声明被跳过。需要在类体内（brace depth > 0 且在 class 范围内）也进行函数匹配：

```csharp
// 在 class body 解析循环中（当前已有 method counting 逻辑 lines 160-184）
// 将 method counting 的正则匹配扩展为：同时记录方法名和所属类名
// 生成 Summary: "ClassName::MethodName"
```

**Token 影响：** 这会显著增加符号数量（可能从 2-3 万增到 5-8 万）。但 `KnowledgeContextProvider` 有 top-10 + token budget 限制，不会影响 prompt 大小。只会提升检索精度。

### 4.3 KnowledgeContextProvider 增强

#### 4.3.1 C/C++ 特有的停用词过滤

**修改文件: `Knowledge/KnowledgeContextProvider.cs`**

当前停用词列表是英语通用的。为 C/C++ 项目增加领域停用词：

```csharp
// 当前停用词
private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "the", "is", "a", "an", "in", "of", "to", "and", "for", "with",
    "what", "how", "does", "this", "that"
};

// 增加 C/C++ 关键字停用词（这些词在几乎所有符号中出现，无区分度）
private static readonly HashSet<string> CppStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "void", "int", "char", "const", "static", "return", "include",
    "class", "struct", "enum", "public", "private", "protected",
    "virtual", "override", "nullptr", "null", "true", "false",
    "bit", "bit8", "bit16", "bit32", "bit64"  // 项目自定义类型高频出现
};
```

**理由：** 当用户查询 "Bit32 类型的函数" 时，"Bit32" 出现在几乎所有符号中，IDF 值极低，不应参与评分。

#### 4.3.2 TF-IDF 增强：C/C++ 语义权重

```csharp
// 在 ComputeScore() 中增加 C/C++ 语义加权
private double ComputeScore(HashSet<string> queryTerms, SymbolInfo symbol)
{
    double score = 0;

    foreach (var term in queryTerms)
    {
        // 现有: exact name match +10, name contains +5, keyword match +IDF
        if (symbol.Name.Equals(term, StringComparison.OrdinalIgnoreCase))
            score += 10.0;
        else if (symbol.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 5.0;

        // NEW: Namespace match bonus (C++ specific)
        if (symbol.Namespace != null &&
            symbol.Namespace.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 3.0;

        // NEW: Base class match bonus
        if (symbol.BaseType != null &&
            symbol.BaseType.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 4.0;

        // Existing keyword IDF match
        // ...
    }

    return score;
}
```

### 4.4 新增能力：C/C++ 头文件依赖图（后续迭代）

这是更远期的能力，不在本次方案范围内，但值得记录：

- 解析 `#include` 关系构建依赖图
- 当用户修改 `.h` 文件时，自动提示影响范围
- 在重构场景中提供 "哪些 .cpp 会受影响" 的上下文

---

## 五、实施路线

### 5.1 优先级排序

```
Phase 1 (零代码) ──→ Phase 2.4 (右键命令) ──→ Phase 2.2-2.3 (SystemPrompt + AgentExecutor)
      ↓                      ↓                              ↓
   立即可做              改 3 个文件                     改 3 个文件
  效果立即可见           用户感知最强                   架构级改进
                                                          ↓
                                                    Phase 3 (知识引擎)
                                                     改 2 个文件
                                                    效果渐进提升
```

### 5.2 各 Phase 的工作量估算

| Phase | 涉及文件 | 新增文件 | 改动行数估算 | 风险 |
|-------|---------|---------|-------------|------|
| Phase 1 | 0（仅 .aica-rules 配置） | 5 个 .md | 0 行代码 | 极低 |
| Phase 2 | 5 个 .cs 文件 | 1 个新 .cs | ~200 行 | 低 |
| Phase 3 | 2 个 .cs 文件 | 0 | ~150 行 | 中（需回归测试） |
| **合计** | **7 个 .cs** | **1 个 .cs + 5 个 .md** | **~350 行** | |

### 5.3 验证矩阵

| 验证场景 | Phase 1 后 | Phase 2 后 | Phase 3 后 |
|----------|-----------|-----------|-----------|
| 生成的 C++ 代码遵循 Allman 花括号 | ✅ | ✅ | ✅ |
| 生成的代码使用 Bit32 而非 int | ✅ | ✅ | ✅ |
| 成员变量自动加 m_ 前缀 | ✅ | ✅ | ✅ |
| 自动添加 doxygen 文件头注释 | ✅ | ✅ | ✅ |
| 右键重构包含内存安全检查 | ❌ | ✅ | ✅ |
| 右键测试生成用 C++ 框架而非 xUnit | ❌ | ✅ | ✅ |
| 代码解释包含 CNC 领域术语 | ❌ | ✅ | ✅ |
| 自由对话中自动识别 C++ 项目 | ❌ | ✅ | ✅ |
| 查询 HNC_CHAN_AXES_MASK 能找到定义 | ❌ | ❌ | ✅ |
| 查询 Qt signal 能定位信号声明 | ❌ | ❌ | ✅ |
| 查询类名能列出成员方法 | 部分 | 部分 | ✅ |

### 5.4 回滚方案

- **Phase 1:** 删除 `.aica-rules/cpp-*.md` 文件即可完全回滚
- **Phase 2:** 所有改动通过 `ProjectLanguageDetector` 守护，非 C/C++ 项目走原有逻辑，零影响
- **Phase 3:** SymbolParser 增强是附加逻辑，不修改现有解析路径

---

## 六、超出本方案的未来方向

以下方向已识别但不在当前方案内，留作后续迭代：

1. **多模态扩展：** MiniMax-M2.5 支持图片输入，可实现 "截图识别 UI Bug" 场景
2. **MISRA C 规则引擎：** 将 MISRA C 的 143 条规则编码为可检查的规则集，在代码审查时自动应用
3. **构建系统集成：** 待用户确认 CMake/MSBuild 后，可加入"一键编译检查"能力
4. **头文件依赖图：** `#include` 关系解析，支持影响范围分析
5. **项目级 .aica-rules 分发机制：** 通过 VS 项目模板自动初始化 C/C++ 规范文件

---

## 附录 A：华中数控 Qt-C++ 编码规范要点摘要

**来源:** `D:\project\软件开发部Qt-C++语言编程规范(1).doc`

### A.1 可靠性规则
- 宏参数和整体加括号
- 除零保护
- 禁止隐式类型转换，变量必须初始化
- 禁止 public 成员变量（用 Get/Set）
- 禁止 goto
- const 修饰输入指针
- 溢出保护（检查运算结果范围）

### A.2 内存安全规则
- 数组访问：下标检查 + NULL 检查
- 数组声明用 `[]` 不用指针 `*`
- 禁止运行时 malloc/new（仅初始化/退出时允许）
- 函数内数组不超过 4096 字节
- free 后置 NULL
- 缓冲区参数传长度
- 禁止 strcpy/sprintf → strlcpy/snprintf

### A.3 文件 I/O 规则
- 所有文件操作检查返回值
- fopen 前 stat 校验
- 写入后 fflush + fsync
- fopen/fclose 配对（含错误路径）
- 新增数据文件：文件头 + CRC 校验码
- 写入前检测磁盘空间

### A.4 程序版式
- Allman 花括号风格（独占一行）
- 4 空格缩进
- 80 字符行宽
- `*` `&` 紧靠变量名
- 一行一事
- 函数名后不留空格

### A.5 命名规范
- 类/结构体/枚举：UpperCamelCase
- 函数：UpperCamelCase
- 变量/参数：lowerCamelCase
- 宏/常量：ALL_CAPS
- 静态变量：s_ 前缀
- 成员变量：m_ + 类型缩写前缀
- 文件名：小写 + wg/dlg 前缀
- slot 命名加 Slot 前缀，signal 加 Single 前缀

### A.6 注释规范
- doxygen 格式（@file/@brief/@param/@return）
- 文件头版权注释（华中数控模板）
- 注释率 20-30%
- 大量代码注释用 `#if 0` 不用 `/* */`
- [in]/[out] 标注参数方向

### A.7 Qt 专属规则
- include 顺序：标准库 → Qt → API → APP
- 前向声明放 QT_BEGIN_NAMESPACE/QT_END_NAMESPACE
- 成员排序：public → slots → signals → protected → private
- 颜色配置在 .qss 不在代码中
- 翻译：wg*.cpp 用 TR，其他用 QObject::TR
- 浮点转字符串指定精度
- 通道号不硬编码

### A.8 可移植性规则
- `#ifdef _LINUX` 区分平台
- include 不用绝对路径
- UTF-8 编码
- 路径用 QDir::toNativeSeparators / DIR_SEPARATOR
- 数据结构注意字节对齐，避免 long 类型

### A.9 性能规则
- 循环中避免重复函数调用（如 strlen 提到循环外）
- 禁止周期性调用 setStyleSheet
- 避免周期性函数中的重复处理
