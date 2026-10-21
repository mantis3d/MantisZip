# 自适应压缩级别（已实现，归档参考）

> 按文件类型/扩展名自动选择压缩级别（媒体类走 Store、文本类走高压缩）。
> **状态**: ✅ 已实现并随 **v0.5.2** 发布（2026-09-17 / 09-18）
> **归档**: 2026-10-21（自 `compression-estimator.md` 拆出，保留设计结论与实测矩阵）
> **后续缺口**: 规则匹配未接线 → [adaptive-compress-ui-convergence.md](../进行中/adaptive-compress-ui-convergence.md) Phase 2

---

## 1. 已交付内容

| 能力 | 实现 |
|------|------|
| 级别模型 | `AdaptiveLevel`（含 `Auto`）、`FormatDefinition`、`FormatCatalog`（`:103/115/125`） |
| 用户覆盖规则 | `AdaptiveOverrideRule`（含 `CustomLevel`）+ `AppSettings.AdaptiveOverrides` |
| 开关 | `AppSettings.AdaptiveCompression`、`AdaptiveSmartDetect`（魔数检测） |
| 自定义格式 | `AppSettings.CustomFormats`（扩展名 → 分类/默认级别） |
| 逐条目分级 | `ZipEntryClassifier.GetAdaptiveLevel`（扩展名 + `MultiThreadedStoreFormatIds`），接线于 `ZipEngine.Compress.cs:202`、`ZipEngine.CompressGroup.cs:757`/`:1292`，门控 `AdaptiveCompression && !Encrypt` 且方法为 Deflate/Deflate64 |
| 比率/级别表 | `CompressionCoefficients`（`(category, level)` 键，ZIP 与 7z 两套） |
| 设置 UI | 压缩设置窗口「自适应压缩」区块（格式目录 / 用户规则 / 多线程提示） |

---

## 2. 引擎能力矩阵（2026-10-21 复核订正）

| 格式 | 逐条目级别 | 逐条目方法 | 多线程 | 备注 |
|------|:---------:|:---------:|:------:|------|
| ZIP | ✅ | ⚠️ 仅 Deflate/Deflate64 分支 | ✅ 分组并行 | `ZipWriterEntryOptions` 4 个构造点：`ZipEngine.CompressGroup.cs:717`（MT Store 组）/`:766`（串行）/`:1180`（AddToArchive）/`:1301`（`ReadFileWithRetry`，方法起 `:1265`） |
| 7z | ❌ | ❌ | ✅ `mt=on` | SharpSevenZip 整档级别；自适应只能作用于整档 |
| Tar | ❌ | ❌ | — | 纯打包无压缩，级别无意义 |
| GZip / Tar.gz | ❌ | ❌ | — | 单流单级别 |
| ZIP 删除条目 | ❌ | ❌ | — | `DeleteEntriesAsync`（`:847`）走 `ZipBinaryRewriter`，不经 `ZipWriterEntryOptions` |

**订正记录**（原计划表与实现不符之处）：
- 原文写「`DeleteEntriesAsync` 也要传 per-entry 级别」→ **错误**，该路径不构造 `ZipWriterEntryOptions`。
- 原文写「BZip2 / ZStandard 也可 Store」→ **未实现**：Store 分支仅覆盖 Deflate/Deflate64 的条目，其余方法按整档级别处理。
- 原文行号（`:2002` / `:1502` / `:1906`）来自 WPF 时代单文件 `ZipEngine.cs`，现已拆为 partial。

---

## 3. 三个实现陷阱（已踩过）

1. **ZIP 逐条目级别必须同一个 `ZipWriter`**：所有条目写进同一 writer，仅通过 `ZipWriterEntryOptions.CompressionLevel` 区分；多 writer 合并会丢失 per-entry 级别。
2. **7z 无法逐条目**：`SharpSevenZip` 的级别是压缩器级参数，UI 不能承诺「7z 按文件类型分级」。
3. **Store 只省时间不省体积**：媒体文件 Store 后大小 ≈ 原大小（仍有容器头），预估列必须用 `GetStoreRate()` = 1.0，不能用 0.15 之类的文本比率。

---

## 4. 安全配方（发布时验证过）

| 场景 | 配方 |
|------|------|
| 照片/视频归档 | ZIP 级别 5 + 自适应（`image_lossy` / `media` / `archive` → Store）；体积几乎不变，速度提升数倍 |
| 代码/文档归档 | ZIP 级别 9 或 7z 级别 5（整档） |
| 混合目录 | ZIP + 自适应；7z 因不支持逐条目，收益有限 |
| 加密压缩 | 自适应**自动关闭**（门控条件），因为加密后 Store 也会被加密，收益消失 |

---

## 5. 遗留缺口

| 缺口 | 影响 | 去向 |
|------|------|------|
| `AdaptiveRuleMatcher` 生产调用点为 0 | 用户在设置里配的「用户规则」不生效，只有扩展名启发式生效 | [adaptive-compress-ui-convergence.md](../进行中/adaptive-compress-ui-convergence.md) Phase 2 |
| `ComputeMajorityLevel` 按**文件数**投票（`:105-123`） | 1 万个小 txt + 1 个 10GB 视频 → 判为文本级别，视频被压 | Phase 2 改为按字节投票 |
| `AdaptiveSmartDetect` 魔数检测未接 | 扩展名错误的文件分类不准 | Phase 2（依赖 `FileFormatDetector`） |
| ZStandard / BZip2 的 Store 未实现 | 这两种方法下自适应无收益 | 视需求另开小计划 |
| Tar / GZip 无逐条目能力 | 自适应对这两种格式无效 | 设计约束，不修 |

---

## 6. 相关文档

- [adaptive-compress-ui-convergence.md](../进行中/adaptive-compress-ui-convergence.md) — 剩余接线工作
- [compression-estimator.md](../未开始/compression-estimator.md) — 预估复用本处比率表与分类逻辑
- [parallel-compress-plan-a.md](../未开始/parallel-compress-plan-a.md) / [-d.md](../未开始/parallel-compress-plan-d.md) — 多线程压缩（与本处自适应联动）
- `docs/RELEASE_NOTES.md` — v0.5.2 用户可见说明
