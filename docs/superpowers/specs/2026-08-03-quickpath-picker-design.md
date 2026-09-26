# QuickPathPicker 自包含可复用路径选择控件 — 设计

日期：2026-08-03
分支：AvaloniaAlpha
范围：`MantisZip.UI.Avalonia`（Avalonia 主力版）

## 背景与动机

当前 **三处路径选择场景** 重复实现了同一套「快捷速选 + 浏览」模式：

1. `CompressSettingsWindow` — 输出目录 TextBox + ⭐🕐🪟📁 + 3 个单 Tab 浮层 + 手写 light-dismiss
2. `ExtractSettingsWindow` — 解压目标 TextBox + ⭐🕐🪟📁 + 3 个单 Tab 浮层 + 手写 light-dismiss
3. `SettingsWindow`(默认路径优先级) — 自定义路径 TextBox + 📁（现有单目录按钮）

三处重复：3 按钮行、3 个单 Tab `QuickPathControl`、3 个 Popup、`PointerPressed(tunnel)` 手动 light-dismiss、选中写回——全部雷同。抽成自包含可复用控件，收敛重复，后续再有路径速选场景可一行集成。

## 目标

一个自包含可复用控件 **`QuickPathPicker`**，封装：路径输入框（AutoCompleteBox）+ ⭐🕐🪟 三个单 Tab 快捷浮层 + 📁 浏览按钮 + 内置 light-dismiss。宿主只声明控件并双向绑定路径，浏览差异通过注入委托解决。

## 非目标（Out of scope）

- **不处理文件名**。浏览选到文件 → 控件自动收敛为父目录；文件名属于「其它控件」职责（如压缩窗口的保存名框）
- **不含目录树 Tab**（单面板场景一致，无多 Tab）
- 不迁移 WPF / 不动 `QuickPathControl` 既有四 Tab 形态（保留给 CustomFilePickerDialog 使用）

## 视觉布局（两行）

```
┌────────────────────────────────────────────────┐
│  [ 路径输入框 (AutoCompleteBox, 全宽/独占一行) ]     │   ← Row 0
├────────────────────────────────────────────────┤
│   ⭐ 收藏    🕐 历史    🪟 窗口   │        📁浏览  │   ← Row 1（按钮一行）
└────────────────────────────────────────────────┘
```

- **Row 0**：`AutoCompleteBox` 独占一行，占满可用宽度 → 输入可视化空间充足
- **Row 1**：左边 ⭐🕐🪟 三快捷按钮 group（内置分隔），最右 📁 浏览按钮
- 全部用主题资源键：`Background`→`{DynamicResource ThemeSurfaceBgBrush}`，`Foreground`/`BorderBrush` 对应 `ThemeTextPrimaryBrush`/`ThemeBorderBrush`；按钮 `ThemeButtonBgBrush/Hover/Pressed`；行高 `ControlHeightSm`（AGENTS.md 规则 4/5/7）

## 公共 API

```
namespace MantisZip.UI.Avalonia.Controls;

public class QuickPathPicker : UserControl
{
    // ── Path 双向绑定 ──
    public string Path { get; set; }              // StyledProperty<TwoWay>: 宿主 VM 的路径属性

    // ── 📁 浏览动作（可注入委托）──
    // (owner, 当前路径 initialPath) → 新路径或 null；返回的文件路径会被归一化为父目录
    public Func<Window?, string?, Task<string?>>? BrowseAction { get; set; }
                                                  // 默认行为：内置纯目录选择
    // ── 目录归一化（内部）──
    private static string CoerceToDirectory(string picked);
                                                  // Directory.Exists → 原样
                                                  // File.Exists      → Path.GetDirectoryName(parent)
                                                  // 其它            → picked
    // ── 交互方法 ──
    void ShowTab(PathTab tab);                    // 打开对应单 Tab 浮层（内部）
    void CloseAllPopups();                        // light-dismiss 用
}
```

### 浏览默认行为

- `BrowseAction` 为 **null** 时 → 内置 `CustomFilePickerDialog.ShowFolderAsync(owner, 当前 Path)`：纯目录选择
- `BrowseAction` 被注入（如 Compress 的保存 / Extract 带冲突预览）→ 用注入的委托
- **无论浏览返回文件还是目录，进入输入框前统一 `NormalizeToDirectory`**：文件自动取父目录

### 布大小/交互

- 三个 `QuickPathControl`：`SingleTab = Favorites/History/Windows`，各 `ApplySingleTabMode()`
- 三个 Popup：`PlacementTarget` 绑定各自按钮、`Placement="Bottom"`、Width 320 / MaxHeight 420、含内容 Border
- **内置 light-dismiss**：控件 `AddHandler`（`PointerPressedEvent`, Tunnel）先关全部浮层，按钮 Click 再开对应浮层。宿主不再需要 `AddHandler`
- 选中任一来源项 → 写 `Path` + 关闭对应浮层

## 宿主接入

| 窗口 | 替换前 | 替换后 | BrowseAction |
|---|---|---|---|
| `SettingsWindow` | TextBox + 📁（现有） | `<QuickPathPicker Path="{Binding CustomPath}" />` | 默认(null) → 内置目录选择（零配置） |
| `CompressSettingsWindow` | TextBox + 4 按钮 + 3 浮层 + 手写 light-dismiss | `<QuickPathPicker Path="{Binding OutputDirectory}" .../>` | 注入 SaveFile 目录确认（保留宿主 SaveFile 语义） |
| `ExtractSettingsWindow` | TextBox + 4 按钮 + 3 浮层 + 手写 light-dismiss | `<QuickPathPicker Path="{Binding DestinationPath}" .../>` | 注入 ExtractFolder（保留宿主冲突预览语义） |

Compress/Extract 的专属浏览逻辑（SaveFile 带格式、ExtractFolder 带 `_entries`）全部收进各宿主 `BrowseAction` 注入，控件不管。

## 本地化（zh/en 新增 1 key）

⭐🕐🪟 按钮 `ToolTip` **复用现有** `QuickPath_TabFavorites` / `QuickPath_TabHistory` / `QuickPath_TabWindows`（zh/en 均已存在，Compress/Extract 已用）。仅新增 📁 浏览按钮一个 key：

| key | zh | en |
|---|---|---|
| `QuickPath_Browse` | 📁 浏览… | 📁 Browse… |

（AutoCompleteBox 占位符可复用 `QuickPath_SearchPlaceholder` 或 `QuickPath_SelectFolder`，实现时三选一。）

## 风险与取舍

- **迁移风险（Compress/Extract）**：改造会去掉它们手写浮层；浏览差异全在 `BrowseAction` 注入脚本，不覆盖独有属性逻辑
- **AutoCompleteBox 主题**：复用 `CustomFilePicker` 已验证的补全逻辑（历史 + 父目录枚举）；地址栏/列表沿用自定义主题样式键
- **不引入新依赖**：复用现有 `QuickPathControl`、`FavoritePathManager`、`PathHistoryManager`、`CustomFilePickerDialog`、`Geo` 资源，均已在仓库

## 测试

- Avalonia 测试项目有 UI 组件测试能力；控件新增 `QuickPathPicker` 需要校验：Path 双向绑定、BrowseAction 注入、目录归一化、浮层开关。手动验收为主要途径（三家宿主集成后）
- 完整后跑 `dotnet build src\MantisZip.UI.Avalonia` + `dotnet test tests\MantisZip.UI.Avalonia.Tests`