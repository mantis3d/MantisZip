# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpCompress + SharpSevenZip

## 版本
- **当前版本**: 0.3.7-refined-4
- **发布日期**: 2026-06-03

## 规划中
- ✅ **引擎统一已完成** — SharpZipLib→SharpCompress + 7z.exe/SevenZipExtractor→SharpSevenZip（v0.3.4）
- ✅ **批量进度文件列表已完成** — `--compress-separate` / `--extract-*` 批量操作进度窗口 + IPC 合并（v0.3.5）
- ✅ **ExtractSettingsWindow 已完成** — 创建 + 重设计，与 CompressSettingsWindow 视觉一致（v0.3.4 创建 / v0.3.6 重设计）
- ✅ **COM 右键菜单已完成** — .NET 9 comhost，Explorer 原生 COM 组件替代静态注册（v0.3.7）
- **文件过滤功能** — 按类型/文件名/大小/日期过滤压缩和解压（详见 `.sisyphus/plans/file-filter-feature.md`）
  - 压缩时只打包匹配条件的文件
  - 解压时只提取匹配条件的条目
  - 支持命名预设持久化
- **代码重构持续** — `CompressSettingsWindow.xaml.cs` (684 行)、`SevenZipEngine.cs` (630 行)、`ShellIntegration.cs` (482 行) 仍有拆分空间

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

### v0.3.7-refined-3 (2026-06-03) 密码工具栏 + 关闭压缩包 + 捐赠 + 空状态重设计

1. **密码按钮三态重设计** — 工具栏密码按钮改为三种视觉状态：🔑 无加密、🔒 有加密未匹配、🔓 已匹配密码；点击 🔒/🔓 分别弹出密码输入/已匹配密码查看对话框
2. **MatchedPasswordDialog 新建** — 查看已匹配密码的对话框，支持眼睛切换明文/密文 + 一键复制
3. **Theme_StatusSuccessBg 主题色** — 亮色/暗色主题新增绿色成功背景色，用于 MatchedPasswordDialog 密码行
4. **PasswordDialog/PasswordManagerWindow RevealByDefault 修复** — 两处对话框现在正确读取 `PasswordRevealByDefault` 设置
5. **密码管理器图标统一** — 工具栏、菜单、设置页面全部改用 🔐 图标
6. **密码输入对话框修复** — 原「显示密码」CheckBox 无事件处理，替换为可用的 👁 Button
7. **关闭压缩包菜单** — 文件菜单新增 ❌ 关闭压缩包 (Ctrl+W)，重置主界面到空状态（清空文件列表、目录树、预览、密码状态、状态栏等）
8. **文件菜单重排序** — 前三项调整为：🆕 新建 → 📂 打开 → 🕐 最近文件 → ❌ 关闭
9. **捐赠对话框** — 帮助菜单新增 ❤️ 捐赠，弹出 DonationDialog：打赏二维码占位 + 三个平台链接（爱发电/GitHub Sponsors/Buy Me a Coffee）
10. **空状态重设计** — 替换旧 DropHint（📁 + 文字 + 超链接）为：居中提示文字 + 两张并排操作卡片（📂 打开压缩包、🔐 密码管理器）

## 版本历史（从新到旧）

### v0.3.7-refined-3 (2026-06-03) 压缩冲突增强："应用到全部" + 目标文件信息面板 + 压缩流程统一计划

1. **CompressConflictDialog 新增"应用到全部"** — 压缩冲突对话框增加 CheckBox，勾选后对后续所有冲突文件自动应用相同操作；勾选时 Rename 按钮文字变为"自动重命名"，输入框禁用；未勾选时显示"重命名"（半自动编辑）；取消按钮改为"跳过"
2. **GUI 独立压缩路径适配** — `RunSeparateCompressAsync` 新增 `applyToAll` / `chosenAction` 闭包记忆逻辑，与提取端 `CreateExtractOptions` 模式一致
3. **CLI 独立压缩路径适配** — `RunCompressSeparateBatch` 同步添加 applyToAll 记忆逻辑，同一套 CompressConflictDialog 在两条循环路径中均支持"应用到全部"
4. **压缩流程统一计划文档** — 创建 `.sisyphus/plans/compress-service-unify.md`，分析压缩端 3 个独立循环 vs 提取端 1 个集中入口的架构差距，提出统一 CompressService 主方案 + 冲突 UI 嵌入 ProgressWindow 备选方案
5. **CompressConflictDialog 新增目标文件信息面板** — 提取冲突对话框已有源/目标对比面板，压缩冲突对话框同样展示目标文件信息（大小、修改时间、完整路径），复用 `Conflict_SizeLabel` / `Conflict_DateLabel` 本地化键；新增 `CompressConflict_TargetLabel` / `CompressConflict_PathLabel` 键
6. **批量进度文件列表新增「已跳过」状态** — `BatchItemStatus` 枚举新增 `Skipped`；`BatchStatusToIconConverter` / `BatchStatusToTextConverter` / `ProgressStatusToBackgroundConverter` 三个转换器同步添加 Skipped 分支（⏭️ 图标、cyan `#00BCD4` 背景、"已跳过"/"Skipped"）；`UpdateBatchItemStatus` 对 Skipped 自动设置 `Progress=100`；`RunCompressSeparateBatch` Cancel 分支由 `failed++` 改为 `skipped++` + 调用 `UpdateBatchItemStatus(Skipped)`，解决最后一个文件跳过时始终显示 Progress=0 的 bug

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
2. **QuickVerifyPassword 调试日志** — catch 块新增 `TraceLog` 记录异常类型和消息，密码验证失败时可排查原因
3. **`PasswordEntry.PatternsDisplay` 属性** — 新增 `[JsonIgnore]` 计算属性，供 XAML 两行列表绑定



### v0.3.7-refined  (2026-06-01)  COM 右键菜单完善（图标 + 文本 + 本地化）

