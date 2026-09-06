# Phase 6: Avalonia 样式统一与视觉打磨 — 实现计划

> **分支**: `avalonia-port`
> **For agentic workers**: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal**: 给 Avalonia 版 MantisZip 所有控件添加统一圆角 6px + hover 过渡动画 0.15s + 输入框焦点高亮 + 对话框间距统一。

**Architecture**: 所有样式修改集中在 `App.axaml` 中的全局 Style Selector 上。主题文件 `ThemeLight.axaml` / `ThemeDark.axaml` 补齐缺失的 Brush 资源。各对话框只改 `Padding` 属性，不动布局结构。

**Tech Stack**: Avalonia UI, XAML Styles, Theme ResourceDictionary

**参考文件**: `docs/superpowers/specs/2026-06-17-avalonia-style-unification-design.md`

**创建日期**: 2026-06-17

---

## 文件总览

### 修改的文件

| 文件 | 内容 |
|------|------|
| `src/MantisZip.UI.Avalonia/Themes/ThemeLight.axaml` | 添加 `Theme_` 前缀 Brush 别名 + 补充缺失颜色资源 |
| `src/MantisZip.UI.Avalonia/Themes/ThemeDark.axaml` | 同步 Light 的颜色资源覆盖范围 |
| `src/MantisZip.UI.Avalonia/App.axaml` | 所有控件样式增强（CornerRadius 4→6、Transitions、焦点） |
| `src/MantisZip.UI.Avalonia/Dialogs/AboutWindow.axaml` | Padding 统一为 16 |
| `src/MantisZip.UI.Avalonia/Dialogs/CommentDialog.axaml` | Padding 统一为 16 |
| `src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml` | Padding 20,7 → 16 |
| `src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml` | Padding 20,7 → 16 |
| `src/MantisZip.UI.Avalonia/Dialogs/PasswordManagerWindow.axaml` | Window Padding 10 → 16 |
| `src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml` | 保持 16,6（离目标差一个水平 6→16? 确认） |
| `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml` | 内容区 Padding 统一 |
| `src/MantisZip.UI.Avalonia/Views/PasswordDialog.axaml` | Padding 16,6 → 16 |

### 不做修改的文件

- `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` — 外层 Border 的 Padding 是布局相关，不做统一改动
- `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml` — 预览面板是嵌入控件，Padding 由容器决定
- `src/MantisZip.UI.Avalonia/Controls/InfoPanel.axaml` — 内部布局控件，保持现状
- `tests/`、`MantisZip.Core/` — 不涉及样式改动

---

## Task 1: 补充 Light/Dark 主题资源

**文件**: `Themes/ThemeLight.axaml`、`Themes/ThemeDark.axaml`

当前 Light 和 Dark 主题有 12 个 Color + 17 个 Brush。需要补充以下缺失资源以满足所有控件绑定：

### 缺失的 Brush（需要在 Light 和 Dark 中都添加）

| Brush Key | Light 值 | Dark 值 | 用途 |
|-----------|----------|---------|------|
| `ThemeFocusBrush` | `#FF0078D4` (Accent) | `#FF4BA0FF` | 输入框焦点边框高亮 |

> **策略**: 直接用现有的 `ThemeAccentBrush` 作为焦点色，不需要单独的颜色。焦点样式直接在 App.axaml 中使用 `{DynamicResource ThemeAccentBrush}`。

### Light 主题的变化

保持原有颜色不变，只添加 `Theme_` 前缀的 Brush 别名。但当前命名已经是 `ThemeXxxBrush`，其实和 `Theme_Xxx` 只差一个下划线。考虑到现有绑定量大，**不重命名，只添加缺失资源**。

**实际需要添加的内容**（因为 DataGridRow:pointerover 需要 hover 色用的 Brush）：

Light 和 Dark 都已经有 `ThemeButtonHoverBrush` 了。所以实际上**没有新增资源的必要**。

→ 简化方案: **Task 1 跳过**。在 Task 2 的实现中直接引用现有的 Brush 键。

---

## Task 2: App.axaml 控件样式增强

**文件**: `App.axaml` (全部在 `<Application.Styles>` 内修改)

### 2a: Button — CornerRadius + Transitions

```xml
<!-- Button -->
<Style Selector="Button">
  <Setter Property="Background" Value="{DynamicResource ThemeButtonBgBrush}" />
  <Setter Property="Foreground" Value="{DynamicResource ThemeTextPrimaryBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeBorderBrush}" />
  <Setter Property="CornerRadius" Value="6" />
  <Setter Property="Padding" Value="8,4" />
  <Setter Property="MinHeight" Value="26" />
  <Setter Property="Transitions">
    <Setter.Value>
      <Transitions>
        <BrushTransition Property="Background" Duration="0:0:0.15" />
        <BrushTransition Property="Foreground" Duration="0:0:0.15" />
      </Transitions>
    </Setter.Value>
  </Setter>
</Style>
```

