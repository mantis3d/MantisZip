# 加密文件名 7z 魔数检测修复计划

> 解决 `EncryptHeaders=true` 的 7z 压缩包在无密码时无法通过魔数检测识别内部文件真实格式，导致预览回退到扩展名分类的问题。
> **状态**: 规划中 | **优先级**: P1

---

## 问题背景

### 现状

`PreviewService.ClassifyPreviewByMagicAsync` (line 201-255) 调用 `ArchiveEntryExtractor.ExtractHeadAsync` 读取文件头部用于魔数检测。

对于 `EncryptHeaders=true` 的 7z 压缩包：
- **无密码时**：`SharpSevenZipExtractor` 构造函数抛出异常（无法读取文件列表）
- **有密码时**：正常工作

当前代码在 `ExtractHeadAsync` (line 232-240) 中捕获异常，直接返回 `Unsupported`，导致魔数检测完全失败，回退到扩展名分类。

### 影响场景

| 场景 | 当前行为 | 期望行为 |
|------|----------|----------|
| 用户浏览加密文件名 7z，未输入密码 | 预览显示"不支持的格式"（实际可能是图片/文本/PDF 等） | 预览提示"需输入密码预览"，或显示锁图标 |
| 用户浏览加密文件名 7z，已有会话密码/密码库匹配 | 正常预览 | 正常预览（已工作） |
| 魔数检测用于格式识别（非预览） | 无法识别真实格式 | 标记"需密码识别" |

---

## 核心设计决策

### 方案对比

| 方案 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| **A: ExtractHeadAsync 抛出特定异常** | 调用方明确知道"需密码" | 需修改异常传播链 | ⭐⭐⭐ |
| **B: ExtractHeadAsync 返回 `Result<byte[], HeadExtractError>`** | 类型安全，显式错误 | 需引入 Result 类型，改动面广 | ⭐⭐ |
| **C: ClassifyPreviewByMagicAsync 传入密码（复用会话/密码库）** | 复用现有密码流程 | 可能触发密码对话框（预览不应弹窗） | ⭐ |
| **D: 标记 `PreviewType.NeedsPassword` + UI 显示锁图标** | 无需密码即可给用户明确反馈 | 魔数检测仍失败，但用户知情 | ⭐⭐⭐⭐ |

**推荐：方案 D + 方案 A 结合**

1. `ExtractHeadAsync` 对 `EncryptHeaders=true` 无密码时抛出 `PasswordRequiredException`（或返回特定错误码）
2. `ClassifyPreviewByMagicAsync` 捕获该异常，返回 `(PreviewType.NeedsPassword, FileFormat.Encrypted, "加密文件名")`
3. `PreviewViewModel.ShowPreviewAsync` 对 `NeedsPassword` 显示专用 UI（锁图标 + "输入密码预览"按钮）

---

## 改动范围

### 核心文件

| 文件 | 改动类型 | 说明 |
|------|----------|------|
| `MantisZip.Core/Utils/ArchiveEntryExtractor.cs` | 🔴 重 | `ExtractHeadAsync` / `ExtractHeadTailAsync` 新增异常处理 |
| `MantisZip.UI.Avalonia/Services/PreviewService.cs` | 🔴 重 | `ClassifyPreviewByMagicAsync` 处理新异常/错误码 |
| `MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | 🟡 中 | 新增 `PreviewType.NeedsPassword` 处理分支 |
| `MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | 🟡 中 | UI: 显示"需密码"状态 + 触发密码输入流程 |

### 枚举扩展

```csharp
// PreviewService.cs: PreviewType 枚举新增
public enum PreviewType
{
    // ... 现有值 ...
    NeedsPassword = 99,  // 特殊值：需密码才能识别格式
}
```

---

## 实施步骤

### Phase 1: Core 层 - ExtractHeadAsync 错误传播

**文件**: `ArchiveEntryExtractor.cs`

