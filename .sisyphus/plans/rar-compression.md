# RAR 压缩（外置 rar.exe / WinRAR.exe）

> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜] (0/8)
> **前置依赖**: 无

## TL;DR

> 利用用户已安装的 WinRAR 的 `rar.exe`（或 `WinRAR.exe a -afrar`）实现 RAR 格式压缩，补齐 RAR 只读解压的短板。包含 rar.exe 自动检测、进度解析、RAR 特有选项（固实/恢复记录/加密等）。
>
> **交付内容**: RarEngine.cs + 格式下拉 + RAR 特有 UI + 设置项
> **估算工时**: 6–8h | **难度**: 🟡中 | **并行**: 部分可并行

---

## Context

### 现状

- RAR 解压已支持（通过 SharpSevenZip / 7z.dll）
- RAR 压缩一直空缺（格式下拉只有 ZIP/7z/TAR.GZ）
- 用户装了 WinRAR 却无法用它压缩

### 用户决策

| 决策项 | 选择 |
|--------|------|
| rar.exe 来源 | 两者都支持：`rar.exe` 优先，找不到则尝试 `WinRAR.exe a -afrar` |
| 格式显示策略 | 始终显示 RAR，找不到 rar.exe 时置灰并提示 |
| 功能范围 | 完整版：固实、恢复记录、分卷、加密、注释均支持 |

### 技术要点

- `Process` + `ProcessStartInfo` 启动外部进程
- stdout 正则 `(\d+)%` 解析进度上报
- Command-line 构建要转义路径（路径含空格）
- `ArchiveFormat.Rar` 已存在枚举值
- `IArchiveEngine` 接口已完整

---

## Work Objectives

### Core Objective
在 MantisZip 中实现 RAR 格式压缩，使用用户已安装的 rar.exe/WinRAR.exe 作为后端。

### Concrete Deliverables
1. `Core/Engines/RarEngine.cs` — RAR 压缩引擎
2. `Core/Utils/RarDetector.cs` — rar.exe 路径检测工具
3. `ArchiveOptions` 扩展 — RAR 特有选项
4. `AppSettings` 扩展 — RarExePath 设置
5. `CompressSettingsWindow` — 格式下拉增加 RAR + RAR 特有面板
6. `SettingsWindow` 扩展 — 高级标签页增加 RarExePath 配置

### Must Have
- `rar a` 压缩命令构建正确，能生成有效 .rar 文件
- 压缩进度实时上报到 ProgressWindow
- 密码加密、分卷、压缩级别正常工作
- rar.exe 找不到时 UI 给出明确提示

### Must NOT Have
- 不替换现有 SharpSevenZip 的 RAR 解压能力（解压继续走 7z.dll）
- 不实现 RAR 压缩包添加/删除条目（`CanAdd = false`, `CanDelete = false`）
- 不支持 RAR 压缩中的多线程核心数等高级调优参数

---

## Verification Strategy

### QA Policy
每个任务通过 agent-executed 场景验证：
- **引擎功能**: 构建 rar.exe 命令 → 压缩测试文件 → 用 SharpSevenZip/7z.dll 解压验证内容 → 对比哈希
- **UI 集成**: CompressSettingsWindow 打开 → 选 RAR → 设密码/级别 → 压缩 → ProgressWindow 显示进度
- **检测逻辑**: 故意不装 / 错误路径 / 正常路径 三种情况验证
- **证据**: 压缩产物 .rar 文件、控制台输出、截图

---

## Execution Strategy

```
Wave 1 (基础 + 检测):
├── 1. RarDetector.cs (核心检测逻辑)
├── 2. ArchiveOptions RAR 扩展 + AppSettings
├── 3. RAR 相关 i18n 字符串

Wave 2 (引擎实现):
├── 4. RarEngine.cs — CompressAsync 核心
├── 5. RarEngine.cs — TestArchiveAsync + 其他接口

Wave 3 (UI 集成):
├── 6. CompressSettingsWindow RAR 格式 + 选项面板
├── 7. SettingsWindow RarExePath + ArchiveEngineFactory 注册

Wave 4 (验证):
└── 8. 端到端测试 + bug 修复
```

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  检查每个 Must Have 的覆盖情况，确认 RarDetector/RarEngine/UI 三项全部实现。检查每个 Must NOT Have 的约束（不解压替代、不实现 Add/Delete）。
- [ ] F2. **Code Quality Review** — `unspecified-high`
  代码质量检查：Process 资源释放（`using`/`finally`）、转义处理、退出码检查、异常路径、空引用保护。
- [ ] F3. **Real Manual QA** — `unspecified-high`
  按任务 8 的场景表逐个执行，验证端到端流程。截图保存证据。
