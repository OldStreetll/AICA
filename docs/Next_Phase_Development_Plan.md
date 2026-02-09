# AICA 下一阶段开发计划

> 基于 Cline 功能对比分析，制定 AICA 从当前状态到功能完备的开发路线图

## 项目现状

| 项目 | 说明 |
|------|------|
| **当前阶段** | Sprint 3 完成，Sprint 4 待开始 |
| **综合完成度** | 约 65%（对标 Cline 功能集） |
| **已实现工具** | 12 个（read_file, edit, write_to_file, list_dir, grep_search, find_by_name, run_command, update_plan, attempt_completion, condense, list_code_definition_names） |
| **已实现子系统** | Agent 执行循环（含闭环/重试/截断处理）、工具注册/分发、LLM 流式通信、安全机制、上下文管理（截断+压缩）、System Prompt 增强、Diff 预览、对话持久化、对话 UI、配置系统、日志系统 |

### 已完成 vs Cline 对标

```
工具数量:           ████████████████░░░░░░░░░░  12/26 (46%)
核心工具(P0):       ██████████████████████████  10/10 (100%)
Agent 循环:         ██████████████████████░░░░        (85%)
上下文管理:         ██████████████████░░░░░░░░        (70%)
安全机制:           ██████████████████░░░░░░░░        (70%)
UI/UX:              ██████████████░░░░░░░░░░░░        (55%)
Prompt 系统:        ████████████████████░░░░░░        (75%)
高级功能:           ████████░░░░░░░░░░░░░░░░░░        (30%)
```

---

## 一、P0 — Agent 循环闭环（预计 3 天）

> **目标**：让 Agent 能正常结束任务、向用户提问、处理错误，从"能跑"变成"能用"

### 1.1 实现 `ask_followup_question` 工具

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/AskFollowupTool.cs` |
| **工时** | 0.5 天 |
| **参考** | Cline `AskFollowupQuestionToolHandler.ts` (107行) |

**功能要求**：
- 参数：`question`（必需）、`options`（可选，预设选项数组）
- 通过 `IUIContext.ShowConfirmationAsync` 或新增专用方法向用户展示问题
- 返回用户的文字回复作为 ToolResult
- Agent 循环收到回复后继续执行

**接口设计**：
```csharp
// 工具定义
Name: "ask_followup_question"
Parameters:
  - question (string, required): 要向用户提出的问题
  - options (array, optional): 预设的回答选项
```

### 1.2 实现 `attempt_completion` 工具

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/AttemptCompletionTool.cs` |
| **工时** | 0.5 天 |
| **参考** | Cline `AttemptCompletionHandler.ts` (279行) |

**功能要求**：
- 参数：`result`（必需，任务完成摘要）、`command`（可选，展示命令）
- 在 UI 中展示完成结果（Markdown 格式）
- 询问用户是否满意：
  - 满意 → Agent 循环正常结束
  - 不满意 → 用户可提供反馈，Agent 继续执行
- 重置 `consecutiveMistakeCount`

**接口设计**：
```csharp
// 工具定义
Name: "attempt_completion"
Parameters:
  - result (string, required): 任务完成的结果摘要
  - command (string, optional): 可选的展示命令
```

### 1.3 独立 TaskState 类 + 错误处理增强

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Agent/TaskState.cs`（新建）、`AgentExecutor.cs`（重构） |
| **工时** | 1 天 |
| **参考** | Cline `TaskState.ts` (73行) |

**功能要求**：
- 从 `AgentExecutor` 中提取状态到独立 `TaskState` 类
- 添加关键状态字段：

```csharp
public class TaskState
{
    // 流式处理标志
    public bool IsStreaming { get; set; }
    
    // 工具执行标志
    public bool DidRejectTool { get; set; }
    public bool DidEditFile { get; set; }
    public string LastToolName { get; set; }
    
    // 错误追踪
    public int ConsecutiveMistakeCount { get; set; }
    public int MaxConsecutiveMistakes { get; set; } = 3;
    
