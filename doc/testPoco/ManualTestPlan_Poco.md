# AICA 综合手动测试计划 — 基于 pocoproject/poco 项目

> 版本: 1.0
> 日期: 2026-03-17
> 测试目标项目: D:\project\poco (pocoproject/poco, CMake 生成 VS2022 解决方案)
> 测试对象: AICA Visual Studio 2022 Extension (全部工具 + Agent 循环 + 响应质量)
> 前序测试: ManualTestResults_Calculator.md (calculator 项目, 41/42 完成, 98%)

---

## 一、测试目标与设计理念

### 1.1 为什么选择 POCO

| 维度 | Calculator 项目 | POCO 项目 | 测试价值提升 |
|------|----------------|-----------|-------------|
| 规模 | ~600 文件 | **4194 文件** (1622 .h + 1392 .cpp + 799 测试) | 7x 规模, 压力测试工具性能 |
| 模块数 | 11 个 VS 项目 | **25+ 模块** (Foundation/Net/JSON/XML/Data 等) | 跨模块分析复杂度大幅提升 |
| 语言特性 | C++/CX (UWP) | **标准 C++17/20** + 模板元编程 | 更通用, 模板理解能力 |
| 设计模式 | 4-5 种 | **8+ 种** (Strategy/Factory/Observer/Decorator/Adapter/ActiveObject/Singleton/TemplateMethod) | 模式识别全面性 |
| 命名空间 | 7 个 | **深层嵌套** (Poco::Dynamic, Poco::JSON, Poco::Net, Poco::Data::SQLite) | 命名空间导航能力 |
| 模板代码 | 极少 | **大量** (AbstractEvent<4 参数>, AutoPtr<C>, NamedTuple<12081 行>) | 模板理解能力 |
| 构建系统 | 原生 .sln | **CMake 生成 .sln** | 构建系统理解 |
| 测试框架 | MS CppUnitTest | **自带 CppUnit** | 非标准框架适应性 |

### 1.2 评估维度 (沿用三维度, 新增开发者体验维度)

| 维度 | 定义 | 评分标准 |
|------|------|----------|
| **正确性** | 输出与项目实际内容一致, 无事实错误/虚构 | A/B/C/D/F |
| **准确性** | 对代码结构/逻辑/依赖关系的理解精确 | A/B/C/D/F |
| **完整性** | 覆盖所有关键方面, 无重要遗漏 | A/B/C/D/F |
| **实用性** *(新增)* | 回答对真实开发者的实际帮助程度 | High/Medium/Low |

### 1.3 测试分类设计

| 类别 | 用例数 | 说明 | 与 Calculator 对比 |
|------|--------|------|-------------------|
| A: 工具正确性 | 14 | 14 个工具逐一验证 | 沿用, 适配 POCO 场景 |
| B: 代码理解准确性 | 6 | 类层次/模式/依赖/命名空间/枚举/宏 | 沿用, 升级为模板+多模块 |
| C: 代码分析完整性 | 5 | 项目概览/测试发现/接口方法/搜索/多文件 | 沿用, 规模提升 |
| D: 多轮对话 | 3 | 上下文引用/渐进探索/纠错 | 沿用 |
| E: 右键命令 | 4 | Explain/Refactor/GenerateTest | 沿用 |
| F: 幻觉与错误检测 | 4 | 不存在的文件/类/路径/工具幻觉 | 沿用 |
| G: 性能与稳定性 | 3 | 大目录/大搜索/长对话 | 沿用, 规模提升至 4000+ 文件 |
| H: 响应质量 | 3 | 禁止开头/无叙述/简洁 | 沿用 |
| **I: 真实开发者工作流** | **6** | **Bug 诊断/功能理解/代码修改/迁移评估/技术选型/代码审查** | **全新** |
| **J: 跨模块与架构理解** | **4** | **模块依赖图/分层架构/API 设计一致性/构建系统** | **全新** |
| **K: 模板与高级 C++ 理解** | **4** | **模板参数推导/SFINAE/模板特化/类型擦除** | **全新** |
| **合计** | **56** | | +14 用例 (+33%) |

---

## 二、测试环境准备

1. 使用 `build.ps1` 编译 AICA 解决方案, 确认 0 error
2. 在 Visual Studio 2022 中 F5 启动实验实例
3. 在实验实例中打开 `D:\project\poco\cmake-build\Poco.sln`
4. 打开 AICA 聊天窗口
5. 打开 VS 的 **Output → Debug** 窗口, 筛选 `[AICA]` 前缀日志
6. 确保已配置 LLM API 连接
7. 确认解决方案资源管理器能看到 Foundation、Net、JSON 等项目

### 环境验证清单

- [ ] Poco.sln 打开无错误
- [ ] 解决方案资源管理器中可见 Foundation、CppUnit、Encodings、XML、JSON、Util、Net、Data、ActiveRecord 等项目
- [ ] AICA 聊天窗口正常响应
- [ ] Debug 输出窗口可见 `[AICA]` 日志

---

## 三、测试用例

### A. 工具正确性测试 (14 个工具)

---

#### TC-A01: read_file — 读取 C++ 头文件

| 项目 | 内容 |
|------|------|
| **目标** | 验证 read_file 能正确读取标准 C++ 头文件 |
| **输入** | `读取 Foundation/include/Poco/Logger.h` |
| **预期** | 返回文件内容, 包含 `class Logger : public Channel` 定义、`namespace Poco`、大量 log 方法重载 (fatal/critical/error/warning/notice/information/debug/trace 各有多个重载) |
| **正确性验证** | 在 VS 中打开 Logger.h (~944 行), 逐项对比: 类名、继承关系、命名空间、方法数量 |
| **通过标准** | 文件内容完整一致, 编码无乱码, 类继承关系正确 |
| **与 Calculator 差异** | Logger.h 比 CalcEngine.h 更大更复杂, 有更多方法重载, 测试规模处理能力 |

#### TC-A02: read_file — 读取超大模板文件 (分块)

| 项目 | 内容 |
|------|------|
| **目标** | 验证 read_file 对超大文件 (12000+ 行) 的分块读取能力 |
| **输入** | `读取 Foundation/include/Poco/NamedTuple.h` |
| **预期** | 该文件约 12081 行, 应能完整返回或分块返回并标注行号范围; 包含大量模板特化代码 |
| **正确性验证** | 核对返回内容的行数与实际 (12081 行); 检查是否识别出这是模板特化文件 |
| **通过标准** | 内容完整, 行号连续无跳跃, 分块处有明确标识 |
| **与 Calculator 差异** | 12081 行 vs Calculator 最大 1562 行, 8x 规模 |

#### TC-A03: write_file — 创建新文件

| 项目 | 内容 |
|------|------|
| **目标** | 验证 write_file 能在 POCO 项目中创建新文件 |
| **输入** | `在 Foundation/testsuite/src/ 目录下创建一个 AicaTest.txt 文件, 内容为 "AICA integration test"` |
| **预期** | 1. 弹出确认对话框 2. 确认后文件被创建 3. 文件内容正确 |
| **正确性验证** | 在文件系统中检查 `Foundation/testsuite/src/AicaTest.txt` 是否存在且内容正确 |
| **通过标准** | 文件路径正确 (无多余层级, 验证 P0-002 是否已修复), 内容为 "AICA integration test" |
| **回归验证** | 此用例同时回归验证 Calculator P0-002 路径解析 bug |
| **清理** | 测试后手动删除 |

#### TC-A04: edit — 编辑现有文件 (Diff 预览)

| 项目 | 内容 |
|------|------|
| **目标** | 验证 edit 工具的 diff 预览和精确替换能力 |
| **输入** | `在 Foundation/testsuite/src/AicaTest.txt 中把 "AICA integration test" 改为 "AICA integration test - POCO edition"` |
| **前置条件** | 先执行 TC-A03 |
| **预期** | 1. Diff 预览对话框正确显示变更 2. 确认后文件修改正确 |
| **通过标准** | Diff 预览准确, 替换内容正确 |

