# Pane 5 审核意见

> **角色**: 审核员（Pane 5）
> **日期**: 2026-04-07
> **状态**: 审核完成

---

## 关键发现：数据源假设错误（影响全局）

在审核各 Pane 输出之前，必须先指出一个**影响所有方案的关键事实错误**：

### 讨论文档和 Pane 1 均假设 `gitnexus://setup` MCP resource 包含 Always/Never/When Debugging/Self-Check 行为规则。这是错误的。

**实际情况**（经源码验证）：

| 内容来源 | 实际内容 | 代码位置 |
|----------|---------|---------|
| `gitnexus://setup` MCP resource | **工具参考表格 + 资源列表**（如 "query: Process-grouped code intelligence"） | `resources.js:385-419 getSetupResource()` |
| AGENTS.md 文件（文件系统） | **行为规则**（Always Do / When Debugging / Never Do / Self-Check） | `ai-context.js:46-104 generateGitNexusContent()` |

`getSetupResource()` 从数据库动态生成工具参考内容，**不包含 Always/Never 行为规则**。行为规则仅存在于 `ai-context.js` 生成的文件版本中。

**影响**：
- Pane 1 的 McpRuleAdapter 设计基于"解析 `gitnexus://setup` 返回的 Always/Never 规则"——但该 resource 里没有这些规则
- 讨论文档中 "~1800 tokens 的 AGENTS.md 行为规则" 描述的是文件系统版本的内容，不是 MCP resource 的内容
- Token 优化估算（-72% / -38%）的前提不成立
- 当前 `AgentExecutor.cs:1033` 实际注入的是工具参考表格，不是行为规则

**Pane 3 已部分发现此问题**（第 5.3 节），但未充分强调其对整体方案的颠覆性影响。

**建议**：在推进任何 MCP 内容结构化方案之前，必须先澄清：
1. 当前 `gitnexus://setup` 注入的工具参考表格是否需要结构化？（可能不需要——它本身就是 LLM 需要的工具指导）
2. 文件系统 AGENTS.md 中的行为规则是否需要通过其他途径注入？（如新增 MCP resource `gitnexus://rules`，或将规则硬编码为内置 Rule）
3. 如果行为规则目前根本没有被注入到 prompt 中（因为 AICA 读的是 MCP resource 不是文件），那"token 优化"的需求是否还存在？

---

## A. 对 Pane 1（McpRuleAdapter 设计）的审核

### 评价：设计质量高，但建立在错误前提之上

**优点**：
1. Rule 属性映射表设计清晰，与现有 SK 架构完美契合
2. `RuleSource.Mcp = 15` 的优先级设计合理（Workspace > Mcp > Global > Builtin）
3. 错误处理策略完备，降级逻辑（解析失败→全量注入为 Always）符合 fail-open 原则
4. 缓存策略（SHA256 哈希键 + 生命周期管理）设计得当
5. 与 RuleEvaluator 的集成方式正确——利用现有评估流程，不改核心代码

**问题**：

| # | 问题 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | **数据源错误** | **致命** | 整个设计基于"解析 `gitnexus://setup` 中的 Always/Never 规则"，但该 resource 不包含这些规则（见上方关键发现） |
| 2 | Token 优化估算不准确 | 高 | -38%~-72% 的估算基于错误的 AGENTS.md 内容分布假设。实际 `gitnexus://setup` 内容是工具参考表（可能 ~800-1000 tokens），行为规则不在其中 |
| 3 | `intent="refactor"` 是新增值 | 低 | Pane 1 正确识别了这一点，但未讨论对 `DynamicToolSelector.ClassifyIntent()` 的影响——需确认 ClassifyIntent 是否会返回 "refactor" |
| 4 | Self-Check 注入方式未决 | 低 | 开放问题 #2（static vs dynamic builder）值得讨论，Self-Check 作为通用检查清单放 `_staticBuilder` 更合理 |

**结论**：**设计需要基于正确的数据源重新评估后才能采纳。** 如果最终决定将文件系统 AGENTS.md 中的行为规则作为内置 Rule（而非从 MCP resource 动态解析），则 Pane 1 的映射表和集成架构仍有参考价值，但不需要 McpRuleAdapter 类，改为在 `.aica-rules/` 中直接提供内置规则文件即可。

---

## B. 对 Pane 2（排期分析）的审核

### 评价：分析全面、方案务实，推荐合理

**优点**：
1. 依赖关系分析完整，正确识别了 SK 作为 MCP-B 的唯一前置依赖
2. 三个方案的对比清晰，风险等级评估合理
3. 方案 A（验证窗口弹性时间）的推荐符合单人开发约束——不影响主线排期是正确的优先考量
4. 应急预案设计务实（MCP-B 可顺延到 Phase 3 启动前）
5. "MCP 是否优先于 Phase 2 R2？否"——判断完全正确

