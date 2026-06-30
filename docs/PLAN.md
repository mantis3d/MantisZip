# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-06-20  
**当前版本**: 0.4.2

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.sisyphus/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P0** | ZIP 压缩流直拷优化 | [zip-copy-mode-optimization.md](.sisyphus/plans/zip-copy-mode-optimization.md) | 🟡中 | 2-3天 | 添加/删除文件时直拷已有条目的压缩数据，避免全量解压再压缩 |
| **P0** | 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | 🟢低 | 1-2h | 哨兵文件触发，路径重定向到 exe 目录 |
| **P0** | CLI/右键菜单权限不足时提权 | [uac-elevation-permission.md](.sisyphus/plans/uac-elevation-permission.md) | 🟡中 | 4-5h | 检测目标目录可写性；默认仅提示不可写目录，设置中开启"允许提升权限"后才弹 UAC 提权 |
| **P1** | 统一路径快捷选择 (QuickPathControl) | [quick-path-control.md](.sisyphus/plans/quick-path-control.md) | 🟡中 | 4-6h | 收藏/历史/资源管理器窗口三合一控件；系统路径（桌面/文档/下载）可隐藏不可删除 |
| **P1** | Win11 一级右键菜单 | [win11-first-level-menu.md](.sisyphus/plans/win11-first-level-menu.md) | 🔴高 | 1-2周 | IExplorerCommand 实现，HKLM 提权注册，双接口共存 |
| **P1** | 压缩解压文件筛选 | [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | 🟢低 | 1-2h | 压缩解压文件筛选 |
| **P1** | 自包含体积优化（Avalonia 迁移后） | [selfcontained-size-optimization.md](.sisyphus/plans/selfcontained-size-optimization.md) | 🟡中 | 4-6h | 三步渐进：InvariantGlobalization → 保守修剪 → 激进修剪，目标降至 20–25 MB |
| **P2** | 魔数识别（内容检测替代扩展名检测） | [preview-magic-detection.md](.sisyphus/plans/preview-magic-detection.md) | 🔴高 | 6-8h | 按真实内容（非扩展名）判断格式 |
| **P2** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.sisyphus/plans/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.sisyphus/plans/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.sisyphus/plans/rar-compression.md) | 🟡中 | 6-8h | 通过已安装的 WinRAR 实现 RAR 压缩 |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.sisyphus/plans/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.sisyphus/plans/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.sisyphus/plans/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.sisyphus/plans/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 原生图标 DLL | [icon-dll.md](.sisyphus/plans/icon-dll.md) | 🟡中 | 2-3h | 将 7 个 .ico 编译进原生资源 DLL，消除路径依赖 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | 格式类库独立分发 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.sisyphus/plans/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | 冻结列（水平滚动时列固定） | [frozen-column.md](.sisyphus/plans/frozen-column.md) | 🟢低 | 1-2h | 右键列标题冻结/取消冻结，分隔线，设置持久化 |
| **P3** | Office 文档内容预览增强 | [office-content-preview.md](.sisyphus/plans/office-content-preview.md) | 🟡中 | 6-8h | docx/xlsx/pptx 从仅元数据扩展到富文本/表格/幻灯片文本渲染 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.sisyphus/plans/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | — | 🔴高 | 6-8h | COM 菜单中展示文件树 |
| **P1** | Avalonia 拖拽直接解压 | [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md) | 🟡中 | 4-6h | Drop 后 WindowFromPoint+ShellWindows 检测目标路径直接解压，跳过 OLE CF_HDROP |
| **P1** | Avalonia Phase 10: WPF 功能补齐 | [avalonia-phase10-feature-parity.md](.sisyphus/plans/avalonia-phase10-feature-parity.md) | 🟡中 | 3-5天 | 文件列表进度条、预览信息面板、状态栏增强 |
| **P4** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **🔍调研** | 跨平台移植可行性 | [cross-platform-port.md](.sisyphus/plans/cross-platform-port.md) | 🟡中大 | 2-3月 | 砍 ShellExt，WPF→Avalonia，WebView2→WebKit，SharpSevenZip→SharpCompress/p7zip，DPAPI→AES-GCM |

---