    // 任务控制
    public bool Abort { get; set; }
    public int ApiRequestCount { get; set; }
    
    // 上下文
    public bool CurrentlySummarizing { get; set; }
}
```

- 在 Agent 循环中加入 consecutive mistake 检测：超过阈值时暂停并提示用户
- 加入 context window exceeded 错误检测和自动处理

### 1.4 Agent 循环中集成新工具的逻辑

| 项目 | 说明 |
|------|------|
| **文件** | `AgentExecutor.cs`、`ChatToolWindowControl.xaml.cs` |
| **工时** | 1 天 |

**功能要求**：
- `attempt_completion` 执行后，Agent 循环根据用户反馈决定是否终止
- `ask_followup_question` 执行后，将用户回答作为新的 user message 继续循环
- 无工具调用时（LLM 只返回文本），自动提示 LLM 使用 `attempt_completion` 或继续执行
- UI 端正确渲染完成结果和提问界面

---

## 二、P1 — Prompt 与上下文增强（预计 4 天）

> **目标**：提升 LLM 输出质量和长对话支持能力

### 2.1 System Prompt 完善

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（重构扩展） |
| **工时** | 1.5 天 |
| **参考** | Cline `src/core/prompts/system-prompt/` |

**功能要求**：
- **工具描述自动生成**：从 `ToolDispatcher` 注册的工具自动生成详细的工具使用说明
- **规则注入**：添加 Agent 行为规则（先读再改、保持缩进、危险命令需确认等）
- **工作区上下文**：注入当前打开文件列表、项目结构摘要、工作目录
- **自定义指令**：支持用户自定义 System Prompt 追加内容（通过 Options）
- **格式化 Tool Calling 指引**：明确告诉 LLM 如何使用 JSON function calling，包括参数格式

**System Prompt 模板结构**：
```
1. 角色定义（你是 AICA，VS2022 AI 编程助手）
2. 工具列表（自动从注册工具生成）
3. 工具使用规则
4. 安全规则
5. 工作区信息
6. 自定义指令（用户配置）
```

### 2.2 上下文截断策略

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Context/ContextManager.cs`（增强） |
| **工时** | 1 天 |
| **参考** | Cline `context-management/` |

**功能要求**：
- **保留首尾，裁剪中间**：始终保留第一条用户消息（任务描述）+ 最近 N 轮对话
- **Token 预算分配**：

| 组成部分 | 预算占比 |
|----------|---------|
| System Prompt | 15% |
| 工具定义 | 10% |
| 对话历史 | 60% |
| 当前上下文 | 15% |

- **截断提示**：被裁剪时插入 `[NOTE] 部分历史对话已被移除以适应上下文窗口` 提示
- **Token 估算优化**：改进当前的粗略估算（当前 `字符数/4`），考虑中文字符（`字符数/2`）

### 2.3 `condense` 上下文压缩工具

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/CondenseTool.cs` |
| **工时** | 1 天 |
| **参考** | Cline `CondenseHandler.ts` (89行) |

**功能要求**：
- 参数：`context`（必需，当前任务摘要）
- 调用 LLM 对长对话进行摘要
- 用摘要替换历史对话，释放 Token 空间
- 展示摘要结果并询问用户确认

### 2.4 noToolsUsed 回退处理

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Agent/AgentExecutor.cs` |
| **工时** | 0.5 天 |

**功能要求**：
- 当 LLM 返回纯文本（无工具调用且非 `attempt_completion`）时，自动追加提示：
  > "你没有使用任何工具。如果任务已完成，请使用 attempt_completion 工具。否则，请继续使用工具完成任务。"
- 递增 `consecutiveMistakeCount`，防止死循环

---

## 三、P1 — 代码分析与 Diff 预览（预计 5 天）

> **目标**：增强代码理解能力和编辑可视化

