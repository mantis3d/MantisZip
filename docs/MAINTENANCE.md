# MantisZip 工程化维护手册

> 本文件回答「这套门禁是谁、为什么这么写、要怎么改、改完怎么验证」。
> **规则**看 [AGENTS.md](../AGENTS.md)，**应用架构**看 [ARCHITECTURE.md](ARCHITECTURE.md)，**逐日变更史**看 [progress-avalonia-detail.md](progress-avalonia-detail.md)。
> 换机器 / 换 agent 接手时，先读这里，再读 AGENTS.md。

---

## 1. 一张图看清组成部分

```
              ┌──────────────────────────────────────────────┐
   agent ────▶│ DSH 插件 tools/dsh-plugin-mantiszip/index.mjs│  薄壳：校验参数→起进程→解析 JSON→渲染
              └───────────────────┬──────────────────────────┘
                                  │ pwsh -File … -Json
   人 / CI ───────────────────────▼──────────────────────────┐
                                  │                          │
                    ┌─────────────┴─────────────┐            │
                    │ scripts/verify-repo.ps1  │◀── 唯一实现 │
                    └─────────────┬─────────────┘            │
                                  │ dotnet build / test      │
                                  ▼                          │
                        MSBuild / 测试宿主 / 仓库文件          │
```

- **唯一实现**：`scripts/verify-repo.ps1`。所有校验逻辑只能写在这里，插件与 CI 都是调用方。
- **DSH 插件**：`tools/dsh-plugin-mantiszip/`，把脚本包成 `mantiszip_verify` / `mantiszip_i18n` / `mantiszip_version` / `mantiszip_guards` 四个工具。
- **CI**：`.github/workflows/ci.yml`，同样是调脚本（外加 NuGet 审计、分析器、build、test）。
- 结论：**换 harness 只丢插件层**（4 个工具名），脚本与 CI 不动；人类只需要 `pwsh`。

## 2. 我要做什么 → 跑哪条命令

| 场景 | 命令 |
|------|------|
| 提交前快速自检（不构建，毫秒级） | `pwsh -File scripts\verify-repo.ps1 -Task check` |
| 改完代码构建验证（规则 12） | `pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia` |
| 跑测试 | `pwsh -File scripts\verify-repo.ps1 -Task test -Target ui-tests` |
| 改了 UI 文案（规则 13） | `pwsh -File scripts\verify-repo.ps1 -Task i18n` |
| 发版改版本号（6 处） | `pwsh -File scripts\verify-repo.ps1 -Task version` |
| 改了快捷键 / 图标 / 文件选择器 | `pwsh -File scripts\verify-repo.ps1 -Task guards` |
| 要机器可读结果 | 任意命令追加 `-Json` |
| 改了脚本本身 | `node tools\dsh-plugin-mantiszip\test\self-test.mjs` |

退出码 **0 = 通过，1 = 有问题**，CI 直接消费。

## 3. `scripts/verify-repo.ps1` 结构

约 1300 行，分七段（每段以 `# ---` 横幅分隔）：

| 段 | 内容 |
|----|------|
| 参数与常量 | `-Task` / `-Target` / `-ExtraArgs(Json)` / `-TimeoutMs` / `-Root` / `-LocalizationDir` / `-Json`；`$TARGETS` 6 个别名、`$I18N_FILES`、`$VERSION_SOURCES` 6 处、诊断正则 |
| 通用工具 | `Clip-Text`、`Format-Seconds`、`Resolve-RepoRoot`、`Read-TextFile`、`Read-AsyncText` |
| build / test | `ConvertFrom-BuildOutput`（解析诊断）、`Get-ConfigurationFromArgs`、`Get-TestAssemblyPath`、`Invoke-VerifyDotnet`（起子进程）、`Format-BuildResult` |
| i18n | `Get-PlaceholderIndexes`、`Test-I18n`、`Format-I18nResult` |
| version | `Test-VersionConsistency`、`Format-VersionResult` |
| guards | `Test-GuardKeyBindings` / `Test-GuardI18nBindings` / `Test-GuardFilePicker` / `Test-GuardIcons`、`Test-Guards`、`Format-GuardsResult` |
| 入口 | `switch ($Task)`，每支 `exit ([int](-not $ok))` |

