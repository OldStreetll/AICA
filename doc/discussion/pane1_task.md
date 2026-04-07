# Pane 1 任务：McpRuleAdapter 详细设计

## 你的角色
你是开发团队成员，负责设计 McpRuleAdapter 组件。

## 背景
请先阅读以下文件了解背景：
1. `/mnt/d/Project/AIConsProject/AICA/doc/discussion/SK_MCP_Integration_Discussion.md` — 总体讨论文档
2. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_v2.12_Issues_Summary.md` — 4.1节 MCP内容结构化吸收
3. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_MCP_Redundant_Files_Issue.md` — MCP冗余文件问题

然后阅读现有 SK/Rules 系统代码：
4. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Rules/Models/Rule.cs` — Rule 模型
5. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Rules/RuleEvaluator.cs` — 规则评估器
6. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Rules/RuleLoader.cs` — 规则加载器
7. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Prompt/SystemPromptBuilder.cs` — Prompt 构建器
8. `/mnt/d/Project/AIConsProject/AICA/src/AICA.Core/Agent/AgentExecutor.cs` — 搜索 InjectMcpResources 方法（约line 1016-1080），了解当前 MCP 内容如何注入

## 你的任务
基于对现有代码的理解，设计 McpRuleAdapter 的详细方案：

1. **AGENTS.md 内容解析逻辑**：Always/Never/When Debugging/When Refactoring/Self-Check 各类规则如何映射为 Rule 对象
2. **Rule 属性映射**：每种 MCP 规则对应的 Type、Intent、Priority、Source 值
3. **与 RuleEvaluator 的集成方式**：McpRuleAdapter 生成的 Rule 如何与 .aica-rules/ 文件规则共存
4. **Token 优化估算**：条件激活后不同 intent 场景下的 token 消耗对比
5. **接口设计**：McpRuleAdapter 的公开方法签名
6. **错误处理**：MCP 不可用时的 fallback 策略

## 输出
将你的分析结果写入：`/mnt/d/Project/AIConsProject/AICA/doc/discussion/pane1_output.md`

注意：只做分析和设计，不要修改任何源代码。
