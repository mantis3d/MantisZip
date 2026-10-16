# 压缩加速三开关 — UI 收敛（Phase 1）+ 魔数检测/自适应规则实现（Phase 2）

> **状态**: 📋 待实施 | **日期**: 2026-10-16 | **优先级**: P1 | **预估工时**: Phase 1 = 4-6h / Phase 2 = 3-4h
> **来源**: 用户 2026-10-16 提出「压缩有个选项有两个开关『多线程』和『自适应』，测试发现不启用自适应时开启多线程反而有反作用」→ 实测 + 代码审计后扩展为两阶段计划
> **原型**: [docs/prototypes/compress-format-options-panel.html](../../docs/prototypes/compress-format-options-panel.html)（交互原型，已覆盖两处界面）

## 背景

压缩加速面板有三个开关（`自适应压缩` / `魔数检测` / `多线程压缩`），压缩对话框 ZIP 面板另有一个 `并行压缩组数`。2026-10-16 实测 + 全仓代码审计后发现四个问题：

| # | 问题 | 证据 |
|---|------|------|
| 1 | **「并行压缩组数」是性能陷阱** | 16 核机器把组数设为 2，压缩只用 **1.95 个核**（进程 CPU/墙钟实测），耗时 **5.47s vs 设 16 组的 1.64s（3.3x）**。根因：N 组路径每组强制 `mt=off`（`ZipEngine.cs:4103`），**并行度 = 组数**（实测 `avgCores ≈ degree`）。且该旋钮对速度**无正收益**（deg=1/2/4/8/16 互有胜负，平台区 2–8） |
| 2 | **「多线程压缩」在自适应关闭时是负收益** | 可压缩数据（text 语料）上「仅多线程」比**完全不开多线程**慢 **1.5–1.8x**。根因：MT 路径改用 7z 的 Deflate（压缩率 +15.2% 但更慢），没有自适应先剔除已压缩文件时，慢的压缩器白白多跑 |
| 3 | **「魔数检测」是空转开关** | `AdaptiveSmartDetect` 全仓**无任何引擎消费者**：只有 UI 绑定 + `AppSettings` 持久化 + `CompressRequest → ArchiveOptions` 透传；`AdaptiveRuleMatcher.ResolveLevel(..., adaptiveSmartDetect:)` 形参**只写日志、不参与分支**。而 `ArchiveEngine.cs:183` 注释写的是「自适应关闭时无效」——语义上该联动，UI 也漏了 |
| 4 | **「自适应规则」也是空转** | `AdaptiveRuleMatcher.ResolveLevel`（唯一会应用规则的函数）**无生产调用点**（仅 `AdaptiveCompressionTests` 引用）；`CompressRequest` / `ArchiveOptions` **根本没有规则字段**。即用户配了规则不生效 |
| 5 | 自定义格式的「魔数 hex」也不生效 | `ZipEntryClassifier.GetAdaptiveLevel` 只读 `format.Extensions`，**从不读 `MagicHex`** |

**Phase 1 目标**：先出一版「UI 不说谎」的可用版本 —— 删除/隐藏空转控件，修掉 #1 #2 两个真实性能坑。
**Phase 2 目标**：实现 #3 #4（魔数检测 + 自适应规则），并把 Phase 1 隐藏的 UI 接回来。

## 决策记录（2026-10-16，用户授权「按你觉得合适的来」）

| 决策 | 取值 | 理由 |
|------|------|------|
| 改动 A（删「并行压缩组数」）与改动 B（多线程依赖自适应）是否进 Phase 1 | **都进** | 两者都已单独拍板；且都是真实缺陷修复 |
| 引擎侧方案 C（`mt = 核数/组数`）是否进 Phase 1 | **进，与改动 A 绑定** | 删掉旋钮后引擎默认组数要定；若不做 C，固定成 8 组反而比现在的 16 组慢（8 组×`mt=off` 只用 8 核：2.023s vs 1.643s）。C 已验证可行（`mt=N` 被 7z 接受，端到端 avgCores 恒 ≈12），4 行改动 |
| 自定义格式「魔数 hex」（#5）是否一起隐藏 | **一起隐藏** | 与魔数检测是同一件事，留着同样是骗人 UI |
| 「隐藏」的实现方式 | **用显式 `false` 门控 + Phase 2 注释，不删标记** | Phase 2 = 改回来（1 行），避免重建标记；代价是留少量 dead markup（计划内已列「Phase 2 恢复清单」） |
| 保留不动的部分 | `AppSettings` 字段、`CompressRequest`/`ArchiveOptions` 透传链路、三语 key、`EditAdaptiveRuleDialog`、`AddCustomFormatDialog` | Phase 2 只需「UI 接回来 + 引擎实现」，链路不重做 |

