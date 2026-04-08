# Bug 修复计划：EditFileTool 中文编码乱码

> **版本**: v3.0 — 二次确认异议修复版
> **日期**: 2026-04-08
> **状态**: 待三次确认
> **触发**: E2E 测试发现编辑文件后中文注释乱码

---

## 一、问题根因

### 破坏流程

1. 文件以 GBK/GB2312 编码存储（中国 C/C++ 项目常见，尤其涉密环境）
2. `VSAgentContext.ReadFileAsync` 用 `new StreamReader(path)` 读取 → 默认 UTF-8
3. GBK 字节被错误解码为乱码
4. Agent 在乱码文本上做编辑
5. `VSAgentContext.WriteFileAsync` 用 `new StreamWriter(path, false)` 写回 → 默认 UTF-8
6. 原始 GBK 编码被覆盖，中文注释永久损坏

---

## 二、收敛结论（5 Pane 共识）

| 讨论点 | 结论 |
|--------|------|
| 1. Encoding fallback | 使用 `Encoding.Default`（.NET Fx 4.8 = 系统 ANSI），加注释说明 .NET 6+ 迁移风险 |
| 2. 纯 ASCII | 无需特殊处理（ASCII 是 UTF-8 子集） |
| 3. 同步 I/O | 保持 `File.ReadAllBytes` 同步，不包装 Task.Run |
| 4. 缓存策略 | 每次 ReadFileAsync 都重新检测并更新缓存 |
| 5. 新文件默认 | UTF-8（无 BOM） |
| 6. BOM 保留 | 检测时记录是否有 BOM，写回时保持原样 |

---

## 三、修复方案（收敛版）

### 新建：`src/AICA.Core/Utils/EncodingDetector.cs`

```csharp
using System;
using System.IO;
using System.Text;

namespace AICA.Core.Utils
{
    /// <summary>
    /// Detects file encoding and preserves it across read/write cycles.
    /// Strategy: BOM detection → UTF-8 strict decode → system ANSI fallback.
    /// </summary>
    public static class EncodingDetector
    {
        /// <summary>
        /// Detection result containing encoding info and whether BOM was present.
        /// </summary>
        public class EncodingInfo
        {
            public Encoding Encoding { get; set; }
            public bool HasBom { get; set; }

            /// <summary>
            /// Returns the encoding configured for write-back (with/without BOM).
            /// </summary>
            public Encoding GetWriteEncoding()
            {
                // UTF-8: respect original BOM presence
                if (Encoding.CodePage == 65001)
                    return new UTF8Encoding(HasBom);

                // Other encodings: return as-is
                return Encoding;
            }
        }

        /// <summary>
        /// Detect file encoding from raw bytes.
        /// 1. Check BOM (UTF-8/UTF-16 LE/BE)
        /// 2. No BOM → try UTF-8 strict decode
        /// 3. UTF-8 fails → system default ANSI (GBK/CP936 on Chinese Windows)
        ///
        /// NOTE: Encoding.Default returns system ANSI codepage on .NET Framework 4.8.
        /// On .NET 6+ it returns UTF-8. If migrating, change fallback to
        /// Encoding.GetEncoding(936) or register CodePagesEncodingProvider.
        /// </summary>
        public static EncodingInfo DetectEncoding(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = false };

            // 1. BOM detection
            if (rawBytes.Length >= 3
                && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = true };

            if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
                return new EncodingInfo { Encoding = Encoding.Unicode, HasBom = true };

            if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
                return new EncodingInfo { Encoding = Encoding.BigEndianUnicode, HasBom = true };

            // 2. Try UTF-8 strict decode
            try
            {
                var utf8Strict = new UTF8Encoding(false, true);
                utf8Strict.GetString(rawBytes);
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = false };
            }
            catch (DecoderFallbackException)
            {
                // ignored — not valid UTF-8
            }

            // 3. Fallback to system default ANSI (GBK on Chinese Windows)
            return new EncodingInfo { Encoding = Encoding.Default, HasBom = false };
        }

        /// <summary>
        /// Read file with encoding detection. Returns content and encoding info.
        /// </summary>
        public static void ReadWithEncoding(string filePath, out string content, out EncodingInfo encodingInfo)
        {
            var rawBytes = File.ReadAllBytes(filePath);
            encodingInfo = DetectEncoding(rawBytes);

            // Skip BOM bytes when decoding
            var preamble = encodingInfo.Encoding.GetPreamble();
            int offset = 0;
            if (encodingInfo.HasBom && preamble.Length > 0 && rawBytes.Length >= preamble.Length)
            {
                bool match = true;
                for (int i = 0; i < preamble.Length; i++)
                {
                    if (rawBytes[i] != preamble[i]) { match = false; break; }
                }
                if (match) offset = preamble.Length;
            }

            content = encodingInfo.Encoding.GetString(rawBytes, offset, rawBytes.Length - offset);
        }
    }
}
```

**设计说明**：
- 使用 `EncodingInfo` 类代替 ValueTuple（避免 System.ValueTuple 依赖问题）
- 使用 `out` 参数代替元组返回（.NET Framework 4.8 兼容）
- `GetWriteEncoding()` 封装 BOM 保留逻辑：UTF-8 文件根据原始 BOM 状态决定写回时是否带 BOM
- 非 UTF-8 编码（GBK 等）无 BOM 概念，直接返回原编码
- **v3.0 修正：类访问级别改为 `public`**（EncodingDetector 在 AICA.Core 中定义，被 AICA.VSIX 引用，internal 会导致编译失败）

