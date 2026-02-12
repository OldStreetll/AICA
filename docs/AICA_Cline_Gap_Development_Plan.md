# AICA 对标 Cline 完善开发计划

> **文档版本**: v1.0  
> **创建日期**: 2026-02-10  
> **基于**: AICA v1.9.0+ 现状 vs Cline v3.56.2 功能集  
> **目标**: 系统性补齐 AICA 与 Cline 的功能差距，达到生产可用水平

---

## 一、现状总览

### 1.1 AICA 已实现功能

| 模块 | 已实现 | 完成度 |
|------|--------|--------|
| **Agent 执行循环** | 迭代循环、流式输出、重试、去重、幻觉抑制、参数增强 | 95% |
| **工具系统** | 11 个工具（read_file, edit, write_to_file, list_dir, grep_search, find_by_name, list_code_definitions, run_command, update_plan, attempt_completion, condense） | 46% |
| **LLM 通信** | OpenAI 兼容 API、SSE 流式、Tool Calling 解析、错误重试 | 100% |
| **安全机制** | SafetyGuard（路径保护、命令白/黑名单、.aicaignore） | 70% |
| **上下文管理** | ContextManager（CJK token 估算、对话截断） | 70% |
| **Prompt 系统** | SystemPromptBuilder（反幻觉、效率、搜索策略规则） | 60% |
| **Workspace** | SolutionSourceIndex + PathResolver（CMake out-of-source 支持） | 100% |
| **对话持久化** | ConversationStorage（JSON 存储、导出 Markdown） | 75% |
| **UI/UX** | WPF WebBrowser 聊天窗口、Markdown 渲染、右键菜单(4个) | 50% |
| **构建/部署** | build.ps1 构建脚本、VSIX 打包 | 80% |

### 1.2 Cline 功能全景 vs AICA 对标

```
                        Cline 功能覆盖                              AICA 状态
─────────────────────────────────────────────────────────────────────────────
核心工具 (11/11)        ████████████████████████████████  100%      ✅ 已有
ask_followup_question   ████████████████████████████████  Cline有   ❌ 缺失
apply_patch             ████████████████████████████████  Cline有   ❌ 缺失
new_task                ████████████████████████████████  Cline有   ❌ 缺失
plan_mode/act_mode      ████████████████████████████████  Cline有   ❌ 缺失
browser_action          ████████████████████████████████  Cline有   ⬜ 不适用(离线)
web_search/web_fetch    ████████████████████████████████  Cline有   ⬜ 不适用(离线)
MCP 协议                ████████████████████████████████  Cline有   ⬜ 低优先级
自动审批系统            ████████████████████████████████  Cline有   ❌ 缺失
检查点/快照             ████████████████████████████████  Cline有   ❌ 缺失
Hooks 系统              ████████████████████████████████  Cline有   ❌ 缺失
用户规则 (.clinerules)  ████████████████████████████████  Cline有   ❌ 缺失
Prompt 变体系统         ████████████████████████████████  Cline有   ❌ 缺失
命令权限控制器          ████████████████████████████████  Cline有   🟡 部分有
上下文追踪              ████████████████████████████████  Cline有   🟡 部分有
工具调用可视化 UI       ████████████████████████████████  Cline有   ❌ 缺失
任务计划面板 UI         ████████████████████████████████  Cline有   ❌ 缺失
对话历史浏览 UI         ████████████████████████████████  Cline有   ❌ 缺失
通知系统                ████████████████████████████████  Cline有   ❌ 缺失
Slash 命令              ████████████████████████████████  Cline有   ❌ 缺失
Skills 系统             ████████████████████████████████  Cline有   ❌ 缺失
```

---

## 二、功能差距详细分析

### 2.1 工具层差距

| Cline 工具 | 功能 | AICA 状态 | 优先级 | 说明 |
|------------|------|-----------|--------|------|
| `ask_followup_question` | Agent 向用户提问并等待回复 | ❌ 缺失 | **P0** | Agent 循环闭环的关键，当前 Agent 无法主动提问 |
| `apply_patch` | V4A diff 格式多文件补丁 | ❌ 缺失 | **P1** | 比 edit 更高效的批量编辑，减少工具调用次数 |
| `new_task` | 启动子任务（新对话上下文） | ❌ 缺失 | **P2** | 复杂任务分解为独立子任务 |
| `plan_mode_respond` | 规划模式回复 | ❌ 缺失 | **P1** | 支持 Plan/Act 双模式 |
| `act_mode_respond` | 执行模式回复 | ❌ 缺失 | **P1** | 支持 Plan/Act 双模式 |
| `summarize_task` | 任务摘要（区别于 condense） | ❌ 缺失 | **P2** | 任务完成后生成结构化摘要 |
| `browser_action` | Puppeteer 浏览器自动化 | ⬜ 不适用 | — | 离线环境无需，但本地 Web 开发可考虑 |
| `web_search` / `web_fetch` | 网页搜索/抓取 | ⬜ 不适用 | — | 离线/内网环境不适用 |
| `use_mcp_tool` / `access_mcp_resource` | MCP 协议工具 | ⬜ 低优先 | P3 | 可扩展工具系统，但离线环境需求不明确 |
| `use_skill` | 技能执行 | ❌ 缺失 | P3 | 依赖 Skills 系统 |

### 2.2 子系统差距

| Cline 子系统 | 功能描述 | AICA 状态 | 优先级 |
|-------------|---------|-----------|--------|
| **AutoApprove 系统** | 按工具类型+路径 细粒度自动审批（YOLO 模式、本地/外部区分） | ❌ 缺失 | **P0** |
| **Plan/Act 双模式** | 规划模式（只规划不执行）和执行模式（执行操作） | ❌ 缺失 | **P1** |
| **Checkpoint 系统** | 基于 Git 的工作区快照，支持回滚到任意步骤 | ❌ 缺失 | **P1** |
| **Hooks 系统** | 工具执行前后的用户自定义钩子（shell 脚本） | ❌ 缺失 | **P2** |
| **用户规则系统** | `.clinerules`/`.aicarules` 目录下的自定义规则注入 System Prompt | ❌ 缺失 | **P1** |
| **Prompt 变体系统** | 根据模型家族选择不同的 System Prompt 模板 | ❌ 缺失 | **P2** |
| **命令权限控制器** | Glob 模式的命令 allow/deny + shell 操作符检测 | 🟡 部分有 | **P1** |
| **上下文追踪** | FileContextTracker（已读文件）、ModelContextTracker（模型状态） | 🟡 部分有 | **P1** |
| **通知系统** | 系统级桌面通知（Agent 需要注意时） | ❌ 缺失 | **P2** |
| **Slash 命令** | `/new`、`/clear` 等快捷命令 | ❌ 缺失 | **P2** |
| **Skills 系统** | 可发现的 `.cline/skills/` 目录下的技能模板 | ❌ 缺失 | **P3** |
| **Focus Chain** | 工具执行相关文件的焦点链管理 | ❌ 缺失 | **P3** |

