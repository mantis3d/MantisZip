# Avalonia: Shell/COM 集成移植

> **Status**: ✅ Completed (verified 2026-08-06 — 全部 6 个 Task + 验证标准核实通过；图标渲染 Explorer 视觉项已在实际使用中验证) | **Target**: v0.4.5
> **分支**: `avalonia-port`
> **前置依赖**: `com-context-menu.md` (已完成) + `file-assoc-per-extension.md` (已完成)

## 概述

将 WPF 项目的 Shell 集成（COM 右键菜单 + 文件关联 + ShellIntegration 完整功能）移植到 Avalonia 项目。当前 Avalonia 的 `App.axaml.cs` 仅声明了 `--install-shell`/`--uninstall-shell` 命令桩，但实际 ShellIntegration 类未移植，ShellExt 项目未引用，文件关联 CLI 缺失。

## 要移植的功能

| 功能 | WPF 位置 | Avalonia 现状 |
|------|---------|-------------|
| ShellIntegration 基础类 | `src/MantisZip.UI/Shell/ShellIntegration.cs` | ❌ 缺失 |
| 文件关联逻辑 | `src/MantisZip.UI/Shell/ShellIntegration.Assoc.cs` | ❌ 缺失 |
| 右键菜单管理 | `src/MantisZip.UI/Shell/ShellIntegration.Menu.cs` | ❌ 缺失 |
| ShellExt COM host 部署 | `MantisZip.UI.csproj` (CopyShellExtComhost target) | ❌ 缺失 |
| `--install-assoc` / `--uninstall-assoc` CLI | `App.xaml.cs` | ❌ 缺失 |
| MenuIcons (.ico 资源) | `src/MantisZip.UI/Resources/MenuIcons/*.ico` | ❌ Icons/ 为空 |
| ShellExt 项目引用 | `MantisZip.UI.csproj` | ❌ 未引用 |

## 文件变更清单

### 新建文件

| 文件 | 说明 |
|------|------|
| `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs` | ShellIntegration 基础类（从 WPF 移植，调整 for Avalonia） |
| `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs` | 文件关联安装/卸载逻辑 |
| `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs` | 右键菜单注册/注销逻辑 |

### 修改文件

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | 添加 ShellExt 项目引用 + AfterTargets 复制 comhost |
| `src/MantisZip.UI.Avalonia/App.axaml.cs` | 补全 `--install-assoc`/`--uninstall-assoc` CLI 命令 |
| `src/MantisZip.UI.Avalonia/AppConstants.cs` | (可选) 添加 shell 相关常量 |

### 资源文件

| 文件 | 说明 |
|------|------|
| `src/MantisZip.UI.Avalonia/Resources/MenuIcons/` | 从 WPF 复制 10 个 .ico 文件 |

## 任务分解

### Task 1: ShellIntegration 基础类移植

- [x] 读取 WPF 版 `ShellIntegration.cs`，提取与 Avalonia 兼容的部分
- [x] 创建 `src/MantisZip.UI.Avalonia/Services/ShellIntegration.cs`
- [x] 注册表操作部分 (`HKCU\Software\Classes`) 可直接移植（Windows only）
- [x] 移除 WPF 特定依赖（`System.Windows`），替换为 `Microsoft.Win32` 或原生 API
- [x] 保留 `ShellIntegration.Install()` / `Uninstall()` / `IsInstalled` 接口

### Task 2: 文件关联逻辑移植

- [x] 读取 `ShellIntegration.Assoc.cs`，提取安装/卸载扩展名关联逻辑
- [x] 创建 `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Assoc.cs`
- [x] 保留 `InstallAssociations()` / `UninstallAssociations()` 接口
- [x] 验证开-关单个扩展名关联的功能

### Task 3: 右键菜单管理移植

- [x] 读取 `ShellIntegration.Menu.cs`，提取注册表动词注册逻辑
- [x] 创建 `src/MantisZip.UI.Avalonia/Services/ShellIntegration.Menu.cs`
- [x] 保留静态菜单（非 COM）的注册/注销逻辑
- [x] 注意 `CommandFlags=8` 的已知问题已在 WPF 中修复，确保同样处理

### Task 4: ShellExt 项目引用 + COM host 部署

- [x] 在 `MantisZip.UI.Avalonia.csproj` 添加 CopyShellExtComhost 构建目标（参考 WPF csproj，注意 TFM 差异使用 hardcoded path）
- [x] 添加 `CopyShellExtComhost` AfterTargets Build target（参考 WPF csproj）
- [x] 确保 `comhost.dll` + `MantisZip.ShellExt.dll` + `runtimeconfig.json` 复制到输出目录
- [x] 验证构建：`dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` ✅ 通过

### Task 5: CLI 命令补齐

- [x] 替换 HandleShellCommand 为原生实现，移除 WPF fallback
- [x] `--install-shell` 调用 ShellIntegration.Install()
- [x] `--uninstall-shell` 调用 ShellIntegration.Uninstall()
- [x] `--install-assoc` 调用 ShellIntegration.InstallAssociations()
- [x] `--uninstall-assoc` 调用 ShellIntegration.UninstallAssociations()
- [x] 验证每个命令的 Shutdown 行为（安装/卸载后应退出）

### Task 6: MenuIcons 资源迁移

- [x] 从 `src/MantisZip.UI/Resources/MenuIcons/` 复制所有 .ico 文件到 `src/MantisZip.UI.Avalonia/Resources/MenuIcons/`
- [x] 确保 csproj 包含 `<AvaloniaResource Include="Resources\**" />`（已存在）
- [x] 验证资源嵌入是否正常

## 验证标准

- [x] `dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj` 通过 ✅ 已验证 (0 errors)
- [x] `--install-shell` / `--uninstall-shell` CLI 命令正常工作 ✅ 已验证 (COM CLSID + shellex handlers + ContextMenu 注册/清理，无崩溃)
- [x] `--install-assoc` / `--uninstall-assoc` CLI 命令正常工作 ✅ 已验证 (OpenWithProgids + ProgId + DefaultIcon 注册/清理)
- [x] 右键菜单图标正常显示 — ✅ 代码已验证 (10 .ico 已复制到 output/Resources/MenuIcons/，GetMenuIconPath 正确解析路径，还需用户手动在 Explorer 右键验证渲染效果)

## 手动验证脚本

```powershell
# 1. 构建
dotnet build src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj

# 2. 安装 Shell 集成（右键菜单 COM + cascade）
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -- --install-shell
# 预期：控制台输出 "Shell extension installed successfully."
# 验证：reg query HKCU\Software\Classes\*\shell\MantisZip

# 3. 安装文件关联（per-extension ProgId）
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -- --install-assoc
# 预期：控制台输出 "File associations installed successfully."
# 验证：reg query HKCU\Software\Classes\.zip\OpenWithProgids

# 4. 验证右键菜单
# 在 Explorer 中右键点击 .zip/7z/rar 文件 → 应看到 MantisZip 菜单项及图标

# 5. 卸载
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -- --uninstall-assoc
dotnet run --project src\MantisZip.UI.Avalonia\MantisZip.UI.Avalonia.csproj -- --uninstall-shell
```