- [x] **2a.1**: 修改 `<Style Selector="Button">`：`CornerRadius` 4→6，添加 `Transitions`
- [x] **2a.2**: 构建验证

### 2b: ToggleButton — CornerRadius + Transitions

```xml
<Style Selector="ToggleButton">
  <Setter Property="Background" Value="{DynamicResource ThemeButtonBgBrush}" />
  <Setter Property="Foreground" Value="{DynamicResource ThemeTextPrimaryBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeBorderBrush}" />
  <Setter Property="CornerRadius" Value="6" />
  <Setter Property="Padding" Value="8,4" />
  <Setter Property="MinHeight" Value="26" />
  <Setter Property="Transitions">
    <Setter.Value>
      <Transitions>
        <BrushTransition Property="Background" Duration="0:0:0.15" />
        <BrushTransition Property="Foreground" Duration="0:0:0.15" />
      </Transitions>
    </Setter.Value>
  </Setter>
</Style>
```

- [x] **2b.1**: 修改 `<Style Selector="ToggleButton">`：CornerRadius 4→6，添加 Transitions
- [x] **2b.2**: 构建验证

### 2c: TextBox — CornerRadius + Padding + Transitions + focus

```xml
<Style Selector="TextBox">
  <Setter Property="Background" Value="{DynamicResource ThemeSurfaceBgBrush}" />
  <Setter Property="Foreground" Value="{DynamicResource ThemeTextPrimaryBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeBorderBrush}" />
  <Setter Property="CornerRadius" Value="6" />
  <Setter Property="Padding" Value="8,8" />
  <Setter Property="Transitions">
    <Setter.Value>
      <Transitions>
        <BrushTransition Property="BorderBrush" Duration="0:0:0.15" />
      </Transitions>
    </Setter.Value>
  </Setter>
</Style>
<Style Selector="TextBox:focus-within">
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeAccentBrush}" />
</Style>
```

- [x] **2c.1**: 修改 TextBox Style：CornerRadius 4→6，Padding 改为 8,8，添加 Transitions
- [x] **2c.2**: 添加 `TextBox:focus-within` 焦点边框变色
- [x] **2c.3**: 构建验证

### 2d: ComboBox — CornerRadius + Transitions + focus

```xml
<Style Selector="ComboBox">
  <Setter Property="Background" Value="{DynamicResource ThemeComboBoxBgBrush}" />
  <Setter Property="Foreground" Value="{DynamicResource ThemeTextPrimaryBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeComboBoxBorderBrush}" />
  <Setter Property="CornerRadius" Value="6" />
  <Setter Property="Padding" Value="8,6" />
  <Setter Property="Transitions">
    <Setter.Value>
      <Transitions>
        <BrushTransition Property="BorderBrush" Duration="0:0:0.15" />
      </Transitions>
    </Setter.Value>
  </Setter>
</Style>
<Style Selector="ComboBox:focus-within">
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeAccentBrush}" />
</Style>
```

- [x] **2d.1**: 修改 ComboBox Style：CornerRadius 4→6，Padding 8,6，添加 Transitions ✅（Padding 在后续修复中补上）
- [x] **2d.2**: 添加 `ComboBox:focus-within` 焦点边框变色
- [x] **2d.3**: 构建验证

### 2e: TabItem — CornerRadius + selected 颜色

```xml
<Style Selector="TabItem">
  <Setter Property="Background" Value="{DynamicResource ThemeTabHeaderBgBrush}" />
  <Setter Property="Foreground" Value="{DynamicResource ThemeTabHeaderFgBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeBorderBrush}" />
  <Setter Property="CornerRadius" Value="0" />
  <!-- CornerRadius on TabItem may not work in Avalonia — keep as 0 if not supported -->
</Style>
<Style Selector="TabItem:selected">
  <Setter Property="Background" Value="{DynamicResource ThemeAccentBrush}" />
  <Setter Property="Foreground" Value="White" />
</Style>
```

> **Avalonia 兼容性**: TabItem 可能不支持 `CornerRadius`。如果构建或运行报错，移除该 Setter，只改 :selected 样式。

- [x] **2e.1**: TabItem :selected 改为主题色背景 + 白色文字
- [x] **2e.2**: 构建验证，确认 TabItem 不支持 CornerRadius（保持 0）

### 2f: DataGrid — ColumnHeader 颜色 + Row 悬停

```xml
<Style Selector="DataGridColumnHeader">
  <Setter Property="Background" Value="{DynamicResource ThemeAccentBrush}" />
  <Setter Property="Foreground" Value="White" />
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeBorderBrush}" />
</Style>
```

