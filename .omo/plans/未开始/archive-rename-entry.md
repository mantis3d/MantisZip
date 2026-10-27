# Work Plan: 压缩包内重命名（引擎级 copy-mode，零重压缩）

> **Status**: 📋 待开始 | **阶段**: [⬜⬜⬜⬜⬜⬜] (0/6)
> **预估工时**: 🟡中 **8-10h**
> **档位**: **S 档**（2026-10-27 从 A 档上移）
> **前置依赖**: 无硬依赖。本计划的 rewriter rename 通道是 A 档 `archive-edit-operation-sequence.md`（暂存区）的**地基**——先做本计划，暂存区能少一半工作量。

## 为什么推倒重写（旧版方案作废）

本文件旧版（2026-10 初稿，3-4h → 6-8h）的方案是 `ArchiveEntryRename`：**提取到 TEMP → `DeleteEntriesAsync` → `AddToArchiveAsync`**。逐行读引擎后确认四条硬伤：

1. **一次改名 = 3 次整包写盘**。ZIP 的添加与删除**各自都是整包 copy-mode 复制**（`src\MantisZip.Core\Engines\ZipEngine.CompressGroup.cs:136-222`、`:894-979`），为了换一个文件名要把 4 GB 的包重写三遍。
2. **7z 会静默改掉用户的压缩等级**。7z 删除路径是全量解压 + `SharpSevenZipCompressor` 重打包，且固定 `CompressionLevel.Normal`（`src\MantisZip.Core\Engines\SevenZipEngine.cs:1336-1345`）⇒ 改名反而把包压得更差或更大。
3. **加密包会掉链子**。旧计划写「重新添加时放弃加密」，等于改名就把条目变成明文，不可接受。
4. **三步之间没有原子性**。任何一步失败就留下「旧条目已删、新条目没加」的破损包；而这个操作改的是用户唯一的原件。

新方案目标：**ZIP 零重压缩**（copy-mode 只改名字字节，压缩数据原样搬运）；**7z 只碰被改条目**（`ModifyArchive` 就地删 + 单条追加）。

## 任务总览

- [ ] **1. Core: `ZipBinaryRewriter` rename 通道**（`renameMap` 参数 + LFH/CD 名字改写）
- [ ] **2. Core: `IArchiveEngine.RenameEntriesAsync` + `CanRename`**（默认抛 / 默认 false，与 `CanAdd`/`CanDelete` 同型）
- [ ] **3. Core: `ZipEngine` / `SevenZipEngine` 实现；`TarGzEngine` 明确拒绝**
- [ ] **4. UI: 重命名对话框 + 右键菜单 + F2**
- [ ] **5. 本地化（三语）**
- [ ] **6. 测试 + 文档 + 构建/CI 验证**

## 现状事实（2026-10-27 逐文件核准）

