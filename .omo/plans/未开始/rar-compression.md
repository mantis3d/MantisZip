# RAR 压缩（外置 rar.exe / WinRAR.exe）

> **状态**: 📋 待定（已修订 2026-07-08）| **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/10)
> **前置依赖**: 无
>
> **修订说明**：补充了 SevenZipEngine 注册冲突处理、RarCompressionMethod 映射修正（缺 -m0）、CompressRequest/BuildOptions 数据链、DynamicFormatOptionsPanel 集成、取消清理、CLI 守卫等。

## TL;DR

> 利用用户已安装的 WinRAR 的 `rar.exe`（或 `WinRAR.exe a -afrar`）实现 RAR 格式压缩，补齐 RAR 只读解压的短板。包含 rar.exe 自动检测、进度解析、RAR 特有选项（固实/恢复记录/加密等）。
>
> **交付内容**: RarEngine.cs + RarDetector.cs + 格式下拉 + RAR 特有 UI + 设置项
> **估算工时**: 8–10h | **难度**: 🟡中 | **并行**: 部分可并行

---

## Context

### 现状

- RAR 解压已支持（通过 SharpSevenZip / 7z.dll）
- RAR 压缩一直空缺（格式下拉只有 ZIP/7z/TAR.GZ）
- 用户装了 WinRAR 却无法用它压缩

### 用户决策

| 决策项 | 选择 |
|--------|------|
| rar.exe 来源 | 两者都支持：`rar.exe` 优先，找不到则尝试 `WinRAR.exe a -afrar` |
| 格式显示策略 | 始终显示 RAR，找不到 rar.exe 时置灰并提示 |
| 功能范围 | 完整版：固实、恢复记录、分卷、加密均支持（注释暂不支持） |

### 技术要点

- `Process` + `ProcessStartInfo` 启动外部进程
- stdout 正则 `(\d+)%` 解析进度上报
- Command-line 构建要转义路径（路径含空格）
- `ArchiveFormat.Rar` 已存在枚举值
- `IArchiveEngine` 接口已完整
- ⚠ **关键**：`SevenZipEngine.CanHandle` 当前返回 `true` 对 `ArchiveFormat.Rar`，需要移除以避免注册冲突

---

## Work Objectives

### Core Objective
在 MantisZip 中实现 RAR 格式压缩，使用用户已安装的 rar.exe/WinRAR.exe 作为后端。

### Concrete Deliverables
1. `Core/Utils/RarDetector.cs` — rar.exe 路径检测工具
2. `Core/Abstractions/ArchiveEngine.cs` — `ArchiveOptions` 扩展（RarSolid/RarRecoveryRecord/RarCompressionMethod）
3. `Core/Models/CompressRequest.cs` — `CompressRequest` RAR 属性扩展 + `CompressService.BuildOptions` 映射
4. `Core/Engines/RarEngine.cs` — RAR 压缩引擎（CompressAsync + 委托解压）
5. `Core/Engines/SevenZipEngine.cs` — 移除 `CanHandle(Rar)` 避免冲突
6. `AppSettings.cs` — RarExePath + RAR 默认选项
7. `CompressSettingsWindow` — 格式下拉增加 RAR + `DynamicFormatOptionsPanel` RAR 选项
8. `App.Compress.cs` — Quick Compress CLI 守卫
9. i18n 字符串（中/英）
10. 端到端验证

### Must Have
- `rar a` 压缩命令构建正确，能生成有效 .rar 文件
- 压缩进度实时上报到 ProgressWindow
- 密码加密、分卷、压缩级别正常工作
- rar.exe 找不到时 UI 给出明确提示
- RarEngine + SevenZipEngine 注册无冲突（RarEngine 全权负责 RAR 格式，解压委托给 SevenZipEngine）

### Must NOT Have
- 不替换现有 SharpSevenZip 的 RAR 解压能力（解压委托给 SevenZipEngine 内部执行）
- 不实现 RAR 压缩包添加/删除条目（`CanAdd = false`, `CanDelete = false`）
- 不支持 RAR 压缩中的多线程核心数等高级调优参数
- 不支持 RAR 注释（`rar a -c` 等参数跳过）

---

## 关键设计决策

