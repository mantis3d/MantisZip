# Avalonia 移植 Phase 2：设置/密码/HTML预览/多语言 + 代码清理

> **分支**: `avalonia-port`（从 Phase 1 继续）  
> **目标**: 在 Phase 0+1 基础上，补齐设置窗口、密码对话框、HTML/Markdown 预览、多语言、CLI 参数处理、拖拽导出；同时清理 Avalonia 与 Core 之间的重复代码  
> **设计决策**:
>   - 设置窗口复用 WPF 的 AppSettings 模型（Core 已有），UI 用 Avalonia 原生控件
>   - HTML 预览：先用 `Avalonia.WebView`（跨平台 Chromium 嵌入），如不可用则回退显示源码
>   - Markdown 预览：Markdig → HTML → WebView 渲染（与 WPF 一致）
>   - 密码对话框：统一 `password` 参数传递，不存储到 PasswordManager
>   - i18n：`.resx` 或 JSON 资源文件 + 运行时切换，与 WPF `L.T()` 模式平行
>   - CLI 参数：在 `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 中解析
>   - 拖拽导出：用 Avalonia 的 `DragDrop` 事件 + 临时文件提取
> **不做的**: Shell 扩展（COM）、PDF 预览、IPC 多实例、进度窗口（后续 Phase）  
> **创建日期**: 2026-06-11  
> **更新日期**: 2026-06-11  
> **状态**: 📋 计划中 | **进度**: [                    ] (0/—)

---

## 文件结构（新增/修改）

```
src/MantisZip.UI.Avalonia/
├── Controls/                              ← 已有
├── Converters/                            ← 已有
├── Localization/                          ← NEW: i18n 资源目录
│   ├── strings.zh-CN.json                 ← 中文资源
│   └── strings.en.json                    ← 英文资源
│
├── Models/
│   ├── ArchiveItemModel.cs                ← 已有
│   └── FormatMetadataItem.cs              ← 已有
│
├── Services/
│   ├── ArchiveService.cs                  ← 修改: 支持 password 参数
│   ├── IconService.cs                     ← 已有
│   └── PreviewService.cs                  ← 修改: 添加 HTML/Markdown 分类
│
├── ViewModels/
│   ├── MainWindowViewModel.cs             ← 修改: 添加 CLI/拖拽/设置/密码命令
│   └── PreviewViewModel.cs                ← 修改: 编码检测/CSV解析改为使用 Core
│   └── SettingsWindowViewModel.cs          ← NEW: 设置窗口 VM
│
├── Views/
│   ├── MainWindow.axaml                   ← 修改: 添加设置/密码菜单项
│   ├── MainWindow.axaml.cs                ← 修改: 拖拽事件
│   ├── PreviewPanel.axaml                 ← 修改: 添加 HTML/Markdown DataTemplate
│   ├── PreviewPanel.axaml.cs              ← 已有
│   ├── SettingsWindow.axaml               ← NEW: 设置窗口
│   ├── SettingsWindow.axaml.cs            ← NEW: 设置窗口代码后置
│   └── PasswordDialog.axaml               ← NEW: 密码输入弹窗
│   └── PasswordDialog.axaml.cs            ← NEW: 密码弹窗代码后置
│
├── Themes/
│   ├── ThemeLight.axaml                   ← 修改: 补充设置窗口控件样式
│   └── ThemeDark.axaml                    ← 修改: 同上
│
├── App.axaml                              ← 修改: 引入 i18n 资源
├── App.axaml.cs                           ← 修改: CLI 参数解析
└── Program.cs                             ← 可能小修改
```

## 任务分解

---

### Task 0：Core 重复清理

> 删除 Avalonia 中与 Core 重复的逻辑，改为调用 Core 方法。

- [ ] **0.1** `PreviewViewModel.DetectAndReadText()` → 改用 `TextEncodingDetector.DetectAndReadText(filePath)`
  - 删除 Avalonia 版私有方法（~42 行）
  - Core 版回退用 ANSICodePage（比硬编码 GBK 更通用）
  - 注意捕获异常 + 兼容现有调用签名
- [ ] **0.2** `PreviewViewModel.ParseCsv()` + `SplitCsvLine()` → 改用 `CsvParser`
  - 删除两个私有方法（~55 行）
  - 用 `CsvParser.ParseCsvLine()` 代替 `SplitCsvLine()`
  - 用 `CsvParser.MakeUniqueColumnNames()` 处理列名冲突
  - `DataTable` 构建逻辑保留在 VM 层（UI 绑定需要）
- [ ] **0.3** `ArchiveItemModel.FormatSize()` + `TorrentFileItem.FormatSize()` → 改用 `FormatUtil.FormatSize()`
  - 删除两个私有方法（~24 行）
  - `ArchiveItemModel.FromCore()` 中调用 `FormatUtil.FormatSize(item.Size)`
  - `TorrentFileItem.SizeDisplay` 调用 `FormatUtil.FormatSize(Size)`
  - 如有格式差异（`F2` vs `0.##`），统一 Core 版行为
