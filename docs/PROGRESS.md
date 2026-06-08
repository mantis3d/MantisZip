# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.3.10
- **发布日期**: 2026-06-08

## 规划中

## 版本历史（从新到旧）

### v0.3.10 (2026-06-08) 测试按钮完整性检查 + ProgressWindow 集成

1. **引擎测试完整性提升**：三个引擎的 `TestArchiveAsync` 从快速检查改为完整完整性验证
   - ZipEngine: `stream.ReadByte()`（每个条目只读 1 字节）→ `stream.CopyTo(Stream.Null)` 完整解压流
   - TarGzEngine: `ListEntriesAsync().Count > 0`（只计数不验证）→ TarReader 逐项 `CopyTo(Stream.Null)` + `.gz` 时 GZipStream 套接
   - SevenZipEngine: 空循环（只报告进度不测试）→ `extractor.ExtractFile(index, Stream.Null)` 逐项解压 + 保留 `extractor.Check()` 结构校验
2. **测试进度 UI 改进**：内联进度条改为 `ProgressWindow`，支持取消操作，取消不弹确认框（直接 `ct.IsCancellationRequested` 检测）
3. **Dispatcher 优先级竞态修复**：`await` 续体（Normal 优先级）先于 Background 进度更新执行，导致进度 50% 就弹出结果对话框。通过 `Dispatcher.Invoke(() => { }, DispatcherPriority.Background)` 刷新解决
4. **UI 主题一致性修复**（跨 7 个 XAML 文件）：
   - `ProgressWindow` 补齐 Window 背景（`Theme_WindowBg`）和总进度条前景色（`Theme_ProgressFill`）
   - `MainWindow` 状态栏背景从误用的 `Theme_ProgressBg` 改为 `Theme_HeaderBg`
   - `App.xaml` 新增隐式 `TabItem` 样式，提取 CompressSettings/ExtractSettings 两窗口的重复样式，`AboutWindow` 自动获得主题化 Tab 头部
   - 5 个对话框主按钮统一使用 `Theme_Accent` + `Theme_TextOnAccent` 强调色（CompressSettings、ExtractSettings、ArchiveComment、About、AppMessageBox）
       - `SettingsWindow` 语言/Label/LogPrivacyMode 两个 ComboBox 补齐缺失的 `Background="{DynamicResource Theme_WindowBg}"`
5. **AGENTS.md 规则补充**：新增"每次 session 自动执行规则"第 3 条（新 UI 控件必须应用主题样式），并补充缺失主题资源时的处理方式；`Light.xaml` 进度条列颜色加深修复大小列对比度
6. **QuickPathControl 设计完成**: 统一路径快捷选择组件系统（QuickPathControl UserControl + QuickPathDialog + FavoriteManagerWindow），覆盖压缩/解压/提取所有路径选择场景；旧 `explorer-path-switcher.md` 归档

### v0.3.9 (2026-06-06 → 06-07) 文件关联 Bug 修复 + 独立 ProgId + 设置窗口 UI 统一

1. **文件关联 Bug 修复**：
   - `.tar.gz` 不再被跳过——设置勾选后真正写入注册表 `OpenWithProgids` + `DefaultIcon`
   - `GetInstalledExtensionCount()` 排除 `.tgz`，UI 状态 "N/7" 计数准确
   - `UninstallAssociations()` 现在也清理自定义扩展名的注册表条目
   - `UninstallAssociationForExtension()` 图标清理现在正确处理 `"{exePath}",0` 格式，自定义扩展名不再残留图标
2. **Per-extension 独立 ProgId**（类似 Bandizip）：
   - 每个格式使用独立 ProgId：`MantisZip.Zip`、`MantisZip.7z`、`MantisZip.Rar`、`MantisZip.Tar`、`MantisZip.TarGz`、`MantisZip.Gz`、`MantisZip.Iso`
   - 每个格式在资源管理器中显示自己的格式图标（`.zip` → `zip.ico`、`.rar` → `rar.ico` 等）
   - 旧版 `MantisZip.Archive` 在安装/升级时自动清理迁移
   - 自定义扩展名使用 `MantisZip.Custom`