### SevenZipEngine 注册冲突处理

当前 `SevenZipEngine.CanHandle`:
```csharp
public bool CanHandle(ArchiveFormat format) =>
    format is ArchiveFormat.SevenZip or ArchiveFormat.Rar or ArchiveFormat.Iso;
```

`ArchiveEngineFactory` 使用 `_engines.FirstOrDefault(e => e.CanHandle(format))` 查找。如果 RarEngine 也注册 RAR，两者会冲突。

**方案**：
1. SevenZipEngine.CanHandle 去掉 `ArchiveFormat.Rar`（保留 SevenZip 和 Iso）
2. RarEngine.CanHandle 返回 `format == ArchiveFormat.Rar`
3. RarEngine 全权处理 ArchiveFormat.Rar 的所有操作入口
4. RarEngine.ExtractAsync / ListEntriesAsync / ExtractEntriesAsync / TestArchiveAsync 内部通过 `ArchiveEngineFactory.GetEngine(ArchiveFormat.SevenZip)` 获取 SevenZipEngine 并委托，间接复用 SharpSevenZip 的解压能力
5. `ArchiveEngineFactory` 静态构造器中 `_engines.Add(new RarEngine())` 在 SevenZipEngine 之后注册（顺序无关紧要，因为 CanHandle 不再冲突）

```csharp
// SevenZipEngine 修改后：
public bool CanHandle(ArchiveFormat format) =>
    format is ArchiveFormat.SevenZip or ArchiveFormat.Iso;
```

```csharp
// RarEngine 委托模式：
public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password, CancellationToken ct)
{
    var sevenZip = ArchiveEngineFactory.GetEngine(ArchiveFormat.SevenZip)
        ?? throw new NotSupportedException("SevenZipEngine 不可用，无法列出 RAR 条目");
    return await sevenZip.ListEntriesAsync(archivePath, password, ct);
}
```

### RarCompressionMethod 映射修正

rar.exe 支持的压缩级别（`-m` 参数）：

| 枚举值 | rar.exe 参数 | 说明 | CompressionLevel 映射（0-9） |
|--------|-------------|------|---------------------------|
| Store | `-m0` | 不压缩 | 0 |
| Fastest | `-m1` | 最快 | 1-2 |
| Fast | `-m2` | 快 | 3 |
| Normal | `-m3` | 普通（默认） | 4-5 |
| Good | `-m4` | 较好 | 6-7 |
| Best | `-m5` | 最好 | 8-9 |

计划原始映射错误：缺少 `-m0`（Store），且 Good/Best 都映射到 `-m5`（重复）。

### 属性命名惯例

遵循现有格式前缀惯例（`SevenZipSolid`、`ZipCompressionMethod`）：

- ✅ `RarSolid`（非 `Solid`）
- ✅ `RarRecoveryRecord`（非 `RecoveryRecord`）
- ✅ `RarCompressionMethod`（非 `RarCompressionMethod` 已正确）

### CompressRequest → BuildOptions → ArchiveOptions 数据链

| 层 | 修改项 |
|----|--------|
| `CompressRequest` | 新增 `RarSolid`、`RarRecoveryRecord`、`RarCompressionMethod` |
| `BuildOptions()` | 从 `request` 映射到 `ArchiveOptions` 的对应属性 |
| `ArchiveOptions` | 新增 `RarSolid`、`RarRecoveryRecord`、`RarCompressionMethod` |
| `RarEngine.CompressAsync` | 读取 `options.RarSolid`、`options.RarRecoveryRecord`、`options.RarCompressionMethod` 构建命令参数 |

### DynamicFormatOptionsPanel 集成

已有 `Controls/DynamicFormatOptionsPanel.xaml/.cs`，`SelectedFormat` 为 `"rar"` 时显示 RAR 专有选项面板。RAR 选项应加入此面板而非创建独立面板。

---

## Task 清单

### Wave 1 (基础 + 检测)

