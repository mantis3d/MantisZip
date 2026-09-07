# Avalonia 移植 Phase 7：功能补齐至 WPF 平级

> **分支**: `avalonia-port`（从 Phase 6 继续）
> **目标**: 补齐 Avalonia 版相对 WPF 版的全部剩余功能缺口，包括 CLI 命令、设置窗口标签页、缺失对话框、Password 标签增强，使 Avalonia 版在功能上与 WPF 版完全平级
> **约束**: ⚠️ 所有修改仅限于 `src/MantisZip.UI.Avalonia/` 及 `tests/MantisZip.UI.Avalonia.Tests/`。不修改 Core 层、WPF 项目、ShellExt
> **设计决策**:
>   - CLI 命令尽量复用 Core/UI 的现有静态方法（ShellIntegration、ArchiveEngineFactory 等）
>   - IPC 多实例模式直接移植 WPF 的 Mutex + NamedPipeServerStream 模式
>   - 设置窗口标签页沿用现有 Avalonia 的 TabControl 模式，与 CompressSettingsWindow 风格一致
>   - 缺失对话框直接移植 WPF XAML 逻辑，适配 Avalonia 样式系统（主题绑定、样式键）
>   - 对话框创建原则：先分析 WPF 的 .xaml + .cs 逻辑，再在 Avalonia 层创建对应 .axaml + .axaml.cs
>   - Windows-only 功能（Shell 集成、IPC、COM）在非 Windows 平台显示错误提示或静默跳过
> **不做的事情**:
>   - 不修改 `MantisZip.Core/` 任何文件
>   - 不修改 `MantisZip.UI/`（WPF 版不动）
>   - 不修改 `MantisZip.ShellExt/`（COM 组件不动）
>   - 不做跨平台 7z 压缩降级（属于 Core 层改动）
>   - 不做非 Windows 平台 Shell 集成
> **创建日期**: 2026-06-18
> **状态**: 📋 计划中 | **进度**: [                    ] (0/—)

---

## 功能缺口总览

| # | 功能 | WPF 位置 | 优先级 | 说明 |
|---|------|---------|:------:|------|
| 1 | CLI: `--compress` / IPC 多实例 | `App.xaml.cs:HandleCompress` | **P0** | 右键菜单→压缩对话框入口 |
| 2 | CLI: `--compress-quick` | `App.xaml.cs:HandleCompressQuick` | **P0** | 默认设置直接压缩 |
| 3 | CLI: `--compress-separate` / IPC | `App.xaml.cs:HandleCompressSeparate` | **P0** | 逐项单独压缩 |
| 4 | CLI: `--compress-combined` / IPC | `App.xaml.cs:HandleCompressCombined` | **P0** | 合并压缩 |
| 5 | CLI: `--extract-smart` | `App.xaml.cs:HandleExtractSmart` | **P0** | 智能解压 CLI |
| 6 | CLI: `--install-shell` / `--uninstall-shell` | `App.xaml.cs` | **P0** | Shell 集成安装卸载 |
| 7 | CLI: `--install-assoc` / `--uninstall-assoc` | `App.xaml.cs` | **P0** | 文件关联安装卸载 |
| 8 | 设置窗口 — Extract 标签页 | `SettingsWindow.xaml` | **P0** | 解压设置 |
| 9 | 设置窗口 — ContextMenu 标签页 | `SettingsWindow.xaml` | **P0** | 右键菜单开关 |
| 10 | 设置窗口 — Advanced 标签页 | `SettingsWindow.xaml` | **P0** | 高级设置（7z 路径、临时文件） |
| 11 | CompressConflictDialog | `Dialogs/CompressConflictDialog` | **P1** | 添加文件冲突 |
| 12 | ConflictDialog | `Dialogs/ConflictDialog` | **P1** | 解压冲突 |
| 13 | ErrorDialog | `Dialogs/ErrorDialog` | **P1** | 通用错误提示 |
| 14 | PasswordEditDialog | `Dialogs/PasswordEditDialog` | **P1** | 密码编辑 |
| 15 | PasswordHelpDialog | `Dialogs/PasswordHelpDialog` | **P1** | 密码帮助 |
| 16 | LogPrivacyHelpDialog | `Dialogs/LogPrivacyHelpDialog` | **P1** | 日志隐私帮助 |
| 17 | MatchedPasswordDialog | `Dialogs/MatchedPasswordDialog` | **P1** | 匹配密码结果 |
| 18 | CompressSettingsWindow Password 标签增强 | `CompressSettingsWindow.Password.cs` | **P2** | 库模式 + 新密码模式 + 自动规则 |
| 19 | DonationDialog | `Dialogs/DonationDialog` | **P3** | 捐赠页 |

