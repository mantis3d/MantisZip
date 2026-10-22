# mantiszip-dsh-plugin

MantisZip 专用的 DeepSeek Harness（DSH）本地插件，给 agent 加四个**只读**维护工具：

| 工具 | 作用 | 对应仓库规则 |
| --- | --- | --- |
| `mantiszip_verify` | 跑 `dotnet build` / `dotnet test`，把 MSBuild 输出压成「文件:行:列 + 错误码 + 首条信息」 | 规则 12（改完必须构建验证） |
| `mantiszip_i18n` | 三语 key 集合对齐、`{0}` 占位符对齐、重复 key、空值 | 规则 13（三语文件 key 必须同步） |
| `mantiszip_version` | 版本号 6 处取值一致性（AppConstants / csproj / PLAN / PROGRESS / 两个 .iss） | Version bump checklist |
| `mantiszip_guards` | 代码规则守卫：快捷键登记（10.1）/ XAML 本地化 key 登记（13）/ 禁用原生 StorageProvider（16）/ 图标注册（8） | 规则 8、10.1、13、16 |

四个工具都**不会修改任何文件**；只读检查，改文件仍由 agent 走常规编辑流程。

## 实现分层：薄壳 + 仓库脚本

检查逻辑**不在插件里**，而在仓库脚本 `scripts/verify-repo.ps1`：

```
agent / CI / 人
      │
      ├── mantiszip_verify  ─┐
      ├── mantiszip_i18n    ─┤
      ├── mantiszip_version ─┼─→ index.mjs（薄壳：校验参数 → 起 pwsh → 解析 JSON → 渲染）
      └── mantiszip_guards  ─┘              │
                                            ▼
                              scripts/verify-repo.ps1（唯一实现）
```

- 插件只做三件事：参数校验、`pwsh -File scripts/verify-repo.ps1 -Task <task> -Json`、把脚本吐出的 JSON 原样返回（**输出契约逐字段不变**）。
- 换 harness（opencode / Reasonix / 纯 CI）不会丢功能：脚本本身就是可执行入口。
- 修 bug 只需改脚本一处，插件不会与脚本行为漂移。

## 不经 DSH 直接跑

```powershell
# 纯文件检查（版本号 + 三语 key + 规则守卫），无构建，毫秒级
pwsh -File scripts\verify-repo.ps1 -Task check

# 构建诊断（人名可直接读，不必看 MSBuild 原始输出）
pwsh -File scripts\verify-repo.ps1 -Task build -Target avalonia
pwsh -File scripts\verify-repo.ps1 -Task test  -Target ui-tests

# 单项检查
pwsh -File scripts\verify-repo.ps1 -Task guards
pwsh -File scripts\verify-repo.ps1 -Task i18n

# 机器可读
pwsh -File scripts\verify-repo.ps1 -Task check -Json
```

- `-Target`：`avalonia`（默认）/ `core` / `shell` / `tests` / `ui-tests` / `solution`
- `-ExtraArgs`：追加给 dotnet 的原始参数，例如 `-ExtraArgs --no-restore`；程序化调用（参数里可能带逗号或空格）改用 `-ExtraArgsJson '["-c","Release"]'`
- `-TimeoutMs`：dotnet 子进程预算，超时即杀进程树并报 `timedOut`
- `-Root`：仓库根；省略时先看脚本自身所在目录的上级（`scripts/..`），再向上找 `MantisZip.sln`
- **退出码 0 = 通过，1 = 有问题** ⇒ CI 可以直接 `run: pwsh -File scripts/verify-repo.ps1 -Task check`
- `-Task test` 有 MTP 兜底：`dotnet test` 失败且「0 条编译诊断 + 输出里没有测试汇总行」时（= 用例一条都没跑起来），自动 `dotnet exec <测试程序集>` 直跑 xunit v3 的 MTP 入口；传了 `-c Release` 时只认该配置的 bin 产物，不会退到旧 Debug 程序集。用例真失败或编译报错不触发兜底

## 为什么是「零依赖纯 ESM」

插件只 `import` `node:` 内置模块，不依赖 `@deepseek-ai/dsh-tools` 等包，也不需要构建步骤。原因是插件从 profile 目录加载时，Node 对裸包名的解析不一定落在 profile 的 `node_modules` 上；零依赖可以彻底绕开这个问题，同时避免 `pnpm install` 和构建脚本带来的信任面。

