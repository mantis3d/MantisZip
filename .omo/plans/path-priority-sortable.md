# 可排序的默认路径优先级（含手动路径）— CustomFilePickerDialog 初始路径

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 Avalonia 的 `CustomFilePickerDialog` 增加「用户可排序的默认路径优先级链」——文件选择器打开时的初始路径不再固定为桌面，而是按用户设定的顺序逐个尝试多个候选路径来源，第一个可用（存在）的路径作为初始目录；并在设置中提供「手动路径」项让用户填入固定路径参与排序。

**决策记录（2026-08-02 与用户讨论确认）：**
1. **可排序链**：用户可在设置中自由排序候选路径来源（↑↓ 按钮移动），运行时按顺序探测，第一个存在的路径为初始路径
2. **排序交互**：↑↓ 按钮移动（非拖拽，与项目现有对话框按钮风格一致）
3. **应用范围**：仅 `CustomFilePickerDialog` 初始路径
4. **存储**：`List<string>`（顺序 = 优先级），默认 `["context", "explorer", "recent", "custom"]`
5. **桌面始终兜底**：桌面固定为链尾，不可移动、不可删除，全链不可用时强制桌面
6. **手动路径**：设置中内嵌 TextBox 填入固定路径（如 `d:\zip`），留空则该行跳过；对应 `custom` 项 + `CustomDefaultPath` 字段
7. **场景路径现状**：PickItems 入口不传上下文（传 null → 该项跳过）；ExtractFolder 传 `ViewModel.DestinationPath`。保持现状

**Why 可排序而非 WPF 四档预设：** WPF 版 `DefaultPathPriority` 只有 4 个固定档位（context/explorer/recent/desktop），每档是写死的优先级链，用户只能选档不能自定义顺序。可排序链让用户精确控制探测顺序，且可插入手动路径。

**Tech Stack:** .NET 10, Avalonia 12.x（无新增依赖）

**新增依赖：** 无（`ExplorerWindowTracker` / `PathHistoryManager` 均在 Core，已引用）

---

## 文件映射

| 文件 | 操作 | 职责 |
|------|------|------|
| `Models/AppSettings.cs` | 修改 | 新增 `DefaultPathOrder`（`List<string>`，默认 `["context","explorer","recent","custom"]`）+ `CustomDefaultPath`（`string`，默认 `""`） |
| `Dialogs/CustomFilePickerDialog.axaml.cs` | 修改 | `ResolveInitialPath` 改为按 `DefaultPathOrder` + 桌面兜底探测；依赖 `AppSettings`（Avalonia 版） |
| `ViewModels/SettingsWindowViewModel.cs` | 修改 | 新增路径优先级项集合（ObservableCollection）+ 上移/下移命令 + 手动路径属性 + 加载/保存 |
| `Views/SettingsWindow.axaml` | 修改 | 「高级」Tab（L1076 区域）新增「默认路径优先级」分组：ItemsControl（每行 ↑↓ + 名称 + 手动路径行内嵌 TextBox）+ 桌面兜底固定行 + 说明文字 |
| `Localization/strings.zh-CN.json` / `strings.en.json` | 修改 | 新增路径来源名称、说明、按钮 ToolTip 等 key（参照 WPF `Settings_DefaultPath_*` 文案） |

**范围外（明确不做）：** WPF 遗留版（维护模式）；QuickPathControl 等其它路径选择入口；多手动路径；拖拽排序；桌面行可移动。

---

## 布局总览（高级 Tab 内）

```
┌ 默认路径优先级 ──────────────────────────────┐
│  [↑][↓] 场景路径                              │
│  [↑][↓] 资源管理器路径                        │
│  [↑][↓] 最近访问                              │
│  [↑][↓] 手动路径  [ d:\zip          ] ←TextBox│
│         桌面（始终作为最终兜底）                │
│  说明：按上到下顺序尝试，第一个可用路径作为      │
│        文件选择器的初始位置                     │
└──────────────────────────────────────────────┘
```

- 首行 ↑ 禁用、末行 ↓ 禁用
- 桌面行无按钮，固定底部
- 手动路径行：名称 + TextBox（仅该行有），留空/路径不存在时跳过

---

## 运行时解析逻辑

`CustomFilePickerDialog.ResolveInitialPath(initialPath)` 改造（替代当前固定逻辑）：

```
输入: initialPath (场景路径, 可 null)
1. chain = AppSettings.DefaultPathOrder (List<string>, 去重 + 过滤未知值)
2. 确保 desktop 不在 chain 中（强制兜底）
3. foreach (kind in chain):
     path = kind switch {
       "context"  => initialPath (非空则校验存在),
       "explorer" => ExplorerWindowTracker.GetActiveExplorerPath(),
       "recent"   => PathHistoryManager.GetRecent(1)?.Path,
       "custom"   => AppSettings.CustomDefaultPath (非空则校验存在),
       _          => null
     }
     if (path 非空且 Directory.Exists(path)) return path
4. return Environment.GetFolderPath(Desktop)  // 兜底
```

