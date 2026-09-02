# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF → Avalonia 迁移中)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 10 + WPF → Avalonia 迁移中 + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.5.0
- **发布日期**: 2026-07-22

## 变更记录结构

> 本文档仅保留**里程碑级**变更（新功能上线、架构级变更、重大 bug 修复）。逐条详细变更（含小 bugfix / i18n / 样式 / 计划类）归档至独立文档：
>
> - **[progress-avalonia-detail.md](progress-avalonia-detail.md)** — Avalonia 版 + 共享层逐条详情（按日期/版本从新到旧）
> - **[progress-wpf.md](progress-wpf.md)** — WPF 遗留版完整历史（按版本从新到旧）
>
> 新增条目规则（AGENTS.md 规则 3）：里程碑级变更在本文档对应月份/版本下追加一行；其余变更追加到 `progress-avalonia-detail.md`。

---

### MantisZip.UI.Avalonia（主力版）— 里程碑

按月分组，每月按日期从新到旧排列。

#### 2026-09

- **09-02** — 修复 ShellExt 复制目标 RID 路径 bug（阻断发布构建：.NET 10 RID 传播使 ShellExt 输出落入 `win-x64` 子目录，publish 报 MSB3030）

#### 2026-08

- **08-24** — WPF 对比审计补齐：7z.dll 运行时缺失引导弹窗（含启动接线修复——设置里的 SevenZipPath 此前从未灌入引擎）+ CLI 补 `--help`/`--test`/未知参数告警
- **08-23** — 加密压缩包交互对齐 WPF：工具栏「密码」按钮三态化（禁用钥匙/红锁待输入/绿开锁已匹配，点击输入或查看当前包密码）+ 可列出加密包取消密码后仍可浏览（未匹配状态，补输密码后解锁）+ 加密条目预览直接提示需要密码
- **08-21** — 文件列表列排序增强：三态循环（升→降→未排序恢复原始顺序）+ 列头箭头状态显示 + 排序状态持久化（window.json 与 WPF 兼容）+ 重填后自动重排 + 压缩率列排序 bug 修复
- **08-20** — WPF 差异补齐 P1 清零：智能打开路径（`SmartOpenPathResolver` + 选中条目/拖拽/主流程三处接线 + `ExtractArchive` 死代码修复）+ 便携模式 Temp 目录重定向（`AppSettings.GetTempDir` + 4 处替换）
- **08-20** — PPTX 预览修复：幻灯片顺序改用 presentation.xml 权威播放顺序（字典序错位修复）+ 占位符无 xfrm 时从 slideLayout 借位几何（标题/章节页不再空白）+ 分组形状坐标递归变换
- **08-20** — XLSX 预览改为纯表格还原：不提取列标题（列名统一 Column1..N），所有行（含合并大标题行）原样展示，保留定位式列数与行列上限
- **08-20** — 拖拽/右键「解压选中项」流程完全统一：`ExtractFlow.RunSelectedItemsExtractionAsync` 共享方法（压缩包一行批处理列表 + 状态驱动 + 失败统一弹窗）
- **08-19** — 拖拽添加到压缩包：WPF `Window_Drop` 三分支完整移植 + 复用解压冲突处理 + `AddFilesToArchiveAsync` 公共方法 + DragAddOverlay 覆层
- **08-18** — 图片预览能力系统：透明/动画能力注册表 + GIF 透明 + Animated WebP 预览
- **08-13** — 预览面板位置/显隐全面修复（四种布局移植 + 三入口统一 + 架构 bug 修复）
- **08-11** — 压缩/解压启动即时反馈（收集弹窗、加载遮罩、设置弹窗秒现）
- **08-09** — 压缩包注释读取展示（ZIP + RAR5，根目录预览）
- **08-08** — 便携模式（Portable.txt → Data/）+ 目录树「自动展开」开关
- **08-07** — 压缩/解压 CLI 流程对齐 + CompressFlow/ExtractFlow 公共流程抽取 + path-manifest A/B 数据集实施
- **08-05** — 解压路径统一（ExtractPathResolver 单一事实源）+ 进度窗口全面对齐 WPF
- **08-04** — 主题三态化（跟随系统/亮/暗）
- **08-03** — QuickPathPicker 自包含路径速选控件 + 默认路径优先级（context/explorer/recent/custom）
- **08-03** — 目录行聚合显示（大小=子树和 / 日期=最新 / 压缩后大小）+ 拖拽/右键解压流程统一
- **08-01** — 文件选择器多选（PickItems 模式：勾选累积 + 跨目录保留）

