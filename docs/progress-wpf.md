# MantisZip WPF 版（遗留）历史变更记录

> **状态**：WPF 版（`MantisZip.UI`）已进入维护模式，迁移完成后将废弃，新功能只添加到 Avalonia 版。
> 本文件为 WPF 版完整历史变更记录，按日期从新到旧排列；仅当修复仅存在于 WPF 的 bug 时追加。

---

## MantisZip.UI（WPF 遗留版）

#### v0.5.0 (2026-08-07)
  - 版本号从 0.4.5 更新到 0.5.0（AppConstants.cs + csproj，与 Avalonia 版同步）
#### v0.4.5 (2026-07-14)
  - **冲突对话框暂停/取消功能** — CompressConflictDialog/ConflictDialog 新增暂停/取消按钮；CompressSettingsWindow 和 App 层新增 PauseFromConflict 重入路径
  - **预设显示 + 筛选统计文字常显** — 修复预设配置和过滤统计文字始终可见
  - **修复 Win11 日文版 ZIP 假阳性密码检测** — 防范 SharpCompress 误弹密码框
  - **预览信息面板切换** — View 菜单新增切换开关，AppSettings.ShowPreviewInfoPanel 持久化
  - **COM handler 动词重命名** — `open` → `mantiszipopen` 防止 Shell 动词冲突
  - **安装下载依赖增加提示** — 安装时下载 .NET/WebView2 增加用户提示
#### v0.4.4+ (2026-07-09) 移除 Applications shell\open\command
  - 移除 `Applications\MantisZip.UI.exe\shell\open\command` 注册，防止新安装时 Shell 关联刷新错误路由
  - `SupportedTypes` 保留，双击走 per-format ProgId 不受影响
#### v0.4.4 (2026-07-07) COM 动态菜单 + pending 状态 + 延迟级联安装
  - **COM 动态菜单组件** — `MantisZip.ShellExt` 实现 `IShellExtInit` + `IContextMenu`
    - 动态菜单文本（「解压到 {name}」「压缩到 {name}.zip」）
    - 纯 Win32 图标加载（无 `System.Drawing` 依赖）
    - 多选文件数量显示（「打开压缩包 等 N 个文件」）
    - 8 个独立菜单项开关（cascade/verb 两种注册方式）
  - **COM + 延迟级联安装流程** — Install 仅注册 COM，级联菜单在检测到 COM 未加载时自动安装
  - **动态菜单状态跟踪** — `DynamicMenuStatus`（Active/Pending/Fallback/Disabled）
  - **pending 态 COM 菜单占位符** — COM handler 检测到 pending 状态时插入灰色禁用分隔符
  - **安装包 .NET 9 检测修复** — 增加文件系统回退检测 `cmd /c dir ...\9.*`
#### v0.4.4+ (2026-07-03) 双击文件默认程序打开
  - 双击文件调用系统默认程序打开，`DoubleClickOpenThreshold` 设置阈值（默认 10MB）
  - 超过阈值时弹出确认对话框；文件 >= 1MB 显示 ProgressWindow
  - Tar/GZip/ISO 不支持单文件提取，给出提示
  - 上级目录（..）选中时预览面板不刷新修复
#### v0.4.4 (2026-07-03) 密码流程统一
  - `ResolvePasswordAsync` 统一密码入口：检查加密 → TryMatchPassword → 对话框循环
  - LoadArchiveAsync / ExtractAsync / RunExtractStatic / HandleExtractBatchCore 全部简化
  - 删除 `ExtractWithPasswordAsync`；修复密码框取消后陷入循环
#### v0.4.3+ (2026-06-30) 工具栏新增「解压选择文件」按钮
  - 位于「解压」与「压缩」之间，行为与右键菜单「解压到…」一致
  - 右键菜单图标统一（📤 → 📑）
#### v0.4.3+ (2026-06-30) 默认路径优先级设置
  - `AppSettings.DefaultPathPriority` 支持 4 种策略：场景相关 / 资源管理器 / 最近使用 / 桌面
  - `ResolveDefaultPath()` 按优先级链自动选取最佳默认路径
  - 设置 UI 高级标签页新增「默认路径优先级」GroupBox
#### v0.4.4 (2026-06-30) 魔数检测预览系统 Phase 2 — UI 集成
  - 魔数优先路由重构（`TryMagicPreview`），写入 `PreviewExtraInfoPanel`
  - 冲突检测 + 切换按钮：魔数结果与扩展名不一致时插入"按扩展名/按魔数"切换按钮
  - `AppSettings.EnableFormatDetection` 开关（默认 true）
