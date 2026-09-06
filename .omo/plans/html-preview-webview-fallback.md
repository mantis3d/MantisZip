# HTML Preview — WebView + 优雅降级

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Avalonia 版 HTML 预览从「ReverseMarkdown → Markdown → 控件树」有损管线升级为「跨平台原生 WebView + ReverseMarkdown 降级」双轨方案。Markdown 预览保持现有控件树管线不变。

> **计划边界（与 office-content-preview-avalonia.md 的分工）：**
> 本计划**只覆盖 HTML 预览的 WebView 升级**。DOCX（→Mammoth→HTML）与 Markdown（→Markdig→HTML）的 WebView 路线属于 office 计划的剩余项（`docs/PLAN.md` P3 条目），**不在本计划范围内**——实施时勿扩展范围。两份计划共享同一条 WebView 初始化/降级基建，office 剩余项可在本计划完成后复用同一套 `IsWebViewAvailable` 检测与降级信号机制。
>
> **状态说明：** 原 Task 5（MarkdownPreviewBuilder Table 支持）已由 `81e5609` 提交提前完成（`TryBuildBlock` 现有 `case Table` → `BuildTable`），故本计划删除该 Task，后续编号顺延。

**Architecture:**
```
ShowHtmlPreview(filePath)
  ├─ 尝试 WebView 初始化
  │   ├─ 成功 → WebView.Navigate(data: URI)  → 完美渲染
  │   └─ 失败（运行时缺失）
  │       └─ ShowHtmlFallback() → 现有 ReverseMarkdown 管线
  │           └─ 提示条："安装 WebView Runtime 可获得更好的 HTML 预览效果"
  └─ 工具栏：源码/渲染切换（降级模式下也可用）
```

**Why not pure WebView:** 跨平台（macOS 用 WKWebView，Linux 用 WPE WebKit，Windows 用 WebView2），无外部运行时强制依赖。

**Tech Stack:** .NET 10, Avalonia 12.0.4, Avalonia.Controls.WebView 12.0.1, ReverseMarkdown 4.7.0, Markdig 0.40.0

**新增依赖：**
| 包 | 版本 | 用途 | 许可证 |
|---|---|---|---|
| `Avalonia.Controls.WebView` | 12.0.1 | 跨平台原生 WebView 控件 | MIT |

**移除依赖：** 无（ReverseMarkdown/Markdig/PdfPig 保留，用作降级方案）

---

## 文件映射

| 文件 | 操作 | 职责 |
|------|------|------|
| `MantisZip.UI.Avalonia.csproj` | 修改 | 添加 `Avalonia.Controls.WebView` |
| `ViewModels/PreviewViewModel.cs` | 修改 | 新增 `ShowHtmlFallback()`、`IsWebViewAvailable`、源码切换逻辑、WebView URI 属性；改 `ShowHtmlPreview` |
| `Views/PreviewPanel.axaml` | 修改 | 新增 WebView 元素，与现有 Markdown 控件树共存 |
| `Views/PreviewPanel.axaml.cs` | 修改 | 注册 WebView 初始化回调，处理 fallback 信号 |
| `Localization/strings.*.json` | 修改 | 新增降级提示 key |

---

### Task 1: 添加 WebView NuGet 依赖

**文件：** `MantisZip.UI.Avalonia.csproj`

在 `ItemGroup` 中添加：
```xml
<PackageReference Include="Avalonia.Controls.WebView" Version="12.0.1" />
```

验证 `dotnet build` 通过。

---

### Task 2: 修改 PreviewViewModel — 新增属性与方法

**文件：** `ViewModels/PreviewViewModel.cs`

#### 2.1 新增 observable 属性

```csharp
[ObservableProperty]
private string _htmlWebViewUri = string.Empty;    // data: URI for WebView

[ObservableProperty]
private bool _isWebViewVisible;                   // WebView 模式可见

[ObservableProperty]
private bool _isFallbackActive;                   // 降级模式标记（工具栏显示不同）

[ObservableProperty]
private bool _isHtmlSourceVisible;               // HTML 源码模式可见

[ObservableProperty]
private string _htmlSourceContent = string.Empty; // HTML 源码文本
```

#### 2.2 现有属性修改