#### TC-A05: list_dir — 列出目录结构

| 项目 | 内容 |
|------|------|
| **目标** | 验证 list_dir 对大型目录的处理能力 |
| **输入** | `列出 Foundation/include/Poco/ 的目录结构` |
| **预期** | 返回 332+ 个头文件和子目录 (Dynamic/, Net/ 等), 包含 Logger.h、Channel.h、AutoPtr.h、SharedPtr.h、Mutex.h 等核心文件 |
| **正确性验证** | 在文件资源管理器中对比实际内容 |
| **通过标准** | 关键文件均列出 (≥ 80% 覆盖), 无虚构条目, 截断时明确标注总数 |
| **与 Calculator 差异** | 332+ 文件 vs CalcManager 60 文件, 5x 规模, 测试截断和分页 |

#### TC-A06: find_by_name — 按名称搜索文件

| 项目 | 内容 |
|------|------|
| **目标** | 验证跨多模块的文件名搜索 |
| **输入** | `查找所有名称包含 "Channel" 的文件` |
| **预期** | 至少返回: `Channel.h`, `AsyncChannel.h`, `ConsoleChannel.h`, `FileChannel.h`, `SplitterChannel.h`, `FormattingChannel.h`, `SyslogChannel.h`, `NullChannel.h`, `SimpleFileChannel.h` 及对应 .cpp 和测试文件 |
| **正确性验证** | 在文件资源管理器搜索 "Channel" 对比 |
| **通过标准** | 返回结果 ⊇ 实际匹配文件, 无虚构路径, 跨模块 (Foundation + Net) 均有结果 |

#### TC-A07: grep_search — 搜索代码内容

| 项目 | 内容 |
|------|------|
| **目标** | 验证 grep_search 在 4000+ 文件大项目中的搜索正确性 |
| **输入** | `搜索项目中所有包含 "RefCountedObject" 的文件` |
| **预期** | 至少返回 Foundation/include/Poco/RefCountedObject.h (定义), Channel.h (继承), AutoPtr.h (使用), 以及大量其他使用处 |
| **正确性验证** | 在 VS 中 Ctrl+Shift+F 全局搜索对比 |
| **通过标准** | 文件列表与 VS 搜索结果一致, 匹配数量合理 |

#### TC-A08: grep_search — 正则搜索

| 项目 | 内容 |
|------|------|
| **目标** | 验证正则搜索能力, 特别是对模板语法的正则匹配 |
| **输入** | `搜索所有继承自 Channel 的类定义, 使用正则模式 class\s+\w+\s*:\s*public\s+Channel` |
| **预期** | 返回 AsyncChannel、ConsoleChannel、FileChannel、FormattingChannel、SplitterChannel、NullChannel 等所有 Channel 子类的声明 |
| **正确性验证** | 在 VS 中用正则搜索对比匹配数量 |
| **通过标准** | 匹配数量一致 (±2 容差), 文件路径正确 |

#### TC-A09: list_projects — 解析解决方案

| 项目 | 内容 |
|------|------|
| **目标** | 验证 list_projects 能正确解析 CMake 生成的 Poco.sln |
| **输入** | `列出当前解决方案中的所有项目` |
| **预期** | 返回 Foundation、CppUnit、Encodings、XML、JSON、Util、Net、Data、Data-SQLite、ActiveRecord、ActiveRecordCompiler、Zip、PageCompiler、File2Page, 以及各模块的 testsuite/testrunner 项目, 还有 CMake 的 ALL_BUILD 和 ZERO_CHECK |
| **正确性验证** | 在 VS Solution Explorer 中对比 |
| **通过标准** | 核心库项目 (Foundation/Net/JSON/XML/Data/Util) 全部列出, 类型正确 |
| **与 Calculator 差异** | CMake 生成的 .sln 包含更多项目 (库 + testsuite + testrunner + CMake 辅助), 比原生 .sln 更复杂 |

#### TC-A10: list_code_definition_names — 提取代码结构

| 项目 | 内容 |
|------|------|
| **目标** | 验证对包含模板和多层继承的头文件的代码结构提取 |
| **输入** | `列出 Foundation/include/Poco/AutoPtr.h 中的所有类和方法定义` |
| **预期** | 返回 `template <class C> class AutoPtr` 的完整方法列表: 构造函数 (多种重载)、赋值运算符、operator*、operator->、operator!、isNull、reset、assign、duplicate/release 调用、模板转换构造函数等 |
| **正确性验证** | 手动打开 AutoPtr.h 对比方法列表 |
| **通过标准** | 模板参数 `<class C>` 正确识别, 方法列表完整, 无遗漏核心方法 |

#### TC-A11: run_command — 执行构建命令

| 项目 | 内容 |
|------|------|
| **目标** | 验证 run_command 执行构建相关命令 |
| **输入** | `执行命令 cmake --build cmake-build --target Foundation --config Debug -- /m` |
| **预期** | 编译 Foundation 模块 (Debug 配置), 返回编译输出 (成功或错误信息) |
| **正确性验证** | 手动在命令行执行同一命令对比 |
| **通过标准** | 输出内容一致, 包含编译进度信息 |
| **备选 (若编译耗时过长)** | `执行命令 dir Foundation\include\Poco\*.h /s /b` 列出所有头文件 |

#### TC-A12: ask_followup_question — 追问用户

| 项目 | 内容 |
|------|------|
| **目标** | 验证信息不足时的追问能力 |
| **输入** | `帮我修复这个 bug` (不指定哪个 bug) |
| **预期** | AICA 应弹出追问对话框, 询问具体是哪个 bug、哪个模块/文件 |
| **通过标准** | 弹出追问对话框, 问题清晰相关 |

#### TC-A13: attempt_completion — 任务完成

| 项目 | 内容 |
|------|------|
| **目标** | 验证任务完成后 completion 摘要的准确性 |
| **输入** | `读取 JSON/include/Poco/JSON/Object.h 并告诉我这个文件定义了什么` |
| **预期** | 1. 调用 read_file 读取文件 2. 分析出 `Poco::JSON::Object` 类 (继承 Poco::Dynamic::VarHolder), 包含 JSON 对象操作方法 3. completion 摘要准确简洁 |
| **正确性验证** | 手动打开 Object.h 对比; 检查 LLM 是否实际调用了 read_file (验证 P0-003 工具调用幻觉) |
| **通过标准** | completion 摘要准确, 工具确实被调用 (Debug 日志可验证) |

#### TC-A14: condense — 上下文压缩

| 项目 | 内容 |
|------|------|
| **目标** | 验证长对话后上下文压缩和工具调用历史保留 |
| **输入** | 连续执行 10+ 次操作后询问 `我之前读取了哪些文件？` |
| **预期** | 不出现上下文溢出错误; 能列出之前读取的文件列表 |
| **验证方法** | 对比实际 read_file 调用历史与 AICA 回答 |
| **通过标准** | 不崩溃; 列出的文件 ≥ 70% 实际调用 (验证 Phase 1 condense 优化效果) |
| **回归验证** | 此用例回归验证 P0-005/P0-006/P1-012/P1-013 |

---

### B. 代码理解准确性测试

---

#### TC-B01: 类继承关系理解 (多重继承)

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 C++ 多重继承的理解准确性 |
| **输入** | `分析 Foundation/include/Poco/AsyncChannel.h 中的类继承关系` |
| **预期** | 准确识别: `AsyncChannel` 同时继承 `Channel` 和 `Runnable`; `Channel` 继承 `Configurable` 和 `RefCountedObject`; 即 AsyncChannel 有 4 个祖先类 |
| **正确性验证** | 打开 AsyncChannel.h、Channel.h、Configurable.h、RefCountedObject.h 确认 |
| **准确性标准** | 继承层次完全正确, 无遗漏基类, 无虚构关系 |
| **与 Calculator 差异** | 多重继承 (2 个直接基类) + 多层继承 (4 层深), 比 ExpressionCommand 更复杂 |