#### v0.4.3 (2026-06-22) QuickPathControl 统一路径选择 + 书签管理器 + 权限跳过
  - QuickPathControl 统一压缩/解压窗口的路径选择（支持收藏夹 / 历史记录 / 资源管理器窗口 / 浏览）
  - 资源管理器窗口检测重写：COM IShellWindows 为主 + Win32 EnumWindows 兜底
  - 书签管理器菜单（工具 > 书签管理器）
  - 压缩包内逐条目权限跳过：`ExtractResult` 类 + try-catch 跳过失败条目继续处理
  - UAC 提权弹窗修复：由事前预检改为响应式拦截，首次弹窗后静默跳过
  - ProgressWindow 错误摘要（可复制 TextBox）
  - DynamicFormatOptionsPanel 后端接线：ZIP 编码/7z 压缩方法/7z 固实选项
  - 默认格式选项设置：`ZipEncoding`、`SevenZipCompressionMethod`、`SevenZipSolid`
  - RELEASE_NOTES.md 双语化
#### v0.4.2 (2026-06-20) 安装程序主题/语言选择修复 + ZIP copy-mode 进度与取消
  - 安装时主题选择不生效修复：`settings.json` 添加占位符 + `PatchSettingsThemeAndLanguage`
  - ZIP 添加/删除进度与取消优化：单遍流式（80KB 块 CRC32 + Deflate），每块粒度进度报告
  - 收尾阶段分步报告：中央目录 92% → 目录尾 94% → 刷盘 97% → 原子替换 100%
#### v0.4.1 (2026-06-18) 发布流程修复 + 文档双语化
  - ZIP Copy-Mode 优化：`ZipBinaryRewriter` 实现二进制级压缩流直拷
  - CI release notes regex 修复
  - RELEASE_NOTES.md 双语化
  - 文件列表增加"返回父目录"项目
  - UAC 提权双模式：`AllowElevation` 设置 + `App.Elevation.cs` + 3 个对话框
  - 解除权限不足响应式拦截 + 提权弹窗行为优化
#### v0.4.0 (2026-06-15) 第一个上线版本
  - 功能基本完成，测试基本完成
  - CLI 参数归一化（`install-assoc` → `--install-assoc`）
  - 右键菜单改为全平台统一静态级联方案（`InstallCascade`），COM 默认不安装
  - 设置窗口新增"动态菜单"选项
  - 临时文件管理 GroupBox + 启动时自动清理
  - Win11 右键菜单不显示修复（HKCU COM 注册被忽略，走静态级联）
  - RELEASE_NOTES.md 移至根目录
  - CI 修复：TarGzEngine 测试 / ISCC ChineseSimplified.isl / ShellExt runtimeconfig.json / en.json 键缺失 / 路径引号截断 / MyAppVersion 传递
  - 全局调试日志增强（`CoreLog.DiagnosticsEnabled` + 43 个 catch 块注入）
  - LogRedactor 隐私脱敏修复（相对路径 regex 分支）
  - README.md 路径修复（反斜杠 → 正斜杠）
#### v0.3.13 (2026-06-14) 修复问题
  - ToggleSepDirBaseline / ToggleProgressBars 根目录状态重置修复
  - CompressConflictDialog 重命名按钮图标丢失修复
#### v0.3.13 (2026-06-13) 对话框 Owner 修正 + 安装脚本 + 字体预览（WPF 端）
  - 对话框 Owner 修正（6 个文件），弹窗不再被主窗口挡住
  - installer.iss 通配符化 + 缺失 DLL 补全
  - 预置用户设置机制（`installer\prebuilt\settings.json`）
  - 字体预览修复（CJK 名优先 + CFF-OTF 回退 + 清理重置）
#### v0.3.13 (2026-06-12) 压缩批处理修复 + 进程残留修复
  - 压缩批处理文件进度条锯齿修复
  - 压缩完成后 exe 进程残留修复（两处 bug）
#### v0.3.13 (2026-06-11) 提取文件列表展示和目录树构建逻辑到 Core
  - `ArchiveTreeBuilder` + `ArchiveEntryLister`（Core/Services）
  - WPF 重构：`BuildFolderTree()` 和 `FilterFiles()` 改为调用 Core 服务
#### v0.3.12 (2026-06-10) 文件列表筛选增强 / 解压路径裁剪
  - 排除文本框 + 子串/通配符两种匹配模式
  - 筛选匹配显示名而非 FullPath（解决根目录名误匹配 bug）
  - 解压路径裁剪设置（保留完整路径 / 相对当前目录）
#### v0.3.11 (2026-06-08) 文件列表拖拽提取修复
  - 异步重入竞态修复（`_isDragExtracting` 标志）
  - ZIP 编码兼容性修复（CP437/GBK 自动探测）
  - Tar/GZip 提取统一委托给 `ArchiveEntryExtractor`
  - 多选/目录拖拽支持、自身拖拽光标修复
#### v0.3.10 (2026-06-06→06-07) 测试按钮完整性检查 + ProgressWindow 集成
  - 引擎测试完整性提升：ZipEngine/TarGzEngine/SevenZipEngine 逐项完整解压验证
  - 测试进度 UI 改为 ProgressWindow，支持取消操作
  - Dispatcher 优先级竞态修复
  - UI 主题一致性修复（跨 7 个 XAML 文件）
