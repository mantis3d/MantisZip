# 文件关联失效修复（悬空 UserChoice + 测试污染真实注册表 + 清理遗漏）

> **状态**: 📋 待实施 | **创建**: 2026-10-15 | **优先级**: P1 | **预估工时**: 3.5-5h
> **来源**: 2026-10-15 现场诊断（本机 HKCU 注册表取证 + 代码溯源）
> **诊断结论**: 关联失效不是被别的解压软件抢走，而是 **MantisZip 自己的卸载逻辑删掉了 per-format ProgId（`MantisZip.Zip`），却从不清理指向它的 `UserChoice`，留下悬空默认关联**；本机直接诱因是**单元测试直接读写真实 HKCU 注册表**。

## 背景

用户报告：`.zip` 文件关联"经常会失效"，且在一台**没装任何其他解压软件**的干净机上复现（故排除"被抢"）。失效表现为：双击 `.zip` 打开的是内置 zip 视图/别的软件、"打开方式"里找不到 MantisZip、图标随之变化。

## 根因（已取证）

### 取证：本机注册表现状

```
HKCU\...\Explorer\FileExts\.zip\UserChoice
    ProgId = MantisZip.Zip          ← 默认指向 MantisZip.Zip
    Hash   = ypQTnXPxo7k=

HKCU\Software\Classes\MantisZip.Zip        ← 不存在！（悬空）
HKCU\Software\Classes\MantisZip.Archive    ← 不存在（旧 ProgId 键已删）

HKCU\Software\Classes\.zip\OpenWithProgids
    MantisZip.Archive = REG_NONE    ← 只剩遗留旧值，无 MantisZip.Zip

HKCU\Software\Classes\.zip\DefaultIcon
    (默认) = D:\Github\MantisZip\tests\MantisZip.Tests\bin\Debug\net10.0-windows10.0.17763.0\Resources\Icons\zip.ico
              ↑ 指向**测试项目输出目录** → 证明是 dotnet test 写进去的
```

`UserChoice` 说"双击用 `MantisZip.Zip`"，但 `MantisZip.Zip` 已不存在 → Windows 无法解析默认处理器 → 回退内置/别家、图标失效、"打开方式"里也看不到 MantisZip。

### 触发链

1. **主因（本机直接病因）**：`tests/MantisZip.Tests/ShellIntegrationAssocTests.cs` 的每个用例都在 `finally` 里调 `ShellIntegration.UninstallAssociationForExtension(".zip")`，**直接在真实 HKCU 注册表上**安装再卸载。每次 `dotnet test` 都会：写 `MantisZip.Zip` ProgId（command 指向测试宿主）+ OpenWithProgids + DefaultIcon（测试目录图标）→ 再删掉 ProgId 和 OpenWithProgids。`UserChoice` 被留下悬空。
2. **同源（任何机器）**：安装包 `installer.iss:121-123` 的 `[UninstallRun]` 在**真正卸载时**跑 `--uninstall-assoc` → `UninstallAssociations()` → 删掉所有 `MantisZip.*` ProgId，**同样不清理 `UserChoice`** → 卸载/重装后悬空。
   > 注：已核实 Inno 安装包**没有** `InitializeSetup`/升级卸载代码，覆盖安装**不会**自动运行旧卸载器，故"升级"不是触发点；触发点是**真卸载**与**跑测试**。

### 顺带发现的两个代码 bug

- **B（DefaultIcon 清不掉）**：`UninstallAssociationForExtension` 清理图标时读的是 `Software\Classes\{ext}\(默认)`（Assoc.cs:183-193），但安装写的是 `Software\Classes\{ext}\DefaultIcon\(默认)`（Assoc.cs:57/64）——**读错键**，导致 `.zip\DefaultIcon` 永久残留（取证里那条测试目录路径就是铁证）。
- **D（旧 ProgId 值残留）**：`CleanupOldProgId`（Assoc.cs:128-131）只删旧 ProgId **键**，不删各扩展名 `OpenWithProgids` 里遗留的 `MantisZip.Archive` **值** → 永久残留（取证已见）。