1. **图标系统重写** — `CreateCompatibleBitmap` → `CreateDIBSection` 32-bit DIB，修复 `MIIM_BITMAP` 透明背景变纯色问题（原因为 DDB 不含 alpha 通道）
2. **主菜单标题图标** — "打开/解压" 和 "压缩" 弹出菜单从 `InsertMenu` + `MF_POPUP` 改为 `InsertMenuItem` + `MIIM_SUBMENU` + `MIIM_BITMAP`，菜单标题现在也显示图标
3. **CleanupIconCache 时序修复** — 从 `QueryContextMenu` 末尾移到开头（Explorer 异步渲染菜单，末尾删除 HBITMAP 导致图标不显示）
4. **菜单文本精简** — 去掉所有 "用 MantisZip" 前缀；"解压到此处" → "原地解压包"，"智能解压到此处" → "智能原地解压"；"用 MantisZip 压缩" → "压缩……"
5. **多选文件动态文本** — 选择多个文件时："打开压缩包 等 N 个文件"、"原地解压N个压缩包"、"智能原地解压N个压缩包"
6. **菜单文本本地化** — 新增 8 个 `ShellExt_*` key 到 `L.cs` + `strings.zh.json` + `strings.en.json`；`ShellIntegration.WriteMenuTextToRegistry()` 安装时将当前语言文本写入注册表；ShellExt 通过 `LoadSettingsFromRegistry()` 读取（硬编码写死回退）
7. **文档同步** — AGENTS.md / PROGRESS.md / PLAN.md / manual-test-checklist.md

### v0.3.7 (2026-05-31) COM 右键菜单 + 注册表设置同步

1. **新建 MantisZip.ShellExt 项目** — .NET 9 类库，`<EnableComHosting>true</EnableComHosting>`，comhost 模式
2. **ContextMenuHandler.cs** — `IShellExtInit` + `IContextMenu` 完整实现，8 个菜单项（打开/压缩/压缩到独立的/压缩到父目录/解压到此处/智能解压/解压到压缩包名/解压到…），层叠/动词双模式，归档扩展名过滤，动态文件名，`Process.Start` 命令行调度，HICON→HBITMAP 图标缓存
3. **NativeMethods.cs** — Win32 互操作：`CF_HDROP` 提取、`InsertMenu`/`MenuItemInfo`、GDI `DrawIconEx` 图标转换、PIDL 路径解析
4. **COM 注册** — `ShellIntegration.InstallCom()`/`UninstallCom()` 在 `HKCU\Software\Classes` 写入 CLSID + shellex，`Install()` 优先 COM 失败回退静态，`IsInstalled` 优先检查 CLSID
5. **设置同步** — `AppSettings.Save()` → `SyncContextMenuToRegistry()` 写 10 个 DWORD 到 `HKCU\Software\MantisZip\ContextMenu`
6. **构建集成** — ShellExt 项目添加进 `.sln`，UI 项目引用，post-build 自动复制 `MantisZip.ShellExt.comhost.dll` 到输出目录
7. **版本升级** — 0.3.7
8. **文档全量同步** — PLAN.md / PROGRESS.md / AGENTS.md / ARCHITECTURE.md / manual-test-checklist.md

### v0.3.6 (2026-05-30) ExtractSettingsWindow UI 重构

1. **ExtractSettingsWindow 布局重写** — 从简易 Auto 堆叠改造为与 CompressSettingsWindow 一致的 **TabControl + GroupBox + 2-column Grid** 架构
   - TabControl TabItem 模板完全复用（Accent 下划线选中态 + Hover 背景）
   - Tab 1「基本」: GroupBox「源文件」+ GroupBox「解压选项」(输出方式 RadioButton 4 项 + 输出路径 TextBox+Browse)
   - Tab 2「高级」: GroupBox「行为设置」(文件冲突 RadioButton 4 项 + 打开文件夹 CheckBox)
   - 2-column Grid 标签列宽 80px，TextBox 高 24px，按钮 80×28，Margin=5,0
   - 窗口宽度 530px（与 CompressSettingsWindow 一致）
2. **配色对齐** — 移除所有显式 Foreground/Background/BorderBrush，靠主题继承，与 CompressSettingsWindow 视觉效果统一
3. **输出路径布局稳定** — 不再 Visibility 隐藏/显示输出路径行（消除跳动），改为始终可见，非手动模式时禁用 TextBox + Browse，显示计算出的目标路径预览
4. **新增本地化键** — 8 个新键（Tab 标题/GroupBox 标题/标签文本）+ 中英文翻译
5. **文档全量同步** — PROGRESS.md / PLAN.md / ARCHITECTURE.md / CLI.md / manual-test-checklist.md / AGENTS.md / 计划文档 / 学习笔记
6. **版本升级** — 0.3.6

### v0.3.5 (2026-05-30) 批处理进度文件列表 + IPC 合并

1. **ProgressWindow 批处理文件列表** — 新增 `BatchItemStatus` 枚举（Pending/InProgress/Completed/Failed）+ `BatchProgressItem` 模型；ProgressWindow 新增 GridView 三列（状态图标 + 文件名 + 进度百分比）；每项独立状态指示（绿色完成/蓝色进行中/红色失败）；100ms 节流更新
2. **ProgressWindow 批处理 API** — `InitBatchMode(List<string>)` / `SetCurrentBatchItem(int)` / `UpdateBatchItemStatus(int, BatchItemStatus, string?)` 支持逐项更新；`BatchCompletedCount` 属性；`BatchCompleteText` 汇总（N 成功 / M 失败）
3. **ProgressWindow 批处理界面** — 原进度条上下位置显示文件列表，图标列 + 文件名列 + 进度 % 列；`HorizontalContentAlignment="Stretch"` 确保内容铺满
4. **密码匹配集成** — `HandleExtractBatchCore` 对加密压缩包先调用 `TryMatchPassword` 自动尝试已保存密码，匹配成功则直接提取，失败显示密码错误并跳过
5. **`--compress-separate` IPC 合并** — 使用 Mutex `MantisZipCompressSeparateMutex` + Pipe `MantisZipCompressSeparatePipe`；Windows 多实例 IPC 收集路径 → 依次独立的 ProgressWindow 处理；800ms 收集窗口
6. **`--compress-combined` IPC 合并** — 使用 Mutex `MantisZipCompressCombinedMutex` + Pipe `MantisZipCompressCombinedPipe`；跨驱动器时提示用户输入归档名称
7. **ExtractSettingsWindow 集成** — `HandleExtractBatch` / `HandleExtractBatchCore` 统一入口；`--extract` 弹出 ExtractSettingsWindow（4 种输出模式）；`--extract-here` / `--extract-smart` / `--extract-to-name` 直接调用批处理核心
8. **Unicode 编码问题修复** — `Process.Start` 调用改为 `UseShellExecute = true`，避免 `Win32Exception` 错误
9. **App.Cli.cs 新增 6 个 CLI 处理函数** — `HandleCompressSeparate`、`HandleCompressCombined`、`HandleExtractHere`、`HandleExtractSmart`、`HandleExtractToNamed`、`HandleExtract`；全部通过 DispatcherTimer 实现 IPC 收集窗口
10. **ExtractSettingsWindow 测试** — 92 个测试全部通过，手动验证压缩/解压流程正常
11. **版本升级** — 0.3.5