### 2.3 UI/UX 差距

| 功能 | Cline 实现 | AICA 现状 | 优先级 |
|------|-----------|-----------|--------|
| **工具调用可视化** | React 可折叠卡片（工具名+参数+状态图标+结果） | 纯文本 🔧 日志 | **P0** |
| **任务计划面板** | 实时步骤状态展示 | 无 | **P1** |
| **对话历史浏览器** | 侧边栏历史列表，可恢复/删除 | 仅后端持久化，UI 无法浏览 | **P1** |
| **Diff 预览增强** | 内嵌 diff 视图，一键接受/拒绝 | 基础 VS diff 服务调用 | **P1** |
| **设置面板** | Webview 内嵌设置页，直观易用 | VS Options 标准对话框 | **P2** |
| **ask_followup UI** | 表单+预设选项按钮 | 无 | **P0** |
| **attempt_completion UI** | 高亮结果卡片+反馈按钮 | 简单文本输出 | **P1** |
| **图片支持** | 消息中嵌入截图 | 无 | **P3** |
| **文件拖放** | 拖放文件作为上下文 | 无 | **P3** |
| **@mention 上下文** | `@file`、`@folder`、`@problems` | 无 | **P2** |
| **Markdown 增强** | 语法高亮、代码块复制按钮 | 基础 Markdig 渲染 | **P2** |

---

## 三、开发阶段规划

### 总体时间线

```
Sprint 5 (W1-2)     Sprint 6 (W3-4)     Sprint 7 (W5-7)     Sprint 8 (W8-10)    Sprint 9 (W11-13)
     │                    │                    │                    │                    │
     ▼                    ▼                    ▼                    ▼                    ▼
┌──────────┐       ┌──────────────┐      ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ P0       │       │ P1-A         │      │ P1-B         │    │ P2-A         │    │ P2-B         │
│ Agent    │──────▶│ 安全+模式     │─────▶│ 检查点+上下文 │───▶│ UI 增强      │───▶│ 高级功能     │
│ 交互闭环  │       │ +规则系统     │      │ +Prompt增强   │    │ +@mention    │    │ +扩展性      │
│ (10天)   │       │ (10天)       │      │ (15天)        │    │ (12天)       │    │ (12天)       │
└──────────┘       └──────────────┘      └──────────────┘    └──────────────┘    └──────────────┘
      │                   │                     │                   │                   │
      ▼                   ▼                     ▼                   ▼                   ▼
   [M1]               [M2]                  [M3]                [M4]                [M5]
 Agent 可交互      安全可控+规划能力      长任务稳定运行      UI 体验达标       功能完备
```

---

### Sprint 5: P0 — Agent 交互闭环（预计 10 天）

> **目标**: 让 Agent 能主动提问、支持自动审批、工具调用可视化，从"能跑"变成"好用"

#### 5.1 实现 `ask_followup_question` 工具

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Tools/AskFollowupTool.cs` |
| **修改文件** | `AgentExecutor.cs`、`ChatToolWindowControl.xaml.cs`、`IUIContext.cs`、`VSUIContext.cs` |
| **工时** | 3 天 |
| **参考** | Cline `AskFollowupQuestionToolHandler.ts` (107行) |

**功能要求**:
- 参数：`question`（必需，string）、`options`（可选，string[] JSON 数组）
- 通过 `IUIContext` 新增方法 `ShowFollowupQuestionAsync(string question, string[] options)` 在 UI 中展示
- UI 渲染：问题文本 + 预设选项按钮 + 自由输入文本框
- 用户选择/输入后，回复作为 `ToolResult` 返回给 Agent
- Agent 循环将回复作为 tool result 继续执行

**IUIContext 接口扩展**:
```csharp
/// <summary>
/// 向用户展示问题并等待回复
/// </summary>
/// <param name="question">要提问的问题</param>
/// <param name="options">预设选项（可选）</param>
/// <returns>用户的回复文本</returns>
Task<string> ShowFollowupQuestionAsync(string question, string[] options, CancellationToken ct);
```

**UI 交互流程**:
```
Agent 调用 ask_followup_question
    ↓
UI 显示问题卡片:
  ┌─────────────────────────────────────┐
  │ 🤔 AICA 有个问题:                    │
  │                                      │
  │ "你希望使用哪个测试框架？"              │
  │                                      │
  │ [MSTest]  [NUnit]  [xUnit]           │
  │                                      │
  │ 或输入自定义回答: [________________]   │
  │                           [回复]      │
  └─────────────────────────────────────┘
    ↓
用户点击选项或输入文字
    ↓
ToolResult 返回 → Agent 继续执行
```

**AgentExecutor 集成要点**:
- `ask_followup_question` 返回后不递增 `consecutiveMistakeCount`
- 用户回复内容作为 tool result，不添加新的 user message
- 支持 `CancellationToken` 取消等待

#### 5.2 实现自动审批系统 (AutoApprove)

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Agent/AutoApproveManager.cs` |
| **修改文件** | `SecurityOptions.cs`、`VSAgentContext.cs`、各工具文件 |
| **工时** | 3 天 |
| **参考** | Cline `autoApprove.ts` (167行) |

**功能要求**:

按工具类别配置自动审批策略：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `AutoApproveReadOperations` | `true` | 自动审批 read_file、list_dir、grep_search、find_by_name、list_code_definitions |
| `AutoApproveWriteOperations` | `false` | 自动审批 edit、write_to_file |
| `AutoApproveSafeCommands` | `false` | 自动审批白名单内的 run_command |
| `AutoApproveAllCommands` | `false` | 自动审批所有 run_command |
| `YoloMode` | `false` | 全自动模式（所有操作不需确认） |

**路径感知审批**:
- 工作区内文件操作 → 使用 `AutoApproveWriteOperations` 设置
- 工作区外文件操作（SourceRoots 内） → 始终需要确认（即使开启自动审批）
- 不在任何已知路径内 → 拒绝