---

## 文件总览

### 修改的文件

| 文件 | 变更内容 |
|------|---------|
| `App.axaml.cs` | 新增 9 个 CLI 命令处理 + IPC 多实例 |
| `AppConstants.cs` | 可选：新增版本/名称常量引用 |
| `ViewModels/SettingsWindowViewModel.cs` | 新增 Extract/ContextMenu/Advanced 标签页属性与命令 |
| `Views/SettingsWindow.axaml` | 新增 3 个 TabItem |
| `ViewModels/CompressSettingsViewModel.cs` | Password 标签增强 |
| `Dialogs/CompressSettingsWindow.axaml` | Password 标签内容增强 |
| `Dialogs/CompressSettingsWindow.axaml.cs` | Password 标签代码后置 |
| `Localization/strings.zh-CN.json` | 新增 i18n 键 |
| `Localization/strings.en.json` | 新增 i18n 键 |

### 新增的文件

| 文件 | 内容 |
|------|------|
| `Dialogs/CompressConflictDialog.axaml` | 压缩冲突对话框 XAML |
| `Dialogs/CompressConflictDialog.axaml.cs` | 压缩冲突对话框逻辑 |
| `Dialogs/ConflictDialog.axaml` | 解压冲突对话框 XAML |
| `Dialogs/ConflictDialog.axaml.cs` | 解压冲突对话框逻辑 |
| `Dialogs/ErrorDialog.axaml` | 通用错误对话框 XAML |
| `Dialogs/ErrorDialog.axaml.cs` | 通用错误对话框逻辑 |
| `Dialogs/PasswordEditDialog.axaml` | 密码编辑对话框 XAML |
| `Dialogs/PasswordEditDialog.axaml.cs` | 密码编辑对话框逻辑 |
| `Dialogs/PasswordHelpDialog.axaml` | 密码帮助对话框 XAML |
| `Dialogs/PasswordHelpDialog.axaml.cs` | 密码帮助对话框逻辑 |
| `Dialogs/LogPrivacyHelpDialog.axaml` | 日志隐私帮助对话框 XAML |
| `Dialogs/LogPrivacyHelpDialog.axaml.cs` | 日志隐私帮助对话框逻辑 |
| `Dialogs/MatchedPasswordDialog.axaml` | 密码匹配结果对话框 XAML |
| `Dialogs/MatchedPasswordDialog.axaml.cs` | 密码匹配结果对话框逻辑 |
| `Dialogs/DonationDialog.axaml` | 捐赠对话框 XAML |
| `Dialogs/DonationDialog.axaml.cs` | 捐赠对话框逻辑 |

### 不做修改的文件

- `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` — 不影响主窗口
- `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml` — 预览面板不改
- `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` — 不修改主 ViewModel
- `src/MantisZip.Core/` — 不做任何 Core 层修改
- `src/MantisZip.UI/` — WPF 版不动
- `tests/` — 测试暂不涉及

---

## Task 1: CLI 命令补齐 — IPC 多实例

**文件**: `App.axaml.cs`

