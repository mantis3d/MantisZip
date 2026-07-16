# 图标替换计划：emoji → PathIcon (Fluent UI System Icons)

## 目标

将 Avalonia 版 (`MantisZip.UI.Avalonia`) 中所有 emoji 文本图标替换为 Avalonia 原生 `PathIcon` 控件，图标数据来自 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)（微软官方开源，MIT 协议）。

## 设计决策

| 决策点 | 选择 | 理由 |
|-------|------|------|
| 图标来源 | Fluent UI System Icons (filled, 24px) | Windows 原生风格，与 Fluent Theme 匹配；MIT 协议；高一致性 |
| 实现方式 | `PathIcon` + `Data` 资源键 | 零运行时依赖，Avalonia 原生，自动继承 Foreground 颜色 |
| 风格选择 | `filled` 系列 | 工具栏/菜单等小尺寸场景 filled 辨识度更高 |
| 大小选择 | 24px（viewBox） | 项目现有图标使用 FontSize 14，24px filled 缩放后清晰度好 |

## 前置条件

实施前确保：
1. 项目可正常构建运行（`dotnet build` + `dotnet run`）
2. 已了解当前所有 emoji 使用位置（已调查完毕，见 "替换清单"）

## 实施步骤

### Step 1：创建图标资源字典 `AppIcons.axaml`

**新文件：** `src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml`

内容结构：
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- 操作类 -->
    <Geometry x:Key="IconOpen">M2 8V6.25...</Geometry>
    <Geometry x:Key="IconFolder">M2 8V6.25...</Geometry>
    <Geometry x:Key="IconRefresh">M12 3.5...</Geometry>
    <!-- ... 其余图标 -->
    
</ResourceDictionary>
```

> **注意：** `PathIcon.Data` 的类型是 `Geometry`，所以资源键直接定义为 `<Geometry>`。

### Step 2：在 `App.axaml` 中合并图标资源字典

在 `App.axaml` 的 `<Application.Resources>` 中添加：

```xml
<ResourceDictionary.MergedDictionaries>
    <!-- 现有主题 -->
    <ResourceInclude Source="avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml" />
    <!-- 新增图标 -->
    <ResourceInclude Source="avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml" />
</ResourceDictionary.MergedDictionaries>
```

### Step 3：替换 XAML 中的 emoji

**模式：** 将每个 `<TextBlock Text="📂" ...>` 替换为：

```xml
<PathIcon Data="{StaticResource IconOpen}" 
          Width="14" Height="14"
          Foreground="{DynamicResource ThemeTextPrimaryBrush}" />
```

**工具栏图标** (ToolbarButtonIcon class) 同理：

```xml
<!-- 替换前 -->
<TextBlock Text="📂" Classes="ToolbarButtonIcon" />
<!-- 替换后 -->
<PathIcon Data="{StaticResource IconOpen}" Classes="ToolbarButtonIcon" />
```

**注意：** 如果 `ToolbarButtonIcon` 样式中有 `FontSize` 或字体相关设置，需要调整为图标大小相关设置，或添加 `Width="16" Height="16"`。

### Step 4：替换 C# 代码中的动态图标

WPF 版有 `MainWindow.UI.cs` 中动态创建 `Emoji.Wpf.TextBlock`。Avalonia 版需要检查：

1. **`MainWindowViewModel.cs`** — 任何动态设置图标的地方
2. **其他 ViewModel/代码-behind** — 创建图标控件的代码

替换模式：
```csharp
// 替换前
return new TextBlock { Text = "📂", FontSize = 14 };

