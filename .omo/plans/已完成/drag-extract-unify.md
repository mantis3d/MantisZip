# 拖拽解压与「解压选中项」流程统一 Implementation Plan

> **状态**: 📋 待实施 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜] (0/7)
> **前置依赖**: 无（承接 WPF 时代 [extract-path-unification.md](extract-path-unification.md)，其 TODOs 1-3 已完成）

---

## 问题

Avalonia 版存在两条独立解压流程，拿到输出路径后行为各异：

| 维度 | 「解压选中项到…」（VM `ExtractEntriesAsync`） | 拖拽解压（`DragDropService`） |
|------|------|------|
| 提取通道 | 引擎批量 `engine.ExtractEntriesAsync()`（一次开包），Tar/Gz 降级全量 | `ArchiveEntryExtractor.ExtractEntryAsync()`（每文件重开压缩包） |
| 路径语义 | `ExtractPreserveFullPath` + 裁剪 `CurrentFolder` 前缀 | `DragDropItemExpander.GetExtractPath`（选中目录锚点，拖出带路径） |
| 冲突处理 | `ArchiveOptions`（Ask → VM 回调，6 策略） | 服务层手动 switch（仅 4 策略，缺 overwrite-if-older/smaller） |
| 进度窗口 | 模态阻塞（`RunWithProgress`） | 非模态 + 自身 `ProgressWindow` 管理 |

另有两个既有缺陷：
- `TarGzEngine.ExtractEntriesAsync` 抛 `NotSupportedException`（右键 tar/gz 只能降级全量解压，选中子目录文件却全量解压，语义错误）
- `MapConflictActionString` 漏匹配设置值 `"overwrite-if-older"`/`"overwrite-if-smaller"`（带连字符）→ 落成 `Overwrite`

---

## 目标

1. 新建 `SelectedItemsExtractService` 统一「拿到输出路径后」的解压流程，两条流程差异仅剩**获取输出路径的方式**
2. 拖拽路径语义改为与右键一致（拖出来的就是文件本身，不带路径）
3. `TarGzEngine` 实现按条目提取（推翻 WPF 计划「TarGz 不改」的决策）
4. 冲突统一走 `AppSettings.FileConflictAction`（6 策略）+ 统一 Ask 弹窗
5. 拖拽进度窗口统一为模态（走 `vm.RunWithProgress`，与右键同一条窗口代码）
6. 「解压选中项到此处」冲突策略由硬编码 `"overwrite"` 改用设置值
7. 修 `MapConflictActionString` 连字符映射漏洞

---

## 设计

### 1. 新建 `SelectedItemsExtractService`（Services/SelectedItemsExtractService.cs）

```csharp
public sealed class SelectedItemsExtractService
{
    public async Task<bool> ExtractEntriesAsync(
        string archivePath,
        string? password,
        IReadOnlyList<ArchiveItem> entries,          // 已展开的 Core 条目
        string destinationPath,                       // ← 两条流程唯一不同的输入（获取方式不同）
        string conflictAction,                        // AppSettings.FileConflictAction
        bool openFolderAfterExtract,
        string currentFolder,                         // 当前浏览的压缩包内路径（路径裁剪锚点）
        bool preserveFullPath,                        // ExtractPreserveFullPath
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,  // 统一 Ask 弹窗
        IProgress<ArchiveProgress> progress,          // 窗口由调用方创建（统一走 RunWithProgress）
        CancellationToken ct)
}
```

内部 = 现 `MainWindowViewModel.ExtractEntriesAsync` 的引擎批量逻辑（原样搬移）：
- 构建 `entryKeys` + `pathOverrides`：`preserveFullPath ? 完整路径 : 裁剪 currentFolder 前缀`，再 `SanitizeEntryPath` + `GetSafePath`
- `CreateExtractOptions(conflictAction)`：Ask → `conflictDialog` 回调（ApplyToAll 记忆 + CancelOperation 抛异常）
- 引擎分派：`engine.ExtractEntriesAsync(...)`，**Tar/Gz 降级分支删除**（引擎已支持按条目）

