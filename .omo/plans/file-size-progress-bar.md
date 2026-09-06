# 文件列表进度条扩展（四列 + 目录独立基准）

> **状态**: ✅ 已完成 | **任务**: [████████] (8/8)

## 目标

在文件列表的「大小」「压缩后大小」「压缩率」「日期」四列中，用**背景进度条**直观展示每列的相对关系。让用户无需仔细读数字即可一眼看出：
- **大小列**：哪些是"大体积文件"（填充度高）
- **压缩后大小列**：哪些文件压缩后体积大（填充度高）
- **压缩率列**：哪些文件压缩效果好（填充度低 = 压缩率高）
- **日期列**：哪些文件是较新的（填充度高）

可通过 **视图 → 进度条** 菜单项一键开关。支持目录独立基准模式（文件与目录分别取 max）。

---

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `src/MantisZip.UI/MainWindow.UI.cs` | ArchiveItem 新增 7 属性 + FilterFiles 插入 ~70 行比例计算 |
| `src/MantisZip.UI/RatioToWidthConverter.cs` | **新文件** — MultiBinding 多值转换器 |
| `src/MantisZip.UI/MainWindow.xaml` | 注册 Converter + View 菜单新增 2 项 + 四列替换为模板列 |
| `src/MantisZip.UI/MainWindow.xaml.cs` | 构造函数同步 SeparatedBaseline 初始状态 |
| `src/MantisZip.UI/MainWindow.Menu.cs` | 新增 `ToggleProgressBars_Click` / `ToggleSepDirBaseline_Click` |
| `src/MantisZip.UI/AppSettings.cs` | 外观区块新增 `ShowProgressBars` / `SeparateDirBaseline` |
| `src/MantisZip.UI/Themes/Light.xaml` | 新增 12 个颜色资源（6 Color + 6 SolidColorBrush，含目录深色） |
| `src/MantisZip.UI/Themes/Dark.xaml` | 新增 12 个颜色资源 |
| `src/MantisZip.UI/Resources/strings.zh.json` | 新增 2 条 key |
| `src/MantisZip.UI/Resources/strings.en.json` | 新增 2 条 key |

---

## 视觉语义（四列）

| 列 | 语义 | 比例基准 | 受独立基准？ | 亮色(文件) | 亮色(目录) | 暗色(文件) | 暗色(目录) |
|----|------|----------|:---:|-----------|-----------|-----------|-----------|
| **大小** | 相对体积 | 当前视图最大 Size | ✅ | `#E0E7FF` | `#A3B8FF` | `#1E3A5F` | `#0F2A45` |
| **压缩后大小** | 相对压缩体积 | 当前视图最大 CompressedSize | ✅ | `#E8E0FF` | `#C4B0FF` | `#2D1B69` | `#1A0F3D` |
| **压缩率** | 压缩效率 | 绝对：CompressedSize/Size，上限 1.0 | ❌ | `#FDE68A` | n/a | `#5C4A1E` | n/a |
| **日期** | 相对新旧 | 当前视图有效日期跨度（仅文件） | ❌ | `#D1FAE5` | n/a | `#14532D` | n/a |

---

## 视图菜单结构

```
视图(_V)
├── 刷新(_R)          F5        ← 已有
├── ──────────
├── ☑ 进度条(_B)                ← 整体显隐开关
├── ☐ 目录独立基准(_D)          ← 目录文件分开算 max，图标透明度随状态变化
```

---

## 实现细节

### 1. ArchiveItem 新增属性（MainWindow.UI.cs）

```csharp
public bool ProgressBarEnabled { get; set; } = true;
public bool SeparateDirBaseline { get; set; } = false;
public bool UseDirProgressColor => IsDirectory && SeparateDirBaseline;
public double SizeRatio { get; set; }
public double CompressedSizeRatio { get; set; }
public double DateRatio { get; set; }
public double RatioBarValue => ProgressBarEnabled && !IsDirectory && Size > 0
    ? Math.Min(RatioSort, 1.0)
    : 0;
```

### 2. 比例计算 — 仅在 FilterFiles 中（一处覆盖所有场景）

`FilterFiles` 的 `sortedItems` 就绪后、`FileListGrid.ItemsSource = sortedItems` 之前插入。

支持两种模式：
- **统一基准**（默认）：全部条目（文件+目录）共享一个 max
- **分列基准**：文件之间比、目录之间比，各自出现 100% 满条

目录条目有聚合的 Size/CompressedSize（来自 `_dirStats`），参与进度条计算。

### 3. RatioToWidthConverter

新建 `src/MantisZip.UI/RatioToWidthConverter.cs`，命名空间 `MantisZip.UI`，四列共用。

### 4. XAML 四列替换

大小/压缩后大小/压缩率/日期四列从 `DataGridTextColumn` → `DataGridTemplateColumn`（Grid 中 Rectangle + TextBlock 叠加），保留 `SortMemberPath` 和 `Width`。

大小/压缩后大小两列的 Rectangle 使用 Style + DataTrigger 按 `UseDirProgressColor` 切换文件/目录颜色。

### 5. 目录深色进度条

`UseDirProgressColor`（`IsDirectory && SeparateDirBaseline`）为 true 时，Size/CompressedSize 列自动切到深色版资源（`*DirBg`），列标题右键菜单风格一致。

### 6. 菜单开关

- `ShowProgressBars` — 控制四列进度条整体显隐
- `SeparateDirBaseline` — 目录独立基准；图标透明度随 `IsChecked` 变化（开=1.0，关=0.2）

---

## 任务清单

- [x] **1. `ArchiveItem` 新增属性 + 门控** — `SizeRatio`、`CompressedSizeRatio`、`DateRatio`、`ProgressBarEnabled`、`SeparateDirBaseline`、`UseDirProgressColor`、`RatioBarValue`
- [x] **2. FilterFiles 比例计算逻辑** — 四列比例计算，统一/分列两种基准模式，目录条目正确参与
- [x] **3. `RatioToWidthConverter`** — 新建文件，XAML 注册
- [x] **4. XAML 四列模板替换** — 大小/压缩后大小/压缩率/日期 四列 DataGridTemplateColumn
- [x] **5. 目录深色颜色 DataTrigger** — Size/CompressedSize 列按 UseDirProgressColor 切换
- [x] **6. 主题颜色资源** — Light.xaml / Dark.xaml 各 12 个资源（含目录深色变体）
- [x] **7. AppSettings + 菜单事件** — `ShowProgressBars` + `SeparateDirBaseline`，View 菜单两项 IsCheckable
- [x] **8. 国际化翻译** — strings.zh.json / strings.en.json 各 2 条 key

---

## 交互细节

| 场景 | 大小列 | 压缩后大小列 | 压缩率列 | 日期列 |
|------|--------|-------------|---------|--------|
| **切换目录（FilterFiles）** | 重新计算 | 重新计算 | 无需重算（绝对比例） | 重新计算 |
| **排序** | 进度条随行移动 | 同左 | 同左 | 同左 |
| **暗色/亮色切换** | 资源字典自动跟随 | 同左 | 同左 | 同左 |
| **菜单关进度条** | 所有 ratio=0 → Rectangle 宽度 0 → 完全隐藏 | 同左 | 同左 | 同左 |
| **目录条目（分列基准开）** | 深色进度条 | 深色进度条 | 无（IsDirectory门控） | 无（无日期） |
| **目录条目（分列基准关）** | 与文件同色进度条 | 与文件同色进度条 | 无 | 无 |

---

## Done

- `dotnet build` 通过，0 错误
- 构建后无新 warning 引入
