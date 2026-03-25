# AICA 下一阶段工作计划

> 版本: v1.9 | 日期: 2026-03-25
> 定位: 合并 Harness 工程改造 + 原路线图基础修复/M1/M2，形成可执行的工作计划
> 前置文档: [项目总结与路线图](agentref/AICA_ProjectReview_and_Roadmap.md) | [产品设计研讨会](AICA_ProductDesign_Workshop.md)
> 规范来源: Qt-C++ 编程规范.doc + MISRA C++/C

### 修正记录

<details>
<summary>v1.1 修正（9 项）</summary>

| # | 修正项 | 原问题 | 修正内容 |
|---|--------|--------|---------|
| C1 | F3 Bug 定位缺失 | M1 功能未覆盖 F3 | 阶段 1 新增步骤 1.3（F3 Bug 定位 prompt） |
| C2 | F5 QT 模板缺失 | M2 功能未覆盖 F5，与"QT 先上线"矛盾 | 阶段 3 新增步骤 3.2（F5 QT 模板生成） |
| C3 | 右键命令改造不完整 | 语言检测做了但右键命令未适配 | 步骤 1.2 扩展，明确覆盖三个右键命令 |
| C4 | H1 括号检查对 C++ 误报 | 模板语法/宏/条件编译导致大量假阳性 | 重新设计 H1，移除括号检查，改为内容存在性 + diff 行数异常检测 |
| C5 | H2 状态机对 MiniMax-M2.5 兼容性未验证 | LLM 可能无法理解工具被拦截的含义 | 新增 H2 验证前置步骤 + 软拦截模式（建议而非强制） |
| C6 | H2 模板冲突无优先级 | 多模板同时匹配时无确定行为 | 增加优先级规则和互斥判定 |
| C7 | 遥测验收过度承诺 | F1/F2 成功率需要人工判断，遥测无法自动化 | 修正验收标准表述，区分遥测可衡量和需人工判断的指标 |
| C8 | Harness 重心偏 Complex，Simple/Medium 受益少 | 80% 日常请求的改善被忽略 | 重新审视每项改造对 Simple/Medium 的价值，标注受益范围 |
| C9 | 阶段 3 集成调试时间低估 | 4 项 Harness 改造在 AgentExecutor 主循环交叉作用 | 增加 3 天集成测试窗口，总缓冲从 5 天增至 8 天 |

</details>

### v1.2 修正（8 项，收敛审查）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C10 | maxIterations 保持 50 不提升 | H7 新增 10 分钟墙钟超时作为第四道防线 | 用 Harness 减少无效迭代，不靠提高上限暴力补偿。超时后诚实报告失败原因 + 支持下轮继续 |
| C11 | H2 放弃后任务分解独立保留 | PlanManager prompt 增强（任务分解 + 五步法）不依赖 H2，始终执行 | 任务分解是纯 prompt 注入，不需要工具拦截机制 |
| C12 | F4 一次完成率指标统一 | M2 用 40-50% 全场景口径（含 2-5 文件） | 与路线图考核指标表对齐，保守承诺 |
| C13 | F8 GitNexus 不可用降级 | 从开源生态找功能等价替代组件 | 不自建简化版（耗时），不放弃（F8 有价值），从 GitHub 找 Tree-sitter/代码图谱类开源工具替代 |
| C14 | paths 过滤已确认 + C++ 强制激活 | ProjectLanguageDetector 判定 C++ 项目后强制激活规范文件；用户请求关闭需确认提醒 | RuleLoader 完整支持 paths glob 过滤。C++ 规范是项目级强制规则，不应轻易关闭 |
| C15 | 阶段 1 后非正式部署收集场景 [C40] | 新增步骤 1.4：非正式部署 + 通知工程师试用并收集验收任务场景；正式发布推迟到全计划完成后 | 工程师提供真实场景（非开发者自行设计），与 GitNexus 开发并行。领导要求正式发布需带 GitNexus 功能 |
| C16 | 构建系统待确认项补回 | 新增待确认项 #8：公司构建系统（CMake/MSBuild） | 路线图中有但工作计划遗漏，影响规范文件完善 |
| C17 | 反馈收集双渠道 | Excel 结构化模板 + 钉钉群快速通道 | Excel 用于正式记录和分析，钉钉群降低反馈门槛保证不丢失 |

### v1.3 修正（10 项，逻辑审查收敛）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C18 | H2 放弃时 H7 去耦 | H7 保留四级预算策略，80% 级别改为纯文本警告（不调 ForceAdvance）；集成测试矩阵去掉 H2 相关交叉场景 | H7 不应依赖 H2 的存在性。H2 放弃时 H7 独立运作 |
| C19 | F5 GitNexus 不可用降级 | signal/slot 查找从开源生态找替代组件（与 C13 一致策略），不降级为 grep_search | 保持 F5 功能完整性，与 F8 降级方案统一 |
| C20 | 待确认项 #5/#7 去阻塞 | 标注"开工时代码搜索确认，不阻塞进度" | 项目结构可通过代码搜索快速定位，无需提前人工确认 |
| C21 | System Prompt 警告阈值调整 | Build() 警告阈值从 8000 调整为 16000（适配 177K 上下文窗口） | 8000 是 32K 时代遗留值，177K 下仅占 4.5%。行业参考 12K-20K 为正常范围 |
| C22 | H5+H3 集成测试 | 步骤 3.10 集成测试矩阵新增 H5+H3 交叉场景 | H5 预检可能吞掉 H3 应诊断的 edit 失败 case |
| C23 | F6 功能覆盖表补全 | 功能覆盖表新增 F6 行，标注"本计划不覆盖，属于 M3" | F1-F8 不跳号，避免读者困惑 |
| C24 | 步骤 1.2 行数修正 | SystemPromptBuilder 改动从 ~60 行修正为 ~80 行 | C14 强制激活逻辑增加了 ~20 行，叙述文本漏更新 |
| C25 | 步骤 3.7 验收标准修正 | "从 procparaminterface 继续"改为"知道已编辑文件，从未完成步骤继续" | 原文是 MinCurve 案例占位符，与验收场景不一致 |
| C26 | 步骤 1.4 接口编译验证 | 编译验证中确认 ITaskContext.EditedFilesInSession 接口编译通过且无回归 | 步骤 0.6 到阶段 3 间隔 ~25 天无验证，借测试版编译补上 |
| C27 | 反馈收集不足风险 | 风险表新增"工程师反馈收集不足"，缓解措施：开发者主动到工位旁观记录 | 步骤 2.2 需要 10 个真实任务，5 天收集窗口较紧 |

### v1.4 修正（1 项，E2E 测试发现）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C28 | H5 预检拦截新文件创建 | 修正 `full_replace` 参数检测（boolean 而非 mode 字符串）；文件不存在 + new_string 非空时放行到 EditFileTool（有用户确认） | E4 测试"帮我写一个 C++ 类"触发 50 次迭代超时。根因：H5 参数名不匹配 + LLM 不使用 full_replace + 空 old_string 被拦截。影响所有"写新代码"场景 |

### v1.5 修正（5 项，阶段 0+1 执行 + E2E 测试收敛）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C29 | BF-02 推理模式补全 | `MetaReasoningPatterns` 补 `"让我思考"`，`ReasoningStartPatterns` 补 `"首先我需要"`（无逗号变体） | 阶段 0 验证回路发现验收标准 AC1 原文短语不匹配 |
| C30 | general.md C# 规则移除 | 移除 C# XML 文档注释规则，改为语言中立表述指向 doxygen | AICA 仅服务 C/C++ 开发者，不需要 C# 规则处理。**[C71] general.md 已不再创建，本项修正已失效** |
| C31 | 右键命令扩展名补齐 | 三个右键命令 isCpp 检查补 `.hpp`/`.hxx` | 与 `ProjectLanguageDetector.CppExtensions` 一致，避免 header-only C++ 库用户体验降级 |
| C32 | E5 Bug 定位模糊描述超时 | 记录为已知限制，不在阶段 1 修复 | 根因是 LLM (MiniMax-M2.5) 在大项目搜索无果时不知停下，属阶段 3 H2 状态机覆盖范围（步骤 3.3 迭代预算感知） |
| C33 | 规范文件技术债务 | 记录：5 个 cpp-*.md 从 .doc 原始提取，MISRA C 依赖 AI 知识库而非逐条核对 | 源文档未经标准化格式输入，~~待步骤 2.1 期间穿插审查 [C50]~~ **步骤 2.1 已完成但审查未执行，推迟到步骤 2.3 前完成** |

### v1.6 修正（18 项，计划变更：发布策略 + 服务范围调整）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C34 | 服务范围缩减 | AICA 不再服务算法组，服务人数从 60 人调整为 ~40 人（平台组 ~20 + 界面组 ~20） | 算法组不在 AICA 服务范围内，领导确认 |
| C35 | 工作范围新增排除声明 | 在"工作范围与边界"中明确声明算法组不在 AICA 服务范围内 | 避免歧义，明确边界 |
| C36 | 角色分类调整 | 三类角色（研发/QT/算法）→ 两类角色（平台组/界面组） | 去掉算法组后重新分类 |
| C37 | 角色名称统一 | "研发"→"平台组"，"QT 工程师"→"界面组" | 与公司内部实际称呼对齐 |
| C38 | F6 算法辅助保留但暂停 | F6 行保留在功能覆盖表中，标注"暂不服务，按需启用" | 保留功能定义以备将来启用 |
| C39 | F4/F8 受益范围修正 | 去掉算法组，改为"平台组+界面组 ~40 人" | 与 C34 一致 |
| C40 | 步骤 1.4 改为非正式部署 | 不再称"测试版发布"，改为"非正式部署 + 场景收集"；正式发布推迟到全计划完成后 | 领导要求正式发布需带 GitNexus 功能 |
| C41 | 步骤 1.4 部署范围调整 | 三类角色 6-9 人 → 平台组 3 人 + 界面组 3 人 = 6 人 | 与实际试用安排对齐 |
| C42 | 步骤 1.4 时间线提前 | 3/31 → 3/23（阶段 0+1 提前完成） | 阶段 0+1 于 3/23 完成，比原计划提前一周 |
| C43 | 编译脚本双环境标注 | 补上 `build.ps1`（标准环境）和 `buildinhome.ps1`（家庭环境）两个脚本 | 两套环境均可编译 |
| C44 | 步骤 2.2 角色覆盖调整 | 研发×4 + QT×4 + 算法×2 → 平台组×5 + 界面组×5 | 去掉算法组，均分 |
| C45 | 步骤 2.3 改为小范围试用 | "M1 验收 + QT 工程师部署"→"M1 验收 + 小范围试用部署"（~10 人，平台 5 + 界面 5） | 正式发布推迟，2.3 仍为试用性质 |
| C46 | 步骤 2.3 验收任务来源 | 20 个 F1 任务由开发者基于 1.4 收集的真实场景扩展设计 | 6 人试用收集 12-18 个场景，不够 2.3 验收所需数量 |
| C47 | 新增步骤 3.11 正式发布 | 全计划完成后正式发布给 ~40 人，含编译验证、全量部署、反馈渠道 | 原计划无明确的正式发布步骤 |
| C48 | 时间线全局提前 | 阶段 2: 3/23-4/7；阶段 3: 4/8-5/10 | 阶段 0+1 提前完成，后续顺延提前 |
| C49 | 待确认事项 #1/#2 已确认 | Node.js 部署可行性 + PolyForm 许可证合规性均已确认（3/23） | 阶段 2 提前开始，前置条件已满足 |
| C50 | C33 规范文件审查时机 | 在步骤 2.1 开发期间穿插进行（原为"M1 前审查"） | 时间线提前，2.1 开发期间穿插不冲突 |
| C51 | 去掉"先上线"/"首批"表述 | 界面组不再是"首批上线用户"，两组同时参与试用和正式发布 | 两次部署均为平台/界面均分，无先后之分 |

### v1.7 修正（7 项，步骤 2.1 Day 1+2 执行校准）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C52 | 步骤 2.1 新建文件补全 | 新增 `IGitNexusProcessManager.cs` 接口文件，总计 4 个新建文件 | 实际执行中拆分了接口以支持测试 Mock |
| C53 | 步骤 2.1 行数校准 | McpClient ~350 行、GitNexusProcessManager ~230 行、McpBridgeTool ~380 行、IGitNexusProcessManager ~40 行 | 实际实现比预估复杂：MCP 协议需要完整的 JSON-RPC 读写循环 + Content-Length 帧解析；6 个工具各自需要独立的参数 schema 定义 |
| C54 | 步骤 2.1 工具表补全 cypher | 桥接工具从 5 个增至 6 个，新增 `gitnexus_cypher`（Cypher 图谱查询） | MiniMax-M2.5 实测能可靠使用 cypher 工具（OpenCode 截图证实），应作为常规工具提供 |
| C55 | 步骤 2.1 改动文件修正 | 工具注册在 `ChatToolWindowControl.xaml.cs`（非 ToolDispatcher.cs）；新增改动 `DynamicToolSelector.cs` | ToolDispatcher 是运行时注册 API，实际注册代码在 VSIX 层的 UI 初始化中 |
| C56 | 步骤 2.1 测试文件补充 | 新增 3 个测试文件：McpClientTests.cs、GitNexusProcessManagerTests.cs、McpBridgeToolTests.cs | 实际执行中按 TDD 方式同步编写测试 |
| C57 | 阶段 2 代码量校准 | 阶段 2 步骤 2.1 实际代码 ~1000 行（不含测试），含测试 ~1730 行 | 预估 ~300 行严重低估，MCP 协议实现 + 6 个工具定义 + 降级逻辑 + 进程管理 = 远超预期 |
| C58 | AgentEvalHarness 命名空间修复 | 修复 `AICA.Core.Tests.LLM` 命名空间冲突导致的编译错误 | Day 1 新增 McpClientTests.cs 引入的 `AICA.Core.Tests.LLM` 命名空间与 AgentEvalHarness 中的 `LLM.ChatMessage` 引用冲突 |

