# Work Plan: 压缩包编辑操作序列（暂存区 + 批量提交）

> **Status**: 📋 待开始 | **阶段**: [⬜⬜⬜⬜⬜⬜] (0/6)
> **预估工时**: 🟡中 **12-16h**（A 档 P1 最小可用）；P2/P3 增强 **8-12h**（B 档，不排期）
> **档位**: **B 档**（2026-10-29 用户调研后从 A 档降级，P1 与 P2/P3 一并排队；与 `archive-rename-entry.md` **同批做**——先有 rewriter 的 rename 通道，本计划能少一半工作量）
> **前置依赖**: **硬依赖 `archive-rename-entry.md`（S 档）**——它先把 `ZipBinaryRewriter` 的 rename 通道和 `IArchiveEngine.RenameEntriesAsync` 做出来，本计划直接复用，否则这里要重做一遍同样的地基。

## Problem

现在对压缩包内容的每一次编辑都是**一次独立的整包写盘**，而用户的心智模型是「先改一堆、最后保存」：

- ZIP 添加 / 删除都是 copy-mode 整包复制（按压缩字节复制，不解压，但**整包重写一遍**）：
  `src\MantisZip.Core\Engines\ZipEngine.CompressGroup.cs:136-222`（添加）、`:894-979`（删除）。
- 7z 添加是就地追加（便宜）：`src\MantisZip.Core\Engines\SevenZipEngine.cs:1416` `CompressionMode.Append`。
- 7z **删除最贵**：全量解压保留条目到 `%TEMP%\MantisZip\DeleteTemp\{guid}` + `SharpSevenZipCompressor` 全量重打包
  （`SevenZipEngine.cs:1281-1345`，且固定 `CompressionLevel.Normal` ⇒ 用户原压缩等级丢失）。

所以「删 20 个文件」现在是 20 次整包复制 / 20 次全量重打包。批量化后是 **1 次**。

**收益排序**：7z 删除 ≫ ZIP 删除 ≈ ZIP 添加 ≫ 7z 添加（7z 添加本来就便宜，批量化只是省掉重复打开）。

## Objective

给用户一个「暂存区」心智模型：

1. 删除 / 重命名（后续加移动、拖拽）**先进内存队列**，文件列表立刻反映结果（虚拟视图）。
2. 用户点「应用」或关闭时，**一次性**提交给引擎；ZIP 走一次 `RewriteAsync`，7z 走一次 `ModifyArchive` + 一次追加。
3. 提交前随时「丢弃」；撤销/重做天然免费（队列在内存里，从未落盘）。

## 用户已拍板的口径（2026-10-27）

| # | 口径 |
|---|------|
| 1 | 暂存范围**只限当前打开的那一个压缩包**（切包即结算，不做跨包队列） |
| 2 | 未提交时切换 / 关闭 → **三选**：应用 / 丢弃 / 取消 |
| 3 | 加密包新增条目 → **沿用原包加密**（要再输一次密码），不静默落明文 |
| 4 | 提供「提交前备份原包 / 另存为新包」开关，**默认关** |
| 5 | 其余按本文档建议 |

## 设计

### 数据模型（UI 层，纯内存）

```csharp
// src/MantisZip.UI.Avalonia/Services/ArchiveEditSession.cs（新）
public abstract record PendingOp;
public sealed record DeleteOp(string EntryPath) : PendingOp;
public sealed record RenameOp(string OldPath, string NewPath) : PendingOp;
public sealed record AddOp(string SourcePath, string TargetPath) : PendingOp;
```

- `ArchiveEditSession` 持有 **原始条目快照**（`IReadOnlyList<ArchiveItem>`，来自打开时的 `ListEntriesAsync`）+ **有序 `List<PendingOp>`**。
- 虚拟视图 = `Project(snapshot, ops)`，**纯函数**，每次变更重算（条目量级 ≤ 数万，O(n·m) 可接受；m 是 op 数，通常 < 100）。
- 语义必须钉死：**暂存视图是唯一真相**。`rename a.txt→b.txt` 之后再 `delete b.txt` 必须合法（按视图里的名字匹配）；反向 `delete a.txt` 后 `rename a.txt→c.txt` 必须被判为无效 op 并提示。

