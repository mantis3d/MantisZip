# i18n 国际化方案 — 实施计划

## 目标

为中/英社区贡献者提供完善的国际化支持。

## 总览

| 方案 | JSON 资源文件 + 静态代理类 + 自定义 MarkupExtension |
|------|--------------------------------------------------|
| 运行时切换 | 支持（无需重启） |
| 社区翻译 | 发 PR 改一个 JSON 文件即可 |
| 翻译可见性 | .github/ 里设公开的翻译讨论 issue |
| 预计工时 | 4-6h（脚本辅助）/ 12-18h（纯手动） |

## 架构

```
MantisZip.UI/
├── Localization/
│   ├── LanguageManager.cs      // 单例，加载/切换语言，事件通知
│   ├── L.cs                    // 静态代理 Strings 类（大量属性）
│   └── LExtension.cs           // MarkupExtension {l:L Key}
│
├── Resources/
│   └── strings.json            // 多语言翻译 JSON
│
└── (所有 .xaml / .cs 文件)      // "中文" → L.XXX / {l:L XXX}
```

## 步骤清单

### Step 1 — 基础设施 (LanguageManager + JSON 加载)

- 创建 `MantisZip.UI/Localization/` 目录
- 创建 `LanguageManager.cs`：
  - 单例
  - 加载 `strings.json` 到内存 `Dictionary<string, Dictionary<string, string>>`
  - `CurrentLanguage` 属性（默认 `"zh"`）
  - `this[string key]` 索引器
  - `SwitchTo(string lang)` 方法，触发 `LanguageChanged` 事件
  - 语言选择保存到 `AppSettings.Language`
- 创建 `LExtension.cs`：
  - 实现 `MarkupExtension`，接受 `string Key`
  - `ProvideValue` 返回 `LanguageManager.Instance[key]`
  - 实现 `IValueConverter`（用于 Binding 场景）
- 初始化：在 `App.OnStartup` 中调用 `LanguageManager.Instance.Initialize()`

### Step 2 — 提取所有字符串 + 生成 JSON 模板

- 编写自动化脚本（PowerShell 或 C# 控制台），扫描所有 .cs / .xaml：
  1. 提取中文字符串字面量（含中文字符的引号字符串）
  2. 提取所有 `AppMessageBox.Show("...")` / `SetStatus("...")` / 等
  3. 提取 XAML 中的 `Content="..."` / `Header="..."` / `Title="..."` / `ToolTip="..."`
  4. 过滤掉非用户界面字符串（日志、调试信息等）
  5. 为每个字符串生成唯一 key（参见下方命名规范）
  6. 输出 `strings.json`（包含 zh 原文，en 留空）
  7. 输出替换报告（每处替换的 文件:行号:old → new）

- **Key 命名规范**：
  - `Menu_XXX` — 菜单项
  - `Btn_XXX` — 按钮
  - `Status_XXX` — 状态栏
  - `Msg_XXX` — 消息框/弹窗
  - `Title_XXX` — 窗口标题
  - `Label_XXX` — 标签/说明文字
  - `ToolTip_XXX` — 工具提示
  - `Preview_XXX` — 预览相关
  - `Progress_XXX` — 进度相关
  - `ContextMenu_XXX` — 右键菜单
  - `ArchiveInfo_XXX` — 压缩包信息
  - `ShellVerb_XXX` — 右键菜单动词显示名
  - `Error_XXX` — 错误消息
  - `Password_XXX` — 密码相关

- **格式字符串处理**：
  - `$"解压失败: {msg}"` → key `Msg_ExtractFailed`，值 `"解压失败: {0}"`
  - C# 代码中 `string.Format(L["Msg_ExtractFailed"], msg)` 或 `S.Msg_ExtractFailed.Format(msg)`
  - 需要 `StringExtensions.Format(this string, params object[])` 扩展方法

### Step 3 — 批量替换 C# 代码字符串

- 对所有 .cs 文件执行脚本替换：
  - `"中文字符串"` → `S.XXX`
  - `$"中文{expr}"` → `S.XXX.Format(arg)` （需要拆分模板和参数）
  - `AppMessageBox.Show("中文", "中文标题")` → `AppMessageBox.Show(S.Msg_XXX, S.Title_XXX)`
