# 浮动/停靠预览面板

> **状态**: 📋 计划中 | **Phase 1**: [⬜⬜⬜⬜⬜] (0/5) | **Phase 2**: [⬜⬜⬜⬜] (0/4) | **Phase 3**: [⬜⬜⬜⬜] (0/4) | **总进度**: (0/13)
> **分支**: `avalonia-port`
> **设计文档**: `docs/superpowers/specs/2026-07-16-floating-preview-panel-design.md`

---

## TL;DR

> **Quick Summary**: 将 Avalonia 版预览面板从固定的 Grid 嵌入改为 Photoshop 风格的可浮动/可停靠面板。分三阶段交付：Phase 1 实现 Button 触发的浮动独立窗口 + 位置记忆；Phase 2 移植 WPF 版的 4 位置停靠切换；Phase 3 实现拖拽分离 + 边缘吸附停靠。
>
> **Deliverables**:
> - `Views/PreviewFloatingWindow.axaml` + `.cs` — 浮动窗口
> - `Models/AppSettings.cs` — 新增浮动/停靠持久化字段
> - `Views/MainWindow.axaml` + `.cs` — 布局重构 + 浮动控制
> - `ViewModels/MainWindowViewModel.cs` — 新增浮动状态属性 + 命令
> - `Services/DockingManager.cs` (Phase 3) — 边缘吸附检测
> - `Views/DockingOverlay.axaml` + `.cs` (Phase 3) — 吸附指示器
>
> **Estimated Effort**: Medium (5–9 天分阶段)
> **Parallel Execution**: Phase 1 内部可并行（AppSettings + FloatingWindow + MainWindow 绑定）
> **Critical Path**: AppSettings → PreviewFloatingWindow → MainWindow 集成 → Phase 2 Grid 重构 → Phase 3 拖拽逻辑

---

## Context

### 当前状态

预览面板（`PreviewPanel : UserControl`）直接在 `MainWindow.axaml` 中嵌入 Grid 布局：

```axaml
<Grid ColumnDefinitions="Auto,5,2.5*,5,3*">
  ...
  <views:PreviewPanel Grid.Column="4"
                      DataContext="{Binding Preview}" />
</Grid>
```

- 只有右侧一种位置，没有停靠切换
- 无法浮动为独立窗口
- WPF 版已经有 4 位置切换逻辑（`ApplyPreviewPosition()`）但未移植

### 设计决策（详见设计文档）

| 决策 | 选择 |
|------|------|
| Panel 实例策略 | 单一实例，浮动时移动 Parent（不重建） |
| ViewModel 感知 | PreviewViewModel 不感知浮动/停靠状态 |
| 第三方依赖 | 零新增依赖 |
| 浮动窗口生命周期 | `Owner = MainWindow`，跟随最小化/关闭 |
| 主题同步 | `RequestedThemeVariant` 继承 MainWindow |
| 多显示器 | 恢复位置时做 `Screen.AllScreens` 边界校验 |

---

## 任务清单

### Phase 1 — Button 切换浮动/停靠 + 位置记忆

核心逻辑：点击「浮动预览」按钮，PreviewPanel 移出到独立 Window；关闭浮动窗口或点击「停靠」移回。

**涉及文件**:

| 文件 | 操作 |
|------|------|
| `Models/AppSettings.cs` | 🔧 修改 — 新增浮动状态/位置字段 |
| `Views/PreviewFloatingWindow.axaml` | 🆕 新增 — 浮动窗口 UI |
| `Views/PreviewFloatingWindow.axaml.cs` | 🆕 新增 — 浮动窗口逻辑 |
| `Views/MainWindow.axaml` | 🔧 修改 — 加浮动按钮 + PreviewPanel 条件可见 |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 浮动窗口创建/销毁/移动逻辑 |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 加 IsPreviewDocked / ToggleFloatPreviewCommand |

---

#### Task 1.1: AppSettings 新增字段

**Files:**
- Modify: `Models/AppSettings.cs`

**步骤**:

- [ ] **1.1a 新增浮动状态枚举/字符串属性**

