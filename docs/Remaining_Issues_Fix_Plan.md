# AICA 残留问题修复计划 — 最终报告

> 基于 FreeCAD 0.19.1 全量测试结果，修复 v1.5.0 后的残留问题。
> 三个阶段全部完成并验证。

## 修复前状态（v1.5.0）

| 测试 | 结果 | 工具调用 | 主要问题 |
|------|------|----------|----------|
| 1. 闲聊守卫 | ✅ 完美 | 0 | 无 |
| 2. 目录列表 | ✅ | ~11 | 轻微幻觉 |
| 3. 代码搜索 | ✅ | 14 | 效率偏低 |
| 4. 代码定义 | ✅ | 30 | 调用过多 |
| 5. 读文件分析 | ❌ | 22 | **严重幻觉：文件不存在时编造内容** |
| 6. 构建系统 | ✅ | 25 | 轻微幻觉 |

---

## 阶段 1：修复 `run_command` 中文乱码 ✅

**问题**：`cmd.exe` 管道输出使用系统 OEM 编码（CP936/GBK），但 `RunCommandTool` 以 UTF-8 解码，导致中文乱码。

**修复文件**：`src/AICA.Core/Tools/RunCommandTool.cs`

**修复方案**：使用 `Encoding.GetEncoding(OEMCodePage)` 匹配 cmd.exe 管道输出编码。
> 注：`chcp 65001` 方案无效（不影响管道输出），改用 OEM 编码匹配。

**验证结果** ✅：
```
stdout: 驱动器 D 中的卷是 新加卷
       卷的序列号是 3099-3674
       D:\Project\AIConsProject\FreeCAD0191\build 的目录
```
中文完美显示。

---

## 阶段 2：优化系统 Prompt — 反幻觉 + 效率引导 ✅

**问题**：文件不存在时模型编造内容（测试 5 严重幻觉）

**修复文件**：`src/AICA.Core/Prompt/SystemPromptBuilder.cs`

**修复方案**：在 `AddRules()` 中新增三个规则段：
1. **Anti-Hallucination (CRITICAL)** — 文件不存在时明确告知，不编造内容
2. **Efficiency** — 大多数任务 2-5 次调用，超过 8 次需重新审视
3. **Search Strategy** — 从目标目录开始，优先用内置工具

**验证结果** ✅：
- 测试 5 重测：从 **100% 幻觉** → **基于实际文件内容的准确分析**
- 模型通过 `run_command type` 读取了真实的 `Application.h` 文件
- 正确识别了 `newDocument()`, `openDocument()`, `AutoTransaction` 等真实方法

---

## 阶段 3：降低 maxIterations + 扩展去重覆盖 ✅

**问题**：`maxIterations=50` 允许过多轮次；去重覆盖不全。

**修复文件**：`src/AICA.Core/Agent/AgentExecutor.cs`

**修复方案**：
1. `maxIterations` 从 50 降至 25
2. `GetToolCallSignature` 扩展：`query`, `pattern`, `name`, `command` 参数做 trim + lowercase

**验证结果** ✅：
- 测试 3 重测 (BRep_Builder)：23 次调用，1 次去重拦截
- 未超过 25 轮限制
- 结果准确：正确报告源文件中无 BRep_Builder（仅存在于 .tlog 构建日志中，被正确排除）

---

## 修复后状态（v1.8.0）

| 测试 | v1.5.0 | v1.8.0 | 改善 |
|------|--------|--------|------|
| 1. 闲聊守卫 | ✅ 0 次 | ✅ 0 次 | 稳定 |
| 5. 读文件分析 | ❌ 幻觉 | ✅ 基于实际数据 | **🎉 根本性改善** |
| run_command 中文 | ❌ 乱码 | ✅ 正确 | **🎉 修复** |
| 去重拦截 | 0-4 次 | 1-4 次 | ✅ 覆盖更广 |
| 最大迭代 | 50 轮 | 25 轮 | ✅ 防止失控 |

## 版本历史

| 版本 | 修复内容 |
|------|----------|
| v1.1.0 | UI 幻觉抑制 |
| v1.2.0 | C/C++ 代码定义解析、GrepSearch 多 include + .tlog 排除 |
| v1.3.0 | ListDirTool 广度优先、移除 build 排除 |
| v1.4.0 | 重复工具调用检测（签名去重） |
| v1.5.0 | 去重路径标准化 |
| v1.6.0 | run_command OEM 编码修复 |
| v1.7.0 | 系统 prompt 反幻觉 + 效率 + 搜索策略 |
| v1.8.0 | maxIterations 50→25 + 去重扩展 query/pattern/name/command |

## 已知限制（非工具 Bug，模型行为层面）

| 限制 | 说明 |
|------|------|
| 工具调用仍偏多（15-25 次） | 受限于私有模型推理能力，已通过 prompt 引导缓解 |
| 搜索策略有时发散 | 模型未完全遵循效率指导 |
| condense 仍会触发 | 调用过多导致 token 溢出，需更强模型或更精细 token 管理 |
