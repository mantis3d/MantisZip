# 跨平台移植实施计划

> WPF→Avalonia 迁移已完成（Phases 0-10），本文档规划 macOS / Linux 平台支持的实施路径。
> **历史调研**: [cross-platform-port-research.md](cross-platform-port-research.md)（2026-06-11 迁移前可行性研究）
> **状态**: 📋 待实施 | **当前版本**: 0.5.0
> **创建日期**: 2026-09-07

---

## 前提与范围

### 已完成（不需要再做）

| 项目 | 状态 | 说明 |
|------|------|------|
| WPF → Avalonia UI 迁移 | ✅ 完成 | 21 个窗口全部迁移，Phases 0-10 |
| WebView2 → 原生渲染 | ✅ 完成 | HTML→Markdig 控件树；PDF→PdfPig+SkiaSharp；SVG→Svg.Skia |
| GIF 动画 | ✅ 完成 | 自实现 `GifDecoder`，无第三方依赖 |
| DPAPI → AES-GCM | ✅ 完成 | `AesGcmDataProtector` + `IDataProtector` 接口 |
| 图像解码 | ✅ 完成 | Avalonia Bitmap + SkiaSharp |
| Animated WebP | ✅ 完成 | SKCodec 统一解码 |
| Office 内容预览 | ✅ 完成 | DOCX/XLSX/PPTX（WPF 版只有元数据） |
| 紧凑度/主题/拖拽添加 | ✅ 完成 | Avalonia 原生跨平台 |

### 跨平台后的范围决策

**砍掉（不移植）**：
- ShellExt COM 右键菜单（`MantisZip.ShellExt` 项目）— 100% Windows 专有
- Explorer 窗口追踪（`ExplorerWindowTracker.cs`）— 无等价物
- Win32 覆层拖拽解压（`OverlayController` + `CustomOleDragDrop`）— 重写成本极高，降级为右键菜单解压

**需要移植**：
- 7z 引擎适配（读取/压缩的平台分支）
- 系统图标获取（`Win32IconProvider` → 平台原生）
- 文件关联注册（Registry → 平台原生）
- 路径/配置适配（`%LOCALAPPDATA%` → XDG / macOS）
- CI/CD 流水线 + 安装包打包
- 拖拽添加（drag-in）— Avalonia 原生基本可工作，需测试

---

## Windows 现有代码的平台依赖清单

### P/Invoke 分布（95 个声明，6 个文件）

| 文件 | P/Invoke 数 | 功能 | macOS/Linux 需要？ |
|------|:-----------:|------|:------------------:|
| `Services/NativeMethods.cs` | ~40 | 窗口管理、光标、OLE、DWM | ❌ 拖拽解压降级后不需要 |
| `Services/OverlayController.cs` | ~15 | Win32 覆层窗口渲染 | ❌ 同上 |
| `Services/CustomOleDragDrop.cs` | — | 自实现 OLE IDataObject/IDropSource | ❌ 同上 |
| `Services/DropTargetDetector.cs` | — | WindowFromPoint + ShellWindows | ❌ 同上 |
| `Services/ShellIntegration.cs` | 1 | SHEmptyRecycleBinW | ❌ ShellExt 砍掉 |
| `Models/Win32IconProvider.cs` | 2 | SHGetFileInfo + DestroyIcon | ✅ 需替换 |
| `Utils/ExplorerWindowTracker.cs` | 7 | ShellWindows 枚举 | ❌ 砍掉 |
| `App.axaml.cs` | 2 | GetConsoleWindow + SetConsoleTitle | ❌ 可选，可砍 |

### 注册表依赖

| 文件 | 功能 | 跨平台方案 |
|------|------|-----------|
| `Services/ShellIntegration.Menu.cs` | 右键菜单安装/卸载 | ❌ 砍掉（macOS/Linux 不走此路径） |
| `Services/ShellIntegration.Assoc.cs` | 文件关联注册 | macOS: `Info.plist` UTI；Linux: `mimeinfo` |
| `Services/ShellIntegration.cs` | 公共 Registry 工具方法 | 删除或条件编译 |
| `App.axaml.cs` | 读取 SevenZipPath 设置 | 改为跨平台配置路径 |

### System.Drawing.Common