> **实现说明：** 以下两种可见性方案是设计草案。当前代码 `ShowHtmlPreview` 为同步方法（`PreviewViewModel.cs:2503`），实施时应**以现有属性结构为准**——先阅读现有 `IsMarkdownOrHtmlVisible`/`IsHtmlVisible` 等计算属性的实际命名与语义再对齐，勿照抄本草案属性名。

`IsMarkdownOrHtmlVisible` 改为排除 WebView 渲染的 HTML：
```csharp
public bool IsMarkdownOrHtmlVisible => PreviewType is PreviewType.Markdown
    or (PreviewType.Html and not WebViewFallback);
```

或者保持现有逻辑不变，让 WebView 独立控制可见性：
```csharp
public bool IsHtmlVisible => PreviewType == PreviewType.Html && !IsWebViewVisible;
```

#### 2.3 改 `ShowHtmlPreview` 方法

```csharp
public async Task ShowHtmlPreview(string filePath)
{
    var html = File.ReadAllText(filePath);
    // 先走 ReverseMarkdown 作为降级备用（无论如何都预计算）
    var fallbackMarkdown = Task.Run(() => {
        var converter = new Converter();
        return converter.Convert(html);
    });

    // 尝试 WebView
    try
    {
        var encoded = Uri.EscapeDataString(html);
        HtmlWebViewUri = $"data:text/html,{encoded}";
        IsWebViewVisible = true;
        IsFallbackActive = false;
        PreviewType = PreviewType.Html;
        IsPreviewVisible = true;
        IsToolbarVisible = true;  // 启用工具栏
    }
    catch (Exception) when (/* WebView 初始化失败条件 */)
    {
        // WebView 不可用，走降级
        var markdown = await fallbackMarkdown;
        var panel = MarkdownPreviewBuilder.Build(markdown);
        MarkdownPreviewPanel = panel;
        IsWebViewVisible = false;
        IsFallbackActive = true;
        PreviewType = PreviewType.Html;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        ShowFallbackNotification = true;
    }
}
```

> **设计决策：** WebView 初始化在 AXAML 中通过 `WebView.Initialized` 事件或 `PreviewPanel.axaml.cs` 代码后置处理，ViewModel 通过事件/委托获知初始化结果。或者用更简单的方式：在 `PreviewPanel.axaml.cs` 中捕获初始化异常，通过 ViewModel 属性通知 fallback。

#### 2.4 新增 `ShowHtmlFallback` 方法

从 `ShowHtmlPreview` 拆分出纯降级路径，供 code-behind 回调调用。

#### 2.5 源码切换（Toggle Preview Source）

```csharp
[ObservableProperty]
private bool _isHtmlSourceMode;

partial void OnIsHtmlSourceModeChanged(bool value)
{
    // 切换 WebView / 源码 TextBox
}
```

#### 2.6 `Clear()` 方法更新

重置新增属性：`HtmlWebViewUri = ""`, `IsWebViewVisible = false`, `IsFallbackActive = false`, `IsHtmlSourceVisible = false` 等。

---

### Task 3: 修改 PreviewPanel.axaml — 添加 WebView

**文件：** `Views/PreviewPanel.axaml`

在现有 Markdown 控件树节（`IsMarkdownOrHtmlVisible`）之后，添加 WebView 节：

```xml
<!-- HTML WebView 预览 -->
<WebView x:Name="HtmlPreviewWebView"
         IsVisible="{Binding IsWebViewVisible}"
         Background="{DynamicResource ThemeSurfaceBgBrush}" />

<!-- HTML 源码预览（WebView 模式下的源码显示） -->
<ScrollViewer IsVisible="{Binding IsHtmlSourceVisible}">
  <TextBox Text="{Binding HtmlSourceContent}"
           IsReadOnly="True"
           TextWrapping="Wrap"
           FontFamily="Consolas"
           Background="{DynamicResource ThemeSurfaceBgBrush}" />
</ScrollViewer>
```

> **注意：** `WebView` 的 `Source` 属性绑定到 `HtmlWebViewUri`。`WebView` 在 Avalonia.Controls.WebView 包中，命名空间通过 xmlns 导入。

---