- [ ] **1. RarDetector.cs — rar.exe/WinRAR.exe 路径检测**

  **Files**: `src/MantisZip.Core/Utils/RarDetector.cs`

  **What to do**:
  - 在 `Core/Utils/RarDetector.cs` 创建静态检测类
  - 检测顺序：`AppSettings.RarExePath`（自定义路径）→ `PATH` 环境变量 → `%ProgramFiles%\WinRAR\rar.exe` → `%ProgramFiles(x86)%\WinRAR\rar.exe`
  - 找不到 `rar.exe` 时尝试同一目录下的 `WinRAR.exe`（通过 `WinRAR.exe a -afrar` 参数调用）
  - 结果缓存在静态字段，每次压缩时重新检测
  - 返回 `(string? exePath, bool useWinRarExe)` 元组
  - 提供 `bool IsAvailable()` 供 UI 查询

  **Must NOT do**:
  - 不要修改注册表来检测 WinRAR 安装路径
  - 不要自动下载或安装任何东西

  **Parallelization**: YES (Wave 1, with tasks 2, 3)
  **Blocks**: Task 4, 7, 8
  **Blocked By**: None

  **Commit**: YES — `feat(core): add RarDetector for rar.exe/WinRAR.exe path detection`

- [ ] **2. ArchiveOptions RAR 扩展 + AppSettings RAR 选项 + CompressRequest 扩展**

  **Files**:
  - `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — ArchiveOptions
  - `src/MantisZip.UI/AppSettings.cs`
  - `src/MantisZip.Core/Services/CompressService.cs` — CompressRequest + BuildOptions

  **What to do**:

  `ArchiveOptions` 新增属性：
  ```csharp
  /// <summary>RAR 固实压缩（-s 参数）。默认 true。</summary>
  public bool RarSolid { get; set; } = true;

  /// <summary>RAR 恢复记录百分比（-rr{N}% 参数）。0=不添加，默认 0。</summary>
  public int RarRecoveryRecord { get; set; } = 0;

  /// <summary>RAR 压缩方式。默认 Normal。</summary>
  public string RarCompressionMethod { get; set; } = "normal";
  ```

  `RarCompressionMethod` 枚举：
  ```csharp
  public enum RarCompressionLevel
  {
      Store,    // -m0
      Fastest,  // -m1
      Fast,     // -m2
      Normal,   // -m3
      Good,     // -m4
      Best      // -m5
  }
  ```

  `AppSettings` 新增（高级区块）：
  ```csharp
  public string RarExePath { get; set; } = "";
  public bool RarSolid { get; set; } = true;
  public int RarRecoveryRecord { get; set; } = 0;
  public string RarCompressionMethod { get; set; } = "normal";
  ```

  `CompressRequest` 新增：
  ```csharp
  public bool RarSolid { get; init; } = true;
  public int RarRecoveryRecord { get; init; }
  public string RarCompressionMethod { get; init; } = "normal";
  ```

  `BuildOptions` 添加映射：
  ```csharp
  RarSolid = request.RarSolid,
  RarRecoveryRecord = request.RarRecoveryRecord,
  RarCompressionMethod = request.RarCompressionMethod,
  ```

  **Parallelization**: YES (Wave 1, with tasks 1, 3)
  **Blocks**: Task 4, 7, 8
  **Blocked By**: None

  **Commit**: YES — `feat(core+ui): add RAR-specific options, CompressRequest, and AppSettings`

- [ ] **3. RAR 相关 i18n 字符串**

  **Files**: `src/MantisZip.UI/Localization/strings.zh.json`, `src/MantisZip.UI/Localization/strings.en.json`, `src/MantisZip.UI/Localization/L.cs`

  **What to do**:
  - 新增中/英 JSON 条目：
    - `Rar_FormatName` = "RAR (.rar)"
    - `Rar_Solid` = "固实压缩"
    - `Rar_RecoveryRecord` = "恢复记录 (%)"
    - `Rar_CompressionMethod` = "压缩方式"
    - `Rar_NotAvailable` = "未找到 rar.exe/WinRAR.exe，RAR 压缩不可用。请安装 WinRAR 或在设置中指定 rar.exe 路径。"
    - `Rar_NotAvailableTitle` = "RAR 压缩不可用"
    - `Rar_PathLabel` = "rar.exe 路径"
    - `Rar_DetectButton` = "检测"
    - `Rar_DetectSuccess` = "已找到 rar.exe: {0}"
    - `Rar_DetectFailed` = "未找到 rar.exe"
  - 在 `L.cs` 中添加对应静态 key 常量

  **Parallelization**: YES (Wave 1, with tasks 1, 2)
  **Blocks**: Task 7, 8
  **Blocked By**: None

  **Commit**: YES — `feat(ui): add RAR compression i18n strings`

### Wave 2 (引擎实现)

- [ ] **4. RarEngine.cs — CompressAsync 核心**

  **Files**: `src/MantisZip.Core/Engines/RarEngine.cs`

  **What to do**:
  - 实现 `IArchiveEngine`
  - `CanHandle` → `format == ArchiveFormat.Rar`
  - `CanAdd`/`CanDelete` → `false`

  **CompressAsync** 核心流程：
  1. `RarDetector.Detect()` 获取 rar.exe 路径
  2. 构建 `rar a` 命令：
     ```
     {rarExePath} a -ep1 -m{compression} [-p{password}] [-v{size}b] [-s] [-rr{N}%] -idp -o+ "{outputPath}" "{sourcePath1}" "{sourcePath2}"
     ```
     - `-ep1` = 从路径中排除基础目录
     - `-m0`~`-m5` = 压缩级别（从 options.RarCompressionMethod 映射）
     - `-p{password}` = 加密（options.Encrypt 且 options.Password 不为空时）
     - `-v{size}b` = 分卷（options.SplitSize > 0 时）
     - `-s` = 固实（options.RarSolid 为 true 时）
     - `-rr{N}%` = 恢复记录（options.RarRecoveryRecord > 0 时）
     - `-idp` = 不显示百分比（通过 stdout 自己解析进度）
     - `-o+` = 覆盖已有文件
  3. `Process` 启动，重定向 stdout
  4. 正则 `(\d+)%` 解析进度 → `ArchiveProgress.PercentComplete`
  5. `CancellationToken` → `Process.Kill()` + **删除部分输出文件**
  6. 退出码 0=成功，非0=抛出异常

  **CompressionLevel 到 -m 参数的映射**（RarCompressionMethod 为 "" 或 null 时使用此映射）：
  ```
  0 → -m0 (Store)
  1-2 → -m1 (Fastest)
  3 → -m2 (Fast)
  4-5 → -m3 (Normal, 默认)
  6-7 → -m4 (Good)
  8-9 → -m5 (Best)
  ```
  如果 `options.RarCompressionMethod` 有值（来自 UI 选择），优先使用它。

  **Process 资源管理**：
  ```csharp
  using var process = new Process();
  // 配置 StartInfo
  try { process.Start(); } catch { ... }
  try { await process.WaitForExitAsync(ct); } catch (OperationCanceledException) {
      // 取消时终止进程 + 清理部分文件
      try { process.Kill(entireProcessTree: true); } catch { }
      try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
      throw;
  }
  ```

  **Parallelization**: NO (depends on Wave 1)
  **Blocks**: Task 5
  **Blocked By**: Tasks 1, 2

  **Commit**: YES — `feat(core): implement RarEngine.CompressAsync via rar.exe`

- [ ] **5. RarEngine.cs — 其余接口实现 + SevenZipEngine 修改**

  **Files**:
  - `src/MantisZip.Core/Engines/RarEngine.cs`
  - `src/MantisZip.Core/Engines/SevenZipEngine.cs`

  **What to do**:

  `RarEngine` 委托方法（所有解压/列表/测试均委托给 SevenZipEngine）：
  ```csharp
  public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(
      string archivePath, string? password = null, CancellationToken ct = default)
  {
      var sevenZip = ArchiveEngineFactory.GetEngine(ArchiveFormat.SevenZip)
          ?? throw new NotSupportedException("缺少 7z 引擎，无法读取 RAR 压缩包");
      return await sevenZip.ListEntriesAsync(archivePath, password, ct);
  }

  public async Task<ExtractResult> ExtractAsync(
      string archivePath, string destinationPath, string? password = null,
      IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default,
      ArchiveOptions? options = null)
  {
      var sevenZip = ArchiveEngineFactory.GetEngine(ArchiveFormat.SevenZip)
          ?? throw new NotSupportedException("缺少 7z 引擎，无法解压 RAR 压缩包");
      return await sevenZip.ExtractAsync(archivePath, destinationPath, password, progress, ct, options);
  }

  public async Task ExtractEntriesAsync(
      string archivePath, IReadOnlyList<string> entryKeys, string destinationPath,
      string? password = null, IProgress<ArchiveProgress>? progress = null,
      CancellationToken ct = default, ArchiveOptions? options = null)
  {
      var sevenZip = ...;
      await sevenZip.ExtractEntriesAsync(archivePath, entryKeys, destinationPath, password, progress, ct, options);
  }

  public async Task<bool> TestArchiveAsync(
      string archivePath, string? password = null,
      IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default)
  {
      // RAR 测试也可以用 rar t 命令：
      //   var result = await RunRarProcessAsync($"t -idp \"{archivePath}\"", ct: ct);
      //   return result.ExitCode == 0;
      // 但为了简单一致，委托给 SevenZipEngine：
      var sevenZip = ...;
      return await sevenZip.TestArchiveAsync(archivePath, password, progress, ct);
  }
  ```

  `SevenZipEngine.CanHandle` 修改：
  ```csharp
  // 修改前：
  public bool CanHandle(ArchiveFormat format) =>
      format is ArchiveFormat.SevenZip or ArchiveFormat.Rar or ArchiveFormat.Iso;

  // 修改后（移除 Rar）：
  public bool CanHandle(ArchiveFormat format) =>
      format is ArchiveFormat.SevenZip or ArchiveFormat.Iso;
  ```

  `ArchiveEngineFactory` 静态构造器注册 RarEngine：
  ```csharp
  _engines.Add(new MantisZip.Core.Engines.ZipEngine());
  _engines.Add(new MantisZip.Core.Engines.SevenZipEngine());
  _engines.Add(new MantisZip.Core.Engines.TarGzEngine());
  _engines.Add(new MantisZip.Core.Engines.RarEngine());  // 新增
  ```

  **Parallelization**: NO
  **Blocks**: Task 10
  **Blocked By**: Task 4

  **Commit**: YES (groups with 4) — `feat(core): implement RarEngine delegates and fix SevenZipEngine registration conflict`

### Wave 3 (UI 集成)

- [ ] **6. DynamicFormatOptionsPanel — RAR 选项面板**

  **Files**: `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml`, `src/MantisZip.UI/Controls/DynamicFormatOptionsPanel.xaml.cs`

  **What to do**:
  - 在 XAML 中添加 RAR 选项面板（`SelectedFormat` 为 `"rar"` 时可见，其他格式时隐藏）
  - 与现有 ZIP/7z 选项面板使用相同显隐逻辑（`Visibility` 绑定或 code-behind 切换）
  - RAR 选项包含：
    - 固实压缩 CheckBox（绑定 `RarSolid`）
    - 恢复记录数字输入框（`0`-`100`，带单位 `%`，绑定 `RarRecoveryRecord`）
    - 压缩方式 ComboBox（`Store` / `Fastest` / `Fast` / `Normal` / `Good` / `Best`，绑定 `RarCompressionMethod`）
  - 实现 `LoadDefaults()` / `SaveDefaults()` 方法（从/向 AppSettings 加载/保存 RAR 选项）
  - 属性暴露给 CompressSettingsWindow 读取：
    ```csharp
    public bool RarSolid { get; set; } = true;
    public int RarRecoveryRecord { get; set; }
    public string RarCompressionMethod { get; set; } = "normal";
    ```

- [ ] **7. CompressSettingsWindow — RAR 格式下拉 + CompressButton 中传递 RAR 选项**

  **Files**:
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml`
  - `src/MantisZip.UI/Dialogs/CompressSettingsWindow.xaml.cs`

  **What to do**:
  - 在 `FormatComboBox` 新增 `<ComboBoxItem Content="RAR (.rar)" Tag="rar"/>`
  - 格式选中事件中检查 `RarDetector.IsAvailable()`：
    - 不可用时弹提示 `L.T(L.Rar_NotAvailable)` 并切回上一个格式
  - `CompressButton_Click` 中传递 RAR 选项给 `CompressRequest`：
    ```csharp
    RarSolid = FormatOptionsPanel.RarSolid,
    RarRecoveryRecord = FormatOptionsPanel.RarRecoveryRecord,
    RarCompressionMethod = FormatOptionsPanel.RarCompressionMethod,
    ```
  - 从 `AppSettings.DefaultFormat` 加载时 RAR 不可用则自动切回 ZIP
  - `LoadDefaultsFromSettings()` 中 `FormatOptionsPanel.LoadDefaults()` 已由 DynamicFormatOptionsPanel 统一处理

  **Parallelization**: NO
  **Blocked By**: Tasks 1, 3, 6

  **Commit**: YES — `feat(ui): add RAR format dropdown and options to CompressSettingsWindow`

