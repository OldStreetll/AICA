# VS2022 AI 辅助编程插件开发计划 V2

> 基于 Cline 项目源码分析，结合 Visual Studio 2022 平台特性制定

## 项目信息

| 项目 | 说明 |
|------|------|
| **项目名称** | AI Coding Assistant for Visual Studio 2022 |
| **项目代号** | AICA-VS2022 |
| **项目类型** | VSIX 插件 |
| **目标平台** | Visual Studio 2022 (17.0+) |
| **开发语言** | C# (.NET 8) |
| **大模型** | Qwen3-Coder-480B-A35B-Instruct（私有化部署） |
| **网络要求** | 完全离线/内网运行 |
| **参考项目** | [Cline](https://github.com/cline/cline) |

---

## 一、Cline 架构分析

### 1.1 核心组件映射

| Cline 组件 | 文件位置 | VS2022 对应 |
|------------|----------|-------------|
| Task | `src/core/task/index.ts` | AgentExecutor.cs |
| ToolExecutor | `src/core/task/ToolExecutor.ts` | ToolDispatcher.cs |
| ToolHandler | `src/core/task/tools/handlers/` | Tools/*.cs |
| ApiHandler | `src/core/api/index.ts` | LLMClient.cs |
| ContextManager | `src/core/context/` | ContextManager.cs |
| CommandPermissionController | `src/core/permissions/` | CommandClassifier.cs |
| ClineIgnoreController | `src/core/ignore/` | FileAccessPolicy.cs |
| DiffViewProvider | `src/integrations/editor/` | DiffViewProvider.cs |

### 1.2 Cline 工具清单（按优先级）

**P0 - 必须实现**:
| 工具 | 功能 | 安全级别 |
|------|------|----------|
| `read_file` | 读取文件 | 安全 |
| `write_to_file` | 创建文件 | 需确认 |
| `replace_in_file` | 差异化编辑 | 需确认 |
| `list_files` | 列出目录 | 安全 |
| `search_files` | 搜索内容 | 安全 |
| `execute_command` | 执行命令 | 分级 |
| `ask_followup_question` | 向用户提问 | 安全 |
| `attempt_completion` | 完成任务 | 安全 |

**P1 - 建议实现**:
| 工具 | 功能 |
|------|------|
| `apply_patch` | 多文件补丁 |
| `list_code_definition_names` | 列出代码定义 |
| `update_plan` | 更新计划 |
| `condense` | 压缩上下文 |

---

## 二、核心接口设计

### 2.1 工具接口

```csharp
public interface IAgentTool
{
    string Name { get; }
    string GetDescription(ToolCall call);
    ToolDefinition GetDefinition();
    Task HandlePartialAsync(ToolCall call, IUIContext ui);
    Task<ToolResult> ExecuteAsync(ToolCall call, ITaskContext context);
}
```

### 2.2 LLM 客户端接口

```csharp
public interface ILLMClient
{
    IAsyncEnumerable<LLMChunk> StreamAsync(
        string systemPrompt,
        IEnumerable<Message> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken ct = default);
    ModelInfo GetModelInfo();
    void Abort();
}
```

### 2.3 安全控制

```csharp
public class CommandClassifier
{
    public CommandSafety Classify(string command)
    {
        // 1. 检测危险字符（反引号、换行）
        if (HasDangerousChars(command)) return CommandSafety.Forbidden;
        
        // 2. 检查黑名单
        if (MatchesDenyList(command)) return CommandSafety.Forbidden;
        
        // 3. 检查白名单
        if (MatchesAllowList(command)) return CommandSafety.Safe;
        
        // 4. 默认需要确认
        return CommandSafety.RequiresConfirmation;
    }
}
```

---

## 三、项目结构

```
src/
├── AIAssistant.Core/              # 核心业务库
│   ├── Agent/                     # Agent 核心
│   │   ├── AgentExecutor.cs       # 执行循环
│   │   ├── TaskState.cs           # 任务状态
│   │   └── MessageHandler.cs      # 消息处理
│   ├── Tools/                     # 工具实现
│   │   ├── IAgentTool.cs          # 工具接口
│   │   ├── ToolDispatcher.cs      # 工具分发
│   │   └── Handlers/              # 工具处理器
│   ├── LLM/                       # LLM 客户端
│   │   ├── ILLMClient.cs
│   │   ├── LLMClient.cs
│   │   └── ToolCallParser.cs
│   ├── Context/                   # 上下文管理
│   │   ├── ContextManager.cs
│   │   └── TokenCounter.cs
│   ├── Security/                  # 安全控制
│   │   ├── SafetyGuard.cs
│   │   ├── CommandClassifier.cs
│   │   └── FileAccessPolicy.cs
│   └── Prompt/                    # Prompt 工程
│       ├── PromptBuilder.cs
│       └── ToolDescriptions.cs
│
├── AIAssistant.VSIX/              # VS 插件项目
│   ├── AIAssistantPackage.cs      # 插件入口
│   ├── Completion/                # 代码补全
│   ├── ToolWindows/               # 工具窗口
│   ├── Editor/                    # 编辑器集成
│   ├── Commands/                  # 命令
│   └── Settings/                  # 设置
│
├── AIAssistant.Roslyn/            # Roslyn 分析库
└── AIAssistant.Tests/             # 单元测试
```

---

## 四、开发阶段规划

### Phase 1: 基础框架 (Week 1-3)
| 任务 | 工时 |
|------|------|
| 创建 VSIX 项目 | 2d |
| LLM 客户端（HTTP/SSE） | 3d |
| Tool Calling 解析 | 2d |
| 基础对话窗口 | 2d |
| 配置系统 | 1d |
| 日志系统 | 1d |

### Phase 2: Agent 核心 (Week 4-7)
| 任务 | 工时 |
|------|------|
| AgentExecutor 执行循环 | 4d |
| ToolDispatcher 分发器 | 2d |
| read_file 工具 | 2d |
| write_to_file 工具 | 2d |
| edit 工具（差异化编辑） | 4d |
| list_files 工具 | 1d |
| search_files 工具 | 2d |
| ask_followup 工具 | 1d |
| attempt_completion 工具 | 1d |

### Phase 3: 安全机制 (Week 8-9)
| 任务 | 工时 |
|------|------|
| SafetyGuard 安全检查 | 2d |
| CommandClassifier 命令分级 | 3d |
| FileAccessPolicy 文件访问控制 | 2d |
| ApprovalManager 审批流程 | 2d |
| execute_command 工具 | 3d |

### Phase 4: UI 完善 (Week 10-12)
| 任务 | 工时 |
|------|------|
| 对话界面（Markdown、代码高亮） | 3d |
| 流式输出显示 | 2d |
| DiffViewProvider 差异预览 | 4d |
| 工具调用渲染 | 2d |
| 任务计划面板 | 2d |
| 代码操作按钮 | 2d |
| 右键菜单集成 | 2d |

### Phase 5: 代码补全 (Week 13-15)
| 任务 | 工时 |
|------|------|
| IAsyncCompletionSource | 3d |
| 上下文收集 | 2d |
| FIM Prompt 格式 | 2d |
| Ghost Text 显示 | 3d |
| Tab/部分接受 | 3d |
| 防抖和缓存 | 3d |

### Phase 6: 代码索引 (Week 16-19)
| 任务 | 工时 |
|------|------|
| Roslyn 代码分析 | 4d |
| 代码块分割 | 2d |
| 本地 Embedding（ONNX） | 3d |
| SQLite 向量存储 | 3d |
| 语义搜索 | 2d |
| 增量索引 | 3d |
| list_code_definitions 工具 | 2d |

### Phase 7: 优化测试 (Week 20-24)
| 任务 | 工时 |
|------|------|
| 性能分析和优化 | 5d |
| 并行工具调用 | 3d |
| 上下文截断策略 | 3d |
| 单元测试 | 6d |
| 集成测试 | 4d |
| 文档完善 | 4d |
| Bug 修复 | 5d |

---

## 五、里程碑

```
Week 1-3   Week 4-7   Week 8-9   Week 10-12  Week 13-15  Week 16-19  Week 20-24
   │          │          │           │           │           │           │
   ▼          ▼          ▼           ▼           ▼           ▼           ▼
┌──────┐  ┌──────┐  ┌──────┐   ┌──────┐   ┌──────┐   ┌──────┐   ┌──────┐
│ M1   │─▶│ M2   │─▶│ M3   │──▶│ M4   │──▶│ M5   │──▶│ M6   │──▶│ M7   │
│基础  │  │Agent │  │安全  │   │ UI   │   │补全  │   │索引  │   │发布  │
└──────┘  └──────┘  └──────┘   └──────┘   └──────┘   └──────┘   └──────┘
```

| 里程碑 | 时间 | 交付 |
|--------|------|------|
| M1 | Week 3 | LLM 通信正常 |
| M2 | Week 7 | Agent 可操作文件 |
| M3 | Week 9 | 安全机制完整 |
| M4 | Week 12 | UI 体验完整 |
| M5 | Week 15 | 代码补全可用 |
| M6 | Week 19 | 语义搜索可用 |
| M7 | Week 24 | 发布版本 |

---

## 六、技术依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| Microsoft.VisualStudio.SDK | 17.x | 插件框架 |
| Microsoft.CodeAnalysis.CSharp | 4.x | Roslyn 分析 |
| Microsoft.ML.OnnxRuntime | 1.x | 本地 Embedding |
| Microsoft.Data.Sqlite | 8.x | 向量存储 |
| CommunityToolkit.Mvvm | 8.x | MVVM |
| AvalonEdit | 6.x | 代码高亮 |
| Markdig | 0.x | Markdown |

---

## 七、验收标准

### 功能验收
- [ ] Agent 可完成"创建 REST API 控制器"任务
- [ ] 差异化编辑准确率 > 95%
- [ ] 代码补全响应 < 500ms
- [ ] 命令执行有安全确认

### 性能验收
| 指标 | 目标 |
|------|------|
| 补全响应 | < 500ms |
| 索引时间 | < 5min |
| 内存占用 | < 500MB |

### 安全验收
- [ ] 无外网请求
- [ ] 危险命令需确认
- [ ] 受保护文件不可访问

---

## 八、风险与对策

| 风险 | 对策 |
|------|------|
| LLM 响应延迟 | 流式输出、请求缓存 |
| 大项目索引慢 | 增量索引、后台异步 |
| 编辑匹配失败 | 提供更多上下文 |
| Token 超限 | 上下文压缩、截断 |
| 命令执行风险 | 严格白名单 |

---

**文档版本**: v2.0  
**创建日期**: 2026-02-04  
**基于**: Cline v3.56.2 源码分析
