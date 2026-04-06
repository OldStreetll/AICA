# AICA Agent 演进计划 v4 — 从工具调用者到自主编程 Agent

> 版本: 4.1
> 日期: 2026-03-19
> 状态: Phase 0-2.5 已完成 + POCO 综合测试 7 Bug 修复, Phase 3 待启动
> 基于: POCO 综合测试 47/50 用例实测 + 业界成熟 Agent 框架深度调研
> 目标: 将 AICA 从 "单轮工具调用者" (44/100) 演进为 "自主编程 Agent" (80/100)
> 参照: OpenHands, Cline, Aider, Semantic Kernel Agent Framework, LangGraph

---

## 一、当前状态评估 (基于 POCO 综合测试)

### 能力评分 (44/100)

| 维度 | 满分 | 当前 | 目标 | 差距 |
|------|------|------|------|------|
| 工具调用 | 10 | 7 | 9 | LLM 可靠性补偿 |
| 任务规划 | 10 | 5 | 8 | 需要多步验证 |
| 上下文管理 | 10 | 4 | 8 | 三层分层管理 |
| 知识检索 | 10 | 6 | 9 | 语义向量检索 |
| 自主性 | 10 | 3 | 7 | 多 Agent + 并行 |
| 记忆持久化 | 10 | 2 | 7 | Memory Bank |
| 主动性 | 10 | 1 | 5 | 事件驱动建议 |
| 多模型适配 | 10 | 5 | 8 | Architect/Editor |
| 错误恢复 | 10 | 6 | 8 | 自验证管线 |
| 用户体验 | 10 | 5 | 8 | 权限门控 + 流式 |

### POCO 测试暴露的关键问题

| 问题 | 测试证据 | 根因 | 需要的能力 |
|------|----------|------|-----------|
| 复杂分析覆盖率仅 50% | TC-K01: 找到 10/20 Channel | 单 Agent 串行，无验证 | 多 Agent + 自验证 |
| 长上下文工具幻觉 | TC-C06/D01: messages≥8 伪造结果 | LLM function calling 退化 | Architect/Editor + condense |
| Condense 后记忆丢失 | TC-G02: "尚未读取任何文件" | condense 摘要质量差 | 分层上下文 + Memory Bank |
| LLM 行数/重载计数错误 | TC-C02: 276 vs 450 行 | LLM 推理弱点 | 自验证管线 |
| 修改冲突循环追问 | TC-C03: 3 次 followup | 冲突检测 UX 差 | 智能冲突解决 |

---

## 二、业界参照框架

### 核心参照

| 框架 | Stars | 关键架构特征 | AICA 可借鉴 |
|------|-------|-------------|------------|
| **OpenHands** | ~69K | 事件流架构，所有交互为 typed events | Event Hub + 不可变状态 |
| **Cline** | ~58K | VS Code Agent, Memory Bank, 权限门控 | 结构化记忆 + 审批流 |
| **Aider** | ~42K | Architect/Editor 双模型, Git 深度集成 | 强模型规划 + 弱模型执行 |
| **SK Agent Framework** | ~27K | C# 原生多 Agent 编排 (Sequential/Concurrent/Handoff) | **直接可用** |
| **Continue** | ~32K | 多模型路由 (补全/聊天/编辑分离) | 任务感知模型选择 |
| **LangGraph** | — | 图结构工作流, 条件边, 循环, 检查点 | 工作流建模 |

### 关键论文

| 论文 | 核心思想 | AICA 应用 |
|------|----------|----------|
| ReVeal (arxiv) | 生成→自验证→迭代改进循环 | 自动生成测试验证输出 |
| Reflection-Driven Control | 反思作为一等控制循环 | CriticAgent 审查每个输出 |
| Context Engineering for Multi-Agent | 意图澄清→语义检索→知识合成→子 Agent 协调 | 分层检索管线 |

---

## 三、Phase 2.5→3 过渡：已实施部分的重构与改进

> **目标**: 在启动 Phase 3 多 Agent 之前，先清理已实施部分的技术债务
> **工期**: 2-3 周 (可与 Phase 3 并行)
> **原则**: 每项改进都有成熟的开源参照

---

### R1 [P0]: AgentExecutor 拆分 — 消除 God Object

**现状**: AgentExecutor.cs ~2000+ 行，集中了主循环、Token 管理、工具调度、幻觉检测、叙述抑制、Plan 管理等所有逻辑。每次修 Bug 都在同一文件中叠加条件分支。

**参照框架**:

| 框架 | 架构模式 | 参考链接 |
|------|---------|---------|
| **OpenHands** | AgentController 只做 `while True: agent.step()`，运行时/沙箱/事件流独立为 SDK | https://github.com/OpenHands/software-agent-sdk |
| **Cline** | Controller (状态管理) / Task (执行) / WebviewProvider (UI) 三层分离 | https://github.com/cline/prompts/blob/main/.clinerules/cline-architecture.md |
| **Aider** | Coder 基类 + Strategy 模式子类 (EditBlockCoder, WholeFileCoder 等)，每种编辑格式独立实现 | https://github.com/Aider-AI/aider/blob/main/aider/coders/base_coder.py |

**AICA 实施方案**:

```
AgentExecutor (瘦身到 ~300 行, 仅编排 step() 循环)
  ├── IterationController        — 循环控制、迭代限制、强制完成判定
  │     参照: OpenHands AgentController 的简洁 while 循环
  ├── TokenBudgetManager         — Token 计算、Condense 触发、预算裁剪
  │     参照: Anthropic Compaction API 的阈值策略
  ├── ToolCallProcessor          — 工具调用解析、去重、签名、Text Fallback
  │     参照: Aider 的 transport/coder 分离
  ├── ResponseProcessor          — 叙述抑制、幻觉检测、thinking 提取、质量过滤
  │     参照: Cline 的模块化 System Prompt 组合
  ├── PlanManager                — 任务规划状态、update_plan 处理
  │     参照: Cline 的 Plan Mode / Act Mode 切换
  └── CompletionHandler          — 完成判定、attempt_completion 处理
```

