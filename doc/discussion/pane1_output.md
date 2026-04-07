# McpRuleAdapter 详细设计

> **作者**: Pane 1（开发员工）
> **日期**: 2026-04-07
> **状态**: 待审核

---

## 一、AGENTS.md 内容解析逻辑

### 1.1 AGENTS.md 结构识别

AGENTS.md 由 GitNexus 生成，包含以下五类规则段落，通过 Markdown 标题或关键词前缀识别：

| 段落类型 | 识别模式 | 示例 |
|----------|----------|------|
| **Always** | 行首 `Always` 或 `## Always` 段落下的条目 | "Always verify with context() before suggesting changes" |
| **Never** | 行首 `Never` 或 `## Never` 段落下的条目 | "Never modify files without understanding dependencies" |
| **When Debugging** | `## When Debugging` 段落下的条目 | 条件性调试行为指导 |
| **When Refactoring** | `## When Refactoring` 段落下的条目 | 条件性重构行为指导 |
| **Self-Check** | `## Self-Check` 或 `## Before Responding` 段落下的条目 | 每次回答前的检查项 |

### 1.2 解析算法

```
输入: string agentsmdContent (从 gitnexus://setup MCP resource 读取)
输出: List<Rule>

1. 按 Markdown 二级标题 (##) 分割文本为 sections
2. 对每个 section:
   a. 识别标题关键词 → 确定 RuleCategory (Always/Never/WhenDebugging/WhenRefactoring/SelfCheck)
   b. 提取 section body（去除标题行）
   c. 按 bullet point (- 或 *) 分割为独立条目
   d. 对于 Always/Never: 每条生成一个独立 Rule（粒度细，支持未来按需禁用）
   e. 对于 When Debugging / When Refactoring / Self-Check: 合并为一个 Rule（同类条目语义关联，拆分会丢失上下文）
3. 为每个 Rule 填充属性（见第二节映射表）
4. 如果解析失败或内容为空 → 返回空列表 + 日志警告
```

### 1.3 段落归类不明确时的处理

AGENTS.md 由 GitNexus 自动生成，格式可能有变化。对于无法归类到五种类型的段落：
- 归入 `Always` 类型（无条件注入，保守策略，不丢失信息）
- 日志记录未识别的段落标题，供后续调试

---

## 二、Rule 属性映射

### 2.1 新增 RuleSource 值

```csharp
public enum RuleSource
{
    Workspace = 20,  // .aica-rules/ (最高)
    Mcp = 15,        // ← 新增：MCP 服务器提供的规则
    Global = 10,     // ~/.aica/rules/
    Remote = 5,      // 远程规则 (future)
    Builtin = 0      // 内置规则 (最低)
}
```

**设计理由**: MCP 规则来自 GitNexus 对项目代码的分析，是项目级规则，优先级应高于全局规则但低于用户在 `.aica-rules/` 中显式定义的规则。用户可通过同 Id 的 Workspace 规则覆盖 MCP 规则（利用 `MergeRules` 的去重逻辑）。

### 2.2 各类规则的属性映射表

| AGENTS.md 类型 | Rule.Id | Metadata.Type | Metadata.Intent | Rule.Source | Rule.Priority | Metadata.Paths |
|----------------|---------|---------------|-----------------|-------------|---------------|----------------|
| **Always** | `mcp-always-{n}` | `"rule"` | `null`（无条件） | `Mcp (15)` | `15` | `[]`（空=通用） |
| **Never** | `mcp-never-{n}` | `"rule"` | `null`（无条件） | `Mcp (15)` | `15` | `[]`（空=通用） |
| **When Debugging** | `mcp-when-debugging` | `"rule"` | `"bug_fix"` | `Mcp (15)` | `15` | `[]` |
| **When Refactoring** | `mcp-when-refactoring` | `"rule"` | `"refactor"` | `Mcp (15)` | `15` | `[]` |
| **Self-Check** | `mcp-self-check` | `"rule"` | `null`（无条件） | `Mcp (15)` | `15` | `[]` |

**映射说明**:

1. **Always/Never → 无 intent 条件**: 这些是通用规则，每次请求都应注入。Type 为 `"rule"`（非 `"skill"`），通过 `EvaluateRules` 路径激活。
2. **When Debugging → intent="bug_fix"**: 与现有 `skill-bug-fix.md` 的 intent 一致，仅在 bug_fix intent 时激活。
3. **When Refactoring → intent="refactor"**: 仅在重构 intent 时激活。注意现有 `.aica-rules/` 没有 `intent="refactor"` 的 skill，这是新增的 intent 值。
4. **Self-Check → 无 intent 条件**: 检查清单应始终注入（对所有任务类型都有价值）。
5. **Id 命名规范**: `mcp-` 前缀明确标识来源，避免与 `.aica-rules/` 文件规则 Id 冲突。

### 2.3 Rule.Content 格式

每个 Rule 的 Content 保留原始 Markdown 文本，但去除段落标题行，只保留正文内容。例如：

```
// Always 规则的 Content:
"Always verify with context() before suggesting changes"

// When Debugging 规则的 Content (合并后):
"- Step through the code path mentally before suggesting fixes\n- Check error logs and stack traces\n- ..."
```

---

## 三、与 RuleEvaluator 的集成方式

### 3.1 集成架构

```
当前流程:
  RuleLoader.LoadAllRulesAsync() → List<Rule> (来自 .aica-rules/ + ~/.aica/rules/)
  ↓
  RuleEvaluator.EvaluateRules() / EvaluateSkillsByIntent()
  ↓
  SystemPromptBuilder.AddRulesFromFilesAsync() / AddSkillsByIntent()

改造后流程:
  RuleLoader.LoadAllRulesAsync() → List<Rule> (来自 .aica-rules/ + ~/.aica/rules/)
  McpRuleAdapter.ParseRulesAsync() → List<Rule> (来自 MCP resource)
  ↓
  合并两个 List<Rule>
  ↓
  RuleEvaluator.EvaluateRules() / EvaluateSkillsByIntent() ← 无需修改
  ↓
  SystemPromptBuilder ← 无需修改
```

### 3.2 合并策略

McpRuleAdapter 生成的 Rule 与 `.aica-rules/` 规则合并时：

1. **追加合并**: `mcpRules` 追加到 `fileRules` 列表末尾
2. **去重**: 通过 `RuleEvaluator.MergeRules()` 按 Id 去重，保留高优先级版本
3. **覆盖机制**: 用户可在 `.aica-rules/` 中创建同 Id 文件（如 `mcp-always-1.md`），因 Workspace=20 > Mcp=15，自动覆盖 MCP 规则

### 3.3 对 RuleEvaluator 的影响

**无需修改 RuleEvaluator**。原因：

- MCP 规则的 `Metadata.Type = "rule"`，走 `EvaluateRules()` 路径（path glob 匹配）
- MCP 规则的 `Metadata.Paths` 为空列表，被视为 universal rule，始终激活
- 有 intent 的 MCP 规则（When Debugging/Refactoring）通过 intent 字段参与条件注入

**但需要新增 intent 过滤逻辑**：当前 `EvaluateRules()` 只做 path 匹配，不处理 intent。需要在 `SystemPromptBuilder` 的调用侧增加 intent 过滤：

```csharp
// 方案 A（推荐）: 在合并后的规则列表中，对有 intent 的 rule 类型规则做条件过滤
var universalRules = allRules.Where(r => string.IsNullOrEmpty(r.Metadata?.Intent));
var intentRules = allRules.Where(r => !string.IsNullOrEmpty(r.Metadata?.Intent)
                                   && string.Equals(r.Metadata.Intent, currentIntent, StringComparison.OrdinalIgnoreCase));
var activeRules = universalRules.Concat(intentRules).ToList();
```

这段逻辑可放在 `SystemPromptBuilder` 中新增的方法，或放在 `McpRuleAdapter` 的 `GetActiveRules(string intent)` 方法中。

### 3.4 调用时机

McpRuleAdapter 的调用应在 `AgentExecutor.InjectMcpResources()` 中，**替代**当前的 `AddMcpResourceContext()` 全量 dump。具体改造点：

```
// 改造前 (InjectMcpResources):
var setup = await client.ReadResourceAsync("gitnexus://setup", ct);
builder.AddMcpResourceContext(content);  // 全量 dump ~1800 tokens

// 改造后:
var setup = await client.ReadResourceAsync("gitnexus://setup", ct);
var mcpRules = _mcpRuleAdapter.ParseRules(setup);  // 解析为 Rule 对象
// mcpRules 合并到已加载的 fileRules 中，由 RuleEvaluator 条件激活
```