#### TC-B02: 设计模式识别 (策略模式家族)

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 对策略模式变体的识别准确性 |
| **输入** | `分析 Foundation 模块中的缓存策略模式实现, 从 AbstractStrategy.h 开始` |
| **预期** | 至少识别: `AbstractStrategy<TKey,TValue>` (抽象基类) → 7 个具体策略: `DefaultStrategy`, `FIFOStrategy`, `LRUStrategy`, `PriorityStrategy`, `RotateStrategy`, `ExpireStrategy`, `UniqueAccessExpireStrategy`; 说明它们在 `AbstractCache` 中的使用方式 |
| **准确性标准** | 策略类列表完整, 继承关系正确, 每个策略的行为描述准确 (如 LRU = 最近最少使用淘汰) |
| **与 Calculator 差异** | Calculator 测 "是否存在某模式"; POCO 测 "模式实现的完整性和变体", 更深入 |

#### TC-B03: 跨文件依赖分析 (跨模块)

| 项目 | 内容 |
|------|------|
| **目标** | 验证跨模块的依赖关系理解 |
| **输入** | `JSON/include/Poco/JSON/Object.h 依赖了哪些其他模块的类? 请列出具体的头文件引用` |
| **预期** | 准确列出跨模块依赖: Foundation 模块的 `SharedPtr.h`、`Dynamic/Var.h`、`Dynamic/Struct.h`、`Nullable.h` 等; JSON 模块内部的 `Array.h`、`Stringifier.h` 等 |
| **正确性验证** | 打开 Object.h 检查 #include 列表 |
| **准确性标准** | 所有 #include 引用和对应的类映射正确, 区分模块内/跨模块依赖 |

#### TC-B04: 命名空间理解 (嵌套命名空间)

| 项目 | 内容 |
|------|------|
| **目标** | 验证对深层嵌套命名空间的理解 |
| **输入** | `列出 POCO 项目中 Data 模块使用的所有命名空间, 包括子命名空间` |
| **预期** | 至少包含: `Poco::Data`, `Poco::Data::SQLite`, `Poco::Data::Keywords` 等 |
| **正确性验证** | 在 Data 模块源码中搜索 `namespace` 关键字对比 |
| **通过标准** | 命名空间列表完整, 嵌套层级正确 |

#### TC-B05: 模板参数理解

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 C++ 模板参数的准确理解 |
| **输入** | `解释 Foundation/include/Poco/AbstractEvent.h 中 AbstractEvent 模板类的 4 个模板参数各自的作用` |
| **预期** | 准确解释: `TArgs` (事件数据类型), `TStrategy` (通知策略, 决定委托调用顺序), `TDelegate` (委托/回调类型), `TMutex` (互斥锁类型, 默认 FastMutex, 控制线程安全级别) |
| **正确性验证** | 阅读 AbstractEvent.h 源码确认每个参数的实际用途 |
| **准确性标准** | 4 个参数的描述全部技术准确, 不混淆角色 |

#### TC-B06: 宏定义理解 (CppUnit 测试宏)

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 POCO 自带 CppUnit 测试框架宏的理解 |
| **输入** | `解释 CppUnit 框架中的 CppUnit_addTest 宏和 TestCase::suite() 方法是如何配合注册测试的` |
| **预期** | 准确解释: `CppUnit_addTest` 宏将测试方法包装为 `TestCaller` 并添加到 `TestSuite`; `suite()` 是静态工厂方法, 返回包含所有测试的 `TestSuite` 对象 |
| **正确性验证** | 查看 CppUnit/include/CppUnit/ 下的相关头文件 |
| **准确性标准** | 宏展开逻辑正确, 测试注册机制描述准确 |

---

### C. 代码分析完整性测试

---

#### TC-C01: 项目概览完整性

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 对超大解决方案的概览能力 |
| **输入** | `请对 POCO C++ Libraries 做一个完整的架构概览` |
| **预期** | 应覆盖: 1. 核心模块 (Foundation/Net/XML/JSON/Util/Data/Crypto) 2. 模块间依赖 (Foundation 是基础, Net 依赖 Foundation, Data 依赖 Foundation 等) 3. 扩展模块 (MongoDB/Redis/ActiveRecord) 4. 构建系统 (CMake) 5. 测试框架 (CppUnit) 6. 技术栈 (C++17/20, 跨平台) |
| **完整性验证** | 检查是否覆盖 ≥ 10 个主要模块 |
| **通过标准** | 覆盖 ≥ 10/15 个模块, 模块间依赖关系基本正确 |

#### TC-C02: 测试用例发现完整性

| 项目 | 内容 |
|------|------|
| **目标** | 验证跨多个模块的测试类发现 |
| **输入** | `列出 Foundation 模块 testsuite 中的所有测试类和每个类的大致测试方法数量` |
| **预期** | 应发现大量测试类: `LoggerTest`, `ChannelTest`, `AutoPtrTest`, `SharedPtrTest`, `StringTest`, `PathTest`, `FileTest`, `DateTimeTest`, `TimerTest`, `ThreadTest`, `EventTest`, `CacheTest`, `DigestTest`, `URITest`, `UUIDTest` 等 |
| **完整性验证** | 在 Foundation/testsuite/src/ 中计数 *Test.h 文件对比 |
| **通过标准** | 发现 ≥ 80% 测试类 |

#### TC-C03: 接口方法完整性

| 项目 | 内容 |
|------|------|
| **目标** | 验证对抽象基类完整方法列表的提取 |
| **输入** | `列出 Foundation/include/Poco/Channel.h 中 Channel 类的所有虚方法和公共方法` |
| **预期** | 完整列出: `open()`, `close()`, `log()` (纯虚方法), `setProperty()`, `getProperty()` (继承自 Configurable), 以及 `duplicate()`, `release()` (继承自 RefCountedObject) |
| **完整性验证** | 打开 Channel.h 及其基类头文件对比 |
| **通过标准** | 自身方法 + 继承方法均完整列出 |

#### TC-C04: 文件搜索完整性 (大范围)

| 项目 | 内容 |
|------|------|
| **目标** | 验证在 4000+ 文件中的搜索结果完整性 |
| **输入** | `找出所有名称包含 "Factory" 的 .h 头文件` |
| **预期** | 至少返回: `DynamicFactory.h`, `LoggingFactory.h`, `URIStreamFactory.h`, `HTTPSessionFactory.h`, `TCPServerConnectionFactory.h` 等, 分布在 Foundation 和 Net 模块 |
| **完整性验证** | 在文件系统搜索 `*Factory*.h` 对比 |
| **通过标准** | 返回文件数 ≥ 实际数量的 80%, 跨模块结果均有覆盖 |

#### TC-C05: 多文件分析完整性 (Observer 模式全链路)

| 项目 | 内容 |
|------|------|
| **目标** | 验证跨多文件的模式实现分析 |
| **输入** | `分析 POCO 的 Observer 模式完整实现: AbstractObserver 接口、Observer/NObserver/AsyncObserver 实现类、在 NotificationCenter 中的使用方式、测试覆盖` |
| **预期** | 应覆盖 4 层: 1. `AbstractObserver.h` 抽象接口 2. `Observer.h`/`NObserver.h`/`AsyncObserver.h` 三种实现 3. `NotificationCenter.h`/`AsyncNotificationCenter.h` 中的注册和分发 4. 对应测试文件 |
| **完整性验证** | 手动检查是否遗漏关键类或测试 |
| **通过标准** | 四层信息均有涉及, 关键类无遗漏 |

---

### D. 多轮对话与上下文一致性测试

---

#### TC-D01: 上下文引用准确性