### v0.3.4 (2026-05-29) ExtractSettingsWindow + 设置界面增强

1. **ExtractSettingsWindow 创建** — 初始版本（XAML + code-behind + ExtractOutputMode 枚举）；支持 4 种输出模式（手动输入/解压到此处/智能解压/解压到压缩包名）；文件列表（只读轻量版）+ 文件计数；11 个本地化键
2. **CompressedDisplayMode 列展示** — 文件列表新增列，对不支持压缩的格式（RAR/ISO 等）显示 `--` 占位；分离目录独立基准线排序开关
3. **7z.dll 状态检测与管理** — `SevenZipEngine` 新增 `CheckDllStatus()` / `ResetDllPath()` API；设置窗口显示 7z.dll 状态（就绪/未找到）；找不到时允许用户手动指定路径
4. **PreserveDirectoryRoot 设置** — Core 层 `ArchiveOptions.PreserveDirectoryRoot` 属性；SevenZipEngine 压缩时传参 `compr.PreserveDirectoryRoot`；设置窗口压缩标签页新增复选框
5. **async void 修复** — 工具栏 Click 事件处理器从 `async void` 改为 `async Task`，消除未观测异常风险
6. **compress preset / COM context menu 计划文档** — 新增两份详细设计方案
7. **文档更新** — PLAN.md / AGENTS.md / 多份计划文档同步

### v0.3.4 (2026-05-28) 引擎统一完成 + 进度平滑修复

1. **引擎统一 Phase 4 完成** — 7z.exe/SevenZipExtractor → SharpSevenZip 2.0.45
   - SharpSevenZipExtractor 替代 SevenZipExtractor 的 ArchiveFile，读取所有 7z/RAR 操作
   - SharpSevenZipCompressor 替代 7z.exe Process 调用
   - ExtractEntriesAsync 实现（原 NotSupportedException）
   - SevenZipExtractor NuGet 包已移除
2. **SharpSevenZip 升级** — 2.0.12 → 2.0.45
3. **ZIP 添加/删除进度修复** — ZipEngine.AddToArchiveAsync 和 DeleteEntriesAsync 重写
   - 移除 SharpZipLib BeginUpdate/CommitUpdate 黑盒
   - 改用提取→重压缩方案（与 SevenZipEngine.DeleteEntriesAsync 一致）
   - 逐文件字节加权进度，无跳跃
4. **文档更新** — README.md / AGENTS.md / PLAN.md / PROGRESS.md / manual-test-checklist.md 同步更新
5. **版本升级** — 0.3.4

### v0.3.4 (2026-05-28) 调试日志系统增强

1. **引擎分发日志** — `ArchiveEngineFactory.GetEngine`/`GetEngineByExtension` 记录引擎选择结果和扩展名映射
2. **文件扫描日志** — `FileScanner.CollectFiles` 记录源路径数量、扫描的文件总数和总字节数
3. **智能解压分析日志** — `ArchiveStructureAnalyzer.HasSingleRootDirectory` 记录每个文件根目录判定过程和最终结论
4. **文件冲突解决日志** — `FileConflictHelper.ResolveByAction` 记录冲突动作和路径；`ShouldOverwriteByTime`/`ShouldOverwriteBySize` 记录文件对比结果；原有空 catch 块改为异常日志
5. **冲突弹窗用户操作日志** — `ConflictDialog`/`CompressConflictDialog` 记录用户选择的处理动作（覆盖/跳过/重命名等）、自定义文件名和"应用到全部"状态
6. **分卷输出日志** — `SplitOutputStream.OpenNextPart` 记录每个分卷的序号、路径和流创建成功状态
7. **密码自动尝试日志** — `TryMatchPassword` 记录匹配过程：遍历每条规则、尝试结果（成功/失败/密码错误/压缩包损坏/达到最大尝试次数）
8. **代码扫描确认** — 验证所有 `async void` / `BeginInvoke` / `Process.Start` / `NamedPipeServerStream` / 等待/通知模式 / 未捕获的 `Task` 异常均已正确处理

### v0.3.3 (2026-05-27) 安装器多语言与预览设置增强

1. **数据表格行/列限制可配置** — 设置 → 预览 → 数据表格 新增子标签页，可分别配置 CSV/SQLite 预览的最大行数和最大列数（范围 3–1000，默认 100）；`AppSettings` 新增 `MaxTablePreviewRows` / `MaxTablePreviewCols`
2. **字体预览字号可配置** — 设置 → 预览 → 字体 新增「预览文本字号」滑块（8–36），控制主窗口字体预览的实际渲染字号；`AppSettings` 新增 `FontPreviewFontSize`
3. **WebView2 启动时预初始化** — `MainWindow.Loaded` 事件中提前调用 `EnsureWebView2InitializedAsync()`，浏览器进程在后台提前创建，消除首次显示 HTML/Markdown/SVG/PDF 预览时的等待延迟
4. **Inno Setup 安装包多语言支持** — 新增简体中文安装界面，安装开始时可选语言；中英文向导文字由 `[CustomMessages]` 管理
5. **安装时配置向导页** — 新增「安装配置」自定义页面，可设置主题（浅色/深色）、安装右键菜单、关联压缩包格式
6. **安装设置持久化** — 安装器将用户选择的 Language + Theme 写入 `%LOCALAPPDATA%\MantisZip\settings.json`，首次启动自动生效
7. **条件系统集成** — `[Run]` 节新增 `--install-shell` / `--install-assoc` 条件执行，根据复选框状态静默安装
8. **文档交叉对比** — 遍历 23 个 `.sisyphus/plans/` 计划文件与 `docs/`，修复遗漏和过时引用
9. **AGENTS.md 修正** — 修正 v0.2.13 错误版本标签为 v0.3.1、补全计划列表遗漏项
10. **PLAN.md 同步** — 补充 3 项已实现设计方案（preview-extended-formats / split-compress / archive-loading-progress）、2 项待实现计划（batch-progress-list / explorer-path-switcher）
11. **版本升级** — 0.3.3