3. **设置窗口 ComboBox 外观统一**：
   - 5 个缺失 `Background="{DynamicResource Theme_WindowBg}"` 的下拉框补全：冲突动作、主题、预览位置、信息面板方向、字体列表
   - `ConflictCombo` 还补齐了 `Width="300"` 和 `HorizontalAlignment="Left"`
4. **压缩密码「不匹配」误报修复**：
   - 用户在明文模式（👁 显示密码）下输入密码后直接点击压缩按钮时，`PasswordBox.Password` 可能仍为旧值，导致与 `ConfirmPasswordBox.Password` 对比时报"两次输入的密码不一致"
   - 修复：在验证前先同步 TextBox 内容到 PasswordBox，与 `GetPassword()` 已有逻辑一致
5. **压缩右键菜单 IPC 期间提前显示 UI**：
   - 三个压缩右键菜单（用 MantisZip 压缩 / 压缩到独立的 / 压缩到父目录名）在 IPC 路径收集期最长 ~3.8s 内无任何 UI 反馈
   - `--compress-separate` / `--compress-combined`：IPC 前创建 ProgressWindow 显示"正在收集文件..."，取消按钮 IPC 期间可用，收集完成后复用同一窗口进入压缩
   - `--compress`：IPC 前显示轻量无边框加载窗"正在收集文件..."，收集完成后自动关闭并弹出 CompressSettingsWindow
   - 新增本地化键 `App_CompressCollecting`
6. **批处理模式下取消按钮真正终止压缩**：
   - `ProgressWindow.CancelButton_Click` 批处理分支此前只 `Close()` 窗口，`_cts.Cancel()` 未调用，压缩在后台继续跑完生成完整文件
   - 修复：批处理分支也调用 `_cts?.Cancel()`，与非批处理模式一致
7. **移除 SharpZipLib 注释编辑耦合**：
   - 新建 `ZipCommentHelper`（Core/Utils）直接操作 ZIP EOCD 字节读写注释，不依赖 SharpZipLib
   - `ArchiveCommentDialog` 保存注释时显示"正在保存注释..."文字提示（本地化键 `Main_ArchiveComment_Saving`）
   - 清理 3 处无用 SharpZipLib import（App.xaml.cs / App.Cli.cs / MainWindow.xaml.cs）
   - 修正 App.Password.cs 注释（SharpZipLib → SharpCompress）
8. **版本号同步**：AppConstants.cs、.csproj、installer.iss 统一到 v0.3.9
9. **修复 .GetAwaiter().GetResult() 同步-异步反模式**：`ResolveSmartDest` 改为 async，用 `await` 替代阻塞
10. **App.Cli.cs 拆分**：按职责拆为 App.Compress.cs（压缩命令）、App.Extract.cs（解压命令）、App.Open.cs（打开/快速压缩），原文件保留为空白 partial 壳
11. **CompressSettingsWindow 拆分**：密码标签页逻辑独立为 CompressSettingsWindow.Password.cs partial 文件，主文件减少 450 行
12. **SettingsWindow.xaml.cs 拆分**：文件关联面板逻辑独立为 SettingsWindow.Assoc.cs partial 文件，主文件从 1051 行降至 602 行
13. **ShellIntegration.cs 拆分**：拆为 ShellIntegration.Menu.cs（右键菜单注册）+ ShellIntegration.Assoc.cs（文件关联注册），原文件保留共享声明（99 行）
14. **MainWindow.UI.cs 类型抽取**：FolderNode、ArchiveItem 子类、CompressedDisplayMode 枚举移到 MainWindow.Types.cs（139 行）

### v0.3.8 (2026-06-06) 右键菜单增强 + 文件关联面板重构 + 文件列表筛选/搜索