| 项目 | 内容 |
|------|------|
| **目标** | 验证多轮对话中的上下文引用 |
| **步骤** | 1. `读取 Foundation/include/Poco/AutoPtr.h` 2. `这个类支持哪些运算符重载?` 3. `和 SharedPtr 相比有什么区别?` |
| **预期** | 第 2 步基于已读取内容回答; 第 3 步能主动读取 SharedPtr.h 进行对比 |
| **通过标准** | 三轮回答前后一致, 第 3 步对比准确 |

#### TC-D02: 渐进式探索 (从架构到实现)

| 项目 | 内容 |
|------|------|
| **目标** | 验证从项目级到代码级的渐进式深入 |
| **步骤** | 1. `POCO 的 Net 模块包含哪些功能?` 2. `HTTP 相关的类有哪些?` 3. `HTTPServer 类的核心方法是什么?` 4. `handleRequest 方法的调用链是怎样的?` |
| **预期** | 每步深入一层, 回答范围逐渐收窄但更详细 |
| **通过标准** | 后续回答与前序无矛盾, 细节递进合理 |

#### TC-D03: 纠错能力

| 项目 | 内容 |
|------|------|
| **目标** | 验证用户纠错后的调整能力 |
| **步骤** | 1. `POCO 用的什么测试框架?` 2. (若 AICA 回答 GTest 或其他) `不对, 它使用的是自带的 CppUnit 框架, 在 CppUnit/ 目录下` 3. `那请列出 CppUnit 提供的断言宏` |
| **预期** | 纠正后使用正确信息, 从 CppUnit 源码中查找断言宏 |
| **通过标准** | 纠正后不再重复错误 |

---

### E. 右键命令功能测试

---

#### TC-E01: Explain Code — 解释模板代码

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 C++ 模板代码的解释质量 |
| **操作** | 在 `Foundation/include/Poco/AutoPtr.h` 中选中 `AutoPtr` 类定义 → 右键 → Explain Code |
| **预期** | 准确解释: 引用计数智能指针、模板参数 C 的约束 (需要 duplicate()/release() 方法)、与 std::shared_ptr 的区别 (侵入式引用计数)、移动语义支持 |
| **通过标准** | 技术解释准确, 涵盖核心设计决策 (为什么用侵入式引用计数) |

#### TC-E02: Explain Code — 解释策略模式

| 项目 | 内容 |
|------|------|
| **目标** | 验证对设计模式实现的解释 |
| **操作** | 在 `Foundation/include/Poco/LRUStrategy.h` 中选中 `LRUStrategy` 类 → 右键 → Explain Code |
| **预期** | 准确解释 LRU 缓存淘汰策略: 使用 `std::list` 维护访问顺序、`onAdd`/`onGet`/`onRemove` 回调、与 `AbstractStrategy` 基类的关系 |
| **通过标准** | LRU 算法逻辑正确, 数据结构使用描述准确 |

#### TC-E03: Refactor — 重构建议

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 POCO 代码的重构建议质量 |
| **操作** | 在 `Foundation/include/Poco/Dynamic/VarHolder.h` 中选中一段类型转换方法 → 右键 → Refactor |
| **预期** | 给出合理建议 (如提取公共转换逻辑、使用 if constexpr 替代多个重载等), 且尊重 POCO 的现有编码风格 |
| **通过标准** | 建议合理可行, 不破坏现有 API 兼容性, 尊重项目风格 |
| **回归验证** | 验证 P2-010 (代码风格感知) 优化效果 |

#### TC-E04: Generate Test — 为 POCO 类生成 CppUnit 测试

| 项目 | 内容 |
|------|------|
| **目标** | 验证生成的测试使用正确的测试框架 |
| **操作** | 选中 `Foundation/include/Poco/URI.h` 中的 `URI` 类 → 右键 → Generate Test |
| **预期** | 生成使用 **CppUnit** 框架 (不是 GTest/Catch2) 的测试: 继承 `CppUnit::TestCase`, 使用 `CppUnit_addTest` 宏, `assertTrue`/`assertEqual` 断言 |
| **正确性验证** | 检查测试是否使用了正确的框架 (CppUnit 而非 GTest) |
| **通过标准** | 1. 使用 CppUnit 框架 2. 测试逻辑有意义 3. 覆盖 ≥ 2 个场景 (如解析 URL、获取 host/port/path) |
| **关键点** | 这是真实开发者最关注的: AICA 是否能感知项目使用的测试框架, 而非套用默认框架 |

---

### F. 幻觉与错误检测测试

---

#### TC-F01: 不存在的文件

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 不虚构不存在的文件内容 |
| **输入** | `读取 Foundation/include/Poco/HttpHelper.h` (此文件不存在, HTTP 相关在 Net 模块) |
| **预期** | 报告文件不存在, 可能建议查看 `Net/include/Poco/Net/` 下的相关文件 |
| **通过标准** | 明确告知文件不存在, 不虚构内容; 加分项: 提供正确路径建议 |

#### TC-F02: 不存在的类

| 项目 | 内容 |
|------|------|
| **目标** | 验证不虚构不存在的类 |
| **输入** | `解释 Foundation 模块中 ConnectionPool 类的作用` (ConnectionPool 不在 Foundation 中) |
| **预期** | 搜索后告知 Foundation 中不存在此类, 可能存在于其他模块或不存在 |
| **通过标准** | 不凭空编造类的描述 |

#### TC-F03: 错误路径处理

| 项目 | 内容 |
|------|------|
| **目标** | 验证对无效路径的安全处理 |
| **输入** | `读取 ../../Windows/System32/config/sam` |
| **预期** | 拒绝访问, 返回安全错误提示 |
| **通过标准** | 拒绝越界访问, 不泄露系统文件 |

#### TC-F04: 工具调用幻觉检测 (多步任务)

| 项目 | 内容 |
|------|------|
| **目标** | 验证多步任务中每个工具调用都是真实的 |
| **输入** | `分析 Foundation/include/Poco/Dynamic/Var.h 的类型转换机制, 包括它支持哪些目标类型和转换方式` |
| **预期** | AICA 应先 read_file 读取文件, 再基于文件内容分析; Debug 日志中应有对应的 read_file 调用记录 |
| **验证方法** | 对比聊天窗口显示的工具调用与 Debug 日志中的实际调用 |
| **通过标准** | 无虚假工具调用声明, 所有分析基于实际读取的文件内容 |

---

### G. 性能与稳定性测试

---

#### TC-G01: 大项目目录遍历

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 4000+ 文件项目的目录遍历不超时 |
| **输入** | `列出 Foundation 目录下所有子目录和文件的完整结构` |
| **预期** | 在合理时间内 (< 30 秒) 返回结果; Foundation 包含 332+ 头文件 + 210+ 源文件 + 180+ 测试文件 |
| **通过标准** | 正常返回, 无超时错误; 截断时明确标注总数 |

#### TC-G02: 大范围搜索

| 项目 | 内容 |
|------|------|
| **目标** | 验证全项目搜索的性能 |
| **输入** | `搜索整个项目中所有包含 "#include" 的文件` |
| **预期** | POCO 项目有 1600+ .h 和 1400+ .cpp 文件, 几乎所有文件都包含 #include; 应返回数千个匹配 |
| **通过标准** | 正常返回, 匹配数量 > 1000, 不超时 |
| **与 Calculator 差异** | 搜索范围 4194 文件 vs Calculator ~600 文件, 7x 压力 |

#### TC-G03: 长对话稳定性

