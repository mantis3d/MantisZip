# 插件生态与 .mp 发布者工具（Publisher）

> **状态**: 📋 待拍板 | **阶段**: [⬜⬜⬜⬜] (0/4)
> 本文件回答一个问题：**MantisZip 的插件生态由哪几块组成、现在各做到哪、Publisher 该放在哪**。
> 写完先不动手，等用户确认「关系与路线」后再分批实施。

---

## 1. 动机

压缩软件要长出生态，通常靠三样东西：**能装插件**、**有人做插件**、**插件能安全分发**。

MantisZip 目前只有第三样的一半：`.mp` 包格式已定义（v1），但主程序既不能装、也没有让第三方「自助产包」的工具。结果是：

- 第三方想给你做插件 → 无从下手（格式靠读文档、摘要靠手算，极易出错）；
- 你收了插件 → 无法一键安装、无法校验、无法展示详情页；
- 插件更新 → 没有版本与兼容性约束（`minHostVersion` 只是文档里的字）。

**目标**：让「作者产包 → 市场索引 → 用户装包 → 宿主运行」这条链路每一环都有确定的工具与校验，且尽量复用已有资产。

---

## 2. 角色与全景

```
┌──────────────┐   .mp（zip + manifest.json + files 摘要）
│  插件作者     │─────────────────────────────┐
│（第三方/你）   │                             ▼
└──────────────┘                    ┌──────────────────┐
        ▲                           │   市场索引        │
        │ 产包/校验                  │ GitHub Release   │
┌───────┴──────────┐                │ 自建索引(VPS)     │
│ Publisher 工具    │                │ PyPI(校验器)      │
│ GUI / CLI / MCP   │                └────────┬─────────┘
└──────────────────┘                         │ 列表 / 详情 / 下载
                                             ▼
                                    ┌──────────────────┐
                                    │  MantisZip 宿主   │
                                    │ 安装器+加载器+市场UI│
                                    └──────────────────┘
```

三个角色、三份职责，互不混：

| 角色 | 职责 | 当前状态 |
|---|---|---|
| **Publisher**（作者侧工装） | 把插件目录变成合格 `.mp`，本地校验 | 🟢 已实现（独立仓库，47+8 测试通过） |
| **市场索引**（服务端/静态） | 列出插件、给下载地址、给 sha256 | 🟡 只有文档里的「三处发布」设想，未建索引 |
| **MantisZip 宿主**（用户侧） | 安装 `.mp`、加载插件、展示市场、更新 | 🔴 0 实现（主仓目前没有任何插件代码） |

> ⚠️ 缺口在宿主：**没有安装器与加载器，Publisher 产出的包无处可去**。这也是本设计要先对齐关系的原因。

---

## 3. Publisher 现状盘点

仓库：`F:\Github\MantisZip-Publisher`（独立 git 仓库，3 次提交，MIT）

| 资产 | 位置 | 完成度 |
|---|---|---|
| `.mp` 规范 v1（字段/规则 M-01~M-10/安全约束） | `docs/mp-format.md` | ✅ 完整 |
| 清单模型 + SemVer + 校验器 + 打包器 | `src/MantisZip.Publisher.Core/` | ✅ 47 个 xUnit 测试 |
| Python 校验器（CI 免 .NET 可用） | `python/` | ✅ 8 个测试，与 C# 规则一致 |
| GUI 六步向导 | `src/MantisZip.Publisher.App/` | ✅ 可运行（截图见 `demo/screenshots/`） |
| CLI `mp pack/validate/inspect/prompts/mcp` | `src/MantisZip.Publisher.Cli/` | ✅ 自包含单文件 win-x64 实测 |
| MCP 服务（4 工具） | `McpServer.cs` | ✅ stdio 实测 |
| 文档 5 份 + 教程 + 第 2 轮演示记录 | `docs/` | ✅ |
| 三处发布真跑 | — | 🔴 需 GitHub 仓库名 / PyPI token / VPS |
| 接进 MantisZip | — | 🔴 未做 |

**不做的事**（明确划界）：Publisher 不是运行时、不加载插件、不连市场；它只负责「产包 + 校验」。

---

## 4. 与主仓的关系：三个方案

| | A. 保持独立仓库 | B. 核心并入主仓 `tools/`，GUI 独立 | C. 全部并入主仓，GUI 内建到宿主 |
|---|---|---|---|
| **形态** | 独立开源项目，宿主另做安装器 | Core/校验器进主仓（被宿主引用），Publisher GUI 仍在独立仓库 | 主仓里加一个「插件开发」页面，用户直接在应用里产包 |
| **维护成本** | 两份 UI（Publisher 向导 vs 宿主） | 一份核心逻辑，两处薄 UI | 一份 UI，随宿主发版 |
| **第三方门槛** | 要拉两个仓库 | 中：核心随主仓演进 | 低但重 |
| **生态入口** | 独立项目更像个"社区工具" | 推荐：核心是宿主的一部分，工具可独立分发 | 供应商把他们锁进宿主 UI |
| **风险** | 规范漂移（两仓各写一份校验） | 主仓 CI 变慢（多一堆测试）；需要把 Publisher 的 47 测试搬过来 | 宿主变胖；插件作者不一定会用宿主 GUI |

