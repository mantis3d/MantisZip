# Avalonia 移植 Phase 3：压缩/解压工作流 + 进度/工具栏/筛选

> **分支**: `avalonia-port`（从 Phase 2 继续）  
> **目标**: 在 Phase 0–2 基础上，补齐压缩/解压核心工作流，包括压缩对话框、解压对话框、进度窗口；同时添加工具栏、状态栏、文件筛选排序等 UI 组件，使 Avalonia 版具备完整的文件管理能力  
> **设计决策**:
>   - **跨平台优先**：所有代码不得依赖 Windows-only API（Win32 P/Invoke、注册表、COM、WebView2 等），确保在 Linux/macOS 下可编译运行
>   - 压缩对话框复用 Core 的 `ArchiveOptions` + `IArchiveEngine.CompressAsync`，UI 用 TabControl（与 WPF 一致）
>   - 解压对话框复用 Core 的 `IArchiveEngine.ExtractAsync`，参数从 UI 收集
>   - 进度窗口用 `IProgress<ArchiveProgress>` 接口 + 非阻塞异步进度条
>   - 密码管理器与设置窗口共享 JSON 持久化路径
>   - 文件筛选在 ViewModel 层做 LINQ 过滤，不涉及引擎改动
>   - 表格排序用 `DataGrid` 内置的 `SortMemberPath`（Avalonia DataGrid 支持）
> **不做的**: 7z 压缩（SharpSevenZip 依赖 Windows 7z.dll）、PDF 预览、COM 扩展、Shell 集成、IPC 多实例、加密文件名 7z 的密码提前输入  
> **创建日期**: 2026-06-14  
> **更新日期**: 2026-06-14  
> **状态**: 📋 计划中

---

## 文件结构（新增/修改）

```
src/MantisZip.UI.Avalonia/
├── Dialogs/                                ← NEW: 对话框目录
│   ├── CompressSettingsWindow.axaml        │
│   ├── CompressSettingsWindow.axaml.cs     │
│   ├── ExtractSettingsWindow.axaml         │
│   ├── ExtractSettingsWindow.axaml.cs      │
│   ├── ProgressWindow.axaml                │
│   ├── ProgressWindow.axaml.cs             │
│   ├── AboutWindow.axaml                   │
│   ├── AboutWindow.axaml.cs                │
│   ├── PasswordManagerWindow.axaml         │
│   └── PasswordManagerWindow.axaml.cs      │
│
├── ViewModels/
│   ├── MainWindowViewModel.cs              ← MODIFY: 添加工具栏/状态栏/筛选/排序
│   ├── CompressSettingsViewModel.cs        ← NEW
│   ├── ExtractSettingsViewModel.cs         ← NEW
│   └── ProgressViewModel.cs                ← NEW
│
├── Views/
│   ├── MainWindow.axaml                    ← MODIFY: 工具栏 + 筛选栏 + 状态栏
│   └── MainWindow.axaml.cs                 ← MODIFY: 拖拽已有
│
├── Services/
│   ├── CompressService.cs                  ← NEW: 封装压缩逻辑 + IPC 多实例
│   └── ExtractService.cs                   ← NEW: 封装解压逻辑
│
└── Models/
    └── AppSettings.cs                      ← MODIFY: 添加解压相关设置字段（ExtractDestination 等）
```

---

## 实现步骤

### Task 0：基础压缩包管理（Close/Refresh）

> 关闭当前压缩包、刷新重新加载。

- [ ] **0.1** `MainWindowViewModel` 添加：
  - `CloseArchiveCommand`：清理所有条目、重置状态、设置标题为"MantisZip"
  - `RefreshCommand`：记录当前路径，调用 `LoadArchiveAsync(CurrentArchivePath)`
- [ ] **0.2** `MainWindow.axaml` 菜单添加：
  - 文件 → 关闭压缩包（Ctrl+W，仅压缩包已加载时启用）
  - 视图 → 刷新（F5，仅压缩包已加载时启用）
- [ ] **0.3** 验证：打开压缩包后按 F5 刷新，关闭后界面重置
- [ ] **0.4** **Commit**: `feat(avalonia): add close/refresh archive commands`

---

### Task 1：解压对话框 + 进度窗口

> 从压缩包解压文件到指定目录，显示进度。

- [ ] **1.1** 创建 `ViewModels/ExtractSettingsViewModel.cs`：
  - 属性：`DestinationPath`（目标目录）、`ConflictAction`（覆盖/重命名/跳过/询问）、`OpenFolderAfterExtract`
  - `BrowseDestinationCommand`：打开文件夹选择器（回调由 View 设置）
  - `ExtractCommand`：收集参数，启动解压
