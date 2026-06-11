# Avalonia 移植 Phase 0：项目骨架 + 文件浏览 + 基本预览

> **分支**: `avalonia-port`（从 `master` 分出）  
> **目标**: 在 Windows 上新建 `MantisZip.UI.Avalonia` 项目，与 WPF 版共存，验证：  
>   1. 能打开 ZIP/7z/tar.gz 并列出文件  
>   2. 能预览文本 / CSV / PE 三种最简单格式  
> **设计决策**（已确认）:
>   - UI 架构: **MVVM**（CommunityToolkit.Mvvm，与 WPF 版一致的包）  
>   - 图像渲染: **SkiaSharp**（但 Phase 0 不涉及图像预览）  
>   - 7z 压缩降级: 非 Windows 平台提示安装 p7zip（Phase 0 不涉及）  
> **不做的**: 不移植设置/密码/主题/ShellExt/WebView2/DPAPI  
> **依赖**: Core 项目（`src/MantisZip.Core/`）无需改动  
> **创建日期**: 2026-06-11  
> **更新日期**: 2026-06-11  
> **状态**: ✅ Phase 0 完成 | **进度**: [█████████████████] (27/27)

> **完成于**: 2026-06-11  
> **分支**: `avalonia-port`  
> **提交**:
> - `2ffaa7a` feat(avalonia): scaffold project skeleton with MainWindow
> - `1cd5f7e` feat(avalonia): add ArchiveService and ArchiveItemModel with FromCore mapping
> - `672c993` feat(avalonia): file list DataGrid with Open archive dialog
> - `58ef9c3` feat(avalonia): text preview with encoding detection
> - `37bf602` feat(avalonia): CSV and PE previews

---

## 文件结构

```
src/MantisZip.UI.Avalonia/                    # 新目录，全是新建文件
├── MantisZip.UI.Avalonia.csproj              # net9.0（非 -windows）
├── App.axaml                                 # Application 定义
├── App.axaml.cs                              # 启动入口
├── appsettings.json                          # 运行时生成的设置（暂用默认值）
│
├── Models/
│   ├── ArchiveItemModel.cs                   # Core.ArchiveItem → UI 展示模型
│   └── ArchiveFormatHelper.cs                # 格式检测扩展方法
│
├── ViewModels/
│   ├── MainWindowViewModel.cs                # 主窗口 VM（文件列表 + 命令）
│   └── PreviewViewModel.cs                   # 预览面板 VM
│
├── Views/
│   ├── MainWindow.axaml                      # 主窗口布局
│   ├── MainWindow.axaml.cs                   # 代码后置（仅视图逻辑）
│   ├── PreviewPanel.axaml                    # 预览面板控件
│   └── PreviewPanel.axaml.cs
│
├── Services/
│   ├── ArchiveService.cs                     # 封装 ArchiveEngineFactory 调用
│   └── PreviewService.cs                     # 预览数据获取（Core 解析器）
│
├── Converters/
│   ├── FileSizeConverter.cs                  # 字节 → "1.23 MB"
│   └── BoolToVisibilityConverter.cs          # 控制预览面板显示
│
├── Controls/
│   └── CsvDataGrid.axaml / .cs              # CSV 预览 DataGrid 封装
│
├── Resources/
│   └── Icons/                                # 内置图标（MIME 类型图标占位）
│
└── Styles/
    └── AppStyles.axaml                       # 基础样式（Phase 1 再移植主题）
```

## 任务分解

### Task 1：项目脚手架

> 创建 csproj、App.axaml、MainWindow 骨架，验证 build + run 通过。

- [ ] **1.1** 创建 `src/MantisZip.UI.Avalonia/` 目录结构
- [ ] **1.2** 编写 `MantisZip.UI.Avalonia.csproj`：
  - TargetFramework: `net9.0`（**不加** `-windows` TFM）
  - OutputType: `WinExe`
  - 包引用：
    - `CommunityToolkit.Mvvm` 最新版（与 WPF 版对齐）
    - `Avalonia.Desktop`（含 Skia 后端）
    - `Avalonia.Controls.DataGrid`
    - `Avalonia.Diagnostics`（Debug 构建时）
  - NuGet 包引用（从 Core 和 WPF 版继承）：
    - `Ude.NetStandard`（文本编码检测）
    - `Microsoft.Data.Sqlite`（SQLite 预览，Phase 1 用）
  - 项目引用：`MantisZip.Core.csproj`
- [ ] **1.3** 编写 `App.axaml` + `App.axaml.cs`：
  - `Application` 定义，引用 `MainWindow`
  - `OnFrameworkInitializationCompleted` 中 `new MainWindow{...}.Show()`
  - DataTemplates 注册 ViewModel → View 映射