```csharp
// 在 Models/AppSettings.cs 的现有属性之后添加

/// <summary>预览面板停靠状态: "Docked" | "Floating"</summary>
public string PreviewDockState { get; set; } = "Docked";

/// <summary>浮动窗口 X (-1 = 未设置)</summary>
public double PreviewFloatingX { get; set; } = -1;

/// <summary>浮动窗口 Y (-1 = 未设置)</summary>
public double PreviewFloatingY { get; set; } = -1;

/// <summary>浮动窗口宽度</summary>
public double PreviewFloatingWidth { get; set; } = 600;

/// <summary>浮动窗口高度</summary>
public double PreviewFloatingHeight { get; set; } = 400;
```

- [ ] **1.1b 启动时恢复浮动状态**（如果在 `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 中，根据 `PreviewDockState == "Floating"` 自动弹出浮动窗口）

<!-- 注意：Task 1.1b 依赖 Task 1.2–1.4 完成，实际排队在后面 -->

---

#### Task 1.2: PreviewFloatingWindow

**Files:**
- Create: `Views/PreviewFloatingWindow.axaml`
- Create: `Views/PreviewFloatingWindow.axaml.cs`

**步骤**:

- [ ] **1.2a 创建浮动窗口 AXAML**

```axaml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MantisZip.UI.Avalonia.Views.PreviewFloatingWindow"
        Title="预览"
        Width="600" Height="400"
        MinWidth="300" MinHeight="200"
        ShowInTaskbar="False"
        WindowStartupLocation="Manual"
        Background="{DynamicResource ThemeWindowBgBrush}">
  <Grid RowDefinitions="Auto,*">
    <!-- 标题栏 -->
    <Border Grid.Row="0" Padding="8,4"
            Background="{DynamicResource ThemeHeaderBgBrush}">
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Grid.Column="0"
                   Text="{Binding HeaderText}"
                   Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                   VerticalAlignment="Center" />
        <Button Grid.Column="1"
                Content="📌"
                ToolTip.Tip="停靠回主窗口"
                Command="{Binding $parent[Window].Tag.DockCommand}" />
      </Grid>
    </Border>
    <!-- PreviewPanel 宿主（由代码填充） -->
    <ContentControl Grid.Row="1" x:Name="PreviewContent" />
  </Grid>
</Window>
```

- [ ] **1.2b 创建浮动窗口 code-behind**

```csharp
namespace MantisZip.UI.Avalonia.Views;

using Avalonia.Controls;
using Avalonia.Threading;

public partial class PreviewFloatingWindow : Window
{
    /// <summary>移入的 PreviewPanel，停靠时需要归还给 MainWindow</summary>
    public Panel? PreviewPanelRef { get; set; }

    /// <summary>停靠回调，由 MainWindow 设置</summary>
    public Action? OnDock { get; set; }

    public PreviewFloatingWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // 关闭时自动停靠（不关闭预览）
        e.Cancel = true;
        DockBack();
    }

    public void DockBack()
    {
        OnDock?.Invoke();
    }

    /// <summary>将 PreviewPanel 移入此窗口</summary>
    public void HostPreviewPanel(Panel panel)
    {
        PreviewPanelRef = panel;
        PreviewContent.Content = panel;
    }

    /// <summary>取回 PreviewPanel</summary>
    public Panel? TakeBackPreviewPanel()
    {
        var panel = PreviewPanelRef;
        PreviewContent.Content = null;
        PreviewPanelRef = null;
        return panel;
    }

    /// <summary>添加"停靠"命令（通过 Tag 传递）</summary>
    public void SetDockCommand(ICommand command)
    {
        Tag = new { DockCommand = command };
    }
}
```

- [ ] **1.2c 位置持久化**：在 `PositionChanged` 和 `Resized` 事件中保存位置/大小到 `AppSettings`

```csharp
public PreviewFloatingWindow()
{
    InitializeComponent();
    Closing += OnClosing;
    PositionChanged += OnPositionChanged;
    Resized += OnResized;
}

private void OnPositionChanged(object? sender, PixelPointEventArgs e)
{
    var settings = AppSettings.Instance;
    settings.PreviewFloatingX = Position.X;
    settings.PreviewFloatingY = Position.Y;
    settings.Save();
}

