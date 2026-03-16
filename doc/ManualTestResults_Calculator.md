# AICA 手动测试验证结果 — 基于 microsoft/calculator 项目

> 验证日期: 2026-03-16 ~ 2026-03-17
> 测试计划: ManualTestPlan_Calculator.md
> 验证方法: 多 Agent 交叉验证（事实核查 + 精确度 + 覆盖度）

---

## 测试结果汇总

| 用例 | 正确性 | 准确性 | 完整性 | 综合评定 |
|------|--------|--------|--------|----------|
| TC-A01 | B | B- | D | PARTIAL |
| TC-A01 (修复后) | **A** | **A** | **B+** | **PASS** |
| TC-A02 | A- | A | B | PARTIAL PASS |
| TC-A03 | B- | B- | B+ | PARTIAL |
| TC-A03 (修复后) | **A** | **A** | **A** | **PASS** |
| TC-A04 | B+ | B | A- | PASS |
| TC-A04 (修复后) | **A** | **A** | **A** | **PASS** |
| TC-A05 | F | F | F | **FAIL** |
| TC-A05 (修复后) | **A** | **A** | **A** | **PASS** |
| TC-A06 | **A** | **A** | **A** | **PASS** |
| TC-A07 | **A** | **A** | **A** | **PASS** |
| TC-A08 | **A** | **A** | **A** | **PASS** |
| TC-A09 | **A** | **A** | **A** | **PASS** |
| TC-A10 | A- | B+ | B | PARTIAL PASS |
| TC-A11 | B+ | A | A | **PASS** |
| TC-A12 | **A** | **A** | **A** | **PASS** |
| TC-A13 | D | F | F | **FAIL** |
| TC-A13 (修复后) | **A-** | **B+** | **B+** | **PASS** |
| TC-A14 | F | F | F | **FAIL** |
| TC-A14 (修复后) | C | C | D | PARTIAL |
| | | | | |
| **B 类: 代码理解准确性** | | | | |
| TC-B01 | A- | A- | B+ | **PASS** |
| TC-B02 | B+ | B | B+ | PARTIAL PASS |
| TC-B03 | **A** | **A** | **A-** | **PASS** |
| TC-B04 | **A** | **A** | **A** | **PASS** |
| TC-B05 | A- | B+ | A- | **PASS** |
| TC-B06 | **A** | **A** | **A-** | **PASS** |
| | | | | |
| **C 类: 代码分析完整性** | | | | |
| TC-C01 | B+ | B+ | C+ | PARTIAL PASS |
| TC-C02 | A- | **A** | A- | **PASS** |
| TC-C03 | **A** | **A** | **A** | **PASS** |
| TC-C04 | B+ | A- | B | PARTIAL PASS |
| TC-C05 | **A** | **A-** | **A-** | **PASS** |

---

## TC-A01: read_file — 读取 C++ 头文件

**输入**: `读取 src/CalcManager/Header Files/CalcEngine.h`

### 正确性: B

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件读取 | PASS | 成功读取了正确的文件 |
| NUM_WIDTH 枚举 | FAIL | AICA 说值为 "QWORD、DWORD、WORD、BYTE"，实际为 `QWORD_WIDTH`、`DWORD_WIDTH`、`WORD_WIDTH`、`BYTE_WIDTH`，丢失了 `_WIDTH` 后缀 |
| 公共方法举例 | PASS | ProcessCommand、DisplayError、PersistedMemObject、GetCurrentResultForRadix 均存在 |
| 私有方法举例 | PASS | ProcessCommandWorker、ResolveHighestPrecedenceOperation、DoOperation 均存在 |
| 私有成员描述 | PASS | 确实存储了当前值、历史记录、精度、基数、角度类型 |

### 准确性: B-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 枚举值名称 | FAIL | 省略了 `_WIDTH` 后缀，不精确 |
| 类名 CCalcEngine | PASS | 正确 |
| 文件描述 | PASS | "计算器引擎类" 定位准确 |

### 完整性: D

AICA 的摘要仅覆盖了文件内容的约 15-20%：

| 类别 | 实际数量 | AICA 提及 | 遗漏率 |
|------|----------|-----------|--------|
| #include 指令 | 12 个 | 0 个 | 100% |
| 公共方法 | ~22 个 | 4 个 | 82% |
| 私有方法 | ~20 个 | 3 个 | 85% |
| 私有成员变量 | ~35 个 | 5 个(概述) | 86% |
| 前向声明 | 2 个 | 0 个 | 100% |
| friend 声明 | 1 个 | 0 个 | 100% |
| 静态方法 | 6 个 | 0 个 | 100% |
| 常量定义 | 1 个 | 0 个 | 100% |
| 接口引用 (ICalcDisplay, IHistoryDisplay) | 2 个 | 0 个 | 100% |

### 发现的问题

1. **[P0-001] 摘要严重不完整**: 仅覆盖约 15-20% 的文件内容，遗漏了大量公共方法、私有方法、成员变量
2. **[P1-001] 枚举值名称不精确**: NUM_WIDTH 枚举值省略了 `_WIDTH` 后缀（QWORD vs QWORD_WIDTH）
3. **[P1-002] 缺少关键结构信息**: 完全遗漏了 #include 依赖、前向声明、friend 声明、静态方法、接口引用
4. **[P2-001] read_file 请求应展示完整内容**: 用户请求"读取文件"，应直接展示文件内容，摘要作为补充而非替代

---

## TC-A02: read_file — 读取大文件（分块）

**输入**: `读取 src/CalculatorUnitTests/CopyPasteManagerTest.cpp`

### 正确性: A-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件读取 | PASS | 成功读取 1562 行文件 |
| TEST_CLASS 名称 | PASS | `CopyPasteManagerTest` 正确 |
| 3 个宏定义 | PASS | ASSERT_POSITIVE_TESTCASES、ASSERT_NEGATIVE_TESTCASES、VERIFY_MAXOPERANDLENGTHANDVALUE_ARE_EQUALS 均正确 |
| 列出的测试方法 | PASS | 所有提到的方法均真实存在，无虚构 |
| 模式覆盖描述 | PASS | Standard、Scientific、Programmer (Bin/Oct/Hex/Dec)、Converter 均正确 |

