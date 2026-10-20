# 文本 / ZIP 注释回退编码策略（非中文 locale 下的 GBK 乱码 + copy-mode 注释损坏）

> **状态**: 🟡 **Phase 1 已实施（A1 + A3 + T6，2026-10-20）**；Phase 2（A2 可配置回退代码页）待定 | **创建**: 2026-10-20 | **优先级**: P2 | **难度**: 🟡中 | **预估工时**: 4-6h（Phase 1 实际 ~2h）
> **来源**: 2026-10-20 CI 首跑暴露 `MainWindowViewModelCommentTests.LoadArchive_WithGbkComment_DecodesCorrectly` 失败（临时以测试侧固定 `zh-CN` 收口，commit `9ac51c0`）；本计划处理其**生产侧真实缺陷**。

---

## 1. 背景

非中文 locale 的 Windows（如 CI runner `en-US`）上，打开含 GBK 注释的 ZIP，注释显示为 `»¶Ó­Ê¹ÓÃ±¾Ñ¹Ëõ°ü` 而非 `欢迎使用本压缩包`。

- **触发条件**：注释不是合法 UTF-8（中文 Windows 下大量旧工具/资源管理器写入的 ZIP 注释都是本地代码页 GBK）
- **影响用户**：所有非 zh-CN 系统 locale 的中文用户（英文版 Windows、系统 locale 改成英文、企业统一镜像等）；这类用户群体在中文软件里并不小
- **影响对象**：ZIP 注释显示；同类缺陷还会污染「copy-mode 重写」时的注释**落盘字节**（见 §3 缺陷 2，属数据损坏）

CI 失败详情（复现证据）：

```
Assert.Contains() Failure: Sub-string not found
String:    "»¶Ó­Ê¹ÓÃ±¾Ñ¹Ëõ°ü"
Not found: "欢迎使用本压缩包"
```

同一批字节 `BB B6 D3 AD CA B9 D3 C3 B1 BE D1 B9 CB F5 B0 FC`（16 字节 GBK）：
- 按 936 解码 → `欢迎使用本压缩包`
- 按 1252 解码 → `»¶Ó­Ê¹ÓÃ±¾Ñ¹Ëõ°ü`（逐字符与 CI 报错一致）

---

## 2. 现状（代码级）

> 下表为**改动前**的现状快照，用于说明缺陷来源；实际已按方案 A 修复，见 §6 状态列。
| # | 位置 | 当前行为 | 症状 |
|---|------|---------|------|
| 1 | `Core/Utils/ZipCommentHelper.cs:148` | `TextEncodingDetector.DecodeText(trimmed)`（`systemFallbackCodePage = 0`） | 注释按**宿主 ANSI** 解码 → 非中文 Windows 上 GBK 注释乱码 |
| 2 | `Core/Utils/TextEncodingDetector.cs:100-102` | `cp = systemFallbackCodePage > 0 ? 该值 : CultureInfo.CurrentCulture.TextInfo.ANSICodePage` | 回退代码页随进程 culture 漂移（zh-CN=936 / en-US=1252 / ja-JP=932） |
| 3 | `UI.Avalonia/ViewModels/PreviewViewModel.cs:954`（`"system"` 选项） | `DecodeText(bytes, null)` → 同上 | 文本预览「系统 ANSI」在非中文 Windows 上同样解不出 GBK |
| 4 | `UI.Avalonia/ViewModels/PreviewViewModel.cs:950`（`auto` 路径） | `DetectAndDecodeText` → Ude 置信度 <0.5 时落到同一回退 | 短文本 Ude 检测不出（16 字节注释必然低于阈值），只能靠回退 |
| 5 | `Core/Utils/ZipBinaryRewriter.cs:163-171`（`ReadEocd`） | `Encoding.UTF8.GetString(...)`，`catch (DecoderFallbackException)` 再 `Encoding.Default.GetString(...)` | **双重错误**，见 §3 缺陷 2：GBK 注释被解成 `���`（U+FFFD） |
| 6 | `Core/Utils/ZipBinaryRewriter.cs:1186-1209`（`WriteEocd`） | `Encoding.UTF8.GetBytes(comment)` | 把上一步已损坏的字符串重新编码写回 → **原始注释字节永久丢失** |

**copy-mode 的注释往返链路**（实际会落到缺陷 2）：

```
ReadEocd(source) → existingComment (string, 已损坏)
  ↓  ZipBinaryRewriter.cs:335 / 606
effectiveComment = comment ?? existingComment ?? ""   （:454 / :735）
  ↓
WriteEocd(output, ..., effectiveComment)               （:455 / :736）
  ↓  Encoding.UTF8.GetBytes
磁盘上注释 = U+FFFD 序列（原 GBK 字节不可恢复）
```

---

## 3. 根因（两条独立缺陷，不能混为一谈）