### v0.3.2 (2026-05-27) 代码拆分与文档交叉更新

1. **App.xaml.cs 文件拆分** — 1977 行拆为 5 个 partial class 文件：App.xaml.cs (600)、App.Cli.cs (967)、App.PipeServer.cs (132)、App.Password.cs (199)、App.Logging.cs (141)
2. **版本号更新** — 0.3.1 → 0.3.2，同步更新 AppConstants.cs + MantisZip.UI.csproj
3. **docs/PLAN.md 交叉更新** — 项目结构补充 App 拆分文件、依赖版本修正 (CommunityToolkit.Mvvm 8.4.2、Markdig 1.2.0、WebView2 1.0.3967.48)、CSV 预览状态修复、项目状态提升至 Phase 6
4. **docs/PROGRESS.md 更新** — 新增 v0.3.2 条目、规划中追加重构持续项
5. **AGENTS.md 更新** — App 拆分说明、结构图补充 App.Cli/PipeServer/Password/Logging
6. **README.md 更新** — 项目结构图补充 App 拆分文件
7. **文档交叉对比** — 遍历 23 个 `.sisyphus/plans/` 计划文件，确认无过时版本号引用
8. **版本升级** — 0.3.2

### v0.3.1 (2026-05-26) 预览修复与注释

1. **WebView2 PDF 内容渲染** — 替换 WebBrowser 为 WebView2，支持 PDF 原生渲染 + 崩溃自动恢复；隐藏 PDF 工具栏中导致崩溃的按钮（Save/Print/SaveAs/MoreSettings/FullScreen）
2. **PDF 页数统计修复** — 修复线性化 PDF（Pages dict 位于文件末尾）、Count 位于 Type/Pages 之前、正则范围不够三种场景导致的页数显示 `--`
3. **图片缩放修复** — 默认 FitWindow 避免小图拉伸；修复缩放按钮失效、大图缩放后不更新、宽图 FitWidth 居中、防止小图 FitWidth 放大
4. **SQLite 预览修复** — 编码/页面大小/表名获取修复
5. **CSV 预览** — 用 `ShowTablePreview` DataGrid 展示，100 行 × 100 列限制
6. **音频码率/ISO 去重** — `FlacParser` 位深修复，`IsoParser` 去除重复解析
7. **加载大文件 overlay** — 打开大压缩包时显示 "正在加载…" 覆盖层
8. **日志自动轮转 10MB** — `debug.log` 超过 10MB 自动重命名为 `debug.1.log` 并新建
9. **LogRedactor 线程安全** — 双重检查锁定修复并发问题
10. **视频解析器修复** — AVI/MKV/MOV/FLV 解析兼容性修复
11. **CI 升级** — GitHub Actions 升级到 Node.js 24 runtimes
12. **压缩包注释编辑** — 新增 `ArchiveCommentDialog`，主窗口编辑菜单增加「压缩包注释」，使用 SharpZipLib `BeginUpdate()` + `SetComment()` 直接修改存档注释，无需重新压缩（仅 ZIP 格式）
13. **压缩设置窗口 TabControl** — `CompressSettingsWindow` 垂直 GroupBox 改为 TabControl，含「通用」和「注释」两个标签页
14. **注释分配策略** — `ArchiveOptions.Comment` (string?) + `CommentDistribution` 枚举（`AllSame`/`FirstOnly`/`PerLine`），分卷压缩时按策略分配注释
15. **注释 TAB 界面** — 文本框 + 分配策略单选按钮组（仅分离模式下可配） + ZIP 注释提示；暗色/亮色主题同步
16. **编辑菜单状态同步** — `UpdateAddDeleteBtnState` 统一同步添加文件/删除文件/压缩包注释三项
17. **本地化扩展** — 新增 13 个语言键值（压缩注释 + 编辑菜单）
18. **文档同步更新** — README.md / AGENTS.md / PLAN.md / PROGRESS.md
19. **版本升级** — 0.3.1

### v0.3.0 (2026-05-22) — 预览格式扩展