// 替换后
var icon = new PathIcon { Width = 14, Height = 14 };
icon.Data = (Geometry)Application.Current!.FindResource("IconOpen");
return icon;
```

### Step 5：处理格式图标（zip/rar/7z/gz/tar/tgz/iso）

**现状：** WPF 版有 `Resources/Icons/` 下的 7 个 .ico 文件，Avalonia 版无。

**方案 A（推荐）：** 对每个格式添加对应的 `Geometry` 资源键。由于 Fluent UI System Icons 没有特定格式的图标，使用 `Archive` / `Document` 类通用图标：

| 格式 | 资源键 | 映射 |
|------|--------|------|
| zip | `IconZip` | `Archive` 图标 |
| rar | `IconRar` | `Archive` 图标 |
| 7z | `Icon7z` | `Archive` 图标 |
| gz | `IconGz` | `Archive` 图标 |
| tar | `IconTar` | `Archive` 图标 |
| tgz | `IconTgz` | `Archive` 图标 |
| iso | `IconIso` | `Disc` 图标 |

**方案 B（更佳）：** 从 WPF 版现有的 .ico 文件中提取 PNG 位图，转为 `Avalonia.Media.Imaging.Bitmap` 作为 `Image` 控件展示。但 .ico 转 Bitmap 需要额外处理。

**实施建议：** 先实施方案 A（统一 Archive 图标），后续如需区分不同格式再进一步细化。

### Step 6：构建验证

1. `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` — 确保无编译错误
2. 运行后手动检查：
   - 工具栏所有图标可见
   - 菜单项所有图标可见
   - 设置窗口各分类图标正确
   - 空状态大图标正常
   - 对话框图标正常
   - 浅色/深色主题切换后图标颜色正确

## 图标映射表

以下是完整映射清单。**实施时**（而非现在）从 Fluent UI System Icons 仓库提取 SVG 路径：

### 操作类

| 语义 | 资源键 | Fluent UI 图标名 | 当前 emoji |
|------|--------|-----------------|-----------|
| 打开/浏览 | `IconOpen` | `Folder` | 📂 |
| 文件夹 | `IconFolder` | `Folder` | 📁 |
| 搜索 | `IconSearch` | `Search` | 🔍 |
| 刷新 | `IconRefresh` | `ArrowSync` | 🔄 |
| 添加 | `IconAdd` | `Add` | ➕ |
| 复制 | `IconCopy` | `Copy` | 📋 |
| 保存 | `IconSave` | `Save` | 💾 |
| 压缩 | `IconCompress` | `ArchiveSettings` | 📦 |
| 删除 | `IconDelete` | `Delete` | 🗑️ |

### 状态类

| 语义 | 资源键 | Fluent UI 图标名 | 当前 emoji |
|------|--------|-----------------|-----------|
| 成功 | `IconCheckmark` | `Checkmark` | ✅ |
| 警告 | `IconWarning` | `Warning` | ⚠️ |
| 失败/关闭 | `IconDismiss` | `Dismiss` | ❌ |
| 信息 | `IconInfo` | `Info` | ℹ️ |

### 密码类

| 语义 | 资源键 | Fluent UI 图标名 | 当前 emoji |
|------|--------|-----------------|-----------|
| 密码/钥匙 | `IconKey` | `Key` | 🔑 |
| 锁定 | `IconLockClosed` | `LockClosed` | 🔒 |
| 解锁 | `IconLockOpen` | `LockOpen` | 🔓 |
| 加密保护 | `IconShieldLock` | `ShieldLock` | 🔐 |

### 功能类

| 语义 | 资源键 | Fluent UI 图标名 | 当前 emoji |
|------|--------|-----------------|-----------|
| 收藏 | `IconStar` | `Star` | ⭐ |
| 定位 | `IconPin` | `Pin` | 📍 |
| 文件关联 | `IconLink` | `Link` | 🔗 |
| 外观 | `IconPaintBrush` | `PaintBrush` | 🎨 |
| 语言 | `IconGlobe` | `Globe` | 🌐 |
| 统计 | `IconDataBarVertical` | `DataBarVertical` | 📊 |
| 信息面板 | `IconPanelRight` | `PanelRightContract` | 📋 |

### 格式类

| 语义 | 资源键 | Fluent UI 图标名 |
|------|--------|-----------------|
| ZIP | `IconZip` | `ArchiveSettings` |
| RAR | `IconRar` | `ArchiveSettings` |
| 7z | `Icon7z` | `ArchiveSettings` |
| GZ | `IconGz` | `ArchiveSettings` |
| TAR | `IconTar` | `ArchiveSettings` |
| TGZ | `IconTgz` | `ArchiveSettings` |
| ISO | `IconIso` | `Dvd` 或 `Document` |

## SVG 路径提取方式

采用 `filled` 风格 24px SVG 文件，路径位于 `<svg><path d="..." /></svg>` 中。从 Fluent UI System Icons 仓库中提取：

格式：`https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets/{图标名}/SVG/ic_fluent_{图标名_小写}_24_filled.svg`

**提取工具辅助：** 实施阶段编写一个简单脚本（Node.js），用 `fetch` 批量下载并提取 `d` 属性，直接拼接成 XAML 格式输出。

预计每个图标耗时约 2 分钟，全部 ~25 个图标约 50 分钟。

## 替换文件清单

### XAML 文件

| 文件 | 替换数量 | 说明 |
|------|---------|------|
| `Views/MainWindow.axaml` | ~40 处 | 工具栏、菜单、状态栏、列头、空状态 |
| `Views/SettingsWindow.axaml` | ~7 处 | 设置分类图标 |
| `Dialogs/CompressConflictDialog.axaml` | 2 处 | 覆盖/取消按钮 |
| `Dialogs/ConflictDialog.axaml` | 2 处 | 覆盖/取消按钮 |
| `Views/PreviewPanel.axaml` | 需确认 | 图片预览相关 |

### C# 文件（可能含动态图标）

| 文件 | 说明 |
|------|------|
| 需 grep 确认是否有动态创建 TextBlock emoji 的代码 | 已知 WPF 版有但在 Avalonia 版中可能已被 ViewModel 化 |

## 风险与注意事项

1. **PathIcon 的尺寸控制**：PathIcon 没有 `FontSize`，需要用 `Width`/`Height` 控制大小。替换后尺寸可能需要微调以匹配原 emoji 的视觉重量。
2. **ToolbarButtonIcon 类样式**：如果样式中有 `FontSize` 控制，替换为 PathIcon 后可能失效，需要调整样式或添加图标尺寸设置。
3. **深色主题自适应**：PathIcon 自动继承 `Foreground`，只要绑定了 `{DynamicResource ThemeTextPrimaryBrush}` 就可以自适应，无需额外处理。
4. **空状态大图标**：现有 `FontSize="64"` 的 📂 需要替换为 `Width="64" Height="64"` 的 PathIcon。
5. **WPF 版不动**：本计划 **只针对 Avalonia 主力版**，WPF 遗留版保持 emoji + `Emoji.Wpf` 不变，待 WPF 废弃后自然消亡。

## 验收标准

### Phase 1：emoji → PathIcon 替换

- [ ] 所有菜单、工具栏、按钮图标正确显示
- [ ] 设置窗口各分类图标可见
- [ ] 对话框图标正确（冲突对话框、密码对话框等）
- [ ] 浅色/深色主题切换后图标颜色跟随
- [ ] 高 DPI 下图标清晰无锯齿
- [ ] 构建无错误，运行时无异常

---

## Phase 2：文件列表行图标 → Windows 系统原生图标

### 目标

将文件列表每行左侧的图标（当前由 `IconProvider` 用 SkiaSharp 自绘的彩色分类方块），替换为通过 `SHGetFileInfo` 获取的 **Windows 系统原生图标**，与用户在资源管理器中看到的图标一致。

### 现状

| 当前行为 | 目标行为 |
|---------|---------|
| `IconProvider`（`Models/IconProvider.cs`）用 SkiaSharp 绘制统一彩色方块 + 类别符号 | 调用 Win32 `SHGetFileInfo` 获取真正的 Windows 文件图标 |
| .zip → 橙色方块带拉链符号 | .zip → 资源管理器中显示的压缩包图标 |
| .txt → 蓝色方块带三横线 | .txt → 记事本图标 |
| .png → 绿色方块带山+太阳 | .png → 图片查看器图标 |
| 目录 → 金色圆角矩形文件夹 | 目录 → Windows 标准文件夹图标 |
| 需要维护 ~70 种扩展名映射 + 14 种颜色定义 | 由 Shell API 自动处理，无需维护映射表 |

### 方案

**方式：** 在 `IconService` 中添加 Win32 优先路径，失败时回退到现有的 `IconProvider`。

```
IconService.GetFileIcon(ext)
  ├─ Win32 路径: SHGetFileInfo → HICON → SkiaSharp → Avalonia Bitmap
  └─ 回退路径: IconProvider.GetFileIcon(ext)  // 现有 SkiaSharp 自绘
