# 字体预览连字（Ligature）效果开关

## 目标

Avalonia 字体预览中，对支持 OpenType 连字的字体（如 Fira Code、Cascadia Code、JetBrains Mono）提供连字效果切换。工具栏始终显示连字按钮，不支持时灰色禁用。

## 技术路线

**HarfBuzzSharp** — HarfBuzz 的 .NET 绑定（MIT 许可），与 SkiaSharp 配合成熟。提供 OpenType 文本整形（shaping），支持控制 `liga` feature。

```
当前: 文本 → SKCanvas.DrawText()               ← Skia 简单映射，无 GSUB
改造: 文本 → HarfBuzz Buffer → Shape() → glyphs → SKCanvas.DrawPositionedGlyphs()
```

## 依赖

- 新增 NuGet: `HarfBuzzSharp`（MIT, ~100KB）
- 无其他外部依赖。HarfBuzzSharp 和 SkiaSharp 由同一团队维护，API 设计一致。

## 实现步骤

### Step 1: 连字检测（判断字体是否支持 liga）

```csharp
// 用 HarfBuzz 检查 OpenType GSUB 表
var hbFont = new HarfBuzzSharp.Font(typeface);  // 包裹 SKTypeface
var face = hbFont.Face;
// 枚举 GSUB 表的 feature 列表，检查是否有 'liga'
bool supportsLigatures = face.GetTableTags(HarfBuzzSharp.TableTag.GSUB)
    .SelectMany(tag => /* 解析 FeatureList 找 'liga' tag */)
    .Any();
```

或者更简单的做法：用 HarfBuzz shaper 对文本做一次 shape，比较开启了 `liga` 和关闭 `liga` 得到的 glyph 数量/序列是否不同。如果不同 → 支持连字。这个办法更贴近实际渲染结果。

### Step 2: 添加 HarfBuzzSharp NuGet

```bash
dotnet add src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj package HarfBuzzSharp
```

### Step 3: 重构 RenderFontPreview — 引入 HarfBuzz shaping 管线

当前 `RenderFontPreview` 用 `DrawText(text, x, y, font, paint)`，改造为：

```
每行文本 → HarfBuzz.Buffer
  → Buffer.AddUtf8(text)
  → Buffer.GuessSegmentProperties()
  → Font.Shape(buffer)
  → 取出 GlyphInfo[] + GlyphPosition[]
  → SKCanvas.DrawPositionedGlyphs(glyphs, positions, ...)
```

关键点：

- **折行逻辑仍需保留**：HarfBuzz 对整个字符串 shape → 得到 glyph 序列 → 按 glyph advance 折行。折行后每行分别 shape。
- **连字开关**：shape 前设置 `buffer.Features = new[] { new FontFeature('liga', enabled ? 1 : 0) }`
- **折行后每行重新 shape**：因为不同行的上下文不同（`Buffer` 输入不同文本），连字只在单行内生效是正常的（跨行不会有连字）。

### Step 4: 修改工具栏

- PreviewViewModel 新增属性：
  - `HasLigatureToggle`（bool）— 总是 true（按钮一直显示）
  - `IsLigatureEnabled`（bool）— 连字开关，从 AppSettings 初始化
  - `CanLigatureToggle`（bool）— 当前字体是否支持连字，控制按钮 Enabled/灰色
- 工具栏：SetToolbar 右侧加一个连字 toggle 按钮 ("连字" / "Ligature")
- 按钮状态绑定到 `CanLigatureToggle`
- 点击时切换 `IsLigatureEnabled`，触发 ReRenderFontPreview

### Step 5: AppSettings 持久化

在 AppSettings 添加：

```csharp
/// <summary>字体预览是否启用连字效果（仅对支持连字的字体有效）。</summary>
public bool FontPreviewEnableLigature { get; set; } = true;
```

settings.json 新增 `FontPreviewEnableLigature` 字段。

### Step 6: 连字检测（决定 CanLigatureToggle）

在 `ShowFont` 中，对加载的字体做一次 HarfBuzz shape 对比：

```csharp
// 带 liga vs 不带 liga，glyph 序列不同 → 支持连字
_canLigature = CheckFontSupportsLigature(typeface);
OnPropertyChanged(nameof(CanLigatureToggle));
```

检测只在 ShowFont 时做一次（切换字体时重新检测）。

### Step 7: 联调验证

用以下字体验证：
1. **Fira Code** — 支持 `liga`（`!=` → 不等号连字）
2. **Cascadia Code** — 支持 `liga`
3. **JetBrains Mono** — 支持 `liga`
4. **Arial / 宋体** — 不支持 `liga`，按钮灰色

验证内容：
- 默认打开连字时，Fira Code 的 `!=` `->` `===` 显示为连字 glyph
- 关闭连字时，回退到普通字符序列
- 不支持连字的字体按钮灰色不可点
- 窗口缩放 re-render 后连字状态保持
- 切换字体后重新检测

## 涉及文件

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | +HarfBuzzSharp NuGet |
| `src/MantisZip.UI.Avalonia/ViewModels/PreviewViewModel.cs` | 新增 Ligature 属性、检测逻辑、shaping 渲染 |
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | +FontPreviewEnableLigature |
| `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml` | 工具栏按钮（如需要） |
| `src/MantisZip.UI.Avalonia/Views/PreviewPanel.axaml.cs` | toolbar 绑定（如需要） |

## 不做的事

- WPF 端不做连字（WPF 将被 Avalonia 取代）
- 不支持其他 OpenType feature 的 UI 控制（只做 `liga` 开关）
- 不做复杂 UI 设置界面（只在预览工具栏有一个 toggle）