**拆分策略**: 渐进式重构，每次提取一个组件，保持测试通过：
1. 先提取 `TokenBudgetManager` (最独立，无副作用)
2. 再提取 `ToolCallProcessor` (工具调用相关逻辑)
3. 提取 `ResponseProcessor` (叙述/幻觉/过滤)
4. 提取 `PlanManager` (Plan 状态)
5. 最后 AgentExecutor 只剩编排循环

---

### R2 [P0]: Condense 摘要质量改进 — 结构化压缩

**现状**: `BuildAutoCondenseSummary` 生成自由文本摘要，condense 后 LLM 丢失"之前读了哪些文件"等关键信息 (TC-G02)。

**参照框架**:

| 框架 | 压缩策略 | 参考链接 |
|------|---------|---------|
| **Anthropic Compaction API** | 7-12K 字符结构化摘要，含 analysis/files/pending/state 分段 | https://platform.claude.com/docs/en/build-with-claude/compaction |
| **ACON 框架** | 压缩指南优化 — 压缩失败时分析原因并更新压缩规则 | https://arxiv.org/abs/2510.00615 |
| **JetBrains 研究** | 观察遮蔽 (选择性隐藏旧工具输出) + LLM 摘要 (独立摘要模型) | https://blog.jetbrains.com/research/2025/12/efficient-context-management/ |
| **Factory.ai** | 评估压缩质量 — 压缩后上下文是否保留任务完成能力 | https://factory.ai/news/evaluating-compression |

**AICA 实施方案**:

```csharp
// 改进: src/AICA.Core/Context/StructuredCondenser.cs
public class StructuredCondenser
{
    /// <summary>
    /// 生成结构化摘要，每个 section 是独立的、可检索的
    /// 参照: Anthropic Compaction API 的分段模式
    /// </summary>
    public CondensedContext Condense(List<ChatMessage> history)
    {
        return new CondensedContext
        {
            // Section 1: 任务目标 (永不压缩)
            TaskObjective = ExtractLatestUserRequest(history),

            // Section 2: 已读文件 (路径 + 行数 + 单句摘要)
            FilesRead = ExtractFilesRead(history)
                .Select(f => $"{f.Path} ({f.Lines} lines): {f.OneSentenceSummary}")
                .ToList(),

            // Section 3: 搜索执行 (查询 + 匹配数)
            SearchesPerformed = ExtractSearches(history)
                .Select(s => $"grep '{s.Query}' in {s.Path}: {s.Matches} matches")
                .ToList(),

            // Section 4: 关键发现 (从 LLM 回答中提取)
            KeyFindings = ExtractKeyFindings(history),

            // Section 5: 已修改文件
            FilesModified = ExtractModifiedFiles(history),

            // Section 6: 错误遇到
            ErrorsEncountered = ExtractErrors(history),

            // Section 7: 当前进度
            CurrentProgress = $"{_taskState.CompletedSteps}/{_taskState.TotalSteps} steps"
        };
    }

    /// <summary>
    /// 参照 ACON: 压缩后验证 — 如果 LLM 无法回答关于压缩内容的问题，
    /// 说明压缩丢失了关键信息，需要改进压缩模板
    /// </summary>
    public bool ValidateCompression(CondensedContext context, string testQuery)
    {
        // 用压缩后的上下文回答测试问题，验证信息保留
        return context.CanAnswer(testQuery);
    }
}
```

---

### R3 [P1]: System Prompt 动态工具注入

**现状**: 每次请求注入全部 13 个工具定义 (~4000 tokens)，即使简单问题只需 1-2 个工具。

**参照框架**:

| 框架 | 工具选择策略 | 参考链接 |
|------|------------|---------|
| **Cline** | Plan Mode 只注入 `plan_mode_respond`; Act Mode 注入全部工具 | https://github.com/cline/cline |
| **AutoTool** (论文) | 基于任务上下文动态选择工具集 | https://arxiv.org/html/2512.13278v1 |
| **MemTool** (论文) | Agent 管理自己的工具上下文窗口，短期记忆决定注入哪些工具 | https://arxiv.org/html/2507.21428 |

**AICA 实施方案**:

```csharp
// 新增: src/AICA.Core/Agent/DynamicToolSelector.cs
public class DynamicToolSelector
{
    // 工具分组
    private static readonly Dictionary<string, string[]> ToolGroups = new()
    {
        ["read"] = new[] { "read_file", "list_dir", "list_code_definition_names", "attempt_completion" },
        ["search"] = new[] { "grep_search", "find_by_name", "attempt_completion" },
        ["modify"] = new[] { "read_file", "edit", "write_to_file", "attempt_completion" },
        ["analyze"] = new[] { "read_file", "grep_search", "list_dir", "list_code_definition_names",
                              "find_by_name", "update_plan", "attempt_completion" },
        ["command"] = new[] { "run_command", "attempt_completion" },
        ["conversation"] = new[] { "attempt_completion" }, // 对话不需要工具
        ["full"] = null, // 全部工具
    };

    /// <summary>
    /// 根据请求内容和复杂度选择工具子集
    /// 参照: Cline Plan/Act 模式 + AutoTool 动态选择
    /// </summary>
    public List<ToolDefinition> SelectTools(
        string userRequest, TaskComplexity complexity, List<ToolDefinition> allTools)
    {
        if (complexity == TaskComplexity.Complex)
            return allTools; // Complex 任务需要全部工具

        var intent = ClassifyIntent(userRequest);
        if (ToolGroups.TryGetValue(intent, out var toolNames) && toolNames != null)
        {
            return allTools.Where(t => toolNames.Contains(t.Name)).ToList();
        }

        return allTools; // 无法分类时返回全部
    }
}
```

**预期效果**: Simple 请求的 System Prompt 从 ~9300 tokens 降到 ~6000 tokens，释放 3000+ tokens 给对话。

---

### R4 [P1]: ResponseQualityFilter 配置化

**现状**: `ReasoningStartPatterns` 和 `MetaReasoningPatterns` 硬编码在 C# 代码中，换模型需改代码。

**参照框架**:

