# 压缩流程统一化（CompressService）

## 冲突处理只是表象，真正的重复是整个压缩流程

压缩时目标文件已存在的冲突处理逻辑散落在多处，每处各自实现 `CompressConflictDialog` 弹窗 + switch 分支 + ApplyToAll 记忆逻辑，导致：

1. **重复代码** — 相同的冲突处理模式写了至少 3 遍
2. **维护风险** — 改一处容易漏其他处（本次 ApplyToAll 就是例子）
3. **行为不一致** — 各路径对 Rename/Add/Overwrite 的后续处理细节可能微妙不同

## 现状盘点

### 压缩冲突对话框 (`CompressConflictDialog`)

```csharp
CompressConflictDialog(string filePath, bool canAdd, string? suggestedName)
```

输出：`ResultAction` (Overwrite / Add / Rename / Cancel) + `ApplyToAll` (bool)

### 所有调用点

#### A. GUI 路径 — CompressSettingsWindow

| 方法 | 循环？ | 位置 | 已修 ApplyToAll？ |
|---|---|---|---|
| `RunManualCompressAsync` | ❌ 单次 | `CompressSettingsWindow.xaml.cs:828` | 不需要 |
| `RunSeparateCompressAsync` | ✅ 循环 | `CompressSettingsWindow.xaml.cs:1010` | ✅ |
| `RunCombinedCompressAsync` | ❌ 单次 | `CompressSettingsWindow.xaml.cs:1119` | 不需要 |

#### B. CLI 路径 — App.Cli.cs

| 方法 | 循环？ | 位置 | 已修 ApplyToAll？ |
|---|---|---|---|
| `RunCompressSeparateBatch` | ✅ 循环 | `App.Cli.cs:176` | ✅ (第二轮) |
| `RunCompressCombined` | ❌ 单次 | `App.Cli.cs:380` | 不需要 |
| `HandleCompressQuick` | ❌ 单次 | `App.Cli.cs:1198` | 不需要 |

#### C. 提取/错误处理（对照参考）

提取冲突 `ConflictDialog` + `ErrorDialog` **已经集中化**，都在 `App.xaml.cs`：

```csharp
// 居中 — CreateExtractOptions 内的 ConflictResolver 闭包
internal static ArchiveOptions CreateExtractOptions()
{
    bool applyToAll = false;
    FileConflictAction? chosenAction = null;
    return new ArchiveOptions
    {
        ConflictAction = GetConflictActionFromSettings(),
        ConflictResolver = info => { /* 统一弹窗逻辑 */ }
    };
}

// 居中 — CreateCompressOptions 内的 ErrorResolver 闭包
internal static ArchiveOptions CreateCompressOptions()
{
    bool applyToAll = false;
    FileErrorAction? chosenAction = null;
    return new ArchiveOptions
    {
        ErrorResolver = info => { /* 统一弹窗逻辑 */ }
    };
}
```

引擎层通过 `FileConflictHelper.ResolvePath` 调用这些回调，调用方只需 `options.ConflictResolver`，不需要自己写 switch。

### 三路重复的代码模式

每个压缩冲突调用点都重复了这段结构：

```
if (File.Exists(outputPath))
    if (applyToAll && chosenAction.HasValue)
        直接用记忆的 action
    else
        弹 CompressConflictDialog
        如果 ApplyToAll 则记忆
        switch (action):
            Overwrite → 继续压缩
            Rename → 改路径后压缩
            Add → 追加到已有压缩包
            Cancel → 跳过/退出
```

## 主方案：统一 CompressService

### 为什么提取端不需要动——架构对比

压缩端之所以需要大改，是因为它和提取端的架构差距巨大：

| 方面 | 压缩端 ❌ | 提取端 ✅ |
|---|---|---|
| 执行循环数量 | **3 个**（`RunSeparateCompressAsync`、`RunCompressSeparateBatch`、Manual/Combined 各一个） | **1 个**（`HandleExtractBatchCore`） |
| GUI 路径 | 自己写完整的压缩循环 + 冲突弹窗 | 只收集参数（写 AppSettings），执行走统一方法 |
| CLI 路径 | 又写一套完整的压缩循环 + 冲突弹窗 | 同样是走 `HandleExtractBatchCore` |
| 冲突处理 | 各路径手动写 `CompressConflictDialog` + switch | 集中化 `CreateExtractOptions()` + `ConflictResolver` 回调 |
| 参数传递 | 各路径从各自来源取值（控件 / AppSettings） | 统一写入 AppSettings，执行时读取 |