### 3.1 `list_code_definition_names` 工具（Roslyn）

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/ListCodeDefinitionsTool.cs` |
| **新增依赖** | `Microsoft.CodeAnalysis.CSharp` |
| **工时** | 2 天 |
| **参考** | Cline `ListCodeDefinitionNamesToolHandler.ts`（使用 Tree-sitter） |

**功能要求**：
- 参数：`path`（必需，文件或目录路径）
- 使用 Roslyn `CSharpSyntaxTree` 解析 C# 文件
- 提取并返回：
  - 命名空间
  - 类/结构体/接口/枚举（含访问修饰符）
  - 方法签名（含参数类型和返回类型）
  - 属性
- 支持目录递归扫描所有 `.cs` 文件
- 输出格式：

```
src/Models/User.cs:
  namespace MyApp.Models
    public class User
      public int Id { get; set; }
      public string Name { get; set; }
      public async Task<bool> ValidateAsync(string token)
```

### 3.2 Diff 预览集成

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.VSIX/Editor/DiffPreviewService.cs`（新建） |
| **工时** | 2 天 |
| **参考** | Cline `DiffViewProvider.ts` |

**功能要求**：
- 使用 VS SDK 的 `IVsDifferenceService` 在 VS 编辑器中打开 diff 视图
- `EditFileTool` 执行前展示 diff 预览
- 用户可在 diff 视图中确认或拒绝更改
- 更新 `IUIContext.ShowDiffPreviewAsync` 的实现（当前 `VSUIContext.cs` 中是空实现）

### 3.3 ListDir 增强为递归模式

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/ListDirTool.cs` |
| **工时** | 0.5 天 |

**功能要求**：
- 添加 `recursive` 参数（默认 false）
- 添加 `max_depth` 参数
- 递归模式下以树形结构展示目录内容
- 排除 `.git`, `bin`, `obj`, `node_modules` 等目录

---

## 四、P1 — Agent 执行健壮性（预计 3 天）

> **目标**：提升 Agent 在异常场景下的稳定性

### 4.1 Context Window Exceeded 检测与处理

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Agent/AgentExecutor.cs`、`OpenAIClient.cs` |
| **工时** | 1 天 |
| **参考** | Cline `context-error-handling.ts` |

**功能要求**：
- 检测 LLM 返回的 400 错误中的 `context_length_exceeded` 类型
- 自动触发上下文截断（调用 ContextManager 裁剪历史）
- 截断后自动重试请求
- 向用户显示通知："上下文窗口已满，自动裁剪历史对话"

### 4.2 自动重试机制

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Agent/AgentExecutor.cs` |
| **工时** | 0.5 天 |

**功能要求**：
- LLM 请求超时或网络错误时自动重试（最多 2 次）
- 指数退避：1s → 3s
- 重试时向 UI 显示 "正在重试..."
- 超过重试次数后向用户报告错误

### 4.3 对话历史持久化

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Storage/ConversationStorage.cs`（新建） |
| **工时** | 1.5 天 |
| **参考** | Cline `storage/disk.ts` |

**功能要求**：
- 将对话历史保存到本地文件（JSON 格式）
- 存储位置：`%LOCALAPPDATA%\AICA\conversations\{taskId}.json`
- 支持恢复上次对话
- 支持导出为 Markdown
- ChatToolWindow 关闭/重启后可恢复对话

---

## 五、P2 — UI/UX 增强（预计 4 天）

> **目标**：提升交互体验

### 5.1 工具调用可视化

| 项目 | 说明 |
|------|------|
| **文件** | `ChatToolWindowControl.xaml.cs`（增强渲染逻辑） |
| **工时** | 1.5 天 |

**功能要求**：
- 工具调用在 UI 中以可折叠卡片展示：
  - 标题：工具名 + 简要参数
  - 展开：完整参数 + 执行结果
  - 状态图标：⏳ 执行中 / ✅ 成功 / ❌ 失败
- `attempt_completion` 的结果以高亮卡片展示
- `ask_followup_question` 以表单形式展示（含预设选项按钮）

### 5.2 任务计划面板

| 项目 | 说明 |
|------|------|
| **文件** | 可集成在 ChatToolWindowControl 中，或新建 `TaskPlanPanel.xaml` |
| **工时** | 1.5 天 |