**问题**：

| # | 问题 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | 弹性时间消耗评估可能偏乐观 | 中 | "弹性缓冲减少约 4-5.5 天"——如果 Phase 2 H1（10-15天）溢出（这在单人开发中很常见），弹性时间可能完全被占用 |
| 2 | MCP-B 排在验证窗口 2 的依据需商榷 | 低 | 验证窗口 2 的主要目的是评估 H1 截断策略和 OH2 方向。加入 MCP-B 可能分散验证重点 |
| 3 | 受数据源问题影响 | 高 | 如果 McpRuleAdapter 的前提不成立（见关键发现），MCP-B 的工作量和排期需要重新评估 |

**关于优先级排序回应**（Pane 2 第五节 #4）：Pane 2 建议 `RuleSource.Mcp` Priority=5（低于 Builtin:0），与 Pane 1 的 Priority=15 矛盾。Pane 1 的 15 更合理——MCP 规则来自项目代码分析，是项目级信息，应高于全局规则。

**结论**：**方案 A 的总体思路可采纳**——利用弹性时间、不影响主线。但具体排期需要在澄清数据源问题后重新制定。MCP-A（冗余文件清理）立即执行的建议维持有效。

---

## C. 对 Pane 3（冗余文件清理方案）的审核

### 评价：分析最为扎实，发现了关键数据源差异

**优点**：
1. GitNexus CLI 能力确认彻底——明确排除了 `--no-context-files` 的存在
2. 文件生成调用链追踪清晰（`analyze.js:330 → ai-context.js:248-265`）
3. 方案 A2（后置清理）代码示例完整可用，包含异常捕获和日志
4. **第 5 节（与 MCP 内容吸收的关系）是全场最有价值的分析**——正确发现了 MCP resource 和文件系统 AGENTS.md 的内容差异
5. 测试方案全面，覆盖了正常/异常/回归场景

**问题**：

| # | 问题 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | 误删防护不够重视 | 低 | Pane 3 提到"AICA 用户不太会手写 AGENTS.md，简单删除即可"，但如果用户恰好使用 Claude Code 或 Cursor 打开同一项目，AGENTS.md 对它们有用。建议增加 GitNexus 标记检测 |
| 2 | 首次打开的窗口期 | 极低 | "文件生成后再删除，存在短暂窗口期"——实际影响几乎为零（analyze 运行期间用户不会去看目录） |
| 3 | 第 5.3 节的关键发现未被提升为全局警告 | 中 | Pane 3 发现了 MCP resource vs 文件 AGENTS.md 内容不同这一关键事实，但只是在"注意事项"中提及，未强调其对 Pane 1 设计的颠覆性影响 |

**关于 Pane 3 的数据源建议**（第 5.3 节）：
- 建议 1（上游新增 `gitnexus://rules` resource）——合理，但增加上游改动
- 建议 2（从 `ai-context.js` 提取规则模板作为内置 Rule）——**这是最务实的方案**，行为规则本质上是通用的 GitNexus 使用指导，可以直接硬编码为 `.aica-rules/` 中的内置规则文件，不需要从 MCP 动态获取

**结论**：**方案 A2（后置清理）完全可以采纳并立即实施。** 这是本次讨论中唯一没有前提争议的方案。

---

## D. 对 Pane 4（ResourceResolver/Descriptor 评估）的审核

### 评价：YAGNI 判断正确，结论清晰

**优点**：
1. 未使用资源的价值评估客观（低频场景通过工具调用已可覆盖）
2. "资源注入 vs 工具调用"的本质区别分析透彻
3. McpServerDescriptor 的过度设计分析完全正确——单 MCP 服务器不需要通用框架
4. "等第二个 MCP 服务器真正出现时再抽象"——符合 YAGNI 原则
5. 替代方案（硬编码 if/else，5 行代码）务实

**问题**：

| # | 问题 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | Token 节省数据引用了 Pane 1 的错误估算 | 中 | 第 3.1 节表格中 McpRuleAdapter "~1400 tokens/请求（-72%）" 来自讨论文档的初始提案，Pane 1 自己的分析已修正为 -18%~-38%，且这两个数字也基于错误前提 |
| 2 | 建议 MCP-B 放入 Phase 3 与 Pane 2 推荐矛盾 | 低 | Pane 4 建议放 Phase 3（OH2 一起），Pane 2 建议放验证窗口 2。这是排期偏好差异，不是错误 |

**结论**：**McpResourceResolver 和 McpServerDescriptor 不做——完全同意。** 这是正确的范围控制决策。