- [ ] **1.4** 编写 `Views/MainWindow.axaml` + `.cs`：
  - 最小骨架：标题栏 + 菜单栏 + 分割面板区域
  - 代码后置：只有 `InitializeComponent()` + 构造函数注入 VM
- [ ] **1.5** 编写 `ViewModels/MainWindowViewModel.cs`：
  - 继承 `ObservableObject`
  - 空属性占位：`Title`、`ArchivePath`
- [ ] **1.6** 验证：`dotnet build src\MantisZip.UI.Avalonia` 编译通过
- [ ] **1.7** 验证：`dotnet run --project src\MantisZip.UI.Avalonia` 窗口启动
- [ ] **1.8** **Commit**: `feat(avalonia): scaffold project skeleton with MainWindow`

### Task 2：文件列表模型 + 服务层

> 封装 Core 的 `ArchiveEngineFactory.ListEntriesAsync`，建立 MVVM 数据流。

- [ ] **2.1** 编写 `Models/ArchiveItemModel.cs`：
  - 包裹 `Core.ArchiveItem`，添加 UI 展示属性：
    - `DisplayName`（文件名）
    - `SizeDisplay`（格式化大小）
    - `CompressedSizeDisplay`
    - `LastModifiedDisplay`
  - 继承 `ObservableObject`
- [ ] **2.2** 编写 `Services/ArchiveService.cs`：
  - 方法 `LoadArchiveAsync(string path, string? password, CancellationToken ct)`：
    1. 检测格式 → `ArchiveEngineFactory.GetEngine()`
    2. `engine.ListEntriesAsync(stream, password, ct)`
    3. 映射 `ArchiveItem` → `ArchiveItemModel`
    4. 返回 `IReadOnlyList<ArchiveItemModel>`
  - 方法 `GetStreamAsync(string path)`：打开文件流 + 缓存管理
- [ ] **2.3** 编写 `Converters/FileSizeConverter.cs`：
  - `IValueConverter`：`long` → `"1.23 MB"`（复用 Core 逻辑或独立实现）
- [ ] **2.4** **Commit**: `feat(avalonia): add ArchiveService and ArchiveItemModel`

### Task 3：文件列表 DataGrid + 打开压缩包

> 核心交互：用户点「打开」→ 选文件 → 列表显示。

- [ ] **3.1** `MainWindowViewModel` 添加：
  - `ObservableCollection<ArchiveItemModel> Entries`
  - `ArchiveItemModel? SelectedEntry`
  - `bool IsArchiveLoaded`
  - `string? CurrentArchivePath`
- [ ] **3.2** `MainWindowViewModel` 添加命令：
  - `OpenArchiveCommand`：`AsyncRelayCommand`
    - `OpenFileDialog` → 获取路径
    - `ArchiveService.LoadArchiveAsync` → 填充 `Entries`
    - 异常处理：损坏压缩包 → 显示错误消息
  - `ClearArchiveCommand`：清空列表
- [ ] **3.3** `MainWindow.axaml` 布局：
  - 顶部 `Menu`：文件 → 打开压缩包、退出
  - 主区域 `Grid` 两列：
    - 左列：`DataGrid` 绑定 `Entries`
    - 右列：空的预览区域 `ContentControl`（Phase 2 填充）
  - DataGrid 列：文件名 | 大小 | 压缩后大小 | 修改日期
  - 状态栏：左下角文件数 / 选中文件信息
- [ ] **3.4** 编写 `Converters/BoolToVisibilityConverter.cs`：
  - 控制无文件时的提示文本显示/隐藏
- [ ] **3.5** 验证：打开一个 ZIP，文件列表正常显示，列可排序
- [ ] **3.6** **Commit**: `feat(avalonia): file list DataGrid with Open archive command`

### Task 4：文本预览

> 最简单的预览：检测编码 → 显示纯文本。

- [ ] **4.1** `PreviewViewModel.cs` 编写：
  - 属性：`PreviewContentType`（enum: None/Text/Csv/Pe）、`TextContent`
  - 方法：`ShowTextPreview(string filePath)`
    - 用 `Ude.NetStandard` 检测编码（Core 已有引用）  
    - 读取前 `MaxTextPreviewBytes` 字节
    - 设置 `TextContent` + `PreviewContentType`
  - 方法：`ClearPreview()`
- [ ] **4.2** `MainWindowViewModel` 连接预览：
  - `SelectedEntry` 变化时触发预览
  - 调用 `ArchiveService` 临时提取文件到 `%TEMP%`
  - 调用 `PreviewViewModel.ShowTextPreview()`