```

### 实现步骤

#### Step 1：新建 Win32IconProvider.cs

新建 `src/MantisZip.UI.Avalonia/Models/Win32IconProvider.cs`，包含：

- P/Invoke 声明：`SHGetFileInfo`（`shell32.dll`）、`DestroyIcon`（`user32.dll`）
- `SHFILEINFO` 结构体
- `GetFileIcon(string extension)` → 返回 `Bitmap?`
- `GetFolderIcon()` → 返回 `Bitmap?`
- `ConcurrentDictionary` 缓存

P/Invoke 声明直接从 WPF 版 `SystemIconHelper.cs` 移植，只需改动 HICON→Bitmap 的转换部分：

```csharp
// WPF 版（旧）：
var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(hIcon, ...);

// Avalonia 版（新）——方式 A（推荐，零新依赖）：
using var skBitmap = SKBitmap.FromImage(SKImage.FromHICON(hIcon));
using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
using var ms = new MemoryStream(data.ToArray());
return new Bitmap(ms);

// Avalonia 版——方式 B（依赖 System.Drawing.Common）：
using var icon = System.Drawing.Icon.FromHandle(hIcon);
using var bmp = icon.ToBitmap();
using var ms = new MemoryStream();
bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
ms.Position = 0;
return new Bitmap(ms);
```

> **推荐方式 A**：项目已有 `SkiaSharp` v3.119.4，`SKImage.FromHICON` 在 Windows 上受支持，零新依赖。

#### Step 2：修改 IconService.cs

```csharp
internal static class IconService
{
    public static Bitmap? GetFileIcon(string extension)
    {
        // Win32 优先
        if (Win32IconProvider.IsSupported)
        {
            var icon = Win32IconProvider.GetFileIcon(extension);
            if (icon != null) return icon;
        }
        // 回退到 SkiaSharp 自绘
        return IconProvider.GetFileIcon(extension);
    }

