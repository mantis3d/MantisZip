# 文本预览语法高亮（AvaloniaEdit + TextMate 集成）

> **状态**: 📋 计划中 | **阶段**: 方案设计完成（2026-09-21 重写为 Avalonia 版）
> **原版**: WPF 时代方案（AvalonEdit + 自定义 XSHD）——AvalonEdit 是 WPF-only 控件，已废弃，本版全部重写

## TL;DR

> **核心目标**: 将当前文本预览的纯 `TextBox` 替换为 AvaloniaEdit `TextEditor`，为 `PreviewService.TextExtensions` 中 40+ 种代码/配置文件扩展名提供语法高亮，亮/暗主题联动。
>
> **技术选型**: **TextMate 方案**（`AvaloniaEdit.TextMate` + TextMateSharp）而非 XSHD——
> - VS Code 同款 `.tmLanguage` 语法，覆盖面远超 AvalonEdit 内置 21 个 XSHD（TypeScript/Go/Rust/YAML/TOML 等开箱即用，**无需自研自定义 XSHD**）
> - 内置 DarkPlus/LightPlus 主题，运行时可 `SetTheme()` 切换，天然解决亮/暗主题联动（XSHD 颜色硬编码不跟随主题，需手工映射）
>
> **交付物**:
> - NuGet `Avalonia.AvaloniaEdit` 12.0.0 + `AvaloniaEdit.TextMate` 12.0.0 引入
> - XAML `ScrollViewer+TextBox` → `TextEditor` 替换（TextMate 自带滚动，去掉外层 ScrollViewer）
> - 基于扩展名/检测格式的语法分发（`GetLanguageByExtension` + 魔数结构特征兜底）
> - 亮/暗主题联动（复用现有 `ActualThemeVariantChanged` 订阅模式）
> - 编码切换兼容（`ApplyEncodingRefresh` 保持生效）
> - 未匹配语言时降级为纯文本
>
> **预估**: ~4-5h（TextMate 免去原方案自定义 XSHD 的 ~2.5h 工作量）
> **并行执行**: 是 — 依赖确认（包版本/Avalonia 兼容）→ XAML 替换（Phase 1）→ 语法分发（Phase 2）可并行
> **关键路径**: 包引入验证 → XAML 替换 → 语法分发 → 主题联动 → 测试

---

## Context

### 原始需求

来自 PLAN.md P2 任务：将文本预览的 TextBox 升级为语法高亮显示，支持 20+ 语言。重写为 Avalonia 版（规则 11：新功能只在 Avalonia 开发）。

### 当前实现（Avalonia 版）

- `PreviewService.cs:44-54` — `TextExtensions` HashSet 定义 **40+ 种文本扩展名**（`.txt .log .ini .cfg .conf .xml .json .cs .csproj .yaml .yml .toml .sh .bat .cmd .ps1 .py .js .ts .tsx .css .scss .less .sql .gitignore .editorconfig .sln .props .targets .ruleset .rc .resx .nuspec .gradle .dockerfile .env .h .c .cpp .hpp .swift .kt .java .rb .go .rs .php .vue`）
- `PreviewService.cs:178` — `TextExtensions.Contains(ext)` → `PreviewType.Text`
- `PreviewPanel.axaml:282-291` — 文本预览为 `<ScrollViewer IsVisible="{Binding IsTextVisible}"><TextBox Text="{Binding TextContent}" IsReadOnly="True" TextWrapping="Wrap" FontFamily/FontSize 绑定/></ScrollViewer>`
- `PreviewViewModel.cs:1019 ShowText(filePath)` — 读取临时文件 → `DecodePreviewBytes()` 解码 → `TextContent = text`
- `PreviewViewModel.cs:928 ApplyEncodingRefresh()` — 编码下拉切换后按 `PreviewType` 分支重解码：`Text` → `TextContent = text`
- `PreviewViewModel.cs:1602-1618 SubscribeThemeChanged/UnsubscribeThemeChanged` — 已有 `app.ActualThemeVariantChanged += OnAppThemeChanged` 订阅先例（字体预览用）
- `MainWindowViewModel.cs:1421-1440` — 预览分发：`case PreviewType.Text: Preview.ShowText(tempFile)`（`MaxTextPreviewBytes` 上限在此检查）
- HTML/Markdown/CSV 走独立预览路径（`PreviewType.Html/Markdown/Csv`），不受本计划影响

### 调研结论（2026-09-21，librarian)

**推荐库: AvaloniaEdit**

