# 浮动/停靠预览面板设计文档

> 日期：2026-07-16
> 状态：草稿
> 对应仓库：MantisZip

## 1. 目标

实现 Photoshop 风格的可浮动/可停靠预览面板：
- 预览面板可以从主窗口拖出成为独立浮动窗口
- 可以拖回主窗口边缘自动吸附停靠
- 支持停靠位置切换（底部/目录树下/文件列表下/文件列表右侧）

## 2. 设计原则

1. **PreviewPanel 作为单一实例**：不创建两个 PreviewPanel 实例，浮动时移动 Control 的 Parent
2. **PreviewViewModel 不变**：ViewModel 完全与显示容器解耦，不感知浮动/停靠状态
3. **渐进式交付**：分 3 个 Phase 实现，每个 Phase 独立可用
4. **零额外第三方依赖**：完全基于 Avalonia 原生能力实现

## 3. 架构

### 3.1 核心概念：PreviewHost

引入 `PreviewHost` 作为预览面板的容器管理层，接管当前 MainWindow 直接嵌入 PreviewPanel 的做法。

```
MainWindow Grid 内容区
  └─ PreviewHost (新控件，管理状态)
       ├─ [Docked]        → PreviewPanel 嵌入 Grid 布局
       ├─ [Floating]      → PreviewPanel 移入 PreviewFloatingWindow
       └─ [Docking]       → DockingOverlay 吸附指示器 (Phase 3)
```

PreviewHost 不继承任何复杂基类，就是一个普通的 `ContentControl` 或辅助逻辑类，由 MainWindow 直接使用。

### 3.2 状态枚举

```csharp
public enum PreviewDockState
{
    Docked,    // 停靠在 MainWindow 内
    Floating,  // 浮动独立窗口
}
```

### 3.3 组件清单

| 组件 | 类型 | 职责 |
|------|------|------|
| `PreviewHost` | 逻辑类 / 附加行为 | 管理 PreviewPanel 的 Parent 切换、状态持久化 |
| `PreviewFloatingWindow` | `Window` | 浮动窗口容器，持有 PreviewPanel，带标题栏 |
| `DockingManager` | 静态帮助类 | 边缘吸附检测、DockingOverlay 管理 (Phase 3) |
| `DockingOverlay` | `Window` (透明覆层) | 吸附位置高亮指示器 (Phase 3) |

### 3.4 不修改的部分

- **PreviewViewModel**：完全不变，继续作为 MainWindowViewModel.Preview 属性
- **PreviewPanel.axaml**：UI 布局不变
- **PreviewPanel.axaml.cs**：code-behind 不变（视口检测、DataGrid 列设置等）
- **MainWindowViewModel**：只加 `PreviewDockState` 属性和一个 `ToggleFloatPreviewCommand`

## 4. Phase 1：基础浮动（Button 切换）

### 4.1 功能

- 点击「浮动预览」按钮，预览面板弹出为独立窗口
- 关闭浮动窗口，预览面板回到 MainWindow
- 浮动窗口位置/大小被记忆

### 4.2 AppSettings 新增字段

```csharp
// 在 Models/AppSettings.cs 添加
public string PreviewDockState { get; set; } = "Docked";  // "Docked" | "Floating"
public double PreviewFloatingX { get; set; } = -1;         // -1 = 未设置
public double PreviewFloatingY { get; set; } = -1;
public double PreviewFloatingWidth { get; set; } = 600;
public double PreviewFloatingHeight { get; set; } = 400;
// Phase 2 添加
public int PreviewDockPosition { get; set; } = 2;          // 1=底部 2=目录树下 3=文件列表下 4=文件列表右侧
```

### 4.3 PreviewFloatingWindow

新文件：`Views/PreviewFloatingWindow.axaml` + `Views/PreviewFloatingWindow.axaml.cs`

```axaml
<Window xmlns="https://github.com/avaloniaui"
        x:Class="MantisZip.UI.Avalonia.Views.PreviewFloatingWindow"
        Title="预览"
        Width="600" Height="400"
        MinWidth="300" MinHeight="200"
        ShowInTaskbar="false"
        WindowStartupLocation="Manual"
        Background="{DynamicResource ThemeWindowBgBrush}">
  <Grid RowDefinitions="Auto,*">
    <!-- 标题栏：显示当前文件名 + 停靠按钮 -->
    <Border Grid.Row="0" Padding="8,4"
            Background="{DynamicResource ThemeHeaderBgBrush}">
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Grid.Column="0"
                   Text="{Binding HeaderText}"
                   Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                   VerticalAlignment="Center" />
        <Button Grid.Column="1"
                Content="📌 停靠"
                Command="{Binding $parent[Window].Tag.DockCommand}" />
      </Grid>
    </Border>
    <!-- PreviewPanel 内容区 (由代码动态填充) -->
    <ContentControl Grid.Row="1" x:Name="PreviewContent" />
  </Grid>
</Window>
```

