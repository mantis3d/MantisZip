# Work Plan: 压缩包识别分层（容器 / 子类型）+ 自解压 exe 支持

> **Status**: 📋 待开始 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜] (0/8)
> **预估工时**: 🟡中 **6-8h**
> **档位**: **A 档**（修「点了没反应」类失信 bug + 兑现一条老 enhancement）
> **对应 issue**: [#54](https://github.com/mantis3d/MantisZip/issues/54)（.jar / .war / .ear / .apk 打不开）、[#36](https://github.com/mantis3d/MantisZip/issues/36)（自解压 exe 打不开）

## Problem

两个 issue 症状不同、坏在同一根链上的两处，都已在 2026-10-27 用一次性探针实测确认。

### #54：.jar / .war / .ear / .apk 打不开 —— 识别得越细，越打不开

实测传导链（真 `app.jar`，`META-INF/MANIFEST.MF` 为首条目，431 字节）：

1. `.jar` 不在 `GetEngineByExtension` 的 switch 里（`src\MantisZip.Core\Abstractions\ArchiveEngine.cs:620-631`），落到魔数兜底 `GetEngineByMagic`。
2. `GetEngineByMagic` 只读前 4096 字节（`ArchiveEngine.cs:660`）→ `FileFormatDetector.Detect`。
3. `Detect` 见 `PK\x03\x04` 就调 `DetectZipSubtype`（`src\MantisZip.Core\Utils\FileFormatDetector.cs:126-132`）；后者扫条目名，只要名字以 `META-INF/` 开头就 `return FileFormat.Odt`（`FileFormatDetector.cs:569-600`，注释原文 "If we see any META-INF/ entry, it's an OpenDocument format"）。
   - jar 的规范首条目就是 `META-INF/MANIFEST.MF` ⇒ **100% 判成 Odt**；war / ear / apk 同理（都含 `META-INF/`）。
4. `MapFileFormatToArchiveFormat`（`ArchiveEngine.cs:681-694`）只认 `Zip/Docx/Xlsx/Pptx/Epub` 五个 zip 子类型，`Odt` 落 `_ => null`（`:692`）⇒ 无引擎。
5. `ArchiveFormatHelper.IsArchiveFile`（`src\MantisZip.UI.Avalonia\Models\ArchiveFormatHelper.cs:13-14`）false ⇒ `src\MantisZip.UI.Avalonia\ViewModels\MainWindowViewModel.cs:769-773` 直接 `StatusMessage = T("Status_UnsupportedFormat", ext)`（「不支持的文件格式: {0}」，`strings.zh-CN.json:1061`）。

同一份字节改名 `.zip` 就正常（命中第 0 层扩展名）⇒ 不是「读不出」，是**识别结论没有对应引擎**。

两个同源缺陷顺带修掉：
- `GetFormatByExtension` 对未知扩展名默认 `ArchiveFormat.Zip`（`ArchiveEngine.cs:616`），而 `GetEngineByExtension` 返回 null（`:630`）——同一文件「格式是 Zip」但「没有引擎」。
- 自适应压缩：jar 本来靠扩展名命中 `ZipEntryClassifier.cs:48` 名单直接 Store（正常）；但改名成未知扩展名且 ≥ `MagicDetectMinBytes`（64KB）时走魔数得 `Odt` → `IsCompressedFormat=false` → 白压一遍（不报错，纯浪费）。

### #36：自解压 exe 打不开 —— 缺一层「桩之后找内嵌包」

实测三样本（`.buildout\sfxprobe\`，已 gitignore）：

| 样本 | `Detect` | `GetEngineByExtension` | `ZipEngine`（SharpCompress） | `SevenZipEngine`（7z.dll） |
|---|---|---|---|---|
| `pk-sfx.exe`（MZ + 尾部 zip） | Unknown | null | 0 条（静默空） | ✅ 3 条 |
| `7z-sfx.exe`（MZ + 尾部 7z） | Unknown | null | 抛 `Failed to locate the Zip Header` | ✅ 4 条 |
| `real-pe-sfx.exe`（真 PE 头 + 尾部 zip，229807 字节） | **Pe** | null | 0 条（静默空） | ✅ 3 条，`ExtractAsync` → `Succeeded=3/Failed=0` |

- 失败发生在最前面：`Detect` 只到 `Pe`（`FileFormatDetector.cs:301-308` 的 MZ + `IsPe` 校验）或 `Unknown` 就返回，**没有任何一层去找 PE 桩之后的内嵌压缩包**。
- `.exe` 不在 `SupportedExtensions`（`ArchiveEngine.cs:581-582`），也不在 `GetEngineByExtension` 的 switch 里。
- **7z.dll 本身能开 SFX**（按签名搜索），所以不需要我们解析 PE 或计算偏移 —— 只需要把这类文件路由到 `SevenZipEngine`。
- 绝对不能路由到 `ZipEngine`：实测它对 SFX zip 返回 **0 条**而不是报错，失败方式比报错更糟（用户打开后看到空包）。
- 写路径早就认识 SFX：`src\MantisZip.Core\Utils\ZipBinaryRewriter.cs:339-344` 与 `:624-629` 检出 `MZ` 即抛 `ZipCopyModeException("SFX ZIP not supported by copy-mode")` 回退 legacy，以保住自解压能力。缺的只是读路径。

## Objective

1. 把「识别」拆成 **容器（Container）+ 子类型（Subtype）** 两个字段：「当压缩包打开」只看容器，预览仍看子类型 ⇒ 物理上消灭「越识别越打不开」。
2. 收紧 `DetectZipSubtype` 的 `META-INF/` 过度识别：只有 `META-INF/manifest.xml` 才可能与 OpenDocument 有关，`META-INF/MANIFEST.MF` 等回落 `Zip`。
3. 第 3 层映射改成**恒等映射**（`ContainerFormat → ArchiveFormat`），删掉 5 项子类型白名单。
4. 新增 SFX 探测：用户**显式打开** `.exe` 时，尾部能确认内嵌容器就交给 `SevenZipEngine`；**绝不登记 `.exe` 文件关联**。
5. 放行 `.jar/.war/.ear/.apk`（zip 引擎）到扩展名白名单、打开对话框与「打开方式」；`.jar` **不抢默认程序**。
6. 完成后用 `gh` 回复 #54 与 #36，并自行判断是否关闭。

## 用户已拍板的口径（2026-10-27）

| # | 口径 |
|---|------|
| 1 | issue #54（.jar 系列）与 #36（自解压 exe）**合并进同一份计划**做 |
| 2 | `.jar` 文件关联**默认不抢**：只登记到「打开方式」，不改默认程序 |
| 3 | SFX 仅在**用户显式打开**时探测；**绝不**给 `.exe` 登记文件关联 |
| 4 | 完成后由 agent 用 `gh` 回复这两个 issue，并自行判断是否关闭 |
| 5 | 其余按本文档建议 |

## 设计

### 1) 双字段识别（Core）

新文件 `src\MantisZip.Core\Utils\FormatDetection.cs`：

```csharp
public enum ContainerFormat { Unknown, Zip, SevenZip, Rar, Tar, GZip, Iso }

public readonly record struct FormatDetection(ContainerFormat Container, FileFormat Subtype)
{
    public static readonly FormatDetection Unknown = new(ContainerFormat.Unknown, FileFormat.Unknown);
    public bool IsContainer => Container != ContainerFormat.Unknown;
}
```

`FileFormatDetector` 新增：

```csharp
// 只跑容器魔数分支，命中即返回；PK 不再进 DetectZipSubtype
public static ContainerFormat DetectContainer(byte[] head, int length);

// 容器 + 子类型（新入口）
public static FormatDetection DetectFormat(byte[] head, int length, byte[]? tail = null);
```

- **`Detect` 的签名与语义保持不变**（`FileFormatDetector.cs:18`），内部改为 `DetectFormat(...).Subtype`，避免动 4 个既有消费者。
- `DetectContainer` 覆盖现状 `Detect` 里的容器分支：ZIP（`PK\x03\x04`，另补 `PK\x05\x06` 空档、`PK\x07\x08` 分卷 —— 现状未覆盖）、7z（`37 7A BC AF 27 1C`）、RAR（4/5，`52 61 72 21 1A 07 …`）、gzip（`1F 8B`）。
- **Tar / ISO 现状只有扩展名识别，本计划不新增魔数分支**（`Detect` 里没有 `ustar` / `CD001` 判定），写清楚避免误期。
- 消费者改造：
  - `ArchiveEngineFactory.GetEngineByMagic`（`ArchiveEngine.cs:655-675`）改调 `DetectContainer`，再走恒等映射 ⇒ 打开压缩包不再需要解析子类型（省掉 `DecompressDeflateBlock` 的 CPU）。
  - `AdaptiveRuleMatcher`（`src\MantisZip.Core\Utils\AdaptiveRuleMatcher.cs:78` `MatchCustomFormatByMagic`、`:93` 「内容已压缩 → Store」）改读容器字段 ⇒ 顺带修掉改名 `.dat` 白压。
  - `PreviewService.ClassifyPreviewByMagicAsync`（`src\MantisZip.UI.Avalonia\Services\PreviewService.cs:225`）继续用 `Detect`（它需要子类型选预览器）。

### 2) 收紧 `META-INF/` 识别

`FileFormatDetector.DetectZipSubtype`（`:569-601`）改为：先精确判 `META-INF/manifest.xml`（OpenDocument 的真正标志，读内容细分 ODT/ODS/ODP，读不出 mime 时仍兜底 `Odt`），再单独处理「其他 `META-INF/` 条目」——**不 return，继续扫下一个条目**：

```csharp
else if (fileName.Equals("META-INF/manifest.xml", StringComparison.OrdinalIgnoreCase))
{
    // …现有内容解析保留…
    return FileFormat.Odt;   // manifest.xml 在、mime 读不出 → 按 ODT 兜底
}
else if (fileName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
{
    // jar / war / ear / apk 的 META-INF/MANIFEST.MF 等不是 OpenDocument 标志，继续扫
}
```

- **不新增 `FileFormat.Jar`**：收紧后 jar 的子类型回落到 `Zip`，预览自然落到「压缩包内的压缩包不可预览」分支（与现状一致）。

### 3) 第 3 层恒等映射

`ArchiveEngine.cs:681-694` 换成：

```csharp
internal static ArchiveFormat? MapContainerToArchiveFormat(ContainerFormat container) => container switch
{
    ContainerFormat.Zip      => ArchiveFormat.Zip,
    ContainerFormat.SevenZip => ArchiveFormat.SevenZip,
    ContainerFormat.Rar      => ArchiveFormat.Rar,
    ContainerFormat.Tar      => ArchiveFormat.Tar,
    ContainerFormat.GZip     => ArchiveFormat.GZip,
    ContainerFormat.Iso      => ArchiveFormat.Iso,
    _ => null
};
```

- 删掉 `MapFileFormatToArchiveFormat`（实施时确认无其他调用点；若有，改成 `MapContainerToArchiveFormat(detection.Container)`）。
- `FileFormat.Encrypted` 的容器归属按魔数给真值：7z 签名 → `SevenZip`，`PK` → `Zip`，`Subtype = Encrypted`。

### 4) SFX 探测（#36）

新文件 `src\MantisZip.Core\Utils\SfxProbe.cs`：

```csharp
// 仅在「扩展名未命中 + 魔数为 Pe/Unknown」时调用
public static bool TryDetectEmbeddedContainer(string filePath, out ContainerFormat container);
```

探测顺序（从便宜到贵）：

1. 读头 4KB：开头就是容器签名（极端情况）→ 命中。
2. 读尾 8KB 窗口扫描容器签名：`PK\x03\x04`、`7z` 签名、`Rar!`、`1F 8B`。命中位置必须 > 头部已读区，避免与文件头误配。
3. 都不中 → `false`（当普通文件，保持现有「不支持的格式」提示）。

明确**不做**：全文件扫描（大 exe 卡顿）、PE 段解析（7z.dll 自己会算偏移）。

命中后的路由：

- 统一返回 `SevenZipEngine`（**不能**返回 `ZipEngine`——实测它对 SFX zip 静默返回 0 条）。
- 触发条件收窄：**仅**用户显式打开单个文件时（`MainWindowViewModel` 的打开入口 / 拖入 / 打开对话框 / CLI 打开）。目录扫描、关联登记一律不探测。
- 文案区分两种情况（规则 13 三语 key）：不是压缩包 → 沿用 `Status_UnsupportedFormat`；是压缩包但引擎报错 → 沿用现有引擎异常提示，不吞。

### 5) 白名单 / 对话框 / 关联

| 位置 | 改动 |
|---|---|
| `ArchiveEngineFactory.SupportedExtensions`（`ArchiveEngine.cs:581-582`） | 追加 `.jar`、`.war`、`.ear`、`.apk`（**不加** `.exe`） |
| `GetEngineByExtension` switch（`:620-631`） | 上述四项 → `GetEngine(ArchiveFormat.Zip)` |
| `GetFormatByExtension`（`:604-618`） | 上述四项；默认分支与 engine 语义对齐（实施时确认调用点影响面） |
| 打开文件对话框过滤（`src\MantisZip.UI.Avalonia\Views\MainWindow.axaml.cs:1467` 附近） | 压缩包过滤模式追加四项；`.exe` 作为**可选**过滤项、不默认选中 |
| `src\MantisZip.UI.Avalonia\Services\ShellIntegration.cs`（`:52-53` / `:56-66` / `:74+`） | 关联登记追加四项；`.jar` **只写 OpenWithProgids（进「打开方式」），不设默认**；`.exe` **绝不登记** |
| 守卫测试 `tests\MantisZip.Tests\ShellIntegrationAssocTests.cs:503-517` | 期望清单同步；新增「`.exe` 不出现在任何关联项」反向断言 |

## 实施步骤

1. **Core 双字段**：新增 `FormatDetection.cs`；`FileFormatDetector` 加 `DetectContainer` / `DetectFormat`，`Detect` 改委派。
2. **收紧 META-INF**：改 `DetectZipSubtype`。
3. **恒等映射**：改 `ArchiveEngineFactory`。
4. **SFX 探测**：新增 `SfxProbe`，接到打开入口的兜底路径。
5. **白名单 / 对话框 / 关联**：按上表改。
6. **文案**：新增提示走三语 key（`strings.zh-CN.json` / `strings.en.json` / `strings.zh-TW.json` 成对插入）。
7. **验证**：`pwsh -File scripts\verify-repo.ps1 -Task build -Target core` → `-Target avalonia` → `-Task test -Target ui-tests` → `-Task check` → `-Task guards`；Core 测试项目本机跑不了（xUnit v2 + VSTest 起不来，见 KI-002），靠 CI 实测。
8. **回复 issue**：`gh issue comment 54` / `gh issue comment 36` 说明根因与修法；CI 绿后按 `stateReason=completed` 关闭。若留下未覆盖项（tar/iso 无魔数识别、`.exe` 不登记关联），**先登记 `docs/KNOWN_ISSUES.md` 的 `KI-nnn` 再关**，并在评论里写明「未覆盖项已登记为 KI-xxx」。

## 测试计划

- **Core 单测**（`tests\MantisZip.Tests`，xUnit v2）：
  - `DetectContainer`：zip / 空 zip（`PK\x05\x06`）/ 7z / rar / gzip / 未知 —— 6 类 + 负样本。
  - `Detect` 回归：docx / xlsx / pptx / epub / odt 结果不变。
  - `DetectZipSubtype`：jar 夹具（首条目 `META-INF/MANIFEST.MF`）→ `Zip`；war 夹具 → `Zip`；真 odt 夹具（`mimetype` + `META-INF/manifest.xml`）→ `Odt`。
  - `ArchiveEngineFactory`：`.jar/.war/.ear/.apk` → `ZipEngine`；`.exe` 不探测时 → null。
  - `SfxProbe`：MZ+zip、MZ+7z、真 PE 头+zip 三正样本 + 真 exe 无内嵌负样本（样本按字节现拼，不入库）。
- **UI 单测**（`tests\MantisZip.UI.Avalonia.Tests`）：`ArchiveFormatHelper.IsArchiveFile(".jar") == true`。
- **手工验证**：真实 `app.jar`（`.buildout\jarprobe\`）打开并浏览条目；`real-pe-sfx.exe` 打开 + 解压 3 条目。

## 风险与回滚

| 风险 | 概率 | 缓解 |
|---|---|---|
| 收紧 META-INF 后真 ODF 识别回退 | 中 | odt/ods 两条夹具单测 + 保留「manifest.xml 在但 mime 读不出 → Odt 兜底」 |
| 恒等映射改变某些子类型行为（如泛型 `OfficeOpenXml`） | 中 | 这些子类型的容器都是 Zip；单测对 20+ 子类型全表断言「容器 = Zip 且引擎非 null」 |
| SFX 探测把普通 exe 误判成压缩包 | 中 | 尾窗 8KB 才有机会命中；命中位置须 > 头读区；`.exe` 不进关联；探测失败不改变原有报错 |
| `.jar` 关联抢用户默认软件 | 高（若写错） | 只写 OpenWithProgids、不写默认；守卫测试反向断言 |
| Core 测试本机不可跑 | 已知 | 本地只编译 + 靠 CI（KI-002） |

回滚：改动集中在 Core 3 个文件（`FileFormatDetector` / `ArchiveEngine` / 新增 `FormatDetection`、`SfxProbe`）+ UI 3 处（对话框、`ShellIntegration`、文案），可整体 revert 一个提交。

## Definition of Done

- [x] `FormatDetection` / `DetectContainer` 落地，`Detect` 行为不变（回归单测通过）
- [x] `.jar/.war/.ear/.apk` 能作为压缩包打开（手工 + 单测）
- [x] 自解压 exe 能打开并解压（单测 + 路由到 `SevenZipEngine`）
- [x] `.exe` 未进任何文件关联；`.jar` 未抢默认程序（守卫测试通过）
- [x] `pwsh -File scripts\verify-repo.ps1 -Task check` exit 0；Core / Avalonia 构建零错误
- [ ] 推送 alpha 后 CI 绿
- [ ] `gh` 回复 #54 / #36 并给出关闭判定；未覆盖项已进 `docs/KNOWN_ISSUES.md`
- [x] 规则 3：`docs/PROGRESS.md` + `docs/progress-avalonia-detail.md` 已更新
- [ ] 规则 18：向用户确认 `RELEASE_NOTES.md` 更新范围

## 实施结果（2026-10-27 完成）

**新增文件**

| 文件 | 作用 |
|---|---|
| `src/MantisZip.Core/Utils/FormatDetection.cs` | `ContainerFormat` 枚举 + `FormatDetection(Container, Subtype)` |
| `src/MantisZip.Core/Utils/SfxProbe.cs` | MZ 桩尾部窗口探测内嵌 zip/7z/RAR 签名 |
| `tests/MantisZip.UI.Avalonia.Tests/FormatDetectionContainerTests.cs` | 27 例（容器识别 / jar 回归 / ODF 不误伤 / SFX / 引擎路由 / 关联负向守卫） |

**改动文件**：`FileFormatDetector.cs`（`DetectContainer` + `DetectFormat` + 收紧 `META-INF/` + 修扫描器推进）、`ArchiveEngine.cs`（`SupportedExtensions` +4 / `GetEngineByExtension` +4 / `GetFormatByExtension` +4 / `MapContainerToArchiveFormat` 恒等映射 / `GetEngineByMagic` 改走容器 + SFX 兜底）、`ShellIntegration.cs`（`ArchiveExtensions` 改显式 8 项）、`MainWindow.axaml.cs`（打开对话框过滤器改为从 `SupportedExtensions` 派生）、`AdaptiveRuleMatcher.cs`（注释明确「按子类型而非容器判已压缩」）、`tests/MantisZip.Tests/ShellIntegrationAssocTests.cs`（守卫改 `CoversAssociatedExtensions` + 反向断言）。

**与计划的偏差（3 处，均有据）**

1. **测试放 UI 测试项目而非 Core 测试项目**：Core 是 xUnit v2，本机 VSTest 起不来（KI-002），放过去等于写完无法本地验证。UI 测试项目同样引用 Core，本地 `dotnet exec` 可跑（本次 478 例全绿）。
2. **文件关联一个都没新增**：`ShellIntegration.ArchiveExtensions` 从「= `SupportedExtensions`」改为显式 8 项常量，关联/右键菜单/设置页三者行为完全不变；`.jar/.war/.ear/.apk` 只进「能打开」。要给它们进关联需新增 4 个 `AppSettings` 开关 + 设置页条目，**登记为 KI-006**。
3. **未新增三语文案**：打开失败仍复用既有 `Status_UnsupportedFormat`；SFX 打开成功走正常列表路径，不需要新文案。

**额外修掉的既有 bug（必须记一笔）**：`DetectZipSubtype` 的扫描循环原来按 `pos += 头部 + 名字 + extra` 推进，**漏掉了压缩数据区**，所以它只能看见第一个条目。后果：ODF 的 `manifest.xml` 排在 `mimetype` 之后 ⇒ 从未被扫到 ⇒ `Odt/Ods/Odp` 识别对真实文件一直是死的（jar 能误判成 Odt 恰恰因为 `MANIFEST.MF` 排第一）。现改为按 `compressedSize` 精确跳过、不匹配时向前搜索下一个 `PK\x03\x04`，并新增 `Detect_RealOpenDocumentText_StillReportsOdt` 回归用例。

**验证口径**：Core / Avalonia 构建 0 警告 0 错误；UI 测试 478 全绿（3 skip 为既有）；`verify-repo.ps1 -Task check` exit 0；真夹具 `.buildout\jarprobe\app.jar` → `Zip/Zip` + `ZipEngine`（修前 `Odt` + null）、`.buildout\sfxprobe\real-pe-sfx.exe` → `Unknown/Pe` + **`SevenZipEngine`**（修前 null）。