提取端的调用链：

```
GUI (ExtractSettingsWindow)
  │  ExtractButton_Click: 只写 AppSettings
  │  DialogResult = true
  │
  └──→ HandleExtractBatchCore ←── CLI 所有 --extract-* 路径
           ↓
        唯一的循环，唯一的冲突回调
        for i in allPaths
            engine.ExtractAsync(...)
```

压缩端的目标就是改成和提取端一样的结构——GUI/CLI 只是不同参数的入口，运行的代码是同一份。

### 核心思路

不仅冲突处理要集中，整个压缩流程都应该统一。GUI 和 CLI 只是不同入口，给同一个 `CompressService` 提供不同参数。

```
GUI (CompressSettingsWindow)           CLI (App.Cli)
  │  从控件取值构造 Request               │  从 AppSettings 构造 Request
  │  包装冲突回调                        │  包装冲突回调
  ├──→ CompressService.CompressAsync() ──┤
  │                                     │
  └── 收到进度 → 更新 UI                └── 收到进度 → 更新 UI
     this.Close()                          app.Shutdown()
```

### CompressService 接口

```csharp
public class CompressRequest
{
    public List<string> SourcePaths { get; init; }      // 源文件列表
    public CompressOutputMode Mode { get; init; }       // Manual / Separate / Combined
    public string Format { get; init; }                  // zip / 7z / tar.gz
    public int CompressionLevel { get; init; }           // 1–9
    public string? Password { get; init; }               // 加密密码
    public long SplitSize { get; init; }                 // 分卷大小
    public string? Comment { get; init; }                // 注释
    public CommentDistribution CommentDistribution { get; init; }
    public bool Encrypt { get; init; }
    public string? OutputPath { get; init; }             // Manual 模式指定
}

public class CompressResult
{
    public int Succeeded { get; init; }
    public int Failed { get; init; }
}

public class CompressService
{
    public async Task<CompressResult> CompressAsync(
        CompressRequest request,
        Func<CompressConflictInfo, CompressConflictResolution>? conflictResolver,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct)
    {
        // 唯一的一份压缩逻辑
        // 1. 按 Mode 决定循环/单次
        // 2. 检测冲突 → 调 conflictResolver 回调
        // 3. 执行压缩 (Overwrite / Rename → CompressAsync, Add → AddToArchiveAsync)
        // 4. 报告进度
    }
}
```

### 冲突回调（与提取端模式一致）

```csharp
// Core/Abstractions/
public record CompressConflictInfo(
    string OutputPath,
    bool CanAdd,
    string? SuggestedName);

public record CompressConflictResolution(
    CompressConflictAction Action,
    string? CustomName);

public delegate CompressConflictResolution CompressConflictResolver(
    CompressConflictInfo info);
```

### 调用方示例

```csharp
// GUI — CompressSettingsWindow.RunSeparateCompressAsync
var result = await CompressService.CompressAsync(
    request,
    conflictResolver: info =>
    {
        // 闭包内记忆 ApplyToAll
        if (applyToAll && chosenAction.HasValue)
            return new CompressConflictResolution(chosenAction.Value, null);

        var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
        dlg.ShowDialog();
        if (dlg.ApplyToAll) { applyToAll = true; chosenAction = dlg.ResultAction; }
        return new CompressConflictResolution(dlg.ResultAction, dlg.CustomName);
    },
    progress,
    ct);
// 完成后窗口管理由 GUI 自行处理
this.Close();

// CLI — App.Cli.RunCompressSeparateBatch
var result = await CompressService.CompressAsync(
    request,
    conflictResolver: info =>
    {
        var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
        dlg.ShowDialog();
        return new CompressConflictResolution(dlg.ResultAction, dlg.CustomName);
    },
    progress,
    ct);
app.Shutdown();
```

### 差异点处理

| 差异 | 解决方案 |
|---|---|
| GUI 参数来自控件，CLI 来自 AppSettings | 统一到 `CompressRequest` POCO，各自填充 |
| GUI 弹窗用 `Dispatcher.Invoke`，CLI 用 `Dispatcher.InvokeAsync` | 回调在 UI 线程调用，由调用方自行决定线程策略 |
| 窗口管理（Hide/Close vs Shutdown） | Service 不做窗口管理，调用方在 await 前后处理 |
| 注释分配（AllSame/FirstOnly/PerLine） | Service 内部按 `CompressMode` 处理 |

## 实施步骤

### Phase 1 — Core 层准备

