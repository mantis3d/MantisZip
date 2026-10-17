# 文件关联失效修复（悬空 UserChoice + 测试污染真实注册表 + 清理遗漏）

> **状态**: 📋 待实施 | **创建**: 2026-10-15 | **优先级**: P1 | **预估工时**: 3.5-5h
> **来源**: 2026-10-15 现场诊断（本机 HKCU 注册表取证 + 代码溯源）
> **诊断结论**: 关联失效不是被别的解压软件抢走，而是 **MantisZip 自己的卸载逻辑删掉了 per-format ProgId（`MantisZip.Zip`），却从不清理指向它的 `UserChoice`，留下悬空默认关联**；本机直接诱因是**单元测试直接读写真实 HKCU 注册表**。

## 背景

用户报告：`.zip` 文件关联"经常会失效"，且在一台**没装任何其他解压软件**的干净机上复现（故排除"被抢"）。失效表现为：双击 `.zip` 打开的是内置 zip 视图/别的软件、"打开方式"里找不到 MantisZip、图标随之变化。

## 根因（已取证）

### 破损态取证（2026-10-15）

```
HKCU\...\Explorer\FileExts\.zip\UserChoice
    ProgId = MantisZip.Zip          ← 默认指向 MantisZip.Zip
    Hash   = ypQTnXPxo7k=

HKCU\Software\Classes\MantisZip.Zip        ← 不存在！（悬空）
HKCU\Software\Classes\MantisZip.Archive    ← 不存在（旧 ProgId 键已删）

HKCU\Software\Classes\.zip\OpenWithProgids
    MantisZip.Archive = REG_NONE    ← 2026-10-17 复测未再现；Bug D 为**代码层缺陷**，现场无该残留

HKCU\Software\Classes\.zip\DefaultIcon
    (默认) = D:\Github\MantisZip\tests\MantisZip.Tests\bin\Debug\net10.0-windows10.0.17763.0\Resources\Icons\zip.ico
              ↑ 指向**测试项目输出目录** → 证明是 dotnet test 写进去的
```

`UserChoice` 说"双击用 `MantisZip.Zip`"，但 `MantisZip.Zip` 已不存在 → Windows 无法解析默认处理器 → 回退内置/别家、图标失效、"打开方式"里也看不到 MantisZip。

### 健康态基线（2026-10-17，`--install-assoc` 修复后）

```
...\FileExts\.zip\UserChoice：ProgId = MantisZip.Zip、Hash = 7fm20Z9+Yno=
...\FileExts\.7z\UserChoice：ProgId = MantisZip.7z
...\FileExts\.tar.gz\UserChoice：不存在
Software\Classes\MantisZip.Zip\shell\open\command（默认）= "F:\GitHub\MantisZip\src\MantisZip.UI.Avalonia\bin\Debug\net10.0\MantisZip.UI.Avalonia.exe" --open-dispatch "%1"
Software\Classes\.zip\OpenWithProgids：MantisZip.Zip = REG_NONE（健康态无 MantisZip.Archive 残留）
Software\Classes\.zip\DefaultIcon（默认）= app bin 下 Resources\Icons\zip.ico
.7z / .rar / .gz 的 DefaultIcon 均残留（Bug B 非 zip 专属）
```

### 复现剧本（2026-10-17 已实测）

1. 跑 `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`（712 通过/0 失败）后 → `Software\Classes\MantisZip.Zip` 消失；`.zip\DefaultIcon` 被覆盖为 `tests\MantisZip.Tests\bin\Debug\net10.0-windows10.0.17763.0\Resources\Icons\zip.ico`；`UserChoice` 不变。
2. 症状：双击 `.zip` 走别家、"打开方式"无 MantisZip、图标暂不变（测试 ico 仍在，`dotnet clean` 后即失效）。
3. 恢复：`MantisZip.UI.Avalonia.exe --install-assoc` → ProgId 重建、**UserChoice 未动**、双击恢复（**实证 Task E 核心假设**）。

### 触发链

1. **主因（本机直接病因）**：`tests/MantisZip.Tests/ShellIntegrationAssocTests.cs` 的每个用例都在 `finally` 里调 `ShellIntegration.UninstallAssociationForExtension(".zip")`，**直接在真实 HKCU 注册表上**安装再卸载。每次 `dotnet test` 都会：写 `MantisZip.Zip` ProgId（command 指向测试宿主）+ OpenWithProgids + DefaultIcon（测试目录图标）→ 再删掉 ProgId 和 OpenWithProgids。`UserChoice` 被留下悬空。
2. **同源（任何机器）**：安装包 `installer.iss:121-123` 的 `[UninstallRun]` 在**真正卸载时**跑 `--uninstall-assoc` → `UninstallAssociations()` → 删掉所有 `MantisZip.*` ProgId，**同样不清理 `UserChoice`** → 卸载/重装后悬空。
   > 注：已核实 Inno 安装包**没有** `InitializeSetup`/升级卸载代码，覆盖安装**不会**自动运行旧卸载器，故"升级"不是触发点；触发点是**真卸载**与**跑测试**。