### 准确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 宏名称 | PASS | 3 个宏名称完全精确 |
| 方法名称 | PASS | 所有列出的方法名拼写正确 |
| 功能描述 | PASS | 各测试方法的功能描述与实际代码逻辑一致 |

### 完整性: B

| 类别 | 实际数量 | AICA 提及 | 遗漏 |
|------|----------|-----------|------|
| TEST_METHOD | 18 个 | 约 15 个 | 遗漏 3 个: `FunctionalCopyPasteTest`、`ValidateStandardScientificOperandLength`、`ValidateProgrammerOperandLength` |
| 私有 Helper 方法 | 19 个 | 0 个 | 100% 遗漏（如 ValidateProgrammerHexQwordPasteExpression 等 19 个子验证方法） |
| 宏定义 | 3 个 | 3 个 | 完整 |
| 成员变量 | 1 个 | 0 个 | `CopyPasteManager ^ m_CopyPasteManager` 未提及 |

### 发现的问题

1. **[P1-003] 遗漏 3 个 TEST_METHOD**: `FunctionalCopyPasteTest`、`ValidateStandardScientificOperandLength`、`ValidateProgrammerOperandLength`
2. **[P1-004] 完全遗漏私有 Helper 架构**: 19 个私有验证方法构成了测试的核心基础设施，未被提及
3. **[P2-002] 未提及文件行数**: 测试计划要求验证分块读取能力，AICA 未明确说明文件总行数（1562 行）

---

## TC-A03: write_file — 创建新文件

**输入**: `在 src/CalcManager/ 目录下创建一个 test_note.txt 文件，内容为 "AICA test file"`

### 正确性: B-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件创建 | PASS | 文件确实被创建 |
| 文件内容 | PASS | 内容为 `AICA test file`，正确 |
| 文件路径 | **FAIL** | 文件实际创建在 `src/src/CalcManager/test_note.txt`（多了一层 `src/`），而非预期的 `src/CalcManager/test_note.txt` |
| 确认对话框 | **未触发** | 测试计划预期弹出确认对话框，但从会话记录看直接写入了 |

### 准确性: B-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| completion 摘要路径 | **FAIL** | 摘要声称文件在 `src/CalcManager/`，实际在 `src/src/CalcManager/` |
| 内容写入 | PASS | 内容精确匹配 |

### 完整性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 任务执行 | PASS | 文件创建 + completion 流程完整 |
| 缺少确认步骤 | **FAIL** | 写入文件操作未经用户确认即执行 |

### 发现的问题

1. **[P0-002] 路径解析错误**: 文件写入路径 `src/src/CalcManager/` 多了一层 `src/`，说明工作目录或路径拼接逻辑有 bug。sln 文件在 `src/` 下，AICA 可能将 sln 所在目录作为 workspace root 又拼接了用户提供的 `src/` 前缀
2. **[P1-005] 写入文件未触发确认对话框**: 文件写入是破坏性操作，应弹出确认对话框让用户审批
3. **[P1-006] completion 摘要路径与实际不一致**: 声称在 `src/CalcManager/` 但实际在 `src/src/CalcManager/`

---

## TC-A04: edit — 编辑现有文件（Diff 预览）

**输入**: `在 src/CalcManager/test_note.txt 中把 "AICA test file" 改为 "AICA test file - modified"`

### 正确性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件编辑 | PASS | 内容成功从 `AICA test file` 变为 `AICA test file - modified` |
| 编辑路径 | FAIL | 与 TC-A03 相同的路径 bug，实际编辑 `src/src/CalcManager/test_note.txt` |
| Diff 预览对话框 | PASS | DiffEditorDialog 正常弹出，显示变更对比 |
| 用户确认 | PASS | 弹出确认窗口，用户确认后才执行修改 |

### 准确性: B

| 检查项 | 结果 | 详情 |
|--------|------|------|
| old_string/new_string | PASS | 匹配和替换均正确 |
| completion 描述 | PASS | 摘要准确描述了修改内容 |
| 路径声称 | FAIL | 声称路径与实际路径不一致（同 P0-002） |

### 完整性: A-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 编辑操作 | PASS | 替换成功 |
| Diff 预览 | PASS | DiffEditorDialog 正常工作 |
| 用户确认 | PASS | 确认流程完整 |
| completion 流程 | PASS | 正常调用 attempt_completion |

### 发现的问题

1. **[P0-002] 路径 bug 延续**: 与 TC-A03 同源，路径多一层 `src/`
2. 注意: Diff 预览和确认对话框均正常工作，edit 工具的 UI 交互流程符合设计预期

---

## TC-A05: list_dir — 列出目录结构

**输入**: `列出 src/CalcManager 的目录结构`

### 正确性: F

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 目录列出 | FAIL | 返回仅 1 个文件 (test_note.txt)，实际目录包含 **60 个文件**，含 3 个子目录 (CEngine/、Header Files/、Ratpack/) |
| 根因 | P0-002 路径 bug | list_dir 实际访问了 `src/src/CalcManager/`，而非真正的 `src/CalcManager/` |
| AICA 判断 | FAIL | 没有质疑结果合理性，反而说"其他源文件可能在不同位置"，合理化了错误结果 |

### 准确性: F

| 检查项 | 实际值 | AICA 报告 |
|--------|--------|-----------|
| 文件数 | 60 个 | 1 个 |
| 子目录 | CEngine/、Header Files/、Ratpack/ | 0 个 |
| 关键文件 | CalcManager.vcxproj、CalculatorManager.h/cpp、Command.h 等 | 全部遗漏 |

### 完整性: F

目录树覆盖率 **1/60 = 1.7%**，几乎完全缺失。

### 发现的问题

1. **[P0-002 升级] 路径 bug 影响范围扩大**: 不仅影响写入/编辑，还导致 list_dir 核心功能完全失效，返回了错误目录的内容
2. **[P1-008] 缺乏结果合理性校验**: CalcManager 是 vcxproj 项目目录，仅含 1 个 txt 文件显然不合理，AICA 应提示异常

---

## TC-A06: find_by_name — 按名称搜索文件

**输入**: `查找所有名称包含 "Rational" 的文件`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 搜索结果 | PASS | 返回 5 个文件，与文件系统实际完全一致 |
| 文件路径 | PASS | 所有路径均正确（路径 bug 已修复） |
| 无虚构 | PASS | 未返回不存在的文件 |

