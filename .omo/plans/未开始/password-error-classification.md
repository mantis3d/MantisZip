# 密码错误 vs 文件损坏 精准分类计划

> 解决当前仅靠字符串匹配无法区分"密码错误"和"文件损坏"，导致用户输入正确密码但文件损坏时误报"密码错误"的问题。
> **状态**: 规划中 | **优先级**: P1

---

## 问题背景

### 现状

`PasswordService.IsPasswordError` (line 123-128) 和 `IsPasswordOrCorruptedDataError` (line 133-140) 仅做字符串匹配：

```csharp
// 当前实现 - 问题：无法区分
public static bool IsPasswordError(Exception ex)
    => ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("encrypted", ...)
        || ex.Message.Contains("decrypt", ...)
        || ex.Message.Contains("encryption", ...);

public static bool IsPasswordOrCorruptedDataError(Exception ex, bool hasEncrypted)
{
    if (IsPasswordError(ex)) return true;
    if (!hasEncrypted) return false;
    // 关键问题：7z.dll "data error" 可能是密码错，也可能是文件损坏
    return ex.Message.Contains("data error", ...) || ex.Message.Contains("corrupted", ...);
}
```

### 实际场景差异

| 底层库 | 密码错误异常 | 文件损坏异常 | 可区分度 |
|--------|-------------|-------------|----------|
| **SharpCompress (ZIP)** | `CryptographicException` / `InvalidDataException` | `InvalidDataException` / `IOException` | ⭐⭐⭐ 可靠（异常类型不同） |
| **SharpSevenZip (7z/RAR)** | `SharpSevenZipArchiveException` (message "Wrong password") | `SharpSevenZipArchiveException` (message "Data error" / "CRC error") | ⭐⭐ 需解析 Message / HRESULT |
| **7z.dll 底层** | HRESULT `0x80090005` (NTE_BAD_DATA) | HRESULT `0x80004005` (E_FAIL) / CRC 错误 | ⭐⭐⭐⭐ 最准确 |

### 影响路径

1. **`TryMatchPassword` (line 72-94)** - 自动匹配密码时，文件损坏会被误判为"密码不匹配"，继续尝试下一个密码，浪费 100 次尝试上限
2. **`QuickVerifyPassword` (line 22-66)** - 密码验证失败无法区分原因
3. **用户手动输入密码** - 输入正确密码但文件损坏 → 提示"密码错误" → 用户反复尝试
4. **`ExtractSettingsViewModel.ValidateAllAsync`** - 逐包校验时误报

---

## 核心设计决策

### 方案对比

| 方案 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| **A: 引入 `PasswordVerificationResult` 枚举** | 类型安全，显式三态 | 需修改所有调用点 | ⭐⭐⭐⭐⭐ |
| **B: 扩展 `IsPasswordError` 返回 `PasswordErrorKind`** | 改动面小 | 仍依赖字符串解析 | ⭐⭐ |
| **C: 底层引擎抛出分类异常** | 源头分类 | 改动 Core 层接口，影响面大 | ⭐⭐⭐ |

**推荐：方案 A** - 引入 `PasswordVerificationResult`，在 `PasswordService` 内部封装异常分类逻辑，对外暴露明确的三态结果。

---

## 目标设计

### 新增类型

```csharp
// PasswordService.cs 或新文件 Core/Abstractions/PasswordVerificationResult.cs

/// <summary>
/// 密码验证/匹配结果的精确分类
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>验证通过，密码正确</summary>
    Success,

    /// <summary>密码错误（或无密码但需要密码）</summary>
    WrongPassword,

    /// <summary>文件损坏/非加密格式/其他不可恢复错误——非密码问题</summary>
    CorruptedOrInvalid,

    /// <summary>无法确定（异常类型未知，建议重试或用户决定）</summary>
    Unknown,
}

/// <summary>
/// 密码验证详细结果
/// </summary>
public readonly record struct PasswordVerifyInfo(
    PasswordVerificationResult Result,
    string? DetailMessage = null,      // 给用户看的详细说明
    Exception? OriginalException = null // 原始异常（调试用）
);
```

### 核心方法重构

