# 压缩包管理器 (Archive Manager)

> 统一管理 MantisZip 打开/压缩/解压过的所有压缩包 — 标签、目录树筛选、缩略图预览
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜] (0/4)

---

## 动机

用户在日常使用 MantisZip 后，缺乏对「处理过的压缩包」的回溯能力：
- 打开过某个压缩包，后来找不到了
- 想按标签（工作/个人/备份）分类管理压缩包
- 想知道最近处理过哪些压缩包、哪些是常用的
- 解压/压缩后忘记输出路径，想回溯操作历史

目前主流压缩软件（WinRAR、7-Zip、Bandizip）均无此类历史管理功能。

**目标：** 提供一个独立窗口，记录所有通过 MantisZip 处理的压缩包，支持手动标签、智能分组（最近/常用）、目录树筛选、缩略图预览。

---

## 设计规格

- **原型**: `docs/prototypes/archive-manager-prototype.html`
- **详细设计**: `docs/superpowers/specs/2026-09-16-archive-manager-design.md`

---

## 架构设计

### 数据存储

SQLite 数据库（`archive-manager.db`），存储在 `%LOCALAPPDATA%\MantisZip\manager\`。

```
数据库路径: %LOCALAPPDATA%\MantisZip\manager\archive-manager.db
缩略图目录: %LOCALAPPDATA%\MantisZip\manager\thumbnails\
```

### 数据库 Schema

```sql
-- 压缩包记录
CREATE TABLE archives (
    id          TEXT PRIMARY KEY,
    path        TEXT UNIQUE NOT NULL,
    filename    TEXT NOT NULL,
    format      TEXT NOT NULL,
    size        INTEGER NOT NULL,
    is_encrypted BOOLEAN DEFAULT 0,
    thumbnail_path TEXT,
    first_opened_at TEXT NOT NULL,
    last_opened_at  TEXT NOT NULL,
    open_count  INTEGER DEFAULT 1
);

-- 标签
CREATE TABLE tags (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT UNIQUE NOT NULL,
    color TEXT
);

-- 压缩包-标签关联（多对多）
CREATE TABLE archive_tags (
    archive_id TEXT NOT NULL REFERENCES archives(id) ON DELETE CASCADE,
    tag_id     INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (archive_id, tag_id)
);

-- 操作记录
CREATE TABLE operations (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    archive_id      TEXT NOT NULL REFERENCES archives(id) ON DELETE CASCADE,
    operation_type  TEXT NOT NULL,
    timestamp       TEXT NOT NULL,
    source_path     TEXT,
    destination_path TEXT,
    file_count      INTEGER,
    total_size      INTEGER,
    success         BOOLEAN DEFAULT 1,
    error_message   TEXT
);
```

### 交互流程

```
MantisZip 执行解压/压缩操作
    ↓
自动记录到 SQLite（ArchiveManagerService.UpsertArchiveAsync）
    ↓
用户打开「压缩包管理器」窗口
    ↓
加载全部记录 → 左侧导航树（智能分组 + 标签 + 格式 + 目录树）
    ↓
