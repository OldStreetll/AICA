# 如何为新规则设计 YAML 元数据 - 完整指南

## 你的问题

> 如果对于一个新的文本，想作为规则进行使用的话，怎么来确定前面的 YAML 信息呢？

## 简短答案

**使用 5 步决策流程：**

1. **paths** - 确定规则适用的文件范围
2. **priority** - 确定规则的重要程度
3. **enabled** - 确定规则是否启用
4. **tags** - 确定规则的分类
5. **元数据** - 添加作者、版本等信息

---

## 5 步决策流程

### 第 1 步：确定 paths（适用范围）

**问题：这个规则适用于哪些文件？**

**决策树：**

```
规则是否特定于某种编程语言？
├─ 是 → 指定文件扩展名
│  ├─ C++: ["**/*.cpp", "**/*.h"]
│  ├─ Python: ["**/*.py"]
│  ├─ JavaScript: ["**/*.js", "**/*.ts"]
│  └─ C#: ["**/*.cs"]
│
├─ 否，特定于某个目录？
│  ├─ 是 → 指定目录路径
│  │  ├─ 后端: ["src/backend/**"]
│  │  ├─ 前端: ["src/frontend/**"]
│  │  └─ 测试: ["**/*.test.*"]
│  │
│  └─ 否，通用规则 → 不指定 paths（或留空）
```

**示例：**

```yaml
# C++ 规则
paths:
  - "**/*.cpp"
  - "**/*.h"

# 后端规则
paths:
  - "src/backend/**"

# 通用规则
paths: []  # 或省略
```

### 第 2 步：确定 priority（优先级）

**问题：这个规则的重要程度是多少？**

**优先级指南：**

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

**快速决策：**

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

### 第 3 步：确定 enabled（启用状态）

**问题：这个规则现在是否应该被应用？**

```yaml
# 规则已准备好应用
enabled: true

# 实验性规则（暂时禁用）
enabled: false

# 临时禁用（等待项目准备）
enabled: false
```

### 第 4 步：确定 tags（分类标签）

**问题：这个规则属于哪个类别？**

**常见标签：**

```yaml
# 按语言
tags: ["cpp", "python", "javascript", "csharp"]

# 按主题
tags: ["coding-standards", "security", "performance"]

# 按范围
tags: ["language-specific", "project-specific"]

# 按状态
tags: ["experimental", "deprecated", "stable"]
```

**选择方法：** 选择 1-3 个最相关的标签

```yaml
# 例子 1：C++ 编码标准
tags: ["cpp", "coding-standards"]

# 例子 2：安全规则
tags: ["security", "sql-injection"]

# 例子 3：性能优化
tags: ["performance", "optimization"]
```

### 第 5 步：确定元数据（可选）

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

## 实际案例

### 案例 1：const 修饰符规则

**规则文本：**
```
"使用 const 修饰符来标记不修改对象的方法"
```

**分析过程：**

```
第 1 步：paths
  问题：这个规则适用于哪些文件？
  答案：C++ 文件（.cpp, .h）
  结果：paths: ["**/*.cpp", "**/*.h"]

第 2 步：priority
  问题：这个规则的重要程度？
  答案：语言特定规则（中等优先级）
  结果：priority: 10

第 3 步：enabled
  问题：规则是否准备好应用？
  答案：是
  结果：enabled: true

第 4 步：tags
  问题：规则属于哪个类别？
  答案：C++ 编码标准
  结果：tags: ["cpp", "coding-standards"]

第 5 步：元数据
  问题：规则的来源？
  答案：C++ 团队
  结果：author: "cpp-team", version: "1.0"
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

### 案例 2：SQL 注入防护规则

**规则文本：**
```
"所有数据库查询必须使用参数化查询，防止 SQL 注入"
```

**分析过程：**

```
第 1 步：paths
  问题：这个规则适用于哪些文件？
  答案：后端代码（src/backend）
  结果：paths: ["src/backend/**", "src/api/**"]

第 2 步：priority
  问题：这个规则的重要程度？
  答案：安全规则（最高优先级）
  结果：priority: 100

第 3 步：enabled
  问题：规则是否准备好应用？
  答案：是
  结果：enabled: true

第 4 步：tags
  问题：规则属于哪个类别？
  答案：安全、SQL 注入防护
  结果：tags: ["security", "sql-injection"]

第 5 步：元数据
  问题：规则的来源？
  答案：安全团队
  结果：author: "security-team", version: "1.0"
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

### 案例 3：通用代码风格规则

**规则文本：**
```
"函数应该保持简洁，最多 50 行代码"
```

**分析过程：**

```
第 1 步：paths
  问题：这个规则适用于哪些文件？
  答案：所有代码文件（通用规则）
  结果：paths: []（或省略）

第 2 步：priority
  问题：这个规则的重要程度？
  答案：建议性规则（低优先级）
  结果：priority: 5

第 3 步：enabled
  问题：规则是否准备好应用？
  答案：是
  结果：enabled: true

第 4 步：tags
  问题：规则属于哪个类别？
  答案：代码风格、可读性
  结果：tags: ["code-style", "readability"]

第 5 步：元数据
  问题：规则的来源？
  答案：开发团队
  结果：author: "dev-team", version: "1.0"
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

然后根据需要添加更多字段。

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

### 5. 定期审查

定期审查和更新规则，确保它们仍然相关。

---

## 常见问题

### Q1：如果规则适用于多种语言怎么办？

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

### Q2：如果规则有例外怎么办？

```yaml
---
paths:
  - "**/*.cpp"
  - "!**/generated/**"  # 排除生成的代码
priority: 10
---
```

### Q3：如何处理规则之间的依赖关系？

```yaml
---
depends_on: ["general.md", "security.md"]
priority: 15
---
```

### Q4：如何快速创建新规则？

使用模板库中的模板，快速创建新规则。

---

## 相关文档

我已经为你创建了以下文档来帮助你：

1. **HOW_TO_DESIGN_YAML_METADATA.md** - 详细的设计指南
2. **YAML_METADATA_QUICK_GUIDE.md** - 快速参考指南
3. **RULE_TEMPLATES_LIBRARY.md** - 6 个规则文件模板
4. **YAML_DESIGN_SUMMARY.md** - YAML 设计总结
5. **YAML_FRONTMATTER_DESIGN.md** - 详细设计说明

---

## 总结

**为新规则设计 YAML 元数据的 5 步流程：**

1. **paths** - 确定规则适用的文件范围
2. **priority** - 确定规则的重要程度（5, 10, 30, 100）
3. **enabled** - 确定规则是否启用（true/false）
4. **tags** - 确定规则的分类（1-3 个标签）
5. **元数据** - 添加作者、版本等信息（可选）

**记住：** 从简单开始，根据需要添加更多字段。

**使用模板库快速创建新规则！**
