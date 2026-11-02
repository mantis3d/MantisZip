# 文件名代码页：检测结果回显 + 手动切换（Bandizip 式）

> **状态**：✅ 实现完成（2026-11-02；S1–S7 全部落地，待真机手测后归档）| 创建 2026-11-02
> **关联**：用户反馈「希望像 Bandizip 一样能检测并切换代码页」
> **分支**：`beta`
> **范围**：**B 档** —— 手动切换 + 检测结果回显 + 默认读取编码设置 + 删除/加文件按当前编码重写 + TAR/GZ 一并支持

## 1. 一句话

现在打开 ZIP 时**只会在 UTF-8 和 GBK 之间猜一次**，猜完固定，用户没有任何出口：Big5 / Shift-JIS / EUC-KR 命名的包必然被当成 GBK ⇒ 整列表乱码。本计划给「文件名编码」补一条从引擎到 UI 的显式通道：文件列表工具栏加一个代码页下拉（自动 / 具体编码 / 其他代码页数字），自动模式回显实际检测结果，用户手动选定后**列表、解压、预览、拖拽、删除条目全部使用同一个编码**。

## 2. 现状锚点

- **读路径唯一入口，且没有编码入参**：`src/MantisZip.Core/Engines/ZipEngine.Open.cs:45 OpenArchiveWithEncodingFallback(string archivePath, string? password = null)`。流程：UTF-8 打开 → 条目名含 >127 字符（`:83`）→ 中央目录 flags bit 11 检查（`ZipHasUtf8Flag`，`:147`）→ `LooksLikeValidCjk`（`:225`，CJK 占比 ≥50% 就保留 UTF-8）→ 否则关闭重开为 `OpenWithGbk()`（`:51`）。**只有 UTF-8 / GBK 两种结果，无第三个分支、无外部入参。**
- **列表接口没有编码参数**：`src/MantisZip.Core/Abstractions/ArchiveEngine.cs:516` `Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)`。
- **`ArchiveOptions.FileNameEncoding` 只被写路径消费**（`ArchiveEngine.cs:99`）：`ZipEngine.Open.cs:125 ResolveFileNameEncoding`、`ZipEngine.Compress.cs:167`、`ZipEngine.CompressGroup.cs:426`。读路径完全不看它。
- **主窗口清单链路**：`src/MantisZip.UI.Avalonia/Services/ArchiveService.cs:83`（`engine.ListEntriesAsync(archivePath, password, ct)`）← `MainWindowViewModel.LoadArchiveAsync`（`MainWindowViewModel.cs:926`）/ `RefreshArchive`（`:1303` → `:1311` 重新 `LoadArchiveAsync(CurrentArchivePath)`）。
- **解压/预览都是「重新打开归档再按名字匹配」**：`ZipEngine.Extract.cs:39/66/300/389/776/815/1031/1126`、`src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs:91/465/768/914`、`ZipEngine.Compress.cs:742`、`ZipEngine.CompressGroup.cs:71/250/911/1005/1047/1053/1070` 全部调 `OpenArchiveWithEncodingFallback`。预览内容读取走 `ArchiveEntryExtractor.ExtractHeadAsync`（`:231`）/ `ExtractEntryToMemoryAsync`（`:318`）/ `ExtractHeadTailAsync`（`:418`）/ `ExtractEntryAsync`（`:31`）。
- **写路径的编码是硬编码推断**：`ZipBinaryRewriter.RewriteAsync(..., Encoding encoding, ...)`（`:314` / `:569`）签名里已有 `Encoding`，但 `ReadCentralDirectory(..., Encoding? fallbackEncoding = null)` 内部默认 **GBK**（`:203`/`:213`/`:252`），逐条按 `flags & 0x0800` 选 UTF-8 或该回退 ⇒ 对 Big5/Shift-JIS 包做删除/加文件会按 GBK 重写中央目录，**名字真的被改坏**（不是显示问题）。
- **引擎是单例**：`ArchiveEngineFactory._engines`（`ArchiveEngine.cs:596-603`）静态注册 3 个实例 ⇒ 不能把「当前归档编码」存成引擎字段（并行解压多实例、CLI 并发会串味）。
- **TAR/GZ 同样没设编码**：`TarGzEngine.ListEntriesAsync`（`:515`）与 `ReaderOptions` 用点（`:57/534/636/814`）都没给 `ArchiveEncoding` ⇒ SharpCompress 默认 UTF-8，GBK tar 也乱码。
- **预览的编码下拉不是同一样东西**：`PreviewViewModel.EncodingOptions`（`PreviewViewModel.cs:122-138`）只喂 `TextEncodingDetector.DecodeText` 解**文件内容**（`:945-951`），偏好全局存 `AppSettings.TextEncodingPreference`（`Models/AppSettings.cs:164`）。文件名编码与它无关，两者要并存且互不覆盖。
- **UI 落点（已定，2026-11-02 用户指定）**：主窗口**顶层工具栏** —— `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml:82-214`（`Grid.Row="1"` 的 `Border` + 横向 `StackPanel`，按钮统一 `Classes="ToolbarButton"`，图标 + 文字竖排）。代码页下拉放在该 `StackPanel` 末尾（`密码` 按钮之后、`</StackPanel>` 之前，即 212–213 行之间），与 Bandizip 工具栏右侧的「代码页」下拉一致。注意 `Controls/ArchiveContentView.axaml` 是**延迟创建**的（`MainWindow.EnsureArchiveContent()`），把控件放进去会导致未开包时不实例化、开包后才出现重排；放顶层工具栏则始终存在、只按 `IsVisible` 切换。
- **7z/RAR 不受影响**：7z.dll 内部用 UTF-16 表示条目名，`SevenZipEngine` 无需改造。