1. **右键菜单修复（批次污染 + 闪烁 + 图标）**：
   - 修复 ShellExt `_fullFileList` 跨右键调用批次污染 — 添加 2 秒时间窗口检测，选少文件不再错误使用上一批的旧大文件列表
   - 修复右键菜单闪烁/空白 — 永久缓存图标 HBITMAP，移除 `CleanupIconCache()` 热路径调用，消除每次右键 40-120ms 图标重载延迟
   - "MantisZip" 子菜单头加图标 — 用 App.ico + `InsertMenuItem` + `MIIM_BITMAP` 替代旧 `InsertMenu` API，根菜单显示软件图标
   - **压缩包计数始终显示** — `FileCountText` 不再隐藏，批处理模式显示 `压缩包 X/Y`（原仅压缩时显示，解压时隐藏）
   - **本地化语义修正** — `Progress_FileCount` 从「文件 X/Y」改为「压缩包 X/Y」/「Archive X/Y」
   - **📌 保持打开切换按钮** — 进度窗口左下角新增图钉 ToggleButton，勾选后进度走完不自动关闭窗口，用户可手动关闭
   - **倒计时期间可切换** — `AutoCloseOrWaitAsync` 每 100ms 轮询 `KeepOpenOnComplete`，倒计时中途勾选/取消勾选即时生效
2. **文件关联面板重构（per-extension 复选框 + 系统图标 + 三态状态）**：
   - 从统一开关改为按扩展名独立复选框列表，支持自定义扩展名添加/删除，行点击切换，全选/取消全选
   - 当前关联程序显示 — 每行显示当前关联的应用名，移除 "Archive"/"Compressed" 等后缀干扰词
   - 系统图标 — 使用 `SystemIconHelper.GetFileIcon` 显示系统真实文件类型图标
   - 打开默认应用按钮 — 修复 `ms-settings:defaultapps` URI 打开失败（添加 `UseShellExecute = true`）
   - 安装 Bug 修复 — 安装按钮现在只关联勾选的格式
   - 关联状态持久化 — 修复每次打开窗口强制全选问题；安装/卸载操作同时保存勾选状态
   - 三态关联状态视觉区分 — 无关联（无色）、已关联未默认（橙色 `#1AFF9800`）、已关联且默认（绿色 `#1A4CAF50`）
   - 默认程序提示 — 安装成功后弹窗增加"请在系统设置中设为默认程序"提示
   - `AppMessageBox.ShowWithAction` — 扩展消息框支持可选操作按钮
   - 删除按钮加宽 — 自定义扩展名 ✕ 按钮从 20×20 扩至 36×24
   - Status 颜色触发修复 — 将 `x:Static` 枚举 DataTrigger 改为 bool 属性绑定
   - `GetExePath` 修复 — 改用 `Assembly.Location` 替代 `Environment.ProcessPath`，兼容 `dotnet run` 场景
   - 自定义扩展名回退到 exe 图标
3. **文件列表筛选/搜索**：
   - 主工具栏「全部子目录」ToggleButton（🌲 图标）展开递归扁平视图
   - 筛选工具栏（文字搜索 + 日期范围 DatePicker × 2 + 大小范围数字输入+单位 ComboBox）
   - 通用过滤引擎 `ArchiveFilter.cs`（Core/Utils）支持组合 AND 过滤，15 个单元测试
   - 多维过滤引擎 — `SearchFilters` record + `ArchiveFilter.ApplyFilters` 支持文字/日期/大小 AND 组合
   - 空结果提示 — 无匹配文件时居中显示"无匹配的文件"，状态栏同步更新 "显示 N/M 个文件"
   - 筛选工具栏显隐 — ToggleButton 控制

### v0.3.7-refined-5 (2026-06-04) 引擎统一

- ✅ **引擎统一已完成** — SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip（v0.3.4）
- ✅ **批量进度文件列表已完成** — `--compress-separate` / `--extract-*` 批量操作进度窗口 + IPC 合并（v0.3.5）
- ✅ **ExtractSettingsWindow 已完成** — 创建 + 重设计，与 CompressSettingsWindow 视觉一致（v0.3.4 创建 / v0.3.6 重设计）
- ✅ **COM 右键菜单已完成** — .NET 9 comhost，Explorer 原生 COM 组件替代静态注册（v0.3.7）

### v0.3.7-refined-4 (2026-06-03) 关于窗口重设计

