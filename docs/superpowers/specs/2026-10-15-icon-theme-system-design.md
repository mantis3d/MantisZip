# 图标主题系统设计 — 可切换风格 + 用户导入图标库

- **日期**: 2026-10-15
- **状态**: 已批准（brainstorming 逐节确认）
- **来源**: 会话头脑风暴（视觉伴侣对比 + 5 轮澄清问题 + 3 方案权衡）
- **下一步**: writing-plans 生成实施计划
- **版本号**: 本设计不涉及版本号变更（规则 2）

---

## 1. 背景与目标

MantisZip 当前只有一套内置图标（`AppIcons.axaml`，Fluent 风格），用户问：

1. 能否做成**可切换的图标风格**？
2. 更进一步，能否做成**用户可导入的图标库**？

目标：两层都要——应用内置多套风格供切换，并支持用户导入自定义图标包 + 逐个图标覆盖。

## 2. 已确认的决策记录

| # | 问题 | 决策 |
|---|------|------|
| D1 | 最终形态 | **C：内置风格 + 可导入图标库**（架构上主题 = 提供同名键的资源字典，导入只是另一入口） |
| D2 | 覆盖范围 | **仅应用内矢量图标**。文件类型系统图标（管线②）、Shell 右键图标、**文件关联图标**全部排除，各走各的流程 |
| D3 | 内置风格阵容 | **6 套**：A 现状（默认）/ C 粗圆角 / D 双色点缀 / E 彩色面性 / F 渐变描边 / G 强调色双色 |
| D4 | 导入粒度 | **B：整套导入 + 单图标覆盖** |
| D5 | 总体方案 | **方案 1**（DrawingImage + DynamicResource 资源字典交换）+ 方案 3 的**控件层**（IconView 薄控件，不要服务层） |
| D6 | 文件关联图标 | **机制完全分离**（注册表 + .ico 独立流程）；将来包可选附带 `assoc/*.ico`，由设置里的**显式按钮**单独应用，不随主题自动触发 |
| D7 | 主题色 × 图标的关系 | **选择各自 + 渲染层自动配套**：图标风格与主题（Light/Dark 及 `ThemeAccentBrush`）是两个正交开关，**不成套绑定**；颜色适配由部件级画刷在渲染层自动完成（null→Foreground、G 底块→ThemeAccentBrush DynamicResource、E/F baked 固定色），不在选择层做耦合。成套预设打包留作未来扩展（§11） |

### 关键事实（2026-10-15 实测）

- `AppIcons.axaml` 现有 **116 个** `<Geometry>` 键（键名 `IconPascalCase`），非 125
- 消费端：**~175 处** `PathIcon Data={StaticResource IconXxx}`、**11 处** `GeometryResourceConverter` 动态绑定（5 文件，**含** `App.axaml:272` 的 TabItem TabIcon）、**2 处** `BoolToIconConverter`、1 处 C# 动态设置（`MatchedPasswordDialog.axaml.cs:85` 复制确认）
- `PathIcon` 由 Fluent 模板用 **`Foreground`** 上色；`App.axaml:132` 的 `ToggleButton:checked PathIcon → Foreground=White` 依赖此语义（选中变白）
- `ToolbarButtonIcon` 字符文本 13 处（TextBlock 字形，非资源图标）——**不在本系统范围**
- 现有唯一图标相关设置：`ShowMenuIcons`（Shell 菜单，与本系统无关）
- `IconTestViewModel.LoadAllIcons()` 注册表（~100+ 条：分类/语义名/资源键/状态/位置）作为**覆盖率清单契约**（AGENTS.md 规则 8 强制维护）

## 3. 范围与非目标

**范围内**：
- 应用内矢量图标契约升级（Geometry → DrawingImage）与消费端迁移
- `IconView` 自定义控件
- 6 套内置主题的生产管线（`tools/IconGen` 脚本 + 编译期 axaml 字典）
- 图标包导入（文件夹/ZIP）、覆盖率校验、持久化
- 单图标覆盖（第③层字典）
- 设置 UI（外观 Tab 图标风格分区 + 图标管理器窗口）
- 错误处理与测试