private void OnResized(object? sender, ResizedEventArgs e)
{
    var settings = AppSettings.Instance;
    settings.PreviewFloatingWidth = ClientSize.Width;
    settings.PreviewFloatingHeight = ClientSize.Height;
    settings.Save();
}
```

- [ ] **1.2d 主题同步**：在显示前设置 `RequestedThemeVariant`

```csharp
// 在 MainWindow 创建浮动窗口时：
floatingWindow.RequestedThemeVariant = this.RequestedThemeVariant;
```

---

#### Task 1.3: MainWindowViewModel 新增属性 + 命令

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`

**步骤**:

- [ ] **1.3a 新增 IsPreviewDocked 属性**

```csharp
[ObservableProperty]
private bool _isPreviewDocked = true;  // true=停靠, false=浮动
```

- [ ] **1.3b 新增 ToggleFloatPreviewCommand**

```csharp
[RelayCommand]
private void ToggleFloatPreview()
{
    IsPreviewDocked = !IsPreviewDocked;
    // 实际浮动/停靠操作在 View code-behind 中处理，
    // ViewModel 只负责状态标记
}
```

- [ ] **1.3c 添加 DockPreviewCommand**（浮动窗口中点击停靠时调用）

```csharp
[RelayCommand]
private void DockPreview()
{
    if (!IsPreviewDocked)
    {
        IsPreviewDocked = true;
        // View code-behind 监听此属性变化或通过事件处理
    }
}
```

---

#### Task 1.4: MainWindow 集成

**Files:**
- Modify: `Views/MainWindow.axaml`
- Modify: `Views/MainWindow.axaml.cs`

**步骤**:

- [ ] **1.4a MainWindow.axaml 改造**：添加浮动按钮 + 条件显示

工具栏新增按钮：
```axaml
<!-- 在工具栏合适位置（如预览相关按钮组） -->
<Button Classes="ToolbarButton"
        Command="{Binding ToggleFloatPreviewCommand}"
        IsEnabled="{Binding Preview.IsPreviewVisible}"
        ToolTip.Tip="浮动预览">
  <StackPanel>
    <TextBlock Text="🔲" Classes="ToolbarButtonIcon" />
    <TextBlock Text="浮动" Classes="ToolbarButtonLabel" />
  </StackPanel>
</Button>
```

PreviewPanel 改为条件可见：
```axaml
<views:PreviewPanel Grid.Column="4"
                    DataContext="{Binding Preview}"
                    IsVisible="{Binding IsPreviewDocked}" />
```

- [ ] **1.4b MainWindow.axaml.cs 浮动/停靠逻辑**

关键方法：

```csharp
private PreviewFloatingWindow? _floatingWindow;

partial void OnIsPreviewDockedChanged(bool isDocked)
{
    if (isDocked)
        DockPreviewInternal();
    else
        FloatPreviewInternal();
}

private void FloatPreviewInternal()
{
    if (_floatingWindow != null) return;

    // 1. 从 Grid 中移除 PreviewPanel
    var previewPanel = this.FindControl<PreviewPanel>("PreviewPanel");

    // 2. 创建浮动窗口
    _floatingWindow = new PreviewFloatingWindow();
    _floatingWindow.RequestedThemeVariant = this.RequestedThemeVariant;
    _floatingWindow.Owner = this;

    // 3. 移入 PreviewPanel
    _floatingWindow.HostPreviewPanel(previewPanel);

    // 4. 设置停靠回调
    _floatingWindow.OnDock = () =>
    {
        ViewModel?.DockPreviewCommand.Execute(null);
    };

    // 5. 恢复上次位置/大小
    var s = AppSettings.Instance;
    if (s.PreviewFloatingX >= 0 && s.PreviewFloatingY >= 0)
    {
        _floatingWindow.Position = new PixelPoint(
            (int)s.PreviewFloatingX, (int)s.PreviewFloatingY);
        _floatingWindow.Width = s.PreviewFloatingWidth;
        _floatingWindow.Height = s.PreviewFloatingHeight;
    }

    // 6. 设置停靠命令（Tag 传递）
    _floatingWindow.SetDockCommand(ViewModel?.DockPreviewCommand);

    // 7. 显示
    _floatingWindow.Show();

    // 8. 持久化状态
    var settings = AppSettings.Instance;
    settings.PreviewDockState = "Floating";
    settings.Save();
}

private void DockPreviewInternal()
{
    if (_floatingWindow == null) return;

    // 1. 取回 PreviewPanel
    var panel = _floatingWindow.TakeBackPreviewPanel();

    // 2. 放回 Grid.Column=4
    Grid.SetColumn(panel, 4);
    // ... 设置其他 Grid 位置属性

    // 3. 关闭浮动窗口
    _floatingWindow.Close();
    _floatingWindow = null;

    // 4. 持久化状态
    var settings = AppSettings.Instance;
    settings.PreviewDockState = "Docked";
    settings.Save();
}
```