**注意**: `gitnexus://repo/{name}/context` 资源（仓库概览）不适合转为 Rule，仍保留 `AddMcpResourceContext()` 注入方式。只有 `gitnexus://setup`（AGENTS.md 行为规则部分）需要结构化。

---

## 四、Token 优化估算

### 4.1 当前 token 消耗

| 内容 | tokens | 注入方式 | 注入条件 |
|------|--------|----------|----------|
| gitnexus://setup (AGENTS.md) | ~1800 | AddMcpResourceContext → _staticBuilder | **无条件，每次注入** |
| gitnexus://repo/{name}/context | ~150 | AddMcpResourceContext → _staticBuilder | **无条件，每次注入** |
| **总计** | **~1950** | | |

### 4.2 改造后 token 消耗估算

假设 AGENTS.md 内容分布：Always ~400t, Never ~300t, When Debugging ~400t, When Refactoring ~350t, Self-Check ~350t

| Intent 场景 | 注入内容 | 估算 tokens | 对比当前 | 节省 |
|-------------|----------|-------------|----------|------|
| **简单对话** (intent=null) | Always + Never + Self-Check + repo/context | ~400+300+350+150 = **~1200** | 1950 | **-38%** |
| **bug_fix** | Always + Never + Self-Check + When Debugging + repo/context | ~400+300+350+400+150 = **~1600** | 1950 | **-18%** |
| **refactor** | Always + Never + Self-Check + When Refactoring + repo/context | ~400+300+350+350+150 = **~1550** | 1950 | **-21%** |
| **modify** (无特殊MCP规则) | Always + Never + Self-Check + repo/context | **~1200** | 1950 | **-38%** |

### 4.3 进一步优化空间

如果将 Always/Never 规则也做 intent 条件化（而非全量注入），可进一步压缩：
- 例如 "Always verify with context()" 只在 modify/refactor intent 时注入
- 但这需要对每条 Always/Never 规则做语义分析，复杂度高，**建议 v1 不做**

**结论**: v1 预估节省 18%~38%（视 intent 而定）。不如初始提案的 72% 激进，因为 Always/Never/Self-Check 是通用规则无法省略。若需更大优化，需要与 MCP-C（McpResourceResolver 按需加载）配合。

---

## 五、接口设计

### 5.1 类定义

```csharp
namespace AICA.Core.Rules.Mcp
{
    /// <summary>
    /// Parses MCP resource content (AGENTS.md) into structured Rule objects
    /// for integration with the existing Rules/Skills evaluation system.
    /// </summary>
    public class McpRuleAdapter
    {
        private readonly ILogger<McpRuleAdapter> _logger;
        private List<Rule> _cachedRules;
        private string _lastContentHash;

        public McpRuleAdapter(ILogger<McpRuleAdapter> logger = null);

        /// <summary>
        /// Parse AGENTS.md content into a list of Rule objects.
        /// Results are cached by content hash — repeated calls with
        /// identical content return the cached list without re-parsing.
        /// </summary>
        /// <param name="agentsmdContent">Raw text from gitnexus://setup resource</param>
        /// <returns>Parsed rules (may be empty, never null)</returns>
        public List<Rule> ParseRules(string agentsmdContent);

        /// <summary>
        /// Return only the rules that should be active for the given intent.
        /// Universal rules (no intent) are always included.
        /// Intent-specific rules are included only on exact match.
        /// </summary>
        /// <param name="intent">Current task intent (e.g. "bug_fix", "refactor"), or null</param>
        /// <returns>Filtered and priority-sorted rules</returns>
        public List<Rule> GetActiveRules(string intent);

        /// <summary>
        /// Clear the cached parse result (e.g. when MCP server reconnects).
        /// </summary>
        public void InvalidateCache();
    }
}
```

### 5.2 使用方式（调用侧伪代码）

```csharp
// 在 AgentExecutor.InjectMcpResources() 中:

// 1. 读取 MCP resource
var setupContent = await client.ReadResourceAsync("gitnexus://setup", ct);

// 2. 解析为 Rule 对象（内部缓存，不会重复解析）
var mcpRules = _mcpRuleAdapter.ParseRules(setupContent);

// 3. 获取当前 intent 下的活跃规则
var activeMcpRules = _mcpRuleAdapter.GetActiveRules(currentIntent);

// 4. 合并到 fileRules（来自 .aica-rules/）
var allRules = fileRules.Concat(activeMcpRules).ToList();
var merged = ruleEvaluator.MergeRules(allRules);

// 5. 注入到 SystemPromptBuilder
foreach (var rule in merged)
    builder.AddDynamicContent(rule.Content);  // 或通过现有 AddRulesFromFilesAsync 路径

// 6. repo/context 资源仍走原路径
builder.AddMcpResourceContext(repoContextContent);
```