- [ ] F4. **Scope Fidelity Check** — `deep`
  确认范围没有膨胀（没有加 UI 不承诺的 RAR 功能，没有改现有解压流程）。

---

## Commit Strategy

| Task | Message |
|------|---------|
| 1 | `feat(core): add RarDetector for rar.exe/WinRAR.exe path detection` |
| 2, 3 | `feat(core+ui): add RAR-specific options, RarExePath setting, and i18n strings` |
| 4, 5 | `feat(core): implement RarEngine (CompressAsync + remaining interfaces)` |
| 6, 7 | `feat(ui): add RAR format dropdown and settings UI, register engine` |
| 8 | `fix(rar): end-to-end fixes` |

---

## Success Criteria

### 核心验证
```bash
# 压缩一个包含文件和子目录的目录
# 产物能用 SharpSevenZip 解压
# 哈希一致
```

### 最终清单
- [ ] RarDetector 能找到已安装的 rar.exe
- [ ] 找不到 rar.exe 时尝试使用 WinRAR.exe
- [ ] CompressSettingsWindow 可选 RAR 格式
- [ ] RAR 不可用时置灰并提示
- [ ] RAR 特有选项（固实/恢复记录/压缩方式）正确传给 rar.exe
- [ ] 加密压缩正常
- [ ] 分卷压缩正常
- [ ] 进度实时上报
- [ ] 取消时进程被终止
- [ ] 解压/列表委托给现有 SevenZipEngine 工作正常
- [ ] 全部 i18n 字符串中英双语


- [ ] 1. RarDetector.cs — rar.exe/WinRAR.exe 路径检测

  **What to do**:
  - 在 `Core/Utils/RarDetector.cs` 创建静态检测类
  - 检测顺序：`AppSettings.RarExePath`（自定义路径）→ `PATH` 环境变量 → `%ProgramFiles%\WinRAR\rar.exe` → `%ProgramFiles(x86)%\WinRAR\rar.exe`
  - 找不到 `rar.exe` 时尝试同一目录下的 `WinRAR.exe`（通过 `WinRAR.exe a -afrar` 参数调用）
  - 结果缓存在静态字段，每次压缩时重新检测
  - 返回 `(string? exePath, bool useWinRarExe)` 元组
  - 提供 `bool IsAvailable()` 供 UI 查询
  - 单元测试：模拟各种路径场景

  **Must NOT do**:
  - 不要修改注册表来检测 WinRAR 安装路径
  - 不要自动下载或安装任何东西

  **Parallelization**:
  - **Can Run In Parallel**: YES (Wave 1)
  - **Parallel Group**: Wave 1 (with tasks 2, 3)
  - **Blocks**: Task 4, 6, 7
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] RarDetector.IsAvailable() 在有 WinRAR 的机器上返回 true
  - [ ] rar.exe 在 PATH 中时被正确找到
  - [ ] 自定义路径通过 AppSettings.RarExePath 生效
  - [ ] 两种都找不到时返回 false

  **Commit**: YES
  - Message: `feat(core): add RarDetector for rar.exe/WinRAR.exe path detection`
  - Files: `src/MantisZip.Core/Utils/RarDetector.cs`

- [ ] 2. ArchiveOptions RAR 扩展 + AppSettings RarExePath

  **What to do**:
  - 在 `ArchiveOptions` 中增加 RAR 特有属性：`Solid`（固实）、`RecoveryRecord`（恢复记录百分比）、`RarCompressionMethod`
  - 新建 `RarCompressionMethod` 枚举（`Store/Fast/Fastest/Normal/Good/Best`）
  - 在 `AppSettings.cs` 高级区块增加 `string? RarExePath`
  - 枚举映射：Store→-m1, Fastest→-m2, Fast→-m3, Normal→-m4, Good→-m5, Best→-m5

  **Parallelization**: YES (Wave 1, with tasks 1, 3)
  **Blocks**: Task 4, 6, 7

  **Commit**: YES (groups with 3)
  - Message: `feat(core): add RAR-specific options and RarExePath setting`

- [ ] 3. RAR 相关 i18n 字符串

  **What to do**:
  - 新增中/英 JSON 条目：格式名、固实、恢复记录、压缩方式、路径配置、不可用提示等
  - 在 `L.cs` 中添加对应静态 key 常量

  **Parallelization**: YES (Wave 1, with tasks 1, 2)
  **Blocks**: Task 6, 7

  **Commit**: YES (groups with 2)
  - Message: `feat(ui): add RAR compression i18n strings`

---