- [ ] **4.3** 更新 `MainWindow.axaml` 预览区域：
  - 根据 `PreviewContentType` 切换显示（`ContentControl` + `DataTemplate`）
  - 文本预览：`ScrollViewer` + `TextBox`（只读，等宽字体）
  - UTF-8 / GBK 测试文件显示正确
- [ ] **4.4** 验证：打开含 .txt/.log/.csv（作为文本）查看预览
- [ ] **4.5** **Commit**: `feat(avalonia): text preview with encoding detection`

### Task 5：CSV 预览

> CSV 以表格方式展示。

- [ ] **5.1** `PreviewViewModel` 添加 CSV 支持：
  - 属性：`DataTable? CsvData`
  - 方法：`ShowCsvPreview(string filePath)`
    - 简单 CSV 解析（逗号分隔，首行表头）
    - 限制 100 行 × 100 列（与 WPF 版一致）
- [ ] **5.2** 编写 `Controls/CsvDataGrid.axaml` + `.cs`：
  - 封装 `DataGrid` + 自动生成列
  - 无数据时的空状态文本
- [ ] **5.3** `MainWindow.axaml` 添加 CSV 的 `DataTemplate`
- [ ] **5.4** 验证：打开含 .csv 的压缩包，选中后显示表格
- [ ] **5.5** **Commit**: `feat(avalonia): CSV preview with DataGrid`

### Task 6：PE 预览（exe/dll）

> 显示 PE 文件元数据：产品名、公司、版本、架构等。

- [ ] **6.1** `PreviewViewModel` 添加 PE 支持：
  - 属性：`IReadOnlyList<(string,string)>? MetadataItems`
  - 方法：`ShowPePreview(string filePath)`
    - 调用 `PeParser.Parse(filePath)`（Core 已有）
    - 映射返回的字典到键值对列表
- [ ] **6.2** `MainWindow.axaml` 添加 PE 预览 `DataTemplate`：
  - `ItemsControl` 显示键值对
  - 格式：分两栏（key 灰色，value 正常色）
- [ ] **6.3** 验证：打开含 .exe/.dll 的压缩包，选中后显示元数据
- [ ] **6.4** **Commit**: `feat(avalonia): PE metadata preview`

### Task 7：清理 + 分支同步策略

> 确保分支干净，与 master 保持同步。

- [ ] **7.1** 确认 `src/MantisZip.UI/`（WPF 项目）未被触碰
- [ ] **7.2** 确认 `src/MantisZip.Core/` 未被修改
- [ ] **7.3** `git merge master` 将 master 最新改动合并到 avalonia-port
- [ ] **7.4** 解决可能的冲突（Core/ 文件，概率低）
- [ ] **7.5** 验证：merge 后 `dotnet build` 通过，Avalonia 版仍可运行
- [ ] **7.6** 更新 `.sisyphus/plans/cross-platform-port.md` 状态标记 Phase 0 完成

---

## 验证清单

Phase 0 完成的验收标准：

- [ ] `dotnet build src\MantisZip.UI.Avalonia` ✅
- [ ] `dotnet run --project src\MantisZip.UI.Avalonia` 启动空窗口 ✅
- [ ] 打开一个 ZIP 文件，文件列表正常显示 ✅
- [ ] 选中 .txt 文件，文本预览正常显示 ✅
- [ ] 选中 .csv 文件，表格预览正常显示 ✅
- [ ] 选中 .exe/.dll 文件，PE 元数据显示正常 ✅
- [ ] WPF 项目 `src/MantisZip.UI/` 未被修改 ✅
- [ ] Core 项目 `src/MantisZip.Core/` 未被修改 ✅
- [ ] `git merge master` 无冲突 ✅

---

## 边界情况与注意事项

1. **OpenFileDialog**：Avalonia 用 `OpenFileDialog`（`Avalonia.Controls`），不是 `Microsoft.Win32.OpenFileDialog`
2. **临时文件清理**：预览提取的临时文件在 `App.axaml.cs` 的 `OnExit` 中清理
3. **编码检测**：`Ude.NetStandard` 在 Core 已有引用，Avalonia 项目加引用即可
4. **大文件**：文本预览限制 `MaxTextPreviewBytes`（默认 1MB），与 WPF 版一致
5. **DataGrid 虚拟化**：Avalonia DataGrid 默认启用虚拟化，大压缩包（10 万+ 文件）不会卡 UI
6. **密码保护的压缩包**：Phase 0 暂不处理密码对话框，在 `ArchiveService` 中统一传入 `password: null`，SharpCompress 遇到加密条目时会抛出异常，捕获后显示"需要密码"提示
