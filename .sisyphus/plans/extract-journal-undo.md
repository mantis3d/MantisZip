# 提取日志与解压「后悔药」(Extract Journal & Undo)

> 每次解压自动记录提取清单，支持一键回滚删除所有释放的文件
> 状态: 📋 待定（与现有提取流程无冲突，可独立实现）

---

## 动机

主流压缩软件的解压操作是**一次性**的——解压完成后，用户对自己释放了哪些文件没有记录，更无法一键清理。

常见场景：
- 下载了一个 ZIP 解压看看内容，不想用了，但散落了一堆文件
- 解压到错误位置，需要手动去清
- 解压了重复版本，旧文件混杂在新文件里难以分辨

**目标**：每次解压操作自动记录提取清单，提供「提取历史」面板和一键回滚。

---

## 架构设计

### 数据流

```
用户点击解压
    ↓
引擎执行 ExtractAsync
    ↓ (同时记录)
ExtractJournal.Record(archivePath, destinationDir, fileList)
    ↓
写入 %LOCALAPPDATA%\MantisZip\journal\{timestamp}.json
    ↓
用户通过 UI →「提取历史」→ 选中一条记录 →「删除释放的文件」
    ↓
ExtractJournal.Rollback(journalEntry) → 倒序删除文件 + 清理空目录
```

### 存储格式

每条日志一个 JSON 文件，按时间命名：

```json
{
  "id": "20260517T143022_8f3a",
  "archiveName": "项目源码.zip",
  "archivePath": "D:\\Downloads\\项目源码.zip",
  "destinationPath": "D:\\Projects\\项目源码",
  "extractedAt": "2026-05-17T14:30:22.123+08:00",
  "totalSize": 48576012,
  "fileCount": 342,
  "files": [
    {
      "path": "D:\\Projects\\项目源码\\src\\Program.cs",
      "size": 4521,
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "lastWriteTime": "2026-03-10T09:15:00+08:00"
    },
    { "path": "...", ... }
  ]
}
```

`sha256` 用于回滚时确认文件未被修改（被修改过则跳过删除，避免丢数据）。

---

## 改动范围

涉及 **5 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Utils/ExtractJournal.cs` | 🆕 新增 — 日志记录 + 回滚逻辑 | 1.5h |
| `Core/Utils/ExtractJournalManager.cs` | 🆕 新增 — 日志列表、查询、清理 | 0.5h |
| `UI/MainWindow.xaml` | 添加「提取历史」菜单项 | 10min |
| `UI/MainWindow.Menu.cs` | 添加「提取历史」事件处理 | 15min |
| `UI/ExtractHistoryWindow.xaml/.cs` | 🆕 新增 — 历史记录查看与回滚 UI | 1h |

**运行时依赖变更：** 无（只用 `System.Text.Json` + `System.Security.Cryptography`，均已引用）

---

## 实现细节

### `ExtractJournal.cs` (Core)

```csharp
public static class ExtractJournal
{
    private static readonly string JournalDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "journal");

    /// <summary>记录一次解压操作。由 ExtactAsync 的调用方在提取完成后调用。</summary>
    public static async Task RecordAsync(
        string archivePath,
        string destinationPath,
        IReadOnlyList<string> extractedFiles,
        CancellationToken ct = default);

    /// <summary>回滚一条提取记录：删除当时释放的文件。</summary>
    /// <returns>成功删除数 / 跳过数（被修改则跳过）/ 失败数</returns>
    public static Task<RollbackResult> RollbackAsync(
        JournalEntry entry,
        IProgress<RollbackProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>列举所有提取记录。</summary>
    public static Task<List<JournalEntry>> ListEntriesAsync();

    /// <summary>清理超过指定天数的记录。</summary>
    public static Task CleanupAsync(int maxDays = 90);
}
```

**RecordAsync 实现要点：**
- 在 `destinationPath` 下递归扫描所有文件（传入 `extractedFiles` 列表也可直接使用）
- 计算每个文件的 SHA256（可选，用于回滚校验）
- 写入 `{JournalDir}/{timestamp}_{shortId}.json`

**RollbackAsync 实现要点：**
- 按文件路径**倒序**删除（先删深层，再删浅层）
- 删除前校验 SHA256：若匹配则删除；不匹配则跳过（用户已修改）
- 删除文件后，逐级向上检查目录是否为空，为空则删除目录
- 异常处理：单个文件删除失败记录错误，继续下一个

### 集成到现有提取流程

在 `MainWindow.xaml.cs` 和 `App.xaml.cs` 中所有调用 `engine.ExtractAsync` 的地方，在成功后追加：

```csharp
// 提取完成后，异步记录日志（不阻塞 UI）
_ = ExtractJournal.RecordAsync(archivePath, destinationPath, extractedFiles);
```

需要引擎在提取过程中**暴露提取的文件列表**。当前 `ExtractAsync` 不返回列表，需要改造：

**方案 A（推荐）：** `ExtractAsync` 新增参数 `List<string> extractedFiles`，引擎将提取的路径追加到此列表。

**方案 B：** 提取完成后扫描目标目录与压缩包条目比对。效率低，不推荐。

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| 用户删除了文件后又回滚 → 回滚把之后创建的其他文件也删了 | 🟡 | 回滚只删除当时记录中的文件，不做目录级别清除 |
| SHA256 计算增加提取耗时 | 🟢 | 可选特性，默认开启但可在设置中关闭 |
| 大量小文件（10万+）日志文件过大 | 🟢 | 单次提取文件数上限可设，超过则警告 |
| 日志目录无限增长 | 🟢 | `CleanupAsync` 定期清理（默认保留 90 天） |
| 用户手动移动了提取目录 | 🟡 | 回滚时检查路径存在性，不存在则跳过 |
| 跨卷解压（解压到不同磁盘） | 🟢 | 路径记录为绝对路径，不受影响 |

---

## 后续扩展

- **提取概要**：在提取历史中显示每个压缩包的「总大小」「文件数」「提取位置」「解压耗时」
- **快速定位**：点击记录可直接打开目标目录
- **差异比较**：同一压缩包多次解压到同一位置 → 显示增量
