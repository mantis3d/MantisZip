# 网页类内容预览：HTML 兄弟资源 / MHT / CHM（issue #79）

> **状态**: 📋 待实施（设计已定稿，样本已实测） | **创建**: 2026-11-12 | **优先级**: A 档（插队）
> **预估工时**: 12-20h（A + B + C 三档；D 档单独立项）
> **分支**: 产品代码落 `alpha`（①是 bug，但为免同批改动跨两个分支，整体按新功能走 `alpha`，见 §9 决策 2）；本文件与 `docs/PLAN.md` / `docs/KNOWN_ISSUES.md` 的改动可在 `plans` 分支完成（规则 19）
> **issue**: [mantis3d/MantisZip#79](https://github.com/mantis3d/MantisZip/issues/79)——`.HTM 及 .MHT 及 .CHM 预览`
> **目标**: 让压缩包内的 `.htm/.html` 能显示图片样式、`.mht/.mhtml` 能渲染、`.chm` 能像压缩包一样打开并把内页直接看掉

---

## TL;DR

issue #79 是**三件独立的事**，可行性差异很大，而且①根本不是「WebView 不支持」，是**两个实现缺陷**：

1. **`.htm` 丢图丢样式** —— 根因有两个，缺一都修不好：
   - `PreviewService.ExtractToTempAsync`（`Services/PreviewService.cs:780`）**只解压被选中的那一个条目**，同包里 `TEST_files/` 的 85 个资源从未落盘；
   - 资源落盘也不够：样本里 183 个 `<link>` 全带 `crossorigin="anonymous"`，在 `file://` 下走 CORS 检查、**CSS 全灭**（实测：带它无样式，去掉它整页正常）。
2. **`.mht` 能满血渲染** —— Chromium 直接 `file://` 导航 `.mht` 效果与 `.htm` 一致（实测逐像素级一致）。**约束**：必须保留 `.mht` 后缀、**不能注入 CSP meta**（会破坏 multipart 结构）。天花板是 Chromium 自己的 MHTML 实现（见 KI-018）。
3. **`.chm` 性价比最高** —— 仓库自带 `7z.dll` 26.00 x64/x86 都含 `Chm` handler，`SharpSevenZip` 可直接列条目 / 单条提取 / 整包解压（实测 296 条目 405ms）。接入现有格式体系即可，**零新依赖**。

**结论**：做 A + B + C，约 12-20h。D（CHM 阅读器：首页 + TOC 侧栏）复用 A 的打包器，单独立项。

---

## 1. 决策与实测依据

### 1.1 2026-11-12 与用户敲定的三条

| # | 问题 | 决定 |
|---|------|------|
| 1 | 做到哪一档 | **A + B + C**；D 单独立计划（等 A 落地后成本才降下来） |
| 2 | 分支 | 先到 `plans` 制定计划再定（本文件）；①虽是 bug，但**整体走 `alpha`**，避免同批改动跨两分支 |
| 3 | `crossorigin` 是否做成开关 | **无条件剥**、只对临时副本生效，**不新增设置项**（`integrity` 样本里 0 个，一并剥，理由见 §3.3） |

### 1.2 样本实测（`TestPreview/Test_{HTM,MHT,CHM}.zip`，不入版本库）

| 样本 | 结构 | 实测结果 |
|---|---|---|
| `Test_HTM.zip` | `Test_HTM.htm`(348 KB) + `TEST_files/` 85 个资源，整包 12.6 MB | 资源引用 `./TEST_files/…`；**只解被引用的 84 个 = 6.69 MB** |
| `Test_MHT.zip` | 单个 `.mht` 9.5 MB，`From: <Saved by Blink>`，multipart/related 85 段（42 css / 24 png / 11 svg / 5 gif / 2 jpeg） | `file://` 直渲染**正常**，与去 `crossorigin` 的 HTM 几乎逐像素一致 |
| `Test_CHM.zip` | `7-zip.chm`(103 条目) + `WinRAR.chm`(296 条目) | `Format=Chm`；整包解压 405 ms；`#SYSTEM` 首页 `start.htm` / `html/HELPRarInfo.htm` 均存在且渲染正常 |

### 1.3 关键对照实验（headless Edge，全部实跑）

| 实验 | 结论 |
|---|---|
| 同一页 + 现有 CSP（`'self'`）/ 不注入 CSP | 输出**逐字节相同**（10054 B，都带样式）⇒ **现有 CSP 不拦同目录资源**，上次「CSP 是元凶」的推测**被推翻** |
| 同一页 + `default-src 'none'` | 3725 B，样式图片全灭 ⇒ CSP 注入机制本身有效（对照有效） |
| `<link crossorigin>` vs 无 | 4233 B 无样式 vs 5298 B 正常 ⇒ **crossorigin 是真正元凶** |
| 剥 `crossorigin` + 注入现有 CSP | 114992 B，与 `--allow-file-access-from-files` 一致 ⇒ **不需要额外启动参数** |
| 无扩展名 PNG（`TEST_files/2351096`） | 正常显示 ⇒ **32 个无后缀资源不用改名** |
| HtmlAgilityPack 1.12.1 往返重写 | 344829 B，`<!DOCTYPE>` / `<link>` / `<img>` / `<svg>` 结构全保留，渲染与手工替换**一致（477355 vs 477382 B）** ⇒ 用 DOM 改属性**安全** |
| `#SYSTEM` 字段表（顺序 `{code,len,data}`） | `code=2` 首页、`3` 标题、`4` HHC、`6` HHK —— 两个样本都能正确解出 |

---

## 2. 现状（代码事实，行号已核）

### 2.1 HTML 预览链路

```
MainWindowViewModel.ShowPreviewAsync        (MainWindowViewModel.Preview.cs:451  case PreviewType.Html)
  → 大小闸门：entry.Size > MaxTextPreviewBytes(1MB) 直接拒     (:458)
  → PreviewService.LoadPreviewContentAsync   (PreviewService.cs:536)
      → PreviewType.Html 不进内存分支（注释明说）→ ExtractToTempAsync  (:617 → :780)
          ⚠️ 只解压选中的那一个条目，兄弟资源从不落盘
  → PreviewViewModel.ShowHtmlPreview         (PreviewViewModel.cs:3247)
      → 按 AppSettings 组 CSP → InjectCspMeta (:3282 → :1063)
      → 另写一份 %TEMP%\MantisZip\Preview\preview_<guid>.html → HtmlWebViewUri
  → PreviewPanel.EnsureWebViewForHtml        (PreviewPanel.axaml.cs:238) 惰性建 NativeWebView
```

### 2.2 三处必须注意的既有实现

- **CSP 拼接代码重复两份**：`PreviewViewModel.cs:1023-1029`（`RebuildHtmlAsync`）与 `:3276-3282`（`ShowHtmlPreview`）。本次若统一走一个 builder，两处合并成一处。
- **HTML 用的是 `MaxTextPreviewBytes`(1 MB) 闸门**（`:458`），但 `MaxPreviewFileSize` 才是「需要完整内容」的闸门（20 MB）。逻辑错位：一个 1.5 MB 的网页会被当成「文本过大」拒掉。
- **`NativeWebView` 有 `WebResourceRequested` 事件**（已反射确认 `Avalonia.Controls.WebView` 12.1.0 暴露该事件 + `WebViewWebResourceRequest.Uri/Headers/Method`）。**本次不用**——`file://` + 同目录落盘已验证足够，不引入网络层拦截（复杂度与跨平台风险都更高）。

### 2.3 CHM / MHT 目前**完全不认**

- `FileFormatDetector`：无 `ITSF` 魔数、无 `.chm` / `.mht` / `.mhtml` 扩展名映射（`:430-500` 区间）。
- `ArchiveExtensionCatalog.All`：无 `.chm`（`Utils/ArchiveExtensionCatalog.cs:39-58`）。
- `ArchiveFormat` 枚举只有 `Zip/SevenZip/Tar/GZip/Rar/Iso`（`Abstractions/ArchiveEngine.cs:314`）。
- `SevenZipEngine.CanHandle` 只认 `SevenZip|Rar|Iso`（`Engines/SevenZipEngine.cs:470`）。
- 全仓 `grep chm|mht` **零命中**（除无关的 `OnAttachedToVisualTree`）。

---

## 3. 方案

### 3.1 A 档 —— 兄弟资源打包器（修 ①）

新增 `Services/PreviewAssetBundler.cs`（UI 层，纯逻辑，可测）：

```
BundleAsync(archivePath, entry, format, allEntries, password, ct) → BundleResult
  ├─ ① 取 HTML 文本（沿用现有落盘，或直接读临时文件）
  ├─ ② HtmlAgilityPack 解析，收集 src/href/data-src 里的**相对路径**
  │     · 跳过 http/https/data/cid/javascript: 及 #fragment
  │     · 只保留能映射到包内条目的引用
  ├─ ③ 相对路径 → 包内条目名（相对 HTML 所在目录解析，支持 .. 上跳）
  ├─ ④ 预算控制：单文件 > 20 MB 跳过；累计 > 50 MB 停止追加并记「已截断」
  ├─ ⑤ engine.ExtractEntriesAsync(archivePath, keys, bundleDir, …, outputPathOverrides)
  │     · outputPathOverrides 把每个条目钉到 bundleDir/<原相对路径>（ZipEngine.cs:753 / SevenZipEngine.cs:1621 都支持）
  ├─ ⑥ 生成 bundleDir/<entry 文件名>：剥 crossorigin / integrity 后写盘
  └─ 返回 (htmlPath, extractedCount, skippedCount, truncated)
```

要点：

- **必须用 `outputPathOverrides`**，否则 `FileConflictHelper.GetSafePath` 会把资源平铺到 `destinationPath` 下，相对结构丢失、CSS 里的 `../icons/` 会断。两个引擎都已支持该参数。
- **只解被引用的**：样本 12.6 MB → 6.69 MB。不能整包解（大包会写满临时盘）。
- **剥 `crossorigin` / `integrity`** 用 HtmlAgilityPack 的 `Attributes.Remove`，实测往返无损。**不用正则**（属性顺序/引号风格/自闭合标签都会翻车）。
- `HtmlAgilityPack` 目前是 `ReverseMarkdown` 的**传递依赖**；本次直接使用，须在 csproj **显式**加 `1.12.1`（与传递版本对齐，不产生新下载）。
- **大小闸门改对**：`MainWindowViewModel.Preview.cs:458` 的 `MaxTextPreviewBytes` 改判 `MaxPreviewFileSize`。
- **降级**：任何一步失败 → 回落到今天的单文件路径（只是没有图片样式），并保持 `IsFallbackActive` 语义不变。

### 3.2 B 档 —— MHT / MHTML

- `FileFormat` 加 `Mht`；`PreviewType` 加 `Mht`；`FileFormatDetector` 加 `.mht/.mhtml` + 魔数（头部含 `MIME-Version:` 且含 `multipart/related`，或 `From: <Saved by`）。
  - ⚠️ 必须**排在 `DetectTextSubtype` 之前**判定，否则会被文本嗅探吃成 `Text`。
- `PreviewService.ClassifyPreview` / `MapFileFormatToPreviewType`（`:287` / `:411`）加分支。
- `PreviewViewModel.ShowMhtPreview(path)`：**把 `.mht` 落到 `preview_<hash>.mht`（保留后缀！）后直接 `HtmlWebViewUri = path`**，`HtmlSourceContent` 留空（源码态显示为「二进制容器，无源码可切换」）。
- **绝不注入 CSP meta**：往 MIME multipart 里插 `<meta>` 会直接破坏结构（第一步就是 `From:` 头，不是 HTML）。单独一条分支，不经过 `InjectCspMeta`。
- 闸门用 `MaxPreviewFileSize`（20 MB）；9.5 MB 样本可过。

### 3.3 C 档 —— CHM 当只读容器

| 改动点 | 文件:行 |
|---|---|
| `ArchiveFormat` 加 `Chm` | `Core/Abstractions/ArchiveEngine.cs:314` |
| `CanHandle` 认 `Chm`（`CanAdd`/`CanDelete` **保持只认 SevenZip** ⇒ 天然只读） | `Core/Engines/SevenZipEngine.cs:469-474` |
| 4 处 `switch (format)` + `ResolveDispatchFormat` | `Core/Utils/ArchiveEntryExtractor.cs:54/259/352/925`、`:564` |
| `GetFormatByExtension` / `GetEngineByExtension` | `Core/Abstractions/ArchiveEngine.cs:664/730` |
| 目录加一行（自动带动设置页 / 关联默认值 / 打开对话框筛选） | `Core/Utils/ArchiveExtensionCatalog.cs:39` |
| `FileFormat` 加 `Chm` + `ITSF` 魔数（`49 54 53 46` = `ITSF`）+ `.chm` 扩展名 | `Core/Utils/FileFormatDetector.cs` |
| 批量「解压选中项」放行（否则 `CanSeedEntries` 会拦下） | `UI/Services/ExtractFlow.cs:431` |
| 压缩后大小列语义（未压缩，按可用处理） | `UI/ViewModels/MainWindowViewModel.Navigation.cs:266` |
| 图标（`archive` 类） | `UI/Models/IconProvider.cs:23`、`UI/Services/ShellIntegration.Assoc.cs:440` 一带 |
| **过滤系统条目**（`#SYSTEM` / `$WWKeywordLinks` 等 11-17 项不进入条目树） | 引擎 `ListEntriesAsync` 的 `.Select` 之前，`SevenZipEngine.cs:1415` 一带已算出 `format`，直接复用 |

**必须做的防御**：`7z.dll` 路径可由用户改（`SevenZipEngine.SevenZipDllPath` + `SevenZipDllResolveCallback`）。老版本 / 精简版 7z.dll 可能没有 `Chm` handler → `SharpSevenZipExtractor` 构造抛异常。要求：**捕获后给「当前 7z.dll 不支持 CHM」的明确提示**（`LocalizationManager.T` 新增三语 key），不能冒泡成崩溃。

### 3.4 D 档 —— CHM 阅读器（**不在本计划范围**）

`#SYSTEM` 字段表已确认能解（`code=2` 首页 / `3` 标题 / `4` HHC / `6` HHK）；`.hhc` 是标准 sitemap（`<OBJECT type="text/sitemap"><param name="Name" …><param name="Local" …>`），可解析成 TOC 树。实现路径：整包解压 → 解析 `#SYSTEM` 拿首页 → 复用 A 的打包逻辑 → 侧栏 TOC。

**留待 D 档决议的已知落差**：`ms-help://` 协议、HxRuntime 脚本在 WebView2 下不工作（7-zip.chm 的首页正是无脚本的，故看起来正常；WinRAR.chm 的部分页会丢脚本行为）。这是**用户预期管理**问题，必须单独立项讨论。

---

## 4. 任务拆分（建议 3 个子代理并行，父代理统一跑构建/测试）

### Task 0 —— 前置（父代理先做，5 min）

- [ ] 切 `alpha`；`AppConstants.Version` 与将要发布的阶段号核对（规则 1：不一致先问用户「是否升版本」，不擅自改）。
- [ ] `MantisZip.UI.Avalonia.csproj` 显式加 `<PackageReference Include="HtmlAgilityPack" Version="1.12.1" />`。
- [ ] 确认 `TestPreview/Test_*.zip` 在 `.gitignore` 内（现状：是），样本不进版本库。

### Task A —— 兄弟资源打包器（写：`Services/PreviewAssetBundler.cs` 新文件）

- [ ] 实现 §3.1 的 `BundleAsync`；`outputPathOverrides` 保相对结构。
- [ ] `PreviewViewModel.ShowHtmlPreview`（`:3247`）改走 bundler；**合并** `:1023-1029` 与 `:3276-3282` 两处 CSP 拼接为单一私有方法。
- [ ] 剥 `crossorigin` / `integrity`（HtmlAgilityPack，不用正则）。
- [ ] 占位行：临时 bundle 目录纳入 `PreviewService` 现有清理链（`CleanupPreviewTempDir` / `CleanupPreviewTempOnStartup`，`PreviewService.cs:833/857`）。
- [ ] 闸门改 `MaxPreviewFileSize`（`MainWindowViewModel.Preview.cs:458`）。
- **验收**：样本 `Test_HTM.zip` 预览 → 页面带样式、GIF 能显示、shields.io 徽章显示；大包/加密包/无资源包不崩、回落单文件。

### Task B —— MHT（写：`Services/PreviewService.cs` + `ViewModels/PreviewViewModel.cs` + Core 的 `FileFormatDetector` / `FileFormatInfo`）

- [ ] `FileFormat.Mht`、`PreviewType.Mht`、扩展名与魔数（**先于文本嗅探**）。
- [ ] `ShowMhtPreview`：保留 `.mht` 后缀、**不注入 CSP**、源码态走「无源码可切换」文案（三语 key）。
- [ ] `PreviewCapabilities` 不注册能力（无缩放/透明）；`IsToolbarVisible` 保持 true 但只有源码切换入口。
- **验收**：样本 `Test_MHT.zip` 秒开、整页正常、可滚动；改名为 `.html` 的同样内容**必须**走 B 档以外的老路径（防误判）。

### Task C —— CHM 容器（写：Core 的 `ArchiveEngine.cs` / `SevenZipEngine.cs` / `ArchiveEntryExtractor.cs` / `ArchiveExtensionCatalog.cs` / `FileFormatDetector.cs` / `FileFormatHelper.cs` + UI 的 `ExtractFlow.cs` / `MainWindowViewModel.Navigation.cs` / 三语）

- [ ] 按 §3.3 表格逐项接线。
- [ ] 系统条目（`#` / `$` 开头）在 `ListEntriesAsync` 过滤掉。
- [ ] 7z.dll 不支持 CHM 时的明确提示（不崩）。
- [ ] 三语 key：`Preview_Chm`、`Status_ChmFailed`（或复用通用失败 + 单独原因）。
- **验收**：`Test_CHM.zip` 两个 chm 都能打开、条目树干净（无 `#SYSTEM`）、整包解压可用、添加/删除条目**不可用**（只读）。

### Task T —— 测试（写：`tests/MantisZip.Tests/Utils/*` + `tests/MantisZip.UI.Avalonia.Tests/*`）

- [ ] Core：`FileFormatDetector` 的 CHM/MHT 魔数与扩展名（不依赖 7z.dll，CI 可跑）。
- [ ] Core：`ArchiveEngineFactory.GetFormatByExtension` / `ResolveReaderFormat` 对 `.chm` 的解析。
- [ ] Core：`ArchiveExtensionCatalog` 新行（含 `AssociateByDefault:false`）。
- [ ] UI：合成 zip（`ZipArchive` 运行时自建，不依赖 `TestPreview/`）→ bundler 保相对结构、剥属性、预算截断。
- [ ] UI：MHT 路径**不经过** `InjectCspMeta`（用属性/调用断言，不靠像素）。
- ⚠️ 需要真 7z.dll 的 CHM 打开用例：沿用 `ArchiveFixtures` 的 `if (archive == null) return;` 跳过模式；**命名避开 `SevenZipEngine`**（CI `--filter "FullyQualifiedName!~SevenZipEngine"`，KI-005），否则永不执行。

---

## 5. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| `crossorigin` 之外还有别的坑（未穷尽） | 部分页面仍丢样式 | 每次做完用样本 + 至少一个「其他来源保存的网页」实测；降级路径永远保留 |
| 临时盘被大网页写满 | 用户磁盘 | 只解被引用资源 + 单文件 20 MB / 累计 50 MB 双预算 + 落盘目录纳入启动清理 |
| `7z.dll` 版本差异导致 CHM 打不开 | 该用户完全不可用 | 明确报错文案 + 设置页已有 7z.dll 路径可换；文档记一句「需 7z.dll 支持 CHM」 |
| MHT 是 Chromium 自己存的才渲染得好（第三方 IE 时代 MHT 可能白屏） | 部分用户觉得「没修好」 | 已登 **KI-018**；UI 上不承诺、失败给降级提示 |
| CHM 系统条目漏过滤污染条目树 | 观感差 | 用例钉住「列表里不含 `#` / `$` 开头条目」 |
| `PreviewType` / `FileFormat` / `ArchiveFormat` 三处枚举各加值，可能漏 switch | 静默走到 Unsupported | 三个 `switch` 都有 `_ =>` 兜底但会**静默**；测试矩阵覆盖新值 |

---

## 6. 验收口径（一次说清）

- 压缩包里选中 `Test_HTM.htm` → 页面带样式、GIF 动图能看、徽章显示；
- 选中 `Test_MHT.mht` → 整页正常渲染，不需要解压释放；
- 选中 `.chm` → 像压缩包一样打开、条目树只有真实内容文件、可整包解压、不能加/删条目；
- 上面三条在「7z.dll 缺失 / 不支持 CHM」的环境下都只是**给出原因**，不崩、不静默；
- `-Task check` / `-Task i18n` / `-Task guards` 全绿；Core + UI 测试全绿；CI 过滤规则不新增「永不运行的用例」。

---

## 7. 明确不做（本次）

1. **不做 CHM 阅读器（首页 + TOC 侧栏）** —— D 档，单独立项。
2. **不改 HTML 的 JavaScript / 外部资源安全策略** —— 沿用现有 `AllowJavaScript` / `AllowExternalResources` 设置，不新增开关。
3. **不用 `WebResourceRequested` 做网络层拦截** —— `file://` + 同目录落盘已验证足够。
4. **不支持 `.mht` 里的 `file:///` 绝对引用**（Chromium 也不解析）—— 只在文档与 KI-018 说明。
5. **不做「网页另存为 MHT」** —— 与本 issue 无关。

---

## 8. 关联

- 规则 13（三语 key）、规则 14（AXAML 中文注释，若动 XAML）、规则 21（KI-018）、规则 1（PLAN 同步 + 阶段版本核对）
- 相关既有计划：`.omo/plans/已完成/html-preview-webview-fallback.md`（WebView + 降级双轨，本计划在其上加「兄弟资源」一层）、`.omo/plans/已完成/remove-webview2-preview.md`（历史：曾整体移除 WebView，后被 fallback 计划部分回滚 —— **本计划不重开那条路线**）
- 已知关联缺陷：KI-005（7z 用例被 CI 过滤）、KI-007（大文件预览整条目落盘，CHM 同理）

---

## 9. 待用户确认（开工前）

1. **阶段号**：本计划插在 v0.5.8 之前还是并入 v0.5.8？按规则 1，开工前须核对 `AppConstants.Version` 并确认升版。
2. **分支**：决定整体走 `alpha`（本文件按此写）。若要求①单独走 `beta` 先发补丁版，Task A 需拆成两次提交，成本 +20% 左右。