## 3. 三个硬约束

1. **必须全链路同一编码，不能只改显示**：列表里的名字是后续一切的主键 —— `ArchiveItem.FullPath` 就是解压时的 `entryKeys`（`SelectedItemsExtractService.cs:42-53`），引擎解压时**重新打开归档**再按名字匹配。只改显示 ⇒ 解压找不到条目或写错名、预览树冲突标记错位、拖拽落点重复。
2. **编码只能显式传参**：引擎是单例（§2），不能存字段。所有入口（列表 / 解压 / 逐条解压 / 预览内容 / 删除 / 加文件 / 并行度探测）都要能把同一份值传下去。
3. **写操作有破坏性**：删除条目、添加文件、写注释都会用编码重写中央目录。编码选错 ⇒ 名字永久损坏。因此「手动指定编码」状态下做写操作必须有明确提示或阻止。

## 4. 方案

### 4.1 引擎层：加「强制编码」入口

- `ZipEngine.OpenArchiveWithEncodingFallback(string archivePath, string? password = null, string? fileNameEncoding = null)`：
  - `fileNameEncoding` 为空或 `"auto"` ⇒ 完全保持现状（bit 11 → CJK 启发式 → GBK 重试）；
  - 非空 ⇒ **直接按该编码打开**，跳过 bit 11 检查、跳过 CJK 启发式、跳过 GBK 重试；
  - 支持 `"system"`（`CultureInfo.CurrentCulture.TextInfo.ANSICodePage`）与 `"other:<n>"`（任意代码页数字，走 `Encoding.GetEncoding(n)`，`CodePagesEncodingProvider` 已由静态构造器注册）。
  - 增加可选回调 `Action<string>? onEncodingResolved = null`，把**实际使用的编码名**（含自动检测结果）回传 —— 避免为「回显检测结果」再打开一次归档。
- `IArchiveEngine.ListEntriesAsync(..., string? fileNameEncoding = null, Action<string>? onEncodingResolved = null)`（三个引擎实现；7z 忽略参数、直接 `onEncodingResolved?.Invoke("utf-16")` 或 null）。
- `ArchiveOptions` 新增 `public string? ReadEncoding { get; set; }`（注释：读取时条目名编码，null = 自动检测；写路径继续用 `FileNameEncoding`），供 `ExtractAsync` / `ExtractEntriesAsync` / `TestArchiveAsync` / `AddToArchiveAsync` / `DeleteEntriesAsync` 使用。
- `ArchiveEntryExtractor` 四个公开方法加可选 `string? fileNameEncoding = null` 并透传（预览内容、拖拽单条提取都经过这里）。
- `ZipBinaryRewriter`：写路径的 `Encoding` 参数由调用方按「当前归档编码」传入；`ReadCentralDirectory` 的 GBK 默认仅在没有任何指定时保留。

