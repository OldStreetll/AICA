# YAML Frontmatter + Markdown 规则格式设计说明

## 问题陈述

为什么规则系统采用 **YAML Frontmatter + Markdown** 格式，而不是纯文本格式？

---

## 核心答案

**YAML Frontmatter 提供了元数据层，使规则系统能够实现以下关键功能：**

1. **条件激活** - 根据文件路径选择性应用规则
2. **优先级管理** - 解决多个规则的冲突
3. **启用/禁用** - 灵活控制规则
4. **分类和搜索** - 组织和查找规则
5. **版本控制** - 追踪规则演变
6. **生态兼容性** - 与现有工具集成

---

## 详细对比

### 1. 条件激活（最重要）

#### 纯文本方案的问题

```
.aica-rules/
├── general.md          # 通用规则
├── cpp-standards.md    # C++ 规则
├── python-standards.md # Python 规则
└── security.md         # 安全规则
```

**加载流程：**
```
用户请求 → 加载所有 .md 文件 → 全部添加到系统提示
```

**结果：**
- 系统提示包含 4 个规则集合
- 用户处理 C++ 文件时，Python 规则也被加载
- 用户处理 Python 文件时，C++ 规则也被加载
- 系统提示变得臃肿，影响 LLM 性能和准确性

#### YAML Frontmatter 方案的优势

```yaml
# cpp-standards.md
---
paths:
  - "**/*.cpp"
  - "**/*.h"
priority: 10
---

# python-standards.md
---
paths:
  - "**/*.py"
priority: 10
---
```

**加载流程：**
```
用户请求（处理 main.cpp）
  ↓
提取候选路径：["main.cpp", "src/utils.h", ...]
  ↓
评估规则条件：
  - cpp-standards.md: paths 匹配 ✓ → 激活
  - python-standards.md: paths 不匹配 ✗ → 跳过
  ↓
只将 cpp-standards.md 添加到系统提示
```

**结果：**
- 系统提示精简，只包含相关规则
- LLM 专注于 C++ 标准
- 性能更好，准确性更高

### 2. 优先级管理

#### 场景：规则冲突

假设有两个规则都适用：

```yaml
# general.md - 通用规则
---
priority: 5
---
# 通用编码标准
- 函数最多 50 行
- 使用有意义的变量名

# cpp-standards.md - C++ 特定规则
---
paths: ["**/*.cpp"]
priority: 10
---
# C++ 编码标准
- 函数最多 100 行（C++ 允许更长）
- 使用 camelCase 命名
```

**优先级解决冲突：**
- 通用规则：优先级 5
- C++ 规则：优先级 10（更高）
- 结果：C++ 规则的"函数最多 100 行"覆盖通用规则

**纯文本方案：** ❌ 无法表达优先级，无法自动解决冲突

### 3. 启用/禁用

#### 场景：临时禁用规则

```yaml
---
enabled: false
---
# 这个规则暂时不适用
```

**优点：**
- 保留规则文件，便于后续启用
- 无需删除或注释
- 便于版本控制

**纯文本方案：** ❌ 只能删除文件或注释整个内容

### 4. 分类和搜索

```yaml
---
tags: ["backend", "security", "performance"]
category: "api-design"
author: "security-team"
created: "2026-01-15"
updated: "2026-03-12"
---
```

**用途：**
- 快速找到特定类别的规则
- 追踪规则的来源和维护者
- 版本控制和审计
- 生成规则文档

**纯文本方案：** ❌ 无法实现

### 5. 规则继承和组合

```yaml
# cpp-backend.md
---
extends: ["general.md", "cpp-standards.md"]
override:
  priority: 15
---
# C++ 后端特定规则
```

**优点：**
- 避免重复
- 支持规则组合
- 灵活的规则组织

**纯文本方案：** ❌ 无法实现

---

## 实际应用场景

### 场景 1：多语言项目

```
.aica-rules/
├── general.md           # 优先级 5
├── cpp-standards.md     # 优先级 10, paths: ["**/*.cpp", "**/*.h"]
├── python-standards.md  # 优先级 10, paths: ["**/*.py"]
├── js-standards.md      # 优先级 10, paths: ["**/*.js", "**/*.ts"]
└── security.md          # 优先级 20（最高）
```