#### v0.3.9 (2026-06-06→06-07) 文件关联 + 独立 ProgId + 设置窗口 UI 统一
  - 文件关联 Bug 修复（.tar.gz 跳过 / 自定义扩展名清理 / 图标清理）
  - Per-extension 独立 ProgId（MantisZip.Zip / MantisZip.7z / …），各自显示格式图标
  - 设置窗口 ComboBox 外观统一
  - 压缩密码"不匹配"误报修复
  - 压缩右键菜单 IPC 期间提前显示 UI
  - 批处理模式下取消按钮真正终止压缩
  - 移除 SharpZipLib 注释编辑耦合（ZipCommentHelper）
  - 代码拆分：App.Cli.cs / CompressSettingsWindow / SettingsWindow / ShellIntegration / MainWindow 按职责拆分
#### v0.3.8 (2026-06-06) 右键菜单增强 + 文件关联面板重构 + 文件列表筛选/搜索
  - 右键菜单修复（批次污染 / 闪烁 / 图标缓存 / 子菜单图标）
  - 进度窗口增强（保持打开切换按钮 / 倒计时即时生效 / 压缩包计数始终显示）
  - 文件关联面板重构：per-extension 复选框 + 系统图标 + 三态状态视觉区分
  - 文件列表筛选/搜索：全部子目录展开 / 文字+日期+大小 AND 过滤引擎 / 空结果提示
#### v0.3.7-refined-5 (2026-06-04) 引擎统一完成
  - SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip 全部完成
  - 批量进度文件列表 / ExtractSettingsWindow / COM 右键菜单全部完成
#### v0.3.7-refined-4 (2026-06-03) 关于窗口重设计
  - AboutWindow 4 标签页（关于/作者/依赖库/致谢）
  - 21 个 About_* 本地化键 + 13 个冒烟测试
#### v0.3.7-refined-3 (2026-06-03) 密码工具栏 + 关闭压缩包 + 捐赠 + 空状态重设计 + 压缩冲突增强
  - 密码按钮三态重设计（无加密/有加密未匹配/已匹配）
  - MatchedPasswordDialog / Theme_StatusSuccessBg 主题色
  - 关闭压缩包菜单（Ctrl+W）/ 文件菜单重排序
  - 捐赠对话框 / 空状态重设计 / CompressConflictDialog"应用到全部"
#### v0.3.7-refined-2 (2026-06-02) 压缩窗口密码 Tab 重设计 + 调试日志增强
  - 对照 `docs/design-compress-password-tab.md` 修复全部差异
  - PasswordBox/TextBox 切换 / 密码强度 `●` 颜色 / 自动规则调整
#### v0.3.7-refined (2026-06-01) COM 右键菜单完善（图标 + 文本 + 本地化）
  - `CreateDIBSection` 32-bit DIB 修复透明背景变纯色
  - 菜单文本精简 + 多选动态文本 + 8 个 ShellExt_* 本地化键
#### v0.3.7 (2026-05-31) COM 右键菜单
  - 新建 MantisZip.ShellExt 项目（.NET 9 comhost）
  - ContextMenuHandler.cs 完整实现 IShellExtInit + IContextMenu，8 个菜单项
  - NativeMethods.cs Win32 互操作 + COM 注册
#### v0.3.6 (2026-05-30) ExtractSettingsWindow UI 重构
  - TabControl + GroupBox + 2-column Grid 架构
  - 配色对齐（移除显式颜色，靠主题继承）
#### v0.3.5 (2026-05-30) 批处理进度文件列表 + IPC 合并
  - ProgressWindow 批处理文件列表（BatchItemStatus + GridView）
  - `--compress-separate` / `--compress-combined` IPC 合并（800ms 收集窗口）
#### v0.3.4 (2026-05-28~29) 引擎统一 + ExtractSettingsWindow + 调试日志
  - SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip 2.0.45
  - ExtractSettingsWindow 创建 + PreserveDirectoryRoot 设置
  - 调试日志系统增强（7 类日志）
#### v0.3.3 (2026-05-27) 安装器多语言与预览设置增强
  - 数据表格行/列限制可配置 + 字体预览字号可配置
  - WebView2 启动时预初始化 + Inno Setup 多语言支持
#### v0.3.2 (2026-05-27) 代码拆分
  - App.xaml.cs 1977 行拆为 5 个 partial class 文件
#### v0.3.1 (2026-05-26) 预览修复与注释
  - WebView2 PDF 内容渲染 / PDF 页数统计修复 / 图片缩放修复
  - GIF 帧导航增强 / 字体预览渲染优化 / PE/PDF 预览缓存
  - 400+ 方法头注释 + 170+ 文件头注释 + 17 份计划文档
#### v0.1.0 (2026-04-24) 初始版本
  - ZIP/7z/RAR/TAR/GZ/TGZ 压缩解压
  - 目录树导航 + 文件列表 + 密码管理器
  - 拖拽解压/压缩