关键行为：
- `PreviewFloatingWindow` 不创建自己的 PreviewPanel
- 代码中通过 `PreviewContent.Content = previewPanel` 从 MainWindow 移入
- `Closing` 事件中执行「停靠回 MainWindow」逻辑
- `PositionChanged` / `Resized` 事件中保存位置到 AppSettings

### 4.4 MainWindow 变更

**MainWindow.axaml**：将 PreviewPanel 的嵌入方式改为通过 PreviewHost 中间层。

当前：
```axaml
<!-- Preview area -->
<views:PreviewPanel Grid.Column="4"
                    DataContext="{Binding Preview}" />
```

改为（Phase 1 简化版——直接用条件控制可见性）：
```axaml
<!-- Preview area -->
<views:PreviewPanel Grid.Column="4"
                    DataContext="{Binding Preview}"
                    IsVisible="{Binding IsPreviewDocked}" />
```

并在 MainWindow 工具栏加一个浮动切换按钮：
```axaml
<Button Command="{Binding ToggleFloatPreviewCommand}"
        ToolTip.Tip="浮动预览">
  <TextBlock Text="🔲" />
</Button>
```

**MainWindow.axaml.cs**：添加浮动窗口管理逻辑。

### 4.5 生命周期

```
用户点击「浮动」按钮
  → MainWindowViewModel.ToggleFloatPreviewCommand
  → MainWindow 代码隐藏：
      1. 记录 PreviewPanel 当前状态
      2. 创建 PreviewFloatingWindow 实例
      3. PreviewFloatingWindow.PreviewContent.Content = this.PreviewPanel (从 Grid 移除)
      4. PreviewFloatingWindow.DataContext = PreviewViewModel (同一实例)
      5. PreviewFloatingWindow.Show()
      6. MainWindowViewModel.IsPreviewDocked = false

用户关闭浮动窗口 / 点击「停靠」按钮
  → PreviewFloatingWindow.Closing / DockCommand
  → 代码隐藏：
      1. 把 PreviewPanel 从 PreviewFloatingWindow 移回 MainWindow Grid
      2. PreviewFloatingWindow.Close()
      3. MainWindowViewModel.IsPreviewDocked = true
      4. 保存浮动窗口位置到 AppSettings
```

### 4.6 注意点

- **主题共享**：PreviewFloatingWindow 的 `RequestedThemeVariant` 必须与 MainWindow 一致。通过 `window.RequestedThemeVariant = MainWindow.RequestedThemeVariant` 同步
- **资源合并**：浮动窗口需要合并 `Themes/ThemeLight.axaml` / `ThemeDark.axaml` 等主题资源
- **所有者为 MainWindow**：`PreviewFloatingWindow.Owner = MainWindow`，使浮动窗口在任务栏不单独显示，且随主窗口最小化/关闭

## 5. Phase 2：多位置停靠

### 5.1 功能

- 预览面板在 MainWindow 内可以切换 4 个停靠位置
- 移植 WPF 版 `ApplyPreviewPosition()` 的布局逻辑

### 5.2 布局规则

与 WPF 版保持一致：

| 位置 | 值 | 说明 |
|------|-----|------|
| 底部 | 1 | 预览面板在文件列表和目录树下方（占整行宽度） |
| 目录树下方 | 2 | 预览面板在目录树底部 |
| 文件列表下方 | 3 | 预览面板在文件列表底部 |
| 文件列表右侧 | 4 | 预览面板在最右侧（当前 Avalonia 版默认） |

### 5.3 Avalonia Grid 布局实现

当前 MainWindow 内容区 Grid 定义为：
```axaml
<Grid ColumnDefinitions="Auto,5,2.5*,5,3*">
```

Phase 2 需要重构为更灵活的布局，支持不同的位置场景。参考 WPF 版 `ApplyPreviewPosition()` 的 Grid 行列操作，但用 Avalonia 的 Grid API 重写。

