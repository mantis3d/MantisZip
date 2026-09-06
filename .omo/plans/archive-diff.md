# 压缩包对比 (Archive Diff)

> 对比两个压缩包的内容差异 — 哪些文件相同、哪些不同、哪些只有一边有
> **状态**: 📋 待定 | **阶段**: [⬜⬜⬜⬜] (0/4)

---

## 动机

用户经常需要知道两个压缩包之间有什么不同：
- 下载了 v1.0.zip 和 v2.0.zip，想知道改了什么
- 备份了两个版本的源码压缩包，想对比文件变更
- 怀疑压缩包有损坏，想对比与正常副本的差异

目前没有主流压缩软件提供此功能 — 用户必须全部解压出来再用 Beyond Compare / WinMerge 等对比工具。

**目标：** 选中两个压缩包 → 一键生成差异报告，显示文件级别的增删改。

---

## 架构设计

### 交互流程

```
用户选择「对比压缩包」
    ↓
打开文件选择对话框（选两个压缩包）
    ↓
后台引擎分别 ListEntriesAsync
    ↓
按文件名索引、比较元数据（大小、CRC32/时间）
    ↓
生成差异结果 → 显示在对比面板
```

## 任务清单

- [ ] **1. Core: `ArchiveDiffEngine`** — 差异计算核心（对比算法 + 结果模型）
- [ ] **2. UI: `ArchiveDiffResultModel` + 展示控件** — 差异结果在 WPF 中的展示
- [ ] **3. UI: 交互流程集成** — 文件选择对话框 + 后台对比 + 结果展示
- [ ] **4. CLI: `--diff` 命令** — 命令行对比输出

### 对比结果模型

```csharp
public class ArchiveDiffResult
{
    public string ArchivePathA { get; set; }
    public string ArchivePathB { get; set; }

    public List<DiffEntry> Entries { get; set; }

    // 统计摘要
    public int SameCount { get; set; }         // 完全一致
    public int DifferentCount { get; set; }    // 同名但内容不同
    public int OnlyInACount { get; set; }      // 左边独有
    public int OnlyInBCount { get; set; }      // 右边独有
}

public class DiffEntry
{
    public string FileName { get; set; }
    public DiffType Type { get; set; }

    // A 的信息（OnlyInB 时为 null）
    public long? SizeA { get; set; }
    public DateTime? ModifiedA { get; set; }
    public long? CompressedSizeA { get; set; }

    // B 的信息（OnlyInA 时为 null）
    public long? SizeB { get; set; }
    public DateTime? ModifiedB { get; set; }
    public long? CompressedSizeB { get; set; }
}

public enum DiffType
{
    Same,       // 文件名+大小+时间+CRC 全部一致
    Different,  // 同名但内容不同
    OnlyInA,    // 仅左边有
    OnlyInB,    // 仅右边有
    Skip        // 目录条目等跳过对比
}
```

---

## 改动范围

涉及 **5 个文件**：

| 文件 | 改动 | 预估工时 |
|------|------|---------|
| `Core/Abstractions/ArchiveItem.cs` | 可能需加 `Crc32` 字段（若引擎支持返回） | 10min |
| `Core/Utils/ArchiveComparer.cs` | 🆕 新增 — 核心对比逻辑 | 2h |
| `UI/MainWindow.xaml` | 添加「对比压缩包」按钮/菜单 | 15min |
| `UI/MainWindow.Menu.cs` | 添加对比事件处理 | 20min |
| `UI/ArchiveDiffWindow.xaml/.cs` | 🆕 新增 — 对比结果展示窗口 | 1.5h |

**运行时依赖变更：** 无

---

## 实现细节

### 对比策略

**第一层：元数据对比（无需提取）**

```
1. 分别从两个压缩包获取文件列表（ListEntriesAsync）
2. 构建 Dictionary<string, ArchiveItem>（按 FullPath 索引）
3. 遍历 A 的条目：
   a. B 中不存在 → OnlyInA
   b. B 中存在但 Size/CompressedSize/LastModified 不一致 → Different
   c. B 中存在且元数据一致 → Same
4. 遍历 B 的条目，跳过已在 A 中的 → OnlyInB
```

**第二层：内容确认（可选，深度对比）**

对于标记为 `Same` 的条目，如果用户怀疑元数据不可靠（如修改时间被重置），可以触**深度对比**：

```
1. 从两个压缩包分别提取同一文件
2. 计算 SHA256
3. 哈希一致 → 确认为 Same
4. 哈希不一致 → 降级为 Different
```

深度对比默认关闭，用户可点击「验证全部」或「单条验证」触发。

### 展示 UI