### 缺陷 1：回退代码页绑定进程 culture

`TextEncodingDetector` 用 `CultureInfo.CurrentCulture.TextInfo.ANSICodePage` 作为「系统 ANSI」。
这是 .NET 的**合法**语义（「本机 ANSI 代码页」），但对本产品是错误默认值：

- ZIP 注释**没有编码标志字段**（EOCD 无 encoding flag），历史上中文工具一律写 CP936；
- 产品是中文优先应用，注释的解码目标应该是「中文工具写的注释」，而不是「宿主 locale 的代码页」；
- 结果：把「机器是中文系统」当成隐含前提，与 AGENTS.md 记录的 `DiskTypeDetectorTests` 同类（环境假设写进实现）。

### 缺陷 2：`Encoding.Default` 在 .NET Core 不等于系统 ANSI，且 `Encoding.UTF8.GetString` 不抛异常

- `Encoding.Default` 在 .NET (Core) 5+ 恒为 **UTF-8**（与 .NET Framework 的 ANSI 语义不同）。`ZipEngine.cs:119 / 1860 / 2909` 的 `"default" => Encoding.Default` 同样踩了这个坑（见 §8 关联缺口）。
- `Encoding.UTF8` 静态实例使用 **替换回退**（`throwOnInvalidBytes: false`），**永不抛** `DecoderFallbackException`。因此 `ZipBinaryRewriter.ReadEocd` 的 `catch` 是**死代码**，GBK 注释只会静默变成 `���`。
- 该缺陷的危害不是「显示乱码」，而是 **copy-mode 重写（删除条目 / 重命名 / 移动等走 copy-mode 的操作）后注释字节被永久破坏**——原文件注释无法恢复。

---

## 4. 影响面（改动前必须核对）

| 消费者 | 入口 | 是否本次改动 |
|--------|------|-------------|
| ZIP 注释读取 | `ZipCommentHelper.ReadComment` ← `MainWindowViewModel`（打开压缩包后显示注释预览） | ✅ 主目标 |
| ZIP 注释写入 | `ZipCommentHelper.WriteComment`（恒 UTF-8） | ⛔ 不改（写入侧统一 UTF-8 是正确的） |
| 文本/ Markdown / HTML / CSV 预览 | `PreviewViewModel.DecodePreviewBytes`（`auto` / `system` / 显式编码三态） | ✅ 仅「回退代码页」可配置化 |
| copy-mode 重写注释 | `ZipBinaryRewriter` 两条链路（`:335/:454/:455` 与 `:606/:735/:736`） | ✅ 改字节透传 |
| 显式编码下拉（`gbk` / `utf-8` / `big5` …） | `DecodeText(bytes, encodingName, …)` | ⛔ 行为不变 |
| `TextEncodingDetector.DetectAndReadText` | **无任何调用点（死代码候选）** | ⛔ 不动，另立清理 |

---

## 5. 方案

### 方案 A（推荐）：**窄域分治**，不做全局编码猜测

三个独立小改动，各自收益明确、误判风险可控：

**A1 — ZIP 注释：固定「UTF-8 → GBK」优先，宿主 ANSI 仅兜底**

```csharp
// ZipCommentHelper.ReadCommentFromStream 末尾
return TextEncodingDetector.DecodeComment(trimmed);
```

新增 `TextEncodingDetector.DecodeComment(byte[])`：
1. UTF-8 BOM → UTF-8
2. 严格 UTF-8（`new UTF8Encoding(false, true)`，失败即非 UTF-8）
3. **GB18030（54936，GBK 超集）** → 中文工具事实标准，解 GBK 字节与 936 结果一致，且额外覆盖 GB18030 专有 4 字节序列
4. 宿主 ANSI 兜底（保持现状，**仅当字节不是合法 GB18030 时**使用）

> 为什么注释可以用「固定 GBK 而非打分」：注释是自由文本、无法知道来源、且**现状本来就是乱码**，固定 GBK 的期望收益远大于误判成本。
> **实现要点（实测踩坑）**：判断「是否为合法 GB18030」必须用**异常回退**解码（`Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)`），不能检查 U+FFFD —— GB18030 的默认解码器把非法/截断序列静默替换成 `?`。

**A2 — 文本预览：默认行为不变，回退代码页改为可注入设置**

- 保持 `DecodeText` 默认语义（宿主 ANSI）——文本预览误判代价高（Western 文本被解成 CJK），不猜；
- 新增 `AppSettings.FallbackCodePage`（`0` = 跟随系统，可选 `936/950/932/949/54936`）；
- Core 不能引用 `AppSettings`，沿用 `CoreLog.Initialize()` 的既有注入模式，新增 `TextEncodingDetector.Initialize(int fallbackCodePage)`，UI 在 `OnFrameworkInitializationCompleted` 与设置保存后各注入一次；
- `PreviewViewModel` 的 `"system"` 选项语义明确为：**设置了就用设置值，未设置才用宿主 ANSI**（下拉标签中的代号同步显示生效值）。

