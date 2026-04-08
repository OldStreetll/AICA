# AICA v2.1 E2E 测试报告

> 日期: 2026-04-08
> 测试范围: Phase 1-3 (commit `e7e1b2f`)
> 测试环境: VS2022 + AICA VSIX, 目标项目 D:\V_2.42_1\

---

## 一、测试结果总览

| Phase | 功能 | 结果 | 备注 |
|-------|------|------|------|
| Phase 3 | OH2 结构化记忆注入 | ✅ 通过 | 记忆文件放入 .aica/memory/ 后注入生效 |
| Phase 3 | OH2 兼容迁移 | ✅ 通过 | 备份目录生成、frontmatter 自动添加 |
| Phase 3 | OH2 Debug 日志 | ⚠️ 改进项 | 只显示 total/injected/tokens，不显示具体注入了哪些 |
| Phase 2 | H1 截断持久化 | ✅ 通过 | 49738 行文件截断后完整内容持久化到 truncations/ |
| Phase 2 | H1 回读提示 | ⚠️ 模型问题 | 提示已注入 ToolResult，但 LLM 未转述给用户 |
| Phase 2 | S3 头文件同步 (.cpp→.h) | 未测 | 需准备配对文件专项测试 |
| Phase 1 | M1 Prune 前移 | 未测 | 需超长对话触发，等用户反馈 |
| Phase 1 | M3 自动格式化 | ⚠️ 不确定 | 未见 Auto-formatted 提示，可能无 diff 或未执行 |
| Phase 1 | SK Skills 系统 | 未测 | 等用户反馈 |
| 全局 | Telemetry JSONL 写入 | 🐛 Bug | TelemetryLogger 未注入 AgentExecutor |

---

## 二、发现的问题

### Bug 1: TelemetryLogger 未注入（严重度：中）

- **位置**: `ChatToolWindowControl.xaml.cs:583`
- **现象**: `%USERPROFILE%\.AICA\telemetry\` 下 JSONL 文件中无 `memory_loaded` 等事件
- **原因**: 构造 `AgentExecutor` 时未传入 `TelemetryLogger` 实例，`_telemetryLogger` 为 null
- **影响**: 所有 Phase 的 telemetry 埋点均不写入，影响验证窗口数据收集
- **修复**: 创建 TelemetryLogger 实例并传入 AgentExecutor 构造函数

### Bug 2: EditFileTool 编辑后中文注释乱码（严重度：高）

- **现象**: 调用 edit 工具后，被编辑文件中修改点周围的中文注释变成乱码
- **复现条件**: 文件含中文注释（可能 GBK 编码），在长会话中编辑
- **疑似原因**: EditFileTool 读写文件时未保持原始编码
- **与 Issue 3 关联**: 可能与长会话后模型生成质量下降有关

### Issue 3: 长会话后 LLM 质量严重下降（严重度：高，已知）

- **现象**: 相同任务在新会话开头 vs 旧长会话末尾执行，质量差距非常大
- **原因**: MiniMax-M2.5 在长上下文下注意力分散
- **现有缓解**: M1 Prune 前移、ConversationCompactor 压缩
- **长期方向**: T2 会话摘要（Phase 收尾）、模型能力提升

### Issue 4: S3 只支持 .cpp→.h 单向同步（严重度：中，功能增强）

- **现象**: 在 .h 中修改函数形参名，AICA 没有同步修改 .cpp
- **原因**: S3 HeaderSyncDetector 设计为 .cpp→.h 方向
- **建议**: 后续考虑双向检测

---

## 三、config.json 说明

测试中确认：`~/.AICA/config.json` 是可选文件，不存在时全部走默认值。AICA 不会自动生成该文件。
与 VS 齿轮设置互不覆盖——齿轮管 LLM 连接/安全/UI，config.json 管 Feature Flags/内部阈值。

---

## 四、后续建议

1. **优先修复**: Bug 2（中文乱码）— 影响用户体验
2. **尽快修复**: Bug 1（Telemetry 未注入）— 验证窗口需要数据
3. **记录跟踪**: Issue 3/4 — 已知问题，在后续 Phase 中持续改进