1. **AboutWindow 新建** — 替代旧 `AppMessageBox.Show()` 为 4 标签页 WPF 对话框（关于/作者/依赖库/致谢），`ResizeMode="CanResizeWithGrip"`，`MinWidth="400"` `MinHeight="350"`，App.ico 窗口图标
2. **关于 Tab 重设计** — 2 列 Grid（标签+内容）展示软件信息，包括：介绍（"轻量级全功能 Windows 压缩/解压软件"）、技术描述、支持格式、许可证、GitHub 仓库链接、Gitee 仓库链接；可扩展行结构
3. **作者 Tab 重设计** — 2 列 Grid 展示 MantisZen 联系方式：邮箱（mailto 超链接）、GitHub 个人页、Gitee 个人页
4. **依赖库 Tab** — 10 项依赖的 4 列表格（库名/版本/许可证/用途），硬编码数据，与 README 一致
5. **致谢 Tab** — 三段式感谢文本（所有开源项目、7-Zip、OpenCode + Sisyphus Agent）
6. **超链接统一处理** — `RequestNavigate` 事件 → `Process.Start(UseShellExecute=true)`，含异常日志
7. **21 个 About_* 本地化键** — 中英文双语，`L.cs` 常量，`l:L` XAML 绑定
8. **13 个冒烟测试** — `AboutWindowTests.cs` 验证 JSON 键存在性/非空/双语一致性/向后兼容（`Main_About_Text` 保留）
9. **死键审计** — `Main_About_Text`/`Main_About_Title` 确认代码无引用（仅 L.cs + JSON 保留）

### v0.3.7-refined-3 (2026-06-03) 密码工具栏 + 关闭压缩包 + 捐赠 + 空状态重设计 + 压缩冲突增强

1. **密码按钮三态重设计** — 工具栏密码按钮改为三种视觉状态：🔑 无加密、🔒 有加密未匹配、🔓 已匹配密码；点击 🔒/🔓 分别弹出密码输入/已匹配密码查看对话框
2. **MatchedPasswordDialog 新建** — 查看已匹配密码的对话框，支持眼睛切换明文/密文 + 一键复制
3. **Theme_StatusSuccessBg 主题色** — 亮色/暗色主题新增绿色成功背景色
4. **PasswordDialog/PasswordManagerWindow RevealByDefault 修复** — 两处对话框现在正确读取 `PasswordRevealByDefault` 设置
5. **密码管理器图标统一** — 工具栏、菜单、设置页面全部改用 🔐 图标
6. **密码输入对话框修复** — 原「显示密码」CheckBox 无事件处理，替换为可用的 👁 Button
7. **关闭压缩包菜单** — 文件菜单新增 ❌ 关闭压缩包 (Ctrl+W)，重置主界面到空状态
8. **文件菜单重排序** — 前三项调整为：🆕 新建 → 📂 打开 → 🕐 最近文件 → ❌ 关闭
9. **捐赠对话框** — 帮助菜单新增 ❤️ 捐赠，弹出 DonationDialog
10. **空状态重设计** — 替换旧 DropHint 为居中提示 + 两张操作卡片（📂 打开压缩包、🔐 密码管理器）
11. **CompressConflictDialog 新增"应用到全部"** — 勾选后对后续所有冲突文件自动应用相同操作
12. **GUI 独立压缩路径适配** — `RunSeparateCompressAsync` 新增 applyToAll 记忆逻辑
13. **CLI 独立压缩路径适配** — `RunCompressSeparateBatch` 同步添加 applyToAll 记忆逻辑
14. **压缩流程统一计划文档** — 创建 `.sisyphus/plans/compress-service-unify.md`
15. **CompressConflictDialog 新增目标文件信息面板** — 展示目标文件信息（大小、修改时间、完整路径）
16. **批量进度文件列表新增「已跳过」状态** — `BatchItemStatus` 枚举新增 `Skipped`

### v0.3.7-refined-2 (2026-06-02) 压缩窗口密码 Tab 重设计 + 调试日志增强