- [ ] **1.4c 启动时恢复浮动状态**

在 `MainWindow` 的 `Initialized` 或 `Loaded` 事件中：

```csharp
protected override void OnInitialized(EventArgs e)
{
    base.OnInitialized(e);
    if (AppSettings.Instance.PreviewDockState == "Floating")
    {
        // 延迟一帧执行，确保布局已完成
        Dispatcher.UIThread.Post(() => ViewModel?.ToggleFloatPreviewCommand.Execute(null));
    }
}
```

---

#### Task 1.5: 验证 Phase 1

- [ ] **1.5a `dotnet build` 通过**（Avalonia 项目）
- [ ] **1.5b 点击浮动按钮 → PreviewPanel 弹出为独立窗口**
- [ ] **1.5c 独立窗口显示预览内容（图片/文本/字体等所有类型）**
- [ ] **1.5d 暗色/亮色主题在浮动窗口中正确继承**
- [ ] **1.5e 关闭浮动窗口 → PreviewPanel 回到 MainWindow 原位**
- [ ] **1.5f 浮动窗口位置/大小在重启后恢复**
- [ ] **1.5g 浮动窗口随主窗口最小化/关闭**
- [ ] **1.5h 信息面板（右侧/底部）在浮动窗口内正常切换**

---

### Phase 2 — 多位置停靠（移植 WPF `ApplyPreviewPosition()`）

核心逻辑：将 WPF 版已有的 4 位置停靠切换移植到 Avalonia 版。

**涉及文件**:

| 文件 | 操作 |
|------|------|
| `Views/MainWindow.axaml` | 🔧 修改 — Grid 布局重构支持多位置 |
| `Views/MainWindow.axaml.cs` | 🔧 修改 — 移植 `ApplyPreviewPosition()` |
| `Models/AppSettings.cs` | 🔧 修改 — 加 `PreviewDockPosition` |
| `ViewModels/MainWindowViewModel.cs` | 🔧 修改 — 加 `PreviewDockPosition` 属性 + 切换命令 |
| `Views/PreviewPanel.axaml.cs` | 🔧 可能修改 — 确保自适应不同宽度 |

---

#### Task 2.1: Grid 布局重构

**Files:**
- Modify: `Views/MainWindow.axaml`

**步骤**:

- [ ] **2.1a 重构内容区 Grid 定义**

当前单层 Grid：
```axaml
<Grid ColumnDefinitions="Auto,5,2.5*,5,3*">
```

改为包含预览行的多层 Grid：
```axaml
<Grid x:Name="ContentGrid"
      ColumnDefinitions="Auto,5,2.5*,5,3*"
      RowDefinitions="*,Auto,4,Auto">
  <!-- 列说明：
        Col 0: FolderTree (Auto)
        Col 1: GridSplitter (5)
        Col 2: FileList (2.5*)
        Col 3: GridSplitter (5)
        Col 4: PreviewPanel (3*) — 仅右侧模式使用

       行说明：
        Row 0: 主内容区（*）
        Row 1: PreviewSplitter (4) — 底部模式使用
        Row 2: PreviewRow (Auto) — 底部模式使用
  -->
  ...
</Grid>
```

- [ ] **2.1b 添加额外的 GridSplitter 引用**（用于底部/目录树下/文件列表下模式的横条分割线）

---

#### Task 2.2: 移植 ApplyPreviewPosition()

**Files:**
- Modify: `Views/MainWindow.axaml.cs`

**步骤**:

- [ ] **2.2a 实现 ApplyPreviewPosition(int position)**

参考 WPF 版 `MainWindow.Preview.cs` 的 `ApplyPreviewPosition()`（~80 行逻辑），用 Avalonia API 重写：

