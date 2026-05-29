# 压缩/解压配置预设

> 将压缩和解压的全部设置保存为命名预设，支持加载、覆盖编辑、右键菜单一键使用。
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜⬜] (0/9)
> **部分依赖**: Phase 2 依赖 COM 右键菜单（`.sisyphus\plans\com-context-menu.md`）

---

## 前置条件

- Phase 2 需要 **COM 右键菜单** 完成（替换现有 `ShellIntegration.cs` 静态注册表方案）
- 文件过滤功能（`file-filter-feature.md`）完成可提供 `FileFilterCriteria` 作为可选的预设内部件

---

## Context

### 设计决策（用户确认）

| 项目 | 决策 |
|------|------|
| 管理界面 | 压缩：`CompressSettingsWindow`；解压：`ExtractSettingsWindow` |
| 编辑方式 | 加载预设 → 修改 → 保存同名 = 覆盖（无单独编辑按钮） |
| 密码 | **不保存** |
| 菜单命名 | 使用预设名称 |
| 菜单注册 | 保存时选择"是否显示在右键菜单" |
| Shell 基底 | Phase 2 依赖 COM 右键菜单（动态枚举，无需静态注册） |

### 阶段规划

```
Phase 1（无依赖，可立即实施）
├── 数据模型（CompressPreset / ExtractPreset）
├── CompressSettingsWindow 预设管理
├── ExtractSettingsWindow 预设管理
├── AppSettings 持久化
└── 预设加载/覆盖逻辑

Phase 2（依赖 COM 右键菜单）
├── COM 菜单注册（标记为"显示在菜单"的预设自动出现）
├── CLI 入口（--compress-with-preset / --extract-with-preset）
└── 预设管理设置页（可选，管理所有预设的菜单可见性）
```

### CompressPreset 数据模型

| 字段 | 类型 | 说明 |
|------|------|------|
| Name | string | 预设名称（也用作菜单名） |
| Format | string | zip / 7z / tar.gz |
| CompressionLevel | int | 0-9 |
| SplitSize | long | 分卷大小（字节），0=不分卷 |
| OutputMode | string | manual / same-dir / separate / combined |
| OutputPath | string? | 手动模式下的输出路径 |
| Comment | string? | 注释文本 |
| CommentDistribution | string? | AllSame / FirstOnly / PerLine |
| Filter | FileFilterCriteria? | 可选的文件过滤条件 |
| ShowInMenu | bool | 是否显示在右键菜单 |

### ExtractPreset 数据模型

| 字段 | 类型 | 说明 |
|------|------|------|
| Name | string | 预设名称（也用作菜单名） |
| DestinationMode | string | same-dir / desktop / last / choose |
| ConflictAction | string | ask / overwrite / rename / skip |
| OpenFolderAfterExtract | bool | 解压后打开文件夹 |
| Filter | FileFilterCriteria? | 可选的文件过滤条件 |
| ShowInMenu | bool | 是否显示在右键菜单 |

---

## Work Objectives

### Core Objective
支持用户将压缩和解压的全部设置保存为命名预设，支持加载、覆盖编辑，并可选地显示在右键菜单中一键使用。

### Concrete Deliverables
- Core: `CompressPreset` 数据模型
- Core: `ExtractPreset` 数据模型
- UI: CompressSettingsWindow 预设管理功能（保存/加载/覆盖/删除）
- UI: ExtractSettingsWindow 预设管理功能（保存/加载/覆盖/删除）
- UI: AppSettings 预设持久化
- CLI: `--compress-with-preset` / `--extract-with-preset`（Phase 2）
- Shell: 预设项出现在右键菜单（Phase 2，依赖 COM 菜单）

### Must Have (Phase 1)
- [ ] CompressPreset 可保存到 AppSettings 并加载
- [ ] ExtractPreset 可保存到 AppSettings 并加载
- [ ] CompressSettingsWindow 可加载预设并填充所有设置项
- [ ] ExtractSettingsWindow 可加载预设并填充所有设置项
- [ ] 同名保存 = 覆盖编辑
- [ ] 预设列表上限 50 个（防止膨胀）
- [ ] 密码**不**保存到预设