`-Task check` = `version` + `i18n` + `guards`，纯文件校验，不构建。JSON 形状是
`{ ok, checks: [ { task, result: {…} } ] }` —— 每项结果是完整对象，不平铺（避免与 `task` 字段抢名字）。

## 4. 五条规则守卫（`-Task guards`）

把 AGENTS.md 里本可机器校验的人工自查变成门禁：

| 守卫 | 规则 | 校验内容 |
|------|------|---------|
| keybindings | 10.1 | `MainWindow.axaml` 的 `InputGesture` ↔ `MainWindow.axaml.cs` 的 `RegisterKeyBindings` ↔ `MainWindowKeyBindingsTests` 期望清单，三方一致（`Ctrl+,` 与 `Ctrl+OemComma` 归一化） |
| i18n-bindings | 13 | XAML `LocalizedStrings[Key]` 的 key 必须登记在 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组，且该 key 必须存在于三语 JSON（**双向**） |
| filepicker | 16 | `OpenFilePickerAsync` / `SaveFilePickerAsync` / `OpenFolderPickerAsync` 只允许命中 `CustomFilePickerDialog.SystemBrowse_Click` |
| icons | 8 | `AppIcons.axaml` 的 `x:Key` 必须在 `IconTestViewModel.LoadAllIcons()` 有对应 `Add()` |
| dialog-seam | 20 | ① `new (Compress\|Extract)SettingsWindow(` 的文件与处数必须与 `$DIALOG_SEAM_REGISTRY` 一致（**正反双向**：没登记即失败，登记了却找不到也失败）② 非注释行出现 `CloseAction` / `DialogResult` 即失败 ③ `new (Compress\|Extract)Request` 只允许在 `src/MantisZip.UI.Avalonia/Services/`、`App.CliCompress.cs`、`App.CliExtract.cs` ④ 两个设置窗口构造函数必须含 `ICompressSession` / `IExtractSession` 形参 |

**已知覆盖边界**（别误以为全查了）：

- filepicker 守卫只认上述 3 个 API，规则 16 还禁 `FilePickerFileType` / `IStorageFile` / `TryGetLocalPath()`，这些目前靠人工评审。
- i18n-bindings 只比 `MainWindowViewModel` 的字典（它不是全量加载 JSON），各对话框/面板自持字典的 key 不在范围内。
- dialog-seam 是**文本级**检查：注释按行首 `//` 或 ` *` 放行（追述历史写法可以，真用不行）；`CloseAction` 出现在字符串字面量里会被判失败；它查不出「确认了但载荷少一个字段」这种语义缺口（那要靠载荷测试，见 `ExtractSettingsSeamTests`）。
- dialog-seam 的白名单把 CLI 直通路径（`--compress-quick/-separate/-combined`）算作合法请求构造点：那三条**没有确认对话框**，本来就该直接驱动执行器；改它们时要同步改 `$DIALOG_SEAM_REQUEST_ALLOWED` 与 AGENTS.md 规则 20。
- guards 是文本级扫描，**跨文件重命名、条件编译、多行拼接**都可能漏。

## 5. CI 门禁（`.github/workflows/ci.yml`，共 7 步）

触发：`push` / `pull_request` 到 **`main`、`alpha`、`beta`** 三条分支。
分支角色（2026-10-22 明确）：`alpha` = **新功能分支**（日常新功能开发）；`beta` = **调试除错分支**（缺陷复现 / 修错 / 验证）；`main` = **发版分支**（只在发版合并时前进）。三条都要过门禁 —— 改 CI 配置时记得与 `AGENTS.md`「分支角色」一节同步。

| 步骤 | 防什么 |
|------|--------|
| `-Task version` | 版本号 6 处漂移（AppConstants / csproj / PLAN / PROGRESS / 两个 .iss） |
| `-Task i18n` | 三语 key 缺失/多余/占位符不一致/重复 key |
| `-Task guards` | 上面五条规则 |
| `dotnet restore -p:WarningsAsErrors=NU1901%3B…` | 已知漏洞依赖（`Directory.Build.props` 开 `NuGetAuditMode=all`，本地只告警） |
| `dotnet format … analyzers --verify-no-changes --severity warn --include src\…` | 产品代码分析器告警（**只圈 `src/`**，tests 存量 xUnit 告警另议） |
| `dotnet build --no-restore -c Release` + `dotnet test …` | 真正编译与用例 |

