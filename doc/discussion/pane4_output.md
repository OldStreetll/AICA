# Pane 4 输出：McpResourceResolver 与 McpServerDescriptor 评估

> **角色**: 开发团队成员（Pane 4）
> **日期**: 2026-04-07
> **状态**: 评估完成

---

## 1. McpResourceResolver 评估

### 1.1 现状分析

**GitNexus MCP 资源使用情况：**

| 资源 URI | 用途 | 当前状态 |
|----------|------|----------|
| `gitnexus://setup` | AGENTS.md 行为规则（~1800 tokens） | **使用中** — `AgentExecutor.cs:1033` 全量 dump 进 prompt |
| `gitnexus://repo/{name}/context` | 仓库特定上下文 | **使用中** — `AgentExecutor.cs:1052` 全量 dump 进 prompt |
| `gitnexus://repo/{name}/clusters` | 代码聚类分析 | **未使用** |
| `gitnexus://repo/{name}/processes` | 流程/工作流信息 | **未使用** |
| `gitnexus://repo/{name}/schema` | 数据结构/模式 | **未使用** |
| 其他（约3个） | 文档描述中提到"6个额外资源" | **未使用** |

**McpClient 已有 `ListResourcesAsync()` 和 `ReadResourceAsync()` 能力**（`McpClient.cs:186-236`），技术上读取任何 MCP 资源没有障碍。

### 1.2 未使用资源的价值评估

| 资源 | 潜在场景 | 价值判断 |
|------|---------|---------|
| clusters | 分析代码架构、理解模块边界时有用 | **低频** — 仅 analyze intent + complex 任务需要 |
| processes | 理解工作流、CI/CD 管线 | **极低频** — C/C++ 项目很少有复杂流程定义 |
| schema | 理解数据结构 | **中频** — 但 `gitnexus_context` 工具已能按需查询 |

**关键发现**：这些资源的信息大多可以通过已注册的 MCP **工具**（如 `gitnexus_query`、`gitnexus_context`）在 Agent 循环中按需获取。资源注入（prompt 层）vs 工具调用（Agent 循环层）的本质区别在于：
- **资源注入**：LLM 直接看到，零工具调用开销，但占用 prompt token
- **工具调用**：按需获取，不占 prompt token，但需要一次工具调用轮次

对于低频场景，工具调用已经够用，专门做资源注入的 McpResourceResolver 价值有限。

### 1.3 按需加载逻辑（如果要做）

利用已有的 `DynamicToolSelector.ClassifyIntent()` 和 `TaskComplexity`：

```
intent=analyze + complexity=Complex → 加载 clusters
intent=bug_fix + complexity=Complex → 加载 clusters + processes
intent=read/modify/general → 不加载额外资源
```

**实现复杂度**：~100 行，在 `InjectMcpResources` 中增加条件分支。

### 1.4 设计建议

**结论：不建议现在实现 McpResourceResolver。**

**理由：**
1. 未使用的 MCP 资源价值有限，低频场景通过工具调用已可覆盖
2. 只有一个 MCP 服务器（GitNexus），资源集合是固定的、已知的，不需要"通用解析器"
3. 真正的 token 节省来自 McpRuleAdapter（将 `gitnexus://setup` 的 ~1800 tokens 条件化降到 ~540），而非加载更多资源
4. 100 行代码 + 测试 + 维护成本 > 偶尔省一次工具调用的收益

**如果未来需要**：在 McpRuleAdapter 完成后，可以直接在 `InjectMcpResources` 中用 3-5 行 if/else 硬编码按需加载逻辑，不需要独立的 Resolver 类。

---

## 2. McpServerDescriptor 评估

### 2.1 当前状态

- AICA 只有 **1 个 MCP 服务器**：GitNexus
- GitNexus 是 AICA 自带的配套组件，不是用户可选的第三方服务
- 工具注册通过 `McpBridgeTool.CreateAllTools()` 已实现自动化（从 `tools/list` 动态创建）
- 资源注入通过 `InjectMcpResources()` 硬编码两个 URI

### 2.2 过度设计分析

McpServerDescriptor 的设想是：

```csharp
// 假设的通用模式
var descriptor = new McpServerDescriptor("gitnexus", ...);
descriptor.RuleExtractionStrategy = ...;  // 如何从该服务器的资源中提取规则
descriptor.ResourceLoadingPolicy = ...;   // 何时加载哪些资源
```

**对比当前实际需求**：