**A3 — copy-mode 注释：字节透传，彻底不 decode/encode**

- `ReadEocd` 返回 `byte[]? commentBytes`（原始字节），不再返回 `string`；
- `WriteEocd` 增加「原样写入字节」路径：`comment == null` 时写 `commentBytes` 原样，显式传入 `string` 时仍 UTF-8 编码；
- 收益：与编码策略**完全解耦**，任何编码的注释在 copy-mode 重写后逐字节不变（可用字节级断言锁定）。

### 方案 B（不推荐）：全局候选代码页打分

候选 `{936, 54936, 950, 932, 949, 宿主 ANSI}`，按「U+FFFD 数 / 控制字符数 / CJK 占比」打分择优。**否决理由**：

- Western 文本（如 `café` → `63 61 66 E9`）用 GBK 解码**不产生 U+FFFD**，却会解出 CJK 假字，打分无法可靠区分；
- Big5 / Shift-JIS 与 GBK 的字节空间大量重叠，需要语种先验，评测成本高；
- 一旦误判，影响面从「注释」扩大到所有文本预览（当前是用户可手动纠正的乱码，误判后仍显示「有内容」的错字，更难发现）。

### 方案 C（最小）：仅做 A1

只让注释不再乱码，copy-mode 损坏与文本预览回退保持现状。可作为 Phase 1 先落地（约 1-1.5h），但**不建议**：缺陷 2 是数据损坏，优先级高于乱码。

### 建议

**A1 + A3 同批做（Phase 1，约 2-3h）**，A2 视反馈再做（Phase 2，约 2h）。A1 无需任何设置项即可解决用户可见问题，A3 阻止数据损坏，二者无互相依赖。

---

## 6. 实施任务

| # | 任务 | 文件 | 验收 | 状态 |
|---|------|------|------|------|
| T1 | 新增 `DecodeComment(byte[])`：BOM → 严格 UTF-8 → 严格 GB18030 → 宿主 ANSI | `Core/Utils/TextEncodingDetector.cs` | 单测：16 字节 GBK → 中文；UTF-8 注释不变；GB18030 专有序列正确 | ✅ |
| T2 | `ReadCommentFromStream` 改调 `DecodeComment` | `Core/Utils/ZipCommentHelper.cs:148` | 单测 + 注释往返（`WriteComment` 后 `ReadComment` 相等） | ✅ |
| T3 | `ReadEocd` 返回原始注释字节；`WriteEocd` 增加字节透传分支 | `Core/Utils/ZipBinaryRewriter.cs:93-173, 1176-1210` | 单测：GBK 注释经 copy-mode 重写后**字节级相等** | ✅ |
| T4 | 修正两条 copy-mode 链路的注释传参（`:454/:455`、`:735/:736`） | `Core/Utils/ZipBinaryRewriter.cs` | 同上；显式传 `comment` 时仍以新值为准 | ✅ |
| T5 | （Phase 2，可选）`AppSettings.FallbackCodePage` + `TextEncodingDetector.Initialize()` 注入 + 设置 UI | `Models/AppSettings.cs`、`App.axaml.cs`、`SettingsWindow.axaml(.cs)`、`Localization/strings.*.json` | 设置项三语键成对；`system` 下拉显示生效代码页 | ⬜ 待定 |
| T6 | **撤销测试侧 `zh-CN` pin**，改为在 `en-US` culture 下断言中文 | `tests/MantisZip.UI.Avalonia.Tests/MainWindowViewModelCommentTests.cs:51-58` | 该用例在任意机器（含 CI）通过，且证明「culture 不再是变量」 | ✅ |

### 实施记录（2026-10-20，Phase 1）

- **实测翻车点（务必保留在代码注释里）**：GB18030 的**默认**解码器把非法/截断序列静默替换成 `?`（**不是** U+FFFD）。首版实现用 `gb18030Text.Contains('\uFFFD')` 判断失效——`café` 的 cp1252 字节 `63 61 66 E9` 被解成 `caf?`，不会退回 ANSI。最终用 `Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)` 严格解码 + `catch (DecoderFallbackException)` 判定。
- **缺陷 2 根因补充**：`Encoding.UTF8`（静态实例）使用替换回退、**永不抛** `DecoderFallbackException`，所以旧代码的 `catch` 是死代码；`Encoding.Default` 在 .NET Core 恒为 UTF-8（不是系统 ANSI），兜底同样无效。
- **验证（按 CI 原样三步、仓库根、整个 sln）**：`dotnet restore` → `dotnet build --no-restore -c Release` → `dotnet test --no-restore -c Release --filter "FullyQualifiedName!~SevenZipEngine"`，三步 exit 0；Core **717 通过 / 1 跳过 / 718 总计**，Avalonia **306 通过 / 3 跳过 / 309 总计**，0 失败。