- [ ] **8. SettingsWindow — RarExePath 配置 + 格式下拉 RAR 选项**

  **Files**: `src/MantisZip.UI/Dialogs/SettingsWindow.xaml`, `src/MantisZip.UI/Dialogs/SettingsWindow.xaml.cs`

  **What to do**:
  - 高级标签页增加 rar.exe 路径配置行（路径 TextBox + 浏览按钮 + "检测"按钮）
  - 遵循现有 7z.dll 路径配置的样式和布局（`_sevenZipPath` 模式）
  - "检测"按钮调用 `RarDetector.Detect()` 并显示结果
  - 默认格式下拉（`DefaultFormatCombo`）新增 `<ComboBoxItem Content="RAR (.rar)" Tag="rar"/>`
  - `LoadSettings()` / `SaveSettings()` 处理 RAR 默认值（`RarSolid`、`RarRecoveryRecord`、`RarCompressionMethod`）

  **Parallelization**: NO
  **Blocked By**: Tasks 1, 2, 3

  **Commit**: YES — `feat(ui): add RarExePath setting and default format option`

- [ ] **9. App.Compress.cs — Quick Compress CLI 守卫**

  **Files**: `src/MantisZip.UI/AppPartials/App.Compress.cs`

  **What to do**:
  - `HandleCompressQuick` 中如果格式为 RAR：
    - 先检查 `RarDetector.IsAvailable()`
    - 不可用时提示用户并优雅降级到 ZIP（或报错退出）
  - `App.CreateCompressOptions()` 中填充 `RarSolid`、`RarRecoveryRecord`、`RarCompressionMethod` 默认值（从 `AppSettings` 读取）

  **Parallelization**: NO
  **Blocked By**: Tasks 1, 2

  **Commit**: YES (groups with 7 or 8)