1. **PE 可执行文件元数据预览** — `PeParser` (Core/Utils) 解析 exe/dll 公司、产品名、文件版本、架构、子系统、描述
2. **PDF 元数据 + 内容渲染** — `PdfParser` 提取版本/页数/标题/作者/加密状态；WebView2 渲染 PDF 内容（size-gated）
3. **字体预览（TTF/OTF/WOFF）** — `FontParser` 解析字族名/样式/字形数；Canvas 样本渲染 + 连字开关（Standard/Contextual/Discretionary）
4. **音频元数据（WAV/FLAC）** — `AudioParser`/`FlacParser` 提取时长/采样率/位深/声道/码率
5. **SQLite 数据库预览** — `SqliteParser` 读取编码/页面大小/表名列表；DataGrid 多表分页展示（TabControl）
6. **ISO 9660 映像元数据** — `IsoParser` 提取卷标/格式/大小
7. **BT 种子解析** — `TorrentParser` 解析 InfoHash/Magnet/文件树/Tracker/创建者；树形渲染
8. **Office 文档元数据（docx/xlsx/pptx）** — `OfficeParser` 提取标题/作者/页数/创建时间
9. **SVG 矢量图渲染** — WebView2 渲染
10. **视频元数据（MP4/MKV/AVI）** — `VideoParser` 提取分辨率/时长/编码
11. **GIF 播放控制** — 播放/暂停/逐帧导航/帧号输入跳转 + 工具栏；WpfAnimatedGif
12. **工具栏重构** — `SetToolbar(left, right)` 公共控件左、格式专用控件右、中间自动分隔
13. **预览信息面板增强** — `PreviewExtraInfoPanel`（格式专用 key-value）+ `SetFormatSpecificInfo()`；通用信息上方分隔线
14. **图片透明度切换** — PNG/ICO/WebP 透明棋盘格背景切换
15. **缩放工具栏** — 适应窗口/100%/适应宽度 + active state 高亮
16. **不支持预览图标居中** — 大号居中图标+文字
17. **图片降采样** — `DecodePixelWidth=1920` 防止大图内存爆炸
18. **设置窗口预览子标签页** — 通用/图片/文本/字体/种子/可执行文件；嵌套 TabControl
19. **预览字体选择** — 系统字体列表 + 默认选项；预览样本文本可编辑
20. **新增 100+ 本地化键值** — 图片/GIF/PE/PDF/字体/音频/SQLite/ISO/Torrent/Office/视频预览相关
21. **主题适配** — 工具栏 Border 使用 `Theme_HeaderBg`、信息面板 `Theme_HeaderBg` 等
22. **版本标识** — `.csproj` 中设置 `<Version>0.3.0</Version>`
23. **版本升级** — 0.3.0

### v0.2.12 (2026-05-21) 异常路径审计

1. **多处空 catch 块替换** — 日志模块、explorer 启动、设置窗口、TarGzEngine 等 8 处 `catch { }` 改为 `App.TraceLog`/`CoreLog.Trace`，异常路径不再丢失
2. **OpenZipFile 文件句柄泄漏修复** — 枚举时 try-catch 包裹，异常时释放已打开的 ZipFile
3. **Shell 菜单项条目规范** — 动词使用数字前缀 `01`→`08` 排序，`CommandFlags=8` 改为显式分隔符动词
4. **ZIP 编码条目属性** — `ZipEntry.Encoding` 统一移除冗余设置
5. **版本升级** — 0.2.12

### v0.2.11 (2026-05-21) 编译警告清零

> ⚠️ **Git 历史存疑**：此版本在 git 历史中无对应版本提交记录，内容可能为后续回顾整理。

1. **`String.Split` 移除** — 所有 `String.Split` 改为 `ReadOnlySpan` 的 `Split`，消除 CA1851 警告
2. **Equals 比较警告** — `string.Equals(string, string)` → `string.Equals(string, string, StringComparison)`，消除 CA1309
3. **返回 `null` 的 `await Task`** — 消除 CS4014/CA1849
4. **`Keyboard.IsKeyDown` 绑定** — 用 `Application.Current.Dispatcher` 加载时绑定，消除 CA1849
5. **`MinWidth = "80"` 赋值** — GridLength 显式构造函数，消除 CA1850
6. **版本升级** — 0.2.11
7. **代码扫描确认** — 验证所有 `async void` / `BeginInvoke` / `Process.Start` / `NamedPipeServerStream` / 等待/通知模式 / 未捕获的 `Task` 异常均已正确处理

### v0.2.10 (2026-05-20) 文件关联

1. **文件关联系统** — 注册 `.zip/.7z/.rar/.tar/.gz/.tgz` ProgId + OpenWithProgids，无管理员
2. **设置页面管** — 安装/卸载按钮 + 当前关联状态显示
3. **Shell 注册通用基类** — `FileAssociation` 统一管理 ProgId 注册/注销
4. **CLI 入口** — `--install-assoc` / `--uninstall-assoc`
5. **Shell 注册/注销隔离** — `ShellAssociation` / `ShellContextMenu` 独立反射检测，`IsShellInstalled()` / `IsAssociationInstalled()` 区分状态
6. **安装/卸载一键** — 设置窗口三按钮（安装 Shell / 卸载 Shell / 安装文件关联）
7. **版本升级** — 0.2.10

### v0.2.9 (2026-05-19) 进度渲染修复

1. **进度条渲染修复** — 创建 `BackgroundDispatcherProgress`（自定义 `IProgress<ArchiveProgress>`），以 `Background(3)` 优先级替代 `Progress<T>` 的 `Normal(8)`，使 WPF 渲染在进度更新之间发生，进度条不再卡死
2. **加权进度（添加到压缩包 + 删除）** — `ZipEngine.AddToArchiveAsync`/`DeleteEntriesAsync` 进度按 `(i+1)/(新文件数+旧条目数)*100` 加权，反映全量工作进度
3. **`Progress<T>` 全部替换** — UI 层 7 处 `new Progress<ArchiveProgress>(...)` 改为 `ProgressWindow.CreateBackgroundProgress()`，统一 Background 优先级调度
4. **`SetProgress`/`SetComplete` 简化** — 移除冗余的 `Dispatcher.BeginInvoke`（自定义 IProgress 已处理调度）
5. **`App.TraceLog` 清理** — 所有 `App.TraceLog` 改为 `App.LogDebug`，统一调试日志输出
6. **工具栏图标/文字放大** — 图标 `18→22`，标签 `10→12`，内边距 `(6,4)→(8,6)`
7. **版本升级** — 0.2.9

### v0.2.8 (2026-05-21) IPC 与日志脱敏

1. **IPC 互斥体/管道名修正** — `App.xaml.cs` 中 `L.T()` 写在字符串字面量内导致 `--compress` 多实例 IPC 完全失效，改为固定英文标识符
2. **L.T() 字符串嵌入修复** — `MainWindow.xaml.cs` 两处 `SetStatus` + `App.xaml.cs` 两处消息框的 `L.T()` 调用写在字符串内部的 bug
3. **SevenZipEngine 7z.exe 自动探测** — 新增 `ResolveSevenZipPath()` 自动搜索 Program Files / Program Files (x86) / PATH，找不到才抛异常
4. **SevenZipEngine 双重枚举消除** — `ExtractAsync` 单遍收集条目到列表，避免两遍解码 7z 头部
5. **Window_Drop fire-and-forget 修复** — `_ = LoadArchiveAsync(...)` → `await`，异常不再被吞噬
6. **ProgressWindow 调度一致化** — `SetComplete` 改用 `BeginInvoke` 非阻塞调度，与 `SetProgress` 保持一致
7. **日志隐私脱敏系统** — 新增 `LogRedactor`（Core/Utils），集中式正则脱敏支持驱动器路径+UNC 路径，脱敏委托注入 `CoreLog.RedactOverride`；`App.Log/LogDebug/LogStartup` 写入前调用脱敏；默认完全脱敏模式；帮助说明窗口
8. **版本升级** — 0.2.8