添加行悬停样式：
```xml
<Style Selector="DataGridRow:pointerover /template/ DataGridCell">
  <Setter Property="Background" Value="{DynamicResource ThemeButtonHoverBrush}" />
</Style>
```

- [x] **2f.1**: DataGridColumnHeader 背景改为 `ThemeAccentBrush` + 白色文字
- [x] **2f.2**: 添加 `DataGridRow:pointerover` 样式
- [x] **2f.3**: 构建验证

### 2g: ProgressBar — CornerRadius

```xml
<Style Selector="ProgressBar">
  <Setter Property="Foreground" Value="{DynamicResource ThemeAccentBrush}" />
  <Setter Property="Background" Value="{DynamicResource ThemeBorderBrush}" />
  <Setter Property="CornerRadius" Value="4" />
</Style>
```

- [x] **2g.1**: 添加 ProgressBar CornerRadius="4"
- [x] **2g.2**: 构建验证

### 2h: ScrollBar Thumb — CornerRadius

```xml
<Style Selector="ScrollBar Thumb">
  <Setter Property="CornerRadius" Value="4" />
</Style>
```

- [x] **2h.1**: 添加 ScrollBar Thumb CornerRadius
- [x] **2h.2**: 构建验证

### 2i: 完整构建验证

- [x] **2i.1**: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 0 错误 0 警告
- [x] **2i.2**: `dotnet test tests/MantisZip.UI.Avalonia.Tests/` 全部通过（35 passed, 2 skipped, 0 failed）

---

## Task 3: 对话框间距统一

### 当前 Padding 值

| 文件 | 当前 | 改为 | 说明 |
|------|------|------|------|
| `Dialogs/AboutWindow.axaml` | `Padding="8,6"` (Window) | `Padding="16"` | 统一为 16 |
| `Dialogs/CompressSettingsWindow.axaml` | `Padding="20,7"` (x2) | `Padding="16"` | 统一为 16 |
| `Dialogs/ExtractSettingsWindow.axaml` | `Padding="20,7"` (x2) | `Padding="16"` | 统一为 16 |
| `Dialogs/PasswordManagerWindow.axaml` | `Padding="10"` (Window) | `Padding="16"` | 统一为 16 |
| `Dialogs/ProgressWindow.axaml` | `Padding="16,6"` | `Padding="16"` | 水平16已有，垂直改为16 |
| `Views/SettingsWindow.axaml` | `Padding="8,2"` (TabControl) | `Padding="16"` | TabControl 外层 |
| `Views/PasswordDialog.axaml` | `Padding="16,6"` | `Padding="16"` | 统一 |

### 注意

有些对话框内的 `Padding` 是作用于面板或容器（如 StackPanel、Border），不是窗口级别的。只修改**窗口顶级容器**的 Padding。内部容器的 Padding 保留不动，以保证内部控件布局不受影响。

- [x] **3.1**: `AboutWindow.axaml` 窗口 Padding 改为 16
- [x] **3.2**: `CompressSettingsWindow.axaml` Grid Margin 改为 16
- [x] **3.3**: `ExtractSettingsWindow.axaml` Grid Margin 改为 16
- [x] **3.4**: `PasswordManagerWindow.axaml` 窗口 Padding 改为 16
- [x] **3.5**: `ProgressWindow.axaml` Grid Margin 改为 16
- [x] **3.6**: `SettingsWindow.axaml` TabControl 外层 Grid Margin 改为 16
- [x] **3.7**: `PasswordDialog.axaml` StackPanel Margin 改为 16
- [x] **3.8**: `CommentDialog.axaml` 窗口 Padding 改为 16
- [x] **3.9**: 构建验证

---

## Task 4: 最终验证

- [x] **4.1**: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` — 0 errors, 0 warnings ✅
- [x] **4.2**: `dotnet test tests/MantisZip.UI.Avalonia.Tests/` — 全部通过（35 passed, 2 skipped, 0 failed）✅
- [ ] **4.3**: 手动验收：按钮悬停有过渡动画（不再是瞬间变色）
- [ ] **4.4**: 手动验收：输入框获得焦点时边框变为主题色
- [ ] **4.5**: 手动验收：DataGrid 列头颜色改变
- [ ] **4.6**: 手动验收：亮/暗切换后所有控件正确显示
- [ ] **4.7**: 手动验收：所有对话框间距一致

---

## 不做的事情

- 不改 MainWindow 布局 Padding（布局依赖特定值）
- 不动 TabControl 的 TabStripPlacement 或内容布局
- 不换字体
- 不重写 ControlTemplate
- 不改 Core 层、WPF 项目