### Wave 4 (验证)

- [ ] **10. 端到端测试 + 边界情况修复**

  **What to do**:
  - 完整流程测试：
    1. 打开 CompressSettingsWindow → 选 RAR → 压缩
    2. ProgressWindow 实时显示进度
    3. 产物可用 SharpSevenZip 打开（验证委托解压路径正常）
    4. 密码加密 → 解压时弹密码框
    5. 分卷 → 生成 `.part1.rar` / `.part2.rar`
    6. 固实 + 恢复记录选项生效
    7. 没有 rar.exe → RAR 置灰并提示
    8. Settings → 设置 rar.exe 路径 → 重新检测生效
    9. 可以通过普通 MainWindow 打开压缩的 .rar 并预览条目
    10. 设置默认格式为 RAR → 重启 → 打开 CompressSettingsWindow → 自动选中 RAR
  - 边界情况：
    - rar.exe 路径含空格
    - 源文件路径含中文/空格
    - 压缩到桌面/中文目录
    - 取消正在进行的压缩 → 部分 .rar 文件被删除
    - rar.exe 中途崩溃（进程异常退出）
    - 选择 RAR 后切回其他格式 → RAR 选项面板隐藏
  - 回归测试：
    - 现有 ZIP/7z/TAR.GZ 压缩正常
    - 现有 RAR 解压正常（通过非 RarEngine 路径）
    - 现有格式的 DynamicFormatOptionsPanel 选项不受影响

  **Parallelization**: NO (final wave)
  **Blocked By**: Tasks 4, 5, 6, 7, 8, 9

  **Commit**: YES (fixup commits as needed)

