# AICA 产品战略复盘总结

> 日期: 2026-03-22
> 参与者: AICA 唯一开发者 + Claude Code（资深 Agent 开发工程师角色）
> 会话时长: 约 3 小时
> 产出物: 能力评分体系、V4 文档审计报告、产品战略方向、C/C++ 专家化技术方案

---

## 一、会话脉络

本次会话经历了 **5 个阶段**，从项目状态摸底逐步深入到产品战略定位：

```
阶段 1: 项目状态摸底
  ↓
阶段 2: 能力量化评估
  ↓
阶段 3: V4 文档专业审计
  ↓
阶段 4: 产品战略定位
  ↓
阶段 5: C/C++ 专家化技术方案
```

---

## 二、阶段 1：项目状态摸底

### 做了什么

1. 从 GitHub clone 最新 AICA 代码覆盖本地目录
2. 阅读 `doc/agentref/` 下全部文档（AgentEvolutionPlan.md、AgentEvolutionPlan_v4.md、FutureImprovements.md、TechnicalDebtCleanup.md、TestResults_Round2.md、ManualTestPlan_R1-R9.md、e2e-test-report.md）
3. 查看 Git 最近 5 次提交，梳理项目变化
4. 对比 TechnicalDebtCleanup.md 版本（1b9a2ac）与最新版本的差异
5. 使用 `buildinhome.ps1` 编译项目（解决了 NETSDK1004 缺失 `project.assets.json` 问题，加 `-Restore` 标志修复）

### 关键发现

- AICA 经历了 R1-R9 大规模重构，AgentExecutor 从 2181 行拆分到 1222 行 + 8 个模块
- Token budget 从 32K 升级到 177K
- 知识索引系统（SymbolParser + TF-IDF）能在 9.3 秒内索引 POCO 项目的 28631 个符号
- E2E 测试 3/3 PASS，Round 2 测试 5/6 PASS
- 项目已从"原型"进入"可用但需打磨"阶段

---

## 三、阶段 2：能力量化评估

### 评分体系

建立了 **10 维度评分框架**，每维度 10 分，总分 100 分：

| 维度 | 得分 | 评分依据 |
|------|------|----------|
| 工具调用能力 | 7/10 | 9 个工具完善，有确认机制和安全检查，但缺乏工具链编排 |
| 任务规划 | 5/10 | 有 PlanManager 但仅限 Complex 任务，无子任务分解 |
| 上下文管理 | 6/10 | 177K token budget + condense 机制，但 condense 有信息丢失风险（TC-13） |
| 知识检索 | 5/10 | TF-IDF + SymbolParser 可用，但 regex 解析精度有限，无语义理解 |
| 自主性 | 5/10 | 最多 50 次迭代循环，有 DynamicToolSelector，但无自我纠错 |
| 记忆系统 | 2/10 | 仅有会话内 condense，无跨会话记忆 |
| 主动性 | 4/10 | ResponseQualityFilter 过滤废话，但不会主动发现问题 |
| 多模型协作 | 3/10 | 单模型串行，无多 Agent 编排 |
| 错误恢复 | 5/10 | 2 次重试 + LLMException 分类，但无根因分析 |
| 用户体验 | 6/10 | 流式输出 + diff 预览 + 确认机制，但 UI 基于 IE Trident 引擎 |
| **总分** | **48/100** | |

### 评价结论

AICA 处于"工具调用者向自主 Agent 过渡"的阶段。工具层做得扎实，但规划、记忆、多模型协作等高级 Agent 能力仍在早期。

---

## 四、阶段 3：V4 文档专业审计

### 审计对象

`AgentEvolutionPlan_v4.md`（1208 行），涵盖 R1-R9 重构 + Phase 3-6 路线图。

### 发现的 5 个系统性问题

| # | 问题 | 严重性 | 影响 |
|---|------|--------|------|
| 1 | **无用户画像** | 高 | 不知道为谁做、解决什么问题，所有设计决策缺乏锚点 |
| 2 | **无可量化指标** | 高 | "提升质量"无法验证，缺少基线和目标数值 |
| 3 | **模型依赖风险** | 高 | 全部架构假设 MiniMax-M2.5 能力，无模型能力边界评估 |
| 4 | **SK 版本锁定** | 高 | SK 1.54.0 锁定阻塞 Phase 3/4 的 Agent Framework 依赖 |
| 5 | **时间线乐观** | 中 | Phase 3-6 缺乏并行依赖分析，未考虑单人开发瓶颈 |

