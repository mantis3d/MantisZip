# 文本预览编码选择器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在预览面板工具栏加入编码选择下拉框，让用户在文本/Markdown/HTML 预览时可手动选择编码立即重渲染，并激活闲置的 `AppSettings.TextEncodingPreference`。

**Architecture:** 三层改动——Core 层 `TextEncodingDetector` 抽出字节级解码 API（`DecodeText(byte[], string?, ...)` + `DetectAndDecodeText`），UI 层 `PreviewViewModel` 增设编码数据源/选中状态/字节缓存并改造三条读取链路（`ShowText` / `ShowMarkdownPreview` / `ShowHtmlPreview` / `ShowHtmlFallback`），View 层 `PreviewPanel.axaml` 工具栏加 ComboBox。切换编码走同一套「字节缓存 → 解码 → 重渲染」管道，无需重新提取压缩包条目。

**Tech Stack:** .NET 10 / Avalonia 12 / CommunityToolkit.Mvvm / Ude.NetStandard / CodePagesEncodingProvider（上一轮修复已全局注册）

**Spec:** 本计划依据前序对话确认的方案：竞品调研（仅 WinRAR 提供文本内容编码切换：查看菜单 + 状态栏点击）、项目现状（`TextEncodingPreference` 死设置、`DetectAndReadText` 无手动通道、`DecodeText` 底层 API 已有）、用户确认范围（文本 + Markdown + HTML 一并纳入）。

## Global Constraints

- 语言：新增 UI 文案必须走本地化（规则 13），`strings.zh-CN.json` / `strings.en.json` 成对同步添加；编码下拉的工具提示若用 XAML `LocalizedStrings[Key]` 绑定，必须同步登记到 `PreviewViewModel.UpdateLocalizedStrings()`（PreviewPanel 的 DataContext 是 PreviewViewModel），并同时加入 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组（该数组是全量字典源）
- 样式：新增 ComboBox 禁止默认颜色（规则 4），`Background`/`Foreground`/`BorderBrush` 用 `ThemeSurfaceBgBrush`/`ThemeTextPrimaryBrush`/`ThemeBorderBrush`；高度用 `{DynamicResource ControlHeight}`；ComboBox 下拉项需 ItemContainerTheme 设 `MinHeight={DynamicResource ControlHeightSm}`（规则 7）
- 间距：Margin/Padding 用 `SpacingXxxThk` 后缀资源（规则 5）
- 提交：conventional commits，`feat(avalonia)` 或 `feat(core,avalonia)`（规则 10）；commit 前更新进度文档（规则 3）
- 构建：每个 Task 完成后 `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`（规则 12）；Core 改动加跑 `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` 与 `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
- 编码注册：上一轮修复已在 `App.axaml.cs` 全局注册 `CodePagesEncodingProvider`，Core 层独立使用时 `DecodeText` 内部已有局部注册兜底，无需重复注册
- `Encoding.GetEncoding` 的编码名：选项 Key 用 .NET 标准名（`gbk`/`big5`/`shift-jis`/`euc-kr`/`gb18030`/`utf-8`/`utf-16`）而非 Ude 返回名（如 `GB18030` 可直接用，但 `Shift_JIS` 带下划线——统一用 .NET 名保证 `Encoding.GetEncoding(key)` 成功）

---

### Task 1: Core — TextEncodingDetector 字节级解码 API + 测试

**Files:**
- Modify: `src/MantisZip.Core/Utils/TextEncodingDetector.cs`
- Test: `tests/MantisZip.Tests/TextEncodingDetectorTests.cs`（新增）

**Interfaces:**
- Consumes: 现有 `DecodeText(byte[], int systemFallbackCodePage = 0)`（被 `ZipCommentHelper.cs:148` 调用，签名不变）
- Produces:
  - `public static (string? Name, double Confidence) DetectEncoding(byte[] data)` —— Ude 检测编码名与置信度，空数据返回 `(null, 0)`
  - `public static (string Text, string? EncodingName) DetectAndDecodeText(byte[] data, int systemFallbackCodePage = 0)` —— 自动检测并解码，返回实际生效编码名（供 UI 显示「自动检测: GBK」）
  - `public static string DecodeText(byte[] data, string? encodingName, int systemFallbackCodePage = 0)` —— 按显式编码名解码；`null`/`"auto"`/无效名时走现有 `DecodeText(byte[], int)` 回退链（UTF-8 BOM → 严格 UTF-8 → 系统 ANSI）

- [ ] **Step 1: 写失败测试 `tests/MantisZip.Tests/TextEncodingDetectorTests.cs`**

```csharp
using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