**AutoApproveManager 接口设计**:
```csharp
public class AutoApproveManager
{
    /// <summary>
    /// 检查工具是否应该自动审批
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="actionPath">操作涉及的文件路径（可选）</param>
    /// <returns>true=自动审批, false=需要用户确认</returns>
    public async Task<bool> ShouldAutoApproveAsync(string toolName, string actionPath = null);
}
```

**修改 SecurityOptions.cs 新增配置项**:
```csharp
[Category("Auto Approval")]
[DisplayName("Auto-approve read operations")]
[Description("Automatically approve read_file, list_dir, grep_search, etc.")]
[DefaultValue(true)]
public bool AutoApproveReadOperations { get; set; } = true;

[Category("Auto Approval")]
[DisplayName("Auto-approve write operations")]
[Description("Automatically approve edit, write_to_file within workspace")]
[DefaultValue(false)]
public bool AutoApproveWriteOperations { get; set; } = false;

// ... 其他配置项
```

#### 5.3 工具调用可视化 UI

| 项目 | 说明 |
|------|------|
| **修改文件** | `ChatToolWindowControl.xaml.cs`（渲染逻辑重构） |
| **工时** | 3 天 |
| **参考** | Cline Webview React 组件 |

**功能要求**:

将当前的 `🔧 tool_name(params)` 纯文本日志改为结构化可折叠卡片：

```
工具调用渲染规范:

┌─ 🔧 read_file ─────────────────── ✅ ─┐
│  📄 src/App/Application.h              │
│  ▸ 展开查看详情                         │
└────────────────────────────────────────┘

展开后:
┌─ 🔧 read_file ─────────────────── ✅ ─┐
│  📄 src/App/Application.h              │
│  ▾ 收起                                │
│                                        │
│  参数:                                  │
│    path: src/App/Application.h         │
│    offset: 1                           │
│    limit: 100                          │
│                                        │
│  结果: (前 5 行)                        │
│    1: #pragma once                     │
│    2: #include <string>                │
│    3: ...                              │
│                                        │
│  耗时: 45ms                            │
└────────────────────────────────────────┘
```

**状态图标**:
- ⏳ 执行中（loading spinner）
- ✅ 成功
- ❌ 失败
- 🚫 用户拒绝
- ⏭️ 自动审批

**HTML/JS 模板**:
- 使用 `<details><summary>` 实现折叠
- 参数和结果使用 `<pre>` + 语法高亮
- `attempt_completion` 结果以绿色高亮卡片展示
- `ask_followup_question` 以交互表单展示

#### 5.4 `attempt_completion` UI 增强

| 项目 | 说明 |
|------|------|
| **修改文件** | `ChatToolWindowControl.xaml.cs`、`AttemptCompletionTool.cs` |
| **工时** | 1 天 |

**功能要求**:
- 完成结果以醒目卡片展示（绿色边框、✅ 图标）
- 显示"满意"/"不满意"按钮
- "不满意"时弹出文本框让用户输入反馈
- 反馈内容作为新的 user message 继续对话
- 当前实现仅返回 `TASK_COMPLETED:` 前缀，需要扩展为结构化交互

**交付物**:
- Agent 可主动提问并等待回复
- 读操作默认自动审批，写操作需确认
- 工具调用以结构化卡片展示
- 任务完成有完善的交互流程

**里程碑 M1**: Agent 可完成"给 User 类添加 Email 属性"的完整交互流程

---

### Sprint 6: P1-A — 安全增强 + 规划模式 + 用户规则（预计 10 天）

> **目标**: 增强安全控制能力，支持 Plan/Act 双模式，支持用户自定义规则

#### 6.1 Plan/Act 双模式

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Agent/AgentMode.cs`、`src/AICA.Core/Tools/PlanModeRespondTool.cs` |
| **修改文件** | `AgentExecutor.cs`、`SystemPromptBuilder.cs`、`ChatToolWindowControl.xaml.cs` |
| **工时** | 4 天 |
| **参考** | Cline `PlanModeRespondHandler.ts` (179行)、`ActModeRespondHandler.ts` (89行) |

**功能要求**:

两种工作模式:

| 模式 | 行为 | 可用工具 |
|------|------|---------|
| **Plan 模式** | 只规划不执行，生成任务计划 | `plan_mode_respond`（输出分析和计划） |
| **Act 模式** | 正常执行，调用工具完成任务 | 所有工具 |

**AgentMode 枚举**:
```csharp
public enum AgentMode
{
    Plan,   // 规划模式 - 只分析和制定计划
    Act     // 执行模式 - 正常执行任务
}
```

**切换机制**:
- UI 中显示 Plan/Act 切换按钮
- Plan 模式下 System Prompt 指示 LLM 只输出分析和计划，不调用文件操作工具
- `plan_mode_respond` 工具用于 LLM 在 Plan 模式下输出结构化计划
- 用户可在 Plan 模式下审核计划后切换到 Act 模式执行

**System Prompt Plan 模式段落**:
```
## Current Mode: PLAN
You are currently in Plan mode. In this mode:
- Analyze the user's request and create a detailed plan
- Use the plan_mode_respond tool to present your analysis and proposed steps
- Do NOT use file editing, creation, or command execution tools
- You may use read-only tools (read_file, list_dir, grep_search) to gather information
- Wait for the user to approve the plan before switching to Act mode
```

#### 6.2 用户规则系统 (.aicarules)

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Context/UserRulesLoader.cs` |
| **修改文件** | `SystemPromptBuilder.cs`、`VSAgentContext.cs` |
| **工时** | 2 天 |
| **参考** | Cline `cline-rules.ts` (181行)、`external-rules.ts` (213行) |

**功能要求**:

支持从以下位置加载用户自定义规则：

| 位置 | 作用域 | 文件 |
|------|--------|------|
| `%USERPROFILE%\.aica\rules\` | 全局规则 | `*.md` |
| `.aica\rules\` (工作区根) | 项目规则 | `*.md` |
| `.aicarules` (工作区根) | 项目规则（单文件） | 直接读取 |
| `.clinerules\` (兼容) | 兼容 Cline 规则 | `*.md` |

**规则注入流程**:
```
启动 Agent 任务时:
  1. 扫描全局规则目录
  2. 扫描项目规则目录
  3. 扫描兼容规则目录
  4. 合并所有规则内容
  5. 注入到 System Prompt 的 "User Instructions" 段落中