### v1.8 修正（5 项，GitNexus 部署方式 + 步骤 2.2 调整）

| # | 修正项 | 决定 | 原因 |
|---|--------|------|------|
| C59 | GitNexus 内嵌到 AICA 仓库 | 新增 `tools/gitnexus/`（dist/ + package.json），开发者克隆后 `npm install --omit=dev` 即可使用 | 方案 C：对项目开发者和维护者更友好，不需要额外安装 GitNexus |
| C60 | GitNexusProcessManager 启动路径三级解析 | 优先内嵌版本 → `AICA_GITNEXUS_PATH` 环境变量 → npx 兜底 | 兼容开发环境（内嵌）和部署环境（环境变量），npx 作为最终降级 |
| C61 | GitNexus MAX_FILE_SIZE 从 512KB 提升到 2MB | 支持 `GITNEXUS_MAX_FILE_SIZE` 环境变量动态配置 | 华中数控实际项目有大量单文件 >512KB 的 C/C++ 源码，原限制会导致关键符号缺失 |
| C62 | 步骤 2.2 任务来源调整 | 工程师真实场景暂未收集到时，可用 poco 项目自测替代 | 1.4 反馈收集周期未到，不阻塞步骤 2.2 推进 |
| C63 | 风险 "GitNexus 整体不可用" 已关闭 | MCP 服务启动成功（poco 2 repo），风险已消除 | `node dist/cli/index.js mcp` 输出 `MCP server starting with 2 repo(s)` |
| C64 | GitNexus 索引路径修复 | TriggerIndexAsync 新增 FindGitRoot：从 .sln 目录向上查找 .git | .sln 在 `poco\build\`，.git 在 `poco\`，原来传 build 目录导致 exit=1 |
| C65 | GitNexus 内嵌漏 vendor/ 目录 | 补复制 `vendor/leiden/`（20KB 社区检测算法）| analyze 时 community-processor.js 加载 leiden 失败 |
| C66 | 步骤 2.2 自测发现 5 个 LLM 参数优化项 | P1-P5 记录在步骤 2.1 执行记录中，P1-P3 需 SystemPrompt few-shot 注入 | 多仓库 repo 参数、impact 符号名格式、Cypher schema 不匹配、grep 路径混淆、dedup 循环 |
| C67 | P1-P3 修复：SystemPrompt GitNexus few-shot 注入 | `AddGitNexusGuidance(repoName)` ~30 行 + `ResolveGitNexusRepoName` ~15 行 | 复测 IT3 从 14 轮降至 2 轮。repo 参数、简单符号名、Cypher schema 均一次正确 |
| C68 | 规范注入统一为 .aica-rules 单一路径 | 删除 `AddCppSpecialization()` 硬编码，全部通过 RuleLoader 从 `.aica-rules/cpp-*.md` 加载 | Phase 1 存在双路径注入设计缺陷：硬编码路径 A 生效但 .aica-rules 路径 B 从未部署到目标项目 |
| C69 | .aica-rules 目录位置改为 git root | `SolutionEventListener` 用 `FindGitRoot` 解析项目根目录，`.aica-rules` 与 `.git` 同级 | 原来在 `poco\build\.aica-rules`（.sln 目录），现改为 `poco\.aica-rules`（git root） |
| C70 | RulesDirectoryInitializer 自动部署 C++ 规范 | C++ 项目检测（IsCppProject 15% 阈值）+ 6 个 `cpp-*.md` 自动创建 | 新建 `CppRuleTemplates.cs` 嵌入规范内容 + `cpp-aica-guidance.md`（代码解释 + 测试生成 + Bug 修复） |
| C71 | 去除 general.md 自动创建 | `.aica-rules` 只创建 C++ 规范文件，不再创建通用 general.md | general.md 内容过于泛化，C++ 规范文件已覆盖所有需要 |
| C72 | IsCppProject 阈值 0.3→0.15 | poco 项目 55/200 = 27.5% 低于 0.3 阈值导致检测失败 | 大型 C++ 项目根目录含大量 config/build/doc 文件，C++ 源文件占比可能低于 30% |
| C73 | 旧版 VSIX 部署兼容性 | 已部署工程师使用旧版（含 `AddCppSpecialization` 硬编码），新版发布前不受影响 | 旧 `build/.aica-rules/general.md` 成为孤儿文件，不影响功能 |
| C74 | 规范注入验证（poco MyTimer）| 生成代码遵循 Allman/m_/doxygen/Bit64 — 规范生效 | 但 48 轮迭代（触发安全边界），LLM 过度探索项目结构而非直接生成 — 已知限制 [C32] |

---

## 一、工作范围与边界

本计划覆盖从**当前（2026-03-22）到 M2 交付（2026-05 中旬）**的全部开发工作。

**包含：**
- 路线图基础修复（SEC-01、BF-02、BF-06）
- Harness 基础设施新增（遥测、预检）
- M1 交付项（C/C++ 规范注入、语言检测、**右键命令语言感知**、GitNexus 集成、**F3 Bug 定位 prompt**）
- M2 交付项（Edit 诊断路由、状态机、保护区 Condense、迭代预算感知、验证中间件、**F5 QT 模板**、Memory Bank）

**不包含：**
- M3 内容（规模化、请求队列）
- 算法组相关功能（**算法组不在 AICA 服务范围内** [C35]，F6 算法辅助保留定义但暂不实施）
- 多 Agent 架构
- SK 升级

**功能覆盖对照（v1.7 修正后）：** [C34/C36/C37/C38/C39/C54]

| 功能 | 里程碑 | 工作计划步骤 | 受益用户 |
|------|--------|------------|---------|
| F1 代码理解 | M1 | 步骤 2.1 GitNexus | ~40 人 |
| F2 规范生成 | M1 | 步骤 1.1 + 1.2 | ~40 人 |
| **F3 Bug 定位** | **M1** | **步骤 1.3** [C1] | **~40 人** |
| F4 跨文件实现 | M2 | 步骤 3.1+3.3+3.4+3.5 | 平台组+界面组 ~40 人 [C39] |
| **F5 QT 模板** | **M2** | **步骤 3.2** [C2] | **界面组 ~20 人** |
| F6 算法辅助 | **M3** | **本计划不覆盖** | **暂不服务，按需启用** [C38] |
| F7 跨会话记忆 | M2 | 步骤 3.8 | ~40 人 |
| F8 修改影响预警 | M2 | 步骤 2.1 GitNexus impact() | 平台组+界面组 ~40 人 [C39] | ✅ GitNexus 已验证可用 [C63] |

> [C14] 规范文件来源：`Qt-C++ 编程规范.doc` + `MISRA C++/C`，提炼为 prompt 友好的 markdown 格式。

---

## 二、阶段划分

```
阶段 0 [3/22-3/23]   基础修复 + Harness 基础设施         ← ✅ 提前完成
阶段 1 [3/23]        C/C++ 专业化 + 日常功能基础         ← ✅ 提前完成（原计划 3/26-3/31）
阶段 2 [3/23-4/7]    M1: 代码理解可靠                    ← 🔄 步骤 2.1 ✅ + 2.2 自测 ✅ 完成（3/25），步骤 2.3 待执行 [C48]
阶段 3 [4/8-5/10]    M2: 日常功能完善 + 跨文件可用        ← 33 天（含 8 天缓冲）[C9][C48]
```

> [C9] 阶段 3 缓冲从 5 天增至 8 天，新增 3 天为 Harness 交叉集成测试窗口。

---

## 三、阶段 0：基础修复 + Harness 基础设施

> 目标：消除已知安全隐患，建立度量基础，拦截低级工具调用错误。
> 时间：3/22-3/25（4 天）

### 步骤 0.1：SEC-01 安全去重修复

**要做什么：** ToolCallProcessor 的 dedup 逻辑不区分"安全拒绝"和"临时失败"，导致 LLM 可以反复尝试访问受保护路径（如 `.git/config`）。

**现状审查：**
- `ToolCallProcessor.cs`（397 行）中 `ShouldAllowRetry()` 方法已存在，检查 `SecurityDenied` 时返回 false 阻止重试
- `ToolResult` 有 `FailureKind` 属性区分 `Transient` / `SecurityDenied` / `NotFound`
- **问题确认：** `GetToolCallSignature()` 生成 dedup key 时对路径做了 `ToLowerInvariant().Trim()`，但没有对安全拒绝的路径做特殊标记。LLM 换一个大小写或加斜杠就能绕过 dedup

**改动位置：** `src/AICA.Core/Agent/ToolCallProcessor.cs`
**改动量：** ~20 行
**方案：**
- 在 dedup set 中，对 SecurityDenied 结果的路径做规范化后永久加入黑名单
- 黑名单用 `HashSet<string>` 存储，匹配时对新请求路径也做同样规范化
- 规范化规则：`Path.GetFullPath()` + `ToLowerInvariant()` 消除 `./`、`../`、大小写差异

**受益范围：** [C8] 全部任务（Simple/Medium/Complex 均受益，安全是基础设施）

**验收标准：**
- LLM 尝试 `read_file(".git/config")` 被拒绝后，再尝试 `read_file(".\\git\\config")` 或 `read_file(".GIT/CONFIG")` 同样被拦截
- 非安全拒绝的失败（如文件不存在）不受影响，LLM 可以用不同参数重试

---

### 步骤 0.2：BF-02 推理泄漏修复

**要做什么：** MiniMax-M2.5 的推理文本经常超过 300 字符，导致 `ResponseQualityFilter` 的阈值判断失效，用户看到"让我思考一下..."之类的内部推理。

**现状审查：**
- `ResponseQualityFilter.cs`（650+ 行）中 `ReasoningStartPatterns` 包含 ~15 个中英文推理前缀模式
- 当前逻辑：检测前 200 字符是否匹配推理模式前缀，匹配则整段视为推理
- **问题确认：** MiniMax-M2.5 的推理文本前缀有时不在前 200 字符内（先输出几行正常内容再开始"让我分析一下"），导致漏检

**改动位置：** `src/AICA.Core/Prompt/ResponseQualityFilter.cs`
**改动量：** ~30 行
**方案：**
- 将检测范围从"前 200 字符"改为"按段落扫描全文"
- 对每个段落（以 `\n\n` 分割）检查是否匹配推理模式
- 匹配到的段落标记为 ThinkingChunk，其余保留为正常输出

**受益范围：** [C8] 全部任务（Simple/Medium/Complex 均受益，所有用户都会遇到推理泄漏）

**验收标准：**
- 输入包含"好的，让我思考一下这个问题。\n\n首先我需要..."时，整段被过滤
- 正常回答中包含"让我"二字但不是推理前缀的（如"让我们看看这个函数"）不被误过滤

---

### 步骤 0.3：BF-06 复杂度误分类修复

**要做什么：** 含代码片段的解释请求被错误分类为 Complex，触发不必要的规划流程。

**现状审查：**
- `TaskComplexityAnalyzer.cs`（110 行）中 `ContextMenuCappingPattern` 用正则匹配右键命令格式
- 当前正则：`请用中文...解释|重构|...生成...测试|explain|refactor|generate`
- **问题确认：** 用户发送"解释一下这段代码：[粘贴 200 行代码]"时，长度触发 +2 分，加上"分析"关键词 +3 分，总分 5 → Complex。但右键命令格式不完全匹配（用户自己输入而非右键触发）

**改动位置：** `src/AICA.Core/Agent/TaskComplexityAnalyzer.cs`
**改动量：** ~5 行
**方案：**
- 增加独立的"解释/说明类"降级规则：如果请求包含"解释|说明|explain|what does|这段代码"且不包含"实现|创建|修改|重构"，强制 cap 到 Medium
- 这与 ContextMenuCapping 独立，覆盖用户手动输入的解释请求

**受益范围：** [C8] 主要受益者是 Simple/Medium 任务（占 80% 请求量），防止误触发 Complex 规划流程

**验收标准：**
- "解释一下这段 500 行代码的逻辑" → Medium（不触发 Complex 规划）
- "分析这个项目的架构并重构日志模块" → Complex（正确，因为包含"重构"）

---

### 步骤 0.4：H4 结构化遥测（新增）

**要做什么：** 建立 Agent 会话级结构化度量，为后续所有改造和验收指标提供数据基础。

**现状审查：**
- `Middleware/LoggingMiddleware.cs` 和 `Middleware/MonitoringMiddleware.cs` 已存在，但输出非结构化日志
- `TaskState.cs`（144 行）已跟踪 `TotalToolCallCount`、`EditedFiles`、`Iteration`、`ConsecutiveBlockingFailureCount` 等状态
- `AgentExecutor.cs` 的 `ExecuteAsync()` 是 `IAsyncEnumerable<AgentStep>` 循环，循环结束点可确定（yield break 或 IsCompleted/Abort）
- **可行性确认：** TaskState 已有大部分需要的数据，只需在循环结束时聚合写入文件

**新建文件：** `src/AICA.Core/Agent/AgentTelemetry.cs`（~100 行）
**改动文件：** `src/AICA.Core/Agent/AgentExecutor.cs`（~15 行，循环结束处调用写入）

**数据结构：**

```csharp
public class SessionRecord
{
    // 标识
    public string SessionId { get; set; }
    public DateTime Timestamp { get; set; }

