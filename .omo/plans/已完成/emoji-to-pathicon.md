# 图标替换计划：emoji → PathIcon (Fluent UI System Icons)

## 状态：✅ **全部完成**（含 Phase 2）

| Phase | 内容 | 状态 | 完成时间 |
|-------|------|------|---------|
| Phase 1 | emoji → PathIcon XAML/C# 替换 | ✅ **已完成** | 已在先前的开发周期完成 |
| Phase 2 | Win32 原生文件图标 (SHGetFileInfo) | ✅ **已完成** | 已在先前的开发周期完成 |

---

## Phase 1：emoji → PathIcon 替换 ✅

### 完成情况

- **AppIcons.axaml**：在 `Resources/Icons/AppIcons.axaml` 创建，定义了 **67 个** `<Geometry>` 资源键
  - 来源：Fluent UI System Icons (filled, 24px, MIT 协议)
  - 远超原始计划的 ~38 个，覆盖了所有操作、状态、密码、功能、导航等图标
- **App.axaml 合并**：`App.axaml:10` 已合并图标资源字典
- **XAML 替换**：所有 .axaml 文件的菜单、工具栏、对话框、预览按钮均已替换为 `PathIcon`，涉及 ~20+ 个文件
- **C# 动态图标**：PreviewTreeNode、MatchedPasswordDialog、PasswordDialog 等代码动态创建的图标均已替换为 `PathIcon`

### 原始计划与实际情况对比

| 项目 | 原始计划 | 实际情况 |
|------|---------|---------|
| 图标数量 | ~25（主映射）+ ~13（关联补充）= **38 个** | **67 个** Geometry 资源键 |
| SVG 提取 | 编写 Node.js 脚本批量下载 | 未编写脚本，路径数据手动提取 |
| 替换文件 | 5 XAML + 1 C# | ~20+ 文件（全面覆盖） |

### 资源键完整列表

```
IconAdd          IconArrowCollapse  IconArrowExpand   IconArrowFitIn
IconArrowRight   IconArrowUp        IconBug           IconCalendar
IconChat         IconCheckmark      IconChevronLeft   IconChevronRight
IconCompress     IconCopy           IconDataBarVertical IconDelete
IconDismiss      IconDocument       IconDownload      IconEdit
IconExport       IconEye            IconEyedropper    IconEyeOff
IconFolder       IconFontDecrease   IconFontIncrease  IconGlobe
IconGrid         IconHeart          IconHistory       IconHome
IconImport       IconInfo           IconKey           IconLightning
IconLink         IconLocation       IconLockClosed    IconLockOpen
IconMoon         IconNavigation     IconNewFile       IconNext
IconOrientation  IconPaintBrush     IconPanelRight    IconPause
IconPin          IconPlay           IconPrevious      IconProhibited
IconQuestion     IconRefresh        IconRuler         IconSave
IconSearch       IconSelectAll      IconSettings      IconShieldLock
IconSignOut      IconStar           IconSubtract      IconTimer
IconWand         IconWarning        IconWrench
```

### 替换涉及的完整文件清单

| 文件 | 说明 |
|------|------|
| `Views/MainWindow.axaml` | 菜单栏、工具栏、搜索过滤栏、空状态图标 |
| `Views/PreviewPanel.axaml` | 缩放控制、字体控制、GIF 控制、透明背景/Alpha 按钮 |
| `Views/SettingsWindow.axaml` | 10 个设置分类 Tab 图标 |
| `Views/PasswordDialog.axaml` | 密码显示/隐藏切换按钮 |
| `Dialogs/ConflictDialog.axaml` | 覆盖、覆盖旧/小文件、重命名、跳过、暂停、取消按钮 |
| `Dialogs/CompressConflictDialog.axaml` | 覆盖、添加、重命名、跳过按钮 |
| `Dialogs/MatchedPasswordDialog.axaml` | 钥匙图标、显示/隐藏切换、复制按钮 |
| `Dialogs/PasswordManagerWindow.axaml` | 工具栏图标 |
| `Dialogs/IconTestWindow.axaml` | 图标测试窗口自身的展示图标 |
| `Dialogs/LogPrivacyHelpDialog.axaml` | 隐私帮助对话框图标 |
| `Controls/ResultTreeView.axaml` | 紧凑切换按钮、树节点图标、冲突警告图标 |
| `Models/PreviewTreeNode.cs` | IconKey 属性返回 "IconFolder"/"IconDocument"/"IconWarning" |
| `Dialogs/MatchedPasswordDialog.axaml.cs` | 复制成功反馈 PathIcon (IconCheckmark) |

### 未替换的 Unicode 字符（不在图标替换范畴内）

| 位置 | 字符 | 原因 |
|------|------|------|
| `DonationDialog.axaml:57,67` | 🔗 ☕ | 按钮标签文本，非图标；属 UI 文本内容 |
| `PreviewPanel.axaml:448,456` | ◀ ▶ | PDF 翻页按钮功能箭头符号，FontSize=14 文本按钮 |
| `IconTestWindow.axaml` | ✅ ⏳ | 测试窗口状态指示文本 |

---

## Phase 2：文件列表行图标 → Windows 系统原生图标 ✅

### 完成情况

完全按计划实施：

- **新建 `Models/Win32IconProvider.cs`**：P/Invoke `SHGetFileInfo` + `DestroyIcon`，使用 SkiaSharp (`SKImage.FromHICON`) 转换 HICON → Avalonia Bitmap
- **修改 `Services/IconService.cs`**：添加 Win32 优先路径，失败时回退到 `IconProvider`（SkiaSharp 自绘）
- **`Models/IconProvider.cs`**：不变，保留为非 Windows 回退
- **调用方零改动**：`ArchiveService`、`MainWindowViewModel` 继续使用 `IconService` 接口

### 实现细节

```
IconService.GetFileIcon(ext)
  ├─ Win32 路径: SHGetFileInfo → HICON → SKImage.FromHICON → PNG → Bitmap
  └─ 回退路径: IconProvider.GetFileIcon(ext)  // SkiaSharp 自绘
```

- 使用 `OperatingSystem.IsWindows()` 静态检测，非 Windows 自动走回退
- `ConcurrentDictionary` 缓存结果
- HICON 句柄转换后立即调用 `DestroyIcon` 释放，无泄漏风险

---

## 验收标准检查

### Phase 1

- [x] 所有菜单、工具栏、按钮图标正确显示
- [x] 设置窗口各分类图标可见
- [x] 对话框图标正确（冲突对话框、密码对话框等）
- [x] 浅色/深色主题切换后图标颜色跟随（PathIcon 自动继承 Foreground）
- [x] 高 DPI 下图标清晰无锯齿（矢量 Geometry）
- [x] 构建无错误，运行时无异常

### Phase 2

- [x] 文件列表中 .zip/.7z/.rar 等压缩包显示真实的 Windows 压缩包图标
- [x] .txt/.md/.json 等文本文件显示系统关联的文本图标
- [x] .png/.jpg/.gif 等图片文件显示系统关联的图片图标
- [x] 目录行显示 Windows 标准文件夹图标
- [x] 非 Windows 系统自动回退 SkiaSharp 自绘图标（不崩溃、不空白）
- [x] 大量文件滚动时无性能问题（缓存生效）
- [x] 构建无错误
- [x] WPF 版 `SystemIconHelper.cs` 保持不变
