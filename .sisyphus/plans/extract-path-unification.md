# 解压路径统一 Implementation Plan

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/9)
> **前置依赖**: 无

---

## 问题

目前存在**三条独立解压路径**，各有各的循环、进度报告、冲突处理、取消处理：

| 路径 | 入口 | 当前实现 | 文件名显示 | pw.Close() |
|------|------|---------|-----------|-----------|
| 完整解压 | `ExtractAsync` → `engine.ExtractAsync()` | 引擎内部 Task.Run 循环全部条目 | ✅ 引擎自动上报 | ⚠️ 同样有 double-close 隐患 |
| 解压选择文件 | `ExtractSelected_Click` → `ExtractSelectedAsync` | UI 线程上手写 for 循环，调 `ArchiveEntryExtractor` | ❌ 刚修复 | ❌ 刚修复 |
| 文件过滤(待实现) | 计划调 `engine.ExtractEntriesAsync()` | 引擎内部 Task.Run 循环过滤后条目 | ✅ 引擎自动上报 | ✅ 不涉及 |

引擎的 `ExtractEntriesAsync`（选择性提取）已全部实现但**从未被 UI 调用**。

---

## 目标

1. 将 `ExtractSelectedAsync` 改为调用 `engine.ExtractEntriesAsync()`，消除重复的手写循环
2. 给 `ExtractEntriesAsync` 加可选的输出路径覆写机制（`pathOverrides`），支持路径裁剪
3. 清理 `ExtractAsync` / `ExtractSelectedAsync` 现有问题（文件名显示、double-close）
4. 为后续文件过滤特性铺平道路

---

## 设计

### pathOverrides 设计

在 `IArchiveEngine.ExtractEntriesAsync` 加一个可选参数：

```csharp
Task ExtractEntriesAsync(
    string archivePath,
    IReadOnlyList<string> entryKeys,
    string destinationPath,
    string? password = null,
    IProgress<ArchiveProgress>? progress = null,
    CancellationToken cancellationToken = default,
    ArchiveOptions? options = null,
    IReadOnlyDictionary<string, string>? outputPathOverrides = null);  // ← 新增
```

**语义**：
- `null` 或空字典：行为不变，输出路径 = `GetSafePath(destinationPath, entryKey)`
- 非空时：对字典中包含的 entryKey，覆盖输出路径为字典中的值
- 未在字典中的 entryKey：仍使用默认路径（按 `GetSafePath(destinationPath, entryKey)`）

**引擎内的修改点**（以 ZipEngine 为例，当前行 378）：

```csharp
var outputPath = outputPathOverrides?.GetValueOrDefault(entryKey)
    ?? FileConflictHelper.GetSafePath(destinationPath, entryKey);
```

对于命中 override 的条目：
- `GetSafePath` 已由调用方在前置处理中完成，引擎不再重复调用
- `Directory.CreateDirectory` 仍照常执行（目录可能不存在）
- 进度上报中的 `CurrentFile` 仍用 `entryKey`（显示压缩包内路径，更清晰）

### 目录条目的处理

引擎的 `ExtractEntriesAsync` 对 `IsDirectory` 的处理是 `GetSafePath(destinationPath, entryKey)` → 创建目录。对于被 override 的路径，目录创建不需要走 override（文件路径已经包含子目录结构，`Path.GetDirectoryName` 就能提取）。

不需要对目录特殊处理——目录 entry 由引擎跳过（`file.IsDirectory → continue`），其下的文件条目各自有 override 路径，写入时 `Directory.CreateDirectory(dir)` 自动创建中间目录。

### TarGzEngine

`TarGzEngine.ExtractEntriesAsync` 目前抛 `NotSupportedException`（TAR/GZ 流式格式不支持按条目选择性提取）。

**方案**：`ExtractSelectedAsync` 在遇到 Tar/Gz 格式时降级到完整解压到临时目录，再把选中文件 copy 到目标位置。或者直接降级为调 `engine.ExtractAsync()` 完整解压到目标目录（忽略选中列表）。

**建议**：直接降级为完整解压（`engine.ExtractAsync`），不做裁剪。Tar/Gz 格式实际上很少在 UI 中浏览选择文件后再解压。

---

## 影响范围

### 文件清单

