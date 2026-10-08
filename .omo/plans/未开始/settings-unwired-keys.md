# 设置项无消费者修复（密码匹配提示 / 默认解压位置 / 级联菜单开关）

> **状态**: 🟡 2026-09-27 三项评估完成（10 已闭环；5/9 结论已记录、暂不实施，启动时分别按裁剪版 / b 方案执行） | **立项**: 2026-09-27 | **优先级**: P3 | **预估工时**: 3-5h
> **来源**: 2026-09-27「压缩对话框不读默认级别」同类问题全量排查（共 10 项，其余 7 项已于同日修复）
> **当前现状**: 5/9 两项设置在 Avalonia 版零消费者（开关存在、可保存，但代码从不读取，评估结论已记录）；10 项 `EnableCascadingMenu` 字段本就不存在（WPF 已由 `EnableDynamicMenu` 替代且已接线），唯一缺陷 AGENTS.md 陈旧描述已于 2026-09-27 修复

## 背景

排查「压缩对话框不读取设置里的默认压缩级别」时，对照 WPF 旧版 `LoadDefaults*` 逻辑与全量 `AppSettings` 字段引用，共发现 10 项「设置写了没人读」问题。2026-09-27 已修复其中 7 项：

| # | 设置项 | 问题 | 状态 |
|---|--------|------|------|
| 1 | `DefaultLevel` | 压缩级别恒 5 | ✅ 已修复 |
| 2 | `DefaultFormat` | 压缩格式恒 zip | ✅ 已修复 |
| 3 | `OpenFolderAfterExtract` | 解压对话框复选框恒不勾选 | ✅ 已修复 |
| 4 | `DeleteArchiveAfterExtract` | GUI 解压路径从不删原包（仅 CLI 调） | ✅ 已修复 |
| 5 | `ShowPasswordMatchNotification` | 无消费者（功能整体未移植） | ⏸️ 2026-09-27 评估：暂不动，结论已记录（启动按裁剪版） |
| 6 | `MaxRecentFiles` | RecentFilesManager 硬编码 10 | ✅ 已修复 |
| 7 | `CleanTempOnStartup` | 启动从不清理 | ✅ 已修复（另见 [clean-temp-on-startup-avalonia.md](clean-temp-on-startup-avalonia.md)） |
| 8 | `AllowElevation` | 提权确认不读开关 | ✅ 已修复 |
| 9 | `ExtractDestination` | 无消费者 | ⏸️ 2026-09-27 评估：暂不动，结论已记录（启动按 b 方案） |
| 10 | `EnableCascadingMenu` | 无消费者 | ✅ 2026-09-27 评估+修复（字段本就不存在，已修 AGENTS.md 陈旧描述，见文内评估结论） |

## 现状核实（2026-09-27）

### 5. ShowPasswordMatchNotification — 功能未移植，非单纯接线

- `AppSettings.ShowPasswordMatchNotification`（默认 true）零引用。
- WPF 语义（`App.Password.cs:195` 附近）：密码库自动匹配成功后，在 `ProgressWindow` 显示「密码匹配成功/尝试密码」提示区块，开关控制显隐。
- Avalonia 端 `ProgressWindow.ShowPasswordAttempt` / `ShowPasswordMatched` **零调用者**——提示 UI 存在但从未被触发。同族的 `PasswordRevealByDefault` 已由 `PasswordService` 接线（保留旧密码默认可见），唯独该提示断链。
- **结论**：不是「读一下设置」能解决的，需要把「解压/压缩时自动匹配密码成功 → 通知进度窗口」的调用链接回来，再挂开关。

#### 2026-09-27 评估结论（用户决策：暂不动，仅记录）

对 WPF 原实现（git 历史 `App.Password.cs` / `App.Extract.cs` / `MainWindow.xaml.cs`）与 Avalonia 现状做了完整核实：