- [ ] **0.4** `FileSizeConverter` 内部改为委托 `FormatUtil.FormatSize`
  - IValueConverter 保留（UI 绑定需要），内部逻辑一行委托
- [ ] **0.5** `ArchiveFormatHelper.IsArchiveFile()` → 改为委托 `ArchiveEngineFactory`
  ```csharp
  public static bool IsArchiveFile(string path) =>
      ArchiveEngineFactory.GetEngineByExtension(path) != null;
  ```
- [ ] **0.6** 验证：`dotnet build` 编译通过，所有预览功能正常
- [ ] **0.7** **Commit**: `refactor(avalonia): remove duplicate code from Core, use Core services directly`

---

### Task 1：设置窗口

> 基于 Core 的 `AppSettings` 模型，创建设置窗口 UI。

- [ ] **1.1** 创建 `ViewModels/SettingsWindowViewModel.cs`：
  - 属性映射到 `AppSettings` 的各个节：
    - 压缩：`DefaultFormat`、`DefaultLevel`
    - 预览：`EnableImagePreview`、`EnableTextPreview`、`MaxTextPreviewBytes`、`TextPreviewFontSize`
    - 调试：`EnableDebugLogging`
  - `LoadCommand` / `SaveCommand`
  - `SettingsWindowViewModel` 继承 `ObservableObject`
- [ ] **1.2** 创建 `Views/SettingsWindow.axaml` + `.cs`：
  - TabControl 布局（与 WPF 版对应）：
    - Tab1 "预览"：预览启用开关、文本预览字号、最大预览字节
    - Tab2 "压缩"：默认格式（下拉框）、压缩级别（滑块 1-9）
    - Tab3 "调试"：调试日志开关
  - 所有控件绑定主题色（`DynamicResource`）
- [ ] **1.3** `MainWindowViewModel` 添加：
  - `OpenSettingsCommand`：打开设置窗口（模态对话框）
  - 菜单项 "设置" → `OpenSettingsCommand`
- [ ] **1.4** 设置持久化：
  - 保存到 `%LOCALAPPDATA%\MantisZip\settings.json`（与 WPF 共享路径）
  - 读取现有 `AppSettings` 实例
- [ ] **1.5** 验证：打开设置窗口，修改设置，重启后生效
- [ ] **1.6** **Commit**: `feat(avalonia): add settings window`

---

### Task 2：密码对话框

> 打开加密压缩包时弹出密码输入窗口。

- [ ] **2.1** `ArchiveService.LoadArchiveAsync` 修改：
  - 添加 `password` 参数传递
  - 首次尝试 `password: null`，如返回 `PasswordRequired` 则触发密码弹窗
- [ ] **2.2** 创建 `Views/PasswordDialog.axaml` + `.cs`：
  - `PasswordBox` 输入密码
  - `CheckBox` "记住会话中"（不保存到 PasswordManager，仅内存缓存）
  - "确定" / "取消" 按钮
  - 主题色绑定
