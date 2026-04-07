# Pane 4 任务：McpResourceResolver 与 McpServerDescriptor 评估

## 你的角色
你是开发团队成员，负责评估 McpResourceResolver 和 McpServerDescriptor 的必要性和设计。

## 背景
请先阅读以下文件：
1. `/mnt/d/Project/AIConsProject/AICA/doc/discussion/SK_MCP_Integration_Discussion.md` — 总体讨论文档
2. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_v2.12_Issues_Summary.md` — 4.1节
3. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_MCP_Redundant_Files_Issue.md` — 层面B

然后阅读相关源码：
4. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Agent/AgentExecutor.cs` — 搜索 InjectMcpResources 方法和 MCP 相关代码
5. 搜索项目中所有 MCP 相关的类和文件（搜索 "Mcp" 或 "mcp" 关键字）
6. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Agent/DynamicToolSelector.cs` — 了解 intent+complexity 决策机制

## 你的任务

### 1. McpResourceResolver 评估
- **现状分析**：GitNexus 提供了哪些 MCP 资源？当前使用了哪些？哪些未使用？
- **价值评估**：未使用的 MCP 资源（clusters/processes/schema）在什么场景下有价值？
- **按需加载逻辑**：如何根据 intent+complexity 决策是否加载特定资源？
- **设计建议**：是否值得现在实现？还是等有更多 MCP 服务器时再做？

### 2. McpServerDescriptor 评估
- **当前 MCP 服务器数量**：AICA 目前只有 GitNexus 一个 MCP 服务器
- **过度设计风险**：为单个服务器设计通用框架是否值得？
- **YAGNI 原则**：如果未来才可能有第二个 MCP 服务器，现在做是否为时过早？
- **替代方案**：是否可以在 McpRuleAdapter 中硬编码 GitNexus 逻辑，等有第二个服务器时再抽象？

### 3. 综合建议
- 从投入产出比角度，McpResourceResolver 和 McpServerDescriptor 是否应该纳入 v2.1 计划？
- 如果纳入，什么时候做最合适？
- 如果不纳入，是否需要在 McpRuleAdapter 设计中为未来扩展预留接口？

## 项目约束（重要）
- 单人开发
- 涉密离线环境
- MiniMax-M2.5 单模型（弱模型 + 强系统）
- 当前只有 GitNexus 一个 MCP 服务器
- v2.1 计划已有24-26周排期

## 输出
将你的分析结果写入：`/mnt/d/Project/AIConsProject/AICA/doc/discussion/pane4_output.md`

注意：只做分析和设计，不要修改任何源代码。