### 提交（Core 一次性批量接口）

```csharp
// src/MantisZip.Core/Abstractions/ArchiveEngine.cs
public sealed record ArchiveBatchPlan
{
    public IReadOnlyList<string> Deletes { get; init; } = [];
    public IReadOnlyList<(string OldPath, string NewPath)> Renames { get; init; } = [];
    public IReadOnlyList<(string SourcePath, string TargetPath)> Adds { get; init; } = [];
}

// 默认实现抛 NotSupportedException，与 CanAdd(:540)/CanDelete(:545) 同型
Task ApplyBatchAsync(string archivePath, ArchiveBatchPlan plan, string? password = null,
    IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default,
    ArchiveOptions? options = null) => throw new NotSupportedException(...);

bool CanApplyBatch(ArchiveFormat format) => false;
```

### 引擎实现

| 格式 | 提交动作 | 次数 |
|---|---|---|
| ZIP | **一次** `ZipBinaryRewriter.RewriteAsync(source, temp, keep: 视图中保留的旧名, renameMap: 改名映射, addEntries: 新增)` → 原子替换 | 1 次整包复制 |
| 7z | `ModifyArchive(archivePath, deleteIndexes, password)` 一次删完 → 对改名条目「取单条→新名 append」→ 新增条目 append | 1 次就地删 + k 次追加 |
| tar.gz | `CanApplyBatch` = false，UI 直接禁用暂存区（与 `TarGzEngine.cs:574`/`:595` 现状一致） | — |

### UI

- 队列可视：文件列表顶部一条「待提交 N 项」提示条（`Apply` / `Discard` 两个按钮 + 撤销/重做）。
- 文件列表行内标记：待删除条目加删除线/strikethrough、待改名显示 `旧名 → 新名`（走现有 `ArchiveItem` 显示层，新增 `PendingState` 装饰属性即可）。
- 提交：`RunWithProgress` 一次，进度文案区分「重写压缩包（无法零重压缩）」与「就地更新」两种代价。
- 未提交守卫：`CloseArchive` / 打开新包 / 关窗三条路径统一走一个 `TryFlushPendingOpsAsync()`，三选对话框（应用 / 丢弃 / 取消）。
- 备份开关：默认关；开启时提交前把原包复制到 `{name}.{yyyyMMddHHmmss}.bak`（同目录，.gitignore 无关），提交成功后提示备份位置。

## 任务总览

- [ ] **1. Core: `ArchiveBatchPlan` + `ApplyBatchAsync` / `CanApplyBatch`**（接口默认实现 + 两个引擎落地）
- [ ] **2. Core: 测试**（ZIP 批量、7z 批量、tar 拒绝、空计划、重复删、改名+删除交互）
- [ ] **3. UI: `ArchiveEditSession`（快照 + ops + 纯函数投影 + 校验）**
- [ ] **4. UI: 提示条 / 行内标记 / 应用 / 丢弃 / 撤销 / 重做**
- [ ] **5. UI: 三处未提交守卫 + 备份开关**
- [ ] **6. 本地化 + 文档 + 验证**

## 难点（已识别，逐条给了对策）

