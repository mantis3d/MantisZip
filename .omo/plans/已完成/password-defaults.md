# 密码相关默认值可配置（加密模式 / 自动产生规则 / 更新符合规则 / 永久保存）

> **状态**: ✅ 已完成（2026-11-27，`beta` 分支，随 v0.5.8 发布） | **创建**: 2026-11-27 | **优先级**: P2 | **预估工时**: 4-5h
> **来源**: GitHub issue [#87](https://github.com/mantis3d/MantisZip/issues/87)（原自 #76 图 5）
> **当前核实**: 四处默认值原先全部硬编码在构造函数或 XAML 里，`AppSettings` 中一个对应字段都没有；现已改为设置项并接线到两个消费端
> **完成情况**: 四个设置项 + 设置页「密码默认值」卡片 + 三语 8 个 key + 两个消费端接线 + 11 例测试全部落地，门禁（build / i18n / guards / tests / ui-tests）全绿

## 背景

打开压缩设置 → 加密页，密码模式每次都默认落在「密码库」，不会记住上次选择的「新密码」；「更新符合规则」「自动产生规则」恒为勾选状态；密码对话框中的「永久保存到密码库」「自动产生规则」每次打开都回到固定值。

issue 期望把这四项做成**可配置的默认值并持久化**，同时给出三条行为约定：

1. 窗口 / 对话框打开时按设置初始化；
2. **窗内临时修改只对本次生效、不回写全局默认值**；
3. 原有选项的行为一律不改（不删「密码库」模式、不删两个规则勾选）。

## 现状核实（2026-11-27）

| 设置项 | 消费点 | 现状 | 位置 |
|---|---|---|---|
| ① 密码模式默认值 | 压缩设置窗加密页 | 硬编码 `_isPasswordLibraryMode = true` | `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs` 字段初始化 |
| ② 自动产生规则（压缩侧） | 压缩设置窗「保存到密码库」卡片 | 硬编码 `_autoGenerateRules = true` | 同文件字段初始化 |
| ② 自动产生规则（对话框侧） | 密码对话框 | XAML 硬编码 `IsChecked="True"` | `src/MantisZip.UI.Avalonia/Views/PasswordDialog.axaml` |
| ③ 更新符合规则 | 压缩设置窗（密码库模式） | 就是 ② 那个字段的另一面文案 | `SaveCheckLabel` 按 `IsPasswordLibraryMode` 切换 |
| ④ 永久保存到密码库 | 密码对话框 | XAML 硬编码 `IsChecked="False"` | `src/MantisZip.UI.Avalonia/Views/PasswordDialog.axaml` |

补充事实：

- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` 现有密码相关字段只有 `ZipEncryptionMethod`、`SevenZipEncryptHeaders`、`ShowPasswordMatchNotification`、`PasswordRevealByDefault` —— **四项新默认值一个都没有**。
- `src/MantisZip.UI.Avalonia/Resources/Defaults/settings.json`（出厂默认，117 键）同样没有。
- **③ 不是独立开关**：压缩设置窗里「更新匹配规则」和「保存到密码库」共用同一个 `CheckBox`，文案由 `SaveCheckLabel` 按当前密码模式切换。所以 ③ 的「默认勾选」实际就是让 `SaveToLibrary` 默认为 `true`，与 ② 的字段是两个不同的开关但同一张卡片。

## 方案

### 1. 设置模型

`AppSettings` 新增四个属性（`public` 才有 setter、才能被源生成 JSON 序列化）：

```csharp
public string PasswordModeDefault { get; set; } = "library";   // "library" | "new"
public bool AutoGenerateRulesByDefault { get; set; } = true;
public bool UpdateMatchingRulesByDefault { get; set; } = true;
public bool SavePermanentlyByDefault { get; set; } = false;
```

默认值刻意与今天的硬编码行为**逐项一致**（`library` / `true` / `true` / `false`）⇒ 没改过设置的老用户升级后行为不变。

- `PasswordModeDefault` 加一处归一化：读到既不是 `library` 也不是 `new` 的值时回落到 `library`（沿用 `MigrateAdaptiveOverridesDefaults` 那类兜底写法），避免手改 settings.json 写错后整页初始化失效。
- `AppSettingsJsonContext.cs` 不用改（源生成跟着类走）。
- 出厂 `settings.json` 补四个键，值与上面一致。

### 2. 设置页（密码选项卡）

在已有的「密码选项」卡片下方**再加一张同款 `Border` 卡片**（`ThemeSurfaceBgBrush` + `ThemeBorderBrush` + `BorderRadius` + `SpacingSmThk` 内边距），标题 + 一行说明 + 四个控件：

| 控件 | 形态 | 绑定 |
|---|---|---|
| 默认密码模式 | **`ComboBox` 两档**（`密码库` / `新密码`） | `SelectedPasswordModeDefaultOption` |
| 自动产生规则 | `CheckBox` | `AutoGenerateRulesByDefault` |
| 更新匹配规则 | `CheckBox` | `UpdateMatchingRulesByDefault` |
| 永久保存到密码库 | `CheckBox` | `SavePermanentlyByDefault` |

- **必须用 `ComboBox` 不用 `RadioButton`**：`SettingsWindow.axaml` 里 `RadioButton` 出现 0 次、`ComboBox` 61 次，该页的既有语言就是下拉；压缩设置窗那边才是单选按钮，两边形态不同是有意的，代码注释里写清楚。
- 文案走 **VM 属性**（`public string XxxText => LocalizationManager.T(...)`），**不要**用 `LocalizedStrings[...]` —— 该 axaml 里 `LocalizedStrings[` 出现 0 次，101 处全是 `Text="{Binding XxxText}"`；新属性要在 `OnCultureChanged` 的集中 `OnPropertyChanged` 块里登记。
- 加载 / 保存照抄 `ShowPasswordMatchNotification` 那一对位置（构造末尾赋字段、`Save()` 里回写）。

### 3. 消费端

**`CompressSettingsViewModel`**：默认值加载块（构造末尾那个 `try { var settings = AppSettings.Load(); ... } catch { }`）里加三行：

```csharp
IsPasswordLibraryMode  = !string.Equals(settings.PasswordModeDefault, "new", StringComparison.OrdinalIgnoreCase);
AutoGenerateRules      = settings.AutoGenerateRulesByDefault;
SaveToLibrary          = settings.UpdateMatchingRulesByDefault;
```

- 赋值顺序在 `UpdateAutoRules()` 之前 —— 那里已经有一条「必须在 SelectedPaths 填充后调用」的注释，本次不要动它的位置。
- 注意 `OnPasswordChanged`（用户手动敲密码）会强制 `IsPasswordLibraryMode = false`，这是**运行期行为、不是默认值**，保持原样。

**`PasswordDialog`**：构造里按设置初始化两个 checkbox，再同步 `PatternsTextBox.IsReadOnly` 与自动规则文本。

### 4. 三语文案

新增约 5–6 个 key（四个标签 + 卡片标题 + 一行说明）。落到 `strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json`，插在文件头 `{` 之后第一行，保持 **UTF-8 with BOM + CRLF + 2 空格**。

四个选项的**既有文案可以复用**（`Compress_Pwd_Library`、`Compress_Pwd_NewPassword`、`Compress_Pwd_AutoRules`、`Compress_Pwd_UpdateRules`、`Password_SavePermanently`），真正新增的是卡片标题与说明行。

## 关键约束（实施前必读）

### 1. `settingswiring` 守卫会卡住「只做界面」

`pwsh -File scripts\verify-repo.ps1 -Task guards` 的 `settingswiring` 检查把 `SettingsWindowViewModel.cs` **排除在「消费者」之外**。所以「加字段 + 加设置页 + 加 VM 读写」三件套会被直接判成死开关：

> `AppSettings.Xxx 有持久化与设置窗口读写，但 src/ 里没有任何运行时消费者（死开关）`

四项必须在 `CompressSettingsViewModel` 与 `PasswordDialog.axaml.cs` 里**真的被读**，本次方案天然满足 —— 但如果实施时想「先只加设置页、消费端下轮再做」，守卫会立刻红。

### 2. 密码对话框的初始化时序陷阱

`PasswordDialog.axaml` 里两个 checkbox 的 `IsChecked` 是硬编码的，`InitializeComponent()` 期间就会触发 `IsCheckedChanged`，而 handler 会去读写 `PatternsTextBox` 并调 `RefreshAutoRules()` —— 此时控件树可能还没建完。

所以：**在 `InitializeComponent()` 之后显式赋值**，不要指望改 XAML 初始值。同理，`PasswordSelector_SelectionChanged` 里有「选中已存条目就强制勾上永久保存」的逻辑，初始化要放在选中逻辑之前，且不能与之打架（用户手动选中条目时仍以选中逻辑为准）。

### 3. 「更新匹配规则」与「保存到密码库」共用一个 checkbox

见「现状核实」补充事实。设置页两个开关要分别对应到 `SaveToLibrary` 与 `AutoGenerateRules` 两个字段，别写成同一个。

### 4. 临时修改不回写

设置窗口的保存路径只写四个新属性；`CompressSettingsViewModel` 与 `PasswordDialog` 只读不写。issue 的第 4 条验收标准就是钉这个。

## 涉及文件

- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（+4 属性 +1 归一化）
- `src/MantisZip.UI.Avalonia/Resources/Defaults/settings.json`（+4 键）
- `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`（字段 / 选项集合 / 文案属性 / 加载 / 保存 / 文化切换通知）
- `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml`（密码选项卡第二张卡片）
- `src/MantisZip.UI.Avalonia/ViewModels/CompressSettingsViewModel.cs`（3 行默认值读取）
- `src/MantisZip.UI.Avalonia/Views/PasswordDialog.axaml.cs`（构造末尾按设置初始化，XAML 不动）
- `src/MantisZip.UI.Avalonia/Localization/strings.{zh-CN,en,zh-TW}.json`（+5~6 key）
- `tests/MantisZip.UI.Avalonia.Tests/PasswordDefaultsSettingsTests.cs`（新建）

## 验证

- `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` → 0 错误
- `-Task i18n` → 三语 key 集合一致、占位符一致
- `-Task guards` → 12 项全过（重点是 `settingswiring` 不报死开关）
- `-Task test -Target ui-tests` → 通过，含新增用例
- 新增用例覆盖四点：
  ① 出厂 `settings.json` 含四个新键且值与 `AppSettings` 初值一致（`AppSettingsDefaultSettingsJsonTests` 的 `EveryKey_MapsToRealProperty` 会顺带覆盖键 → 属性映射）；
  ② `new CompressSettingsViewModel(...)` 在沙箱设置里写好 `PasswordModeDefault = "new"` 后，`IsPasswordLibraryMode == false`；
  ③ `PasswordDialog` 构造后两个 checkbox 的初值跟随设置（headless 可构造 Window）；
  ④ 窗内改 `IsPasswordLibraryMode` / `AutoGenerateRules` 不会写回 `AppSettings`。
- 手工抽查：改动前的老配置（无这四个键）加载后四项仍是 `library / true / true / false`。

## 边界

- 不做「密码强度默认级别」「密码长度默认值」等其它密码设置项。
- 不改密码对话框的既有交互（显示切换、匹配提示、选中已存条目时的强制勾选）。
- 不改 `MigrateAdaptiveOverridesDefaults` 之外的迁移逻辑；新字段缺键时保留字段初值，不做版本迁移。
- 不引入 CLI 侧开关。
