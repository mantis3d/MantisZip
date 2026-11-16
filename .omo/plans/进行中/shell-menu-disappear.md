# 右键菜单用一次就消失（issue #80）

> **状态**: 🟡 已实施（2026-11-11，代码 + 单测 + 门禁全绿），**待用户真机复测** | **创建**: 2026-11-10 | **更新**: 2026-11-11 | **优先级**: P1 | **预估工时**: 8-12h
> **来源**: [GitHub issue #80](https://github.com/mantis3d/MantisZip/issues/80)（MantisZip 0.5.6 安装版，Win11 26100/26300）

## 现象

用户启用右键菜单后：右键 → 用 MantisZip 菜单压一次（操作本身成功）→ **之后再右键，MantisZip 菜单项永久消失**。在设置里点一次「应用」能恢复，但再使用一次又会消失。

## 已确认的现场（2026-11-11 用户回填）

菜单消失后查注册表：

| 查询 | 结果 | 含义 |
|---|---|---|
| `HKCU\Software\MantisZip\ContextMenu` → `DynamicMenuStatus` | **`active`** | 状态机已判「COM 生效」 |
| `HKCU\Software\Classes\*\shell\MantisZip` | **不存在** | 静态级联兜底已被删掉 |
| `HKCU\Software\Classes\*\shellex\ContextMenuHandlers\MantisZip` | 存在（`{C90B2A1E-…}`） | COM 注册仍在 |

`startup-trace.log` 同步吻合：早期 `Init.ShellFirstRun` 是 196–1206ms（`pending` → 重装级联的重活），状态翻转后连续十几条全是 **3–4.6ms**（`active` 早退）。即「既没有真菜单、也没有兜底，且永不恢复」的畸形状态。

## 现状：两套菜单 + 三态状态机

`AppSettings.EnableDynamicMenu` **默认 `true`**（`Models/AppSettings.cs:155`），出厂走 COM 路线。

| 环节 | 位置 | 行为 |
|---|---|---|
| `Install()` | `Services/ShellIntegration.Menu.cs:42-81` | 先 `Uninstall()`；COM 可用 → **只注册 COM、不装级联**，写 `pending`；COM 不可用 → 写 `disabled` + 装级联 |
| COM handler `QueryContextMenu` | `ShellExt/ContextMenuHandler.cs:394-427` | `pending` → 只插一条禁用分隔线 `────`；`≠active` → **返回 0**；只有 `active` 才出真菜单 |
| `CheckComStatus()` | `ShellIntegration.Menu.cs:531-585` | 每次启动跑；**只在 `pending` 时动作**：Explorer 进程模块里找到 `comhost.dll` → 置 `active` + `UninstallStaticMenus()`；找不到 → `InstallCascade()` |
| 启动接线 | `App.axaml.cs:215-223` | 每次启动无条件调一次 |

## 根因（两条，缺一不可）

### 根因 1：健康信号是错的 —— 「dll 被 Explorer 加载」≠「handler 能用」

`CheckComStatus()` 用 `proc.Modules` 里出现 `MantisZip.ShellExt.comhost.dll` 作为「COM 已生效」的判据。但 `comhost.dll` 是 .NET 的 native shim：Explorer 调 `CoCreateInstance` 时会先 `LoadLibrary` 它，再问 `DllGetClassObject`。**即使 `DllGetClassObject` 失败（运行时缺失 / 位宽不符 / runtimeconfig 不匹配），DLL 仍会留在 Explorer 的模块列表里。**

于是：激活失败 → 模块在 → 判 `active` → 删掉级联 → 真菜单出不来、兜底也没了。

### 根因 2：`active` 之后没有任何回退路径

`CheckComStatus()` 第一行 `if (status != pending) return;` —— 一旦 `active` 就永久早退。`DynamicMenuStatus_Fallback`（`Services/ShellIntegration.cs:120`）这个常量**定义了却从未被写入过**（全仓只有定义与设置页读取处的 `"fallback"` 分支），即「COM 挂了回退级联」这个设计状态根本没实现。

## 附带发现的两个硬缺陷

1. **`Install()` 第一步就 `Uninstall()`，而 `Uninstall()` 里有 `RestartExplorerForUnload()`（`ShellIntegration.Menu.cs:104-141`）会 `proc.Kill()` 掉 explorer.exe 再重启**（还带 `Thread.Sleep(500)` + 每个进程 `WaitForExit(3000)`）。它的本意只是「卸载程序删文件前释放 comhost.dll 句柄」，不该出现在 UI 安装路径上 —— 用户在设置里点「应用」等于重启资源管理器。
2. **每次右键启动都无条件重写整套级联**（~60 个注册表键 + `SHChangeNotify`），5 个实例并发时 1184ms，写/删交错无互斥。而且 `CheckComStatus` 每次启动都枚举 Explorer 模块（`proc.Modules` 是跨进程快照，150–800ms）。

## 方案对比

| 方案 | 内容 | 代价 | 风险 |
|---|---|---|---|
| **A 自报心跳** | COM handler 每次真正出菜单时自己写 `LastRenderUtc` + Explorer PID；`CheckComStatus` 只认心跳，**删掉模块枚举**；`active` 但心跳过期/从未写过 → 写 `fallback` + 装回级联 | 中（~4h） | handler 要写注册表（HKCU，无权限问题） |
| **B 默认回级联** | `EnableDynamicMenu` 出厂默认 `false`，COM 降为 opt-in「实验」 | 极小（1 行 + 文案） | 失去动态菜单的「带文件名标签 / 多选计数」；换来零 COM 依赖 |
| **C 追激活失败根因** | 为什么 `DllGetClassObject` 失败（位宽 / runtimeconfig / runtime 版本） | 需用户机器现场 | 结论可能依赖用户环境 |
| **D 拆坑** | ① 抽出不重启 Explorer 的 `CleanupRegistrations()` ② 级联写/删加互斥 + 幂等 ③ 稳态下不重写 | 中（~4h） | 触碰安装/卸载契约，需回归 |

**推荐：A + D 必做，B 请用户拍板，C 用一条诊断命令先定性。**

关键结论：**A 必须换掉健康信号**。原来计划的「`active` 时再探一次模块」是无效的 —— 根因 1 说得很清楚，模块在并不代表能用。唯一可信的信号是 handler 自己报「我真的出菜单了」。

## 实施顺序（建议拆三个提交）

### 提交 1：A —— handler 自报 + `active` 可回退

- `ShellExt/ContextMenuHandler.cs`：`QueryContextMenu` 成功插入菜单项后写
  `HKCU\Software\MantisZip\ContextMenu` → `LastRenderUtc`（ISO8601）+ `LastRenderExplorerPid`（`Environment.ProcessId`）
  - 写失败静默（`try/catch`，绝不抛给 Explorer）
  - 仅在 `itemsAdded > 0` 时写，避免「返回 0」也刷新心跳
- `ShellIntegration.Menu.cs` `CheckComStatus()` 重写：
  - **删除 `Process.Modules` 枚举**（省掉每次启动 150–800ms）
  - `pending`：心跳在 N 分钟内且 PID 是当前 Explorer → `active` + `UninstallStaticMenus()`；否则装级联（保持 `pending`）
  - `active`：心跳过期（> 7 天）**或**从未写过且安装超过 1 天 → `fallback` + `InstallCascade()` + `NotifyAssocChanged()`
  - 阈值写进常量并注释理由（为什么是 7 天 / 1 天）
- `fallback` 语义定死：级联在位；COM handler 因 `≠active` 返回 0（现状已如此，**不改 ShellExt 的三态判定**）

### 提交 2：D —— 拆掉 Explorer 重启 + 并发幂等

- 从 `Uninstall()` 抽出 `CleanupRegistrations()`（纯注册表清理）；`Uninstall()` = `CleanupRegistrations()` + `RestartExplorerForUnload()`，仅保留给 `--uninstall-shell`（安装程序 `[UninstallRun]` 需要）
- `Install()` 改调 `CleanupRegistrations()` → 设置页点「安装 / 应用」不再重启资源管理器
- `InstallCascade` / `UninstallStaticMenus` 用 `Local\MantisZip.ShellReg` 命名互斥串行化；写入前比对现状，一致就跳过（幂等，省掉 200ms 重写）
- `App.axaml.cs` 启动路径：`CheckComStatus` 在稳态（级联已就位且状态无需变化）时零注册表写入

### 提交 3：B（决策点，需用户确认）

- `EnableDynamicMenu` 默认值 `true` → `false`；设置页「动态菜单（实验）」文案同步
- 老配置不受影响（`AppSettings` 已有该键，只改默认值）
- 若用户不同意，跳过此提交，改在 `docs/KNOWN_ISSUES.md` 立一条等 C 档结论

## 涉及文件

- `src/MantisZip.ShellExt/ContextMenuHandler.cs`（心跳写入）
- `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs`（`Install` / `Uninstall` / `CheckComStatus` / `InstallCascade` / `UninstallStaticMenus`）
- `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs`（`fallback` 语义注释、心跳键名常量）
- `src/MantisZip.UI.Avalonia/Models/AppSettings.cs`（仅提交 3：默认值）
- `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs`（安装/应用路径改调新入口；`fallback` 文案分支已存在）
- `tests/MantisZip.UI.Avalonia.Tests/`（状态机迁移用例：`pending`+新鲜心跳→`active`；`active`+过期心跳→`fallback`+级联在位；`active`+从未心跳→保持；重复调用幂等。用 `RegistryHost` 沙箱 + `NotifyAssocChangedForTesting` 注入缝）

## 验证

- `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` 0 错误
- `pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests` 全绿
- 真机手工验收（需用户机器，本机未装 shell 集成）：
  1. 启用菜单 → 右键压一次 → 再右键，**菜单仍在**
  2. 设置里点「安装 / 应用」→ **资源管理器不重启**（任务栏不闪）
  3. 多选 5 个文件右键压一次 → 启动耗时不再出现 ~1s 尖峰
  4. 把 `DynamicMenuStatus` 手工改成 `active` 但清掉心跳 → 下次启动自动回到级联
- `pwsh -File scripts\verify-repo.ps1 -Task check`（版本 / 三语 / 守卫）

## 边界

- 不动 `QueryContextMenu` 的三态判定语义（`pending` / `active` / `fallback` 仍落在「≠active → 返回 0」的既有分支）
- 不动 `installer.iss` 的 `[UninstallRun] --uninstall-shell`（卸载时重启 Explorer 是必要行为）
- 不做 Win11 一级菜单（MSIX sparse package），见 `win11-first-level-menu.md`
- 不新增用户可见文案（`fallback` 状态文案已存在）

## 待回填 / 未决

- [x] 用户补两项诊断（2026-11-11 已请）：`%LOCALAPPDATA%\MantisZip\ShellExt\log.txt`（**注意：不是 `MantisZip\ShellExt.log`**，2026-11-10 的回复里路径写错了）与 `New-Object -ComObject MantisZip.ContextMenu` 的激活结果 —— 用户 2026-11-11 回填：**重装后菜单恢复正常**，激活命令「无任何报错、返回 `System.__ComObject`」= **激活成功**；`ShellExt\log.txt` 显示 `QueryContextMenu` 正常插入 8 项、图标预加载成功、无异常
- [x] 据激活结果确认 C 档 —— **结论：激活本身没问题，`DllGetClassObject` 失败这个假设被推翻**。真实成因是「模块已加载」与「handler 是否被调用」的错位：升级前 `ShellExt\log.txt` **不存在**（handler 从未被实例化），但 Explorer 模块列表里已有 comhost.dll —— 最可能是 11-03 升级时旧 comhost 模块仍挂在当时那个 explorer.exe 进程里，磁盘文件被替换后该进程内的 COM 激活/模块状态与磁盘不一致，于是那个进程里不再调用 handler；旧代码只看「模块在不在」⇒ 判 `active`、删掉级联 ⇒ 「模块在、handler 不干活、兜底也没了」。用户重装会走 `--uninstall-shell`（内含 explorer.exe 重启），进程一换、模块重载即恢复 —— 与他「重装后立刻正常、日志里 8 项正常」完全吻合。**因此不需要再追激活失败的根因，要修的正是本计划的「假健康信号 + `active` 无回退」**
- [x] 确认是否采纳提交 3（改出厂默认）—— 用户 2026-11-11 授权「都按最稳妥的方式，你自己决定」，已采纳（默认 `false`）
- [x] 0.5.6 安装包里的 `MantisZip.ShellExt.comhost.dll` 与 `MantisZip.ShellExt.runtimeconfig.json` 是否自洽 —— 用户实测重装后 handler 正常工作，且 comhost 版本号属 SDK 自带原生 shim，**不是**问题所在
- [ ] **0.5.7 发布后请用户按「启用 → 右键压一次 → 再右键」复测**（用户已答应），通过后归档到 `.omo/plans/已完成`

## 实施记录（2026-11-11）

三个提交合并为一次实施，与计划的差异如下（**计划正文保持原样，以本节为准**）：

| 项 | 计划 | 实际 | 原因 |
|---|---|---|---|
| `pending` 期菜单 | 只插一条禁用分隔线占位 | **渲染真菜单**，同时级联兜底在位 | 占位方案两头空（既无真菜单、也无法证明自己可用）。宁可短暂出现两个入口，也不让用户没菜单 |
| 心跳阈值 | `pending` N 分钟 / `active` 7 天 / 从未写过 1 天 | `Fresh` 30 分钟（+ PID 必须是存活 Explorer）/ `Recent` 7 天 / 试用期 `ComProbeWindow` **2 天** | 30 分钟够覆盖「右键 → 点菜单项 → 新进程启动」；2 天试用期让「装了但从不右键」的用户也能确定回退 |
| `active` 心跳「从未写过」 | 「且安装超过 1 天」才回退 | 直接回退（`Never` → `fallback`） | 能进入 `active` 必然写过心跳，出现 `Never` 只可能是键被清 / 老安装 ⇒ 立即回退更安全 |
| 状态机形态 | 隐含写在 `CheckComStatus` 里 | 抽出**纯函数** `DecideComAction(status, health, probeExpired)` + `ComStatusAction` 枚举 | 决策表可单测（17 组 `[InlineData]`），避免再出现「状态机错了没人发现」 |
| 测试位置 | `tests/MantisZip.UI.Avalonia.Tests/` | `tests/MantisZip.Tests/ShellMenuStatusMachineTests.cs`（`[Collection("RegistryTests")]` + `RegistryHost` 沙箱） | 注册表沙箱范式已在 `MantisZip.Tests`，无需重造 |
| 设置页卸载入口 | 未提 | 新增 `ShellIntegration.RemoveMenus()` | `UninstallShell` 原先借道 `Uninstall()`，会重启资源管理器 |
| 「不动三态判定」 | 边界里写明不动 | **已改**：`pending` 与 `active` 都出真菜单 | 见第一行 |

未做：C 档（激活失败的具体原因）仍等用户回填激活命令结果；`--uninstall-shell` 的 Explorer 重启行为保持不变。

已知边界（登记 KI-018）：`active` 状态下 handler 若再静默失效，自愈要等下一次 MantisZip 启动 —— 菜单不出现时不会拉起任何进程。

