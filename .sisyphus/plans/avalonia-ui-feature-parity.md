# Avalonia: UI 功能补齐（对话框 + 控件 + 转换器）

> **Status**: 📋 Planned | **Target**: v0.4.5
> **分支**: `avalonia-port`
> **前置依赖**: `uac-elevation-permission.md` (WPF 已实现)

## 概述

将 WPF 项目中存在但 Avalonia 尚未移植的 UI 功能补齐。包括 8 个对话框窗口、2 个自定义控件、1 个转换器。这些功能与 Shell/COM 无关，纯 UI 层面。

## 要移植的对话框

| 对话框 | WPF 源文件 | 功能 | 优先级 |
|--------|----------|------|--------|
| ElevationDialog | `Dialogs/ElevationDialog.xaml(.cs)` | UAC 提权询问 | P0 |
| ElevationFailedDialog | `Dialogs/ElevationFailedDialog.xaml(.cs)` | 提权失败提示 | P0 |
| ElevationInfoDialog | `Dialogs/ElevationInfoDialog.xaml(.cs)` | 权限不足信息 | P0 |
| AddFavoriteDialog | `Dialogs/AddFavoriteDialog.xaml(.cs)` | 添加收藏路径 | P1 |
| FavoriteManagerWindow | `Dialogs/FavoriteManagerWindow.xaml(.cs)` | 管理收藏路径 | P1 |
| QuickPathDialog | `Dialogs/QuickPathDialog.xaml(.cs)` | 快速路径选择 | P1 |
| QuickPathPreDialog | `Dialogs/QuickPathPreDialog.xaml(.cs)` | 预选路径对话框 | P1 |
| ArchiveCommentDialog | `Dialogs/ArchiveCommentDialog.xaml(.cs)` | 编辑 ZIP 注释 | P2 |
| ArchiveSaveAsDialog | `Dialogs/ArchiveSaveAsDialog.xaml(.cs)` | 压缩包另存为 | P2 |
| UnifiedExtractDialog | `Dialogs/UnifiedExtractDialog.xaml(.cs)` | 统一解压对话框 | P2 |
| AppMessageBox | `AppMessageBox.xaml(.cs)` | 自定义消息弹窗 | P2 |

## 要移植的控件

| 控件 | WPF 源文件 | 功能 | 优先级 |
|------|----------|------|--------|
| DynamicFormatOptionsPanel | `Controls/DynamicFormatOptionsPanel.xaml(.cs)` | 压缩格式动态选项 (7z 固实块等) | P2 |
| QuickPathControl | `Controls/QuickPathControl.xaml(.cs)` | 快速路径输入控件 | P1 |

## 要移植的转换器

| 转换器 | WPF 源文件 | 功能 |
|--------|----------|------|
| BatchStatusConverters | `Converters/BatchStatusConverters.cs` | 批量状态显示转换器 |

## 文件变更清单

### 新建文件（对话框）

```
src/MantisZip.UI.Avalonia/Dialogs/
├── ElevationDialog.axaml           # 提权询问对话框
├── ElevationDialog.axaml.cs
├── ElevationFailedDialog.axaml     # 提权失败对话框
├── ElevationFailedDialog.axaml.cs
├── ElevationInfoDialog.axaml       # 权限不足信息对话框
├── ElevationInfoDialog.axaml.cs
├── AddFavoriteDialog.axaml         # 添加收藏路径对话框
├── AddFavoriteDialog.axaml.cs
├── FavoriteManagerWindow.axaml     # 收藏管理器窗口
├── FavoriteManagerWindow.axaml.cs
├── QuickPathDialog.axaml           # 快速路径选择对话框
├── QuickPathDialog.axaml.cs
├── QuickPathPreDialog.axaml        # 预选路径对话框
├── QuickPathPreDialog.axaml.cs
├── ArchiveCommentDialog.axaml      # 压缩包注释编辑
├── ArchiveCommentDialog.axaml.cs
├── ArchiveSaveAsDialog.axaml       # 另存为对话框
├── ArchiveSaveAsDialog.axaml.cs
├── UnifiedExtractDialog.axaml      # 统一解压对话框
├── UnifiedExtractDialog.axaml.cs
```

### 新建文件（控件）

```
src/MantisZip.UI.Avalonia/Controls/
├── DynamicFormatOptionsPanel.axaml
├── DynamicFormatOptionsPanel.axaml.cs
├── QuickPathControl.axaml
├── QuickPathControl.axaml.cs
```

### 新建文件（转换器）

```
src/MantisZip.UI.Avalonia/Converters/
├── BatchStatusConverters.cs
```

### 修改文件

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs` | 添加 Elevation/Favorites/QuickPath 相关命令和回调 |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml` | 添加菜单项绑定（收藏、快速路径等） |
| `src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs` | 注册新对话框回调 |
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 添加 Elevation 提权重启逻辑 |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | 新增对话框相关 key |
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | 新增对话框相关 key |

## 对话框移植参考

### Elevation 系列对话框（P0）

WPF 参考: `uac-elevation-permission.md` 计划文档

三个对话框的交互流程：
1. **ElevationDialog**: 操作中遇到 `UnauthorizedAccessException` → 询问是否以管理员身份重启
   - AllowElevation=true 时显示
   - 点确认 → 以管理员身份重启当前进程（传递相同的 CLI 参数）
   - 点取消 → 降级为 ElevationInfoDialog 模式
