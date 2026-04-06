# YAML 元数据设计决策工具

## 快速决策指南

使用这个工具来快速确定新规则的 YAML 元数据。

---

## 第 1 步：确定 paths（适用范围）

**问题：这个规则适用于哪些文件？**

### 选项 A：语言特定规则

```yaml
# C++
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.hpp"

# Python
paths:
  - "**/*.py"

# JavaScript/TypeScript
paths:
  - "**/*.js"
  - "**/*.ts"
  - "**/*.jsx"
  - "**/*.tsx"

# C#
paths:
  - "**/*.cs"

# Java
paths:
  - "**/*.java"
```

### 选项 B：目录特定规则

```yaml
# 后端代码
paths:
  - "src/backend/**"
  - "src/api/**"

# 前端代码
paths:
  - "src/frontend/**"
  - "src/ui/**"

# 测试代码
paths:
  - "**/*.test.*"
  - "**/*.spec.*"
  - "tests/**"

# 配置文件
paths:
  - "**/*.json"
  - "**/*.yaml"
  - "**/*.yml"
```

### 选项 C：通用规则

```yaml
# 不指定 paths（适用于所有文件）
paths: []
# 或省略 paths 字段
```

---

## 第 2 步：确定 priority（优先级）

**问题：这个规则的重要程度是多少？**

### 优先级速查表

```
priority: 5
├─ 用途：建议性规则
├─ 例子：代码风格、注释规范
└─ 冲突时：被其他规则覆盖

priority: 10-15
├─ 用途：语言特定规则
├─ 例子：C++ 编码标准、Python 规范
└─ 冲突时：覆盖低优先级规则

priority: 20-50
├─ 用途：项目强制规则
├─ 例子：项目架构、设计模式
└─ 冲突时：覆盖中优先级规则

priority: 100
├─ 用途：安全/合规规则
├─ 例子：SQL 注入防护、数据保护
└─ 冲突时：覆盖所有其他规则
```

### 快速选择

```
这个规则涉及安全或合规吗？
├─ 是 → priority: 100
│
这个规则是项目强制的吗？
├─ 是 → priority: 30
│
这个规则是语言特定的吗？
├─ 是 → priority: 10
│
这个规则是建议性的吗？
├─ 是 → priority: 5
```

---

## 第 3 步：确定 enabled（启用状态）

**问题：这个规则现在是否应该被应用？**

```yaml
# 规则已准备好应用
enabled: true

# 实验性规则（暂时禁用）
enabled: false

# 临时禁用（等待项目准备）
enabled: false
```

---

## 第 4 步：确定 tags（分类标签）

**问题：这个规则属于哪个类别？**

### 常见标签

```yaml
# 按语言
tags: ["cpp", "python", "javascript", "csharp"]

# 按主题
tags: ["coding-standards", "naming-conventions", "error-handling"]
tags: ["performance", "security", "testing", "documentation"]

# 按范围
tags: ["language-specific", "project-specific", "team-specific"]

# 按状态
tags: ["experimental", "deprecated", "stable", "pending"]
```

### 选择标签的方法

```
选择 1-3 个最相关的标签：

1. 语言标签（如果适用）
   - cpp, python, javascript, csharp, java

2. 主题标签
   - coding-standards, security, performance, testing

3. 状态标签（如果需要）
   - experimental, deprecated, stable
```

---

## 第 5 步：确定元数据（可选）

**问题：这个规则的来源和版本是什么？**

```yaml
author: "team-name"           # 规则的作者或团队
version: "1.0"                # 规则版本
created: "2026-03-12"         # 创建日期
updated: "2026-03-12"         # 最后更新日期
description: "..."            # 规则描述
changelog: |                  # 变更日志
  v1.0: 初始版本
```

---

## 实际示例

### 示例 1：C++ const 修饰符规则

**规则文本：**
```
"使用 const 修饰符来标记不修改对象的方法"
```

**决策过程：**

```
第 1 步：paths
  → 这是 C++ 规则
  → paths: ["**/*.cpp", "**/*.h"]

第 2 步：priority
  → 语言特定规则
  → priority: 10

第 3 步：enabled
  → 已准备好应用
  → enabled: true

第 4 步：tags
  → C++ 编码标准
  → tags: ["cpp", "coding-standards"]

第 5 步：元数据
  → C++ 团队
  → author: "cpp-team", version: "1.0"
```

**最终 YAML：**

```yaml
---
paths:
  - "**/*.cpp"
  - "**/*.h"
priority: 10
enabled: true
tags: ["cpp", "coding-standards"]
category: "language-specific"
author: "cpp-team"
version: "1.0"
---

# C++ const 修饰符规则

## 规则说明
使用 const 修饰符来标记不修改对象的方法。

## 示例

### ❌ 错误做法
```cpp
int getValue() {  // 应该是 const
    return m_value;
}
```

### ✅ 正确做法
```cpp
int getValue() const {  // 正确
    return m_value;
}
```
```