| 通用框架要解决的问题 | 当前实际 |
|---------------------|---------|
| 多服务器注册和管理 | 只有 1 个，`GitNexusProcessManager` 单例管理 |
| 不同服务器的规则提取策略 | 只有 AGENTS.md 一种格式 |
| 服务器发现和自动连接 | 不需要，GitNexus 是硬编码启动的 |
| 标准化资源→Rule 转换 | 只有一种转换需求 |

### 2.3 YAGNI 评估

**第二个 MCP 服务器的可能性分析：**

| 候选 | 可能性 | 时间线 |
|------|--------|--------|
| 数据库 MCP（如 sqlite-mcp） | 低 — AICA 面向 C/C++ 开发，非数据密集型 | 遥远 |
| 文档/知识库 MCP | 低 — 涉密离线环境限制了第三方服务 | 遥远 |
| 编译器/构建系统 MCP | 中 — 有价值但需要自研 | v3.0+ |
| 自研第二个 MCP | 低 — 单人开发，精力有限 | 不确定 |

**结论**：在可预见的 v2.1 周期（24-26 周）内，第二个 MCP 服务器出现的概率极低。

### 2.4 替代方案

**推荐：在 McpRuleAdapter 中直接硬编码 GitNexus 逻辑。**

```csharp
// McpRuleAdapter.cs — 直接针对 GitNexus
public class McpRuleAdapter
{
    // 解析 gitnexus://setup 内容为 Rule 对象
    public List<Rule> ParseAgentsMd(string agentsMdContent) { ... }
    
    // 按 intent 过滤规则
    public List<Rule> GetRulesForIntent(string intent) { ... }
}
```

**不需要的抽象层**：
- 不需要 `IMcpServerDescriptor` 接口
- 不需要 `McpServerRegistry` 注册表
- 不需要 `IRuleExtractionStrategy` 策略模式
- 不需要配置文件定义服务器元数据

**如果未来有第二个 MCP 服务器**（比如 v3.0）：
- 那时再从 McpRuleAdapter 中提取通用接口，成本约 1 天
- 有两个具体实例后做抽象，抽象质量远高于现在基于一个实例的猜测
- 届时的需求可能和现在想象的完全不同

---

## 3. 综合建议

### 3.1 投入产出比总结

| 组件 | 代码量 | Token 节省 | 架构价值 | 建议 |
|------|--------|-----------|---------|------|
| **McpRuleAdapter** | ~150 行 | ~1400 tokens/请求（-72%） | 高 — 统一 prompt 注入机制 | **值得做** |
| **McpResourceResolver** | ~100 行 | 边际（低频场景） | 低 — 解决不存在的问题 | **不做** |
| **McpServerDescriptor** | ~80 行 | 无 | 低 — 单服务器过度抽象 | **不做** |

### 3.2 v2.1 计划建议

1. **McpRuleAdapter 应纳入 v2.1**：这是唯一有实质 token 节省和架构收益的组件。建议放入 Phase 3（OH2 记忆升级）一起做，因为同属 prompt token 优化领域。

2. **McpResourceResolver 和 McpServerDescriptor 不纳入 v2.1**：
   - 投入产出比不合理
   - 单人开发应聚焦高价值任务
   - YAGNI：为不存在的需求写代码是浪费

3. **为未来扩展预留的最小成本方案**：
   - McpRuleAdapter 的 `ParseAgentsMd()` 方法签名接受 `string content` 而非直接读 MCP，便于未来接入其他来源
   - 不需要接口或抽象类，方法签名本身已足够灵活
   - 如果条件加载额外资源的需求出现，在 `InjectMcpResources` 中加 if/else 即可（5 行代码）

### 3.3 一句话结论

> **只做 McpRuleAdapter，不做 McpResourceResolver 和 McpServerDescriptor。** 用最少代码解决最大痛点（token 浪费），避免为单一 MCP 服务器构建通用框架。等第二个 MCP 服务器真正出现时再抽象，一天就能做完，质量还更高。

---

## 附录：源码依据

| 文件 | 关键点 |
|------|--------|
| `AgentExecutor.cs:1019-1080` | `InjectMcpResources()` — 当前全量 dump 逻辑 |
| `McpClient.cs:186-236` | `ListResourcesAsync()` + `ReadResourceAsync()` — 已有读取能力 |
| `DynamicToolSelector.cs` | `ClassifyIntent()` + `ToolGroup` — intent+complexity 决策框架 |
| `McpBridgeTool.cs` | 工具层已实现自动桥接，不需要 Descriptor 管理工具注册 |
| `GitNexusProcessManager.cs` | 单例管理唯一 MCP 服务器，不需要多服务器注册表 |
