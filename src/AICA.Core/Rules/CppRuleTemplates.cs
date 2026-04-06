using System.Collections.Generic;

namespace AICA.Core.Rules
{
    /// <summary>
    /// Embedded C/C++ coding standard rule templates (Q/HNC 43 + MISRA C).
    /// Used by RulesDirectoryInitializer to populate .aica-rules/ for C++ projects.
    /// </summary>
    internal static class CppRuleTemplates
    {
        /// <summary>
        /// Returns all C++ rule templates as (fileName, content) pairs.
        /// </summary>
        public static IReadOnlyList<(string FileName, string Content)> GetAll()
        {
            return new[]
            {
                ("cpp-code-style.md", CodeStyle),
                ("cpp-reliability.md", Reliability),
                ("cpp-file-io.md", FileIO),
                ("cpp-qt-specific.md", QtSpecific),
                ("cpp-comment-template.md", CommentTemplate),
                ("cpp-aica-guidance.md", AicaGuidance)
            };
        }

        public const string CodeStyle = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
enabled: true
priority: 20
---

# C/C++ 代码风格规范 (Q/HNC 43)

## 花括号
- 使用 Allman 风格：花括号单独占一行，与引用语句左对齐
- 例外：do-while/struct/union 后有 "";"" 的除外；头文件中仅一行的函数定义除外

## 缩进与行宽
- 水平缩进使用 4 个空格（Tab = 4 空格）
- 代码行最大长度 80 字符，长表达式在低优先级运算符处拆分

## 语句规范
- 一行只做一件事：只定义一个变量，或只写一条语句
- if/for/while/do 自占一行，执行语句不得紧跟其后
- 不论执行语句有多少（即使空语句），都要加 {} 表明语句块

## 命名规范
- 成员变量使用 m_ 前缀（如 m_nCount, m_strName）
- 禁止使用 public 成员变量，用 Get***/Set*** 接口代替
- 修饰符 * 和 & 紧靠变量名（如 `Bit8 *name = &value;`）

## 空行与空格
- 函数内部局部变量定义后、处理语句前加空行
- 每个函数定义结束后加空行；函数返回语句和其它语句之间加空行
- 函数名后紧跟 `(`，不留空格
- `,` 向前紧跟不留空格，后面留空格
- `;` 向前紧跟不留空格
- 二元运算符（+= >= <= && || <<）前后加空格
- 一元运算符（++ -- & *）前后不加空格
- `[]` `->` `.` 前后不加空格
- 文件最后一行为空行

## 类型系统
- 使用 HNC 类型宏：Bit8, Bit16, Bit32, Bit64, uBit32 等
- 禁止直接使用 int/long/short，统一用 HNC 类型宏
";

        public const string Reliability = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
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
";

        public const string FileIO = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
enabled: true
priority: 20
---

# C/C++ 文件读写规范 (Q/HNC 43)

## 文件操作配对
- fopen/fclose 必须配对，异常路径中不要忘记 fclose
- fclose 后将文件指针置 NULL
- nc_findfirst/nc_findclose 必须配对
- 禁止使用全局文件句柄变量

## 路径与打开
- 打开文件前用 stat() 检查路径有效性，不能仅靠 fopen 返回值
- Linux 下检查路径是否为文件夹（S_ISDIR）
- 文件名/文件夹名/路径名长度统一使用宏 PATH_NAME_LEN

## 读写检查
- fread/fwrite 必须检查返回值（实际读写的数据块个数）
- fseek 返回值不为 -1
- fgets 非文件尾时返回值不为空
- fputs 返回值不为 EOF

## 写文件落盘
- 写文件关闭前必须调用 fflush + fsync（Linux），保证数据立即写入磁盘
- 获取文件描述符：`Bit16 fd = fileno(fp);`

## 数据文件规范
- 新增数据文件必须有文件头（FileHead 结构）
- 保存时写入校验码（CRC32），载入时验证
- 新增文本文件必须有版本信息，读取时检查版本
";

        public const string QtSpecific = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
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
";

        public const string CommentTemplate = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
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
";

        public const string AicaGuidance = @"---
paths:
  - ""**/*.cpp""
  - ""**/*.h""
  - ""**/*.c""
  - ""**/*.cc""
  - ""**/*.cxx""
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
";
    }
}