| # | 难点 | 对策 |
|---|------|------|
| 1 | 「暂存视图是真相」的语义容易被写成「原始列表过滤」 | 投影函数只吃 `(snapshot, ops)`，op 之间按顺序累积；补一组纯函数单测（含 rename→delete、delete→rename 非法） |
| 2 | 切包 / 关窗 / 崩溃三条路径的守卫最易漏 | `TryFlushPendingOpsAsync()` 单一入口；`CloseArchiveCommand`、`OpenArchiveCommand`、`Window.Closing` 三处都必须过它；补 UI 测试断言「三条路径都调用了它」 |
| 3 | 加密包新增条目 | `ArchiveOptions { Encrypt = true, Password = <重新输入> }`；无密码时禁止入队并提示 |
| 4 | 压缩后总大小不可精确预知 | 不显示「提交后大小」，只显示「待提交 N 项」；避免假精度 |
| 5 | ZIP64 / 非承载 method 会回退 legacy 全解全压，单价变贵 | 提交前预判并**明说**：「此包需要完整重写，预计较慢」；不在后台静默降级 |
| 6 | 现有源码文本守卫测试会连坐 | `tests\MantisZip.UI.Avalonia.Tests\ExtractParallelDegreeWiringTests.cs` 等按源码文本断言调用路径；本计划改 `DeleteFiles` 调用点时必须同步改守卫（或把守卫指向新的单一入口） |

## 验收标准

- [ ] ZIP：连续删 10 项 + 改名 2 项 → **仅 1 次**整包写盘（用 `CoreLog` 或文件 `LastWriteTime` 计数证明）
- [ ] 7z：删 10 项 → 1 次 `ModifyArchive`，不再走「全量解压 + 重打包」老路径
- [ ] 未提交关窗 → 三选生效；选「取消」则窗口不关
- [ ] rename→delete 组合语义正确；delete→rename 被拒
- [ ] tar.gz 打开时暂存区按钮灰化并有说明
- [ ] 三语文案齐全；`pwsh -File scripts\verify-repo.ps1 -Task check` exit 0
- [ ] 进度文档（里程碑 + 细节）更新；RELEASE_NOTES 按规则 18 与用户确认

## B 档增强（P2/P3，8-12h，不排期）

| 项 | 说明 |
|---|---|
| 重命名 / 移动到…入队 | 把单次重命名改成入队（复用 `RenameOp`），并支持「移动到目录」 |
| 拖拽移动 | 文件列表条目拖到目录树节点 = 入队 `RenameOp`（换父路径） |
| 逐步撤销 / 重做 | 队列级 undo/redo（P1 只做「整队丢弃」） |
| 另存为新包 | 提交目标可选新文件（而不是覆盖原包），与备份开关二选一 |

## File Changes Summary（P1 最小可用）

| 文件 | 变更 |
|---|---|
| `src/MantisZip.Core/Abstractions/ArchiveEngine.cs` | 新增 `ArchiveBatchPlan`、`ApplyBatchAsync`（默认抛）、`CanApplyBatch`（默认 false） |
| `src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs` | 新增 `ApplyBatchAsync` 实现（一次 `RewriteAsync`） |
| `src/MantisZip.Core/Engines/SevenZipEngine.cs` | 新增 `ApplyBatchAsync` 实现（`ModifyArchive` + 追加），顺手修「删除固定 Normal 等级」的既有缺陷 |
| `src/MantisZip.Core/Utils/ZipBinaryRewriter.cs` | 复用 S 档已加的 rename 通道（无新增改动） |
| `src/MantisZip.UI.Avalonia/Services/ArchiveEditSession.cs` | 新增（快照 + ops + 投影 + 校验） |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.EditQueue.cs` | 新增 partial（命令 + 提示条状态 + 守卫入口） |
| `src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml` | 顶部提示条 + 行内待提交标记 |
| `src/MantisZip.UI.Avalonia/Dialogs/…` | 未提交三选对话框（可复用 `CloseArchiveDialog` 或新建） |
| `src/MantisZip.UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json` | 新 key（三语必须同步） |
| `tests/MantisZip.Tests/Engines/*Batch*Tests.cs` | 新增 |

## Open Questions

1. **7z 批量删除后是否重排索引**：`ModifyArchive` 接受 index 集合，但删除顺序可能影响后续 append 的索引 —— 实现时用「从大到小」删，并补测试。
2. **暂存队列是否持久化**（崩溃恢复）：建议**不做**（P3 再说），崩溃 = 丢弃，与 git 工作区不同。
3. **是否同时批量解压**：解压不属于「编辑」，保持独立，不进本队列。
