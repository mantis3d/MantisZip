# 结果预览：展示输出文件 + 冲突检测

## 问题

### 压缩预览

当前 `ResultPreviewService.BuildCompressPreview` 只是把源文件/目录平铺成树，缺少**将要生成的压缩包文件本身**这一层。用户无法直观看到：

- 会生成几个压缩包
- 每个压缩包输出到什么位置
- 压缩包内包含哪些文件
- 目标路径是否已存在同名压缩包（文件冲突）

## 设计

### 预览树层级

根据 `CompressOutputMode` 分两种布局：

#### Manual / Combined（单压缩包）

```
📦 压缩内容
└── 📦 archive.zip                 ← 输出的压缩包文件
    ├── 📁 Docs\                   ← 源文件/目录（去掉了公共前缀的相对路径）
    │   ├── report.docx
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

- 根节点下只有一个压缩包节点
- 压缩包显示文件名（含扩展名）
- 其下是源文件的目录树（同当前 `BuildCompressPreview` 的树，再加一层包装）

#### Separate（每源文件一个压缩包）

```
📦 压缩内容
├── 📁 C:\Users\Docs\              ← 压缩包输出目录（完整路径）
│   ├── 📦 report.zip  ⚠️          ← 已存在冲突
│   │   └── report.docx
│   └── 📦 invoice.zip
│       └── invoice.pdf
└── 📁 D:\Photos\vacation\         ← 不同输出目录
    └── 📦 IMG_001.zip
        └── IMG_001.jpg
```

- 按压缩包实际输出目录分组
- 每个目录节点显示**完整路径**（`DisplayLabel = 全路径`）
- 目录下是该位置要生成的压缩包
- 每组中如有多个压缩包共享同一输出目录，合并到一个目录下

### 压缩包节点属性

| 属性 | 值 |
|------|----|
| `Name` | 文件名（如 `archive.zip`） |
| `FullPath` | 输出完整路径（如 `D:\output\archive.zip`） |
| `DisplayLabel` | 文件名 |
| `Size` | 0（压缩前未知） |
| `IsArchiveNode` | `true`（新增） |
| `IconKey` | `IconArchive`（新增） |
| `ExistsAtDestination` | `File.Exists(FullPath)` 的结果 |
| `Children` | 该压缩包内的源文件/目录树 |

### 新增 IconArchive

在 `AppIcons.axaml` 中新增一个 ZIP 压缩包风格的 PathIcon Geometry（简洁的带拉链文件夹轮廓）。

### 冲突检测

对于每个要生成的压缩包，计算其完整输出路径后调用 `File.Exists()`：

- **Manual/Combined**：检查 `outputPath`（用户指定的路径或自动计算的路径）
- **Separate**：按 `ComputeSeparateOutputPath` 逻辑为每个源路径计算输出路径后逐项检查
- **结果**：存在 → `ExistsAtDestination = true` → TreeView 尾部显示 ⚠️ 红色图标（已有机制）

### 压缩包名称计算

为了在预览中计算输出路径而不依赖 `CompressRequest`，在 ViewModel 层实现轻量版名称推导：

```csharp
string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt)
{
    string baseName;
    if (Directory.Exists(sourcePath))
        baseName = Path.GetFileName(sourcePath.TrimEnd('\\'));
    else
        baseName = keepOriginalExt 
            ? Path.GetFileName(sourcePath) 
            : Path.GetFileNameWithoutExtension(sourcePath);
    string ext = format == "tar.gz" ? ".tar.gz" : "." + format;
    return baseName + ext;
}
```

- **Manual**：用 ViewModel 的 `OutputPath`
- **Combined**：用 ViewModel 计算的组合输出路径（已有 `RefreshOutputPathState` 逻辑）
- **Separate**：`Path.Combine(源父目录, ComputeArchiveName(源路径, 格式))`

## 变更范围

### PreviewTreeNode.cs

```csharp
/// <summary>是否为压缩包节点（显示归档图标）。</summary>
public bool IsArchiveNode { get; set; }
```

`IconKey` 新增分支：

```csharp
if (IsArchiveNode) return "IconArchive";
```

### AppIcons.axaml

新增 `IconArchive` Geometry（压缩包图标）。

### ResultPreviewService.cs

`BuildCompressPreview` 新增参数：

```csharp
public static PreviewTreeNode BuildCompressPreview(
    IReadOnlyList<string> sourcePaths,
    string? rootName = null,
    FileFilterCriteria? filter = null,
    CompressOutputMode outputMode = CompressOutputMode.Manual,
    string? outputPath = null,
    string format = "zip",
    bool keepOriginalExtension = false)
