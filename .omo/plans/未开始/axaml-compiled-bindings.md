# Work Plan: AXAML 编译期绑定（渐进式开启 `x:CompileBindings`）

> **Status**: 📋 待开始 | **阶段**: [⬜⬜⬜⬜⬜⬜⬜⬜] (0/8)
> **预估工时**: 🟡中 **10-14h**
> **档位**: **A 档 · v0.5.7**（2026-10-29 落到具体版本阶段；消「绑定写错却一切正常」这类静默失败，是规则 23 的兜底）
> **关联规则**: 规则 23（新写的绑定必须「接线错了就失败得响」）

## Problem

仓库现在几乎全部是**运行时反射绑定**：属性名写错、类型不匹配、`DataContext` 类型不对，Avalonia 都不会在构建期报错，只会在运行期表现为「没值」「红字校验文本」或「永远是默认值」。这类缺陷已经真实发生过多次：

- 2026-10-28：设置窗口 7 个 `TextBox` 直绑 `int`，用户敲字母 → 绑定转换失败 → 控件下方红字（修法见规则 22，但**根因之一是这类错误编译期看不出来**）。
- 2026-10-28：`languages.json` 大小写不匹配 → 反序列化「成功」但字段全空（规则的另一个面，见 `JsonDefaults`）。
- 死开关（`AppSettings` 存了值、有 UI、无消费者）同样属于「界面上一切正常、点了什么也没发生」。

实测现状（2026-10-28 统计，`src/MantisZip.UI.Avalonia` 下）：

| 指标 | 数量 |
|---|---|
| `.axaml` 文件总数 | **50** |
| 显式 `x:CompileBindings="True"` | **1** |
| 显式 `x:CompileBindings="False"` | **1** |
| 声明了 `x:DataType` | **13** |

即绝大多数文件连「编译器能校验绑定」的前提（`x:DataType`）都还没有。

## Objective

1. 在**不改变运行时行为**的前提下，把编译期绑定覆盖率从 1/50 提到「凡是能开的全开」。
2. 建立可执行的门禁，防止新文件回退到裸运行时绑定。
3. 把「确实必须运行时绑定」的位置登记成显式清单（带原因），而不是让它悄悄存在。

## 用户口径

1. **渐进式，不做大爆炸重写**：按文件推进，每批可独立构建、可独立回滚。
2. **行为零变化**：只加 `x:DataType` / `x:CompileBindings` 与必要的绑定改写，不动 UI 结构。
3. **每个文件改完必须实机验证**：编译期绑定会把「运行期静默」变成「构建期报错」，因此首批要挑**已有 `x:DataType` 的 13 个文件**（错误面最小）。
4. **不追求 100%**：`HelpDialog` 这类 `DataContext = this` 自绑定、以及表达力不足的位置保留运行时绑定，但必须写注释说明原因。

## 设计

### 一、分三批推进

| 批次 | 范围 | 关键动作 |
|---|---|---|
| **B1 试水** | 已有 `x:DataType` 的 13 个文件 | 逐个加 `x:CompileBindings="True"`，把报错改成显式绑定（`CompiledBinding` / `!` 空值断言）；先小后大 |
| **B2 补类型** | 结构简单、`DataContext` 类型明确的 `Controls/`、`Dialogs/` | 补 `x:DataType`（多数指向同名 VM 或 `x:DataType="vm:XxxViewModel"`），再开编译绑定 |
| **B3 收尾 + 门禁** | `Views/` 主力窗口、`App.axaml` 全局样式 | 同上；同步把守卫与规则写进脚本与 `AGENTS.md` |

### 二、四类难点与对策

| 难点 | 现象 | 对策 |
|---|---|---|
| 字典索引器 `{Binding LocalizedStrings[Key]}` | 编译绑定无法静态解析索引器 | 保持运行时绑定：该 `TextBlock` 上写 `{ReflectionBinding LocalizedStrings[Key]}`，或整文件保留 `x:CompileBindings="False"` 并注释原因（规则 13 已要求 key 登记，双保险） |
| `DataTemplate` 无类型 | 模板内绑定全在运行期解析 | 给 `DataTemplate` 加 `x:DataType`（`DataTemplate x:DataType="vm:RowViewModel"`）；`ItemsControl` 的 `ItemTemplate` 尤其要注意 |
| `$parent` / `RelativeSource` / `ElementName` | 编译期拿不到目标的类型 | 显式 `x:DataType` + `CompiledBinding` 的 `Source`，或用 `ReflectionBinding` 局部豁免并注释 |
| `DataContext = this`（code-behind 自绑定，如 `HelpDialog`） | 编译期无法得知自身类型 | 全文件 `x:CompileBindings="False"`（规则 13 已规定的写法），并在文件头注释「自绑定：属性由 code-behind 提供」 |

### 三、门禁（B3 落地）

`scripts/verify-repo.ps1 -Task guards` 增第 9 条 `compilebindings`：