```

**UserRulesLoader 接口**:
```csharp
public class UserRulesLoader
{
    /// <summary>加载所有适用的用户规则</summary>
    public async Task<string> LoadRulesAsync(string workingDirectory);
    
    /// <summary>扫描指定目录下的规则文件</summary>
    private async Task<List<string>> ScanRulesDirectoryAsync(string directory);
}
```

#### 6.3 命令权限控制器增强

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Security/CommandPermissionController.cs` |
| **修改文件** | `SafetyGuard.cs`、`RunCommandTool.cs` |
| **工时** | 2 天 |
| **参考** | Cline `CommandPermissionController.ts` (412行) |

**功能要求**:

当前 `SafetyGuard` 使用简单的白名单/黑名单字符串匹配。需要增强为：

| 功能 | 当前 | 目标 |
|------|------|------|
| 匹配方式 | 精确字符串 | Glob 模式（`dotnet *`、`git *`） |
| Shell 操作符检测 | 无 | 检测 `&&`、`\|\|`、`;`、`>`、`<`、`\|` |
| 链式命令处理 | 无 | 逐段解析和验证 |
| 重定向检测 | 无 | 检测并阻止文件重定向 |
| 权限结果 | 允许/拒绝 | 允许/拒绝/需确认 + 详细原因 |

**Shell 操作符安全规则**:
```csharp
// 检测危险的 shell 操作符
private static readonly (string Operator, string Description)[] DangerousOperators = new[]
{
    ("&&", "Command chaining"),
    ("||", "Conditional execution"),
    (";", "Command separator"),
    ("|", "Pipe"),
    (">", "Output redirect"),
    (">>", "Output append"),
    ("<", "Input redirect"),
    ("$(", "Command substitution"),
    ("`", "Backtick substitution"),
};
```

#### 6.4 apply_patch 多文件补丁工具

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Tools/ApplyPatchTool.cs` |
| **工时** | 2 天 |
| **参考** | Cline `ApplyPatchHandler.ts` (744行) |

**功能要求**:

支持 V4A diff 格式的多文件补丁：

```
*** Begin Patch
*** Add File: src/Models/NewClass.cs
+ using System;
+ 
+ namespace MyApp.Models
+ {
+     public class NewClass { }
+ }

*** Update File: src/Models/User.cs
@@ class User
     public string Name { get; set; }
-    // TODO: add email
+    public string Email { get; set; }
+    public bool IsEmailVerified { get; set; }

*** Delete File: src/Models/OldClass.cs
*** End Patch
```

**操作类型**:
- `Add File` → 创建新文件（等同于 write_to_file）
- `Update File` → 应用上下文 diff（比 edit 更智能：使用上下文行定位而非精确字符串匹配）
- `Delete File` → 删除文件（需确认）

**V4A Diff 解析器设计**:
```csharp
public class V4APatchParser
{
    public List<PatchOperation> Parse(string patchContent);
}

public class PatchOperation
{
    public PatchAction Action { get; set; }  // Add, Update, Delete
    public string FilePath { get; set; }
    public List<PatchHunk> Hunks { get; set; }  // Update 操作的变更块
    public string NewContent { get; set; }       // Add 操作的完整内容
}

public class PatchHunk
{
    public string[] ContextBefore { get; set; }  // 上下文行（@@ 标记 + 前 3 行）
    public string[] RemovedLines { get; set; }   // - 前缀行
    public string[] AddedLines { get; set; }     // + 前缀行
    public string[] ContextAfter { get; set; }   // 后 3 行上下文
}
```

**交付物**:
- Plan/Act 双模式切换
- `.aicarules` 用户规则加载
- 增强的命令权限控制（Glob + Shell 操作符检测）
- V4A diff 格式 apply_patch 工具

**里程碑 M2**: Agent 安全可控，可先规划后执行

---

### Sprint 7: P1-B — 检查点 + 上下文增强 + Prompt 系统（预计 15 天）

> **目标**: 支持工作区快照回滚，增强长任务上下文管理，提升 Prompt 质量

#### 7.1 Checkpoint 工作区快照系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Checkpoint/CheckpointManager.cs`、`src/AICA.Core/Checkpoint/CheckpointTracker.cs` |
| **修改文件** | `AgentExecutor.cs`、`ChatToolWindowControl.xaml.cs` |
| **工时** | 5 天 |
| **参考** | Cline `checkpoints/CheckpointTracker.ts` (548行)、`checkpoints/index.ts` (1075行) |

**功能要求**:

使用 Git 实现工作区快照（与 Cline 相同策略）：

| 功能 | 说明 |
|------|------|
| **自动快照** | 每次工具执行文件变更后，自动创建 Git commit 快照 |
| **快照浏览** | UI 中显示步骤列表，每步标注已修改的文件 |
| **对比快照** | 对比当前状态与任意快照的文件差异 |
| **回滚** | 一键回滚到任意历史快照 |
| **隔离** | 使用独立的 shadow Git repo，不影响用户的 Git 仓库 |

**实现策略**:
```
工作区目录: D:\Project\MyApp\
Shadow Git: D:\Project\MyApp\.aica\checkpoints\

快照流程:
  1. Agent 执行 edit/write_to_file 成功后
  2. 将变更文件复制到 shadow repo
  3. 执行 git add + git commit (消息包含工具名+参数摘要)
  4. 记录 commit hash → 对应 Agent 步骤

回滚流程:
  1. 用户选择要回滚到的步骤
  2. git diff 获取变更文件列表
  3. 从 shadow repo checkout 对应版本
  4. 复制回工作区
  5. 截断 Agent 对话历史到对应步骤
```

**CheckpointManager 接口**:
```csharp
public interface ICheckpointManager
{
    /// <summary>初始化检查点系统（首次快照当前状态）</summary>
    Task InitializeAsync(string workingDirectory);
    
    /// <summary>创建检查点快照</summary>
    Task<string> CreateCheckpointAsync(string description, IEnumerable<string> changedFiles);
    
    /// <summary>获取所有检查点</summary>
    Task<List<CheckpointInfo>> GetCheckpointsAsync();
    
    /// <summary>对比两个检查点之间的差异</summary>
    Task<List<FileDiff>> DiffCheckpointsAsync(string fromHash, string toHash);
    
    /// <summary>回滚到指定检查点</summary>
    Task RestoreCheckpointAsync(string commitHash);
    
    /// <summary>清理检查点数据</summary>
    Task CleanupAsync();
}

public class CheckpointInfo
{
    public string CommitHash { get; set; }
    public string Description { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<string> ChangedFiles { get; set; }
    public int StepIndex { get; set; }
}
```