```

构建逻辑改为三层：

1. 根据 `outputMode` 和源路径计算各压缩包的输出路径
2. 检查各路径 `File.Exists` 标记冲突
3. 为每个压缩包创建 `PreviewTreeNode { IsArchiveNode = true }`
4. 将源文件/目录作为子节点添加到对应压缩包节点下
5. Separate 模式下按输出父目录分组，创建中间目录节点

### CompressSettingsViewModel.cs

`BuildCompressPreview` 传递输出模式信息：

```csharp
public void BuildCompressPreview(FileFilterCriteria? filter = null)
{
    // ...
    PreviewRoot = ResultPreviewService.BuildCompressPreview(
        SelectedPaths.ToList(),
        rootName: ...,
        filter: filter,
        outputMode: OutputMode,
        outputPath: OutputPath,
        format: DefaultFormat);
    // Separate 模式下不需要 outputPath（自动计算）
}
```

### CompressSettingsWindow.axaml.cs

保持现有 `BuildPreview()` 和 `OnFileFilterChanged()` 流程不变，自动获取 ViewModel 的 `OutputMode`/`OutputPath`/`DefaultFormat`。

### ResultTreeView.axaml

无需改动。`IconKey` 绑定 + `ExistsAtDestination` 绑定均已存在。

---

## 解压预览设计

### 现状

当前 `BuildExtractPreview` 的结构：

```
📦 archive_name                    ← root: DisplayLabel = destDir名, FullPath = ""
├── 📁 Docs\                       ← 目录节点
│   ├── report.docx  ⚠️            ← 已支持文件冲突
│   └── invoice.pdf
└── 📁 vacation\
    └── IMG_001.jpg
```

- ✅ 已按归档条目构建树
- ✅ 每个文件已支持 `ExistsAtDestination` 检测
- ❌ root 节点的 `FullPath = ""`，`DirectoryInfoText` 不显示统计
- ❌ 目标目录不是树中可见的目录节点，只是 root 的 DisplayLabel 文本
- ❌ 与压缩设计的层级结构不一致

### 设计

与压缩预览统一层级结构：root 是概念容器，目标目录是第一层目录节点。

**Normal 模式解压到 D:\Dest\：**

```
📦 解压结果                        ← root（概念容器，FullPath = ""）
└── 📁 D:\Dest\                    ← 目标目录节点（完整路径）
    ├── 📁 Docs\
    │   ├── report.docx  ⚠️        ← 文件冲突（已实现）
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

**Smart 模式解压到 D:\Dest\archive_name\：**

```
📦 解压结果
└── 📁 D:\Dest\archive_name\       ← 目标目录（含智能解压子目录）
    ├── 📁 Docs\
    │   ├── report.docx  ⚠️
    │   └── invoice.pdf
    └── 📁 vacation\
        └── IMG_001.jpg
```

**多压缩包解压到同一目录：**

```
📦 解压结果
└── 📁 D:\Dest\
    ├── 📁 archive_1\
    │   ├── 📁 Docs\
    │   └── report.docx  ⚠️
    └── 📁 archive_2\
        ├── photos\
        └── ...
```

所有条目合并展示，文件冲突检测独立于来源。

### 目标目录节点属性

| 属性 | 值 |
|------|----|
| `Name` | 目录名（如 `Dest`） |
| `FullPath` | `destDir` 完整路径 |
| `DisplayLabel` | `destDir` 完整路径 |
| `Children` | 提取出的文件/目录树 |
| `IsDirectoryNode` | `true`（自动从 `Children.Count > 0` 推导） |
| `IconKey` | `IconFolder`（已有） |

### 冲突检测

- **文件级冲突**：已实现，`BuildExtractPreview` 中 `checkExists` 参数控制
- **展示**：文件节点尾部 ⚠️ 红色 `IconWarning`，已有绑定
- 目标目录节点本身不检查冲突（目录总是存在或可创建）

### 解压预览变更范围

| 文件 | 改动 |
|------|------|
| `ResultPreviewService.cs` | `BuildExtractPreview` 将 root 改为概念容器，新增目标目录子节点；`root.DisplayLabel` 从 `destDir` 改为固定"解压结果" |
| `ExtractSettingsViewModel.cs` | 无需改动（传参方式不变） |
| `ResultTreeView.axaml` | 无需改动 |

### 与压缩预览对比

| 维度 | 压缩预览 | 解压预览 |
|------|---------|---------|
| 输出包装层 | 压缩包节点（`IsArchiveNode=true`） | 目标目录节点（`IsDirectoryNode=true`） |
| 包装层图标 | `IconArchive`（新增） | `IconFolder`（已有） |
| 包装层显示 | 文件名 | 完整路径 |
| 冲突检测 | 压缩包级 `File.Exists(输出路径)` | 文件级 `File.Exists(目标路径)`（已有） |
| 统计 | 压缩包节点显示 "N 项 · X MB" | 目标目录节点显示 "N 项 · X MB" |

## 整体实现顺序

1. `AppIcons.axaml` — 添加 `IconArchive` Geometry
2. `PreviewTreeNode.cs` — 添加 `IsArchiveNode` + `IconKey` 分支
3. `ResultPreviewService.cs` — 重构 `BuildCompressPreview`（压缩包包装层 + 冲突检测 + 目录分组）
4. `CompressSettingsViewModel.cs` — `BuildCompressPreview` 传递输出模式/路径/格式
5. `ResultPreviewService.cs` — 重构 `BuildExtractPreview`（目标目录节点拆分，保持兼容）
6. `CompressSettingsWindow.axaml.cs` — 传递 Mode 和 OutputPath（现有事件流程不变）