- [ ] **2.3** `MainWindowViewModel` 添加：
  - `ShowPasswordDialog()` 方法
  - 缓存密码字典 `Dictionary<string, string> _sessionPasswords`（路径→密码）
- [ ] **2.4** `LoadArchiveAsync` 流程：
  1. 检查 `_sessionPasswords` 是否有缓存密码
  2. 尝试解密
  3. 如果 `PasswordRequired` → 弹密码窗
  4. 用户输入密码后重试
  5. 如果取消 → 显示"需要密码"消息
- [ ] **2.5** **Commit**: `feat(avalonia): add password dialog for encrypted archives`

---

### Task 3：HTML / Markdown 预览

> HTML 和 Markdown 渲染预览。

- [ ] **3.1** 调研 Avalonia WebView 方案：
  - `Avalonia.WebView`（跨平台 Chromium）
  - 或 `Avalonia.WebView2`（Windows-only，与 WPF 一致）
  - 选择跨平台方案
- [ ] **3.2** `PreviewService.ClassifyPreview` 添加：
  - `HtmlExtensions`：`.html`, `.htm`
  - `MarkdownExtensions`：`.md`, `.markdown`
- [ ] **3.3** HTML 预览：
  - `PreviewViewModel.ShowHtmlPreview(string filePath)`：
    - 读取 HTML 文件内容
    - 设置 `TextContent`（源码）
    - 同时加载到 WebView 渲染
  - `PreviewPanel.axaml` 添加 HTML 的 DataTemplate：
    - WebView 控件渲染内容
    - 可选：`ToggleSourceCommand` 切换源码/渲染视图
- [ ] **3.4** Markdown 预览：
  - `PreviewViewModel.ShowMarkdownPreview(string filePath)`：
    - 用 `Markdig` 将 Markdown 转为 HTML
    - 注入暗色主题 CSS（根据当前主题）
    - 将 HTML 传给 WebView 渲染
  - 与 HTML 预览共享 WebView DataTemplate
- [ ] **3.5** 验证：打开含 .html/.htm/.md 的压缩包，渲染正常
- [ ] **3.6** **Commit**: `feat(avalonia): add HTML and Markdown preview with WebView`

---

### Task 4：i18n 多语言

> 中文/英文界面切换。

- [ ] **4.1** 创建 `Localization/` 目录 + 资源文件：
  - `strings.zh-CN.json` — 中文（默认，完整翻译）
  - `strings.en.json` — 英文
  - 格式：`{ "key": "value" }` 键值对
  - 键名按模块前缀：`Menu_OpenArchive`、`Preview_Text`、`Status_Loaded` 等
- [ ] **4.2** 创建本地化管理器：
  - `LocalizationManager` 静态类
  - `CurrentCulture` 属性（切换时触发事件）
  - `T(string key)` 方法（以 WPF 的 `L.T()` 为参考）
  - 从 JSON 文件加载资源
  - 线程安全
- [ ] **4.3** `MainWindowViewModel` 添加：
  - `string CurrentLanguage` 属性
  - `SwitchLanguageCommand`（zh-CN / en 切换）
  - 菜单项 "语言 → 中文 / English"
- [ ] **4.4** 所有 ViewModel/View 中的硬编码字符串改为 `LocalizationManager.T()` 调用：
  - 菜单标题（"打开压缩包"、"退出"、"视图"等）
  - 状态栏消息（"已加载 N 个条目"、"正在加载..."等）
  - 预览面板（"未支持"、"图片预览"等）
  - 设置窗口标签
  - 密码对话框提示
- [ ] **4.5** 验证：切换语言，界面文字即时更新
- [ ] **4.6** **Commit**: `feat(avalonia): add i18n support with Chinese and English`

---

### Task 5：CLI 参数处理

> 支持命令行参数 `--open`、`--extract`、`--compress` 等。

