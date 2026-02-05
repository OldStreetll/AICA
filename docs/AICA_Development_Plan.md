# AICA - AI Coding Assistant for Visual Studio 2022

## 完整开发计划书

> **项目代号**: AICA (AI Coding Assistant)  
> **目标平台**: Visual Studio 2022 (17.0+)  
> **创建日期**: 2026-02-05  
> **文档版本**: v1.0

---

## 目录

1. [项目概述](#一项目概述)
2. [需求分析与拆解](#二需求分析与拆解)
3. [技术可行性分析](#三技术可行性分析)
4. [架构设计](#四架构设计)
5. [阶段性任务规划](#五阶段性任务规划)
6. [难度分析与风险评估](#六难度分析与风险评估)
7. [验收标准](#七验收标准)
8. [附录](#八附录)

---

## 一、项目概述

### 1.1 项目背景

开发一款运行在 Visual Studio 2022 平台的 AI 辅助编程插件，调用私有化部署的大语言模型，提供类似 Cline/Cursor/Windsurf 的智能编程体验。**所有功能完全离线/内网运行，不接入任何外部网络**。

### 1.2 核心目标

| 目标 | 描述 |
|------|------|
| **AI Agent 能力** | 具备自主规划、工具调用、多步骤任务执行能力 |
| **代码理解** | 理解整个项目结构，支持语义搜索 |
| **代码操作** | 读取、编辑、创建文件，精确差异化修改 |
| **命令执行** | 安全执行终端命令，分级审批机制 |
| **代码补全** | 智能上下文感知的实时代码补全 |
| **完全离线** | 所有功能不依赖外部网络 |

### 1.3 参考项目

| 项目 | 参考价值 |
|------|----------|
| **Cline** | Agent 架构、工具设计、安全机制、Prompt 工程 |
| **AI-Studio** | VS2022 插件开发模式、右键菜单集成、选项配置 |

---

## 二、需求分析与拆解

### 2.1 功能需求矩阵

#### 2.1.1 P0 - 核心必备功能

| 功能模块 | 需求描述 | 子需求 |
|----------|----------|--------|
| **Agent 执行引擎** | LLM 驱动的自主任务执行 | Agent Loop、工具分发、结果反馈 |
| **文件操作工具** | 读写文件能力 | read_file、write_to_file、edit（差异化编辑） |
| **代码搜索工具** | 在项目中搜索代码 | grep_search、find_by_name、code_search（语义） |
| **LLM 通信** | 与私有大模型通信 | HTTP Client、流式响应、Tool Calling 解析 |
| **对话界面** | 用户交互界面 | Tool Window、消息渲染、工具调用展示 |
| **安全机制** | 操作安全控制 | 文件访问策略、操作确认、.aicaignore |

#### 2.1.2 P1 - 重要功能

| 功能模块 | 需求描述 | 子需求 |
|----------|----------|--------|
| **命令执行** | 终端命令能力 | run_command、命令分级、输出捕获 |
| **任务规划** | 任务分解与跟踪 | update_plan、计划面板 UI |
| **上下文管理** | 智能上下文收集 | Token 预算、优先级裁剪、相关文件收集 |
| **右键菜单** | IDE 集成 | 解释代码、重构建议、生成测试 |

#### 2.1.3 P2 - 增强功能

| 功能模块 | 需求描述 | 子需求 |
|----------|----------|--------|
| **代码补全** | 实时补全建议 | IAsyncCompletionSource、Inline 显示、Tab 接受 |
| **代码索引** | 项目语义索引 | Roslyn 分析、本地 Embedding、向量存储 |
| **Diff 预览** | 编辑差异预览 | DiffViewProvider、接受/拒绝操作 |

### 2.2 非功能需求

| 类别 | 需求 | 指标 |
|------|------|------|
| **网络隔离** | 完全离线运行 | 零外网请求 |
| **性能** | 响应及时 | 补全 < 500ms，对话首字 < 2s |
| **安全** | 操作可控 | 危险操作 100% 需确认 |
| **兼容性** | VS2022 支持 | 17.0 - 18.0 |
| **稳定性** | 不影响 IDE | 内存增量 < 300MB |

### 2.3 需求依赖关系图

```
                    ┌─────────────────┐
                    │   LLM 通信层    │ ◀── 基础依赖
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │ 工具系统  │  │ 安全机制  │  │ 上下文   │
        └────┬─────┘  └────┬─────┘  └────┬─────┘
             │             │             │
             └─────────────┼─────────────┘
                           ▼
                    ┌─────────────────┐
                    │  Agent 执行引擎  │ ◀── 核心能力
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │ 对话界面  │  │ 右键菜单  │  │ 代码补全  │
        └──────────┘  └──────────┘  └──────────┘
                           │
                           ▼
                    ┌─────────────────┐
                    │   代码索引系统   │ ◀── 增强能力
                    └─────────────────┘
```

---

## 三、技术可行性分析

### 3.1 VS2022 扩展开发可行性

#### 3.1.1 技术验证（基于 AI-Studio 分析）

| 能力 | 可行性 | 验证依据 |
|------|--------|----------|
| **VSIX 插件开发** | ✅ 成熟 | AI-Studio 已实现，使用 Community.VisualStudio.Toolkit |
| **Tool Window** | ✅ 成熟 | VS SDK 原生支持，AI-Studio 已实现 |
| **右键菜单集成** | ✅ 成熟 | .vsct 文件定义，AI-Studio 已实现 |
| **选项页面** | ✅ 成熟 | BaseOptionModel 模式，AI-Studio 已实现 |
| **代码补全** | ✅ 可行 | IAsyncCompletionSource 接口 |
| **编辑器操作** | ✅ 可行 | ITextBuffer、ITextView API |

#### 3.1.2 AI-Studio 关键技术栈

```csharp
// AI-Studio 使用的核心包
Community.VisualStudio.Toolkit.17  // VS 扩展工具包
Microsoft.VisualStudio.SDK         // VS SDK
Microsoft.VSSDK.BuildTools         // 构建工具
Microsoft.Extensions.AI            // AI 抽象层
Microsoft.CodeAnalysis.CSharp      // Roslyn
Markdig                            // Markdown 渲染
```

### 3.2 Agent 架构可行性

#### 3.2.1 技术验证（基于 Cline 分析）

| 能力 | 可行性 | 实现方案 |
|------|--------|----------|
| **Agent Loop** | ✅ 可行 | 参考 Cline Task 类，C# async/await 实现 |
| **Tool Calling** | ✅ 可行 | LLM 返回 JSON，解析后分发执行 |
| **流式响应** | ✅ 可行 | HTTP SSE，IAsyncEnumerable 处理 |
| **差异化编辑** | ✅ 可行 | 字符串查找替换，唯一性校验 |
| **安全机制** | ✅ 可行 | 命令分类器 + 确认对话框 |

#### 3.2.2 Cline 核心架构映射

| Cline 组件 | C# 实现 |
|------------|---------|
| `Task` (Agent Loop) | `AgentExecutor.cs` |
| `ToolExecutor` | `ToolDispatcher.cs` |
| `IFullyManagedTool` | `IAgentTool` 接口 |
| `ApiHandler` | `LLMClient.cs` |
| `CommandPermissionController` | `CommandClassifier.cs` |
| `ClineIgnoreController` | `FileAccessPolicy.cs` |

### 3.3 LLM 集成可行性

#### 3.3.1 OpenAI Compatible API

```
私有化 LLM 服务（vLLM/TGI）提供 OpenAI 兼容 API：
├── POST /v1/chat/completions     ✅ 标准接口
├── stream: true                   ✅ 流式支持
├── tools: [...]                   ✅ Tool Calling 支持
└── 内网部署                       ✅ 完全可控
```

#### 3.3.2 技术验证

```csharp
// AI-Studio 的 LLM 调用方式（可直接复用）
IChatClient client = new OpenAI.Chat.ChatClient(
    model: "qwen3-coder",
    credential: new ApiKeyCredential(apiKey),
    options: new OpenAIClientOptions { 
        Endpoint = new Uri("http://内网地址:8000/v1/") 
    }
).AsIChatClient();

await foreach (var chunk in client.GetStreamingResponseAsync(messages))
{
    // 流式处理
}
```

### 3.4 可行性结论

| 方面 | 结论 | 风险等级 |
|------|------|----------|
| **VS2022 插件开发** | 完全可行，有成熟参考 | 🟢 低 |
| **Agent 架构** | 可行，需要自行实现核心逻辑 | 🟡 中 |
| **LLM 集成** | 完全可行，标准 API | 🟢 低 |
| **代码补全** | 可行，VS API 支持 | 🟡 中 |
| **代码索引** | 可行，但工作量大 | 🟡 中 |
| **完全离线** | 可行，需严格控制依赖 | 🟢 低 |

---

## 四、架构设计

### 4.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Visual Studio 2022                               │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                        AICA Plugin                             │  │
│  │                                                                │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │                    表现层 (UI)                           │  │  │
│  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐   │  │  │
│  │  │  │对话窗口   │ │任务面板   │ │右键菜单   │ │设置页面   │   │  │  │
│  │  │  │ToolWindow│ │PlanPanel │ │ContextMenu│ │ Options  │   │  │  │
│  │  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘   │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  │                              │                                 │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │                   Agent 层                               │  │  │
│  │  │  ┌──────────────────────────────────────────────────┐   │  │  │
│  │  │  │              AgentExecutor                        │   │  │  │
│  │  │  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐    │   │  │  │
│  │  │  │  │Planner │ │Dispatch│ │Context │ │ Safety │    │   │  │  │
│  │  │  │  └────────┘ └────────┘ └────────┘ └────────┘    │   │  │  │
│  │  │  └──────────────────────────────────────────────────┘   │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  │                              │                                 │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │                   工具层 (Tools)                         │  │  │
│  │  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐│  │  │
│  │  │  │ReadFile│ │  Edit  │ │ Search │ │RunCmd  │ │ Plan   ││  │  │
│  │  │  └────────┘ └────────┘ └────────┘ └────────┘ └────────┘│  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  │                              │                                 │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │                  基础设施层                               │  │  │
│  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐   │  │  │
│  │  │  │LLMClient │ │ Roslyn   │ │VectorDB  │ │ Process  │   │  │  │
│  │  │  │HTTP/SSE  │ │ Analyzer │ │ SQLite   │ │ Manager  │   │  │  │
│  │  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘   │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    │      内网 LLM 服务             │
                    │   (vLLM / TGI / Triton)       │
                    │   Qwen3-Coder / DeepSeek      │
                    └───────────────────────────────┘
```

### 4.2 项目结构

```
src/
├── AICA.Core/                           # 核心业务库（.NET Standard 2.0）
│   ├── Agent/                           # Agent 核心
│   │   ├── AgentExecutor.cs             # Agent 执行循环
│   │   ├── AgentContext.cs              # Agent 上下文
│   │   └── AgentPlanner.cs              # 任务规划器
│   │
│   ├── Tools/                           # 工具实现
│   │   ├── IAgentTool.cs                # 工具接口
│   │   ├── ToolDispatcher.cs            # 工具分发器
│   │   ├── ToolResult.cs                # 工具结果
│   │   ├── Handlers/                    # 工具处理器
│   │   │   ├── ReadFileTool.cs          # 读文件
│   │   │   ├── EditFileTool.cs          # 差异化编辑
│   │   │   ├── WriteFileTool.cs         # 创建文件
│   │   │   ├── GrepSearchTool.cs        # 文本搜索
│   │   │   ├── FindByNameTool.cs        # 文件名搜索
│   │   │   ├── ListDirTool.cs           # 目录列表
│   │   │   ├── RunCommandTool.cs        # 命令执行
│   │   │   └── UpdatePlanTool.cs        # 计划管理
│   │   └── Definitions/                 # 工具定义
│   │       └── ToolDefinitions.cs       # 工具 Schema
│   │
│   ├── LLM/                             # LLM 客户端
│   │   ├── ILLMClient.cs                # 客户端接口
│   │   ├── LLMClient.cs                 # HTTP 实现
│   │   ├── StreamingHandler.cs          # 流式处理
│   │   ├── ToolCallParser.cs            # 工具调用解析
│   │   └── Models/                      # 数据模型
│   │       ├── ChatMessage.cs
│   │       ├── ChatCompletionRequest.cs
│   │       ├── ChatCompletionChunk.cs
│   │       └── ToolCall.cs
│   │
│   ├── Context/                         # 上下文管理
│   │   ├── IContextManager.cs           # 接口
│   │   ├── ContextManager.cs            # 实现
│   │   ├── ContextWindow.cs             # 上下文窗口
│   │   └── TokenCounter.cs              # Token 计数
│   │
│   ├── Security/                        # 安全控制
│   │   ├── SafetyGuard.cs               # 安全守卫
│   │   ├── CommandClassifier.cs         # 命令分类
│   │   └── FileAccessPolicy.cs          # 文件访问策略
│   │
│   └── Prompt/                          # Prompt 工程
│       ├── SystemPromptBuilder.cs       # System Prompt 构建
│       └── ToolDescriptionBuilder.cs    # 工具描述构建
│
├── AICA.VSIX/                           # VS 插件项目（.NET Framework 4.8）
│   ├── AICAPackage.cs                   # 插件入口
│   ├── source.extension.vsixmanifest    # 清单文件
│   ├── VSCommandTable.vsct              # 命令定义
│   │
│   ├── ToolWindows/                     # 工具窗口
│   │   ├── ChatToolWindow.cs            # 对话窗口
│   │   ├── ChatToolWindowControl.xaml   # 对话界面
│   │   ├── ChatViewModel.cs             # 视图模型
│   │   └── MessageRenderer.cs           # 消息渲染
│   │
│   ├── Commands/                        # 命令
│   │   ├── OpenChatCommand.cs           # 打开对话
│   │   ├── ExplainCodeCommand.cs        # 解释代码
│   │   ├── RefactorCommand.cs           # 重构建议
│   │   └── GenerateTestCommand.cs       # 生成测试
│   │
│   ├── Options/                         # 设置
│   │   ├── GeneralOptions.cs            # 通用设置
│   │   ├── CommandsOptions.cs           # 命令设置
│   │   └── SecurityOptions.cs           # 安全设置
│   │
│   └── Resources/                       # 资源
│       ├── Icons/
│       └── Styles/
│
├── AICA.Indexing/                       # 代码索引库（可选，Phase 后期）
│   ├── CodeIndexer.cs
│   ├── VectorStore.cs
│   └── EmbeddingService.cs
│
└── AICA.Tests/                          # 单元测试
    ├── Agent/
    ├── Tools/
    └── LLM/
```

### 4.3 核心接口设计

#### 4.3.1 工具接口

```csharp
public interface IAgentTool
{
    /// <summary>工具名称（与 LLM 调用名匹配）</summary>
    string Name { get; }
    
    /// <summary>获取工具定义（用于 System Prompt）</summary>
    ToolDefinition GetDefinition();
    
    /// <summary>流式生成时的部分处理（可选）</summary>
    Task HandlePartialAsync(ToolCall call, IUIContext ui);
    
    /// <summary>执行工具</summary>
    Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context);
}
```

#### 4.3.2 LLM 客户端接口

```csharp
public interface ILLMClient
{
    /// <summary>流式对话</summary>
    IAsyncEnumerable<LLMChunk> StreamChatAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken ct = default);
    
    /// <summary>取消当前请求</summary>
    void Abort();
}
```

#### 4.3.3 Agent 执行器

```csharp
public interface IAgentExecutor
{
    /// <summary>执行用户请求</summary>
    IAsyncEnumerable<AgentStep> ExecuteAsync(
        string userRequest,
        IAgentContext context,
        CancellationToken ct = default);
}
```

---

## 五、阶段性任务规划

### 5.1 总体时间线

```
Week 1-2    Week 3-4    Week 5-7    Week 8-9    Week 10-11   Week 12-14
   │           │           │           │            │            │
   ▼           ▼           ▼           ▼            ▼            ▼
┌──────┐   ┌──────┐   ┌──────┐   ┌──────┐    ┌──────┐    ┌──────┐
│Phase1│──▶│Phase2│──▶│Phase3│──▶│Phase4│───▶│Phase5│───▶│Phase6│
│ 基础 │   │ LLM  │   │Agent │   │ 搜索 │    │ 命令 │    │ 优化 │
│ 框架 │   │ 集成 │   │ 核心 │   │ 工具 │    │ 执行 │    │ 测试 │
└──────┘   └──────┘   └──────┘   └──────┘    └──────┘    └──────┘
    │           │          │          │           │           │
    ▼           ▼          ▼          ▼           ▼           ▼
  [M1]       [M2]       [M3]       [M4]        [M5]        [M6]
 插件可用   LLM通信   Agent工作  搜索可用    命令可用    发布版本
```

---

### Phase 1: 基础框架搭建 (Week 1-2)

**目标**: 创建可运行的 VS2022 插件骨架

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 1.1 | 创建解决方案和项目结构 | 0.5d | 🟢 简单 |
| 1.2 | 配置 VSIX 项目（参考 AI-Studio） | 1d | 🟢 简单 |
| 1.3 | 实现 Package 和基础命令 | 1d | 🟢 简单 |
| 1.4 | 创建 Chat Tool Window 骨架 | 1.5d | 🟡 中等 |
| 1.5 | 实现选项页面（LLM 配置） | 1d | 🟢 简单 |
| 1.6 | 搭建日志系统 | 0.5d | 🟢 简单 |
| 1.7 | 创建右键菜单框架 | 1d | 🟢 简单 |

**交付物**:
- 可安装的 VSIX 插件
- 可打开的对话窗口
- 可配置的选项页面
- 右键菜单入口

**里程碑 M1**: 插件可在 VS2022 中加载和使用

---

### Phase 2: LLM 通信层 (Week 3-4)

**目标**: 实现与私有 LLM 的完整通信能力

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 2.1 | 实现 LLMClient（HTTP 请求） | 1.5d | 🟡 中等 |
| 2.2 | 实现流式响应处理（SSE） | 2d | 🟡 中等 |
| 2.3 | 实现 Tool Calling 解析 | 1.5d | 🟡 中等 |
| 2.4 | 对话界面绑定 LLM | 1.5d | 🟡 中等 |
| 2.5 | 流式消息渲染（Markdown） | 2d | 🟡 中等 |
| 2.6 | 错误处理和重试机制 | 1d | 🟢 简单 |

**交付物**:
- 可与内网 LLM 通信
- 支持流式输出显示
- Tool Calling JSON 解析

**里程碑 M2**: 可在对话窗口与 LLM 进行基础对话

---

### Phase 3: Agent 核心实现 (Week 5-7)

**目标**: 实现 Agent 执行循环和文件操作工具

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 3.1 | 设计并实现 IAgentTool 接口 | 1d | 🟡 中等 |
| 3.2 | 实现 ToolDispatcher 工具分发 | 1.5d | 🟡 中等 |
| 3.3 | 实现 AgentExecutor 执行循环 | 3d | 🔴 困难 |
| 3.4 | 实现 read_file 工具 | 1d | 🟢 简单 |
| 3.5 | 实现 edit 工具（差异化编辑）** | 3d | 🔴 困难 |
| 3.6 | 实现 write_to_file 工具 | 1d | 🟢 简单 |
| 3.7 | 实现 list_dir 工具 | 0.5d | 🟢 简单 |
| 3.8 | 实现 SafetyGuard 安全检查 | 1.5d | 🟡 中等 |
| 3.9 | 实现 FileAccessPolicy (.aicaignore) | 1d | 🟡 中等 |
| 3.10 | UI 集成：工具调用展示 | 2d | 🟡 中等 |
| 3.11 | UI 集成：确认对话框 | 1d | 🟢 简单 |

**交付物**:
- Agent 可执行多步骤任务
- 可读取、编辑、创建文件
- 文件操作需用户确认

**里程碑 M3**: Agent 可完成"创建一个类"等基础任务

---

### Phase 4: 搜索工具实现 (Week 8-9)

**目标**: 实现代码搜索能力

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 4.1 | 实现 grep_search 工具 | 1.5d | 🟡 中等 |
| 4.2 | 实现 find_by_name 工具 | 1d | 🟢 简单 |
| 4.3 | 实现上下文收集器 | 2d | 🟡 中等 |
| 4.4 | 实现 Token 预算管理 | 1.5d | 🟡 中等 |
| 4.5 | 实现上下文截断策略 | 1d | 🟡 中等 |
| 4.6 | 实现 update_plan 工具 | 1d | 🟢 简单 |
| 4.7 | UI：任务计划面板 | 2d | 🟡 中等 |

**交付物**:
- 可在项目中搜索代码
- 智能上下文收集
- 任务计划可视化

**里程碑 M4**: Agent 可理解并操作项目中的代码

---

### Phase 5: 命令执行与安全 (Week 10-11)

**目标**: 实现终端命令能力和完整安全机制

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 5.1 | 实现 run_command 工具 | 2d | 🟡 中等 |
| 5.2 | 实现命令输出捕获 | 1d | 🟢 简单 |
| 5.3 | 实现 CommandClassifier 命令分级 | 2d | 🟡 中等 |
| 5.4 | 实现后台命令支持 | 1.5d | 🟡 中等 |
| 5.5 | UI：命令确认对话框 | 1d | 🟢 简单 |
| 5.6 | UI：命令输出显示 | 1.5d | 🟡 中等 |
| 5.7 | 完善右键菜单命令 | 2d | 🟢 简单 |

**交付物**:
- Agent 可安全执行命令
- 命令分级审批机制
- 完整的右键菜单功能

**里程碑 M5**: Agent 可执行 dotnet build 等命令

---

### Phase 6: 优化与测试 (Week 12-14)

**目标**: 性能优化、全面测试、文档完善

| 任务 ID | 任务描述 | 工时 | 难度 |
|---------|----------|------|------|
| 6.1 | 性能分析与优化 | 2d | 🟡 中等 |
| 6.2 | 单元测试（Agent、Tools） | 3d | 🟡 中等 |
| 6.3 | 集成测试（端到端场景） | 2d | 🟡 中等 |
| 6.4 | Bug 修复 | 3d | 🟡 中等 |
| 6.5 | 用户文档编写 | 1.5d | 🟢 简单 |
| 6.6 | 部署文档编写 | 1d | 🟢 简单 |
| 6.7 | 最终打包与发布准备 | 1d | 🟢 简单 |

**交付物**:
- 性能达标
- 测试覆盖
- 完整文档

**里程碑 M6**: 发布就绪版本

---

### 5.2 里程碑检查点

| 里程碑 | 时间 | 验收标准 |
|--------|------|----------|
| **M1** | Week 2 | 插件可安装，窗口可打开 |
| **M2** | Week 4 | 可与 LLM 对话，流式输出 |
| **M3** | Week 7 | Agent 可创建/编辑文件 |
| **M4** | Week 9 | Agent 可搜索代码 |
| **M5** | Week 11 | Agent 可执行命令 |
| **M6** | Week 14 | 发布就绪 |

---

## 六、难度分析与风险评估

### 6.1 技术难度矩阵

| 模块 | 难度 | 原因 | 对策 |
|------|------|------|------|
| **VSIX 插件基础** | 🟢 低 | 有 AI-Studio 参考 | 直接参考实现 |
| **LLM HTTP 通信** | 🟢 低 | 标准 HTTP | 使用 HttpClient |
| **流式响应处理** | 🟡 中 | SSE 解析 | IAsyncEnumerable |
| **Tool Calling 解析** | 🟡 中 | JSON 结构解析 | System.Text.Json |
| **Agent 执行循环** | 🔴 高 | 状态管理复杂 | 参考 Cline 架构 |
| **差异化编辑** | 🔴 高 | 唯一性匹配 | 充分测试 |
| **命令安全分级** | 🟡 中 | 规则设计 | 白名单优先 |
| **上下文管理** | 🟡 中 | Token 预算 | 优先级裁剪 |
| **WPF UI 开发** | 🟡 中 | MVVM 模式 | 使用 Toolkit |

### 6.2 风险评估与对策

| 风险 | 可能性 | 影响 | 对策 |
|------|--------|------|------|
| **LLM 响应延迟** | 🟡 中 | 🟡 中 | 流式输出、请求缓存 |
| **差异化编辑失败** | 🟡 中 | 🔴 高 | 提供更多上下文、回退机制 |
| **Agent 死循环** | 🟢 低 | 🔴 高 | 最大迭代限制、超时控制 |
| **命令执行危险** | 🟢 低 | 🔴 高 | 严格白名单、强制确认 |
| **Token 超限** | 🟡 中 | 🟡 中 | 智能截断、压缩 |
| **VS 兼容性问题** | 🟢 低 | 🟡 中 | 多版本测试 |
| **内存泄漏** | 🟡 中 | 🟡 中 | 资源释放、性能监控 |

### 6.3 关键技术难点详解

#### 难点 1: Agent 执行循环

```
挑战：
├── 状态管理复杂（多轮对话、工具结果）
├── 错误恢复（工具失败后如何继续）
├── 死循环检测（LLM 可能陷入重复）
└── 并发控制（用户取消、超时）

解决方案：
├── 参考 Cline Task 类设计
├── 实现清晰的状态机
├── 最大迭代次数限制（50 次）
└── CancellationToken 贯穿全流程
```

#### 难点 2: 差异化编辑

```
挑战：
├── old_string 必须唯一匹配
├── 空白字符敏感
├── 大文件性能
└── 编辑冲突处理

解决方案：
├── 精确字符串匹配 + 唯一性校验
├── 失败时返回详细错误信息
├── 要求 LLM 提供更多上下文
└── Diff 预览 + 用户确认
```

#### 难点 3: 上下文管理

```
挑战：
├── Token 预算有限（32K-128K）
├── 上下文相关性判断
├── 大项目文件太多
└── 截断后信息丢失

解决方案：
├── 优先级裁剪（当前文件 > 相关文件 > 项目结构）
├── 关键信息永远保留（首条消息、最近 N 轮）
├── 截断提示告知 LLM
└── 按需加载（LLM 可请求更多文件）
```

---

## 七、验收标准

### 7.1 功能验收

| 功能 | 验收标准 | 测试方法 |
|------|----------|----------|
| **插件加载** | VS2022 中正常加载 | 手动安装测试 |
| **LLM 通信** | 可与内网 LLM 对话 | 发送消息验证 |
| **Agent 文件操作** | 可创建/读取/编辑文件 | 执行"创建类"任务 |
| **差异化编辑** | 精确修改代码片段 | 执行"添加方法"任务 |
| **代码搜索** | 可搜索项目中的代码 | 执行"找到所有控制器"任务 |
| **命令执行** | 可安全执行命令 | 执行"运行 dotnet build" |
| **安全确认** | 危险操作需确认 | 尝试删除文件 |

### 7.2 性能验收

| 指标 | 目标 | 测量方法 |
|------|------|----------|
| **对话首字延迟** | < 2s | 计时测量 |
| **文件读取** | < 100ms | 性能计数器 |
| **编辑应用** | < 500ms | 性能计数器 |
| **内存增量** | < 300MB | 任务管理器 |
| **启动时间** | < 3s | VS 启动计时 |

### 7.3 安全验收

| 检查项 | 标准 |
|--------|------|
| **网络隔离** | 零外网请求（网络监控验证） |
| **文件保护** | .aicaignore 生效 |
| **命令分级** | 危险命令 100% 需确认 |
| **数据安全** | API Key 加密存储 |

---

## 八、附录

### 8.1 核心工具定义

| 工具名 | 功能 | 安全级别 |
|--------|------|----------|
| `read_file` | 读取文件内容 | ✅ 安全 |
| `edit` | 差异化编辑文件 | ⚠️ 需确认 |
| `write_to_file` | 创建新文件 | ⚠️ 需确认 |
| `list_dir` | 列出目录 | ✅ 安全 |
| `grep_search` | 文本搜索 | ✅ 安全 |
| `find_by_name` | 文件名搜索 | ✅ 安全 |
| `run_command` | 执行命令 | 🔶 分级确认 |
| `update_plan` | 更新计划 | ✅ 安全 |

### 8.2 技术依赖清单

| 依赖 | 版本 | 用途 |
|------|------|------|
| Microsoft.VisualStudio.SDK | 17.x | VS 扩展框架 |
| Community.VisualStudio.Toolkit.17 | 17.0+ | 简化开发 |
| Microsoft.VSSDK.BuildTools | 17.x | VSIX 构建 |
| System.Text.Json | 8.x | JSON 处理 |
| Markdig | 0.x | Markdown 渲染 |

### 8.3 System Prompt 模板

```markdown
你是 AICA，一个运行在 Visual Studio 2022 中的 AI 编程助手。

## 可用工具

- read_file: 读取文件内容
- edit: 精确编辑文件（查找替换）
- write_to_file: 创建新文件
- list_dir: 列出目录内容
- grep_search: 在文件中搜索文本
- find_by_name: 按名称搜索文件
- run_command: 执行终端命令
- update_plan: 管理任务计划

## 工作原则

1. 先读取文件理解上下文，再进行修改
2. 使用 edit 进行精确修改，不要重写整个文件
3. old_string 必须在文件中唯一存在
4. 危险命令需要用户确认
5. 遵循项目现有的代码风格
```

### 8.4 参考资源

| 资源 | 链接/位置 |
|------|-----------|
| Cline 源码 | `d:\Project\AIConsProject\cline` |
| AI-Studio 源码 | `d:\Project\AIConsProject\AI-Studio` |
| VS SDK 文档 | docs.microsoft.com/visualstudio/extensibility |
| Cline 分析报告 | `docs/Cline_Analysis_Report.md` |

---

**文档版本**: v1.0  
**创建日期**: 2026-02-05  
**下一步**: 进入 Phase 1 开发
