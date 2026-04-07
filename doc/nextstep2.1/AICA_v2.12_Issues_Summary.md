# AICA v2.12.0 问题总结与后续优化方向

> 日期: 2026-04-03
> 版本: v2.12.0 (commit `70517c0`)
> 状态: 已发布，流式输出待解决，3项优化待讨论

---

## 一、v2.12.0 已完成功能

| 功能 | 说明 | 状态 |
|------|------|------|
| IncrementalRenderer | 增量DOM渲染，insertAdjacentHTML替代innerHTML全量替换 | ✅ |
| Planning浮动面板 | VS2022暗色主题，进度条+步骤打勾+CSS checkbox hack折叠 | ✅ |
| PlanProgressTracker | LLM自动标记步骤完成，~450 tokens/轮，防降级+静默失败 | ✅ |
| 输出顺序优化 | 思考→工具调用→观察→决策 有序交错 | ✅ |
| 思考块默认展开 | CSS反转，默认展开可折叠 | ✅ |

---

## 二、E2E 修复记录（6个问题）

| # | 问题 | 根因 | 修复方案 | 状态 |
|---|------|------|---------|------|
| 1 | 文本非流式蹦出 | WPF MSHTML Dispatcher.Invoke密集调用间不重绘 | Task.Delay(15ms)无效已回退 | ⚠️ 未解决 |
| 2 | 文本和工具调用顺序错误 | responseBuilder跨迭代累积 + streaming-text div位置固定 | TextChunk 4路分派 + ActionStart吸收文本 | ✅ |
| 3 | Plan面板多Plan点击白屏 | showPlan用window.location.href导航离开 | 纯JS DOM操作 + window._planData数组 | ✅ |
| 4 | 首段文本重复 | ToolStart创建thinking block但旧streaming-text div未清除 | ActionStart吸收responseBuilder → thinking，清空旧div | ✅ |
| 5 | 最终回复在Thought块里 | hasToolCalls分支创建thinking block | 移除该分支，最终回复走responseBuilder | ✅ |
| 6 | 最终回复显示在顶部 | streaming-text div ID复用，getElementById返回旧位置空壳 | 唯一ID(递增计数器) + RemoveElement(DOM移除) | ✅ |

---

## 三、未解决问题：流式输出

### 现象
简单对话和最终回复文本一次性全部出现，而非逐字流式输出。

### 根因
WPF WebBrowser 控件使用 IE11/MSHTML 内核。`Dispatcher.Invoke` 密集调用（每个 TextChunk 一次）导致 UI 线程没有空闲时间处理 `WM_PAINT` 消息，浏览器在所有 chunk 处理完毕后才一次性重绘。

### 已尝试方案
- `Task.Delay(15ms)` 在 TextChunk 后让出 UI 线程 → **无效**，已回退

### 可能的解决方向
1. **Dispatcher.BeginInvoke + DispatcherPriority.Render** — 异步分发 + 渲染优先级
2. **execScript 强制重绘** — 通过 JS 触发 `document.body.offsetHeight` 强制 reflow
3. **节流渲染** — 累积 N 个 chunk 后才更新一次 DOM（降低更新频率）
4. **WebView2 迁移** — 长期方案，彻底解决 IE11 限制

---

## 四、已发现的优化方向（待讨论）

### 4.1 MCP 内容结构化吸收

**问题**: AICA 的 MCP 集成未参考标准模式，GitNexus 提供的有价值内容未被充分利用。

**现状**:
- AGENTS.md 行为规则（Always/Never/When Debugging/When Refactoring/Self-Check）作为不透明文本 dump 进 prompt（~1800 tokens）
- 6 个额外 MCP 资源（clusters/processes/schema 等）完全未使用
- 生成的 AGENTS.md、CLAUDE.md、.claude/ 文件 AICA 从不读取，是冗余产物

**初步设计方向**:
- **McpRuleAdapter**: 解析 AGENTS.md 为结构化规则，融入 RuleEvaluator（intent/phase 条件激活）
- **McpResourceResolver**: 按需动态获取额外 MCP 资源，根据 intent+complexity 决策
- **McpServerDescriptor**: 可复用的 MCP 吸收模式，未来新 MCP 服务器自动吸收行为规则
- **--no-context-files**: AICA 内部触发 analyze 时不生成冗余文件
- Token 优化：简单请求从 ~1950 降到 ~540 tokens（-72%）

**状态**: 需深入讨论后决定

### 4.2 记忆系统优化

**现状**:
- `.aica/memory/` — MemoryBank.cs 管理的跨会话记忆，4000 char cap
- `.aica/progress/` — TaskProgressStore.cs 管理的任务进度检查点

**可能优化方向**:
- 记忆检索和利用效率
- 跨会话上下文恢复
- 记忆淘汰和更新策略
- 与 Planning/PlanProgressTracker 的整合

**状态**: 需讨论确定具体方向

### 4.3 冗余文件清理

**问题**: AICA 打开项目时会在项目根目录生成以下冗余文件：

| 文件/目录 | 创建者 | 对AICA是否有用 |
|----------|--------|--------------|
| AGENTS.md | GitNexus ai-context.js | ❌ 不读取（通过MCP资源获取相同内容） |
| CLAUDE.md | GitNexus ai-context.js | ❌ 不读取（与AGENTS.md内容相同） |
| .claude/ | GitNexus ai-context.js | ❌ 不读取（为Claude Code生成的技能文件） |
| .aica/ | MemoryBank + TaskProgressStore | ✅ 使用 |
| .aica-rules/ | RulesDirectoryInitializer | ✅ 使用 |
| .gitnexus/ | GitNexus analyze | ✅ 使用（知识图谱数据库） |

**解决方案**: GitNexusProcessManager 触发 analyze 时追加 `--no-context-files` 参数

---

## 五、代码规模（v2.12.0）

- 新增文件: IncrementalRenderer.cs (~250行) + PlanProgressTracker.cs (~215行)
- 修改文件: ChatToolWindowControl.xaml.cs (+436/-128) + AgentExecutor.cs (+86) + ChatModels.cs (+4)
- 设计文档: 4份（OutputOptimization Design+Review, PlanPanel Design+Review）
- 生产代码总计: ~100个.cs文件, ~31,000行