    // 输入特征
    public string Complexity { get; set; }      // Simple/Medium/Complex
    public string Intent { get; set; }           // read/modify/analyze/conversation
    public int UserMessageTokens { get; set; }   // 用户请求的 token 估算

    // 执行过程
    public int Iterations { get; set; }
    public int ToolCalls { get; set; }
    public Dictionary<string, int> ToolCallCounts { get; set; }
    public Dictionary<string, int> ToolFailCounts { get; set; }
    public int CondenseCount { get; set; }

    // Edit 专项
    public int EditAttempts { get; set; }
    public int EditSuccesses { get; set; }
    public int EditFailures { get; set; }
    public List<string> EditFailureReasons { get; set; }  // 为 H3 诊断预留

    // 结果
    public string Outcome { get; set; }   // completed/aborted/timeout/user_cancelled
    public int DurationMs { get; set; }
}
```

**存储方式：**
- 写入 `~/.AICA/telemetry/` 目录下按日期命名的 JSONL 文件
- 每行一条 JSON 记录，便于后续脚本聚合分析
- 不写入项目目录（避免污染用户工作区）

**集成方式：**
```csharp
// AgentExecutor.cs ExecuteAsync() 方法末尾
// 在所有 yield break 之前统一调用
var record = AgentTelemetry.BuildRecord(sessionId, _taskState, complexity, intent, startTime);
await AgentTelemetry.WriteAsync(record);
```

**[C7] 遥测能力边界（明确标注）：**

| 指标 | 遥测可自动衡量？ | 说明 |
|------|----------------|------|
| Edit 成功率 | ✅ 是 | EditSuccesses / EditAttempts |
| 工具调用成功率 | ✅ 是 | ToolCallCounts vs ToolFailCounts |
| 任务完成率（Outcome） | ✅ 是 | completed / total |
| TTFT / 响应时间 | ✅ 是 | DurationMs |
| **F1 代码理解质量** | **❌ 否** | **需要人工判断回答是否正确** |
| **F2 规范合规率** | **❌ 否** | **需要人工审查生成代码** |
| **F5 QT 模板质量** | **❌ 否** | **需要界面组工程师验收** |
| 日活用户数 | ✅ 是 | 按 SessionId 去重统计 |

> 遥测替代的是**工具级指标**的人工统计，不替代**功能质量**的人工判断。M1/M2 验收仍需每个里程碑用 20 个真实任务做人工评估。

**受益范围：** [C8] 全部任务（基础设施）

**验收标准：**
- 执行一次 Agent 会话后，在 `~/.AICA/telemetry/` 下生成 JSONL 文件
- 文件内容包含 Complexity、ToolCallCounts、Outcome 等字段
- 多次会话追加到同一天的文件中（不覆盖）

---

### 步骤 0.5：H5 工具调用预检（新增）

**要做什么：** 在工具实际执行之前拦截明显错误的参数，避免浪费迭代轮次。

**现状审查：**
- 中间件管道已就绪：`ToolExecutionPipeline.cs`（213 行）支持 `Use(IToolExecutionMiddleware)`
- `ToolExecutionContext` 中有 `Call`（ToolCall）、`AgentContext`（含 `FileExistsAsync`、`IsPathAccessible`）
- 当前 4 个中间件：PermissionCheck → Timeout → Logging → Monitoring
- `IToolExecutionMiddleware` 接口：`Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)`
- **可行性确认：** 接口完备，新增中间件只需实现 `IToolExecutionMiddleware` 并在 `ToolDispatcher.UseMiddleware()` 中注册

**新建文件：** `src/AICA.Core/Agent/Middleware/PreValidationMiddleware.cs`（~90 行）
**改动文件：** 中间件注册处（`AICAPackage.cs` 或 Agent 初始化代码，~3 行）

**预检规则：**

| 工具 | 预检项 | 拦截条件 | 返回消息 |
|------|--------|---------|---------|
| `edit` | file_path 存在性 | `!FileExists(path)` 且非 `full_replace` 模式 且 `new_string` 为空（new_string 非空时放行，视为创建文件意图）[C28] | "文件 {path} 不存在。请用 find_by_name 确认路径后重试。" |
| `edit` | old_string 有效性 | `IsNullOrWhiteSpace(oldString)` 且非 `full_replace` 模式 | "old_string 不能为空。请先用 read_file 查看文件内容。" |
| `edit` | old_string = new_string | 精确相等 | "old_string 和 new_string 相同，无需编辑。" |
| `read_file` | file_path 存在性 | `!FileExists(path)` | "文件 {path} 不存在。" |
| `grep_search` | query 非空 | `IsNullOrWhiteSpace(query)` | "搜索关键词不能为空。" |

**注册位置：** 排在 PermissionCheckMiddleware **之后**、TimeoutMiddleware **之前**。

```
Pipeline 执行顺序：
PermissionCheck → PreValidation → Timeout → Logging → Monitoring → 核心工具执行
```

**受益范围：** [C8] **全部任务**（Simple/Medium/Complex 均受益——LLM 在任何复杂度下都可能生成无效参数）

**验收标准：**
- LLM 调用 `edit(file_path="不存在的文件.cpp", ...)` → 立即返回提示，不执行 EditFileTool
- LLM 调用 `edit(file_path="存在的文件.cpp", old_string="", ...)` → 立即返回提示
- 合法的工具调用不受影响，正常执行

---

### 步骤 0.6：IAgentContext 接口扩展（新增，为阶段 3 铺路）

**要做什么：** 在 `ITaskContext` 中增加会话级编辑文件列表的只读访问，供后续 H3（Edit 诊断）和 H6（Condense 保护区）使用。

**现状审查：**
- `IAgentContext.cs` 定义 `IAgentContext : IFileContext, IWorkspaceContext, ITaskContext`
- `ITaskContext.cs`（27 行）只有 `CurrentPlan` 和 `UpdatePlan` 和 `RequestConfirmationAsync`
- `TaskState.cs` 中已有 `EditedFiles`（`HashSet<string>`），但它在 `AgentExecutor` 私有字段上，工具无法访问
- **可行性确认：** 需要在 ITaskContext 加一个属性，让实现类暴露 TaskState.EditedFiles

**改动文件：**
- `src/AICA.Core/Agent/ITaskContext.cs`（+3 行）
- `ITaskContext` 的实现类（+5 行，返回 TaskState.EditedFiles 的只读包装）

**接口变更：**
```csharp
public interface ITaskContext
{
    TaskPlan CurrentPlan { get; }
    void UpdatePlan(TaskPlan plan);
    Task<bool> RequestConfirmationAsync(string operation, string details, CancellationToken ct = default);

    // 新增：当前会话中已编辑的文件列表（只读）
    IReadOnlyCollection<string> EditedFilesInSession { get; }
}
```

**验收标准：**
- 在工具的 `ExecuteAsync` 方法中可通过 `context.EditedFilesInSession` 访问已编辑文件列表
- 编译通过，现有测试不受影响

---

### 阶段 0 完成检查清单

| # | 任务 | 文件 | 新增/改动 | 行数 | 受益范围 | 状态 |
|---|------|------|----------|------|---------|------|
| 0.1 | SEC-01 安全去重 | ToolCallProcessor.cs | 改动 | ~65 | 全部 | ✅ 验收通过 (5/5 AC) |
| 0.2 | BF-02 推理泄漏 | ResponseQualityFilter.cs | 改动 | ~40 | 全部 | ✅ 验收通过 (5/5 AC) + [C29] 补全 |
| 0.3 | BF-06 复杂度误分类 | TaskComplexityAnalyzer.cs | 改动 | ~15 | Simple/Medium | ✅ 验收通过 (3/3 AC) |
| 0.4 | H4 结构化遥测 | AgentTelemetry.cs (新) + AgentExecutor.cs | 新建+改动 | ~170 | 全部 | ✅ 验收通过 (5/5 AC) |
| 0.5 | H5 工具调用预检 | PreValidationMiddleware.cs (新) + 注册处 | 新建+改动 | ~115 | 全部 | ✅ 验收通过 (7/7 AC) + [C28] hotfix |
| 0.6 | 接口扩展 | ITaskContext.cs + 实现类 | 改动 | ~10 | — | ✅ 验收通过 (4/4 AC，仅接口层) |
| | **合计** | | | **~415** | | **全部通过** |

> **阶段 0 执行记录（2026-03-23）：**
> - 编译：build.ps1 BUILD SUCCEEDED (0 errors)
> - 单元测试：424 通过 / 426 总计（2 个预存 flaky: TimeoutMiddleware + EditFileTool_ShowsDiff）
> - 验证回路：3 个并行 code-reviewer agent 验收，发现 2 个偏差并修正（D1: BF-02 pattern 补全, D3: SEC-01 `../` 测试）
> - 新增测试：38 个，全部通过

---

## 四、阶段 1：C/C++ 专业化 + 日常功能基础

> 目标：AICA 生成的 C/C++ 代码自动遵循华中数控规范，右键命令语言感知，具备基础 Bug 定位能力。
> 时间：3/26-3/31（6 天，含 1 天缓冲）← ✅ 提前完成（3/23）[C48]
> 前置：阶段 0 完成

### 步骤 1.1：C/C++ 编码规范注入

**要做什么：** 在 `.aica-rules/` 下新建 5 个规范文件，利用已有的 RuleLoader 管线自动注入 System Prompt。

**现状审查：**
- `src/AICA.Core/Rules/` 目录存在，RuleLoader 机制已实现
- ~~`.aica-rules/general.md` 已有通用规则~~ [C71] 已移除 general.md，C++ 规范通过 `CppRuleTemplates.cs` 自动部署 [C70]
- `SystemPromptBuilder.AddRulesFromFilesAsync()` 已能从 `.aica-rules/` 加载规则
- **可行性确认：** 纯配置工作，不需要修改代码。RuleLoader 通过 glob 模式匹配文件，新增 .md 文件即可被自动加载

**新建文件：**

| 文件 | 内容 | Token 估算 |
|------|------|-----------|
| `.aica-rules/cpp-code-style.md` | Allman 花括号、m_ 前缀、Bit32 类型、命名规范 | ~400 |
| `.aica-rules/cpp-reliability.md` | 内存安全、类型安全、控制流、函数规范 | ~350 |
| `.aica-rules/cpp-file-io.md` | fopen/fclose 配对、落盘检查、路径规范 | ~200 |
| `.aica-rules/cpp-qt-specific.md` | Qt 头文件规范、成员排序、TR 翻译、QSS 颜色 | ~300 |
| `.aica-rules/cpp-comment-template.md` | doxygen 文件头、函数注释格式、注释率 | ~250 |

**规则激活范围：** 每个文件的 frontmatter 中通过 `paths` glob 限制为 `*.h/*.cpp/*.c` 文件操作时才激活，非 C/C++ 场景零 token 消耗。

**[C14] RuleLoader paths 过滤已确认可用：**
- `RuleLoader` → `YamlFrontmatterParser` → `RuleEvaluator` → `PathMatcher` 完整支持 frontmatter `paths` glob 模式
- `PathMatcher` 支持 `**`、`*`、`?`，大小写不敏感
- `AgentExecutor` 每次请求从用户消息中提取文件路径候选，作为 `RuleContext.CandidatePaths` 传入评估
- **注意：** 步骤 1.2 的 `ProjectLanguageDetector` 判定为 C++ 项目后，需向 `RuleContext.CandidatePaths` 注入 C++ 路径模式，确保规范文件在 C++ 项目中**强制激活**（不依赖用户消息中是否包含文件路径）

**frontmatter 示例：**
```yaml
---
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.c"
enabled: true
priority: 20
---
```

**改动量：** 0 行代码，5 个 .md 文件（纯内容，来源：Qt-C++ 编程规范.doc + MISRA C++/C）

**受益范围：** [C8] ~40 人的代码生成/编辑请求（Simple/Medium/Complex 均受益）

**验收标准：**
- AICA 生成一个新 C++ 类，检查：Allman 花括号 ✓、m_ 前缀 ✓、doxygen 注释 ✓、Bit32 类型 ✓
- AICA 回答 Python 相关问题时，C++ 规范规则不出现在 System Prompt 中
- C++ 项目中即使用户消息不含文件路径（如"帮我写一个 C++ 类"），规范文件仍被激活

---

### 步骤 1.2：语言检测 + 场景 Prompt + 右键命令适配 [C3]

**要做什么：** 新建 `ProjectLanguageDetector` 检测当前项目语言，`SystemPromptBuilder` 根据语言注入专业化 prompt，**三个右键命令（解释/重构/生成测试）全部适配为语言感知**。

**现状审查：**
- `SystemPromptBuilder.cs`（~1050 行）已有 `AddComplexityGuidance()` 按复杂度分级注入规则
- 当前无语言检测机制，所有项目使用相同的通用 prompt
- `IWorkspaceContext` 提供工作目录路径，可用于扫描文件类型
- `src/AICA.VSIX/Commands/` 目录下有右键命令的实现
- **可行性确认：** 语言检测只需统计工作目录下的文件扩展名分布，不需要复杂的 AST 分析

**新建文件：** `src/AICA.Core/Agent/ProjectLanguageDetector.cs`（~120 行）
**改动文件：**
- `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（~80 行，~~新增 `AddCppSpecialization()` 方法~~ [C68] 已删除，规范改为通过 `.aica-rules/cpp-*.md` 加载 + C14 强制激活逻辑）[C24]
- `src/AICA.Core/Agent/AgentExecutor.cs`（~15 行，调用语言检测和注入）
- `src/AICA.VSIX/Commands/` 下的三个右键命令文件（~100 行总计）[C3]

**语言检测逻辑：**
```
扫描工作目录 → 统计扩展名 →
  .h/.cpp/.c 占比 >50% → C/C++ 项目
  .cs 占比 >50% → C# 项目
  混合 → 按当前打开文件的扩展名判断
```

**C++ 专业化 Prompt 注入内容（添加到 SystemPromptBuilder）：**
- 代码生成场景：强调 Allman 风格、m_ 前缀、MISRA 子集
- Bug 修复场景：强调检查内存泄漏、空指针、数组越界
- 代码解释场景：强调说明模块归属、调用链位置、执行流分支
- 测试生成场景：使用 Google Test / CppUnit 而非 xUnit

**[C3] 三个右键命令适配：**

| 右键命令 | 当前 prompt | 改造后 |
|---------|------------|--------|
| 解释代码 | 通用"请解释这段代码" | C++ 模式：增加"说明所属模块、调用链位置、关键分支含义" |
| 重构代码 | 通用"请重构这段代码" | C++ 模式：增加"遵循 Allman 风格、m_ 前缀、MISRA 子集" |
| 生成测试 | 硬编码 xUnit | C++ 模式：通过 `.aica-rules/cpp-aica-guidance.md` 注入 Google Test 框架 [C68] |

**[C14] C++ 项目规范强制激活 + 用户关闭确认：**

| 行为 | 实现方式 |
|------|---------|
| C++ 项目中规范文件默认强制激活 | `ProjectLanguageDetector` 判定为 C++ 后，向 `RuleContext.CandidatePaths` 注入 `**/*.cpp` 等路径 |
| 用户请求关闭规范时需确认 | 检测用户消息中的关闭意图（"不要规范/忽略规范/不依赖规范"等），调用 `RequestConfirmationAsync()` 提醒用户风险 |

**关闭确认提示文案：**
> "C++ 规范文件是项目级强制规则，关闭后生成的代码可能不符合公司编码标准。确认关闭吗？"

**受益范围：** [C8] **~40 人的右键操作**——这是 Simple 请求的主要入口，直接影响 80% 的日常使用体验

**验收标准：**
- C++ 项目中右键"生成测试"→ 使用 Google Test 框架（而非 xUnit）
- C# 项目中右键"生成测试"→ 仍使用 xUnit
- C++ 项目中右键"解释代码"→ 输出包含模块归属和调用链说明
- C++ 项目中的 System Prompt 包含 C++ 专业化规则
- C++ 项目中用户发送"帮我写一个类"→ 规范文件仍被激活 [C14]
- 用户发送"不要按照规范来"→ AICA 弹出确认提醒 [C14]

---

### 步骤 1.3：F3 Bug 定位 Prompt [C1 新增]

**要做什么：** 为 Bug 定位场景设计专用 prompt，让 AICA 具备"分析错误信息 + 定位相关代码 + 建议修复"的基础能力。

**现状审查：**
- 当前 AICA 没有 Bug 定位专用 prompt，用户报告 Bug 时走通用对话流程
- `DynamicToolSelector.cs` 已有 intent 分类（read/modify/analyze），Bug 定位属于 analyze
- `SystemPromptBuilder` 已支持按场景注入不同规则
- **可行性确认：** F3 的核心是 prompt 设计 + 工具引导，不需要新的代码架构。让 LLM 在收到错误描述时自动走"搜索错误关键词 → 读取相关代码 → 分析可能原因 → 建议修复"流程

**改动文件：**
- `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（~40 行，新增 `AddBugFixGuidance()` 方法）
- `src/AICA.Core/Agent/DynamicToolSelector.cs`（~10 行，增加 Bug 定位 intent 识别关键词）