## Phase 1：UI 收敛（4-6h）

### Task 1 — 删除「并行压缩组数」控件（UI 全删 + 设置字段）

| 文件 | 改动 |
|------|------|
| `Controls/DynamicFormatOptionsPanel.axaml` | 删 `ParallelDegreePanel`（标签 + `ParallelDegreeInput` NumericUpDown + 灰色提示） |
| `Controls/DynamicFormatOptionsPanel.axaml.cs` | 删 `ParallelCompressDegree` 属性、`ParallelDegreeLabel`/`ParallelDegreeHint`、`LoadDefaults` 里的播种、`ZipMultiThreadCheck_IsCheckedChanged` 与 `UpdateTotalSourceSize` 里的面板显隐 |
| `ViewModels/CompressSettingsViewModel.cs` | 删 `ParallelCompressDegree` 属性 + 钳制 handler + 构造播种 + 2 条 `LocalizedStrings` |
| `Dialogs/CompressSettingsWindow.axaml.cs` | `SnapshotFormatOptionsToViewModel` 删 `ParallelCompressDegree` 快照 |
| `Views/MainWindow.axaml.cs:249` | 删 `cvm.ParallelCompressDegree = dialog.ViewModel.ParallelCompressDegree` |
| `Services/CompressFlow.cs:99` | 删 `ParallelCompressDegree = vm.ParallelCompressDegree` |
| `Models/AppSettings.cs:104` | 删 `ParallelCompressDegree` 字段（旧 `settings.json` 里的值被忽略，无害） |
| 三语 `strings.*.json` | 删 `Compress_ParallelDegree` / `Compress_ParallelDegree_Hint`（三语同步，否则 `AllThreeLanguages_HaveSameKeySet` 失败） |

> **保留** `ArchiveOptions.ParallelCompressDegree`（`ArchiveEngine.cs:168`）—— 引擎层输入，`ParallelCompressTests` / `scripts/bench-zip-mt.cs` 仍在使用，**显式值仍照旧生效**（测试用的 1/3/4 不受影响）。

### Task 2 — 引擎侧方案 C：并行度与组数解耦（与 Task 1 绑定）

| 文件 | 改动 |
|------|------|
| `Core/Engines/ZipEngine.cs:4103` | `compr.CustomParameters["mt"] = batchIndex.HasValue ? Math.Max(1, Environment.ProcessorCount / Math.Max(1, batchCount ?? 1)).ToString() : "on";` |
| `Core/Engines/ZipEngine.cs:4346` `ResolveParallelCompressDegree` | 默认规则改为 `clamp(ProcessorCount, 2, 8)`；**显式正数仍照旧生效**（保 `degree=1` 回退语义） |
| `Core/Services/CompressService.cs:103` | `CompressRequest.ParallelCompressDegree` 默认 `ProcessorCount` → `0`（触发新规则） |

**依据**：`mt=N` 经 `CustomParameters["mt"]` 被 7z 接受（`-mmt=N`），实测 avgCores ≈ N；`G 组 × mt=核数/G` 时 **avgCores 恒 ≈12，与 G 无关**（G=2 从 5.47s → 1.32s）。默认组数取 8 落在实测平台区（G=2–8 差异 ≤3%，G=16 略差，G=1 因串行镜像慢 5%）。

### Task 3 — 改动 B：多线程压缩依赖自适应压缩（两处 UI）

- **设置窗口**（`Views/SettingsWindow.axaml:162` 多线程压缩 CheckBox）：`IsEnabled` 绑定新属性 `IsMultiThreadedEnabled => AdaptiveCompression`；自适应关闭时**自动取消勾选**并**记住原状态**，重新开启时还原。
- **压缩对话框 ZIP 面板**（`DynamicFormatOptionsPanel.axaml:77` `ZipMultiThreadCheck`）：同上（自适应关闭 → 取消勾选 + `IsEnabled=false`）。
- 两处下方各显示一行灰色说明（新增 **1 个三语 key**，两处共用）。
- 新增 ViewModel 成员：`SettingsWindowViewModel` / `CompressSettingsViewModel` 各加「用户意图 vs 生效值」两态 + `OnAdaptiveCompressionChanged` 联动（照抄既有 `UpdateTotalSourceSize` 的 `_mtCheckedBeforeLimit` 记住/还原模式）。
- `SettingsWindowViewModel` 连带简化：`IsStoreFormatSectionVisible`（原 `adaptive && MT`）在 MT 依赖自适应后等价于 `MT`；`IsUserRulesSectionVisible` 由 Task 5 处置。

