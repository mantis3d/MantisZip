# Phase 8: 设置窗口 WPF 功能复刻

## 目标

将 Avalonia 版设置窗口增强到与 WPF 版功能平级：新增 4 个标签页、增强 3 个已有标签页、实现语言动态切换基础设施、调整布局风格。

## 前置条件

- Phase 7（功能补齐）已完成
- 当前分支：`avalonia-port`

## 任务分解

### Task 1：LanguageManager 基础设施（前置依赖）

**文件**：
- 新建 `src/MantisZip.UI.Avalonia/Services/LanguageManager.cs`
- 修改 `strings.zh-CN.json`、`strings.en.json`（新增语言相关键）
- 现有 `LocalizationManager` 在 `Services/LocalizationManager.cs`

**实现要点**：
- 实现 `AvailableLanguages`（`(Code, DisplayName)[]`）列表
- `CurrentLanguage` 属性，切换时重新加载 `strings.*.json` 并触发 UI 刷新
- 所有 ViewModel 的 `LocalizationManager.T()` 调用改为支持动态切换
- 参考 WPF `LanguageManager`（`src/MantisZip.UI/AppPartials/App.Language.cs` 或类似文件）

**验收标准**：
- ComboBox 切换语言后，当前打开的窗口 UI 文本即时刷新
- 语言选择持久化到 `AppSettings.Language`

### Task 2：新增「外观」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml` — 新增 TabItem
- 修改 `ViewModels/SettingsWindowViewModel.cs` — 新增属性

**实现要点**：
- 主题 ComboBox：Light / Dark（枚举值绑定到 `AppSettings.Theme`）
  - 切换时调用主题切换方法（参考 WPF `ThemeCombo_SelectionChanged`）
- 最大最近文件数：TextBox + NumericOnly 校验
- i18n 键：`Settings_Tab_Appearance`、`Settings_Appearance_Theme`、`Settings_Appearance_Theme_Light`、`Settings_Appearance_Theme_Dark`、`Settings_Appearance_MaxRecentFiles`

### Task 3：新增「密码管理」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml`
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 修改 `Models/AppSettings.cs`（添加缺失的属性）

**实现要点**：
- `ShowPasswordMatchNotification` CheckBox
- `PasswordRevealByDefault` CheckBox（首次打开密码输入框时是否自动显示密码）
- i18n 键：`Settings_Tab_Password`、`Settings_Pwd_ShowNotification`、`Settings_Pwd_ShowHint`、`Settings_Pwd_RevealDefault`

### Task 4：新增「语言」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml`
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 依赖 Task 1（LanguageManager）

**实现要点**：
- 语言 ComboBox 绑定 `LanguageManager.AvailableLanguages`
- SelectionChanged → 切换语言 → 刷新当前窗口 UI
- 译者信息 TextBlock（`LanguageTranslatorText`）
- i18n 键：`Settings_Tab_Language`、`Settings_Language`

### Task 5：新增「文件关联」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml`
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 修改 `Models/AppSettings.cs`（添加关联属性）
- 可选：新建 `ViewModels/FormatAssocItem.cs`（CheckBox 绑定模型）

**实现要点**：
- 8 个内置扩展名：`.zip`、`.7z`、`.rar`、`.tar`、`.tar.gz`、`.gz`、`.iso` + 自定义
- 全选/取消全选按钮
- 自定义扩展名列表（带添加/删除）
- 非 Windows 平台显示"当前平台不支持"
- 代码逻辑参考 WPF `SettingsWindow.Assoc.cs`
- 调用 `ShellIntegration.Assoc`（通过 CLI 委托给 WPF exe）
- i18n 键：`Settings_Tab_FileAssoc`、`Settings_Assoc_*`

### Task 6：增强「压缩」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml`（Compress TabItem 内）
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 修改 `Models/AppSettings.cs`

**实现要点**：
- 现有控件后追加 Separator + 三个 CheckBox：
  - `CloseAfterCompress` — 压缩完成后关闭对话框
  - `KeepOriginalExtension` — 保留原文件扩展名（abc.max → abc.max.zip）
  - `PreserveDirectoryRoot` — 压缩文件夹时保留外层目录
- i18n 键：`Settings_Compress_CloseAfterDone`、`Settings_Compress_KeepExt`、`Settings_Compress_PreserveRoot`

### Task 7：增强「预览」标签页（最复杂）

**文件**：
- 修改 `Views/SettingsWindow.axaml`（Preview TabItem → 子 TabControl）
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 修改 `Models/AppSettings.cs`