### 关键认知（修复方向的约束）

Windows 的 `UserChoice` 带 `Hash`（由 SID+扩展名+ProgId+时间戳派生）且 Win11 有 UCPD 驱动保护，**第三方程序不得直接写默认**（写了的 Hash 对不上会被系统清除）。因此：
- MantisZip **不能**程序化把自己设成默认（现状代码也从不写 `UserChoice`，只写"打开方式候选"）。
- 但 `UserChoice` 的**删除**是允许的（只是 SetValue 被 DENY）。
- **重要**：只要 `UserChoice` 仍指向 `MantisZip.Zip`，把 `MantisZip.Zip` ProgId **重新建出来**（含正确的 `shell\open\command`），默认就会自动恢复可用——这正是 E 自愈的基础。

## 方案

| 任务 | 内容 | 作用 |
|------|------|------|
| A | 注册表访问收口到可注入基路径的 `RegistryHost`，测试重定向到沙箱 | 根治"跑测试毁真实关联" |
| B | 卸载时，**仅当** `UserChoice\Progid` 指向 `MantisZip*` 时删除该 `UserChoice` 键 | 卸载/升级不再留悬空默认 |
| C | 修 DefaultIcon 清理读错键 | 图标残留可清除 |
| D | `CleanupOldProgId` 同时清理各扩展名 `OpenWithProgids` 里的旧 ProgId 值 | 清掉遗留脏值，恢复"打开方式"可见性 |
| E | 启动自愈：`UserChoice` 指向我们或用户设置启用、但 ProgId 缺失 → 重建 ProgId（不写 UserChoice） | 兜底修复已损坏的机器 + 防复发 |
| F | 安装包 `[UninstallRun]` 补 `RunOnceId`（硬化）；确认升级不触发卸载 | 消除重复条目/确认边界 |

**技术选型（A）**：不引入完整接口/内存 mock，而是给注册表访问加一个**基路径重定向**——生产不变，测试把 `Software\` 下的写入重定向到 `Software\MantisZip\AssocTestSandbox\`。理由：改动是纯机械替换（路径字符串不变）、diff 小、且测试仍走真实注册表代码路径（保真度高于内存 mock）。

**限制（写进文档，不在本计划修）**：应用无法程序化设默认。若 Windows 自己清掉了 `UserChoice`（UCPD/hash 校验），只能引导用户在 Windows"默认应用"里重设；E 只能救"`UserChoice` 还在但 ProgId 被删"这类损坏。

---

## 任务拆分

### Task A: 注册表访问收口 + 测试沙箱隔离

**Files:**
- Create: `src/MantisZip.UI.Avalonia/Services/RegistryHost.cs`
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs:97,112,118,124`
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs:17,30,50,102,173,183,216,246`
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs:17,22,25,28,31,255,518`
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs:133`
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

**Interfaces:**
- Produces: `internal static class RegistryHost`（`Open(string fullPath, bool writable=false)` / `Create(string fullPath)` / `Delete(string fullPath)` / `UseSandbox(string sandboxRelativeToSoftware)` / `Reset()`）
- 路径字符串保持**原样**（如 `@"Software\Classes\{ext}\OpenWithProgids"`），只有调用的方法名变。

- [ ] **Step 1: 新建 `RegistryHost.cs`**

```csharp
using Microsoft.Win32;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// HKCU 注册表访问的唯一入口。
/// 生产：路径原样落盘。
/// 测试：<see cref="UseSandbox"/> 后，所有以 "Software\" 开头的路径被重定向到
/// "Software\{sandbox}\..."，从而不污染真实的文件关联/右键菜单注册。
/// </summary>
internal static class RegistryHost
{
    private const string Root = @"Software";
    private static string? _sandbox;   // 相对 Software 的一段，如 "MantisZip\AssocTestSandbox"

    internal static RegistryKey? Open(string fullPath, bool writable = false)
        => Registry.CurrentUser.OpenSubKey(Map(fullPath), writable);