| 文件 | 用途 | 跨平台影响 |
|------|------|-----------|
| `Models/Win32IconProvider.cs` | HICON → PNG 转换 | 需替换为平台原生图标 API |

---

## 实施阶段

### Phase 0: 基础设施（3-5 天）

**目标**：CI 能在 macOS/Linux 上构建和测试。

| 任务 | 说明 | 依赖 |
|------|------|------|
| CI 添加 macOS/Linux runner | `release.yml` + `ci.yml` 加矩阵 `matrix.os: [windows-latest, macos-latest, ubuntu-latest]` | 无 |
| 路径适配 | `AppSettings.cs`、`WindowStateManager.cs` 等 9 个文件中的 `Environment.GetFolderPath` 需加平台分支 | 无 |
| `LogRedactor` 路径正则 | 加 `/` 路径匹配分支 | 无 |
| 7z.dll 检测逻辑 | `App.axaml.cs` 中 7z.dll 路径探测加 `RuntimeInformation.IsOSPlatform` 分支 | 无 |
| 构建验证 | `dotnet build` 在三个平台通过 | 上述改动 |

**验收标准**：`dotnet build` + `dotnet test` 在 Windows/macOS/Linux 三平台 CI 通过。

### Phase 1: 引擎层适配（1-2 周）

**目标**：压缩/解压在 macOS/Linux 可用。

| 任务 | 说明 | 工作量 |
|------|------|--------|
| 7z 读取验证 | 测试 `SharpCompress.SevenZipArchive` 在 macOS/Linux 上读取 7z 文件（加密/固实/非固实），记录兼容性差异 | 3 天 |
| 7z 写入策略 | 选项 A: UI 标注"macOS/Linux 7z 压缩需安装 p7zip" + CLI 调用；选项 B: 禁用 7z 压缩（只支持 ZIP+tar.gz）。**需要决策** | 1 天决策 + 2 天实现 |
| RAR 读取验证 | 测试 `SharpCompress.RarArchive` 在非 Windows 上的 RAR5/RAR4/加密兼容性 | 2 天 |
| `ArchiveEntryExtractor` 平台分支 | 7z/RAR 单项提取的 SharpSevenZip 调用改为 SharpCompress 分支 | 2 天 |
| `PasswordService` 异常分类 | `ClassifySharpSevenZipException` 中 HRESULT 判断仅 Windows 有效，需加 SharpCompress 异常分支 | 1 天 |

**验收标准**：macOS/Linux 上能压缩 ZIP/tar.gz（+ 可选 7z），能解压 ZIP/7z/RAR/tar.gz，预览提取正常。

### Phase 2: UI 平台适配（2-3 周/平台）

**目标**：核心 UI 功能在 macOS/Linux 可用。

#### macOS 专项

| 任务 | 说明 | 工作量 |
|------|------|--------|
| 系统图标 | `Win32IconProvider` → macOS `NSWorkspace` 或 Avalonia 内置文件类型图标 | 3 天 |
| 文件关联 | `ShellIntegration.Assoc` → macOS `Info.plist` UTI 注册（需 `.app` bundle 结构） | 3 天 |
| DMG/PKG 打包 | CI 生成 macOS 安装包（`dotnet publish -r osx-x64` + `create-dmg` 或 `productbuild`） | 2 天 |
| 拖拽添加测试 | Avalonia 原生 DragDrop 在 macOS 上的行为验证 | 1 天 |
| 路径验证 | `~/Library/Application Support/MantisZip` 确保读写正确 | 0.5 天 |

#### Linux 专项

| 任务 | 说明 | 工作量 |
|------|------|--------|
| 系统图标 | freedesktop 图标主题查找（`/usr/share/icons` 或 `~/.local/share/icons`） | 2 天 |
| 文件关联 | `.desktop` 文件 + `mimeinfo` 注册 | 2 天 |
| AppImage/DEB 打包 | CI 生成 Linux 安装包（`dotnet publish -r linux-x64` + AppImage 或 dpkg-deb） | 2 天 |
| 拖拽添加测试 | Avalonia 原生 DragDrop 在 Linux X11/Wayland 上的行为验证 | 1 天 |
| 路径验证 | `~/.local/share/MantisZip` 确保读写正确 | 0.5 天 |

**验收标准**：macOS/Linux 上能正常安装、启动、浏览压缩包、预览、压缩、解压、文件关联双击打开。