---

### 示例 2：SQL 注入防护规则

**规则文本：**
```
"所有数据库查询必须使用参数化查询"
```

**决策过程：**

```
第 1 步：paths
  → 后端代码
  → paths: ["src/backend/**", "src/api/**"]

第 2 步：priority
  → 安全规则（最高优先级）
  → priority: 100

第 3 步：enabled
  → 已准备好应用
  → enabled: true

第 4 步：tags
  → 安全、SQL 注入防护
  → tags: ["security", "sql-injection"]

第 5 步：元数据
  → 安全团队
  → author: "security-team", version: "1.0"
```

**最终 YAML：**

```yaml
---
paths:
  - "src/backend/**"
  - "src/api/**"
priority: 100
enabled: true
tags: ["security", "sql-injection"]
category: "security"
author: "security-team"
version: "1.0"
---

# SQL 注入防护规则

## 规则说明
所有数据库查询必须使用参数化查询，防止 SQL 注入攻击。

## 示例

### ❌ 错误做法
```csharp
string query = $"SELECT * FROM users WHERE id = {userId}";
```

### ✅ 正确做法
```csharp
string query = "SELECT * FROM users WHERE id = @userId";
var result = db.ExecuteQuery(query, new { userId = userId });
```
```

---

### 示例 3：通用代码风格规则

**规则文本：**
```
"函数应该保持简洁，最多 50 行代码"
```

**决策过程：**

```
第 1 步：paths
  → 通用规则（所有文件）
  → paths: []（或省略）

第 2 步：priority
  → 建议性规则
  → priority: 5

第 3 步：enabled
  → 已准备好应用
  → enabled: true

第 4 步：tags
  → 代码风格、可读性
  → tags: ["code-style", "readability"]

第 5 步：元数据
  → 开发团队
  → author: "dev-team", version: "1.0"
```

**最终 YAML：**

```yaml
---
priority: 5
enabled: true
tags: ["code-style", "readability"]
category: "general"
author: "dev-team"
version: "1.0"
---

# 函数长度规则

## 规则说明
函数应该保持简洁，最多 50 行代码。

## 原因
- 提高代码可读性
- 便于测试和维护
- 降低复杂度
```

---

## 快速参考表

### 按规则类型

| 规则类型 | paths | priority | tags | 例子 |
|---------|-------|----------|------|------|
| 语言特定 | 文件扩展名 | 10 | 语言名 | C++ 编码标准 |
| 安全规则 | 相关目录 | 100 | security | SQL 注入防护 |
| 项目特定 | 项目目录 | 30 | 项目名 | 项目架构 |
| 通用规则 | 空 | 5 | 主题 | 代码风格 |
| 实验性 | 相关路径 | 10 | experimental | 新最佳实践 |

### 按优先级

| 优先级 | 用途 | 例子 |
|--------|------|------|
| 5 | 建议性 | 代码风格 |
| 10-15 | 语言特定 | C++ 标准 |
| 20-50 | 项目强制 | 架构规则 |
| 100 | 安全/合规 | SQL 注入防护 |

---

## 最佳实践

### 1. 从简单开始

```yaml
---
priority: 10
enabled: true
---
```

### 2. 使用一致的命名

```yaml
# ✓ 好
tags: ["cpp", "coding-standards"]

# ✗ 不好
tags: ["C++", "Coding Standards"]
```

### 3. 为复杂规则添加描述

```yaml
---
description: "C++ 编码标准，包括命名规范和内存管理"
---
```

### 4. 记录变更

```yaml
---
version: "2.0"
changelog: |
  v2.0: 添加了 const 修饰符检查
  v1.0: 初始版本
---
```

---

## 常见问题

### Q：如果规则适用于多种语言怎么办？

```yaml
---
paths:
  - "**/*.cpp"
  - "**/*.py"
  - "**/*.js"
priority: 5
tags: ["multi-language", "code-style"]
---
```

### Q：如果规则有例外怎么办？

```yaml
---
paths:
  - "**/*.cpp"
  - "!**/generated/**"  # 排除生成的代码
priority: 10
---
```

### Q：如何处理规则之间的依赖关系？

```yaml
---
depends_on: ["general.md", "security.md"]
priority: 15
---
```

---

## 总结

**5 步确定 YAML 元数据：**

1. **paths** - 确定规则适用的文件范围
2. **priority** - 确定规则的重要程度（5, 10, 30, 100）
3. **enabled** - 确定规则是否启用（true/false）
4. **tags** - 确定规则的分类（1-3 个标签）
5. **元数据** - 添加作者、版本等信息（可选）

**记住：** 从简单开始，根据需要添加更多字段。