### Must Have (Phase 2)
- [ ] CLI `--compress-with-preset "名称" <paths...>` 可用
- [ ] CLI `--extract-with-preset "名称" <archive>` 可用
- [ ] 标记 ShowInMenu 的预设出现在右键菜单
- [ ] 删除预设后菜单项自动消失

### Must NOT Have
- 预设内不包含密码
- 不直接依赖文件过滤功能（Filter 字段在过滤功能完成后才可用，为 optional）
- Phase 1 不修改 ShellIntegration.cs（等待 COM 菜单）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: NO
- **Automated tests**: NO
- **Agent-Executed QA**: ALWAYS

---

## Execution Strategy

### Parallel Execution Waves

```
Phase 1 (Core data models + UI integration):
├── Task 1: CompressPreset data model
├── Task 2: ExtractPreset data model
├── Task 3: AppSettings persistence + preset list management
├── Task 4: CompressSettingsWindow preset save/load UI
├── Task 5: ExtractSettingsWindow preset save/load UI
├── Task 6: Localization strings
├── F1-F4: Phase 1 verification

Phase 2 (Shell + CLI — after COM menu):
├── Task 7: CLI --compress-with-preset entry point
├── Task 8: CLI --extract-with-preset entry point
├── Task 9: COM context menu integration for presets
├── F1-F4: Phase 2 verification
```

### Critical Path
Task 1 → Task 3 → Task 4 → F1-F4 (Phase 1)
COM Menu → Task 7 → Task 9 → F1-F4 (Phase 2)

---

## TODOs

### Phase 1