用户点击筛选 → 过滤右侧列表 → 点击条目显示详情面板
```

### 窗口布局

```
┌──────────────────────────────────────────────────────────┐
│ 🗂 压缩包管理器    🔍 搜索...          ⚙ ↻             │
├──────────┬───────────────────────────┬───────────────────┤
│ 智能分组 │ ☰ 详情  ⊞ 缩略图         │   选中压缩包详情  │
│ ● 全部   ├───────────────────────────┤                   │
│ ● 最近7天│ 📦 photo.zip              │ photo.zip         │
│ ● 常用   │   D:\Photos\photo.zip     │ D:\Photos\photo  │
│          │   12.5 MB | ZIP | 05-16   │ ZIP | 12.5 MB    │
│ 标签     │                           │ 最近打开: 05-16   │
│ ● 工作   ├───────────────────────────┤ 打开次数: 5       │
│ ● 备份   │ 📦 project.7z             │                   │
│          │   D:\Code\project.7z      │ 🏷 工作           │
│ 格式     │   45.2 MB | 7Z | 05-15   │ [编辑标签]        │
│ ● ZIP (3)│                           │                   │
│ ● 7z (1) ├───────────────────────────┤ 📂 操作历史       │
│          │ 📦 backup.zip             │ 解压 → D:\Backup  │
│ 目录     │   D:\Backup\backup.zip    │ 压缩 ← D:\Files  │
│ ● D:\    │   8.1 MB | ZIP | 05-14    │                   │
│   ● Photos│                          │                   │
│   ● Code │                           │                   │
├──────────┴───────────────────────────┴───────────────────┤
│ 4 个压缩包 │ 总大小: 65.8 MB │ 筛选: 全部               │
└──────────────────────────────────────────────────────────┘
```

---

## 任务清单

- [ ] **1. Core: `ArchiveManagerDb`** — SQLite 数据库连接 + Schema 初始化
- [ ] **2. Core: `ArchiveManagerService` — Archive CRUD** — 压缩包记录增删查改
- [ ] **3. Core: `ArchiveManagerService` — Tag CRUD** — 标签管理 + 关联
- [ ] **4. Core: `ArchiveManagerService` — Operation Recording** — 操作记录写入/查询
- [ ] **5. UI: `ArchiveManagerViewModel`** — 窗口逻辑 + 筛选 + 搜索
- [ ] **6. UI: `ArchiveManagerWindow`** — UI 布局（AXAML）
- [ ] **7. UI: 目录树筛选** — 稀疏目录树（仅包含压缩包的目录）
- [ ] **8. UI: 集成 — 自动记录** — 在解压/压缩流程中自动调用 RecordOperation
- [ ] **9. UI: 菜单入口** — 在主窗口添加「压缩包管理器」菜单项
- [ ] **10. 验证: 端到端冒烟测试** — 构建 + 运行 + 验证全流程

---

## 改动范围

涉及 **7 个文件**（新增 5，修改 2）：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Utils/ArchiveManagerDb.cs` | 🆕 新增 — SQLite 连接 + Schema | 30min |
| `Core/Services/ArchiveManagerService.cs` | 🆕 新增 — CRUD + 操作记录 | 1.5h |
| `UI/ViewModels/ArchiveManagerViewModel.cs` | 🆕 新增 — 窗口逻辑 | 1h |
| `UI/Views/ArchiveManagerWindow.axaml` | 🆕 新增 — UI 布局 | 1h |
| `UI/Views/MainWindow.axaml` | 修改 — 添加菜单项 | 10min |
| `UI/ViewModels/MainWindowViewModel.cs` | 修改 — 添加打开命令 | 10min |
| `tests/MantisZip.Tests/ArchiveManagerTests.cs` | 🆕 新增 — Core 层单元测试 | 1h |

**运行时依赖变更：** `Microsoft.Data.Sqlite`（项目已有）

---

## 实现细节

### Task 1: ArchiveManagerDb — SQLite Schema & Connection

**文件**: `src/MantisZip.Core/Utils/ArchiveManagerDb.cs` (🆕), `tests/MantisZip.Tests/ArchiveManagerTests.cs` (🆕)

- [ ] Step 1: 写失败测试 — `OpenOrCreate_CreatesAllTables`，验证 4 张表存在
- [ ] Step 2: 写失败测试 — `OpenOrCreate_IsIdempotent`，重复打开不报错
- [ ] Step 3: 写实现 — `ArchiveManagerDb` 类，构造函数打开连接 + `EnsureSchema()`
- [ ] Step 4: 测试通过
- [ ] Step 5: Commit `feat(core): add ArchiveManagerDb with SQLite schema`

### Task 2: ArchiveManagerService — Archive CRUD

**文件**: `src/MantisZip.Core/Services/ArchiveManagerService.cs` (🆕)

- [ ] Step 1: 写失败测试 — `UpsertArchive_InsertsNewRecord` / `_UpdatesExistingRecord` / `DeleteArchive_RemovesRecord`
- [ ] Step 2: 写实现 — `UpsertArchiveAsync` / `GetAllArchivesAsync` / `DeleteArchiveAsync`
- [ ] Step 3: 测试通过
- [ ] Step 4: Commit `feat(core): add ArchiveManagerService with archive CRUD`

### Task 3: ArchiveManagerService — Tag CRUD

**文件**: `src/MantisZip.Core/Services/ArchiveManagerService.cs` (修改)

- [ ] Step 1: 写失败测试 — `CreateTag_AddsTagRecord` / `DeleteTag_RemovesTagAndAssociations` / `AssignTag_CreatesAssociation` / `UnassignTag_RemovesAssociation`
- [ ] Step 2: 写实现 — `CreateTagAsync` / `DeleteTagAsync` / `GetAllTagsAsync` / `AssignTagAsync` / `UnassignTagAsync` / `GetTagsForArchiveAsync`
- [ ] Step 3: 测试通过
- [ ] Step 4: Commit `feat(core): add tag CRUD to ArchiveManagerService`

### Task 4: ArchiveManagerService — Operation Recording

**文件**: `src/MantisZip.Core/Services/ArchiveManagerService.cs` (修改)