```xml
<!-- ArchiveDiffWindow 布局 -->
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>      <!-- 标题 + 统计摘要 -->
        <RowDefinition Height="*"/>          <!-- 差异列表 -->
        <RowDefinition Height="Auto"/>      <!-- 操作按钮栏 -->
    </Grid.RowDefinitions>

    <!-- Row 0: 统计摘要 -->
    <StackPanel>
        <TextBlock Text="v1.0.zip vs v2.0.zip"/>
        <TextBlock Text="相同: 120 | 不同: 15 | 仅左边: 8 | 仅右边: 3"/>
    </StackPanel>

    <!-- Row 1: 差异列表 — ListView 带颜色标记 -->
    <ListView>
        <ListView.View>
            <GridView>
                <GridViewColumn Header="状态" DisplayMemberBinding="..."/>  <!-- 🟢/🟡/🔴 -->
                <GridViewColumn Header="文件名" DisplayMemberBinding="..."/>
                <GridViewColumn Header="大小(A)" DisplayMemberBinding="..."/>
                <GridViewColumn Header="大小(B)" DisplayMemberBinding="..."/>
                <GridViewColumn Header="修改时间(A)" DisplayMemberBinding="..."/>
                <GridViewColumn Header="修改时间(B)" DisplayMemberBinding="..."/>
            </GridView>
        </ListView.View>
    </ListView>

    <!-- Row 2: 操作按钮 -->
    <StackPanel>
        <Button Content="导出差异报告"/>
        <Button Content="提取差异文件"/>
    </StackPanel>
</Grid>
```

### 差异报告导出

支持导出为文本/TXT 格式：

```
===== 压缩包对比报告 =====
左: v1.0.zip (2026-05-01)
右: v2.0.zip (2026-05-15)

相同: 120 个文件
不同: 15 个文件
仅左边有: 8 个文件
仅右边有: 3 个文件

===== 仅左边有 =====
  src/old_module.cs     12 KB
  src/legacy_config.xml   4 KB

===== 仅右边有 =====
  src/new_module.cs     18 KB
  src/migration.sql      6 KB

===== 内容不同 =====
  src/Program.cs        15 KB → 22 KB
  README.md             3 KB → 5 KB
  ...
```

### 与现有 `IArchiveEngine` 的集成

已有引擎均已实现 `ListEntriesAsync`，返回 `IReadOnlyList<ArchiveItem>`，其中包含 `Name`、`Size`、`CompressedSize`、`LastModified`。

差异：`ArchiveItem` 当前没有 `Crc32`/哈希字段。如果需要深对比，有两种做法：

1. **轻量版（推荐 Phase 1）**：只用元数据（大小+时间）对比，不做哈希
2. **完整版（Phase 2）**：引擎新增 `GetEntryChecksumAsync(entryName)` 方法，返回 CRC32 或 SHA256

Phase 1 足够覆盖 90% 的实用场景（大小不同一定不同，时间不同很可能不同）。Phase 2 按需补充。

---

## 风险

| 风险 | 等级 | 对策 |
|------|------|------|
| 大压缩包对比慢（10 万+条目） | 🟢 | ListEntriesAsync 本身很快；UI 用虚拟化列表 |
| 深度对比涉及提取，耗时 | 🟡 | 深度对比默认关闭，用户手动触发 |
| 压缩包加密需要密码 | 🟡 | 对比前要求输入密码（复用现有 PasswordDialog） |
| 不同格式压缩包对比（ZIP vs 7z） | 🟢 | ListEntriesAsync 格式无关，对比逻辑通用 |

---

## 后续扩展

- **一键提取差异文件**：选中 diff 条目 → 提取到指定目录
- **批量对比**：选中多个压缩包，两两交叉对比
- **与提取历史联动**：对比「上次提取的版本」和「压缩包内的当前版本」

---

## Definition of Done

- [ ] `ArchiveDiffEngine` 完成差异计算，返回结构化的 `ArchiveDiffResult`
- [ ] UI 展示面板完成（差异列表 + 统计摘要 + 详情查看）
- [ ] `--diff` CLI 命令完成
- [ ] 拖拽/文件选择交互正常
- [ ] 加密压缩包在密码输入后可对比
- [ ] `dotnet build` 通过

### Final Checklist

- [ ] 轻量对比（大小+时间）正常工作
- [ ] 差异结果 UI 清晰展示 Same/Different/OnlyInA/OnlyInB
- [ ] 统计摘要正确（SameCount / DifferentCount / OnlyInACount / OnlyInBCount）
- [ ] 大压缩包（10万+条目）列表不卡顿
- [ ] 加密压缩包密码处理正常
- [ ] 不同格式交叉对比正常
