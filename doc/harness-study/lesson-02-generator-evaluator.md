# 第二课：Generator-Evaluator 分离模式

> 日期：2026-04-05
> 来源：Anthropic "Harness Design for Long-Running Apps" + AICA v2.1 计划

---

## 核心问题

**Self-Evaluation Bias**：Agent 评价自己的产出总是过度自信。

原因：生成和评估共享同一个上下文，LLM 刚花大量 token 思考如何生成，自然倾向于认可自己的产出（禀赋效应）。

## 解决方案：三 Agent 架构

```
用户需求（1-4 句话）
     │
     ▼
┌──────────┐
│  Planner  │  需求 → 产品规格（不指定具体实现）
└────┬─────┘
     ▼
┌──────────┐
│ Generator │  按 Sprint 逐个实现功能，自评后提交
└────┬─────┘
     ▼
┌──────────┐
│ Evaluator │  独立上下文 + 交互式测试 + 按标准打分
│           │  不通过 → 反馈回 Generator 重做
└──────────┘
```

## 三个关键设计决策

| # | 决策 | 为什么重要 |
|---|------|----------|
| 1 | **Evaluator 有独立上下文** | 没看到 Generator 的思考过程，只看运行结果，打破自评偏差 |
| 2 | **交互式测试而非静态审查** | 通过 Playwright 点击、滚动、填表单，能发现运行时 bug 和交互缺陷 |
| 3 | **Sprint 契约提前协商** | 动手前商定"成功是什么样"，防止 scope creep 和移动球门柱 |

## 评估标准工程

Anthropic 前端设计任务的四个打分维度：

| 维度 | 权重 | 含义 |
|------|------|------|
| Design Quality | 高 | 配色、排版、布局的连贯视觉语言 |
| Originality | 高 | 自主设计决策 vs 套模板的"AI 味" |
| Craft | 中 | 排版层次、间距、对比度——技术细活 |
| Functionality | 中 | 能不能用（不管好不好看） |

**权重是引导模型冒险的杠杆**：不加权时模型走向"安全但平庸"，因为功能正确比设计出彩容易。

## 评估者校准

评估者的初始表现很差——能发现问题但随后自己合理化掉。

迭代校准流程：
1. 读评估者日志，找到"发现问题但放过"的判断偏差点
2. 针对性更新评估 Prompt
3. 加入 few-shot 示例（"这种情况应该扣分"）
4. 多轮循环直到评估者判断与人类一致

本质是 Prompt Engineering 的具体化：标注 → 调整 → 验证 → 迭代。

## Critic 纠正

### GAN 类比有误导性

Anthropic 原文说 "GAN-inspired"，但实际运作与 GAN 差别很大：

| | GAN | Generator-Evaluator |
|---|-----|-------------------|
| 训练方式 | 梯度对抗，同步优化 | Prompt 固定，不互相优化 |
| 目标 | Generator 欺骗 Discriminator | Generator 满足 Evaluator 标准 |
| 关系 | 对抗（零和） | 协作（Evaluator 帮 Generator 改进） |

**更准确的类比是 Code Review**：一人写代码，另一人审查，审查意见反馈回去修改。

### AICA ReviewAgent 与 Anthropic Evaluator 是不同层次的评估

| | Anthropic Evaluator | AICA ReviewAgent（计划） |
|---|----|----|
| 测试方式 | Playwright 打开真实应用交互 | 无工具，纯推理审查 |
| 输入 | 运行中的应用截图 + 交互结果 | 代码 diff 文本 |
| 能发现 | 运行时 bug、交互缺陷、视觉问题 | 逻辑错误、风格问题 |
| 发现不了 | — | 运行时行为、跨文件链接错误 |

- ReviewAgent = **代码审查**（静态）
- Evaluator = **QA 测试**（动态）
- 两者互补，不是替代

## AICA 映射

| Anthropic 方案 | AICA 对应 | 说明 |
|---------------|----------|------|
| Evaluator Agent | ReviewAgent（SubAgent 实例） | 代码审查层，纯推理，1 次迭代/15s/4K token |
| Playwright 交互测试 | S2 后台异步构建 + VS Error List | 运行时验证层，用 IDE 能力替代浏览器 |
| Sprint 契约 | S5 任务模板化 | 通过 System Prompt 注入任务框架 |
| 评估标准维度 | 待建设 | AICA 尚未定义 C/C++ 场景的评估维度 |

## 核心收获

分离不是目的。三者结合才是这个模式有效的原因：
1. **独立上下文** — 打破自评偏差
2. **交互式验证** — 发现运行时问题
3. **预定标准** — 防止 scope creep 和事后刁难