### 修改：`src/AICA.VSIX/Agent/VSAgentContext.cs`

**新增字段**：
```csharp
private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AICA.Core.Utils.EncodingDetector.EncodingInfo>
    _fileEncodings = new System.Collections.Concurrent.ConcurrentDictionary<string, AICA.Core.Utils.EncodingDetector.EncodingInfo>(
        StringComparer.OrdinalIgnoreCase);
```

**修改 ReadFileAsync**：
```csharp
// 旧代码
using (var reader = new StreamReader(fullPath))
{
    return await reader.ReadToEndAsync();
}

// 新代码
string content;
AICA.Core.Utils.EncodingDetector.EncodingInfo encodingInfo;
AICA.Core.Utils.EncodingDetector.ReadWithEncoding(fullPath, out content, out encodingInfo);
_fileEncodings[fullPath] = encodingInfo;
System.Diagnostics.Debug.WriteLine(
    string.Format("[AICA] ReadFile encoding: {0}, BOM: {1}, file: {2}",
        encodingInfo.Encoding.EncodingName, encodingInfo.HasBom, fullPath));
return content;
```

**修改 WriteFileAsync**：
```csharp
// 旧代码
using (var writer = new StreamWriter(fullPath, false))
{
    await writer.WriteAsync(content);
}

// 新代码
AICA.Core.Utils.EncodingDetector.EncodingInfo encodingInfo;
if (!_fileEncodings.TryGetValue(fullPath, out encodingInfo))
    encodingInfo = new AICA.Core.Utils.EncodingDetector.EncodingInfo
    {
        Encoding = System.Text.Encoding.UTF8,
        HasBom = false
    };

var writeEncoding = encodingInfo.GetWriteEncoding();
using (var writer = new StreamWriter(fullPath, false, writeEncoding))
{
    await writer.WriteAsync(content);
}
System.Diagnostics.Debug.WriteLine(
    string.Format("[AICA] WriteFile encoding: {0}, BOM: {1}, file: {2}",
        writeEncoding.EncodingName, encodingInfo.HasBom, fullPath));
```

**v3.0 新增：修改 ShowDiffAndApplyAsync**（Critical 遗漏修复）

EditFileTool 和 WriteFileTool 的实际写入路径是 `ShowDiffAndApplyAsync`，不是 `WriteFileAsync`。该方法中有 3 处 `File.WriteAllText` 和 1 处 `File.ReadAllText` 需要修复：

```csharp
// line 646: 写临时文件（供 VS diff 展示）
// 旧: File.WriteAllText(tempFile, newContent);
// 新: 用原文件编码写入
AICA.Core.Utils.EncodingDetector.EncodingInfo encInfo;
_fileEncodings.TryGetValue(fullPath, out encInfo);
var writeEnc = encInfo != null ? encInfo.GetWriteEncoding() : System.Text.Encoding.UTF8;
File.WriteAllText(tempFile, newContent, writeEnc);

// line 649: 写备份文件
// 旧: File.WriteAllText(backupFile, originalContent);
// 新:
File.WriteAllText(backupFile, originalContent, writeEnc);

// line 699: 从临时文件读回最终内容
// 旧: var finalContent = File.ReadAllText(tempFile);
// 新:
var finalContent = File.ReadAllText(tempFile, writeEnc);

// line 702: 写入目标文件
// 旧: File.WriteAllText(fullPath, finalContent);
// 新:
File.WriteAllText(fullPath, finalContent, writeEnc);
```

---

## 四、文件修改矩阵

| 文件 | Pane | 操作 |
|------|------|------|
| `src/AICA.Core/Utils/EncodingDetector.cs` | Pane 1 | 新建 |
| `src/AICA.VSIX/Agent/VSAgentContext.cs` | Pane 2 | 修改 ReadFileAsync + WriteFileAsync + ShowDiffAndApplyAsync + 新增 _fileEncodings |

**冲突**: 无。

## 五、验收标准

- [ ] GBK 编码文件读取后中文正常显示
- [ ] 编辑 GBK 文件后写回保持 GBK 编码，中文不乱码
- [ ] UTF-8 with BOM 文件写回后保留 BOM
- [ ] UTF-8 without BOM 文件写回后不添加 BOM
- [ ] 纯 ASCII 文件正常处理
- [ ] 新建文件默认 UTF-8（无 BOM）
- [ ] Debug 日志输出检测到的编码信息
- [ ] ShowDiffAndApplyAsync 的临时文件读写保持编码一致
- [ ] 编译通过，.NET Framework 4.8 兼容

---

## 六、v3.0 异议修复记录

| 异议 | 来源 | 修复 |
|------|------|------|
| ShowDiffAndApplyAsync 写入路径未覆盖 | Pane 2/4/5 | 新增 ShowDiffAndApplyAsync 4 处 File.Read/WriteAllText 的编码处理 |
| EncodingDetector 是 internal 跨项目不可见 | Pane 2/3 | internal → public |