主要差异点：
- Avalonia 的 `Grid.SetRow` / `Grid.SetColumn` / `Grid.SetRowSpan` / `Grid.SetColumnSpan` 与 WPF 一致
- `GridLength` 类型一致
- 需要在内存中定义更多 Grid 行列，以适应不同布局

建议布局结构：
```
OuterGrid (Row 3 内容区)
  ColumnDefinitions="Auto,5,2.5*,5,3*"
  RowDefinitions="Auto,4,*,Auto,4,Auto"

  位置 4 (右侧)：PreviewPanel Grid.Column=4, Row=0, RowSpan=6
  位置 1 (底部)：PreviewPanel Grid.Column=0, Row=3, ColSpan=5
  位置 2 (目录树下)：PreviewPanel Grid.Column=0, Row=3, ColSpan=1
  位置 3 (文件列表下)：PreviewPanel Grid.Column=2, Row=3, ColSpan=3
```

### 5.4 切换 UI

- 在预览面板工具栏加一个停靠位置切换按钮（Dropdown 或 循环切换）
- 位置记忆到 AppSettings.PreviewDockPosition

## 6. Phase 3：拖拽分离 + 边缘吸附（PS 风格）

### 6.1 功能

- 从预览面板标题栏拖拽拖出主窗口时自动变为浮动窗口
- 拖回 MainWindow 边缘时显示蓝色吸附指示器
- 松手时自动停靠到对应位置
- 拖拽过程中实时预览

### 6.2 拖拽分离

预览面板的标题栏实现拖拽检测：

```csharp
// PreviewPanel 标题栏 MouseDown
private void OnTitleBarMouseDown(object sender, PointerPressedEventArgs e)
{
    if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        this.PointerMoved += OnTitleBarDragMove;
        this.PointerReleased += OnTitleBarDragRelease;
    }
}

private void OnTitleBarDragMove(object sender, PointerEventArgs e)
{
    var pos = e.GetPosition(this);
    var delta = pos - _dragStartPoint;
    if (Math.Abs(delta.X) > 10 || Math.Abs(delta.Y) > 10)
    {
        _isDragging = true;
        // 进入浮动模式：移出 PreviewPanel 到 PreviewFloatingWindow
        DetachToFloatingWindow();
    }
}

private void OnTitleBarDragRelease(object sender, PointerReleasedEventArgs e)
{
    this.PointerMoved -= OnTitleBarDragMove;
    this.PointerReleased -= OnTitleBarDragRelease;
}
```

### 6.3 边缘吸附检测

`DockingManager` 检测浮动窗口与 MainWindow 边缘的距离：

```csharp
public static DockingHitResult DetectDockTarget(
    PixelPoint floatingPos, Size floatingSize,
    PixelPoint mainWindowPos, Size mainWindowSize,
    double threshold = 30)
{
    // 计算 MainWindow 的 4 条边 + 中心
    // 如果浮动窗口边缘距离 MainWindow 对应边缘 < threshold，则触发吸附
    // 返回 DockingHitResult (位置 + 方向)
}
```

### 6.4 DockingOverlay

当检测到接近边缘时，显示一个透明覆层窗口指示停靠位置：

```csharp
public class DockingOverlay : Window
{
    public DockingOverlay()
    {
        TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;
        Background = new SolidColorBrush(Color.FromArgb(60, 0, 120, 255));
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
    }

    public void ShowAt(Rect bounds)
    {
        this.Position = new PixelPoint((int)bounds.X, (int)bounds.Y);
        this.Width = bounds.Width;
        this.Height = bounds.Height;
        this.Show();
    }
}
```

### 6.5 拖拽停靠流程

```
用户拖拽浮动窗口 → DockingManager 不断检测：
  ├── 距离边缘 < 30px → 显示 DockingOverlay 吸附指示器
  │   ├── 松手 → 停靠到对应位置 → 隐藏浮动窗口 → 移回 MainWindow Grid
  │   └── 继续拖远 → 隐藏 DockingOverlay
  └── 距离边缘 ≥ 30px → 无操作

用户在浮动窗口区域外松手 → 停留在当前状态
```

## 7. 文件变更清单

### 新增文件