- [ ] **1.2** 创建 `Dialogs/ExtractSettingsWindow.axaml` + `.cs`：
  - 标题"解压设置"
  - 目标路径：TextBox + 浏览按钮（FolderBrowserDialog）
  - 冲突处理：ComboBox（覆盖/自动重命名/跳过/每次询问）
  - 复选框"解压后打开文件夹"
  - "解压" / "取消" 按钮
  - 所有控件 `{DynamicResource Theme*}` 绑定
- [ ] **1.3** 创建 `ViewModels/ProgressViewModel.cs`：
  - `PercentComplete`（int 0–100）、`FileName`（当前文件）、`StatusMessage`（状态文字）
  - `IsIndeterminate`（不确定进度模式）
  - `CancelCommand` + `CancellationTokenSource`
- [ ] **1.4** 创建 `Dialogs/ProgressWindow.axaml` + `.cs`：
  - 进度条（ProgressBar）
  - 文件名标签
  - 状态文字
  - "取消" 按钮
  - 标题显示当前操作（"正在解压..." / "正在压缩..."）
- [ ] **1.5** `MainWindowViewModel` 添加：
  - `ExtractCommand`：打开 `ExtractSettingsWindow`
  - `ExtractToHereCommand`：直接解压到压缩包所在目录（CLI `--extract-here` 的 GUI 版）
  - `ExtractToNameCommand`：解压到压缩包名子目录
- [ ] **1.6** 右键菜单 / 菜单项：
  - 选中条目 → 解压选定文件到…；不选 → 解压全部
- [ ] **1.7** 验证：解压 ZIP/7z/tar.gz，进度条正确，取消正常
- [ ] **1.8** **Commit**: `feat(avalonia): add extract dialog with progress window`

---

### Task 2：压缩对话框 + 进度窗口

> 选择文件/文件夹压缩为 ZIP/7z/tar.gz，显示进度。

- [ ] **2.1** 创建 `ViewModels/CompressSettingsViewModel.cs`：
  - 属性：`DefaultFormat`（zip/7z/tar.gz）、`CompressionLevel`（1–9）、`OutputPath`、`Password`、`Comment`、`CommentDistribution`
  - `BrowseOutputCommand`：保存文件选择器
  - `StartCompressCommand`：启动压缩
  - 密码 Tab 内容：PasswordBox + 加密方式
  - 注释 Tab 内容：TextBox + 分布策略
- [ ] **2.2** 创建 `Dialogs/CompressSettingsWindow.axaml` + `.cs`：
  - TabControl 三页：通用（格式/级别/输出路径）、加密（密码/确认/强度指示）、注释
  - 与 WPF `CompressSettingsWindow` 布局对齐
- [ ] **2.3** `MainWindowViewModel` 添加：
  - `NewArchiveCommand`：打开 CompressSettingsWindow（文件列表为空）
  - `CompressCommand`：打开 CompressSettingsWindow（选中文件预填入）
  - 对应菜单项和工具栏按钮
- [ ] **2.4** 验证：压缩 ZIP + 7z + tar.gz，带密码/注释，进度窗口正确
- [ ] **2.5** **Commit**: `feat(avalonia): add compress dialog with progress window`

---

### Task 3：工具栏 + 状态栏

> 快速操作按钮和底部信息栏。

- [ ] **3.1** `MainWindow.axaml` 添加工具栏（Menu 下方）：
  - 新建压缩包（📦）、打开（📂）、解压（📤）、压缩（📥）
  - 筛选栏切换按钮（🔍）、预览面板切换按钮
  - 工具栏背景 `ThemeHeaderBgBrush`
- [ ] **3.2** `MainWindow.axaml` 添加状态栏（底部）：
  - `{Binding StatusMessage}` 显示当前状态
  - 条目计数："已加载 N 个条目" / "选中 M 个文件"
  - 选中文件总大小
  - 压缩包格式/大小信息
- [ ] **3.3** `MainWindowViewModel` 添加：
  - `SelectionStats` 计算属性：选中条目数 + 总大小
  - `ArchiveStats` 计算属性：格式 + 原始大小 + 压缩后大小 + 压缩率
- [ ] **3.4** 验证：工具栏按钮点击有效，状态栏随操作更新
- [ ] **3.5** **Commit**: `feat(avalonia): add toolbar and status bar`

---

### Task 4：文件筛选 + 排序

> 按名称、日期、大小搜索和筛选文件列表。

- [ ] **4.1** `MainWindow.axaml` 添加筛选栏（工具栏下方，可折叠）：
  - 文件名搜索 TextBox（实时过滤）
  - 日期范围：起始日期选择器 + 结束日期选择器
  - 大小范围：最小值 TextBox + 单位 ComboBox + 最大值 TextBox + 单位 ComboBox
  - 子文件夹切换 CheckBox "显示子文件夹内容"
  - 折叠/展开按钮（与工具栏筛选按钮联动）
