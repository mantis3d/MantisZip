# 文本预览语法高亮 (AvalonEdit 集成)

> **状态**: 📋 计划中 | **阶段**: 方案设计完成

## TL;DR

> **核心目标**: 将当前文本预览的纯 `TextBox` 替换为 AvalonEdit `TextEditor`，为 40+ 种文件扩展名提供语法高亮。
>
> **交付物**:
> - NuGet `AvalonEdit` 包引入，XAML `TextBox` → `TextEditor` 替换
> - 基于扩展名的自动高亮分发（`GetDefinitionByExtension`）
> - 自定义 XSHD 定义 10–12 种无内置高亮的语言
> - 亮/暗主题同步（AvalonEdit 前景/背景/关键字色跟随主题切换）
> - 字号工具栏适配（保持 A−/A+ 功能）
> - 未找到高亮定义时降级为纯文本
>
> **预估**: ~7h
> **并行执行**: 是 — 自定义 XSHD（3–4 个独立文件）可与 XAML 替换并行开发
> **关键路径**: 主题色同步方案 → XAML 替换 → XSHD 嵌入 → 测试

---

## Context

### 原始需求

来自 PLAN.md P2 任务：将文本预览的 TextBox 用 AvalonEdit 替换，支持 20+ 语言语法高亮。

### 当前实现

- `MainWindow.xaml:660` — `PreviewTextBox` 是一个普通 `TextBox`，`IsReadOnly=True`，等宽字体
- `ShowTextPreview`（`MainWindow.Preview.Text.cs:99`）— 将文本内容赋值给 `PreviewTextBox.Text`
- `ChangeTextFontSize` — 通过 `PreviewTextBox.FontSize` 调节字号
- 40+ 种扩展名在 `TextExtensions` HashSet 中定义（`.cs`/`.py`/`.js`/`.rs`/`.go`/`.xml`/`.json`/`.sql` 等）
- 所有文件统一纯文本显示，无任何高亮

### 调研结论

AvalonEdit （NuGet `AvalonEdit`，MIT 许可证）是 SharpDevelop/ILSpy 使用的 WPF 文本编辑器组件。

**内置高亮语言（21 个 XSHD）**:

| XSHD 文件 | 匹配扩展名 | 在我们的 TextExtensions 中？ |
|-----------|-----------|:---:|
| CSharp-Mode.xshd | `.cs` | ✅ |
| XML-Mode.xshd | `.xml`, `.csproj`, `.sln`, `.props`, `.targets`, `.ruleset`, `.resx`, `.nuspec` | ✅ |
| JSON-Mode.xshd | `.json`, `.json5` | ✅ |
| JavaScript-Mode.xshd | `.js` | ✅ |
| CSS-Mode.xshd | `.css` | ✅ |
| HTML-Mode.xshd | `.html`, `.htm` | (.htm 不在 TextExtensions) |
| CPP-Mode.xshd | `.c`, `.cpp`, `.h`, `.hpp` | ✅ |
| Java-Mode.xshd | `.java` | ✅ |
| Python-Mode.xshd | `.py` | ✅ |
| PHP-Mode.xshd | `.php` | ✅ |
| PowerShell.xshd | `.ps1` | ✅ |
| TSQL-Mode.xshd | `.sql` | ✅ |
| VB-Mode.xshd | — | — |
| ASPX.xshd | — | — |
| Boo.xshd | — | — |
| MarkDown-Mode.xshd | `.md`, `.markdown` | (已由 Markdown 预览处理) |
| Patch-Mode.xshd | — | — |
| Tex-Mode.xshd | — | — |
| Coco-Mode.xshd | — | — |

**需要自定义 XSHD 的语言**（在 TextExtensions 中有但无内置）:

| 扩展名 | 语言 | 优先级 | 方案 |
|--------|------|:------:|------|
| `.ts`, `.tsx` | TypeScript/TSX | 🔴 高 | 社区 XSHD 或基于 JavaScript-Mode 修改 |
| `.go` | Go | 🔴 高 | 社区 XSHD |
| `.rs` | Rust | 🟡 中 | 社区 XSHD |
| `.sh`, `.bash` | Shell Script | 🟡 中 | 社区 XSHD（已有） |
| `.yaml`, `.yml` | YAML | 🟡 中 | 社区 XSHD |
| `.toml` | TOML | 🟢 低 | 简易 XSHD（只有注释 + 键值对） |
| `.swift` | Swift | 🟢 低 | 社区 XSHD |
| `.kt` | Kotlin | 🟢 低 | 基于 Java-Mode 修改 |
| `.rb` | Ruby | 🟢 低 | 社区 XSHD |
| `.bat`, `.cmd` | Batch | 🟢 低 | 简易 XSHD |
| `.gradle` | Gradle | 🟢 低 | Groovy 风格，简易 XSHD |
| `.dockerfile` | Dockerfile | 🟢 低 | 简易 XSHD（指令高亮） |
| `.vue` | Vue | 🟢 低 | 基于 HTML + JS 组合 |

**基准建议**: 高优先级（4 种）必须做，中优先级（3 种）推荐做，低优先级（6 种）可延后。

### 关键约束

- 当前 `ShowTextPreview` 接收 `extension` 参数但未使用 → 正可用于 `GetDefinitionByExtension`
- HTML/Markdown 预览走独立路径（WebView2），不经过 `ShowTextPreview`，不受影响
- `.md`/`.markdown` 不在 `TextExtensions` 中（在 `MarkdownExtensions` 中），AvalonEdit 内置的 MarkDown-Mode 用不上
- `PreviewTextBox.FontSize` 调节（A−/A+ 按钮）需改为 `textEditor.FontSize`
- 编码检测逻辑（`DetectAndReadText`）保持不变，只替换显示控件

---

## Work Objectives

### 核心目标

将文本预览从纯文本升级为语法高亮显示，覆盖 40+ 种扩展名，支持亮/暗主题联动。

### 可量化指标

| 指标 | 目标 |
|------|------|
| 内置高亮覆盖 | 14/24 种有匹配内置 XSHD |
| 自定义 XSHD 覆盖（高优） | 4 种（`.ts`/`.tsx`/`.go`/`.sh`） |
| 主题同步 | 亮/暗色切换时编辑器颜色实时跟随 |
| 性能 | 打开 5MB 文本文件无明显卡顿（借助现有 `MaxTextPreviewBytes`） |
| 降级 | 未匹配高亮定义的文件仍正常显示纯文本 |

### 非目标

- 不替换 HTML/Markdown/CSV 预览路径（它们走独立的 WebView2/DataGrid）
- 不做编辑功能（`IsReadOnly=true` 不变）
- 不做行号显示（预览场景不需要）
- 不做代码折叠/自动补全等 IDE 功能

---

## Work Breakdown

### Phase 0: 调研确认（已完成）

- AvalonEdit API 确认：`TextEditor` 控件、`SyntaxHighlighting` 属性、`FontSize` 兼容
- 内置 XSHD 列表确认（21 个）
- 自定义 XSHD 加载方案确认：嵌入为资源 → `HighlightingLoader.Load` → `RegisterHighlighting`

### Phase 1: 基础集成（~2h，可并行）

#### Task 1.1 — NuGet 包引入 + XAML 替换（~1h）

1. 在 `MantisZip.UI.csproj` 中添加 `<PackageReference Include="AvalonEdit" Version="..." />`
2. `MainWindow.xaml`:
   - 在 Window 级别添加 xmlns: `xmlns:avalonEdit="http://icsharpcode.net/sharpdevelop/avalonedit"`
   - 将 `PreviewTextBox` 的 `<TextBox>` 替换为 `<avalonEdit:TextEditor>`
   - 映射属性：`IsReadOnly="True"`, `FontFamily`, `FontSize`, `WordWrap`, `HorizontalScrollBarVisibility`, `VerticalScrollBarVisibility`
   - 移除 `BorderThickness`（AvalonEdit 无此属性）
3. `MainWindow.Preview.Text.cs`: `PreviewTextBox.Text = content` → `PreviewTextBox.Document.Text = content`（或 `.Text` setter）

**风险**: AvalonEdit 的 `Text` property setter 内部会创建新 Document，等价于 `Text = content` 是可用的。

#### Task 1.2 — 语法高亮分发（~1h）

在 `ShowTextPreview` 中，在设置内容后添加：

```csharp
var ext = Path.GetExtension(filePath); // 或使用传入的 extension 参数
textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
// 未匹配到定义时 GetDefinitionByExtension 返回 null → 纯文本显示
```