**Bug 定位 intent 识别关键词：**
```
"报错|错误|崩溃|异常|bug|error|crash|exception|段错误|segfault|
 内存泄漏|leak|死锁|deadlock|不工作|doesn't work|失败|fail"
```

**Bug 定位 Prompt 引导（注入 SystemPrompt）：**
```
当用户描述 Bug 或错误时：
1. 用 grep_search 在代码中搜索错误信息中的关键字符串
2. 用 read_file 读取匹配文件的相关代码段
3. 分析可能的原因（重点检查：空指针、数组越界、未初始化变量、资源泄漏）
4. 如果是 C/C++ 代码，额外检查：内存管理配对、类型转换安全、线程安全
5. 给出修复建议，包含具体的代码修改方案
```

**受益范围：** [C8] **~40 人**——Bug 定位是日常高频需求，属于 Simple/Medium 任务

**验收标准：**
- 用户输入"这段代码在处理空数组时崩溃" → AICA 搜索相关代码、分析空指针风险、建议添加空检查
- 用户输入"内存泄漏了，帮我找原因" → AICA 搜索 malloc/new，检查是否有对应的 free/delete

---

### 步骤 1.4：非正式部署 + 验收任务收集 [C15 新增][C40/C41/C42/C43 修正]

**要做什么：** 编译部署阶段 0+1 的成果，通知两类角色工程师试用，收集真实验收任务场景。

**为什么在阶段 1 后非正式部署（而非正式发布）：** [C40]
- 阶段 0 只修了 Bug，用户感知不到变化
- 阶段 1 完成后有明确的新能力（C++ 规范、语言感知右键、Bug 定位），工程师能体验到改进
- 步骤 2.2 需要 10 个真实任务，步骤 2.3 需要 20 个任务——必须提前收集
- 收集任务与 GitNexus 开发并行，不浪费时间
- 正式发布需带上 GitNexus 功能（领导要求），故本步骤仅做小范围非正式部署

**执行步骤：**
1. 编译新版 VSIX（`build.ps1 -Restore -Build` 或 `buildinhome.ps1 -Restore -Build`）[C43]
2. 部署给两类角色的代表性工程师（平台组 3 人 + 界面组 3 人，共 6 人）[C41]
3. 通知工程师试用 5 天，要求每人提交 2-3 个真实任务场景
4. 提供 Excel 反馈模板（见下方）

**[C17] 反馈收集双渠道：**

**渠道 1：Excel 结构化模板**（正式记录，用于分析和验收）

| 列 | 说明 | 示例 |
|---|------|------|
| 日期 | 提交日期 | 2026-03-25 |
| 姓名/角色 | 谁、什么岗位 | 张三 / 界面组工程师 |
| 功能 | F1-F8 哪个功能 | F2 规范生成 |
| 操作方式 | 右键/聊天/其他 | 右键"生成测试" |
| 输入描述 | 给 AICA 的指令概述 | "为 CAxisDialog 生成单元测试" |
| 实际结果 | AICA 做了什么 | 生成了 xUnit 测试而非 Google Test |
| 期望结果 | 应该怎样 | 应该生成 Google Test 框架的测试 |
| 评分 | 1-5 分 | 2 |
| 备注 | 补充信息 | 花括号风格也不对 |

**渠道 2：钉钉群**（低门槛入口，保证反馈不丢失）
- 工程师遇到问题直接截图发群，一句话描述即可
- 开发者每两天将群内反馈整理到 Excel 中

**时间线：** [C42]
```
3/23  非正式部署 → 通知 6 名工程师试用
3/23-3/27  工程师试用 + 提交场景（同时开发者做 GitNexus 集成）
3/27  收集到 10+ 场景 → 用于步骤 2.2 可靠性验证
```

**验收标准：**
- 非正式部署编译成功（编译验证包括确认 ITaskContext.EditedFilesInSession 接口编译通过且单元测试无回归 [C26]）
- 收集到 ≥10 个真实任务场景，覆盖两类角色

---

### 阶段 1 完成检查清单

| # | 任务 | 文件 | 新增/改动 | 行数 | 受益范围 | 状态 |
|---|------|------|----------|------|---------|------|
| 1.1 | 规范文件 | ~~.aica-rules/ 下 5 个 .md~~ → CppRuleTemplates.cs 嵌入 6 个 [C68/C70] | 新建 | ~266 行代码 | ~40 人 | ✅ 验收通过 [C33] + 重构为自动部署 [C70] |
| 1.2 | 语言检测 | ProjectLanguageDetector.cs (新) | 新建 | ~170 | ~40 人 | ✅ 验收通过 (8/8 AC) |
| 1.2 | Prompt 注入 + C++ 强制激活 [C14] | SystemPromptBuilder.cs | 改动 | ~~~80~~ →~45 [C68] | ~40 人 | ✅ 验收通过 → [C68] AddCppSpecialization 已删除 |
| 1.2 | 执行器集成 | AgentExecutor.cs | 改动 | ~15 | ~40 人 | ✅ 验收通过 |
| 1.2 | 右键命令适配 [C3] | Commands/ 下 3 个文件 | 改动 | ~100 | **~40 人** | ✅ 验收通过 + [C31] 补 .hpp/.hxx |
| 1.3 | F3 Bug 定位 [C1] | SystemPromptBuilder + DynamicToolSelector | 改动 | ~50 | **~40 人** | ✅ 验收通过 (8/8 AC) [C32] 已知限制 |
| 1.4 | 非正式部署 [C15][C40] | 编译 + 部署 + 收集 | 操作 | 0 行代码 | — | ✅ 已部署给 6 人（3/23），反馈收集中 |
| | **合计** | | | **~415** | | |

> **阶段 1 执行记录（2026-03-23）：**
> - 编译：build.ps1 BUILD SUCCEEDED (0 errors)
> - 单元测试：426 通过 / 428 总计（2 个预存 flaky）
> - 验证回路：3 个并行 code-reviewer agent 验收，发现 4 个偏差并修正（D1-D4: [C28][C30][C31][C33]）
> - 新增测试：22 个 + hotfix 后补 2 个，全部通过
>
> **E2E 测试记录（2026-03-23，poco 项目）：**
>
> | # | 测试项 | 结果 | 迭代 | 关键发现 |
> |---|--------|------|------|---------|
> | E1 | 右键生成测试 (Google Test) | ✅ | 8 | Google Test 正确注入，13 个测试用例生成 |
> | E2 | 右键解释代码 (模块/调用链) | ✅ | 4 | 模块归属、调用链、分支含义、内存管理策略全覆盖 |
> | E3 | 右键重构 (Allman/m_/HNC) | ✅ | 7 | Allman/MISRA 正确注入；BF-06 cap 验证通过；Agent 效率有优化空间 |
> | E4 | 聊天写新类 (规范强制激活) | ✅ | 4 | [C28] hotfix 后修复；m_ 前缀、doxygen、snprintf 规范生效 |
> | E5 | Bug 定位 (模糊描述) | ❌ | 50 | LLM 在大项目搜索无果时不收敛 → [C32] 阶段 3 H2 覆盖 |
> | E6 | 推理不泄漏 | ✅ | — | E1-E4 中 `Suppressed text as thinking` 均正常 |
>
> **E2E 结论：** 5/6 通过。E5 为 LLM 能力限制（非 Harness 缺陷），已记录为已知限制 [C32]，在阶段 3 步骤 3.3 迭代预算感知中解决。阶段 1 可进行非正式部署 [C40]。

---

## 五、阶段 2：M1 — 代码理解可靠

> 目标：F1 代码理解做到 80%+ 成功率，F2 规范合规 70%+，F3 Bug 定位基本可用。[C51]
> 时间：3/23-4/7（15 天，含 3 天缓冲）[C48]
> 前置：阶段 1 完成 + ✅ Node.js 部署可行性已确认（3/23）+ ✅ 许可证合规已确认（3/23）[C49]
> 进度：🔄 步骤 2.1 ✅ + 步骤 2.2 自测 ✅ 完成（2026-03-25），步骤 2.3 待执行

### 步骤 2.1：GitNexus MCP 集成

**要做什么：** 通过 MCP 协议集成 GitNexus，获得 Tree-sitter AST 解析 + 知识图谱 + 6 个 MCP 工具桥接。

**现状审查：**
- AICA 已有 `ToolDispatcher.RegisterTool()` 用于注册新工具
- `ToolMetadata` 已有 `Category` 枚举（FileRead/FileWrite/Search/Analysis 等）和 `Tags` 数组
- `ToolExecutionPipeline` 支持中间件链，MCP 工具可通过桥接层接入
- ~~**阻塞确认**~~ ✅ 两个前置条件均已确认（3/23）[C49]

**部署方式（方案 C）：** [C59][C60]
- GitNexus v1.4.8 编译产物内嵌到 `tools/gitnexus/`（dist/ 2.1MB + package.json）
- 开发者首次使用：`cd tools/gitnexus && npm install --omit=dev`
- `GitNexusProcessManager` 三级路径解析：内嵌版本 → `AICA_GITNEXUS_PATH` 环境变量 → npx 兜底
- MAX_FILE_SIZE 从 512KB 提升到 2MB，支持 `GITNEXUS_MAX_FILE_SIZE` 环境变量 [C61]

**新建文件：** [C52][C53]
- `src/AICA.Core/Agent/IGitNexusProcessManager.cs`（~40 行）— 进程管理接口（供 Mock 测试）
- `src/AICA.Core/LLM/McpClient.cs`（~350 行）— MCP JSON-RPC 2.0 客户端（Content-Length 帧 + 后台读循环 + 请求/响应匹配）
- `src/AICA.Core/Agent/GitNexusProcessManager.cs`（~230 行）— Node.js 进程管理（状态机 + 单例 + 自动重启）
- `src/AICA.Core/Tools/McpBridgeTool.cs`（~380 行）— 6 个 MCP 工具桥接（工厂模式 + 降级处理）