| 属性 | 值 |
|---|---|
| NuGet 包 | `Avalonia.AvaloniaEdit` |
| 最新稳定版 | **12.0.0**（2026-04-08 发布） |
| 许可证 | MIT |
| 维护者 | AvaloniaUI 官方团队（活跃） |
| 目标框架 | `net8.0` + `net10.0` |
| Avalonia 兼容 | **v12.0.0 要求 Avalonia ≥ 12.0.0**（本项目 12.0.4 ✅） |

**TextMate 集成包**（独立安装）:

| 属性 | 值 |
|---|---|
| NuGet 包 | `AvaloniaEdit.TextMate` 12.0.0 |
| 依赖 | `Avalonia.AvaloniaEdit ≥ 12.0.0` + `TextMateSharp ≥ 2.0.3` + `TextMateSharp.Grammars ≥ 2.0.3` |

**核心 API**:

```csharp
// 语法分发（扩展名 → 语言 → grammar scope）
var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
var textMateInstallation = editor.InstallTextMate(registryOptions);
var lang = registryOptions.GetLanguageByExtension(fileExtension); // 未匹配返回 null
if (lang != null)
    textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(lang.Id));

// 主题切换（运行时可换）
textMateInstallation.SetTheme(registryOptions.LoadTheme(ThemeName.LightPlus)); // 或 DarkPlus
```

**性能**: 内部 rope-based `TextDocument` + 行虚拟化（仅渲染可见行），配合现有 `MaxTextPreviewBytes`（默认 5MB）上限，大文件无压力。设置内容优先用 `editor.Document = new TextDocument(text)`（大字符串避免重复拷贝）。

**Gotchas**:
- v11→v12 断 netstandard2.0/net6.0，本项目必须用 v12（Avalonia 12.0.4）
- TextMate 主题色不随控件 Foreground 继承——暗色模式下需 `SetTheme(DarkPlus)`，不是靠 `ThemeSurfaceBgBrush` 覆盖
- 只读模式仍显示 caret：纯预览需 `editor.TextArea.Caret.IsHidden = true`
- 每个 `InstallTextMate()` 调用独立主题状态；多个编辑器共享 `RegistryOptions` 但各有 `Installation`
- `Text={"..."}` 绑定可用但每次赋值建新 Document——大文件建议 code-behind 设置 Document

**备选方案**（不采用）: `SyntaxColorizer`（单人项目、功能简单）、`Huskui.Avalonia.Code`（非独立）、`TextEdit`（WIP 未生产级）。AvaloniaEdit 是唯一成熟选择。

### 关键约束

- `ShowText` 已接收 `filePath` 参数（temp 文件路径），`Path.GetExtension` 可取扩展名
- `.md/.markdown/.html/.csv` 不在 `TextExtensions`（走独立预览器），TextMate 语法分发只覆盖 Text 预览路径
- `TextPreviewFontFamily`/`FontSize` 绑定需迁移到 TextEditor（TextEditor 支持标准 `FontFamily`/`FontSize`）
- 编码检测与选择器（`DecodePreviewBytes`/`ApplyEncodingRefresh`）保持不变，只替换显示控件
- 新 UI 控件须应用主题样式（规则 4）——TextEditor 背景/前景绑定 `ThemeSurfaceBgBrush`/`ThemeTextPrimaryBrush`

---

## 架构设计（本会话已确认）

### PreviewType（查看器）与 Language（高亮）分离

| 层 | 职责 | 现状 |
|----|------|------|
| `PreviewType` | 决定**用什么查看器**（Text/Csv/Markdown/Html/...） | 已有，不变 |
| `Language` | 决定**文本如何高亮**（只作用于 Text 预览器） | 本计划引入 |

### 语言识别优先级链（Future-ready）

| 优先级 | 来源 | 说明 | 状态 |
|:---:|------|------|:---:|
| 1 | 扩展名 | `GetLanguageByExtension(.cs → csharp)` 主路径 | 本计划 |
| 2 | 魔数 | `FileFormatDetector.Detect`（XML/SVG 魔数 → 对应语言） | Phase 2 增强 |
| 3 | 结构特征 | JSON/INI 内容识别（2026-09-21 已落地，`LooksLikeJson`/`LooksLikeIni`）→ `.txt` 文件实为 JSON 时也能高亮 | Phase 2 增强 |
| 4 | 纯文本 | 无匹配 → 无高亮 | 本计划 |

> 说明：Markdown 是纯文本超集、无法内容识别（已确认架构结论）；CSV 已有独立查看器 + 扩展名兜底，均不需语法高亮。