### Phase 3: 打磨与发布（1 周）

| 任务 | 说明 |
|------|------|
| macOS `.app` bundle 结构 | `Contents/MacOS/` + `Contents/Resources/` + `Info.plist` |
| Linux `.desktop` 文件 | 图标、分类、MIME 类型 |
| 持久化配置跨平台验证 | `settings.json`、`metadata-panel.json`、密码库在三个平台的读写路径 |
| 便携模式跨平台 | `Portable.txt` 在 macOS/Linux 上的 `Data/` 目录位置 |
| 文档更新 | README 多平台系统要求、安装指南 |

---

## 策略决策点（需要你确认）

### 决策 1: 7z 压缩在 macOS/Linux 上怎么做？

| 选项 | 优点 | 缺点 |
|------|------|------|
| **A. 禁用** — UI 只显示 ZIP + tar.gz | 最简单，无外部依赖 | 功能降级 |
| **B. p7zip CLI** — 检测到 p7zip 时启用 7z 压缩 | 功能完整 | 需用户安装 p7zip，进程调用有进程管理开销 |
| **C. 等待** — 等社区出纯托管 7z 编码器 | 优雅 | 遥遥无期 |

**我的建议**: 选项 A（先禁用）+ 选项 B 作为后续增强。先把 macOS/Linux 能跑的基础版做出来，7z 压缩按需再加。

### 决策 2: 拖拽解压（drag-out）怎么处理？

| 选项 | 说明 |
|------|------|
| **A. 砍掉** — macOS/Linux 不支持拖拽解压，用户通过右键菜单或工具栏解压 | 最简单 |
| **B. 简化版** — 用 Avalonia 原生 DragDrop（无 Win32 覆层），功能降级但可用 | 中等 |
| **C. 完整重写** — 用 NSDragSession / GTK DnD + 平台原生覆层 | 成本极高 |

**我的建议**: 选项 A（先砍掉）。拖拽解压是锦上添花，核心功能（压缩/解压/预览）优先。

### 决策 3: macOS/Linux 右键菜单要不要做？

| 选项 | 说明 |
|------|------|
| **A. 不做** — 用户通过应用内操作或文件关联双击打开 | 最简单 |
| **B. FinderSync（macOS）+ .desktop actions（Linux）** | 原生体验但开发量大 |

**我的建议**: 选项 A（先不做）。右键菜单是 Windows 深度集成的产物，macOS/Linux 用户习惯不同。

---

## 工作量汇总

| 阶段 | 范围 | 预估工时 |
|------|------|---------|
| Phase 0 基础设施 | CI + 路径适配 | 3-5 天 |
| Phase 1 引擎适配 | 7z/RAR 跨平台 + 降级策略 | 1-2 周 |
| Phase 2 UI 适配 | macOS 专项 + Linux 专项 | 2-3 周/平台 |
| Phase 3 打磨发布 | 打包 + 文档 | 1 周 |
| **合计（单平台）** | | **4-6 周** |
| **合计（双平台）** | | **6-8 周**（部分任务可并行） |

> 注意：以上为单人全职开发估算。如果 Core 层跨平台验证顺利（Phase 1），实际可能更快。

---

## 风险

| 风险 | 程度 | 缓解 |
|------|------|------|
| SharpCompress 7z 读取兼容性不如 7z.dll | 🟡 中 | Phase 1 充分测试，记录差异 |
| macOS `.app` bundle 签名/公证 | 🟡 中 | 先不做签名，用户手动允许运行 |
| Linux 发行版碎片化（Wayland/X11/glibc 版本） | 🟡 中 | AppImage 自包含，减少依赖 |
| Avalonia 在 macOS/Linux 的控件行为差异 | 🟢 低 | Phase 0 先跑通验证 |
| p7zip 在不同发行版的路径不同 | 🟢 低 | `which p7zip` 动态检测 |

---

## Related

- [cross-platform-port-research.md](cross-platform-port-research.md) — 迁移前可行性研究（历史参考）
- [selfcontained-size-optimization.md](selfcontained-size-optimization.md) — 自包含体积优化（跨平台后更关键）
- [new-format-support.md](new-format-support.md) — 新增压缩格式（BZip2/XZ/Zstd 用 SharpCompress 无外部依赖，跨平台友好）