---

## E. 总体审核

### E.1 方案一致性检查

| 检查项 | 结果 |
|--------|------|
| Pane 1-4 之间是否有矛盾？ | **有**：Pane 2 建议 Mcp Priority=5，Pane 1 设计 Priority=15（取 Pane 1） |
| Pane 2 vs Pane 4 排期建议？ | **不一致**：Pane 2→验证窗口 2，Pane 4→Phase 3。差异可接受，具体取决于弹性时间可用性 |
| 数据源问题是否被充分认识？ | **否**：仅 Pane 3 部分发现，其他 Pane 未意识到 |

### E.2 遗漏的风险

1. **当前 prompt 中实际注入了什么？** 需要运行一次 AICA 并打印 `gitnexus://setup` 的实际返回内容，确认 token 量和内容类型。讨论文档称 "~1800 tokens"，但如果实际内容是工具参考表，token 量可能不同。

2. **行为规则是否目前完全未被注入？** 如果 AGENTS.md 文件中的 Always/Never 规则从未进入 prompt（因为 AICA 读的是 MCP resource 不是文件），那这些规则一直没有生效。这意味着：
   - 现有 LLM 行为可能并不依赖这些规则
   - 新增注入这些规则可能改变现有行为（需要测试）
   - "token 优化"的需求需要重新定义——不是"减少注入"而是"是否需要开始注入"

3. **工具参考表的价值问题**：当前注入的工具参考表（来自 `gitnexus://setup`）对 LLM 使用 GitNexus 工具有指导作用。如果结构化处理不当，可能反而降低 LLM 的工具调用质量。在 MiniMax-M2.5 这样的弱模型上，工具使用指导尤其重要。

### E.3 与 v2.1 计划契合度

| 方面 | 评估 |
|------|------|
| MCP-A 冗余文件清理 | **高度契合** — 用户痛点修复，0.5天，无排期影响 |
| MCP-B McpRuleAdapter | **需重新评估** — 数据源问题改变了设计方向和工作量 |
| MCP-C McpResourceResolver | **不做** — 与 v2.1 计划无冲突 |
| MCP-D McpServerDescriptor | **不做** — YAGNI，与 v2.1 计划无冲突 |

### E.4 最终建议

#### 立即采纳

| 方案 | 理由 |
|------|------|
| **MCP-A 冗余文件清理（Pane 3 方案 A2）** | 0.5天、无依赖、代码完整可用、解决用户直接痛点。本周 Phase 2 R2 间隙完成 |
| **MCP-C/D 不做（Pane 4 结论）** | YAGNI 判断正确，节省精力聚焦核心任务 |

#### 需要重新评估后决定

| 方案 | 原因 | 建议行动 |
|------|------|---------|
| **MCP-B McpRuleAdapter** | 数据源假设错误，需要先确认：(1) `gitnexus://setup` 实际内容和 token 量 (2) 行为规则是否需要注入 (3) 如需注入，最佳方式是什么 | 先做一次实际运行验证 `gitnexus://setup` 返回内容，再决定方案方向 |

#### 替代方案建议（如果行为规则需要注入）

与其构建 McpRuleAdapter 从 MCP 动态解析，**更简单的方案**是：
1. 将 `ai-context.js` 中的 GitNexus 行为规则（Always/Never/When Debugging/Self-Check）直接作为 `.aica-rules/` 中的内置规则文件
2. 利用已有的 RuleLoader + RuleEvaluator 机制自动加载和条件激活
3. 无需新增任何类，只需新增 3-4 个 `.md` 规则文件（~0.5天工作量）
4. Pane 1 的 Rule 属性映射表可直接复用于编写这些 `.md` 文件的 YAML frontmatter

这个替代方案的优势：
- 工作量从 2-3 天降到 0.5 天
- 不依赖 MCP resource 的内容格式
- 不需要解析器——直接用已有的 RuleLoader
- 完全复用 SK 基础设施，零架构新增
- 如果 GitNexus 更新了行为规则，只需更新 `.md` 文件

---

## 附录：源码验证记录

| 验证项 | 文件 | 行号 | 结果 |
|--------|------|------|------|
| `getSetupResource()` 内容 | `resources.js` | 385-419 | 返回工具参考表格，不含行为规则 |
| AGENTS.md 行为规则来源 | `ai-context.js` | 46-104 | Always/Never/When Debugging/Self-Check 在此生成 |
| `AgentExecutor` 注入的数据源 | `AgentExecutor.cs` | 1033 | 读取 MCP resource（工具参考），不是文件 |
| `generateAIContextFiles` 调用 | `analyze.js` | 330 | 无条件调用，无 flag 控制 |