**功能要求**：
- 在聊天窗口侧边或顶部展示当前任务计划
- 实时显示 `update_plan` 工具更新的步骤状态
- 每个步骤显示状态图标：⏳ 待办 / 🔄 进行中 / ✅ 完成 / ❌ 失败

### 5.3 细粒度自动审批

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.VSIX/Options/SecurityOptions.cs`（增强）、`VSAgentContext.cs` |
| **工时** | 1 天 |
| **参考** | Cline `autoApprovalSettings` |

**功能要求**：
- 按工具类型配置自动审批：

| 选项 | 默认值 |
|------|--------|
| 自动审批读取操作 (read_file, list_dir, grep_search, find_by_name) | ✅ 开启 |
| 自动审批文件编辑 (edit) | ❌ 关闭 |
| 自动审批文件创建 (write_to_file) | ❌ 关闭 |
| 自动审批安全命令 (白名单内的 run_command) | ❌ 关闭 |

---

## 六、P2 — 高级工具（预计 4 天）

### 6.1 `apply_patch` 多文件补丁工具

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/ApplyPatchTool.cs` |
| **工时** | 3 天 |
| **参考** | Cline `ApplyPatchHandler.ts` (744行) |

**功能要求**：
- 支持统一 diff 格式的多文件补丁
- 操作类型：ADD（新建文件）、UPDATE（修改文件）、DELETE（删除文件）
- 每个文件变更独立确认
- 失败时回滚已应用的变更
- 与 DiffPreviewService 集成

### 6.2 `new_task` 子任务工具

| 项目 | 说明 |
|------|------|
| **文件** | `src/AICA.Core/Tools/NewTaskTool.cs` |
| **工时** | 1 天 |
| **参考** | Cline `NewTaskHandler.ts` |

**功能要求**：
- 参数：`task`（任务描述）、`context`（任务上下文）
- 在新的对话窗口/标签中启动子任务
- 子任务共享同一工作区但拥有独立的对话历史

---

## 七、开发阶段时间线

```
Sprint 1 (Week 1)        Sprint 2 (Week 2-3)       Sprint 3 (Week 4-5)       Sprint 4 (Week 6-7)
     │                        │                          │                         │
     ▼                        ▼                          ▼                         ▼
┌──────────┐           ┌──────────────┐           ┌──────────────┐          ┌──────────────┐
│ P0       │           │ P1           │           │ P1           │          │ P2           │
│ Agent    │──────────▶│ Prompt +     │──────────▶│ 代码分析 +    │─────────▶│ UI/UX +      │
│ 循环闭环  │           │ 上下文增强    │           │ Diff + 健壮性  │          │ 高级工具      │
│ (3天)    │           │ (4天)        │           │ (8天)         │          │ (8天)        │
└──────────┘           └──────────────┘           └──────────────┘          └──────────────┘
                                                                           
交付物:                  交付物:                    交付物:                   交付物:
- ask_followup           - System Prompt 完善        - Roslyn 代码定义         - 工具调用可视化
- attempt_completion     - 上下文截断策略            - Diff 预览集成           - 任务计划面板
- TaskState 类           - condense 工具             - Context Exceeded 处理   - 细粒度审批
- noToolsUsed 处理       - noToolsUsed 回退          - 自动重试               - apply_patch
                                                    - 对话历史持久化          - new_task
```

---

## 八、里程碑与验收标准

| 里程碑 | 时间 | 验收标准 |
|--------|------|----------|
| **M1: Agent 可用** | Week 1 | Agent 可完成 "创建一个 HelloWorld.cs 文件" 任务并正常结束 |
| **M2: 智能对话** | Week 3 | 长对话不会 Token 超限；LLM 输出稳定使用工具 |
| **M3: 代码理解** | Week 5 | 可列出项目代码定义；编辑操作有 Diff 预览；对话可恢复 |
| **M4: 完整体验** | Week 7 | 工具调用可视化；多文件补丁；自动审批读操作 |

