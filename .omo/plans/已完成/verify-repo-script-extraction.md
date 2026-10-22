# 维护脚本下沉：`scripts/verify-repo.ps1` + DSH 插件薄壳

**Goal:** 把 `tools/dsh-plugin-mantiszip` 里三个只读工具（构建诊断 / 三语 key 对齐 / 版本号 6 处一致性）的实现逻辑下沉为仓库脚本 `scripts/verify-repo.ps1`，插件退化为只负责「起进程 + 解析 JSON + 渲染」的薄壳。此后 CI、其它 agent harness（opencode / Reasonix）、人类命令行共用同一份校验逻辑；换 harness 不丢能力。

**Architecture:** 脚本是唯一事实源（退出码 + `-Json` 机器可读输出）；DSH 插件只保留工具声明（JSON Schema）与 `render`，`execute` 改为 spawn `pwsh -File scripts/verify-repo.ps1 -Json`。

**Tech Stack:** PowerShell 7 / Node ESM（零依赖）/ GitHub Actions（后续消费方）

---

## 背景：为什么必须下沉

`tools/dsh-plugin-mantiszip/index.mjs`（739 行）把三件事的**实现**和 **DSH 工具声明**焊在一起：

- 只有 DSH 能调用 → 换 opencode / Reasonix / 人类命令行全部失去能力，只能退回肉眼看 MSBuild 输出
- CI 无法复用 → `.github/workflows/ci.yml` 目前不校验 i18n / 版本号，而这两个校验**抓到过真实问题**（`strings.zh-TW.json` 的 `Extract_ParallelDegree` 重复 key 就是插件首跑发现的）
- `AGENTS.md` 规则 12 / 13 / 版本清单把 `mantiszip_*` 写成强制要求 → 在非 DSH 环境下是「不存在的工具」，规则断档

## 目标与非目标

**目标**

1. `scripts/verify-repo.ps1` 承载全部逻辑，5 个 task：`build` / `test` / `i18n` / `version` / `check`（`check` = version + i18n，纯文件校验，秒级）
2. 输出契约与现有三个工具**逐字段一致**（插件返回值、`output.schema`、`render` 行为都不变）
3. 插件 `index.mjs` 只保留 schema / render / 进程编排
4. `AGENTS.md` 规则 12、规则 13、Version bump checklist 改为「脚本为通用路径，DSH 下等价快捷键」

**非目标（本期不做）**

- 不动 `.github/workflows/ci.yml`（另立条目：CI 消费方接线）
- 不做 opencode 兼容（用户明确暂不需要）
- 不新增 `hygiene` / `docs` / `installer` 等第 4 层新工具（脚本架构先落地）
- 不改任何业务代码、不升版本号

---

### Task 1: 脚本骨架与 root 解析

**Files:**
- Create: `scripts/verify-repo.ps1`

- [x] **Step 1: 参数面**

`-Task build|test|i18n|version|check`、`-Target avalonia|core|shell|tests|ui-tests|solution`、`-ExtraArgs`、`-TimeoutMs`（默认 600000）、`-Root`、`-LocalizationDir`、`-Json`。

- [x] **Step 2: root 解析**

`-Root` 显式传入 → 校验含 `MantisZip.sln`，否则报错；未传入 → `$PSScriptRoot` 的父目录（`scripts/` 的父），仍不含 `MantisZip.sln` 则从当前目录向上找 8 层。全部失败抛明确错误（沿用插件「已尝试过哪些路径」的风格）。

- [x] **Step 3: `#Requires -Version 7.0`**

仓库规则 9 已规定 Windows + PowerShell 7；不写 5.1 兼容分支。

### Task 2: build / test（行为对齐 `mantiszip_verify`）

**Files:**
- Modify: `scripts/verify-repo.ps1`

- [x] **Step 1: 子进程与环境**