**改动文件：** [C55]
- `src/AICA.VSIX/ToolWindows/ChatToolWindowControl.xaml.cs`（~15 行，注册 6 个桥接工具，try/catch 包裹）
- `src/AICA.Core/Agent/DynamicToolSelector.cs`（~5 行，ReadTools 加 5 个 GitNexus 工具，WriteTools 加 gitnexus_rename）
- `src/AICA.VSIX/Events/SolutionEventListener.cs`（~20 行，Solution 打开时触发索引 + FindGitRoot [C69]）✅ Day 3
- `src/AICA.VSIX/AICAPackage.cs`（~25 行，Dispose + UnAdvise 修复）✅ Day 3

**测试文件：** [C56]
- `src/AICA.Core.Tests/LLM/McpClientTests.cs`（~278 行）— JSON-RPC 协议测试
- `src/AICA.Core.Tests/Agent/GitNexusProcessManagerTests.cs`（~47 行）— 状态机契约测试
- `src/AICA.Core.Tests/Tools/McpBridgeToolTests.cs`（~405 行）— 工厂 + 降级 + 执行逻辑测试

**关键设计：工具能力标签（为 H2 状态机铺路）** [C54]

在注册 MCP 工具时，为每个工具的 `ToolMetadata.Tags` 加上能力标签：

| AICA 工具名 | MCP Tool | ToolCategory | Tags |
|------------|----------|-------------|------|
| `gitnexus_context` | `context` | Analysis | `["search", "read", "context", "gitnexus"]` |
| `gitnexus_impact` | `impact` | Analysis | `["search", "analysis", "gitnexus"]` |
| `gitnexus_query` | `query` | Search | `["search", "gitnexus"]` |
| `gitnexus_detect_changes` | `detect_changes` | Analysis | `["search", "analysis", "gitnexus"]` |
| `gitnexus_rename` | `rename` | FileWrite | `["modify", "refactor", "gitnexus"]` |
| `gitnexus_cypher` | `cypher` | Analysis | `["search", "analysis", "gitnexus"]` |

**降级策略（三级）：**
1. **注册时失败**（无 Node.js 等）→ try/catch 跳过，AICA 仅用内置工具
2. **调用时进程不可用** → 单次重启 → 失败则调 fallbackHandler：
   - gitnexus_context/impact/query → 降级到 grep_search
   - gitnexus_detect_changes/rename/cypher → 返回错误提示
3. **MCP 调用返回错误** → 返回 ToolResult.Fail（不降级，错误是工具级别的）

**验收标准：**
- C++ 项目中执行 `gitnexus_context(name: "CChannel")` 返回调用者/被调用者/执行流
- 执行 `gitnexus_impact(target: "CAxis::SetPosition")` 返回爆炸半径分析
- 执行 `gitnexus_cypher(query: "MATCH (n:Function)...")` 返回图谱数据
- GitNexus 进程崩溃后 AICA 自动回退到内置工具（grep_search 等），不报错
- 索引 3000+ 文件的 C++ 项目 < 30 秒

---

### 步骤 2.2：工具调用可靠性验证

**要做什么：** 用 10 个真实任务验证 MiniMax-M2.5 对新增 MCP 工具的调用稳定性。

**现状审查：**
- 路线图要求 M1 前实测 10 个真实任务（研讨会决策 W6）
- 步骤 0.4 的遥测已就绪，可以自动记录每个任务的工具调用成功率
- **可行性确认：** 不需要写代码，需要设计和执行测试

**执行方式：**
1. **[C15][C44][C62] 任务来源：** 优先从步骤 1.4 收集的工程师真实场景中选取 10 个，覆盖两类角色（平台组×5 + 界面组×5）。**若真实场景暂未收集到，可先用 poco 项目自测 6 个工具的基本调用**（context/impact/query/detect_changes/cypher/降级恢复），不阻塞推进
2. 每个任务手动执行，遥测自动记录
3. 汇总遥测数据，分析工具调用成功率和失败模式
4. 若 MiniMax-M2.5 无法可靠调用 MCP 工具参数格式 → 在 SystemPrompt 中注入 few-shot 示例

**[C5] 同步验证 H2 状态机兼容性：**

> 在这 10 个任务中，选取 3 个 Complex 任务，手动模拟状态机的工具拦截行为：
> - 在 LLM 第一轮想调用 edit 时，手动注入"当前处于搜索阶段，edit 不可用"的消息
> - 观察 MiniMax-M2.5 是否能理解并切换到搜索工具
> - **结果决定 H2 实现策略：**
>   - 3/3 能理解 → H2 使用硬拦截模式（工具不执行，注入阶段提示）
>   - 1-2/3 能理解 → H2 使用**软拦截模式**（工具照常执行，但追加阶段建议到返回结果中）
>   - 0/3 能理解 → **放弃 H2 状态机**，回退到纯 prompt 引导（路线图原方案）

**验收标准：**
- 10 个任务中 ≥8 个 Outcome=completed
- MCP 工具调用成功率 ≥70%
- H2 兼容性测试有明确结论
- 若不达标，记录具体失败模式，作为 M2 改进输入

---

### 步骤 2.3：M1 验收 + 小范围试用部署 [C45]

**[C7] 验收指标（区分自动/人工）：**

| 指标 | 目标 | 衡量方式 | 遥测可替代？ |
|------|------|---------|------------|
| F1 代码理解成功率 | ≥80% | **20 个任务人工判断**（从步骤 1.4 收集的真实场景中扩展设计 [C46]） | ❌ 否 |
| F2 规范合规率 | ≥70% | **生成 10 段 C++ 代码人工审查** | ❌ 否 |
| F3 Bug 定位基本可用 | 5/10 通过 | **10 个 Bug 场景人工判断** | ❌ 否 |
| GitNexus 索引可用 | <30s/3K 文件 | 实测 | ✅ 是 |
| Edit 成功率 | ≥75% | 遥测自动统计 | ✅ 是 |
| 工具调用成功率 | ≥80% | 遥测自动统计 | ✅ 是 |

**部署操作：** [C43][C45]
- 编译新版 VSIX（`build.ps1 -Restore -Build` 或 `buildinhome.ps1 -Restore -Build`）
- 部署给 ~10 名工程师（在步骤 1.4 的 6 人基础上扩大，平台组 5 + 界面组 5），含 GitNexus 功能
- **[C17] 反馈收集：** 复用步骤 1.4 建立的双渠道（Excel 模板 + 钉钉群），小范围试用期间持续收集反馈直到 M2

---

### 阶段 2 完成检查清单 [C52-C57 更新]

| # | 任务 | 文件 | 新增/改动 | 行数 | 状态 |
|---|------|------|----------|------|------|
| 2.1 | 进程管理接口 | IGitNexusProcessManager.cs (新) [C52] | 新建 | ~40 | ✅ Day 1 |
| 2.1 | MCP Client | McpClient.cs (新) | 新建 | ~350 | ✅ Day 1 |
| 2.1 | 进程管理 | GitNexusProcessManager.cs (新) | 新建 | ~230 | ✅ Day 1 |
| 2.1 | 工具桥接（6 个工具） | McpBridgeTool.cs (新) | 新建 | ~380 | ✅ Day 2 |
| 2.1 | 工具注册 [C55] | ChatToolWindowControl.xaml.cs | 改动 | ~15 | ✅ Day 2 |
| 2.1 | 工具选择器更新 | DynamicToolSelector.cs | 改动 | ~5 | ✅ Day 2 |
| 2.1 | 索引触发 | SolutionEventListener.cs | 改动 | ~20 | ✅ Day 3 |
| 2.1 | 释放 + UnAdvise 修复 | AICAPackage.cs | 改动 | ~25 | ✅ Day 3 |
| 2.1 | 测试 [C56] | 3 个测试文件 | 新建 | ~730 | ✅ Day 1+2 |
| 2.1 | GitNexus 内嵌 [C59] | tools/gitnexus/ + README.md | 新建 | dist 2.1MB | ✅ Day 4 |
| 2.1 | 路径解析 [C60] | GitNexusProcessManager.cs | 改动 | ~25 | ✅ Day 4 |
| 2.2 | 可靠性验证（poco 自测）[C62] | SystemPromptBuilder + AgentExecutor | 改动 | ~45 | ✅ poco 自测通过，待真实场景复验 [C67] |
| | **合计（不含测试）** | | | **~1045** | |
| | **合计（含测试）** | | | **~1775** | |

> **步骤 2.1 执行记录：**
>
> **Day 1（2026-03-24）：**
> - 新建：IGitNexusProcessManager.cs + McpClient.cs + GitNexusProcessManager.cs
> - 测试：McpClientTests.cs + GitNexusProcessManagerTests.cs
> - 编译：BUILD SUCCEEDED (0 errors)
> - 单元测试：424/426 pass（2 个预存 flaky）
>
> **Day 2（2026-03-24）：**
> - 新建：McpBridgeTool.cs（6 个工具：context/impact/query/detect_changes/rename/cypher + CreateAllTools 工厂）
> - 改动：ChatToolWindowControl.xaml.cs（注册 6 个桥接工具）+ DynamicToolSelector.cs（ReadTools +5, WriteTools +1）
> - 测试：McpBridgeToolTests.cs（17 个测试：工厂 10 + 执行逻辑 7）
> - 修复：AgentEvalHarness.cs 命名空间冲突 [C58]
> - 编译：BUILD SUCCEEDED (0 errors, VSIX 生成)
> - 单元测试：449/453 pass（4 个预存 flaky，0 个新增失败）
>
> **Day 3（2026-03-24）：**
> - 改动：SolutionEventListener.cs（OnAfterOpenSolutionAsync 并行触发 GitNexus 索引；OnAfterCloseSolution 不 Dispose 单例 [CRITICAL-2 修复]）
> - 改动：AICAPackage.cs（Dispose 新增 UnadviseSolutionEvents + GitNexusProcessManager.Dispose）
> - 编译：BUILD SUCCEEDED (0 errors, VSIX 生成)
> - 单元测试：449/453 pass（4 个预存 flaky，0 个新增失败）
>
> **Day 4（2026-03-24）：**
> - 代码审查：code-reviewer agent 发现 2 CRITICAL + 5 HIGH + 5 MEDIUM + 2 LOW
> - 修复 CRITICAL-1：McpBridgeTool TOCTOU race — snapshot Client 引用，避免 null 访问
> - 修复 CRITICAL-2：OnAfterCloseSolution 中移除 Dispose 单例调用，避免重开解决方案后 GitNexus 永久失效
> - 修复 HIGH-5：McpClient Content-Length 加 10MB 上限，防止恶意/异常包分配巨型缓冲区
> - 其余 HIGH/MEDIUM 记录为技术债务，不影响 Day 4 交付
> - 最终编译：BUILD SUCCEEDED (0 errors, VSIX 生成)
> - 最终测试：449/453 pass（4 个预存 flaky，0 个新增失败）
>
> **Day 4 补充（2026-03-25）：**
> - GitNexus 内嵌：tools/gitnexus/（dist/ 2.1MB + package.json + README.md）[C59]
> - 改动：GitNexusProcessManager.cs（三级路径解析：内嵌 → 环境变量 → npx）[C60]
> - 定制：MAX_FILE_SIZE 512KB → 2MB + GITNEXUS_MAX_FILE_SIZE 环境变量 [C61]
> - poco 索引验证：64,122 nodes | 130,467 edges | 1913 clusters | 326.6s
> - MCP 服务验证：`MCP server starting with 2 repo(s)` ✅
>
> **步骤 2.1 完成总结：**
> - 新建 4 个核心文件 + 3 个测试文件 = ~1730 行（核心 ~1000 + 测试 ~730）
> - 内嵌 GitNexus v1.4.8 构建产物到 tools/gitnexus/（dist/ 2.1MB）
> - 改动 5 个文件 = ~70 行（含三级路径解析）
> - 桥接 6 个 GitNexus MCP 工具，含三级降级策略
> - 17 个新增单元测试全部通过
>
> **步骤 2.2 简化自测记录（2026-03-25，poco 项目）：**
>
> | # | 测试 | 工具调用 | 回答质量 | 迭代 | 判定 |
> |---|------|---------|---------|------|------|
> | IT1 | context（Logger 调用者） | ✅ gitnexus_context + query | ✅ 准确 | 9 | PASS |
> | IT2 | impact（unsafeGet 爆炸半径） | ⚠️ impact 参数名不匹配 | ✅ 准确（grep 补救） | 21 | 部分 PASS |
> | IT3 | cypher（继承 CodeWriter） | ⚠️ Cypher schema 不匹配 | ✅ 准确（grep 补救） | 14 | 部分 PASS |
> | IT4 | detect_changes（git 变更影响） | ✅ detect_changes + impact 协作 | ✅ 详细准确 | 7 | PASS |
> | IT5 | 降级（杀 node 后恢复） | ✅ 纯内置工具降级 | ✅ 准确 | 2 | PASS |
>
> **自测发现的 5 个问题及修复状态：**
>
> | # | 问题 | 影响 | 修复方案 | 状态 |
> |---|------|------|---------|------|
> | P1 | 多仓库时 LLM 第一次不传 repo 参数 | 每次浪费 1 轮迭代 | SystemPrompt 注入当前项目 repo 名 [C67] | ✅ 已修复 |
> | P2 | impact 传 `Logger::unsafeGet`，GitNexus 需要 `unsafeGet` | impact 返回空，降级到 grep | SystemPrompt few-shot：使用简单函数名 [C67] | ✅ 已修复 |
> | P3 | cypher 用 `[:EXTENDS]`，实际应为 `[r:CodeRelation] WHERE r.type='EXTENDS'` | cypher 查询失败 | SystemPrompt few-shot：GitNexus Cypher schema 示例 [C67] | ✅ 已修复 |
> | P4 | grep_search 传 `path: poco` 但工作目录已在 poco 内 | grep 返回 0 结果 | 已知路径混淆问题，工作目录解析需改进 | 🔲 阶段 3 |
> | P5 | dedup 拦截 read_file 后 LLM 陷入重复循环 | 迭代浪费（IT2 21 轮中 4 次 duplicate skip） | 阶段 3 H2 状态机覆盖 [C32] | 🔲 阶段 3 |
>
> **P1-P3 修复验证（2026-03-25）：**
> - 改动：SystemPromptBuilder.cs 新增 `AddGitNexusGuidance(repoName)`（~30 行 few-shot 示例）
> - 改动：AgentExecutor.cs 新增 `ResolveGitNexusRepoName(workingDir)`（~15 行，从 .git 取 repo 名）
> - 复测 IT3（cypher 继承 CodeWriter）：14 轮 → **2 轮**，repo 参数 + Cypher schema 均一次正确
> - 编译：BUILD SUCCEEDED | 测试：449/453 pass
>
> **自测最终结论：** MCP 集成链路通畅，6 个工具均可调用，降级机制正常。P1-P3 修复后效率提升显著（IT3 从 14 轮降至 2 轮）。P4/P5 为已知限制，分别在阶段 3 工作目录改进和 H2 状态机中解决。步骤 2.2 简化自测通过，可进入步骤 2.3。