1. **密码选项卡布局重设计** — 对照 `docs/design-compress-password-tab.md` 修复全部差异：
   - 密码库条目改为两行显示（描述 + 规则）
   - 👁 按钮实现真正的 PasswordBox/TextBox 切换（主密码 + 确认密码同时切换）
   - 两个 RadioButton 始终可用，仅内容面板切换禁用/透明度
   - `PwdSelectedStatus` 始终显示，未选择时显示默认提示文字
   - 密码强度改用 `●` + 颜色（红/橙/绿）替代 emoji
   - "自动规则"移到规则文本框左侧，与"规则"标签上下排列
   - "仅 zip 和 7z 支持加密"提示移到 EncryptCheckBox 右侧
   - 两个 GroupBox 与共享区之间增加分隔线
   - "保存到密码库"默认勾选
   - 选中密码条目不再覆盖规则框内容
   - 源文件增减触发自动规则重新计算
   - 搜索框占位文字不再误过滤密码列表
   - 共享区描述/规则框用 `IsEnabled` 替代 `IsReadOnly`，显示标准禁用外观
   - 切到加密 tab 时统一调用 `RefreshPasswordTabUI()` 刷新所有 UI 状态
2. **QuickVerifyPassword 调试日志** — catch 块新增 `TraceLog` 记录异常类型和消息
3. **`PasswordEntry.PatternsDisplay` 属性** — 新增 `[JsonIgnore]` 计算属性，供 XAML 两行列表绑定

### v0.3.7-refined (2026-06-01) COM 右键菜单完善（图标 + 文本 + 本地化）

1. **图标系统重写** — `CreateCompatibleBitmap` → `CreateDIBSection` 32-bit DIB，修复 `MIIM_BITMAP` 透明背景变纯色问题
2. **主菜单标题图标** — "打开/解压" 和 "压缩" 弹出菜单从 `InsertMenu` + `MF_POPUP` 改为 `InsertMenuItem` + `MIIM_SUBMENU` + `MIIM_BITMAP`
3. **CleanupIconCache 时序修复** — 从 `QueryContextMenu` 末尾移到开头
4. **菜单文本精简** — 去掉所有 "用 MantisZip" 前缀
5. **多选文件动态文本** — 选择多个文件时："打开压缩包 等 N 个文件"、"原地解压N个压缩包" 等
6. **菜单文本本地化** — 新增 8 个 `ShellExt_*` key 到 `L.cs` + `strings.zh.json` + `strings.en.json`

### v0.3.7 (2026-05-31) COM 右键菜单 + 注册表设置同步

1. **新建 MantisZip.ShellExt 项目** — .NET 9 类库，`<EnableComHosting>true</EnableComHosting>`，comhost 模式
2. **ContextMenuHandler.cs** — `IShellExtInit` + `IContextMenu` 完整实现，8 个菜单项
3. **NativeMethods.cs** — Win32 互操作：`CF_HDROP` 提取、`InsertMenu`/`MenuItemInfo`、GDI `DrawIconEx` 图标转换、PIDL 路径解析
4. **COM 注册** — `ShellIntegration.InstallCom()`/`UninstallCom()` 在 `HKCU\Software\Classes` 写入 CLSID + shellex
5. **设置同步** — `AppSettings.Save()` → `SyncContextMenuToRegistry()` 写 10 个 DWORD 到 `HKCU\Software\MantisZip\ContextMenu`
6. **构建集成** — ShellExt 项目添加进 `.sln`，UI 项目引用，post-build 自动复制 comhost.dll
7. **版本升级** — 0.3.7

### v0.3.6 (2026-05-30) ExtractSettingsWindow UI 重构

1. **ExtractSettingsWindow 布局重写** — 从简易 Auto 堆叠改造为与 CompressSettingsWindow 一致的 **TabControl + GroupBox + 2-column Grid** 架构
2. **配色对齐** — 移除所有显式 Foreground/Background/BorderBrush，靠主题继承
3. **输出路径布局稳定** — 不再 Visibility 隐藏/显示输出路径行（消除跳动）
4. **新增本地化键** — 8 个新键 + 中英文翻译
5. **版本升级** — 0.3.6

