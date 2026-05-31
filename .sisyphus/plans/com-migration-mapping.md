# ShellIntegration 迁移映射表

> Task 2 产出：当前静态注册表方案 → COM `IContextMenu` 映射

## 1. 注册表写入点

| 当前操作 | 位置 | COM 替代 |
|---------|------|---------|
| 写入动词名/显示字符串 | `HKCU\Software\Classes\{target}\shell\{verb}` | `QueryContextMenu` 返回菜单文本 |
| 写入 CLI 命令 | `HKCU\Software\Classes\{target}\shell\{verb}\command` | `InvokeCommand` 中 `Process.Start` |
| 写入 AppliesTo | 同上的 `AppliesTo` 值 | 代码中检查文件扩展名 |
| 写入 Icon | 同上的 `Icon` 值 | `MENUITEMINFO` 的 `HBITMAP`/`HICON` |
| 写入分隔符 | CommandFlags=8 (单独 separator verb) | `InsertMenu` 的 `MF_SEPARATOR` |
| 写入层叠入口 | `ExtendedSubCommandsKey` | `InsertMenu` 的 `MF_POPUP` |
| 安装检测 | 检查 `*\shell\MantisZip` 等键存在 | 检查 `CLSID\{GUID}` 存在 |
| 刷新 Shell | `SHChangeNotify` | 不变 |

## 2. 8 个菜单项

| ID | 菜单 | Toggle 设置 | CLI | 过滤 |
|----|------|-------------|-----|------|
| 1 | 用 MantisZip 打开 | `EnableOpenMenu` | `--open` | 仅压缩包 |
| 2 | 解压到此处 | `EnableExtractHereMenu` | `--extract-here` | 仅压缩包 |
| 3 | 智能解压 | `EnableSmartExtractMenu` | `--extract-smart` | 仅压缩包 |
| 4 | 解压到（文件名） | `EnableExtractToNamedMenu` | `--extract-to-name` | 仅压缩包 |
| 5 | 解压到… | `EnableExtractToMenu` | `--extract` | 仅压缩包 |
| 6 | 压缩到独立的 | `EnableCompressSeparate` | `--compress-separate` | 所有 |
| 7 | 压缩到（父目录名） | `EnableCompressCombined` | `--compress-combined` | 所有 |
| 8 | 用 MantisZip 压缩 | `EnableCompressMenu` | `--compress` | 所有 |

## 3. 三种目标类型

| 目标 | 显示菜单 | 说明 |
|------|---------|------|
| `*` (文件) | 全部 8 个 | 解压组仅对压缩包显示 |
| `Directory` (文件夹) | 仅压缩 3 个 | 8-10 |
| `Directory\Background` (背景) | 仅压缩 (--compress) | 路径传 `%V` 当前目录 |

## 4. 设置项（需同步到注册表供 COM 读取）

```
EnableCascadingMenu  (DWORD)  — 层叠 vs 独立
ShowMenuIcons        (DWORD)  — 是否显示图标
EnableOpenMenu       (DWORD)
EnableExtractHereMenu(DWORD)
EnableSmartExtractMenu(DWORD)
EnableExtractToNamedMenu(DWORD)
EnableExtractToMenu  (DWORD)
EnableCompressSeparate(DWORD)
EnableCompressCombined(DWORD)
EnableCompressMenu   (DWORD)
```

## 5. 保留在 ShellIntegration.cs 的代码（不迁移）

- `InstallAssociations()` / `UninstallAssociations()` — 文件关联
- `AreAssociationsInstalled` — 文件关联检测
- `ProgId` / `ArchiveExtensions` — 文件关联专用
- `GetIconPath()` — 文件关联的 per-extension 图标

## 6. 需修改的 ShellIntegration.cs 方法

| 方法 | 改动 |
|------|------|
| `IsInstalled` | 改为检测 `CLSID\{GUID}` 注册表键 |
| `Install()` | 新增 COM 安装路径：调用 `InstallCom()`；回退到现有静态注册 |
| `Uninstall()` | 新增 `UninstallCom()` + 保留旧静态清理 |
| 新增 `InstallCom()` | 写入 `shellex\ContextMenuHandlers` + CLSID + `regsvr32` |
| 新增 `UninstallCom()` | 删除上述键 + `regsvr32 /u` |

## 7. 压缩包扩展名列表

COM 组件需要自己维护一份用于 AppliesTo 过滤：

```
.zip .7z .rar .tar .tgz .tar.gz .gz .iso
```
