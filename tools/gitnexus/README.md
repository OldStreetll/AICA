# GitNexus — AICA 内嵌版

AICA 通过 MCP 协议调用 GitNexus 提供 AST 级别的代码理解能力（符号查询、影响分析、图谱搜索等）。

## 首次设置

**前置条件：** Node.js >= 18（运行 `node --version` 确认）

```powershell
cd tools\gitnexus
npm install --omit=dev
```

完成后验证：

```powershell
node dist/cli/index.js mcp
# 看到 "MCP server starting with N repo(s)" 即成功，Ctrl+C 退出
```

## 索引项目

AICA 在 VS2022 中打开解决方案时会自动触发索引。也可以手动索引：

```powershell
node dist/cli/index.js analyze <项目路径>
# 例如：node dist/cli/index.js analyze D:\Project\MyProject
```

首次索引耗时取决于项目大小（3000 文件约 5 分钟），后续增量索引很快。

## 配置

| 环境变量 | 说明 | 默认值 |
|----------|------|--------|
| `AICA_GITNEXUS_PATH` | 自定义 GitNexus 入口路径（覆盖内嵌版本） | 无 |
| `GITNEXUS_MAX_FILE_SIZE` | 单文件索引大小上限（字节） | 2097152 (2MB) |

## 工作原理

AICA 的 `GitNexusProcessManager` 按以下优先级查找 GitNexus：

1. 本目录下的 `dist/cli/index.js`（内嵌版本）
2. `AICA_GITNEXUS_PATH` 环境变量指定的路径
3. `npx -y gitnexus@latest`（npm 在线版本，兜底）

找到后通过 `node <path> mcp` 启动 MCP 服务，AICA 通过 stdin/stdout 与之通信。

## 升级 GitNexus

```powershell
# 在 GitNexus 源码目录（非本目录）
cd D:\Project\AIConsProject\GitNexus\gitnexus
git pull
npx tsc

# 复制构建产物到本目录
copy dist\* D:\Project\AIConsProject\AICA\tools\gitnexus\dist\ /E /Y
copy package.json D:\Project\AIConsProject\AICA\tools\gitnexus\
# 然后重新 npm install --omit=dev
```

## 桥接的 6 个工具

| 工具 | 用途 |
|------|------|
| `gitnexus_context` | 360° 符号视图：调用者、被调用者、执行流 |
| `gitnexus_impact` | 爆炸半径分析：修改某符号会影响什么 |
| `gitnexus_query` | 混合搜索（BM25 + 语义 + RRF） |
| `gitnexus_detect_changes` | git diff → 受影响执行流映射 |
| `gitnexus_rename` | 多文件协调重命名（需用户确认） |
| `gitnexus_cypher` | 原生 Cypher 图谱查询 |

## 故障排查

- **AICA 中 GitNexus 工具不可用：** 打开 DebugView 过滤 `[AICA] GitNexus`，检查日志
- **索引卡住：** 大项目首次索引可能需要 5-10 分钟，耐心等待
- **"bundled version not found" 日志：** `npm install --omit=dev` 未执行，或 `node_modules` 被清除
- **文件被跳过：** 超过 2MB 的文件会被跳过（可通过 `GITNEXUS_MAX_FILE_SIZE` 调大）