### 具体缺陷摘要

- R1-R9 重构的代码质量高，但文档中缺少回滚方案和性能基线
- Phase 3.5 自验证管线依赖 SK Agent Framework 的 multi-turn 能力，与 1.54.0 版本冲突
- Phase 4 分层上下文（热/温/冷三层）设计精巧但实现复杂度被低估
- Phase 5 Memory Bank 缺乏存储格式和一致性模型的具体设计
- SharedContext 在多 Agent 间的序列化瓶颈被忽视

---

## 五、阶段 4：产品战略定位

### 关键转折点

开发者明确拒绝了"修修补补"的战术路线，提出要从**架构层面和产品层面**系统性思考 AICA 的方向。

> "漏洞的填补是暂时的，没有从架构层面和产品层面系统性的、全局的考虑 AICA 的产品方向。"

### 信息收集（Q&A 环节）

通过 3 轮共 13 个问题，收集到以下关键信息：

**产品环境：**
- 公司：武汉华中数控（CNC/工业自动化），代码涉密
- 用户：~80 名 C/C++ 开发者，6 人正在试用旧版本（f43df8a）
- 平台：VS2022，无法使用 Cursor/Claude Code 等外部工具
- LLM：MiniMax-M2.5（220B 参数，私有部署，多模态），唯一可用模型
- 开发团队：仅 1 人，全程 vibe coding

**并发性能数据：**（从 `性能测试结果.xlsx` 提取）

| 并发数 | 输入 tokens | TTFT (秒) |
|--------|-------------|-----------|
| 20 | 10K | 23 |
| 50 | 10K | 102 |
| 20 | 20K | 52 |
| 50 | 20K | 207 |

**试用反馈痛点：**

| 痛点 | 状态 | 说明 |
|------|------|------|
| 1. 无输出反馈（仅工具调用） | 已修复 | 新版本几乎不再出现 |
| 2. 输出不稳定 | 极偶发 | 新版本已基本稳定 |
| 3. 网络连接不稳定 | 极偶发 | 重新发送即可恢复 |
| 4. 复杂指令无法处理 | **仍显著** | 任务拆解和幻觉问题 |
| 5. 超出 agent 循环上限 | 已修复 | MaxAgentIterations 已调至 50 |

### 三面结构性墙壁

分析了 AICA 与 Cursor 级别工具之间的差距：

| 维度 | AICA 现状 | Cursor/Claude Code | 差距本质 |
|------|-----------|-------------------|----------|
| **模型能力** | MiniMax-M2.5 (220B) | Claude 3.5/GPT-4o | 模型本身决定了代码理解和生成的上限 |
| **平台** | VS2022 VSIX + IE Trident | Electron + 现代 Web | UI 渲染能力、Extension API 丰富度 |
| **并发** | 50 并发时 TTFT 207s | 云端弹性扩容 | 私有部署的物理限制 |

### 核心战略决策

**重新定位：** 从"企业版 Cursor"转为"**企业内部 C/C++ AI 编程助手**"。

不追求通用 AI coding agent 的全面能力，而是在**一个垂直领域**（C/C++ + CNC 工业软件）做到极致。

**理由：**
1. 公司 90% 代码是 C/C++，不需要通用能力
2. C/C++ 编码规范（Qt + MISRA C）是明确可编码的规则，LLM 可以遵循
3. CNC 领域知识（通道、轴、PLC、G-code、HNC API）是独特优势，Cursor 不具备
4. 在涉密环境中，AICA 是**唯一选择**，不存在外部竞品

### 四阶段路线图

| 阶段 | 名称 | 目标 | 时间线 |
|------|------|------|--------|
| 第 1 阶段 | 稳定交付 | 6 人试用稳定可用，3 月底交付 | 2026-03 |
| 第 2 阶段 | C/C++ 专家化 | 生成代码自动遵循公司规范，4 场景专业化 | 2026-04~05 |
| 第 3 阶段 | 规模化到 80 人 | 并发优化、请求队列、token 压缩 | 2026-06~08 |
| 第 4 阶段 | 架构升级 | 多 Agent、Memory Bank、自验证管线 | 2026-09+ |

### 用户确认的方向

- "C/C++ 专家化方向完全符合公司需求，极其认同"
- MiniMax-M2.5 是多模态模型，计划拓展图片输入识别 UI 问题
- 上级希望 AICA 持续迭代进化
- 暂不需要建立模型测评集
- 构建系统和测试框架信息待用户调研后反馈

### 一个重要纠正