当前 Avalonia 版已支持：`--open`, `--extract`, `--extract-here`, `--extract-to-name`

需要新增：
- `--compress` → 弹压缩对话框，IPC 多实例合并路径
- `--compress-quick` → 直接默认设置压缩 + ProgressWindow
- `--compress-separate` → 逐项单独压缩，IPC
- `--compress-combined` → 合并压缩，IPC
- `--extract-smart` → 智能解压
- `--install-shell` → 安装右键菜单
- `--uninstall-shell` → 卸载右键菜单
- `--install-assoc` → 安装文件关联
- `--uninstall-assoc` → 卸载文件关联

### IPC 多实例模式

参照 WPF `App.xaml.cs` 的 `HandleCompress` 系列方法：

```
1. Mutex 命名: MantisZipCompressMutex / MantisZipCompressSeparateMutex / MantisZipCompressCombinedMutex
2. Pipe 命名: MantisZipCompressPipe / MantisZipCompressSeparatePipe / MantisZipCompressCombinedPipe
3. 第一个进程: 创建 Mutex + NamedPipeServerStream + 800ms 收集窗口
4. 后续进程: Mutex.WaitOne 失败 → 连接 Pipe 发送路径 → 退出
5. 超时后: 第一个进程关闭 Pipe → 显示压缩对话框 / ProgressWindow
```

> **跨平台注意**: 非 Windows 平台下，--compress 等命令直接打开压缩对话框（不做 IPC，因为 ShellExt 跨平台不可用）。--install-shell 等命令显示"当前平台不支持"提示。

### 具体实现步骤

- [ ] **1.1**: 在 `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 中添加 `--compress` 分支，调用 `ShowCompressWindow` 并传入 IPC 收集的路径列表
- [ ] **1.2**: 实现 `HandleCompress(string[] paths)` — 尝试获取 Mutex `MantisZipCompressMutex`，成功则启动 PipeServer 收集，失败则 PipeClient 发送
- [ ] **1.3**: 实现 PipeServer 收集循环（800ms 超时），收集后关闭 pipe
- [ ] **1.4**: 在 MainWindow 层面实现 `StartCompressWithPaths(string[] paths)` 打开 CompressSettingsWindow
- [ ] **1.5**: 实现 `--compress-quick` 处理 — 直接创建 ProgressWindow + CompressService.CompressAsync
- [ ] **1.6**: 实现 `--compress-separate` 处理 — IPC 收集 + 逐项压缩 + ProgressWindow
- [ ] **1.7**: 实现 `--compress-combined` 处理 — IPC 收集 + 合并压缩 + ProgressWindow
- [ ] **1.8**: 实现 `--extract-smart` — 使用 ArchiveStructureAnalyzer 判断目标目录 + ExtractAsync
- [ ] **1.9**: 实现 `--install-shell` / `--uninstall-shell` — 调用 ShellIntegration.Install() / .Uninstall()
- [ ] **1.10**: 实现 `--install-assoc` / `--uninstall-assoc` — 调用 ShellIntegration.InstallAssociations() / .UninstallAssociations()
- [ ] **1.11**: 构建验证：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` — 0 errors

---

## Task 2: 设置窗口标签页补齐

**文件**: `ViewModels/SettingsWindowViewModel.cs`, `Views/SettingsWindow.axaml`

当前 Avalonia 设置窗口有 Preview/Compress/Debug 三页。需要新增 Extract/ContextMenu/Advanced 三页。

### 2a: Extract 标签页

WPF 设置中的解压设置：
- **目标路径** (Destination): RadioButton 组 — 询问我(ask) / 压缩包目录(same-dir) / 桌面(desktop)
- **文件冲突** (FileConflictAction): RadioButton 组 — 询问(ask) / 覆盖(overwrite) / 自动重命名(rename) / 跳过(skip)
- **解压后打开文件夹** (OpenFolderAfterExtract): CheckBox

### 2b: ContextMenu 标签页

