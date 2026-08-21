# Draft: 文件列表缩略图查看方式 (file-list-thumbnails)

> status: awaiting-approval
> intent: CLEAR（访谈模式，分叉全部已问）
> review_required: false

## 用户请求与已定决策（访谈结果）
1. **出图范围**（三档）：
   - **图像类**（Image / AnimatedImage(GIF/动画WebP) / Svg / IcoGallery）→ 真实缩略图（128px，SKCodec/Svg.Skia/IcoParser 解码）
   - **文本类**（PreviewType.Text / Csv / Markdown / Html）→ **文字卡片**：头部 4KB 字节解码后显示前 ~150 字符（两模式都用）
   - **其余** → 现有系统文件图标（IconService）
2. **切换入口**：文件列表工具栏按钮（循环切换）+ 持久化 AppSettings。
3. **平台**：仅 Avalonia（规则 11）。
4. **尺寸**：固定 128px（无配置 UI）。
5. **视图形态**：三态布局 `FileListLayoutMode{Details, Tile, Content}`（用户可选）：
   - `Details`：现状 DataGrid（16px 图标列）——**唯一保留 DataGrid 的模式**
   - `Tile`：ItemsRepeater + UniformGridLayout 平铺网格（128px 内容卡 + 下方文件名）
   - `Content`：**自定义垂直行布局（非 DataGrid）**——[128px 内容卡] + 右侧元数据块（文件名加粗大字 + 文件大小/压缩后大小/压缩率/修改日期 标签值行，显示名与信息面板一致），与 Tile 共用选择层基建

## Content 模式元数据块（已核实：零提取）
- 字段全部来自内存中 `ArchiveItemModel` 现有显示属性：`NameDisplay` / `SizeDisplay` / `CompressedSizeDisplay` / `RatioDisplay` / `LastModifiedDisplay`（`CompressedSizeAvailable=false` 时压缩后大小/比率自然显示空，与列一致）。
- 标签显示名复用信息面板 i18n key（`Metadata_Key_FileSize` 等，`LocalizationManager.T`），观感与预览窗口下方面板一致。
- **明确不做**：格式专属元数据（图片尺寸/PDF 页数/PE 版本等）需逐条目解压解析，列表内会拖慢滚动，由预览面板在看选中项时提供。

## 从 preview-quick-modes.md 第 7 章采纳（已核实）
- `ThumbnailCache`：LRU 有界 + 异步 `GetOrCreateAsync` + 关包 `Clear()`。
- 滚动按需加载 + 取消；SKBitmap 降采样；无缩略图项回退图标。
- 排除 Magick.NET 格式（PSD/HDR/EXR/TIFF/TGA，插件尚不存在）。

## 探索结论（已核实）
- `DataGrid` FileListGrid（MainWindow.axaml 行 959-1150）；`SelectedItem→SelectedEntry` + `SelectionChanged` 同步 `SelectedEntries`（行 1231）；`OnSelectedEntryChanged`→`ShowPreviewAsync`（MainWindowViewModel.cs:726）。
- 已有 `FileListViewMode{All,FilesOnly,DirectoriesOnly}`（类型过滤，行 862-868）→ 新枚举名 `FileListLayoutMode{Details,Tile,Content}` 避冲突。
- `ExtractHeadAsync`（Core）：ZIP 实现全条目解压进内存再取头 → 图像提取需 **8MB 大小上限**；7z 固实自动降级全量 → **主动跳过固实包**（`IsSevenZipSolid`）。
- 图像解码需**完整字节**（PNG/JPEG 截断不可靠）；文本解码 `TextEncodingDetector.DecodeText(byte[])`（Core，UTF-8→ANSI，无需临时文件）。
- 密码：`GetSessionPassword` → 提取传密码。分类按扩展名（无 I/O）。
- 虚拟化：Avalonia 12.0.4 无内置 VirtualizingWrapPanel；官方推荐 ItemsRepeater + UniformGridLayout（无内置选择）→ Tile/Content 共用自建选择层。
- 约定：规则 4/5/7/8/13。

