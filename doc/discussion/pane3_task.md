# Pane 3 任务：MCP-A 冗余文件清理方案

## 你的角色
你是开发团队成员，负责设计冗余文件清理的具体实施方案。

## 背景
请先阅读以下文件：
1. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_MCP_Redundant_Files_Issue.md` — 冗余文件问题详述
2. `/mnt/d/Project/AIConsProject/AICA/doc/discussion/SK_MCP_Integration_Discussion.md` — 总体讨论文档

然后阅读相关源码：
3. 找到 `GitNexusProcessManager.cs`（在 AICA.Core 项目中搜索），重点关注 `TriggerIndexAsync` 和 `TriggerIndexWithProgressAsync` 方法
4. 了解 GitNexus analyze 命令的调用方式和参数

## 你的任务

1. **确认 GitNexus CLI 能力**：
   - 阅读 GitNexusProcessManager.cs 中调用 analyze 的具体命令行参数
   - 分析是否已有 `--no-context-files` 或类似参数
   - 如果没有，评估三种替代方案（A1/A2/A3）的可行性

2. **设计具体修改方案**：
   - 精确到代码行级别的修改描述
   - 考虑向后兼容性（GitNexus 版本升级时）
   - 考虑首次打开 vs 重复打开项目的场景

3. **测试方案**：
   - 如何验证冗余文件不再生成
   - 如何验证 GitNexus 索引功能仍正常（知识图谱数据库不受影响）
   - 回归测试要点

4. **与 MCP 内容吸收的关系**：
   - 冗余文件清理后，AGENTS.md 的内容是否仍可通过 MCP resource 获取？
   - 是否影响 McpRuleAdapter 后续的工作？

## 输出
将你的分析结果写入：`/mnt/d/Project/AIConsProject/AICA/doc/discussion/pane3_output.md`

注意：只做分析和设计，不要修改任何源代码。
