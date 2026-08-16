# 拖拽提取目标检测 — Marker 文件方案

## TL;DR

**问题**: `DragDrop.DoDragDrop` 返回后，Source 端（MantisZip）无法获知用户把文件拖到了哪个 Explorer 目录。

**方案**: 利用 `Shell.Application` COM 枚举已打开的 Explorer 窗口路径，创建一个 0 字节的 GUID 命名 marker 文件拖出去，`DoDragDrop` 返回后扫描这些路径查找 marker 文件，从而推断目标提取目录。

**状态**: 📋 待评估（需与 VFDO 方案对比后决定路线）

---

## 背景

当前 MantisZip 的拖拽提取行为（`MainWindow.DragDrop.cs`）是 **急加载模式**：

```
选中条目 → ExpandDragItems → 全部解压到 %TEMP% → DoDragDrop(CF_HDROP, 所有 temp 路径)
```

这导致：
- 每次 IO 两次（解压到 temp + Explorer 复制到目标）
- 大文件拖拽时用户需等待解压完成才能开始拖
- 用户可能中途取消但 temp 已占空间

**理想行为**（延迟加载）：用户拖出时立即响应，文件实际在 Explorer 请求时才生成。这是原 VFDO 方案的目标。但 VFDO 也无法获知 target 路径，且实现复杂（COM P/Invoke）。

本方案探索另一种思路：**先知道拖到哪，再提取到那**。

---

## 核心思路

### 原理

```
PreviewMouseMove
  ├─ 1. ExplorerWindowTracker.GetOpenExplorerWindows()  ← Shell.Application COM
  ├─ 2. 创建 0 字节 marker "__MZ_{GUID}.marker" 到 %TEMP%
  ├─ 3. DoDragDrop(new DataObject(FileDrop, [marker_path]), Copy)
  │     └── 用户拖动 marker 到 Explorer → Explorer 复制到目标目录
  └─ 4. DoDragDrop 返回
       └─ 5. 扫描所有已知路径 + Desktop 下是否有 "__MZ_*.marker"
            ├─ 找到 → 目标路径确定 → 执行 ExtractAsync(target)
            └─ 没找到 → fallback 到文件夹选择对话框
```

### 关键组件

1. **ExplorerWindowTracker**（来自 `quick-path-control.md` 设计，尚未实现）
   - `Type.GetTypeFromProgID("Shell.Application")` + `dynamic`
   - 遍历 `shell.Windows()`，按 `FullName.Contains("explorer.exe")` 过滤
   - 解析 `LocationURL`（`file:///C:/My%20Folder` → `C:\My Folder`）
   - 跳过 `::{GUID}` 特殊文件夹
   - 增加 Desktop 路径：`Environment.GetFolderPath(Environment.SpecialFolder.Desktop)`

2. **Marker 文件**
   - 0 字节（内容不重要，只用作信标）
   - 文件名：`__MZ_{Guid.NewGuid():N}.marker`
   - 创建于 `Path.GetTempPath()` 下
   - 拖完后删除（源和目标的都删）

3. **扫描逻辑**
   - `Directory.EnumerateFiles(path, "__MZ_*.marker", SearchOption.TopDirectoryOnly)`
   - 每次 `DoDragDrop` 返回后启动
   - 有失败时重试：每 100ms 一次，最多 2s（对抗 AV/网络延迟）
   - 扫描范围：一级子目录也扫（应对拖到子文件夹图标上的场景）

---

## 边界问题分析

