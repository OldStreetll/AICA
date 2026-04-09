# AICA v2.1 开发交接指南

> 新设备/新 Claude Code 实例接管工作流时使用此文档。

## 第一步：克隆仓库

```bash
git clone https://github.com/OldStreetll/AICA.git
cd AICA
```

## 第二步：迁移 Claude Code 记忆

将以下文件复制到新设备的 Claude Code 项目记忆目录。

**源路径（旧设备）**：`~/.claude/projects/<项目路径哈希>/memory/`
**目标路径（新设备）**：`~/.claude/projects/<新项目路径哈希>/memory/`

> 提示：在新设备的 Claude Code 中随便说一句话让它创建记忆目录，然后替换文件。
> 或者直接告诉新实例"请阅读 D:\project\AICA\doc\2.1plan\HANDOVER.md 接管工作"。

### 记忆文件内容（如无法迁移文件，可直接告诉新实例以下内容）

#### MEMORY.md（索引）
```
# Memory Index
- [user_aica_learner.md](user_aica_learner.md) — AICA项目唯一开发者，agent新手，追求产品级架构思维
- [project_aica_product.md](project_aica_product.md) — AICA产品定位、C/C++专家化方向、4阶段路线图、技术约束
- [project_aica_study.md](project_aica_study.md) — Agent演进(7课)+Harness Engineering(7课)学习进度
- [project_aica_v21_plan.md](project_aica_v21_plan.md) — AICA v2.1统一实施方案+四实例联合审查报告
- [reference_local_projects.md](reference_local_projects.md) — D:\project下的已知项目索引
```

#### 关键记忆摘要

**用户画像**：AICA 项目唯一开发者，agent 新手但追求产品级架构思维。深度 Go 经验，.NET/C# 通过 AICA 项目学习中。

**产品定位**：企业内部 C/C++ AI编程助手，VS2022 VSIX，涉密离线环境，MiniMax-M2.5 单模型，20并发约束。

**技术栈**：.NET Framework 4.8 + .NET Standard 2.0，SK 1.54.0，177K token budget。

**核心策略**：弱模型 + 强系统。

## 第三步：了解当前进度

### 已完成

| Phase | 内容 | 提交 |
|-------|------|------|
| Phase 0 | T1 Telemetry + FF1 Feature Flags + EditPipeline骨架 | ✅ |
| Phase 1 R1 | M1 Prune前移 + ResetToClean + DiagnosticsStep迁移 + FormatStep | ✅ |
| Phase 1 R2 | SK Skills系统（Rule扩展+SkillTool+被动注入+4模板） | ✅ |
| Telemetry补线 | 全链路JSONL持久化 + telemetry-analysis.ps1 | ✅ |
| Phase 2 R1 | H1截断持久化Pilot（ReadFile+RunCommand）+ S3头文件同步 | ✅ |
| Phase 2 R2 | H1截断持久化批量接入 + MCP-A冗余清理 | ✅ `2194697` |
| Phase 3 | OH2 结构化记忆升级 + H3a 权限反馈注入 | ✅ `e7e1b2f` |
| Bug fix | TelemetryLogger注入 + 中间件注册 + PermissionCheck修正 | ✅ `9ad9a1c` |
| Bug fix | 文件编码检测与保留（修复中文乱码） | ✅ `98b7f96` |
| Bug fix | 修复_taskState null NullReferenceException | ✅ `6a9ae71` |

### 待完成

```
Phase 4     ⬜ H2 文件快照与回滚 + H3b 权限决策持久化
Phase 5     ⬜ OH5 SubAgent泛化+ReviewAgent PoC + PA1 PlanAgent优化 + S2 后台构建
Phase 6     ⬜ OH3 Hooks钩子系统 + S1 符号检索增强
Phase 7     ⬜ S4 GitNexus主动触发
收尾        ⬜ T2 会话摘要 + 集成测试 + Bug修复
```

### 排期

内部 26-28 周，对外 24-26 周。当前约在第 4-5 周位置。

## 第四步：关键文档

开发前必读（按优先级）：

1. **`doc/2.1plan/AICA_v2.1_Four_Instance_Review.md`** — 四实例审查终稿，**开发权威依据**
2. **`doc/2.1plan/AICA_v2.1_Unified_Plan_v2.1.md`** — 原始方案（与审查报告冲突时以审查报告为准）
3. **`doc/harness-study/`** — Harness 工程学习笔记（设计决策的深层理由）

## 第五步：开发工作流

### 构建命令

```powershell
# 始终使用此脚本构建（包含 VSIX）
powershell -ExecutionPolicy Bypass -File D:\project\AICA\buildinhome.ps1

# 带清理和恢复的完整构建
powershell -ExecutionPolicy Bypass -File D:\project\AICA\buildinhome.ps1 -Clean -Restore
```

VSIX 产出物：`D:\project\AICA\src\AICA.VSIX\bin\Debug\AICA.vsix`

### 四实例并行开发模式

本项目使用 tmux 4 pane 并行开发模式：
- 每个 pane 运行一个 Claude Code 实例
- Pane 0 负责协调、分工、集成验证、提交
- Pane 1/2/3 负责不同文件的并行开发
- **关键原则：4个实例操作完全不同的文件，避免冲突**

启动：
```bash
tmux new-session -s aica
# Ctrl+b % 分割3次，得到4个pane
# 每个pane运行 claude --dangerously-skip-permissions
```

### 横切规则（审查报告中的10项）

1. SK 保守精确匹配（intent 完全匹配才注入模板）
2. S2 debounce（多文件编辑最后一个文件后触发构建）
3. S2 线程安全（JoinableTaskFactory.RunAsync）
4. S2 跨层通信（BuildStep仅触发，结果通过BuildResultCache传递）
5. H1 pilot 策略（先改2工具验证接口，再批量）
6. H1 依赖澄清（S2→H1 软依赖）
7. OH2 兼容迁移（旧.md自动归类+推导description）
8. 验证窗口增项（Feature Flags退化检查 + ReviewAgent偏差分析）
9. OH5 golden tests（2-3个基准测试case）
10. AgentExecutor 提取约束（提取前先补回归测试）
