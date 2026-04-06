# AICA MCP 工具透传 + System Prompt 重构计划

## Context

非正式部署测试发现 MiniMax-M2.5 不主动选择 GitNexus 工具（D-01），根因是 AICA 手动重写了简化版工具描述，丢失了 GitNexus 原生的 `WHEN TO USE` 引导和多个有用参数。对比 OpenCode 发现：同样的 MiniMax-M2.5 + GitNexus，OpenCode 能正确选择工具，因为它直接透传 MCP 原生工具定义。

本次重构两个目标：
1. **MCP 工具透传**：从 MCP `tools/list` 获取原生工具定义，替代手动硬编码
2. **System Prompt 精简**：借鉴 OpenCode 的简洁风格，去掉冗余规则，减少 token 浪费

---

## Part 1: MCP 工具定义透传

### Step 1.1: McpClient 新增 ListToolsAsync

**文件:** `src/AICA.Core/LLM/McpClient.cs`

新增方法，调用 MCP `tools/list` 获取服务器端工具定义：

```csharp
public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken ct)
{
    var result = await SendRequestAsync("tools/list", new { }, ct);
    // 解析 result.tools 数组，每个元素含 name, description, inputSchema
    return parsed;
}
```

新增数据类：
```csharp
public class McpToolDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public JsonElement InputSchema { get; set; }  // 原生 JSON Schema，直接透传
}
```

### Step 1.2: McpBridgeTool 改为动态获取定义

**文件:** `src/AICA.Core/Tools/McpBridgeTool.cs`

**改动：**
- `CreateAllTools()` 改为 `async CreateAllToolsAsync(IGitNexusProcessManager pm)`
- 先调用 `McpClient.ListToolsAsync()` 获取原生定义
- 用原生 description 和 inputSchema 替代手动硬编码
- 保留 fallback：如果 `ListToolsAsync` 失败，回退到当前硬编码定义
- 保留 ToolMetadata（Category, Tags, TimeoutSeconds 等 AICA 特有属性）

**透传的参数（当前缺失的）：**

| 工具 | 新增参数 |
|------|---------|
| context | `uid`, `file_path`, `include_content` |
| query | `task_context`, `goal`, `limit`, `max_symbols`, `include_content` |
| detect_changes | `scope`, `base_ref` |
| impact | （从 GitNexus 原生获取完整参数）|

**透传的描述（替代当前简化版）：**
- GitNexus 原生描述包含 `WHEN TO USE` / `AFTER THIS` 引导
- 原生描述 ~300-500 字 vs 当前 ~30-50 字

### Step 1.3: 参数转换层

**文件:** `src/AICA.Core/Tools/McpBridgeTool.cs`

MCP inputSchema 是 JSON Schema 格式，需要转换为 AICA 的 `ToolParameters` 格式：

```csharp
private static ToolParameters ConvertMcpSchema(JsonElement inputSchema)
{
    // 从 inputSchema.properties 提取参数定义
    // 映射 type, description, required, enum, default
    // 返回 ToolParameters
}
```

### Step 1.4: 调用链适配

**文件:** `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`

当前 `CreateAllTools()` 是同步调用。改为 async 后，调用链需要适配：
- `ChatToolWindowControl` 中注册 GitNexus 工具的位置改为 async

---

## Part 2: System Prompt 精简重构

### 核心原则（借鉴 OpenCode）

1. **工具描述不放 System Prompt** — 已通过 function calling API 的 tools 参数发送，System Prompt 中的文本描述是冗余的
2. **简洁直接** — OpenCode 的 default.txt ~100 行，AICA 当前 ~800 行
3. **规则按场景裁剪** — 当前 DynamicToolSelector 已做工具裁剪，System Prompt 也应相应裁剪
4. **去掉 GitNexus 专门引导** — 原生工具描述自带 `WHEN TO USE`，不需要额外 few-shot

### Step 2.1: 移除 AddToolDescriptions

**文件:** `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

删除 `AddToolDescriptions()` 方法（lines 51-80）。工具定义已通过 `ToolDispatcher.GetToolDefinitions()` → OpenAI function calling API 发送，System Prompt 中再写一遍是浪费 2000-3000 tokens。

### Step 2.2: 移除 AddGitNexusGuidance

**文件:** `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

删除 `AddGitNexusGuidance()` 方法（lines 524-561）。原生工具描述已包含 `WHEN TO USE` 引导 + few-shot 示例不再需要（原生描述更准确）。

同时删除 `AgentExecutor.cs` 中对 `AddGitNexusGuidance` 的调用。