1. 在 `MantisZip.Core.Abstractions` 新增：
   - `CompressConflictAction` 枚举（已有，考虑从 UI 移到 Core）
   - `CompressConflictInfo` record
   - `CompressConflictResolution` record
   - `CompressConflictResolver` 委托类型

2. 新增 `CompressService` 类（放在 Core 层或新建 Core/Services）

### Phase 2 — 调用方迁移

3. `RunCompressSeparateBatch` (App.Cli.cs) — 改为调 `CompressService`
4. `RunSeparateCompressAsync` (CompressSettingsWindow.xaml.cs) — 改为调 `CompressService`
5. `RunManualCompressAsync` / `RunCombinedCompressAsync` — 同上（单次压缩）
6. `HandleCompressQuick` / `RunCompressCombined` — 同上

### Phase 3 — 清理

7. 确认所有压缩入口都经过 `CompressService`
8. 删除各调用方内联的冲突处理/压缩循环代码
9. 确认 GUI 和 CLI 行为完全一致

## 优先级

- **P0** — Phase 1（基础设施）
- **P1** — Phase 2（迁移所有路径）
- **P2** — Phase 3（清理）

## 验收标准

- [ ] 所有压缩入口（GUI Manual/Separate/Combined + CLI quick/separate/combined）调用同一个 `CompressService`
- [ ] 冲突处理通过回调注入，调用方不再自己写 switch
- [ ] ApplyToAll 在所有循环路径中行为一致
- [ ] 所有场景行为与改动前一致

---

## 备选方案：冲突 UI 嵌入 ProgressWindow

### 思路

不弹独立的 `CompressConflictDialog` 窗口，而是把冲突交互直接嵌入到 `ProgressWindow` 中。冲突发生时进度窗口变为"冲突模式"——暂停进度、显示冲突按钮组，用户选择后恢复进度继续压缩。

### 机制：TaskCompletionSource 替代 ShowDialog

```
压缩线程                          ProgressWindow（UI 线程）
  │                                    │
  │  检测到冲突                        │  正常显示进度
  │  设置暂停状态                      │
  │  Dispatcher.InvokeAsync ──────→    │
  │                                    │  隐藏进度，显示冲突面板
  │  await tcs.Task (等待)             │  用户点按钮
  │                                    │  tcs.SetResult()
  │  ←── 拿到结果 ──────────           │
  │                                    │  显示进度，隐藏冲突面板
  │  继续压缩                          │
```

### ProgressWindow 冲突面板示意

```
┌───────────────────────────────────────┐
│  📦 正在压缩...              [×]      │
│  ████████░░░░ 60%                    │
│  当前文件: photo.jpg                  │
├───────────────────────────────────────┤
│  ── 文件冲突 ──                      │
│  目标文件 "photo.zip" 已存在          │
│                                       │
│  [覆盖] [添加到压缩包] [重命名] [跳过] │
│  ☑ 对后续所有文件使用相同操作         │
├───────────────────────────────────────┤
│  总进度: ██████░░░░░░ 50%            │
└───────────────────────────────────────┘
```

### 优点

1. **UI 统一** — 压缩冲突和提取冲突都在同一个窗口解决，不再弹独立窗口
2. **体验更好** — 用户看到进度上下文，知道停了在等什么
3. **架构更干净** — `CompressConflictDialog` 可以彻底删除
4. **循环路径天然统一** — GUI 和 CLI 都指向同一个 ProgressWindow，冲突处理路径自然归一

### 缺点 & 风险

1. **改动量大** — 涉及 `ProgressWindow` 的布局改造（增加冲突面板、切换逻辑）
2. **进度状态管理** — 冲突时进度条需要"冻结"当前值，解决后恢复
3. **Manual/Combined 模式** — 这些模式下压缩还没开始就发现冲突，此时 ProgressWindow 还没创建。需要调整顺序——先 Show ProgressWindow，再检查冲突
4. **提取冲突也用 ProgressWindow？** — 提取端的 `ConflictResolver` 被引擎层在后台线程调用，目前也是弹独立窗口。如果要统一，提取冲突也得改，范围更大

### 与主方案的关系

主方案是**逻辑统一**（公共 Service + 回调委托），备选方案是**UI 也统一**（嵌入 ProgressWindow）。两个不冲突，可以分阶段做：

- **Phase 1**：主方案，抽 `CompressService` + `CompressConflictResolver` 回调（逻辑统一）
- **Phase 2**：可选，把回调里的 `CompressConflictDialog` 替换为 `ProgressWindow` 嵌入（UI 统一）