`GetDefinitionByExtension` 内部会用内置注册的扩展名匹配。对于自定义 XSHD 需先注册。

**问题**: `GetDefinitionByExtension` 只匹配 AvalonEdit 内置注册的扩展名。自定义 XSHD 注册时需指定扩展名列表，之后就可以用同一 API。

### Phase 2: 自定义 XSHD（~2.5h，可并行）

所有 XSHD 文件作为**嵌入资源**（`EmbeddedResource`）放在 `MainWindow/Preview/Highlighting/` 目录下。

启动时统一注册：

```csharp
// App.OnStartup 或 MainWindow 静态构造中
var manager = HighlightingManager.Instance;
var assembly = typeof(MainWindow).Assembly;
foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".xshd")))
{
    using var stream = assembly.GetManifestResourceStream(name);
    using var reader = new XmlTextReader(stream);
    var def = HighlightingLoader.Load(reader, manager);
    // 扩展名已在 XSHD 的 extensions 属性中
    // 如果 XSHD 没有 extensions 属性，手动调用 RegisterHighlighting
}
```

#### Task 2.1 — TypeScript/TSX（高优，~0.8h）

- 找社区 XSHD 或基于 JavaScript-Mode 修改
- 需覆盖：`.ts`, `.tsx` 扩展名
- 关键字：`interface`, `type`, `enum`, `as`, `const`, `let`, `async`, `await`, `typeof` 等 TS 特有

#### Task 2.2 — Go（高优，~0.6h）

- 新 XSHD：关键字（`func`, `go`, `defer`, `select`, `chan`, `struct`, `interface`, `map`, `range` 等）
- 单行注释 `//`、多行注释 `/* */`、字符串、数字

#### Task 2.3 — Shell Script（高优，~0.4h）

- 覆盖 `.sh` 扩展名
- Shebang `#!` 行高亮、关键字（`if`, `then`, `else`, `fi`, `for`, `while`, `case`, `function` 等）
- 变量 `$VAR`、字符串

#### Task 2.4 — YAML（中优，~0.4h）

- 覆盖 `.yaml`, `.yml` 扩展名
- 注释 `#`、键值对冒号高亮、字符串、数字、布尔值

#### Task 2.5 — 其余低优（~0.3h，可延后）

- Rust/Toml/Dockerfile/Batch/Kotlin/Ruby 各一个简易 XSHD

### Phase 3: 主题集成（~1h）

AvalonEdit 的语法颜色是自管理的不跟随 WPF 主题资源。需要监听主题切换并重新应用颜色。

**方案**: 在 `ThemeManager.ThemeChanged` 事件中重新加载所有 XSHD 定义的颜色。

实现方式：

```csharp
// 在 MainWindow 中
ThemeManager.ThemeChanged += (_, theme) =>
{
    if (PreviewTextBox.Visibility == Visibility.Visible && PreviewTextBox.SyntaxHighlighting != null)
    {
        // 重新设置高亮以刷新颜色
        var h = PreviewTextBox.SyntaxHighlighting;
        PreviewTextBox.SyntaxHighlighting = null;
        PreviewTextBox.SyntaxHighlighting = h;
    }
    // 设置编辑器背景/前景
    PreviewTextBox.Background = (Brush)FindResource("Theme_WindowBg");
    // 注意：AvalonEdit 的文本前景色由 XSHD 定义控制，不直接继承 Foreground
};
```

更彻底的方案：为每个 XSHD 定义两套颜色（亮/暗），在注册时根据当前主题选择。
或者：在 XSHD 中不指定具体颜色值，而在运行时通过 `HighlightingColor` 的 `Foreground`/`Background` 属性动态设置。

**推荐方案**: 
1. XSHD 中定义语义化颜色名（`Comment`, `String`, `Keyword`, `Number` 等）
2. 在 `App.xaml` 中添加两套 `Theme_SyntaxHighlight_*` 资源
3. 主题切换时遍历 `HighlightingManager.Instance.HighlightingDefinitions` 中的 `NamedHighlightingColors`，用对应主题色覆盖

### Phase 4: 工具栏适配 + 集成测试（~1h）

#### Task 4.1 — 字号工具栏（~0.3h）

- `ChangeTextFontSize` 中的 `PreviewTextBox.FontSize` → `textEditor.FontSize`
- 字号显示/更新逻辑不变

