# 讨论：文件过滤功能 — 用户操作

## 讨论背景

与用户一起 review `file-filter-feature.md` 计划，重点关注**用户操作流程**的合理性和体验。

---

## 目前计划的用户操作流程

### 压缩场景

#### 场景 A：普通压缩（有 CompressSettingsDialog）
1. 用户选择文件/文件夹 → 右键 → 压缩菜单 → 打开 CompressSettingsDialog
2. 对话框底部有一个 **Expander（默认折叠）**，标题"文件过滤条件"
3. 用户展开后：
   - 先看到"启用过滤"开关（默认关闭）
   - 启用后，4 个区可编辑：扩展名、文件名、大小、日期
   - 顶部的预设下拉框可加载已有预设
4. 用户配置过滤 → 确定 → 只对匹配文件进行压缩

#### 场景 B：快捷压缩（--compress-quick / separate / combined，无对话框）
1. 用户选择文件 → 快捷压缩
2. 如果有 `ActiveFilterPresetName` 非空 → **自动**应用该预设
3. 如果没有预设 → **弹出轻量 FileFilterEditor 对话框**
4. 用户选"取消" → 不过滤，压缩所有文件

### 解压场景
1. 用户打开/选择压缩包 → 提取（here / to-name / smart / to）
2. 系统调用 `ListEntriesAsync` 获取全量条目
3. 判断是否启用过滤：
   - 启用 → 弹出 ExtractFilterDialog
   - 禁用 → 全量提取（原有行为）
4. ExtractFilterDialog 显示："共 42 个文件，过滤后将提取 12 个"
5. 确定后只提取匹配条目

### 预设系统
- 可在 FileFilterEditor 中保存/加载预设
- 上限 20 个
- 存储在 settings.json 中

---

## ✅ 已达成共识

### 方案变更：Expander → Tab 3

**原计划**：CompressSettingsDialog 底部加 Expander（默认折叠）
**新方案**：新增第 3 个 Tab "文件过滤"

理由：
- Tab 更显眼，用户能直接看到过滤功能的存在
- 按类别分 tab，不会杂乱
- 扩展性好，将来加更多选项只需加 tab
- 与现有"通用""注释"tab 风格一致

### 现有 TabControl 结构（确认）

```
TabControl
├── Tab 1: "通用" (General)
│   ├── 源文件/目录 (GroupBox)
│   ├── 压缩包设置 (格式/级别/分卷)
│   └── 加密 (密码)
├── Tab 2: "注释" (Comment)
│   ├── 注释文本框
│   └── 分配策略 (独立模式下)
└── Tab 3: "文件过滤" (Filter) ← 新增
    ├── 启用过滤开关
    ├── 预设栏 (下拉框 + 保存/删除)
    ├── 扩展名区 (常用类型勾选 + 自定义)
    ├── 文件名区 (通配符)
    ├── 大小区 (范围 + 单位)
    └── 日期区 (DatePicker)
```

### 3. 解压场景 — 每次都要弹窗？
- 计划没说解压时是否支持预设自动应用
- 如果每次提取都弹 ExtractFilterDialog，用户可能会觉得烦
- 是否应该跟压缩一样：有预设自动应用，无预设才弹窗？

### 4. 过滤维度的易用性
- 扩展名：常用类型勾选（音频/视频/图片/文档/压缩包）+ 自定义输入
- 文件名模式：通配符 `*` 和 `?`
- 大小范围：B/KB/MB/GB
- 日期范围：DatePicker
- 用户能否直观理解这些？

### 5. 预设的默认值
- 计划没有提到是否内置默认预设
- 要不要预置一些常见场景？比如"仅图片"、"仅文档"、"大文件(>100MB)"

### 6. 多个过滤条件的关系
- 目前是 AND 逻辑（全部满足才匹配）
- 用户是否需要知道这个？UI 上需要提示吗？

### 7. 解压过滤对话框的入口
- 提取菜单有 4 个入口（here / to-name / smart / to）
- 加上 MainWindow 里的提取
- 是不是所有入口都统一加过滤？还是部分不加？

