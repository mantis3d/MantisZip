# 压缩包管理器（Archive Manager）设计规格

> **日期**: 2026-09-16
> **状态**: 设计完成，待实施规划
> **原型**: `docs/superpowers/specs/archive-manager-prototype.html`

---

## 1. 概述

为 MantisZip 新增「压缩包管理器」功能 — 一个独立窗口，统一管理用户通过 MantisZip 处理过的压缩包记录，支持标签分类、目录浏览、缩略图预览。

### 1.1 设计决策摘要

| 决策项 | 选择 | 理由 |
|--------|------|------|
| 窗口模式 | 独立管理器窗口（左导航 + 右详情） | 统一入口，不分散到主界面各处 |
| 数据模型 | 以压缩包为中心（非操作为中心） | 用户关心"有哪些压缩包"，不是"做过什么操作" |
| 标签系统 | 纯手动标签 | YAGNI，自动分类难做好，手动标签最灵活 |
| 数据来源 | 仅 MantisZip 操作记录 | 不扫描文件系统，打开即自动记录 |
| 存储方案 | SQLite + 缩略图文件 | 查询快，关系清晰，缩略图独立文件按需加载 |
| 设计拆分 | 两个独立设计：管理器 + 工具 | 管理器是平台型，Extract Journal / Archive Diff 是工具型 |

### 1.2 功能范围

**本设计覆盖：**
- 压缩包管理器窗口（数据存储 + UI 界面）
- 标签系统（创建/编辑/删除/分配）
- 目录树过滤器（稀疏树，只显示含压缩包的目录）
- 缩略图系统（按需生成，JPEG 存文件）

**本设计不覆盖（独立设计）：**
- Extract Journal & Undo（已有方案：`.omo/plans/未开始/extract-journal-undo.md`）
- Archive Diff（已有方案：`.omo/plans/未开始/archive-diff.md`）
- 未来可能的工具型功能（如压缩包合并、批量转换等）

---

## 2. 数据模型

### 2.1 存储位置

```
%LOCALAPPDATA%\MantisZip\manager\
├── archive-manager.db       ← SQLite 数据库
└── thumbnails\              ← 缩略图文件目录
    ├── {id}.jpg
    └── ...
```

### 2.2 SQLite Schema

```sql
-- 压缩包元数据（核心表）
CREATE TABLE archives (
    id          TEXT PRIMARY KEY,        -- GUID
    path        TEXT UNIQUE NOT NULL,    -- 完整文件路径
    filename    TEXT NOT NULL,           -- 显示文件名（不含路径）
    format      TEXT NOT NULL,           -- zip / 7z / tar.gz / rar ...
    size        INTEGER NOT NULL,        -- 文件大小（字节）
    is_encrypted BOOLEAN DEFAULT 0,      -- 是否加密
    thumbnail_path TEXT,                 -- 缩略图相对路径（nullable）
    first_opened_at TEXT NOT NULL,       -- 首次打开时间（ISO8601）
    last_opened_at  TEXT NOT NULL,       -- 最近打开时间（ISO8601）
    open_count  INTEGER DEFAULT 1        -- 打开次数
);

-- 标签定义
CREATE TABLE tags (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT UNIQUE NOT NULL,
    color TEXT                          -- 可选，标签颜色（hex）
);

-- 压缩包↔标签 多对多
CREATE TABLE archive_tags (
    archive_id TEXT NOT NULL REFERENCES archives(id) ON DELETE CASCADE,
    tag_id     INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (archive_id, tag_id)
);

-- 操作记录（压缩/解压）
CREATE TABLE operations (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    archive_id      TEXT NOT NULL REFERENCES archives(id) ON DELETE CASCADE,
    operation_type  TEXT NOT NULL,       -- 'compress' / 'extract'
    timestamp       TEXT NOT NULL,       -- ISO8601
    source_path     TEXT,                -- 压缩时：源文件列表（JSON array）
    destination_path TEXT,               -- 解压时：目标目录
    file_count      INTEGER,
    total_size      INTEGER,
    success         BOOLEAN DEFAULT 1,
    error_message   TEXT
);
```

### 2.3 数据流

```
用户通过 MantisZip 打开/压缩/解压
    ↓
ArchiveManagerService.RecordOpenAsync(path)
    ↓ INSERT OR REPLACE archives（更新 last_opened_at + open_count）
    ↓
如果是解压操作：
    ↓ INSERT operations (type='extract', destination_path=...)
    ↓ （同时写 ExtractJournal，两个系统独立）
如果是压缩操作：
    ↓ INSERT operations (type='compress', source_path=...)
```