| 位置 | 值 | PreviewPanel 位置 |
|------|-----|-------------------|
| 底部 | 1 | Grid.Column=0, Row=2, ColSpan=5 |
| 目录树下方 | 2 | Grid.Column=0, Row=2, ColSpan=1 |
| 文件列表下方 | 3 | Grid.Column=2, Row=2, ColSpan=3 |
| 文件列表右侧 | 4 | Grid.Column=4, Row=0, RowSpan=3 |

关键差异：
- Avalonia 的 `Grid.SetRow` / `Grid.SetColumn` / `Grid.SetRowSpan` / `Grid.SetColumnSpan` API 与 WPF 一致
- `GridLength` 类型一致
- 需要为每个位置管理独立的 `GridSplitter` 可见性

- [ ] **2.2b 实现 ShowPreviewPanel() 和位置大小记忆**

从 WPF 版移植 `ShowPreviewPanel()` + `_lastPreviewSizes` 字典，每个位置独立记忆大小。

---

#### Task 2.3: AppSettings + ViewModel 停靠位置

**Files:**
- Modify: `Models/AppSettings.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`

**步骤**:

- [ ] **2.3a AppSettings 加字段**

```csharp
public int PreviewDockPosition { get; set; } = 2;  // 1=底部 2=目录树下 3=文件列表下 4=文件列表右侧
```

- [ ] **2.3b ViewModel 加属性 + 命令**

```csharp
[ObservableProperty]
private int _previewDockPosition = 2;

partial void OnPreviewDockPositionChanged(int value)
{
    AppSettings.Instance.PreviewDockPosition = value;
    AppSettings.Instance.Save();
    // View code-behind 监听此变化触发 ApplyPreviewPosition
}

[RelayCommand]
private void CycleDockPosition()
{
    PreviewDockPosition = (PreviewDockPosition % 4) + 1;
}
```

---

#### Task 2.4: 位置切换 UI

- [ ] **2.4a 在预览面板工具栏添加位置切换按钮**（或 dropdown）

```axaml
<Button Command="{Binding CycleDockPositionCommand}"
        ToolTip.Tip="切换停靠位置">
  <TextBlock Text="↕" />
</Button>
```

- [ ] **2.4b 验证每个位置的布局正确性**

---

### Phase 3 — 拖拽分离 + 边缘吸附停靠

核心逻辑：从预览面板标题栏拖拽，拖出主窗口边缘时自动创建浮动窗口；拖回时显示吸附指示器并停靠。

**涉及文件**:

| 文件 | 操作 |
|------|------|
| `Views/PreviewPanel.axaml` | 🔧 修改 — 加可拖拽标题栏（如果当前没有标题栏） |
| `Views/PreviewPanel.axaml.cs` | 🔧 修改 — 拖拽检测逻辑 |
| `Services/DockingManager.cs` | 🆕 新增 — 边缘吸附检测算法 |
| `Views/DockingOverlay.axaml` | 🆕 新增 — 吸附指示器窗口 |
| `Views/DockingOverlay.axaml.cs` | 🆕 新增 — 指示器逻辑 |
| `Views/PreviewFloatingWindow.axaml.cs` | 🔧 修改 — 集成拖拽检测 |

---

#### Task 3.1: PreviewPanel 拖拽标题栏

**Files:**
- Modify: `Views/PreviewPanel.axaml`（如果当前没有独立标题栏区）
- Modify: `Views/PreviewPanel.axaml.cs`

**步骤**:

- [ ] **3.1a 确保 PreviewPanel 顶部有可拖拽的标题栏区域**

如果当前没有，在 PreviewPanel.axaml 的 Toolbar 之上/周围添加一个标题栏 Border 作为拖拽手柄。

- [ ] **3.1b 实现拖拽检测（拖出阈值 → 触发浮动）**