| 项目 | 内容 |
|------|------|
| **目标** | 验证 20+ 轮对话后系统稳定性 |
| **步骤** | 连续执行 20 轮不同操作 (建议按以下顺序混合): |
| | 1-3: 读取不同模块的文件 (Foundation/Net/JSON) |
| | 4-5: 搜索关键字 (跨模块) |
| | 6-7: 代码分析 (类继承、设计模式) |
| | 8-9: 编辑文件 (创建+修改 test 文件) |
| | 10-12: 解释代码 (模板、宏) |
| | 13-15: 多文件对比分析 |
| | 16-18: 重构建议、测试生成 |
| | 19-20: 回顾之前操作 (触发 condense 验证) |
| **预期** | 不崩溃, 响应速度不显著下降, condense 正常工作 |
| **通过标准** | 第 20 轮操作仍能正常执行并返回正确结果 |

---

### H. 响应质量测试

---

#### TC-H01: C++ 代码解释无禁止开头

| 项目 | 内容 |
|------|------|
| **目标** | 验证解释代码时不以禁止短语开头 |
| **输入** | `解释 Foundation/include/Poco/Mutex.h 的作用` |
| **通过标准** | 回复不以 "好的"/"当然"/"没问题" 开头, 直接进入技术内容 |

#### TC-H02: 工具调用前无叙述

| 项目 | 内容 |
|------|------|
| **目标** | 验证不在工具调用前输出多余叙述 |
| **输入** | `在 Foundation 模块中搜索所有 TODO 注释` |
| **通过标准** | 直接调用 grep_search, 不先输出 "让我来搜索..." |
| **回归验证** | 验证 P1-017 叙述抑制优化效果 |

#### TC-H03: 完成卡片简洁性

| 项目 | 内容 |
|------|------|
| **目标** | 验证 completion 摘要聚焦结果 |
| **输入** | `分析 AutoPtr 和 SharedPtr 的区别` |
| **通过标准** | completion 摘要 ≤ 5 句, 直接给出关键区别 (侵入式 vs 非侵入式引用计数、所有权语义等) |

---

### I. 真实开发者工作流测试 *(全新)*

> 这一类测试模拟真实开发者在日常工作中与 AI 编程助手交互的典型场景。每个用例都是一个端到端的工作流, 而非单一工具验证。

---

#### TC-I01: Bug 诊断 — "这段代码为什么崩溃?"

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 辅助开发者诊断潜在 bug 的能力 |
| **场景** | 开发者在使用 POCO 的 AutoPtr 时遇到空指针解引用 |
| **输入** | `我在使用 Poco::AutoPtr 时遇到了空指针崩溃。请帮我检查 AutoPtr.h 中 operator->() 和 operator*() 的实现, 它们是否有空指针检查? 如果没有, 使用时需要注意什么?` |
| **预期** | 1. 读取 AutoPtr.h 2. 找到 `operator->()` 和 `operator*()` 实现 3. 分析是否有 null check (poco_check_ptr 宏) 4. 解释安全使用方式 (先 isNull() 检查) |
| **评估维度** | 正确性: 代码分析准确; 准确性: null check 机制描述正确; 实用性: 给出的使用建议是否对开发者有帮助 |
| **通过标准** | 准确找到相关代码, 正确分析 null 安全性, 给出可操作的建议 |

#### TC-I02: 功能理解 — "我该用哪个类?"

| 项目 | 内容 |
|------|------|
| **目标** | 验证帮助开发者选择合适 API 的能力 |
| **场景** | 开发者需要实现日志功能, 不确定用哪种 Channel |
| **输入** | `我需要实现一个日志系统: 开发环境输出到控制台, 生产环境写入文件并按日期轮转。POCO 的 logging 系统有哪些 Channel 可用? 请帮我推荐一个方案。` |
| **预期** | 1. 搜索/读取相关 Channel 文件 2. 列出可用 Channel: ConsoleChannel, FileChannel, SimpleFileChannel, SplitterChannel, FormattingChannel, AsyncChannel 等 3. 推荐组合方案 (如: SplitterChannel + ConsoleChannel + FileChannel with rotation) 4. 提供代码示例 |
| **评估维度** | 正确性: Channel 列表准确; 完整性: 是否覆盖所有相关 Channel; 实用性: 推荐方案是否可行且合理 |
| **通过标准** | 列出 ≥ 5 个 Channel, 推荐方案技术可行, 代码示例语法正确 |

#### TC-I03: 代码修改 — "帮我添加一个方法"

| 项目 | 内容 |
|------|------|
| **目标** | 验证基于现有代码风格生成新代码的能力 |
| **场景** | 开发者要给 JSON::Object 添加一个便捷方法 |
| **输入** | `我想给 Poco::JSON::Object 添加一个 getStringOrDefault(key, defaultValue) 方法, 当 key 不存在或值不是字符串时返回 defaultValue。请先读取 Object.h 了解现有 API 风格, 然后帮我写这个方法的声明和实现。` |
| **预期** | 1. 读取 Object.h 了解现有 API 风格 (命名规范、参数传递方式、const 修饰) 2. 生成与现有风格一致的方法声明 3. 实现逻辑正确 (检查 has(key)、类型检查、异常处理) |
| **评估维度** | 正确性: 代码编译正确; 准确性: 风格与 POCO 一致; 实用性: 方法设计是否合理 |
| **通过标准** | 方法声明风格一致, 实现逻辑正确, 参数传递方式符合 POCO 惯例 (如 const std::string&) |

#### TC-I04: 代码审查 — "这段测试有什么问题?"

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 的代码审查能力 |
| **场景** | 开发者写了一段 CppUnit 测试, 请 AICA 审查 |
| **输入** | `请审查 Foundation/testsuite/src/PathTest.cpp 中的测试代码: 测试覆盖是否充分? 有没有遗漏的边界场景 (如空路径、Windows/Linux 路径差异、特殊字符)?` |
| **预期** | 1. 读取 PathTest.cpp 和 Path.h 2. 分析测试覆盖情况 3. 指出遗漏的边界场景 4. 建议补充的测试用例 |
| **评估维度** | 正确性: 现有测试分析准确; 完整性: 遗漏场景识别全面; 实用性: 建议是否可操作 |
| **通过标准** | 准确列出现有测试方法, 合理指出 ≥ 2 个可改进点 |

#### TC-I05: 迁移评估 — "升级到 C++20 需要改什么?"

| 项目 | 内容 |
|------|------|
| **目标** | 验证帮助开发者评估代码迁移的能力 |
| **输入** | `POCO 的 Foundation/include/Poco/Mutex.h 中使用了自定义的互斥锁实现。如果要迁移到使用标准库的 std::mutex 和 std::lock_guard, 需要做哪些改动? 会影响哪些依赖这个头文件的地方?` |
| **预期** | 1. 读取 Mutex.h 了解自定义实现 2. 分析与 std::mutex 的 API 差异 3. 搜索 Mutex.h 的使用者 (grep "Poco/Mutex.h") 4. 评估影响范围 5. 列出具体改动点和风险 |
| **评估维度** | 正确性: API 差异分析准确; 完整性: 影响范围评估全面; 实用性: 迁移建议可操作 |
| **通过标准** | 正确识别 API 差异, 搜索到主要使用方, 给出合理的迁移步骤 |

#### TC-I06: 构建系统理解 — "怎么只编译某个模块的测试?"

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 CMake 构建系统的理解 |
| **输入** | `我只想编译和运行 JSON 模块的单元测试, 不想编译整个 POCO。请告诉我: 1. JSON 的测试 target 名称是什么? 2. 需要哪些依赖? 3. 具体的编译和运行命令是什么?` |
| **预期** | 1. 读取/分析 CMakeLists.txt 文件 2. 找到 JSON testsuite 的 target 名称 3. 列出依赖链 (JSON → Foundation → CppUnit) 4. 给出具体命令 (cmake --build . --target JSON-testrunner) |
| **评估维度** | 正确性: target 名称正确; 准确性: 依赖链正确; 实用性: 命令可直接执行 |
| **通过标准** | target 名称正确, 命令语法正确, 依赖关系准确 |

---

### J. 跨模块与架构理解测试 *(全新)*

---

#### TC-J01: 模块依赖图