### 功能验收测试用例

| 测试场景 | 预期结果 |
|----------|----------|
| "给 User 类添加一个 Email 属性" | Agent 读取文件 → 编辑文件 → Diff 预览 → attempt_completion |
| "这个方法是做什么的？"（选中代码） | Agent 使用 read_file → 返回解释 → attempt_completion |
| "项目里有哪些 Controller？" | Agent 使用 grep_search/find_by_name → 返回列表 → attempt_completion |
| "运行 dotnet build" | Agent 使用 run_command（白名单自动执行）→ 分析输出 → attempt_completion |
| Agent 连续犯错 3 次 | 暂停并提示用户 "Agent 遇到困难，是否继续？" |
| 对话超过 20 轮 | 自动截断历史或建议使用 condense |
| LLM 只返回文本不调用工具 | 自动提示 LLM 使用 attempt_completion 或继续 |

---

## 九、新增文件清单

| 文件路径 | 说明 | 优先级 |
|----------|------|--------|
| `src/AICA.Core/Tools/AskFollowupTool.cs` | 向用户提问工具 | P0 |
| `src/AICA.Core/Tools/AttemptCompletionTool.cs` | 任务完成工具 | P0 |
| `src/AICA.Core/Agent/TaskState.cs` | 独立任务状态类 | P0 |
| `src/AICA.Core/Tools/CondenseTool.cs` | 上下文压缩工具 | P1 |
| `src/AICA.Core/Tools/ListCodeDefinitionsTool.cs` | 代码定义列表工具 | P1 |
| `src/AICA.Core/Storage/ConversationStorage.cs` | 对话历史持久化 | P1 |
| `src/AICA.VSIX/Editor/DiffPreviewService.cs` | VS Diff 预览服务 | P1 |
| `src/AICA.Core/Tools/ApplyPatchTool.cs` | 多文件补丁工具 | P2 |
| `src/AICA.Core/Tools/NewTaskTool.cs` | 子任务工具 | P2 |

### 需修改的现有文件

| 文件路径 | 修改内容 | 优先级 |
|----------|----------|--------|
| `src/AICA.Core/Agent/AgentExecutor.cs` | 提取 TaskState、noToolsUsed 处理、auto-retry、context exceeded | P0-P1 |
| `src/AICA.Core/Prompt/SystemPromptBuilder.cs` | 工具描述自动生成、规则注入、工作区上下文 | P1 |
| `src/AICA.Core/Context/ContextManager.cs` | 截断策略、Token 估算优化 | P1 |
| `src/AICA.Core/LLM/OpenAIClient.cs` | Context exceeded 错误检测 | P1 |
| `src/AICA.Core/Tools/ListDirTool.cs` | 添加递归和 max_depth 参数 | P1 |
| `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs` | 工具调用渲染、完成结果展示、提问 UI | P2 |
| `src/AICA.VSIX/Agent/VSUIContext.cs` | ShowDiffPreviewAsync 真实实现 | P1 |
| `src/AICA.VSIX/Options/SecurityOptions.cs` | 细粒度自动审批配置 | P2 |

---

## 十、技术风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| LLM 不稳定调用 `attempt_completion` | Agent 无法正常结束 | System Prompt 中强化规则 + noToolsUsed 回退提示 |
| LLM 生成的 tool call JSON 格式错误 | 工具无法执行 | 已有 `TryParseTextToolCalls` 回退；可进一步增强容错 |
| Roslyn 分析大项目耗时长 | 响应慢 | 限制扫描深度和文件数量、后台异步索引 |
| VS DiffService API 兼容性 | 不同 VS 版本行为不同 | 使用 Community.VisualStudio.Toolkit 封装，回退到文本 diff |
| 对话历史 JSON 文件过大 | 磁盘/内存压力 | 定期清理、限制保存条数 |

---

**文档版本**: v1.0  
**创建日期**: 2026-02-06  
**基于**: AICA 现状分析 + Cline v3.56.2 功能对标