| 框架 | 过滤策略 | 参考链接 |
|------|---------|---------|
| **piratos/llmfilters** | YAML 配置 FilterBlock 管线，pre/post 处理钩子 | https://github.com/piratos/llmfilters |
| **Open WebUI Pipelines** | 可配置过滤管线，按模型选择不同过滤器链 | https://github.com/open-webui/pipelines |

**AICA 实施方案**:

```json
// 新增: /.aica/filter-config.json
{
    "models": {
        "MiniMax-M2.5": {
            "reasoningStartPatterns": [
                "i need to check", "i need to search", "让我查看", "让我搜索"
            ],
            "metaReasoningPatterns": [
                "the user is asking me to", "用户想要我"
            ],
            "suppressionLengthThreshold": 300,
            "enableTextFallback": true
        },
        "claude-sonnet": {
            "reasoningStartPatterns": [],
            "metaReasoningPatterns": [],
            "suppressionLengthThreshold": 0,
            "enableTextFallback": false
        }
    }
}
```

```csharp
// 改进: src/AICA.Core/Prompt/ConfigurableResponseFilter.cs
public class ConfigurableResponseFilter
{
    private readonly FilterConfig _config;

    public ConfigurableResponseFilter(string modelName)
    {
        _config = FilterConfigLoader.LoadForModel(modelName);
    }

    public bool IsInternalReasoning(string text)
    {
        if (text.Length > _config.SuppressionLengthThreshold)
            return false;
        // 使用配置中的 patterns 而非硬编码
        return _config.ReasoningStartPatterns.Any(p =>
            text.ToLowerInvariant().StartsWith(p));
    }
}
```

---

### R5 [P1]: 工具去重放宽 — 失败不计入

**现状**: 工具调用失败 (Not Found, Error) 后的重试被去重拦截。TC-G02 中 XMLReader.h 路径错误后重试 4 次均被拦截。

**实施方案**:

```csharp
// 改进: AgentExecutor 工具去重逻辑
// 变更: 只有成功的工具调用才计入去重签名池
if (toolResult.Success)
{
    executedToolSignatures.Add(toolSignature);
}
else
{
    // 失败的调用不计入去重，允许下次重试
    System.Diagnostics.Debug.WriteLine(
        $"[AICA] Tool '{toolCall.Name}' failed, NOT adding to dedup set (allows retry)");
}
```

---

### R6 [P2]: SafetyGuard 沙箱化

**现状**: 基于黑名单的命令拦截，永远有绕过的可能。

**参照框架**:

| 框架 | 沙箱方案 | 参考链接 |
|------|---------|---------|
| **OpenHands** | Docker 容器隔离 (LocalDockerRuntime) | https://github.com/OpenHands/OpenHands |
| **E2B** | Firecracker microVM, ~150ms 启动, .NET SDK | https://github.com/e2b-dev/E2B |
| **DotNetIsolator** | WASM/Wasmtime 隔离 .NET 运行时 | https://github.com/SteveSandersonMS/DotNetIsolator |
| **microsandbox** | 自托管 microVM, <200ms 启动 | https://github.com/zerocore-ai/microsandbox |

**AICA 分层方案**:

```csharp
// 新增: src/AICA.Core/Security/ICommandSandbox.cs
public interface ICommandSandbox
{
    Task<CommandResult> ExecuteAsync(string command, string workingDir, TimeSpan timeout);
}

// 层级 1: 进程隔离 (默认, 无需额外依赖)
public class ProcessSandbox : ICommandSandbox { ... }

// 层级 2: Docker 隔离 (检测 Docker Desktop 可用时启用)
public class DockerSandbox : ICommandSandbox { ... }

// 层级 3: E2B 云隔离 (配置 API Key 后启用)
public class E2BSandbox : ICommandSandbox { ... }
```

---

### R7 [P2]: ChatToolWindowControl UI 层拆分

**现状**: ChatToolWindowControl.xaml.cs 混合了会话管理、Agent 执行、HTML 渲染、Plan 面板、侧边栏等所有逻辑。Bug 5/7 的根因就是 Plan 管理散布在多处。

**参照**: Cline 的 Controller / Task / WebviewProvider 三层分离。

**AICA 实施方案**:

```
ChatToolWindowControl.xaml.cs (UI 事件绑定, ~200 行)
  ├── ConversationManager.cs    — 会话 CRUD + 切换 + 持久化
  ├── AgentRunner.cs            — Agent 执行循环 + 流式渲染
  ├── PlanPanelController.cs    — Plan 面板状态 + DOM 操作
  ├── HtmlRenderer.cs           — HTML 构建 + 浏览器交互
  └── SidebarController.cs      — 侧边栏列表 + 搜索
```

---

### R8 [P2]: Agent 测试框架

**参照框架**:

| 框架 | 测试策略 | 参考链接 |
|------|---------|---------|
| **SWE-bench** | 真实 GitHub issue → 生成补丁 → 运行隐藏测试验证 | https://github.com/SWE-bench/SWE-bench |
| **EleutherAI Eval Harness** | 通用 LLM 评估框架，可配置任务和指标 | https://github.com/EleutherAI/lm-evaluation-harness |

**AICA 两级测试策略**:

```csharp
// 层级 1: 确定性单元测试 (Mock LLM, 无 API 调用)
public class MockLlmProvider : ILLMClient
{
    // 预定义的 LLM 响应序列
    private readonly Queue<string> _responses;

    public MockLlmProvider(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }
}

[Fact]
public async Task AgentExecutor_SimpleQuery_CompletesInOneIteration()
{
    var mock = new MockLlmProvider(
        // LLM 返回 attempt_completion
        "{\"tool_calls\": [{\"name\": \"attempt_completion\", ...}]}"
    );
    var executor = new AgentExecutor(mock, ...);
    var steps = await executor.ExecuteAsync("你好").ToListAsync();
    Assert.Single(steps.Where(s => s.Type == AgentStepType.Complete));
}

// 层级 2: 集成评估 (真实 LLM, 固定测试仓库)
// 参照 SWE-bench: 给定 issue 描述 → Agent 生成补丁 → 运行测试验证
public class AgentEvalHarness
{
    public async Task<EvalResult> EvaluateAsync(string testRepoPath, string issueDescription)
    {
        var agent = CreateAgent(testRepoPath);
        var result = await agent.ExecuteAsync(issueDescription);
        var testsPassed = await RunTestSuite(testRepoPath);
        return new EvalResult { Patch = result, TestsPassed = testsPassed };
    }
}
```