| 位置 | 事实 |
|---|---|
| `src\MantisZip.Core\Utils\ZipBinaryRewriter.cs:314` | `RewriteAsync(string sourcePath, string destPath, HashSet<string>? keepEntryNames, List<NewEntry>? addEntries, Encoding encoding, string? comment = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)` —— **没有 rename 通道**（全文 `Rename` 零命中） |
| 同文件 `:201` / `:854` / `:935` / `:1167` | `ReadCentralDirectory` / `CopyEntryAsync` / `ReadAndMaybeRewriteLfh` / `WriteCentralDirectory` —— 名字字节的读写全在这四处 |
| 同文件 `CdEntry` | `internal readonly record struct CdEntry(…, byte[]? RawFileNameBytes = null)` —— 原名字节透传（防止「解码→重编码」损坏老包） |
| `ZipEngine.CompressGroup.cs:1576-1605`、`:482-483` | copy-mode 只承载 method 0/8/9；不承载则回落 legacy 全解全压 |
| 同文件 `:222` / `:979` | 两条 legacy 回退分支（`copy-mode not available, falling back to legacy path`） |
| `src\MantisZip.Core\Engines\SevenZipEngine.cs:1509-1511` | `delCompr.ModifyArchive(archivePath, deleteIndexes, password)` —— 注释写明探针已验证「index→null 删除有效」 |
| 同文件 `:1416` | 添加走 `CompressionMode.Append`（就地追加，便宜） |
| `src\MantisZip.Core\Engines\TarGzEngine.cs:574` / `:595` | `DeleteEntriesAsync` / `AddToArchiveAsync` 均 `NotSupportedException` |
| `src\MantisZip.Core\Abstractions\ArchiveEngine.cs:540` / `:545` | `CanAdd` / `CanDelete` 接口默认 `false`——新方法照这个形状加 |
| `src\MantisZip.UI.Avalonia\Controls\ArchiveContentView.axaml:272-296` | 文件列表右键菜单；`Ctx_Delete` 在 `:293-295`（`DeleteFilesCommand` + `IsEnabled="{Binding CanDeleteEntries}"`） |
| `src\MantisZip.UI.Avalonia\Views\MainWindow.axaml.cs:1007` | `RegisterKeyBindings(MainWindowViewModel vm)` —— 快捷键唯一事实源（规则 10.1） |
| `src\MantisZip.UI.Avalonia\ViewModels\MainWindowViewModel.Compress.cs:100` | `DeleteFiles`：确认框 → `PendingOperationNote`（`:133`）→ `RunWithProgress` → `engine.DeleteEntriesAsync(...)`（`:155`）——重命名照这个骨架写 |

## Approach

### 引擎分工

| 格式 | 实现 | 代价 |
|---|---|---|
| ZIP | 一次 `RewriteAsync`，加 `renameMap`：保留条目按压缩字节原样复制，只把 LFH + CD 里的名字替换 | O(整包复制)，**零重解压/零重压缩** |
| 7z | 单条提取到 `%TEMP%\MantisZip\Rename\{guid}` → `ModifyArchive` 就地删旧索引 → 按新名追加 | O(1 条数据) + 一次结构重写 |
| tar.gz | `CanRename` = false，菜单灰化 + 提示 | — |

### Core API

```csharp
// src/MantisZip.Core/Abstractions/ArchiveEngine.cs（与 CanAdd(:540)/CanDelete(:545) 同型）
bool CanRename(ArchiveFormat format) => false;

Task RenameEntriesAsync(
    string archivePath,
    IReadOnlyList<(string OldPath, string NewPath)> renames,
    string? password = null,
    IProgress<ArchiveProgress>? progress = null,
    CancellationToken cancellationToken = default,
    ArchiveOptions? options = null) => throw new NotSupportedException("…该格式不支持重命名");
```

- 一个方法带**列表**而不是单条：目录改名要一次改 N 个后代条目，单独删改会让中间态暴露给其它读者。
- 先写 `{archive}.tmp` 再原子替换（`File.Move(src, dst, overwrite: true)`），失败保留原件。

### ZIP：给 rewriter 加 renameMap

签名末尾追加可选参数（三处现有调用点全部兼容）：

```csharp
public static Task<RewriteResult> RewriteAsync(
    string sourcePath, string destPath,
    HashSet<string>? keepEntryNames, List<NewEntry>? addEntries,
    Encoding encoding, string? comment = null,
    IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default,
    IReadOnlyDictionary<string, string>? renameMap = null)   // 旧名 → 新名
```

三处改写点：

1. `ReadAndMaybeRewriteLfh`（`:935`）：命中 renameMap 时把新名编码成字节写回 LFH，并让 `CopyEntryAsync` 重算后续偏移。
2. `WriteCentralDirectory`（`:1167`）：命中的 CD 条目用新名字节替换 `RawFileNameBytes`（**同时**决定是否翻转 UTF-8 标志位）。
3. `RewriteResult` 增加 `EntriesRenamed`，供进度与日志。

硬约束：

