# mantiszip-dsh-plugin

MantisZip 专用的 DeepSeek Harness（DSH）本地插件，给 agent 加三个**只读**维护工具：

| 工具 | 作用 | 对应仓库规则 |
| --- | --- | --- |
| `mantiszip_verify` | 跑 `dotnet build` / `dotnet test`，把 MSBuild 输出压成「文件:行:列 + 错误码 + 首条信息」 | 规则 12（改完必须构建验证） |
| `mantiszip_i18n` | 三语 key 集合对齐、`{0}` 占位符对齐、重复 key、空值 | 规则 13（三语文件 key 必须同步） |
| `mantiszip_version` | 版本号 6 处取值一致性（AppConstants / csproj / PLAN / PROGRESS / 两个 .iss） | Version bump checklist |

三个工具都**不会修改任何文件**；只读检查，改文件仍由 agent 走常规编辑流程。

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

之所以要写 `config.root`：DSH 宿主进程的工作目录不一定是仓库（本机实测是 profile 目录），而工具需要知道去哪个仓库跑构建。写入后无论从哪个目录启动都能命中；也可以随时在 patch 里改成别的仓库路径。

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

自检覆盖：模块导出契约（`name` / `inject` / `apply`）、JSON Schema 子集合法性、返回值与 `output.schema` 的字段对齐、三个工具在真实仓库上的运行结果。

## 沙箱注意

`mantiszip_verify` 需要启动 `dotnet` 并捕获输出，且 `dotnet build` 必须能写 `obj/`。

在 DSH 的 **workspace-write 沙箱**下，如果 shell 子进程拿不到子目录写权限（现象：`MSB4018 ... obj\...editorconfig is denied`），或 Node 子进程报 `spawn EPERM`，说明本次会话的沙箱没有覆盖构建目录。

实测：增量构建（`obj/` 已是最新、无需写文件）在沙箱内可以跑通；**clean / 首次构建会被挡**。此时要么把该会话切到「完全权限」，要么重启会话让沙箱重新授权。这是环境限制，不是插件缺陷。

同理，`node test/self-test.mjs` 的构建段需要的管道输出也被沙箱禁止，跑自检建议用 `--skip-build`，或开「完全权限」再跑完整版。

## 已知发现

`mantiszip_i18n` 首次运行就抓到一个仓库既有问题：

- `src/MantisZip.UI.Avalonia/Localization/strings.zh-TW.json` 第 480 与 482 行重复定义了 `Extract_ParallelDegree`（`平行解压…` 与 `並行解壓…`），JSON 解析时后者覆盖前者，前者是死条目。
- 现有测试 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 校验的是解析后的 key 集合，重复 key 会被折叠，所以测不出来。

修复方式：删掉第 480 行那条旧译文即可（不影响三语 key 数量）。

## 结构

```
tools/dsh-plugin-mantiszip/
├── index.mjs          # 插件本体（零依赖 ESM，导出 name / inject / apply）
├── package.json       # 包元数据 + dsh.bundle patch 声明
├── cordis.patch.yml   # 安装后自动插入的行（id: mantiszip-tools）
├── install.ps1        # 建联接 + 写 patch
├── uninstall.ps1      # 反向操作
├── test/self-test.mjs # 不依赖 DSH 运行时的自检
└── README.md
```