**非目标（明确排除）**：
- 文件类型系统图标（`IconService`/`Win32IconProvider`/`IconProvider` Bitmap 管线）——Q2 决策 A
- Shell 右键菜单 .ico（ShellExt COM 宿主）
- 文件关联注册表图标（机制分离，D6）
- `ToolbarButtonIcon` 字符文本（13 处保持现状，后续若统一另行立项）
- 图标风格与主题（Light/Dark 及 `ThemeAccentBrush`）**正交**，互不干扰（D7：颜色适配由渲染层部件级画刷自动完成，不在选择层绑定）

## 4. 架构

### 4.1 资源契约：键 → DrawingImage

每个图标键的值统一为 `DrawingImage`，内部含 1+ 个 `GeometryDrawing`（部件）：

- 单部件 + `Brush=null` → **上下文着色**：由 `IconView.Foreground` 渲染（保留现有语义）
- 单部件 + 非空 Brush → baked（渐变等）
- 多部件 → 可混合：例如 D/G = 底块部件（baked 半透明/强调色）+ 线框部件（null → Foreground）
- E 彩彩色 = 多部件全 baked 原色
- SVG path 数据与 `Geometry` 同源，导入解析零转换损耗；多色 SVG 直接映射多部件

### 4.2 三层字典层叠（优先级从低到高）

```
App.axaml MergedDictionaries:
 ① 默认主题 A（现状 116 键转换，永远垫底，缺键兜底 → 永不空白）
 ② 当前主题层（6 内置套之一 或 导入包）—— 切换 = 替换本层
 ③ Override 层（单图标覆盖，最高优先）—— 恢复默认 = 删条目
```

- 切主题 = 替换第②层字典 → `DynamicResource` 自动全 UI 刷新（Avalonia 原生机制）
- 层叠天然支持缺键回退与覆盖优先，无任何特判代码

### 4.3 IconView 控件（新增，约 100 行）

`Controls/IconView.cs`：

- `Source: DrawingImage?` StyledProperty —— 消费端写 `Source="{DynamicResource IconXxx}"`
- `Foreground: IBrush?` StyledProperty —— **注册 `inherits: true`**（像 TextBlock 一样继承父级文字色）；为 null 时回退 `ThemeTextPrimaryBrush`（经资源订阅缓存，避免每次 Render 查资源）
- 渲染：遍历 `Source.Drawing` 的 `GeometryDrawing`，部件 `Brush == null` 用 `Foreground`，否则用部件 Brush；`Width`/`Height`/Stretch 尺寸语义与 `PathIcon` 对齐（均匀缩放）
- **样式选择器**：`App.axaml:132` 扩为 `ToggleButton:checked PathIcon, ToggleButton:checked IconView → Foreground=White`
- 命名空间注册进各消费 axaml 的 `xmlns`

**为什么不能直接用 `Image`**（§1 修正，证据驱动）：`Image` 渲染 baked DrawingImage 会丢失 `Foreground` 上下文着色，「选中变白」等语义全部失效；Avalonia 官方 `PathIcon` 也是 `Foreground` 上色（Fluent 模板绑定证实）。IconView = 方案 1 的资源机制 + 方案 3 的控件层，**不引入图标服务**（DynamicResource 的自动刷新是白送的，不自造轮子）。

### 4.4 消费端迁移（机械替换）

| 现状 | 迁移后 | 数量 |
|------|--------|------|
| `<PathIcon Data="{StaticResource IconXxx}"/>` | `<controls:IconView Source="{DynamicResource IconXxx}"/>` | ~175 处 / 15 文件 |
| `GeometryResourceConverter`（键→Geometry） | 重命名 `IconResourceConverter`（键→DrawingImage），注册键同步改 | 11 处 / 5 文件 |
| `BoolToIconConverter` 输出消费 | 输出适配为 DrawingImage 解析 | 2 处 |
| C# 动态设置 `PathIcon.Data` | `IconView.Source`（`MatchedPasswordDialog.axaml.cs:85`） | 1 处 |
| TabItem `TabIcon`（附加属性存键字符串，`App.axaml:272`） | 渲染控件换 IconView，附加属性保持 string 键 | 含于 converter 行的 11 处内 |

