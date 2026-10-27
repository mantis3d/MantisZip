# 安装器产品身份统一与旧版迁移桥（issue #45）

## TL;DR

> **Quick Summary**: 两个安装包（WebSetup / Offline）当前用**不同的 AppId**，导致 Inno 认为它们是两个产品，装另一个变体时**不会卸载旧版**，直接往同一个 `{autopf}\MantisZip` 写入 → 两套 `unins000.*` 互删文件、两条同名 ARP 条目、卸载钩子静默失败。本计划把两者统一成同一产品身份，并加一段迁移桥收拾存量机器。
>
> **Deliverables**:
> - `installer.iss` / `installer-selfcontained.iss` 共用同一 AppId + `SetupMutex`
> - `[Code] PrepareToInstall` 迁移桥：静默卸载旧 `{963001F3-…}` 身份，失败即中止并提示手动卸载
> - 加固：跨变体残留清理、卸载可选删个人设置、首次运行标记清理
> - 可诊断：`{app}\install-manifest.json` + `HKLM\Software\MantisZip` 的 `InstalledVariant`/`InstalledVersion`
>
> **Estimated Effort**: Medium（~3-4h，含编译验证；不含真实安装实验）
> **Parallel Execution**: NO（两个脚本共用同一套桥接代码，必须同步改）
> **Critical Path**: 统一 AppId → 迁移桥 → 加固 → manifest → 编译验证

---

## Context

### Original Request

GitHub issue `mantis3d/MantisZip#45`（2026-10-18，label `bug`，0 评论）原文：

> 安装新版的时候，旧版卸载不干净导致出问题。比如要求安装运行时。

附带截图是 .NET apphost 的框架缺失框（`MantisZip.UI.Avalonia.exe` / `You must install or update .NET to run this application.` / `→ Download it now`）。用户后续反馈：**除了截图没有更多细节**，也无法提供复现环境。

### Discussion Summary

截图这个框由 **.NET apphost** 弹出，唯一前提是该 exe 按自己的 `runtimeconfig.json` 去共享框架解析、而机器上找不到对应 .NET。由此可推：

- 自包含（Offline）版正常**不会**弹此框（其 `runtimeconfig.json` 用 `includedFrameworks`，运行时 DLL 就在 `{app}`）。
- 出现此框 ⇒ 磁盘处于下列之一：① 混装后的残缺目录（框架依赖版的 `runtimeconfig.json`/exe 残留，或自包含版的 `hostfxr.dll`/`coreclr.dll` 被另一套卸载器删掉）；② WebSetup 安装时选了「否 — 跳过下载」。

两种情况都指向同一个结构性问题，而不是某台机器的偶发损坏：

- `installer.iss:13` AppId=`{F7A3C8E1-2D4B-4A9F-9C6E-8B5D7A3F2E1C}`
- `installer-selfcontained.iss:17` AppId=`{963001F3-5748-4834-9489-97D6D00E3917}`
- 两者 `DefaultDirName` 都是 `{autopf}\MantisZip`，`AppName` 都是 `MantisZip`
- 两者都**没有** `[UninstallDelete]`、没有 `SetupMutex`

AppId 不同 ⇒ Inno 视作两个独立产品 ⇒ 装另一个变体**不会先卸旧版**，直接写入同一目录：两套安装日志记录同一批路径，卸任一条都会删掉另一套在用的 DLL/runtimeconfig；`[UninstallRun]` 钩子（`--uninstall-shell` / `--uninstall-assoc`）依赖 `{app}\MantisZip.UI.Avalonia.exe` 存在，被对方先删就**静默失败** ⇒ 注册表残留，即字面意义的「卸不干净」。

当年立项依据 `.omo/plans/已完成/self-contained-installer.md:35` 写的是「两个 Inno Setup 脚本不能使用相同的 AppId，否则会互相覆盖注册信息」——这条**说反了**：相同 AppId 才是「同一产品，升级时自动先卸旧版」；不同 AppId 才是互不认识地共享目录。

### Research Findings