### v0.3.5 (2026-05-30) 批处理进度文件列表 + IPC 合并

1. **ProgressWindow 批处理文件列表** — `BatchItemStatus` 枚举 + `BatchProgressItem` 模型；GridView 三列；每项独立状态指示
2. **`--compress-separate` IPC 合并** — Mutex `MantisZipCompressSeparateMutex` + Pipe；800ms 收集窗口
3. **`--compress-combined` IPC 合并** — Mutex + Pipe；跨驱动器时提示用户输入归档名称
4. **ExtractSettingsWindow 集成** — `HandleExtractBatch` / `HandleExtractBatchCore` 统一入口
5. **Unicode 编码问题修复** — `Process.Start` 调用改为 `UseShellExecute = true`
6. **版本升级** — 0.3.5

### v0.3.4 (2026-05-28~29) 引擎统一 + ExtractSettingsWindow + 调试日志

1. **引擎统一** — SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip 2.0.45
   - SharpSevenZipExtractor 替代 SevenZipExtractor 的 ArchiveFile
   - SharpSevenZipCompressor 替代 7z.exe Process 调用
   - ExtractEntriesAsync 实现（原 NotSupportedException）
   - SevenZipExtractor NuGet 包已移除
2. **SharpSevenZip 升级** — 2.0.12 → 2.0.45
3. **ZIP 添加/删除进度修复** — 移除 SharpZipLib BeginUpdate/CommitUpdate，改用提取→重压缩方案
4. **ExtractSettingsWindow 创建** — 初始版本（XAML + code-behind + ExtractOutputMode 枚举）
5. **7z.dll 状态检测与管理** — `SevenZipEngine` 新增 `CheckDllStatus()` / `ResetDllPath()` API
6. **PreserveDirectoryRoot 设置** — Core 层 `ArchiveOptions.PreserveDirectoryRoot` 属性
7. **async void 修复** — 工具栏 Click 事件处理器从 `async void` 改为 `async Task`
8. **调试日志系统增强** — 引擎分发、文件扫描、智能解压分析、文件冲突解决、冲突弹窗用户操作、分卷输出、密码自动尝试 7 类日志

### v0.3.3 (2026-05-27) 安装器多语言与预览设置增强

1. **数据表格行/列限制可配置** — 设置 → 预览 → 数据表格子标签页
2. **字体预览字号可配置** — 设置 → 预览 → 字体滑块（8–36）
3. **WebView2 启动时预初始化** — `MainWindow.Loaded` 中提前调用 `EnsureWebView2InitializedAsync()`
4. **Inno Setup 安装包多语言支持** — 简体中文安装界面
5. **安装时配置向导页** — 主题/右键菜单/文件关联
6. **安装设置持久化** — 安装器写入 `settings.json`，首次启动自动生效
7. **版本升级** — 0.3.3

### v0.3.2 (2026-05-27) 代码拆分与文档交叉更新

1. **App.xaml.cs 文件拆分** — 1977 行拆为 5 个 partial class 文件
2. **版本号更新** — 0.3.1 → 0.3.2
3. **文档交叉更新** — PLAN.md / PROGRESS.md / AGENTS.md / README.md

### v0.3.1 (2026-05-26) 预览修复与注释

1. **WebView2 PDF 内容渲染** — 替换 WebBrowser 为 WebView2
2. **PDF 页数统计修复** — 修复线性化 PDF 三种场景页数显示 `--`
3. **图片缩放修复** — 默认 FitWindow 避免小图拉伸
4. **GIF 帧导航增强** — 仿 YouTube 式时间戳 + 帧位置选择器
5. **字体预览渲染优化** — 字体预览字号独立于文本预览字号
6. **PE/PDF 预览缓存** — `ConcurrentDictionary` 缓存元数据 5s
7. **README.md 功能一览表** — 分类分级的完整功能列表
8. **代码注释规范化** — 400+ 处方法头注释、内部类注释、参数注释
9. **文件头注释** — 170+ 源文件补齐文件级别注释（用途/版权/作者）
10. **计划文档** — `.sisyphus/plans/` 新增 17 份计划文档
11. **关于页面** — 添加 7-Zip LGPL 许可证声明