T6 是**闭环判据**：commit `9ac51c0` 里的 `CultureInfo.CurrentCulture = zh-CN` 是临时脚手架，生产侧修好后必须删掉，否则等于用测试侧 fix 永久掩盖产品缺陷。

---

## 7. 测试计划

**Core（`tests/MantisZip.Tests`）— 已实现 7 条**
1. ✅ `DecodeComment_GbkBytes_ReturnsChinese`（16 字节短注释，Ude 必然判不出的场景）
2. ✅ `DecodeComment_GbkBytes_UnderEnUsCulture_ReturnsChinese`（显式 `CurrentCulture = en-US` + `finally` 恢复）——锁定「不依赖宿主 locale」
3. ✅ `DecodeComment_Utf8WithBom_ReturnsTextWithoutBom`
4. ✅ `DecodeComment_Utf8WithoutBom_ReturnsSame`
5. ✅ `DecodeComment_Gb18030FourByteSequence_Decodes`（GBK 超集覆盖：`中文€𝄞`）
6. ✅ `DecodeComment_Cp1252Bytes_UnderEnUsCulture_StillDecodesViaAnsi`（护栏：西文不被误判为 GB18030）
7. ✅ `RewriteAsync_PreservesGbkCommentBytes_ByteExact`（字节级相等，覆盖 T3/T4）

**Avalonia（`tests/MantisZip.UI.Avalonia.Tests`）**
8. ✅ `LoadArchive_WithGbkComment_DecodesCorrectly`：culture pin 由 `zh-CN` 反转成 **`en-US`**，经 `LoadArchiveAsync` 断言 `Preview.TextContent` 含 `欢迎使用本压缩包`（T6）

**人工验证（需 en-US 系统 locale 或改注册表 `ACP=1252` 的虚拟机）— 未执行**
- 打开 GBK 注释 ZIP → 预览面板显示中文
- 文本预览「系统 ANSI」对 GBK 文本的行为与设置一致（Phase 2）
- 删除包内一个条目（copy-mode）→ 用十六进制对比注释字节未变（自动化已覆盖字节级断言，人工为确认 UI 路径）

---

## 8. 风险与边界

- **误判风险**：A1 只作用于 ZIP 注释（自由文本、现状已坏），且仅在严格 UTF-8 失败后生效，收益/风险比清晰；A2 不引入猜测，只提供显式开关。
- **性能**：注释 ≤ 64 KB，解码 + 一次 UTF-8 严格校验可忽略。
- **兼容性**：`DecodeText` 公开签名不变；显式编码路径不变；写入侧仍恒 UTF-8（不回退到 GBK 写，避免产生新的不可读注释）。
- **禁止**：不要为了让测试变绿而把 `DecodeComment` 的 GBK 优先写死在 `DecodeText` 全局路径（会波及文本预览与未来调用方）。

### 关联缺口（另立项，本计划不做）

1. `ZipEngine.ResolveFileNameEncoding` 的 `"default" => Encoding.Default` = UTF-8，而用户选「默认」通常期望系统 ANSI → 中文文件名压缩行为与预期不符（`ZipEngine.cs:119 / 1860 / 2909`）。
2. `TextEncodingDetector.DetectAndReadText` 无任何调用点（疑似死代码），建议单独清理。
3. `ZipBinaryRewriter.cs:171` 的 `Encoding.Default` 残留（T3 落地后该分支整体删除，无需单独修）。

---

## 9. 完成标准（DoD）

**Phase 1（已达成）**
- [x] T1-T4 落地，Core 测试全绿（实增 7 条用例，超出 ≥6 的要求）
- [x] T6 完成：`MainWindowViewModelCommentTests` 的 pin 反转为 `en-US`，并在该 culture 下通过
- [x] `dotnet build src\MantisZip.UI.Avalonia`（Release）+ Core 测试 + Avalonia 测试全绿
- [x] 规则 12 验证（`mantiszip_verify` 工具在本会话不可用 → 已回退 `dotnet build` / `dotnet test`，判定标准一致）
- [x] 规则 3 进度文档：`docs/progress-avalonia-detail.md` 记录「注释编码回退修正 + copy-mode 注释字节透传」
- [ ] 人工验证（en-US 真机/VM）—— 自动化已覆盖同一路径，真机验证待用户环境

**Phase 2（T5，待决策）**
- [ ] `AppSettings.FallbackCodePage` + `TextEncodingDetector.Initialize()` + 设置 UI + 三语键
- [ ] 决策后：若做完则整份计划移入 `.omo/plans/已完成/` 并在 `docs/PLAN.md` 标记完成；若不做则把 T5 拆为独立计划、本计划收尾归档