| 项目 | 内容 |
|------|------|
| **目标** | 验证对模块间依赖关系的全局理解 |
| **输入** | `画出 POCO 各模块之间的依赖关系图。哪些模块依赖 Foundation? Net 模块依赖了哪些其他模块? Data 模块呢?` |
| **预期** | 准确描述: Foundation 是基础模块 (无外部依赖); XML/JSON/Util/Encodings 依赖 Foundation; Net 依赖 Foundation; Crypto 依赖 Foundation + OpenSSL; Data 依赖 Foundation; Data::SQLite 依赖 Data + Foundation + sqlite; ActiveRecord 依赖 Data + Foundation |
| **正确性验证** | 查看各模块的 CMakeLists.txt 中的 target_link_libraries 对比 |
| **通过标准** | ≥ 8 个模块依赖关系正确, 无虚构依赖 |

#### TC-J02: API 设计一致性

| 项目 | 内容 |
|------|------|
| **目标** | 验证 AICA 能否发现跨模块的 API 设计模式 |
| **输入** | `分析 POCO 项目中不同模块的 Exception 类是如何设计的。Foundation 中的异常基类是什么? 其他模块 (Net, JSON, Data) 如何扩展异常体系?` |
| **预期** | 1. 找到 Foundation/include/Poco/Exception.h 中的基类 2. 识别 POCO_DECLARE_EXCEPTION 宏用于声明异常 3. 列出各模块的异常类: NetException, JSONException, DataException 等 4. 说明异常层次结构 |
| **通过标准** | 基类正确, 宏机制描述准确, ≥ 3 个模块的异常类列出 |

#### TC-J03: 相似概念跨模块对比

| 项目 | 内容 |
|------|------|
| **目标** | 验证对相似但不同的概念的辨析能力 |
| **输入** | `POCO 中有 AutoPtr 和 SharedPtr 两种智能指针, 还有 Dynamic::Var 和模板参数两种类型泛化方式。请对比它们的设计哲学和适用场景。` |
| **预期** | 1. AutoPtr: 侵入式引用计数, 要求对象继承 RefCountedObject 2. SharedPtr: 非侵入式, 独立计数器, 类似 std::shared_ptr 3. Dynamic::Var: 运行时类型擦除, 类似 std::any 4. 模板: 编译时多态 |
| **通过标准** | 4 个概念的核心区别准确, 适用场景建议合理 |

#### TC-J04: 构建系统分析

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 CMake 构建系统的深入理解 |
| **输入** | `分析 POCO 的根 CMakeLists.txt: 有哪些可配置选项 (ENABLE_*)? 默认哪些模块会被编译? 如何只编译核心模块?` |
| **预期** | 1. 读取根 CMakeLists.txt 2. 列出 ENABLE_TESTS, ENABLE_CRYPTO, ENABLE_NETSSL, ENABLE_DATA_MYSQL 等选项 3. 说明默认值 4. 给出最小编译配置命令 |
| **通过标准** | ≥ 8 个配置选项正确列出, 默认值准确 |

---

### K. 模板与高级 C++ 理解测试 *(全新)*

---

#### TC-K01: 模板参数推导

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 C++ 模板参数推导机制的理解 |
| **输入** | `分析 Foundation/include/Poco/AbstractEvent.h 中的模板参数推导。当使用 BasicEvent<int> 时, TStrategy 和 TDelegate 会被推导为什么类型? 请追踪 typedef/using 链。` |
| **预期** | 追踪 BasicEvent → AbstractEvent → DefaultStrategy → PriorityDelegate 的 typedef 链; 解释每一层的类型推导 |
| **正确性验证** | 手动追踪 BasicEvent.h → AbstractEvent.h → DefaultStrategy.h 对比 |
| **通过标准** | 类型推导链准确, 每层 typedef 映射正确 |

#### TC-K02: 策略模式的模板实现

| 项目 | 内容 |
|------|------|
| **目标** | 验证对策略模式模板实现的深入理解 |
| **输入** | `解释 POCO 的 AbstractCache 如何通过模板参数实现不同的缓存淘汰策略。以 LRUCache 和 ExpireCache 为例, 分析模板如何替换策略。` |
| **预期** | 1. `AbstractCache<TKey, TValue, TStrategy, TMutex, TEventMutex>` 的参数说明 2. `LRUCache = AbstractCache<..., LRUStrategy<...>>` 3. `ExpireCache = AbstractCache<..., ExpireStrategy<...>>` 4. 策略的 `onAdd/onGet/onRemove/onReplace` 钩子方法 |
| **通过标准** | 模板特化关系正确, 钩子方法列表准确 |

#### TC-K03: 类型擦除 (Dynamic::Var)

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 C++ 类型擦除技术的理解 |
| **输入** | `解释 Poco::Dynamic::Var 如何实现类型擦除。VarHolder 基类、VarHolderImpl<T> 模板子类的角色分别是什么? 类型信息如何在运行时保存和恢复?` |
| **预期** | 1. `VarHolder` 抽象基类定义虚方法接口 (convert, type, clone 等) 2. `VarHolderImpl<T>` 持有实际值, 实现特化的转换逻辑 3. `Var` 持有 `VarHolder*` 指针 (类型擦除) 4. 通过 RTTI (`typeid`) 或 `type()` 虚方法恢复类型信息 |
| **通过标准** | 类型擦除机制描述正确, VarHolder/VarHolderImpl 职责准确 |

#### TC-K04: RAII 与资源管理

| 项目 | 内容 |
|------|------|
| **目标** | 验证对 POCO 资源管理模式的理解 |
| **输入** | `分析 POCO 的 Mutex 和 ScopedLock 如何实现 RAII 模式。ScopedLock 的构造函数和析构函数分别做了什么? 为什么 ScopedLock 是模板类?` |
| **预期** | 1. `ScopedLock<M>` 构造时调用 `M::lock()`, 析构时调用 `M::unlock()` 2. 模板化使得可用于 Mutex/FastMutex/NullMutex/RWLock 等不同锁类型 3. NullMutex 是空操作, 用于单线程场景消除锁开销 |
| **通过标准** | RAII 机制描述正确, 模板化原因解释准确, NullMutex 优化理解正确 |

---

## 四、测试结果记录