public class TextEncodingDetectorTests
{
    static TextEncodingDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] GbkBytes(string s) => Encoding.GetEncoding("gbk").GetBytes(s);

    [Fact]
    public void DecodeText_ExplicitGbk_DecodesChinese()
    {
        var bytes = GbkBytes("中文内容");
        var result = TextEncodingDetector.DecodeText(bytes, "gbk");
        Assert.Equal("中文内容", result);
    }

    [Fact]
    public void DecodeText_InvalidName_FallsBackToUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        var result = TextEncodingDetector.DecodeText(bytes, "not-a-real-encoding");
        Assert.Equal("hello", result); // 无效名不抛异常，走回退链
    }

    [Fact]
    public void DecodeText_NullOrAuto_BehavesLikeDefault()
    {
        var bytes = Encoding.UTF8.GetBytes("auto test");
        Assert.Equal("auto test", TextEncodingDetector.DecodeText(bytes, null));
        Assert.Equal("auto test", TextEncodingDetector.DecodeText(bytes, "auto"));
    }

    [Fact]
    public void DetectAndDecodeText_GbkBytes_ReturnsTextAndEncodingName()
    {
        var bytes = GbkBytes("GBK 数据");
        var (text, name) = TextEncodingDetector.DetectAndDecodeText(bytes);
        Assert.Equal("GBK 数据", text);
        Assert.NotNull(name);
    }

    [Fact]
    public void DetectAndDecodeText_Empty_ReturnsEmptyWithNullName()
    {
        var (text, name) = TextEncodingDetector.DetectAndDecodeText(Array.Empty<byte>());
        Assert.Equal(string.Empty, text);
        Assert.Null(name);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
Expected: 编译失败 —— `TextEncodingDetector` 缺少 `DetectEncoding` / `DetectAndDecodeText` / `DecodeText(byte[], string?, ...)` 重载

- [ ] **Step 3: 实现 Core API**

在 `TextEncodingDetector.cs` 顶部新增：

```csharp
/// <summary>Ude 检测编码名与置信度。空数据返回 (null, 0)。</summary>
public static (string? Name, double Confidence) DetectEncoding(byte[] data)
{
    if (data.Length == 0) return (null, 0);
    var detector = new CharsetDetector();
    detector.Feed(data, 0, data.Length);
    detector.DataEnd();
    return (string.IsNullOrEmpty(detector.Charset) ? null : detector.Charset, detector.Confidence);
}

/// <summary>自动检测并解码字节，返回 (文本, 实际生效编码名)。</summary>
public static (string Text, string? EncodingName) DetectAndDecodeText(byte[] data, int systemFallbackCodePage = 0)
{
    if (data.Length == 0) return (string.Empty, null);

    var (detected, confidence) = DetectEncoding(data);
    CoreLog.Trace("DetectAndDecodeText: detected={0}, confidence={1:P1}", detected, confidence);

    // 置信度 >= 50% 且编码名有效 → 用检测到的编码解码
    if (confidence >= 0.5 && !string.IsNullOrEmpty(detected))
    {
        try
        {
            var enc = Encoding.GetEncoding(detected);
            return (enc.GetString(data), detected);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("DetectAndDecodeText: detected encoding {0} failed: {1}", detected, ex.Message);
        }
    }

    // 回退链：BOM → 严格 UTF-8 → 系统 ANSI（复用现有 DecodeText 逻辑）
    return (DecodeText(data, systemFallbackCodePage), null);
}

/// <summary>按显式编码名解码字节。null / "auto" / 无效名 → 走自动回退链。</summary>
public static string DecodeText(byte[] data, string? encodingName, int systemFallbackCodePage = 0)
{
    if (data.Length == 0) return string.Empty;

    if (!string.IsNullOrEmpty(encodingName) &&
        !encodingName.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            return Encoding.GetEncoding(encodingName).GetString(data);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("DecodeText: explicit encoding {0} invalid: {1}", encodingName, ex.Message);
        }
    }
    return DecodeText(data, systemFallbackCodePage);
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`
Expected: 5 个测试全部 PASS

- [ ] **Step 5: 构建 Core + 提交**

Run: `dotnet build src\MantisZip.Core\MantisZip.Core.csproj` → 0 错误
Commit: `feat(core): TextEncodingDetector 新增字节级编码检测与按名解码 API`

---

### Task 2: ViewModel — 编码数据源 + 选中状态 + ShowText 链路改造

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`

**Interfaces:**
- Consumes: Task 1 的 `DetectAndDecodeText(byte[])` / `DecodeText(byte[], string?)`
- Produces:
  - `EncodingOption` 记录类型（Key/DisplayName 可显示本地化名）
  - `public IReadOnlyList<EncodingOption> EncodingOptions { get; }` —— 下拉数据源，首项 auto
  - `[ObservableProperty] private EncodingOption? _selectedEncoding` —— 双向绑定 SelectedItem
  - `public bool HasEncodingSelector` —— `PreviewType is Text or Markdown or Html`
  - `private byte[]? _textPreviewBytes` —— 当前文本类预览的原始字节缓存
  - `public string? CurrentDetectedEncodingName` —— auto 模式下显示「自动检测: XX」
  - `private string? CurrentEncodingKey` —— 当前生效编码名（auto 或显式 Key），ShowXxx 与切换共用

- [ ] **Step 1: 新增 EncodingOption 类型 + 编码选项数据源 + 状态属性**

在 `PreviewViewModel.cs` 类顶部（using 区后）新增：

```csharp
/// <summary>编码下拉选项：Key 为 .NET 编码名或 "auto"/"system", DisplayName 为本地化显示名。</summary>
public sealed record EncodingOption(string Key, string DisplayName);
```

类内新增字段与属性（放在现有 `_previewLoadVersion` 字段附近）：

```csharp
private byte[]? _textPreviewBytes;                 // 当前文本类预览的原始字节缓存
private string? _currentEncodingKey;               // 当前生效：null=未初始化, "auto", "system", 或具体编码名
private string? _currentDetectedEncodingName;      // auto 模式下 Ude 检测结果（如 "GB18030"）

/// <summary>编码下拉数据源（固定，首项为自动检测）。</summary>
public IReadOnlyList<EncodingOption> EncodingOptions { get; } = BuildEncodingOptions();

[ObservableProperty]
private EncodingOption? _selectedEncoding;

/// <summary>文本 / Markdown / HTML 预览显示编码选择器。</summary>
public bool HasEncodingSelector =>
    PreviewType is PreviewType.Text or PreviewType.Markdown or PreviewType.Html;

/// <summary>auto 模式下实际检测到的编码名（供 UI 显示「自动检测: GBK」）。</summary>
public string? CurrentDetectedEncodingName => _currentDetectedEncodingName;
```

新增构建选项的静态方法：

```csharp
/// <summary>构建编码下拉选项。首项为自动检测；其余为常用单字节/中文字符编码。</summary>
private static List<EncodingOption> BuildEncodingOptions()
{
    var list = new List<EncodingOption>
    {
        new("auto", LocalizationManager.T("Preview_Encoding_Auto")),
        new("utf-8", "UTF-8"),
        new("utf-16", "UTF-16 LE"),
        new("gbk", "GBK (936)"),
        new("gb18030", "GB18030 (54936)"),
        new("big5", "Big5 (950)"),
        new("shift-jis", "Shift-JIS (932)"),
        new("euc-kr", "EUC-KR (949)"),
    };
    int ansiCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
    list.Add(new("system", LocalizationManager.T("Preview_Encoding_SystemAnsi", ansiCp)));
    return list;
}
```

- [ ] **Step 2: 新增统一解码管线方法**

```csharp
/// <summary>按当前选中编码解码缓存的字节并返回文本。auto/system 走自动检测/系统 ANSI。</summary>
private (string Text, string? DetectedName) DecodePreviewBytes()
{
    if (_textPreviewBytes == null) return (string.Empty, null);

    switch (_currentEncodingKey)
    {
        case null or "auto":
            var (text, name) = TextEncodingDetector.DetectAndDecodeText(_textPreviewBytes);
            _currentDetectedEncodingName = name;
            return (text, name);
        case "system":
            return (TextEncodingDetector.DecodeText(_textPreviewBytes, null), null);
        default:
            return (TextEncodingDetector.DecodeText(_textPreviewBytes, _currentEncodingKey), null);
    }
}

/// <summary>编码下拉切换处理：更新生效编码、持久化偏好、重解码当前预览。</summary>
partial void OnSelectedEncodingChanged(EncodingOption? value)
{
    if (value == null) return;
    _currentEncodingKey = value.Key;
    if (_textPreviewBytes != null)
        ApplyEncodingRefresh();
    PersistEncodingPreference(value.Key);
}

/// <summary>按当前编码重解码并刷新对应预览内容。</summary>
private void ApplyEncodingRefresh()
{
    var (text, _) = DecodePreviewBytes();
    OnPropertyChanged(nameof(CurrentDetectedEncodingName));
    switch (PreviewType)
    {
        case PreviewType.Text:
            TextContent = text;
            break;
        case PreviewType.Markdown:
            RebuildMarkdown(text);
            break;
        case PreviewType.Html:
            _ = RebuildHtmlAsync(text);
            break;
    }
}

/// <summary>持久化编码偏好到 AppSettings.TextEncodingPreference（规则：auto 以外的选择记住）。</summary>
private static void PersistEncodingPreference(string key)
{
    var settings = AppSettings.Load();
    settings.TextEncodingPreference = key;
    settings.Save();
}
```

- [ ] **Step 3: 在 `ShowText` 中使用字节缓存 + 解码管线**

将 `ShowText`（约 line 829）改造为：

```csharp
/// <summary>显示文本预览。</summary>
public void ShowText(string filePath)
{
    _textPreviewBytes = File.ReadAllBytes(filePath);
    var (text, _) = DecodePreviewBytes();
    TextContent = text;
    PreviewType = PreviewType.Text;
    IsPreviewVisible = true;
    IsToolbarVisible = true;
    OnPropertyChanged(nameof(CurrentDetectedEncodingName));
    // 从设置加载文本预览字号和字体（原逻辑保留）...
}
```

- [ ] **Step 4: 构建验证**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`
Expected: 0 错误（此时 `Preview_Encoding_Auto` / `Preview_Encoding_SystemAnsi` key 尚未添加，编译会过但运行时空 key——Task 4 补齐本地化）

- [ ] **Step 5: 提交**

Commit: `feat(avalonia): PreviewViewModel 新增编码选择状态与字节级解码管线`

---

### Task 3: ViewModel — Markdown / HTML 链路解码改造

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs`

**Interfaces:**
- Consumes: Task 2 的 `_textPreviewBytes` / `DecodePreviewBytes()` / `ApplyEncodingRefresh()`
- Produces:
  - `private void RebuildMarkdown(string markdown)` —— 复用 `MarkdownPreviewBuilder.Build`
  - `private async Task RebuildHtmlAsync(string html)` —— fallback 模式重建控件树 / WebView 模式重写临时文件

- [ ] **Step 1: 改造 `ShowMarkdownPreview`**

```csharp
/// <summary>显示 Markdown 预览（Markdig AST → Avalonia 控件树）。</summary>
public void ShowMarkdownPreview(string filePath)
{
    _textPreviewBytes = File.ReadAllBytes(filePath);
    var (markdown, _) = DecodePreviewBytes();
    RebuildMarkdown(markdown);
    PreviewType = PreviewType.Markdown;
    IsPreviewVisible = true;
    IsToolbarVisible = true;
    IsWebViewVisible = false;
}

/// <summary>用 Markdig 构建控件树并同步 HtmlSourceContent（源码视图复用）。</summary>
private void RebuildMarkdown(string markdown)
{
    MarkdownPreviewPanel = MarkdownPreviewBuilder.Build(markdown);
    HtmlSourceContent = markdown;
}
```

- [ ] **Step 2: 改造 `ShowHtmlPreview` / `ShowHtmlFallback`**

`ShowHtmlPreview` 开头（约 line 2682）改为字节缓存 + 解码：

```csharp
public async Task ShowHtmlPreview(string filePath)
{
    _textPreviewBytes = await File.ReadAllBytesAsync(filePath);
    var (html, _) = DecodePreviewBytes();
    // 后续逻辑保持不变，但 fallback 预计算任务改用它解码后的 html
    var fallbackMarkdownTask = Task.Run(() =>
    {
        var converter = new Converter();
        return converter.Convert(html);
    });
    // ...（原 WebView 临时文件写入/CSP/导航逻辑不动）
}
```

`ShowHtmlFallback`（约 line 2742）同步改造：

```csharp
public async Task ShowHtmlFallback(string filePath)
{
    CleanupHtmlTempFile();
    _textPreviewBytes = await File.ReadAllBytesAsync(filePath);
    var (html, _) = DecodePreviewBytes();
    var converter = new Converter();
    var markdown = converter.Convert(html);
    RebuildMarkdown(markdown);
    IsWebViewVisible = false;
    IsFallbackActive = true;
    PreviewType = PreviewType.Html;
    IsPreviewVisible = true;
    IsToolbarVisible = true;
}
```

- [ ] **Step 3: 新增 HTML 切换重渲染方法**

```csharp
/// <summary>HTML 预览切换编码后重渲染：fallback 模式重建控件树；WebView 模式重写临时文件并刷新 Uri。</summary>
private async Task RebuildHtmlAsync(string html)
{
    if (IsFallbackActive)
    {
        var converter = new Converter();
        var markdown = converter.Convert(html);
        RebuildMarkdown(markdown);
        OnPropertyChanged(nameof(MarkdownPreviewPanel));
        return;
    }

    // WebView 模式：重写临时文件（新 Guid 触发 Uri 变化强制刷新）
    CleanupHtmlTempFile();
    var tempHtmlPath = Path.Combine(
        Path.GetTempPath(), "MantisZip", "Preview",
        $"preview_{Guid.NewGuid():N}.html");
    try
    {
        var dir = Path.GetDirectoryName(tempHtmlPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var settings = AppSettings.Load();
        var cspParts = new List<string>();
        cspParts.Add(settings.AllowExternalResources ? "default-src * data: blob:" : "default-src 'self' data: blob:");
        cspParts.Add(settings.AllowJavaScript ? "script-src 'self' 'unsafe-inline'" : "script-src 'none'");
        cspParts.Add("frame-src 'none'");
        var csp = string.Join("; ", cspParts);
        var secureHtml = $"""<meta http-equiv="Content-Security-Policy" content="{csp}">{html}""";
        await File.WriteAllTextAsync(tempHtmlPath, secureHtml);
        _currentHtmlTempPath = tempHtmlPath;
        HtmlWebViewUri = tempHtmlPath;
        HtmlSourceContent = html;
    }
    catch (Exception ex)
    {
        App.DebugLog($"RebuildHtmlAsync: WebView refresh failed ({ex.Message}), falling back to ReverseMarkdown");
        CleanupHtmlTempFile();
        var converter = new Converter();
        var markdown = converter.Convert(html);
        RebuildMarkdown(markdown);
        IsWebViewVisible = false;
        IsFallbackActive = true;
    }
}
```

注意：`_currentHtmlTempPath` 字段已存在（ShowHtmlPreview 使用），`CleanupHtmlTempFile` 已存在。

- [ ] **Step 4: 构建验证 + 提交**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误
Commit: `feat(avalonia): Markdown/HTML 预览接入编码解码管线，切换编码即时重渲染`

---

### Task 4: View — 工具栏编码下拉 ComboBox + 本地化

**Files:**
- Modify: `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json`
- Modify: `src/MantisZip.UI.Avalonia/Localization/strings.en.json`
- Modify: `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs`（keys 数组登记）

**Interfaces:**
- Consumes: Task 2/3 的 `EncodingOptions` / `SelectedEncoding` / `HasEncodingSelector` / `CurrentDetectedEncodingName`
- Produces: 工具栏 ComboBox（字体调节按钮之后、GIF 控件之前）

- [ ] **Step 1: 添加本地化 key（zh + en 成对）**

`strings.zh-CN.json` 文件头 `{` 之后插入：

```json
  "Preview_Encoding_Auto": "自动检测",
  "Preview_Encoding_SystemAnsi": "系统 ANSI ({0})",
  "Preview_Encoding_Detected": "检测到: {0}",
  "Preview_Tooltip_Encoding": "切换文本编码",
```

`strings.en.json` 同步：

```json
  "Preview_Encoding_Auto": "Auto detect",
  "Preview_Encoding_SystemAnsi": "System ANSI ({0})",
  "Preview_Encoding_Detected": "Detected: {0}",
  "Preview_Tooltip_Encoding": "Switch text encoding",
```

- [ ] **Step 2: 登记 XAML 绑定 key 到两个 UpdateLocalizedStrings**

`MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组（约 line 238-295）追加：

```csharp
"Preview_Encoding_Auto", "Preview_Encoding_SystemAnsi", "Preview_Encoding_Detected", "Preview_Tooltip_Encoding",
```

`PreviewViewModel.UpdateLocalizedStrings()`（约 line 111）追加：

```csharp
LocalizedStrings["Preview_Encoding_Auto"] = LocalizationManager.T("Preview_Encoding_Auto");
LocalizedStrings["Preview_Encoding_SystemAnsi"] = LocalizationManager.T("Preview_Encoding_SystemAnsi");
LocalizedStrings["Preview_Encoding_Detected"] = LocalizationManager.T("Preview_Encoding_Detected");
LocalizedStrings["Preview_Tooltip_Encoding"] = LocalizationManager.T("Preview_Tooltip_Encoding");
// 编码下拉选项的 DisplayName 依赖本地化 key，语言切换后重建
foreach (var opt in EncodingOptions)
{
    opt = opt.Key switch
    {
        "auto" => opt with { DisplayName = LocalizationManager.T("Preview_Encoding_Auto") },
        "system" => opt with { DisplayName = LocalizationManager.T("Preview_Encoding_SystemAnsi",
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage) },
        _ => opt,
    };
}
```

注意：`EncodingOptions` 为 `IReadOnlyList`，语言切换重建 display name 需改为可写列表或局部重建——实现时若编译不便，可将 `EncodingOptions` 改为 `List<EncodingOption>` 并支持索引赋值，或添加 `RefreshEncodingOptions()` 方法重建。**实现约束：不能引入 `opt with` 修改只读列表导致无法生效的代码——若列表元素不可变，改为整体重建 `EncodingOptions` 为新的 `List<EncodingOption>` 并触发 `OnPropertyChanged(nameof(EncodingOptions))`。**

- [ ] **Step 3: PreviewPanel.axaml 工具栏加 ComboBox**

在字体调节按钮 StackPanel（`HasFontSizeControls`，约 line 79）之后、GIF 控件之前插入：

```xml
<!-- 编码选择下拉：文本/Markdown/HTML 预览时可用，切换立即按所选编码重解码渲染 -->
<ComboBox ItemsSource="{Binding EncodingOptions}"
          SelectedItem="{Binding SelectedEncoding}"
          IsVisible="{Binding HasEncodingSelector}"
          MinWidth="110" MaxWidth="180"
          Height="{DynamicResource ControlHeight}"
          Margin="{DynamicResource SpacingXxsThk}"
          VerticalAlignment="Center"
          Background="{DynamicResource ThemeSurfaceBgBrush}"
          Foreground="{DynamicResource ThemeTextPrimaryBrush}"
          BorderBrush="{DynamicResource ThemeBorderBrush}"
          ToolTip.Tip="{Binding LocalizedStrings[Preview_Tooltip_Encoding]}">
  <ComboBox.ItemContainerTheme>
    <ControlTheme TargetType="ComboBoxItem">
      <Setter Property="MinHeight" Value="{DynamicResource ControlHeightSm}" />
      <Setter Property="Padding" Value="{DynamicResource SpacingSmThk}" />
    </ControlTheme>
  </ComboBox.ItemContainerTheme>
</ComboBox>
```

- [ ] **Step 4: 构建验证 + 提交**

Run: `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误
Commit: `feat(avalonia): 预览工具栏新增编码选择下拉，支持文本/Markdown/HTML 手动切换编码`

---

### Task 5: 手动验证 + 进度文档 + 收尾

**Files:**
- Modify: `docs/progress-avalonia-detail.md`
- Modify: `docs/PROGRESS.md`
- Modify: `docs/PLAN.md`（计划状态同步，规则 1）

- [ ] **Step 1: 手动验证清单（运行应用）**

Run: `dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`

验证项：
1. 打开含 GBK 文本的压缩包（如 100DVD.rar 的 .txt 条目）→ 预览显示乱码 → 工具栏编码下拉选择 GBK (936) → 乱码消失
2. 打开 GBK 编码的 .md 文件 → 默认乱码 → 切 GBK → Markdown 正常渲染
3. 打开 GBK 编码的 .html 文件 → 切 GBK → HTML 内容正常（WebView 与 fallback 两模式各验证一次）
4. 切换回「自动检测」→ 恢复自动检测渲染，且显示检测到的编码名
5. 重启应用后打开同一文件 → 上次手动选择的编码被记忆（TextEncodingPreference 持久化生效）
6. 中文与英文语言下各验证一次下拉显示名（设置 → 语言切换）

- [ ] **Step 2: 更新进度文档**

`docs/progress-avalonia-detail.md` 顶部（最新日期段）追加：

```markdown
**2026-09-19**
- **feat** — 预览面板工具栏新增编码选择下拉（文本/Markdown/HTML 三链路），手动切换编码即时重渲染；激活原死设置 `TextEncodingPreference`，手动选择持久化（Core `TextEncodingDetector` 新增 `DetectEncoding`/`DetectAndDecodeText`/`DecodeText(byte[], string?)` 字节级 API）
```

`docs/PROGRESS.md` 对应月份段追加一行（里程碑级）：

```markdown
- **09-19** — 文本/Markdown/HTML 预览支持手动选择编码并即时重渲染，偏好持久化
```

`docs/PLAN.md`：将本计划条目从待实现区移除或标记完成。

- [ ] **Step 3: 移动计划文件 + 最终构建测试**

将 `.omo/plans/未开始/text-preview-encoding-selector.md` 移动到 `.omo/plans/已完成/`

Run:
- `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` → 0 错误
- `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj` → 全部 PASS

- [ ] **Step 4: 提交**

Commit: `docs: 进度文档更新 — 文本预览编码选择器上线`

---

## Self-Review 结论

- **Spec 覆盖**：工具栏下拉（Task 4）✓、三条链路（Task 2/3）✓、即时重渲染 ✓、TextEncodingPreference 激活（Task 2 Step 2 PersistEncodingPreference）✓、本地化（Task 4）✓、计划/进度文档（Task 5）✓
- **类型一致性**：`EncodingOption` 记录类型在 Task 2 定义后被 Task 4 使用；`DecodePreviewBytes()` 返回值 `(string Text, string? DetectedName)` 在 Task 2/3 一致；`RebuildMarkdown(string)` / `RebuildHtmlAsync(string)` 在 Task 3 定义并被 Task 2 的 `ApplyEncodingRefresh()` 调用
- **已知风险**：`EncodingOptions` 语言切换重建（Task 4 Step 2 注释已给出两种实现方案）；`utf-16` 编码名对应 UTF-16 LE（.NET 标准名），非 BOM 的 UTF-16 文本可能以空格开头——属预期行为，用户可再切回 auto