2. **ElevationFailedDialog**: 已提权但仍失败 → 提示用户手动处理
3. **ElevationInfoDialog**: 权限不足，但不允许提权 → 显示哪些文件/目录被跳过

移植要点：
- 使用 `Process.Start` 重启自身（`Environment.ProcessPath` + `runas` verb）
- 不调用 `app.Shutdown()`，batch 继续处理
- Avalonia 中无 `System.Windows.MessageBox` → 用原生窗口代替

### Favorites 收藏夹（P1）

- `AddFavoriteDialog`: 输入路径 + 名称，添加到收藏列表
- `FavoriteManagerWindow`: 列表展示、重命名、删除、排序
- 数据持久化：AppSettings 或独立 JSON 文件
- Avalonia 中已有 `AppSettings.cs` → 添加收藏列表属性

### QuickPath 快速路径（P1）

- `QuickPathControl`: AutoCompleteBox + 历史记录下拉
- `QuickPathPreDialog`: 启动时选择路径（如 7z.dll 选择）
- `QuickPathDialog`: 通用路径选择
- 移植要点：Avalonia 自带 `AutoCompleteBox`

### ArchiveCommentDialog（P2）

- 编辑 ZIP EOCD 注释
- 注意：仅 ZIP 格式支持，需要在加载压缩包后启用
- 通过 `ZipFile.BeginUpdate()` + `SetComment()` + `CommitUpdate()` 实现

### ArchiveSaveAsDialog（P2）

- 压缩包内复制/重命名条目（另存为新文件）
- 解压后重新压缩，或使用 SharpCompress API 选择性复制

### AppMessageBox（P2）

- WPF 自定义 MessageBox 样式
- Avalonia: 可用原生 `TextBlock` + `Button` 简单替代
- 或使用自定义 Window 模拟

## 任务分解

### Task 1: Elevation 系列对话框移植（P0）

- [ ] 创建 `ElevationDialog.axaml` + `.axaml.cs`
  - 布局：图标 + 说明文本 + 路径列表 + 确认/取消按钮
  - 主题绑定：`Background="{DynamicResource ThemeWindowBgBrush}"`
- [ ] 创建 `ElevationFailedDialog.axaml` + `.axaml.cs`
- [ ] 创建 `ElevationInfoDialog.axaml` + `.axaml.cs`
- [ ] 在 `App.axaml.cs` 中添加 Elevation 重启逻辑
  - `RestartAsAdmin(string[] args)` 方法
  - 使用 `Process.Start` + `runas` verb
- [ ] 在相关 CLI handler 中集成 Elevation 逻辑

### Task 2: Favorites 收藏夹移植（P1）

- [ ] 在 `AppSettings.cs` 添加 `FavoritePaths` 列表属性
- [ ] 创建 `AddFavoriteDialog.axaml` + `.axaml.cs`
  - 输入路径（带浏览按钮）+ 名称 + 确定/取消
- [ ] 创建 `FavoriteManagerWindow.axaml` + `.axaml.cs`
  - ListBox + 重命名/删除/上移/下移按钮
- [ ] 在 `MainWindowViewModel.cs` 添加收藏夹命令
- [ ] 在 `MainWindow.axaml` 菜单中添加收藏夹入口

### Task 3: QuickPath 移植（P1）

- [ ] 创建 `QuickPathControl.axaml` + `.axaml.cs`
  - TextBox + AutoCompleteBox 结合
  - 历史记录建议
- [ ] 创建 `QuickPathDialog.axaml` + `.axaml.cs`
  - 使用 QuickPathControl
- [ ] 创建 `QuickPathPreDialog.axaml` + `.axaml.cs`
  - 启动时路径选择（用于 7z.dll 定位等）

### Task 4: 次要对话框移植（P2）

- [ ] 创建 `ArchiveCommentDialog.axaml` + `.axaml.cs`
  - 多行 TextBox + 保存/取消
  - 通过 `ArchiveService` 调用 SharpCompress API
- [ ] 创建 `ArchiveSaveAsDialog.axaml` + `.axaml.cs`
- [ ] 创建 `UnifiedExtractDialog.axaml` + `.axaml.cs`

### Task 5: 控件移植（P2）

- [ ] 创建 `DynamicFormatOptionsPanel.axaml` + `.axaml.cs`
  - 根据压缩格式动态显示选项
  - 7z: 固实块大小、加密方法等
  - ZIP: 编码选择等
- [ ] 创建 `QuickPathControl.axaml` + `.axaml.cs`（如 Task 3 未做）

### Task 6: AppMessageBox + 转换器移植（P2）

- [ ] 创建 Avalonia 版 AppMessageBox（或确认是否可用原生替代已足够）
- [ ] 创建 `BatchStatusConverters.cs`

### Task 7: 集成 + 菜单绑定

- [ ] 在 `MainWindowViewModel.cs` 注册新对话框回调
- [ ] 在 `MainWindow.axaml` 添加菜单项
- [ ] 补全中英文 localization keys

## 验证标准

- [ ] 所有对话框可正常打开/关闭
- [ ] Elevation 系列对话框在权限不足场景正确触发
- [ ] 收藏夹新建/删除/排序正常工作，数据持久化
- [ ] QuickPathControl 显示历史建议
- [ ] ArchiveCommentDialog 能读取和写入 ZIP 注释
- [ ] `dotnet build` 通过