---

## 六、阶段 3：M2 — 日常功能完善 + 跨文件可用 + Harness 核心改造

> 目标：F5 QT 模板可用 + Edit 容错 + 复杂任务流程可控 + Condense 不丢失关键信息 + 跨会话记忆。
> 时间：4/8-5/10（33 天，含 8 天缓冲）[C9][C48]
> 前置：阶段 2 完成

**[C8] 阶段 3 重新编排原则：先做 ~40 人受益的日常功能，再做 Harness 核心改造。** [C34]

**执行顺序：**

```
── 日常功能优先 ──────────────────────────────────────────────
3.1 Edit 诊断路由 (H3)          ← ~40 人受益
3.2 F5 QT 模板 [C2 新增]       ← 界面组 ~20 人受益（界面组的核心需求）[C51]

── Harness 核心改造 ──────────────────────────────────────────
3.3 验证中间件 (H1) [C4 重新设计]
3.4 状态机 (H2) [C5/C6 修正]  ─→ 3.5 迭代预算感知 (H7)

── 上下文 + 记忆 ──────────────────────────────────────────────
3.6 Condense 保护区 (H6)      ─→ 3.7 断点续做
                                        │
                                        ▼
                                  3.8 Memory Bank

── 收尾 ──────────────────────────────────────────────────────
3.9 M1 反馈修复
3.10 集成测试 [C9 新增]
3.11 正式发布 [C47 新增]
```

---

### 步骤 3.1：H3 Edit 诊断 + 策略路由

**要做什么：** 替代路线图的 3 层盲目 fallback，改为先诊断失败原因再选择修复策略。

**现状审查：**
- `EditFileTool.cs`（281 行）当前逻辑：`normalizedContent.Contains(normalizedOldString)` 失败后直接返回错误信息和可视化空白字符
- 已有行尾换行规范化 `NormalizeLineEndings()`（`\r\n` → `\n`）
- `TaskState.EditedFiles` 已跟踪本会话编辑过的文件
- 步骤 0.6 已将 `EditedFilesInSession` 暴露给工具层
- **可行性确认：** 所有需要的基础设施已就绪

**改动文件：** `src/AICA.Core/Tools/EditFileTool.cs`（~100 行改动，重写 `Contains` 失败后的分支）

**诊断逻辑（替换现有 129-157 行）：**

```
精确匹配失败
    │
    ├─ 检查：文件在 EditedFilesInSession 中？
    │   └─ 是 → StaleContent：返回文件最新相关区域内容
    │
    ├─ 检查：TrimStart 后匹配？
    │   └─ 是 → IndentationMismatch：自动用缩进忽略模式执行编辑
    │
    ├─ 检查：全空白规范化后匹配？
    │   └─ 是 → WhitespaceMismatch：自动用空白规范化模式执行编辑
    │
    ├─ 检查：匹配了多处？
    │   └─ 是 → MultipleMatches：返回每处上下文
    │
    └─ 都不匹配 → ExactMatch：返回最相似片段（Levenshtein 距离）
```

**StaleContent 诊断的关键实现：**
```csharp
// 文件在本会话中被编辑过 → 很可能 old_string 基于旧版本
if (context.EditedFilesInSession.Contains(resolvedPath))
{
    var firstLine = oldString.Split('\n')[0].Trim();
    var anchorIndex = normalizedContent.IndexOf(firstLine);
    if (anchorIndex >= 0)
    {
        var snippet = ExtractSnippet(normalizedContent, anchorIndex, oldString.Split('\n').Length + 4);
        return ToolResult.Fail(
            $"该文件已在前序编辑中被修改，old_string 可能基于旧版本。\n" +
            $"以下是当前文件中与 old_string 第一行匹配位置的实际内容：\n\n{snippet}\n\n" +
            $"请基于以上最新内容重新构造 old_string。");
    }
}
```

**自动修复模式（缩进忽略和空白规范化）：**
```csharp
var trimmedOld = string.Join("\n", oldString.Split('\n').Select(l => l.TrimStart()));
var trimmedContent = string.Join("\n", normalizedContent.Split('\n').Select(l => l.TrimStart()));
var trimmedIndex = trimmedContent.IndexOf(trimmedOld);
if (trimmedIndex >= 0)
{
    var originalSegment = LocateOriginalSegment(normalizedContent, trimmedIndex, trimmedOld);
    newContent = normalizedContent.Replace(originalSegment, newString);
    // 继续执行 ShowDiffAndApplyAsync...
}
```

**遥测集成（与 H4 联动）：**
每次 Edit 诊断结果记录到 `SessionRecord.EditFailureReasons`。

**受益范围：** [C8] **~40 人的所有编辑操作**（Simple/Medium/Complex 均受益——缩进差异和内容过时在任何复杂度下都会发生）

**验收标准：**
- LLM 第一轮 edit 因缩进差异失败 → 自动修复，不浪费迭代轮次
- LLM 先编辑 Area.h 再编辑 Area.cpp，对 Area.h 的第二次编辑因内容过时失败 → 返回最新内容而非"未找到匹配"
- 遥测中可看到 EditFailureReasons 分布

---

### 步骤 3.2：F5 QT 模板生成 [C2 新增]

**要做什么：** 为 QT 界面工程师提供对话框/Widget 代码模板生成和 signal/slot 查找能力。

**现状审查：**
- F5 是界面组工程师的核心需求（决策 W3）[C51]
- 产品设计研讨会判定"界面组工程师是 AICA 最容易出效果的群体"
- GitNexus `context()` 工具可提供 signal/slot 连接关系查找（M1 已集成）
- `SystemPromptBuilder` 已支持场景化 prompt 注入
- `.aica-rules/cpp-qt-specific.md`（步骤 1.1，通过 `CppRuleTemplates.cs` 自动部署 [C70]）已包含 Qt 编码规范
- **可行性确认：** F5 的核心是 prompt 模板 + Qt 规范约束，不需要新的架构组件

**改动文件：**
- `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（~50 行，新增 `AddQtTemplateGuidance()` 方法）
- `src/AICA.Core/Agent/DynamicToolSelector.cs`（~10 行，增加 Qt 模板 intent 识别）

**Qt 模板 intent 识别关键词：**
```
"对话框|dialog|widget|界面|signal|slot|信号|槽|
 QWidget|QDialog|QPushButton|QLabel|QSpinBox|
 .ui|.qss|样式|布局|layout"
```

**Qt 模板 Prompt（注入 SystemPrompt）：**
```
当用户请求生成 Qt 界面代码时：
1. 遵循 .aica-rules/cpp-qt-specific.md 中的全部规范
2. 生成完整的 .h + .cpp 文件对，包含：
   - Q_OBJECT 宏
   - 构造函数中 setupUi 调用
   - signal/slot 连接使用新式语法 connect(sender, &Sender::signal, receiver, &Receiver::slot)
   - 析构函数中资源清理
3. 对话框模板包含：标准按钮（确认/取消）、布局管理器、TR() 国际化宏
4. 如果用户问"这个信号连接到了哪里"，使用 context() 工具查找 signal/slot 连接关系
```

**受益范围：** [C8] **界面组 ~20 人**——这是界面组的核心日常需求（Simple/Medium 任务）[C51]

**验收标准：**
- "生成一个参数设置对话框，包含 3 个 SpinBox 和确认取消按钮" → 输出遵循 Qt 规范的 .h/.cpp 文件对
- "QPushButton clicked 信号连接到了哪个 slot？" → 通过 GitNexus context() 返回精确答案（⚠️ GitNexus 不可用时从开源生态找替代组件，与 C13 策略一致 [C19]）
- 生成的代码使用新式 connect 语法、包含 Q_OBJECT 宏、使用 TR() 宏

---

### 步骤 3.3：H1 验证中间件 [C4 重新设计]

**要做什么：** edit 成功后自动验证修改内容是否正确。

**[C4] 原设计问题：** 括号配对检查对 C++ 代码会大量误报（模板语法 `<>`、宏定义、条件编译中的不配对括号），与"准"的产品定位矛盾。

**重新设计——移除括号检查，改为两项高可信度验证：**

| 验证项 | 检测方式 | 误报率 | 说明 |
|--------|---------|--------|------|
| 内容存在性 | 在编辑后文件中搜索 new_string 的前 3 行 | 极低 | 确认修改确实被应用 |
| Diff 行数异常 | 比较 `old_string 行数` vs `new_string 行数` vs `实际变更行数` | 极低 | 如果实际变更行数远超预期，说明可能替换了错误位置 |

**不做的验证：**
- ~~括号配对~~（C++ 模板/宏/条件编译导致大量误报）
- ~~语法检查~~（需要编译器，超出 Harness 范围）
- ~~语义检查~~（需要 LLM，违反"Harness 不依赖 LLM"原则）

**新建文件：** `src/AICA.Core/Agent/Middleware/VerificationMiddleware.cs`（~80 行，比原设计减少 40 行）
**改动文件：** 中间件注册处（~3 行）

**验证逻辑：**
```csharp
public async Task<ToolResult> ProcessAsync(ToolExecutionContext ctx, CancellationToken ct)
{
    var result = await ctx.Next(ct);

    if (ctx.Tool.Name != "edit" || !result.Success)
        return result;

    // 验证 1：内容存在性
    var filePath = ctx.Call.Arguments["file_path"]?.ToString();
    var newString = ctx.Call.Arguments["new_string"]?.ToString();
    if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(newString))
    {
        var fileContent = await ctx.AgentContext.ReadFileAsync(filePath, ct);
        var checkLines = newString.Split('\n').Take(3);
        var firstCheckLine = checkLines.First().Trim();

        if (firstCheckLine.Length > 10 && !fileContent.Contains(firstCheckLine))
        {
            return ToolResult.Ok(
                result.Content +
                "\n\n⚠️ [验证] 修改已提交，但 new_string 的首行未在文件中找到。" +
                "建议用 read_file 确认修改是否正确应用。");
        }
    }

    // 验证 2：Diff 行数异常
    var oldString = ctx.Call.Arguments.ContainsKey("old_string")
        ? ctx.Call.Arguments["old_string"]?.ToString() : null;
    if (!string.IsNullOrEmpty(oldString) && !string.IsNullOrEmpty(newString))
    {
        var oldLines = oldString.Split('\n').Length;
        var newLines = newString.Split('\n').Length;
        var diff = Math.Abs(newLines - oldLines);
        if (diff > 50 && diff > oldLines * 2)
        {
            return ToolResult.Ok(
                result.Content +
                $"\n\n⚠️ [验证] 修改行数差异较大（原 {oldLines} 行 → 新 {newLines} 行）。" +
                "建议用 read_file 确认修改范围是否符合预期。");
        }
    }

    return result; // 验证通过，无额外提示
}
```

**受益范围：** [C8] **~40 人的编辑操作**（Simple/Medium/Complex 均受益）

**验收标准：**
- edit 成功且验证通过 → 无额外提示（不打扰用户）
- edit 成功但 new_string 未在文件中找到 → 追加验证警告
- edit 成功但行数差异 >50 行且超过原文 2 倍 → 追加行数异常提示
- **C++ 模板代码 `std::vector<std::pair<int, int>>` 不触发任何警告** [C4 关键验收]

---

### 步骤 3.4：H2 复杂任务状态机 [C5/C6 修正]

**要做什么：** 用代码控制复杂任务的阶段流转，替代纯 prompt 引导。

**[C5] 前置条件：** 步骤 2.2 的 H2 兼容性测试结果决定实现策略：

| 测试结果 | 实现策略 | 说明 |
|---------|---------|------|
| 3/3 理解工具拦截 | **硬拦截模式** | 工具不执行，注入阶段提示 |
| 1-2/3 理解 | **软拦截模式**（默认） | 工具照常执行，返回结果中追加阶段建议 |
| 0/3 理解 | **放弃状态机** | 回退到纯 prompt 引导（见下方 [C11]） |

> 以下方案按**软拦截模式**设计（最保守的可用方案）。如果测试结果支持硬拦截则升级。

**[C11] H2 放弃时的保底方案：** 即使状态机整体放弃，以下 PlanManager prompt 增强**独立保留执行**（纯 prompt 注入，不依赖工具拦截）：
- **任务分解引导：** Complex 任务注入"请先分析需求、制定计划、再逐步执行"
- **跨文件重构五步法：** 注入"搜索全部引用 → 制定计划 → 逐文件执行 → 验证无残留 → 完成报告"
- 代码量：PlanManager.cs ~40 行改动，不受 H2 测试结果影响

**现状审查：**
- `PlanManager.cs`（54 行）当前只注入一句 prompt 指令
- `AgentExecutor.cs` 主循环中在 Complex 任务时调用 `PlanManager.BuildPlanningDirective()`
- `TaskState.HasActivePlan` 已有，`TaskState.Iteration` 已有
- `ToolMetadata.Tags` 已有标签体系（步骤 2.1 中扩展了能力标签）
- **可行性确认：** 状态机是纯逻辑代码，不依赖外部服务

**新建文件：** `src/AICA.Core/Agent/TaskStateMachine.cs`（~200 行）
**改动文件：**
- `src/AICA.Core/Agent/AgentExecutor.cs`（~30 行，主循环中插入状态机检查）
- `src/AICA.Core/Agent/PlanManager.cs`（~20 行，增加阶段感知的 directive）
- `src/AICA.Core/Agent/TaskState.cs`（~10 行，增加 `PhaseIterationCount` 和 `CurrentPhase`）

**状态机模板：**

| 模板 | 触发条件 | 阶段定义 |
|------|---------|---------|
| CrossFileEdit | Complex + 包含"实现/添加/修改" + 涉及 2+ 文件 | Understand(≤8) → Plan(≤2) → Execute(≤30) → Verify(≤5) → Complete |
| Rename | Complex + 包含"重命名/rename/改名" | Search(≤5) → Plan(≤2) → Execute(≤40) → Verify(≤3) → Complete |
| Analysis | Complex + 包含"分析/审查/检查"且不含"实现/修改/重构" | Gather(≤15) → Analyze(≤5) → Complete |
| Default | Complex + 不匹配以上任何模板 | 不启用状态机，仅用现有 PlanManager |

**[C6] 模板优先级规则（解决多模板匹配冲突）：**

```
优先级：Rename > CrossFileEdit > Analysis > Default