| # | 问题 | 影响面 | 处置 |
|---|------|--------|------|
| 1 | **拖到 Explorer 内的子文件夹图标** | marker 在 `C:\Users\A\Docs\` 而不是 `C:\Users\A\` | 扫描一级子目录；仍不到则 fallback |
| 2 | **拖到桌面** | Desktop 不在 ShellWindows 枚举中 | 显式加入 Desktop 路径到扫描列表 |
| 3 | **拖到非 Explorer 目标**（压缩文件夹、其他应用） | marker 不在任何已知路径 | **必 fallback** |
| 4 | **拖拽途中用户开了新 Explorer 窗口** | 新窗口不在捕获列表 | 概率极低，接受 |
| 5 | **AV 延迟 / 网络路径** | marker 未及时写入 | 100ms 重试 × 20 次 = 2s |
| 6 | **用户点了取消拖拽** | `DoDragDrop` 返回 `DragDropEffects.None`，文件没被复制 | 检测 `DragDropEffects`，是 None 则跳过扫描 |
| 7 | **多个 Explorer 窗口同时触发** | 可能同时扫到 marker 在多个位置 | 限制：只认 `FileSystemWatcher` 或 `EnumerateFiles` 第一个找到的 |

---

## 两种备选方案对比

### 方案 A：Marker 文件 + 目录扫描（本方案）

| 维度 | 评价 |
|------|------|
| 实现复杂度 | 🟡 中（ExplorerWindowTracker + 扫描逻辑，约 1-2h） |
| 覆盖率 | 70-80%（Explorer 主区域 + Desktop + 一级子目录） |
| 性能 | 🟢 极低（0 字节 marker 创建 + `EnumerateFiles` 扫描） |
| 用户体验 | ✅ 无感（成功时）；⛔ 失败时弹对话框（打断感） |
| 可靠性 | 🟡 边界较多，但 fallback 兜底 |

### 方案 B：Active Explorer 窗口检测（简化版）

```
DoDragDrop 返回后 → 检查前台 Explorer 窗口路径 → 直接提取到那里
```

```csharp
var activePath = GetActiveExplorerPath(); // Shell.Application COM
if (activePath != null)
    await ExtractAsync(activePath, items);
else
    ShowFolderPicker();
```

| 维度 | 评价 |
|------|------|
| 实现复杂度 | 🟢 极低（仅 `GetActiveExplorerPath`，0.5h） |
| 覆盖率 | 60%（如果用户拖到非前台窗口或桌面则失效） |
| 可靠性 | 🟢 逻辑简单，几乎无 Bug |

### 方案 C：VFDO（已有计划）

| 维度 | 评价 |
|------|------|
| 实现复杂度 | 🔴 高（COM IDataObject + GlobalAlloc P/Invoke，4-6h） |
| 无感度 | ✅ 完全无感（Explorer 按需请求文件内容） |
| 可靠性 | ✅ COM 标准协议，Explorer 原生支持 |
| 核心价值 | 避免两次 IO，与 target 发现无关 |

---

## 建议的决策标准

| 如果你的优先级是 | 选 |
|-----------------|-----|
| 快速实现一个"拖到 Explorer 自动提取" | **方案 B** — 最简，30min 搞定，配合当前急加载模式 |
| 覆盖更多场景但可接受 fallback | **方案 A（本方案）** — marker 文件 + 目录扫描 |
| 彻底解决拖拽体验，不关心实现成本 | **VFDO（方案 C）** — 真正的延迟渲染 |
| 想先做最简单的，不行再升级 | **方案 A 作为 B 的增强** — 以 B 为基础，加 marker 扫描作为锦上添花 |

---

## 关联依赖

- `ExplorerWindowTracker` — 与 `quick-path-control.md` 复用同一 COM 封装
- `MainWindow.DragDrop.cs` — 修改拖出流程
- `ArchiveEngine.ExtractAsync` — 提取到指定目录（现有功能）

## 参考实现

- Shell.Application COM 枚举：[Winhance](https://github.com/memstechtips/Winhance/blob/main/src/Winhance.Infrastructure/Features/Common/Services/ExplorerWindowManager.cs)
- 7-Zip 源代通过 `IShellExtInit::Initialize` 接收 `pidlFolder`（此为 Shell Extension 标准做法，与拖拽不同）
- StackOverflow: [Get file path for drag n drop file on Windows Explorer](https://stackoverflow.com/questions/20971127/get-file-path-for-drag-n-drop-file-on-windows-explorer)