- [ ] **5.1** `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 中解析参数：
  ```csharp
  var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
  ```
  - `--open <path>`：启动并加载压缩包
  - `--extract <path>`：直接解压到同目录（无需 UI）
  - `--extract-here <path>`：解压到当前目录
  - `--extract-to-name <path>`：解压到以文件名命名的目录
- [ ] **5.2** 创建 CLI 处理流程：
  - 无参数 → 正常启动 MainWindow
  - 有参数 → 处理完后退出（或显示窗口）
  - 使用 Core 的 `ArchiveEngineFactory` + `IArchiveEngine.ExtractAsync`
- [ ] **5.3** 验证：`dotnet run -- --open test.zip` 启动并加载压缩包
- [ ] **5.4** **Commit**: `feat(avalonia): add CLI argument handling`

---

### Task 6：拖拽导出

> 从文件列表拖拽文件到资源管理器。

- [ ] **6.1** `MainWindow.axaml.cs` 添加拖拽事件：
  - `ListBox/DragDrop`（或 DataGrid 拖拽）
  - `PreviewMouseMove` 检测拖动开始
- [ ] **6.2** 提取临时文件：
  - `ArchiveEntryExtractor.ExtractEntryAsync`（Core 已有）
  - 提取到 `%TEMP%\MantisZip\DragDrop\{GUID}\`
  - 子目录保留（使用 `FullPath` 的相对路径）
- [ ] **6.3** 创建 `DataObject`：
  - `DragDrop.SetDataObject` → `DataObject(DataFormats.FileDrop, paths[])`
- [ ] **6.4** 清理：
  - 拖拽完成后删除临时目录
  - 或进程退出时统一清理
- [ ] **6.5** 自己的窗口防护：
  - `_isOwnDrag` 标记防止 `Window_Drop` 响应自己的拖拽
- [ ] **6.6** 验证：从压缩包拖文件到桌面，文件复制成功
- [ ] **6.7** **Commit**: `feat(avalonia): add drag-drop support for extracting files`

---

### Task 7：清理 + 合并 main

> 确保分支干净，与 main 保持同步。

- [ ] **7.1** 确认 `src/MantisZip.UI/`（WPF 项目）未被触碰
- [ ] **7.2** 确认所有新文件有正确的命名空间、主题绑定
- [ ] **7.3** `git merge main` 将 main 最新改动合并到 avalonia-port
- [ ] **7.4** 解决可能的冲突
- [ ] **7.5** 验证：`dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [ ] **7.6** 更新本计划状态为 ✅

---

## 验证清单

Phase 2 完成的验收标准：

- [ ] `dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [ ] `dotnet run --project src\MantisZip.UI.Avalonia` 窗口启动
- [ ] 所有预览格式功能不受清理影响
- [ ] 设置窗口打开，修改设置后生效
- [ ] 加密压缩包弹出密码对话框
- [ ] HTML 预览渲染正常
- [ ] Markdown 预览渲染正常（可切换源码）
- [ ] 界面可在中文/英文间切换
- [ ] `--open test.zip` CLI 参数启动并加载压缩包
- [ ] 从压缩包拖拽文件到资源管理器成功
- [ ] WPF 项目 `src/MantisZip.UI/` 未被修改

---

## 边界情况与注意事项

1. **WebView 选型**：若 `Avalonia.WebView`（跨平台）不可用，Windows 上先用 `Avalonia.WebView2`，其他平台回退显示 HTML/Markdown 源码
2. **密码缓存**：`_sessionPasswords` 仅存当前进程内存，退出即清空。不写入磁盘
3. **设置共享**：与 WPF 版共用 `%LOCALAPPDATA%\MantisZip\settings.json`，需注意字段兼容性
4. **i18n 切换**：运行时切换需要重新绑定所有字符串——通过 `PropertyChanged` 或重置 `DataContext`
5. **CLI 解压**：`--extract` 等解压命令需要 `IArchiveEngine.ExtractAsync`，Core 已支持
6. **拖拽大文件**：提取到临时目录可能耗时，考虑加 `ProgressWindow`（后续 Phase 实现）
7. **FormatUtil.FormatSize 格式统一**：Core 版目前用 `0.##`（去末尾零），Avalonia 版用 `F2`（固定两位）。清理时应统一为 `F2`（更整齐，与 WPF 一致）