迁移可用脚本辅助；完成标准 = 全仓无 `PathIcon Data="{StaticResource Icon` 残留（Avalonia 自带控件模板中的 PathIcon 不属我方代码，不管）。

### 4.5 明确排除的管线

见 §3 非目标。文件关联图标走现有 `ShellIntegration.Assoc` + `.ico` 流程，与本系统**零共享代码路径**（仅数据层面将来可选：包附带 `assoc/*.ico` + 显式按钮，本期不实现）。

## 5. 内置主题生产（6 套）

| 套 | 生产方式 | 部件/画刷 | 上下文着色 |
|----|----------|-----------|------------|
| **A 现状**（默认） | 脚本转换现有 116 键 → 单部件 DrawingImage | 单部件 / null | ✅ 全保留 |
| **C 粗圆角** | Phosphor Bold / Lucide 按语义映射（MIT），缺口手补 | 单部件 / null | ✅ 全保留 |
| **F 渐变** | 复用 C 的几何，只换画刷（蓝→紫渐变，部件内静态 LinearGradientBrush） | 单部件 / baked 渐变 | ⚠️ 选中态保持渐变 |
| **D 双色** | Phosphor Duotone 分层文件（base + accent 双 path） | 底块 baked 半透明 + 线框 null | ✅ 线框保留 |
| **G 强调色** | 同 D，底块画刷 = 主题级强调画刷（`DynamicResource` 引用，随主题可换色） | 底块 DynamicResource + 线框 null | ✅ 线框保留 |
| **E 彩色** | 彩色图标库/手绘，每图标 2+ 部件 | 全 baked 原色 | ⚠️ 选中态保持彩色 |

**生产管线**：
- `tools/IconGen`（dev-time 脚本，入仓库可复现）：输入 = 现有几何 / 开源库 SVG / 语义映射表；输出 = `Icons.A.axaml` … `Icons.G.axaml` 六个编译期字典
- 6 套全量编译进包（~116 键 × 6 ≈ 700 drawings，path 数据合计 < 1MB，无按需加载必要）
- **覆盖率契约**：IconGen 输出校验对着 `IconTestViewModel` 注册表跑，缺键测试期报警（116 × 6 全存在）
- 开源库协议：Phosphor MIT / Lucide MIT / Tabler MIT；About 页保留图标库署名
- 语义映射表键名沿用 `IconPascalCase`，映射不上的键手绘补齐

## 6. 图标包导入

### 6.1 包格式（文件夹与 ZIP 同构）

```
my-icons.zip / my-icons/
├── manifest.json       # 可选：包元数据 + 键名映射
├── icons/
│   ├── IconFolder.svg  # 首选：按键名命名
│   └── folder-open.svg # 或：库原名 + manifest mapping
├── preview.png         # 可选：风格选择器缩略图
└── assoc/              # 可选预留（本期不接，见 D6）
```

### 6.2 manifest.json（全字段可选；无 manifest 按键名约定即可导入）

```json
{
  "name": "My Icons", "id": "my-icons", "version": "1.0",
  "author": "…", "license": "MIT",
  "mapping": { "IconFolder": "folder-open.svg" }
}
```

manifest 损坏 → 警告 + 按键名约定继续（不阻断）。

### 6.3 导入数据流