### 2.4 缩略图

- **生成时机**：首次打开压缩包时，用现有 `PreviewService` 图片检测提取第一张图片
- **存储格式**：JPEG，质量 60，最大 200×200px
- **文件路径**：`thumbnails/{id}.jpg`，存于 `archives.thumbnail_path`
- **无缩略图时**：显示格式图标（ZIP/7z/RAR 等）

---

## 3. 管理器窗口

### 3.1 布局

```
┌──────────────────────────────────────────────────────────────┐
│  🗂 压缩包管理器                              [搜索栏] [设置] │
├────────────┬─────────────────────────────────────────────────┤
│  📁 全部(8) │  project.zip                    [🏷️工作][备份] │
│  ⏱ 最近(3) │  路径: D:\Downloads\...                       │
│  ⭐ 常用(4) │  大小: 45.2 MB  格式: ZIP  🔒加密             │
│  ──────── │  打开: 3次  最近: 2026-09-15                    │
│  🏷️ 工作(4)│                                                 │
│  🏷️ 备份(4)│  ┌─ 操作记录 ──────────────────────┐          │
│  🏷️ 照片(2)│  │ 09-15  解压 → D:\Projects\...   │          │
│  🏷️ 重要(2)│  │ 09-10  解压 → D:\Projects\...   │          │
│  ──────── │  │ 09-01  压缩 ← D:\Projects\...   │          │
│  📦 ZIP(6) │  └─────────────────────────────────┘          │
│  📦 7z(1)  │                                                 │
│  📦 RAR(1) │  [打开] [资源管理器] [删除记录]                │
│  ──────── │                                                 │
│  📁 D:\(5) │                                                 │
│   Downloads│                                                 │
│   Backups  │                                                 │
│   Projects │                                                 │
│   Archive  │                                                 │
│   Releases │                                                 │
│  📁 E:\(2) │                                                 │
│  📁 F:\(1) │                                                 │
├────────────┴─────────────────────────────────────────────────┤
│  状态栏: 8 个压缩包 | 总大小: 5.2 GB | 筛选: 全部           │
└──────────────────────────────────────────────────────────────┘
```

### 3.2 左侧导航

| 区域 | 节点 | 点击行为 |
|------|------|---------|
| 智能分组 | 全部 / 最近 7 天 / 常用 (≥3次) | 过滤右侧列表 |
| 标签 | 用户自定义标签 + 「+ 新建标签...」 | 按标签过滤；右键：重命名/删除/改颜色 |
| 格式 | ZIP / 7z / RAR（自动从数据生成） | 按格式过滤 |
| 目录 | 稀疏目录树（只显示含压缩包的目录） | 按目录过滤；可展开子目录 |

**目录树规则：**
- 只到目录层级，不显示压缩包文件名
- 每个目录节点显示子项数量
- 可展开/折叠
- 点击目录 = 过滤右侧列表（与标签/格式同级过滤器）

### 3.3 右侧列表

**两种视图模式：**

| 视图 | 布局 | 信息密度 |
|------|------|---------|
| 详情模式 | 表格行 | 文件名、路径、大小、格式、标签、最近打开、打开次数 |
| 缩略图模式 | 网格卡片 | 缩略图 + 文件名 + 大小/格式 + 标签 |

**排序：** 名称 / 大小 / 最近打开 / 打开次数（三态循环：升序→降序→恢复原始）

**过滤逻辑：** 所有过滤器（标签/格式/目录/智能分组）互斥，同一时间只激活一个。搜索框与过滤器叠加。

### 3.4 右侧详情面板

选中压缩包后显示：
- **头部**：格式图标 + 文件名 + 标签徽章
- **文件信息**：路径、大小、格式、加密状态、打开次数、首次/最近打开时间
- **操作记录**：该压缩包的所有压缩/解压记录（来自 operations 表）
- **编辑标签**：当前标签（可删除）+ 「+ 添加」
- **操作按钮**：打开 / 在资源管理器中显示 / 删除记录

---

## 4. 标签系统

### 4.1 数据

存在 SQLite `tags` 表：`id` + `name`（UNIQUE）+ `color`（可选 hex）。

### 4.2 操作