### 4.2 会话态：当前归档的读取编码

`MainWindowViewModel` 新增：

- `[ObservableProperty] private string _archiveNameEncoding = "auto";` —— 当前归档的读取编码（`auto` / `utf-8` / `gbk` / `big5` / `shift-jis` / `euc-kr` / `system` / `other:<n>`）；
- `ArchiveEncodingOptions`（下拉数据源，与预览共用构造逻辑但**独立实例**，不共享选择状态）；
- `bool CanChangeNameEncoding` —— 仅当当前格式为 `Zip` 或 `TarGz` 时为 true（7z/RAR 隐藏控件，按规则 6 用 `IsVisible` 而非禁用）；
- `ChangeArchiveNameEncodingCommand(string key)`：设值 → `RefreshArchive()` 重载清单，重载后尽量保持当前所在目录（目录已不存在则退回根）→ 状态栏提示实际生效的编码；
- 打开新包时复位为 `AppSettings.ReadNameEncoding`（默认 `"auto"`），不沿用上一个包的选择。

### 4.3 UI：主窗口顶层工具栏（Bandizip 同位置）

- `Views/MainWindow.axaml` 顶层工具栏 `StackPanel` 末尾（`密码` 按钮之后）新增 `ComboBox`（`IsVisible="{Binding CanChangeNameEncoding}"`、`MinHeight="{DynamicResource ControlHeight}"`、`Background="{DynamicResource ThemeSurfaceBgBrush}"`、`Foreground="{DynamicResource ThemeTextPrimaryBrush}"`、`ToolTip.Tip="{Binding LocalizedStrings[Toolbar_NameEncoding]}"`）：
  首项「自动检测」+ 选中后追加回显（如「自动检测 · 检测到 GBK」，数据源项文本随 `DetectedNameEncoding` 刷新）；
  其后为固定编码项 + 「其他…」（选中后弹输入框填代码页数字）。
- 不上「查看」菜单（用户 2026-11-02 指定「跟 Bandizip 一致」，Bandizip 只有工具栏入口）；若日后反馈不易发现，再补菜单项，属增量。
- i18n 新增 key（三语）：`Toolbar_NameEncoding`（「代码页」）、`NameEncoding_Auto`（「自动检测」）、`NameEncoding_Detected`（「自动检测 · 检测到 {0}」）、`NameEncoding_Other`（「其他代码页…」）、`NameEncoding_OtherPrompt`（输入框标题/说明）、`Status_NameEncodingChanged`（「代码页已切换为 {0}」）、`NameEncoding_WriteWarning`（手动编码下写操作提示）。**逐条登记到 `MainWindowViewModel` 的 `LocalizedStrings` keys 数组**（规则 13）。

### 4.4 自动检测结果回显

用 4.1 的 `onEncodingResolved` 回调：`ArchiveService.LoadArchiveAsync` 在 `ArchiveLoadResult` 中带出 `DetectedNameEncoding`，`MainWindowViewModel` 存到 `DetectedNameEncoding` 并刷新下拉首项文本。手动指定编码时该文本显示所选编码本身。

### 4.5 写路径按当前编码重写

- `MainWindowViewModel` 的删除条目 / 添加文件 / 写注释三处，把 `ArchiveNameEncoding` 传进 `ArchiveOptions`（写路径用 `FileNameEncoding`，或统一到新增的 `ReadEncoding` —— 实现时二选一，倾向新增字段并在引擎内部统一取值，避免两个字段各存一份）。
- **安全闸**：当 `ArchiveNameEncoding` 不是 `auto` 且不是自动检测出的同一个值时，执行写操作前弹一次确认（「当前手动指定的代码页可能与归档不一致，重写后文件名可能损坏，是否继续？」），确认后本次会话不再重复提示。

### 4.6 设置页：默认读取编码