---

### R9 [P2]: 复杂度驱动的 Prompt 差异化

**现状**: Simple/Medium/Complex 三级分类仅影响是否触发 Plan，不影响 LLM 的工具使用策略。

**实施方案**:

```csharp
// 改进: SystemPromptBuilder 根据复杂度注入不同指令
public SystemPromptBuilder AddComplexityGuidance(TaskComplexity complexity)
{
    switch (complexity)
    {
        case TaskComplexity.Simple:
            _builder.AppendLine(
                "## Task Mode: SIMPLE\n" +
                "This is a simple query. Prefer answering from your knowledge context. " +
                "Only use tools if the knowledge context is insufficient. " +
                "Do NOT create a task plan.");
            break;
        case TaskComplexity.Medium:
            _builder.AppendLine(
                "## Task Mode: MEDIUM\n" +
                "This task may require 1-2 tool calls. " +
                "Be efficient — avoid unnecessary searches.");
            break;
        case TaskComplexity.Complex:
            _builder.AppendLine(
                "## Task Mode: COMPLEX\n" +
                "This is a complex multi-step task. " +
                "Create a task plan first, then execute systematically.");
            break;
    }
    return this;
}
```

---

### 重构优先级与依赖关系

```
R1 (AgentExecutor 拆分) ← 最高优先级, 后续所有改进的基础
  │
  ├── R2 (Condense 质量) ← 可与 R1 并行, 独立模块
  │
  ├── R3 (动态工具注入) ← 依赖 R1 中的 ToolCallProcessor 提取
  │
  ├── R4 (Filter 配置化) ← 依赖 R1 中的 ResponseProcessor 提取
  │
  ├── R5 (去重放宽) ← 依赖 R1 中的 ToolCallProcessor 提取
  │
  ├── R9 (复杂度 Prompt) ← 独立, 可随时实施
  │
  ├── R7 (UI 拆分) ← 独立于 Core 改动
  │
  ├── R8 (测试框架) ← 应与 R1 同步建设
  │
  └── R6 (沙箱) ← 可延后到 Phase 3
```

---

## 四、演进路线 (Phase 3-6 详细方案)

---

### Phase 3: 多 Agent 协作 [最高优先级]

> **目标**: 突破单 Agent 串行瓶颈，效率提升 3 倍，覆盖率提升到 80%+
> **参照**: Semantic Kernel Agent Framework (Concurrent/Handoff), Aider (Architect/Editor)
> **工期估计**: 4-6 周

#### 3.1 Architect/Editor 双模型模式

**参照**: Aider — https://github.com/Aider-AI/aider

Aider 的核心创新：强推理模型 (Architect) 描述解决方案，弱/快模型 (Editor) 格式化代码编辑。

**AICA 实现方案**:

```
用户请求 → TaskComplexityAnalyzer
             ├── Simple → Editor 模型直接执行 (快, 便宜)
             ├── Medium → Architect 规划 + Editor 执行
             └── Complex → Architect 规划 + 多 Agent 并行 + Editor 执行
```

```csharp
// 新增: src/AICA.Core/Agent/ArchitectEditorPipeline.cs
public class ArchitectEditorPipeline
{
    private readonly ILLMClient _architectModel;  // 强推理模型 (如 Claude/GPT-4)
    private readonly ILLMClient _editorModel;     // 快速模型 (如 MiniMax-M2.5)

    public async Task<AgentResult> ExecuteAsync(string userRequest)
    {
        var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(userRequest);

        if (complexity == TaskComplexity.Simple)
        {
            // 简单任务: Editor 直接执行
            return await _editorModel.ExecuteWithTools(userRequest);
        }

        // 中等/复杂任务: Architect 先规划
        var plan = await _architectModel.GeneratePlan(userRequest);

        // Editor 按计划执行每一步
        foreach (var step in plan.Steps)
        {
            var result = await _editorModel.ExecuteStep(step);
            step.Status = result.Success ? StepStatus.Completed : StepStatus.Failed;
        }

        return AgentResult.FromPlan(plan);
    }
}
```

**配置**:
```json
// AICA Options 页面新增
{
    "ArchitectModel": "claude-sonnet-4-6",
    "EditorModel": "MiniMax-M2.5",
    "UseArchitectForComplexTasks": true
}
```

#### 3.2 SK 原生多 Agent 编排

**参照**: Semantic Kernel Agent Framework — https://github.com/microsoft/semantic-kernel

SK 已内置 5 种编排模式，AICA 可直接使用：

| SK 编排模式 | AICA 应用场景 | 示例 |
|------------|-------------|------|
| **Sequential** | 规划→实现→测试→审查 | Plan → Code → Test → Review |
| **Concurrent** | 并行搜索/分析 | 同时搜 3 个模块的继承关系 |
| **Handoff** | 动态委托给专家 Agent | "这是安全问题" → 转给 SecurityAgent |
| **Magentic** | 管理者协调专家团队 | OrchestratorAgent 指挥 4 个子 Agent |
| **Group Chat** | 多 Agent 讨论问题 | CodeAgent + ReviewAgent 对话 |

**AICA 子 Agent 定义**:

```csharp
// 新增: src/AICA.Core/Agent/Specialists/
public class ResearchAgent : ChatCompletionAgent
{
    // 并行搜索代码库，收集信息
    // 工具: grep_search, find_by_name, list_code_definitions
    // 特点: 只读，无修改权限，小上下文窗口
}

public class CodeAgent : ChatCompletionAgent
{
    // 生成和修改代码
    // 工具: read_file, edit, write_to_file
    // 特点: 可写，需审批
}

public class ReviewAgent : ChatCompletionAgent
{
    // 审查代码变更质量
    // 工具: read_file, grep_search
    // 特点: 只读，输出结构化审查结果
}

public class BuildAgent : ChatCompletionAgent
{
    // 编译和运行测试
    // 工具: run_command
    // 特点: 沙箱执行，超时保护
}
```