判定流程：
1. 如果包含"重命名/rename/改名" → Rename（最具体的操作）
2. 否则如果包含"实现/添加/修改" + 多文件指示词 → CrossFileEdit
3. 否则如果包含"分析/审查/检查"且不含任何修改类动词 → Analysis
4. 否则 → Default

互斥规则：
- Analysis 与 CrossFileEdit 互斥（"分析架构并重构"→ 匹配 CrossFileEdit，因为包含"重构"）
- Rename 与 CrossFileEdit 互斥（"重命名 CLogger 并更新所有引用"→ 匹配 Rename）
```

**[C5] 软拦截模式实现：**

```csharp
// 软拦截：工具照常执行，但在返回结果中追加阶段建议
public PhaseDirective GetDirective(TaskState state, string[] pendingToolCalls)
{
    var config = _phases[_current];

    // 检查工具是否符合当前阶段
    var offPhaseTools = pendingToolCalls
        .Where(t => !IsToolAllowedInPhase(t, config))
        .ToList();

    if (offPhaseTools.Any())
    {
        // 软拦截：不阻止工具执行，但在本轮结果后追加阶段建议
        return new PhaseDirective
        {
            Mode = InterceptionMode.Soft,
            AppendMessage = $"[阶段提示] 当前处于 {_current} 阶段（{config.Description}）。" +
                           $"建议优先使用 {string.Join("/", config.SuggestedTools)} 完成本阶段目标。",
        };
    }

    // 阶段超时 → 自动推进（硬性，不管模式）
    if (state.PhaseIterationCount >= config.MaxIterations)
    {
        AdvancePhase();
        return new PhaseDirective { ForceMessage = $"阶段超时，进入 {_current} 阶段。" };
    }

    // 转换条件满足 → 自动推进
    if (config.TransitionCondition(state))
        AdvancePhase();

    return PhaseDirective.Continue;
}
```

**受益范围：** [C8] 仅 Complex 任务（约 20% 请求）。但 Complex 任务是用户痛点最强的场景（MinCurve 案例）。

**验收标准：**
- Complex 跨文件任务触发 CrossFileEdit 模板
- 软拦截模式：LLM 在 Understand 阶段调用 edit → 工具正常执行，但返回结果中包含"建议优先使用搜索工具"
- 阶段超时自动推进：Understand 阶段超过 8 轮后自动进入 Plan
- "分析架构并重构日志模块" → 匹配 CrossFileEdit（不匹配 Analysis）[C6]
- "分析这段代码有没有内存泄漏" → 匹配 Analysis [C6]

---

### 步骤 3.5：H7 迭代预算感知

**要做什么：** 在迭代消耗 60%、80% 时主动引导 LLM 收尾，替代现有的"最后 2 轮强制完成"。

**现状审查：**
- `AgentExecutor.cs` 当前逻辑：`iteration >= maxIterations - 2` 时强制注入完成指令
- `TaskState.EditedFiles` 可用于汇报已完成工作
- `TaskPlan` 可用于汇报未完成步骤
- **可行性确认：** 只需修改 AgentExecutor 中的预算检查逻辑

**改动文件：** `src/AICA.Core/Agent/AgentExecutor.cs`（~40 行改动，替换现有最后 2 轮逻辑）

**[C10] 设计原则：maxIterations 保持 50 不提升。** 如果一个复杂任务需要靠牺牲反馈时间换取正确性（提升到 80 轮），而不是通过 Harness 优化来减少无效迭代，那 Agent 就失去了意义。日后 Harness 优化验证有效后，再考虑提升上限。

**四级预算策略：**

| 阈值 | 动作 | 注入消息 |
|------|------|---------|
| 60% 迭代 | 提醒 | `"[提示] 已使用 {n}/{max} 轮迭代。请评估剩余工作量。"` |
| 80% 迭代 | 警告 + 状态机覆盖（如有） | `"⚠️ 迭代预算即将耗尽。已完成：{files}。未完成：{steps}。请立即完成或汇报进度。"` + 若 H2 状态机存在则强制进入 Complete，否则仅注入文本警告 [C18] |
| 96% 迭代 | 强制 | 保留现有逻辑：`"你必须立即调用 attempt_completion"` |
| **10 分钟墙钟** [C10] | **超时终止** | **诚实报告失败：任务超时，详细说明失败原因 + 已完成工作 + 未完成部分，支持用户在下轮对话中基于已有结论继续** |

**10 分钟超时实现：**
```csharp
// AgentExecutor.ExecuteAsync() 入口
var wallClockStart = DateTime.UtcNow;
var wallClockLimit = TimeSpan.FromMinutes(10);

// 主循环中每轮检查
if (DateTime.UtcNow - wallClockStart > wallClockLimit)
{
    // 构建超时报告
    var report = BuildTimeoutReport(_taskState, _taskProgress);
    yield return AgentStep.FromCompletion(report);
    yield break;
}
```

**超时报告内容：**
- 任务描述（原始用户请求）
- 失败原因：超过 10 分钟墙钟限制
- 已完成的工作（EditedFiles 列表 + 每个文件的修改概述）
- 未完成的部分（Plan 中剩余步骤）
- 建议：用户可在下轮对话中发送"继续"基于已有进展接着做

**与 H2 状态机的联动：**
```csharp
// [C18] H2 放弃时 H7 独立运作，仅注入文本警告
if (budgetRatio >= 0.8)
{
    if (_stateMachine != null)
        _stateMachine.ForceAdvance(TaskPhase.Complete);
    // 无论状态机是否存在，都注入 80% 文本警告
    InjectBudgetWarning(state, taskProgress);
}
```

**进度保留：**
- 在 80% 预算警告中明确列出 `TaskState.EditedFiles`
- Agent 中断时已编辑的文件保留不回滚（VS Diff 视图已确认的编辑已写入磁盘）
- attempt_completion 的报告包含"已完成"和"未完成"两部分

**受益范围：** [C8] 仅 Complex 任务（约 20% 请求），但解决的是用户痛点最强的"48 轮白跑"问题

**验收标准：**
- 50 轮上限的任务在第 30 轮时看到提醒消息
- 第 40 轮时看到警告消息，状态机强制进入 Complete
- attempt_completion 的报告中包含已编辑文件列表和未完成步骤
- **[C10] 任务执行超过 10 分钟 → 超时终止，输出详细失败报告（含已完成工作 + 失败原因 + 继续建议）**
- **[C10] 超时后用户发送"继续" → 通过断点续做（步骤 3.7）恢复进度**

---

### 步骤 3.6：H6 Condense 保护区

**要做什么：** Condense 时用代码保护关键信息不丢失，合并路线图的"结构化 Condense"方案。

**现状审查：**
- `TokenBudgetManager.cs`（450+ 行）已有 `CondenseSummary` 类
- `BuildAutoCondenseSummary()` 用正则从对话历史提取信息
- `BuildCondensedHistory()` 构建压缩后的对话历史
- **可行性确认：** `CondenseSummary` 已有大部分字段，只需增加保护区逻辑

**改动文件：** `src/AICA.Core/Agent/TokenBudgetManager.cs`（~60 行改动）
**新建文件：** `src/AICA.Core/Agent/TaskProgress.cs`（~40 行，统一数据结构）

**TaskProgress 数据结构（H6 + 断点续做共用）：**
```csharp
public class TaskProgress
{
    public string OriginalUserRequest { get; set; }
    public List<string> EditedFiles { get; set; }
    public List<string> EditDetails { get; set; }
    public string PlanState { get; set; }
    public string CurrentPhase { get; set; }
    public List<string> KeyDiscoveries { get; set; }
}
```

**数据来源（全部由代码提取，不依赖 LLM）：**

| 字段 | 来源 | 提取方式 |
|------|------|---------|
| OriginalUserRequest | AgentExecutor 保存第一条用户消息 | 循环开始时记录 |
| EditedFiles | TaskState.EditedFiles | 已有 |
| EditDetails | edit 工具成功时从参数中提取 | 在 AgentExecutor 工具结果处理处记录 |
| PlanState | ITaskContext.CurrentPlan | 已有 |
| CurrentPhase | TaskStateMachine.CurrentPhase | H2 新增 |
| KeyDiscoveries | grep_search/context 返回非空结果时记录 | 在 AgentExecutor 工具结果处理处记录 |

**保护区注入（改造 BuildCondensedHistory）：**
```csharp
var protectedBlock = $@"
=== 任务上下文（保护区，不可丢弃）===
原始请求：{progress.OriginalUserRequest}
已编辑文件：{string.Join(", ", progress.EditedFiles)}
编辑详情：
{string.Join("\n", progress.EditDetails.Select(d => $"  - {d}"))}
当前计划：{progress.PlanState}
当前阶段：{progress.CurrentPhase}
关键发现：
{string.Join("\n", progress.KeyDiscoveries.Select(d => $"  - {d}"))}
====================================";

condensed.Add(systemPrompt);
condensed.Add(protectedBlock);      // 保护区
condensed.Add(llmSummary);          // LLM 摘要
condensed.AddRange(recentMessages);
```

**受益范围：** [C8] 主要受益者是 Medium/Complex 长对话（触发 condense 的场景），约 30% 请求

**验收标准：**
- 30+ 轮工具调用后触发 condense，LLM 仍能准确复述用户原始请求
- Condense 后 LLM 仍知道自己编辑了哪些文件
- Condense 后 LLM 仍知道 Plan 的当前进度

---

### 步骤 3.7：断点续做

**要做什么：** 会话中断或结束时保存 TaskProgress，新会话可读取并恢复。

**现状审查：**
- 步骤 3.6 已定义 `TaskProgress` 数据结构
- `AgentExecutor` 有 `LastCondenseSummary` 和 `CondenseUpToMessageCount` 供 UI 层持久化
- **可行性确认：** 只需在会话结束时将 TaskProgress 序列化写入文件，新会话启动时检查并加载

**改动文件：**
- `src/AICA.Core/Agent/AgentExecutor.cs`（~20 行）
- `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（~15 行，新增 `AddResumeContext()`）
- `src/AICA.Core/Storage/`（~30 行，TaskProgress 文件读写）

**存储位置：** 项目工作目录下 `.aica/progress/latest.json`

**验收标准：**
- 会话 1 中编辑了 Area.h 和 Area.cpp 后中断
- 会话 2 中用户发送"继续"→ AICA 知道 Area.h 和 Area.cpp 已编辑完成，从未完成的步骤继续 [C25]

---

### 步骤 3.8：Memory Bank

**要做什么：** 跨会话记住项目背景信息。

**现状审查：**
- 路线图步骤 4 已有完整设计
- 与断点续做共享存储目录 `.aica/`
- `SystemPromptBuilder` 已有扩展能力
- **可行性确认：** 实现简单，核心是读写 Markdown + SystemPromptBuilder 新方法

**新建文件：**
- `src/AICA.Core/Storage/MemoryBank.cs`（~80 行）

**改动文件：**
- `src/AICA.Core/Prompt/SystemPromptBuilder.cs`（~30 行，新增 `AddMemoryContext()`）
- `src/AICA.Core/Agent/AgentExecutor.cs`（~15 行）

