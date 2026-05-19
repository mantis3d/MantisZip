# MantisZip 开发进度文档

## 项目概述
- **项目名称**: MantisZip
- **类型**: Windows 压缩/解压软件 (WPF)
- **目标**: 替代 Bandizip 的开源压缩软件
- **技术栈**: .NET 9 + WPF + SharpZipLib + SevenZipExtractor

## 版本
- **当前版本**: 0.2.9
- **发布日期**: 2026-05-19

## 版本历史（按日期排序）

### v0.1.0 (2026-04-22)
1. **ZIP 解压** - 基于 SharpZipLib，支持 GBK 编码
2. **ZIP 压缩** - 基于 SharpZipLib
3. **7z 解压** - 基于 SevenZipExtractor
4. **RAR 解压（只读）** - 基于 SevenZipExtractor
5. **TAR 压缩/解压** - 基于 SharpZipLib
6. **GZ 压缩/解压** - 基于 SharpZipLib
7. **TAR.GZ (.tgz) 压缩/解压** - 基于 SharpZipLib
8. **目录树导航** - 左侧面板显示压缩包内目录结构
9. **文件列表** - 右侧面板显示当前目录下的直接子项
10. **密码管理** - 支持 glob/regex 模式匹配的密码管理器
11. **密码输入对话框** - 下拉选择已保存的密码
12. **版本号显示** - 右下角状态栏显示
13. **拖拽解压** - 拖拽 ZIP 文件到窗口解压
14. **拖拽压缩** - 拖拽普通文件生成 ZIP

### v0.1.1 (2026-04-24)
1. **7z 压缩** - 基于 7z.exe
2. **压缩进度条** - 每 100ms 时间间隔更新
3. **取消功能** - 压缩/解压过程中可取消
4. **拖拽 Explorer 卡死修复** - 改用 Show() 非阻塞
5. **隐藏设置窗口** - 压缩时隐藏，完成后恢复
6. **关于页面** - 添加 7-Zip LGPL 许可证声明

### v0.1.2 (2026-05-08)
1. **文件预览** - 选中文件后预览图片/文本内容
2. **预览信息面板** - 图片预览时右侧显示文件名、大小、压缩率等信息
3. **ArchiveEntryExtractor** - 单文件提取工具类 (Core)
4. **退出清理** - 程序退出时清理预览临时文件
5. **目录树绑定** - IsExpanded/IsSelected 改为 INotifyPropertyChanged 绑定
6. **智能目录树选择** - 双击进入子目录时不重建树，而是查找展开并选中已有节点
7. **多选支持** - 文件列表改为 Extended 选择模式，状态栏显示多选统计
8. **状态栏增强** - 添加目录统计、选中统计、压缩包概览
9. **预览行高 Star 支持** - 预览行高保存 GridLength 类型，支持 Star/Pixel 两种模式
10. **过滤保护** - 添加 _isProgrammaticFilter 防止编程切换目录触发预览

### v0.1.3 (2026-05-09)
1. **修复 `_currentFormat` bug** - 非 ZIP 格式预览改用扩展名映射，不再误判为 SevenZip
2. **AppSettings 设置系统** - 用户偏好 JSON 持久化（压缩/解压/菜单/预览/高级），`AppSettings` 单例
3. **SettingsWindow 设置窗口** - 五标签页 UI（压缩/解压/上下文菜单/预览/高级），Shell 状态检测 + 即时应用
4. **ShellIntegration 右键菜单** - HKCU 无管理员注册，层叠子菜单/独立动词双模式，per-verb 开关，AppliesTo 过滤器，shell32.dll 图标
5. **SystemIconHelper 系统图标** - SHGetFileInfo 获取 16x16 文件类型图标，ConcurrentDictionary 缓存，支持虚拟文件
6. **ProgressWindow 双进度条** - 文件级进度（顶部）+ 总体进度（底部），`SetProgress(ArchiveProgress)` 重载
7. **ArchiveProgress.FilePercentComplete** - Core 层新增 per-file 粒度字段
8. **ZipEngine per-file 进度** - ExtractAsync/CompressAsync 逐文件汇报 0%→100%，100ms 节流
9. **CLI 入口点** - `--compress`（多实例 IPC 合并路径）、`--compress-quick`、`--extract`、`--open`、`--install-shell` / `--uninstall-shell`
10. **MainWindow 增强** - 预览设置感知（EnableImagePreview/EnableTextPreview/MaxTextPreviewBytes），异步图片解码 DecodePixelWidth=1920
11. **全局初始化 App.InitializeApp()** - 所有入口统一执行 GBK 编码注册
12. **ZipEngine 目录条目修复** - ExtractAsync 创建空目录条目而非跳过

