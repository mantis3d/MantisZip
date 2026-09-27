# 设置项无消费者修复（密码匹配提示 / 默认解压位置 / 级联菜单开关）

> **状态**: 📋 未开始 | **立项**: 2026-09-27 | **优先级**: P3（部分需产品决策） | **预估工时**: 3-5h
> **来源**: 2026-09-27「压缩对话框不读默认级别」同类问题全量排查（共 10 项，其余 7 项已于同日修复）
> **当前现状**: 三个设置项在 Avalonia 版零消费者——开关存在、可保存，但代码从不读取

## 背景

排查「压缩对话框不读取设置里的默认压缩级别」时，对照 WPF 旧版 `LoadDefaults*` 逻辑与全量 `AppSettings` 字段引用，共发现 10 项「设置写了没人读」问题。2026-09-27 已修复其中 7 项：

| # | 设置项 | 问题 | 状态 |
|---|--------|------|------|
| 1 | `DefaultLevel` | 压缩级别恒 5 | ✅ 已修复 |
| 2 | `DefaultFormat` | 压缩格式恒 zip | ✅ 已修复 |
| 3 | `OpenFolderAfterExtract` | 解压对话框复选框恒不勾选 | ✅ 已修复 |
| 4 | `DeleteArchiveAfterExtract` | GUI 解压路径从不删原包（仅 CLI 调） | ✅ 已修复 |
| 5 | `ShowPasswordMatchNotification` | 无消费者（功能整体未移植） | ⬜ 本计划 |
| 6 | `MaxRecentFiles` | RecentFilesManager 硬编码 10 | ✅ 已修复 |
| 7 | `CleanTempOnStartup` | 启动从不清理 | ✅ 已修复（另见 [clean-temp-on-startup-avalonia.md](clean-temp-on-startup-avalonia.md)） |
| 8 | `AllowElevation` | 提权确认不读开关 | ✅ 已修复 |
| 9 | `ExtractDestination` | 无消费者 | ⬜ 本计划（需产品决策） |
| 10 | `EnableCascadingMenu` | 无消费者 | ⬜ 本计划（需产品决策） |

## 现状核实（2026-09-27）

### 5. ShowPasswordMatchNotification — 功能未移植，非单纯接线

- `AppSettings.ShowPasswordMatchNotification`（默认 true）零引用。
- WPF 语义（`App.Password.cs:195` 附近）：密码库自动匹配成功后，在 `ProgressWindow` 显示「密码匹配成功/尝试密码」提示区块，开关控制显隐。
- Avalonia 端 `ProgressWindow.ShowPasswordAttempt` / `ShowPasswordMatched` **零调用者**——提示 UI 存在但从未被触发。同族的 `PasswordRevealByDefault` 已由 `PasswordService` 接线（保留旧密码默认可见），唯独该提示断链。
- **结论**：不是「读一下设置」能解决的，需要把「解压/压缩时自动匹配密码成功 → 通知进度窗口」的调用链接回来，再挂开关。

### 9. ExtractDestination — 双命令设计导致设置失效

- WPF：`ResolveExtractDestinationStatic`（`App.xaml.cs:605-629`）在 `Extract_Click` / `ExtractSelected_Click` / `FileListCtx_ExtractTo` 读取（`ask`/`same-dir`/`desktop`），决定解压到哪。
- Avalonia：解压入口改为显式双命令（「解压」弹对话框选目录 / 「解压到此处」用当前位置），设置项无任何读取点。
- **方案（待决策）**：
  - a. 保留双命令现状，把设置项从设置页移除（承认语义已变）；
  - b. 让「解压」命令按设置预填目的地（`same-dir` → 源目录、`desktop` → 桌面，`ask` → 保持现状），对话框仍可改；
  - c. 恢复 WPF 语义（按设置直接跳过/预填对话框）。
- **倾向**：b——最小改动、保留用户显式选择权。

### 10. EnableCascadingMenu — COM 优先后开关失效

- WPF：该开关切换「单个 MantisZip 级联子菜单」vs「各动作为独立顶层菜单」两种注册模式。
- Avalonia：`ShellIntegration` 改为 COM 组件优先（`InstallCom`）+ 静态注册回退，两种模式的切换逻辑未读该设置；AGENTS.md 仍描述其控制两种模式（文档与实现脱节）。
- **方案（待决策）**：
  - a. COM 模式内实现级联/平铺两种形态并接开关（工作量大）；
  - b. 明确 COM 模式只支持一种形态，设置项移除/隐藏，同步更新 AGENTS.md；
  - c. 保留开关仅作用于静态注册回退路径，并在设置页注明「仅回退模式生效」。
- **倾向**：先确认 COM 版是否已支持两种形态（`ContextMenuHandler` 的 `ExtendedSubCommandsKey` 逻辑），再在 a/c 间决策。

## 方案（三项通用步骤）

1. 逐项确认产品语义（5 走功能移植、9/10 需先做上述决策）。
2. 按对应方案接线：新增读取点 → 复用现有 UI/注册机制，不引入重复字段。
3. 测试：每项补「设置值 → 行为」镜像断言（参照 `CompressSettingsViewModelTests.AdvancedFormatOptions_Constructor_MirrorsAppSettings` 风格）。
4. 同步更新 AGENTS.md 中关于 `EnableCascadingMenu`/`ExtractDestination` 的描述。

## 影响文件（预估）

- 5: `App.Password.cs` / 解压流程（`ExtractFlow` 或 `MainWindowViewModel`）+ `ProgressWindow.xaml(.cs)` + 测试
- 9: `MainWindowViewModel.ExtractSelectedTo` 或 `ExtractArchive` + `ExtractSettingsWindow` 预填 + 测试
- 10: `ShellIntegration.Assoc.cs` / `ContextMenuHandler.cs` + `SettingsWindow.axaml` + AGENTS.md

## 验收标准

- 三个设置项在设置页修改后，重启应用对应行为随之变化。
- `dotnet build` + `dotnet test` 全部通过；设置页不再出现「永远无效」的开关。