---

## Verification Strategy

### QA Policy
每个任务通过 agent-executed 场景验证：
- **引擎功能**: 构建 rar.exe 命令 → 压缩测试文件 → 用 SharpSevenZip/7z.dll 解压验证内容 → 对比哈希
- **UI 集成**: CompressSettingsWindow 打开 → 选 RAR → 设密码/级别 → 压缩 → ProgressWindow 显示进度
- **检测逻辑**: 故意不装 / 错误路径 / 正常路径 三种情况验证
- **注册冲突验证**: `ArchiveEngineFactory.GetEngine(ArchiveFormat.Rar)` 返回 RarEngine，调用解压方法正常委托到 SevenZipEngine
- **证据**: 压缩产物 .rar 文件、控制台输出、截图

---

## Execution Strategy

```
Wave 1 (基础 + 检测):
├── 1. RarDetector.cs (核心检测逻辑)
├── 2. ArchiveOptions RAR 扩展 + AppSettings + CompressRequest
├── 3. RAR 相关 i18n 字符串

Wave 2 (引擎实现):
├── 4. RarEngine.cs — CompressAsync 核心
├── 5. RarEngine.cs — 委托实现 + SevenZipEngine CanHandle 修改 + Factory 注册

Wave 3 (UI 集成):
├── 6. DynamicFormatOptionsPanel RAR 选项面板
├── 7. CompressSettingsWindow RAR 格式 + 选项传递
├── 8. SettingsWindow RarExePath + 格式下拉 + 默认值
├── 9. App.Compress.cs CLI 守卫

Wave 4 (验证):
└── 10. 端到端测试 + bug 修复
```

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  检查每个 Must Have 的覆盖情况，确认 RarDetector/RarEngine/UI 三项全部实现。确认 SevenZipEngine 注册冲突已正确处理。检查每个 Must NOT Have 的约束（不解压替代、不实现 Add/Delete）。
- [ ] F2. **Code Quality Review** — `unspecified-high`
  代码质量检查：Process 资源释放（`using`/`finally`）、转义处理、退出码检查、取消时清理、空引用保护。