- [ ] **4.2** `MainWindowViewModel` 添加：
  - `FilterFiles()` 方法：对 `_allItems` 做 LINQ 过滤（名称包含/日期范围/大小范围）
  - `_isProgrammaticFilter` 标志：防止 FilterFiles 触发 SelectionChanged 预览
  - `ToggleFilterBarCommand`
  - `ShowSubfolders` 属性
- [ ] **4.3** DataGrid 列头排序：
  - 每列设置 `SortMemberPath="属性名"`
  - 点击列头自动排序（Avalonia DataGrid 内置支持）
- [ ] **4.4** 验证：输入文件名实时过滤，日期/大小筛选正确，排序正常
- [ ] **4.5** **Commit**: `feat(avalonia): add file filter bar and column sorting`

---

### Task 5：密码管理器

> 管理已保存的密码库。

- [ ] **5.1** 创建 `Dialogs/PasswordManagerWindow.axaml` + `.cs`：
  - 已保存密码列表（DataGrid：描述/匹配规则/密码掩码）
  - 添加/编辑/删除按钮
  - 搜索/过滤已保存密码
  - 主题色绑定
- [ ] **5.2** `MainWindowViewModel` 添加：
  - `OpenPasswordManagerCommand`
  - 菜单项 "编辑 → 密码管理器"
- [ ] **5.3** 数据来源：复用 `PasswordManager`（Core 已有的静态类）
- [ ] **5.4** 验证：添加密码，重新打开加密压缩包自动匹配
- [ ] **5.5** **Commit**: `feat(avalonia): add password manager window`

---

### Task 6：关于窗口 + 收尾

> 应用信息、版本号、许可证。

- [ ] **6.1** 创建 `Dialogs/AboutWindow.axaml` + `.cs`：
  - 应用图标 + 名称 "MantisZip"
  - 版本号（从 `AppConstants.Version` 读取）
  - 技术栈信息（.NET 9 + Avalonia + SharpCompress）
  - 许可证链接（GitHub）
  - "关闭" 按钮
- [ ] **6.2** `MainWindowViewModel` 添加：
  - `OpenAboutCommand`
  - 菜单项 "帮助 → 关于"
- [ ] **6.3** 确认无 WPF 文件被修改
- [ ] **6.4** `git merge main` 同步
- [ ] **6.5** 验证：`dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [ ] **6.6** **Commit**: `feat(avalonia): add about dialog and finalize Phase 3`

---

## 验证清单

- [ ] 关闭压缩包后界面重置
- [ ] F5 刷新重新加载
- [ ] 解压对话框设置目标路径，解压成功
- [ ] 解压进度窗口实时更新，可取消
- [ ] 压缩对话框设置格式/级别/密码，压缩成功
- [ ] 压缩进度窗口正常
- [ ] 工具栏按钮均可用
- [ ] 状态栏显示条目/统计信息
- [ ] 文件名搜索实时过滤
- [ ] 日期/大小筛选正确
- [ ] 列头点击可排序
- [ ] 密码管理器添加/编辑/删除正常
- [ ] 关于窗口显示版本号
- [ ] WPF 项目未修改
- [ ] `dotnet build` 0 errors, 0 warnings

---

## 边界情况与注意事项

1. **跨平台架构要求**：`NativeWebView` 已跨平台（Win WebView2/macOS WebKit/Linux WebKitGTK）。但现有部分代码仍标记 `[SupportedOSPlatform("windows")]`（如 `ArchiveService`、`IconService`），后续需逐步剥离 Win32 依赖或提供跨平台回退
2. **进度窗口的非阻塞性**：`ProgressWindow` 调用 `ShowDialog` 会阻塞，需要改为 `Show` + 异步更新模式，或在后台线程执行操作
3. **压缩/解压的取消**：通过 `CancellationToken` 传递，引擎层需要定期检查取消标志
4. **7z 压缩不做**：SharpSevenZip 依赖 Windows 7z.dll COM 接口，不跨平台。Phase 3 只做 ZIP 和 tar.gz 压缩，7z 压缩留待后续解决（Linux 可用 p7zip 进程调用）
5. **注释仅 ZIP**：注释 Tab 显示"仅 ZIP 格式支持注释"提示
6. **筛选性能**：大压缩包（10000+ 文件）的 LINQ 过滤应在后台线程执行，避免 UI 卡顿
7. **密码管理器**：`PasswordManager.MaxEntries = 1000`，添加时需检查是否已满
8. **文件选择器**：压缩对话框的"选择输出路径"用 `SaveFilePicker`，解压对话框用 `FolderPicker`（均为跨平台 API）
