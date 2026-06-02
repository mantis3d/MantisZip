# MantisZip — 开发计划

> 未来待开发功能规划。已实现功能请见 [docs/PROGRESS.md](docs/PROGRESS.md)，技术架构请见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

**项目状态**: 🟢 开发中  
**最后更新**: 2026-06-02  
**当前版本**: 0.3.7-refined-2

---

## 近期（P2）

| 任务 | 说明 |
|------|------|
| 文本预览语法高亮 | 用 AvalonEdit 替换当前 TextBox，支持 20+ 语言语法高亮（C#/Python/XML/HTML/SQL/JS 等） |
| 压缩包内重命名/移动 | 右键「重命名」/「移动到…」、F2 快捷键、extract→delete→add 流程；支持 ZIP/7z |
| RAR 压缩（外置 rar.exe/WinRAR.exe） | 通过已安装的 WinRAR 实现 RAR 格式压缩；支持固实/恢复记录/加密/分卷 |

## 远期（P3）

| 任务 | 说明 | 工作量 |
|------|------|--------|
| **VirtualFileDataObject** | COM 原生 IDataObject 替代 WPF 包装，拖拽延迟渲染不崩溃 | 中 |
| 右键菜单目录结构预览 | 在 COM 菜单中读取压缩包 entry 列表，展示文件树（Bandizip 风格） | 高 |
| 外部工具视频元数据 | ffprobe 提取时长/分辨率/编码 | 低 |
| 发布 Release | GitHub Releases + 自动构建 | 低 |

---

## 待实现设计方案

以下功能已有独立方案设计文档（`.sisyphus/plans/`），按优先级排序。

| 优先级 | 功能 | 设计文档 | 难度 | 预估工时 | 说明 |
|--------|------|----------|:----:|:--------:|------|
| **P2** | 便携版模式 | [portable-mode.md](.sisyphus/plans/portable-mode.md) | 🟢低 | 1-2h | 哨兵文件触发，路径重定向到 exe 目录 |
| **P2** | 魔数识别（内容检测替代扩展名检测） | [preview-magic-detection.md](.sisyphus/plans/preview-magic-detection.md) | 🔴高 | 6-8h | 按真实内容（非扩展名）判断格式 |
| **P2** | 提取日志与解压「后悔药」 | [extract-journal-undo.md](.sisyphus/plans/extract-journal-undo.md) | 🟡中 | 3-4h | 解压记录 + 一键回滚 |
| **P2** | 文件列表筛选/搜索 | -- | 🟢低 | 1-2h | 搜索框实时过滤 |
| **P2** | 压缩解压文件筛选 | [file-filter-feature.md](.sisyphus/plans/file-filter-feature.md) | 🟢低 | 1-2h | 压缩解压文件筛选 |
| **P2** | MSI 安装包 (WiX) | [msi-packaging-wix.md](.sisyphus/plans/msi-packaging-wix.md) | 🟡中 | 2-3h | Inno Setup → WiX MSI 迁移 |
| **P2** | RAR 压缩（外置 rar.exe） | [rar-compression.md](.sisyphus/plans/rar-compression.md) | 🟡中 | 6-8h | 通过已安装的 WinRAR 实现 RAR 压缩 |
| **P2** | 压缩包内重命名/移动条目 | [archive-rename-entry.md](.sisyphus/plans/archive-rename-entry.md) | 🟡中 | 3-4h | 右键重命名(F2)/移动到… |
| **P2** | 路径快速选择 | [explorer-path-switcher.md](.sisyphus/plans/explorer-path-switcher.md) | 🟢低 | 1-2h | Ctrl+G 唤出资源管理器路径列表 |
| **P2** | 压缩/解压配置预设 | [compress-preset.md](.sisyphus/plans/compress-preset.md) | 🟡中 | 3-4h | 命名预设保存全部设置 |
| **P2** | 压缩流程统一化 (CompressService) | [compress-service-unify.md](.sisyphus/plans/compress-service-unify.md) | 🟡中 | 4-6h | GUI/CLI 压缩路径统一为 CompressService |
| **P3** | 压缩预估 (Compression Estimator) | [compression-estimator.md](.sisyphus/plans/compression-estimator.md) | 🟡中 | 4-5h | 压缩前估算大小/耗时 |
| **P3** | VirtualFileDataObject | [virtual-file-data-object.md](.sisyphus/plans/virtual-file-data-object.md) | 🔴高 | 6-8h | COM 原生 IDataObject 替代 WPF OLE 桥 |
| **P3** | 压缩包对比 (Archive Diff) | [archive-diff.md](.sisyphus/plans/archive-diff.md) | 🟡中 | 3-4h | 压缩包文件级差异对比 |
| **P3** | 可插拔预览模块体系 | [preview-modular-providers.md](.sisyphus/plans/preview-modular-providers.md) | 🟡中 | 3-4h | 格式类库独立分发 |
| **P3** | 发布 Release | — | 🟢低 | 1-2h | GitHub Releases + CI 构建 |
| **P3** | 预览格式扩展 — RTF | — | 🟢低 | 1-2h | WPF RichTextBox |
| **P3** | 预览格式扩展 — LNK | — | 🟡中 | 2-3h | IShellLink 解析 |
| **P3** | 预览格式扩展 — ZIP 嵌套 | — | 🟡中 | 3-4h | extract→re-LoadArchiveAsync |
| **P3** | 右键菜单目录结构预览 | — | 🔴高 | 6-8h | COM 菜单中展示文件树 |
| **P3** | 外部工具视频元数据 | — | 🟢低 | 2-3h | ffprobe 集成 |

---

*此文档将随开发进度持续更新*