**实现要点**：
- 现有简单布局 → 嵌套 TabControl（与 WPF PreviewSubTabs 一致）
- 子标签页 1 — **文本**：启用图像预览 CheckBox、启用文本预览 CheckBox、字体族 Combo（系统字体列表）、字号 Slider（实时预览，需回调 MainWindow）、颜色 Emoji 开关、最大文本大小 Slider（MB）
- 子标签页 2 — **字体**：样本文本 TextBox（多行可编辑）、字体预览字号 Slider
- 子标签页 3 — **表格**：最大行数 TextBox、最大列数 TextBox
- 子标签页 4 — **布局**：预览位置 Combo（底部/树下/文件列表下/右侧）、信息面板方向 Combo（水平/垂直）、显示预览面板 CheckBox、最大预览文件大小 Slider（MB）
- i18n 键：参考 WPF `Settings_Preview_Tab_Text`、`Settings_Preview_Tab_Font`、`Settings_Preview_Tab_Table`、`Settings_Preview_Tab_Position` 等

### Task 8：增强「调试」标签页

**文件**：
- 修改 `Views/SettingsWindow.axaml`（Debug TabItem 内）
- 修改 `ViewModels/SettingsWindowViewModel.cs`
- 修改 `Models/AppSettings.cs`

**实现要点**：
- 现有 EnableDebugLogging CheckBox 保持不变
- 追加：日志隐私模式 Combo（off / filename / extension / full）
- 帮助按钮 → `LogPrivacyHelpDialog`（Phase 7 已有）
- 日志文件路径显示（只读 TextBlock）
- i18n 键：`Settings_Debug_LogPrivacyMode`、`Settings_Debug_LogPath`、`Settings_Debug_LogPrivacyHelp`、`Settings_Debug_Restart`

### Task 9：布局调整

**文件**：
- 修改 `Views/SettingsWindow.axaml` — TabControl 属性 + TabItem Header

**实现要点**：
- `TabControl.TabStripPlacement="Left"`（匹配 WPF 布局）
- 每个 TabItem 添加 emoji 图标：
  - 🔧 压缩
  - 📂 解压
  - 👁 预览
  - 🎨 外观
  - 🔐 密码管理
  - 🌐 语言
  - 🖱️ 上下文菜单
  - 🐛 调试
  - ⚙️ 高级
  - 🔗 文件关联
- 调整 TabItem.MinWidth/MinHeight 适配左侧布局
- 注意：Theme 资源需保证左侧 TabStrip 在暗色模式下可见

### Task 10：ViewModel 设置持久化

**文件**：
- 修改 `ViewModels/SettingsWindowViewModel.cs` — `SaveSettings()` / `LoadSettings()`
- 修改 `Models/AppSettings.cs` — 添加所有缺失的属性字段

**实现要点**：
- 从 WPF `SettingsWindow.xaml.cs` 的 `SaveSettings()` / `LoadSettings()` 复刻完整的读写逻辑
- 确保所有新标签页的设置在 Save/Load 中覆盖
- 确保 JSON 序列化兼容性（新字段不影响旧 settings.json）

## 文件清单

### 修改文件
| 文件 | 变更内容 |
|------|----------|
| `Views/SettingsWindow.axaml` | 全部 10 个标签页 + 左侧布局 + emoji 图标 |
| `ViewModels/SettingsWindowViewModel.cs` | 所有新属性 + Save/Load + 命令 |
| `Models/AppSettings.cs` | 添加所有缺失的 WPF 属性字段 |
| `Localization/strings.zh-CN.json` | 新增 50+ 翻译键 |
| `Localization/strings.en.json` | 新增 50+ 翻译键 |

### 新建文件
| 文件 | 说明 |
|------|------|
| `Services/LanguageManager.cs` | 动态语言切换基础设施 |
| `ViewModels/FormatAssocItem.cs` | 文件关联条目 ViewModel（可选） |

### 不修改文件
| 文件 | 原因 |
|------|------|
| `App.axaml` / `App.axaml.cs` | CLI 和 IPC 在 Phase 7 已完成 |
| `MainWindow.axaml` / `MainWindow.axaml.cs` | 设置窗口独立，不涉及主窗口 |
| `Dialogs/*.axaml` | Phase 7 已全部完成 |
| ShellExt 相关 | WPF 独占功能，委托执行 |

## 构建与验证

1. 每次 Task 完成后：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` — 0 errors, 0 warnings
2. 全部完成后：`dotnet test tests/MantisZip.UI.Avalonia.Tests/` — 全部通过
3. 所有新增控件必须绑定主题资源键（`Theme*Brush` 系列）

## 工作量预估

| Task | 内容 | 预估文件 | 难度 |
|------|------|:--------:|:----:|
| 1 | LanguageManager 基础设施 | 2-3 | 🟡中 |
| 2 | 外观标签 | 2 | 🟢低 |
| 3 | 密码管理标签 | 2 | 🟢低 |
| 4 | 语言标签（依赖 Task 1） | 2 | 🟢低 |
| 5 | 文件关联标签 | 3-4 | 🟡中 |
| 6 | 压缩标签增强 | 2 | 🟢低 |
| 7 | 预览标签增强（最复杂） | 3 | 🔴高 |
| 8 | 调试标签增强 | 2 | 🟢低 |
| 9 | 布局调整 | 1 | 🟢低 |
| 10 | ViewModel 设置持久化 | 2 | 🟡中 |
| **合计** | | **~20** | |