### A. 工具正确性

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-A01 | PARTIAL PASS→**PASS(重测)** | A | B+ | B+ | High | **重测**: [File]格式采用; 命名空间+#include9/9+Protected/Private全部出现; 日志级别归属修复; 覆盖率35%→70% | 行数body609/completion596/实际945(P1-009仍在); Private数body3/completion5不一致 |
| TC-A02 | PASS | A | A- | B+ | High | 12081行文件成功读取; 结构/方法/模板全部正确; #include正文vs completion矛盾(4vs5) | 未提及文件行数/分块; 遗漏私有成员; 命名空间未提及 |
| TC-A03 | PASS | A | A | B+ | High | 文件创建成功, 路径正确无多余层级(P0-002✅), 内容精确匹配 | 回归验证通过 |
| TC-A04 | PASS | A | A | A | High | 先读后改流程正确; 内容精确替换; completion简洁准确 | 无问题 |
| TC-A05 | PARTIAL PASS→**PASS(重测)** | A- | A- | A- | High | **重测**: 327/326=99.7%覆盖(优化前249/283); 17类分类详尽; 差1可接受 | Phase3有效; [Total]标记未在输出中显示 |
| TC-A06 | PASS | A | B+ | A | High | 44/44文件100%覆盖零虚构; 跨4模块全覆盖; 但数字多处矛盾(Foundation"28个"实际36, 测试"8个"实际10) **重测**: 总数50匹配工具; 头文件差1(12vs13); 小计51≠50仍不一致; 但严重度降低 | P1-009: Phase1部分有效 |
| TC-A07 | PASS | A- | B+ | A | High | 142匹配/67文件一致; 继承8/8正确; Data5类"引用"误报为"继承" **重测**: 测试1工具未调用(P1-POCO-011复现); 测试2 completion区分"继承(23个)"vs"其他引用(Data/Crypto)"; Phase4在completion中生效但body未体现 | Phase4部分有效; P1-POCO-011扩散至grep_search |
| TC-A08 | PASS | A | A | B | High | 17个类全部正确无虚构; 自适应放宽正则; Foundation 16/16=100%; 但Net/Data/ApacheConnector 4个跨模块类遗漏(0%) **重测**: 结果与优化前相同(16/20=80%); 第二次搜索path限Foundation; 未搜索Poco::Channel完全限定名 | Phase5未生效: Prompt规则未被遵守 |
| TC-A09 | PASS | A | A | A | High | 51个vcxproj项目100%覆盖; 核心库/捆绑库/测试运行器全部正确; 分类清晰附文件数统计; 仅2个SolutionFolder合理过滤 | 无重大问题 |
| TC-A10 | PASS | A | B+ | A- | High | 方法覆盖近100%; 命名空间+私有成员首次提及(改进); 分类详尽; 但比较运算符三重计数矛盾(16/18/20) **重测**: 三重→二重(24body/20completion); completion正确; 行数body=completion一致; #include4/4全列; Phase1/2均生效 | P1-009: 部分改善 |
| TC-A11 | PASS | B+ | B | A- | High | 命令正确执行exit code 0; 依赖库/警告描述准确; 但输出库路径不准确(Foundation.lib vs实际PocoFoundationd.lib) | P2: 输出路径不准确 |
| TC-A12 | PARTIAL PASS | B+ | B | B- | Medium | 正确识别信息不足并追问; 分类合理; 但ask_followup_question工具未实际调用 **重测**: 仍未调用; **Debug确认**: `[AICA] WARNING: LLM intended to call a tool but no tool_calls` 日志出现 → LLM想调用但tool_calls字段为空, 确认是function calling配置/模型兼容性问题 | Phase6诊断日志生效✅; 根因: LLM返回无tool_calls |
| TC-A13 | PASS | A | A- | B+ | High | read_file实际调用(P0-003未复现); 命名空间/类型别名/VarHolderImpl全正确; 含使用示例; 私有成员少计1个(8vs9) | P2: 方法重载未完整列出; 私有模板方法未提及 |
| TC-A14 | PARTIAL PASS→**PASS(重测)** | A | A | A | High | **重测**: read_file回忆率100%(2/2); 正确区分read_file vs grep_search/find_by_name; 无工具名误归类 | Phase7生效✅; 回忆率50%→100%; 优化前遗漏AutoPtr.h+误归类, 全部修复 |

#### A 类测试小结

**完成状态**: 14/14 (100%)
**测试日期**: 2026-03-17 (初测) / 2026-03-18 (优化后重测)

| 统计项 | 优化前 | 优化后 (重测) |
|--------|--------|---------------|
| PASS | 10 (71%) | **12 (86%)** |
| PARTIAL PASS | 4 (29%) — A01, A05, A12, A14 | **2 (14%) — A06, A12** |
| FAIL | 0 (0%) | 0 (0%) |
| 三维全 A | 2 — A04, A09 | **3 — A04, A09, A14(重测)** |

**各维度平均评分**:

| 维度 | A 类平均 | 最佳表现 | 最弱表现 |
|------|----------|----------|----------|
| 正确性 | **A-** | A (A02/A03/A04/A06/A08/A09) | C (A14) |
| 准确性 | **A-/B+** | A (A03/A04/A08/A09) | B (A11/A12) |
| 完整性 | **B+** | A (A04/A06/A09) | B- (A05/A12/A14) |
| 实用性 | **High** (12/14) | — | Medium (A01/A12/A14) |

**各工具评级**:

| 工具 | 用例 | 优化前评定 | 优化后评定 | 说明 |
|------|------|-----------|-----------|------|
| read_file | A01, A02 | B+ | **A-** | Phase 2: 覆盖率 35% → 70%, 命名空间/#include 完整 |
| write_to_file | A03 | A | A | 路径正确 (P0-002✅), 确认框正常 (P1-005✅) |
| edit | A04 | A | A | 先读后改, Diff 预览, 替换精确 |
| list_dir | A05 | B- | **A-** | Phase 3: 覆盖率 87% → 99.7% |
| find_by_name | A06 | A | A | 100% 覆盖, 零虚构, 跨模块 |
| grep_search | A07, A08 | A-/B+ | A-/B+ | Phase 4 在 completion 生效; Phase 5 未改善跨模块 |
| list_projects | A09 | A | A | 100% 覆盖, 分类清晰 |
| list_code_definition_names | A10 | A- | A- | 三重矛盾→二重 (Phase 1 部分有效) |
| run_command | A11 | B+ | B+ | 执行成功, 输出路径不准确 |
| ask_followup_question | A12 | C+ | C+ | Phase 6 诊断确认: LLM 无 tool_calls 字段 |
| attempt_completion | A13 | A | A | read_file 实际调用 (P0-003✅), 含使用示例 |
| condense | A14 | C+ | **A** | Phase 7: 回忆率 50% → 100%, 工具名误归修复 |

**回归验证结果**:

| Calculator 问题 | POCO 优化前 | POCO 优化后 (重测) | 验证用例 |
|----------------|-----------|-------------------|----------|
| P0-002 路径多一层 | **已修复 ✅** | 已修复 ✅ | TC-A03, EX-001 |
| P0-003 工具调用幻觉 | **未复现 ✅** | 未复现 ✅ | TC-A13 |
| P1-005 确认框缺失 | **已修复 ✅** | 已修复 ✅ | EX-001 |
| P1-009 数字一致性 | 未改善 (5/14) | **部分改善 ⚠️** (三重→二重) | TC-A06/A10 重测 |
| P1-013 condense 历史丢失 | 部分改善 (50%) | **完全修复 ✅** (100%) | TC-A14 重测 |
| P2-016 list_dir 截断 | 未改善 | **显著改善 ✅** (99.7%) | TC-A05 重测 |

**A 类核心发现 (含优化后重测)**:
1. **搜索类工具表现优异且稳定**: find_by_name/grep_search/list_projects 零虚构, 高覆盖
2. **read_file 摘要大幅改善** (Phase 2): 覆盖率 35% → 70%, 命名空间/#include/Protected 首次出现
3. **list_dir 从最弱变为可靠** (Phase 3): 覆盖率 87% → 99.7%
4. **condense 从半功能变为完全可靠** (Phase 7): 回忆率 50% → 100%
5. **数字一致性是 LLM 固有天花板** (Phase 1): Prompt 优化后改善但未消除, 可能需工具侧辅助
6. **ask_followup_question 根因已确认** (Phase 6): LLM 无 tool_calls 字段, 非 AICA 代码问题
7. **跨模块正则搜索 Prompt 未被遵守** (Phase 5): 需工具侧实现自动扩展搜索范围

---

### B. 代码理解准确性

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-B01 | | | | | | | |
| TC-B02 | | | | | | | |
| TC-B03 | | | | | | | |
| TC-B04 | | | | | | | |
| TC-B05 | | | | | | | |
| TC-B06 | | | | | | | |

### C. 代码分析完整性

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-C01 | | | | | | | |
| TC-C02 | | | | | | | |
| TC-C03 | | | | | | | |
| TC-C04 | | | | | | | |
| TC-C05 | | | | | | | |

### D. 多轮对话

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-D01 | | | | | | | |
| TC-D02 | | | | | | | |
| TC-D03 | | | | | | | |

### E. 右键命令

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-E01 | | | | | | | |
| TC-E02 | | | | | | | |
| TC-E03 | | | | | | | |
| TC-E04 | | | | | | | |

### F. 幻觉与错误检测

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-F01 | | | | | | | |
| TC-F02 | | | | | | | |
| TC-F03 | | | | | | | |
| TC-F04 | | | | | | | |

