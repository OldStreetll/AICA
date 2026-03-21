# AICA Agent 深度探索 — 学习笔记与 Q&A 记录

> 助教记录 | 开始日期: 2026-03-20
> 学习者与导师的对话记录，记录从 "工具调用者" 到 "自主 Agent" 的探索之旅

---

## 目录

- [Session 1: 项目全貌与架构认知](#session-1)

---

## Session 1: 项目全貌与架构认知 {#session-1}

*日期: 2026-03-20*

### 导师开场

导师对 AICA 项目做了整体概述，核心要点如下：

- AICA 是 Visual Studio 的 VSIX 扩展，AI 编程助手
- 当前状态：**44/100 分**，本质上是一个"单轮工具调用者"
- Phase 0-2.5 已完成（SK 框架、知识索引、工具 fallback、任务规划、去重）
- Phase 3 是迈向真正 Agent 的关键一步

### 第一课内容

**主题: Agent 的本质 — 循环与自主决策**

导师指出，理解 Agent 的关键在于认清其本质——不是"能调用工具"，而是一个**带有自主决策能力的循环**。

#### 核心观点

1. **Agent 的核心不是"能调用工具"，而是一个带有自主决策能力的循环。** 工具调用只是手段，真正的 Agent 能在循环中自主判断下一步该做什么。

2. **AICA 的 AgentExecutor 已有循环，但缺乏决策质量和自主性。** 循环的骨架在，但"大脑"还不够成熟。

3. **五个关键差距：**

   | 维度 | 当前状态 | 目标状态 |
   |------|---------|---------|
   | 决策 | 完全依赖 LLM 单次推理 | 有编排层做策略决策 |
   | 失败处理 | 重试或放弃 | 反思原因、换策略、换工具、换模型 |
   | 复杂任务 | 一个 LLM 从头做到尾 | 拆分给专家 Agent |
   | 验证 | 不验证 | 独立验证 Agent 检查输出 |
   | 上下文 | 32K 一刀切 | 热/温/冷三层管理 |

4. **比喻：** 现在像独自工作的实习生，目标是带团队的项目经理。

5. **技术债核心：** `AgentExecutor.cs`（约 2000 行）是核心也是最大技术债。

### Q&A 记录

#### Q0（导师提问）

> **问**：你对 AICA 目前的代码熟悉到什么程度？

**状态**：✅ 学员选择继续，导师按中等熟悉度展开。

#### Q1（导师提问）

> **问**：下一步想深入哪个方向？
>
> 选项：
> 1. 拆解 AgentExecutor
> 2. 多 Agent 协作
> 3. 自验证管线
> 4. 分层上下文

**状态**：⏳ 等待学员回答

#### Q2（学员提问）— 核心疑问：多 Agent 在 32K 限制下如何保证质量提升

> **问**：如果实现了拆分给专家 Agent 的目标，在上下文为 32K 不变的情况下，怎么保证 AICA 的输出质量就一定能好于之前呢？

**答**（导师）：

核心洞察：**不是同一个 32K**。每个子 Agent 有独立的 32K 上下文窗口。

**1. 上下文隔离**
- 单 Agent：32K 要装 System Prompt (~9K) + 知识上下文 (~2K) + 所有轮次的工具调用结果 → 7-8 轮后触发 condense，信息大量丢失
- 多 Agent：3 个子 Agent = 3 × 32K = 96K 有效上下文，每个窗口只装自己子任务的内容，不被无关信息污染
- 子 Agent 的 System Prompt 更小（只注入 3-4 个相关工具，省 ~5000 tokens）

**2. 专家化 System Prompt**
- 通用 prompt 指导所有事 vs 每个 Agent 的 prompt 专门优化其擅长领域
- 同样的 LLM，更聚焦的指令 → 更好的输出质量

**3. 交叉验证**
- 单 Agent 无人检查结果
- 多 Agent 中 ReviewAgent 用不同搜索策略验证结果完整性（Phase 3.5 CompletenessChecker）

**代价与权衡**：
- API 调用次数 ×N，token 成本更高
- 编排逻辑更复杂，调试更难
- 子 Agent 间信息传递可能丢失细节

**设计策略**：按复杂度分级（Simple→单Agent, Medium→双Agent, Complex→多Agent并行+验证），不是所有任务都用多 Agent。

**关键结论**：质量提升是结构性的（上下文隔离 + 专家化 + 交叉验证），但 Orchestrator 编排逻辑和 SharedContext 设计至关重要，拆分不合理反而可能更差。

**助教补充**：这个问题触及了多 Agent 架构的核心价值命题。学员展现了批判性思维——不盲目接受"多 Agent 就是好"的假设，而是追问其在约束条件下的实际效果。建议后续关注 SharedContext 的设计细节和 Orchestrator 的任务拆分策略。

**状态**：✅ 已解答

---

### 第二课：Orchestrator — 多 Agent 的大脑

*日期: 2026-03-20*

**核心观点**：多 Agent 架构中，最关键的不是子 Agent 有多强，而是谁来决定怎么拆任务、怎么分配、怎么汇总——这个角色就是 Orchestrator。

**AICA 现状分析**：
- 当前的"大脑"是 `TaskComplexityAnalyzer.cs`（~79行），基于正则和关键词的评分器
- 能力：判断任务复杂度（Simple/Medium/Complex）
- 局限：不能决定拆成几个子任务、交给谁、依赖关系、结果汇总策略

**演进路径**：
```
现在: TaskComplexityAnalyzer(正则) → score>=4触发Plan → AgentExecutor单循环
目标: TaskComplexityAnalyzer → Orchestrator编排决策
      ├── Simple → EditorAgent 直接执行
      ├── Medium → ArchitectAgent 规划 → EditorAgent 执行
      └── Complex → ArchitectAgent 规划 → 多子Agent并行 → ReviewAgent 验证
```

**两种编排流派**：
| 流派 | 代表 | 做法 | 优缺点 |
|------|------|------|--------|
| 规则编排 | LangGraph | 代码写死工作流图 | 可预测、便宜，但不灵活 |
| LLM 编排 | SK Magentic、OpenHands | LLM 当"经理"决定下一步 | 灵活，但多消耗 LLM 调用 |

AICA v4 选择 SK 编排模式（混合方案）：简单/中等用规则编排，复杂用 LLM 动态编排。

**与 Q2 的联系**：
1. 任务拆分质量决定子 Agent 能否在 32K 内高效工作（太粗→不够，太细→汇总丢失）
2. SharedContext 避免子 Agent 重复劳动
3. 结果汇总策略决定最终输出完整性

#### Q3（导师提问）

> **问**：接下来想深入哪个方向？(1) Orchestrator 具体实现 (2) AgentExecutor 拆分 (3) 端到端完整例子

**状态**：⏳ 等待学员回答

---

### 第三课：深入两个层面 — 注意力稀释与序列化瓶颈

*日期: 2026-03-20*

#### Q4（学员提问）— 专家化 Prompt 为什么能提升质量

> **问**：为什么分模块的 prompt 调用会带来质量提升？是因为 prompt 分模块后 agent 使用时更加精准，而不是在一大坨 prompt 里找应该用什么而提升了效率吗？

**答**（导师）：

学员的直觉对了一半。LLM 不是像人一样"翻找"prompt——它同时看到所有内容。问题不在于"找不到"，而在于三个更深层的原因：

**原因 1：注意力稀释（Lost in the Middle）**
- LLM 注意力机制对 prompt 开头和结尾关注度最高，中间部分容易被"忽视"
- AICA 现有 9500 tokens 的 System Prompt，中间的 CRITICAL 规则经常被违反
- 子 Agent 只有 ~3000 tokens 的 prompt，每条规则都在注意力"甜区"
- 这解释了为什么 AICA 到处写 "CRITICAL" 但效果有限——在对抗注意力稀释

**原因 2：指令冲突**
- 通用 prompt 里的规则互相矛盾（"立刻调工具" vs "直接文本回答"）
- LLM 不是不知道规则，而是不知道该优先遵循哪条
- 专家 Agent 的规则集不存在这种冲突

**原因 3：Token 预算的零和博弈**
- System Prompt 9.5K tokens 占总预算 30%，且每轮 API 调用都要重复发送
- 子 Agent 的 prompt 只有 ~3K，每轮多出 6.5K 给实际工作内容
- 这是物理空间问题，不是效率问题

**助教总结**：这个问题纠正了一个常见误解。关键认知升级：LLM 的 prompt 遵循问题不是"检索"问题，而是"注意力分配"问题。

**状态**：✅ 已解答

---

#### Q5（学员提问）— Agent 间通信为什么会丢失信息

> **问**：我了解到了 agent 之间通信会导致信息的丢失，为什么？核心原因在哪？

**答**（导师）：

核心原因有两个：

**1. 序列化瓶颈（最核心）**
- Agent 之间只能通过文本通信
- 内部丰富的"理解"（推理链、不确定性、上下文关联）必须压缩成有限长度文本
- 比喻：读了 300 页的书，要在便签纸上转述——信息损失不可避免
- 本质是信息论问题：高维内部表征被投影到低维文本输出

**具体丢失的信息类型**：
| 信息类型 | Agent 内部有 | 传递后 | 丢失的 |
|---------|------------|--------|--------|
| 文件完整内容 | 150 行代码 | 一句摘要 | 代码细节 |
| 隐含关系 | 接口依赖 | 只有类名 | 依赖关系 |
| 设计意图 | "测试用空实现" | 只有类名 | 语义理解 |
| 纠错经验 | 路径错→纠正 | 不传递 | 过程经验 |

**2. 上下文窗口的物理边界**
- 3 个子 Agent 各 ~7000 tokens 输出 = 21000 tokens
- Orchestrator 装不下原始数据，只能接收摘要

**缓解方案**：
- SharedContext（旁路通道，结构化数据不经过文本序列化）
- Phase 4 分层上下文（冷层存储原始数据，按需检索）

**导师总结**：两个问题指向同一个底层矛盾——LLM 上下文窗口有限，所有设计都是在这个约束下做权衡。多 Agent 不是消除约束，而是把"一个大窗口不够用"变成"多个小窗口之间的通信损耗"。

**助教补充**：学员连续追问层面 2 和层面 3 的深层原因，展示了从"知道结论"到"理解机制"的学习进阶。两个问题的共同底层——上下文窗口约束——是理解所有 Agent 架构设计决策的钥匙。

**状态**：✅ 已解答

---

---

### 第四课：Orchestrator 的具体实现 — 从现状到目标

*日期: 2026-03-20*

**学员请求**：深入了解 Orchestrator 的具体实现。

#### 五层递进分析

**第一层：现在的"Orchestrator"**
- AICA 没有独立的 Orchestrator，所有编排逻辑硬编码在 AgentExecutor.ExecuteAsync (~2000行)
- 6 个混在一起的职责：构建 Prompt、复杂度判断、Token 管理、LLM 调用、工具执行、完成判定
- 这就是 R1 说的 God Object 问题

**第二层：Orchestrator 的本质**
- 把"做什么"和"怎么做"分离
- 现在：AgentExecutor 既决策又执行（经理自己写代码）
- 目标：Orchestrator 只决策，子 Agent 执行（经理分配任务）

**第三层：SK 实现方案**
- 每个子 Agent 是独立的 ChatCompletionAgent，有自己的 Kernel、Prompt、工具集
- KernelFactory 已有 CreateLightweight（无工具 Kernel），扩展此模式为不同角色创建不同 Kernel
- 核心子 Agent：ResearchAgent（搜索工具）、CodeAgent（读写工具）、ReviewAgent（只读工具）
- Orchestrator 按复杂度路由：Simple→单 Agent, Medium→Sequential(Architect→Editor), Complex→并行+验证

**第四层：SK 五种编排模式在 AICA 的应用**
| SK 模式 | AICA 场景 | 使用时机 |
|---------|----------|---------|
| Sequential | Architect→Code→Review | Medium 任务 |
| Concurrent | 多 ResearchAgent 并行搜索 | 信息收集 |
| Handoff | 动态转给 SecurityAgent | 异常处理 |
| Magentic | Orchestrator 动态决策 | Phase 6 |
| Group Chat | Agent 间讨论 | Phase 6 |

**第五层：Orchestrator 和 AgentExecutor 的关系**
- 不是替换，是包装
- 每个子 Agent 内部还是 AgentExecutor 循环——但是瘦身版
- R1 拆分是 Phase 3 的前提：先拆 2000 行→~300 行核心循环，再加 Orchestrator 层

**关键决策：混合编排方案**
- Simple/Medium：纯规则编排（if/else），不消耗 LLM，快且可预测
- Complex：LLM 只负责"拆任务"，编排执行还是规则
- 原因：LLM 做编排可靠性不够（可能忘记调用子 Agent 或死循环传话）

**助教观察**：这节课从代码实际出发（AgentExecutor 源码 + KernelFactory），五层递进地构建了 Orchestrator 的完整认知。关键洞察是"不是替换而是包装"——保留 AgentExecutor 作为子 Agent 的执行引擎。

#### Q6（导师提问）

> **问**：想继续深入哪个方面？(1) 子 Agent 工具集隔离 (2) SharedContext 共享发现 (3) R1 拆分 AgentExecutor (4) 新疑问

**状态**：⏳ 等待学员回答

---

#### Q7（学员提问）— AgentExecutor 为什么不能既决策又执行？深层矛盾在哪？

> **问**：AICA 的 AgentExecutor 既然已经知道做什么和怎么做，无非只是混在了一起，那么为什么不能既决定做什么同时又亲自做？使用 Orchestrator 只不过可以将做什么和怎么做分离，无非是使用了多 agent 模式来提高质量罢了，这其中有什么深层的矛盾所在吗？

**答**（导师）：

**学员对的部分**：如果 LLM 足够强（无限上下文、完美注意力），单个 AgentExecutor 确实可以。分离不是目的，是对 LLM 局限性的妥协。

**核心矛盾**：**"看全局"和"看细节"争夺同一个认知资源（上下文窗口）。**

当 AgentExecutor 深入执行细节（读代码、分析搜索结果）时，决策者的全局视野被挤压。就像一个人专注写代码时难以同时思考整体架构——不是不会，是注意力是有限资源。

**三个具体矛盾点**：

1. **上下文内容对抗**：决策者需要全貌+摘要，执行者需要具体代码+搜索细节。两者争夺同一个 32K 窗口。执行细节越多，全局信息越少。

2. **Prompt 指令对抗**：战略指令（"创建计划"）和战术指令（"读文件前检查路径"）同时存在，LLM 不知道当前该优先听哪个。导致该反思策略时继续惯性调工具。

3. **错误恢复层次冲突**：战术错误（路径拼错→重试）和战略错误（搜索策略错误→换方法）需要不同层次的推理。挤在一个循环里效果不可靠。

**关键纠正**：学员说"无非是提高质量罢了"——这个"无非"要去掉。拆分不是可选优化，而是 LLM 有限认知带宽下的**必然选择**。类比：操作系统分离内核态/用户态不是为了代码好看，而是两种模式对资源的需求从根本上冲突。

**前提条件**：如果未来 LLM 有 100 万 token 上下文 + 完美注意力，单 Agent 可能就够了。当前技术约束下，分离是最务实方案。

**助教补充**：这个问题极有价值——学员在挑战"分离"的必要性，不满足于"多 Agent 就是好"的表面解释。导师的回答从"认知带宽"角度给出了根本性解释，将问题从工程层面提升到了认知科学层面。核心认知升级：分离不是设计偏好，而是对抗 LLM 注意力有限性的结构性解决方案。

**状态**：✅ 已解答

---

### 第五课：上下文窗口的真相 — 32K 从哪来，能不能改

*日期: 2026-03-21*

#### Q8（学员提问）— 上下文窗口能否通过硬编码增加？如何确定 LLM 的真实限制？

> **问**：目前 AICA 的上下文窗口是 32K，是否可以通过硬编码的形式主动添加来增加上下文窗口？怎么来判断是否达到了 LLM 的极限？怎么去准确地确定一个 LLM 的上下文窗口？

**答**（导师）：

**1. 32K 的来源**：

AICA 的 32K 不是 LLM 的限制，是 AICA 自己设的安全预算。

两个控制点：
- VSIX 层：`int tokenBudget = Math.Max(8000, options.MaxTokens * 8)` → 用户设 MaxTokens=4096 → 4096×8=32768
- AgentExecutor 默认值：`int maxTokenBudget = 32000`

**技术上改配置就能改大**，不需要改代码。

**2. 三个容易混淆的概念**：
| 概念 | 含义 | AICA 中 | 示例 |
|------|------|---------|------|
| 模型上下文窗口 | 训练时决定的硬限制 | 取决于用户连的模型 | MiniMax-M2.5: 1M |
| AICA Token Budget | AICA 自己设的软限制 | maxTokenBudget=32000 | 可改大 |
| MaxTokens | LLM 输出的最大 token 数 | 4096 | 与上下文窗口不同 |

**3. 确定 LLM 真实上下文窗口的四种方法**：
1. 看官方文档（最简单但有"水分"，声称支持≠真正好用）
2. 查 API `/v1/models` 端点获取 `context_length`（推荐）
3. 撞墙法（AICA 当前做法）：等 API 返回 `context_length_exceeded` 错误，砍半重试
4. 启动时探测：初始化时主动查询模型能力（推荐的改进方案）

**4. 改大了不代表变好的三个原因**：
1. **Token 估算不准**：AICA 用"字符数÷4"，对中文严重低估（中文 1 字符可能 1-2 个 token）
2. **注意力质量下降**：窗口越大，Lost in the Middle 越严重
3. **延迟和成本**：128K 窗口每轮发送 ~80K tokens vs 32K 窗口 ~20K tokens，成本 ×4

**5. 关键结论**：
正确的方向不是把窗口改大，而是 Phase 4 的分层上下文——用智能策略把有限窗口用好。比喻：不需要无限大的书桌，需要的是好的文件柜系统。

**助教补充**：学员从"能不能改代码"切入，导师通过源码分析揭示了三层概念的区别。关键认知：AICA 的 32K 是自设预算不是模型限制，但单纯改大并不能解决根本问题（注意力、成本、估算精度）。这与 Phase 4 分层上下文的设计动机完美衔接。

**状态**：✅ 已解答

---

---

### 第六课：AgentExecutor 拆解 — 解剖 2000 行的 God Object

*日期: 2026-03-21*

**学员请求**：深入了解 AgentExecutor 拆解方面的细节。

#### 全貌分析

导师通过实际代码标注了 AgentExecutor.cs 2181 行的职责分布：

| 职责 | 内容 | 行数 | 占比 |
|------|------|------|------|
| 初始化/状态 | Prompt 构建、复杂度、Plan、历史 | ~120 行 | 6% |
| Token 管理 | MicroCompact、截断、Condense、安全边界 | ~200 行 | 9% |
| LLM 通信 | 流式调用、重试、错误处理 | ~140 行 | 6% |
| 响应/工具处理 | Text Fallback、抑制、幻觉、去重、执行、恢复 | ~600 行 | 28% |
| 辅助方法 | BuildAutoCondenseSummary、签名、解析等 | ~1100 行 | 51% |

**超过一半的代码是辅助方法，主循环约 1060 行。**

#### 6 个目标组件

1. **TokenBudgetManager** (~350 行)：MicroCompact、截断、两级 Condense、安全边界、摘要构建
2. **ToolCallProcessor** (~450 行)：Text Fallback、去重签名、参数增强、工具执行、EditedFiles 追踪
3. **ResponseProcessor** (~300 行)：thinking 提取、文本抑制、幻觉检测、叙述停滞、质量过滤
4. **PlanManager** (~100 行)：Plan 注入、update_plan 处理、PlanAwareRecovery
5. **CompletionHandler** (~150 行)：attempt_completion、condense、强制完成、错误恢复
6. **LLMCaller** (~180 行)：StreamChatAsync 封装、重试逻辑、异常分类

#### 瘦身后的 AgentExecutor (~250 行)

拆分后主循环变成 5 个清晰阶段的管线：
1. Token 预算管理
2. 调用 LLM
3. 处理响应
4. 执行工具
5. 判定完成

#### 拆分顺序及原因

1. TokenBudgetManager（最独立，输入输出最干净）
2. ToolCallProcessor（依赖 TaskState 但不依赖其他）
3. ResponseProcessor（依赖已有的 ResponseQualityFilter）
4. PlanManager（最小组件）
5. CompletionHandler + LLMCaller（与主循环耦合最紧，最后提取）

**原则**：每步提取后确保测试通过——渐进式重构，不是推倒重来。

#### 与 Orchestrator 的关系

- 拆分是 Phase 3 的**前提**
- 不先拆 → Phase 3 需要 N 个 2000 行 God Object 并行运行
- 拆分后 → 子 Agent 可组合式配置（不同 ToolCallProcessor + 不同 TokenBudgetManager）
- ResearchAgent 用搜索专用组件 + 小预算；CodeAgent 用代码编辑组件 + 大预算

**助教观察**：本课是全系列最"工程化"的一课——从代码行号到职责分布到拆分顺序都有据可查。关键洞察是"拆分不是代码整洁的追求，而是 Phase 3 多 Agent 的硬性前提"。学员至此已经理解了从 God Object → 组件化 → 多 Agent 组合的完整演进路径。

**状态**：✅ 已讲解

---

### 第七课：三个端到端例子 — 从用户请求到最终输出

*日期: 2026-03-21*

**学员请求**：举一些端到端的完整例子。

#### 例子 1：Simple — "Logger 是什么？"

- 流程：TaskComplexityAnalyzer → Simple → 单 Agent 直接回答
- 知识上下文（TF-IDF Top-10）足够回答，0 次工具调用
- Phase 3 后：**完全不变**，Simple 不需要多 Agent

#### 例子 2：Medium — "重构 ReadFileTool 支持 async 流"

**现在（单 Agent）**：7 次 LLM 调用，Iter 6 触发 Condense 丢失修改细节，完成后未验证

**Phase 3 后（Architect + Editor）**：
- ArchitectAgent（独立 32K）：read_file × 2 + grep × 1 → 输出完整规划文档
- EditorAgent（独立 32K）：read_file × 1 + edit × 1 → 按规划精准修改
- 关键改善：Architect 有完整信息做高质量规划，Editor 不受搜索历史干扰，无 Condense 丢失

#### 例子 3：Complex — "分析 Logger 全部 Channel 子类"（TC-K01 真实用例）

**现在（单 Agent）**：9 轮，Condense 后只记住 10/20 个 Channel，覆盖率 50%

**Phase 3 后（完整多 Agent）**：
1. ArchitectAgent：分析任务，输出 4 种搜索策略
2. 3 个 ResearchAgent **并行**执行：
   - Agent-1：grep Foundation/ → 8 个 Channel
   - Agent-2：grep Net/ → 5 个 Channel
   - Agent-3：find_by_name + 补充 grep → 7 个额外 Channel
   - SharedContext 避免重复读文件
3. ReviewAgent：交叉验证，补充发现 2 个（平台特定/废弃）
4. 最终结果：22/22，覆盖率 100%

**核心对比表**：
| 维度 | 现在 | Phase 3 |
|------|------|---------|
| Channel 发现数 | 10/20 (50%) | 22/22 (100%) |
| Condense 次数 | 1（丢信息）| 0 |
| 有效上下文 | 32K | 4 × 32K |
| 总耗时 | ~45s（串行）| ~20s（并行）|
| 交叉验证 | 无 | ReviewAgent |

#### 核心规律

1. **Simple 不拆**——拆了反而多花钱、多延迟，没收益
2. **Medium 拆两层**——Architect 规划质量高，Editor 执行精准
3. **Complex 并行拆**——唯一能突破单窗口限制的方案 + 交叉验证保证完整性

**助教总结**：三个例子完美展示了"按复杂度分级"的设计哲学。学员至此已理解：不是"所有任务都用多 Agent"，而是"只在必要时才拆分"。Complex 例子用 TC-K01 真实数据（50% → 100% 覆盖率）证明了多 Agent 的结构性优势。

**状态**：✅ 已讲解

*本文档由助教 agent 持续维护，记录学习过程中的所有疑问与解答。*