- Inno Setup 的 `AppId` 是产品身份：安装时发现同 AppId 已存在，会先静默运行旧版 `unins000.exe` 再写新文件。
- 静默卸载时 Inno 若无法删除被占用文件，会登记 `PendingFileRenameOperations`（重启后删除）。若此后我们又往同一路径写入新文件，重启会把**新文件**删掉 ⇒ 迁移桥必须先确保应用没有在运行。
- Inno 卸载器在需要自删时会把自己复制到临时名再退出，`Exec(..., ewWaitUntilTerminated)` 可能早于真正删除结束 ⇒ 迁移桥必须**轮询旧卸载注册表键消失**来判定完成，不能只看退出码。
- `PrepareToInstall(var NeedsRestart: Boolean): String` 在「开始复制文件」之前调用，返回非空字符串即以该文本中止安装 —— 正是「先卸旧的、后写新的」的唯一正确挂点。
- `{#MyAppVersion}` 这类预处理内联在 `[Code]` 段同样生效，可在 Pascal 里拼 JSON / 写注册表。
- 本机**没有 ISCC**（`Inno Setup 6\ISCC.exe` 三个常见路径均不存在），`.github/workflows/release.yml:79` 用 `choco install innosetup` 在 runner 上装 ⇒ 本地只能做静态一致性校验，最终编译验证必须靠 release workflow 或用户本机装 ISCC。

---

## Work Objectives

### Core Objective

让两个安装变体成为**同一产品的两种打包**：装任一变体都先干净移除旧变体，磁盘上永远只有一套文件、ARP 里永远只有一条同名条目；并对已处于烂状态的存量机器提供一次性自愈路径。

### Concrete Deliverables

- `installer.iss`：统一 AppId、`SetupMutex`、迁移桥、跳过运行时二次确认、`[InstallDelete]` 残留清理、manifest、卸载删设置询问
- `installer-selfcontained.iss`：同一 AppId、`SetupMutex`、迁移桥、manifest、卸载删设置询问、修正过时注释
- `[CustomMessages]`：中英双语新增 3 条（跳过运行时二次确认、旧版正在运行、旧版卸载失败）
- 本计划文档 + `docs/PLAN.md` S 档一行

### Definition of Done

- [x] 两个 `.iss` 的 AppId 完全一致，且 `installer-selfcontained.iss` 不再含旧注释
- [x] 两个脚本的 `[Code]` 都实现 `PrepareToInstall`，且逻辑一致（查旧键 → 关应用 → 静默卸载 → 轮询确认 → 失败中止）
- [x] 跳过运行时的二次确认默认按钮为「否」
- [x] 装完生成 `{app}\install-manifest.json`，并在 `HKLM\Software\MantisZip` 写 `InstalledVariant` / `InstalledVersion`
- [x] 卸载时询问是否同时删除 `%LOCALAPPDATA%\MantisZip`（默认否），并且始终清理 `HKCU\Software\MantisZip`
- [x] 静态一致性校验脚本通过（AppId 相同、旧 AppId 常量存在、`begin/end` 配平）
- [x] `pwsh -File scripts\verify-repo.ps1 -Task check` 通过（版本 6 处、三语 key、五条守卫）
- [ ] 编译验证：本机装 ISCC 后编译两个脚本通过，或由 release workflow 编译通过 —— **仍未完成**：本机无 ISCC、`ci.yml` 不编译安装包，只能等下次 `release.yml`（打 tag）首次编译确认

---

## 实施步骤

### 第 1 步：统一产品身份

- `installer-selfcontained.iss` 的 `AppId` 改为 `{F7A3C8E1-2D4B-4A9F-9C6E-8B5D7A3F2E1C}`（与 `installer.iss` 相同），删掉「必须用不同 AppId」的过时注释，改写成解释「同一产品两种打包」。
- 两个脚本的 `[Setup]` 段都加 `SetupMutex=MantisZipSetupMutex`，避免两个安装程序同时写同一目录。

### 第 2 步：迁移桥（存量机器自愈）

两个脚本的 `[Code]` 各加一段（逻辑完全一致）：

