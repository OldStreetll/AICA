# AICA 测试问题优化报告

## 优化时间
2026-03-09

## 优化背景

基于测试场景1-4的验证结果，发现了两个需要优化的问题：
1. **场景二：行号偏差问题** - 使用offset/limit读取文件时，Agent报告的行号与实际行号有偏差
2. **场景四：偏离用户指令问题** - 当发现用户指令与实际情况不符时，Agent未先询问用户就自行决定修改其他文件

---

## 优化方案

### 优化1: 修复行号偏差问题

**问题分析**:
- 当使用`read_file`工具的`offset`和`limit`参数时，返回的内容不包含行号信息
- Agent需要手动计算行号，容易出现偏差（6-11行的偏差）
- 影响：Agent报告的代码位置不准确，虽然不影响逻辑理解，但降低了精准度

**解决方案**:
在`ReadFileTool.cs`中，当使用offset/limit参数时，自动为返回的内容添加行号：

**修改文件**: `src/AICA.Core/Tools/ReadFileTool.cs`

**修改位置**: 第96-119行

**修改内容**:
```csharp
if (offset.HasValue || limit.HasValue)
{
    var lines = content.Split('\n');
    var startIndex = (offset ?? 1) - 1;
    var count = limit ?? (lines.Length - startIndex);

    if (startIndex < 0) startIndex = 0;
    if (startIndex >= lines.Length) return ToolResult.Ok("(empty - offset beyond file length)");

    count = System.Math.Min(count, lines.Length - startIndex);

    // Add line numbers to help Agent reference code accurately
    var numberedLines = new System.Text.StringBuilder();
    numberedLines.AppendLine($"[Showing lines {startIndex + 1}-{startIndex + count} of {lines.Length}]");
    numberedLines.AppendLine();
    for (int i = 0; i < count; i++)
    {
        var lineNumber = startIndex + i + 1;
        numberedLines.AppendLine($"{lineNumber,6}: {lines[startIndex + i]}");
    }
    content = numberedLines.ToString();
}
```

**输出格式示例**:
```
[Showing lines 96-120 of 325]

    96: if (offset.HasValue || limit.HasValue)
    97: {
    98:     var lines = content.Split('\n');
    ...
   120: }
```

**预期效果**:
- ✅ Agent可以直接看到准确的行号
- ✅ 无需手动计算offset
- ✅ 报告代码位置时100%准确

---

### 优化2: 增强指令冲突处理机制

**问题分析**:
- 场景四中，用户要求修改ReadFileTool和WriteFileTool
- Agent发现这两个文件已经符合要求，无需修改
- Agent自行决定修改AskFollowupQuestionTool，未先询问用户
- 虽然技术上正确，但偏离了用户的明确指令

**解决方案**:
在System Prompt中增加"处理指令冲突"的明确指导

**修改文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

**修改位置**: 第112-126行（新增章节）

**新增内容**:
```
### CRITICAL: Handling Instruction Conflicts
- **When you discover that the user's instruction conflicts with the actual situation:**
  - Example: User asks to modify FileA and FileB, but you discover they are already in the desired state
  - Example: User asks to fix a bug, but you find the bug doesn't exist or is already fixed
  - Example: User asks to implement feature X, but you find it's already implemented
- **DO NOT proceed with modifications without user confirmation. Instead:**
  1. Clearly report your findings: 'I found that FileA and FileB already use the desired pattern'
  2. Use `ask_followup_question` to ask the user what they want to do:
     - Provide clear options (e.g., 'Keep as is', 'Modify anyway', 'Modify other files')
     - Explain the current state and why you're asking
  3. Wait for user response before proceeding
- **DO NOT make assumptions and modify different files than requested without asking first.**
- **DO NOT say 'I'll modify FileC instead' without user permission.**
- **Respect user's explicit instructions unless there's a clear technical reason not to (e.g., safety, file doesn't exist).**
```

**预期效果**:
- ✅ Agent发现指令冲突时，先报告发现
- ✅ 使用`ask_followup_question`询问用户意图
- ✅ 等待用户确认后再执行
- ✅ 不会自行偏离用户指令

---

### 优化3: 增强read_file工具使用提示

**问题分析**:
- Agent可能不知道使用offset/limit时会返回带行号的内容
- 需要在System Prompt中明确说明

**解决方案**:
在"Tool Usage Tips"章节中增强`read_file`工具的说明