- [ ] 4. RarEngine.cs — CompressAsync 核心

  **What to do**:
  - `src/MantisZip.Core/Engines/RarEngine.cs` 实现 `IArchiveEngine`
  - `CanHandle` → 仅 `ArchiveFormat.Rar`
  - `CompressAsync` 核心：
    1. `RarDetector.Detect()` 获取路径
    2. 构建 `rar a` 命令：`-ep1 -m{mode} [-p{password}] [-v{size}b] [-s] [-rr{N}%] -idp -o+`
    3. `Process` 启动，重定向 stdout
    4. 正则 `(\d+)%` 解析进度 → `ArchiveProgress.PercentComplete`
    5. `CancellationToken` → `Process.Kill()`
    6. 退出码 0=成功，非0=抛出异常
  - `CanAdd`/`CanDelete` → false
  - `CompressionLevel` 映射：0→-m1, 1-3→-m2, 4-5→-m3, 6-7→-m4, 8-9→-m5

  **Parallelization**: NO (depends on Wave 1)
  **Blocks**: Task 5, 8
  **Blocked By**: Tasks 1, 2

  **Commit**: YES
  - Message: `feat(core): implement RarEngine.CompressAsync via rar.exe`

- [ ] 5. RarEngine.cs — 其余接口实现

  **What to do**:
  - `ListEntriesAsync` → 委托给 SevenZipEngine
  - `ExtractAsync` / `ExtractEntriesAsync` → 委托给 SevenZipEngine
  - `TestArchiveAsync` → `rar t -idp "{path}"`，解析 stdout 是否包含 "All OK"
  - `AddToArchiveAsync` / `DeleteEntriesAsync` → `throw NotSupportedException`

  **Parallelization**: NO
  **Blocks**: Task 8
  **Blocked By**: Task 4

  **Commit**: YES (groups with 4)

---

- [ ] 6. CompressSettingsWindow — RAR 格式 + 选项面板

  **What to do**:
  - CompressSettingsWindow.xaml：在 FormatComboBox 新增 `<ComboBoxItem Content="RAR (.rar)" Tag="rar"/>`
  - 格式选中事件中检查 `RarDetector.IsAvailable()`：
    - 不可用时弹提示 `L.T(L.Message_RarNotAvailable)` 并切回上一个格式
  - 新增 RAR 选项面板（选中 RAR 时可见）：
    - 固实压缩 CheckBox（绑定 ArchiveOptions.Solid）
    - 恢复记录数字输入框（绑定 ArchiveOptions.RecoveryRecord）
    - 压缩方式 ComboBox（绑定 ArchiveOptions.RarCompressionMethod）
  - 选项面板默认隐藏，选择 RAR 时显示，切走时隐藏
  - 从 `AppSettings.DefaultFormat` 加载时 RAR 不可用则自动切回 ZIP

  **Parallelization**: NO
  **Blocked By**: Tasks 1, 3

  **Commit**: YES
  - Message: `feat(ui): add RAR format and options panel to CompressSettingsWindow`

- [ ] 7. SettingsWindow RarExePath + ArchiveEngineFactory 注册

  **What to do**:
  - SettingsWindow 高级标签页增加 rar.exe 路径配置行（路径 TextBox + 浏览按钮 + "检测"按钮）
  - "检测"按钮调用 `RarDetector.Detect()` 并显示结果
  - `ArchiveEngineFactory` 静态构造器中注册 `RarEngine`
  - 确保 `RarEngine` 的委托（调用 SevenZipEngine）不会因循环依赖出问题（RarEngine 通过 `ArchiveEngineFactory.GetEngine` 获取 SevenZipEngine）

  **Parallelization**: NO
  **Blocked By**: Tasks 1, 2, 3

  **Commit**: YES (groups with 6)
  - Message: `feat(ui): add RarExePath setting and register RarEngine`

---

- [ ] 8. 端到端测试 + 修复

  **What to do**:
  - 完整流程测试：
    1. 打开 CompressSettingsWindow → 选 RAR → 压缩
    2. ProgressWindow 实时显示进度
    3. 产物可用 7-Zip / SharpSevenZip 打开
    4. 密码加密 → 解压时弹密码框
    5. 分卷 → 生成 .part1.rar / .part2.rar
    6. 固实 + 恢复记录选项生效
    7. 没有 rar.exe → RAR 置灰并提示
    8. Settings → 设置 rar.exe 路径 → 重新检测生效
  - 处理边界情况：
    - rar.exe 路径含空格
    - 源文件路径含中文/空格
    - 压缩到桌面/中文目录
    - 取消正在进行的压缩
    - rar.exe 中途崩溃（进程异常退出）

  **Parallelization**: NO (final wave)
  **Blocked By**: Tasks 4, 5, 6, 7

  **Commit**: YES (fixup commits as needed)
  - Message: `fix(rar): end-to-end fixes`