- [ ] F3. **Real Manual QA** — `unspecified-high`
  按任务 10 的场景表逐个执行，验证端到端流程。截图保存证据。
- [ ] F4. **Scope Fidelity Check** — `deep`
  确认范围没有膨胀（没有加 UI 不承诺的 RAR 功能，没有改现有解压流程）。

---

## Commit Strategy

| Task | Message |
|------|---------|
| 1 | `feat(core): add RarDetector for rar.exe/WinRAR.exe path detection` |
| 2, 3 | `feat(core+ui): add RAR-specific options, CompressRequest, AppSettings, and i18n` |
| 4, 5 | `feat(core): implement RarEngine with CompressAsync and SevenZipEngine delegation` |
| 6, 7 | `feat(ui): add RAR format dropdown and DynamicFormatOptionsPanel` |
| 8, 9 | `feat(ui): add RarExePath setting and CLI guard` |
| 10 | `fix(rar): end-to-end fixes` |

---

## Success Criteria

### 核心验证
```bash
# 压缩一个包含文件和子目录的目录
# 产物能用 SharpSevenZip 正常列出/解压
# 哈希一致
```

### 最终清单
- [ ] RarDetector 能找到已安装的 rar.exe
- [ ] 找不到 rar.exe 时尝试使用 WinRAR.exe
- [ ] CompressSettingsWindow 可选 RAR 格式
- [ ] RAR 不可用时置灰并提示
- [ ] RAR 特有选项（固实/恢复记录/压缩方式）正确传给 rar.exe
- [ ] RarCompressionMethod 正确映射 6 级（Store -m0 ~ Best -m5）
- [ ] 加密压缩正常
- [ ] 分卷压缩正常
- [ ] 进度实时上报
- [ ] 取消时进程被终止，部分文件被清理
- [ ] SevenZipEngine.CanHandle 已移除 Rar 格式，无注册冲突
- [ ] RarEngine 委托 ListEntries/Extract/Test 到 SevenZipEngine 正常
- [ ] 现有 ZIP/7z/TAR.GZ 压缩不受影响
- [ ] 现有 RAR 解压不受影响
- [ ] 全部 i18n 字符串中英双语
- [ ] Quick Compress CLI 在 RAR 不可用时正确降级