代价是工具定义必须直接写成 dsh-tools 接受的 JSON Schema 子集（`type` / `properties` / `required` / `items` / `enum` 等），而不是用 `defineTool` 的简写。`test/self-test.mjs` 会校验这一点。

## 安装

```powershell
# 默认装到 desktop profile
pwsh -File tools\dsh-plugin-mantiszip\install.ps1

# 或指定 profile
pwsh -File tools\dsh-plugin-mantiszip\install.ps1 -Profile web
```

脚本做两件事（幂等）：

1. 在 `%USERPROFILE%\.dsh\profiles\<profile>\node_modules\mantiszip-dsh-plugin`（以及共享的 `profiles\node_modules`）建目录联接，指向本目录；
2. 在 `<profile>\cordis.patch.yml` 末尾追加带起止标记的 `insert` 段，其中 `config.root` 写入本仓库的绝对路径。

之所以要写 `config.root`：DSH 宿主进程的工作目录不一定是仓库（本机实测是 profile 目录），而工具需要知道去哪个仓库跑构建、找 `scripts/verify-repo.ps1`。写入后无论从哪个目录启动都能命中；也可以随时在 patch 里改成别的仓库路径。

**装完必须完全退出并重新打开 DeepSeek Harness**，profile 只在启动时组合。

卸载：

```powershell
pwsh -File tools\dsh-plugin-mantiszip\uninstall.ps1
```

卸载只移除自己写入的联接和 patch 段，不碰其他插件。

## 自检

```powershell
# 含一次真实 dotnet build（约 10-30 秒）
node tools\dsh-plugin-mantiszip\test\self-test.mjs

# 跳过构建，只校验模块契约、schema、i18n、版本号
node tools\dsh-plugin-mantiszip\test\self-test.mjs --skip-build
```

自检覆盖 5 段：模块导出契约（`name` / `inject` / `apply`）、JSON Schema 子集合法性、返回值与 `output.schema` 的字段对齐，以及 `version` / `i18n` / `guards` / `verify` 四个工具在真实仓库上的运行结果（构建段实际走「插件 → pwsh → 脚本 → dotnet」全链路）。

## 沙箱注意

`mantiszip_verify` 需要启动 `pwsh`/`dotnet` 并捕获输出，且 `dotnet build` 必须能写 `obj/`。

在 DSH 的 **workspace-write 沙箱**下，可能遇到两种环境限制（与插件本身无关）：

- shell 子进程拿不到子目录写权限（现象：`MSB4018 ... obj\... is denied`、`MSB3491 ... Access to the path ... is denied`）⇒ clean / 首次构建被挡；
- Node 子进程被禁止开管道（现象：`spawn EPERM`）⇒ 插件与 `self-test.mjs` 的构建段都跑不起来。

两者都是「本次会话的沙箱没有覆盖构建目录 / 管道」导致的。绕过方式：

- 改用脚本（同样受沙箱限制，但报错点更清晰）：`pwsh -File scripts\verify-repo.ps1 -Task check`（纯文件检查，不受影响）；
- 或把该会话切到「完全权限」/ 重启会话让沙箱重新授权，再跑完整自检。

## 已知发现

`mantiszip_i18n` 首次运行就抓到过一个仓库既有问题：`strings.zh-TW.json` 里 `Extract_ParallelDegree` 被重复定义（JSON 解析时后者覆盖前者，前者是死条目），而 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验的是解析后的 key 集合，重复 key 会被折叠、测不出来。**该问题已修复**；重复 key 检测保留在脚本里作为回归护栏。

## 结构

```
tools/dsh-plugin-mantiszip/
├── index.mjs          # 薄壳：校验参数 → 起 pwsh → 解析 JSON → 渲染
├── package.json       # 包元数据 + dsh.bundle patch 声明
├── cordis.patch.yml   # 安装后自动插入的行（id: mantiszip-tools）
├── install.ps1        # 建联接 + 写 patch
├── uninstall.ps1      # 反向操作
├── test/self-test.mjs # 不依赖 DSH 运行时的自检
└── README.md

scripts/
└── verify-repo.ps1    # 唯一实现：build / test / i18n / version / guards / check（-Json 供插件调用）
```