    internal static RegistryKey Create(string fullPath)
        => Registry.CurrentUser.CreateSubKey(Map(fullPath));

    internal static void Delete(string fullPath)
        => Registry.CurrentUser.DeleteSubKeyTree(Map(fullPath), throwOnMissingSubKey: false);

    /// <summary>测试专用：重定向到沙箱（参数为相对 Software 的路径段）。</summary>
    internal static void UseSandbox(string sandboxRelativeToSoftware) => _sandbox = sandboxRelativeToSoftware;

    /// <summary>测试专用：恢复生产行为。</summary>
    internal static void Reset() => _sandbox = null;

    private static string Map(string fullPath)
    {
        if (_sandbox == null) return fullPath;
        if (!fullPath.StartsWith(Root, System.StringComparison.OrdinalIgnoreCase)) return fullPath;
        var rest = fullPath.Substring(Root.Length).TrimStart('\\');
        return string.IsNullOrEmpty(rest)
            ? $@"{Root}\{_sandbox}"
            : $@"{Root}\{_sandbox}\{rest}";
    }
}
```

- [ ] **Step 2: 把 `ShellIntegration.cs` 的 3 个 helper 改为走 RegistryHost（路径不变）**

```csharp
    private static void SetRegistryValue(string subKey, string? valueName, string value)
    {
        using var key = RegistryHost.Create(subKey);
        key?.SetValue(valueName, value);
    }

    private static void SetRegistryDword(string subKey, string valueName, int value)
    {
        using var key = RegistryHost.Create(subKey);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static void DeleteRegistryKey(string subKey)
        => RegistryHost.Delete(subKey);
```
同时 `ShellIntegration.cs:97`（`GetDynamicMenuStatus`）的 `Registry.CurrentUser.OpenSubKey(ContextMenuRegPath)` → `RegistryHost.Open(ContextMenuRegPath)`。

- [ ] **Step 3: 替换 `ShellIntegration.Assoc.cs` 与 `ShellIntegration.Menu.cs` 里的全部 `Registry.CurrentUser.*`**

机械替换（保持路径字符串原样）：
- `Registry.CurrentUser.OpenSubKey(X)` → `RegistryHost.Open(X)`
- `Registry.CurrentUser.OpenSubKey(X, writable: true)` → `RegistryHost.Open(X, writable: true)`
- `Registry.CurrentUser.CreateSubKey(X)` → `RegistryHost.Create(X)`
- `Registry.CurrentUser.DeleteSubKeyTree(X, throwOnMissingSubKey: false)` → `RegistryHost.Delete(X)`

涉及行（Assoc.cs）：17、30、50、102、173、183、216、246；
（Menu.cs）：17、22、25、28、31、255、518。

- [ ] **Step 4: `App.axaml.cs:133` 同步改写**

```csharp
using var firstRunKey = RegistryHost.Open(@"Software\MantisZip", writable: true);
```
（`RegistryHost` 在 `MantisZip.UI.Avalonia.Services` 命名空间，`App.axaml.cs` 需 `using MantisZip.UI.Avalonia.Services;`——已存在则忽略。）

- [ ] **Step 5: 测试类挂沙箱**

在 `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs` 让测试类实现 `IDisposable`：

```csharp
public class ShellIntegrationAssocTests : IDisposable
{
    private const string Sandbox = @"MantisZip\AssocTestSandbox";

    public ShellIntegrationAssocTests() => RegistryHost.UseSandbox(Sandbox);

    public void Dispose()
    {
        RegistryHost.Reset();
        // 直接删沙箱（Reset 后 RegistryHost.Delete 会落到生产路径，故用 Registry 原语）
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\MantisZip\AssocTestSandbox", throwOnMissingSubKey: false);
    }
    // ... 原有测试体不变
}
```
`RegistryHost` 为 `internal`，`MantisZip.UI.Avalonia.csproj:17` 已有 `InternalsVisibleTo("MantisZip.Tests")`，可直接访问。

- [ ] **Step 6: 构建并跑测试**

Run:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```
Expected: 编译 0 错误；测试全绿。

- [ ] **Step 7: 验证真实注册表不被污染**

Run（测试前后各跑一次，输出应完全一致，且**不含**任何 `MantisZip.` 键）：
```powershell
reg query "HKCU\Software\Classes\MantisZip.Zip" /s
reg query "HKCU\Software\Classes\.zip\OpenWithProgids"
```
Expected: 值不受 `dotnet test` 影响；沙箱被清理干净（`reg query "HKCU\Software\MantisZip\AssocTestSandbox"` 报找不到）。

- [ ] **Step 8: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/RegistryHost.cs src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs src/MantisZip.UI.Avalonia/App.axaml.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "fix(avalonia): 文件关联注册表访问收口 + 测试沙箱隔离，杜绝 dotnet test 污染真实关联"
```

---

### Task B: 卸载时清理悬空 UserChoice

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs:166-200`（`UninstallAssociationForExtension`）
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

**Interfaces:**
- Consumes: `RegistryHost`（Task A）
- Produces: 无新公开 API（行为变更）

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void Uninstall_ClearsDanglingUserChoice_WhenPointsToUs()
{
    try
    {
        ShellIntegration.InstallAssociationForExtension(".zip");
        // 模拟用户"打开方式→始终"写入的默认（沙箱内）
        using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            uc.SetValue("Progid", "MantisZip.Zip");

        ShellIntegration.UninstallAssociationForExtension(".zip");

        using var check = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice");
        Assert.Null(check);   // 指向我们的 UserChoice 应被删除
    }
    finally { CleanupExtension(".zip"); }
}

[Fact]
public void Uninstall_PreservesUserChoice_WhenPointsToOtherApp()
{
    try
    {
        ShellIntegration.InstallAssociationForExtension(".zip");
        using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            uc.SetValue("Progid", "CompressedFolder");

        ShellIntegration.UninstallAssociationForExtension(".zip");

        using var check = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice");
        Assert.NotNull(check);                     // 别家的默认不得动
        Assert.Equal("CompressedFolder", check!.GetValue("Progid"));
    }
    finally { CleanupExtension(".zip"); }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj --filter Uninstall_Clear`
Expected: 第一个用例 FAIL（UserChoice 仍在）。

- [ ] **Step 3: 在 `UninstallAssociationForExtension` 末尾加清理**

```csharp
        // 删除独立 ProgId 键（仅当该 ProgId 不被其他扩展引用时）
        DeleteRegistryKey($@"Software\Classes\{progId}");

        // 清理"悬空默认"：仅当 UserChoice 指向我们的 ProgId 时删除该键。
        // UserChoice 的 SetValue 权限被系统 DENY，但删除整键是允许的——
        // 删掉后 Windows 干净回退，而不是保留一个指向已删 ProgId 的幽灵默认。
        try
        {
            var ucPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice";
            string? curProgId;
            using (var uc = RegistryHost.Open(ucPath))
                curProgId = uc?.GetValue("Progid") as string;

            if (!string.IsNullOrEmpty(curProgId) &&
                curProgId.StartsWith("MantisZip", StringComparison.OrdinalIgnoreCase))
            {
                RegistryHost.Delete(ucPath);
            }
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to clear UserChoice: {ex.Message}"); }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
Expected: PASS（含两个新用例）。

- [ ] **Step 5: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "fix(avalonia): 卸载文件关联时清理指向 MantisZip 的悬空 UserChoice"
```

---

### Task C: 修 DefaultIcon 清理读错键

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs:178-196`
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void Uninstall_RemovesDefaultIcon()
{
    try
    {
        ShellIntegration.InstallAssociationForExtension(".zip");
        using (var k = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon"))
            Assert.NotNull(k?.GetValue(null));     // 安装后应存在

        ShellIntegration.UninstallAssociationForExtension(".zip");

        using var after = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon");
        Assert.Null(after?.GetValue(null));        // 卸载后应清掉
    }
    finally { CleanupExtension(".zip"); }
}
```

- [ ] **Step 2: 跑测试确认失败** → `--filter Uninstall_RemovesDefaultIcon`，Expected: FAIL。

- [ ] **Step 3: 把读错的键改对**

`Assoc.cs:183` 附近，把
```csharp
using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
if (extKey?.GetValue(null) is string currentIcon)
{ ... }
```
改为读 `DefaultIcon` 子键：
```csharp
using var iconKey = RegistryHost.Open($@"Software\Classes\{ext}\DefaultIcon", writable: true);
if (iconKey?.GetValue(null) is string currentIcon)
{
    if (iconPath != null && currentIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase))
        iconKey.DeleteValue(null!);
    else if (exePath != null && currentIcon.Equals($@"""{exePath}"",0", StringComparison.OrdinalIgnoreCase))
        iconKey.DeleteValue(null!);
}
```

- [ ] **Step 4: 跑测试确认通过** → 全绿。

- [ ] **Step 5: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "fix(avalonia): 修正文件关联卸载时 DefaultIcon 清理读取的子键路径"
```

---

### Task D: 清理遗留 OpenWithProgids 旧 ProgId 值

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs:127-131`（`CleanupOldProgId`）
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void CleanupOldProgId_RemovesLegacyOpenWithValue_ButKeepsCurrent()
{
    try
    {
        using (var k = RegistryHost.Create(@"Software\Classes\.zip\OpenWithProgids"))
        {
            k.SetValue("MantisZip.Archive", Array.Empty<byte>(), Microsoft.Win32.RegistryValueKind.None);
            k.SetValue("MantisZip.Zip", Array.Empty<byte>(), Microsoft.Win32.RegistryValueKind.None);
        }

        ShellIntegration.CleanupOldProgId();

        using var after = RegistryHost.Open(@"Software\Classes\.zip\OpenWithProgids");
        Assert.Null(after?.GetValue("MantisZip.Archive"));   // 旧值清掉
        Assert.NotNull(after?.GetValue("MantisZip.Zip"));    // 现值保留
    }
    finally { CleanupExtension(".zip"); }
}
```

- [ ] **Step 2: 跑测试确认失败** → `--filter CleanupOldProgId`，Expected: FAIL。

- [ ] **Step 3: 扩展 `CleanupOldProgId`**

```csharp
    public static void CleanupOldProgId()
    {
        DeleteRegistryKey($@"Software\Classes\{OldProgId}");

        // 旧版单 ProgId 时代在 OpenWithProgids 里留下的值也要清（只删键不删值会永久残留，
        // 且该值指向已删除的 ProgId，导致"打开方式"里出现一个打不开的条目）。
        foreach (var ext in ArchiveExtensions)
        {
            try
            {
                using var key = RegistryHost.Open($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                key?.DeleteValue(OldProgId, throwOnMissingValue: false);
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.CleanupOldProgId: failed to clear {ext} OpenWithProgids: {ex.Message}"); }
        }
    }
```

- [ ] **Step 4: 跑测试确认通过** → 全绿。

- [ ] **Step 5: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "fix(avalonia): 清理旧版 ProgId 遗留的 OpenWithProgids 值"
```

---

### Task E: 启动自愈（重建缺失 ProgId）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs`（新增 `SelfHealAssociations`）
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs:160-170`（非便携分支，调用自愈）
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

**Interfaces:**
- Produces: `ShellIntegration.SelfHealAssociations(AppSettings s)`（`public static`）
- 依赖：需一个 ext→AppSettings 关联开关 的映射（见 Step 1）。

- [ ] **Step 1: 暴露 ext→开关映射（放 `ShellIntegration`，供自愈与设置页共用）**

```csharp
    /// <summary>扩展名对应的"用户是否启用该格式关联"设置项。</summary>
    internal static bool IsAssocEnabledInSettings(AppSettings s, string ext) => ext.ToLowerInvariant() switch
    {
        ".zip"    => s.AssocZip,
        ".7z"     => s.Assoc7z,
        ".rar"    => s.AssocRar,
        ".tar"    => s.AssocTar,
        ".tgz" or ".tar.gz" => s.AssocTarGz,
        ".gz"     => s.AssocGz,
        ".iso"    => s.AssocIso,
        _         => false,
    };
```
（`SettingsWindowViewModel.IsEnabledFromSettings`（:405）可后续改为调用此方法去重；本任务不强制。）

- [ ] **Step 2: 写失败测试**

```csharp
[Fact]
public void SelfHeal_RebuildsProgId_WhenUserChoiceDangles()
{
    try
    {
        // 模拟损坏现场：UserChoice 指向我们，但 ProgId 缺失
        using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            uc.SetValue("Progid", "MantisZip.Zip");
        using (var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
            Assert.Null(chk);   // 前置：ProgId 不存在

        ShellIntegration.SelfHealAssociations(new AppSettings());

        using var after = RegistryHost.Open(@"Software\Classes\MantisZip.Zip\shell\open\command");
        Assert.NotNull(after?.GetValue(null));   // ProgId 被重建，UserChoice 恢复可解析
    }
    finally { CleanupExtension(".zip"); }
}
```

- [ ] **Step 3: 跑测试确认失败** → `--filter SelfHeal`，Expected: FAIL（方法不存在）。

- [ ] **Step 4: 实现 `SelfHealAssociations`**

```csharp
    /// <summary>
    /// 启动自愈：对每个内置扩展名，若"用户曾把我们设为默认（UserChoice 指向 MantisZip）"
    /// 或"用户在设置里启用了该扩展名关联"，但对应的 per-format ProgId 缺失，则重建它。
    ///
    /// 只重建 ProgId + OpenWithProgids + DefaultIcon，**从不写 UserChoice**（哈希保护），
    /// 因此绝不会覆盖用户在 Windows 里对其他程序的选择——只把自己丢失的注册补回来。
    /// 若 UserChoice 仍指向我们，重建 ProgId 后双击默认即自动恢复。
    /// </summary>
    public static void SelfHealAssociations(AppSettings s)
    {
        foreach (var ext in ArchiveExtensions)
        {
            try
            {
                var progId = GetProgId(ext);
                bool userChoseUs;
                using (var uc = RegistryHost.Open($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
                    userChoseUs = (uc?.GetValue("Progid") as string)
                        ?.StartsWith("MantisZip", StringComparison.OrdinalIgnoreCase) == true;

                if (!userChoseUs && !IsAssocEnabledInSettings(s, ext)) continue;

                bool progIdMissing;
                using (var k = RegistryHost.Open($@"Software\Classes\{progId}"))
                    progIdMissing = k == null;

                if (progIdMissing)
                {
                    App.DebugLog($"ShellIntegration.SelfHealAssociations: rebuilding missing ProgId for {ext} (userChoseUs={userChoseUs})");
                    InstallAssociationForExtension(ext);
                }
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.SelfHealAssociations: failed for {ext}: {ex.Message}"); }
        }
    }
```

- [ ] **Step 5: 启动接线（`App.axaml.cs`，非便携分支内，`CheckComStatus()` 之前）**

```csharp
            // ── 文件关联自愈：修复"UserChoice 悬空/ProgId 被误删"的历史损坏 ──
            try
            {
                ShellIntegration.SelfHealAssociations(AppSettings.Load());
            }
            catch (Exception healEx)
            {
                App.DebugLog($"OnFrameworkInitializationCompleted: SelfHealAssociations failed: {healEx.Message}");
            }
```

- [ ] **Step 6: 跑测试确认通过** → 全绿。

- [ ] **Step 7: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs src/MantisZip.UI.Avalonia/App.axaml.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "feat(avalonia): 启动时自愈缺失的文件关联 ProgId，修复悬空默认"
```

---

### Task F: 安装包卸载钩子硬化

**Files:**
- Modify: `installer.iss:121-123`
- Modify: `installer-selfcontained.iss`（对应 `[UninstallRun]` 段）

> 已核实：Inno 安装包无升级卸载代码，覆盖安装不自动跑旧卸载器，故**升级不会触发** `--uninstall-assoc`；无需为升级加特殊处理（升级后的关联由 Task E 自愈保障）。本任务仅补 `RunOnceId` 硬化。

- [ ] **Step 1: 给两条 `[UninstallRun]` 加 `RunOnceId`**

```ini
[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden; WorkingDir: "{app}"; RunOnceId: "MnzUninstallShell"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-assoc"; Flags: runhidden; WorkingDir: "{app}"; RunOnceId: "MnzUninstallAssoc"
```
（`installer-selfcontained.iss` 同步。）

- [ ] **Step 2: 编译校验（如本机装有 ISCC）**

Run: `ISCC installer.iss`
Expected: 0 error。

- [ ] **Step 3: Commit**

```bash
git add installer.iss installer-selfcontained.iss
git commit -m "chore(installer): 卸载钩子加 RunOnceId 防止重复条目"
```

---

## 涉及文件

- 新增：`src/MantisZip.UI.Avalonia/Services/RegistryHost.cs`
- 修改：`Services/ShellIntegration.cs`、`Services/ShellIntegration.Assoc.cs`、`Services/ShellIntegration.Menu.cs`、`App.axaml.cs`
- 测试：`tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`
- 安装包：`installer.iss`、`installer-selfcontained.iss`
- 文档：`docs/PROGRESS.md`、`docs/progress-avalonia-detail.md`、`docs/PLAN.md`

## 验证

1. **构建**：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 错误；`dotnet build src\MantisZip.Core\MantisZip.Core.csproj`（如 Core 未动则跳过）。
2. **测试**：`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 全绿。
3. **回归隔离（关键）**：测试前记录 `HKCU\Software\Classes\MantisZip.*` 与 `.zip\OpenWithProgids`，跑测试后再记录，两次一致且无新增 `MantisZip.` 键；`HKCU\Software\MantisZip\AssocTestSandbox` 被清理。
4. **端到端（修复已损坏的机器）**：
   - 先清掉当前悬空的 `UserChoice` 与残留：手动删 `HKCU\...\FileExts\.zip\UserChoice`（或直接启动新构建触发 E）。
   - 启动新构建 → `.zip` 双击恢复用 MantisZip 打开（E 重建 ProgId 后 UserChoice 可解析）。
   - 设置页"安装关联"后"取消全部关联" → 无悬空 `UserChoice`、无残留 `DefaultIcon`、无残留 `OpenWithProgids\MantisZip.Archive`。
5. **卸载**：安装包安装 → 卸载 → `HKCU\Software\Classes\MantisZip.*` 全部消失且无悬空 `UserChoice`。

## 边界

- **不**程序化写 `UserChoice`（哈希/UCPD 保护，写了会被系统清除）；E 只重建 ProgId。
- 不改 `Core` 层；不改右键菜单（COM/cascade）逻辑。
- 仅 Windows（`Registry.CurrentUser`）；Avalonia-first（规则 11）。
- 不动版本号（规则 2，需用户明确要求才动）。
- Task E 的启动自愈在便携模式下**不执行**（与现有 `App.axaml.cs` 便携分支一致）。

## 风险与限制

- **A 的机械替换面较广**（约 29 处 `Registry.CurrentUser`）：靠"路径字符串不变、只换方法名"降低风险；构建 + 测试 + 第 3 步回归隔离验证兜底。
- **UserChoice 删/读权限**：不同 Windows 版本对 `UserChoice` 的读取一般允许；删除整键一般允许，但个别加固环境可能拒绝 → 均已 try/catch 包裹，失败仅记日志，不影响主流程。
- **应用无法设默认**：这是平台限制，非本计划可解。若用户机器的 `UserChoice` 被 Windows 自身清空，E 无能为力，需在设置页提示"到 Windows 默认应用里重设"（**本计划不含此 UI 提示，记为后续项**）。
- **测试仍用真实注册表（沙箱子树）**：需保证 `Dispose` 一定清理；沿用现有 `[Collection("RegistryTests", DisableParallelization = true)]` 串行约束。