- [ ] Step 1: 写失败测试 — `RecordOperation_InsertsRecord` / `_CompressWithSourcePaths`
- [ ] Step 2: 写实现 — `RecordOperationAsync` / `GetOperationsAsync`
- [ ] Step 3: 测试通过
- [ ] Step 4: Commit `feat(core): add operation recording to ArchiveManagerService`

### Task 5: ArchiveManagerViewModel — Core Logic

**文件**: `src/MantisZip.UI.Avalonia/ViewModels/ArchiveManagerViewModel.cs` (🆕)

- [ ] Step 1: 实现 ViewModel — `LoadAsync` / `SetFilter` / `ApplyFilter` / 搜索 / 目录树构建
- [ ] Step 2: `dotnet build` 通过
- [ ] Step 3: Commit `feat(avalonia): add ArchiveManagerViewModel with filtering`

### Task 6: ArchiveManagerWindow — UI Layout

**文件**: `src/MantisZip.UI.Avalonia/Views/ArchiveManagerWindow.axaml` (🆕), `.axaml.cs` (🆕)

- [ ] Step 1: 创建 AXAML — 三栏布局（左导航 + 右列表 + 详情面板）+ 状态栏
- [ ] Step 2: `dotnet build` 通过
- [ ] Step 3: Commit `feat(avalonia): add ArchiveManagerWindow layout`

### Task 7: Directory Tree Filter — Sparse Tree

**文件**: `src/MantisZip.UI.Avalonia/ViewModels/ArchiveManagerViewModel.cs` (修改)

- [ ] Step 1: 实现 `BuildDirTree` — 从压缩包路径构建稀疏目录树
- [ ] Step 2: `dotnet build` 通过
- [ ] Step 3: Commit `feat(avalonia): implement sparse directory tree builder`

### Task 8: Integration — Auto-Record on Extract/Compress

**文件**: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` (修改)

- [ ] Step 1: 在 App 启动初始化 `ArchiveManagerService`，在解压/压缩成功后调用 `RecordOperationAsync`
- [ ] Step 2: `dotnet build` 通过
- [ ] Step 3: Commit `feat(avalonia): integrate ArchiveManager auto-recording`

### Task 9: Menu Entry — Open Manager Window

**文件**: `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` (修改), `ViewModels/MainWindowViewModel.cs` (修改)

- [ ] Step 1: 在工具菜单添加「压缩包管理器」菜单项 + `OpenManagerCommand`
- [ ] Step 2: `dotnet build` 通过
- [ ] Step 3: Commit `feat(avalonia): add menu entry to open Archive Manager`

### Task 10: End-to-End Smoke Test

- [ ] Step 1: `dotnet build src/MantisZip.UI.Avalonia/` 通过
- [ ] Step 2: 运行应用 → 打开 ZIP → 打开管理器 → 验证记录出现
- [ ] Step 3: 验证标签、筛选、详情面板工作正常
- [ ] Step 4: Commit `feat(avalonia): Archive Manager - initial implementation complete`

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| SQLite 数据库损坏（异常关机） | 🟢 | WAL 模式 + 事务写入 |
| 缩略图生成影响性能 | 🟡 | 后台线程异步生成，队列限流 |
| 目录树节点过多导致卡顿 | 🟡 | 仅显示有压缩包的目录（稀疏树） |
| 跨平台路径分隔符 | 🟢 | 使用 `Path.DirectorySeparatorChar` |
| 数据库迁移（未来 Schema 变更） | 🟡 | 初版简单，后续可加版本号 + 迁移脚本 |

---

## 后续扩展

- **自动分类标签**：基于路径/文件名模式自动打标签（需用户确认）
- **导入导出**：标签和记录的 JSON 导入导出
- **批量操作**：多选压缩包批量打标签/删除记录
- **与 Extract Journal 联动**：显示完整解压历史（时间线视图）
- **与 Archive Diff 联动**：对比管理器中的两个压缩包

---

## Definition of Done

- [ ] `ArchiveManagerDb` 完成 Schema 初始化 + 连接管理
- [ ] `ArchiveManagerService` 完成 Archive/Tag/Operation CRUD
- [ ] `ArchiveManagerViewModel` 完成筛选、搜索、目录树
- [ ] `ArchiveManagerWindow` UI 布局完成（三栏 + 状态栏）
- [ ] 在解压/压缩流程中自动记录操作
- [ ] 主窗口菜单项可打开管理器
- [ ] 端到端冒烟测试通过
- [ ] `dotnet build` 通过
- [ ] `dotnet test tests/MantisZip.Tests/` 通过