**编排示例 — 复杂分析任务**:

```csharp
// TC-K01 "分析 Logger 系统架构" 用多 Agent 方案
var research = new ConcurrentOrchestration(
    new ResearchAgent("搜索所有 Channel 子类"),
    new ResearchAgent("搜索所有 Formatter 子类"),
    new ResearchAgent("分析 Logger 继承关系")
);

var results = await research.InvokeAsync(kernel, userRequest);
// 3 个 Agent 并行执行，合并结果

var review = new ReviewAgent("验证覆盖完整性");
var verification = await review.InvokeAsync(kernel, results);
// 审查 Agent 检查是否有遗漏

var completion = await CompileResults(results, verification);
```

**预期效果**: TC-K01 从 19 轮/50% 覆盖率提升到 ~6 轮/90%+ 覆盖率。

#### 3.3 共享上下文 (SharedContext)

多 Agent 之间需要共享发现，避免重复搜索：

```csharp
// 新增: src/AICA.Core/Agent/SharedContext.cs
public class SharedContext
{
    // 线程安全的共享发现池
    public ConcurrentDictionary<string, string> Discoveries { get; }
    public ConcurrentBag<string> FilesRead { get; }
    public ConcurrentBag<string> SymbolsFound { get; }

    // Agent 汇报发现
    public void ReportDiscovery(string agentName, string key, string value);

    // Agent 查询其他 Agent 的发现
    public string GetDiscovery(string key);
}
```

#### 3.4 交付物与验收标准

| 交付物 | 验收标准 |
|--------|----------|
| ArchitectEditorPipeline | Complex 任务由 Architect 规划，Editor 执行 |
| ResearchAgent / CodeAgent / ReviewAgent / BuildAgent | 4 个子 Agent 可独立运行 |
| ConcurrentOrchestration 集成 | 并行搜索耗时降低 50%+ |
| SharedContext | 子 Agent 间共享发现，无重复搜索 |
| TC-K01 重测 | Channel 覆盖率 ≥ 80% |

---

### Phase 3.5: 自验证管线 [紧随 Phase 3]

> **目标**: Agent 执行后自动验证输出正确性
> **参照**: ReVeal (arxiv), Reflection-Driven Control (arxiv)
> **工期估计**: 2-3 周

#### 3.5.1 验证管线架构

```
Agent 输出 → StaticVerifier → TestGenerator → TestRunner → ReflectionLoop
                  ↓                ↓               ↓              ↓
            Roslyn 分析      生成测试用例      沙箱执行     失败→重试(max 3)
```

**关键原则** (来自 ReVeal 论文): 生成代码的 Agent 和验证代码的 Agent 必须是**不同的 Agent/模型**，避免"自我祝贺机器"。

```csharp
// 新增: src/AICA.Core/Agent/Verification/
public class VerificationPipeline
{
    public async Task<VerificationResult> VerifyAsync(AgentOutput output)
    {
        // 1. 静态验证 — Roslyn 分析器
        var staticResult = await _roslynAnalyzer.AnalyzeAsync(output.ModifiedFiles);
        if (staticResult.HasErrors) return VerificationResult.Fail(staticResult);

        // 2. 测试生成 — 用不同的模型/Agent
        var tests = await _testGenerator.GenerateTestsAsync(output, _reviewModel);

        // 3. 测试执行 — 沙箱
        var testResult = await _testRunner.RunInSandboxAsync(tests);

        // 4. 反思循环 — 最多 3 次重试
        if (!testResult.AllPassed)
        {
            return await _reflectionLoop.RetryAsync(output, testResult, maxRetries: 3);
        }

        return VerificationResult.Pass();
    }
}
```

#### 3.5.2 结果完整性检查

针对 TC-K01 类分析任务的覆盖率验证：

```csharp
// 新增: src/AICA.Core/Agent/Verification/CompletenessChecker.cs
public class CompletenessChecker
{
    /// <summary>
    /// 检查搜索结果是否可能有遗漏
    /// 例: 搜到 10 个 Channel 子类，但头文件目录有 20+ 个 Channel 相关文件
    /// </summary>
    public async Task<CompletenessReport> CheckAsync(
        string query, List<SearchResult> results, ProjectKnowledgeStore knowledge)
    {
        // 交叉验证: 用不同工具/方法再搜一次
        var crossCheck = await _researchAgent.CrossVerify(query, results);

        return new CompletenessReport
        {
            OriginalCount = results.Count,
            CrossCheckCount = crossCheck.Count,
            PotentiallyMissed = crossCheck.Except(results).ToList(),
            Confidence = CalculateConfidence(results, crossCheck)
        };
    }
}
```

---

### Phase 4: 分层上下文管理 [关键基础设施]

> **目标**: 从 32K "一刀切" 到 "无限" 有效上下文
> **参照**: Windsurf Context Engine, Code-Specific RAG (Qodo), Roslyn-based chunking
> **工期估计**: 4-6 周

#### 4.1 三层上下文架构

```
┌─────────────────────────────────────────────┐
│ 热层 (Hot): 当前对话                          │
│ ~8K tokens                                   │
│ 最近 2-3 轮对话 + 最新工具结果                 │
│ 管理: AgentExecutor 直接管理                   │
└─────────────────────────────────────────────┘
        ↑ 按需提升 / 降级 ↓
┌─────────────────────────────────────────────┐
│ 温层 (Warm): 会话摘要                         │
│ ~4K tokens                                   │
│ 关键发现、已读文件列表、修改记录、决策历史       │
│ 管理: SmartContextSelector                    │
│ 格式: 结构化 Markdown (类似 Memory Bank)       │
└─────────────────────────────────────────────┘
        ↑ 按需检索 / 缓存 ↓
┌─────────────────────────────────────────────┐
│ 冷层 (Cold): 知识库                           │
│ 无限容量                                      │
│ 项目索引 + 向量嵌入 + 代码语义图                │
│ 管理: RAG 检索管线                             │
│ 存储: 本地向量数据库 + 文件索引                 │
└─────────────────────────────────────────────┘
```