**用户处理 main.cpp：**
- 加载：general.md, cpp-standards.md, security.md
- 跳过：python-standards.md, js-standards.md

**用户处理 utils.py：**
- 加载：general.md, python-standards.md, security.md
- 跳过：cpp-standards.md, js-standards.md

**纯文本方案：** ❌ 所有规则都被加载

### 场景 2：临时规则调整

```yaml
# cpp-standards.md
---
enabled: true
priority: 10
---

# cpp-experimental.md
---
enabled: false  # 实验性规则，暂时禁用
priority: 15
---
```

**优点：** 快速启用/禁用，无需删除文件

**纯文本方案：** ❌ 无法实现

### 场景 3：规则版本控制

```yaml
---
version: "2.0"
changelog: |
  v2.0: 更新了命名规范
  v1.0: 初始版本
---
```

**优点：** 追踪规则演变，便于审计

**纯文本方案：** ❌ 无法实现

---

## 与其他工具的兼容性

YAML Frontmatter 是一个**广泛采用的标准**：

| 工具 | 用途 | 支持 YAML Frontmatter |
|------|------|----------------------|
| Jekyll | 静态网站生成器 | ✓ |
| Hugo | 静态网站生成器 | ✓ |
| Obsidian | 笔记应用 | ✓ |
| Cline | AI 编程助手 | ✓ |
| GitHub Pages | 网站托管 | ✓ |
| Notion | 知识库 | ✓ |

**优点：** 用户可能已经熟悉这个格式，工具生态成熟

---

## 性能影响

### 系统提示大小对比

**假设场景：** 5 个规则文件，每个 500 字符

#### 纯文本方案
```
系统提示 = 基础提示 + 所有 5 个规则
        = 5000 + (500 × 5)
        = 7500 字符
```

#### YAML + Markdown 方案（条件激活）
```
系统提示 = 基础提示 + 相关规则（平均 2 个）
        = 5000 + (500 × 2)
        = 6000 字符
```

**节省：** 20% 的系统提示大小

**影响：**
- 更快的 LLM 响应
- 更低的 API 成本
- 更高的准确性（LLM 专注于相关规则）

---

## 设计决策总结

| 特性 | 纯文本 | YAML + Markdown |
|------|--------|-----------------|
| 条件激活 | ❌ | ✓ |
| 优先级管理 | ❌ | ✓ |
| 启用/禁用 | ❌ | ✓ |
| 分类和搜索 | ❌ | ✓ |
| 版本控制 | ❌ | ✓ |
| 规则继承 | ❌ | ✓ |
| 生态兼容性 | ❌ | ✓ |
| 简单性 | ✓ | ✓ |
| 学习曲线 | 低 | 低 |

---

## 最佳实践

### 规则文件模板

```yaml
---
# 必需字段
paths:
  - "**/*.cpp"
  - "**/*.h"
priority: 10
enabled: true

# 可选字段
tags: ["cpp", "coding-standards"]
category: "language-specific"
author: "team"
version: "1.0"
created: "2026-03-12"
---

# 规则标题

## 部分 1
- 规则内容

## 部分 2
- 规则内容
```

### 优先级指南

```
内置规则：0
全局规则：10
本地规则：20
安全规则：100（最高）
```

### 路径模式

```yaml
paths:
  - "**/*.cpp"           # 所有 .cpp 文件
  - "src/**"             # src 目录下的所有文件
  - "src/backend/**"     # src/backend 目录下的所有文件
  - "**/*.test.cpp"      # 所有测试文件
```

---

## 结论

**YAML Frontmatter + Markdown 格式不是为了复杂而复杂，而是为了实现以下关键功能：**

1. **智能条件激活** - 只加载相关规则
2. **灵活的优先级管理** - 解决规则冲突
3. **易于维护** - 启用/禁用、版本控制
4. **生态兼容性** - 与现有工具集成
5. **可扩展性** - 支持未来的高级特性

**这个设计来自 Cline 项目的实战经验，已被证明是有效的。**

---

**参考资源：**
- Cline 规则系统：https://github.com/cline/cline
- YAML Frontmatter 标准：https://jekyllrb.com/docs/front-matter/
- 规则系统实现：`src/AICA.Core/Rules/`