### Task 4 — 隐藏「魔数检测」（两处）

| 位置 | 改动 |
|------|------|
| `ViewModels/SettingsWindowViewModel.cs:189` | `IsSmartDetectVisible => AdaptiveCompression;` → `=> false;  // Phase 2：实现魔数检测后改回` |
| `Controls/DynamicFormatOptionsPanel.axaml:71` | `SmartDetectCheck` 加 `IsVisible="False"` + 注释 |
| `Controls/DynamicFormatOptionsPanel.axaml.cs:220` | `AdaptiveSmartDetect` getter 恒返回 `false`（控件虽在但已隐藏，避免值继续透传造成「以为生效」） |

> 保留 `AppSettings.AdaptiveSmartDetect`、`CompressRequest`/`ArchiveOptions.AdaptiveSmartDetect`、三语 `Settings_AdaptiveSmartDetect*` / `FormatOptions_ZIP_SmartDetect`。

### Task 5 — 隐藏「自适应规则」（设置窗口卡片④）

| 文件 | 改动 |
|------|------|
| `ViewModels/SettingsWindowViewModel.cs:198` | `IsUserRulesSectionVisible => AdaptiveCompression && !MultiThreadedCompression;` → `=> false;  // Phase 2` |

> 保留 `AppSettings.AdaptiveOverrides`、`SettingsWindowViewModel.AdaptiveOverrides` 集合与 `AddRule`/`EditRule`/`DeleteRule` 命令、`Dialogs/EditAdaptiveRuleDialog.*`（Phase 2 复用）。卡片隐藏后这些成员暂不可达，属预期。

### Task 6 — 隐藏自定义格式「魔数 hex」

| 文件 | 改动 |
|------|------|
| `Dialogs/AddCustomFormatDialog.axaml:25` | `MagicHexTextBox` 所在行加 `IsVisible="False"` + 注释 |
| `ViewModels/SettingsWindowViewModel.cs:2187` | `MagicHexDisplay` 恒返回 `""`（列表里不再显示 `[FFD8FFE0]`） |

> 保留 `FormatDefinition.MagicHex` 字段与 `AddCustomFormatDialog.MagicHex` 属性（Phase 2 复用）。

### Task 7 — 文案与文档同步

- 新增三语 key（改动 B 的说明行），插入三语文件头并保持 key 集同步（规则 13）。
- `AGENTS.md` 的「加速」段落：改为「三开关 + 格式目录；**魔数检测 / 自适应规则 UI 已隐藏（Phase 2 实现）**」；删除「并行压缩组数」相关描述（含 `DynamicFormatOptionsPanel` 的 7z 面板说明）。
- `docs/PLAN.md` 同步本计划行（已加）。
- `docs/progress-avalonia-detail.md` 记录本次变更（规则 3）。

### Task 8 — 验证

- `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj`（规则 12）
- `dotnet build src\MantisZip.Core\MantisZip.Core.csproj`
- `dotnet test tests\MantisZip.Tests\MantisZip.Tests.csproj`（Task 2 的引擎改动必须全绿）
- `dotnet test tests\MantisZip.UI.Avalonia.Tests\MantisZip.UI.Avalonia.Tests.csproj`（**注意规则 17**：XAML 改动需 `--no-incremental` 全量重建，增量构建会掩盖 `AVLN3000`）
- 同步更新引用了被删成员的测试（`CompressSettingsViewModelTests` 等）

**Phase 1 验收清单**

- [ ] 压缩对话框 ZIP 面板不再出现「并行压缩组数」；勾选多线程时也不再出现
- [ ] 组数语义消失后，MT 压缩速度不低于改前（16 核：8 组 × `mt=2` ≈ 改前 16 组 × `mt=off`）
- [ ] 自适应关闭 → 多线程自动取消勾选 + 置灰（两处 UI），重新开启自适应后恢复原勾选
- [ ] 设置窗口「加速」页与压缩对话框 ZIP 面板都不再出现「魔数检测」
- [ ] 设置窗口「加速」页不再出现「自适应规则」卡片
- [ ] 自定义格式对话框不再出现「魔数 hex」输入框；格式目录列表不再显示 `[hex]`
- [ ] `settings.json` 旧字段仍可加载（不抛异常、不丢其它设置）
- [ ] 全量构建 + 测试通过