| 操作 | 入口 | 行为 |
|------|------|------|
| 创建标签 | 左侧「+ 新建标签...」或详情面板「+ 添加」 | 弹出输入框，输入名称 + 选颜色（可选） |
| 删除标签 | 右键标签节点 → 删除 | 级联删除 `archive_tags`，不影响压缩包记录 |
| 重命名 | 右键标签节点 → 重命名 | 直接编辑 |
| 打标签 | 详情面板「+ 添加」 | 插入 `archive_tags` |
| 去标签 | 详情面板标签旁「✕」 | 删除 `archive_tags` 记录 |

### 4.3 约束

- 标签名唯一
- 一个压缩包可有多个标签
- 标签数量无上限

---

## 5. 集成点

### 5.1 与主窗口集成

- **入口**：菜单栏「工具 → 压缩包管理器」或快捷键
- **独立窗口**：非模态，可与主窗口并存
- **启动时**：不自动打开（按需启动）

### 5.2 与 Extract Journal 集成

- 两者独立存储（manager.db vs journal/*.json）
- 管理器的 `operations` 表记录操作摘要
- Extract Journal 的详细文件清单保持不变
- 未来可从管理器跳转到 Extract Journal 的详细记录

### 5.3 与 Archive Diff 集成

- 从管理器选中两个压缩包 → 右键 → 「对比」
- 调用 Archive Diff 引擎，结果在 Diff 窗口展示

### 5.4 数据录入

每次用户通过 MantisZip 打开/压缩/解压时，自动调用 `ArchiveManagerService`：

```csharp
// 在所有 engine.ExtractAsync / engine.CompressAsync 调用成功后：
_ = ArchiveManagerService.RecordOperationAsync(
    archivePath: archivePath,
    operationType: "extract", // 或 "compress"
    destinationPath: destPath,
    sourcePaths: sourcePaths
);
```

---

## 6. 与现有代码的关系

### 6.1 复用

| 现有组件 | 复用方式 |
|---------|---------|
| `PreviewService` 图片检测 | 缩略图生成时提取第一张图片 |
| `PasswordManager` 模式 | SQLite 存储模式参考 |
| `IArchiveEngine.ListEntriesAsync` | 获取压缩包元数据（格式、大小、加密状态） |
| `AppSettings` 模式 | 设置项持久化参考 |

### 6.2 新增组件

| 组件 | 位置 | 职责 |
|------|------|------|
| `ArchiveManagerService` | `Core/Services/` | 数据录入、查询、标签管理 |
| `ArchiveManagerDb` | `Core/Utils/` | SQLite 连接、Schema 管理 |
| `ArchiveManagerWindow` | `UI.Avalonia/Views/` | 管理器窗口 |
| `ArchiveManagerViewModel` | `UI.Avalonia/ViewModels/` | 窗口逻辑 |
| `ThumbnailService` | `Core/Services/` | 缩略图生成与缓存 |

---

## 7. 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| 数据库文件损坏 | 🟡 | 定期备份；损坏时重建（从文件系统验证存在性） |
| 缩略图生成耗时 | 🟢 | 异步生成，不阻塞 UI；首次打开时在后台生成 |
| 文件被移动/删除 | 🟡 | 查询时验证路径存在性，不存在则标记为「已失效」 |
| 跨平台兼容性 | 🟢 | SQLite + 文件路径均为 .NET 跨平台 API |
| 大量记录性能 | 🟢 | SQLite 索引 + 虚拟化列表，百级记录无压力 |

---

## 8. Definition of Done

- [ ] `ArchiveManagerDb` 完成 Schema 创建与迁移
- [ ] `ArchiveManagerService` 完成数据录入、查询、标签 CRUD
- [ ] 管理器窗口 UI 完成（左导航 + 右列表 + 详情面板）
- [ ] 过滤系统完成（标签/格式/目录/智能分组/搜索）
- [ ] 标签系统完成（创建/删除/重命名/分配/去分配）
- [ ] 目录树过滤器完成（稀疏树，只显示含压缩包的目录）
- [ ] 缩略图系统完成（异步生成 + 按需加载）
- [ ] 详情模式 + 缩略图模式切换
- [ ] 排序功能（名称/大小/最近打开/打开次数）
- [ ] 数据录入集成（打开/压缩/解压时自动记录）
- [ ] 预留与 Extract Journal / Archive Diff 的集成接口（数据模型兼容，不实现工具本身）
- [ ] `dotnet build` 通过
- [ ] 原型验证（布局与交互符合设计）