**三层现状**：
1. **开关全就绪零消费**：`AppSettings` 字段 + 设置页复选框「显示密码匹配通知」（`Settings_Pwd_ShowNotification`）+ `SettingsWindowViewModel` 读写均已存在，无任何行为读取。
2. **通知 UI 全套已建成零调用**：`ProgressWindow.PasswordSection`（三行布局、显示/复制按钮、警告/成功底色）+ `ProgressViewModel.ShowPasswordAttempt/ShowPasswordMatched/HidePasswordSection` + 三语本地化 key（`Progress_PwdMatched` 等）全部就绪；`ShowPasswordMatched` 内部还消费 `PasswordRevealByDefault`。废弃需删 5+ 处（含三语 key），**成本不低于接线**。
3. **匹配时机已迁移**（比计划描述更准确的诊断）：WPF 是「解压时才匹配 → 进度窗口内通知」；Avalonia 把匹配前移为——
   - GUI 打开压缩包：`MainWindowViewModel.LoadArchiveAsync` Phase A → 工具栏绿钥匙 + 状态栏提示（**已冗余**）
   - 解压设置对话框：`ExtractSettingsViewModel.TryAutoUnlockAsync` → 行徽标 ✓/🔑（**已冗余**）
   - CLI/右键解压：`App.ResolveCliPassword` → **完全静默仅 DebugLog（真实缺口）**——压缩包未在 GUI 打开过时，密码库自动匹配成功→解压→开文件夹全程无提示，WPF 此处有通知，Avalonia 丢失

**评估倾向（未实施）**：
- **裁剪版移植（约 1-2h，推荐）**：GUI 三个解压命令在 `RunWithProgress` 回调内经 `ProgressWindow.CurrentVisible` 调 `ShowPasswordMatched`（密码取 session cache、描述用 `FindSavedPasswordEntry`）挂开关；CLI 把 `ResolveCliPassword` 移到进度窗口创建后补通知——修复 CLI 真实缺口。
- **完整对齐 WPF（约 2-3h）**：裁剪版 + CLI 批处理密码循环接 `ShowPasswordAttempt` 逐个尝试黄底提示。
- **废弃（不推荐）**：需删设置页 CheckBox + `SettingsWindowViewModel` 两处 + `AppSettings` 字段 + `ProgressWindow` 三方法 + `ProgressViewModel` 四方法 + `PasswordSection` AXAML + ~10 个三语 key + AGENTS.md「密码管理」行，且永久放弃 CLI 反馈缺口的载体。

**状态**：⏸️ 按用户决策暂不实施，保持现状；若后续启动本计划第 5 项，直接按上述裁剪版方案执行。

### 9. ExtractDestination — 双命令设计导致设置失效

- WPF：`ResolveExtractDestinationStatic`（`App.xaml.cs:605-629`）在 `Extract_Click` / `ExtractSelected_Click` / `FileListCtx_ExtractTo` 读取（`ask`/`same-dir`/`desktop`），决定解压到哪。
- Avalonia：解压入口改为显式双命令（「解压」弹对话框选目录 / 「解压到此处」用当前位置），设置项无任何读取点。
- **方案（待决策）**：
  - a. 保留双命令现状，把设置项从设置页移除（承认语义已变）；
  - b. 让「解压」命令按设置预填目的地（`same-dir` → 源目录、`desktop` → 桌面，`ask` → 保持现状），对话框仍可改；
  - c. 恢复 WPF 语义（按设置直接跳过/预填对话框）。
- **倾向**：b——最小改动、保留用户显式选择权。

#### 2026-09-27 评估结论（用户决策：暂不动，仅记录）

对 WPF 终版（`98ca7d4^`，WPF 删除前最后一版）与 Avalonia 现状做了完整核实：

**WPF 终版语义**（`App.xaml.cs:605 ResolveExtractDestinationStatic`）：
- `same-dir` → 直接返回压缩包目录，**不弹任何对话框**；`desktop` → 直接返回桌面，**不弹任何对话框**；`ask` → 弹 `QuickPathPreDialog` 选目录（起点=压缩包目录），取消=中止。
- 3 个调用点：`Extract_Click`（**仅条目列举失败的回退路径**——终版 WPF 主路径其实已改弹富对话框 `ExtractSettingsWindow`）、`ExtractSelected_Click`（工具栏解压选中）、`FileListCtx_ExtractTo`（右键解压到）。即 WPF 自己也已部分富对话框化，本设置实际只管「选中解压/右键解压到」+ 回退路径。

