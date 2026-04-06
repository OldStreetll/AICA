---
name: AICA产品方向与技术决策
description: AICA产品定位、技术栈约束、已确认的C/C++专家化方向和4阶段路线图
type: project
---

## 产品定位
"企业内部 C/C++ AI编程助手" — 不是"企业版Cursor"，而是在VS2022上为涉密环境下的C/C++开发者提供专业辅助。

## 4个核心使用场景
1. 辅助编写代码（代码生成）
2. 提供开发建议
3. 编写测试用例
4. 辅助代码重构

## 已确认的战略方向
- C/C++ 专家化：用户明确表示"完全符合公司需求，极其认同"
- 编码规范集成：Qt-C++ 规范（D:\project\qt_coding_standard.txt）+ MISRA C 标准
- 多模态扩展：MiniMax-M2.5 支持图片输入，计划用于 UI bug 识别
- 持续迭代：上级希望AICA能够持续进化

## 3个结构性约束
1. **模型**：仅 MiniMax-M2.5，暂无替代，未来有计划部署更强模型
2. **平台**：VS2022 VSIX，.NET Framework 4.8，WPF WebBrowser (IE Trident)
3. **并发**：20并发+10K input → TTFT 23s；50并发+20K → TTFT 207s

## 技术栈
- SK 1.54.0（锁版本，System.Text.Json 8.x 兼容性）
- Token budget: 177K，condense threshold: msg=70
- AgentExecutor: 1222行（R1-R9重构后）

## 交付节点
- 2026-03-31: 6人试用能辅助开发（基本功能稳定）
- 试用痛点：仅痛点4（复杂指令处理）仍显著，其余已修复或极偶发

## 待用户确认
- 构建系统（CMake? MSBuild?）和测试框架 — 用户需进一步调研后反馈

**Why:** AICA 是涉密环境下唯一可用的AI编程工具，对公司代码质量提升有战略价值。
**How to apply:** 所有建议都要围绕C/C++专家化方向，考虑MiniMax-M2.5的能力边界和VS2022平台限制。