```csharp
public class PasswordService
{
    /// <summary>
    /// 快速验证密码，返回精确分类结果
    /// </summary>
    public PasswordVerifyInfo QuickVerifyPasswordEx(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            bool ok = QuickVerifyPassword(archivePath, password, engine); // 复用现有逻辑
            return ok ? PasswordVerifyInfo.Success() : PasswordVerifyInfo.WrongPassword();
        }
        catch (Exception ex)
        {
            return ClassifyException(ex, engine, hasEncrypted: true);
        }
    }

    /// <summary>
    /// 从已保存密码匹配并验证，返回精确结果
    /// </summary>
    public (string Password, string Description, PasswordVerifyInfo VerifyInfo)? TryMatchPasswordEx(string archivePath, IArchiveEngine engine)
    {
        const int maxAttempts = 100;
        var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        var candidatePasswords = allMatches.Count > maxAttempts 
            ? allMatches.Take(maxAttempts).ToList() 
            : allMatches.ToList();
        var tried = new HashSet<string>();

        foreach (var entry in candidatePasswords)
        {
            var pwd = entry.Password;
            if (!tried.Add(pwd)) continue;

            var verifyInfo = QuickVerifyPasswordEx(archivePath, pwd, engine);
            if (verifyInfo.Result == PasswordVerificationResult.Success)
                return (pwd, entry.Description ?? pwd, verifyInfo);

            // 关键优化：如果是文件损坏，直接返回，不再尝试其他密码
            if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                return (null, null, verifyInfo); // 标记损坏，上层直接报错
        }
        return null;
    }

    /// <summary>
    /// 异常分类核心逻辑
    /// </summary>
    private static PasswordVerifyInfo ClassifyException(Exception ex, IArchiveEngine engine, bool hasEncrypted)
    {
        // 1. SharpCompress (ZIP)
        if (engine is ZipEngine)
            return ClassifySharpCompressException(ex);

        // 2. SharpSevenZip (7z/RAR/ISO)
        if (engine is SevenZipEngine)
            return ClassifySharpSevenZipException(ex);

        // 3. 通用兜底
        return ClassifyGenericException(ex, hasEncrypted);
    }

    private static PasswordVerifyInfo ClassifySharpCompressException(Exception ex)
    {
        return ex switch
        {
            CryptographicException => PasswordVerifyInfo.WrongPassword("密码错误"),
            InvalidDataException ide when ide.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                => PasswordVerifyInfo.WrongPassword("密码错误"),
            InvalidDataException => PasswordVerifyInfo.CorruptedOrInvalid("文件损坏或格式无效"),
            IOException => PasswordVerifyInfo.CorruptedOrInvalid("读取文件失败"),
            _ => PasswordVerifyInfo.Unknown(ex.Message)
        };
    }

    private static PasswordVerifyInfo ClassifySharpSevenZipException(Exception ex)
    {
        // SharpSevenZipArchiveException 包含 HRESULT 和 Message
        if (ex is SharpSevenZipArchiveException szEx)
        {
            // HRESULT 分类（最准确）
            uint hresult = (uint)szEx.HResult;
            if (hresult == 0x80090005) // NTE_BAD_DATA - 密码错误
                return PasswordVerifyInfo.WrongPassword("密码错误");
            if (hresult == 0x80004005) // E_FAIL - 通用失败，结合 Message 判断
            {
                var msg = szEx.Message.ToLowerInvariant();
                if (msg.Contains("wrong password") || msg.Contains("password"))
                    return PasswordVerifyInfo.WrongPassword("密码错误");
                if (msg.Contains("data error") || msg.Contains("crc") || msg.Contains("corrupt"))
                    return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏或数据错误");
            }
        }

        // 兜底：Message 解析
        var message = ex.Message.ToLowerInvariant();
        if (message.Contains("wrong password") || message.Contains("password"))
            return PasswordVerifyInfo.WrongPassword("密码错误");
        if (message.Contains("data error") || message.Contains("crc error") || message.Contains("corrupt"))
            return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏");

        return PasswordVerifyInfo.Unknown(ex.Message);
    }

    private static PasswordVerifyInfo ClassifyGenericException(Exception ex, bool hasEncrypted)
    {
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("password") || msg.Contains("encrypted") || msg.Contains("decrypt"))
            return PasswordVerifyInfo.WrongPassword("密码错误");
        if (hasEncrypted && (msg.Contains("data error") || msg.Contains("corrupt")))
            return PasswordVerifyInfo.CorruptedOrInvalid("文件损坏");
        return PasswordVerifyInfo.Unknown(ex.Message);
    }
}
```

---

## 改动范围

### 核心文件