```csharp
private Point _dragStartPoint;
private bool _isDragging;

private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        this.PointerMoved += OnTitleBarPointerMoved;
        this.PointerReleased += OnTitleBarPointerReleased;
        e.Handled = true;
    }
}

private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
{
    if (_isDragging) return;
    var pos = e.GetPosition(this);
    var delta = pos - _dragStartPoint;
    if (Math.Abs(delta.X) > 10 || Math.Abs(delta.Y) > 10)
    {
        _isDragging = true;
        // 触发浮动：通知 MainWindow
        var vm = DataContext as PreviewViewModel;
        // 通过静态事件或回调通知 MainWindow 执行浮动
        RequestFloatPreview?.Invoke();
        this.PointerMoved -= OnTitleBarPointerMoved;
        this.PointerReleased -= OnTitleBarPointerReleased;
    }
}
```

- [ ] **3.1c 定义 RequestFloatPreview 事件**

```csharp
// PreviewPanel.cs 中
public static event Action? RequestFloatPreview;
```

---

#### Task 3.2: DockingManager — 边缘吸附检测

**Files:**
- Create: `Services/DockingManager.cs`

**步骤**:

- [ ] **3.2a 实现吸附检测算法**

```csharp
namespace MantisZip.UI.Avalonia.Services;

public enum DockTarget
{
    None,
    Bottom,
    Left,
    Right
}

public readonly record struct DockingHitResult(
    DockTarget Target,
    Rect Bounds);  // 吸附后 PreviewPanel 应占据的屏幕区域

public static class DockingManager
{
    private const double Threshold = 30.0;

    /// <summary>
    /// 检测浮动窗口是否靠近 MainWindow 边缘，返回吸附目标。
    /// </summary>
    public static DockingHitResult DetectDockTarget(
        PixelPoint floatingPos, Size floatingSize,
        PixelPoint mainWindowPos, Size mainWindowSize)
    {
        var fx = floatingPos.X;
        var fy = floatingPos.Y;
        var fw = floatingSize.Width;
        var fh = floatingSize.Height;
        var mx = mainWindowPos.X;
        var my = mainWindowPos.Y;
        var mw = mainWindowSize.Width;
        var mh = mainWindowSize.Height;

        // 底部：浮动窗口顶部接近 MainWindow 底部
        if (Math.Abs(fy - (my + mh)) < Threshold && fx >= mx - Threshold && fx + fw <= mx + mw + Threshold)
        {
            return new DockingHitResult(DockTarget.Bottom,
                new Rect(mx, my + mh - 200, mw, 200));  // 底部 200px
        }

        // 左侧：浮动窗口左侧接近 MainWindow 左侧
        if (Math.Abs(fx - mx) < Threshold && fy >= my - Threshold && fy + fh <= my + mh + Threshold)
        {
            return new DockingHitResult(DockTarget.Left,
                new Rect(mx, my, 220, mh));  // 左侧 220px（目录树宽度）
        }

        // 右侧：浮动窗口右侧接近 MainWindow 右侧
        if (Math.Abs((fx + fw) - (mx + mw)) < Threshold && fy >= my - Threshold && fy + fh <= my + mh + Threshold)
        {
            return new DockingHitResult(DockTarget.Right,
                new Rect(mx + mw - 350, my, 350, mh));  // 右侧 350px
        }

        return new DockingHitResult(DockTarget.None, Rect.Empty);
    }
}
```

---

#### Task 3.3: DockingOverlay — 吸附指示器

**Files:**
- Create: `Views/DockingOverlay.axaml`
- Create: `Views/DockingOverlay.axaml.cs`

**步骤**:

- [ ] **3.3a 创建吸附指示器窗口**

```csharp
namespace MantisZip.UI.Avalonia.Views;

public class DockingOverlay : Window
{
    public DockingOverlay()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        TransparencyLevelHint = WindowTransparencyLevel.Transparent;
        Background = new SolidColorBrush(Color.FromArgb(50, 0, 120, 255), 0.2);
        IsHitTestVisible = false;
    }

    public void ShowAt(Rect screenBounds)
    {
        Position = new PixelPoint((int)screenBounds.X, (int)screenBounds.Y);
        Width = screenBounds.Width;
        Height = screenBounds.Height;
        Show();
    }

    public void HideOverlay()
    {
        Hide();
        Width = 0;
        Height = 0;
    }
}
```

- [ ] **3.3b 在浮动窗口拖拽过程中调用**

在 PreviewFloatingWindow 的拖拽循环中轮询 `DockingManager.DetectDockTarget()`：