### 2. `TarGzEngine.ExtractEntriesAsync` 实现（替代 NotSupportedException）

- **tar/tar.gz**：单次 `TarReader` 顺序扫描，匹配 `entryKeys` 的条目按 `outputPathOverrides`（或默认 `GetSafePath(dest, entryKey)`）输出，走 `ResolvePathAsync` 冲突处理 + per-file 进度 + 恢复修改时间（复用 `ExtractAsync` 的 tar 分支骨架，加 keySet 过滤）
- **纯 .gz**：整个流解压到目标（输出路径用 `outputPathOverrides[entryName]`，`.gz` 条目名 = `Path.GetFileNameWithoutExtension(archivePath)`）

### 3. 拖拽改造

**`DragDropService.ExecuteAfterDropAsync` 简化**：
- 保留：`DetectTargetDirectory` + own-window 取消 + 错误弹窗
- 改为：`ExpandItems` 展开 → `vm.RunWithProgress(服务调用)`（模态，删自身 `ProgressWindow`/`Task.Run` 外层管理）→ 完成后打开文件夹
- 删除：手动冲突 switch、`ArchiveEntryExtractor` 循环、`ShowConflictDialogAsync`、`_applyAllAction`
- 新增构造参数：`currentFolder`（路径裁剪锚点）

**`MainWindow.axaml.cs` 拖拽入口**：把 `vm2.CurrentFolder` 传给 `DragDropService` 构造函数

**`DragDropItemExpander.GetExtractPath`**：删除（路径计算统一走服务）；`ExpandItems` 保留（两条流程各自展开，右键用当前视图/拖拽用全量，语义合理保留）

### 4. 右键改造（VM）

- `ExtractSelectedTo` / `ExtractSelectedHere`：改为调 `SelectedItemsExtractService`（`RunWithProgress` 包 progress）；`ExtractSelectedHere` 冲突策略由 `"overwrite"` 改为 `settings.FileConflictAction`
- 删除私有 `ExtractEntriesAsync`、`CreateExtractOptions` 的 VM 内部版（挪进服务）
- `MapConflictActionString` 补 `"overwrite-if-older"` / `"overwrite-if-smaller"` 连字符映射
- `ShowExtractFileConflictDialogAsync`（MainWindow.axaml.cs 实现）：补 `dlg.CancelOperation → 抛 OperationCanceledException`（拖拽「取消整个操作」语义保留）

### 5. 呈现差异（保留）

- 错误呈现：右键走 `RunWithProgress` 静默 + 状态栏，拖拽失败弹 `AppMessageBox`（无确认环节，必须弹）
- 展开函数各自保留（输入源不同）

---

## 影响范围

| # | 文件 | 改动 |
|---|------|------|
| 1 | `src/MantisZip.Core/Engines/TarGzEngine.cs` | `ExtractEntriesAsync` 从抛异常改为实现（tar/gz 按条目提取） |
| 2 | `src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs` | **新建**：统一解压流程 |
| 3 | `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | `ExtractSelectedTo/Here` 调服务；删私有 `ExtractEntriesAsync`/`CreateExtractOptions`；修 `MapConflictActionString` |
| 4 | `src/MantisZip.UI.Avalonia/Services/DragDropService.cs` | 简化：删冲突 switch/逐文件提取/自身窗口；改调服务 + `RunWithProgress`；加 `currentFolder` 参数 |
| 5 | `src/MantisZip.UI.Avalonia/Services/DragDropItemExpander.cs` | 删 `GetExtractPath`（`ExpandItems` 保留） |
| 6 | `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | 拖拽入口传 `CurrentFolder`；`ShowExtractFileConflictDialogAsync` 补 CancelOperation |
| 7 | `docs/PLAN.md` + `docs/PROGRESS.md` | 提交前同步 |