**已知缺口**：test 带 `--filter "FullyQualifiedName!~SevenZipEngine"`，29 条 7z 用例在 CI 里永不运行（CI 没装 7-Zip）。要补的话：加 choco/7-Zip 安装步骤再删过滤器。

### 5.1 CI 红了怎么判（2026-10-27 定）

**先看栈，再决定「改代码」还是「重跑」。** 拉失败作业日志：

```powershell
# PAT 在 GCM 凭据里（注意必须带 username，`git credential fill` 不带时返回空）
$cred = ("protocol=https`nhost=github.com`nusername=mantis3d`n`n" | git credential-manager get)
$env:GH_TOKEN = (($cred | Select-String '^password=') -replace '^password=','')
$env:LocalAppData = 'F:\Github\MantisZip\.buildout\ghcache'   # 必须：否则 gh 写 run-log 缓存目录被拒
gh run list --repo mantis3d/MantisZip --branch alpha --limit 5
gh run view <run-id> --repo mantis3d/MantisZip --log-failed *> .buildout\logs\ci.log
Get-Content .buildout\logs\ci.log | Select-String '  Failed MantisZip|Test Run Failed|Total tests:'
```

日志行前缀是 `build` + `UNKNOWN STEP` + ISO 时间戳，用 `-replace '^build\tUNKNOWN STEP\t\S+Z '` 剥掉即成人话。作业页详情与 annotations **不含**用例名（只有构建告警），必须走上面这条日志路径。

> 顺带（本沙箱专属，非仓库问题）：`git push` 会因 GCM 的 prompt 脚本起 `sh.exe` 失败而崩成 `couldn't create signal pipe, Win32 error 5` → `fatal: could not read Username for 'https://github.com'`。绕过写法：取到 PAT 后用一次性 header 推 ——
> `git -c "http.https://github.com/.extraheader=AUTHORIZATION: basic <base64('x-access-token:'+PAT)>" push origin <branch>`（base64 由 `[Convert]::ToBase64String` 现算，PAT 不落盘、不写进仓库配置）。

已知三类失败，判据不同，处置也不同：

| 类型 | 判据 | 处置 |
|------|------|------|
| **A. 用例顺序依赖（已根治，防复发）** | 断言失败，`Actual` 里出现 `'gbk' is not a supported encoding name` | 结构上已不可能复发：`ZipEngine` 静态构造器自注册 GBK（`src/MantisZip.Core/Engines/ZipEngine.Open.cs` 类顶部）+ 两个测试程序集各一个 `TestAssemblySetup.cs`（`[ModuleInitializer]`）。**新增任何走 `Encoding.GetEncoding("gbk"/936)` 的 Core 路径，要么复用已注册的类，要么在该类静态构造器里注册**，不要再依赖「谁先跑谁注册」 |
| **B. Avalonia 无头测试竞态（上游缺陷，决定「不动」）** | `[Test Case Cleanup Failure]` + `InvalidOperationException: The calling thread cannot access this object`，栈在 `AvaloniaHeadlessPlatform.Initialize → AppBuilder.SetupUnsafe → HeadlessUnitTestSession.EnsureIsolatedApplication` | **重跑**：`gh run rerun <run-id> --failed`（不要用空提交触发重跑，会污染提交历史）。受害用例是随机的（2026-10-26 挑中 `ResultTreeViewFilterTests.TogglingCompactMode_KeepsFilterApplied`），别顺着用例名去改被测代码 |
| **C. 取消类用例靠墙钟竞速（已根治，防复发）** | 断言失败，`Assert.ThrowsAny() Failure: No exception was thrown`，且被测用例里用 `CancelAfter(<毫秒>)` 或 `Thread.Sleep` 来制造「取消 / 超时」 | 已改成事件驱动（`tests/MantisZip.Tests/Engines/ParallelExtractTests.cs`：等首个条目 `Completed` 再取消，另加一条「token 预先 Cancel」兜底）。**新增取消类用例不要用 `CancelAfter` / `Sleep` 抢时间窗**：Windows 定时器精度约 15 ms，快机器上任务可能先跑完再收到取消请求；触发点必须由被测代码的执行进度决定 |