- `keepEntryNames` 语义不变（仍按**旧名**判定保留），renameMap 只影响保留条目的名字。
- 新名必须能被调用方传入的 `encoding` 无损表示；不能表示、或字数超 `0xFFFF`、或必须翻转 UTF-8 标志位而原包其它条目编码不一致 ⇒ **抛 `ZipCopyModeException`**，由引擎回退 legacy（此时进度文案写「需要完整重写压缩包」）。**绝不静默降级成乱码**。
- ZIP64：`ReadEocd` 已经会抛 `ZipCopyModeException` ⇒ 自动走 legacy，无需额外分支。

### 7z

1. `ListEntriesAsync` 拿全量，定位旧名 index（目录则匹配全部后代，用 `ArchivePath.MatchesEntryOrDescendant`）。
2. 只把**被改条目**提取到 TEMP（不是整包）。
3. `ModifyArchive(archivePath, deleteIndexes, password)` 一次删完（index 从大到小）。
4. `CompressFileDictionary` + `CompressionMode.Append` 以新名追加。
5. 若归档为加密（`ArchiveItem.IsEncrypted` / 头部加密），**必须**有密码；无密码 ⇒ 拒绝并提示（口径同用户拍板：沿用原包加密，不落明文）。

### UI（Avalonia）

- 右键菜单：在 `ArchiveContentView.axaml:293` 的 `Ctx_Delete` 之前插入
  `MenuItem Header="{Binding LocalizedStrings[Ctx_Rename]}" Command="{Binding RenameEntryCommand}" IsEnabled="{Binding CanRenameEntries}"`。