### Task 4: 修改 PreviewPanel.axaml.cs — WebView 初始化和 fallback 处理

**文件：** `Views/PreviewPanel.axaml.cs`

#### 4.1 监听 WebView 初始化事件

```csharp
private async void OnHtmlWebViewInitialized(object? sender, EventArgs e)
{
    // WebView 初始化成功 — 可以安全导航
    // 如果之前储备了 URI，开始导航
    if (_vm?.HtmlWebViewUri is { Length: > 0 } uri)
    {
        HtmlPreviewWebView.Source = new Uri(uri);
    }
}
```

#### 4.2 处理初始化失败

WebView 初始化失败（如无 WebView2 Runtime / WPE 缺失）时触发 fallback：

```csharp
// 方案 A: 在 OnDataContextChanged 或构造函数中订阅 WebView 的异常
// WebView 没有直接的初始化失败事件，需在 try-catch 中处理

// 方案 B: 在 ViewModel 的 ShowHtmlPreview 中使用 PlatformCheck
// 先检测平台支持度再决定是否使用 WebView
private static bool IsWebViewAvailable()
{
    try
    {
        // 尝试创建 WebView 实例看是否抛出
        // 或者检查平台特定条件
        return true;
    }
    catch
    {
        return false;
    }
}
```

> **实现建议：** 检查 `WebViewPlatformSupport` 或类似机制。实际实现时，可以在 ViewModel 中延迟检测——第一次显示 HTML 时尝试创建 WebView，如果失败则记录状态并走降级，后续不再尝试。

---

### Task 5: 工具栏支持

**文件：** `ViewModels/PreviewViewModel.cs` + `Views/PreviewPanel.axaml`

当前 HTML 预览 `IsToolbarVisible = false`。WebView 模式下需要工具栏：

| 按钮 | WebView 模式 | 降级模式 |
|------|-------------|---------|
| 源码/渲染切换 | ✅ 显示 | ✅ 显示 |
| 字号 A−/A+ | ❌ WebView 不控制字号 | ✅ 可控制 |
| 降级提示条 | — | ✅ 显示 |

修改 `HasFontSizeControls` 等 computed 属性，使其在降级模式的 HTML 预览中返回 true。

---

### Task 6: 清理与验证

1. `dotnet build` 0 errors 0 warnings
2. 测试 HTML 文件预览（含 tables/images/css）
3. 测试降级路径（在无 WebView 运行时的环境）
4. WebView 模式下验证 data: URI 可正常渲染 HTML/CSS/JS
5. 源码/渲染切换功能正常
6. 清理页面、切换文件时 WebView 状态正确重置

---

## 注意事项

1. **data: URI 大小限制：** 极大型 HTML 文件（>2MB）可能受 data: URI 限制。如果遇到此问题，改为提取到临时文件后 `Navigate` 文件路径。
2. **data: URI 支持性（技术风险点）：** `Avalonia.Controls.WebView` 12.0.1 的跨平台后端（Win→WebView2 / Mac→WKWebView / Linux→WPE WebKit）对 `data:` URI 的支持程度需在 Task 1 加依赖后**先做 5 分钟冒烟验证**（导航一个 `data:text/html,<h1>test</h1>` 看是否渲染）。若某平台后端不支持 `data:` URI，改为 `Navigate` 临时文件路径方案，并将 `HtmlWebViewUri` 属性改为文件路径类型。此验证应作为 Task 1 验收的一部分。
3. **WebView 与 Avalonia 主题：** WebView 渲染的 HTML 页面不感知 Avalonia 主题色。如果需要深色模式同步，在 HTML 中注入 CSS media query 或内联样式。
4. **WebView 背景透明：** 无法实现 Avalonia 控件级的背景透明。`Background` 绑定 `ThemeSurfaceBgBrush` 作为底色。
5. **Linux WPE WebKit：** 用户需安装 `libwpewebkit-2.0`、`libwpe-1.0`、`libWPEBackend-fdo-1.0` 运行时库（见 [Avalonia 文档](https://docs.avaloniaui.net/docs/app-development/embedding-web-content#linux)）。
6. **降级策略触发时机：** 建议只检测一次并缓存结果，避免每次打开 HTML 都重复检测。