**推荐 B（折中）**：

1. 把 `MantisZip.Publisher.Core`（清单/校验/打包/SemVer）**并入主仓**，命名 `MantisZip.PluginKit`（`src/MantisZip.PluginKit/`），宿主安装器直接引用它校验 `.mp`——**单一事实源**，杜绝两份校验规则漂移；
2. Publisher 的 **GUI/CLI 留在独立仓库**（面向外部作者，开源友好），CI 里引用主仓的 PluginKit 包（NuGet 私有源或 submodule 二选一，见决策点）；
3. Python 校验器保留（CI/非 .NET 场景）。

---

## 5. 安全模型（决定能否开放上架）

| 层 | 机制 | 现状 |
|---|---|---|
| 包完整性 | `files` 逐条 sha256 + size，M-09 校验篡改 | ✅ 已实现 |
| 路径安全 | 拒绝 `../`、绝对路径、盘符（zip-slip） | ✅ 已实现 |
| 能力白名单 | 9 个 capability（archive.* / fs.* / ui.* / network.out / process.spawn） | ✅ 清单层校验 |
| 运行时隔离 | 插件在宿主进程内跑还是子进程跑？ | ❌ 未定（本设计待拍板） |
| 签名/信任 | 作者签名 + 市场白名单？ | ❌ 未定 |
| 自动更新 | 宿主检查 `version` + `minHostVersion` | ❌ 未定 |

**必须先定的原则建议**：

- 插件**不在宿主进程内直接 `Assembly.Load`**（否则一个坏 DLL 就能拿走全部权限）——建议独立子进程 + 能力授权（`network.out`/`process.spawn` 默认拒绝）；
- 上架审核 = 市场索引里白名单 + 包签名，签名密钥由你/市场方持有（Publisher 后续加 `mp sign`）。

---

## 6. 用户旅程

**作者 A（不用 AI）**：下 Publisher → 六步向导填元数据/README/截图 → 打包 → 校验通过 → 发 GitHub Release → 在 PR 里贴 `.mp` 链接 → 你审核后进索引。

**作者 B（用 AI）**：AI 客户端配 MCP → 「把我这个目录打成 .mp」→ AI 调 `mp_pack` → `mp_validate` → 报告问题 → 修完重跑。

**用户**：设置 → 插件 → 市场（列表/详情/能力说明）→ 装 → 宿主校验 sha256 + 能力 → 子进程加载 → 功能出现在面板/右键。

---

## 7. 分阶段路线（估时）

| 阶段 | 内容 | 工时 | 依赖 |
|---|---|---|---|
| **P1** 宿主装包闭环 | 主仓新增 `PluginKit`（校验+解包）+ 设置里的「安装 .mp」+ 已装列表/卸载 | 8-12h | 本设计拍板 |
| **P2** 市场最小闭环 | 索引 JSON 格式 + 宿主市场页（列表/详情/安装）+ 一个官方插件 demo | 12-16h | P1 |
| **P3** 运行时 | 插件子进程协议（能力授权、超时、日志）+ 1 个真插件示例 | 16-24h | P1 |
| **P4** 发布与信任 | Publisher 接主仓 PluginKit、GitHub/PyPI/VPS 真跑、签名 `mp sign`、自动更新 | 8-12h | P1-P3 |

---

## 8. 待你拍板的决策点

1. **关系**：按推荐 B（核心并入主仓 `MantisZip.PluginKit`，GUI/CLI 留独立仓库）？还是 A 全独立 / C 全并入？
2. **运行时**：插件跑在**独立子进程**（推荐，安全）还是宿主进程内（简单，危险）？
3. **市场索引**：先做**静态 JSON（GitHub 托管）** 还是直接上 VPS 服务？
4. **签发**：这版要不要签名？不要的话，插件能力只做「声明式 + 用户确认」。
5. **发布渠道**：三处（GitHub Release / PyPI / VPS）都做，还是先只做 GitHub Release？
6. **命名**：主仓新库叫 `MantisZip.PluginKit` 可以吗？

---

## 9. 现有资产索引（便于接手）

- 规范：`F:\Github\MantisZip-Publisher\docs\mp-format.md`
- 校验规则：`src/MantisZip.Publisher.Core/MpValidator.cs`（M-01~M-10）+ `MpPackage.ValidatePackage`
- Python 对等实现：`python/mantiszip_plugin_tools/validate.py`
- MCP 工具：`src/MantisZip.Publisher.Cli/McpServer.cs`（mp_validate / mp_inspect / mp_pack / mp_prompts）
- 教程：`docs/tutorial.md`；第 2 轮演示：`docs/rounds/round-2-ai-loop.md`
- 示例包：`demo/out/quick-zip-1.0.0.mp`