**修改文件**: `src/AICA.Core/Prompt/SystemPromptBuilder.cs`

**修改位置**: 第148-150行（新增）

**新增内容**:
```
- `read_file`: Supports reading large files in chunks using `offset` and `limit` parameters.
  - **IMPORTANT: When using offset/limit, the tool returns content with line numbers (e.g., '   123: code here'). Use these line numbers when referencing code locations in your analysis.**
  - **When reporting code locations, always use the actual line numbers shown in the tool output, NOT calculated offsets.**
```

**预期效果**:
- ✅ Agent知道使用offset/limit会返回带行号的内容
- ✅ Agent知道应该使用工具输出中的行号，而非计算offset
- ✅ 提高代码位置引用的准确性

---

## 修改文件清单

| 文件 | 修改类型 | 修改行数 | 说明 |
|------|----------|----------|------|
| `src/AICA.Core/Tools/ReadFileTool.cs` | 功能增强 | +12, -1 | 添加行号显示功能 |
| `src/AICA.Core/Prompt/SystemPromptBuilder.cs` | Prompt优化 | +20 | 新增指令冲突处理指导 + read_file使用提示 |

**总计**: 2个文件，+32行，-1行

---

## 验证计划

### 验证方法

**验证优化1（行号偏差）**:
1. 使用`read_file`工具读取文件，指定offset和limit参数
2. 检查返回内容是否包含行号
3. 验证行号格式是否正确（例如：`   123: code here`）
4. 在后续测试中观察Agent报告的行号是否准确

**验证优化2（指令冲突）**:
1. 设计一个测试场景：要求Agent修改已经符合要求的文件
2. 观察Agent是否先报告发现
3. 观察Agent是否使用`ask_followup_question`询问用户
4. 观察Agent是否等待用户确认后再执行

**验证优化3（工具提示）**:
1. 在System Prompt生成后，检查是否包含新增的read_file使用提示
2. 在后续测试中观察Agent是否正确使用行号信息

---

## 预期效果

### 优化1效果
- **场景二类型任务**: 行号准确率从95%提升到100%
- **代码位置引用**: 完全准确，无偏差
- **用户体验**: 用户可以直接根据Agent报告的行号定位代码

### 优化2效果
- **场景四类型任务**: 不再自行偏离用户指令
- **用户沟通**: Agent会主动询问用户意图
- **任务执行**: 更符合用户预期

### 优化3效果
- **Agent行为**: 正确理解read_file工具的输出格式
- **代码引用**: 使用工具输出中的行号，而非计算offset

---

## 风险评估

### 优化1风险
**风险**: 行号格式可能影响某些解析逻辑
**概率**: 低
**缓解**:
- 仅在使用offset/limit时添加行号
- 完整读取文件时保持原格式不变
- 行号格式清晰，易于识别

### 优化2风险
**风险**: Agent可能过度询问用户，降低效率
**概率**: 低
**缓解**:
- 仅在"指令与实际冲突"时询问
- 正常情况下Agent仍然自主决策
- 明确了询问的触发条件

### 优化3风险
**风险**: 无
**概率**: 无
**说明**: 仅增加提示，不改变工具行为

---

## 后续工作

### 立即行动
1. ✅ 代码修改完成
2. ⏳ 编译验证
3. ⏳ 重新测试场景二（验证行号准确性）
4. ⏳ 设计场景验证优化2（指令冲突处理）
5. ⏳ 继续场景五至十的测试

### 长期监控
1. 收集Agent使用行号的准确性数据
2. 收集Agent询问用户的频率和合理性
3. 根据反馈持续优化System Prompt

---

## 总结

### 优化成果
- ✅ 修复了行号偏差问题（场景二）
- ✅ 增强了指令冲突处理机制（场景四）
- ✅ 改进了工具使用提示

### 优化特点
- **精准定位**: 针对测试中发现的具体问题
- **最小侵入**: 仅修改必要的代码和Prompt
- **向后兼容**: 不影响现有功能
- **易于验证**: 可以通过简单测试验证效果

### 预期影响
- **场景二类型任务**: 行号准确率100%
- **场景四类型任务**: 不再偏离用户指令
- **整体质量**: 从96.3%提升到预计98%+

---

**优化人员**: Claude Opus 4.6
**优化日期**: 2026-03-09
**状态**: ✅ 代码修改完成，待编译验证
**下一步**: 编译项目并重新测试场景二和场景四