- 涉及文件：
  - MainWindow.xaml.cs
  - MainWindow.Preview.cs
  - MainWindow.DragDrop.cs
  - MainWindow.Menu.cs
  - MainWindow.UI.cs
  - App.xaml.cs
  - AppSettings.cs
  - ProgressWindow.xaml.cs
  - PasswordDialog.xaml.cs
  - PasswordManagerWindow.xaml.cs
  - PasswordEditDialog.xaml.cs
  - PasswordHelpDialog.xaml.cs
  - SettingsWindow.xaml.cs
  - CompressSettingsWindow.xaml.cs
  - CompressConflictDialog.xaml.cs
  - ConflictDialog.xaml.cs
  - ErrorDialog.xaml.cs
  - AppMessageBox.xaml.cs
  - ShellIntegration.cs
  - SystemIconHelper.cs（如果有）
- **MantisZip.Core** 中的异常消息也需要提取（约 20 条）

### Step 4 — 批量替换 XAML 字符串

- 对所有 .xaml 文件执行脚本替换：
  - `Content="中文"` → `Content="{l:L XXX}"`
  - `Header="中文"` → `Header="{l:L XXX}"`
  - `Title="中文"` → `Title="{l:L XXX}"`
  - `ToolTip="中文"` → `ToolTip="{l:L XXX}"`
  - `Text="中文"` → `Text="{l:L XXX}"`
  - `InputGestureText="中文"` → `InputGestureText="{l:L XXX}"`
- 添加 XAML 命名空间：`xmlns:l="clr-namespace:MantisZip.UI.Localization"`
- 涉及文件：
  - MainWindow.xaml（~100 处替换）
  - SettingsWindow.xaml（~50 处替换）
  - ProgressWindow.xaml
  - PasswordDialog.xaml
  - PasswordEditDialog.xaml
  - PasswordManagerWindow.xaml
  - PasswordHelpDialog.xaml
  - CompressSettingsWindow.xaml
  - CompressConflictDialog.xaml
  - ConflictDialog.xaml
  - ErrorDialog.xaml
  - AppMessageBox.xaml

### Step 5 — 各窗口语言切换刷新

- 定义刷新接口/基类方法 `OnLanguageChanged()`
- 每个 Window 订阅 `LanguageManager.Instance.LanguageChanged`：
  - 重新设置窗口中有绑定但非自动刷新的属性
  - MessageBox 和 Dialog 无需刷新（它们是模态的，下次打开会自动用新语言）
  - 状态栏等动态内容在下次 `SetStatus` 时自动使用新语言
- 主要是确保打开中的 ProgressWindow 标题、状态文本能切换
- Shell 右键菜单（ShellIntegration.cs）的语言切换：重新执行 `Install()`

### Step 6 — 英语翻译填充

- 翻译所有 en 条目
- 可以用 LLM 批量翻译，人工审校
- 对于格式字符串，确保翻译后占位符数量一致

### Step 7 — 验证

- 检查所有 XAML 编译无错误
- 切换到英文模式，遍历所有窗口/对话框/功能，检查遗漏
- 切换到中文模式，确认回退正常
- 检查格式字符串参数数量是否匹配
- 确认快捷键（`_F`, `_O` 等）在每个语言中正确设置

## 关键设计决策

### 1. JSON 结构：单文件还是多文件？

**选单文件**（`strings.json` 包含所有语言）：
- 一个 PR 能改所有翻译
- diff 集中，review 方便
- 文件会逐渐变大，但 300 条 × 5 语言 ≈ 50KB 完全可以接受
- 多文件方案（每语言单独文件）也可以，但需要加载逻辑更复杂

### 2. 静态类生成：手写还是自动？

**手写** 300+ 属性很痛苦。推荐以下方式之一：
- **Source Generator**（最优雅，但 C# 新手不熟悉）
- **T4 模板**（从 JSON 生成 `.cs` 文件，build 时自动运行）
- 最简方案：手写 + 用脚本一次性生成骨架代码，然后手动微调