## Phase 2：实现（3-4h，Phase 1 发布后启动）

### Task 9 — 实现魔数检测

- `Core/Utils/ZipEntryClassifier.cs`：新增 `FileFormat` → 「已压缩」集合映射；`GetAdaptiveLevel` 增 `smartDetect` 形参；文件 ≥ 阈值时读头部 4KB → `FileFormatDetector.Detect`（**已存在，953 行，35+ 魔数**）→ 命中「已压缩」集合则返回 `0`（Store）。
- 头部读取 helper：抽 `ArchiveEngineFactory.GetEngineByMagic`（`ArchiveEngine.cs:586`）的写法。
- 接线 4 个调用点：`ZipEngine.cs` 1899 / 2930 / 3260 / 3795（传 `options.AdaptiveSmartDetect`）。
- **待定项**（启动 Phase 2 时拍板）：
  1. 「已压缩」集合边界 —— `Pe`（exe/dll 其实可压）、`Sqlite`、`Svg`、`Tar`（未压缩归档）是否计入？
  2. 「大文件」阈值：常量（如 64KB）还是新设置项？
  3. 冲突方向：扩展名说可压 + 魔数说已压 → Store（无争议）；反过来（`.jpg` 实为文本）是否改回压缩？
- 收益提示：只对**无扩展名 / 扩展名骗人 / 改名残留**文件有增益；正常命名的照片视频已被扩展名规则 Store，不受影响。
- 测试：`.dat` 实为 JPEG → Store；阈值边界；扩展名/魔数冲突。

### Task 10 — 实现自适应规则

- 规则需要穿过链路才能生效：`CompressRequest` / `ArchiveOptions` 新增规则字段 → `ZipEngine` 分拣时调用 `AdaptiveRuleMatcher.ResolveLevel`（该函数已实现且**优先级语义已写好**：用户规则 > 内置分类）。
- 需设计：规则的 `FormatIds` → 扩展名/魔数匹配；与 `ZipEntryClassifier` 扩展名分类的先后关系；与多线程 Store 分组的交互。
- 测试：规则命中/未命中/禁用规则/多条规则优先级。

### Task 11 — 恢复 Phase 1 隐藏的 UI

「Phase 2 恢复清单」（Task 4/5/6 的 `false` 门控逐条改回，并接上真实实现）：

| 位置 | 恢复动作 |
|------|----------|
| `SettingsWindowViewModel.IsSmartDetectVisible` | `false` → `AdaptiveCompression` |
| `DynamicFormatOptionsPanel.axaml` `SmartDetectCheck` | 移除 `IsVisible="False"` |
| `DynamicFormatOptionsPanel.AdaptiveSmartDetect` getter | 恒 `false` → 读控件 |
| `SettingsWindowViewModel.IsUserRulesSectionVisible` | `false` → `AdaptiveCompression && !MultiThreadedCompression` |
| `AddCustomFormatDialog.axaml` `MagicHexTextBox` 行 | 移除 `IsVisible="False"` |
| `SettingsWindowViewModel.MagicHexDisplay` | `""` → `[hex]` 显示 |
| 设置窗口「加速」页 魔数检测 的「自适应关闭时禁用」联动（本次被隐藏取代的「改动 C」） | 重新加回 |

## 风险

| 风险 | 级别 | 缓解 |
|------|------|------|
| 删除 `AppSettings.ParallelCompressDegree` 后旧 `settings.json` 反序列化异常 | 🟢 低 | STJ 忽略未知字段；验收清单已含「旧 settings.json 可加载」 |
| XAML 隐藏门控（`IsVisible="False"`）被后续开发当成「无用代码」清掉 | 🟡 中 | 每处加 `// Phase 2：实现后移除` 注释 + 本计划「恢复清单」 |
| 规则 17 陷阱：增量构建掩盖 `AVLN3000` | 🟡 中 | Task 8 强制 `--no-incremental` |
| Phase 2 拖太久，隐藏的 UI 长期缺席 | 🟡 中 | Phase 2 仅 3-4h 且基础设施（`FileFormatDetector` / `AdaptiveRuleMatcher`）已存在 |
| 删除三语 key 造成三语 key 集不同步 | 🟢 低 | 三语同步删除；`AllThreeLanguages_HaveSameKeySet` 会兜住 |
