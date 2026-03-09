# AICA 强制完成机制优化方案

## 核心理念

**信任Agent的自主判断，让attempt_completion工具发挥真正作用**

---

## 推荐方案：移除主动强制完成，仅保留安全边界

### 设计原则

1. **Agent自主决策** - 让LLM自己判断何时调用attempt_completion
2. **安全边界保护** - 仅在真正危险的情况下介入
3. **质量优先** - 不为了速度牺牲准确性

---

## 具体实施

### 第一步：移除主动强制完成逻辑

**修改文件**: `src/AICA.Core/Agent/AgentExecutor.cs`

**修改位置**: 第116-152行

**修改前**:
```csharp
// ── Force-completion: when enough tools have been called, remove tools to force text-only summary ──
int currentTokens = conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
double tokenUsageRatio = (double)currentTokens / conversationBudget;
bool forceCompletion = _taskState.HasEverUsedTools &&
    (_taskState.TotalToolCallCount >= 12 ||
     _taskState.Iteration >= 10 ||
     tokenUsageRatio > 0.70 ||
     _taskState.Iteration >= _maxIterations - 2);

if (forceCompletion)
{
    // 注入 [SUMMARIZE] 消息，移除工具...
}
```

**修改后**:
```csharp
// ── Safety boundaries: only intervene in truly dangerous situations ──
int currentTokens = conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
double tokenUsageRatio = (double)currentTokens / conversationBudget;

// Only force completion in extreme edge cases
bool forceCompletion = false;

// Edge case 1: Approaching absolute iteration limit (last 2 iterations)
if (_taskState.Iteration >= _maxIterations - 2)
{
    forceCompletion = true;
    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary: approaching max iterations ({_taskState.Iteration}/{_maxIterations})");
}

// Edge case 2: Context window critically full (>90%)
if (tokenUsageRatio > 0.90)
{
    forceCompletion = true;
    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary: context window critically full ({tokenUsageRatio:P0})");
}

if (forceCompletion)
{
    string reason = _taskState.Iteration >= _maxIterations - 2
        ? $"approaching iteration limit ({_taskState.Iteration}/{_maxIterations})"
        : $"context window critically full ({tokenUsageRatio:P0})";

    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary triggered: {reason}");

    bool alreadySummarizing = conversationHistory.Any(m =>
        m.Role == LLM.ChatRole.System && m.Content != null &&
        m.Content.Contains("[SAFETY_BOUNDARY]"));

    if (!alreadySummarizing)
    {
        yield return AgentStep.TextChunk($"\n\n⚠️ *安全边界触发 ({reason})，正在完成任务...*\n\n");

        conversationHistory.Add(ChatMessage.System(
            $"[SAFETY_BOUNDARY] You are approaching system limits ({reason}). " +
            "You MUST call `attempt_completion` NOW to summarize your findings. " +
            "Provide a comprehensive summary based on ALL information gathered so far."));
    }
}
```

**关键改变**:
1. ✅ 移除工具调用次数阈值（12次）
2. ✅ 移除迭代轮数阈值（10轮）
3. ✅ 提高Token阈值（70% → 90%）
4. ✅ 仅在真正危险时触发（最后2轮 或 Token>90%）
5. ✅ 即使触发，也保留attempt_completion工具（不传null）

---

### 第二步：提高最大迭代次数

**修改位置**: 第37行

**修改前**:
```csharp
int maxIterations = 25
```

**修改后**:
```csharp
int maxIterations = 50  // 增加到50轮，给复杂任务更多空间
```

**理由**:
- 综合测试场景可能需要20-30轮
- 50轮是合理的上限，足够完成复杂任务
- 安全边界在48轮时触发，留有缓冲

---

### 第三步：优化System Prompt

**修改文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

**在第94-107行的attempt_completion说明中增强**:

**修改前**:
```csharp
_builder.AppendLine("### MANDATORY: Task Completion");
_builder.AppendLine("- **YOU MUST CALL `attempt_completion` AFTER EVERY TASK.** This is NOT optional.");
```

**修改后**:
```csharp
_builder.AppendLine("### MANDATORY: Task Completion");
_builder.AppendLine("- **YOU MUST CALL `attempt_completion` AFTER EVERY TASK.** This is NOT optional.");
_builder.AppendLine("- **IMPORTANT: You have full autonomy to decide when a task is complete.** There are no artificial limits on tool calls or iterations.");
_builder.AppendLine("- **Call `attempt_completion` as soon as you have gathered sufficient information to answer the user's question completely.**");
_builder.AppendLine("- **Do NOT over-explore.** If you already have a good answer, call `attempt_completion` promptly. Quality over quantity.");
```

---

## 预期效果

### 优点

1. ✅ **Agent完全自主** - 可以充分探索，直到真正完成任务
2. ✅ **输出质量提升** - 不会被过早截断，结果更完整准确
3. ✅ **设计一致性** - attempt_completion真正成为"MUST USE"工具
4. ✅ **灵活性增强** - 简单任务快速完成，复杂任务有足够空间

### 安全保障

1. ✅ **仍有硬限制** - 最大50轮迭代，防止真正的无限循环
2. ✅ **Token保护** - 90%阈值防止上下文溢出
3. ✅ **安全边界** - 在最后2轮强制完成，确保有输出

### 风险控制

| 风险 | 概率 | 缓解措施 |
|------|------|----------|
| Agent不调用attempt_completion | 低 | System Prompt强调"MUST USE" |
| 无限循环 | 极低 | 50轮硬限制 + 安全边界 |
| Token溢出 | 低 | 90%阈值 + 自动截断 |
| API成本增加 | 中 | 可接受，质量优先 |

---

## 测试验证

### 验证场景

1. **简单任务** - 验证Agent能快速调用attempt_completion（5轮内）
2. **复杂任务** - 验证Agent有足够空间完成（20-30轮）
3. **极端任务** - 验证安全边界正常工作（接近50轮）

### 成功标准

- ✅ 所有任务都通过attempt_completion正常结束
- ✅ 没有任务被过早截断
- ✅ 安全边界在极端情况下正常触发
- ✅ 输出质量和完整性提升

---

## 实施步骤

1. 修改 AgentExecutor.cs（移除主动强制完成）
2. 提高 maxIterations 到 50
3. 优化 SystemPromptBuilder.cs（增强prompt）
4. 编译测试
5. 重新运行测试场景一验证
6. 继续测试场景二至十

---

## 总结

这个方案的核心是：

> **信任Agent，给它自主权，只在真正危险时介入**

这符合现代Agent系统的设计理念，也符合你对质量和准确性的追求。

**推荐立即实施！**