### 顺带发现的两个代码 bug

- **B（DefaultIcon 清不掉）**：`UninstallAssociationForExtension` 清理图标时读的是 `Software\Classes\{ext}\(默认)`（Assoc.cs:183-193），但安装写的是 `Software\Classes\{ext}\DefaultIcon\(默认)`（Assoc.cs:57/64）——**读错键**，导致 `.zip\DefaultIcon` 永久残留（取证里那条测试目录路径就是铁证）。
- **D（旧 ProgId 值残留）**：`CleanupOldProgId`（Assoc.cs:128-131）只删旧 ProgId **键**，不删各扩展名 `OpenWithProgids` 里遗留的 `MantisZip.Archive` **值** → **代码层缺陷，2026-10-17 现场不可复现**。

### 关键认知（修复方向的约束）

Windows 的 `UserChoice` 带 `Hash`（由 SID+扩展名+ProgId+时间戳派生）且 Win11 有 UCPD 驱动保护，**第三方程序不得直接写默认**（写了的 Hash 对不上会被系统清除）。因此：
- MantisZip **不能**程序化把自己设成默认（现状代码也从不写 `UserChoice`，只写"打开方式候选"）。
- 但 `UserChoice` 的**删除**在多数环境是允许的（`SetValue` 通常被 DENY）；Win11 UCPD 加固环境可能连同删除一起拒绝，故一律按 **best-effort** 处理（见 Task B）。
- **重要**：只要 `UserChoice` 仍指向 `MantisZip.Zip`，把 `MantisZip.Zip` ProgId **重新建出来**（含正确的 `shell\open\command`），默认就会自动恢复可用——这正是 E 自愈的基础。

## 方案

| 任务 | 内容 | 作用 |
|------|------|------|
| A | 注册表访问收口到可注入基路径的 `RegistryHost`，测试重定向到沙箱 | 根治"跑测试毁真实关联" |
| B | 卸载时，**仅当** `UserChoice\Progid` 指向 `MantisZip*` 时尝试删除该 `UserChoice` 键（best-effort；失败不中断卸载） | 卸载/升级不再留悬空默认 |
| C | 修 DefaultIcon 清理：**读对 `Software\Classes\{ext}\DefaultIcon` 的默认值** + 放宽清理判据（现值相等 ∨ 快照 `"exe",0` 相等 ∨ 指向路径不存在 → 任一命中即删） | 图标残留可清除（含 `dotnet test` 写入的测试输出目录残留） |
| D | `CleanupOldProgId` 同时清理各扩展名 `OpenWithProgids` 里的旧 ProgId 值（验收基于构造场景） | 清掉遗留脏值，恢复"打开方式"可见性 |
| E | 启动自愈：`UserChoice.Progid` 以 `MantisZip` 开头 ∧ `Software\Classes\{ProgId}` 缺失 ∧ **该格式未被用户显式禁用** → 重建 ProgId（不写 UserChoice）。同步修正 `UninstallAllAssoc`/`DeselectAllAssoc` 持久化行为；处理 ProgId 存在但 `shell\open\command` 失效路径的加固（含 `EnsureApplicationsRegistered()`） | 兜底修复已损坏的机器 + 防复发 |
| F | 安装包 `[UninstallRun]` 补 `RunOnceId`（硬化）；确认升级不触发卸载 | 消除重复条目/确认边界 |
| G | **新增**：所有写 `HKCU\Software\Classes` 关联键的路径之后统一调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero)`（覆盖安装/卸载/自愈/清理 OpenWithProgids 残留值） | 及时刷新 Explorer 图标/打开方式 |

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
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs:147`
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

**Interfaces:**
- Produces: `internal static class RegistryHost`（`Open(string fullPath, bool writable=false)` / `Create(string fullPath)` / `Delete(string fullPath)` / `UseSandbox(string sandboxRelativeToSoftware)` / `Reset()`）
- Produces: `RegistryHost.DeleteInterceptionForTesting`（`internal static Action<string>?`，默认 `null`）—— **测试注入缝**：非 `null` 时 `Delete(string fullPath)` 在真正删除**之前**先调用该委托，委托抛出的异常直接向上传播（用于模拟 UCPD/权限拒绝），委托正常返回则继续执行真实删除；为 `null` 时行为与原来完全一致。`RegistryHost` 是 `static class`、**没有其它注入点**，真实注册表上无法稳定复现"删除被拒绝"，故由本缝支撑 Task B 的确定性失败用例。
- 路径字符串保持**原样**（如 `@"Software\Classes\{ext}\OpenWithProgids"`），只有调用的方法名变。