#### 2026-07

- **07-31** — QuickPathControl Tab 式速选面板重构 + CustomFilePickerDialog 统一路径选择 + 拖拽光标方案 C（自实现 OLE 拖拽）
- **07-27** — 元数据面板可配置系统（MetadataRegistry + 渲染引擎 + 设置 UI）+ 密码子系统全面补齐
- **07-23** — 拖拽系统重构（Avalonia DragDrop + DragDropService 后置解压 + 覆盖层）
- **07-22** — P1-3 FileFilterEditor 移植（三维过滤 + 预设管理 + 临时预设）
- **07-21** — Office 文档内容预览（DOCX/XLSX/PPTX 纯文本 + 表格）
- **07-20** — Shell/COM 集成移植（ShellIntegration + 文件关联 + 右键菜单 + COM host）+ 双击行为 CLI 分发
- **07-18** — emoji→PathIcon 替换（Phase 2）+ 文件列表行图标改用系统原生
- **07-17** — 紧凑度模式 + 上下文工具栏 + 结果预览面板
- **07-16** — 移除 WebView2 依赖（Markdown/HTML/PDF 纯 .NET 跨平台预览）+ 预览两阶段加载
- **07-15** — ICO 多帧画廊预览 + P0-2 压缩选项 + P0-3 魔数检测预览集成
- **07-13** — UI 功能补齐（对话框 + 控件 + 转换器）
- **07-02** — Phase 10: WPF 功能补齐（进度条/信息面板/状态栏）

#### 2026-06

- **06-21** — Phase 9: 文件列表交互补齐
- **06-21** — Phase 8: 设置窗口 TabControl 重构 + i18n 补全 + ComboBox 修复
- **06-19** — Phase 7: CLI 命令补齐 + IPC 多实例 + 10 个新对话框
- **06-17** — Phase 6: 样式统一与视觉打磨
- **06-17** — Phase 5: 工具栏按钮样式重构
- **06-15** — Phase 4: App.axaml 统一控件样式
- **06-11** — Phase 0: 项目骨架（首次提交）

### MantisZip.UI（WPF 遗留版）

> WPF 版已进入**维护模式**（迁移完成后废弃），完整历史见 [progress-wpf.md](progress-wpf.md)。仅当修复仅存在于 WPF 的 bug 时追加。

### 共享层（Core / ShellExt / 构建）— 里程碑

按版本分组，每组按日期从新到旧排列。

#### v0.5.0

- **08-31** — .NET 9 → .NET 10 升级（LTS，支持至 2028-11）：全部 7 个项目 TargetFramework 更新 + 移除废弃 `Avalonia.Diagnostics` 包 + `System.Drawing.Common` 升级至 10.0.8
- **08-31** — 卸载/更新文件占用修复：`CloseApplications=yes`（Restart Manager 检测用户关闭占用进程）+ `ShellIntegration.Uninstall` 重启 Explorer 释放 comhost.dll 句柄
- **08-24** — 发布脚本 copy-7z-dll 按 PE 头校验架构：x86 目录不再误拷 64 位 7z.dll（历届安装包均受影响），缺失架构警告跳过 + installer x86 行 skipifsourcedoesntexist
- **08-19** — 添加到压缩包重名条目冲突处理（AddConflictHelper 条目名级解析）+ 保留浏览目录前缀（entryBasePath）
- **08-09** — Core 新增 ArchiveCommentReader（ZIP + RAR5 注释统一读取）+ ZIP 注释编码兼容（GBK 回退）
- **08-07** — 发布管线切 Avalonia + 便携版双变体 + WebSetup + 移除调试符号

#### v0.4.5

- **08-07** — ShellExt COM 右键菜单扩展 + 路径清单统一 Core 侧（A/B 数据集）
- **08-03** — ArchiveEntryExtractor 支持纯 GZip/ISO 单条目提取 + 目录聚合统计 DirStats 增加 NewestModified
- **07-27** — 异步冲突解析 API（CompressConflictResolver async 化 + 引擎异步冲突解析）
- **07-20** — Avalonia Shell/COM 集成

#### v0.4.4

- **07-02** — 压缩包路径处理一站式重构 + 安装包增强（.NET 9 自动下载）
- **06-30** — 魔数检测预览系统 Phase 1（Core 侧）

#### v0.4.x 早期