### 5.3 内部辅助类型

```csharp
/// <summary>
/// Categories recognized in AGENTS.md content.
/// </summary>
internal enum McpRuleCategory
{
    Always,
    Never,
    WhenDebugging,
    WhenRefactoring,
    SelfCheck,
    Unknown  // fallback → 按 Always 处理
}
```

---

## 六、错误处理与 Fallback 策略

### 6.1 MCP 不可用时

| 场景 | 行为 | 理由 |
|------|------|------|
| GitNexus 未启动 / State != Ready | 跳过 MCP 规则注入，不报错 | 与当前 `InjectMcpResources` 行为一致 |
| `gitnexus://setup` 读取失败 | 跳过，日志 Warning | Fail-open，不影响核心功能 |
| `gitnexus://setup` 内容为空 | 返回空规则列表 | 空列表不影响下游 |

### 6.2 解析失败时

| 场景 | 行为 | 理由 |
|------|------|------|
| AGENTS.md 格式变化，无法识别段落 | 将全部内容作为单个 Always 规则注入 | **降级为等效于当前行为**（全量注入），不丢失信息 |
| 部分段落解析成功，部分失败 | 成功部分正常返回，失败部分作为 Always 注入 | 最大化结构化利用，最小化信息损失 |
| 解析抛出异常 | catch + 日志 Error + 返回空列表 | Fail-open 原则 |

### 6.3 缓存策略

- **缓存键**: `agentsmdContent` 的 SHA256 哈希
- **缓存失效**: 显式调用 `InvalidateCache()`，或内容哈希变化时自动重新解析
- **缓存生命周期**: 与 `McpRuleAdapter` 实例相同（建议与 `AgentExecutor` 同生命周期）
- **线程安全**: `_cachedRules` 用 `volatile` 或 `lock` 保护（`AgentExecutor` 可能多线程调用）

### 6.4 日志策略

```
[AICA] McpRuleAdapter: parsed 12 rules from AGENTS.md (Always=5, Never=3, WhenDebugging=1, WhenRefactoring=1, SelfCheck=1, Unknown=1)
[AICA] McpRuleAdapter: cache hit, returning 12 cached rules
[AICA] McpRuleAdapter: GetActiveRules(intent=bug_fix) → 10 rules (universal=9, intent-matched=1)
[AICA] McpRuleAdapter: parse failed for section "## Custom Section", treating as Always rule
```

---

## 七、实现估算

| 项目 | 行数 | 说明 |
|------|------|------|
| `McpRuleAdapter.cs` | ~120-150 | 核心解析 + 缓存 + GetActiveRules |
| `McpRuleCategory.cs` (可内联) | ~15 | 枚举定义 |
| `RuleSource` 枚举新增 `Mcp=15` | ~3 | 修改现有文件 |
| `AgentExecutor.InjectMcpResources()` 改造 | ~20 | 替换全量 dump 为结构化注入 |
| 单元测试 | ~100 | ParseRules + GetActiveRules + 降级 + 缓存 |
| **总计** | **~260-290** | |

---

## 八、开放问题（待讨论）

1. **Always/Never 规则粒度**: 当前设计为每条独立 Rule。如果 AGENTS.md 中 Always 条目有 10+ 条，合并为一个 Rule 更简洁但丢失了按条禁用能力。哪种更合适？
2. **Self-Check 的注入方式**: Self-Check 是"每次回答前检查"，语义上更接近 system prompt 的静态部分而非动态规则。是否应保留在 `_staticBuilder` 而非 `_dynamicBuilder`？
3. **intent 映射扩展**: 未来 AGENTS.md 可能出现 "When Testing"、"When Reviewing" 等新段落。是否需要预留 intent 映射配置（而非硬编码）？
4. **`gitnexus://repo/{name}/context` 的结构化**: 本设计只处理 `gitnexus://setup`（AGENTS.md），repo context 仍全量注入。是否需要在 v1 中一并结构化？
5. **RuleSource.Mcp 的优先级值**: 选择 15（Workspace=20 和 Global=10 之间）。如果 MCP 规则应低于 Global（用户全局偏好优先于项目分析），则应设为 `Mcp=8`。需确认优先级语义。