1. 设置 → 外观 → 「导入图标包…」→ `CustomFilePickerDialog`（规则 15：`ShowOpenFileAsync` 限 ZIP 模式 / `ShowOpenItemsAsync` 允许文件夹，**禁用原生 StorageProvider**）
2. ZIP → 解压到 `icon-packs/<id>/`（SharpCompress 已有依赖 + `FileConflictHelper.SanitizeEntryPath` 防 Zip Slip）
3. 解析 SVG：**只提取几何与填充色，不渲染不执行**（`XmlReader` 禁 DTD/实体防 XXE）；`viewBox` 归一化到 24×24
4. 构建 `DrawingImage`：颜色 → baked 画刷；无色形状 → null 部件（跟随 Foreground）
5. 覆盖率校验：对照 116 键清单 → 缺键**只警告不阻断**（层叠兜底 A）
6. 落盘 + `registry.json` 登记 → 替换第②层字典 → 全 UI 即时刷新 → `AppSettings.SelectedIconTheme` 持久化
7. id 重复 → 询问「覆盖已有 / 取消」

### 6.4 SVG 解析边界（v1）

- **支持**：`path` / `circle` / `rect` / `line` / `polygon` / `polyline`、solid 填充色、viewBox 归一化、多部件多色
- **不支持即报错（不静默丢内容）**：`clipPath` / `mask` / `filter` / `text` / `use` / 嵌套 `svg` / 外链
- **原子拒绝**：任一 SVG 解析失败、0 个有效图标、ZIP 损坏、XXE/DTD、Zip Slip → 整包不导入 + 失败文件清单对话框

### 6.5 存储布局（与 settings.json 同目录）

```
%LOCALAPPDATA%\MantisZip\
├── settings.json            # + SelectedIconTheme（"A"|"C"|"D"|"E"|"F"|"G"|pack id，默认 "A"）
├── icon-packs\
│   ├── registry.json        # 已导入包元数据（id/name/path/coverage/…）
│   └── <id>\…               # 解压后的包内容
└── icon-overrides\          # 单图标覆盖：IconXxx.svg 逐键落盘 → 永远第③层
```

### 6.6 单图标覆盖

- 图标管理器中逐键「替换…」→ 选 SVG → §6.3 步骤 3-4 解析 → 写入 `icon-overrides/IconXxx.svg` + 第③层字典加条目 → 即时生效
- 「恢复」= 删条目 + 删文件 → 回落当前主题层
- 损坏的 override → 跳过该键 + TraceLog，不阻断启动

## 7. 设置 UI

### 7.1 外观 Tab「图标风格」分区（SettingsWindow 外观 Tab 内新增）

- **图标风格**下拉：A/C/D/E/F/G + 已导入包；每项带 4 图标缩略预览；切换即时生效（无「应用」按钮）
- **[导入图标包…]**：走 §6.3 数据流；失败弹问题清单对话框
- **已导入列表**：`name（覆盖率 x%，缺 N 键）` + 删除按钮
- **[打开图标管理器…]**：显示已覆盖数量徽章
- **说明文案**：「图标风格只影响应用内图标；文件关联与右键菜单图标在各自设置中单独管理」
- 与 Light/Dark 主题正交，互不干扰

### 7.2 图标管理器（新窗口 `IconManagerWindow`）

- 顶部：搜索框（键名/语义名）+ 过滤徽章（全部 116 / 已覆盖 N / 当前风格缺失 N）+ 当前风格覆盖率警告横幅
- 网格卡片：图标预览 + 键名 + 语义名（**数据源 = IconTestViewModel 注册表**，同一注册表即覆盖率契约）
- 每卡操作：**[替换…]**（→ §6.6）/ **[恢复]**
- 行高遵守规则 7（`ControlHeight` 资源键）

### 7.3 规则对照

- **规则 13**：全部新文案三语成对（`strings.zh-CN/en/zh-TW.json`）；XAML 用到的 key 登记进 `MainWindowViewModel.UpdateLocalizedStrings()` 的 keys 数组；窗口 code-behind 文案用 `public string Xxx => LocalizationManager.T("Key")` + `x:CompileBindings="False"`
- **规则 4**：控件显式绑定主题资源键（`ThemeSurfaceBgBrush` 等，键名以 `Brush` 结尾）
- **规则 5**：间距/高度用 `SpacingXxxThk` / `ControlHeight` 动态资源
- **规则 14**：新 axaml 文件全部主要控件加中文注释
- **规则 15**：所有选择入口仅 `CustomFilePickerDialog`

