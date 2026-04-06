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

# C/C++ 可靠性规范 (Q/HNC 43 + MISRA C)

## 宏与表达式
- 宏值多于一项必须使用括号：`#define ERROR_DATA_LENGTH (10+1)`
- 函数宏每个参数必须括起来：`#define WEEKS_TO_DAYS(w) ((w)*7)`
- 除法/求余运算必须除零保护
- 避免隐式类型转换，使用显式 cast
- 多运算符表达式使用括号区分优先级

## 变量初始化
- 局部变量和全局静态变量在引用前必须初始化
- 类成员变量在构造函数中初始化
- 指针变量初始化为已知地址或 NULL
- 数组和动态内存必须赋初值

## 控制流
- 禁止使用 goto 语句
- switch 每个 case 必须有 break（无 break 时加注释说明）
- default 分支不能遗漏，即使不需要处理也保留 `default: break;`
- 不要在 if/while/switch 条件表达式中使用 ++/--

## 内存安全
- 空指针检查：读写内存前必须检查指针非 NULL
- 越界保护：数组访问前必须检查索引范围
- 通过 [] 访问数组，不要用 *(array+n)
- 运行中（init 后 exit 前）禁止 malloc 动态分配
- malloc/free 必须配对，仅在 init/exit 调用
- free 后立即将指针置 NULL
- 禁止函数内部申请超过 4096 字节的大数组

## 字符串安全
- 禁止 strcpy/strncpy/strcat/strncat，用 strlcpy/strlcat 代替
- 禁止 sprintf，用 snprintf 代替
- snprintf 禁止同一内存同时作输入和输出
- 传入缓冲区指针的接口必须包含长度参数

## 函数参数
- 仅作输入的指针参数加 const 修饰
- 会导致溢出的加法/乘法/移位运算，使用 Bit64