- **06-22** — DynamicFormatOptions 压缩格式动态选项（v0.4.3）
- **06-20** — ZIP copy-mode 优化（ZipBinaryRewriter，v0.4.2）
- **06-18** — 自包含安装包（v0.4.1）
- **06-15** — 发布基础设施（v0.4.0）

#### v0.3.x

- **06-15** — 完全移除 SharpZipLib 生产依赖 + DPAPI → AES-GCM 跨平台加密（v0.3.13）
- **06-11** — RAR 提取进度条（v0.3.13）
- **06-08** — ZIP 编码兼容性（v0.3.11）
- **05-31** — ShellExt COM 组件创建（v0.3.7）
- **05-28** — 引擎统一（SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip，v0.3.4）

---

## 历史设计方案索引

以下设计方案对应功能已在过往版本中完成，对应设计文档存于 `.omo/plans/` 供回溯参考：

| 功能 | 设计文档 | 实现版本 |
|------|----------|:--------:|
| Avalonia: WPF 差异补齐总表（P0–P2 全部清零：双击行为/删除原包、便携模式（含 Temp 重定向）、文件过滤控件、默认路径优先级、信息面板持久化、智能打开路径（含 `ExtractArchive` 死代码修复）、冲突对话框暂停/取消、密码导入导出、收藏夹、Enable 设置、AllowElevation 等） | [avalonia-wpf-diff-plan.md](.omo/plans/avalonia-wpf-diff-plan.md) | v0.5.0 |
| 添加到压缩包重名条目冲突处理（`AddConflictHelper` 条目名级解析、语义方向与解压相反：新数据更新/更大→覆盖；ZIP copy-mode `keepEntryNames` 排除被覆盖条目 + legacy Phase 2 应用解析结果；7z 覆盖经 `ModifyArchive`(index→null) 删除 + `CompressFileDictionary` Append 重加；Avalonia Ask 弹窗复用 ConflictDialog，新标题 key `AddConflict_Title`） | [add-archive-conflict-handling.md](.omo/plans/add-archive-conflict-handling.md) | v0.5.0 |
| Avalonia 拖拽添加（`MainWindowViewModel.AddFilesToArchiveAsync` 抽取 + WPF `Window_Drop` 三分支移植：已打开+压缩包→切换打开 / 已打开+文件→确认框→添加到 `CurrentFolder` / 未打开→打开或 `CompressSettingsWindow` 预填 + `DragAddOverlay` 窗口内两色覆层（绿=可添加/红=格式不支持，呼吸动画对齐拖拽解压）+ 文件夹拖入支持） | [drag-add-overlay.md](.omo/plans/drag-add-overlay.md) | v0.5.0 |
| 图片预览能力系统（`PreviewCapabilities` 能力注册表 [Flags]：Zoom/Transparency/FlattenAlpha/AnimationControls 取代 `HasXxxControls` 硬编码 + `PreviewType.Gif`→`AnimatedImage`（GIF/WebP 动画共用）+ GIF 透明棋盘格 + Animated WebP 分流（SKCodec `FrameCount>1`）） | [image-preview-capabilities.md](.omo/plans/image-preview-capabilities.md) | v0.5.0 |
| 拖拽/右键解压流程统一（`SelectedItemsExtractService` 统一解压动作、`TarGzEngine` 按条目提取、冲突统一走设置 6 策略 + 统一 Ask 弹窗、拖拽路径语义与右键一致、`MapConflictActionString` 连字符映射漏洞修复） | [drag-extract-unify.md](.omo/plans/drag-extract-unify.md) | v0.4.5 |
| 目录行聚合显示（`DirStats`+`ComputeDirectoryStats` 增加 `NewestModified`；Avalonia `ArchiveItemModel` 显示属性改派生计算属性 + `CompressedSizeAvailable`；`PopulateEntries` 基于过滤后 `filteredSource` 应用聚合） | [directory-size-date-aggregate.md](.omo/plans/directory-size-date-aggregate.md) | v0.4.5 |
| 路径清单统一（A/B 数据集：预览=实际绝对一致，CompressPlan 唯一事实来源 + 压缩/解压过滤白名单 + IsBuildPending 按钮门禁） | [path-manifest-unification.md](.omo/plans/path-manifest-unification.md) | v0.4.5（⏳ 交互清单待用户 GUI 验证） |
| 统一路径快捷选择（WPF QuickPathControl + 数据层；Avalonia 演进为 Tab 式速选面板 + CustomFilePickerDialog，QuickPathBuddy 概念并入 Tab+搜索一体化，QuickPathPreDialog 过渡方案废弃） | [quickpath-unified.md](.omo/plans/archived/quickpath-unified.md)（已归档，Avalonia 部分被 [quickpath-control-redesign.md](.omo/plans/quickpath-control-redesign.md) 取代） | v0.4.3+（Avalonia 演进 v0.4.5） |
| QuickPathPicker 自包含路径速选控件（Compress/Extract/Settings 三宿主，AutoCompleteBox 补全 + ⭐🕐🪟 浮层 + 目录归一化，浏览器差异经注入委托） | [2026-08-03-quickpath-picker.md](docs/superpowers/plans/2026-08-03-quickpath-picker.md) + [设计](docs/superpowers/specs/2026-08-03-quickpath-picker-design.md) | v0.4.5（⏳ 待用户 GUI 验证） |
| 文件选择器多选（PickItems 模式：勾选累积 + 跨目录保留 + 右栏已选面板；CompressSettingsWindow 合并「添加文件/文件夹」单按钮） | [file-picker-multi-select.md](.omo/plans/file-picker-multi-select.md) | v0.4.5 |
| 可排序的默认路径优先级（文件选择器初始路径 context/explorer/recent/custom） | [path-priority-sortable.md](.omo/plans/path-priority-sortable.md) | v0.4.5 |
| 解压路径统一（`ExtractEntriesAsync` + `pathOverrides`，单一事实源 `ExtractPathResolver`） | [extract-path-unification.md](.omo/plans/extract-path-unification.md) | v0.4.5 |
| 移除 WebView2 依赖（Markdown/HTML/PDF 跨平台预览） | [remove-webview2-preview.md](.omo/plans/remove-webview2-preview.md) | v0.4.5 |
| 便携版模式 | [portable-mode.md](.omo/plans/portable-mode.md) | v0.4.5 |
| 文件冲突对话框暂停/取消 | [conflict-dialog-pause-cancel.md](.omo/plans/conflict-dialog-pause-cancel.md) | v0.4.5 |
| 压缩选项增强（7z/ZIP 格式参数扩展） | [compression-options-enhancement.md](.omo/plans/compression-options-enhancement.md) | v0.4.5 |
| 上下文工具栏重构（目录树+文件列表） | [context-toolbars.md](.omo/plans/context-toolbars.md) | v0.4.5 |
| 解压/压缩结果预览面板 | [result-preview-panel.md](.omo/plans/result-preview-panel.md) | v0.4.5 |
| 元数据信息面板可配置 | [metadata-panel-configurable.md](.omo/plans/metadata-panel-configurable.md) | v0.4.5 |
| 紧凑度模式（Compactness Mode） | [compactness-mode.md](.omo/plans/compactness-mode.md) | v0.4.5 |
| 预览两阶段加载（信息栏+内容分离） | [preview-two-phase-loading.md](.omo/plans/preview-two-phase-loading.md) | v0.4.5 |
| Avalonia: Shell/COM 集成移植 | [avalonia-shell-com-integration.md](.omo/plans/avalonia-shell-com-integration.md) | v0.4.5 |
| Avalonia Phase 10: WPF 功能补齐 | [avalonia-phase10-feature-parity.md](.omo/plans/avalonia-phase10-feature-parity.md) | v0.4.5 |
| Avalonia: i18n 补齐 + 杂物清理 | [avalonia-i18n-and-cleanup.md](.omo/plans/avalonia-i18n-and-cleanup.md) | v0.4.5 |
| 压缩解压文件筛选 | [file-filter-feature.md](.omo/plans/file-filter-feature.md) | v0.4.5 |
| emoji 替换为 Fluent UI PathIcon + 文件列表行图标改用系统原生 | [emoji-to-pathicon.md](.omo/plans/emoji-to-pathicon.md) | v0.4.5 |
| 双击行为 + 解压后删原包 | [doubleclick-extract-settings.md](.omo/plans/doubleclick-extract-settings.md) | v0.4.4+ |
| 魔数检测文件真实格式 | [preview-magic-detection.md](.omo/plans/preview-magic-detection.md) | v0.4.4 |
| 密码流程统一 | [password-flow-unification.md](.omo/plans/password-flow-unification.md) | v0.4.4 |
| 字体预览连字效果开关（HarfBuzzSharp shaping + `CheckFontSupportsLigature` 连字检测 + `IsLigatureEnabled`/`ToggleLigature` 命令 + `CanLigatureToggle` 灰禁用 + 工具栏按钮 + `FontPreviewEnableLigature` 持久化） | [font-preview-ligature.md](.omo/plans/font-preview-ligature.md) | v0.4.4 |
| 致谢贡献者名单 | [contributors-panel.md](.omo/plans/contributors-panel.md) | v0.4.3+ |
| 安装程序 .NET 9 自动下载 | [installer-dotnet-autodownload.md](.omo/plans/installer-dotnet-autodownload.md) | v0.4.3+ |
| 预览格式扩展（12 种元数据格式） | [preview-extended-formats.md](.omo/plans/preview-extended-formats.md) | v0.3.0 |
| 快速压缩拆分为独立/合并两项 | [split-compress.md](.omo/plans/split-compress.md) | v0.2.10 |
| 加载大文件 overlay | [archive-loading-progress.md](.omo/plans/archive-loading-progress.md) | v0.3.1 |
| 添加到/从压缩包删除 | [archive-add-delete.md](.omo/plans/archive-add-delete.md) | v0.2.9 |
| 暗色/亮色主题 | [dark-theme.md](.omo/plans/dark-theme.md) | v0.2.9 |
| 日志隐私脱敏 | [log-privacy-redaction.md](.omo/plans/log-privacy-redaction.md) | v0.2.8 |
| 国际化 (i18n) | [i18n-localization.md](.omo/plans/i18n-localization.md) | v0.2.8 |
| 智能解压 (Smart Extract) | [smart-extract.md](.omo/plans/smart-extract.md) | v0.2.10 |
| 文件列表筛选/搜索 | [file-list-filter-search.md](.omo/plans/file-list-filter-search.md) | v0.3.8 |
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip) | [engine-unification-sharpcompress.md](.omo/plans/engine-unification-sharpcompress.md) | v0.3.4 |
| 文件大小进度条 | [file-size-progress-bar.md](.omo/plans/file-size-progress-bar.md) | v0.3.4 |
| PNG 透明通道控制 | [png-transparency-3way.md](.omo/plans/png-transparency-3way.md) | v0.3.4+ |
| 批量进度文件列表 | [batch-progress-list.md](.omo/plans/batch-progress-list.md) | v0.3.5 |
| 解压配置面板 (ExtractSettingsWindow) | [extract-settings-window.md](.omo/plans/extract-settings-window.md) | v0.3.6 |
| COM 右键菜单 | [com-context-menu.md](.omo/plans/com-context-menu.md) | v0.3.7 |
| COM 迁移映射表 | [com-migration-mapping.md](.omo/plans/com-migration-mapping.md) | v0.3.7（辅助文档） |
| 压缩窗口密码 Tab 重设计 | [design-compress-password-tab.md](.omo/plans/design-compress-password-tab.md) | v0.3.7-refined-2 |
| 关于窗口重设计 | [about-window-redesign.md](.omo/plans/about-window-redesign.md) | v0.3.7-refined-4 |
| 文件关联 per-extension ProgId | [file-assoc-per-extension.md](.omo/plans/file-assoc-per-extension.md) | v0.3.9 |
| 移除 SharpZipLib 注释编辑耦合 | [remove-sharpziplib.md](.omo/plans/remove-sharpziplib.md) | v0.3.9 |
| ZipEngine SharpZipLib 完全迁移 (加密路径→SharpSevenZip) | [zipengine-sharpcompress-migration.md](.omo/plans/zipengine-sharpcompress-migration.md) | v0.3.13 |
| 压缩流程统一化 (CompressService) | [compress-service-unify.md](.omo/plans/compress-service-unify.md) | v0.4.0 |
| 发布 Release | [release-automation.md](.omo/plans/release-automation.md) | v0.4.0 |
| 返回上级目录 (.. 导航行) | [parent-directory-entry.md](.omo/plans/parent-directory-entry.md) | v0.4.0 |
| ZIP 压缩流直拷优化 (ZipBinaryRewriter) | [zip-copy-mode-optimization.md](.omo/plans/zip-copy-mode-optimization.md) | v0.4.2 |
| UAC 提权 + 权限不足处理 | [uac-elevation-permission.md](.omo/plans/uac-elevation-permission.md) | v0.4.2 |
| 自包含安装包发布 | [self-contained-installer.md](.omo/plans/self-contained-installer.md) | v0.4.2 |