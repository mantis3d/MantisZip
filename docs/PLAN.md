# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-06-09  
**当前版本**: 0.3.13

---


## 待实现设计方案

以下功能已有独立方案设计文档（`.sisyphus/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P2** | 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | 🟢低 | 1-2h | 哨兵文件触发，路径重定向到 exe 目录 |
| **P2** | 魔数识别（内容检测替代扩展名检测） | [preview-magic-detection.md](.sisyphus/plans/preview-magic-detection.md) | 🔴高 | 6-8h | 按真实内容（非扩展名）判断格式 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.sisyphus/plans/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P2** | 压缩解压文件筛选 | [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | 🟢低 | 1-2h | 压缩解压文件筛选 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.sisyphus/plans/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.sisyphus/plans/rar-compression.md) | 🟡中 | 6-8h | 通过已安装的 WinRAR 实现 RAR 压缩 |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.sisyphus/plans/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 统一路径快捷选择 (QuickPathControl) | [quick-path-control.md](.sisyphus/plans/quick-path-control.md) | 🟡中 | 4-6h | 收藏/历史/资源管理器窗口三合一控件；系统路径（桌面/文档/下载）可隐藏不可删除 |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.sisyphus/plans/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 压缩流程统一化 (CompressService) | [compress-service-unify.md](.sisyphus/plans/compress-service-unify.md) | 🟡中 | 4-6h | GUI/CLI 压缩路径统一为 CompressService |
| **P2** | ZipEngine 完全迁移到 SharpCompress | [zipengine-sharpcompress-migration.md](.sisyphus/plans/zipengine-sharpcompress-migration.md) | 🟡中 | 4h | CompressAsync / AddToArchiveAsync / DeleteEntriesAsync 从 SharpZipLib 迁移到 SharpCompress，彻底移除依赖 |
| **P2** | 文本预览语法高亮 | [text-preview-syntax-highlighting.md](.sisyphus/plans/text-preview-syntax-highlighting.md) | 🟡中 | 5-7h | AvalonEdit 替换 TextBox，支持 20+ 语言语法高亮，主题色联动 |
| **P2** | 嵌入缩略图预览 | [embedded-thumbnail-preview.md](.sisyphus/plans/embedded-thumbnail-preview.md) | 🟢低 | 2-3天 | MetadataExtractor(RAW) + Shell API(通用) 两层提取嵌入缩略图；完成后可扩展文件列表缩略图模式 |
| **P3** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.sisyphus/plans/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.sisyphus/plans/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 原生图标 DLL | [icon-dll.md](.sisyphus/plans/icon-dll.md) | 🟡中 | 2-3h | 将 7 个 .ico 编译进原生资源 DLL，消除路径依赖 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | 格式类库独立分发 |
| **P3** | 发布 Release | — | 🟢低 | 1-2h | GitHub Releases + CI 构建 |
| **P3** | 文件列表自定义列 | [custom-columns.md](.sisyphus/plans/custom-columns.md) | 🟡中 | 4-6h | 可自定义显示文件元数据列（文档标题、图片尺寸等） |
| **P3** | Office 文档内容预览增强 | [office-content-preview.md](.sisyphus/plans/office-content-preview.md) | 🟡中 | 6-8h | docx/xlsx/pptx 从仅元数据扩展到富文本/表格/幻灯片文本渲染 |
| **P3** | ICO 文件自身图标显示 | [ico-file-icon-extract.md](.sisyphus/plans/ico-file-icon-extract.md) | 🟢低 | 2-3h | ico 文件列表显示自身嵌入图标 |
| **P3** | 右键菜单目录结构预览 | — | 🔴高 | 6-8h | COM 菜单中展示文件树 |
| **P3** | 拖拽提取目标检测 | [drag-drop-marker-target.md](.sisyphus/plans/drag-drop-marker-target.md) | 🟡中 | 1-3h | Marker 文件探测拖放目标目录（方案待定，需对比 VFDO 后决策） |
| **P3** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |
| **🔍调研** | 跨平台移植可行性 | [cross-platform-port.md](.sisyphus/plans/cross-platform-port.md) | 🟡中大 | 2-3月 | 砍 ShellExt，WPF→Avalonia，WebView2→WebKit，SharpSevenZip→SharpCompress/p7zip，DPAPI→AES-GCM |

---

*此文档将随开发进度持续更新*