### v0.1.0 (2026-04-24) 初始版本

1. **ZIP 解压** — 基于 SharpZipLib，支持 GBK 编码
2. **ZIP 压缩** — 基于 SharpZipLib
3. **7z 解压** — 基于 SevenZipExtractor
4. **RAR 解压（只读）** — 基于 SevenZipExtractor
5. **TAR 压缩/解压** — 基于 SharpZipLib
6. **GZ 压缩/解压** — 基于 SharpZipLib
7. **TAR.GZ (.tgz) 压缩/解压** — 基于 SharpZipLib
8. **目录树导航** — 左侧面板显示压缩包内目录结构
9. **文件列表** — 右侧面板显示当前目录下的直接子项
10. **密码管理** — 支持 glob/regex 模式匹配的密码管理器
11. **密码输入对话框** — 下拉选择已保存的密码
12. **版本号显示** — 右下角状态栏显示
13. **拖拽解压** — 拖拽 ZIP 文件到窗口解压
14. **拖拽压缩** — 拖拽普通文件生成 ZIP

---

## 历史设计方案索引

以下设计方案对应功能已在过往版本中完成，对应设计文档存于 `.sisyphus/plans/` 供回溯参考：

| 功能 | 设计文档 | 实现版本 |
|------|----------|:--------:|
| 预览格式扩展（12 种元数据格式） | [preview-extended-formats.md](.sisyphus/plans/preview-extended-formats.md) | v0.3.0 |
| 快速压缩拆分为独立/合并两项 | [split-compress.md](.sisyphus/plans/split-compress.md) | v0.2.10 |
| 加载大文件 overlay | [archive-loading-progress.md](.sisyphus/plans/archive-loading-progress.md) | v0.3.1 |
| 添加到/从压缩包删除 | [archive-add-delete.md](.sisyphus/plans/archive-add-delete.md) | v0.2.9 |
| 暗色/亮色主题 | [dark-theme.md](.sisyphus/plans/dark-theme.md) | v0.2.9 |
| 日志隐私脱敏 | [log-privacy-redaction.md](.sisyphus/plans/log-privacy-redaction.md) | v0.2.8 |
| 国际化 (i18n) | [i18n-localization.md](.sisyphus/plans/i18n-localization.md) | v0.2.8 |
| 智能解压 (Smart Extract) | [smart-extract.md](.sisyphus/plans/smart-extract.md) | v0.2.10 |
| 文件列表筛选/搜索 | [file-list-filter-search.md](.sisyphus/plans/file-list-filter-search.md) | v0.3.8 |
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip) | [engine-unification-sharpcompress.md](.sisyphus/plans/engine-unification-sharpcompress.md) | v0.3.4 |
| 文件大小进度条 | [file-size-progress-bar.md](.sisyphus/plans/file-size-progress-bar.md) | v0.3.4 |
| PNG 透明通道控制 | [png-transparency-3way.md](.sisyphus/plans/png-transparency-3way.md) | v0.3.4+ |
| 批量进度文件列表 | [batch-progress-list.md](.sisyphus/plans/batch-progress-list.md) | v0.3.5 |
| 解压配置面板 (ExtractSettingsWindow) | [extract-settings-window.md](.sisyphus/plans/extract-settings-window.md) | v0.3.6 |
| COM 右键菜单 | [com-context-menu.md](.sisyphus/plans/com-context-menu.md) | v0.3.7 |
| COM 迁移映射表 | [com-migration-mapping.md](.sisyphus/plans/com-migration-mapping.md) | v0.3.7（辅助文档） |
| 压缩窗口密码 Tab 重设计 | [design-compress-password-tab.md](docs/design-compress-password-tab.md) | v0.3.7-refined-2 |
| 关于窗口重设计 | [about-window-redesign.md](.sisyphus/plans/about-window-redesign.md) | v0.3.7-refined-4 |
| 文件关联 per-extension ProgId | [file-assoc-per-extension.md](.sisyphus/plans/file-assoc-per-extension.md) | v0.3.9 |
