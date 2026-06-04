# Draft: 文件列表筛选/搜索

## 当前现状（调研发现）

### 文件列表机制
- `_allItems` (List&lt;ArchiveItem>) 存储所有文件条目
- `FilterFiles(folderPath)` 根据当前目录路径筛选 + 隐式目录合成
- 无任何搜索/过滤文本框
- `FileListGrid` 是 DataGrid，ItemsSource 在 `FilterFiles` 中设置
- 支持排序（`FileListGrid_Sorting`），排序状态记忆和恢复

### 现有 UI 布局
- 文件列表位于 ContentGrid 的 TreeView + DataGrid 区域
- FileListPanel (Grid.Row=1) 包含 Archive Info + ContentGrid
- 状态栏显示 DirStats, SelectionStats, ArchiveStats, PasswordStatus

### 用户名/路径筛选
- 文件名列：NameDisplay（绑定显示名）
- 全路径列：无（FullPath 存在但不显示）
- 列排序支持 SortMemberPath

### 相关工作
- 压缩设置窗口已有搜索框模式（PwdSearchBox → 密码搜索）
- 无文件列表本地化字符串与搜索/筛选相关

## 已确认的需求

### 功能拆分
- 这是三套独立的系统：
  - **「显示所有子目录文件」** ToggleButton — 位于主工具栏，影响文件列表本身的显示模式
  - **筛选工具栏** — 位于 Archive Info 和 FileListGrid 之间，包含多维度过滤控件
  - **筛选逻辑** — 共享的过滤引擎

### 筛选工具栏布局

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ 🔍 [文本搜索................] │ 日期: [开始日期]  │ 大小: [最小值] [单位▼] │  [×清除] │
│                              │       [结束日期]  │       [最大值] [单位▼] │          │
└──────────────────────────────────────────────────────────────────────────────┘
```

- 🔍 文字搜索（左区）：TextBox，水印"筛选文件…"，大小写不敏感匹配所有可见列
- 📅 日期范围（中区）：两个 DatePicker（上下叠放），过滤 LastModified 区间
- 📊 大小范围（右区）：两个数字输入 + 单位 ComboBox（B/KB/MB/GB，上下叠放），过滤 Size
- 过滤组间使用竖线 | 分隔
- × 清除按钮清除所有过滤条件

### 搜索范围
- 受「显示所有子目录文件」开关控制
- 开关关闭（默认）：`FilterFiles` 只显示当前目录的直接条目（隐式目录合成），筛选在此视图内过滤
- 开关开启：`FilterFiles` 递归展开当前目录所有子目录文件为扁平列表，筛选在此扩展视图中过滤

### 搜索触发方式
- 所有过滤控件均实时生效（TextChanged / DateChanged / SelectionChanged），无需按钮

### 搜索匹配字段
- 文字搜索：所有可见列（NameDisplay, SizeDisplay, CompressedSizeDisplay, RatioDisplay, CRC32Display, DateDisplay）
- 日期过滤：LastModified 在 [StartDate, EndDate] 区间内
- 大小过滤：Size 在 [MinSize, MaxSize] 区间内（换算为标准字节比较）

### UI 布局
- **「显示所有子目录文件」ToggleButton**：主工具栏 ToolBar 上
- **筛选工具栏**：在 Archive Info 栏和 FileListGrid 之间的新增行

### 筛选工具栏视觉
- 文字搜索：水印"筛选文件…"，左侧 🔍 图标
- 日期：DatePicker × 2（开始/结束），竖排
- 大小：TextBox（数字）+ ComboBox（B/KB/MB/GB）× 2（最小/最大），竖排
- 清除按钮：×，清除所有过滤条件
- 过滤组间竖线分隔
- 空结果：DataGrid 区域居中文字 + 状态栏同步

### 「显示所有子目录文件」ToggleButton
- 图标：🌲
- 位置：主工具栏（ToolBar）上
- 仅在加载压缩包后可用
- 扁平视图只显示文件（不含目录条目）

### 搜索与导航交互
- 所有过滤始终在当前目录上下文内生效
- 切换目录树节点时，过滤条件保持，新目录自动应用全部过滤
- 清除全部恢复当前视图的完整列表

## 技术实现要点（初步）
1. **FilterFiles 修改**：新增 `showSubfolders` 参数，为 true 时从 `_allItems` 中搜集当前目录下所有递归文件（不含目录），跳过隐式目录合成
2. **筛选引擎**：提取为独立方法 `ApplyFilters(IReadOnlyList<ArchiveItem> items, SearchFilters filters)`，支持文字/日期/大小组合过滤
3. **ToggleButton 交互**：切换时重新调用 `FilterFiles` 刷新列表
4. **筛选条件数据结构**：`SearchFilters` 类/record 封装文字、日期范围、大小范围

## 范围边界
- IN: 筛选工具栏（文字+日期+大小）、显示所有子目录 ToggleButton、通用过滤引擎
- OUT: 压缩/解压文件筛选（file-filter-feature.md 已有独立计划）
- OUT: 预览系统的任何修改
- OUT: 左侧目录树的任何变更

## 关键设计问题（待确认）
1. 搜索触发方式 — 自动实时过滤（输入即搜）还是按按钮/回车触发？
2. UI 位置 — 搜索框放在哪里？工具栏上？文件列表上方？状态栏？
3. 搜索匹配什么字段？文件名？全路径？
4. 大小写敏感？
5. 显示所有子目录文件 与 搜索框 的交互关系？搜索时可以配合目录树导航吗？
6. 本地化字符串命名空间
