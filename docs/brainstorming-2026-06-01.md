# 功能脑暴 — 2026-06-01

> 基于 v0.3.7-refined（COM 右键菜单已完成）后的待定功能评估排序。

## 已完成的支柱功能

- SharpCompress 引擎统一 ✅
- COM 右键菜单（v0.3.7）✅
- ExtractSettingsWindow 重设计 ✅
- 批量进度列表 ✅
- 文件大小进度条 ✅

## 候选功能按投入产出比排序

### 1️⃣ 便携版模式 — 🟢 小投入，高回报

**计划文件**: `.sisyphus/plans/portable-mode.md`
**任务数**: 5 | **预估工时**: 1-2h

最简单、独立、无依赖，改动范围清晰：
- 哨兵文件检测（`Portable.txt`）
- `AppSettings` / `PasswordManager` 路径重定向
- 跳过 Shell 注册
- 预览临时目录重定向

**价值**: 一次性写好，U 盘用户直接受益，零维护成本。

### 2️⃣ 压缩配置预设 — 🟡 中投入，用户粘性提升

**计划文件**: `.sisyphus/plans/compress-preset.md`
**任务数**: 9（Phase 1 独立 5 个任务）

Phase 1（无 COM 依赖）可以立即做：
- 数据模型 `CompressPreset` / `ExtractPreset`
- `CompressSettingsWindow` 预设管理（保存/加载）
- `ExtractSettingsWindow` 预设管理
- `AppSettings` 持久化

**价值**: 常用配置场景（如"快速 ZIP 无密码"、"高压缩 7z 分卷"）一键切换，和 COM 右键菜单联动后更强（Phase 2）。

### 3️⃣ 文件过滤 — 🟡 中投入，差异化功能

**计划文件**: `.sisyphus/plans/file-filter-feature.md`
**任务数**: 11

按扩展名/大小/日期过滤后再压缩或解压。工作量较大（ExtractSettingsWindow 加 Tab、8 个内置预设、UI 控件联动）。

**注意**: 和压缩预设有功能重叠，预设可以包含过滤条件。

### 4️⃣ 压缩预估 — 🟡 中投入，解决核心痛点

**计划文件**: `.sisyphus/plans/compression-estimator.md`
**任务数**: 5

"选 ZIP 还是 7z？级别 5 还是 9？"——用户每次压缩都在盲选。四级精度策略设计，可先做前两级。

### 5️⃣ 魔数检测 — 🔴 大投入，但对体验提升大

**计划文件**: `.sisyphus/plans/preview-magic-detection.md`
**任务数**: 6

改名/无后缀的文件也能正确预览。改动链条长（Core 检测引擎 + UI 集成 + 回退逻辑）。

### 6️⃣ 重命名/移动条目 — 🟢 小投入

**计划文件**: `.sisyphus/plans/archive-rename-entry.md`
**任务数**: 6 | **预估工时**: 3-4h

纯增量功能，提取→删除→添加的模式已经很成熟。F2 重命名 + 右键菜单。

### 7️⃣ Explorer 路径快速选择 — 🟢 小投入

**计划文件**: `.sisyphus/plans/explorer-path-switcher.md`
**任务数**: 6

Ctrl+G 唤起资源管理器路径列表，压缩/解压时快速填路径。独立功能，不依赖其他模块。

## 推荐优先级

| 顺序 | 功能 | 理由 |
|------|------|------|
| 🥇 | 便携版模式 | 最简单、零依赖、一次性收益、不改 UI |
| 🥈 | 压缩配置预设 Phase 1 | 紧随 COM 菜单完成，日常使用频率高 |
| 🥉 | Explorer 路径快速选择 | 小而美，提升压缩解压操作流畅度 |
| 🏅 | 压缩预估 | 解决用户盲选痛点，中等投入 |