要点：
- `context` 空或不存在 → 跳过，不打断链
- `recent` 无历史 → 跳过
- `custom` 留空或路径不存在 → 跳过
- 环境变量展开：`Environment.ExpandEnvironmentVariables`（复用现有 `ResolveInitialPath` 行为）

---

## Task 分解

### Task 1: AppSettings 字段 + 序列化兼容

- [ ] `Models/AppSettings.cs` 新增：
  ```csharp
  /// <summary>默认路径优先级顺序（不含桌面，桌面始终兜底）。值域: context / explorer / recent / custom。</summary>
  public List<string> DefaultPathOrder { get; set; } = new() { "context", "explorer", "recent", "custom" };
  /// <summary>手动路径值（对应 DefaultPathOrder 中的 "custom" 项）。</summary>
  public string CustomDefaultPath { get; set; } = "";
  ```
- [ ] 确认 `AppSettings` 序列化机制（JSON，`List<string>` 直接支持），无需额外迁移逻辑
- [ ] 手动验证：空字段时 JSON 反序列化正常（旧 settings.json 无这两个字段 → 走默认值）

### Task 2: CustomFilePickerDialog.ResolveInitialPath 改造

- [ ] `ResolveInitialPath` 改为读取 `AppSettings`（Avalonia 版单例），按 DefaultPathOrder 探测
- [ ] `custom` 项使用 `AppSettings.CustomDefaultPath`
- [ ] 桌面兜底（`Environment.SpecialFolder.Desktop`）
- [ ] 校验：过滤未知值、去重、desktop 不在链中
- [ ] 保留 `initialPath` 参数语义（场景路径 = `context` 项）
- [ ] 无回归：现有调用方（PickFolder/SaveFile/OpenFile/ExtractFolder/PickItems）行为符合预期

### Task 3: SettingsWindowViewModel 排序逻辑

- [ ] 新增 `PathPriorityItem` 模型（Kind, DisplayName, IsManualPath, ↑↓ 可用性）或复用现有模式
- [ ] `ObservableCollection<PathPriorityItem>` 加载自 `AppSettings.DefaultPathOrder`
- [ ] `MoveUp` / `MoveDown` 命令（边界禁用）
- [ ] `CustomPath` 属性绑定 TextBox
- [ ] 保存：写回 `DefaultPathOrder` + `CustomDefaultPath`（确认 SettingsWindow 保存机制）

### Task 4: SettingsWindow.axaml UI

- [ ] 「高级」Tab 新增「默认路径优先级」GroupBox/分组（参照 WPF `Settings_DefaultPath_GroupHeader` 样式）
- [ ] ItemsControl + 每行模板：↑↓ 按钮（PathIcon `IconArrowUp` / `IconArrowDown`）+ 名称 TextBlock + 手动路径行 TextBox（仅 Kind==custom 显示）
- [ ] 桌面兜底固定行（无按钮）
- [ ] 说明文字（灰字小号）
- [ ] 遵循主题资源键（AGENTS.md 规则 4）+ 紧凑度资源（规则 5）+ 列表行高（规则 7）
- [ ] 新增控件后确认无默认系统色

### Task 5: 本地化 key

- [ ] zh-CN / en 各新增：
  - 分组标题（如 `Settings_DefaultPath_GroupHeader`）
  - 4 个来源名称：场景路径 / 资源管理器路径 / 最近访问 / 手动路径
  - 桌面兜底行文案
  - 说明文字
  - ↑↓ 按钮 ToolTip（可选）
- [ ] 参照 WPF 版 `Settings_DefaultPath_*` key 文案（zh/en 已在 WPF strings 中存在可复用）

### Task 6: 验证

- [ ] `dotnet build src\MantisZip.UI.Avalonia` 0 错误（新增代码 0 警告）
- [ ] `dotnet test tests\MantisZip.UI.Avalonia.Tests` 全部通过
- [ ] lsp_diagnostics 干净
- [ ] JSON 有效性（node JSON.parse）
- [ ] 手动验收清单（用户执行）：
  - 设置 → 高级 → 调整顺序（↑↓ 移动、边界禁用）
  - 手动路径填 `d:\zip`（存在/不存在/留空 三种情况）
  - 打开压缩设置 → 添加文件/文件夹 → 初始路径按链生效
  - 解压场景 → 目标目录选择 → 初始路径 = DestinationPath（context 优先时）
  - 全链不可用（如 recent 无历史、explorer 无活动窗口、custom 留空、context 为 null）→ 桌面兜底
  - 主题切换后排序 UI 颜色正常

---

## 风险与注意

1. **ExplorerWindowTracker 是 Windows-only**（P/Invoke + COM，CA1416 suppress）——Avalonia 当前 net10.0 跨平台目标，非 Windows 平台该调用应返回 null（跳过该链项），不崩溃
2. **PathHistoryManager** 记录的是浏览历史（QuickPathControl/文件选择器导航时写入），recent 项可能为空 → 跳过即可
3. **旧 settings.json 兼容**：无新字段时走默认值，无需迁移
4. **WPF 版同步**：本计划不改 WPF（维护模式），但 `Settings_DefaultPath_*` key 文案已在 WPF strings 存在，Avalonia 侧复用需核对 key 命名
