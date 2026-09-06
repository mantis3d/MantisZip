# Phase 6: Avalonia 样式统一与视觉打磨 — 设计规格

> **分支**: `avalonia-port`（从 Phase 5 继续）
> **风格**: Modern Flat — 圆角 6px + 过渡动画 0.15s + 焦点光晕 + 统一间距
> **创建日期**: 2026-06-17
> **状态**: 📋 待实现

---

## 1. 动机

Phase 0–5 完成了 Avalonia 版的功能移植，但各控件的样式分散在 `App.axaml` 中，全局没有统一的圆角/过渡/焦点/间距规范。视觉上显得粗糙：
- 按钮无圆角、hover 瞬间跳变
- 输入框无焦点高亮
- 各对话框间距不统一
- Dark 主题缺失部分控件颜色

本次 Phase 6 专注于**在不改变布局结构**的前提下，通过修改 `App.axaml` 中的全局 `Style` 选择器 + 微调主题文件，给整个应用统一的外观。

---

## 2. 约束

- ❌ 不重新设计颜色调色板（仅使用现有色值，Dark 不新增色值）
- ❌ 不换字体（保持系统 Segoe UI）
- ❌ 不改布局结构（不碰 Grid Row/Column 定义）
- ❌ 不重写控件模板视觉树（只用 Setter 改属性，不写 ControlTemplate）
- ❌ 不改 Core 层、WPF 项目
- ✅ 所有改动限于 `src/MantisZip.UI.Avalonia/`

---

## 3. 颜色系统

### 3.1 当前命名现状

主题文件 (`ThemeLight.axaml` / `ThemeDark.axaml`) 使用驼峰命名如 `ThemeWindowBg`、`ThemeTextPrimary`。AGENTS.md 约定使用 `Theme_` 前缀命名（如 `Theme_WindowBg`）。两者并存，本阶段**仅添加别名不作迁移**：

在主题文件中添加 `Theme_` 前缀的 `SolidColorBrush` 资源指向现有色值。

### 3.2 Dark 主题策略

保持现有 Dark 色值不变。只补充当前缺失的控件色值（确保 Dark 主题下所有控件有对应的颜色资源），保持与 Light 主题的资源键覆盖范围一致。

### 3.3 补充缺失颜色

如果某个控件在当前主题中缺少对应的颜色资源，使用最接近的现有色值补充。

---

## 4. 控件样式统一

所有样式修改在 `App.axaml` 中完成，不涉及各窗口 `.axaml` 文件。

### 4.1 Button / ToggleButton

```xml
<Style Selector="Button, ToggleButton">
  <Setter Property="CornerRadius" Value="6" />
</Style>

<Style Selector="Button:pointerover, ToggleButton:pointerover">
  <!-- Background 已有的 -->
  <Setter Property="Foreground" Value="{DynamicResource ThemeTextPrimaryBrush}" />
  <Style.Animations>
    <Animation Duration="0:0:0.15" FillMode="Forward">
      <KeyFrame Cue="0%">
        <Setter Property="Background" Value="{DynamicResource ThemeButtonBgBrush}" />
      </KeyFrame>
      <KeyFrame Cue="100%">
        <Setter Property="Background" Value="{DynamicResource ThemeButtonHoverBrush}" />
      </KeyFrame>
    </Animation>
  </Style.Animations>
</Style>
```

> **注意**: Avalonia 中 `Transitions` 只在 `StyledElement` 上作为属性。对于 Selector-based Style，使用 `<Style.Animations>` 实现过渡效果。如果 `Transitions` 属性直接在 Style Setter 中可用，优先用 `Transitions`。

实际实现时验证哪种方式在 Avalonia 中工作，优先使用 `Transitions` 属性：
```xml
<Setter Property="Transitions">
  <Setter.Value>
    <Transitions>
      <BrushTransition Property="Background" Duration="0:0:0.15" />
      <BrushTransition Property="Foreground" Duration="0:0:0.15" />
    </Transitions>
  </Setter.Value>
</Setter>
```

### 4.2 ToolbarButton（已存在的样式类）

保持 `Classes="ToolbarButton"` 独立于通用 Button 样式。工具栏按钮保持高度 54，不需要圆角。

### 4.3 TextBox

```xml
<Style Selector="TextBox">
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
```

焦点样式——Avalonia 没有 `:focus` 伪类，使用 `:focus-within`：
```xml
<Style Selector="TextBox:focus-within">
  <Setter Property="BorderBrush" Value="{DynamicResource ThemeAccentBrush}" />
</Style>
```

### 4.4 ComboBox

```xml
<Style Selector="ComboBox">
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

### 4.5 TabControl / TabItem

```xml
<Style Selector="TabItem">
  <Setter Property="CornerRadius" Value="6,6,0,0" />
  <Setter Property="Padding" Value="12,8" />
  <Setter Property="Transitions">
    <Setter.Value>
      <Transitions>
        <BrushTransition Property="Background" Duration="0:0:0.15" />
      </Transitions>
    </Setter.Value>
  </Setter>
</Style>
<Style Selector="TabItem:selected">
  <Setter Property="Background" Value="{DynamicResource ThemeAccentBrush}" />
  <Setter Property="Foreground" Value="White" />
</Style>
<Style Selector="TabItem:pointerover">
  <Setter Property="Background" Value="{DynamicResource ThemeButtonHoverBrush}" />
</Style>
```

### 4.6 DataGrid

```xml
<Style Selector="DataGridColumnHeader">
  <Setter Property="CornerRadius" Value="6,6,0,0" />
  <Setter Property="Background" Value="{DynamicResource ThemeAccentBrush}" />
  <Setter Property="Foreground" Value="White" />
