---
paths:
  - "**/*.cpp"
  - "**/*.h"
  - "**/*.c"
  - "**/*.cc"
  - "**/*.cxx"
enabled: true
priority: 20
---

# C/C++ 注释规范 (Q/HNC 43)

## 文件头注释 (doxygen 格式)
```
/*!
 * @file filename.cpp
 * @brief 文件功能简述
 * @note 详细说明
 *
 * @version V1.00
 * @date YYYY/MM/DD
 * @author 作者/团队
 * @copyright 武汉华中数控股份有限公司软件开发部
 */
```

## 函数注释 (doxygen 格式)
```
/**
 * @brief 函数功能简述
 * @param [in] paramName：参数说明
 * @param [out] outParam：输出参数说明
 * @return 返回值说明
 * @attention 注意事项
 */
```

## 注释规则
- 注释率目标：程序总行数的 20%~30%
- 行末注释使用 `//`，`//` 后留一个空格
- `/* */` 内部前后留空格
- 大段代码注释使用 `#if 0` / `#endif`，不要用 `/* */` 或 `//`
- 嵌套结束处加注释标识（如 `} // while 循环`, `} // switch (key)`）
- 边写代码边注释，修改代码同时修改注释
- 不再有用的注释要及时删除