    public static Bitmap? GetFolderIcon()
    {
        if (Win32IconProvider.IsSupported)
        {
            var icon = Win32IconProvider.GetFolderIcon();
            if (icon != null) return icon;
        }
        return IconProvider.GetFolderIcon();
    }

    public static void ClearCache()
    {
        Win32IconProvider.ClearCache();
        IconProvider.ClearCache();
    }
}
```

`Win32IconProvider.IsSupported` 用静态构造或 lazy 检测当前 OS 是否为 Windows。

#### Step 3：清理可选的冗余代码

实施后可以考虑：
- 保留 `IconProvider.cs` 不变（作为非 Windows 回退）
- `ExtensionCategory` 和 `CategoryColor` 字典在 Windows 上不再使用，但不影响功能
- 不删除任何现有代码，保证零回归风险

### 涉及文件

| 文件 | 改动类型 |
|------|---------|
| `Models/Win32IconProvider.cs` | **新建** |
| `Services/IconService.cs` | **修改**（添加 Win32 优先路径） |
| `Models/IconProvider.cs` | **不变**（保留为回退） |

**调用方（零改动）：**
- `Services/ArchiveService.cs:35`
- `ViewModels/MainWindowViewModel.cs:791`

### 风险与注意事项

1. **HICON 句柄泄漏**：`SHGetFileInfo` 返回的 HICON 必须在转换后调用 `DestroyIcon` 释放。WPF 版已有此处理，可以直接复用。
2. **跨平台兼容**：`Win32IconProvider.IsSupported` 确保非 Windows 系统自动走 SkiaSharp 回退。
3. **缓存策略**：`ConcurrentDictionary` 缓存 HICON 转换后的 `Bitmap` 对象，避免重复调用 Shell API。
4. **主题变更**：Windows 系统图标不会随应用主题变化——这是系统图标的行为，可以接受。
5. **性能**：`SHGetFileInfo` 首次调用时较慢（~5ms），缓存后为 O(1)。

### 验收标准

- [ ] 文件列表中 .zip/.7z/.rar 等压缩包显示真实的 Windows 压缩包图标
- [ ] .txt/.md/.json 等文本文件显示系统关联的文本图标
- [ ] .png/.jpg/.gif 等图片文件显示系统关联的图片图标
- [ ] 目录行显示 Windows 标准文件夹图标
- [ ] 非 Windows 系统自动回退 SkiaSharp 自绘图标（不崩溃、不空白）
- [ ] 大量文件滚动时无性能问题（缓存生效）
- [ ] 构建无错误
- [ ] WPF 版 `SystemIconHelper.cs` 保持不变
