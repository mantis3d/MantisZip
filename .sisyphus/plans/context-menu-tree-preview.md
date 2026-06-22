# 右键菜单目录结构预览

> **状态**: 📋 待实现 | **实现版本**: TBD

## TL;DR

> **Quick Summary**: 在 COM 右键菜单的 MantisZip 弹出子菜单中新增"📂 浏览内容"子菜单，展示压缩包顶层文件/目录结构，让用户无需打开软件即可一窥包内内容。
>
> **核心原则**：仅信息展示（禁用态菜单项）、仅读取顶层、严格性能上限（500ms / 100条目 / 30项）。
>
> **Deliverables**:
> - `ContextMenuHandler.cs` 修改：在 `QueryContextMenu` 中读取压缩包头并构建树形子菜单
> - `MantisZip.Core` 新增 `SyncListTopEntries` 同步方法（ShellExt 无 async 环境）
> - `AppSettings` 新增 `EnableContextMenuTreePreview` 开关（默认开启）
> - 设置界面添加对应开关
>
> **Estimated Effort**: Medium（6-8h）
> **Parallel Execution**: NO（顺序依赖）
> **Critical Path**: Core 新增方法 → ShellExt 树构建 → 设置集成

---

## 目录

- [右键菜单目录结构预览](#右键菜单目录结构预览)
  - [TL;DR](#tldr)
  - [1. 动机](#1-动机)
  - [2. 设计](#2-设计)
    - [2.1 菜单结构](#21-菜单结构)
    - [2.2 支持格式](#22-支持格式)
    - [2.3 参数限制](#23-参数限制)
    - [2.4 性能保障](#24-性能保障)
    - [2.5 错误降级](#25-错误降级)
    - [2.6 实例状态推测](#26-实例状态推测)
  - [3. 实现步骤](#3-实现步骤)
    - [Task 1: Core — `SyncListTopEntries` 方法](#task-1-core--synclisttopentries-方法)
    - [Task 2: ShellExt — 构建树子菜单](#task-2-shellext--构建树子菜单)
    - [Task 3: 设置集成](#task-3-设置集成)
  - [4. 依赖与影响](#4-依赖与影响)
  - [5. 配置项](#5-配置项)
  - [6. 备用方案 B（文档就绪，待触发）](#6-备用方案-b文档就绪待触发)
  - [7. 定义完成 (Definition of Done)](#7-定义完成-definition-of-done)
  - [A. 附录：跨平台影响](#a-附录跨平台影响)

---

## 1. 动机

当前右键菜单只能执行预设操作（解压/压缩），用户无法在不打开软件的情况下了解压缩包内部文件结构。Bandizip、WinRAR 等主流工具均已支持此功能。实现后：

- 右键 → 快速浏览包内内容，决定是否需要解压
- 减少不必要的软件启动
- 提升右键菜单的完整性与专业感

---

## 2. 设计

### 2.1 菜单结构

```
MantisZip
├── 打开压缩包 filename
├── ════════════════════          ← disabled text separator
├── 📂 浏览内容                   ← 子菜单（含 archive icon）
│   ├── 📁 Documents/  (12 项)    ← disabled, MFT_STRING
│   ├── 📄 readme.txt             ← disabled, MFT_STRING
│   ├── 📄 photo.jpg              ← disabled, MFT_STRING
│   ├── 📁 src/  (5 项)           ← disabled, MFT_STRING
│   └── 📄 ...还有 15 个项目       ← disabled, MFT_STRING
├── ════════════════════          ← disabled text separator
├── 原地解压包
├── 智能原地解压
├── 解压到目录 filename
├── 解压到……
├── ════════════════════
├── 压缩到 filename.zip
├── 压缩到 parentDir.zip
├── 压缩到……
```

关键设计决定：

| 决定 | 理由 |
|:----|:------|
| 独立子菜单"📂 浏览内容"而非混排 | 保持现有操作项结构不变；树内容本身是信息性而非操作性 |
| 所有树条目均 **禁用态**（`MFS_DISABLED \| MFS_GRAYED`） | Windows 标准约定：信息展示；用户看过后使用下方解压操作；避免增删命令 ID 映射复杂度 |
| 仅 **一层**（根目录文件+文件夹） | 减少 QueryContextMenu 阻塞时间；Windows 子菜单不支持懒加载 |
| 排序：文件夹在前，文件在后，各自按名称字母 | 符合文件管理器惯例 |

### 2.2 支持格式

| 格式 | 读取方式 | 性能 | 支持度 |
|:----|:---------|:----:|:------:|
| `.zip` | `SharpCompress.Archives.Zip.ZipArchive.Open()` | ✅ 快 | ✅ 完整 |
| `.tar` | `SharpCompress.Archives.Tar.TarArchive.Open()` | ✅ 快 | ✅ 完整 |
| `.tgz` / `.tar.gz` | `TarArchive.Open(gzipStream)` | ✅ 快 | ✅ 完整 |
| `.gz` | `TarArchive.Open()`（单文件无目录，显示文件名） | ✅ 快 | ✅ 基本 |
| `.7z` | `SharpCompress.Archives.SevenZip.SevenZipArchive.Open()` | ⚠️ 较慢 | ✅ 基本（无加密 / 非固实） |
| `.rar` | `SharpCompress.Archives.Rar.RarArchive.Open()` | ⚠️ 较慢 | ✅ 基本 |
| `.iso` | 不支持 | — | ❌ |

> 7z/RAR 使用 SharpCompress 自身的解析器（不依赖 7z.dll）。对于加密或固实压缩包，SharpCompress 可能无法列出条目——此时自动降级，跳过树预览。

格式检测：使用 `Path.GetExtension()` + 扩展名字典映射到对应的 `SharpCompress` Archive 类型。与 Core 的 `ArchiveEngineFactory.GetFormatByExtension()` 保持逻辑一致。

### 2.3 参数限制

```
MAX_ENTRIES_READ    = 100   // 最多读取 100 个条目
MAX_DISPLAY_ITEMS   = 30    // 最多显示 30 项（含文件夹计数行）
TIMEOUT_MS          = 500   // 读取超时（超过则跳过树预览）
MAX_FILE_SIZE_MB    = 200   // 超过此大小的压缩包跳过实时读取
```

- `MAX_ENTRIES_READ=100`：读取 100 条后停止进一步读取。对于绝大多数压缩包，100 条足以展示根目录内容。
- `MAX_DISPLAY_ITEMS=30`：根目录超过 30 项时，最后一项显示"📄 ...还有 N 个项目"（`N = rootCount - 29`）。
- `TIMEOUT_MS=500`：使用 `CancellationTokenSource(500ms)` + `Task.Run` + `Wait()` 模式。超时时树条目不添加，菜单按正常（无预览）显示。
- `MAX_FILE_SIZE_MB=200`：超过此大小直接跳过读取。通过 `new FileInfo(path).Length` 预检。

### 2.4 性能保障

ShellExt 运行在 Explorer.exe 进程内，QueryContextMenu 是同步调用。Explorer 对 COM 扩展有超时机制（通常 5-10 秒），但任何 > 500ms 的延迟都会给用户明显的卡顿感。

保障策略（逐级防护）：

```
QueryContextMenu 开始
  │
  ├─ 文件 > 200MB ? ────────── Yes → 跳过（不阻塞）
  │
  ├─ 扩展名不在支持列表 ? ───── Yes → 跳过
  │
  ├─ 设置 EnableContextMenuTreePreview = false ? ─── Yes → 跳过
  │
  ├─ 多文件选择 ? ──────────── Yes → 跳过（只对单文件压缩包展示树预览）
  │
  └─ 开始读取（SharpCompress Open + 遍历条目）
      │
      ├─ 超时 500ms ? ──────── Yes → 放弃，销毁部分构建的子菜单
      │
      └─ 完成 → 构建树子菜单
```

**多文件跳过**：用户选择了多个文件时，不展示树预览（哪个文件的目录？增加复杂度/延迟）。仅单文件压缩包展示树。

### 2.5 错误降级

| 场景 | 行为 |
|:----|:------|
| 格式不支持（.iso） | 直接跳过，不添加"浏览内容"子菜单 |
| 文件过大（>200MB） | 跳过 |
| 读取超时（>500ms） | 跳过，已创建的空子菜单销毁 |
| 读取异常（损坏/加密/无权限） | `catch` 记录日志，跳过 |
| 条目数 > 30 | 截断，显示"...还有 N 个项目" |
| 条目数 = 0 | 在子菜单中显示"（空压缩包）"（禁用态文字） |
| 多文件选择 | 跳过 |

### 2.6 实例状态推测

ContextMenuHandler 当前不支持在 Initialize 和 QueryContextMenu 之间传递格式名/归档路径之外的额外状态。每次 QueryContextMenu 调用时需要重新检测。

由于读取逻辑轻量（<500ms，纯托管代码），不做缓存。每次 QueryContextMenu 都实时读取。

---

## 3. 实现步骤

### Task 1: Core — `SyncListTopEntries` 方法

**文件**: `MantisZip.Core/Utils/ArchiveQuickLister.cs`（新建）

```csharp
namespace MantisZip.Core.Utils;

public static class ArchiveQuickLister
{
    /// <summary>
    /// 同步读取压缩包顶层条目。专为 ShellExt COM 组件设计（无 async 环境）。
    /// 返回排序后的条目列表：文件夹在前，文件在后。
    /// </summary>
    /// <param name="filePath">压缩包路径</param>
    /// <param name="maxEntries">最多读取条目数（默认 100）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>顶层条目元组列表（名称, 是否为文件夹, 大小字节）</returns>
    public static List<EntryInfo> ListTopEntries(
        string filePath,
        int maxEntries = 100,
        CancellationToken cancellationToken = default);
}

public readonly record struct EntryInfo(string Name, bool IsDirectory, long Size);
```

实现要点：

1. **格式检测**：通过扩展名映射到 SharpCompress 的 Archive 类型：
   - `.zip` → `ZipArchive.Open(path)`
   - `.tar` / `.tgz` / `.tar.gz` / `.gz` → `TarArchive.Open(path)`（GZip 用 `GZipStream` 包裹）
   - `.7z` → `SevenZipArchive.Open(path)`
   - `.rar` → `RarArchive.Open(path)`
   - 其他 → 返回空列表

2. **条目遍历**：
   - 遍历 `archive.Entries`，对每个条目提取 `Key`（相对路径）
   - 取路径的第一段作为顶层名称
   - 用 `HashSet<string>` 去重（同一文件夹下的多个文件共用一个文件夹名）
   - 前 `maxEntries` 条后停止

3. **排序**：
   - 文件夹在前（`IsDirectory = true`），按名称字母
   - 文件在后，按名称字母

4. **取消支持**：每次迭代检查 `cancellationToken`。ShellExt 传入 500ms 超时的 `CancellationToken`。

5. **压缩包关闭**：使用 `using` 确保 Archive 和底层 Stream 及时释放。

6. **异常安全**：内部 try-catch，任何异常返回空列表。

#### 为什么不用 `IArchiveEngine.ListEntriesAsync`？

- ShellExt 运行在 Explorer 进程内，**无法使用 async/await**（QueryContextMenu 是同步 COM 方法）
- `IArchiveEngine` 涉及 `ArchiveEntryExtractor`、密码管理等 ShellExt 不需要的复杂性
- `SyncListTopEntries` 是纯 SharpCompress 操作，轻量、可控、可取消

### Task 2: ShellExt — 构建树子菜单

**文件**: `src/MantisZip.ShellExt/ContextMenuHandler.cs`

修改范围：

**2a. 常量追加**

新增命令 ID 和图标资源名：

```csharp
private const int CmdIdTreePreview = 8;   // 仅用于 "浏览内容" 子菜单占位符
private const int MaxTreeEntriesRead = 100;
private const int MaxTreeDisplayItems = 30;
private const int TreeTimeoutMs = 500;
private const long MaxFileSizeForTree = 200L * 1024 * 1024; // 200 MB

// 新增图标缓存
private static IntPtr _cachedIconTreePreview = IntPtr.Zero;  // Browse.ico（复用 Open.ico 或新增)
```

资源映射表新增：

```csharp
["Browse.ico"] = "MantisZip.ShellExt.Resources.Browse.ico",
```

**2b. 构建树方法**

```csharp
/// <summary>
/// 构建"浏览内容"子菜单。失败或超时时返回空 handle。
/// </summary>
private IntPtr BuildTreeSubmenu(string archivePath, ref uint popupIndex, ref uint idCmd)
{
    IntPtr treeMenu = NativeMethods.CreatePopupMenu();
    uint treeIdx = 0;

    try
    {
        // 1. 文件大小预检
        var fi = new FileInfo(archivePath);
        if (!fi.Exists || fi.Length > MaxFileSizeForTree)
        {
            DestroyAndReturnNull(treeMenu);
            return IntPtr.Zero;
        }

        // 2. 读取条目（带超时）
        var cts = new CancellationTokenSource(TreeTimeoutMs);
        var entries = ArchiveQuickLister.ListTopEntries(archivePath, MaxTreeEntriesRead, cts.Token);

        // 3. 空压缩包处理
        if (entries.Count == 0)
        {
            InsertDisabledItem(treeMenu, treeIdx++, "（空压缩包）");
            return treeMenu;  // 仍有子菜单，只是显示"空"
        }

        // 4. 添加条目（最多 MAX_DISPLAY_ITEMS 条）
        int displayCount = Math.Min(entries.Count, MaxTreeDisplayItems);
        for (int i = 0; i < displayCount; i++)
        {
            var entry = entries[i];
            string text = entry.IsDirectory
                ? $"📁 {entry.Name}/  ({FormatSize(entry.Size)})"
                : $"📄 {entry.Name}  ({FormatSize(entry.Size)})";
            InsertDisabledItem(treeMenu, treeIdx++, text);
        }

        // 5. 溢出提示
        if (entries.Count > MaxTreeDisplayItems)
        {
            int remaining = entries.Count - MaxTreeDisplayItems;
            InsertDisabledItem(treeMenu, treeIdx++, $"📄 ...还有 {remaining} 个项目");
        }

        return treeMenu;
    }
    catch (Exception ex)
    {
        ShellExtLog.Error("BuildTreeSubmenu exception", ex);
        DestroyAndReturnNull(treeMenu);
        return IntPtr.Zero;
    }
}
```

**辅助方法**：

```csharp
private void InsertDisabledItem(IntPtr hMenu, uint position, string text)
{
    var mii = new MenuItemInfo
    {
        cbSize = Marshal.SizeOf<MenuItemInfo>(),
        fMask = NativeMethods.MIIM_STRING | NativeMethods.MIIM_FTYPE | NativeMethods.MIIM_STATE,
        fType = NativeMethods.MFT_STRING,
        fState = NativeMethods.MFS_DISABLED | NativeMethods.MFS_GRAYED,
        wID = 0,  // 无命令 ID
        dwTypeData = Marshal.StringToCoTaskMemUni(text),
        cch = (uint)text.Length,
    };
    NativeMethods.InsertMenuItem(hMenu, position, true, ref mii);
    Marshal.FreeCoTaskMem(mii.dwTypeData);
}
```

**2c. QueryContextMenu 修改**

在 extract 组和 compress 组之间插入树预览：

```csharp
// ─── 树预览组 ───
if (singleFileArchive && _enableTreePreview)
{
    IntPtr treeMenu = BuildTreeSubmenu(archivePath, ref popupIndex, ref idCmd);
    if (treeMenu != IntPtr.Zero)
    {
        // 添加 "浏览内容" 子菜单
        InsertMenuItem(popupMenu, popupIndex++, idCmd++, "浏览内容",
            CmdIdTreePreview, showIcon: true, hSubMenu: treeMenu);
        _cmdIdOrder.Add(CmdIdTreePreview);
    }
}
```

`singleFileArchive` 检测：当 `_selectedFiles.Count == 1` 且文件扩展名为支持格式时。

**2d. 图标新增**

- `Browse.ico`（复用现有 `Open.ico` 或从 UI 资源目录新增）
- `EnsureIconsPreloaded` 中预加载
- `GetOrLoadIcon("Browse.ico", ref _cachedIconTreePreview)`

### Task 3: 设置集成

**3a. `AppSettings` 新增**

```csharp
/// <summary>右键菜单显示压缩包目录结构预览</summary>
public bool EnableContextMenuTreePreview { get; set; } = true;
```

**3b. 注册表同步**

`ShellIntegration.WriteSettingsToRegistry()` 新增写入：

```csharp
WriteRegistryBool(@"ContextMenu\EnableTreePreview", settings.EnableContextMenuTreePreview);
```

**3c. ShellExt 读取**

`LoadSettingsFromRegistry()` 新增：

```csharp
_enableTreePreview = ReadRegistryBool(@"ContextMenu\EnableTreePreview", true);
```

**3d. 设置 UI**

`SettingsWindow` 的右键菜单相关区域追加复选框：

```
[✔] 右键菜单显示压缩包目录结构预览
```

---

## 4. 依赖与影响

| 项目 | 依赖 |
|:-----|:------|
| Core | 无新增 NuGet（SharpCompress 已引用）|
| ShellExt | Core 项目引用已有（间接获得 SharpCompress）|
| UI | 无新增依赖 |

**不需要**：
- 新增 NuGet 包（SharpCompress 已通过 Core 传递引用）
- 修改 ShellExt 的 COM 注册
- 修改 .csproj 或构建配置

**运行时无额外分发文件**：SharpCompress 是纯托管库，已打包在 MantisZip.Core.dll 中。

---

## 5. 配置项

| 键 | 类型 | 默认值 | 说明 |
|:---|:----:|:------:|:-----|
| `EnableContextMenuTreePreview` | bool | `true` | 全局启用/禁用 |

---

## 6. 备用方案 B（文档就绪，待触发）

若 Plan A 上线后用户反馈希望看到**更多层级**或**可操作条目**，按此方案升级：

### B1. 多级展开

- 文件夹条目改为真实子菜单（`MIIM_SUBMENU`），显示该文件夹下的直接子项
- 限制总条目数仍为 100，递归深度不超过 **2 层**（根 → 文件夹 → 子文件夹展开）
- 每层独立应用 `MAX_DISPLAY_ITEMS` 限制

### B2. 可操作条目

- 文件条目改为 **启用态**（`MFS_ENABLED`），并分配命令 ID
- 点击文件时：启动 `MantisZip.UI.exe --open <archive> --extract-entry <entryKey>`（暂不实现，仅为方案预留）
- 需要解决：ShellExt 需将条目 Key 映射到命令 ID，`InvokeCommand` 中根据 ID 还原 Key

### B3. 风险与成本

| 项目 | B1 | B2 |
|:----|:--:|:--:|
| 额外实现工时 | +3-4h | +6-8h |
| QueryContextMenu 阻塞风险 | 🟡 中（递归构建） | 🟢 低（无额外读取） |
| 命令 ID 溢出风险 | 🟢 低 | 🟡 中（30项 × N 命令） |

---

## 7. 定义完成 (Definition of Done)

- [ ] `ArchiveQuickLister.ListTopEntries` 在 Core 层实现并单元测试覆盖（正常/空/损坏/加密/超时）
- [ ] ShellExt 右键菜单在单文件 ZIP/TAR/GZ 压缩包上正确显示树预览子菜单
- [ ] 文件夹前缀 `📁`、文件前缀 `📄`、大小格式化正确
- [ ] 条目数 > 30 时正确截断并显示溢出提示
- [ ] 空压缩包显示"（空压缩包）"
- [ ] 不支持的格式（.iso）不显示树预览
- [ ] 多文件选择时不显示树预览
- [ ] 超时/异常时正常降级，不影响其他菜单项
- [ ] 树预览条目为禁用态，不可点击
- [ ] 设置开关 `EnableContextMenuTreePreview` 正确控制显隐（注册表读写两端对齐）
- [ ] 大文件（>200MB）跳过
- [ ] 非压缩包文件（右键普通文件）不触发

---

## A. 附录：跨平台影响

本功能深度依赖 Windows COM（`IContextMenu` / `IShellExtInit`）、Win32 HMENU 和注册表，属于 **🔴 冲突** 等级。

| 层级 | 跨平台影响 |
|:-----|:-----------|
| Core | `ArchiveQuickLister` 是纯 C# + SharpCompress，**无需修改**即可跨平台 |
| ShellExt | 废弃。Linux 改用 `.desktop` actions，macOS 改用 `NSExtension` |
| 设置 | `EnableContextMenuTreePreview` 在非 Windows 平台无意义，UI 中置灰或隐藏 |
