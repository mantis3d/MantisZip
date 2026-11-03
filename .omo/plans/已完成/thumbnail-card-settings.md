# 缩略图卡片外观设置（v0.5.6 追加）

> **档位**: A 档 · v0.5.6 追加项
> **状态**: ✅ 已完成（2026-11-02 归档，随 v0.5.6；实施记录见文末）
> **来源**: 用户 2026-11-02 提出（m10407 / m10435）

## 背景

v0.5.6 加完「平铺 / 内容」两种缩略图布局后，卡片的观感是写死的：128 px、无边框、透明区域露出列表底色。
用户提出两点：

1. 缩略图是否加外边框（白底图片贴在浅色背景上时看不清边界）。
2. 带透明通道的图（PNG / SVG）是否保留透明。
3. 追加：垫的底色要能自己选；外边框默认关；缩略图尺寸也一起加。

## 设计

**不走解码层改，走资源注入**——与 `App.Theming.cs` 的 `ApplyCompactness()` 同一套路：
`AppSettings` 存值 → 启动/保存时 `ApplyThumbnailStyle()` 注入 `ResourceDictionary` → XAML 用 `{DynamicResource}` 取。
好处：切换即时生效、不重建条目模型、不改 `ThumbnailService` 之外的任何解码逻辑。

| 设置项 | `AppSettings` 字段 | 默认 | 注入的资源 |
|---|---|---|---|
| 显示外边框 | `bool ThumbnailShowBorder` | `false` | `ThumbnailCardBorderThickness`（`Thickness` 0 或 1） |
| 保留透明 | `bool ThumbnailUseTransparency` | `true` | `ThumbnailCardBackdropBrush`（透明 或 实色） |
| 垫底色 | `string ThumbnailBackdropColor` | `"#FFFFFF"` | 同上（`Color.TryParse` 失败回落白色） |
| 卡片尺寸 | `int ThumbnailSize` | `128`（96 / 128 / 160） | `ThumbnailCardSize`、`ThumbnailTileItemWidth`、`ThumbnailTileCellWidth` |

**尺寸影响解码**：`ThumbnailService` 的构造器新增可选参 `thumbnailSize`，`BuildCacheKey` 把尺寸拼进缓存键
（`archivePath|entryPath|size`）⇒ 切尺寸不串图、切回来能命中缓存。主窗口在 `StartThumbnailBatchIfNeeded()`
里比较当前尺寸与服务尺寸，变了就重建服务；`App.ThumbnailStyleChanged` 事件驱动 `ReloadThumbnailsForStyleChange()`
（详情布局直接返回，平铺/内容清空 `ThumbnailSource` 后重跑批量加载）。

**取色控件**：Avalonia 12 的 `Avalonia.Controls.ColorPicker` 包**没有 `ColorPickerButton`**
（旧版有，包里实际只有 `ColorView` / `ColorSpectrum` / `ColorSlider` / `ColorPreviewer`）⇒ 用
`Button` + `Flyout` 内嵌 `cp:ColorView`，按钮上显示当前 `#RRGGBB`。包的主题需在 `App.axaml` 引入：
`avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml`（不引则 Flyout 里是空白面板）。

## 任务

- [x] T1 `AppSettings` 四个字段 + `App.Theming.ApplyThumbnailStyle()` / `RefreshThumbnailStyle()` / `ThumbnailStyleChanged`
- [x] T2 `ArchiveContentView.axaml` 卡片改 `Border` + 全文 `DynamicResource` 化（含平铺 `WrapPanel` 宽高、内容行 `Auto` 列）
- [x] T3 `ThumbnailService` 尺寸参数化（`DefaultThumbnailSize`、`_thumbnailSize`、缓存键含尺寸、ICO 目标 `max(160, size)`）
- [x] T4 主窗口：`_thumbnailServiceSize` 比较重建 + `ReloadThumbnailsForStyleChange()` + 事件订阅
- [x] T5 设置窗「外观 → 缩略图」子页签（边框 / 垫底色 + 取色 / 尺寸三档）+ VM 读写与本地化刷新
- [x] T6 i18n 三语 11 个 key（`Settings_Tab_AppearanceThumbnail`、`Settings_Thumbnail_*`）
- [x] T7 真机目视（边框开关 × 透明/垫色 × 三档尺寸；160px + 边框 + `#FFF2D5` 垫底已目视确认；设置窗「外观 → 缩略图」子页签已截图 `docs/images/version/v0.5.6/ThumbnailCardSettings.png`）
- [x] T8 测试与文档（细节文档 + PROGRESS + 规则 18 确认 RELEASE_NOTES）

## 验收口径

1. 外边框默认关；打开后平铺与内容两种布局的卡片都出现 1px 边框。
2. 「保留透明」默认开：PNG/SVG 的透明区域露出列表底色，浅色/深色主题各自正确。
3. 关掉「保留透明」后透明区域铺所选底色；取色器能改，改完列表立即跟着变。
4. 尺寸三档 96 / 128 / 160：切档后卡片、平铺列宽同步变；缩略图按新边长重新生成（来回切换不重复解码）。
5. 三项设置重启后保持；`settings.json` 里分别是 `ThumbnailShowBorder` / `ThumbnailUseTransparency` / `ThumbnailBackdropColor` / `ThumbnailSize`。
6. 详情布局完全不受影响（卡片相关资源不参与 DataGrid）。