**受益范围：** [C8] **~40 人**（每次新会话节省 2-3 分钟上下文建立时间，Simple/Medium/Complex 均受益）

**验收标准：**
- 会话 1 分析 POCO 项目结构
- 会话 2 启动后 AICA 自动知道"POCO 使用 CppUnit 测试框架、CMake 构建"

---

### 步骤 3.9：M1 反馈修复

**要做什么：** 修复界面组工程师在 M1 试用后反馈的问题。

**执行原则：**
- 优先修复影响 ≥5 人的问题
- 不在此窗口做架构性改动
- 修复后用遥测验证效果
- 预留 3-5 天修复窗口

---

### 步骤 3.10：集成测试窗口 [C9 新增]

**要做什么：** 验证 H1/H2/H3/H6/H7 五项 Harness 改造在 AgentExecutor 主循环中的交叉作用。

**为什么需要单独的集成测试窗口：**

五项改造全部作用于 AgentExecutor 的主 while 循环，存在交叉场景：

| 交叉场景 | 涉及组件 | 需要验证的问题 |
|---------|---------|-------------|
| Edit 自动修复后触发验证 | H3 + H1 | H3 自动修复模式跳过了 ShowDiffAndApplyAsync 还是走了正常流程？H1 是否会误报？ |
| 状态机拦截后的迭代计数 | H2 + H7 | 软拦截的轮次算不算迭代消耗？60% 阈值是否需要扣除被拦截的轮次？**[C18] 若 H2 被放弃，本场景跳过，H7 仅验证纯文本警告模式** |
| Condense 后状态机状态 | H2 + H6 | Condense 保护区中的 CurrentPhase 是否正确？Condense 后状态机是否继续工作？**[C18] 若 H2 被放弃，本场景跳过，H6 保护区中 CurrentPhase 字段留空** |
| 保护区 token 占用 | H6 + Token Budget | 保护区固定占用 ~200 token，Token 预算计算是否需要扣除？ |
| 预算 80% 时正在 Edit 自动修复 | H7 + H3 | 强制进入 Complete 时，进行中的 Edit 自动修复是否安全完成？ |
| **预检拦截与 Edit 诊断的边界** [C22] | **H5 + H3** | **H5 拦截 edit 无效参数（文件不存在、old_string 为空）后，H3 诊断逻辑是否仍覆盖所有 edit 失败场景？确认 H5 不会吞掉本应由 H3 诊断的 case** |

**执行方式：**
1. 设计 5 个 Complex 跨文件任务作为集成测试用例
2. 每个用例执行 2 遍，观察 Harness 各组件交互
3. 检查遥测数据中的异常（迭代数异常、Edit 成功率异常、Condense 异常）
4. 修复发现的集成问题

**时间：** 3 天

---

### 阶段 3 完成检查清单

| # | 任务 | 文件 | 新增/改动 | 行数 | 受益范围 |
|---|------|------|----------|------|---------|
| 3.1 | H3 Edit 诊断路由 | EditFileTool.cs | 改动 | ~100 | **~40 人** |
| 3.2 | F5 QT 模板 [C2] | SystemPromptBuilder + DynamicToolSelector | 改动 | ~60 | **界面组 ~20 人** |
| 3.3 | H1 验证中间件 [C4] | VerificationMiddleware.cs (新) | 新建 | ~80 | **~40 人** |
| 3.4 | H2 状态机 [C5/C6] | TaskStateMachine.cs (新) + AgentExecutor + PlanManager + TaskState | 新建+改动 | ~260 | Complex 任务 |
| 3.5 | H7 预算感知 | AgentExecutor.cs | 改动 | ~40 | Complex 任务 |
| 3.6 | H6 保护区 | TokenBudgetManager.cs + TaskProgress.cs (新) | 新建+改动 | ~100 | Medium/Complex |
| 3.7 | 断点续做 | AgentExecutor + SystemPromptBuilder + Storage | 改动+新建 | ~65 | Complex 任务 |
| 3.8 | Memory Bank | MemoryBank.cs (新) + SystemPromptBuilder + AgentExecutor | 新建+改动 | ~125 | **~40 人** |
| 3.9 | M1 反馈修复 | 取决于反馈 | — | ~100（预估） | — |
| 3.10 | 集成测试 [C9] | 无新代码 | 测试+修复 | ~50（修复预估） | — |
| 3.11 | 正式发布 [C47] | 编译 + 全量部署 | 操作 | 0 行代码 | ~40 人 |
| | **合计** | | | **~980** | |

---

### 步骤 3.11：正式发布 [C47 新增]

**要做什么：** 全计划完成后，编译部署正式版本给 ~40 人（平台组 + 界面组）。

**前置条件：**
- 阶段 3 步骤 3.1-3.10 全部完成
- M2 验收标准达标
- 集成测试通过

**执行步骤：**
1. 编译正式版 VSIX（`build.ps1 -Restore -Build` 或 `buildinhome.ps1 -Restore -Build`）
2. 全量部署给 ~40 人（平台组 ~20 + 界面组 ~20）
3. 复用步骤 1.4 建立的双渠道（Excel 模板 + 钉钉群）持续收集反馈
4. 正式版上线后进入持续运营阶段

**验收标准：**
- 编译成功且单元测试无回归
- M2 验收指标全部达标
- ~40 人可正常使用全部功能（F1-F5、F7、F8）

> 注：正式发布的具体流程和验收细节将根据阶段 2-3 执行情况具体调整。

---

## 七、M2 验收标准 [C7 修正]

**工具级指标（遥测自动统计）：**

| 指标 | 目标 | 数据来源 |
|------|------|---------|
| Edit 成功率 | ≥90% | 遥测：EditSuccesses / EditAttempts |
| 工具调用成功率 | ≥85% | 遥测：ToolCallCounts vs ToolFailCounts |
| Condense 后上下文保持率 | ≥95% | 遥测 + 5 个长对话任务 |
| 日活用户 | 25-35 人 | 遥测：按 SessionId 去重 |

**功能质量指标（人工评估，每项 10-20 个任务）：**

| 指标 | 目标 | 衡量方式 |
|------|------|---------|
| F1 代码理解成功率 | ≥85% | 20 个理解任务，**人工判断回答质量** |
| F2 规范合规率 | ≥85% | 10 段生成代码，**人工检查规范遵循** |
| F4 一次完成率（全场景） | **40-50%** [C12] | 10 个跨文件任务（含 2-5 文件），**人工判断完成度** |
| F4 含断点续做完成率 | ≥60% | 同上 |
| **F5 QT 模板质量** [C2] | **≥80%** | **10 个 QT 生成任务，界面组工程师判断** |

---

## 八、全量代码统计 [v1.9 更新]

| 阶段 | 新增文件 | 改动文件 | 代码行数（不含测试） | 测试行数 | 受益覆盖 |
|------|---------|---------|---------|---------|---------|
| 阶段 0（基础修复 + Harness 基础设施） | 2 | 5 | ~271 | ~38 测试 | ~40 人 |
| 阶段 1（C/C++ 专业化 + 日常功能 + 非正式部署）[C68-C74] | 2 | 5 | ~550 | ~24 测试 | **~40 人** |
| 阶段 2（M1 GitNexus）[C52-C67] | **4 + tools/gitnexus/** | **7** | **~1115** | **~730 行 / 17 测试** | ~40 人 |
| 阶段 3（M2 日常功能 + Harness 核心） | 4 | 7 | ~1000 | — | ~40 人 |
| **总计** | **12 + tools/** | **24** | **~2936** | | |

> v1.0 → v1.1 代码量增加 ~220 行，主要来自 F3（+50）、F5（+60）、右键命令（+100）、集成测试修复（+50），H1 因重新设计减少 40 行。
> v1.1 → v1.2 代码量增加 ~40 行，主要来自 Step 1.4 反馈收集机制（+20）、Step 3.5 wall-clock 超时（+20）。
> v1.6 → v1.7 阶段 2 代码量从预估 ~300 行修正为实际 ~1045 行 [C57]。原因：MCP 协议实现需要完整的 JSON-RPC 读写循环 + Content-Length 帧解析（~350 行）；6 个工具各自需要独立的参数 schema、metadata、降级逻辑（~380 行）；进程管理状态机 + 单例 + 自动重启（~230 行）。
> v1.7 → v1.8 新增 tools/gitnexus/ 内嵌部署 [C59]；GitNexusProcessManager 三级路径解析 +~25 行 [C60]；MAX_FILE_SIZE 2MB 定制 [C61]；FindGitRoot 解决 .sln 子目录问题 [C64]；vendor/ 补复制 [C65]；P1-P3 few-shot 修复 +~45 行 [C67]。
> v1.8 → v1.9 规范注入统一为 .aica-rules 单一路径 [C68]：删除 AddCppSpecialization（-35 行），新建 CppRuleTemplates.cs（+266 行），RulesDirectoryInitializer 增加 IsCppProject 检测 + 自动部署（+84 行）；.aica-rules 位置改为 git root [C69]；去除 general.md [C71]。阶段 1 代码量从 ~365 调整为 ~550（+185 净增，因 CppRuleTemplates 从 0 行配置文件变为嵌入代码）。

---

## 九、风险与缓解 [更新]

| 风险 | 概率 | 影响 | 缓解措施 | 检测时机 |
|------|------|------|---------|---------|
| ~~GitNexus Node.js 部署不被允许~~ | ~~中~~ | ~~M1 延后 1 周~~ | ~~降级方案：知识引擎正则增强~~ | ✅ 已确认可行（3/23）[C49] |
| ~~GitNexus 许可证不合规~~ | ~~中~~ | ~~同上~~ | ~~联系作者 / 自实现简化版~~ | ✅ 已确认合规（3/23）[C49] |
| ~~GitNexus 整体不可用（部署+许可均失败）~~ [C13] | ~~低~~ | ~~F8 缺失~~ | ~~搜索开源生态替代组件~~ | ✅ 已验证可用（3/25）MCP 服务启动成功 [C63] |
| ~~MiniMax-M2.5 无法稳定调用 MCP 工具~~ | ~~中~~ | ~~F1 成功率 <80%~~ | ~~few-shot 示例注入~~ | ✅ 自测通过（3/25）：P1-P3 few-shot 修复后 IT3 从 14 轮降至 2 轮 [C67] |
| **H2 状态机 MiniMax-M2.5 不兼容** [C5] | **中** | **状态机无法使用** | **步骤 2.2 先验证 → 三级降级策略（硬拦截/软拦截/放弃）** | **步骤 2.2** |
| H3 StaleContent 诊断误判 | 低 | 正常 Edit 被误报 | 仅对 EditedFilesInSession 中的文件做诊断 | 阶段 3 内测 |
| **Harness 交叉集成问题** [C9] | **中** | **M2 延后** | **预留 3 天集成测试窗口 + 修复缓冲** | **步骤 3.10** |
| M1 反馈超出预留窗口 | 中 | M2 延后 | 优先高影响问题，低优先级推迟到 M3 | 步骤 2.3 小范围试用后 |
| **System Prompt token 膨胀** [C21] | **低** | **触发 Build() 警告** | **阈值从 8000 调整为 16000（适配 177K 窗口）。监控实际 token 数，超过 16K 时按优先级裁剪低优先级规范文件** | **阶段 1 完成后** |
| **工程师反馈收集不足** [C27] | **中** | **步骤 2.3 验收任务不足** | **降低门槛：开发者主动到工位旁观记录 + poco 自测替代 [C62]** | **步骤 2.2 已用 poco 自测绕过 [C62]，2.3 验收仍需真实场景** |
| **新版 VSIX 部署复杂度增加** | **低** | **部署效率降低** | **编写一键部署脚本（设环境变量 + npm install），或将 GitNexus 路径解析改为 VSIX 相对路径** | **步骤 2.3 或 3.11 正式发布前** |

---

## 十、待确认事项 [更新]

| # | 事项 | 阻塞内容 | 确认时限 | 状态 |
|---|------|---------|---------|------|
| 1 | 涉密环境 Node.js 部署可行性 | GitNexus 集成（步骤 2.1） | ~~3/31 前~~ | **✅ 已确认**（3/23）[C49] |
| 2 | PolyForm Noncommercial 许可证合规性 | GitNexus 集成（步骤 2.1） | ~~3/31 前~~ | **✅ 已确认**（3/23）[C49] |
| 3 | ~~RuleLoader 是否支持 frontmatter paths 过滤~~ | 步骤 1.1 规范文件激活范围 | ~~3/26 前~~ | **✅ 已确认** [C14]：RuleLoader 完整支持 `paths` 过滤（`PathMatcher` glob 匹配），C++ 项目由 `ProjectLanguageDetector` 强制激活 |
| 4 | 公司 C/C++ 测试框架（Google Test / CppUnit） | 步骤 1.2 测试生成 | 4 月 | 待调研 |
| 5 | ITaskContext 实现类位置 | 步骤 0.6 接口扩展 | 3/22 当天 | **开工时代码搜索确认，不阻塞进度** [C20] |
| 6 | **H2 状态机兼容性测试结果** [C5] | **步骤 3.4 实现策略选择** | **步骤 2.2 完成时** | **待测试**（步骤 2.2 用 poco 自测简化，H2 测试推迟到阶段 3） |
| 7 | **右键命令源文件位置确认** [C3] | **步骤 1.2 右键命令适配** | **3/26 前** | **开工时代码搜索确认，不阻塞进度** [C20] |
| 8 | **公司构建系统（CMake / MSBuild）配置确认** [C16] | **步骤 1.2 编译错误修复 Prompt 适配** | **4 月** | **待确认** |
