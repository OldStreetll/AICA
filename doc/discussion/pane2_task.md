# Pane 2 任务：排期与依赖分析

## 你的角色
你是开发团队成员，负责分析 MCP 内容结构化吸收任务如何融入 v2.1 实施计划。

## 背景
请先阅读以下文件：
1. `/mnt/d/Project/AIConsProject/AICA/doc/discussion/SK_MCP_Integration_Discussion.md` — 总体讨论文档
2. `/mnt/d/Project/AIConsProject/AICA/doc/2.1plan/AICA_v2.1_Unified_Plan_v2.1.md` — v2.1 完整实施计划（重点阅读 Phase 排期、依赖关系、SK 任务、验证窗口）
3. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_v2.12_Issues_Summary.md` — 4.1节
4. `/mnt/d/Project/AIConsProject/AICA/doc/nextstep2.1/AICA_MCP_Redundant_Files_Issue.md`

## 当前进度
- Phase 0 已完成（T1-infra Telemetry）
- Phase 1 已完成（M1 Prune前移 + M3 自动格式化 + SK Skills系统）
- Phase 2 R1 已完成（H1 ReadFile+RunCommand 截断持久化 Pilot）
- 当前在 Phase 2 R2（H1 剩余5工具截断持久化接入）

## 你的任务
分析 MCP 内容结构化吸收（分为3个子任务：McpRuleAdapter / McpResourceResolver / McpServerDescriptor）应该如何融入 v2.1 计划：

1. **依赖分析**：
   - MCP 各子任务与现有18个任务的依赖关系
   - 前置条件（SK已完成 ✅、RuleEvaluator已就绪 ✅）
   - 后续任务是否受益（OH2记忆升级、S1符号检索等）

2. **排期方案**（至少提出2-3个可选方案）：
   - 每个方案说明：放在哪个 Phase/窗口、工作量估算、对总排期的影响
   - 考虑验证窗口弹性时间的利用
   - 考虑与现有任务的并行性

3. **风险评估**：
   - 新增任务对24-26周总排期的影响
   - 单人开发约束下的可行性
   - 如果不做的风险（架构债务累积）

4. **推荐方案**：给出你的推荐并说明理由

## 附加信息
MCP 子任务工作量估算（来自 nextstep2.1 文档）：
- MCP-A 冗余文件清理：0.5天
- McpRuleAdapter：~150行，估2-3天
- McpResourceResolver：~100行，估1-2天
- McpServerDescriptor：~80行，估1天（可选）

## 输出
将你的分析结果写入：`/mnt/d/Project/AIConsProject/AICA/doc/discussion/pane2_output.md`

注意：只做分析，不要修改任何源代码。