## 8. 错误处理

| 场景 | 行为 |
|------|------|
| ZIP 损坏 / 0 有效图标 / XXE·DTD / Zip Slip / 任一 SVG 不兼容 | **整包原子拒绝** + 失败文件清单对话框（不导半套）+ TraceLog |
| manifest 损坏 | 警告 + 按键名约定继续 |
| 覆盖率缺键（合法） | 允许导入/切换 → UI 徽章 + 日志；层叠兜底 A，永不空白 |
| 包 id 重复 | 询问：覆盖 / 取消 |
| 启动时 `SelectedIconTheme` 指向已删包 / 字典装载异常 | **静默回退 A** + TraceLog（启动路径不弹窗） |
| 单个 override SVG 损坏 | 跳过该键（回落主题层）+ TraceLog |
| 导入写盘失败（磁盘满等） | 错误对话框 + TraceLog，清理半成品目录 |

- 日志统一 `App.TraceLog` → `LogRedactor` 路径脱敏（现有 `LogPrivacyMode` 机制）

## 9. 测试策略

**纯逻辑单测（默认随实现放 `MantisZip.UI.Avalonia` 侧，测试进 `MantisZip.UI.Avalonia.Tests`；除非实施时出现跨项目复用需求，不预先下沉 Core）**
1. SVG 解析器：合法 path/circle/rect、多部件色、viewBox 归一化；拒绝 clipPath/text/use/DTD/XXE 样本
2. 覆盖率计算：全套 / 部分 / 空；`registry.json` 序列化 round-trip
3. Zip Slip 拒绝（SanitizeEntryPath 语义）

**UI 层（MantisZip.UI.Avalonia.Tests）**
4. IconView 渲染语义：null 部件用 Foreground、baked 部件忽略 Foreground（headless 断言绘制笔刷）
5. 切换 `SelectedIconTheme` → 第②层字典被替换、缺键回落第①层
6. IconGen 输出校验：6 字典 × 116 键全存在（对着 IconTestViewModel 注册表）
7. 三语 key 对称：现有 `AboutWindowTests.AllThreeLanguages_HaveSameKeySet` 自动覆盖新增 key

**手动 QA 清单**
8. 6 套 × Light/Dark × 选中态截图走查；导入 happy 路径与全部失败路径；图标管理器替换/恢复回路；迁移后全 UI 与迁移前像素级对照（A 套必须零视觉差异）

## 10. 已知行为差异与风险

| 项 | 说明 |
|----|------|
| 选中态差异 | E/F/G 的 baked 部件在 `ToggleButton:checked` 下**不转白**（保持彩色/渐变）——已批准的接受项；A/C/D 线框/G 线框全保留 |
| Foreground 继承 | IconView 的 Foreground 注册 `inherits:true`，比 PathIcon「不继承需选择器兜底」更符合直觉；迁移后目检菜单/工具栏默认色 |
| 迁移面 | ~175 处机械替换是最大工作量项；脚本化 + 迁移前后截图对照控制风险 |
| 开源依赖 | Phosphor/Lucide/Tabler 均 MIT，仅需署名；映射表需人工校对语义 |
| E 套成本 | 唯一需要全新绘制/找彩色源的套（架构上已由多部件 DrawingImage 吸收） |

## 11. 未来扩展（非本期）

- 包附带 `assoc/*.ico` + 设置显式按钮「同时更新文件关联图标」（D6 留口）
- 主题 × 图标成套预设（D7 留口；若未来引入用户自定义主题色再评估成套打包）
- `ToolbarButtonIcon` 字符文本统一迁入 IconView
- 按主题分档的图标密度/大小预设

## 12. 开放问题

无阻塞项。实施计划阶段可再定的细节（不需回访设计）：
- IconGen 用 PowerShell 还是 C# 控制台（倾向 C#，与仓库技术栈一致、可复用 SVG 解析器代码）
- 图标管理器是否复用 `IconTestWindow` 的网格代码（倾向独立窗口，IconTestWindow 维持 Debug-only 职责）