---

## ✅ 已确认决策汇总

### 1. UI 方案
- **压缩对话框**：Tab 3 "文件过滤" 取代原计划 Expander
- **解压对话框**：新建 ExtractSettingsWindow（TabControl），与 CompressSettingsWindow 对等设计
- 未来加功能只需加 Tab

### 2. 快捷模式不参与过滤
- **快捷压缩**（--compress-quick / separate / combined）：不过滤，纯快捷
- **快捷解压**（--extract-here / --extract-to-name / --extract-smart）：不过滤，纯快捷
- **MainWindow SmartExtract_Click**：不过滤
- 用户如果想一键过滤压缩，后续通过**压缩配置预设**功能自己生成

### 3. 解压过滤触发时机
- 只在 **MainWindow Extract_Click** 和 **--extract** 弹 ExtractSettingsWindow
- **没有自动应用预设逻辑** — 用户手动在对话框里启用过滤

### 4. 内置预设（共 8 个）
| 预设名 | 条件 |
|--------|------|
| 📷 仅图片 | .jpg .jpeg .png .gif .bmp .webp .svg |
| 🎵 仅音频 | .mp3 .wav .flac .aac .ogg .wma |
| 🎬 仅视频 | .mp4 .avi .mkv .mov .wmv .flv |
| 📄 仅文档 | .pdf .doc .docx .xls .xlsx .ppt .pptx .txt |
| 🗜 仅压缩包 | .zip .7z .rar .tar .gz .tgz |
| 📦 大文件(>100MB) | 大小 ≥ 100MB |
| 📅 本月修改 | 日期 ≥ 当月 1 日 |
| 🗑 排除缓存/临时文件 | 文件名模式：*.tmp *.cache *.log *.bak |

### 5. 压缩配置预设（新计划）

#### 用户确认的设计

| 问题 | 决策 |
|------|------|
| 管理界面在哪？ | 压缩在 **CompressSettingsWindow**，解压在 **ExtractSettingsWindow** |
| 预设能编辑吗？ | **加载 → 修改 → 存同名 = 覆盖编辑**（没有单独的编辑按钮） |
| 存密码吗？ | **不存** |
| 菜单叫什么？ | **预设的名字** |
| 菜单注册？ | 保存时用户**选择是否显示在右键菜单** |

#### 设计推演

**CompressPreset 模型应该存什么？**
- 名称（也用作菜单名）
- 格式、压缩级别、分卷大小
- 输出方式（手动/同目录/分离）
- 输出路径（手动模式下）
- 注释文本 + 分配策略
- 文件过滤条件（来自文件过滤功能的 FileFilterCriteria）
- 是否显示在菜单（bool）

**ExtractPreset 模型应该存什么？**
- 名称
- 目标路径模式（同目录/桌面/手动选择）
- 文件冲突处理
- 解压后打开文件夹
- 文件过滤条件
- 是否显示在菜单

**Shell 注册方案**
- 当前已有右键菜单系统（`ShellIntegration.cs`），但现有的是**静态注册表条目**
- 预设菜单需要**动态注册**——保存时注册，删除时反注册
- 或更简单：**软件启动时统一同步**一次所有标记为"菜单"的预设
- 压缩预设 → 注册到 `*`（文件）和 `Directory`（文件夹）
- 解压预设 → 注册到 `*`（文件，仅压缩包）

**CLI 入口**
- `--compress-with-preset "预设名" <文件路径...>`
- `--extract-with-preset "预设名" <压缩包路径>`

### 6. ExtractSettingsWindow 结构
```
TabControl
├── Tab 1: "通用" (General)
│   ├── 目标路径（同目录 / 桌面 / 指定位置）
│   ├── 文件冲突处理（覆盖 / 跳过 / 重命名）
│   ├── 解压后打开文件夹
│   └── ...将来更多选项
└── Tab 2: "文件过滤" (Filter)
    ├── 启用过滤开关
    ├── 预设栏 + 4 个过滤区
    └── 统计提示（共 N 个，过滤后将提取 M 个）
```