## 计划设计概要
- **AppSettings**（仅 Avalonia）：`FileListLayoutMode`（int，默认 0=Details）。
- **ArchiveItemModel**：新增 `ThumbnailSource`（Bitmap?，图像卡）+ `TextSnippet`（string?，文字卡）+ `HasThumbnailContent`（任一非空）+ `IsSelected`（Tile/Content 选择视觉）。
- **ThumbnailCache**（新，LRU ~200-500）：`GetOrCreateAsync`（返回图像 Bitmap 或文本片段，缓存 key=archivePath+FullPath）+ `Clear()`。
- **ThumbnailService**（新 Services/）：扩展名分类（PreviewType 图像族→解码管线；文本族→文本管线；其余 null）；图像管线（8MB 上限→固实跳过→`ExtractHeadAsync`→SKCodec 首帧/Svg.Skia/IcoParser→缩放 128）；文本管线（`ExtractHeadAsync(4096, password)`→`DecodeText`→前 ~150 字符）；后台队列（并发 4）+ CTS 取消（切目录/关包）；关包清缓存。
- **共享选择层**（新 `Services/ThumbnailSelectionController.cs`，Tile 与 Content 共用）：指针命中→单选/Ctrl+Shift 多选→同步 `SelectedEntry`/`SelectedEntries`/`IsSelected`；双击进目录/打开；KeyDown（Enter/Back/Delete）；右键菜单（复用 DataGrid ContextMenu 结构）；拖拽出（复用现有 pointer→`CustomOleDragDrop` 管线）。
- **MainWindow.axaml**：Grid.Row=2 内 DataGrid（仅 Details）与 ScrollViewer+ItemsRepeater（Tile=UniformGridLayout MinItemWidth≈136/MinItemHeight≈160；Content=StackLayout 垂直行，行≈[128px 卡 | 元数据块]）按 LayoutMode 切换可见性；工具栏三态循环按钮 + Label（新 PathIcon，注册 IconTestViewModel）；Content 行模板 = 左卡（图像/文字/图标三态）+ 右元数据块（NameDisplay 加粗 + 4 行 `Metadata_Key_*` 标签值）；选中高亮（主题资源键）。
- **模式切换**：选择状态迁移（`SelectedEntry`/`SelectedEntries` 跨模式保留，切回 Details 时回写 DataGrid `SelectedItem`）。
- **排序**：Details 保留列排序；Tile/Content 无排序 UI（Must-NOT-Have）。
- **i18n**：`Toolbar_LayoutMode`/三态 Label key 成对 + UpdateLocalizedStrings 登记（元数据标签复用既有 `Metadata_Key_*`，需核实存在）。
- **测试**（tests-after）：ThumbnailCache/ThumbnailService 纯逻辑（LRU 上限/分类映射/大小上限/固实跳过/文本截断）+ ThumbnailSelectionController 纯逻辑（修饰键组合/多选集合维护）单测；UI 人工验证。
- **文档**：PLAN.md 加行；实施后 PROGRESS.md + progress-avalonia-detail.md（规则 1/3）。

## Must-NOT-Have（边界）
- 非图像非文本类不出内容卡（图标）；不做格式专属元数据；不做 PDF 缩略图；不做 Magick.NET 格式；不做尺寸选择器（固定 128）。
- WPF 版不做（规则 11）。
- Tile/Content 无排序 UI、无方向键/Home/End/PageUp/PageDown（Enter/Back/Delete 支持）。
- 不依赖 preview-quick-modes 先行。

## 工作量预估（已含 Content 自定义布局）
**4–5 天**：ThumbnailCache/Service 管线 ~1.5 天、共享选择层（Tile+Content 两模式接入）~1.5 天、Tile/Content 模板与模式切换 UI ~1 天、元数据块 ~0.5 天、测试 + 文档 ~0.5 天。

## 下一步
用户批准 → 写 `.omo/plans/file-list-thumbnails.md` → PLAN.md 同步。