</Style>
<Style Selector="DataGridRow:selected /template/ DataGridCell">
  <Setter Property="Background" Value="{DynamicResource ThemeListSelectedBrush}" />
</Style>
<Style Selector="DataGridRow:pointerover /template/ DataGridCell">
  <Setter Property="Background" Value="{DynamicResource ThemeButtonHoverBrush}" Opacity="0.5" />
</Style>
```

### 4.7 ProgressBar

```xml
<Style Selector="ProgressBar">
  <Setter Property="CornerRadius" Value="4" />
</Style>
```

### 4.8 ScrollBar

```xml
<Style Selector="ScrollBar Thumb">
  <Setter Property="CornerRadius" Value="4" />
</Style>
```

### 4.9 Border / Panel

统一边框颜色和圆角，不覆盖默认值。

### 4.10 Menu / MenuItem

保持现有样式（已有 hover/foreground 修正）。

---

## 5. 间距统一

### 5.1 对话框 Padding

| 文件 | 当前 | 目标 |
|------|------|------|
| Dialogs/AboutWindow.axaml | 未知 | Padding="16" |
| Dialogs/CommentDialog.axaml | 已知已有 | 维持或统一为 16 |
| Dialogs/CompressSettingsWindow.axaml | 未知 | Padding="16" |
| Dialogs/ExtractSettingsWindow.axaml | 未知 | Padding="16" |
| Dialogs/PasswordManagerWindow.axaml | 未知 | Padding="16" |
| Dialogs/ProgressWindow.axaml | 未知 | Padding="16" |
| Views/SettingsWindow.axaml | 未知 | Padding="16" |
| Views/PasswordDialog.axaml | 未知 | Padding="16" |

### 5.2 控件间距

| 场景 | 间距值 |
|------|--------|
| StackPanel 子元素间距 | Spacing="8" |
| GroupBox 内部 | Margin/Padding 12 |
| 对话框内部区域间距 | 12 |
| 工具栏按钮间距 | 4（已统一） |

> **实现策略**: 不盲目替换全部已有间距值。只修改明显不一致或不符合规范的窗口。做最小化改动。

---

## 6. 工作项清单

### Task 1: 主题文件补齐
- [ ] 1.1 `ThemeLight.axaml`: 添加 `Theme_` 前缀 Brush 别名（作为已有颜色的引用）
- [ ] 1.2 `ThemeDark.axaml`: 同步添加与 Light 相同集合的颜色资源键
- [ ] 1.3 验证: 构建通过，亮/暗切换正常

### Task 2: App.axaml 控件样式增强
- [ ] 2.1 Button 添加 CornerRadius + Transitions
- [ ] 2.2 TextBox 添加 CornerRadius + Padding + focus-within + Transitions
- [ ] 2.3 ComboBox 添加 CornerRadius + focus-within + Transitions
- [ ] 2.4 TabItem 添加 CornerRadius + selected/pointerover 样式
- [ ] 2.5 DataGridColumnHeader 添加 CornerRadius + 颜色
- [ ] 2.6 DataGridRow 添加 hover/alternating 样式
- [ ] 2.7 ProgressBar 添加 CornerRadius
- [ ] 2.8 ScrollBar Thumb 添加 CornerRadius
- [ ] 2.9 验证: 构建通过，hover 有过渡动画，焦点有高亮

### Task 3: 对话框间距统一
- [ ] 3.1 AboutWindow Padding 统一为 16
- [ ] 3.2 CommentDialog Padding 统一为 16
- [ ] 3.3 CompressSettingsWindow Padding 统一为 16
- [ ] 3.4 ExtractSettingsWindow Padding 统一为 16
- [ ] 3.5 PasswordManagerWindow Padding 统一为 16
- [ ] 3.6 ProgressWindow Padding 统一为 16
- [ ] 3.7 SettingsWindow Padding 统一为 16
- [ ] 3.8 PasswordDialog Padding 统一为 16
- [ ] 3.9 验证: 所有对话框间距一致

### Task 4: 验收
- [ ] 4.1 `dotnet build` 0 错误
- [ ] 4.2 `dotnet test` 全部通过
- [ ] 4.3 亮/暗主题切换后所有控件颜色正确
- [ ] 4.4 按钮 hover 有平滑过渡
- [ ] 4.5 输入框焦点有主题色边框

---

## 7. 不做的事情

- 不改布局结构（Grid Row/Column 定义、MainWindow 分区比例）
- 不改字体（保持系统 Segoe UI）
- 不重写 ControlTemplate（所有改动仅用 Setter）
- 不改为新颜色调色板
- 不改 Core 层、WPF 项目、测试项目（测试项目无样式相关变更时不动）

---

## 8. 边界情况

1. **Avalonia Transitions 支持**: Selector-based Style 中 `Transitions` 属性直接作为 Setter 使用。如果无效，回退为 `Style.Animations` + 关键帧动画。

2. **CornerRadius 对不同控件的影响**: 某些控件（如 ProgressBar、ScrollBar）的 CornerRadius 可能需要特定的控件模板支持。如果 Setter 不生效，不进一步深究。

3. **Dialog Padding 冲突**: 有些对话框内部有 Grid 或 DockPanel 依赖 Padding 进行布局。修改 Padding 后需要视觉验证控件没有位移或裁剪。

4. **Color 命名冲突**: 添加 `Theme_` 前缀别名时确保不与现有资源键冲突。新别名使用 `<SolidColorBrush x:Key="Theme_WindowBgBrush" Color="{StaticResource ThemeWindowBg}" />` 方式。

5. **TabItem CornerRadius 支持**: Avalonia TabItem 可能不支持 `CornerRadius` 属性。如果 Setter 不生效，移除该样式。