- 快捷键（规则 10.1 三步）：`RegisterKeyBindings` 加 `Add(Key.F2, KeyModifiers.None, vm.RenameEntryCommand)`；同处 `MenuItem` 写 `InputGesture="F2"`（显示用）；更新 `tests\MantisZip.UI.Avalonia.Tests\MainWindowKeyBindingsTests.cs` 期望清单。
- 对话框：新建 `src\MantisZip.UI.Avalonia\Dialogs\RenameEntryDialog.axaml(.cs)`，照 `Dialogs/PasswordDialog.axaml` 的形状（标题 + 只读原名 + 输入框 + 内联校验提示 + 确定/取消）；打开时**只选中主文件名、保留扩展名**（与资源管理器一致）。按规则 14 写中文注释、规则 4 用主题资源、规则 5 间距用 `SpacingXxxThk`。
- 校验：空、含 `/` 或 `\`、含控制字符 → 内联提示不提交；与同目录已有条目重名 → 走冲突确认（覆盖或改名）。
- VM：`MainWindowViewModel.Compress.cs` 增 `RenameEntry`（`[RelayCommand]`），骨架照 `DeleteFiles`（`:100`）：取选中项 → 弹对话框 → `PendingOperationNote` → `RunWithProgress(() => engine.RenameEntriesAsync(...))` → 刷新并回到原目录。
- 多选 / 不支持格式：`CanRenameEntries` = 单选 && `_engine.CanRename(_currentFormat)`。
- **范围边界（S 档不扩散）**：「重命名」只改**同一目录内的文件名**；「移动到其它目录」属于 A 档暂存区的拖拽/移动到…，本计划不做。

### 测试

- `tests\MantisZip.Tests\Utils\ZipBinaryRewriterRenameTests.cs`（新）
  - 改名后 `CompressedSize` / `Crc32` / 压缩字节**与原条目一致**（证明零重压缩）
  - 目录条目改名（名字以 `/` 结尾）
  - 非 UTF-8 归档的旧名字节保持原样；新名不可表示时抛 `ZipCopyModeException`
  - 同时「改名 + 删除 + 新增」三种 op 一次提交
- `tests\MantisZip.Tests\Engines\ZipEngineRenameTests.cs` / `SevenZipEngineRenameTests.cs`（新）
- `tests\MantisZip.UI.Avalonia.Tests\RenameEntryDialogTests.cs`（新，校验规则 + 扩展名选中逻辑）；`MainWindowKeyBindingsTests` 更新
- **本地限制**：Core 测试项目是 xUnit v2，本机 VSTest 起不来（`docs\KNOWN_ISSUES.md` KI-002）⇒ 本地只能编译 + 跑 UI 测试（`dotnet exec`），Core 用例以 CI 为准。

## 风险

| 风险 | 对策 |
|---|---|
| 改 `RewriteAsync` 波及既有添加/删除路径（4 处调用点：`ZipEngine.CompressGroup.cs:178`、`:606`、`:703`、`:941`） | 新参数默认 `null`，先只加参数跑一遍回归；再补「既有无 rename、行为逐字节一致」的测试（对比 fixture 哈希） |
| 名字重编码破坏老包 | 沿用 `CdEntry.RawFileNameBytes` 透传；只有在命中 renameMap 的条目才重新编码 |
| 7z 追加可能改变该条目的压缩方法/等级 | 计划内**接受**（SharpSevenZip 不提供逐条目控制），进度文案不承诺「保持原压缩率」 |
| ZIP64 / 不承载 method → legacy 慢路径 | 提交前预判并明说「需要完整重写，较慢」 |
| 源码文本守卫测试连坐（`ExtractParallelDegreeWiringTests` 等按源码文本断言调用路径） | 改动调用点时同步改守卫，或让守卫指向新的单一入口 |

## Definition of Done

- [ ] `dotnet build` 0 error；`pwsh -File scripts\verify-repo.ps1 -Task check` exit 0
- [ ] CI 绿（Core 用例在 CI 跑；本地只保证编译 + UI 用例）
- [ ] ZIP 改名后 `CompressedSize` / `Crc32` 与原条目一致
- [ ] 7z 改名可用；tar.gz 菜单灰化且有说明
- [ ] F2 生效、不与已有快捷键冲突，`MainWindowKeyBindingsTests` 同步
- [ ] 三语文案齐全（`verify-repo.ps1 -Task i18n` 通过）
- [ ] `docs\PROGRESS.md` + `docs\progress-avalonia-detail.md` 更新；RELEASE_NOTES 按规则 18 与用户确认

## File Changes Summary

| 文件 | 变更 |
|---|---|
| `src\MantisZip.Core\Utils\ZipBinaryRewriter.cs` | `RewriteAsync` 加 `renameMap` 参数；LFH/CD 名字改写；`RewriteResult.EntriesRenamed` |
| `src\MantisZip.Core\Abstractions\ArchiveEngine.cs` | 新增 `CanRename`、`RenameEntriesAsync`（默认实现） |
| `src\MantisZip.Core\Engines\ZipEngine.CompressGroup.cs` | 实现 `RenameEntriesAsync`（一次 `RewriteAsync` + 原子替换） |
| `src\MantisZip.Core\Engines\SevenZipEngine.cs` | 实现 `RenameEntriesAsync`（提取单条 + `ModifyArchive` + 追加） |
| `src\MantisZip.Core\Engines\TarGzEngine.cs` | `CanRename` 保持 false；`RenameEntriesAsync` 抛带说明的 `NotSupportedException` |
| `src\MantisZip.UI.Avalonia\Dialogs\RenameEntryDialog.axaml(.cs)` | 新增对话框 |
| `src\MantisZip.UI.Avalonia\ViewModels\MainWindowViewModel.Compress.cs` | `RenameEntry` 命令 + `CanRenameEntries` |
| `src\MantisZip.UI.Avalonia\Controls\ArchiveContentView.axaml` | 右键菜单加「重命名」 |
| `src\MantisZip.UI.Avalonia\Views\MainWindow.axaml.cs` | `RegisterKeyBindings` 加 F2 |
| `src\MantisZip.UI.Avalonia\Localization\strings.{zh-CN,en,zh-TW}.json` | 新增 key（三语同步） |
| `tests/MantisZip.Tests/Utils/ZipBinaryRewriterRenameTests.cs` 等 | 新增测试 |

## Open Questions

1. **目录改名是否也在 S 档做**：目录条目 + 全部后代改名，成本只多约 0.5h（renameMap 天然支持批量），**建议一起做**。
2. **重命名后是否记住滚动位置/当前目录**：建议回到原目录、选中改后的条目。
3. **旧版计划里的「同长度名字 in-place 改名」优化**：已被本方案消解（现在**默认**就是原地改名字节，不限长度）。