## 风险

- `ColorView` 在 `Flyout` 里的 DataContext 继承依赖 Avalonia 的 Popup 行为，若不继承需改为 `x:CompileBindings="False"` + 显式绑定源。
- 卡片尺寸改动会让平铺布局的列数变化，超大目录重排有一次性开销（`WrapPanel` 无虚拟化，已知）。

## 实施记录（2026-11-02）

| 文件 | 改动 |
|---|---|
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | 四个字段：`ThumbnailShowBorder`(false) / `ThumbnailUseTransparency`(true) / `ThumbnailBackdropColor`("#FFFFFF") / `ThumbnailSize`(128) |
| `src/MantisZip.UI.Avalonia/App.Theming.cs` | `ApplyThumbnailStyle()` 注入五个资源键、`ParseBackdropBrush()` 容错、`RefreshThumbnailStyle()` + `ThumbnailStyleChanged` 事件 |
| `src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml` | 卡片改 `Border` 包 `Panel`，宽高/边框/背景全走 `DynamicResource`；平铺 `ItemWidth` 与项宽改资源；内容行首列 `128` → `Auto` |
| `src/MantisZip.UI.Avalonia/Services/ThumbnailService.cs` | `DefaultThumbnailSize` 常量 + 构造器 `thumbnailSize` 形参，解码/SVG/缩放/缓存键全链路带尺寸 |
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 尺寸变化时重建服务（`_thumbnailServiceSize`）、`ReloadThumbnailsForStyleChange()`、订阅 `ThumbnailStyleChanged` |
| `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs` | 四个属性 + `ThumbnailBackdropColorHex` + 三档选项 + 读/存 + 11 条本地化通知 |
| `src/MantisZip.UI.Avalonia/Views/SettingsWindow.axaml(.cs)` | 新子页签「缩略图」（三张卡片）；保存后 `App.RefreshThumbnailStyle()` |
| 三份 `Localization/strings.*.json` | 11 个 key（1423 个 key 校验通过） |
| `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` / `App.axaml` | 新增 `Avalonia.Controls.ColorPicker` 12.1.2 + Fluent 主题 StyleInclude |

**偏差（3 条）**

1. **`ColorPickerButton` 在 Avalonia 12 不存在**：`Avalonia.Controls.ColorPicker` 里只有 `ColorView` / `ColorSpectrum` / `ColorSlider` / `ColorPreviewer` / `ColorPicker`（flyout 版）。改为 `Button` + `Button.Flyout` 内嵌 `<cp:ColorView>`，按钮内容显示当前色值（`ThumbnailBackdropColorHex`）。
2. **`ThumbnailUseTransparency` 语义定为「保留透明」**（默认 true）：alpha 始终保留，勾掉才铺纯色底；XAML 里用 `BoolConverters.Not` 反一下显示为「垫纯色底」。
3. **缓存键加尺寸**：`archivePath|entryPath|size`，来回切档不重复解码。

**验证**

- `dotnet build -c Release --no-incremental` 0 警告；`-Task test -Target ui-tests -c Release` 38.9s 全绿；`-Task test -Target tests` 全绿。
- `-Task i18n` 1423 key、`-Task check` 版本 0.5.6 六处 / 1423 key / 8 守卫全绿。
- 真机（`settings.json` 置 `FileListLayoutMode=1`、`ThumbnailShowBorder=true`、`ThumbnailUseTransparency=false`、`ThumbnailBackdropColor="#FFF2D5"`、`ThumbnailSize=160`）：平铺卡片变 160px、每张卡 1px 细边框、透明图周围露出米色垫底、文字卡同为米色底 ⇒ 四项设置全部生效。
- **设置窗截图（2026-11-02 补）**：`docs/images/version/v0.5.6/ThumbnailCardSettings.png`（916×678）。拍摄办法：临时加 `MZ_SHOT_THUMBNAIL_TAB=1` 环境变量钩子（`MainWindow.Opened` 自动打开设置窗、`SettingsWindow` 构造里把 `MainTabs.SelectedIndex = 2` / `AppearanceTabs.SelectedIndex = 1`），跑一次 Release 截图后 `git checkout` 撤销。
- **踩坑（截图自动化）**：早先截不到设置窗，是因为脚本用 `SetWindowPos(hwnd, HWND_TOPMOST)` 把主窗口置顶——**非置顶的子窗口永远画不到置顶的 owner 之上**，`ShowDialog(this)` 弹出的设置窗其实已经打开、只是被主窗口盖住；且 Esc 既关菜单也关对话框，按 pid 枚举时窗口已被自己关掉。此时用 `HWND_NOTOPMOST`（-2）复位、或按子窗口自己的 `HWND` 矩形抓图，就没有问题。