- B 的上游：`AvaloniaUI/Avalonia#22021`（session 重建期 `s_uiThread` 认领竞态，仍 open）、`#21467`（testhost 死锁）。本仓库 `tests/MantisZip.UI.Avalonia.Tests/TestParallelization.cs` 已有 `DisableTestParallelization = true` 仍会触发，测试内也无自有后台线程 —— 属 session 重建内部竞态。
- **重跑后还红在同一处**（尤其是断言失败）→ 才当回归查。
- **一次红里挂多条**、其中一条是 B 的栈 → 整批可能是连带污染，先整批重跑，别逐条查。
- **频率基线**：2026-10 约 5 次 run 出现 1 次。若变成每 2~3 次必红，改走 ② 等上游补丁后升级 Avalonia（`Avalonia.Headless.XUnit` 须与 UI 项目主版本一致），或 ③ 评估 `AvaloniaTestIsolationLevel.PerAssembly`（整程序集共用一个 `Application`，会漏状态、有假绿风险，需单独评审）。
- **新增 UI 测试不要扩大 B 的窗口**：`Task.Run` / `async void` / `DispatcherTimer` / `ConfigureAwait(false)` / 从后台线程碰 `Dispatcher.UIThread`，都会让竞态更容易命中。
- **发版前必须真绿**：`main` / `alpha` 都没有分支保护（无 required status checks），红 CI 拦不住 push 也拦不住打 tag —— 「红着发版」全靠自觉，发版前确认目标 commit 结论为 success。

## 6. DSH 插件（`tools/dsh-plugin-mantiszip/`）

```powershell
pwsh -File tools\dsh-plugin-mantiszip\install.ps1              # 装到 desktop profile
pwsh -File tools\dsh-plugin-mantiszip\install.ps1 -Profile web # 或指定 profile
pwsh -File tools\dsh-plugin-mantiszip\uninstall.ps1            # 卸载（只移除自己的联接与 patch 段）
node tools\dsh-plugin-mantiszip\test\self-test.mjs             # 五段自检（含一次真实构建）
node tools\dsh-plugin-mantiszip\test\self-test.mjs --skip-build
```

- **改校验逻辑**：只改 `scripts/verify-repo.ps1`，插件不用动。
- **改工具声明**（参数、schema、渲染文案）：改 `index.mjs`，**装完/改完要完全退出并重开 DSH**（profile 只在启动时组合；`cordis.patch.yml` 没变就不用重装）。
- 新增工具时记得同步 `test/self-test.mjs` 里「注册了 N 个工具」的断言——它硬编码数量，加工具不更新会直接挂（2026-10-22 踩过）。
- 插件是零依赖纯 ESM（理由见插件 README），工具定义只能写 dsh-tools 接受的 JSON Schema 子集。

## 7. 反直觉实现（改动前务必先读）

这几处写法**看起来多余，删掉就会复发**，注释也写在代码里：

1. **子进程用 `System.Diagnostics.Process`，不用 `Start-Process`**：`Start-Process -PassThru -RedirectStandardOutput/-RedirectStandardError` 时 `$p.ExitCode` **恒为 `$null`**（`WaitForExit($ms)` / `WaitForExit()` / `Refresh()` / `Get-Process` 都取不到；实测 `cmd /c exit 3` 打印空）。没有退出码就无法判定 `ok`，所以走 `ProcessStartInfo` + `ArgumentList` + 异步读流 + `WaitForExit($BudgetMs)`。
2. **MTP 兜底按配置取程序集**：`Get-TestAssemblyPath -Configuration` 只认 `bin/<配置>/**`，找不到返回 `$null` 让调用方先构建。否则 `-c Release` 时会拿旧 Debug 产物去 `dotnet exec`，测试照样「通过」，测的却是旧二进制。
3. **`target` 字段显式传参（`-TargetName`）**：旧实现靠 PowerShell 动态作用域捕获脚本级 `$Target`，函数一挪或加同名 `param` 就静默变空串。
4. **MTP 兜底不追加任何 dotnet/MSBuild 参数**：`dotnet exec <assembly>` 之后的参数交给测试程序集自己解析，`-nologo` / `-c Release` 会被 xunit 当未知选项。
5. **兜底的触发条件必须三条同时成立**：失败 + 0 条编译诊断 + 输出里没有 `Passed!`/`Failed!`/`Total: N` 汇总行。少任何一条都会掩盖真实失败（编译错误或用例失败不触发）。