| # | 文件 | 改动 |
|---|------|------|
| 1 | `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | `IArchiveEngine` 接口加 `outputPathOverrides` 参数 |
| 2 | `src/MantisZip.Core/Engines/ZipEngine.cs` | `ExtractEntriesAsync` 应用 override |
| 3 | `src/MantisZip.Core/Engines/SevenZipEngine.cs` | `ExtractEntriesAsync` 应用 override |
| 4 | `src/MantisZip.UI/MainWindow/MainWindow.Menu.cs` | 重写 `ExtractSelectedAsync`：去掉手写循环，改调引擎 |
| 5 | `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` | (可选) `ExtractAsync` 简化为调 `ExtractEntriesAsync` |
| 6 | `docs/PROGRESS.md` | 提交前更新 |

### 不影响

- `TarGzEngine.cs` — 不改（抛 NotSupportedException）
- `ArchiveEntryExtractor.cs` — 不改（仍被预览子系统使用）
- `ProgressWindow.xaml/cs` — 不改
- `FileConflictHelper.cs` — 不改
- 文件过滤计划 (file-filter-feature.md) — 后置受影响，但接口一次性改到位

---

## 迁移步骤

### Step 1: 接口 + 引擎实现（Core 层）

#### 1.1 `IArchiveEngine` 加参数

`src/MantisZip.Core/Abstractions/ArchiveEngine.cs` — `ExtractEntriesAsync` 声明加：

```csharp
IReadOnlyDictionary<string, string>? outputPathOverrides = null
```

在最末尾 `}` 前加最后一个参数。

#### 1.2 ZipEngine 应用 override

`src/MantisZip.Core/Engines/ZipEngine.cs` — `ExtractEntriesAsync` 方法，第 378 行附近：

```csharp
// 原来：
var outputPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);

// 改为：
var outputPath = outputPathOverrides?.GetValueOrDefault(entryKey)
    ?? FileConflictHelper.GetSafePath(destinationPath, entryKey);
```

#### 1.3 SevenZipEngine 应用 override

`src/MantisZip.Core/Engines/SevenZipEngine.cs` — `ExtractEntriesAsync` 方法，第 680 行附近：

```csharp
// 原来：
var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);

// 改为：
var outputPath = outputPathOverrides?.GetValueOrDefault(fileName)
    ?? FileConflictHelper.GetSafePath(destinationPath, fileName);
```

注意: SevenZipEngine 内部对 entryKey 做了 `ArchivePath.Normalize`，所以 `outputPathOverrides` 的 key 也对应 normalized 后的值。调用方需要在构建 override 字典时做同样的 normalize。

#### 1.4 验证 Core 编译

```powershell
dotnet build src\MantisZip.Core\MantisZip.Core.csproj
```

### Step 2: 重写 ExtractSelectedAsync（UI 层）

`src/MantisZip.UI/MainWindow/MainWindow.Menu.cs` — `ExtractSelectedAsync` 方法。

#### 2.1 计算 entryKeys 和 pathOverrides

保留现有的文件列表计算逻辑（`selectedDirs` + `filesToExtract`），但改为生成引擎需要的结构：

```csharp
var entryKeys = new List<string>();
var pathOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

foreach (var item in filesToExtract)
{
    entryKeys.Add(item.FullPath);

    // 路径裁剪（与原来相同的逻辑）
    var outputEntryPath = item.FullPath;
    if (!AppSettings.Instance.ExtractPreserveFullPath && !string.IsNullOrEmpty(_currentFolder))
    {
        var cf = _currentFolder.TrimEnd('/') + "/";
        if (outputEntryPath.StartsWith(cf, StringComparison.OrdinalIgnoreCase))
            outputEntryPath = outputEntryPath.Substring(cf.Length);
    }

    var safeEntryPath = FileConflictHelper.SanitizeEntryPath(outputEntryPath);
    var outputPath = FileConflictHelper.GetSafePath(dest, safeEntryPath);
    pathOverrides[item.FullPath] = outputPath;
}
```

关键：字典的 key 用 `item.FullPath`（压缩包内原始路径，引擎用它查找条目），value 用裁剪后的安全输出路径。

#### 2.2 调用引擎

```csharp
var opts = App.CreateExtractOptions();