#### 4.2 SmartContextSelector

```csharp
// 新增: src/AICA.Core/Context/SmartContextSelector.cs
public class SmartContextSelector
{
    /// <summary>
    /// 根据当前请求，从三层上下文中智能组装 prompt
    /// </summary>
    public ContextBundle SelectContext(string userRequest, int tokenBudget)
    {
        var bundle = new ContextBundle(tokenBudget);

        // 1. 热层: 始终包含 (最近 2-3 轮)
        bundle.AddHot(_conversationHistory.GetRecent(3));

        // 2. 温层: 包含会话摘要 (已读文件、关键发现)
        bundle.AddWarm(_sessionSummary.GetRelevant(userRequest));

        // 3. 冷层: 按需检索
        var retrievals = await _ragPipeline.RetrieveAsync(userRequest, bundle.RemainingBudget);
        bundle.AddCold(retrievals);

        return bundle;
    }
}
```

#### 4.3 代码感知 RAG 管线

**参照**: Qodo — https://www.qodo.ai/blog/rag-for-large-scale-code-repos/

当前 AICA 使用正则 TF-IDF，需要升级到语义向量检索：

```
代码文件 → Roslyn/正则 Chunker → 按 class/method 分块
            ↓
     ONNX 嵌入模型 (本地, 离线)
            ↓
     向量存储 (SK VolatileVectorStore 或 Qdrant)
            ↓
     查询时: 用户请求 → 嵌入 → 最近邻检索 → Top-K 代码块
```

**分块策略** (不同文件类型):

| 文件类型 | 分块单位 | 工具 |
|---------|---------|------|
| C# (.cs) | class / method / property | Roslyn SyntaxTree |
| C/C++ (.h/.cpp) | class / function | 正则 SymbolParser (已有) |
| JSON/XML | 键路径 / 节点 | 递归解析 |
| Markdown | 标题段落 | Heading 分割 |

**SK VectorStore 集成** (Phase 0 预留):

```csharp
// 升级: src/AICA.Core/Knowledge/VectorKnowledgeStore.cs
public class VectorKnowledgeStore
{
    private readonly IVectorStore _vectorStore;
    private readonly ITextEmbeddingGenerationService _embedder;

    public async Task IndexProjectAsync(string projectRoot)
    {
        var chunks = _codeChunker.ChunkProject(projectRoot);
        foreach (var chunk in chunks)
        {
            var embedding = await _embedder.GenerateEmbeddingAsync(chunk.Content);
            await _vectorStore.UpsertAsync(new CodeChunkRecord
            {
                Id = chunk.Id,
                FilePath = chunk.FilePath,
                SymbolName = chunk.SymbolName,
                Content = chunk.Content,
                Embedding = embedding
            });
        }
    }

    public async Task<List<CodeChunk>> RetrieveAsync(string query, int topK = 10)
    {
        var queryEmbedding = await _embedder.GenerateEmbeddingAsync(query);
        return await _vectorStore.SearchAsync(queryEmbedding, topK);
    }
}
```

**嵌入模型选择** (离线/隐私优先):

| 模型 | 大小 | 维度 | 特点 |
|------|------|------|------|
| all-MiniLM-L6-v2 (ONNX) | 80MB | 384 | 通用，快速，ONNX 兼容 |
| CodeBERT (ONNX) | 500MB | 768 | 代码专用，语义更准 |
| Nomic Embed (ONNX) | 274MB | 768 | 长文本，8K 上下文 |

#### 4.4 改进 Condense 摘要质量

**TC-G02 问题**: condense 后 LLM 忘记之前读了哪些文件。

```csharp
// 改进: BuildAutoCondenseSummary
public string BuildAutoCondenseSummary(List<ChatMessage> history)
{
    var summary = new StringBuilder();

    // 1. 已读文件清单 (带摘要)
    summary.AppendLine("## Files Read:");
    foreach (var file in ExtractFilesRead(history))
        summary.AppendLine($"- {file.Path} ({file.Lines} lines): {file.Summary}");

    // 2. 搜索结果清单
    summary.AppendLine("## Searches Performed:");
    foreach (var search in ExtractSearches(history))
        summary.AppendLine($"- grep '{search.Query}': {search.Matches} matches in {search.Files} files");

    // 3. 关键发现 (从 LLM 回答中提取)
    summary.AppendLine("## Key Findings:");
    foreach (var finding in ExtractKeyFindings(history))
        summary.AppendLine($"- {finding}");

    // 4. 当前任务状态
    summary.AppendLine("## Current Task Status:");
    summary.AppendLine($"- User request: {ExtractLatestUserRequest(history)}");
    summary.AppendLine($"- Progress: {_taskState.CompletedSteps}/{_taskState.TotalSteps} steps");

    return summary.ToString();
}
```

---

### Phase 5: 跨会话记忆 (Memory Bank)

> **目标**: 新会话自动知道项目上下文和用户偏好
> **参照**: Cline Memory Bank — https://docs.cline.bot/prompting/cline-memory-bank
> **工期估计**: 2-3 周

#### 5.1 Memory Bank 架构

**参照**: Cline 的 Memory Bank 是最成熟的开源方案。

```
/.aica/
├── memory/
│   ├── project-brief.md       ← 项目概述 (目标、技术栈、架构)
│   ├── active-context.md      ← 当前工作焦点 (正在做什么、下一步)
│   ├── system-patterns.md     ← 架构决策、代码模式、命名约定
│   ├── user-preferences.md    ← 用户偏好 (语言、风格、常用操作)
│   └── corrections.md         ← 用户纠正记录 (上次做错了什么)
├── decisions/
│   └── YYYY-MM-DD-topic.md    ← 架构决策日志
└── sessions/
    └── latest-session.json    ← 上次会话状态 (用于恢复)
```