### v0.2.7 (2026-05-18) 设置窗口竖向 Tab + 本地化

1. **设置窗口改用竖向 Tab** — `TabStripPlacement="Left"`，左侧竖排标签页
2. **Tab 图标** — 每个标签页加 Emoji 图标，自定义 `TabItem` ControlTemplate（38px 最小高度、选中态底色 + 3px 蓝色左边框 + 加粗指示）
3. **Tab 布局** — 图标左对齐 + 文字居中，`MinWidth="150"` 更宽松
4. **彩色 Emoji 渲染开关** — 添加 `AppSettings.UseColorEmoji` 和 `App.ApplyTextRenderingMode()`（WPF 原生不支持彩色 Emoji，功能待 Emoji.Wpf 库实现）
5. **本地化补全** — CompressSettingsWindow/SettingsWindow/ConflictDialog/ErrorDialog/CompressConflictDialog/PasswordManagerWindow 等 8 个文件的硬编码中文改用 `l:L` 绑定；需代码填充的 dialog header 改用 `L.T()`/`L.TF()` 方法
6. **L.cs 重构** — 重新生成（423 键），追加 `T()`/`TF()` 静态方法
7. **文档记录** — `docs/PLAN.md` 记录 Emoji.Wpf 方案为 P2 待实现任务
8. **版本升级** — 0.2.7

### v0.2.6 (2026-05-17) 文档重构