`Start-Process dotnet -WorkingDirectory $root -NoNewWindow -PassThru -RedirectStandardOutput/Error <tempfile>`；环境强制英文 UI：`DOTNET_CLI_UI_LANGUAGE=en`、`VSLANG=1033`、`DOTNET_NOLOGO=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。

参数保持与原实现一致：`<action> <proj> -nologo -nodeReuse:false -v:m`，build 额外 `-clp:NoSummary`，再追加 `-ExtraArgs`。

- [x] **Step 2: 超时与截断**

`$proc.WaitForExit($TimeoutMs)` 返回 false → `timedOut=true`，`Kill($true)`，`exitCode=-1`；单流读取超过 256 KB 截断并置 `truncated`（与原 `MAX_RAW_BYTES` 一致）。

- [x] **Step 3: 诊断解析**

三条正则照搬：带文件 `file(line,col): error CODE: msg`、无文件 `error CODE: msg`、dotnet 裸 `error: msg`；末尾 `[proj.csproj]` 归属标记剥离；按 `file|line|column|code|message` 去重；error/warning 分列；每类最多 100 条。计数用**去重后全量**，列表取前 100。

- [x] **Step 4: 捕获文本解码**

重定向文件按严格 UTF-8 解码，失败回退 Latin1（绝不抛异常）。因为已强制英文 UI，中文路径乱码不影响诊断识别。

### Task 3: i18n（行为对齐 `mantiszip_i18n`）

**Files:**
- Modify: `scripts/verify-repo.ps1`

- [x] **Step 1: 三份文件读取**

`ConvertFrom-Json -AsHashtable`（保留插入顺序，等价 JS `Object.keys()`）；原始文本单独保留供重复 key 扫描。

- [x] **Step 2: 五项判定**

① key 集合：以 zh-CN 为基准，`missing`（en/zhTW 缺）/ `extra`（en/zhTW 多）；② `{0}` 占位符下标集合按升序比对；③ 重复 key：对原始文本跑 `(?m)^\s*"((?:[^"\\]|\\.)*)"\s*:`，第二次出现即记（**JSON 解析会折叠，只有扫原文才能发现**）；④ 空值：`Trim()` 后为空的字符串（仅提示，不计入 `issueCount`）；⑤ `issueCount` = missing + extra + 占位符不一致 + 重复 key（**不含空值**，与原实现一致）。

### Task 4: version（行为对齐 `mantiszip_version`）

**Files:**
- Modify: `scripts/verify-repo.ps1`

- [x] **Step 1: 六个来源**

`AppConstants.cs` / AValonia `.csproj` / `docs/PLAN.md` / `docs/PROGRESS.md` / `installer.iss` / `installer-selfcontained.iss`，正则与原实现一致；取值 `Trim()` 后剥掉尾部 `[.。,，]`。

- [x] **Step 2: 判定**

读不到文件 → `missingSources` 记 `key（读不到 file）`；正则不中 → 记 `key`；`ok = missingSources 为空 且 unique 只有一个`。

### Task 5: 输出与退出码

- [x] **Step 1: 两种输出**

默认人读（与现有 `render*` 文案一致，✅/❌ 开头 + 明细）；`-Json` 输出单文档 JSON（字段名与 `output.schema` 完全对齐）。`-Task check` 的人读输出为 version / i18n / guards 三段拼接，JSON 为 `{ok, checks:[{task, result:{...}}]}`（每项包一层 `result`，插件不消费 `check`）。

- [x] **Step 2: 退出码**

`ok` → 0；否则 1。CI 直接看退出码。

### Task 6: 插件改薄壳

**Files:**
- Modify: `tools/dsh-plugin-mantiszip/index.mjs`

- [x] **Step 1: 保留什么**

`TARGETS`、三个工具的 `parameters` / `output.schema` / `render*`、`resolveRoot`、`runProcess`（改跑 `pwsh`）。

- [x] **Step 2: 删掉什么**

MSBuild 正则与解析、i18n 判定、version 正则（全部下沉到脚本）。

- [x] **Step 3: 调用方式**

`pwsh -NoProfile -NoLogo -NonInteractive [-ExecutionPolicy Bypass（仅 win32）] -File <root>/scripts/verify-repo.ps1 -Json -Root <root> -Task <task> [-Target …] [-ExtraArgs …] [-TimeoutMs …]`；stdout 解析为 JSON 直接返回；解析失败抛错并附 stderr 尾部。

### Task 7: 文档与规则统一