#### Task 4.2 — 清理旧代码引用（~0.3h）

- 检查所有引用 `PreviewTextBox` 的代码（至少 5 处：`.cs` 中 5 个文件引用）
- `MainWindow.Preview.cs:ClearPreviewContent` → 改为 `PreviewTextBox.Clear()` 或 `PreviewTextBox.Text = ""`
- `MainWindow.Preview.cs:HideAllPreviewControls` → `PreviewTextBox.Visibility = Visibility.Collapsed`（属性名不变）
- `MainWindow.Menu.cs` → 检查预览切换逻辑

#### Task 4.3 — 测试（~0.4h）

- 手动测试 10 种语言的高亮显示
- 主题切换后颜色刷新
- 大文件（5MB）无明显卡顿
- 编码检测兼容性（中文 GBK 文件 + 高亮）

---

## 决策记录

### 1. 自定义 XSHD 范围

| 选项 | 工作量 | 覆盖扩展名 |
|------|:------:|:----------:|
| A: 仅高优 4 种（推荐初始） | ~2.5h | `.ts/.tsx/.go/.sh` |
| B: 高优 + 中优 7 种 | ~3.0h | A + `.yaml/.rs/.toml` |
| C: 全部 13 种 | ~4.0h | B + `.swift/.kt/.rb/.bat/.gradle/.dockerfile/.vue` |

**初始建议**: 选 B（7 种），其余低优可在后续迭代中按需添加。

### 2. 主题色同步策略

| 选项 | 工作量 | 效果 |
|------|:------:|:----:|
| A: XSHD 硬编码颜色，主题切换时重新赋值 | ~0.5h | 颜色固定，不跟随主题 |
| B: XSHD 语义色 + 运行时主题映射（推荐） | ~1.0h | 完美跟随亮/暗色主题 |
| C: XSHD 中定义两套颜色 + 运行时切换 | ~1.5h | 同上，但 XSHD 更复杂 |

**推荐**: B — 语义色分离，主题切换时动态映射。

---

## 依赖与风险

### 外部依赖

| 依赖 | 版本 | 许可证 | 用途 |
|------|------|--------|------|
| `AvalonEdit` NuGet | latest (≥6.x) | MIT | 语法高亮文本编辑器 |
| 社区 XSHD 文件 | — | MIT/CC0 | TypeScript/Go/YAML 等的高亮定义 |

### 风险

| 风险 | 概率 | 影响 | 缓解 |
|------|:----:|:----:|------|
| AvalonEdit 与 .NET 9 兼容性 | 🟢 低 | 🔴 高 | 先创建测试项目验证 NuGet 包可安装 |
| 大文件性能退化 | 🟢 低 | 🟡 中 | 已有 `MaxTextPreviewBytes` 限制（默认 5MB） |
| 主题切换不刷新颜色 | 🟡 中 | 🟡 中 | 用 `ThemeChanged` 事件重新应用高亮定义 |
| 自定义 XSHD 颜色在暗色下不可读 | 🟡 中 | 🟡 中 | 主题映射策略 B 解决 |
| AvalonEdit 的 `Text` setter 编码行为 | 🟢 低 | 🟢 低 | 与现有 TextBox 行为一致（字符串已解码） |

---

## 验收标准

- [ ] 14 种内置匹配扩展名自动获得语法高亮
- [ ] 自定义 XSHD 覆盖的扩展名获得对应高亮
- [ ] 无匹配扩展名降级为纯文本（不崩溃）
- [ ] A−/A+ 字号按钮仍可调节编辑器字号
- [ ] 切换亮/暗主题后编辑器颜色实时更新
- [ ] 编码检测（GBK/Shift-JIS/UTF-8）仍正常工作
- [ ] `MaxTextPreviewBytes` 限制仍然生效
- [ ] 无 `PreviewTextBox` 引用残留导致编译错误

---

## 滚动计划（可选）

如果工作量超出预期，可裁剪：

| 阶段 | 内容 | 工时 |
|:----:|------|:----:|
| **P0** | NuGet + XAML 替换 + 内置高亮分发 | ~2h |
| **P1** | 高优 XSHD 4 种 | ~2.5h |
| **P2** | 主题色同步 | ~1h |
| **P3** | 中优 XSHD 3 种 + 低优 XSHD 6 种（延后） | ~1.5h |
