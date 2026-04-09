# Bug 修复计划：npm install 超时时间过短

> **版本**: v2.0 final
> **日期**: 2026-04-09
> **状态**: 短期方案已实施，长期方案搁置（node_modules 1.1GB 不可行）

---

## 一、问题描述

AICA 初始化时，GitNexus 的 npm install 超时时间不足，在网络较慢或涉密环境下容易超时失败。

## 二、当前代码

**文件**: `src/AICA.Core/Agent/GitNexusProcessManager.cs`

```csharp
// line 89: npm install 超时硬编码 120 秒
bool exited = npmProc.WaitForExit(120000);

// line 28: GitNexus 启动超时可配置，默认 15 秒
private static int StartTimeoutMs => Config.AicaConfig.Current.Tools.GitNexusStartTimeoutMs;
```

**对比**：
- npm install 超时：**120 秒硬编码**，不可配置
- GitNexus 启动超时：**15 秒，可通过 config.json 配置**

## 三、讨论点

1. **npm install 超时应该设多长？**
   - 当前 120 秒在正常网络下够用，但涉密/离线/慢网络环境可能不够
   - 设太长会导致失败时用户等待过久
   - 建议值？180s？300s？还是可配置？

2. **是否应该像 GitNexusStartTimeoutMs 一样做成可配置项？**
   - 方案 A：在 `ToolConfig` 中新增 `NpmInstallTimeoutMs`（默认 300000），通过 config.json 可调
   - 方案 B：直接改大硬编码值（如 300 秒），简单直接
   - 方案 C：不设超时（WaitForExit() 无参数），等 npm 自行完成

3. **超时后的行为是否合理？**
   - 当前：超时后 Kill 进程，静默继续（node_modules 可能不完整）
   - 是否需要重试？或提示用户手动运行？

4. **npm install 的可见性**
   - 当前 `CreateNoWindow = false`，用户可看到控制台
   - 是否需要在 AICA UI 中也显示进度/状态？

5. **离线环境考虑**
   - 涉密环境无外网，npm install 必然失败
   - 是否应该支持预打包 node_modules（VSIX 中直接包含）？
   - 或者检测无网络时跳过 npm install？