**Files:**
- Modify: `AGENTS.md`（规则 12、规则 13、Version bump checklist）
- Modify: `tools/dsh-plugin-mantiszip/README.md`

- [x] **Step 1: AGENTS.md**

三处改为：通用路径 = `pwsh scripts\verify-repo.ps1 -Task …`，DSH 下等价于 `mantiszip_*`（保留原工具名作为快捷方式，不再写成唯一途径）。

- [x] **Step 2: 插件 README**

说明「实现已下沉，插件是薄壳」，补脚本用法与非 DSH 场景表格。

### Task 8: 验证

- [x] **Step 1: 三任务真跑**

`-Task version`（应 0.5.3 六处一致）、`-Task i18n`、`-Task check`。

- [x] **Step 2: 构建真跑**

`-Task build -Target avalonia` 必须 `ok=true`（规则 12）。

- [x] **Step 3: 自检**

`node tools/dsh-plugin-mantiszip/test/self-test.mjs`（含一次真实构建）全绿。

- [x] **Step 4: 负向验证**

临时构造版本号不一致 / 重复 key 的夹具，确认脚本能报出并返回退出码 1（避免「永远绿」的假门禁）。

---

## 风险与对策

| 风险 | 对策 |
|---|---|
| PowerShell 与 JS 行为细节不一致（去重、截断、计数） | Task 8 逐任务对照原输出；`issueCount` 不含空值等边界写进 Task 3 |
| 重定向输出编码 | 强制英文 UI + UTF-8 严格解码 + Latin1 兜底 |
| DSH 沙箱内 `dotnet` 写 `obj/` 被挡（插件 README 已记录） | 属环境限制，脚本错误信息保持原样；不改判定标准 |
| 插件调用链变长（多一次 pwsh 启动，约 100-300ms） | 相对 `dotnet build` 数十秒可忽略 |

## 后续（不在本期）

1. `.github/workflows/ci.yml` 增加 `pwsh scripts/verify-repo.ps1 -Task check`，并装 7-Zip 放开 `SevenZipEngine` 过滤
2. `-Task hygiene` / `-Task docs` / `-Task installer`（第 4 层工具）
3. opencode 侧接线（用户明确暂不需要）

---

## 完成记录（2026-10-21）

全部 Task 1–8 已实施并验证通过。与计划的偏差：

| 计划 | 实际 | 原因 |
|---|---|---|
| Task 2 Step 1 用 `Start-Process -PassThru -RedirectStandardOutput` | 用 `System.Diagnostics.Process` + `ProcessStartInfo.ArgumentList` | `Start-Process` 带重定向时 `$p.ExitCode` **恒为 `$null`**（`WaitForExit($ms)` / `WaitForExit()` / `Refresh()` 均取不到；实测 `cmd /c exit 3` 打印空），拿不到退出码就无法判定 `ok` |
| Task 5 `-Task check` 的 JSON 形状 `{ok, checks:[{task,ok,…}]}` | 改为 `{ok, checks:[{task, result:{…}}]}` | 后续加 `guards` 后每项结果本身是完整对象，平铺会与 `task` 字段抢名字；插件不消费 `check`，仅供 CI/人工 |
| Task 6 Step 3 命令行含 `-Root <root>` | 改为进程 `cwd = root`，不传 `-Root` | 少一处引号/空格转义风险；脚本 `Resolve-RepoRoot` 优先看 `$PSScriptRoot` 上级，天然命中仓库 |

验证方式：真实仓库 `-Task version`（0.5.3 六处一致）、`-Task i18n`（1276 key、0 问题）、`-Task check`（exit 0）、`-Task build -Target avalonia`（exit 0 / 0 错误 / 0 警告）；`node tools/dsh-plugin-mantiszip/test/self-test.mjs` 四段全绿（含 23.2s 真实构建）；负向夹具（版本号不一致 + 缺文件 + 缺/多/重复 key + 占位符错位）返回 exit 1 并逐条报出。

环境限制（非缺陷，已写入插件 README）：DSH workspace-write 沙箱下 `dotnet build` 写 `obj/` 报 `MSB3491 … Access to the path … is denied`，Node 起带管道的子进程报 `spawn EPERM`；脚本/插件在完全权限或 CI 上正常。
