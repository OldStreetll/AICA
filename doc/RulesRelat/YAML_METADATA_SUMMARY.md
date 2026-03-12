# 如何为新规则设计 YAML 元数据 - 快速总结

## 你的问题

> 如果对于一个新的文本，想作为规则进行使用的话，怎么来确定前面的 YAML 信息呢？

## 答案：5 步决策流程

### 第 1 步：paths（适用范围）

**问题：** 这个规则适用于哪些文件？

```yaml
# 语言特定
paths: ["**/*.cpp", "**/*.h"]

# 目录特定
paths: ["src/backend/**"]

# 通用规则
paths: []  # 或省略
```

### 第 2 步：priority（优先级）

**问题：** 这个规则的重要程度是多少？

```yaml
priority: 5      # 建议性（代码风格）
priority: 10     # 语言特定（C++ 标准）
priority: 30     # 项目强制（架构规则）
priority: 100    # 安全/合规（SQL 注入防护）
```

### 第 3 步：enabled（启用状态）

**问题：** 这个规则现在是否应该被应用？

```yaml
enabled: true    # 已准备好应用
enabled: false   # 实验性或临时禁用
```

### 第 4 步：tags（分类标签）

**问题：** 这个规则属于哪个类别？

```yaml
tags: ["cpp", "coding-standards"]
tags: ["security", "sql-injection"]
tags: ["performance", "optimization"]
```

### 第 5 步：元数据（可选）

**问题：** 这个规则的来源和版本是什么？

```yaml
author: "team-name"
version: "1.0"
created: "2026-03-12"
```

---

## 实际例子

### 例子 1：C++ const 修饰符规则

**规则文本：** "使用 const 修饰符来标记不修改对象的方法"

**YAML 元数据：**

```yaml
---
paths: ["**/*.cpp", "**/*.h"]
priority: 10
enabled: true
tags: ["cpp", "coding-standards"]
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

### 例子 2：SQL 注入防护规则

**规则文本：** "所有数据库查询必须使用参数化查询"

**YAML 元数据：**

```yaml
---
paths: ["src/backend/**", "src/api/**"]
priority: 100
enabled: true
tags: ["security", "sql-injection"]
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

### 例子 3：通用代码风格规则

**规则文本：** "函数应该保持简洁，最多 50 行代码"

**YAML 元数据：**

```yaml
---
priority: 5
enabled: true
tags: ["code-style", "readability"]
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

## 快速决策表

| 规则类型 | paths | priority | tags | 例子 |
|---------|-------|----------|------|------|
| 语言特定 | 文件扩展名 | 10 | 语言名 | C++ 编码标准 |
| 安全规则 | 相关目录 | 100 | security | SQL 注入防护 |
| 项目特定 | 项目目录 | 30 | 项目名 | 项目架构 |
| 通用规则 | 空 | 5 | 主题 | 代码风格 |

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
tags: ["cpp", "coding-standards"]  # ✓ 好
tags: ["C++", "Coding Standards"]  # ✗ 不好
```

### 3. 添加示例

总是包含正确和错误的代码示例。

### 4. 记录变更

```yaml
version: "2.0"
changelog: |
  v2.0: 添加了新检查
  v1.0: 初始版本
```

---

## 相关文档

我已经为你创建了详细的文档：

1. **YAML_METADATA_COMPLETE_GUIDE.md** - 完整指南
2. **HOW_TO_DESIGN_YAML_METADATA.md** - 详细设计指南
3. **YAML_METADATA_QUICK_GUIDE.md** - 快速参考
4. **RULE_TEMPLATES_LIBRARY.md** - 6 个规则模板

---

## 总结

**5 步确定 YAML 元数据：**

1. **paths** - 文件范围
2. **priority** - 重要程度（5, 10, 30, 100）
3. **enabled** - 启用状态（true/false）
4. **tags** - 分类标签（1-3 个）
5. **元数据** - 作者、版本等（可选）

**记住：** 从简单开始，根据需要添加更多字段。

**使用模板库快速创建新规则！**
