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

# Qt-C++ 专用规范 (Q/HNC 43)

## 头文件保护
- 使用 #ifndef/#define/#endif 防止重复引用
- 格式：`#ifndef CLASSNAME_H` / `#define CLASSNAME_H` / `#endif // CLASSNAME_H`

## 头文件引用
- 禁止 `#include <QtGui>`、`#include <QtCore>`、`#include <QtXml>` 等大包含
- 必须引用具体头文件（如 `#include <QKeyEvent>`）
- include 分类顺序：标准库 -> Qt 库 -> API -> APP
- 源文件 include 顺序：标准库 -> Qt 库 -> API -> APP -> 自身头文件

## 前置声明
- 如果 .h 中仅使用类的指针（未实例化），用前置声明代替 #include
- 前置声明放在 QT_BEGIN_NAMESPACE 和 QT_END_NAMESPACE 之间

## 类成员排序
- 访问类别顺序：public -> public slots -> signals -> protected -> protected slots -> private -> private slots
- 同一访问类别内：成员变量在前，成员函数在后

## 多语言支持
- 界面文本使用 tr() 函数包裹，支持国际化翻译

## HNC 软件开发规则 (Q/HNC 43 第8章)
- 提交代码前必须编译通过，禁止提交编译不过的代码
- API 接口必须有文档说明（参数、返回值、异常）
- 文件读写路径使用统一宏 PATH_NAME_LEN
- 菜单名命名需统一规范
- 键盘按键处理需统一方案
- 绘图配色需遵循 HNC 配色标准
- 多通道场景需考虑通道隔离
- 多分辨率场景需适配不同屏幕
- 多语言场景所有字符串使用 tr() 包裹
- 系统参数操作需通过统一接口
- 自定义控件需遵循 HNC 控件规范