## 已废弃方案

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |废弃原因 |
|--------|------|----------|:----:|:--------:|------|------|
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.sisyphus/plans/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 | 跨平台移植（Avalonia）后不再依赖 WPF OLE 桥，无 OLE CF_HDROP bug，VFDO 无存在必要 |
| **P2** | 文本预览语法高亮 | [text-preview-syntax-highlighting.md](.sisyphus/plans/text-preview-syntax-highlighting.md) | 🟡中 | 5-7h | AvalonEdit 替换 TextBox，支持 20+ 语言语法高亮 | AvalonEdit 是 WPF-only 控件，跨平台移植（Avalonia）后需使用 AvaloniaEdit 完全重写 |
| **P4** | 拖拽提取目标检测 | [drag-drop-marker-target.md](.sisyphus/plans/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录 | 被 [drag-drop-direct-extract.md](.sisyphus/plans/drag-drop-direct-extract.md) 取代——WindowFromPoint+ShellWindows 更直接可靠 |

---

## 跨平台移植影响分析

> 对 `.sisyphus/plans/` 下全部 43 个计划进行的 WPF→Avalonia 兼容性评估。（2026-06-15）

跨平台移植（WPF→Avalonia + 砍 ShellExt）对现有计划的影响分三类：

| 影响等级 | 数量 | 含义 |
|---------|:----:|------|
| 🟢 无影响 | 7 | Core 层纯 C# 逻辑，开箱即用 |
| 🟡 需调整 | 22 | Core 逻辑可复用，UI 层（XAML/控件）需移植到 Avalonia |
| 🔴 冲突 | 14 | 依赖 COM/注册表/Shell API/WPF 独占控件，需完全重写或平台替代方案 |

### 🟢 无影响（7 个）

`i18n-localization.md`、`log-privacy-redaction.md`、`portable-mode.md`、`preview-magic-detection.md`、`preview-modular-providers.md`、`remove-sharpziplib.md`、`zipengine-sharpcompress-migration.md`

### 🟡 需调整 — Core 可复用，UI 需移植（22 个）

`about-window-redesign.md`、`archive-add-delete.md`、`archive-diff.md`、`archive-loading-progress.md`、`archive-rename-entry.md`、`batch-progress-list.md`、`compress-preset.md`、`parent-directory-entry.md`（Phase1）、`compress-service-unify.md`、`compression-estimator.md`、`custom-columns.md`、`design-compress-password-tab.md`、`engine-unification-sharpcompress.md`、`extract-journal-undo.md`、`file-filter-feature.md`、`file-list-filter-search.md`、`file-size-progress-bar.md`、`ico-file-icon-extract.md`、`office-content-preview.md`、`png-transparency-3way.md`、`preview-extended-formats.md`、`smart-extract.md`、`split-compress.md`

### 🔴 冲突 — 需完全重写或废弃（14 个）

`com-context-menu.md`、`com-migration-mapping.md`、`dark-theme.md`、`embedded-thumbnail-preview.md`、`extract-settings-window.md`、`file-assoc-per-extension.md`、`icon-dll.md`、`msi-packaging-wix.md`、`quick-path-control.md`、`quickpath-unified.md`、`rar-compression.md`、`text-preview-syntax-highlighting.md`、`virtual-file-data-object.md`

### 关键发现

1. **🔴 的共性**：全部依赖 Windows Shell API（COM/注册表/P/Invoke）或 WPF 独占控件。跨平台后这些功能要么砍掉（COM 右键菜单），要么需要 OS 级不同实现（Linux `.desktop` actions / macOS `NSExtension`）。
2. **🟡 的规律一致**：Core 层的解析器/算法/模型全是纯 C# 可复用，只有 UI 渲染层（WPF XAML + 控件）需要移植到 Avalonia 等价物。
3. **🟢 的 7 个计划**：基本是纯工具代码（正则、IO、字节操作）或刻意保持平台无关的接口设计。
4. **最值得跨平台前实现的计划**：`compress-service-unify.md`——其架构刻意把逻辑集中到 Core 层，减少平台 UI 面。
5. **Core 层遗留问题**：`PasswordManager`（DPAPI → 需 `IDataProtector` 抽象）和 `SevenZipEngine`（7z.dll Windows-only）需要在跨平台前解决。

*此文档将随开发进度持续更新*