WPF 设置中的上下文菜单设置：
- **EnableOpenMenu** — 打开压缩包
- **EnableCompressMenu** — 压缩菜单
- **EnableExtractHereMenu** — 解压到此处
- **EnableExtractToNamedMenu** — 解压到命名文件夹
- **EnableExtractToMenu** — 解压到……
- **EnableSmartExtractMenu** — 智能解压
- **EnableCompressSeparate** — 压缩到独立
- **EnableCompressCombined** — 压缩到合并
- **ShowMenuIcons** — 显示图标
- **安装/卸载按钮** — 调用 ShellIntegration.Install()/.Uninstall()

### 2c: Advanced 标签页

WPF 设置中的高级设置：
- **7z 路径** (SevenZipPath): TextBox + 浏览按钮（Windows-only）
- **保留目录根** (PreserveDirectoryRoot): CheckBox
- **临时文件管理** GroupBox:
  - 清理预览临时文件按钮
  - 清理所有临时文件按钮
  - 启动时自动清理 CheckBox

### 具体实现步骤

- [ ] **2.1**: 在 `SettingsWindowViewModel.cs` 中添加 `TabExtractHeader`、`TabContextMenuHeader`、`TabAdvancedHeader` 属性
- [ ] **2.2**: 添加 Extract 设置属性：`ExtractDestination`（int 枚举值）、`FileConflictAction`（int）、`OpenFolderAfterExtract`（bool）
- [ ] **2.3**: 添加 ContextMenu 设置属性：8 个 toggle 属性 + `ShowMenuIcons`
- [ ] **2.4**: 添加 Advanced 设置属性：`SevenZipPath`、`PreserveDirectoryRoot`
- [ ] **2.5**: 实现 `InstallShellCommand` / `UninstallShellCommand` RelayCommand
- [ ] **2.6**: 实现 `BrowseSevenZipCommand` RelayCommand
- [ ] **2.7**: 实现 `CleanPreviewTempCommand` / `CleanAllTempCommand` RelayCommand
- [ ] **2.8**: 在 `SettingsWindow.axaml` 中新增 3 个 TabItem（Extract/ContextMenu/Advanced），XAML 布局参照 WPF
- [ ] **2.9**: Extract 标签内容：RadioButton 组 + CheckBox，用 StackPanel + GroupBox 布局
- [ ] **2.10**: ContextMenu 标签内容：CheckBox 列表 + 安装/卸载按钮 + 分隔线
- [ ] **2.11**: Advanced 标签内容：TextBox + Browse 按钮 + CheckBox + 临时文件管理 GroupBox
- [ ] **2.12**: 所有新控件绑定主题色（`Theme_WindowBg`、`Theme_TextPrimary`、`Theme_Border` 等）
- [ ] **2.13**: 构建验证

---

## Task 3: CompressConflictDialog

**WPF 位置**: `Dialogs/CompressConflictDialog.xaml` + `.cs`

**用途**: 向压缩包添加文件时，如果同名文件已存在，弹出此对话框让用户选择处理方式。

**WPF 逻辑**:
- 显示冲突文件名列表（DataGrid 或 ListBox）
- 选项: 覆盖全部 / 跳过全部 / 自动重命名 / 取消
- 返回选中的 `ConflictAction` 枚举

**实现要点**:
- 接收 `List<ConflictFileInfo>`（文件名 + 已存在标记）
- 返回 `CompressConflictResult`（Action + CustomName）
- 使用 DialogResult 模式（ShowDialog<bool?>）
- 整体 Window 背景 `ThemeWindowBgBrush`，按钮 `ThemeButtonBgBrush`

### 具体步骤

- [ ] **3.1**: 创建 `Dialogs/CompressConflictDialog.axaml` + `.axaml.cs`，移植 WPF XAML 布局
- [ ] **3.2**: 在 Avalonia 的 `AddFiles` 命令执行前插入冲突检测逻辑
- [ ] **3.3**: 构建验证