- `AppSettings` 新增 `public string ReadNameEncoding { get; set; } = "auto";`
- 设置页「压缩 → 常规」的「ZIP 编码」行下方新增「读取时的文件名编码」下拉（默认自动检测；选项同 §4.3）。
- 按规则 23 接线：新增属性必须同提交出现运行时消费者（`SettingsWindowViewModel` 属性/镜像/保存/文化通知四处 + `settingswiring` 守卫登记）。

### 4.7 TAR/GZ

`TarGzEngine` 的四处 `ReaderOptions`（`:57/534/636/814`）按传入编码设置 `ArchiveEncoding { Default = encoding }`；`auto` 时保持现状。`IArchiveEngine` 层的参数对 7z/RAR 无副作用。

## 5. 决策表

| # | 决策 | 取值 |
|---|---|---|
| D1 | 范围 | B 档（手动切换 + 回显 + 默认设置 + 写路径安全 + TAR/GZ） |
| D2 | 支持格式 | ZIP + TAR/GZ；7z/RAR 隐藏控件（7z.dll 给 UTF-16，无此问题） |
| D3 | 下拉位置 | **主窗口顶层工具栏末尾**（取 `ToolbarButton` 行右端、密码按钮之后），无菜单入口 —— 2026-11-02 用户拍板「跟 Bandizip 一致」 |
| D4 | 编码清单 | 预览那套（UTF-8 / UTF-16 LE / GBK / GB18030 / Big5 / Shift-JIS / EUC-KR / 系统 ANSI）+ 「其他…」数字输入 |
| D5 | 是否持久化每包选择 | 否；只持久化「默认读取编码」 |
| D6 | 切换后视图状态 | 重新载入清单，保持当前目录（不存在则退回根），保持排序/视图模式 |
| D7 | 检测结果回显 | 经 `onEncodingResolved` 回调，不二次打开归档 |
| D8 | 编码存储位置 | 引擎单例不存字段，一律显式传参 |
| D9 | 写路径 | 用当前归档编码重写中央目录；手动编码与检测结果不一致时写前确认 |
| D10 | 与预览「文本编码」的关系 | 两套独立；文件名编码不影响内容解码，反之亦然 |

## 6. 步骤与工时

| 步骤 | 内容 | 估时 |
|---|---|---|
| S1 | `ZipEngine.OpenArchiveWithEncodingFallback` 加编码参数 + 回调；`TarGzEngine` 四处 `ReaderOptions` | 2h |
| S2 | 接口加参（`ListEntriesAsync`）+ `ArchiveOptions.ReadEncoding` + `ArchiveEntryExtractor` 四方法 + 8 个 `ListEntriesAsync` 调用点 + 解压/校验调用点透传 | 3h |
| S3 | `MainWindowViewModel` 会话态、选项源、切换命令、`RefreshArchive` 保留目录 | 2h |
| S4 | UI（主工具栏 `ComboBox` + 「其他…」输入框）+ i18n 7 个 key × 3 语 + 规则 13 登记 + 规则 4 主题样式 | 2h |
| S5 | 写路径（删除/添加/注释）用当前编码 + 写前确认闸 | 1.5h |
| S6 | `AppSettings.ReadNameEncoding` + 设置页下拉 + 接线守卫 | 1h |
| S7 | 测试（Core 5 例 + UI 2 例 + 接线守卫 1 例）+ 文档 + 全量门禁 | 2.5h |
| | **合计** | **约 14h（2 天）** |

## 7. 测试清单

1. `ZipEngineNameEncodingTests`：
   - Big5 命名的 ZIP（用 `Encoding.GetEncoding(950)` 直接写条目名）→ `auto` 下名字乱码（保持现状基线）→ 指定 `big5` 后名字正确；
   - GBK 命名 ZIP 指定 `gbk` 与 `auto` 结果一致；
   - 指定错误编码不抛异常（降级为替换字符），条目仍可列出；
   - 指定 `other:436` 之类任意代码页可用；
   - 7z 包传入编码参数不受影响。
