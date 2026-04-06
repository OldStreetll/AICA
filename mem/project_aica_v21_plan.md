---
name: AICA v2.1 统一实施方案
description: AICA v2.1 统一实施方案 v2.1 最终版（三轮双实例审查后收敛），8 Phase/18任务+4横切项/24-26周
type: project
---

AICA v2.1 统一实施方案 **v2.1 最终版**，文档在 D:\project\AICA\doc\2.1plan\：
- **`AICA_v2.1_Unified_Plan_v2.1.md`** — 最终执行方案（2026-04-06，v2.1 三轮审查后收敛）
- `AICA_v2.1_Unified_Plan_v2.md` — v2.0 修订版（保留参考）
- `AICA_v2.1_Unified_Plan.md` — v1.0 原始方案（保留参考）
- 审查报告：6 份（review_strict_audit/review_feasibility/review_joint_discussion/review_v2_strict_audit/review_v2_feasibility/review_v2_joint_conclusion/review_v2.1_final_check/review_v2.1_my_check）

**排期**：24 周目标 / 26 周上限

**8 Phase + 3 弹性窗口 结构**：
- Phase 0 (W1): T1基础设施 + FF1框架 + M1 Prune + 基线采集
- Phase 1 (W2-3): M3 格式化+PostEditPipeline+DiagnosticsStep迁移 + SK Skills（被动注入）
- [弹性窗口1 W4]
- Phase 2 (W5-7): H1 截断持久化（通用基础设施）+ S3 头文件同步
- Phase 3 (W8-10): OH2 记忆升级(8-10天) + H3a 权限反馈
- [弹性窗口2 W11]
- Phase 4 (W12-14): H2 快照 + H3b 权限持久化
- Phase 5 (W15-17): OH5 SubAgent+ReviewAgent单维度PoC → PA1 PlanAgent优化(串行) + S2 构建(5-7天)
- [弹性窗口3 W18]
- Phase 6 (W19-21): OH3 Hooks(8-10天) + S1 符号检索
- Phase 7 (W22): S4 Impact分析
- 收尾 (W23-24): T2会话摘要 + 集成测试 + Bug修复

**核心原则**：优先模型无关的系统级功能（S3/S2/S4/M3）> 模型依赖的功能（OH5/PA1）

**Why:** v2.1 经三轮审查确认收敛（30+问题→12→4，架构级→文字级），方案可执行
**How to apply:** 后续 AICA 开发严格参考 v2.1 方案，Phase 0 可立即启动
