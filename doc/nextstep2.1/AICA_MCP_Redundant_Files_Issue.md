# AICA — MCP 冗余文件问题与 MCP 内容吸收方案

> 日期: 2026-04-03（整理自此前讨论）
> 状态: **待讨论确认后实施**

---

## 一、问题描述

AICA 安装并成功初始化（打开解决方案触发 GitNexus analyze）后，会在**用户的项目目录**中生成以下文件/文件夹：

| 文件/目录 | 来源 | AICA 是否使用 |
|-----------|------|---------------|
| `AGENTS.md` | GitNexus analyze 生成 | 间接使用（通过 MCP resource `gitnexus://setup` 读取，~1800 tokens dump 进 prompt） |
| `CLAUDE.md` | GitNexus analyze 生成 | **完全不使用** |
| `.claude/` | GitNexus analyze 生成 | **完全不使用** |

**用户痛点：**
- 项目目录被污染，出现用户不认识的文件
- 这些文件是 Claude Code 的上下文文件，对 AICA 用户毫无意义
- 用户需要手动清理，或将其加入 .gitignore

---

## 二、根因分析

### 2.1 GitNexus analyze 命令行为

GitNexus 的 `analyze` 子命令在索引代码库时，默认会：
1. 解析代码结构，构建知识图谱（**这是 AICA 需要的**）
2. 生成 `AGENTS.md`（GitNexus 行为规则，供 Claude Code 使用）
3. 生成 `CLAUDE.md`（项目上下文摘要，供 Claude Code 使用）
4. 生成 `.claude/` 目录（Claude Code 配置）

步骤 2-4 是为 Claude Code CLI 设计的，AICA 作为独立 VSIX 扩展不需要这些。

### 2.2 AICA 当前对 AGENTS.md 的使用方式

`AgentExecutor.cs` (line ~964) 通过 MCP resource 读取 AGENTS.md 内容：

```csharp
// Read setup resource (AGENTS.md — tells LLM how to use GitNexus)
var setup = await client.ReadResourceAsync("gitnexus://setup", ct);
```

读取到的内容（~1800 tokens）包含：
- **Always/Never 规则**：如 "Always verify with context() before suggesting changes"
- **When Debugging/When Refactoring 规则**：条件性行为指导
- **Self-Check 清单**：每次回答前的检查项

这些内容作为**不透明文本**整体 dump 进 System Prompt，没有经过结构化处理。

### 2.3 未使用的 MCP 资源

GitNexus 还提供了 6 个额外 MCP 资源（clusters/processes/schema 等），AICA 当前只使用了 `gitnexus://setup` 和 `gitnexus://repo/{name}`，其余完全未使用。

---

## 三、解决方案概述

分为两个层面：

### 层面 A：消除冗余文件（用户直接痛点）

**目标：** GitNexus analyze 不再在项目目录中生成 AGENTS.md、CLAUDE.md、.claude/

**可能方案：**

| 方案 | 描述 | 可行性 |
|------|------|--------|
| A1. `--no-context-files` 参数 | 调用 analyze 时传入标志禁止生成 | 需确认 GitNexus CLI 是否支持此参数 |
| A2. 后置清理 | analyze 完成后删除这些文件 | 简单粗暴，但 analyze 是可见控制台进程，需在退出后执行 |
| A3. .gitignore 注入 | 自动将 AGENTS.md/CLAUDE.md/.claude/ 加入 .gitignore | 不解决文件存在问题，只解决 git 追踪 |

**推荐：** 先确认 A1 是否可行（查看 GitNexus analyze 的 CLI help），不行则用 A2。

### 层面 B：MCP 内容结构化吸收（深层优化）

**目标：** 将 AGENTS.md 中有价值的行为规则结构化，替代当前的文本 dump

**设计方向（待确认）：**

1. **McpRuleAdapter** (~150 行)
   - 解析 AGENTS.md 内容为结构化 Rule 对象
   - 融入现有 RuleEvaluator 系统
   - 按 intent/phase 条件激活（如 "debugging" intent 才加载 debugging 规则）
   - Token 优化：简单请求从 ~1950 降到 ~540 tokens（-72%）

2. **McpResourceResolver** (~100 行)
   - 按需动态获取额外 MCP 资源（clusters/processes）
   - 根据 intent + complexity 决策是否加载
   - 避免每次请求都加载全部资源

3. **McpServerDescriptor** (~80 行)
   - 可复用的 MCP 吸收模式
   - 未来新 MCP 服务器自动吸收行为规则
   - 标准化 MCP 资源 → AICA Rules 的转换流程

---

## 四、涉及文件

| 文件 | 角色 |
|------|------|
| `AICA.Core/Agent/GitNexusProcessManager.cs` | `TriggerIndexAsync` / `TriggerIndexWithProgressAsync` 调用 analyze |
| `AICA.Core/Agent/AgentExecutor.cs` (~line 964) | 读取 `gitnexus://setup` MCP resource，dump 进 prompt |
| `AICA.Core/Rules/RuleEvaluator.cs` | 现有规则系统，可扩展吸收 MCP 规则 |
| `AICA.Core/Prompt/SystemPromptBuilder.cs` | 组装 System Prompt，包含 MCP 内容 |

---

## 五、当前状态与待决事项

- [x] 问题识别和初步方案设计
- [ ] **确认 GitNexus analyze 是否支持 `--no-context-files` 或类似参数**
- [ ] 决定层面 A 的具体方案（A1/A2/A3）
- [ ] 决定层面 B 是否在当前阶段实施（还是只做层面 A）
- [ ] 层面 B 的详细设计评审（planner + reviewer）

---

## 六、优先级建议

| 层面 | 价值 | 成本 | 建议 |
|------|------|------|------|
| **A: 消除冗余文件** | 高（用户直接痛点） | 低（可能 1 行改动） | **立即实施** |
| **B: MCP 内容吸收** | 中（token 优化 + 架构改善） | 中（~330 行，3 个新文件） | 后续评估 |