**Avalonia 现状（三层全就绪零消费）**：
1. 设置页 ComboBox「默认解压目录」（`SettingsWindow.axaml:231-244`，选项 每次询问/压缩包所在目录/桌面）+ `SettingsWindowViewModel` 读写（819/1307）+ 三语 key（`Settings_Extract_Dest_*`）均就绪，无任何行为读取。
2. 三个解压入口全部硬编码默认 `目录/包名`：富对话框 `ExtractSettingsViewModel` ctor（`ExtractSettingsViewModel.cs:199`，`ExtractArchive` 与 CLI `--extract` 共用 → 改 ctor 自动生效）、`ExtractTo` 强制覆盖（`MainWindowViewModel.cs:2647`）、裸目录选择器 `ExtractSelectedTo` defaultDest（`MainWindowViewModel.cs:2351`）。
3. 固定语义命令（`ExtractArchiveHere`/`ExtractArchiveToName`/`SmartExtract`/CLI 三个直解 verb）不受设置影响（与 WPF 一致）；无任何测试引用。

**三方案对比**：

| | a 删除设置 | b 预填目的地（倾向） | c 恢复 WPF 跳过对话框 |
|---|---|---|---|
| 成本 | ~0.5h | **~1-2h** | ~2-3h + UX 确认 |
| 富对话框 | 不变 | **保留**（same-dir→预填包目录、desktop→预填桌面、ask→维持 `目录/包名`） | same-dir/desktop 跳过 → 丢失过滤/单次冲突选项/预览 |
| 标签吻合 | — | ✅「默认解压目录」字面吻合 | ❌（语义变成「是否询问」，标签得改） |
| 风险 | 桌面快捷目的地能力被删且无处安放 | **低**（纯预填，不改变弹窗决策） | 中：与已有「解压到此处」命令功能重复；升级用户 Ctrl+E 突然无确认直解 |

**评估倾向（未实施）**：**b**——①设置页标签「默认解压目录」与预填语义完全自洽；②改动点仅 3 处（ctor 预填 / `ExtractTo` 覆盖 / `ExtractSelectedTo` defaultDest），CLI `--extract` 自动生效，可仿 `AdvancedFormatOptions_Constructor_MirrorsAppSettings` 加镜像测试锁回归；③c 的跳过对话框与 Avalonia 已有「解压到此处」命令功能重复，且让过滤/单次冲突覆盖对 same-dir/desktop 用户不可达；④a 会永久删除桌面目的地这一 WPF 遗留能力。

**状态**：⏸️ 按用户决策暂不实施，保持现状；若后续启动本计划第 9 项，直接按上述 b 方案执行。

### 10. EnableCascadingMenu — COM 优先后开关失效

- WPF：该开关切换「单个 MantisZip 级联子菜单」vs「各动作为独立顶层菜单」两种注册模式。
- Avalonia：`ShellIntegration` 改为 COM 组件优先（`InstallCom`）+ 静态注册回退，两种模式的切换逻辑未读该设置；AGENTS.md 仍描述其控制两种模式（文档与实现脱节）。
- **方案（待决策）**：
  - a. COM 模式内实现级联/平铺两种形态并接开关（工作量大）；
  - b. 明确 COM 模式只支持一种形态，设置项移除/隐藏，同步更新 AGENTS.md；
  - c. 保留开关仅作用于静态注册回退路径，并在设置页注明「仅回退模式生效」。
- **倾向**：先确认 COM 版是否已支持两种形态（`ContextMenuHandler` 的 `ExtendedSubCommandsKey` 逻辑），再在 a/c 间决策。

#### 2026-09-27 评估结论（用户决策：记录 + 修复 AGENTS.md）

**计划前提有误：该设置在 Avalonia 中根本不存在，且在 WPF 时期已被删除。**