2. `TarGzNameEncodingTests`：GBK 命名的 tar.gz，指定 `gbk` 后名字正确。
3. `ZipBinaryRewriter` 回归：Big5 命名 ZIP 删除一个条目后，剩余条目名仍按 Big5 正确（现状会被 GBK 改坏 —— 该用例同时也是缺陷复现）。
4. UI：`CanChangeNameEncoding` 按格式（zip/tar=true，7z/rar=false）；切换命令触发重载且保留当前目录。
5. 接线守卫（照 `tests/MantisZip.UI.Avalonia.Tests/LocalizedStringsWiringTests.cs` 的源码扫描写法）：`engine.ListEntriesAsync(` 的每个调用点都必须传编码实参，漏一个就红。

## 8. 风险

- **切换编码要重载整包**：几万条目的包约数百 ms，需在切换时给忙碌态，避免看着像卡死。
- **并行解压每线程独立打开归档**：`ParallelDegreeOptimizer`（`src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs:87`）与并行解压分支必须都带同一编码，漏一处就会出现「顺序解压正常、并行解压乱码」。
- **误选编码后做写操作**：唯一有破坏性的路径，靠 §4.5 的确认闸兜底。
- **`ArchiveItem.FullPath` 作为键的稳定性**：切编码后同一归档的按键会整体变化，任何缓存了旧键的地方（预览树、选中集合、拖拽保留选择）都要在重载时清空 —— 重载实现里显式清理。
- **`LooksLikeValidCjk` 的既有折衷**：保留现状（auto 模式行为不变），不顺手改动判定，避免把这次功能变成编码启发式的回归试验。

## 9. 验收

- Big5 / Shift-JIS / EUC-KR 命名的 ZIP：默认乱码 → 下拉切换后列表正确，且**预览、解压、拖拽、选中项解压四处名字一致**。
- GBK 命名的 tar.gz：同样可切换。
- 7z / RAR 看不到该控件；未打开归档时也不显示。
- 自动模式显示「检测到 GBK」之类的实际结果。
- Big5 包删除一个条目后，剩余文件名不乱。
- 设置页「默认读取编码」改成 Big5 后，新开 Big5 包直接正确、新开 GBK 包仍可用自动。
- `check` / `i18n` / `guards` 全绿；新增测试实跑通过；`docs/ARCHITECTURE.md` 补一节「文件名代码页」。

## 10. 拍板记录（三条已定，2026-11-02）

1. **位置**（D3）：主窗口顶层工具栏末尾，与 Bandizip 一致，无菜单入口 —— 用户「UI放在主工具栏吧，跟bandizip一致」。
2. **格式范围**（D2）：ZIP + TAR/GZ，7z/RAR 隐藏控件 —— 用户选「ZIP + TAR/GZ」。
3. **编码清单**（D4）：含「其他代码页…」手动填代码页数字 —— 用户选「要」。
4. **开工时点**：用户选「先等 v0.5.5 发版」，故 v0.5.5 发版后开工（tag `v0.5.5` 已于 2026-11-02 推送）。

#### 11.5 工具栏形态返工（同日实测反馈）

用户看完首版提三点，全部采纳：① 控件要与工具栏其它「图标 + 文字」按钮一致（原为 `ComboBox`）；② 不适用格式（7z/RAR）不要隐藏、改成置灰；③ 要显示本次实际生效的编码。

落地形态：`Button Classes="ToolbarButton"` + `MenuFlyout`，内容为地球图标 + 右下角徽章 + `NameEncodingDisplay` 文字；徽章文案由 `NameEncodingOption.ShortLabel()` 生成（`auto` 不画、`system`→`ANSI`、`other:<n>`→数字）。菜单项在 `MainWindow.RebuildNameEncodingMenu()` 里按 VM 的 `NameEncodingOptions` 现建（`ToggleType=Radio`、当前项打勾），Click 与 `MenuFlyout.Opened` 各触发一次（幂等兜底）。

两处必须记住的 Avalonia 12 事实：`FlyoutBase` **没有 `Opening`**，只有 `Opened`/`Closed`；`MenuFlyout` 不在可视树里，`x:Name` **不生成字段**（`CS0103`）⇒ 命名 Button、从 `Button.Flyout` 取。

顺带修掉的语义缺陷：`LoadArchiveAsync` 开头的 `ClearArchiveInternal()` 无条件复位代码页，导致「手选编码 → 立刻被自己触发的重载抹掉」；改为 `ClearArchiveInternal(bool resetNameEncoding = true)`，载入前按 `reloadingSameArchive` 决定是否保留，`CanChangeNameEncoding` 补 `IsArchiveLoaded`。