| 文件 | Phase | 说明 |
|------|-------|------|
| `Views/PreviewFloatingWindow.axaml` | 1 | 浮动窗口 UI |
| `Views/PreviewFloatingWindow.axaml.cs` | 1 | 浮动窗口逻辑 |
| `Services/DockingManager.cs` | 3 | 边缘吸附检测 |
| `Views/DockingOverlay.axaml` | 3 | 吸附指示器 |
| `Views/DockingOverlay.axaml.cs` | 3 | 吸附指示器逻辑 |

### 修改文件

| 文件 | Phase | 变更 |
|------|-------|------|
| `Models/AppSettings.cs` | 1 | 加 PreviewDockState / 浮动位置字段 / PreviewDockPosition |
| `Views/MainWindow.axaml` | 1 | PreviewPanel 包裹条件可见性、加浮动按钮 |
| `Views/MainWindow.axaml.cs` | 1 | 浮动窗口创建/销毁逻辑 |
| `Views/MainWindow.axaml` | 2 | Grid 布局重构支持多位置停靠 |
| `Views/MainWindow.axaml.cs` | 2 | ApplyPreviewPosition() 移植 |
| `ViewModels/MainWindowViewModel.cs` | 1 | 加 IsPreviewDocked / ToggleFloatPreviewCommand |
| `ViewModels/MainWindowViewModel.cs` | 2 | 加 PreviewDockPosition / ChangeDockPositionCommand |

### 不变文件

- `ViewModels/PreviewViewModel.cs` — 不修改
- `Views/PreviewPanel.axaml` — 不修改
- `Views/PreviewPanel.axaml.cs` — 不修改
- `Services/PreviewService.cs` — 不修改

## 8. 风险与注意事项

### 8.1 Parent 切换时的视觉闪烁
PreviewPanel 从 Grid 移除再添加到 Window 时，可能发生闪烁。
**对策**：使用 `OpacityMask` 或 `IsVisible` 在切换前隐藏，切换完成后再显示。

### 8.2 ScrollViewer SizeChanged 在浮动后需要重新绑定
PreviewPanel 的 code-behind 中订阅了 `PreviewContentScroller.SizeChanged`，浮动后控件在视觉树中的位置变了，但 SizeChanged 事件仍然会触发（因为控件自身大小变化不依赖父容器）。已验证可行。

### 8.3 浮动窗口独立 Dragging 可能卡顿
如果 PreviewPanel 内容复杂（如字体预览大量 SkiaSharp 渲染），拖动时可能卡顿。
**对策**：Phase 3 可考虑拖动时降低渲染质量，或仅拖动窗口边框（Windows 默认的 `DragMove` 已带此优化）。

### 8.4 多显示器
位置保存和恢复需要考虑多显示器场景。使用 `Screen.AllScreens` 验证恢复位置仍在可视区域内。如果不在，重置到主屏幕中心。

### 8.5 GIF 动画在 Parent 切换时
DispatcherTimer 在 Parent 切换时不会自动停止，但 PreviewPanel 移出视觉树后 `Bitmap` 更新不会被显示，没有副作用。切回后自动恢复。

## 9. 验收标准

### Phase 1
- [ ] 点击「浮动预览」按钮，预览面板弹出为独立窗口
- [ ] 浮动窗口显示当前预览内容的标题
- [ ] 关闭浮动窗口，预览面板回到 MainWindow 原位
- [ ] 浮动窗口位置/大小在重启后恢复
- [ ] 主题（暗色/亮色）在浮动窗口中正确继承
- [ ] 浮动窗口随主窗口最小化/关闭

### Phase 2
- [ ] 预览面板可在 4 个位置间切换
- [ ] 位置切换后预览内容不中断
- [ ] GridSplitter 在对应位置正常工作
- [ ] 位置在重启后持久化

### Phase 3
- [ ] 从预览面板标题栏拖拽可分离为浮动窗口
- [ ] 拖回 MainWindow 边缘 30px 内显示吸附指示器
- [ ] 松手后自动停靠到对应位置
- [ ] 拖拽过程流畅无卡顿
- [ ] 多显示器下行为正确

## 10. Timeline 估算

| Phase | 工作量 | 说明 |
|-------|--------|------|
| Phase 1 | 1–2 天 | 浮动窗口 + Button 切换 + 位置记忆 |
| Phase 2 | 1–2 天 | Grid 布局重构 + 4 位置切换 |
| Phase 3 | 3–5 天 | 拖拽分离 + 吸附检测 + Overlay |
| **合计** | **5–9 天** | 取决于拖拽吸附的精细程度 |