---

## Work Objectives

### 核心目标

将文本预览从纯文本升级为语法高亮显示，覆盖 `TextExtensions` 40+ 种扩展名，亮/暗主题联动。

### 可量化指标

| 指标 | 目标 |
|------|------|
| TextExtensions 覆盖 | 40+ 种全部命中 TextMate grammar（`GetLanguageByExtension` 预期绝大部分非 null） |
| 主题同步 | 亮/暗切换时语法颜色实时跟随（SetTheme） |
| 性能 | 打开 5MB 文本无卡顿（TextDocument + 行虚拟化 + 现有 MaxTextPreviewBytes） |
| 降级 | 未匹配语言仍显示纯文本，不崩溃 |
| 回归 | 编码选择器、A−/A+ 字号、字体切换全部保持可用 |

### 非目标

- 不替换 HTML/Markdown/CSV 预览路径（独立查看器）
- 不做编辑功能（`IsReadOnly=true` 不变）
- 不做行号/代码折叠/自动补全等 IDE 功能
- 不引入自研 XSHD 定义（TextMate 覆盖面已足够）

---

## Work Breakdown

### Phase 0: 调研确认（已完成）

- [x] AvaloniaEdit 版本确认：12.0.0（要求 Avalonia ≥12.0.0，本项目 12.0.4 ✅）
- [x] TextMate 方案确认：`AvaloniaEdit.TextMate` + `TextMateSharp.Grammars`（含 VS Code 语法全集）
- [x] 主题切换 API 确认：`Installation.SetTheme(RegistryOptions.LoadTheme(ThemeName.X))`
- [x] 现状代码梳理（TextExtensions / PreviewPanel / ShowText / ApplyEncodingRefresh / 主题订阅先例）

### Phase 1: 基础集成（~1.5h）

#### Task 1.1 — NuGet 包引入（~0.2h）

```xml
<!-- MantisZip.UI.Avalonia.csproj -->
<PackageReference Include="Avalonia.AvaloniaEdit" Version="12.0.0" />
<PackageReference Include="AvaloniaEdit.TextMate" Version="12.0.0" />
```

验证：`dotnet build` 通过，确认与 Avalonia 12.0.4 无冲突。

#### Task 1.2 — XAML 替换（~0.8h）

`PreviewPanel.axaml:281-291`，`ScrollViewer+TextBox` → `TextEditor`：

```xml
<!-- 文本预览：AvaloniaEdit 只读编辑器，TextMate 语法高亮 -->
<mvaEdit:TextEditor x:Name="PreviewTextEditor"
                    IsVisible="{Binding IsTextVisible}"
                    IsReadOnly="True"
                    WordWrap="True"
                    FontFamily="{Binding TextPreviewFontFamily}"
                    FontSize="{Binding FontSize}"
                    Background="{DynamicResource ThemeSurfaceBgBrush}"
                    Foreground="{DynamicResource ThemeTextPrimaryBrush}"
                    HorizontalScrollBarVisibility="Auto"
                    VerticalScrollBarVisibility="Auto" />
```

- xmlns: `xmlns:mvaEdit="using:AvaloniaEdit"`（Avalonia 命名空间语法，非 WPF 的 URI 语法）
- 去掉外层 ScrollViewer（TextEditor 自带滚动）
- 新增控件须中注释（规则 14）+ 主题样式（规则 4）

#### Task 1.3 — 内容设置 refactor（~0.5h）

`TextEditor` 无 `TextContent` 绑定兼容层顾虑，直接 code-behind 驱动：

```csharp
// PreviewPanel.axaml.cs
public partial class PreviewPanel : UserControl
{
    private TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _registryOptions;
    private TextEditor _editor => PreviewTextEditor;

    /// <summary>加载文本内容（配套 PreviewViewModel.ShowText / ApplyEncodingRefresh）。</summary>
    public void SetPreviewText(string text)
    {
        _editor.Document = new TextDocument(text); // 大字符串避免重复拷贝
        // caret 隐藏（纯预览）——TextMate.Installation 生效前调用，防闪烁
        if (!_editor.TextArea.Caret.IsHidden) _editor.TextArea.Caret.IsHidden = true;
    }
}
```

在 `PreviewViewModel.TextContent` setter 与 View 建立单向桥（View 订阅 `PropertyChanged` 或 VM 暴露事件，最小侵入方案：View 监听 `DataContext` 的 `PropertyChanged`，`TextContent` 变化 → `SetPreviewText`）。

