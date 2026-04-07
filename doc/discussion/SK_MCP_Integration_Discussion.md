# 讨论：MCP 内容结构化吸收与 Skills 系统整合方案

> **发起人**: Pane 0（总指挥）
> **审核人**: Pane 5（审核员）
> **日期**: 2026-04-07
> **状态**: 待讨论

---

## 一、背景

### 1.1 已完成的 SK（Skills）系统

v2.1 Phase 1 已完成 SK 任务，建立了完整的 Skills/Rules 架构：

| 组件 | 文件 | 功能 |
|------|------|------|
| Rule 模型 | `Rule.cs` + `RuleMetadata.cs` | 支持 Type(rule/skill)、Intent、Paths、Priority |
| 加载器 | `RuleLoader.cs` | 从 .aica-rules/ 和 ~/.aica/rules/ 加载 .md 文件 |
| 评估器 | `RuleEvaluator.cs` | 按 path glob 匹配 + intent 精确匹配 |
| 注入器 | `SystemPromptBuilder.cs` | AddRulesFromFilesAsync() + AddSkillsByIntent() |
| 解析器 | `YamlFrontmatterParser.cs` | 提取 YAML frontmatter → RuleMetadata |
| 模板 | .aica-rules/ (11个文件) | 4 个 skill 模板 + 7 个 rule 文件 |

**关键设计**: 规则按 Source 优先级（Workspace:20 > Global:10 > Builtin:0）、intent 条件激活、path glob 匹配。

### 1.2 当前 MCP 内容注入方式

`AgentExecutor.cs` 的 `InjectMcpResources()` (line 1016-1080)：
- 读取 `gitnexus://setup`（AGENTS.md 内容，~1800 tokens）
- 读取 `gitnexus://repo/{name}/context`（仓库特定上下文）
- **不透明文本全量 dump** 进 SystemPromptBuilder._staticBuilder
- 无条件注入，每次请求都消耗 ~1800 tokens

### 1.3 nextstep2.1 中提出的优化方向

`AICA_v2.12_Issues_Summary.md` 4.1 节和 `AICA_MCP_Redundant_Files_Issue.md` 层面 B 提出：

| 组件 | 估算代码量 | 功能 |
|------|-----------|------|
| McpRuleAdapter (~150行) | 解析 AGENTS.md → 结构化 Rule 对象 | 融入 RuleEvaluator，按 intent/phase 条件激活 |
| McpResourceResolver (~100行) | 按需加载 MCP 资源 | 根据 intent+complexity 决策是否加载 clusters/processes |
| McpServerDescriptor (~80行) | 可复用 MCP 吸收模式 | 标准化 MCP 服务器→Rule 转换 |

**Token 优化效果**: 简单请求从 ~1950 降到 ~540 tokens（-72%）

---

## 二、问题分析

### 2.1 架构冲突

当前存在两套并行的 prompt 注入机制：

```
SystemPrompt
├── [精细注入] Rules/Skills 系统（已完成）
│   ├── RuleEvaluator: path glob + intent 匹配
│   ├── 条件激活，按需注入
│   └── ~200-500 tokens（视匹配结果）
│
└── [粗暴注入] MCP 内容（待优化）
    ├── InjectMcpResources: 全量 dump
    ├── 无条件注入，每次 ~1800 tokens
    └── 与 Rules 系统完全独立
```

**矛盾**: SK 系统精心设计了条件激活机制，但 MCP 内容绕过了这套机制。

### 2.2 现有 AGENTS.md 内容结构

AGENTS.md 包含：
- **Always 规则**: 如 "Always verify with context() before suggesting changes"
- **Never 规则**: 如 "Never modify files without understanding dependencies"
- **When Debugging 规则**: 条件性行为指导
- **When Refactoring 规则**: 条件性行为指导
- **Self-Check 清单**: 每次回答前的检查项

这些内容天然适合转换为 Rule 对象：
- Always/Never → 通用规则（无 intent 条件）
- When Debugging → intent="bug_fix" 条件规则
- When Refactoring → intent="modify" 条件规则

### 2.3 两个独立子问题

| 子问题 | 描述 | 影响 |
|--------|------|------|
| **A: 冗余文件清理** | GitNexus analyze 生成 AGENTS.md/CLAUDE.md/.claude/ | 用户痛点，低成本修复 |
| **B: MCP 内容结构化吸收** | 将 MCP 内容融入 Rule 系统 | 架构一致性 + token 优化 |

---

## 三、方案选项

### 方案 1：独立新增 Phase（推荐讨论）

将 MCP 内容吸收作为新任务加入 v2.1 计划：

| 任务 | 阶段 | 工作量 | 依赖 |
|------|------|--------|------|
| MCP-A: 冗余文件清理 | 可立即执行 | 0.5 天 | 无 |
| MCP-B: McpRuleAdapter | Phase 2 或 3 | 2-3 天 | SK 已完成 ✅ |
| MCP-C: McpResourceResolver | Phase 3 或 4 | 1-2 天 | MCP-B |
| MCP-D: McpServerDescriptor | 可选/长期 | 1 天 | MCP-B |

**优点**: 不扰乱现有排期
**缺点**: 增加总工作量

### 方案 2：融入 OH2（Phase 3 记忆升级）

MCP 内容吸收与 OH2 记忆系统升级一起做：
- OH2 已涉及 prompt token 优化
- McpRuleAdapter 也是 prompt token 优化
- 合并后 OH2 工作量从 8-10 天增加到 10-13 天

**优点**: 概念相近，集中优化 prompt 效率
**缺点**: OH2 已较重，加码有排期风险

### 方案 3：拆分到最近的空闲窗口

- MCP-A（冗余文件清理）：立即做（0.5天，无依赖）
- MCP-B（McpRuleAdapter）：放入验证窗口 1 的弹性时间（第 4 周后 3 天）
- MCP-C/D：放入验证窗口 2（第 11 周）

**优点**: 利用弹性时间，不增加 Phase 工作量
**缺点**: 弹性时间本用于消化溢出

---

## 四、待讨论问题

1. **优先级判断**: MCP 内容结构化吸收是否应该优先于当前的 Phase 2 R2（H1 剩余5工具截断持久化接入）？
2. **方案选择**: 方案 1/2/3 哪个更合适？是否有其他方案？
3. **MCP-A 冗余文件清理**: 是否可以立即执行（不影响其他任务，0.5天）？
4. **McpRuleAdapter 设计**: AGENTS.md 内容解析为 Rule 对象的具体映射逻辑——Always/Never 是否应该用不同的 RuleSource？
5. **与 v2.1 计划的兼容性**: 新增任务是否影响 24-26 周总排期？
6. **McpServerDescriptor 的必要性**: 当前只有 GitNexus 一个 MCP 服务器，通用化是否过度设计？

---

## 五、请 Pane 5 审核要点

1. 上述分析是否准确？是否遗漏了重要的关联？
2. 三个方案的优缺点分析是否合理？
3. 是否有架构层面的风险我没有考虑到？
4. 对 "MCP-A 冗余文件清理可立即执行" 的判断是否同意？
5. 对于 McpServerDescriptor 是否过度设计的判断？