#### 5.2 自动记忆管理

```csharp
// 新增: src/AICA.Core/Memory/MemoryBank.cs
public class MemoryBank
{
    private readonly string _memoryDir; // /.aica/memory/

    /// <summary>
    /// 会话开始时加载所有记忆文件到上下文
    /// </summary>
    public string LoadAllMemories()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[PROJECT MEMORY — from previous sessions]");

        foreach (var file in Directory.GetFiles(_memoryDir, "*.md"))
        {
            var content = File.ReadAllText(file);
            sb.AppendLine($"\n### {Path.GetFileNameWithoutExtension(file)}");
            sb.AppendLine(content);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 会话结束时自动更新 active-context.md
    /// </summary>
    public async Task UpdateActiveContextAsync(SessionSummary summary)
    {
        var content = $@"# Active Context
Updated: {DateTime.Now:yyyy-MM-dd HH:mm}

## Current Focus
{summary.CurrentTask}

## Recent Changes
{string.Join("\n", summary.FilesModified.Select(f => $"- {f}"))}

## Key Findings
{string.Join("\n", summary.KeyFindings.Select(f => $"- {f}"))}

## Next Steps
{string.Join("\n", summary.NextSteps.Select(s => $"- {s}"))}
";
        await File.WriteAllTextAsync(
            Path.Combine(_memoryDir, "active-context.md"), content);
    }

    /// <summary>
    /// 用户纠正时记录到 corrections.md
    /// </summary>
    public async Task RecordCorrectionAsync(string correction, string context)
    {
        var entry = $"\n## {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                    $"**Context**: {context}\n" +
                    $"**Correction**: {correction}\n";

        await File.AppendAllTextAsync(
            Path.Combine(_memoryDir, "corrections.md"), entry);
    }
}
```

#### 5.3 记忆注入到 System Prompt

```csharp
// 改进: SystemPromptBuilder
public SystemPromptBuilder AddMemoryContext(MemoryBank memory)
{
    var memoryContent = memory.LoadAllMemories();
    if (!string.IsNullOrWhiteSpace(memoryContent))
    {
        // 记忆作为 High 优先级 (仅次于工具定义)
        _sections.Add(new PromptSection("Memory", memoryContent, ContextPriority.High, order: 2));
    }
    return this;
}
```

---

### Phase 6: 高级 Agent 行为

> **目标**: 从被动响应到主动助手
> **参照**: Devin (主动建议), OpenHands (事件流), LangGraph (图工作流)
> **工期估计**: 4-6 周

#### 6.1 事件驱动架构

**参照**: OpenHands — https://github.com/OpenHands/OpenHands

将所有 Agent 交互建模为 typed events：

```csharp
// 新增: src/AICA.Core/Events/
public abstract class AgentEvent
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Source { get; set; }
}

public class UserMessageEvent : AgentEvent { public string Message { get; set; } }
public class ToolCallEvent : AgentEvent { public ToolCall Call { get; set; } }
public class ToolResultEvent : AgentEvent { public ToolResult Result { get; set; } }
public class PlanUpdateEvent : AgentEvent { public TaskPlan Plan { get; set; } }
public class FileChangeEvent : AgentEvent { public string Path { get; set; } }
public class BuildResultEvent : AgentEvent { public bool Success { get; set; } }

// 事件中心
public class EventHub
{
    public event Action<AgentEvent> OnEvent;
    public void Publish(AgentEvent evt) => OnEvent?.Invoke(evt);
    public IObservable<T> OfType<T>() where T : AgentEvent;
}
```

#### 6.2 主动建议系统

```csharp
// 新增: src/AICA.Core/Agent/ProactiveAdvisor.cs
public class ProactiveAdvisor
{
    private readonly EventHub _eventHub;

    public ProactiveAdvisor(EventHub eventHub)
    {
        // 监听文件保存事件
        _eventHub.OfType<FileChangeEvent>().Subscribe(OnFileChanged);

        // 监听编译事件
        _eventHub.OfType<BuildResultEvent>().Subscribe(OnBuildResult);
    }

    private async void OnFileChanged(FileChangeEvent evt)
    {
        // 检查是否引入了明显问题
        var analysis = await _quickAnalyzer.AnalyzeAsync(evt.Path);
        if (analysis.HasIssues)
        {
            _eventHub.Publish(new SuggestionEvent
            {
                Message = $"⚠️ {evt.Path} 中发现 {analysis.Issues.Count} 个问题",
                Severity = analysis.MaxSeverity
            });
        }
    }

    private async void OnBuildResult(BuildResultEvent evt)
    {
        if (!evt.Success)
        {
            // 自动分析编译错误并建议修复
            var suggestion = await _buildAnalyzer.SuggestFixAsync(evt.Errors);
            _eventHub.Publish(new SuggestionEvent
            {
                Message = $"编译失败，建议修复: {suggestion}",
                AutoFixAvailable = true
            });
        }
    }
}
```

#### 6.3 图结构工作流

**参照**: LangGraph — 工作流建模为有向图

```csharp
// 新增: src/AICA.Core/Workflow/WorkflowGraph.cs
public class WorkflowGraph
{
    private readonly Dictionary<string, IWorkflowNode> _nodes;
    private readonly List<(string from, string to, Func<WorkflowState, bool> condition)> _edges;

    // 构建工作流图
    public WorkflowGraph AddNode(string name, IWorkflowNode node);
    public WorkflowGraph AddEdge(string from, string to, Func<WorkflowState, bool> condition = null);

    // 执行工作流
    public async Task<WorkflowResult> ExecuteAsync(WorkflowState initialState);
}

// 示例: 代码修改工作流
var workflow = new WorkflowGraph()
    .AddNode("plan", new PlannerNode())
    .AddNode("implement", new ImplementNode())
    .AddNode("test", new TestNode())
    .AddNode("review", new ReviewNode())
    .AddNode("fix", new FixNode())
    .AddEdge("plan", "implement")
    .AddEdge("implement", "test")
    .AddEdge("test", "review", state => state.TestsPassed)
    .AddEdge("test", "fix", state => !state.TestsPassed)   // 循环: 测试失败→修复
    .AddEdge("fix", "test")                                  // 修复后重新测试
    .AddEdge("review", "complete", state => state.ReviewPassed);
```