### 准确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件名拼写 | PASS | 全部正确 |
| 文件大小 | PASS | 提供了准确的文件大小信息 |
| 路径层级 | PASS | `src\CalcManager\CEngine\`、`src\CalcManager\Header Files\`、`src\CalculatorUnitTests\` 均正确 |

### 完整性: A

| 实际文件 | AICA 返回 | 结果 |
|----------|-----------|------|
| src\CalcManager\CEngine\Rational.cpp | 返回 | PASS |
| src\CalcManager\CEngine\RationalMath.cpp | 返回 | PASS |
| src\CalcManager\Header Files\Rational.h | 返回 | PASS |
| src\CalcManager\Header Files\RationalMath.h | 返回 | PASS |
| src\CalculatorUnitTests\RationalTest.cpp | 返回 | PASS |

覆盖率 **5/5 = 100%**，无遗漏无虚构。

---

## TC-A07: grep_search — 搜索代码内容

**输入**: `搜索项目中所有包含 "ICalcDisplay" 的文件`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件数量 | PASS | AICA: 12 个文件，实际: **12 个**，完全一致 |
| 匹配数 | PASS | 21 处匹配，合理 |
| 无虚构 | PASS | 所有文件均真实存在 |

### 准确性: A

| AICA 返回的文件 | 实际存在 | 功能描述正确性 |
|----------------|----------|----------------|
| Header Files\ICalcDisplay.h | PASS | "接口定义" 正确 |
| Header Files\CalcEngine.h | PASS | "使用 ICalcDisplay 参数" 正确 |
| Header Files\History.h | PASS | "包含接口头文件" 正确 |
| CalculatorManager.h | PASS | "继承 ICalcDisplay" 正确 |
| CalculatorManager.cpp | PASS | "构造函数参数" 正确 |
| CEngine\calc.cpp | PASS | PASS |
| CEngine\History.cpp | PASS | PASS |
| CalculatorDisplay.h | PASS | "实现 ICalcDisplay" 正确 |
| CalculatorDisplay.cpp | PASS | PASS |
| CalcManager.vcxproj | PASS | "项目文件引用" 正确 |
| CalcManager.vcxproj.filters | PASS | PASS |
| CalculatorManagerTest.cpp | PASS | "测试中的实现" 正确 |

### 完整性: A

覆盖率 **12/12 = 100%**，无遗漏。

---

## TC-A08: grep_search — 正则搜索

**输入**: `搜索所有 TEST_METHOD 定义，使用正则模式 TEST_METHOD(.*)`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 总匹配数 | PASS | AICA: 343，实际: **343**，完全一致 |
| 文件数量 | PASS | AICA: 17，实际: **17**，完全一致 |
| 无虚构 | PASS | 所有文件均存在 |

### 准确性: A

逐文件计数全部精确匹配：

| 文件 | AICA | 实际 | 结果 |
|------|------|------|------|
| UnitConverterViewModelUnitTests.cpp | 48 | 48 | PASS |
| StandardViewModelUnitTests.cpp | 47 | 47 | PASS |
| CalcInputTest.cpp | 37 | 37 | PASS |
| HistoryTests.cpp | 36 | 36 | PASS |
| CalculatorManagerTest.cpp | 21 | 21 | PASS |
| DateCalculatorUnitTests.cpp | 20 | 20 | PASS |
| NarratorAnnouncementUnitTests.cpp | 19 | 19 | PASS |
| CopyPasteManagerTest.cpp | 18 | 18 | PASS |
| NavCategoryUnitTests.cpp | 18 | 18 | PASS |
| CurrencyConverterUnitTests.cpp | 17 | 17 | PASS |
| MultiWindowUnitTests.cpp | 17 | 17 | PASS |
| UnitConverterTest.cpp | 16 | 16 | PASS |
| LocalizationSettingsUnitTests.cpp | 8 | 8 | PASS |
| RationalTest.cpp | 8 | 8 | PASS |
| UtilsTests.cpp | 6 | 6 | PASS |
| CalcEngineTests.cpp | 4 | 4 | PASS |
| LocalizationServiceUnitTests.cpp | 3 | 3 | PASS |

### 完整性: A

**17/17 文件，343/343 匹配数，100% 覆盖。**

---

## TC-A09: list_projects — 解析解决方案

**输入**: `列出当前解决方案中的所有项目`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 项目数量 | PASS | AICA: 11，sln 实际项目（排除 Solution Items 虚拟文件夹）: **11**，一致 |
| 排除虚拟文件夹 | PASS | 正确排除了 "Solution Items" |
| 无虚构 | PASS | 所有列出的项目均存在于 sln 中 |

### 准确性: A

| 项目名 | sln 类型 | AICA 类型 | 结果 |
|--------|----------|-----------|------|
| CalcManager | vcxproj | vcxproj | PASS |
| Calculator | csproj | csproj | PASS |
| Calculator.ManagedViewModels | csproj | csproj | PASS |
| CalculatorUITestFramework | csproj | csproj | PASS |
| CalculatorUITests | csproj | csproj | PASS |
| CalculatorUnitTests | vcxproj | vcxproj | PASS |
| CalcViewModel | vcxproj | vcxproj | PASS |
| CalcViewModelCopyForUT | vcxproj | vcxproj | PASS |
| GraphControl | vcxproj | vcxproj | PASS |
| GraphingImpl | vcxproj | vcxproj | PASS |
| TraceLogging | vcxproj | vcxproj | PASS |

**11/11 项目名和类型全部正确。** 额外提供了文件数和过滤器数信息。

### 完整性: A

覆盖率 **11/11 = 100%**。

---

## TC-A10: list_code_definition_names — 提取代码结构

**输入**: `列出 src/CalcManager/CalculatorManager.h 中的所有类和方法定义`

### 正确性: A-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 命名空间 | PASS | `CalculationManager` 正确 |
| 类名和继承 | PASS | `CalculatorManager final : ICalcDisplay` 正确 |
| 3 个枚举 | PASS | CalculatorMode、CalculatorPrecision、MemoryCommand 全部正确 |
| 方法存在性 | PASS | 列出的方法全部实际存在，无虚构 |
| 方法数量 | MINOR | 正文说 "48 个" → completion 说 "44 个"，实际约 **45 个**（42 public + 3 private），数字不一致但接近 |

### 准确性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 方法分类 | PASS | 内存/显示/模式/核心/历史分类合理准确 |
| 方法计数自相矛盾 | FAIL | 正文说 48，completion 说 44，两处不一致 |
| ICalcDisplay override | PASS | 12 个 override 方法均识别 |
| 构造函数 | 未明确提及 | `CalculatorManager(ICalcDisplay*, IResourceProvider*)` 未单独列出 |

### 完整性: B

| 类别 | 实际 | AICA 提及 | 结果 |
|------|------|-----------|------|
| 枚举 | 3 个 | 3 个 | PASS |
| 公共方法 | ~42 个 | 大部分列出 | PASS |
| 私有方法 | 3 个 | 部分 | PARTIAL |
| 私有成员变量 | 15 个 | 0 个 | **MISS** |
| 前向声明 | 2 个（Command、HISTORYITEM） | 0 个 | **MISS** |
| #include 指令 | 4 个 | 0 个 | **MISS** |
| 构造函数 | 1 个 | 未明确 | **MISS** |
| GetHistoryItems 重载 | 2 个 | 未区分 | **MISS** |

### 发现的问题

1. **[P1-009] 方法计数自相矛盾**: 正文说 48 个方法，completion 说 44 个，实际约 45 个
2. **[P1-010] 遗漏成员变量和前向声明**: 15 个私有成员变量、2 个前向声明、4 个 #include 均未提及
3. 注意: 相比 TC-A01 的 read_file 摘要（仅 15-20% 覆盖），list_code_definition_names 工具的方法覆盖率约 85%，有显著改善

---

## TC-A11: run_command — 执行构建命令

**输入**: `执行命令 dir src\CalculatorUnitTests\*.cpp 列出所有测试文件`

### 正确性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 文件列表 | PASS | 21 个 .cpp 文件，与实际完全一致 |
| 工具选择 | 注意 | 用户要求执行 `dir` 命令（run_command），AICA 用 list_dir 替代，结果正确但未遵循用户指定的工具 |
| 无虚构 | PASS | 所有列出文件均存在 |

### 准确性: A

21/21 文件名全部精确匹配，无拼写错误。

### 完整性: A

覆盖率 **21/21 = 100%**，无遗漏。

### 发现的问题

1. **[P2-003] 工具替换未告知用户**: 用户明确要求 `执行命令 dir`，AICA 使用 list_dir 替代。虽然结果正确且 list_dir 更安全，但应告知用户工具替换的原因

---

## TC-A12: ask_followup_question — 追问用户

**输入**: `重构这个方法` (不指定哪个方法/文件)

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 追问触发 | PASS | AICA 在信息不足时正确调用 ask_followup_question |
| 对话框弹出 | PASS | 弹出追问对话框 |
| 问题相关性 | PASS | 追问内容与用户请求相关，询问具体要重构哪个方法/文件 |

### 准确性: A

追问内容清晰、相关，未做错误假设。

### 完整性: A

符合预期行为，在信息不足时主动询问而非猜测。

---

## TC-A14: condense — 上下文压缩

**输入**: `我之前读取了哪些文件？` (在 12+ 轮对话之后)

### 正确性: F

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 回答用户问题 | FAIL | 用户问"之前读取了哪些文件"，AICA 从未回答此问题 |
| condense 后行为 | FAIL | 调用 condense 后完全丢失对话上下文，开始重放旧任务（创建 test_note.txt） |
| 工具调用混乱 | FAIL | 连续调用 write_to_file（失败）→ ask_followup_question（两次失败）→ edit（用户取消）→ attempt_completion |
| ask_followup_question | FAIL | options 参数两次触发 "Options array cannot be empty" 错误 |

### 准确性: F

completion 摘要说"文件操作已取消"，但用户根本没有请求文件操作。

### 完整性: F

condense 后完全丢失了之前 12 轮对话的上下文，未回答用户的问题。

### 发现的问题

1. **[P0-005] condense 后上下文丢失导致行为混乱**: condense 压缩上下文后，AICA 丢失当前对话意图，重放旧任务
2. **[P0-006] ask_followup_question options 参数 bug**: options 数组两次触发 "Options array cannot be empty" 错误
3. **[P1-012] condense 后未回答原始问题**: 即使 condense 有问题，也应继续处理用户的原始问题

---

## TC-A13: attempt_completion — 任务完成

**输入**: `读取 src/CalcManager/Command.h 文件并告诉我这个文件定义了什么`

### 正确性: D

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 工具调用 | **FAIL** | AICA 未实际调用 read_file，凭 LLM 知识生成内容。幻觉检测系统已捕获此问题 |
| 枚举名称 | **FAIL** | 声称 `NUMBER_0 ~ NUMBER_9`，实际为 `Command0 ~ Command9` |
| 命令名称 | **FAIL** | 声称 `ADD, SUBTRACT, MULTIPLY`，实际为 `CommandADD, CommandSUB, CommandMUL` |
| CommandGroup 枚举 | **FAIL** | 声称存在 `CommandGroup` 枚举类型，**文件中完全不存在，虚构** |
| 命令数量 | **FAIL** | 声称 101 个，实际 `CalculationManager::Command` 约 130+ 个（含 64 个 BINPOSxx） |

### 准确性: F

| AICA 声称 | 实际 |
|-----------|------|
| `NUMBER_0 ~ NUMBER_9` | `Command0 ~ Command9` |
| `ADD, SUBTRACT, MULTIPLY, DIVIDE` | `CommandADD, CommandSUB, CommandMUL, CommandDIV` |
| `CommandGroup` 枚举存在 | **不存在，虚构** |
| 1 个命名空间 | 实际 3 个（UnitConversionManager、CurrencyConversionManager、CalculationManager） |
| 101 个命令 | 实际 ~130+ 个 |

### 完整性: F

| 类别 | 实际 | AICA 提及 | 结果 |
|------|------|-----------|------|
| 命名空间 | 3 个 | 0 个明确提及 | MISS |
| 枚举类型 | 4 个（3个Command + 1个CommandType） | 2 个（1个正确 + 1个虚构） | FAIL |
| BINPOSxx 系列命令（64个） | 存在 | 未提及 | MISS |

### 发现的问题

1. **[P0-003] 工具调用幻觉**: AICA 未实际调用 read_file 就生成了文件内容描述（幻觉检测系统已捕获）
2. **[P0-004] 虚构不存在的内容**: `CommandGroup` 枚举在文件中完全不存在，是 LLM 凭空编造
3. **[P1-011] 枚举值命名系统性错误**: 所有命令名称缺少 `Command` 前缀

---

## TC-B01: 类继承关系理解

**输入**: `分析 src/CalcManager/ExpressionCommand.h 中的类继承关系`

### 正确性: A-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| CParentheses → IParenthesisCommand | PASS | 正确 |
| CUnaryCommand → IUnaryCommand | PASS | 正确 |
| CBinaryCommand → IBinaryCommand | PASS | 正确 |
| COpndCommand → IOpndCommand | PASS | 正确 |
| ISerializeCommandVisitor | PASS | 正确识别为抽象访问者接口 |
| final 关键字 | PASS | 正确识别所有类都是 final |
| 设计模式识别 | PASS | 正确识别命令模式 + 访问者模式 |
| 接口定义位置 | MINOR | 声称接口"定义在 ExpressionCommandInterface.h"，但 ISerializeCommandVisitor 实际定义在 ExpressionCommand.h 本文件中 |

### 准确性: A-

继承关系图、类名拼写全部正确。ISerializeCommandVisitor 的定义位置有小偏差。

### 完整性: B+

| 类别 | 实际 | AICA 提及 | 结果 |
|------|------|-----------|------|
| 类继承关系 | 4 对 | 4 对 | **100%** |
| ISerializeCommandVisitor 的 4 个 Visit 重载 | 4 个 | 提及但未详列 | PARTIAL |
| 各类的方法列表 | ~25 个方法 | 仅提及 GetCommandType/Accept | MISS |
| 私有成员变量 | ~10 个 | 未提及 | MISS |

继承关系覆盖 100%，但方法和成员变量未详列（用户主要问的是继承关系，可接受）。

---

## TC-B02: 设计模式识别

**输入**: `分析 CalcManager 模块使用了哪些设计模式`

### 正确性: B+

| 识别的模式 | 结果 | 详情 |
|------------|------|------|
| 观察者模式 (ICalcDisplay, IHistoryDisplay) | **PASS** | 正确，经典回调/观察者模式 |
| 访问者模式 (ISerializeCommandVisitor + Accept) | **PASS** | 正确，有代码依据 |
| 命令模式 (CParentheses, CUnaryCommand 等) | **PASS** | 正确，将操作封装为对象 |
| 接口分离原则 (ISP) | **PASS** | 正确，4 个细粒度接口 |
| 桥接模式 (CCalcEngine ↔ ICalcDisplay) | **PARTIAL** | 更准确是依赖倒置/回调模式，非严格桥接 |
| 单例模式 (CalculatorManager) | **FAIL** | CalculatorManager 构造函数是 public 的，不是单例 |
| 模板方法模式 (Rational) | **FAIL** | Rational 使用运算符重载，不是模板方法模式 |

### 准确性: B

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 观察者模式描述 | PASS | ICalcDisplay 回调机制准确 |
| 访问者模式描述 | PASS | Visit/Accept 机制准确 |
| 命令模式描述 | PASS | 4 个命令类正确 |
| 模板方法模式 | FAIL | 错误归类，Rational 不使用此模式 |
| "支持撤销/重做" | FAIL | completion 声称命令模式"支持撤销/重做"，代码中无此机制 |
| 正文与 completion 不一致 | MINOR | 正文列 6 个模式，completion 列 5 个，分类有差异 |

### 完整性: B+

| 测试计划预期的模式 | AICA 是否识别 | 结果 |
|-------------------|---------------|------|
| Command 模式 (ExpressionCommand 层次) | 是 | PASS |
| Visitor 模式 (ISerializeCommandVisitor) | 是 | PASS |
| Observer/Callback 模式 (ICalcDisplay) | 是 | PASS |
| Factory 模式 (CalculatorManager 管理 3 个引擎) | **否** | **MISS** |

### 发现的问题

1. **[P1-014] 虚构不存在的设计模式**: 错误归类模板方法模式（Rational）和单例模式（CalculatorManager）
2. **[P1-015] 无代码依据的功能声称**: 声称命令模式"支持撤销/重做"，但代码中无 undo/redo
3. **[P2-004] 遗漏 Factory 模式**: CalculatorManager 管理 3 个 CCalcEngine 实例（standard/scientific/programmer），根据模式切换选择引擎

---

## TC-B03: 跨文件依赖分析

**输入**: `CalculatorManager 类依赖了哪些其他类？请列出具体的头文件引用`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| #include 列表 | PASS | 4 个 #include 全部正确，无遗漏无虚构 |
| 各头文件对应的类 | PASS | CalculatorHistory、CCalcEngine、Rational、ICalcDisplay 映射正确 |
| 前向声明依赖 | PASS | 正确识别 Command (enum) 和 HISTORYITEM (struct) |
| 成员变量依赖 | PASS | ICalcDisplay\*、CCalcEngine\*、IResourceProvider\* 等均正确 |
| 依赖注入模式 | PASS | 正确识别 DI 用法 |

### 准确性: A

4 个头文件路径和对应类名全部精确匹配。

### 完整性: A-

| 类别 | 实际 | AICA 提及 | 结果 |
|------|------|-----------|------|
| #include 指令 | 4 | 4 | **100%** |
| 前向声明 | 2 (Command, HISTORYITEM) | 2 | **100%** |
| 关键成员变量 | 5+ 个 | 5 个列出 | PASS |
| IHistoryDisplay 间接依赖 | 通过 CalcEngine.h 引入 | 未提及 | MINOR |

---

## TC-B04: 命名空间理解

**输入**: `列出 CalcManager 项目中使用的所有命名空间`

### 正确性: A

全部 7 个命名空间（含子命名空间和前向声明）正确识别，无虚构。

### 准确性: A

| AICA 列出 | 实际 | 用途描述 |
|-----------|------|----------|
| CalculationManager | 6 处声明 | 业务逻辑层 — 正确 |
| CalcEngine | 4 处声明 | 计算引擎核心 — 正确 |
| CalcEngine::RationalMath | 1 处声明 | 有理数运算 — 正确 |
| UnitConversionManager | 2 处声明 | 单位转换 — 正确 |
| UnitConversionManager::NumberFormattingUtils | 2 处声明 | 数字格式化 — 正确 |
| CurrencyConversionManager | 1 处声明 | 货币转换 — 正确 |
| CalculatorEngineTests | 1 处前向声明 | 测试 — 正确（正文中提及） |

### 完整性: A

覆盖率 **7/7 = 100%**。额外提供了声明数量统计（55 处/32 文件）。

---

## TC-B05: 枚举值理解

**输入**: `Command.h 中定义了哪些枚举？列出 CalculatorCommand 的前 10 个值`

### 正确性: A-

4 个枚举全部正确识别。正确指出实际名称是 `CalculationManager::Command` 而非 "CalculatorCommand"。前 10 个值名称和数值全部正确。

### 准确性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 枚举值名称和数值 | PASS | 全部正确 |
| UnitConversionManager 计数 (16) | PASS | 正确 |
| CurrencyConversionManager 计数 (15) | PASS | 正确 |
| CalculationManager::Command 计数 | MINOR | 声称 101 个，实际约 130+（含 64 个 BINPOSxx） |
| 正文与 completion 排序不一致 | FAIL | 正文按声明顺序（从 DEG=321 开始），completion 按数值排序（从 NULL=0 开始） |

### 完整性: A-

4/4 枚举类型 100% 覆盖，前 10 个值完整列出。

---

## TC-C01: 项目概览完整性

**输入**: `请对 Calculator 解决方案做一个完整的架构概览`

### 正确性: B+

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 项目数量 | PASS | 11 个项目，正确 |
| 分层架构 | PASS | UI层(C#) → ViewModel层(C++/CX) → 业务逻辑层(C++) |
| 架构模式 | PASS | MVVM + 命令 + 观察者 + 访问者 |
| 技术栈 | PARTIAL | 提到 C#、C++/CX，遗漏 DirectX、UWP、XAML |
| 未主动探索 | MINOR | 未调用工具做新探索，仅依赖之前对话知识 |

### 准确性: B+

提到的内容全部正确，ICalcDisplay 解耦、OBSERVABLE_PROPERTY_RW 绑定描述准确。

### 完整性: C+

| 预期覆盖项 | 是否提及 | 结果 |
|------------|----------|------|
| 分层架构 (Engine → ViewModel → View) | 是 | PASS |
| 所有主要模块 | 部分（遗漏 TraceLogging、CalcViewModelCopyForUT、GraphingInterfaces） | PARTIAL |
| 项目间依赖关系 | 仅提及 ICalcDisplay，无完整依赖图 | MISS |
| 技术栈 (C++/CX, XAML, UWP, DirectX) | 部分 | PARTIAL |
| 测试项目 | 简要提及 | PARTIAL |

项目覆盖约 7/11，略低于测试计划的 "≥ 8/10" 通过标准。completion 摘要仅 3 行，对于"完整架构概览"来说过于简略。

### 发现的问题

1. **[P1-016] 架构概览过于简略**: "完整架构概览"仅产出 3 行摘要，缺少依赖图、技术栈细节
2. **[P2-005] 未主动补充探索**: 有足够的工具可用但未主动调用 list_dir/list_projects 来补充信息

---

## TC-C02: 测试用例发现完整性

**输入**: `列出 CalculatorUnitTests 项目中的所有测试类和每个类的测试方法数量`

### 正确性: A-

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 总测试方法数 | PASS | 343，完全一致 |
| 逐文件计数 | PASS | 17 个文件每个计数全部精确 |
| 文件数正文描述 | MINOR | 正文声称 "16 个测试文件"，实际 17 个，但表格中 17 个全部列出 |

### 准确性: A

17 个文件逐一计数全部精确匹配，测试类名称全部正确。

### 完整性: A-

覆盖率 **17/17 = 100%**，远超测试计划的 "≥ 11/13" 通过标准。正文数字（16）与表格（17 行）有微小不一致。

---

## TC-C04: 文件搜索完整性

**输入**: `找出所有 .h 头文件中定义了 class 关键字的文件`

### 正确性: B+

搜索范围仅限 CalcManager 目录，遗漏了 `src/CalcViewModel/Common/Utils.h`。CalcManager 范围内 7 个文件全部正确。

### 准确性: A-

列出的 7 个文件和 18 个类/接口名称全部正确，无虚构。

### 完整性: B

| 范围 | 实际 | AICA | 覆盖率 |
|------|------|------|--------|
| 全项目 | 8 个 | 7 个 | 87.5% |
| CalcManager | 7 个 | 7 个 | 100% |

遗漏: `src/CalcViewModel/Common/Utils.h`。达到测试计划 "≥ 80%" 通过标准。

### 发现的问题

1. **[P2-006] 搜索范围不完整**: 用户要求"所有 .h 头文件"，AICA 仅搜索了 CalcManager 目录

---

## TC-C05: 多文件分析完整性

**输入**: `分析 Rational 类的完整实现：头文件中的声明、cpp 中的实现、测试文件中的覆盖`

### 正确性: A

三层文件（Rational.h → Rational.cpp → RationalTest.cpp）全部正确定位、读取和分析。

### 准确性: A-

| 层级 | 分析内容 | 结果 |
|------|----------|------|
| 头文件 | 命名空间、常量、7 个构造函数、19 个运算符重载 | PASS |
| 实现 | PRAT 转换模式、ratpak 库函数列表（addrat/subrat/mulrat 等） | PASS |
| 测试 | 8 个 TEST_METHOD 全部列出，正确指出仅覆盖取模运算 | PASS |
| 行数 | Rational.h=58 行、RationalTest.cpp=175 行（实际约 221 行，有偏差） | MINOR |

### 完整性: A-

三层覆盖全部达成，额外识别了测试覆盖缺口（"未测试加减乘除、位运算、比较运算"）。未主动提及 RationalMath.h/cpp 但用户未要求。

---

## TC-C03: 接口方法完整性

**输入**: `列出 ICalcDisplay 接口的所有虚方法`

### 正确性: A

11 个纯虚方法全部正确列出，无虚构无遗漏。

### 准确性: A

| # | 方法 | 用途描述 | 结果 |
|---|------|----------|------|
| 1 | SetPrimaryDisplay | 设置主显示文本 | PASS |
| 2 | SetIsInError | 设置错误状态 | PASS |
| 3 | SetExpressionDisplay | 设置表达式显示 | PASS |
| 4 | SetParenthesisNumber | 未闭合括号数量 | PASS |
| 5 | OnNoRightParenAdded | 右括号未添加 | PASS |
| 6 | MaxDigitsReached | 达到最大位数 | PASS |
| 7 | BinaryOperatorReceived | 接收二元运算符 | PASS |
| 8 | OnHistoryItemAdded | 历史记录新增 | PASS |
| 9 | SetMemorizedNumbers | 设置记忆数字 | PASS |
| 10 | MemoryItemChanged | 记忆项变更 | PASS |
| 11 | InputChanged | 输入变更 | PASS |

### 完整性: A

覆盖率 **11/11 = 100%**。正确标注为观察者模式核心接口。

---

## TC-B06: 宏定义理解

**输入**: `解释 CalcViewModel 中 OBSERVABLE_PROPERTY_RW 宏的作用和用法`

### 正确性: A

| 检查项 | 结果 | 详情 |
|--------|------|------|
| 宏定义位置 | PASS | `src/GraphControl/Utils.h` 第 73-92 行 |
| getter 生成 | PASS | `t get() { return m_##n; }` |
| setter 生成（含值变化检查） | PASS | `if (m_##n != value) { m_##n = value; RaisePropertyChanged(L#n); }` |
| 私有成员生成 | PASS | `t m_##n;` |
| MVVM 模式识别 | PASS | 正确关联到数据绑定 |
| OBSERVABLE_OBJECT 依赖 | PASS | 正确指出需配合使用 |

### 准确性: A

宏展开细节与源码一致，C++/CX 语法正确，用法示例合理。相关宏（OBSERVABLE_PROPERTY_R、OBSERVABLE_NAMED_PROPERTY_RW、PROPERTY_RW）均真实存在。

### 完整性: A-

覆盖了宏的定义位置、展开结果、用法、依赖和相关宏。未提及 `##`/`#` 预处理运算符细节和宏内的 `public:/private:` 访问修饰符切换。

---

## 修复验证：P0-002 路径解析 Bug

### 修复内容
- `VSAgentContext.GetSolutionDirectory()`: 新增 `FindProjectRoot()` 向上查找项目根目录（.git 等标记）
- `PathResolver`: 新增 `SmartCombine()` 路径去重安全网
- `VSAgentContext.WriteFileAsync()`: 改用 PathResolver + SafetyGuard 验证

### 修复后重测结果

#### TC-A03 (修复后): write_file — 创建新文件

| 检查项 | 修复前 | 修复后 |
|--------|--------|--------|
| 文件路径 | FAIL (`src/src/CalcManager/`) | **PASS** (`src/CalcManager/`) |
| 文件内容 | PASS | PASS |
| 文件系统验证 | 路径错误 | **文件在正确位置** |

#### TC-A04 (修复后): edit — 编辑现有文件

| 检查项 | 修复前 | 修复后 |
|--------|--------|--------|
| 编辑路径 | FAIL (编辑了错误路径的文件) | **PASS** (编辑了正确路径的文件) |
| 文件内容 | PASS (`AICA test file - modified`) | PASS |
| Diff 预览 | PASS | PASS |

#### TC-A05 (修复后): list_dir — 列出目录结构

| 检查项 | 修复前 | 修复后 |
|--------|--------|--------|
| 目录内容 | FAIL (仅 1 个文件) | **PASS** (完整列出) |
| 子目录 | FAIL (0 个) | **PASS** (CEngine/、Header Files/、Ratpack/) |
| 文件数 | 1 个 | **约 48 个** (含子目录内容) |
| 子目录文件数验证 | - | CEngine=12, Header Files=12, Ratpack=15 全部正确 |

### 修复验证结论

**P0-002 已修复并验证通过。** 三个受影响的用例全部从 FAIL 恢复为 PASS。

---

## 问题分类汇总

### P0 — 严重问题

| 编号 | 用例 | 问题描述 | 状态 |
|------|------|----------|------|
| P0-001 | TC-A01 | attempt_completion 摘要严重不完整，仅覆盖 15-20% 文件内容 | **已修复 ✅** (覆盖率提升至 ~45-50%) |
| P0-002 | TC-A03/A04/A05 | 路径解析错误：workspace root 拼接导致路径重复 | **已修复 ✅** |
| P0-003 | TC-A13 | **工具调用幻觉**: LLM 未调用 read_file 就生成了文件内容，增加 3 次重试限制 | **已修复 ✅** |
| P0-004 | TC-A13 | **虚构不存在的内容**: CommandGroup 枚举虚构，修复后 LLM 正确调用工具不再虚构 | **已修复 ✅** |
| P0-005 | TC-A14 | **condense 后上下文丢失**: 压缩上下文后完全丢失对话意图，重放旧任务 | **已修复 ✅** (不再重放旧任务，降级为 P1-013) |
| P0-006 | TC-A14 | **ask_followup_question options bug**: options 数组序列化失败，两次触发空数组错误 | **已修复 ✅** (修复后正常弹出) |

### P1 — 重要问题

| 编号 | 用例 | 问题描述 | 状态 |
|------|------|----------|------|
| P1-001 | TC-A01 | 枚举值名称不精确（QWORD vs QWORD_WIDTH） | LLM 行为 |
| P1-002 | TC-A01 | 完全遗漏 #include 依赖、前向声明、friend 声明、接口引用等结构信息 | LLM 行为 |
| P1-003 | TC-A02 | 遗漏 3 个 TEST_METHOD（FunctionalCopyPasteTest 等） | LLM 行为 |
| P1-004 | TC-A02 | 完全遗漏 19 个私有 Helper 验证方法 | LLM 行为 |
| P1-005 | TC-A03 | ~~write_file 未触发确认对话框~~ 实际已弹出确认框，问题不存在 | **已关闭** |
| P1-006 | TC-A03/A04 | completion 摘要声称路径与实际操作路径不一致 | **已修复 ✅** |
| P1-008 | TC-A05 | 缺乏结果合理性校验：60 个文件的项目目录仅返回 1 个文件 | **已修复 ✅** |
| P1-009 | TC-A10 | 方法计数自相矛盾：正文说 48 个，completion 说 44 个，实际约 45 个 | LLM 行为 |
| P1-010 | TC-A10 | 遗漏成员变量和前向声明：15 个私有成员、2 个前向声明、4 个 #include 未提及 | LLM 行为 |
| P1-011 | TC-A13 | 枚举值命名系统性错误：所有命令缺少 `Command` 前缀（NUMBER_0 vs Command0） | **已修复 ✅** |
| P1-012 | TC-A14 | condense 后未回答原始问题，即使压缩出错也应继续处理用户请求 | 待改进 |
| P1-013 | TC-A14 | condense 摘要未保留文件操作历史，导致 LLM 无法回答"之前读取了哪些文件" | 待改进 |
| P1-014 | TC-B02 | 虚构不存在的设计模式：错误归类模板方法模式和单例模式 | LLM 行为 |
| P1-015 | TC-B02 | 无代码依据的功能声称：声称命令模式"支持撤销/重做"，代码中无此机制 | LLM 行为 |
| P1-016 | TC-C01 | 架构概览过于简略：completion 仅 3 行，缺少依赖图和技术栈细节 | LLM 行为 |

### P2 — 一般问题

| 编号 | 用例 | 问题描述 | 状态 |
|------|------|----------|------|
| P2-001 | TC-A01 | "读取文件"请求仅给出摘要而非完整内容展示 | LLM 行为 |
| P2-002 | TC-A02 | 未提及文件总行数（1562 行），无法验证分块能力 | LLM 行为 |
| P2-003 | TC-A11 | 用户要求执行 `dir` 命令，AICA 用 list_dir 替代但未告知用户 | LLM 行为 |
| P2-004 | TC-B02 | 遗漏 Factory 模式：CalculatorManager 管理 3 个 CCalcEngine 实例 | LLM 行为 |
| P2-005 | TC-C01 | 未主动补充探索：有工具可用但未调用 list_dir/list_projects 补充信息 | LLM 行为 |
| P2-006 | TC-C04 | 搜索范围不完整：用户要求"所有 .h 头文件"，AICA 仅搜索 CalcManager | LLM 行为 |

### 问题统计

| 级别 | 总数 | 已修复 | 待修复 | LLM 行为 | 待改进 |
|------|------|--------|--------|----------|--------|
| P0 | 6 | **6** | 0 | 0 | 0 |
| P1 | 15 | 3 | 0 | 9 | 2 |
| P2 | 6 | 0 | 0 | 6 | 0 |
| **合计** | **27** | **9** | **0** | **15** | **2** |

> **LLM 行为**: 属于 LLM 固有局限（如摘要遗漏、过度推断），需通过 prompt 优化或模型升级改善，无法通过代码修复。

---

## 趋势观察

### A 类: 工具正确性（修复后最终结果）

| 维度 | A01修 | A02 | A03修 | A04修 | A05修 | A06 | A07 | A08 | A09 | A10 | A11 | A12 | A13修 | A14修 |
|------|-------|-----|-------|-------|-------|-----|-----|-----|-----|-----|-----|-----|-------|-------|
| 正确性 | A | A- | A | A | A | **A** | **A** | **A** | **A** | A- | B+ | **A** | A- | C |
| 准确性 | A | A | A | A | A | **A** | **A** | **A** | **A** | B+ | **A** | **A** | B+ | C |
| 完整性 | B+ | B | A | A | A | **A** | **A** | **A** | **A** | B | **A** | **A** | B+ | D |

### B 类: 代码理解准确性

| 维度 | B01 | B02 | B03 | B04 | B05 | B06 |
|------|-----|-----|-----|-----|-----|-----|
| 正确性 | A- | B+ | **A** | **A** | A- | **A** |
| 准确性 | A- | B | **A** | **A** | B+ | **A** |
| 完整性 | B+ | B+ | A- | **A** | A- | A- |

### C 类: 代码分析完整性

| 维度 | C01 | C02 | C03 | C04 | C05 |
|------|-----|-----|-----|-----|-----|
| 正确性 | B+ | A- | **A** | B+ | **A** |
| 准确性 | B+ | **A** | **A** | A- | A- |
| 完整性 | C+ | A- | **A** | B | A- |

### 阶段性结论

**A 类测试（14 个用例）已全部完成：**
- **全 A 满分**: A03修/A04修/A05修/A06/A07/A08/A09/A12 — 8 个用例
- **PASS（A-/B+）**: A01修/A02/A10/A11/A13修 — 5 个用例
- **PARTIAL**: A14修 — 1 个用例（condense 摘要质量待改进）
- **6 个 P0 bug 全部已修复 ✅**

**B 类测试（6/6 个用例已全部完成）：**
- **全 A 满分**: B03/B04/B06 — 3 个用例
- **PASS**: B01/B05 — 2 个用例
- **PARTIAL PASS**: B02 — 1 个用例（错误归类了模板方法模式和单例模式）
- B 类核心能力：继承关系分析(B01)、依赖分析(B03)、命名空间(B04)、宏理解(B06) 表现优异；设计模式识别(B02) 存在 LLM 过度推断

**C 类测试（5/5 个用例已全部完成）：**
- **全 A 满分**: C03 — 1 个用例
- **PASS**: C02/C05 — 2 个用例
- **PARTIAL PASS**: C01/C04 — 2 个用例
- C 类核心能力：接口方法提取(C03) 满分；测试发现(C02)、多文件分析(C05) 优秀；项目概览(C01) 和搜索范围(C04) 略有不足

**综合统计：**

| 指标 | 值 |
|------|-----|
| 总测试用例 | 25 (A:14 + B:6 + C:5) |
| PASS | 19 (76%) |
| PARTIAL PASS | 5 (20%) |
| FAIL（修复前） | 3 → 修复后全部提升 |
| P0 bug 发现 | 6 个，**全部已修复** |
| P1 问题 | 15 个（3 已修复，9 LLM 行为，1 待修复，2 待改进） |
| P2 问题 | 6 个（全部 LLM 行为） |

**关键发现：**
- **搜索类工具表现最优**: find_by_name、grep_search、list_projects 全 A
- **路径相关工具已修复**: write_file、edit、list_dir 修复后全 A
- **代码分析能力强**: 继承关系、依赖分析、接口提取、宏理解均表现优秀
- **LLM 总结环节是主要短板**: 摘要覆盖率不足、计数不一致、设计模式过度推断
- **LLM 行为问题占多数 (15/27)**: 属于 LLM 固有局限，需通过 prompt 优化或模型升级改善