替代方案：保留 XAML `Text="{Binding TextContent}"` 绑定（Text setter 内部建新 Document）——接入最快，5MB 上限下可接受；若性能/闪烁不佳再切 code-behind。**建议先走绑定，Task 4.1 验证后再决定**。

### Phase 2: 语法分发（~1h）

`ShowText` 路径中，VM 需把「检测到的 Language 标识」传给 View（如 `PreviewLanguage` 属性），View 据此设置 grammar：

```csharp
// PreviewPanel.axaml.cs
public void SetHighlightLanguage(string scopeName)  // 或 fileExtension 直接传
{
    _registryOptions ??= new RegistryOptions(ThemeName.DarkPlus); // 初值暗色，主题联动见 Phase 3
    _textMateInstallation ??= _editor.InstallTextMate(_registryOptions);

    var lang = scopeExtensions(当前文件名扩展名);
    if (lang == null) { _textMateInstallation.SetGrammar(null); return; } // 降级纯文本
    _textMateInstallation.SetGrammar(_registryOptions.GetScopeByLanguageId(lang.Id));
}
```

优先级链实现（螺纹进 `PreviewViewModel.ShowText` 或独立 `LanguageResolver`）：

1. **扩展名**：`registryOptions.GetLanguageByExtension(Path.GetExtension(filePath))`（null → 下一步）
2. **魔数/结构特征增强**（低优先，可后置）：若扩展名未命中但 `ClassifyPreviewByMagicAsync` 已识别 `FileFormat.Json/Xml/Ini`（含 `.txt` 实际是 JSON 的内容识别场景），用对应语法（`json`/`xml`/`ini` scope）

### Phase 3: 主题联动（~0.8h）

复用 `PreviewViewModel.cs:1602-1618` 的 `ActualThemeVariantChanged` 订阅模式（字体预览已用），或迁移到 View：

```csharp
// PreviewPanel.axaml.cs — 挂载/卸载
protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);
    if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnAppThemeChanged;
}
protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
{
    Application.Current.ActualThemeVariantChanged -= OnAppThemeChanged;
    base.OnDetachedFromVisualTree(e);
}

private void OnAppThemeChanged(object? sender, EventArgs e)
{
    var isDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
    if (_textMateInstallation != null && _registryOptions != null)
    {
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(
            isDark ? ThemeName.DarkPlus : ThemeName.LightPlus));
    }
    // 编辑器底色仍走 DynamicResource ThemeSurfaceBgBrush，语法色由 TextMate 主题管理
}
```

要点：语法着色由 TextMate 主题控制，不依赖控件 Foreground；背景保持 `ThemeSurfaceBgBrush` 资源。

### Phase 4: 兼容性回归 + 集成测试（~1h）

#### Task 4.1 — 内容路由验证（编码切换/字号/字体）

- `ApplyEncodingRefresh` Text 分支 → `TextContent` 变化 → TextEditor Document 更新（绑定方案自动生效；code-behind 方案手动桥）
- A−/A+ 字号按钮（`FontSize` 绑定）→ TextEditor.FontSize
- `TextPreviewFontFamily` 绑定 → TextEditor.FontFamily
- `TextContent` 清空/`Clear()` 时 Document 清空（`PreviewType` 切换时 `TextContent = string.Empty` 正常）

#### Task 4.2 — 主题切换回归

亮 → 暗 → 亮循环：语法色 / 编辑器背景 / 状态栏均正确。

#### Task 4.3 — 降级与性能

- 未知扩展名（如 `.dat` 纯文本）→ 无高亮，纯文本显示
- 5MB 大文件打开流畅，滚动无卡顿
- 10+ 种语言（.cs/.py/.ts/.go/.rs/.yaml/.json/.xml/.sh/.java）抽查高亮正确

### Phase 5: 计划后置增强（可选，独立立项）

| 项 | 说明 |
|----|------|
| 高亮开关 | `AppSettings.TextPreviewHighlight`（默认 true），关闭时 SetGrammar(null) |
| WordWrap 开关 | 代码预览默认不换行（现默认 Wrap），新增设置 |
| 行号 | `ShowLineNumbers=true`（原计划非目标，如需再议） |
| 结构化 .txt 高亮 | `.txt` 文件经 JSON/INI 内容识别命中时用对应 grammar（Phase 2 优先级链第 2 条） |

---

## 决策记录

### 1. 高亮引擎（TextMate vs XSHD）