1. 常量 `LegacySelfContainedKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{963001F3-5748-4834-9489-97D6D00E3917}_is1'`。
2. `LegacyKeyExists`：依次查 `HKLM64` / `HKLM32` / `HKCU` 三处。
3. 键存在则：
   - 用 `cmd /c tasklist /FI "IMAGENAME eq MantisZip.UI.Avalonia.exe" /NH | find /I "…" >nul` 的退出码判断应用是否在跑（比写临时文件再解码更稳，不受控制台代码页影响）；在跑则 `MsgBox` 询问，同意后 `taskkill /F`，拒绝则中止。
   - 取 `UninstallString` 拆出 exe 与参数，追加 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`，`Exec(..., ewWaitUntilTerminated)`。
   - **轮询**旧键是否消失（500ms × 最多 60s，`WaitForLegacyKeyGone(60000)`）——不只看退出码，因为 Inno 卸载器会把自己复制到临时名后提前退出。
   - 成功则顺手清 `HKCU\Software\MantisZip` 的 `FirstRunShell`/`FirstRunAssoc`（避免旧标记压过本次安装的选择）。
   - 失败（键仍在）⇒ `PrepareToInstall` 返回 `CustomMessage('LegacyUninstallFailedMsg')` 中止安装。
4. 键不存在 ⇒ 直接返回 `''`（幂等，架好桥后可长期保留）。

### 第 3 步：加固

- WebSetup 脚本加 `[InstallDelete]`，清理自包含变体遗留的运行时文件（`hostfxr.dll`、`hostpolicy.dll`、`coreclr.dll`、`clrjit.dll`、`mscordaccore*.dll`、`mscordbi.dll`、`System.Private.CoreLib.dll`、`createdump.exe`、`unins*.exe|dat`）——只删这份**已知清单**，不碰用户自建文件。
- 卸载：`CurUninstallStepChanged(usUninstall)` 里询问「是否同时删除个人设置」，**默认否**（`MB_DEFBUTTON2`），静默卸载不问、不删；同意则 `DelTree({localappdata}\MantisZip)`。无论选什么都不留垃圾：始终 `RegDeleteKeyIncludingSubkeys(HKCU, 'Software\MantisZip')`。

### 第 4 步：可诊断

- `CurStepChanged(ssPostInstall)` 末尾写 `{app}\install-manifest.json`：`{"variant":"WebSetup|Offline","version":"0.5.4","installedAt":"…","appId":"{F7A3…}"}`。
- 同时 `RegWriteStringValue(HKLM64, 'Software\MantisZip', 'InstalledVariant'|'InstalledVersion', …)`。
- 以后同类报告让用户发这个文件即可定位，不必再靠猜。

### 第 5 步：跳过运行时的二次确认（WebSetup）

`NextButtonClick` 的 `IDNO` 分支改为：先弹 `CustomMessage('SkipRuntimeConfirmMsg')`（`MB_YESNO or MB_DEFBUTTON2`，默认「否」），确认才继续安装，否则回到 Ready 页。

### 第 6 步：验证

- 静态一致性：两个 `.iss` 的 AppId 相同、旧 AppId 常量存在、`begin`/`end` 配平、`[Code]` 段新增过程齐全。
- `pwsh -File scripts\verify-repo.ps1 -Task check`（版本 6 处 + 三语 key + 五条守卫）。
- 编译：装 ISCC 后 `ISCC /dMyAppVersion=0.5.4 installer.iss` 与 `installer-selfcontained.iss`；本机无 ISCC 时由 release workflow 编译，**不假装本地验证过**。

---

## 已定口径（用户 m00746）

1. WebSetup 的「跳过运行时」**保留**，但必须二次确认（不采用「直接删除该选项」）。
2. 卸载时**要**「同时删除个人设置」勾选（默认不勾）。
3. 版本号**沿用当前 0.5.4**，不升版（v0.5.3 / v0.5.4 均未打 tag、未发 Release，0.5.4 仍是开发中版本号）。

---

## 风险与回滚

| 风险 | 缓解 |
|---|---|
| 迁移桥静默卸载旧版时旧版卸载器删不掉被占用文件 → 登记重启后删除 → 重启把新文件删了 | 先检测并关闭应用；再轮询键消失；找不到旧 `UninstallString` 的 exe 时按「无残留」继续 |
| 旧卸载器退出码非 0 但实际已卸干净 | 判据不用退出码，用「旧注册表键是否消失」 |
| 存量机器上旧键存在但程序早已被手动删除 | `FileExists(Exe)` 为假时视为已清理，继续安装 |
| 统一 AppId 后两个变体不能再共存 | 有意为之：目录结构（同名/同目录/同 exe）下共存本来就是假的 |
| 本机无 ISCC，编译未验证 | 明确如实说明；交给 release workflow 或用户本机验证，不做假绿 |

**回滚**：本计划只改两个 `.iss` 与文档，不改产品代码；回滚即 `git revert` 单个提交，对已发版用户无影响（下一次安装回到「两个身份」的旧行为）。