**排除规则**（不纳入快照的目录/文件）:
```
.git/
.vs/
bin/
obj/
node_modules/
*.user
*.suo
.aica/
```

#### 7.2 上下文追踪系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Context/FileContextTracker.cs`、`src/AICA.Core/Context/EnvironmentContextTracker.cs` |
| **修改文件** | `AgentExecutor.cs`、`SystemPromptBuilder.cs` |
| **工时** | 3 天 |
| **参考** | Cline `FileContextTracker.ts`、`EnvironmentContextTracker.ts` |

**FileContextTracker**:
- 跟踪 Agent 执行过程中读取和修改过的文件
- 在 System Prompt 的 environment details 段落中注入已读文件列表
- 避免 Agent 重复读取同一文件（除非文件已被修改）
- 提供 `GetRecentlyReadFiles()` 和 `GetModifiedFiles()` 方法

```csharp
public class FileContextTracker
{
    private readonly Dictionary<string, FileContextEntry> _trackedFiles = new();
    
    public void RecordFileRead(string filePath, int lineCount);
    public void RecordFileModified(string filePath);
    public bool HasBeenReadSinceLastModification(string filePath);
    public IReadOnlyList<FileContextEntry> GetRecentlyAccessedFiles(int limit = 20);
    public string GetEnvironmentDetails();  // 生成注入 System Prompt 的文本
}
```

**EnvironmentContextTracker**:
- 自动收集 VS 环境信息（打开的文件、活动文档、光标位置、诊断错误）
- 通过 VS SDK 的 `DTE2` 获取编辑器状态
- 在任务开始时和每轮循环时更新

```csharp
public class EnvironmentContextTracker
{
    public string ActiveDocumentPath { get; }
    public int ActiveDocumentLine { get; }
    public List<string> OpenDocuments { get; }
    public List<DiagnosticInfo> CurrentErrors { get; }
    public string GetEnvironmentSummary();
}
```

#### 7.3 Prompt 变体系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Prompt/PromptVariant.cs`、`src/AICA.Core/Prompt/PromptRegistry.cs` |
| **修改文件** | `SystemPromptBuilder.cs` |
| **工时** | 3 天 |
| **参考** | Cline `PromptRegistry.ts` (338行)、`variants/` 目录 |

**功能要求**:

不同的 LLM 模型对 System Prompt 的偏好不同：

| 模型家族 | 特点 | Prompt 策略 |
|---------|------|-------------|
| **Qwen** | 擅长中文、工具调用格式严格 | 中文规则、JSON 严格格式指导 |
| **DeepSeek** | 推理能力强、上下文长 | 精简规则、减少冗余约束 |
| **ChatGLM** | 中文优化 | 中文规则、示例驱动 |
| **通用 (Generic)** | 默认 | 完整规则集 |

**PromptRegistry 设计**:
```csharp
public class PromptRegistry
{
    private readonly Dictionary<string, IPromptVariant> _variants = new();
    
    public void Register(string modelFamily, IPromptVariant variant);
    public IPromptVariant GetVariant(string modelName);
    public string BuildSystemPrompt(SystemPromptContext context);
}

public interface IPromptVariant
{
    string ModelFamily { get; }
    bool Matches(string modelName);  // 模型名匹配（正则或前缀）
    string GetToolCallingGuidance();  // 工具调用格式指导
    string GetBehaviorRules();        // 行为规则
    int RecommendedMaxTokens { get; } // 推荐最大 token
}
```

#### 7.4 ContextManager 增强

| 项目 | 说明 |
|------|------|
| **修改文件** | `src/AICA.Core/Context/ContextManager.cs` |
| **工时** | 2 天 |

**增强内容**:
- 集成 FileContextTracker 的数据
- 集成 EnvironmentContextTracker 的数据
- 优化截断策略：保留工具调用结果中的关键信息（文件路径、错误消息），丢弃大段文件内容
- 引入 token 预算分配：

| 预算组件 | 占比 | 说明 |
|---------|------|------|
| System Prompt + 工具定义 | 20% | 固定部分 |
| 用户规则 | 5% | .aicarules 内容 |
| 环境上下文 | 5% | 打开文件、错误列表等 |
| 对话历史（保护） | 15% | 首条消息 + 最近 3 轮 |
| 对话历史（可裁剪） | 55% | 中间对话，按时间倒序裁剪 |

#### 7.5 对话历史浏览 UI

| 项目 | 说明 |
|------|------|
| **修改文件** | `ChatToolWindowControl.xaml`、`ChatToolWindowControl.xaml.cs` |
| **工时** | 2 天 |

**功能要求**:
- 在聊天窗口顶部添加"历史记录"按钮
- 点击后显示历史对话列表（标题 + 时间 + 消息数）
- 可选择恢复历史对话继续
- 可删除历史对话
- 可导出为 Markdown
- 利用已有的 `ConversationStorage` 后端

**交付物**:
- 工作区快照与回滚
- 文件/环境上下文自动追踪
- 模型感知的 Prompt 变体系统
- 增强的上下文管理
- 对话历史浏览 UI

**里程碑 M3**: 长任务稳定运行，出问题可回滚

---

### Sprint 8: P2-A — UI 增强 + @mention 上下文（预计 12 天）

> **目标**: 提升 UI 交互体验到接近 Cline 水平

#### 8.1 @mention 上下文引用

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Context/MentionParser.cs`、`src/AICA.VSIX/Context/VSMentionProvider.cs` |
| **修改文件** | `ChatToolWindowControl.xaml.cs`、`AgentExecutor.cs` |
| **工时** | 4 天 |
| **参考** | Cline `mentions/` 目录 |

**支持的 @mention 类型**:

| 语法 | 功能 | 实现方式 |
|------|------|---------|
| `@file:path/to/file.cs` | 将文件内容注入上下文 | 读取文件，追加到 user message |
| `@folder:src/Models` | 将目录结构注入上下文 | list_dir 结果追加到 user message |
| `@problems` | 将 VS 错误列表注入上下文 | 通过 `DTE2.ToolWindows.ErrorList` 获取 |
| `@selection` | 将编辑器选中文本注入上下文 | 通过 `DTE2.ActiveDocument.Selection` 获取 |
| `@git:diff` | 将 Git diff 注入上下文 | 执行 `git diff` 获取 |

**输入框自动补全**:
- 用户输入 `@` 后弹出补全菜单
- 文件路径支持模糊匹配
- 选择后在输入框中显示为高亮标记

**MentionParser**:
```csharp
public class MentionParser
{
    /// <summary>解析消息中的 @mention，提取并替换为实际内容</summary>
    public async Task<(string processedMessage, List<MentionContext> contexts)> 
        ParseAndResolveAsync(string rawMessage, IAgentContext context);
}