我在分析中假设 "Qwen3-Coder-Next 比 MiniMax-M2.5 强"，被开发者质疑——性能测试只测了吞吐量/延迟，不代表输出质量。这是一个正确的挑战。教训：**不要在缺乏证据时做模型能力的主观排序**。

---

## 六、阶段 5：C/C++ 专家化技术方案

### 编码规范分析

完整阅读了公司 Qt-C++ 编码规范（`软件开发部Qt-C++语言编程规范(1).doc`，33740 字符），提取了 7 个类别约 100 条可执行规则。

### 技术方案（详见 CppSpecialization_TechPlan.md）

| Phase | 名称 | 改代码? | 涉及文件 | 核心改动 |
|-------|------|---------|---------|---------|
| Phase 1 | 规范注入 | 否 | 5 个 .aica-rules/*.md | 编码规范直接注入 System Prompt |
| Phase 2 | 场景 Prompt 升级 | 是 | 5 个 .cs + 1 个新 .cs | 语言检测 + 右键命令专业化 + CNC 领域上下文 |
| Phase 3 | 知识引擎强化 | 是 | 2 个 .cs | Qt signals/slots 解析 + HNC 枚举索引 + C++ 语义加权 |

**总改动量：** ~350 行代码 + 5 个规范文件，所有改动对非 C/C++ 项目零影响。

---

## 七、关键决策记录

| # | 决策 | 理由 | 决策者 |
|---|------|------|--------|
| D1 | AICA 定位为 "C/C++ 专家助手" 而非 "通用 AI agent" | 公司 90% C/C++ 代码，涉密环境无外部竞品 | 开发者确认 |
| D2 | 不建立模型测评集 | MiniMax-M2.5 是唯一可用模型，测评集当前无法驱动模型切换决策 | 开发者决定 |
| D3 | 利用 .aica-rules 而非硬编码注入规范 | 零代码修改、可随项目分发、用户可自定义 | 技术分析 |
| D4 | Phase 2 使用文件扩展名检测语言 | 轻量、准确、无需读文件内容 | 技术分析 |
| D5 | 3 月底交付标准定为 "辅助开发可用" | 试用期间核心场景：编写代码、开发建议、测试用例、代码重构 | 开发者确认 |

---

## 八、待办事项

| # | 事项 | 负责 | 依赖 | 状态 |
|---|------|------|------|------|
| T1 | 执行 Phase 1：创建 .aica-rules/cpp-*.md 规范文件 | Claude Code | 无 | 待启动 |
| T2 | 调研公司 C/C++ 项目的构建系统（CMake? MSBuild?） | 开发者 | 需接触公司代码环境 | 待调研 |
| T3 | 调研公司 C/C++ 项目使用的测试框架 | 开发者 | 需接触公司代码环境 | 待调研 |
| T4 | 执行 Phase 2：实现 ProjectLanguageDetector + Prompt 升级 | Claude Code | T2/T3 信息可优化但非阻塞 |待启动 |
| T5 | 执行 Phase 3：SymbolParser + KnowledgeContextProvider 增强 | Claude Code | Phase 2 完成后 | 待启动 |
| T6 | 将新版 AICA 部署给 6 名试用者更新 | 开发者 | Phase 1+2 完成后 | 待启动 |
| T7 | 多模态图片输入功能设计 | 后续迭代 | Phase 2 完成后 | 未开始 |

---

## 九、本次会话的方法论反思

### 做对了什么

1. **先摸底再评价：** 完整阅读所有文档 + 代码 + Git 记录后才做评分，避免主观臆断
2. **量化评估：** 10 维度评分框架提供了客观的能力画像
3. **直面结构性问题：** 三面墙壁的分析诚实地指出了 AICA 与 Cursor 的差距和不可逾越性
4. **响应用户方向纠偏：** 从修修补补转向系统性产品思考
5. **利用已有架构：** Phase 1 零代码修改就能产生效果，因为发现了 .aica-rules 管线

### 需要改进的

1. **初始方向偏差：** 最初给出的 8 天修 bug 方案被用户否定为"修修补补"，说明我低估了用户的产品思维层次
2. **模型能力假设：** 在没有证据的情况下假设 Qwen3-Coder-Next > MiniMax-M2.5，被正确质疑
3. **痛点严重性误判：** 初始将 5 个痛点都当作严重问题，但用户反馈其中 4 个已解决或极偶发
4. **应更早进入战略讨论：** 花了较多时间在战术层面，用户需要的是产品方向和架构层面的思考

### 经验提炼

> **对于一个涉密环境中的唯一 AI 工具，竞争力不来自追赶通用 AI agent 的全面能力，而来自在特定领域的不可替代性。**

---

## 十、行动路线：接下来一步步该做什么

### 全局视角

```
现在                                                                  未来
 │                                                                     │
 ▼                                                                     ▼
[已完成的地基]          [近期：稳定+专业化]           [中期：规模化]          [远期：智能化]
 Phase 0-2.5             Phase 1-3 完善                规模化到 80 人          架构升级
 R1-R9 重构              C/C++ 专家化                  并发/队列优化           多 Agent
 177K token              Memory Bank                   token 压缩             自验证
 9 工具 + 知识索引        已有问题修复                   监控体系               事件驱动

 ──── 已完成 ────   ──── 第 1 优先级 ────    ──── 第 2 优先级 ────   ──── 第 3 优先级 ────
```

### 第 1 步：修复已完成工作中的遗留问题（预计 2-3 天）

这些是已有代码中的 bug 和安全问题，不修复会影响后续所有工作的基础稳定性。

| 序号 | 问题 | 文件 | 紧急度 | 做什么 |
|------|------|------|--------|--------|
| 1.1 | SEC-01: R5 dedup 不区分安全拒绝和临时失败 | `ToolCallProcessor.cs` | 🔴 必须 | 判断 error message 是否包含 "Access denied" / "path traversal"，若是则保留 dedup 签名不允许重试 |
| 1.2 | BF-02: 推理泄漏 300 字符阈值问题 | `ResponseQualityFilter.cs` | 🟡 建议 | 方案 A: 仅检查前 200 字符是否匹配推理模式；若匹配则按段落分割，丢弃推理段 |
| 1.3 | BF-06: 代码解释被误分类为 Complex | `TaskComplexityAnalyzer.cs` | 🟡 建议 | 已有 Fix 5 但需确认正则覆盖完整，增加 "解释这段代码" 的 Medium 上限 |

**验证方式：** 跑一遍 ManualTestPlan_R1-R9.md 中对应的测试项（BF-02、BF-06、SEC-01 相关）

### 第 2 步：C/C++ 专家化 Phase 1 — 创建编码规范文件（预计 1 天）

详见 `CppSpecialization_TechPlan.md` Phase 1。

| 序号 | 任务 | 产出 |
|------|------|------|
| 2.1 | 在用户的 C/C++ 项目 `.aica-rules/` 下创建 5 个规范文件 | `cpp-code-style.md`, `cpp-reliability.md`, `cpp-file-io.md`, `cpp-qt-specific.md`, `cpp-comment-template.md` |
| 2.2 | 用 AICA 在 C/C++ 项目中生成一个新类进行验证 | 检查花括号风格、m_ 前缀、doxygen 注释、Bit32 类型 |
| 2.3 | 将规范文件打包为模板，便于分发给 80 人 | 可选：AICA 增加 "初始化 C/C++ 规范" 命令 |

**这一步零代码修改，可以立即做。**

### 第 3 步：C/C++ 专家化 Phase 2 — 场景 Prompt 升级（预计 2-3 天）

详见 `CppSpecialization_TechPlan.md` Phase 2。

| 序号 | 任务 | 涉及文件 |
|------|------|----------|
| 3.1 | 新建 `ProjectLanguageDetector.cs` | `Agent/ProjectLanguageDetector.cs`（新文件） |
| 3.2 | `SystemPromptBuilder` 增加 `AddCppSpecialization()` | `Prompt/SystemPromptBuilder.cs` |
| 3.3 | `AgentExecutor` 中调用语言检测和注入 | `Agent/AgentExecutor.cs` |
| 3.4 | 三个右键命令改为语言感知 | `Commands/RefactorCommand.cs`, `GenerateTestCommand.cs`, `ExplainCodeCommand.cs` |
| 3.5 | `TaskComplexityAnalyzer` 增加 C++ 关键词 | `Agent/TaskComplexityAnalyzer.cs` |

**验证方式：** 在 C/C++ 项目中测试三个右键菜单 + 自由对话，对比改动前后的输出质量。

### 第 4 步：Memory Bank 实现（预计 3-5 天）

> 注意：这一步**从 V4 文档的 Phase 5 前移**。理由见本文档第十一节的讨论。

| 序号 | 任务 | 说明 |
|------|------|------|
| 4.1 | 设计 Memory Bank 存储结构 | 在项目 `/.aica/memory/` 下存储 Markdown 文件 |
| 4.2 | 实现会话结束时自动提取关键信息 | 从 condense summary 中提取 project-brief、active-context、corrections |
| 4.3 | 实现新会话启动时自动注入 Memory | 在 SystemPromptBuilder 中增加 `AddMemoryContext()` |
| 4.4 | 验证跨会话记忆 | 会话 1 分析 POCO 项目结构，会话 2 验证 AICA 是否记得 "POCO 使用 CppUnit" |

**不依赖 SK 升级，不依赖多 Agent，当前架构即可实现。**

### 第 5 步：Condense 结构化增强（预计 2-3 天）

| 序号 | 任务 | 说明 |
|------|------|------|
| 5.1 | 将 condense summary 从自由文本改为结构化模板 | 7 段式：任务目标 / 已读文件 / 已搜索 / 关键发现 / 已修改 / 遇到错误 / 当前进度 |
| 5.2 | 增加 "永不压缩" 的保护字段 | 用户原始请求、Plan 状态、修改的文件列表 |
| 5.3 | 验证长对话稳定性 | 设计 30+ 轮工具调用测试，触发 condense 后检查 LLM 是否仍理解任务 |

### 第 6 步：C/C++ 专家化 Phase 3 — 知识引擎强化（预计 3-5 天）

详见 `CppSpecialization_TechPlan.md` Phase 3。

| 序号 | 任务 | 涉及文件 |
|------|------|----------|
| 6.1 | SymbolParser 增加 Qt signals/slots 解析 | `Knowledge/SymbolParser.cs` |
| 6.2 | SymbolParser 增加 HNC_ 枚举值提取 | `Knowledge/SymbolParser.cs` |
| 6.3 | SymbolParser 增加类成员函数提取 | `Knowledge/SymbolParser.cs` |
| 6.4 | KnowledgeContextProvider 增加 C++ 停用词 + 语义加权 | `Knowledge/KnowledgeContextProvider.cs` |

### 第 7 步：部署新版本给试用者 + 收集反馈（预计 1-2 天）

| 序号 | 任务 | 说明 |
|------|------|------|
| 7.1 | 编译新版 VSIX | `buildinhome.ps1 -Restore -Build` |
| 7.2 | 部署给 6 名试用者 | 替换旧版本 f43df8a |
| 7.3 | 收集结构化反馈 | 聚焦：代码生成是否遵循规范、右键功能体验、长对话稳定性 |
| 7.4 | 根据反馈调整 | 准备 1-2 天的修复窗口 |

### 第 8 步：规模化准备（第 2 优先级，预计 2-4 周）

| 序号 | 任务 | 说明 |
|------|------|------|
| 8.1 | 请求队列 + 排队提示 | 当并发超过阈值时，排队而非超时报错 |
| 8.2 | Token 压缩策略 | System Prompt 分级（Simple 省 40%），减少每次请求的 input tokens |
| 8.3 | 请求优先级 | 右键菜单命令（短请求）优先，自由对话（长请求）排后 |
| 8.4 | 使用监控 | 记录每次请求的 token 用量、响应时间、成功率 |

### 第 9 步：架构升级评估（第 3 优先级，视模型/SK 发展而定）

| 序号 | 任务 | 前置条件 |
|------|------|----------|
| 9.1 | 评估 SK 升级路径（1.54 → 1.71+） | 等 SK 稳定 + .NET Standard 2.0 兼容确认 |
| 9.2 | 评估 MiniMax-M2.5 多 Agent 编排能力 | 需要实测：同一模型扮演 Architect + Editor 的效果 |
| 9.3 | 如模型升级可用，评估新模型在 CNC 代码上的表现 | 等公司部署决策 |
| 9.4 | 根据前三项结果决定 Phase 3（多 Agent）是否启动 | 风险评估完成后 |

### 时间线概览

```
3月22-24日    第 1 步: 修复遗留问题 (SEC-01, BF-02, BF-06)
3月25日       第 2 步: C/C++ 规范文件 (零代码)
3月26-28日    第 3 步: 场景 Prompt 升级 (350 行代码)
3月29-31日    ──── 3月底交付节点 ──── 部署给试用者
4月第1-2周    第 4 步: Memory Bank + 第 5 步: Condense 增强
4月第3-4周    第 6 步: 知识引擎强化 + 第 7 步: 二次反馈
5月           第 8 步: 规模化准备 (队列/压缩/监控)
6月+          第 9 步: 架构升级评估
```

### 你个人需要做的事（非编码）

| 任务 | 优先级 | 说明 |
|------|--------|------|
| 调研公司 C/C++ 项目的构建系统 | 🟡 中 | CMake? MSBuild? 其他？影响 Phase 2 测试生成命令的 prompt |
| 调研公司使用的 C/C++ 测试框架 | 🟡 中 | Google Test? CppUnit? 自研？影响 GenerateTestCommand 的 prompt |
| 把新版 AICA 部署给试用者 | 🔴 高 | 第 3 步完成后立即部署 |
| 收集试用反馈（结构化问卷） | 🔴 高 | 聚焦 4 个场景的实际使用体验 |
| 与上级沟通 MiniMax-M2.5 以外的模型部署计划 | 🟢 低 | 影响第 9 步的架构升级决策 |

---

## 十一、V4 路线图深度讨论

### 11.1 已完成工作中需要完善的部分

对照 V4 文档的 R1-R9 和 Phase 0-2.5，以下是"已完成但有隐患"的项目：

#### 隐患 1：R5 安全漏洞（SEC-01）— 必须修复

**现状：** R5 让失败的工具调用可以重试，但没有区分"安全拒绝"和"临时失败"。

**后果：** LLM 可以反复尝试读取 `.git/config` 等受保护路径。这在安全审计中会是一个明确的漏洞。

**修复方案：**
```
工具调用失败
  ├── error message 含 "Access denied" / "security" / "path traversal"
  │     → 保留 dedup 签名（阻止重试）
  │     → 在 assistant message 中注入 "[SECURITY] 此路径不可访问，请勿重试"
  │
  └── 其他错误（文件不存在、超时、格式错误）
        → 不加入 dedup（允许重试）
```

**工作量：** ~20 行代码，`ToolCallProcessor.cs` 中的 dedup 判断逻辑。

#### 隐患 2：ResponseQualityFilter 300 字符阈值（BF-02）— 建议修复

**现状：** `IsInternalReasoning` 方法在文本超过 300 字符时直接返回 false。MiniMax-M2.5 的推理文本经常超过 300 字符，导致泄漏。

**后果：** 用户看到类似"让我思考一下...首先分析代码结构..."的废话文本，影响专业感。

**修复方案：** 不依赖全文长度判断。改为：检测文本前 200 字符是否匹配推理模式前缀。若匹配，在第一个空行处截断，丢弃推理段，保留回答段。

**工作量：** ~30 行代码，`ResponseQualityFilter.cs`。

#### 隐患 3：Condense 摘要质量（TC-13）— 建议增强

**现状：** msg=70 阈值将 condense 触发延迟到了很后面，但这是"绕过"而非"解决"。condense 一旦触发，摘要质量仍然可能导致 LLM 丢失上下文。

**后果：** 对于真正的长对话（50+ 轮工具调用的复杂分析任务），condense 触发后 LLM 可能"忘记"自己在做什么。

**改进方案：** 将 `BuildAutoCondenseSummary` 输出从自由文本改为结构化模板（7 段式），确保关键信息不丢失。详见第 5 步。

#### 隐患 4：DynamicToolSelector 意图分类粒度粗（R3/R9）— 可后续优化

**现状：** 意图分类为 5 种（conversation/read/modify/command/analyze），关键词匹配。

**后果：** 一些边缘请求被分错类别。例如"帮我看看这个函数有没有内存泄漏"（应该是 analyze，但可能被分到 read）。

**建议：** 当前不紧急。在 C/C++ 专家化完成后，可考虑增加 "review" 意图（代码审查场景，需要 read + 领域知识 + 质量评估能力）。

#### 隐患 5：SymbolParser 的 C++ 函数正则有误判风险

**现状：** CppFunctionRegex 用一个复杂正则匹配顶层函数声明，已过滤 if/while/for/switch/return 等控制流关键字。

**风险：** C++ 函数声明的多样性（模板函数、运算符重载、析构函数）可能导致漏匹配或误匹配。

**建议：** 在 Phase 3 知识引擎强化时一并解决，增加模板函数和运算符重载的正则。

### 11.2 Phase 3 之后的路线：哪些有用且可行

V4 文档规划了 Phase 3 → 3.5 → 4 → 5 → 6，跨度约 9 个月。我逐个分析**可行性和优先级**，然后给出建议的重排方案。

#### Phase 3（多 Agent 协作）— 🟡 有用但风险高，建议推迟

**V4 原设计：** Architect/Editor 双 Agent + SK Agent Framework 编排

**可行性分析：**

| 维度 | 评估 |
|------|------|
| SK 依赖 | SK 1.54.0 → 1.71+ 升级是**高风险**操作。System.Text.Json 8.x 兼容性是核心约束，贸然升级可能导致 VS2022 宿主加载失败。需要逐版本验证。 |
| 模型能力 | Architect/Editor 模式需要模型能可靠地执行"只规划不执行"的角色约束。MiniMax-M2.5 是否能做到？没有实测数据。如果 Architect 角色"忍不住"直接写代码，整个管线就失效了。 |
| Token 成本 | 多 Agent = 多次 API 调用。按并发数据，20 人 × 平均 2 Agent/请求 = 40 并发，TTFT 可能飙升到 100s+。在解决规模化之前做多 Agent 会恶化并发问题。 |
| 单人开发 | 多 Agent 系统的调试和维护复杂度远超单 Agent。一个人维护多 Agent 编排是巨大挑战。 |

**建议：** 推迟到以下条件满足后再启动：
1. SK 升级路径验证完成（或找到不依赖 SK Agent Framework 的轻量方案）
2. MiniMax-M2.5 的 Architect 角色可靠性经过实测验证
3. 规模化问题（并发/队列）已解决

**替代方案（轻量版）：** 不做完整的多 Agent 编排，而是在**单 Agent 内增加自检步骤**。例如在 attempt_completion 之前，让 LLM 自问"我是否遗漏了什么？"。这不需要多 Agent，也不需要 SK 升级，但能捕获部分 TC-K01（覆盖率不足）的问题。

#### Phase 3.5（自验证管线）— 🟢 部分可行，建议拆分

**V4 原设计：** StaticVerifier → TestGenerator → TestRunner → ReflectionLoop

**可行性分析：**

| 组件 | 可行性 | 说明 |
|------|--------|------|
| StaticVerifier（静态检查） | ✅ 高 | 对 C/C++ 用 regex 检查编码规范违规（花括号风格、命名、危险函数）。不需要 Roslyn，不需要 SK 升级。 |
| TestGenerator（测试生成） | 🟡 中 | 已有 GenerateTestCommand，可复用。但需要公司测试框架信息。 |
| TestRunner（沙箱执行） | ⚠️ 低 | 需要 R6 SafetyGuard 沙箱（未实现）。在 VS2022 内编译运行 C++ 测试是复杂操作。 |
| ReflectionLoop（反思循环） | ✅ 高 | 不需要多 Agent。在 AgentExecutor 循环内增加一个 "self-check" 步骤即可。 |

**建议：** 拆成两部分：
- **3.5-Lite（可立即做）：** StaticVerifier（regex 规范检查）+ ReflectionLoop（自检步骤）
- **3.5-Full（后续做）：** TestGenerator + TestRunner（依赖沙箱和测试框架信息）

#### Phase 4（分层上下文）— 🟡 核心有用，ONNX 部分可推迟

**V4 原设计：** Hot/Warm/Cold 三层 + ONNX 向量数据库 + CodeBERT 嵌入

**可行性分析：**

| 组件 | 可行性 | 说明 |
|------|--------|------|
| Hot/Warm/Cold 概念 | ✅ 高 | 逻辑清晰，当前 ContextManager 已有雏形。关键是把 condense 输出从自由文本改为结构化，这就是"Warm 层"的本质。 |
| 结构化 Condense | ✅ 高 | 不依赖任何外部库，改 `BuildAutoCondenseSummary` 的输出模板即可。**这是 Phase 4 中 ROI 最高的改动。** |
| ONNX 向量嵌入 | ⚠️ 低 | 需要引入 ONNX Runtime（~50MB），VS2022 插件加载体积增大。且 TF-IDF 对公司代码已经工作（9890 符号 9.8s）。向量检索在当前规模（数万符号）下相比 TF-IDF 的提升有限。 |
| CodeBERT / all-MiniLM-L6-v2 | ⚠️ 低 | 模型文件需要随 VSIX 分发或首次使用时下载。在涉密内网环境中，下载外部模型是障碍。 |

**建议：**
- **立即做：** 结构化 Condense（就是第 5 步），这是 Phase 4 的核心价值，不需要 ONNX。
- **推迟：** ONNX 向量检索。等 TF-IDF 方案遇到瓶颈（例如项目规模超过 10 万符号）时再考虑。

#### Phase 5（Memory Bank）— 🟢 高可行性，建议前移

**V4 原设计：** 会话结束自动写入 `.aica/memory/`，新会话自动读取并注入 System Prompt。

**可行性分析：**

| 维度 | 评估 |
|------|------|
| 技术复杂度 | 低。读写 Markdown 文件 + 在 SystemPromptBuilder 中增加一个 `AddMemoryContext()` 方法。不依赖 SK 升级、不依赖外部库。 |
| 用户价值 | **极高**。这是解决"每次新会话都要重新解释项目背景"的核心方案。对 80 人团队来说，每人每天节省 2-3 分钟的上下文建立时间 = 每天节省 160-240 分钟。 |
| 风险 | 低。Memory Bank 是附加功能，不会影响现有流程。最坏情况：memory 文件为空，退化为当前行为。 |
| 与 C/C++ 专家化协同 | 高。Memory Bank 可以记住"这个项目使用 Qt 5.12"、"构建系统是 CMake"、"测试框架是 Google Test"，这些信息在 C/C++ 专家化中有直接价值。 |

**建议：前移到第 4 步**（已体现在行动路线中）。

#### Phase 6（事件驱动 + 主动建议）— 🔴 推迟

**V4 原设计：** EventHub + Proactive Advisor + WorkflowGraph

**可行性分析：**

| 维度 | 评估 |
|------|------|
| 复杂度 | 极高。事件驱动系统需要 VS2022 的 Build Event、File Change Event 的可靠订阅。WorkflowGraph 需要状态机引擎。单人开发和维护成本高。 |
| 用户价值 | 中。"编译失败后 3 秒内自动建议修复"很酷，但用户可能更需要的是"我问的问题它能答对"。 |
| 依赖 | 依赖 Phase 3（多 Agent）和 Phase 4（上下文管理）的完成。 |

**建议：** 推迟到 2026 Q4 或更后。当前优先级远低于专业化和规模化。

### 11.3 建议的重排路线图

基于以上分析，V4 文档的 Phase 顺序应当重排：

**V4 原顺序：**
```
Phase 3 (多Agent) → Phase 3.5 (自验证) → Phase 4 (分层上下文) → Phase 5 (Memory Bank) → Phase 6 (事件驱动)
```

**建议新顺序：**
```
C/C++ 专家化 → Phase 5 (Memory Bank, 前移) → Phase 4-Lite (结构化Condense)
    → Phase 3.5-Lite (静态检查+自检) → 规模化 → Phase 3 (多Agent, 条件成熟后)
    → Phase 4-Full (向量检索, 按需) → Phase 6 (事件驱动, 远期)
```

**重排理由表：**

| 调整 | 理由 |
|------|------|
| C/C++ 专家化插入最前 | V4 中没有这个方向，但这是产品定位的核心差异化。优先级最高。 |
| Phase 5 前移到 Phase 3 之前 | Memory Bank 不依赖 SK 升级，实现简单，用户价值极高。没有理由排在多 Agent 之后。 |
| Phase 4 拆分为 Lite + Full | 结构化 Condense 立即可做（Lite），ONNX 向量检索等需要时再做（Full）。 |
| Phase 3.5 拆分为 Lite + Full | 静态检查 + 自检步骤不依赖多 Agent（Lite），测试生成 + 沙箱执行依赖 R6（Full）。 |
| Phase 3 推迟 | 依赖 SK 升级 + 模型验证 + 并发容量。在这三个前提满足前启动 Phase 3 风险过高。 |
| Phase 6 保持远期 | 复杂度高、依赖多、用户价值相对较低。 |
| 规模化新增 | V4 中完全没有考虑 80 人并发的问题。这在 Phase 5 之后、Phase 3 之前必须解决。 |

---

## 十二、产出物索引

（更新于 2026-03-22 第二次会话）

| 文件 | 位置 | 内容 |
|------|------|------|
| 本文档 | `doc/agentref/ProductStrategy_Retrospective.md` | 会话复盘总结 |
| C/C++ 专家化技术方案 | `doc/agentref/CppSpecialization_TechPlan.md` | Phase 1-3 实施细节 |
| Agent 演进计划 V4 | `doc/agentref/AgentEvolutionPlan_v4.md` | 原始路线图（已审计） |
| E2E 测试报告 | `doc/agentref/e2e-test-report.md` | 3/3 PASS |
| Round 2 测试结果 | `doc/agentref/TestResults_Round2.md` | 5/6 PASS |
| 技术债务学习笔记 | `doc/agentref/TechnicalDebtCleanup.md` | 7 节课 Agent 学习记录 |