1. **Avalonia 全仓零匹配**：`rg -ni "cascad" src tests` 仅命中 `CascadeRoot`/`InstallCascade`；`AppSettings.cs` 上下文菜单区段无此字段，设置页无复选框、无三语 key。
2. **WPF 自己已删**：commit `5b431fb`（2026-06-17，`feat(settings): add dynamic menu toggle for COM/static context menu`）将死设置 `EnableCascadingMenu` **替换**为 `EnableDynamicMenu`（commit 明言 "Replace dead EnableCascadingMenu"、"Remove unused CascadeCheck (cascade-only since v0.4.0)"）——WPF v0.4.0 起静态路径仅级联一种形态，**verb 模式（顶层独立动词）作为死代码废弃**。
3. **替代者已接线**：`EnableDynamicMenu`（默认 true）被 `ShellIntegration.Install()`（`ShellIntegration.Menu.cs:42-72`）消费——true → `InstallCom()`（`CheckComStatus()` 检测 COM 未在 Explorer 加载时自动装静态级联回退），false → 静态级联；设置页有对应复选框（`SettingsWindow.axaml:925`）。
4. **前置确认答案**（计划要求先确认再决策）：`ContextMenuHandler`（COM 版）固定输出「打开/解压 + 压缩」两组子菜单，**仅支持级联形态**；`ShellIntegration.Menu.cs` 方法清单只有 `InstallCom`/`InstallCascade`，无 verb 安装器（`UninstallStaticMenus` 仅清理历史残留）→ 计划选项 a（COM 内实现双形态）无必要性。
5. **真正缺陷 = AGENTS.md 两处陈旧描述**：
   - `AGENTS.md:215` 设置清单列了不存在的 `EnableCascadingMenu`（且同时列了真实的 `EnableDynamicMenu`）；
   - `AGENTS.md:257` "Two modes controlled by `AppSettings.EnableCascadingMenu`" + Cascade（默认 **off**）/Verb 两模式描述三处皆错（设置名、默认值应为 on=COM 动态、verb 模式已删）。

**处理（用户决策）**：修复 AGENTS.md 两处——L215 删 `EnableCascadingMenu` 并注明废弃；L257 段落重写为 `EnableDynamicMenu` 控制「COM 动态 / 静态级联回退」双模式 + 历史注记（`5b431fb`）。零代码改动。计划选项 a/b/c 基于「设置存在」的错误前提，全部作废。

**状态**：✅ 2026-09-27 已修复 AGENTS.md（215 / 257-260 两处）；本项无需代码实施，第 10 项闭环。

## 方案（三项通用步骤）

1. ✅ 逐项确认产品语义（2026-09-27 三项评估完成：5 暂不动启动按裁剪版、9 暂不动启动按 b、10 已闭环无需实施）。
2. （仅 5/9 启动时）按对应方案接线：新增读取点 → 复用现有 UI/注册机制，不引入重复字段。
3. （仅 5/9 启动时）测试：每项补「设置值 → 行为」镜像断言（参照 `CompressSettingsViewModelTests.AdvancedFormatOptions_Constructor_MirrorsAppSettings` 风格）。
4. 同步更新 AGENTS.md 描述：`EnableCascadingMenu` ✅ 已于 2026-09-27 修复（215/257-260）；`ExtractDestination` 描述随第 9 项实施时再同步（当前 AGENTS.md:213 列该字段仍属实——字段存在，仅无消费者）。

## 影响文件（预估）

- 5: `App.Password.cs` / 解压流程（`ExtractFlow` 或 `MainWindowViewModel`）+ `ProgressWindow.xaml(.cs)` + 测试
- 9: `MainWindowViewModel.ExtractSelectedTo` 或 `ExtractArchive` + `ExtractSettingsWindow` 预填 + 测试
- 10: ✅ 已完成——仅 `AGENTS.md`（215 / 257-260 两处），零代码改动

## 验收标准

- （5/9 启动实施后）对应设置在设置页修改后，重启应用行为随之变化；`dotnet build` + `dotnet test` 全部通过，设置页不再出现「永远无效」的开关。
- 10：✅ 已达成——`rg -ni "EnableCascadingMenu" src tests` 零匹配，AGENTS.md 描述与实现一致。