### Step 2.3: 精简 Core Rules

**文件:** `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

当前 Core Rules（lines 127-166）~1200 tokens。借鉴 OpenCode 风格精简：

**保留（必要规则）：**
- 任务完成规则（attempt_completion）— 但简化表述
- 安全规则（路径边界、权限）
- 平台特定命令（Windows vs Unix）

**移除（冗余/低效）：**
- 工具调用规则的过度详细说明（LLM 已通过 function calling 知道如何调用工具）
- attempt_completion 的大段详细要求（简化为 1-2 条核心规则）
- 数量一致性检查规则（过于细节，可以移到 .aica-rules）

### Step 2.4: 精简 Advanced Rules

当前 ~1500 tokens。保留有效规则，移除 MiniMax-M2.5 不需要的规则：

**保留：**
- 效率规则（最小化工具调用）
- 搜索策略（C++ 特定搜索模式）
- 代码生成质量（语法正确性）
- 反幻觉规则

**移除：**
- 证据引用规则（对 MiniMax 效果有限）
- 结构化输出格式要求（过于细节）
- 工具替代透明度（原生描述已解决）

### Step 2.5: 借鉴 OpenCode 风格重写 Base Prompt

**当前 Base Prompt（lines 28-40）：** 通用角色描述
**目标：** 借鉴 OpenCode default.txt 的风格，增加：
- 简洁回答原则（fewer than 4 lines）
- 不加不必要的前言/后记
- 遵循代码库现有约定
- 代码引用格式 `file_path:line_number`

### Step 2.6: 更新右键命令移除 GitNexus 引导

**文件:**
- `src/AICA.VSIX/Commands/ExplainCodeCommand.cs`
- `src/AICA.VSIX/Commands/GenerateTestCommand.cs`
- `src/AICA.VSIX/Commands/RefactorCommand.cs`

移除之前添加的 GitNexus 引导文本（"如有 GitNexus 工具可用，请优先使用..."）。原生工具描述自带引导后不再需要。

---

## Part 3: 验证

### Step 3.1: 编译 + 测试
- `build.ps1 -Restore -Build` → BUILD SUCCEEDED
- `vstest.console.exe` → 无新增失败

### Step 3.2: 用户机器部署验证
- 安装新 VSIX
- 删除 `.gitnexus/` 重新索引
- DebugView 确认：
  - `tools/list` 获取成功
  - 原生工具描述被使用
  - System Prompt token 数显著下降

### Step 3.3: 功能复验
- 右键解释代码 → LLM 自主选择 gitnexus_context（无需 prompt 引导）
- 右键生成测试 → LLM 自主选择 gitnexus_context
- 对话式问题 → 正常回答，不调用工具

---

## 实施顺序

| Step | 内容 | 改动文件 | 预估行数 |
|------|------|---------|---------|
| 1.1 | McpClient.ListToolsAsync | McpClient.cs | +40 |
| 1.2 | McpBridgeTool 动态获取定义 | McpBridgeTool.cs | ~60 改动 |
| 1.3 | Schema 转换层 | McpBridgeTool.cs | +30 |
| 1.4 | 调用链 async 适配 | ChatToolWindowControl.xaml.cs | ~5 |
| 2.1 | 移除 AddToolDescriptions | SystemPromptBuilder.cs | -30 |
| 2.2 | 移除 AddGitNexusGuidance | SystemPromptBuilder.cs + AgentExecutor.cs | -40 |
| 2.3 | 精简 Core Rules | SystemPromptBuilder.cs | ~-30 |
| 2.4 | 精简 Advanced Rules | SystemPromptBuilder.cs | ~-40 |
| 2.5 | 重写 Base Prompt | SystemPromptBuilder.cs | ~+20 -15 |
| 2.6 | 移除右键 GitNexus 引导 | 3 个 Command 文件 | -6 |
| 3.x | 验证 | — | — |

**预估 token 节省：**
- 移除工具文本描述：~2000-3000 tokens
- 精简规则：~500-800 tokens
- 移除 GitNexus 引导：~400 tokens
- **总计：~3000-4000 tokens 节省（约 40%）**

---

## 风险

| 风险 | 概率 | 缓解 |
|------|------|------|
| MCP tools/list 调用失败 | 低 | 回退到硬编码定义 |
| 原生描述太长导致 function calling 超 token | 低 | 截断过长描述（>1000 字） |
| 规则精简后 LLM 行为退化 | 中 | 保留核心规则，逐步移除并验证 |
| async 改动破坏启动时序 | 低 | 工具注册失败不阻塞 AICA 启动 |