1. **README 重写** — 「未来计划」升级为「开发计划」全景路线图，按功能域分 6 组，已完成/规划中状态标记，链接指向详细设计文档
2. **文档冗余清理** — PROGRESS.md 移除待实现功能/技术架构/重复开发日志/已知问题章节；PLAN.md 变更日志缩短为引用 PROGRESS.md
3. **恢复 v0.2.3 版本历史** — 填补被 v0.2.4 覆盖时误删的版本条目，修正 v0.2.2 条目归属
4. **ISO 格式正式记录** — PLAN.md 格式支持表 + README 核心引擎表新增 ISO 解压
5. **新增计划文档** — engine-unification-sharpcompress.md、preview-format-detection.md、file-size-progress-bar.md
6. **文件大小进度条方案** — 纳入开发计划，纯 UI 改动
7. **RAR 预览修复** — ArchiveEntryExtractor 重复标签 + 路径分隔符不匹配导致"未找到条目"
8. **GIF 动画预览** — 引入 WpfAnimatedGif 包，gif 文件用 `ImageBehavior.SetAnimatedSource` 播放
9. **Markdown 增强** — Markdig 管道添加 PipeTables、EmphasisExtras(Strikethrough/Subscript/Superscript)、TaskLists、AutoIdentifiers、EmojiAndSmiley 扩展
10. **统一消息弹窗 AppMessageBox** — 创建 `AppMessageBox.xaml/cs` 替换所有 `MessageBox.Show()`（37+ 调用点，9 个文件）
11. **Zip64 修复** — SharpZipLib 1.4.0 → 1.4.2（修复 Zip64 扩展信息解析错误）
12. **SharpCompress 迁移计划** — 新增 Phase 4（7z.exe → SevenZipSharp），4 阶段全量计划
13. **文件类型图标** — `ShellIntegration.GetIconPath()` + 逐扩展名 `DefaultIcon`，图标文件放 `Resources\Icons\`，编译时自动复制
14. **右键菜单层叠模式默认启用** — `EnableCascadingMenu` 默认值 `false→true`，避免 Windows 15 动词上限导致多文件右键菜单消失
15. **预览提前显示基础信息** — `ShowPreviewAsync` 优先调用 `SetPreviewInfo(item)` + `ShowPreviewPanel()`，加载前先展示文件名/大小/压缩率
16. **ZIP 注释预览** — 读取 `ZipFile.ZipFileComment` 并像文本文件一样显示在预览内容区，字体大小跟随 `TextPreviewFontSize` 设置
17. **空选择显示压缩包总览** — `FileListGrid_SelectionChanged` + `FilterFiles` 末尾检测 `SelectedItems.Count == 0` 时调用 `ShowArchiveInfo()`
18. **目录统计** — `_dirStats` 预计算缓存，文件列表大小列显示目录下所有文件之和，目录预览显示文件数/原始大小/压缩后/压缩率
19. **版本升级** — 0.2.6

### v0.2.5 (2026-05-13) MainWindow 文件拆分

1. **MainWindow 文件拆分** — 92 KB / 2400 行拆为 5 个文件，编译零警告零错误
2. **RAR 路径分隔符修复** — SevenZipEngine 将 `\` 转为 `/`，解决 RAR 文件目录不显示的问题
3. **目录重复显示修复** — FilterFiles 通用去重 + Houdini ZIP 的重复目录问题
4. **密码对话框增强** — 新增"保存到密码库"区域，支持描述和匹配规则输入
5. **文件列表右键菜单** — 解压到…/解压到所在文件夹/复制文件名/复制完整路径
6. **预览大小滑块改进** — 范围 1-100 MB、默认 15 MB、可输入精确数值
7. **版本号简化** — VersionDisplay 改为属性 `=> "v" + Version`，只维护一处
8. **版本升级** — 0.2.5

### v0.2.4 (2026-05-13) 信息面板重构 + 冲突处理

1. **预览信息面板重构** — 三列布局（原始大小|压缩后|压缩率），格式信息在上/通用信息在下，文件名跨三列
2. **共享解压逻辑抽取** — `TryMatchPassword` / `PromptForPassword` / `ExtractWithPasswordAsync`，三处入口统一
3. **压缩目标文件冲突** — CompressConflictDialog，支持覆盖/添加到压缩包/自动重命名/取消
4. **压缩文件读取错误处理** — ErrorDialog，重试/跳过/中止 + 应用到全部
5. **调试日志开关** — 设置→高级，默认关闭，开启后记录详细日志，含打开日志文件按钮
6. **还原文件修改时间** — 解压后用 `File.SetLastWriteTime` 恢复条目原始时间
7. **拖拽解压开关** — 设置→解压，可禁用文件列表拖拽提取
8. **OverwriteIfSmaller** — 新增冲突策略，仅当压缩包内文件更大时覆盖
9. **目录日期修复** — `DateTime.MinValue` 显示 `---` 而非 `0001-01-01`
10. **无密码压缩包密码区修复** — `HasEncryptedEntries` 预检，无加密不显示
11. **版本升级** — 0.2.4

### v0.2.3 (2026-05-13) 文件冲突 + 暂停/继续 + ISO 支持

1. **ISO 格式支持** — SevenZipEngine 扩展处理 `.iso` 镜像文件
2. **文件冲突处理系统** — `FileConflictAction` 枚举 (Overwrite/Rename/Skip/Ask) + `FileConflictHelper.ResolvePath` + `ConflictDialog` 弹窗，全引擎集成
3. **暂停/继续功能** — ProgressWindow `ManualResetEventSlim` + `PauseAwareProgress` 包装器，支持解压中暂停/继续
4. **文件关联** — 注册 `.zip/.7z/.rar` 等格式的 ProgId + OpenWithProgids，设置页管理
5. **CLI 文件关联参数** — `--install-assoc` / `--uninstall-assoc` 命令行安装/卸载文件关联
6. **共享解压选项萃取** — `CreateExtractOptions()` + `ConflictResolver` 回调，三方解压入口统一
7. **版本升级** — 0.2.3

### v0.2.2 (2026-05-13) 密码自动匹配

1. **SevenZipEngine 路径可配置** — 从 `private const` 改为 `static` 属性，启动时从 AppSettings 加载
2. **ZIP AES-256 加密检测** — `IsCrypted || AESKeySize > 0` 覆盖传统加密和 AES 加密
3. **7z 加密检测** — `entry.IsEncrypted` 替代硬编码 `false`
4. **QuickVerifyPassword** — 读 1 字节快速验证密码，不等完整解压
5. **密码区集成到进度条窗口** — 显示尝试/匹配状态、显示/隐藏密码、复制按钮，取代旧独立密码对话框
6. **自动匹配密码** — 打开压缩包时自动遍历已保存密码规则，匹配后预览/拖拽直接使用
7. **工具栏密码按钮** — 加密压缩包未匹配密码时可点击输入
8. **状态栏密码指示** — 显示 🔑 已匹配密码 / 🔒 需要密码
9. **密码对话框自动弹出** — 打开加密压缩包且无匹配密码时弹窗输入
10. **预览加载进度** — 预览窗格显示不确定进度条
11. **预览文件大小上限** — 设置中可配置，超过上限不预览
12. **测试加密压缩包** — TestArchiveAsync 传递 `_currentPassword`，加密时先弹密码框
13. **隐式目录推导** — BuildFolderTree + FilterFiles 从文件路径推导目录节点，解决无显式目录条目的 ZIP 不显示子目录
14. **预览竞态修复** — 添加 `_previewCts` 取消令牌，切换文件时取消旧预览
15. **代码去重** — FormatSize、ResolveExtractDestination、OpenInExplorer 统一入口
16. **ShutdownMode 修复** — CLI 模式关进度条不自动退出，密码对话框能正常显示
17. **版本升级** — 0.2.2

### v0.2.1 (2026-05-12) 加密 ZIP 修复

1. **加密 ZIP 解压密码提示修复** — ZipEngine `ListEntriesAsync` 设置 `IsEncrypted`；`ExtractAsync` 预检 `IsCrypted` 抛出中文异常
2. **密码管理器帮助窗口** — PasswordHelpDialog 讲解匹配规则 + 范例

### v0.2.0 (2026-05-13) 开源 + 安装包

1. **MIT 许可证 + LICENSE 文件** — 项目切换为 MIT 开源
2. **OpenCode 声明 + 捐赠链接** — README 添加 Sisyphus Agent 致谢和捐赠按钮
3. **512x512 应用图标** — App.ico 嵌入 EXE，标题栏 + 任务栏 + 右键菜单全部使用自定义图标
4. **默认布局优化** — 预览面板默认右侧、信息面板默认纵向、窗口 1200×800、目录树默认 396px
5. **滚动条拖拽冲突修复** — FileListGrid 滚动条点击忽略拖拽检测
6. **压缩扫描进度** — ZipEngine/TarGzEngine EnumerateFiles + 100ms 进度报告，不再卡"正在准备..."
7. **Inno Setup 安装包** — 自动生成 MantisZip-0.2.0-Setup.exe 安装程序

### v0.1.6 (2026-05-12) 目录预览 + 工具栏开关

1. **目录预览** — 选中文件夹时显示系统文件夹图标 + 目录信息
2. **工具栏预览开关** — ToggleButton 控制预览面板显隐，状态持久化 ShowPreviewPanel
3. **图片解码降采样优化** — 仅对宽度 >1920px 的图片设 DecodePixelWidth=1920，小图保持原生清晰度
4. **MaxWidth/MaxHeight 约束** — 设 PreviewImage MaxWidth/MaxHeight 为实际像素，防止 Stretch="Uniform" 拉伸小图
5. **预览开关收起残留空白修复** — 收起时 HidePreview() 复位 Grid 行/列尺寸
6. **预览开关重显修复** — 打开时 ShowPreviewPanel() 恢复布局 + 重显选中项预览

### v0.1.5 (2026-05-11) HTML/Markdown 预览 + 分卷压缩

> ⚠️ **Git 历史存疑**：此版本各项功能的实际提交日期分散在 2026-05-09（Shell/拖拽）、2026-05-20（分卷压缩）和 2026-05-23（WebBrowser 预览），无对应 v0.1.5 版本提交。可能为回顾性整理版本号。

1. **HTML 预览** — WebBrowser 加载 .html/.htm 文件预览
2. **Markdown 预览** — Markdig 渲染 .md/.markdown 为带样式的 HTML
3. **文本预览字号** — AppSettings.TextPreviewFontSize + SettingsWindow 滑块 + 实时预览
4. **Shell 菜单重构** — 新增 --extract-here / --extract-to-name CLI；菜单项重命名排序
5. **Shell 安装移至设置** — 工具菜单移除安装/卸载，改为设置窗口三按钮（安装/卸载/应用）
6. **拖拽提取增强** — ProgressWindow 全程展示 + 子目录结构保留 + _isOwnDrag 防自投
7. **分卷压缩** — CompressSettingsWindow 分卷大小 ComboBox；ZIP 引擎 SplitOutputStream；7z 引擎传 -v{size}b

### v0.1.4 (2026-05-09) 拖拽增强

1. **拖出到 Explorer 拖拽提取** — 7-Zip 急切提取模型：提取后拖拽
2. **ProgressWindow 拖拽集成** — 提取时显示进度 → 拖拽时显示"正在拖拽"提示
3. **_isOwnDrag 防自投** — 拖回自己窗口时忽略，不弹添加到压缩包
4. **子目录结构保留** — 使用 FullPath 保留目录层次
5. **自定义 IDataObject 实验（废弃）** — 确认 WPF OLE bridge bug，不可修复
6. **VirtualFileDataObject 列入未来计划** — COM 原生 IDataObject，可实现延迟渲染不崩溃

### v0.1.3 (2026-05-09) 设置系统 + Shell 菜单

1. **修复 `_currentFormat` bug** — 非 ZIP 格式预览改用扩展名映射，不再误判为 SevenZip
2. **AppSettings 设置系统** — 用户偏好 JSON 持久化（压缩/解压/菜单/预览/高级），`AppSettings` 单例
3. **SettingsWindow 设置窗口** — 五标签页 UI（压缩/解压/上下文菜单/预览/高级），Shell 状态检测 + 即时应用
4. **ShellIntegration 右键菜单** — HKCU 无管理员注册，层叠子菜单/独立动词双模式，per-verb 开关，AppliesTo 过滤器，shell32.dll 图标
5. **SystemIconHelper 系统图标** — SHGetFileInfo 获取 16x16 文件类型图标，ConcurrentDictionary 缓存，支持虚拟文件
6. **ProgressWindow 双进度条** — 文件级进度（顶部）+ 总体进度（底部），`SetProgress(ArchiveProgress)` 重载
7. **ArchiveProgress.FilePercentComplete** — Core 层新增 per-file 粒度字段
8. **ZipEngine per-file 进度** — ExtractAsync/CompressAsync 逐文件汇报 0%→100%，100ms 节流
9. **CLI 入口点** — `--compress`（多实例 IPC 合并路径）、`--compress-quick`、`--extract`、`--open`、`--install-shell` / `--uninstall-shell`
10. **MainWindow 增强** — 预览设置感知（EnableImagePreview/EnableTextPreview/MaxTextPreviewBytes），异步图片解码 DecodePixelWidth=1920
11. **CLI 模式窗口关闭修复** — `ShutdownMode = ShutdownMode.OnExplicitShutdown`
12. **版本升级** — 0.1.3

### v0.1.2 (2026-05-08) 文件预览

1. **文件预览** — 选中文件后预览图片/文本内容
2. **预览信息面板** — 图片预览时右侧显示文件名、大小、压缩率等信息
3. **ArchiveEntryExtractor** — 单文件提取工具类 (Core)
4. **退出清理** — 程序退出时清理预览临时文件
5. **目录树绑定** — IsExpanded/IsSelected 改为 INotifyPropertyChanged 绑定
6. **智能目录树选择** — 双击进入子目录时不重建树，而是查找展开并选中已有节点
7. **多选支持** — 文件列表改为 Extended 选择模式，状态栏显示多选统计
8. **状态栏增强** — 添加目录统计、选中统计、压缩包概览
9. **预览行高 Star 支持** — 预览行高保存 GridLength 类型，支持 Star/Pixel 两种模式
10. **过滤保护** — 添加 _isProgrammaticFilter 防止编程切换目录触发预览

### v0.1.1 (2026-04-24) 7z 压缩 + 进度条

1. **7z 压缩** — 基于 7z.exe
2. **压缩进度条** — 每 100ms 时间间隔更新
3. **取消功能** — 压缩/解压过程中可取消
4. **拖拽 Explorer 卡死修复** — 改用 Show() 非阻塞
5. **隐藏设置窗口** — 压缩时隐藏，完成后恢复
6. **关于页面** — 添加 7-Zip LGPL 许可证声明

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
| 引擎统一 (SharpZipLib→SharpCompress + 7z.exe→SharpSevenZip) | [engine-unification-sharpcompress.md](.sisyphus/plans/engine-unification-sharpcompress.md) | v0.3.4 |
| 文件大小进度条 | [file-size-progress-bar.md](.sisyphus/plans/file-size-progress-bar.md) | v0.3.4 |
| PNG 透明通道控制 | [png-transparency-3way.md](.sisyphus/plans/png-transparency-3way.md) | v0.3.4+ |
| 批量进度文件列表 | [batch-progress-list.md](.sisyphus/plans/batch-progress-list.md) | v0.3.4 |
| 解压配置面板 (ExtractSettingsWindow) | [extract-settings-window.md](.sisyphus/plans/extract-settings-window.md) | v0.3.6 |
| COM 右键菜单 | [com-context-menu.md](.sisyphus/plans/com-context-menu.md) | v0.3.7 |
| COM 迁移映射表 | [com-migration-mapping.md](.sisyphus/plans/com-migration-mapping.md) | v0.3.7（辅助文档） |
| 压缩窗口密码 Tab 重设计 | [design-compress-password-tab.md](docs/design-compress-password-tab.md) | v0.3.7-refined-2 |
| 关于窗口重设计 | [about-window-redesign.md](.sisyphus/plans/about-window-redesign.md) | v0.3.7-refined-4 |