1. 定义 `HeadExtractError` 枚举或 `PasswordRequiredException`：
```csharp
public enum HeadExtractError
{
    None,
    PasswordRequired,      // EncryptHeaders=true 且无密码
    NotSupported,          // 格式不支持单文件提取
    EntryNotFound,
    IOError,
}
```

2. 修改 `ExtractHeadAsync` 签名（保持向后兼容，内部用 Tupe/out）：
```csharp
// 选项 A: 抛异常（简单）
if (format == ArchiveFormat.SevenZip && IsSevenZipEncryptHeaders(extractor) && string.IsNullOrEmpty(password))
    throw new PasswordRequiredException("EncryptHeaders=true 7z requires password to read entries");

// 选项 B: 返回 (byte[], HeadExtractError) - 需修改所有调用方
```

**建议选项 A**：抛自定义异常 `PasswordRequiredException : Exception`，仅在 `ClassifyPreviewByMagicAsync` 捕获，其他调用方（如预览提取）保持现有 try-catch 行为。

3. 新增 `IsSevenZipEncryptHeaders` 检测：
```csharp
private static bool IsSevenZipEncryptHeaders(SharpSevenZipExtractor extractor)
{
    try { return extractor.EncryptHeaders; } catch { return false; }
}
```

### Phase 2: PreviewService - 魔数检测处理

**文件**: `PreviewService.cs`

```csharp
public static async Task<(PreviewType type, FileFormat format, string? displayName)> ClassifyPreviewByMagicAsync(...)
{
    try
    {
        var head = await ArchiveEntryExtractor.ExtractHeadAsync(...);
        // 现有逻辑
    }
    catch (PasswordRequiredException)
    {
        return (PreviewType.NeedsPassword, FileFormat.Encrypted, LocalizationManager.T("Preview_EncryptedFilename"));
    }
    catch (Exception ex)
    {
        // 现有兜底
    }
}
```

### Phase 3: PreviewViewModel - UI 处理

**文件**: `PreviewViewModel.cs` / `MainWindowViewModel.cs` (ShowPreviewAsync)

在 `switch (previewType)` 中新增 case：

```csharp
case PreviewType.NeedsPassword:
    Preview.ShowNeedsPassword(entry.NameDisplay ?? entry.Name);
    StatusMessage = LocalizationManager.T("Preview_EntryNeedsPassword");
    break;
```

**PreviewPanel.axaml** 需新增显示模式：锁图标 + 文案"此文件在加密文件名压缩包中，请先输入密码" + "输入密码"按钮（复用 `ShowPasswordDialog` 回调）。

### Phase 4: 密码输入后刷新预览

用户点击"输入密码" → 调用 `ShowPasswordDialog` → 验证密码 → 重新触发 `ShowPreviewAsync`（此时 `_currentPassword` 已设置，魔数检测正常工作）。

---

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| `PasswordRequiredException` 泄漏到非预览调用方（如提取预览头部） | 仅在 `ClassifyPreviewByMagicAsync` 捕获；其他调用方按现有异常处理（提取时会走正常密码流程） |
| `IsSevenZipEncryptHeaders` 检测不准 | 7z.dll 的 `EncryptHeaders` 属性在构造 `SharpSevenZipExtractor` 时即可读取（无需密码） |
| 预览面板新增状态增加复杂度 | 复用现有 `ShowUnsupported` / `ShowLoading` 模式，仅换文案和图标 |

---

## 验收标准

1. ✅ `7z a -mhe=on -p"pwd" test.7z image.png` - 无密码浏览时，预览区显示"需输入密码预览" + 锁图标
2. ✅ 点击"输入密码" → 正确密码 → 预览正常显示图片
3. ✅ 错误密码 → 提示错误 → 可重试
4. ✅ 已有会话密码/密码库匹配 → 无需交互直接预览（现有逻辑不变）
5. ✅ 非加密文件名 7z / ZIP / RAR 预览不受影响

---

## 相关文档

- [密码流程统一](password-flow-unification.md) - 统一的密码解析入口，本计划可复用其 `ResolvePasswordAsync`
- [预览魔数检测](preview-magic-detection.md) - 魔数检测架构文档