- [ ] 1. `CompressPreset` 数据模型

  **What to do**:
  - 新建 `src/MantisZip.Core/Presets/CompressPreset.cs`
  - 字段（见上方设计表）：
    - `string Name`
    - `string Format` — "zip" / "7z" / "tar.gz"
    - `int CompressionLevel` — 0-9
    - `long SplitSize` — 0 = 不分卷
    - `string OutputMode` — "manual" / "same-dir" / "separate" / "combined"
    - `string? OutputPath`
    - `string? Comment`
    - `string? CommentDistribution` — "AllSame" / "FirstOnly" / "PerLine"
    - `FileFilterCriteria? Filter` — 可选，可 null
    - `bool ShowInMenu` — 默认 false
  - `CompressSettingsWindow.LoadPreset(CompressPreset preset)` — 将预设填充到 UI 控件
  - `CompressSettingsWindow.BuildPreset(string name) — 从 UI 读取当前设置构建预设

  **Must NOT do**:
  - 不要包含密码字段
  - Filter 字段先用 `null` 默认值（文件过滤功能完成后才填充）

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] All fields serialize/deserialize correctly
  - [ ] Filter field nullable (default null)
  - [ ] ShowInMenu defaults to false

  **Commit**: YES
  - Message: `feat(core): add CompressPreset data model`

---

- [ ] 2. `ExtractPreset` 数据模型

  **What to do**:
  - 新建 `src/MantisZip.Core/Presets/ExtractPreset.cs`
  - 字段：
    - `string Name`
    - `string DestinationMode` — "same-dir" / "desktop" / "last" / "choose"
    - `string ConflictAction` — "ask" / "overwrite" / "rename" / "skip"
    - `bool OpenFolderAfterExtract`
    - `FileFilterCriteria? Filter` — 可选
    - `bool ShowInMenu` — 默认 false
  - `ExtractSettingsWindow.LoadPreset(ExtractPreset preset)` — 填充到 UI
  - `ExtractSettingsWindow.BuildPreset(string name)` — 从 UI 读取构建

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: None (can parallel with Task 1)

  **Acceptance Criteria**:
  - [ ] All fields serialize/deserialize correctly
  - [ ] Filter nullable

  **Commit**: YES (groups with 1)
  - Message: `feat(core): add ExtractPreset data model`

---

- [ ] 3. AppSettings 持久化 + 预设管理

  **What to do**:
  - 修改 `AppSettings.cs`：
    - 新增 `List<CompressPreset> CompressPresets { get; set; }`，默认空
    - 新增 `List<ExtractPreset> ExtractPresets { get; set; }`，默认空
  - `SaveSettings()` / `LoadSettings()` 中序列化/反序列化
  - 辅助方法：
    - `FindCompressPreset(string name)` — 按名称查找
    - `SaveCompressPreset(CompressPreset preset)` — 同名覆盖/新增
    - `DeleteCompressPreset(string name)`
    - 同样提供 Extract 版本
  - 预设上限 50 个（每种），防止 settings.json 膨胀

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: Task 1, Task 2

  **Acceptance Criteria**:
  - [ ] Presets survive save/load round-trip
  - [ ] Max 50 enforced
  - [ ] Overwrite works (same name replaces)

  **Commit**: YES (groups with 1)
  - Message: `feat(ui): add preset persistence to AppSettings`
  - Files: `src/MantisZip.UI/AppSettings.cs`

---

- [ ] 4. CompressSettingsWindow 预设管理

  **What to do**:
  - 在 CompressSettingsWindow 中添加预设管理 UI：
    - **预设栏**：预设下拉框（显示当前已保存的压缩预设列表）
    - **加载按钮**：选中预设 → 点击加载 → 填充所有设置项到 UI
    - **保存为预设按钮**：弹出命名对话框 → 保存当前设置 → 同名覆盖
    - **另存为按钮**：总是新建（不同于保存的覆盖行为）
    - **删除按钮**：确认后删除（不可删除当前正在使用的预设）
    - **"显示在右键菜单"复选框**：保存时可勾选
  - 加载预设时还原所有设置项：格式、级别、分卷、输出方式、路径、注释
  - 保存预设时从 UI 读取所有设置项
  - 不要在预设中保存密码（加密选项可以提示"密码不随预设保存"）

  **Must NOT do**:
  - 不要存储密码

  **References**:
  - Core/Presets/CompressPreset.cs (Task 1)
  - UI/AppSettings.cs (Task 3)

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: Task 1, Task 3

  **Acceptance Criteria**:
  - [ ] Save preset captures all settings (except password)
  - [ ] Load preset restores all settings
  - [ ] Overwrite works (save same name → update existing)
  - [ ] Delete removes from list
  - [ ] ShowInMenu checkbox available on save

  **Commit**: YES
  - Message: `feat(ui): add compress preset management to CompressSettingsWindow`
  - Files:
    - `src/MantisZip.UI/CompressSettingsWindow.xaml`
    - `src/MantisZip.UI/CompressSettingsWindow.xaml.cs`

---

- [ ] 5. ExtractSettingsWindow 预设管理

  **What to do**:
  - 与 Task 4 类似的预设管理 UI 添加到 ExtractSettingsWindow：
    - 预设下拉框 + 加载/保存/删除/另存为按钮
    - "显示在右键菜单"复选框
  - 加载预设时恢复：目标路径模式、冲突处理、打开文件夹选项
  - 保存预设时从 UI 读取

  **References**:
  - Core/Presets/ExtractPreset.cs (Task 2)
  - UI/AppSettings.cs (Task 3)
  - UI/ExtractSettingsWindow.xaml (已有)

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: Task 2, Task 3

  **Acceptance Criteria**:
  - [ ] Save/Load/Delete/Overwrite all work
  - [ ] ShowInMenu checkbox available

  **Commit**: YES
  - Message: `feat(ui): add extract preset management to ExtractSettingsWindow`
  - Files:
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml`
    - `src/MantisZip.UI/ExtractSettingsWindow.xaml.cs`

---

- [ ] 6. 本地化字符串

  **What to do**:
  - 添加 key：
    - `Preset_Save`, `Preset_Load`, `Preset_Delete`, `Preset_SaveAs`
    - `Preset_NamePrompt`, `Preset_NameRequired`, `Preset_OverwriteConfirm`
    - `Preset_DeleteConfirm`, `Preset_ShowInMenu`
    - `Preset_PasswordNotSaved`（提示密码不保存）
    - `Preset_NoPresets`（下拉为空时显示）
  - zh + en 翻译

  **Parallelization**:
  - Wave: Phase 1
  - Blocked By: None

  **Commit**: YES
  - Message: `feat(i18n): add preset management localization strings`