---

## Task 4: ConflictDialog

**WPF 位置**: `Dialogs/ConflictDialog.xaml` + `.cs`

**用途**: 解压时目标文件已存在的冲突处理（更简单的版本）。

**WPF 逻辑**:
- 显示冲突文件名
- 选项: 覆盖 / 跳过 / 全部覆盖 / 全部跳过 / 自动重命名 / 取消
- 返回 `ConflictAction` 枚举 + `applyToAll` 标记

### 具体步骤

- [ ] **4.1**: 创建 `Dialogs/ConflictDialog.axaml` + `.axaml.cs`
- [ ] **4.2**: 在 `ExtractService` 或 `ExtractSettingsViewModel` 流程中集成冲突检测
- [ ] **4.3**: 构建验证

---

## Task 5: ErrorDialog

**WPF 位置**: `Dialogs/ErrorDialog.xaml` + `.cs`

**用途**: 通用错误提示对话框，显示错误标题+消息+详情展开。

**WPF 逻辑**:
- 自动计算大小（MaxWidth=600, MaxHeight=400）
- 标题 + 错误图标 + 消息文字
- "详细信息" Expander（可选）
- 关闭按钮

### 具体步骤

- [ ] **5.1**: 创建 `Dialogs/ErrorDialog.axaml` + `.axaml.cs`

---

## Task 6: PasswordEditDialog

**WPF 位置**: `Dialogs/PasswordEditDialog.xaml` + `.cs`

**用途**: 编辑密码管理器中的单条密码条目（描述、密码文本、匹配规则）。

**WPF 逻辑**:
- TextBox: 描述（Description）
- TextBox + 显示切换按钮: 密码（Password）
- TextBox: 匹配规则（Rules）
- 确认/取消按钮

### 具体步骤

- [ ] **6.1**: 创建 `Dialogs/PasswordEditDialog.axaml` + `.axaml.cs`

---

## Task 7: PasswordHelpDialog

**WPF 位置**: `Dialogs/PasswordHelpDialog.xaml` + `.cs`

**用途**: 密码管理器的使用说明帮助对话框。

**WPF 逻辑**:
- 静态文本内容，无交互逻辑
- 标题 + 说明文字
- 关闭按钮

### 具体步骤

- [ ] **7.1**: 创建 `Dialogs/PasswordHelpDialog.axaml` + `.axaml.cs`

---

## Task 8: LogPrivacyHelpDialog

**WPF 位置**: `Dialogs/LogPrivacyHelpDialog.xaml` + `.cs`

**用途**: 日志隐私模式的说明帮助对话框。

**WPF 逻辑**:
- 说明四种日志隐私模式（off/filename/extension/full）
- 静态文本 + 关闭按钮

### 具体步骤

- [ ] **8.1**: 创建 `Dialogs/LogPrivacyHelpDialog.axaml` + `.axaml.cs`

---

## Task 9: MatchedPasswordDialog

**WPF 位置**: `Dialogs/MatchedPasswordDialog.xaml` + `.cs`

**用途**: 自动匹配到密码库中的条目后，显示匹配结果供用户确认。

**WPF 逻辑**:
- 显示匹配的密码条目信息（描述 + 匹配规则）
- 确认使用 / 取消按钮

### 具体步骤

- [ ] **9.1**: 创建 `Dialogs/MatchedPasswordDialog.axaml` + `.axaml.cs`

---

## Task 10: CompressSettingsWindow Password 标签增强

**文件**: `Dialogs/CompressSettingsWindow.axaml` + `.axaml.cs`, `ViewModels/CompressSettingsViewModel.cs`

当前 Avalonia 的 CompressSettingsWindow 已有 Password 标签但功能简单。需要移植 WPF `CompressSettingsWindow.Password.cs` 的完整逻辑。

### WPF 功能参考