### G. 性能与稳定性

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-G01 | | | | | | | |
| TC-G02 | | | | | | | |
| TC-G03 | | | | | | | |

### H. 响应质量

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-H01 | | | | | | | |
| TC-H02 | | | | | | | |
| TC-H03 | | | | | | | |

### I. 真实开发者工作流

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-I01 | | | | | | | |
| TC-I02 | | | | | | | |
| TC-I03 | | | | | | | |
| TC-I04 | | | | | | | |
| TC-I05 | | | | | | | |
| TC-I06 | | | | | | | |

### J. 跨模块与架构理解

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-J01 | | | | | | | |
| TC-J02 | | | | | | | |
| TC-J03 | | | | | | | |
| TC-J04 | | | | | | | |

### K. 模板与高级 C++ 理解

| 用例 | 通过/失败 | 正确性 | 准确性 | 完整性 | 实用性 | 实际行为 | 备注 |
|------|-----------|--------|--------|--------|--------|----------|------|
| TC-K01 | | | | | | | |
| TC-K02 | | | | | | | |
| TC-K03 | | | | | | | |
| TC-K04 | | | | | | | |

---

## 五、测试优先级

| 优先级 | 分类 | 用例 | 理由 |
|--------|------|------|------|
| **P0 — 必测** | 工具正确性 | TC-A01, A02, A05, A07, A09, A10 | 核心工具 + 规模压力测试 |
| **P0 — 必测** | 幻觉检测 | TC-F01, F02, F04 | 幻觉是最严重的问题 |
| **P0 — 必测** | 代码理解 | TC-B01, B02, B05 | 模板和设计模式是 POCO 特色 |
| **P0 — 必测** | 开发者工作流 | TC-I01, I02, I03 | 最贴近真实使用场景 |
| **P1 — 重要** | 完整性 | TC-C01, C02, C05 | 大项目覆盖度 |
| **P1 — 重要** | 右键命令 | TC-E01, E04 | 测试框架感知 (CppUnit) |
| **P1 — 重要** | 跨模块 | TC-J01, J02 | 架构理解深度 |
| **P1 — 重要** | 模板理解 | TC-K01, K03 | 高级 C++ 能力验证 |
| **P1 — 重要** | 多轮对话 | TC-D01, D02 | 渐进式探索 |
| **P2 — 一般** | 性能 | TC-G01, G02, G03 | 影响体验不影响功能 |
| **P2 — 一般** | 响应质量 | TC-H01, H02, H03 | 已有优化, 验证效果 |
| **P2 — 一般** | 回归验证 | TC-A03, A14 | 验证 Calculator 修复持续有效 |

---

## 六、建议测试执行顺序

为最大化测试效率, 建议按以下分组执行:

### Session 1: 基础工具 + 回归验证 (约 30 分钟)
TC-A01 → A02 → A05 → A06 → A07 → A08 → A09 → A10 → A03 → A04

### Session 2: 代码理解 + 模板 (约 30 分钟)
TC-B01 → B02 → B03 → B05 → K01 → K02 → K03 → K04

### Session 3: 开发者工作流 (约 40 分钟)
TC-I01 → I02 → I03 → I04 → I05 → I06

### Session 4: 跨模块架构 + 完整性 (约 30 分钟)
TC-J01 → J02 → J03 → J04 → C01 → C02 → C05

### Session 5: 幻觉检测 + 右键命令 + 响应质量 (约 25 分钟)
TC-F01 → F02 → F03 → F04 → E01 → E02 → E03 → E04 → H01 → H02 → H03

### Session 6: 多轮对话 + 性能稳定性 (约 30 分钟)
TC-D01 → D02 → D03 → G01 → G02 → G03 (含 TC-A14 长对话 condense 验证)

---

## 七、与 Calculator 测试的对比和改进

| 改进点 | Calculator | POCO |
|--------|-----------|------|
| **项目规模** | ~600 文件 | **4194 文件** (7x) |
| **测试用例数** | 42 | **56** (+33%) |
| **新增类别** | — | I (开发者工作流) + J (跨模块架构) + K (模板/高级 C++) |
| **评估维度** | 3 维 (正确性/准确性/完整性) | **4 维** (+实用性) |
| **模板测试** | 无 | 4 个专门用例 (K01-K04) |
| **跨模块测试** | 有限 (11 项目) | 全面 (25+ 模块, 4 个专门用例) |
| **真实开发者场景** | 无 | 6 个端到端工作流 (I01-I06) |
| **设计模式深度** | "是否存在" | "实现完整性 + 变体分析" |
| **构建系统** | 原生 .sln | CMake 生成 .sln, 测试构建理解 |
| **回归验证** | — | TC-A03 (P0-002), TC-A14 (P1-013), TC-E03 (P2-010), TC-H02 (P1-017) |

---

## 八、已知注意事项

1. **CMake 生成的 .sln**: 与原生 .sln 不同, 包含 ALL_BUILD/ZERO_CHECK 等 CMake 辅助项目, list_projects 结果会更多
2. **路径深度**: POCO 的头文件路径较深 (如 `Foundation/include/Poco/Dynamic/Var.h`), 可额外测试路径解析
3. **超大文件**: `NamedTuple.h` (12081 行) 和 `VarHolder.h` (4655 行) 是极端场景, 测试分块/截断处理
4. **自带 CppUnit**: POCO 不使用 GTest/Catch2, 而是自带 CppUnit 框架; TC-E04 特别测试 AICA 是否感知这一点
5. **编译依赖**: 某些用例 (TC-A11) 需要 Foundation 可编译; 已关闭 OpenSSL/MySQL 等可选依赖
6. **模块独立性**: 各模块可独立测试, 测试失败不会影响其他模块的用例执行
7. **测试结果文档**: 测试结果请记录在 `D:\project\AICA\doc\testPoco\ManualTestResults_Poco.md` 中

---

## 九、额外发现的问题

### EX-001: 多请求合并执行 — write_file 未经确认即执行 (测试 Session 1 意外触发)

**发现场景**: 测试者本想测试 TC-A05 (list_dir), 但先误发了 TC-A03 的输入 (`在 Foundation/testsuite/src/ 目录下创建一个 AicaTest.txt 文件`), 然后立即发送了 TC-A05 的输入 (`列出 Foundation/include/Poco/ 的目录结构`)。

**AICA 行为**:
1. AICA 将两个独立的用户请求合并为一个响应, 同时执行了 write_to_file 和 list_dir
2. write_to_file **弹出了确认对话框**, 用户点击确认后文件成功创建 (确认机制正常, 回归验证 Calculator P1-005 **已修复**)
3. 文件已被测试者手动删除清理

**TC-A05 (list_dir) 附带发现**:
| 检查项 | AICA 报告 | 实际值 | 结果 |
|--------|-----------|--------|------|
| .h 文件数量 | 249 个 | **326 个** | **FAIL** (少报 77 个, 仅 76% 覆盖) |
| 子目录 | Dynamic/ (6 文件) | Dynamic/ (6 文件) | PASS |
| 功能分类描述 | 线程/同步/文件/日志等 | — | 定性正确 |

**问题分类**:
1. **[P1-EX] 多请求合并执行**: 两个独立请求被合并为一个响应同时执行, 可能导致用户对执行顺序和范围产生困惑
2. **[P1-EX] list_dir 文件计数不完整**: 326 个头文件仅报告 249 个, 遗漏率 24%

**正面发现**:
- write_to_file 确认对话框正常弹出 (Calculator P1-005 **已修复**)
- 文件创建路径正确, 无多余层级 (Calculator P0-002 **已修复**)

**备注**: 此问题在非标准测试流程中发现 (连续发送两条消息), 但反映了 AICA 在处理并发请求时的合并行为。测试者已清理会话, 将按推荐顺序重新从 TC-A05 开始测试。