public class MentionContext
{
    public MentionType Type { get; set; }  // File, Folder, Problems, Selection, GitDiff
    public string Reference { get; set; }   // 原始引用文本
    public string Content { get; set; }     // 解析后的内容
}
```

#### 8.2 任务计划面板

| 项目 | 说明 |
|------|------|
| **修改文件** | `ChatToolWindowControl.xaml`、`ChatToolWindowControl.xaml.cs` |
| **工时** | 2 天 |

**功能要求**:
- 在聊天窗口上方或侧边显示当前任务计划
- 实时同步 `update_plan` 工具更新的步骤
- 每步显示状态图标: ⏳ Pending / 🔄 In Progress / ✅ Completed / ❌ Failed
- 可点击步骤跳转到对应的对话位置

**HTML 渲染模板**:
```html
<div class="task-plan">
  <h3>📋 任务计划</h3>
  <div class="plan-step completed">✅ 1. 读取 User.cs 文件</div>
  <div class="plan-step in-progress">🔄 2. 添加 Email 属性</div>
  <div class="plan-step pending">⏳ 3. 更新构造函数</div>
  <div class="plan-step pending">⏳ 4. 运行测试验证</div>
</div>
```

#### 8.3 Slash 命令

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Commands/SlashCommandParser.cs` |
| **修改文件** | `ChatToolWindowControl.xaml.cs` |
| **工时** | 1.5 天 |

**支持的命令**:

| 命令 | 功能 |
|------|------|
| `/new` | 清空对话，开始新任务 |
| `/clear` | 清空当前对话显示 |
| `/plan` | 切换到 Plan 模式 |
| `/act` | 切换到 Act 模式 |
| `/history` | 显示对话历史 |
| `/export` | 导出当前对话为 Markdown |
| `/checkpoint` | 显示检查点列表 |
| `/rollback` | 回滚到上一个检查点 |
| `/help` | 显示帮助信息 |

#### 8.4 Markdown 渲染增强

| 项目 | 说明 |
|------|------|
| **修改文件** | `ChatToolWindowControl.xaml.cs`（HTML 模板） |
| **工时** | 2 天 |

**增强内容**:
- 代码块复制按钮（右上角 📋 图标）
- 代码块语言标签显示
- 表格渲染优化
- 链接可点击（在默认浏览器中打开）
- Mermaid 图表支持（可选）
- 代码块行号显示

#### 8.5 通知系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.VSIX/Notifications/NotificationService.cs` |
| **修改文件** | `AgentExecutor.cs` |
| **工时** | 1 天 |

**功能要求**:
- Agent 需要用户关注时发送 VS InfoBar 通知
- 触发场景：
  - `ask_followup_question` 等待用户回复
  - `attempt_completion` 等待用户确认
  - Agent 连续失败达到阈值
  - 任务完成
- 通知可配置（通过 SecurityOptions 开关）

#### 8.6 new_task 子任务工具

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Tools/NewTaskTool.cs` |
| **工时** | 1.5 天 |
| **参考** | Cline `NewTaskHandler.ts` (64行) |

**功能要求**:
- 参数：`task`（任务描述）、`context`（相关上下文信息）
- 在 UI 中以新的对话标签/分区启动子任务
- 子任务共享工作区但拥有独立的对话历史和 Agent 实例
- 子任务完成后，结果摘要返回给父任务

**交付物**:
- @mention 上下文引用（@file, @folder, @problems, @selection）
- 任务计划面板
- Slash 命令
- Markdown 渲染增强
- 通知系统
- 子任务工具

**里程碑 M4**: UI 交互体验接近 Cline 水平

---

### Sprint 9: P2-B — 高级功能 + 扩展性（预计 12 天）

> **目标**: 补齐高级功能，提升可扩展性和长期可维护性

#### 9.1 Hooks 系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Hooks/HookExecutor.cs`、`src/AICA.Core/Hooks/HookDiscovery.cs` |
| **工时** | 4 天 |
| **参考** | Cline `hooks/hook-executor.ts` (283行)、`hook-factory.ts` (1041行) |

**功能要求**:

用户可在 `.aica/hooks/` 目录下创建 shell 脚本，在工具执行前后自动运行：

| Hook 类型 | 触发时机 | 用途示例 |
|-----------|---------|---------|
| `pre-edit` | edit/write_to_file 执行前 | 代码格式检查、lint |
| `post-edit` | edit/write_to_file 执行后 | 自动格式化、构建验证 |
| `pre-command` | run_command 执行前 | 命令安全审计日志 |
| `post-command` | run_command 执行后 | 输出后处理 |
| `pre-compact` | condense 执行前 | 保存上下文快照 |

**Hook 文件格式**:
```
.aica/hooks/
├── pre-edit.ps1       # PowerShell 脚本
├── post-edit.ps1
├── pre-command.bat     # 批处理脚本
└── post-command.bat
```

**Hook 执行环境变量**:
| 变量 | 说明 |
|------|------|
| `AICA_TOOL_NAME` | 当前工具名 |
| `AICA_TOOL_PARAMS` | 工具参数 JSON |
| `AICA_FILE_PATH` | 操作的文件路径 |
| `AICA_WORKING_DIR` | 工作目录 |

**Hook 安全约束**:
- Hook 执行超时限制（默认 30 秒）
- Hook 返回非 0 退出码可阻止工具执行（pre-hooks）
- Hook 执行需要在 Options 中启用
- Hook 错误不影响 Agent 循环（记录日志并继续）

#### 9.2 Skills 系统

| 项目 | 说明 |
|------|------|
| **新建文件** | `src/AICA.Core/Skills/SkillDiscovery.cs`、`src/AICA.Core/Skills/SkillLoader.cs` |
| **工时** | 2 天 |
| **参考** | Cline `skills.ts` (136行) |

**功能要求**:

Skills 是预定义的任务模板，存放在 `.aica/skills/` 目录：

```
.aica/skills/
├── create-unit-test.md
├── add-logging.md
├── create-api-endpoint.md
└── refactor-to-pattern.md
```