```csharp
// 在 PreviewFloatingWindow 中
private DockingOverlay? _overlay;
private CancellationTokenSource? _dockCheckCts;

private void StartDockDetection()
{
    _dockCheckCts = new CancellationTokenSource();
    var ct = _dockCheckCts.Token;
    _ = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct);
            if (ct.IsCancellationRequested) break;

            var result = DockingManager.DetectDockTarget(
                Position, ClientSize,
                mainWindowPos, mainWindowSize);

            Dispatcher.UIThread.Post(() =>
            {
                if (result.Target != DockTarget.None)
                {
                    _overlay ??= new DockingOverlay();
                    _overlay.ShowAt(result.Bounds);
                }
                else
                {
                    _overlay?.HideOverlay();
                    _overlay = null;
                }
            });
        }
    }, ct);
}
```

---

#### Task 3.4: 拖拽松手吸附停靠

- [ ] **3.4a 松手时检测吸附 → 停靠**

在浮动窗口的 `PointerReleased` 或 `Deactivated` 事件中：

```csharp
private void On dragFinished()
{
    _dockCheckCts?.Cancel();

    var result = DockingManager.DetectDockTarget(
        Position, ClientSize, mainWindowPos, mainWindowSize);

    if (result.Target != DockTarget.None)
    {
        // 停靠到对应位置
        _overlay?.Close();
        _overlay = null;
        OnDockToPosition?.Invoke(result.Target);
    }
    else
    {
        // 保持浮动
        _overlay?.HideOverlay();
    }
}
```

- [ ] **3.4b 多显示器边界处理**

在恢复位置时增加 `Screen.AllScreens` 校验：如果保存的位置不在任何屏幕的可视区域内，重置到主屏幕中心。

---

## 验证清单

### Phase 1
- [ ] `dotnet build` 通过
- [ ] 浮动窗口弹出/关闭正常
- [ ] 所有预览类型在浮动窗口中正常工作
- [ ] 主题继承正确
- [ ] 位置记忆/恢复正常
- [ ] 多显示器场景边界处理

### Phase 2
- [ ] 4 个停靠位置切换均正常工作
- [ ] GridSplitter 在各个位置功能正常
- [ ] 位置切换不中断预览内容
- [ ] 每个位置的大小独立记忆

### Phase 3
- [ ] 拖拽标题栏可分离为浮动窗口
- [ ] 边缘 30px 内显示蓝色吸附指示器
- [ ] 松手后自动停靠到对应位置
- [ ] 拖拽过程无卡顿
- [ ] 跨显示器吸附正确

## 文件变更汇总

### 新增 (4)

| 文件 | Phase |
|------|-------|
| `Views/PreviewFloatingWindow.axaml` | 1 |
| `Views/PreviewFloatingWindow.axaml.cs` | 1 |
| `Services/DockingManager.cs` | 3 |
| `Views/DockingOverlay.axaml` + `.cs` | 3 |

### 修改 (5)

| 文件 | Phase | 变更内容 |
|------|-------|----------|
| `Models/AppSettings.cs` | 1,2 | 浮动状态+位置+停靠位置字段 |
| `ViewModels/MainWindowViewModel.cs` | 1,2 | IsPreviewDocked + Toggle/Dock 命令 + DockPosition |
| `Views/MainWindow.axaml` | 1,2 | 浮动按钮 + Grid 布局重构 |
| `Views/MainWindow.axaml.cs` | 1,2 | 浮动管理 + ApplyPreviewPosition() |
| `Views/PreviewPanel.axaml` | 3 | 拖拽标题栏 |

### 不变

- `ViewModels/PreviewViewModel.cs` — 不修改
- `Services/PreviewService.cs` — 不修改
- `Views/PreviewPanel.axaml.cs` — Phase 3 加拖拽逻辑前不变

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Parent 切换时 PreviewPanel 闪烁 | 切换时用 Opacity 过渡或延迟显示 |
| ScrollViewer SizeChanged 在浮动后不触发 | 已验证：SizeChanged 随控件自身大小变化，与父容器无关 |
| 拖拽时 SkiaSharp 渲染卡顿 | 利用 OS 窗口拖拽优化（`DragMove()` 默认开启） |
| 多显示器恢复位置溢出 | `Screen.AllScreens` 边界校验 + 回退到主屏幕中心 |