- 新增 `.axaml`（`git diff` 判新增）必须带 `x:CompileBindings="True"` 或显式 `"False"`（配注释）；
- 声明了 `x:DataType` 的文件**禁止**再出现 `x:CompileBindings="False"`（除非落在脚本内的豁免表，带原因）；
- 统计型软告警（不失败）：打印「已开编译绑定 / 有 x:DataType / 总数」三项比值，用于观察推进率。

> 门禁采用「只增不减」策略：**不对存量 50 个文件一次性变红**，只约束新增与已迁移文件——否则本计划会自己阻塞其它所有人。

### 四、AGENTS.md 同步

规则 23 的「数据绑定」一行补一条可执行判据：新增/修改 `.axaml` 时，能拿到 `DataContext` 类型就必须声明 `x:DataType` 并开 `x:CompileBindings="True"`；豁免需注释原因并在守卫豁免表登记。

## 实施步骤

1. 清点 50 个 `.axaml`，按「有 `x:DataType` / 结构简单 / 复杂模板 / 自绑定」四类打标，产出迁移顺序表（写在本计划附录，随进度勾选）。
2. **B1**：13 个已有 `x:DataType` 的文件逐个开 `x:CompileBindings="True"`，修掉编译器报出的绑定错误；每 2-3 个文件跑一次 `dotnet build` + UI 全量测试。
3. **B2**：`Controls/`（含 `FileFilterEditor`、`ArchiveContentView`、`DynamicFormatOptionsPanel`）与 `Dialogs/` 补 `x:DataType` 后开启。
4. **B3**：`Views/MainWindow.axaml`（最大、绑定最多）与 `SettingsWindow.axaml` 分批推进；`App.axaml` 的全局 `Style` 里没有 `{Binding}`，只需确认无遗漏。
5. 写守卫 `Test-GuardCompileBindings` + 注册进 `Test-Guards`（第 9 条）+ 更新 `docs/MAINTENANCE.md` §4 表格。
6. 更新 `AGENTS.md` 规则 23；在 `docs/ARCHITECTURE.md` 绑定章节补一段「编译绑定 vs 运行时绑定」的取舍口径。
7. 跑 `-Task check` / `-Task build -Target avalonia` / UI 全量测试，并按规则 3 更新 `docs/PROGRESS.md` 与 `docs/progress-avalonia-detail.md`。
8. 提交（建议按批 3 个提交：B1 / B2+B3 / 守卫+文档），push `beta`，等 CI。

## 测试计划

- **行为回归**：UI 全量测试（当前 **557 例 / 3 Skip**）必须全绿——编译绑定若把某个绑定从「静默无值」变成「有值」，测试会暴露出来。
- **新增守卫测试**：`tests/MantisZip.UI.Avalonia.Tests/CompiledBindingGuardTests.cs` —— 源码守卫，断言 `src/` 下 `.axaml` 全量里 `x:CompileBindings="False"` 的位置与脚本豁免表一致。
- **人工抽查**：主窗口文件列表、设置窗口各页、预览面板、进度窗口、帮助弹窗各点一遍（无头测试不覆盖渲染细节）。

## 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 编译绑定报错量大，单文件改不完 | 迁移卡住 | 按文件推进，允许中途提交；单文件过大（`MainWindow.axaml`）可先局部用 `{ReflectionBinding}` 豁免 |
| `LocalizedStrings[Key]` 大量命中 | 迁移收益被豁免抵消 | 这批保留运行时绑定但已在规则 13 用「key 必须登记」兜住，属有意留白 |
| 批量改动引发 UI 回归 | 用户可见 | 每批跑全量测试 + 人工抽查；发现回归立刻回滚该批 |
| 守卫把存量文件一起判红 | 阻塞他人 | 门禁只约束「新增 / 已迁移」文件，存量只统计不判红 |

## Definition of Done

- [ ] B1 的 13 个文件全部开启 `x:CompileBindings="True"`，构建 0 错误
- [ ] B2/B3 完成，`x:CompileBindings="True"` 覆盖率 ≥ 80%（豁免文件带注释）
- [ ] `-Task guards` 第 9 条 `compilebindings` 落地，且存量不判红
- [ ] `AGENTS.md` 规则 23 与 `docs/MAINTENANCE.md` §4 同步
- [ ] `-Task check` / `-Task build -Target avalonia` / UI 全量测试（557 例）全绿
- [ ] 进度文档（PROGRESS + progress-avalonia-detail）已更新
- [ ] 提交并 push 到 `beta`，CI 绿
- [ ] 按规则 18 与用户确认 RELEASE_NOTES 范围

## 附录：迁移顺序表（B1，2026-10-28 统计）

> 待实施时逐项填写文件名与状态（`⬜ 未开始 / 🟨 进行中 / ✅ 已开启`）。13 个已声明 `x:DataType` 的文件从「绑定数量最少」到「最多」排序推进。