### v0.1.4 (2026-05-11)
1. **拖出到 Explorer 拖拽提取** - 7-Zip 急切提取模型：提取后拖拽
2. **ProgressWindow 拖拽集成** - 提取时显示进度 → 拖拽时显示"正在拖拽"提示
3. **_isOwnDrag 防自投** - 拖回自己窗口时忽略，不弹添加到压缩包
4. **子目录结构保留** - 使用 FullPath 保留目录层次
5. **自定义 IDataObject 实验（废弃）** - 确认 WPF OLE bridge bug，不可修复
6. **VirtualFileDataObject 列入未来计划** - COM 原生 IDataObject，可实现延迟渲染不崩溃

### v0.1.5 (2026-05-11)
1. **HTML 预览** - WebBrowser 加载 .html/.htm 文件预览
2. **Markdown 预览** - Markdig 渲染 .md/.markdown 为带样式的 HTML
3. **文本预览字号** - AppSettings.TextPreviewFontSize + SettingsWindow 滑块 + 实时预览
4. **Shell 菜单重构** - 新增 --extract-here / --extract-to-name CLI；菜单项重命名排序
5. **Shell 安装移至设置** - 工具菜单移除安装/卸载，改为设置窗口三按钮（安装/卸载/应用）
6. **拖拽提取增强** - ProgressWindow 全程展示 + 子目录结构保留 + _isOwnDrag 防自投
7. **分卷压缩** - CompressSettingsWindow 分卷大小 ComboBox；ZIP 引擎 SplitOutputStream；7z 引擎传 -v{size}b

### v0.1.6 (2026-05-12)
1. **目录预览** - 选中文件夹时显示系统文件夹图标 + 目录信息
2. **工具栏预览开关** - ToggleButton 控制预览面板显隐，状态持久化 ShowPreviewPanel
3. **图片解码降采样优化** - 仅对宽度 >1920px 的图片设 DecodePixelWidth=1920，小图保持原生清晰度
4. **MaxWidth/MaxHeight 约束** - 设 PreviewImage MaxWidth/MaxHeight 为实际像素，防止 Stretch="Uniform" 拉伸小图
5. **预览开关收起残留空白修复** - 收起时 HidePreview() 复位 Grid 行/列尺寸
6. **预览开关重显修复** - 打开时 ShowPreviewPanel() 恢复布局 + 重显选中项预览

### v0.2.0 (2026-05-12)
1. **MIT 许可证 + LICENSE 文件** - 项目切换为 MIT 开源
2. **OpenCode 声明 + 捐赠链接** - README 添加 Sisyphus Agent 致谢和捐赠按钮
3. **512x512 应用图标** - App.ico 嵌入 EXE，标题栏 + 任务栏 + 右键菜单全部使用自定义图标
4. **默认布局优化** - 预览面板默认右侧、信息面板默认纵向、窗口 1200×800、目录树默认 396px
5. **滚动条拖拽冲突修复** - FileListGrid 滚动条点击忽略拖拽检测
6. **压缩扫描进度** - ZipEngine/TarGzEngine EnumerateFiles + 100ms 进度报告，不再卡"正在准备..."
7. **Inno Setup 安装包** - 自动生成 MantisZip-0.2.0-Setup.exe 安装程序