**Skill 文件格式**:
```markdown
---
name: Create Unit Test
description: 为指定类生成单元测试
parameters:
  - name: class_name
    description: 要测试的类名
    required: true
  - name: framework
    description: 测试框架 (mstest/nunit/xunit)
    required: false
    default: mstest
---

## Steps

1. 读取目标类的源代码
2. 分析类的公开方法和属性
3. 创建对应的测试类文件
4. 为每个公开方法生成测试方法
5. 使用 AAA 模式 (Arrange, Act, Assert)
```

- Agent 可通过 `use_skill` 工具调用技能
- 技能内容注入到 System Prompt 中作为任务指导
- 用户可在 Slash 命令中使用 `/skill:create-unit-test class_name=User`

#### 9.3 单元测试框架

| 项目 | 说明 |
|------|------|
| **新建项目** | `src/AICA.Tests/` (xUnit 测试项目) |
| **工时** | 4 天 |

**测试覆盖范围**:

| 模块 | 测试内容 | 优先级 |
|------|---------|--------|
| **AgentExecutor** | 循环控制、去重、幻觉抑制、错误处理 | P0 |
| **ToolDispatcher** | 工具注册、分发、错误处理 | P0 |
| **PathResolver** | 路径解析各场景（工作区内、SourceRoots、消歧） | P0 |
| **SolutionSourceIndex** | .sln/.vcxproj/.csproj 解析 | P0 |
| **SafetyGuard** | 路径访问控制、命令权限 | P0 |
| **CommandPermissionController** | Glob 匹配、Shell 操作符检测 | P1 |
| **ContextManager** | 截断策略、Token 估算 | P1 |
| **V4APatchParser** | 补丁解析各格式 | P1 |
| **MentionParser** | @mention 解析 | P1 |
| **UserRulesLoader** | 规则文件加载 | P2 |
| **ConversationStorage** | 存储/读取/清理 | P2 |

**项目结构**:
```
src/AICA.Tests/
├── Agent/
│   ├── AgentExecutorTests.cs
│   ├── ToolDispatcherTests.cs
│   └── AutoApproveManagerTests.cs
├── Tools/
│   ├── ApplyPatchParserTests.cs
│   └── CommandPermissionTests.cs
├── Workspace/
│   ├── PathResolverTests.cs
│   └── SolutionSourceIndexTests.cs
├── Security/
│   └── SafetyGuardTests.cs
├── Context/
│   ├── ContextManagerTests.cs
│   └── MentionParserTests.cs
└── Fixtures/
    ├── TestSolutions/     # 测试用 .sln/.csproj 文件
    └── TestPatches/       # 测试用 V4A 补丁文件
```

#### 9.4 构建与发布优化

| 项目 | 说明 |
|------|------|
| **修改文件** | `build.ps1`、`.gitignore`、VSIX 清单 |
| **工时** | 2 天 |

**内容**:
- CI/CD 脚本（GitHub Actions 或内网 Jenkins）
- 自动版本号管理
- Release 配置优化（Release 构建、代码签名）
- README 更新（安装指南、功能列表、截图）
- CHANGELOG.md 自动生成

**交付物**:
- Hooks 系统
- Skills 系统
- 完整单元测试
- 构建/发布流程

**里程碑 M5**: 功能完备，可扩展

---

## 四、新增/修改文件清单汇总

### 新建文件

| Sprint | 文件路径 | 说明 |
|--------|---------|------|
| 5 | `src/AICA.Core/Tools/AskFollowupTool.cs` | 向用户提问工具 |
| 5 | `src/AICA.Core/Agent/AutoApproveManager.cs` | 自动审批管理器 |
| 6 | `src/AICA.Core/Agent/AgentMode.cs` | Plan/Act 模式枚举和逻辑 |
| 6 | `src/AICA.Core/Tools/PlanModeRespondTool.cs` | 规划模式响应工具 |
| 6 | `src/AICA.Core/Context/UserRulesLoader.cs` | 用户规则加载器 |
| 6 | `src/AICA.Core/Security/CommandPermissionController.cs` | 命令权限控制器 |
| 6 | `src/AICA.Core/Tools/ApplyPatchTool.cs` | V4A 多文件补丁工具 |
| 7 | `src/AICA.Core/Checkpoint/CheckpointManager.cs` | 检查点管理器 |
| 7 | `src/AICA.Core/Checkpoint/CheckpointTracker.cs` | 检查点追踪器 |
| 7 | `src/AICA.Core/Context/FileContextTracker.cs` | 文件上下文追踪 |
| 7 | `src/AICA.Core/Context/EnvironmentContextTracker.cs` | 环境上下文追踪 |
| 7 | `src/AICA.Core/Prompt/PromptVariant.cs` | Prompt 变体接口 |
| 7 | `src/AICA.Core/Prompt/PromptRegistry.cs` | Prompt 变体注册表 |
| 8 | `src/AICA.Core/Context/MentionParser.cs` | @mention 解析器 |
| 8 | `src/AICA.VSIX/Context/VSMentionProvider.cs` | VS 环境 @mention 提供器 |
| 8 | `src/AICA.Core/Commands/SlashCommandParser.cs` | Slash 命令解析器 |
| 8 | `src/AICA.VSIX/Notifications/NotificationService.cs` | 通知服务 |
| 8 | `src/AICA.Core/Tools/NewTaskTool.cs` | 子任务工具 |
| 9 | `src/AICA.Core/Hooks/HookExecutor.cs` | Hook 执行器 |
| 9 | `src/AICA.Core/Hooks/HookDiscovery.cs` | Hook 发现器 |
| 9 | `src/AICA.Core/Skills/SkillDiscovery.cs` | Skill 发现器 |
| 9 | `src/AICA.Core/Skills/SkillLoader.cs` | Skill 加载器 |
| 9 | `src/AICA.Tests/` (整个项目) | 单元测试 |

### 需修改的现有文件