### 不影响

- `ArchiveEntryExtractor.cs` — 不改（仍被预览子系统 / 拖拽之外的场景使用）
- `FileConflictHelper.cs` / `ProgressWindow` — 不改
- ZipEngine / SevenZipEngine — 不改（`ExtractEntriesAsync` + pathOverrides 已就绪）

---

## 行为变化汇总（提交时需在 PROGRESS.md 注明）

1. 拖拽拖出单文件不再带路径（裁剪当前浏览层）；`ExtractPreserveFullPath` 开启时保留完整路径
2. 拖拽冲突处理从 4 策略 → 6 策略（补 overwrite-if-older / overwrite-if-smaller）
3. 拖拽提取通道从逐文件 `ArchiveEntryExtractor` → 引擎批量（性能提升）
4. 右键 tar/gz 不再降级全量（按条目解压，语义修正）
5. 「解压选中项到此处」冲突策略改用设置值（默认 ask 会弹窗）
6. 拖拽进度窗口统一为模态阻塞（与右键同一窗口代码）

---

## 迁移步骤

### Step 1: TarGzEngine 实现按条目提取（Core）
- 实现 `ExtractEntriesAsync`：tar/tar.gz 分支（keySet 过滤 + overrides）+ 纯 .gz 分支
- `dotnet build src\MantisZip.Core` 验证

### Step 2: 新建 SelectedItemsExtractService
- 从 VM 搬移引擎批量逻辑 + 路径计算 + `CreateExtractOptions`
- 修正 `MapConflictActionString` 连字符映射

### Step 3: VM 改造
- `ExtractSelectedTo` / `ExtractSelectedHere` 调服务
- 删除私有 `ExtractEntriesAsync` / `CreateExtractOptions`

### Step 4: DragDropService 改造
- 简化流程，调服务 + `vm.RunWithProgress`
- 删冲突 switch / 逐文件提取 / 自身窗口 / `GetExtractPath`
- 加 `currentFolder` 构造参数

### Step 5: MainWindow.axaml.cs
- 拖拽入口传 `CurrentFolder`
- `ShowExtractFileConflictDialogAsync` 补 CancelOperation

### Step 6: 验证
- `dotnet build src\MantisZip.UI.Avalonia` 0 errors
- 回归：右键解压（zip/7z/tar.gz）、拖拽解压（zip/7z/tar.gz）、冲突弹窗（ask）、取消整个操作

### Step 7: 文档
- 更新 `docs/PLAN.md`（本计划行）
- 提交前更新 `docs/PROGRESS.md`（Avalonia 线索）

---

## 工作量估算

| Step | 内容 | 预计时间 |
|------|------|---------|
| 1 | TarGzEngine 按条目提取 | 40 min |
| 2 | SelectedItemsExtractService 新建 | 30 min |
| 3 | VM 改造 | 20 min |
| 4 | DragDropService 改造 | 30 min |
| 5 | MainWindow.axaml.cs | 15 min |
| 6 | 构建 + 回归验证 | 20 min |
| 7 | 文档同步 | 10 min |
| | **合计** | **~2.5 h** |

---

## TODOs

- [ ] 1. TarGzEngine 实现 `ExtractEntriesAsync`（tar/gz 按条目提取）
- [ ] 2. 新建 `SelectedItemsExtractService`（搬移引擎批量逻辑 + 修映射漏洞）
- [ ] 3. VM：`ExtractSelectedTo/Here` 调服务，删私有方法
- [ ] 4. DragDropService 简化 + 模态进度 + `currentFolder` 参数
- [ ] 5. MainWindow.axaml.cs：传 CurrentFolder + 冲突回调补 CancelOperation
- [ ] 6. 构建 + 回归验证
- [ ] 7. PLAN.md / PROGRESS.md 同步
