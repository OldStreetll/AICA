# AICA Agent 演进计划 — 从工具调用者到真正的 Agent

> 版本: 2.0
> 日期: 2026-03-18
> 状态: **Phase 0-1 已完成**, Phase 2 待启动
> 基于: POCO A 类测试发现 + AICA 源码深度分析 + SK 架构集成
> 目标: 将 AICA 从 "单轮工具调用者" 演进为 "自主任务完成者"

---

## 一、现状分析

### 当前架构 (Phase 1 完成后)

```
用户请求 → AgentExecutor (单循环) → LLM → 工具调用 → 返回结果
                ↓                     ↑
        SystemPromptBuilder     KnowledgeContextProvider
        (工具定义 + 规则)       (TF-IDF 检索 Top-10 符号)
                                      ↑
                              ProjectKnowledgeStore
                              (3237 files, 28631 symbols)
                                      ↑
                              ProjectIndexer + SymbolParser
                              (解决方案打开时后台索引)
```

### 关键文件与规模

| 组件 | 文件 | 行数 | 职责 |
|------|------|------|------|
| AgentExecutor | Agent/AgentExecutor.cs | ~1860 | 主循环, condense, 工具调度, **知识注入** |
| SystemPromptBuilder | Prompt/SystemPromptBuilder.cs | ~660 | 系统 Prompt 构建, **AddKnowledgeContext** |
| ToolDispatcher | Agent/ToolDispatcher.cs | 133 | 工具路由 |
| ToolRegistry | Agent/ToolRegistry.cs | 173 | 工具注册 |
| OpenAIClient | LLM/OpenAIClient.cs | 80+ | LLM HTTP 通信 |
| 14 个工具 | Tools/*.cs | 3644 | 文件/搜索/命令/交互 |
| **KernelFactory** | SK/KernelFactory.cs | ~120 | **SK Kernel 构建 (Phase 0)** |
| **LLMClientAdapter** | SK/Adapters/*.cs | ~400 | **SK 适配器 (Phase 0)** |
| **SymbolParser** | Knowledge/SymbolParser.cs | ~360 | **C/C++/C# 符号提取 (Phase 1)** |
| **ProjectIndexer** | Knowledge/ProjectIndexer.cs | ~230 | **目录扫描 + 项目根探测 (Phase 1)** |
| **KnowledgeContextProvider** | Knowledge/KnowledgeContextProvider.cs | ~200 | **TF-IDF 检索 + prompt 格式化 (Phase 1)** |
| **ProjectKnowledgeStore** | Knowledge/ProjectKnowledgeStore.cs | ~55 | **单例索引存储 (Phase 1)** |
| VSAgentContext | VSIX/Agent/VSAgentContext.cs | — | VS 工作区上下文 |
| SolutionEventListener | VSIX/Events/SolutionEventListener.cs | ~210 | **解决方案事件 + 自动索引 (Phase 1)** |

### POCO 测试暴露的核心局限

| 局限 | 测试证据 | 根因 | Phase 0-1 改善 |
|------|----------|------|----------------|
| 上下文窗口小 (32K) | TC-A14: condense 后信息丢失 | maxTokenBudget = 32000 | ❌ 未解决 |
| 单 Agent 串行 | 复杂任务需多轮才能完成 | AgentExecutor 单循环 | ❌ 未解决 |
| function calling 不可靠 | TC-A12/A07: tool_calls 为空 | LLM 模型兼容性 | ❌ 未解决 |
| 无项目记忆 | 每次会话重新理解项目 | 无持久化知识库 | ✅ **Phase 1 知识索引** |
| LLM 计数弱 | 5/14 用例数字矛盾 | LLM 固有弱点 | ❌ 未解决 |
| 被动响应 | 用户问一步做一步 | 无任务分解能力 | ❌ 未解决 |

---

## 二、目标架构

```
用户请求 → TaskPlanner (任务分解)
              ↓
        AgentOrchestrator (编排)
        ├── MainAgent (对话 + 决策)
        ├── ResearchAgent (并行搜索/读文件)
        ├── CodeAgent (代码生成/修改)
        └── ReviewAgent (审查/验证)
              ↓
        ProjectKnowledgeBase (RAG)
        ├── 文件索引 (结构/符号)       ← Phase 1 ✅ 已完成
        ├── 依赖图 (模块/类/继承)
        └── 会话记忆 (跨会话持久化)
              ↓
        ToolCallFallback (可靠执行)
        ├── function calling (优先)
        ├── 文本解析 fallback
        └── 工具侧计数注入
```

---

## 三、演进路线 (7 个 Phase, 渐进式)

---

### Phase 0: SK 框架集成 ✅ 已完成 [2026-03-18]

> 引入 Semantic Kernel 作为底层框架，为后续多 Agent、向量索引等能力打基础

#### 完成内容

| 组件 | 文件 | 说明 |
|------|------|------|
| KernelFactory | SK/KernelFactory.cs | 构建 SK Kernel，注册 LLM 服务和工具插件 |
| LLMClientChatCompletionService | SK/Adapters/LLMClientChatCompletionService.cs | 将现有 OpenAIClient 适配为 SK IChatCompletionService |
| ChatMessageConverter | SK/Adapters/ChatMessageConverter.cs | AICA ChatMessage ↔ SK ChatMessageContent 双向转换 |
| AgentToolPluginAdapter | SK/Adapters/AgentToolPluginAdapter.cs | 将 14 个 IAgentTool 包装为 SK KernelFunction 插件 |

#### 技术决策

- **SK 版本固定 1.54.0**: 最后一个与 System.Text.Json 8.x 兼容的版本。SK ≥ 1.55 引入 STJ 10.x，在 VS2022 .NET Framework 4.8 宿主中导致 MissingMethodException
- **不使用 SK InMemoryVectorStore**: preview 包的 init-only 属性在 netstandard2.0 下有编译问题。Phase 1 采用自定义 TF-IDF，Phase 4 再引入 SK VectorStore

#### 验证结果

- 45 个 SK 单元测试全部通过
- VSIX 回归测试通过

---

### Phase 1: 项目知识库 ✅ 已完成 [2026-03-18]

> 让 AICA "记住" 项目结构，不再每次从零开始理解

#### 完成内容

| 组件 | 文件 | 说明 |
|------|------|------|
| SymbolParser | Knowledge/SymbolParser.cs | 正则提取 C/C++ (class/struct/enum/function/typedef/#define) 和 C# (class/struct/enum/interface) 符号 |
| ProjectIndexer | Knowledge/ProjectIndexer.cs | 递归扫描目录，跳过 build/.git/bin 等。FindProjectRoot 自动从 sln 子目录向上定位 .git 所在的项目根 |
| KnowledgeContextProvider | Knowledge/KnowledgeContextProvider.cs | TF-IDF 关键词匹配：query 分词 → 驼峰/下划线拆分 → IDF 加权评分 → Top-10 检索 → 格式化注入 prompt (≤2000 tokens) |
| ProjectKnowledgeStore | Knowledge/ProjectKnowledgeStore.cs | 线程安全单例（volatile），供 AgentExecutor 和 SystemPromptBuilder 访问 |

#### 集成点

1. **SolutionEventListener.OnAfterOpenSolution** → 后台 `Task.Run` 调用 `ProjectIndexer.IndexDirectoryAsync` → `ProjectKnowledgeStore.SetIndex`
2. **SolutionEventListener.OnAfterCloseSolution** → `ProjectKnowledgeStore.Clear`
3. **AgentExecutor.ExecuteAsync** → `provider.RetrieveContext(userRequest)` → `builder.AddKnowledgeContext`
4. **SystemPromptBuilder.BuildSections** → 知识作为 Normal 优先级 section（token 紧张时可裁剪）

#### 调试过程中发现并修复的 Bug

| Bug | 原因 | 修复 |
|-----|------|------|
| 索引 0 files, 0 symbols | sln 在 `poco/build/`，`build` 在跳过列表中 | `FindProjectRoot` 向上查找 `.git` 目录 |
| 知识未注入 prompt | AgentExecutor 用 `Build()` 不经过 `BuildSections()` | 在 `Build()` 前显式调用 `AddKnowledgeContext` |
| AI 仍调用 read_file | 缺少引导提示 | 知识上下文添加 "Use this information DIRECTLY without calling read_file" |
| SplitIdentifier 不拆 HTTPRequest | 缺少大写缩写→单词转换 | 增加 prevIsUpper && nextIsLower 检测 |

#### 验证结果 (POCO 项目实测)

| 指标 | 结果 |
|------|------|
| 索引文件数 | 3237 |
| 提取符号数 | 28631 |
| 索引耗时 | 9.3 秒 (< 30 秒目标) |
| "Logger 是什么" 回答 | ✅ 直接使用索引知识回答，包含文件路径、继承关系、方法数 |
| read_file 调用 | ✅ 未调用（知识足够回答基本问题） |
| 空项目 | ✅ 0 files, 0 symbols，无崩溃 |
| 关闭/重新打开 | ✅ 清除 → 重新索引 |
| 单元测试 | 45/45 Knowledge 测试通过 |
| 回归测试 | 287/289 全量通过（2 个预有失败） |

#### 已知限制

1. **工具调用问题**: MiniMax-M2.5 模型的 function calling 不稳定，AI 有时在文本中描述工具调用而非实际调用。这是 Phase 0 遗留问题，非 Phase 1 范围
2. **符号数量大**: 28631 个符号，但 RetrieveContext 只注入 Top-10，IDF 计算延迟可接受
3. **无增量更新**: 文件修改后不会自动重新索引，需关闭再打开解决方案
4. **无持久化**: 索引在内存中，每次打开都重新扫描（9.3s 可接受）

---

### Phase 1.5: 工具调用可靠性修复 [计划中]

> 解决 function calling 失败问题，这是影响所有后续 Phase 的基础

#### 1.5.1 Tool Call Fallback — 解决 function calling 失败

**问题**: TC-A12/A07 中 LLM 返回无 tool_calls 字段, 工具未执行

**方案**: 当检测到工具意图但无 tool_calls 时, 从文本中解析工具调用

#### 1.5.2 工具侧计数注入

**方案**: 搜索/列表工具在返回结果末尾自动附加精确统计

#### 1.5.3 Agent Loop 完善

**问题**: `Conversational message on iteration 1, yielding text and completing (toolCalls=1)` — 即使收到 tool_calls，也直接结束而不执行

**方案**: 确保 Agent Loop 在收到 tool_calls 时执行工具并继续循环

---

### Phase 2: 任务规划系统 [计划中]

> 让 AICA 能自主分解和执行多步任务

#### 2.1 TaskPlanner — 分析用户请求复杂度，决定是否需要分解

#### 2.2 执行计划 UI — 聊天窗口中显示可折叠的执行计划

#### 2.3 自主重试与策略调整 — 工具失败时分析原因并切换策略

---

### Phase 3: 多 Agent 协作 [计划中]

> 突破单 Agent 串行瓶颈

- AgentOrchestrator (编排)
- ResearchAgent / CodeAgent / ReviewAgent / BuildAgent
- SharedContext (并行发现共享)

---

### Phase 4: 上下文窗口突破 [计划中]

> 从 32K 限制到 "无限" 有效上下文

- 热/温/冷 三层上下文架构
- SmartContextSelector
- 可选: SK VectorStore + ONNX 嵌入

---

### Phase 5: 跨会话记忆 [计划中]

> 让 AICA 记住用户偏好和项目理解

- 记忆类型: UserPreference, ProjectFact, Correction, WorkHistory
- 存储: `.aica/memory.json`

---

### Phase 6: 高级 Agent 行为 [计划中]

> 让 AICA 表现得像真正的 AI 编程助手

- 主动建议 / 使用模式学习 / 响应自动验证

---

## 四、技术栈

| 组件 | 技术 | 状态 |
|------|------|------|
| AI 框架 | **Semantic Kernel 1.54.0** | ✅ Phase 0 集成 |
| 项目索引 | **正则 SymbolParser + TF-IDF** | ✅ Phase 1 完成 |
| 符号提取 | 正则 (覆盖 80%+ 场景) | ✅ Phase 1 完成 |
| 向量索引 | SK VectorStore + ONNX (可选) | 📋 Phase 4 计划 |
| 并行执行 | Task.WhenAll + CancellationToken | 📋 Phase 3 计划 |
| 记忆存储 | JSON 文件 (.aica/ 目录) | 📋 Phase 5 计划 |
| 工具 Fallback | ToolCallTextParser | 📋 Phase 1.5 计划 |

---

## 五、实施路线图

```
Phase 0 ✅              Phase 1 ✅              Phase 1.5
┌────────────────┐     ┌──────────────────┐    ┌──────────────────┐
│ SK 1.54.0 集成 │     │ ProjectIndexer   │    │ Tool Fallback    │
│ 4 个适配器     │────→│ KnowledgeContext │───→│ 计数注入         │
│ 45 测试通过    │     │ 28631 symbols    │    │ Agent Loop 修复  │
└────────────────┘     └──────────────────┘    └──────────────────┘
                                                       │
Phase 5                  Phase 4                Phase 3 ← Phase 2
┌────────────────┐     ┌──────────────────┐    ┌──────────────────┐
│ 跨会话记忆     │←────│ 分层上下文       │←───│ AgentOrchestrator│
│ 记忆注入       │     │ SK VectorStore   │    │ 子 Agent 类型    │
│ 纠正学习       │     │ 向量索引 (可选)  │    │ 共享上下文       │
└────────────────┘     └──────────────────┘    └──────────────────┘
                                │
                       Phase 6
                       ┌──────────────────┐
                       │ 主动建议         │
                       │ 使用模式学习     │
                       │ 响应自动验证     │
                       └──────────────────┘
```

---

## 六、每 Phase 交付物与验收标准

| Phase | 交付物 | 验收标准 | 状态 |
|-------|--------|----------|------|
| **0** | SK 集成, 4 个适配器 | 45 测试通过, VSIX 回归通过 | ✅ 完成 |
| **1** | ProjectIndexer, KnowledgeContextProvider, ProjectKnowledgeStore | POCO 索引 <30s; "Logger 是什么" 直接回答 | ✅ 完成 |
| **1.5** | ToolCallTextParser, 计数注入, Agent Loop 修复 | TC-A12 PASS; tool_calls 正确执行 | 📋 计划中 |
| **2** | TaskPlanner, 执行计划 UI, RecoveryStrategies | "分析日志系统架构" 自动拆解为 3+ 步 | 📋 计划中 |
| **3** | AgentOrchestrator, ResearchAgent, SharedContext | 并行执行耗时降低 50%+ | 📋 计划中 |
| **4** | SmartContextSelector, 分层上下文 | 50 轮对话后仍能回忆第 1 轮发现 | 📋 计划中 |
| **5** | MemoryStore, 记忆注入 | 新会话自动知道 "POCO 用 CppUnit" | 📋 计划中 |
| **6** | ProactiveAdvisor, ResponseValidator | 数字一致性自动校验 | 📋 计划中 |

---

## 七、风险与缓解

| 风险 | 级别 | 缓解措施 | 当前状态 |
|------|------|----------|----------|
| SK 版本兼容性 (STJ 10.x) | 高 | 固定 SK 1.54.0 | ✅ 已解决 |
| 项目索引耗时 (大项目) | 中 | 后台异步, FindProjectRoot | ✅ 9.3s |
| LLM function calling 不可靠 | 高 | Phase 1.5 Text Fallback | ⚠️ 待解决 |
| 多 Agent 并行的 token 成本 | 高 | 子 Agent 使用小上下文 | 📋 Phase 3 |
| .NET Standard 2.0 限制 | 中 | 避免 .NET 6+ API | ✅ 已验证 |
| VS 扩展性能 | 中 | 索引异步执行, UI 不阻塞 | ✅ 已验证 |