| 文件 | 修改 Sprint | 修改内容 |
|------|------------|---------|
| `AgentExecutor.cs` | 5, 6, 7, 8, 9 | 集成 AutoApprove、Plan/Act 模式、上下文追踪、Hooks |
| `IUIContext.cs` | 5 | 新增 ShowFollowupQuestionAsync |
| `VSUIContext.cs` | 5 | 实现 ShowFollowupQuestionAsync |
| `ChatToolWindowControl.xaml.cs` | 5, 7, 8 | 工具卡片渲染、历史浏览、@mention、Slash 命令、计划面板 |
| `ChatToolWindowControl.xaml` | 7, 8 | 布局调整（历史按钮、计划面板、模式切换） |
| `SecurityOptions.cs` | 5, 8 | 自动审批配置项、Hook 开关 |
| `SystemPromptBuilder.cs` | 6, 7 | 用户规则注入、Plan 模式指导、环境上下文、Prompt 变体 |
| `SafetyGuard.cs` | 6 | 集成 CommandPermissionController |
| `RunCommandTool.cs` | 6 | 使用增强的命令权限控制 |
| `ContextManager.cs` | 7 | 增强截断策略、token 预算分配 |
| `AttemptCompletionTool.cs` | 5 | 结构化交互支持 |
| `build.ps1` | 9 | CI/CD 支持 |
| `AICA.sln` | 9 | 添加 AICA.Tests 项目 |

---

## 五、里程碑与验收标准

| 里程碑 | Sprint | 时间 | 核心验收标准 |
|--------|--------|------|-------------|
| **M1: Agent 可交互** | 5 | Week 2 | Agent 可主动提问；读操作自动审批；工具调用可视化卡片 |
| **M2: 安全+规划** | 6 | Week 4 | Plan/Act 切换；.aicarules 生效；命令权限 Glob 匹配；apply_patch 可用 |
| **M3: 长任务稳定** | 7 | Week 7 | Checkpoint 可创建/回滚；长对话不丢失关键上下文；历史对话可浏览 |
| **M4: UI 体验** | 8 | Week 10 | @mention 可用；Slash 命令可用；计划面板实时更新；Markdown 代码块可复制 |
| **M5: 功能完备** | 9 | Week 13 | Hooks 可用；Skills 可用；单元测试覆盖核心模块；构建流程自动化 |

### 关键验收测试用例

| 测试场景 | Sprint | 预期行为 |
|----------|--------|---------|
| "给 User 类添加 Email 属性" | M1 | Agent 读文件 → 提问"使用什么验证？" → 用户回复 → edit → Diff 预览 → attempt_completion |
| 输入 `/plan 重构 UserService` | M2 | 切换 Plan 模式 → Agent 分析并输出计划 → 用户确认 → `/act` 切换执行 |
| Agent 修改 3 个文件后出错 | M3 | 用户可在 UI 中选择回滚到第 2 步的检查点 |
| 输入 `@file:User.cs 这个类缺少什么？` | M4 | User.cs 内容自动注入上下文 → Agent 分析 |
| `.aica/hooks/post-edit.ps1` 存在时 | M5 | edit 工具执行后自动运行 hook 脚本 |

---

## 六、功能完成后的 Cline 对标预期

```
完成所有 Sprint 后:

核心工具:           ██████████████████████████████  13/13 (100%)  ✅
Agent 循环:         ██████████████████████████████        (100%)  ✅
自动审批:           ██████████████████████████████        (100%)  ✅
安全机制:           ██████████████████████████████        (100%)  ✅
上下文管理:         ████████████████████████████░░         (95%)  ✅
Prompt 系统:        ████████████████████████████░░         (95%)  ✅
Workspace 感知:     ██████████████████████████████        (100%)  ✅ (含 VS 特有能力)
对话持久化:         ██████████████████████████████        (100%)  ✅
检查点系统:         ████████████████████████████░░         (90%)  ✅
UI/UX:              ████████████████████████░░░░░░         (80%)  ✅
用户规则/Skills:    ████████████████████████████░░         (90%)  ✅
Hooks 系统:         ████████████████████████░░░░░░         (80%)  ✅
单元测试:           ████████████████████████░░░░░░         (80%)  ✅
综合完成度:         ████████████████████████████░░     约 92%  ✅
```

**与 Cline 的差异（设计上不追赶的部分）**:
- ❌ **浏览器自动化** — 离线环境不适用
- ❌ **Web 搜索/抓取** — 离线环境不适用
- ❌ **MCP 协议** — 可作为后续扩展
- ❌ **React Webview** — 使用 WPF WebBrowser（VS2022 限制）
- ❌ **40+ LLM 提供商** — 仅需 OpenAI 兼容 API（内网 LLM）

**AICA 独有优势（Cline 没有的）**:
- ✅ **SolutionSourceIndex** — 解析 .sln/.vcxproj/.csproj，支持 CMake out-of-source
- ✅ **PathResolver** — 跨工作区 + 源码根的统一路径解析
- ✅ **VS 原生集成** — 右键菜单、VS Diff 服务、Error List、Solution Explorer

---

## 七、技术风险与对策

| 风险 | 影响 | 可能性 | 对策 |
|------|------|--------|------|
| Checkpoint Git 操作与用户 Git 冲突 | 🔴 高 | 🟡 中 | 使用完全隔离的 shadow repo，不在用户 .git 中操作 |
| WPF WebBrowser 控件功能限制 | 🟡 中 | 🟡 中 | 复杂交互用 JS 实现（已有基础）；极端情况考虑 CefSharp |
| V4A Diff 解析复杂度 | 🟡 中 | 🟢 低 | 先实现基础格式，逐步支持 @@ 上下文标记 |
| Prompt 变体维护成本 | 🟡 中 | 🟡 中 | 以 Generic 为基础，变体只覆盖差异部分 |
| Hook 脚本安全风险 | 🟡 中 | 🟢 低 | 超时控制 + 可在 Options 中全局禁用 |
| 大量新代码引入回归 | 🟡 中 | 🟡 中 | Sprint 9 集中编写单元测试 + 每个 Sprint 后做 FreeCAD 回归测试 |

---

## 八、工时汇总

| Sprint | 内容 | 工时 |
|--------|------|------|
| Sprint 5 | P0: Agent 交互闭环 | 10 天 |
| Sprint 6 | P1-A: 安全+模式+规则+补丁 | 10 天 |
| Sprint 7 | P1-B: 检查点+上下文+Prompt+历史UI | 15 天 |
| Sprint 8 | P2-A: @mention+计划面板+Slash+通知+子任务 | 12 天 |
| Sprint 9 | P2-B: Hooks+Skills+测试+CI/CD | 12 天 |
| **总计** | | **59 天（约 12 周）** |

---

**文档版本**: v1.0  
**创建日期**: 2026-02-10  
**基于**: AICA v1.9.0+ 现状分析 + Cline v3.56.2 完整功能对标