- [ ] **Step 1: 新建 `RegistryHost.cs`（测试构造函数必须幂等清理沙箱 `Software\MantisZip\AssocTestSandbox`，否则上次崩溃残留会让 `Assert.False` 前置断言失败）**

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

- [ ] **Step 1b: 新增 `DeleteInterceptionForTesting` 测试注入缝（供 Task B 确定性构造"删除失败"）**

`RegistryHost` 是 `static class`、没有其它注入点，真实注册表上无法稳定复现 UCPD/权限拒绝。先在字段区新增注入缝字段：

```csharp
    /// <summary>测试注入缝：非 null 时 Delete 在真正删除之前先调用该委托。
    /// 委托抛出的异常直接向上传播（用于模拟 UCPD / 权限拒绝）；
    /// 委托正常返回则继续执行真实删除。仅测试赋值；生产路径恒为 null。</summary>
    internal static System.Action<string>? DeleteInterceptionForTesting;
```

再在 `Delete(string fullPath)` 方法体**首行**插入守卫（表达式体 `=>` 改为块体，其余实现不变）：

```csharp
    internal static void Delete(string fullPath)
    {
        DeleteInterceptionForTesting?.Invoke(fullPath);   // 测试注入：可在此抛出以模拟 UCPD / 权限拒绝
        Registry.CurrentUser.DeleteSubKeyTree(Map(fullPath), throwOnMissingSubKey: false);
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

- [ ] **Step 4: `App.axaml.cs:147` 同步改写**（首次运行键），非便携分支 143-188，`CheckComStatus` 178

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

    public ShellIntegrationAssocTests()
    {
        // 测试构造函数必须幂等清理沙箱
        RegistryHost.Reset();
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\MantisZip\AssocTestSandbox", throwOnMissingSubKey: false);
        RegistryHost.UseSandbox(Sandbox);
        // 幂等清除删除拦截：保证注入缝不跨用例串扰（Task B 的失败用例会临时设置它）
        RegistryHost.DeleteInterceptionForTesting = null;
    }

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
`RegistryHost` 为 `internal`，`MantisZip.UI.Avalonia.csproj:19` 已有 `InternalsVisibleTo("MantisZip.Tests")`，可直接访问。

- [ ] **Step 6: 构建并跑测试**

Run:
```powershell
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj
dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj
```
Expected: 编译 0 错误；测试全绿。

- [ ] **Step 7: 验证真实注册表不被污染**

Run（测试前后各跑一次，**逐项对照应完全一致** —— 值可能非空：已装关联的机器上 `MantisZip.Zip` 本就合法存在，关键是"**不得新增键、不得发生值变化**"，而不是"必须为空/不含 `MantisZip.` 键"）：
```powershell
reg query "HKCU\Software\Classes\MantisZip.Zip" /s
reg query "HKCU\Software\Classes\.zip\OpenWithProgids"
```
Expected: 测试前后逐项一致（值可非空，不得有新键、不得有值变化）；沙箱被清理干净（`reg query "HKCU\Software\MantisZip\AssocTestSandbox"` 报找不到）。

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
                try
                {
                    RegistryHost.Delete(ucPath);
                }
                catch (Exception ex)
                {
                    App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to clear UserChoice: {ex.Message}");
                    // best-effort：不清理 UserChoice 不应中断卸载流程
                }
            }
        }
        catch (Exception ex) { App.DebugLog($"ShellIntegration.UninstallAssociationForExtension: failed to clear UserChoice: {ex.Message}"); }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
Expected: PASS（含两个新用例）。

- [ ] **Step 5: 注入式单测（删除 UserChoice 抛异常时不崩溃）**

`RegistryHost` 是 `static class`、没有其它注入点，真实注册表上无法确定性构造"删除被拒绝"，故使用 Task A 新增的注入缝 `DeleteInterceptionForTesting`（不再设法制造真实 ACL/UCPD 失败）。用例 `Uninstall_DoesNotThrow_WhenUserChoiceDeleteThrows`：注入删除抛异常 → 断言 `UninstallAssociationForExtension` **不抛出**、异常被 `App.DebugLog` 记录、其余卸载步骤（OpenWithProgids 值删除 / DefaultIcon 清理 / ProgId 键删除）**仍然完成**；`finally` 中把注入缝置回 `null`。

```csharp
[Fact]
public void Uninstall_DoesNotThrow_WhenUserChoiceDeleteThrows()
{
    try
    {
        ShellIntegration.InstallAssociationForExtension(".zip");
        using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            uc.SetValue("Progid", "MantisZip.Zip");

        // 注入"删除被拒绝"：只对 UserChoice 路径抛异常（模拟 UCPD / 权限拒绝）
        RegistryHost.DeleteInterceptionForTesting = path =>
        {
            if (path.IndexOf("UserChoice", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new UnauthorizedAccessException("simulated UCPD denial");
        };

        var thrown = Record.Exception(() => ShellIntegration.UninstallAssociationForExtension(".zip"));
        Assert.Null(thrown);   // best-effort：整体不抛异常；异常路径写入 App.DebugLog（不中断卸载）

        // UserChoice 仍在（删除失败属预期，不视为阻塞；由 Task E 自愈兜底）
        using (var uc = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            Assert.Equal("MantisZip.Zip", uc?.GetValue("Progid"));

        // 其余卸载步骤仍然完成：ProgId 键已删、OpenWithProgids 里的值已删
        using (var progId = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
            Assert.Null(progId);
        using (var owp = RegistryHost.Open(@"Software\Classes\.zip\OpenWithProgids"))
            Assert.Null(owp?.GetValue("MantisZip.Zip"));
    }
    finally
    {
        RegistryHost.DeleteInterceptionForTesting = null;   // 恢复注入缝，防跨用例串扰
        CleanupExtension(".zip");
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs
git commit -m "fix(avalonia): 卸载文件关联时清理指向 MantisZip 的悬空 UserChoice（best-effort，不中断卸载）"
```

---

### Task C: 修 DefaultIcon 清理（读对键 + 放宽清理判据）

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

- [ ] **Step 1b: 补「测试输出目录残留图标」用例（用户实际症状）**

现状判据是**严格相等**（`currentIcon.Equals(iconPath)` / `currentIcon.Equals($@"""{exePath}"",0")`），而现场残留值指向 `tests\MantisZip.Tests\bin\...\Resources\Icons\zip.ico` —— 与现值、快照都不相等，**即使把键读对也永远清不掉**。故必须用构造场景锁定该残留：

```csharp
[Fact]
public void Uninstall_RemovesOrphanedDefaultIcon_PointingToTestsOutputDir()
{
    try
    {
        // 构造现场症状：DefaultIcon 指向测试输出目录的 ico（与现值/快照均不相等）
        using (var k = RegistryHost.Create(@"Software\Classes\.zip\DefaultIcon"))
            k.SetValue(null, @"D:\Github\MantisZip\tests\MantisZip.Tests\bin\Debug\net10.0-windows10.0.17763.0\Resources\Icons\zip.ico");

        ShellIntegration.UninstallAssociationForExtension(".zip");

        using var after = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon");
        Assert.Null(after?.GetValue(null));   // 残留值必须被清掉（这是用户实际症状）
    }
    finally { CleanupExtension(".zip"); }
}
```

- [ ] **Step 2: 跑测试确认失败** → `--filter "Uninstall_Removes"`，Expected: FAIL（键读错 + 判据过严，两条用例都失败）。

- [ ] **Step 3: 把读错的键改对 + 放宽清理判据**

`Assoc.cs:183` 附近，把
```csharp
using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
if (extKey?.GetValue(null) is string currentIcon)
{ ... }
```
改为读 `DefaultIcon` 子键**的默认值**（`Assoc.cs:57/64` 安装时写的就是 `Software\Classes\{ext}\DefaultIcon` 的默认值），并放宽清理判据：
```csharp
using var iconKey = RegistryHost.Open($@"Software\Classes\{ext}\DefaultIcon", writable: true);
if (iconKey?.GetValue(null) is string currentIcon && !string.IsNullOrWhiteSpace(currentIcon))
{
    // 判据放宽（命中任一即清）：
    //   ① 与安装时写入的现值相等；
    //   ② 与快照（"exe",0）相等；
    //   ③ 指向的文件不存在（安装目录变更、或测试输出目录被 dotnet clean 清掉）；
    //   ④ 指向 ...\Resources\Icons\...（本项目与测试程序集唯一的图标落点；测试输出目录、
    //      旧安装目录的残留都命中该特征，而严格相等判据永远匹配不到 → 残留永久清不掉的根因）。
    bool matchesCurrent  = iconPath != null && currentIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase);
    bool matchesSnapshot = exePath != null && currentIcon.Equals($@"""{exePath}"",0", StringComparison.OrdinalIgnoreCase);
    bool pointsToMissing = !File.Exists(TrimIconIndex(currentIcon));
    bool isOurIconDir    = currentIcon.IndexOf(@"\Resources\Icons\", StringComparison.OrdinalIgnoreCase) >= 0;

    if (matchesCurrent || matchesSnapshot || pointsToMissing || isOurIconDir)
        iconKey.DeleteValue(null!);
}
```

配套新增私有 helper（放在 `GetIconPath` 附近的 `#region 文件关联` 内；`System.IO` 已在 `Assoc.cs:1` 引入）：

```csharp
/// <summary>从图标注册表值中提取文件路径：去掉引号与 ",0" / ",1" 索引后缀。</summary>
private static string TrimIconIndex(string rawValue)
{
    var value = rawValue.Trim();
    if (value.StartsWith('"'))
    {
        var end = value.IndexOf('"', 1);
        if (end > 1) return value.Substring(1, end - 1);
    }
    var comma = value.LastIndexOf(',');
    return comma > 0 ? value.Substring(0, comma) : value;
}
```

- [ ] **Step 4: 跑测试确认通过** → 全绿（含 `Uninstall_RemovesDefaultIcon` 与 `Uninstall_RemovesOrphanedDefaultIcon_PointingToTestsOutputDir`）。

验收（构造场景）：写入 `tests\MantisZip.Tests\bin\...\Resources\Icons\zip.ico` 形态的残留值 → 清理后该值消失（这是用户实际症状）；同时 `app bin\Resources\Icons\zip.ico`（安装时写入的现值）也必须被清掉。

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
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs`（Step 1 的 ext→开关映射 helper）
- Modify: `src/MantisZip.UI.Avalonia/App.axaml.cs:143-188`（非便携分支，调用自愈）
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`（Step 6b 持久化修正）
- Test: `tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`

**Interfaces:**
- Produces: `ShellIntegration.SelfHealAssociations(AppSettings s)`（`public static`）
- 依赖：需一个 ext→AppSettings 关联开关 的映射（见 Step 1）。

- [ ] **Step 1: 暴露 ext→开关映射（放 `ShellIntegration`，供自愈与设置页共用）**

```csharp
    /// <summary>该格式关联是否"未被用户显式禁用"。注意：默认值为 true（并不代表用户主动启用），false 只会由用户主动取消关联的动作产生——自愈判据把 false 视为"显式禁用，不得自愈"。</summary>
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
（`SettingsWindowViewModel.IsEnabledFromSettings`（:547-560）可后续改为调用此方法去重；本任务不强制。）

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

[Fact]
public void SelfHeal_DoesNotHeal_WhenNoRegistryTrace()
{
    // 无注册表痕迹 → 不自愈
    var s = new AppSettings { AssocZip = false };
    ShellIntegration.SelfHealAssociations(s);
    using var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip");
    Assert.Null(chk);
}

[Fact]
public void SelfHeal_DoesNotHeal_WhenExplicitlyDisabledAndProgIdMissing()
{
    // 显式禁用 + ProgId 缺失 → 不自愈
    try
    {
        using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            uc.SetValue("Progid", "MantisZip.Zip");
        var s = new AppSettings { AssocZip = false };
        ShellIntegration.SelfHealAssociations(s);
        using var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip");
        Assert.Null(chk);
    }
    finally { CleanupExtension(".zip"); }
}

[Fact]
public void SelfHeal_Rebuilds_TwoFormats()
{
    try
    {
        // 模拟损坏现场：.zip 与 .7z 的 UserChoice 都指向我们，但两个 ProgId 键都不存在
        using (var ucZip = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
            ucZip.SetValue("Progid", "MantisZip.Zip");
        using (var uc7z = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.7z\UserChoice"))
            uc7z.SetValue("Progid", "MantisZip.7z");
        using (var chkZip = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
            Assert.Null(chkZip);
        using (var chk7z = RegistryHost.Open(@"Software\Classes\MantisZip.7z"))
            Assert.Null(chk7z);

        ShellIntegration.SelfHealAssociations(new AppSettings());   // 默认各格式均启用

        // 硬约束：全格式统一——两个格式都必须被重建，不得只救 .zip
        using var afterZip = RegistryHost.Open(@"Software\Classes\MantisZip.Zip\shell\open\command");
        Assert.NotNull(afterZip?.GetValue(null));
        using var after7z = RegistryHost.Open(@"Software\Classes\MantisZip.7z\shell\open\command");
        Assert.NotNull(after7z?.GetValue(null));
    }
    finally
    {
        CleanupExtension(".zip");
        CleanupExtension(".7z");
    }
}

[Fact]
public void IsAssocEnabledInSettings_CoversSupportedExtensions()
{
    // 防未来新增格式导致自愈被静默关闭：ArchiveEngineFactory.SupportedExtensions 中每个扩展名
    // 都必须命中显式分支，不得落进 `_ => false` 默认分支。
    var s = new AppSettings
    {
        AssocZip = true, Assoc7z = true, AssocRar = true, AssocTar = true,
        AssocTarGz = true, AssocGz = true, AssocIso = true,
    };
    foreach (var ext in ArchiveEngineFactory.SupportedExtensions)   // [".zip",".7z",".rar",".tar",".tgz",".tar.gz",".gz",".iso"]
    {
        Assert.True(ShellIntegration.IsAssocEnabledInSettings(s, ext),
            $"{ext} 落入了 `_ => false` 默认分支：未来新增格式会让自愈被静默关闭");
    }

    // 反向哨兵：防上一条断言因实现恒真而假通过
    Assert.False(ShellIntegration.IsAssocEnabledInSettings(s, ".not-a-real-ext"));
    Assert.False(ShellIntegration.IsAssocEnabledInSettings(new AppSettings { AssocTarGz = false }, ".tgz"));
}
```

- [ ] **Step 3: 跑测试确认失败** → `--filter SelfHeal`，Expected: FAIL（方法不存在）。

- [ ] **Step 4: 实现 `SelfHealAssociations`**

```csharp
    /// <summary>
    /// 启动自愈：对每个内置扩展名，**仅当同时满足以下三条（AND）**时，才重建缺失的 per-format ProgId：
    ///   1) `UserChoice.Progid` 以 `MantisZip` 开头（注册表事实：用户曾把我们设为默认）；
    ///   2) `Software\Classes\{ProgId}` 键缺失（确实损坏）；
    ///   3) 该格式未被用户在设置里显式禁用（`IsAssocEnabledInSettings` 为 true）。
    /// 注意：**不是 OR**——第 1 条是必需的注册表事实，第 3 条只是"用户未显式禁用"的闸门，
    /// 单独满足"设置里启用"而无注册表痕迹时**不得**自愈（否则会凭空给从未关联过的格式写注册表）。
    /// 本三条 AND 即判据的权威口径，与代码内注释一致。
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

                // 触发判据（三条 AND）：
                //   1) UserChoice.Progid 以 MantisZip 开头（注册表痕迹）
                //   2) Software\Classes\{ProgId} 键缺失（确实损坏）
                //   3) 该格式未被用户在设置里显式禁用
                if (!userChoseUs) continue;                        // 无我们的痕迹 → 不自愈
                if (!IsAssocEnabledInSettings(s, ext)) continue;   // 显式禁用 → 不自愈

                bool progIdMissing;
                using (var k = RegistryHost.Open($@"Software\Classes\{progId}"))
                    progIdMissing = k == null;

                if (progIdMissing)
                {
                    App.DebugLog($"ShellIntegration.SelfHealAssociations: rebuilding missing ProgId for {ext} (userChoseUs={userChoseUs})");
                    InstallAssociationForExtension(ext);
                    // 自愈成功后注册应用
                    EnsureApplicationsRegistered();
                }
                // 额外加固：ProgId 键存在但 shell\open\command 指向失效路径时也可考虑重建（本计划先标记，后续如需再细化）
            }
            catch (Exception ex) { App.DebugLog($"ShellIntegration.SelfHealAssociations: failed for {ext}: {ex.Message}"); }
        }
    }
```

> **口径一致性（硬性）**：XML 注释、代码内 `触发判据（三条 AND）` 注释、`.swarm/context.md` 的权威判据必须是同一条**三条 AND**——① `UserChoice.Progid` 以 `MantisZip` 开头（注册表事实，必需）；② `Software\Classes\{ProgId}` 键缺失（确实损坏）；③ 该格式未被用户在设置里显式禁用（仅作闸门）。**不要**改回"只看注册表事实"或"注册表痕迹 OR 设置启用"（后者会让从未关联过的格式被凭空写入注册表，且与 `SelfHeal_DoesNotHeal_WhenExplicitlyDisabledAndProgIdMissing` 相矛盾）。

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
- [ ] **Step 6b: 同步修正 `UninstallAllAssoc` 与 `DeselectAllAssoc`（`SettingsWindowViewModel.cs:505-518、2057-2081`）**  
  `UninstallAllAssoc`、`DeselectAllAssoc` 执行时需**立即**把 `_settings.Assoc* = false` **持久化**（不等 Save 按钮）；`InstallSelectedAssoc` 对称置 true。
- [ ] **Step 7: Commit**

```bash
git add src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs src/MantisZip.UI.Avalonia/App.axaml.cs tests/MantisZip.Tests/ShellIntegrationAssocTests.cs src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs
git commit -m "feat(avalonia): 启动时自愈缺失的文件关联 ProgId，修复悬空默认（同步设置页持久化）"
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

### Task G: 统一 SHChangeNotify 刷新关联状态（新增）

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs`（新增 `NotifyAssocChanged()` helper）
- Modify: `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs`（安装/卸载/自愈/清理路径接线）

**依据**：SME 结论，置信度 **HIGH** —— 写完 `HKCU\Software\Classes` 关联键后必须调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero)`，否则 Explorer 因缓存不会立即刷新图标/"打开方式"列表。现状：该方法已 import（`ShellIntegration.cs:29-32`：`[DllImport("shell32.dll")]` 的 `private static extern void SHChangeNotify(...)` 在 :29-30，`SHCNE_ASSOCCHANGED` 常量在 :32），但未在所有关联写入路径调用。

- [ ] **Step 1: 新增 `NotifyAssocChanged()` helper（封装 SHChangeNotify）**
- [ ] **Step 2: 在 `InstallAssociationForExtension` 成功后调用 `NotifyAssocChanged()`**
- [ ] **Step 3: 在 `UninstallAssociationForExtension` 成功后调用 `NotifyAssocChanged()`**
- [ ] **Step 4: 在 `SelfHealAssociations` 重建后调用 `NotifyAssocChanged()`**
- [ ] **Step 5: 在 `CleanupOldProgId` 清理后调用 `NotifyAssocChanged()`**
- [ ] **Step 6: 枚举其余写 `HKCU\Software\Classes` 的调用点，未通知的补齐调用**（**不做**无界全仓扫描；按 Task A 已列出的调用清单逐个确认）

  1. `Services/ShellIntegration.cs`：`:97`（`GetDynamicMenuStatus`，**只读**→ 不需要）、`:112` / `:118`（`SetRegistryValue` / `SetRegistryDword` 内的 `CreateSubKey`）、`:124`（`DeleteRegistryKey`）—— helper 内不通知，由 `Install()` / `Uninstall()` 收口点各调一次 `NotifyAssocChanged()`。
  2. `Services/ShellIntegration.Assoc.cs`：`:17` / `:30` / `:102` 为**只读**查询、`:246`（`GetCurrentHandler`，其中 `:266` 读的是 `Registry.ClassesRoot` 下的 HKCR 键，与 HKCU 写入无关）→ **均不需要通知**；`:50` / `:173` / `:183` 已分别由 Step 2 / Step 3 覆盖；`:216`（`UninstallAssociations` 的自定义 ProgId / Applications 清理）需确认在收口点补通知。
  3. `Services/ShellIntegration.Menu.cs`：`:17` / `:22` / `:25` / `:28` / `:31` / `:255` / `:518` —— 全部落在 `Install()` / `Uninstall()` 路径内，由这两个收口点补一次 `NotifyAssocChanged()`。
  4. 清单之外的路径：先确认它确实写 `HKCU\Software\Classes`（或其子键），确认后再加通知；不机械铺开、不为了"扫全仓"而加无意义调用。

- [ ] **Step 7: 构建 + 跑测试 → 全绿。**
- [ ] **Step 8: Commit**

---

## 涉及文件

- 新增：`src/MantisZip.UI.Avalonia/Services/RegistryHost.cs`
- 修改：`Services/ShellIntegration.cs`、`Services/ShellIntegration.Assoc.cs`、`Services/ShellIntegration.Menu.cs`、`App.axaml.cs`、`ViewModels/SettingsWindowViewModel.cs`
- 测试：`tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`
- 安装包：`installer.iss`、`installer-selfcontained.iss`
- 文档：`docs/PROGRESS.md`、`docs/progress-avalonia-detail.md`、`docs/PLAN.md`

## 验证

> **本节即权威验收基线**：本项目无 `.swarm/spec.md`（本计划属**外部计划导入**形态），验收一律以本文件所列步骤为准。

1. **构建**：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 错误；`dotnet build src\MantisZip.Core\MantisZip.Core.csproj`（如 Core 未动则跳过）。
2. **测试**：`dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` 全绿。
3. **回归隔离（关键）**：测试前记录 `HKCU\Software\Classes\MantisZip.*` 与 `.zip\OpenWithProgids`，跑测试后再记录，**两次逐项对照一致**（值可能非空——已装关联的机器上 `MantisZip.Zip` 本就合法存在；关键是**不得新增键、不得发生值变化**）；`HKCU\Software\MantisZip\AssocTestSandbox` 被清理。
4. **端到端（修复已损坏的机器）**：
   - 先制造"悬空默认 + 图标残留"现场：**删除 `Software\Classes\MantisZip.Zip`（ProgId 键），保留 `UserChoice`**（删 `UserChoice` 会抹掉 Task E 唯一的验证前提）；同时确保 `Software\Classes\.zip\DefaultIcon` 处于"指向测试输出目录 ico"的残留形态（Task C 的清理对象）。
   - 启动新构建 → `.zip` 双击恢复用 MantisZip 打开（E 重建 ProgId 后 UserChoice 可解析）。
   - 设置页"安装关联"后"取消全部关联" → `DefaultIcon` 残留（含测试目录 ico）已清除、`OpenWithProgids` 旧值残留已清除；`UserChoice` 按 **best-effort** 处理（删除失败仅记日志，不作为验收硬指标）。
5. **卸载**：安装包安装 → 卸载 → `HKCU\Software\Classes\MantisZip.*` 全部消失；指向我们的 `UserChoice` **尽力清除**（失败仅记日志、不视为阻塞，由 Task E 自愈兜底）。
6. **.7z 回归组**：验证 `FileExts\.7z\UserChoice` 仍为 `MantisZip.7z`，且自愈不破坏其他格式。

## 边界

- **不**程序化写 `UserChoice`（哈希/UCPD 保护，写了会被系统清除）；E 只重建 ProgId。凡提及"删 UserChoice 以清除痕迹"一律改为"**尽力删除 + 失败记录 + 自愈兜底**"。
- 不改 `Core` 层；不改右键菜单（COM/cascade）逻辑。
- 仅 Windows（`Registry.CurrentUser`）；Avalonia-first（规则 11）。
- 不动版本号（规则 2，需用户明确要求才动）。
- Task E 的启动自愈在便携模式下**不执行**（与现有 `App.axaml.cs` 便携分支一致）。
- **可选、当前不做**：用 `AssocQueryString`/`IQueryAssociations` 做只读损坏检测（YAGNI）。

## 风险与限制

- **A 的机械替换面较广**（20 处 `Registry.CurrentUser` 调用：`ShellIntegration.cs` 97/112/118/124 + `ShellIntegration.Menu.cs` 17/22/25/28/31/255/518 + `ShellIntegration.Assoc.cs` 其余）：靠"路径字符串不变、只换方法名"降低风险；构建 + 测试 + 第 3 步回归隔离验证兜底。
- **UserChoice 删/读权限**：不同 Windows 版本对 `UserChoice` 的读取一般允许；删除整键一般允许，但个别加固环境可能拒绝 → 均已 try/catch 包裹，失败仅记日志，不影响主流程（best-effort）。
- **应用无法设默认**：这是平台限制，非本计划可解。若用户机器的 `UserChoice` 被 Windows 自身清空，E 无能为力，需在设置页提示"到 Windows 默认应用里重设"（**本计划不含此 UI 提示，记为后续项**）。
- **测试仍用真实注册表（沙箱子树）**：需保证 `Dispose` 一定清理；沿用现有 `[Collection("RegistryTests", DisableParallelization = true)]` 串行约束。
- **Task B 的 best-effort 删除会静默移除 Windows 默认关联**：与旧行为（关联悬空但键完好、重新启用即可复活）不同，把指向我们的 `UserChoice` 整键删掉后，Windows 会回退到内置/别家处理程序，用户需在系统"默认应用"里重新指定；这是**有意为之**（悬空默认比没有默认更糟），且重建关联（`--install-assoc` / 设置页）即可恢复。本轮**不新增 UI 提示文案**（避免三语本地化范围膨胀，规则 13），仅在文档中说明该语义，后续如需提示再单独评估。
- **串行实施约束**：Task A / B / C / D / E / G **都改 `ShellIntegration.Assoc.cs`**（共享同一批修改点），因此这些任务必须**按 A → B → C → D → E → G 串行执行**，不得并行修改同一文件；Task F（安装包）与其它文件互不相干，可独立进行。

## 执行流程

计划移入 `.omo/plans/进行中` → 逐任务 `coder→reviewer→test_engineer` → **每次提交前**更新 `docs/PROGRESS.md`（里程碑）+ `docs/progress-avalonia-detail.md`（细节）并同步 `docs/PLAN.md` → 完成后移入 `.omo/plans/已完成`，条目从 `PLAN.md` 移入 `PROGRESS.md` 的【历史设计方案索引】。提交信息用 conventional commits `<type>(<scope>): 中文描述`；shell 一律 PowerShell。

## 环境事实（2026-10-17）

- `ucpd` 服务 **RUNNING**；UCPD 只保护**枚举键清单**（默认浏览器 `UrlAssociations\{http,https}\UserChoice` + 默认 PDF + 任务栏/搜索栏/Widgets），`.zip` 与 `Explorer\FileExts\*` **不在**公开清单中；该清单随驱动版本演进，故**不依赖**它。
