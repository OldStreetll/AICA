# AICA v2.6+ 开发计划（v1.1 APPROVED）

> 版本: v1.1 | 日期: 2026-04-01
> 基线: v2.5 (commit fab9edb)
> 评审: planner + architect reviewer 双向评审，两轮达成一致
> 状态: **APPROVED**

---

## 执行顺序与阶段

```
Phase 1: 待办3 (测试修复) v2.5.1 ─── 无依赖 (~200行)
              │
              ▼
Phase 2: 待办4 (C++ Rules) v2.5.2 ─── 需要原文档 (~150行)
              │
              ▼
Phase 3: 待办5 (消息Part化) v2.6.0 ─── 影响面广 (~520行)
              │
              │ [显式依赖: SQLite messages 表设计依赖 Parts 数据模型]
              ▼
Phase 4: 待办2 (SQLite持久化) v2.7.0 ─── NuGet依赖 (~972行)
              │
              ▼
Phase 5: 待办1 (Tree-sitter) v2.8.0 ─── 最高风险 (~1020行)
```

总工作量: ~2862 行, 10 新文件

---

## Phase 1 (v2.5.1): 修复 7 个 pre-existing 测试

- 工作量: ~200 行
- NuGet: 无
- 风险: LOW
- 验收: `dotnet test` 全部通过（0 failures），无测试被 skip 或 delete

---

## Phase 2 (v2.5.2): C++ Rules 审查

- 工作量: ~150 行
- NuGet: 无
- 风险: LOW
- 前置条件: 需要用户提供 Q/HNC 43 原文档
- 验收: CppRuleTemplates.cs 内容与原文一致，.aica-rules/ 同步

---

## Phase 3 (v2.6.0): 消息 Part 化

### 核心设计: 双通道向后兼容

```csharp
public class ChatMessage
{
    private string _content;
    private List<IContentPart> _parts;

    public string Content
    {
        get => (_parts == null || _parts.Count == 0)
            ? _content
            : string.Join("", _parts.OfType<TextPart>().Select(p => p.Text));
        set { _content = value; _parts = null; }
    }

    public List<IContentPart> Parts
    {
        get => _parts;
        set { _parts = value; _content = null; }
    }

    public bool HasMultimodalParts =>
        _parts != null && _parts.Any(p => !(p is TextPart));
}
```

### 改动范围

| 文件 | 类型 | 行数 |
|------|------|------|
| `LLM/ContentParts.cs` | 新建 | ~80 |
| `LLM/ChatMessage.cs` | 重写 | ~60 |
| `LLM/OpenAIClient.cs` | 修改 — Parts 序列化 | ~50 |
| `SK/Adapters/ChatMessageConverter.cs` | 修改 — ImagePart 支持 | ~35 |
| `Agent/ConversationCompactor.cs` | 修改 — Parts 感知 condense | ~20 |
| `Context/ContextManager.cs` | 修改 — EstimateTokens 支持 Parts | ~25 |
| `Storage/ConversationStorage.cs` | 修改 — 序列化 Parts | ~30 |
| 11 个文件 | 审查确认兼容 | ~20 |
| 测试 (3个) | 新建/修改 | ~200 |
| **合计** | | **~520** |

- NuGet: 无
- 风险: MEDIUM（影响面广，101 处 .Content 引用）
- 验收: Content getter 向后兼容, OpenAIClient 正确序列化, 全量测试通过

---

## Phase 4 (v2.7.0): SQLite 持久化

### 技术方案

- NuGet: `Microsoft.Data.Sqlite` **3.1.36** (LTS)
- 备选: 直接 P/Invoke sqlite3.dll
- DB 路径: `~/.AICA/aica.db`
- 卸载策略: 保留用户数据

### 表结构

```sql
conversations (id, title, project_path, project_name, solution_path,
              working_dir, created_at, updated_at, context_summary, summary_up_to_index)

messages (id, conversation_id, role, content, parts_json,
         tool_name, tool_logs_html, completion_data, timestamp)

plan_history (id, conversation_id, html_content, seq_order)
```

### 改动范围

| 文件 | 类型 | 行数 |
|------|------|------|
| `Storage/SqliteConversationStorage.cs` | 新建 | ~420 |
| `Storage/DatabaseMigrator.cs` | 新建 | ~120 |
| `Storage/JsonToSqliteMigrator.cs` | 新建 | ~100 |
| `Storage/TitleGenerator.cs` | 新建 | ~60 |
| VSIX 切换存储实例 | 修改 | ~22 |
| 测试 (2个) | 新建 | ~250 |
| **合计** | | **~972** |

- 风险: MEDIUM（native DLL 加载 + JSON 迁移）
- 验收: 新建/恢复会话, JSON 自动迁移, 会话标题生成

---

## Phase 5 (v2.8.0): Tree-sitter 代码解析

### 技术方案

- 预编译 tree-sitter + tree-sitter-c + tree-sitter-cpp → `tree-sitter-native.dll` (x64)
- kernel32 LoadLibrary/GetProcAddress polyfill (NativeLoader.cs)
- ISymbolParser 接口 + regex fallback
- 仅支持 Windows x64

### 改动范围

| 文件 | 类型 | 行数 |
|------|------|------|
| `Knowledge/ISymbolParser.cs` | 新建 | ~30 |
| `Knowledge/TreeSitter/NativeLoader.cs` | 新建 | ~60 |
| `Knowledge/TreeSitter/TreeSitterInterop.cs` | 新建 | ~170 |
| `Knowledge/TreeSitter/TreeSitterParser.cs` | 新建 | ~300 |
| `Knowledge/TreeSitter/TreeSitterLanguage.cs` | 新建 | ~80 |
| SymbolParser/ProjectIndexer/KnowledgeStore | 修改 | ~70 |
| VSIX 打包 native DLL | 修改 | ~10 |
| 测试 (3个) | 新建 | ~300 |
| **合计** | | **~1020** |

- NuGet: 无（P/Invoke）
- 风险: HIGH（native DLL 编译+分发）
- 验收: C/C++ 解析正确率 >95%, regex fallback 正常, 索引速度不退化

---

## 关键约束

| 约束 | Phase 3 | Phase 4 | Phase 5 |
|------|---------|---------|---------|
| .NET Standard 2.0 | OK | OK (3.1.x) | OK (P/Invoke) |
| NuGet 依赖 | 0 | 1 | 0 |
| MiniMax 兼容 | OK (向后兼容序列化) | N/A | N/A |
| Windows x64 | N/A | N/A | 显式约束 |

---

## 风险与缓解

| 风险 | Phase | 缓解 |
|------|-------|------|
| native DLL 分发 | 4, 5 | 参考 ripgrep 打包; P/Invoke 备选 |
| Part 化破坏兼容 | 3 | Content getter 保持 string; 全量测试 |
| JSON→SQLite 迁移 | 4 | 幂等+可重试; 失败 fallback JSON |
| tree-sitter 编译 | 5 | 预编译 DLL 检入; regex fallback |
| C++ Rules 原文不可获取 | 2 | 基于 CppRuleTemplates 做增量审查 |