### v0.2.1 (2026-05-12)
1. **加密 ZIP 解压密码提示修复** - ZipEngine `ListEntriesAsync` 设置 `IsEncrypted`；`ExtractAsync` 预检 `IsCrypted` 抛出中文异常
2. **密码管理器帮助窗口** - PasswordHelpDialog 讲解匹配规则 + 范例

### v0.2.2 (2026-05-13)
1. **SevenZipEngine 路径可配置** - 从 `private const` 改为 `static` 属性，启动时从 AppSettings 加载
2. **ZIP AES-256 加密检测** - `IsCrypted || AESKeySize > 0` 覆盖传统加密和 AES 加密
3. **7z 加密检测** - `entry.IsEncrypted` 替代硬编码 `false`
4. **QuickVerifyPassword** - 读 1 字节快速验证密码，不等完整解压
5. **密码区集成到进度条窗口** - 显示尝试/匹配状态、显示/隐藏密码、复制按钮，取代旧独立密码对话框
6. **自动匹配密码** - 打开压缩包时自动遍历已保存密码规则，匹配后预览/拖拽直接使用
7. **工具栏密码按钮** - 加密压缩包未匹配密码时可点击输入
8. **状态栏密码指示** - 显示 🔑 已匹配密码 / 🔒 需要密码
9. **密码对话框自动弹出** - 打开加密压缩包且无匹配密码时弹窗输入
10. **预览加载进度** - 预览窗格显示不确定进度条
11. **预览文件大小上限** - 设置中可配置，超过上限不预览
12. **测试加密压缩包** - TestArchiveAsync 传递 `_currentPassword`，加密时先弹密码框
13. **隐式目录推导** - BuildFolderTree + FilterFiles 从文件路径推导目录节点，解决无显式目录条目的 ZIP 不显示子目录
14. **预览竞态修复** - 添加 `_previewCts` 取消令牌，切换文件时取消旧预览
15. **代码去重** - FormatSize、ResolveExtractDestination、OpenInExplorer 统一入口
16. **ShutdownMode 修复** - CLI 模式关进度条不自动退出，密码对话框能正常显示
17. **版本升级** - 0.2.2

### v0.2.3 (2026-05-13)
1. **ISO 格式支持** - SevenZipEngine 扩展处理 `.iso` 镜像文件
2. **文件冲突处理系统** - `FileConflictAction` 枚举 (Overwrite/Rename/Skip/Ask) + `FileConflictHelper.ResolvePath` + `ConflictDialog` 弹窗，全引擎集成
3. **暂停/继续功能** - ProgressWindow `ManualResetEventSlim` + `PauseAwareProgress` 包装器，支持解压中暂停/继续
4. **文件关联** - 注册 `.zip/.7z/.rar` 等格式的 ProgId + OpenWithProgids，设置页管理
5. **CLI 文件关联参数** - `--install-assoc` / `--uninstall-assoc` 命令行安装/卸载文件关联
6. **共享解压选项萃取** - `CreateExtractOptions()` + `ConflictResolver` 回调，三方解压入口统一
7. **版本升级** - 0.2.3

### v0.2.4 (2026-05-13)
1. **预览信息面板重构** - 三列布局（原始大小|压缩后|压缩率），格式信息在上/通用信息在下，文件名跨三列
2. **共享解压逻辑抽取** - `TryMatchPassword` / `PromptForPassword` / `ExtractWithPasswordAsync`，三处入口统一
3. **压缩目标文件冲突** - CompressConflictDialog，支持覆盖/添加到压缩包/自动重命名/取消
4. **压缩文件读取错误处理** - ErrorDialog，重试/跳过/中止 + 应用到全部
5. **调试日志开关** - 设置→高级，默认关闭，开启后记录详细日志，含打开日志文件按钮
6. **还原文件修改时间** - 解压后用 `File.SetLastWriteTime` 恢复条目原始时间
7. **拖拽解压开关** - 设置→解压，可禁用文件列表拖拽提取
8. **OverwriteIfSmaller** - 新增冲突策略，仅当压缩包内文件更大时覆盖
9. **目录日期修复** - `DateTime.MinValue` 显示 `---` 而非 `0001-01-01`
10. **无密码压缩包密码区修复** - `HasEncryptedEntries` 预检，无加密不显示
11. **版本升级** - 0.2.4