---

## 四、实施路线图

```
2026 Q2                    2026 Q3                    2026 Q4
├── Phase 3 (6w)           ├── Phase 4 (6w)           ├── Phase 6 (6w)
│   ├── Architect/Editor   │   ├── 三层上下文          │   ├── 事件驱动架构
│   ├── SK 多 Agent 编排   │   ├── 代码 RAG 管线      │   ├── 主动建议
│   └── SharedContext      │   ├── 向量嵌入索引        │   └── 图工作流
│                          │   └── Condense 质量改进   │
├── Phase 3.5 (3w)         │                          │
│   ├── 自验证管线         ├── Phase 5 (3w)           │
│   ├── CriticAgent        │   ├── Memory Bank        │
│   └── 完整性检查         │   ├── 自动记忆更新       │
│                          │   └── 记忆注入 prompt     │
```

## 五、技术栈演进

| 组件 | 当前 | 目标 | 技术选择 |
|------|------|------|----------|
| AI 框架 | SK 1.54.0 | SK 1.71+ | 升级 SK, 启用 Agent Orchestration |
| 多 Agent | 无 | SK Agent Framework | Sequential/Concurrent/Handoff |
| 向量索引 | TF-IDF | SK VectorStore + ONNX | all-MiniLM-L6-v2 或 CodeBERT |
| 代码分块 | 无 | Roslyn SyntaxTree + 正则 | C# 用 Roslyn, C/C++ 用 SymbolParser |
| 记忆 | 无 | Memory Bank (Cline 模式) | Markdown 文件 + JSON |
| 事件系统 | 无 | EventHub (OpenHands 模式) | Rx.NET Observable |
| 工作流 | 无 | WorkflowGraph (LangGraph 模式) | 自建有向图 |
| MCP | 无 | SK MCP 集成 | SK v1.71 原生支持 |

## 六、风险与缓解

| 风险 | 级别 | 缓解措施 |
|------|------|----------|
| SK 升级兼容性 (1.54→1.71) | 高 | 渐进升级，逐版本验证 |
| 多 Agent token 成本 | 高 | 子 Agent 使用小上下文 + 弱模型; Architect 仅用于 Complex |
| ONNX 嵌入在 VS 宿主中的性能 | 中 | 后台异步索引，不阻塞 UI |
| 多 Agent 调试困难 | 中 | EventHub 全量日志 + 可视化工具 |
| Memory Bank 隐私 | 低 | 本地存储，不上传; .gitignore 敏感文件 |
| .NET Standard 2.0 限制 (Rx.NET) | 低 | System.Reactive 支持 netstandard2.0 |

## 七、成功标准

| Phase | 验收标准 | 量化指标 |
|-------|----------|----------|
| 3 | 并行分析 + Architect/Editor | TC-K01 覆盖率 ≥ 80%, 迭代次数 ≤ 8 |
| 3.5 | 自验证管线 | 代码修改后自动验证，错误检出率 ≥ 70% |
| 4 | 分层上下文 + 向量 RAG | TC-G02 condense 后仍能回忆第 1 轮; 50 轮对话稳定 |
| 5 | Memory Bank | 新会话自动知道 "POCO 用 CppUnit"; 用户纠正被记住 |
| 6 | 事件驱动 + 主动建议 | 编译失败后 3 秒内自动建议修复 |

---

## 八、参考资源

### 核心参照项目
- OpenHands: https://github.com/OpenHands/OpenHands (~69K stars)
- Cline: https://github.com/cline/cline (~58K stars)
- Aider: https://github.com/Aider-AI/aider (~42K stars)
- Semantic Kernel: https://github.com/microsoft/semantic-kernel (~27K stars)
- Continue: https://github.com/continuedev/continue (~32K stars)

### 关键文档
- SK Agent Orchestration: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-orchestration/
- SK Multi-Agent Blog: https://devblogs.microsoft.com/semantic-kernel/semantic-kernel-multi-agent-orchestration/
- Cline Memory Bank: https://docs.cline.bot/prompting/cline-memory-bank
- Aider Architect/Editor: https://aider.chat/2024/09/26/architect.html

### 重构参照
- OpenHands Software Agent SDK: https://github.com/OpenHands/software-agent-sdk
- Cline Architecture Doc: https://github.com/cline/prompts/blob/main/.clinerules/cline-architecture.md
- Aider Coder 基类: https://github.com/Aider-AI/aider/blob/main/aider/coders/base_coder.py
- piratos/llmfilters (YAML 过滤管线): https://github.com/piratos/llmfilters
- E2B 云沙箱 (.NET SDK): https://github.com/e2b-dev/E2B
- DotNetIsolator (WASM 隔离): https://github.com/SteveSandersonMS/DotNetIsolator
- microsandbox (microVM): https://github.com/zerocore-ai/microsandbox
- SWE-bench (Agent 评估基准): https://github.com/SWE-bench/SWE-bench
- EleutherAI Eval Harness: https://github.com/EleutherAI/lm-evaluation-harness

### 论文
- ReVeal: Self-Evolving Code Agents (arxiv 2506.11442)
- Reflection-Driven Control for Code Agents (arxiv 2512.21354)
- Context Engineering for Multi-Agent LLM Code Assistants (arxiv 2508.08322)
- RAG for Large-Scale Code Repos (Qodo): https://www.qodo.ai/blog/rag-for-large-scale-code-repos/
- AutoTool: Dynamic Tool Selection (arxiv 2512.13278)
- MemTool: Short-term Memory for Tools (arxiv 2507.21428)
- ACON: Context Compression Optimization (arxiv 2510.00615)
- Anthropic Compaction API: https://platform.claude.com/docs/en/build-with-claude/compaction
- JetBrains Context Management: https://blog.jetbrains.com/research/2025/12/efficient-context-management/
- Factory.ai Evaluating Compression: https://factory.ai/news/evaluating-compression