## 8. 环境坑（跨机器复现过的）

| 现象 | 原因 / 解法 |
|------|------------|
| `dotnet restore` 报「无法获取 NuGetScratch 锁定文件访问权限 → 无法对 NuGet.Config 进行操作」 | 不是 ACL，是 IDE/MSBuild 常驻节点持锁：`dotnet build-server shutdown` 后立刻恢复。`NUGET_SCRATCH` 对 SDK 10.0.400 无效 |
| `dotnet test` 抛 `Win32Exception (5): 拒绝访问`（`DefaultEngineInvoker.SetParentProcessExitCallback`） | VSTest testhost 打不开父进程句柄 → 走 `-Task test` 的 MTP 兜底（`dotnet exec`）。**不是沙箱专属，用户本机同样复现** |
| CI 里写 `WarningsAsErrors=NU1901;NU1902` 报 `MSB1006: 属性无效` | 分号会被 MSBuild 当多开关，必须写 `%3B` |
| `MSB4018/MSB3491 ... Access to the path 'obj\…' is denied` | DSH workspace-write 沙箱不给子目录写权限，只影响当前 shell；用 `danger-full-access` 或让用户提权 |
| node 子进程 `spawn EPERM` | 沙箱禁止具名管道；同样只在受限沙箱里出现 |
| git 报 `LF will be replaced by CRLF` | 仓库统一 CRLF：索引 LF / 工作区 CRLF（`.gitattributes` + `.editorconfig`）。新增二进制记得加进 `.gitattributes` 的 `binary` 组 |
| 三份 `strings.*.json` 带 BOM | 与 `.editorconfig` 的 `[*.json] charset = utf-8-bom` 一致，**不要**改成无 BOM（规则 13 已在 2026-10-22 改为「UTF-8 with BOM」，此前写的「无 BOM」是过期表述） |
| 改了 `installer*.iss` 本机却没法验证 | 本机没有 ISCC（Inno Setup 编译器），`ci.yml` 也**不编译**安装包——只有 `release.yml` 打 tag 时才 `choco install innosetup` 后编译。所以 `.iss` 的语法/API 错误只能等首次发版暴露；改完至少要人工核对结构（`[Code]` 的 `begin`/`end` 计数、`[CustomMessages]` 中英成对、两个脚本的 AppId 保持一致），必要时在 beta 上推一个测试 tag 验证 |

## 9. 还没做的（有意留白，不是忘了）

- **7-Zip 用例**：CI 装 7-Zip 后去掉 `SevenZipEngine` 过滤。
- **whitespace / style 门禁**：存量约 704 处 / 90 文件（单行 XML 注释被要求拆行）、40 处 IMPORTS 顺序，需要一次性全仓重排的独立提交。
- **覆盖率**：VSTest 走 `--collect` 不通，MTP 要给测试项目加 `Microsoft.Testing.Extensions.CodeCoverage`，涉及测试依赖决策。
- **Dependabot**：`NuGet` + `github-actions` 两条生态。
- **ISCC 干跑**：PR 上编译安装脚本，防 `installer.iss` 语法错误到发版才暴露。

## 10. 相关文件

| 文件 | 作用 |
|------|------|
| [AGENTS.md](../AGENTS.md) | 每次 session 必读的硬性规则与速查 |
| [scripts/verify-repo.ps1](../scripts/verify-repo.ps1) | 自检逻辑唯一实现 |
| [tools/dsh-plugin-mantiszip/README.md](../tools/dsh-plugin-mantiszip/README.md) | 插件安装、工具 schema、沙箱限制 |
| [.github/workflows/ci.yml](../.github/workflows/ci.yml) | CI 门禁 |
| [.editorconfig](../.editorconfig) / [.gitattributes](../.gitattributes) / [Directory.Build.props](../Directory.Build.props) | 风格基线 / 行尾 / NuGet 审计 |
| [progress-avalonia-detail.md](progress-avalonia-detail.md) | 逐日变更与当时的排障记录（找「为什么」的第二落点） |
| [KNOWN_ISSUES.md](KNOWN_ISSUES.md) | 已知未修问题登记簿（规则 21）：等上游 / 等计划 / 缺决策的条目与复评条件；发版前扫一遍 |