| 选项 | 工作量 | 语言覆盖 | 主题联动 |
|------|:------:|:--------:|:--------:|
| A: XSHD（AvalonEdit 内置 21 个） | ~2h | 21 种内置 + 需自研自定义（TS/Go/YAML 等缺失） | 需手工映射语义色 |
| **B: TextMate（推荐）** | **~1h** | VS Code 语法全集（TextExtensions 全覆盖，无自研） | **内置 DarkPlus/LightPlus，SetTheme 一行切换** |
| C: 混合（TextMate 主 + XSHD 补） | ~2.5h | 全覆盖 | 混合管理复杂 |

**选 B**：覆盖、主题、工作量全面胜出；无 XSHD 自研负担（原 WPF 方案需自研 TS/Go/YAML 等 7-13 个 XSHD 的核心痛点消失）。

### 2. 内容接入方式（绑定 vs code-behind）

| 选项 | 工作量 | 性能 | 备注 |
|------|:------:|:----:|------|
| A: `Text="{Binding TextContent}"`（初始） | 极小 | 可接受（Text setter 每次建新 Document） | 5MB 上限下足够，零额外代码 |
| B: code-behind Document 注入 | ~0.3h | 更优（复用 Document） | 需 VM→View 桥（PropertyChanged 订阅），闪烁可控 |

**建议**：先 A 后 B——A 快速落地验证功能，若编码切换/大文件出现性能或闪烁问题，升级 B（Task 4.1 决策点）。

---

## 依赖与风险

### 外部依赖

| 依赖 | 版本 | 许可证 | 用途 |
|------|------|--------|------|
| `Avalonia.AvaloniaEdit` | 12.0.0 | MIT | 语法高亮只读编辑器 |
| `AvaloniaEdit.TextMate` | 12.0.0 | MIT | TextMate 语法 + 主题引擎 |
| `TextMateSharp` + `.Grammars` | ≥2.0.3（传递） | MIT | .tmLanguage 解析 + VS Code 语法集 |

### 风险

| 风险 | 概率 | 影响 | 缓解 |
|------|:----:|:----:|------|
| AvaloniaEdit 12.0.0 与 Avalonia 12.0.4 兼容问题 | 🟢 低 | 🔴 高 | Phase 1.1 先加包 build 验证再动手改 XAML |
| 暗色主题下语法色不可读 | 🟢 低 | 🟡 中 | TextMate 内置 DarkPlus 主题为 VS Code 暗色配色，天然适配；验证走 Task 4.2 |
| 大文件性能退化 | 🟢 低 | 🟡 中 | TextDocument + 行虚拟化 + 现有 MaxTextPreviewBytes |
| TextMate 初始化在主题切换前用错初值主题 | 🟡 中 | 🟢 低 | 初值用 `RequestedThemeVariant==Dark ? DarkPlus : LightPlus` 而非硬编码 DarkPlus |
| `Text` 绑定频繁换 Document 引起闪烁 | 🟡 中 | 🟢 低 | 升级 code-behind Document 注入（决策 2 的 B 方案） |
| 安装包体积增加 | 🟢 低 | 🟢 低 | TextMateSharp.Grammars 含全部语法约数 MB，按需裁剪语法集可再议 |

---

## 验收标准

- [ ] `Avalonia.AvaloniaEdit` 12.0.0 + `AvaloniaEdit.TextMate` 12.0.0 引入后 build 0 错误
- [ ] `PreviewType.Text` 预览用 TextEditor 显示，无 ScrollViewer 残留
- [ ] 至少 10 种代表语言（.cs/.py/.ts/.go/.rs/.yaml/.json/.xml/.sh/.java）语法高亮正确
- [ ] 未知扩展名降级纯文本（不崩溃、无高亮）
- [ ] 亮/暗主题切换后语法色实时更新
- [ ] A−/A+ 字号、字体切换、编码选择器切换全部保持可用
- [ ] `MaxTextPreviewBytes` 限制仍生效
- [ ] 5MB 大文件打开与滚动无明显卡顿
- [ ] 无 `PreviewTextBox`/WPF 时代引用残留

---

## 滚动计划（可选）

如果工作量超出预期，可裁剪：

| 阶段 | 内容 | 工时 |
|:----:|------|:----:|
| **P0** | NuGet + XAML 替换 + `Text` 绑定接入 | ~1.5h |
| **P1** | 扩展名语法分发（TextMate 基础路径） | ~1h |
| **P2** | 主题联动（SetTheme） | ~0.8h |
| **P3** | 结构化 .txt 高亮（魔数/JSON/INI 兜底）+ code-behind Document 优化 | ~1h |