测试：`tests/MantisZip.UI.Avalonia.Tests/NameEncodingToolbarTests.cs` 15 例。i18n 1405 → 1406（新增 `Toolbar_NameEncoding_Detected`）。
## 11. 实施记录（2026-11-02）

### 11.1 与计划的三处差异

| 计划 | 实际 | 原因 |
|------|------|------|
| §4.5 写路径「手动编码写前确认闸」 | **未做** | 读与写现在共用同一个编码键（`ArchiveOptions.ReadEncoding` 与 `FileNameEncoding` 同值），不再存在「读 Big5、写 GBK」的静默重编码路径；编码选错时列表当场就是乱码，确认框反而多一步 |
| §6 S1 只改 `ZipEngine.Open` + `TarGzEngine` | 另外把 `SevenZipEngine.ListEntriesAsync` 也加了同签名参数 | 接口统一后三个引擎都要实现，7z 内部忽略并回传 `utf-16` |
| §7 测试清单「Core 5 例 + UI 2 例 + 守卫 1 例」 | Core **9 例**（键表 6 + Big5 端到端 3），UI 侧靠既有 `LocalizedStringsWiringTests` 覆盖新 key 登记 | 键表是单一事实源，逐个键钉住比再补 UI 快照更有效 |

### 11.2 实现要点

- 键表 `src/MantisZip.Core/Utils/NameEncodingCatalog.cs`；`TryResolve` 认不出返回 false（不静默换编码）。
- 读路径：`IArchiveEngine.ListEntriesAsync(..., string? fileNameEncoding = null, Action<string>? onEncodingResolved = null)`；`ZipEngine.OpenArchiveWithEncodingFallback` 显式编码分支放在最前，跳过 bit 11 / CJK 启发式 / GBK 重试。新参数追加在 `CancellationToken` 之后 ⇒ 74 处既有按位置传参的调用点零改动。
- 写路径：`ResolveFileNameEncoding` 改走 `NameEncodingCatalog`（此前只认 `gbk` / `default`，其它一律 UTF-8）；删除条目与添加文件都按会话编码读写，顺带修掉 Big5 包删条目把存活条目名写坏的缺陷。
- 抽取链路：`ArchiveEntryExtractor` 四个公开方法 + 私有链全加参数；`ExtractFlow.RunSelectedItemsExtractionAsync` / `ExtractFlow.ExtractAsync` / `SelectedItemsExtractService.ExtractEntriesAsync` / `CreateExtractOptions` 各加可选 `nameEncoding`，GUI 三处全量解压与两处选中项解压都传 `NameEncodingRequestKey`；CLI 不传（保持 auto）。
- UI：主工具栏末尾 `ComboBox`（`IsVisible=CanChangeNameEncoding`，仅 ZIP/TAR/GZ）+ `Dialogs/CodePageInputDialog`（数字校验）；设置页压缩页签「默认读取代码页」写入 `AppSettings.ReadNameEncoding`，`ClearArchiveInternal` 时作为开包初值。
- i18n：`Toolbar_NameEncoding` / `_Auto` / `_System` / `_Other` / `_OtherTitle` / `_OtherHint` / `Settings_Zip_ReadEncoding` 共 7 个 key × 三语（总数 1398 → 1405）。

### 11.3 验证

- `build -Target core -c Release -t:Rebuild` 0 警告；`build -Target avalonia -c Release` 0 警告。
- `test -Target tests` 全绿（`NameEncodingCatalogTests` 6 例 + `ZipNameEncodingTests` 3 例全过；仅存量 2 条警告）。
- `test -Target ui-tests` 全绿（28 条存量警告）。
- `i18n` 1405 key 三语一致；`check` 0.5.5 六处；`guards` 8 项。

### 11.4 仍未验收

- 真机手测：Big5 / Shift-JIS 包切代码页后清单、预览、解压、删条目是否符合预期（自动化只覆盖 ZIP 读写与键表）。
- RAR 未接（条目名同样有编码问题，但当前引擎读名字的方式不同，另立计划）。