---
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.c"
  - "**/*.cc"
  - "**/*.cxx"
enabled: true
priority: 15
---

# AICA C/C++ 行为指导

## 代码解释
- 说明代码所属模块和在项目中的位置
- 描述调用链和执行流程
- 解释关键分支的含义和内存管理策略

## 测试生成
- 使用 Google Test 框架（TEST_F, EXPECT_EQ, ASSERT_NE 等宏）
- 不要使用 xUnit/NUnit/JUnit 等其他语言的测试框架

## Bug 修复
- 重点检查：内存泄漏（malloc/free 配对）、空指针解引用、数组越界
- 检查：隐式类型转换、除零风险、未初始化变量
- 字符串安全：禁止 strcpy/sprintf，用 strlcpy/snprintf