### 3. 格式字符串参数

```csharp
// 需要这个扩展方法
public static string Format(this string template, params object[] args)
    => string.Format(template, args);
```

这样 `S.Msg_ExtractFailed.Format(ex.Message)` 比 `string.Format(S.Msg_ExtractFailed, ex.Message)` 更简洁。

### 4. 非字符串资源（图片、图标）

不在本次 i18n 范围内。如果有需要替换的图片（如语言特定图标），后续再处理。

### 5. 和 AppSettings 的集成

```csharp
// AppSettings.cs
public string Language { get; set; } = "zh"; // "zh" / "en"
```

### 6. Core 层的异常消息

`MantisZip.Core` 中的用户可见异常消息也需提取。Core 层不能引用 UI 层的 `L` 类。两种方案：
- **方案 A**：Core 层也建一个 `Localization` 类（但需要避免耦合）
- **方案 B（推荐）**：Core 层的异常消息保持原文不变，UI 层在 catch 住后翻译后抛给用户
- **方案 C**：Core 层抛异常时使用错误码/枚举，UI 层根据错误码查找翻译文本

**推荐方案 B**：改动最小。Core 层继续抛中文消息，UI 层在 `AppMessageBox.Show` 时用 `L` 翻译。但如果将来 Core 层被其他项目引用，这个方案不够好。折中方案是 Core 层抛英文消息（开发者的通用语言），UI 层按需翻译成用户语言。

## 回退策略

- 如果某个 key 在当前语言中缺失 → 回退到英文
- 如果英文也缺失 → 显示 `!KEY_NAME!`（醒目提示遗漏）
- LanguageManager 启动时自动检测 JSON 文件完整性，日志警告缺失 key

## 文件变更清单

```
新增：
  src/MantisZip.UI/Localization/LanguageManager.cs
  src/MantisZip.UI/Localization/LExtension.cs
  src/MantisZip.UI/Localization/L.cs               (静态代理，大量属性)
  src/MantisZip.UI/Resources/strings.json           (翻译文件)

修改：
  src/MantisZip.UI/App.xaml.cs                      (初始化 LanguageManager)
  src/MantisZip.UI/AppSettings.cs                   (+ Language 属性)
  src/MantisZip.UI/MainWindow.xaml                  (XAML 全部标签替换)
  src/MantisZip.UI/MainWindow.xaml.cs               (.cs 全部字符串替换)
  src/MantisZip.UI/MainWindow.Preview.cs            (同上)
  src/MantisZip.UI/MainWindow.DragDrop.cs           (同上)
  src/MantisZip.UI/MainWindow.Menu.cs               (同上)
  src/MantisZip.UI/MainWindow.UI.cs                 (同上)
  src/MantisZip.UI/SettingsWindow.xaml              (XAML + 语言选择器)
  src/MantisZip.UI/SettingsWindow.xaml.cs           (语言切换逻辑)
  src/MantisZip.UI/ProgressWindow.xaml              (XAML)
  src/MantisZip.UI/ProgressWindow.xaml.cs           (.cs)
  src/MantisZip.UI/PasswordDialog.xaml / .cs        (同上)
  src/MantisZip.UI/PasswordManagerWindow.xaml / .cs (同上)
  src/MantisZip.UI/PasswordEditDialog.xaml / .cs    (同上)
  src/MantisZip.UI/PasswordHelpDialog.xaml / .cs    (同上)
  src/MantisZip.UI/CompressSettingsWindow.xaml / .cs(同上)
  src/MantisZip.UI/CompressConflictDialog.xaml / .cs(同上)
  src/MantisZip.UI/ConflictDialog.xaml / .cs        (同上)
  src/MantisZip.UI/ErrorDialog.xaml / .cs           (同上)
  src/MantisZip.UI/AppMessageBox.xaml / .cs         (同上)
  src/MantisZip.UI/ShellIntegration.cs              (右键菜单显示名)
  src/MantisZip.Core/Utils/CoreLog.cs               (如果有用户可见消息)
  src/MantisZip.Core/Engines/  (异常消息，见 Core 层策略)
```