| 文件 | 改动类型 | 说明 |
|------|----------|------|
| `MantisZip.UI.Avalonia/Services/PasswordService.cs` | 🔴 重 | 新增 `PasswordVerificationResult`、`PasswordVerifyInfo`、`QuickVerifyPasswordEx`、`TryMatchPasswordEx`、`ClassifyException` 等 |
| `MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 🟡 中 | `LoadArchiveAsync` 密码解析流程：使用 `TryMatchPasswordEx`，根据 `VerifyInfo.Result` 分流 |
| `MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs` | 🟡 中 | `ValidateAllAsync`：使用 `QuickVerifyPasswordEx`，损坏文件标记为 `SourceArchiveStatus.Corrupted` |
| `MantisZip.UI/AppPartials/App.Password.cs` (WPF) | 🟡 中 | 同步修复 WPF 版同名方法（`TryMatchPassword`、`QuickVerifyPassword`、`IsPasswordError`） |

### 调用点更新

| 调用点 | 现有方法 | 新方法 | 处理逻辑变更 |
|--------|----------|--------|-------------|
| `MainWindowViewModel.LoadArchiveAsync` (line 889) | `TryMatchPassword` | `TryMatchPasswordEx` | `CorruptedOrInvalid` → 直接报错不再弹密码框 |
| `MainWindowViewModel.EnterPasswordAsync` (line 1204) | `QuickVerifyPassword` | `QuickVerifyPasswordEx` | `CorruptedOrInvalid` → "文件损坏" 而非 "密码错误" |
| `ExtractSettingsViewModel.ValidateAllAsync` | `QuickVerifyPassword` | `QuickVerifyPasswordEx` | 损坏标记为 `Corrupted` 状态 |
| `App.ResolvePasswordAsync` (WPF) | 内部调用 | 内部调用 | 同步更新分流逻辑 |

---

## 实施步骤

### Phase 1: 核心分类逻辑 (PasswordService.cs)

1. 新增 `PasswordVerificationResult` enum + `PasswordVerifyInfo` record
2. 实现 `ClassifyException` / `ClassifySharpCompressException` / `ClassifySharpSevenZipException` / `ClassifyGenericException`
3. 新增 `QuickVerifyPasswordEx` / `TryMatchPasswordEx` 包装现有方法
4. 保留旧方法 `QuickVerifyPassword` / `TryMatchPassword` / `IsPasswordError` 标记 `[Obsolete]` 兼容

### Phase 2: Avalonia UI 接入

1. `MainWindowViewModel.LoadArchiveAsync` - 密码自动匹配分流
2. `MainWindowViewModel.EnterPasswordAsync` - 手动输入验证分流
3. `ExtractSettingsViewModel.ValidateAllAsync` - 逐包校验分流
4. 新增 `SourceArchiveStatus.Corrupted` 状态（或复用 `Error` + 详细消息）

### Phase 3: WPF 同步修复

1. `App.Password.cs` 同步新增分类逻辑
2. `MainWindow.xaml.cs` `LoadArchiveAsync` / `ExtractAsync` 接入

### Phase 4: 测试验证

准备测试用例：
- ✅ 正常加密包 + 正确密码 → Success
- ✅ 正常加密包 + 错误密码 → WrongPassword
- ✅ 损坏的加密包 + 正确密码 → CorruptedOrInvalid（不再误报 WrongPassword）
- ✅ 损坏的加密包 + 错误密码 → CorruptedOrInvalid（优先判断损坏）
- ✅ 非加密包误传密码 → Success（无加密条目直接通过）
- ✅ 7z EncryptHeaders=true + 正确/错误密码
- ✅ RAR 加密 + 正确/错误密码

---

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| SharpSevenZip HRESULT 不稳定/版本差异 | 双重判断：优先 HRESULT，回退 Message 解析；单测覆盖常见错误码 |
| 现有调用方忘记迁移 | 旧方法标记 `[Obsolete("Use QuickVerifyPasswordEx / TryMatchPasswordEx")]`，编译警告提醒 |
| `Unknown` 结果导致上层不知如何处理 | `Unknown` 视为 `WrongPassword`（保守策略：让用户重试），日志记录原始异常供事后分析 |

---

## 验收标准

1. ✅ 损坏的加密 ZIP/7z/RAR，输入正确密码 → 提示"文件损坏"而非"密码错误"
2. ✅ 密码库自动匹配遇到损坏文件 → 立即停止尝试其他密码，报告损坏（不消耗 100 次上限）
3. ✅ 解压设置窗口逐包校验 → 损坏包显示"文件损坏"状态，非"需密码"
4. ✅ 所有现有加密场景（ZIP AES-256 / 7z EncryptHeaders=true/false / RAR）验证流程不回归
5. ✅ WPF 版同步修复，行为一致

---

## 相关文档

- [密码流程统一](password-flow-unification.md) - `ResolvePasswordAsync` 内部应调用 `QuickVerifyPasswordEx`/`TryMatchPasswordEx`
- [加密文件名魔数检测](encrypted-filename-magic-detection.md) - 魔数检测异常分类可复用本计划的 `ClassifyException`