if (format == ArchiveFormat.Tar || format == ArchiveFormat.GZip)
{
    // Tar/Gz 不支持按条目解压，降级为完整解压
    var engine = ArchiveEngineFactory.GetEngineByExtension(_currentArchivePath!);
    if (engine != null)
    {
        var progress = ProgressWindow.CreateBackgroundProgress(pw);
        await engine.ExtractAsync(_currentArchivePath!, dest, _currentPassword,
            progress, pw.CancellationToken, opts);
    }
}
else
{
    var engine = ArchiveEngineFactory.GetEngineByExtension(_currentArchivePath!);
    if (engine != null)
    {
        var progress = ProgressWindow.CreateBackgroundProgress(pw);
        await engine.ExtractEntriesAsync(_currentArchivePath!, entryKeys, dest,
            _currentPassword, progress, pw.CancellationToken, opts, pathOverrides);
    }
}
```

#### 2.3 清理 catch 块

取消 `pw.SetProgress()` 和 `pw.CancellationToken.ThrowIfCancellationRequested()` 等手动管理代码——引擎会处理这些。

catch 块保留，但 `pw.Close()` 加上 double-close 保护（已在上一轮修改中完成）。

#### 2.4 验证 UI 编译

```powershell
dotnet build src\MantisZip.UI\MantisZip.UI.csproj -p:BuildProjectReferences=false
```

### Step 3: 验证功能

#### 3.1 编译完整项目

```powershell
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
```

#### 3.2 逻辑验证（阅读确认）

- [ ] `ExtractEntriesAsync` 得到 `pathOverrides` 后，写入路径是否正确
- [ ] 进度上报是否包含文件名（引擎自动设 `CurrentFile = entryKey`）
- [ ] 冲突处理是否正常工作（引擎内 `FileConflictHelper.ResolvePath`）
- [ ] 取消是否正常（引擎内 `cancellationToken.ThrowIfCancellationRequested`）
- [ ] Tar/Gz 降级路径是否合理

### Step 4（可选）: 简化 ExtractAsync

如果统一后确认 `ExtractAsync` 和 `ExtractEntriesAsync` 的循环体完全一致，可以将 `ExtractAsync` 简化为：

```csharp
public async Task<ExtractResult> ExtractAsync(...)
{
    var allKeys = GetNonDirectoryEntryKeys(archive);
    await ExtractEntriesAsync(archivePath, allKeys, destinationPath, password, progress, ct, options);
    return new ExtractResult { SucceededEntries = allKeys.Count, ... };
}
```

**不强制**——两个方法的返回值不同（`ExtractResult` vs `void`），简化后需要适配。可以先不碰。

---

## 对文件过滤计划的影响

统一后，文件过滤特性（file-filter-feature.md）的解压集成变为：

```
Extract_Click → ExtractSettingsWindow → 计算 filteredEntryKeys
                                              ↓
ExtractEntriesAsync(filteredEntryKeys, pathOverrides: null)
                                              ↑
ExtractSelected_Click → 计算 selectedEntryKeys
                          + pathOverrides     ↑
```

文件过滤不需要 pathOverrides（它只筛选文件，不裁剪路径），所以直接传 `null` 即可。统一对文件过滤的影响是**纯粹的减负**——不需要再为 Extract_Click 单独 hook 引擎调用。

---

## 工作量估算

| Step | 内容 | 预计时间 |
|------|------|---------|
| 1.1-1.3 | 接口 + 引擎改动 | 20 min |
| 1.4 | Core 编译验证 | 2 min |
| 2.1-2.3 | 重写 ExtractSelectedAsync | 40 min |
| 2.4 | UI 编译验证 | 2 min |
| 3 | 功能验证 | 10 min |
| 4 (可选) | 简化 ExtractAsync | 20 min |
| | **合计** | **~1.5 h** |

---

## TODOs

- [x] **1. IArchiveEngine 接口 + 引擎实现**
  - [x] 1.1 `IArchiveEngine.ExtractEntriesAsync` 加 `outputPathOverrides` 参数
  - [x] 1.2 `ZipEngine.ExtractEntriesAsync` 应用 override
  - [x] 1.3 `SevenZipEngine.ExtractEntriesAsync` 应用 override
  - [x] 1.4 `dotnet build` Core 验证

- [x] **2. 重写 `ExtractSelectedAsync`**
  - [x] 2.1 构建 entryKeys 列表 + pathOverrides 字典（保留路径裁剪逻辑）
  - [x] 2.2 改为调用 `engine.ExtractEntriesAsync()`，Tar/Gz 降级
  - [x] 2.3 清理过时的手动进度/取消管理代码
  - [x] 2.4 `dotnet build` UI 验证

- [x] **3. 验证**
  - [x] 3.1 `dotnet build` 完整项目
  - [x] 3.2 阅读确认各路径的正确性
  - [x] 3.3 更新 `docs/PROGRESS.md`

- [ ] **4.（可选）简化 `ExtractAsync`**
  - [ ] 4.1 引擎层将 `ExtractAsync` 改为委托给 `ExtractEntriesAsync`
  - [ ] 4.2 `dotnet build` 验证