**库模式 (Library mode)**:
- `PasswordListBox` 列出所有保存的密码
- 每条显示两行: 描述 + 匹配规则
- `SearchBox` 过滤
- 选中后自动填入密码

**新密码模式 (New password mode)**:
- `PasswordBox` + 显示切换按钮（👁）
- `ConfirmPasswordBox` + 匹配指示
- 强度指示器（彩色 `●` 圆点）
- 保存到库 CheckBox（默认开启）
- 描述 TextBox
- 自动规则 CheckBox + 规则 TextBox

**共享区域**:
- RadioButton 始终启用（两个模式可切换）
- 只有内容面板禁用/变灰

### 具体实现步骤

- [ ] **10.1**: 在 `CompressSettingsViewModel.cs` 中添加库模式/新密码模式属性（`IsLibraryMode`、`IsNewPasswordMode`、`SearchFilter` 等）
- [ ] **10.2**: 添加密码强度计算逻辑 + 强度指示属性（`PasswordStrength`、`StrengthColor`）
- [ ] **10.3**: 添加密码匹配验证属性（`IsPasswordMatch`、`PasswordMatchMessage`）
- [ ] **10.4**: 添加保存到库相关属性（`SaveToLibrary`、`PasswordDescription`、`AutoGenerateRules`、`RulesText`）
- [ ] **10.5**: 实现 `RefreshPasswordTabUI()` 方法
- [ ] **10.6**: 修改 `CompressSettingsWindow.axaml` 的 Password TabItem 内容：
  - RadioButton 组（库模式 / 新密码模式）
  - 库模式面板: SearchBox + ListBox（两行显示）
  - 新密码模式面板: PasswordBox + ConfirmPasswordBox + 强度指示 + 保存选项
  - 共享区域: 保存到库 CheckBox + 描述 + 自动规则
- [ ] **10.7**: 所有新控件绑定主题色
- [ ] **10.8**: 构建验证

---

## Task 11: DonationDialog

**WPF 位置**: `Dialogs/DonationDialog.xaml` + `.cs`

**用途**: 捐赠信息显示。

### 具体步骤

- [ ] **11.1**: 创建 `Dialogs/DonationDialog.axaml` + `.axaml.cs`

---

## Task 12: i18n 补齐

- [ ] **12.1**: 在 `strings.zh-CN.json` 和 `strings.en.json` 中添加所有新对话框的文本键
- [ ] **12.2**: 在 `MainWindowViewModel.cs` 的 `UpdateLocalizedStrings()` 中添加新键
- [ ] **12.3**: 构建验证

---

## Task 13: 最终验证

- [ ] **13.1**: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` — 0 errors, 0 warnings
- [ ] **13.2**: `dotnet test tests/MantisZip.UI.Avalonia.Tests/` — 全部通过
- [ ] **13.3**: 验收测试（手动）：
  - CLI: `--compress` 弹压缩对话框
  - CLI: `--compress-quick` 直接压缩 + 进度窗
  - CLI: `--compress-separate` 逐项压缩
  - CLI: `--compress-combined` 合并压缩
  - CLI: `--extract-smart` 智能解压
  - CLI: `--install-shell` / `--uninstall-shell`
  - 设置窗口: Extract/ContextMenu/Advanced 三标签页功能正常
  - 压缩冲突对话框显示正确
  - 解压冲突对话框显示正确
  - ErrorDialog 显示正确
  - Password 标签库模式/新密码模式切换正常
  - i18n 切换后新文本显示正确
  - 亮/暗主题切换后新对话框正常

---

## 不做的事情

- 不修改 Core 层（包括 7z 引擎、LogRedactor 等）
- 不修改 WPF 项目
- 不修改 ShellExt COM 组件
- 不做跨平台 7z 压缩降级（属于 Core 层独立的未来工作）
- 不做非 Windows 平台的 Shell 集成
- 不重构现有代码结构（如 SettingsWindow 现有标签页不改动）