### v0.2.5 (2026-05-13)
1. **MainWindow 文件拆分** - 92 KB / 2400 行拆为 5 个文件，编译零警告零错误
2. **RAR 路径分隔符修复** - SevenZipEngine 将 `\` 转为 `/`，解决 RAR 文件目录不显示的问题
3. **目录重复显示修复** - FilterFiles 通用去重 + Houdini ZIP 的重复目录问题
4. **密码对话框增强** - 新增"保存到密码库"区域，支持描述和匹配规则输入
5. **文件列表右键菜单** - 解压到…/解压到所在文件夹/复制文件名/复制完整路径
6. **预览大小滑块改进** - 范围 1-100 MB、默认 15 MB、可输入精确数值
7. **版本号简化** - VersionDisplay改为属性 `=> "v" + Version`，只维护一处
8. **版本升级** - 0.2.5



### v0.2.6 (2026-05-17)
1. **README 重写** - 「未来计划」升级为「开发计划」全景路线图，按功能域分 6 组，已完成/规划中状态标记，链接指向详细设计文档
2. **文档冗余清理** - PROGRESS.md 移除待实现功能/技术架构/重复开发日志/已知问题章节；PLAN.md 变更日志缩短为引用 PROGRESS.md
3. **恢复 v0.2.3 版本历史** - 填补被 v0.2.4 覆盖时误删的版本条目，修正 v0.2.2 条目归属
4. **ISO 格式正式记录** - PLAN.md 格式支持表 + README 核心引擎表新增 ISO 解压
5. **新增计划文档** - engine-unification-sharpcompress.md、preview-format-detection.md、file-size-progress-bar.md
6. **文件大小进度条方案** - 纳入开发计划，纯 UI 改动
7. **RAR 预览修复** - ArchiveEntryExtractor 重复标签 + 路径分隔符不匹配导致"未找到条目"
8. **GIF 动画预览** - 引入 WpfAnimatedGif 包，gif 文件用 `ImageBehavior.SetAnimatedSource` 播放
9. **Markdown 增强** - Markdig 管道添加 PipeTables、EmphasisExtras(Strikethrough/Subscript/Superscript)、TaskLists、AutoIdentifiers、EmojiAndSmiley 扩展
10. **统一消息弹窗 AppMessageBox** - 创建 `AppMessageBox.xaml/cs` 替换所有 `MessageBox.Show()`（37+ 调用点，9 个文件）
11. **Zip64 修复** - SharpZipLib 1.4.0 → 1.4.2（修复 Zip64 扩展信息解析错误）
12. **SharpCompress 迁移计划** - 新增 Phase 4（7z.exe → SevenZipSharp），4 阶段全量计划
13. **文件类型图标** - `ShellIntegration.GetIconPath()` + 逐扩展名 `DefaultIcon`，图标文件放 `Resources\Icons\`，编译时自动复制
14. **右键菜单层叠模式默认启用** - `EnableCascadingMenu` 默认值 `false→true`，避免 Windows 15 动词上限导致多文件右键菜单消失
15. **预览提前显示基础信息** - `ShowPreviewAsync` 优先调用 `SetPreviewInfo(item)` + `ShowPreviewPanel()`，加载前先展示文件名/大小/压缩率
16. **ZIP 注释预览** - 读取 `ZipFile.ZipFileComment` 并像文本文件一样显示在预览内容区，字体大小跟随 `TextPreviewFontSize` 设置
17. **空选择显示压缩包总览** - `FileListGrid_SelectionChanged` + `FilterFiles` 末尾检测 `SelectedItems.Count == 0` 时调用 `ShowArchiveInfo()`
18. **目录统计** - `_dirStats` 预计算缓存，文件列表大小列显示目录下所有文件之和，目录预览显示文件数/原始大小/压缩后/压缩率
19. **版本升级** - 0.2.6

### v0.2.7 (2026-05-18)
1. **设置窗口改用竖向 Tab** - `TabStripPlacement="Left"`，左侧竖排标签页
2. **Tab 图标** - 每个标签页加 Emoji 图标，自定义 `TabItem` ControlTemplate（38px 最小高度、选中态底色 + 3px 蓝色左边框 + 加粗指示）
3. **Tab 布局** - 图标左对齐 + 文字居中，`MinWidth="150"` 更宽松
4. **彩色 Emoji 渲染开关** - 添加 `AppSettings.UseColorEmoji` 和 `App.ApplyTextRenderingMode()`（WPF 原生不支持彩色 Emoji，功能待 Emoji.Wpf 库实现）
5. **本地化补全** - CompressSettingsWindow/SettingsWindow/ConflictDialog/ErrorDialog/CompressConflictDialog/PasswordManagerWindow 等 8 个文件的硬编码中文改用 `l:L` 绑定；需代码填充的 dialog header 改用 `L.T()`/`L.TF()` 方法
6. **L.cs 重构** - 重新生成（423 键），追加 `T()`/`TF()` 静态方法
7. **文档记录** - `docs/PLAN.md` 记录 Emoji.Wpf 方案为 P2 待实现任务
8. **版本升级** - 0.2.7

### v0.2.8 (2026-05-19)
1. **IPC 互斥体/管道名修正** - `App.xaml.cs` 中 `L.T()` 写在字符串字面量内导致 `--compress` 多实例 IPC 完全失效，改为固定英文标识符
2. **L.T() 字符串嵌入修复** - `MainWindow.xaml.cs` 两处 `SetStatus` + `App.xaml.cs` 两处消息框的 `L.T()` 调用写在字符串内部的 bug
3. **SevenZipEngine 7z.exe 自动探测** - 新增 `ResolveSevenZipPath()` 自动搜索 Program Files / Program Files (x86) / PATH，找不到才抛异常
4. **SevenZipEngine 双重枚举消除** - `ExtractAsync` 单遍收集条目到列表，避免两遍解码 7z 头部
5. **Window_Drop fire-and-forget 修复** - `_ = LoadArchiveAsync(...)` → `await`，异常不再被吞噬
6. **ProgressWindow 调度一致化** - `SetComplete` 改用 `BeginInvoke` 非阻塞调度，与 `SetProgress` 保持一致
7. **日志隐私脱敏系统** - 新增 `LogRedactor`（Core/Utils），集中式正则脱敏支持驱动器路径+UNC 路径，脱敏委托注入 `CoreLog.RedactOverride`；`App.Log/LogDebug/LogStartup` 写入前调用脱敏；默认完全脱敏模式；帮助说明窗口
8. **版本升级** - 0.2.8

### v0.2.9 (2026-05-19)
1. **进度条渲染修复** - 创建 `BackgroundDispatcherProgress`（自定义 `IProgress<ArchiveProgress>`），以 `Background(3)` 优先级替代 `Progress<T>` 的 `Normal(8)`，使 WPF 渲染在进度更新之间发生，进度条不再卡死
2. **加权进度（添加到压缩包 + 删除）** - `ZipEngine.AddToArchiveAsync`/`DeleteEntriesAsync` 进度按 `(i+1)/(新文件数+旧条目数)*100` 加权，反映全量工作进度
3. **`Progress<T>` 全部替换** - UI 层 7 处 `new Progress<ArchiveProgress>(...)` 改为 `ProgressWindow.CreateBackgroundProgress()`，统一 Background 优先级调度
4. **`SetProgress`/`SetComplete` 简化** - 移除冗余的 `Dispatcher.BeginInvoke`（自定义 IProgress 已处理调度）
5. **`App.TraceLog` 清理** - 所有 `App.TraceLog` 改为 `App.LogDebug`，统一调试日志输出
6. **工具栏图标/文字放大** - 图标 `18→22`，标签 `10→12`，内边距 `(6,4)→(8,6)`
7. **版本升级** - 0.2.9