---

### Phase 2（依赖 COM 右键菜单完成后实施）

- [ ] 7. CLI — `--compress-with-preset`

  **What to do**:
  - 新增 CLI 入口 `--compress-with-preset "预设名" <路径...>`
  - 在 `App.OnStartup` 中处理
  - 根据预设名从 AppSettings 加载 CompressPreset
  - 应用预设中的所有设置：格式、级别、分卷、输出方式、路径
  - 如果预设包含 Filter 且文件过滤功能已就绪，调用 FileFilterHelper
  - 调用对应的压缩方法

  **References**:
  - UI/App.Cli.cs — 现有 CLI 入口
  - UI/App.xaml.cs — CLI 路由

  **Parallelization**:
  - Wave: Phase 2
  - Blocked By: COM 右键菜单完成

  **Acceptance Criteria**:
  - [ ] --compress-with-preset "name" <files> works
  - [ ] Unknown preset shows error message
  - [ ] All preset settings applied correctly

  **Commit**: YES
  - Message: `feat(cli): add --compress-with-preset`

---

- [ ] 8. CLI — `--extract-with-preset`

  **What to do**:
  - 新增 CLI 入口 `--extract-with-preset "预设名" <archive>`
  - 从 AppSettings 加载 ExtractPreset
  - 应用设置并执行提取

  **Parallelization**:
  - Wave: Phase 2
  - Blocked By: COM 右键菜单完成

  **Commit**: YES (groups with 7)
  - Message: `feat(cli): add --extract-with-preset`

---

- [ ] 9. COM 右键菜单集成

  **What to do**:
  - 在 COM 菜单处理程序中（`IContextMenu.QueryContextMenu`）枚举所有标记 ShowInMenu 的预设
  - 压缩预设：注册到 `*`（文件）和 `Directory`（文件夹）
  - 解压预设：注册到 `*`（文件，仅压缩包扩展名）
  - 菜单项文本 = 预设名称
  - 点击后调用对应的 CLI：`--compress-with-preset "名称"` / `--extract-with-preset "名称"`
  - ShowInMenu 变化时 COM 菜单自动反映（动态枚举无需重新注册）

  **References**:
  - COM 右键菜单计划（待实施）
  - UI/AppSettings.cs — 预设列表
  - Core/Presets/CompressPreset.cs, ExtractPreset.cs

  **Parallelization**:
  - Wave: Phase 2
  - Blocked By: COM 右键菜单完成

  **Acceptance Criteria**:
  - [ ] Presets with ShowInMenu=true appear in context menu
  - [ ] Presets with ShowInMenu=false do not appear
  - [ ] Clicking preset menu item triggers correct CLI command
  - [ ] Adding/removing preset reflects immediately

  **Commit**: YES
  - Message: `feat(shell): integrate presets with COM context menu`

---

## Final Verification Wave

- [ ] F1. Plan Compliance Audit — `oracle`
- [ ] F2. Code Quality Review — `unspecified-high`
- [ ] F3. Real Manual QA — `unspecified-high`
- [ ] F4. Scope Fidelity Check — `deep`

---

## Commit Strategy

- **1-3**: `feat(core): add CompressPreset, ExtractPreset, and persistence`
- **4**: `feat(ui): add compress preset management to CompressSettingsWindow`
- **5**: `feat(ui): add extract preset management to ExtractSettingsWindow`
- **6**: `feat(i18n): add preset localization strings`
- **7-9**: `feat(shell): add CLI + COM menu integration for presets`

---

## Success Criteria

### Final Checklist
- [ ] CompressPreset save/load/overwrite/delete all work
- [ ] ExtractPreset save/load/overwrite/delete all work
- [ ] Passwords never stored in presets
- [ ] Presets with ShowInMenu appear in right-click menu (Phase 2)
- [ ] --compress-with-preset / --extract-with-preset CLI work (Phase 2)
