# Phase 3 (v2.6.0) 消息 Part 化 — 详细设计方案

> 版本: v1.1 (修订版) | 日期: 2026-04-01
> 评审: designer + architect reviewer 双向评审，两轮达成一致
> 状态: **APPROVED**

---

## 一、需求

### 功能 1: ImagePart — 图片上传
- 用户可粘贴截图给 AICA（报错界面、UI 布局、Debug 输出）
- 序列化为 OpenAI vision 格式
- MiniMax-M2.5 是多模态模型，现在就能用
- 图片限制: max 2048×2048 像素, max 2MB base64, 超限自动缩放+JPEG降级

### 功能 2: CodePart — 代码结构化（右键命令）
- 用户在 VS2022 编辑器选中代码 → 右键 "Send to AICA with Context"
- 自动捕获: 文件路径、起始行号、语言、项目名
- LLM 收到带元数据的结构化代码块

---

## 二、数据模型

### IContentPart + TextPart/ImagePart/CodePart

新建 `src/AICA.Core/LLM/ContentParts.cs` (~95行)

```csharp
public interface IContentPart { ContentPartType Type { get; } }
public enum ContentPartType { Text, Image, Code }

public class TextPart : IContentPart {
    public ContentPartType Type => ContentPartType.Text;
    public string Text { get; }
}

public class ImagePart : IContentPart {
    public const int MaxBase64Bytes = 2 * 1024 * 1024;
    public const int MaxDimension = 2048;
    public ContentPartType Type => ContentPartType.Image;
    public string Base64Data { get; }  // 构造时验证大小
    public string MediaType { get; }
    public string ToDataUrl() => $"data:{MediaType};base64,{Base64Data}";
}

public class CodePart : IContentPart {
    public ContentPartType Type => ContentPartType.Code;
    public string Code { get; }
    public string FilePath { get; }
    public int StartLine { get; }
    public string Language { get; }
    public string ProjectName { get; }
    public string ToStructuredText()  // 生成带元数据的 markdown 代码块
}
```

### ChatMessage 双通道

```csharp
public class ChatMessage {
    private string _content;
    private List<IContentPart> _parts;

    public string Content {
        get => (_parts == null || _parts.Count == 0)
            ? _content
            : ConcatTextAndCodeParts(_parts);
        set { _content = value; _parts = null; }
    }

    public List<IContentPart> Parts {
        get => _parts;
        set { _parts = value; _content = null; }
    }

    public bool HasMultimodalParts =>
        _parts != null && _parts.Any(p => !(p is TextPart));
}
```

向后兼容: 101处 `.Content` 引用无需修改。

---

## 三、OpenAIClient 序列化

### RequestMessage.Content: JsonElement

```csharp
// 纯文本 → string → JsonElement (SerializeToElement)
// 含 ImagePart → content array → JsonElement (SerializeToElement)
```

用 `Dictionary<string, object>` 构建 content array，避免 anonymous type 序列化问题。
项目 STJ 版本 8.0.5，`SerializeToElement()` 完全可用。

---

## 四、VSIX 改动

### 图片粘贴
- Ctrl+V → `Clipboard.ContainsImage()` → 缩放(>2048px) → PNG编码 → 超2MB转JPEG 85 → ImagePart
- 输入框追加 `[Image attached]` 占位符

### CodePart 右键命令
- 新建 `SendCodeToAicaCommand.cs` (~60行)
- VS 编辑器右键菜单 "Send to AICA with Context"
- DTE API: `ActiveDocument.FullName` + `Selection.TopLine` + `Language`
- 注入 `ChatToolWindowControl.AttachCodePart(codePart)`

---

## 五、适配层

| 模块 | 改动 |
|------|------|
| **ContextManager** | 新增 `EstimateMessageTokens(ChatMessage)`, ImagePart=765 tokens |
| **ConversationCompactor** | condense 前降级 ImagePart → `"[N image(s) omitted]"` |
| **ConversationStorage** | `ConversationMessageRecord.PartsJson` 字段 (null=向后兼容) |
| **SK ChatMessageConverter** | multimodal 分支: ImagePart → SK ImageContent |

---

## 六、改动清单

| # | 文件 | 类型 | 行数 |
|---|------|------|------|
| 1 | `LLM/ContentParts.cs` | 新建 | ~95 |
| 2 | `LLM/ChatMessage.cs` | 重写 | ~60 |
| 3 | `LLM/OpenAIClient.cs` | 修改 | ~55 |
| 4 | `SK/Adapters/ChatMessageConverter.cs` | 修改 | ~35 |
| 5 | `Agent/ConversationCompactor.cs` | 修改 | ~20 |
| 6 | `Context/ContextManager.cs` | 修改 | ~25 |
| 7 | `Storage/ConversationStorage.cs` | 修改 | ~30 |
| 8 | `VSIX/ChatToolWindowControl.xaml.cs` | 修改 | ~90 |
| 9 | `VSIX/ChatModels.cs` | 修改 | ~10 |
| 10 | `VSIX/Commands/SendCodeToAicaCommand.cs` | 新建 | ~60 |
| 11 | `VSIX/.vsct` | 修改 | ~10 |
| 12-14 | 测试 (3个) | 新建 | ~220 |
| **合计** | | | **~710** |

---

## 七、分阶段

- **Phase 3a**: Core 数据模型 + 序列化 (文件 1-7, 12-14)
  - 验收: 全量测试通过, Content getter 向后兼容
- **Phase 3b**: VSIX UI 集成 (文件 8-11)
  - 验收: 粘贴截图→LLM回答图片内容, 右键发送代码→LLM知道文件路径
  - 含 MiniMax vision API curl 验证

---

## 八、已知限制

- token 估算: ImagePart 固定 765 tokens，待 CalibrateFromUsage 校准
- ContextManager.EstimateTokens(m.Content) 不含 ImagePart token，Phase 4 迁移
- MiniMax vision API 格式需 Phase 3b 后验证
