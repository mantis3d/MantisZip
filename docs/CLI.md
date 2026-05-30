# 命令行使用指南

MantisZip 支持丰富的命令行参数，主要用于 Shell 右键菜单集成和自动化脚本。

## 参数列表

| 参数 | 说明 |
|------|------|
| *(无参数)* | 正常启动主窗口 |
| `--open <路径>` | 启动主窗口并加载压缩包 |
| `--compress <路径1> <路径2> ...` | 显示压缩对话框（支持多实例 IPC 合并路径） |
| `--compress-quick <路径1> ...` | 使用默认设置直接压缩，显示进度窗口 |
| `--compress-separate <路径1> <路径2> ...` | 依次将每个选定项压缩到各自所在目录 |
| `--compress-combined <路径1> <路径2> ...` | 将所有选定项合并压缩到公共父目录（跨盘时弹窗输入名称） |
| `--extract <路径>` | 显示解压设置窗口（ExtractSettingsWindow），支持 4 种输出模式 + 冲突策略 + 打开文件夹选项 |
| `--extract-here <路径>` | 解压到当前目录 |
| `--extract-smart <路径>` | 智能解压（自动检测是否保留顶层文件夹） |
| `--extract-to-name <路径>` | 解压到以压缩包名命名的子目录 |
| `--install-shell` | 安装 Shell 右键菜单 |
| `--uninstall-shell` | 卸载 Shell 右键菜单 |
| `--install-assoc` | 安装文件关联（.zip/.7z/.rar 等默认用 MantisZip 打开） |
| `--uninstall-assoc` | 卸载文件关联 |
| `--test` | 启动测试模式（检查应用配置是否正确） |
| `--help`, `-h` | 显示帮助信息 |

## 示例

```powershell
# 打开压缩包浏览
MantisZip.UI.exe --open "D:\文档.zip"

# 快速压缩（默认设置）
MantisZip.UI.exe --compress-quick "D:\照片" -- "D:\备份.zip"

# 独立压缩多个项目
MantisZip.UI.exe --compress-separate "D:\照片" "D:\文档"

# 合并压缩到公共目录
MantisZip.UI.exe --compress-combined "D:\照片" "D:\文档"

# 快速解压
MantisZip.UI.exe --extract "D:\软件包.7z"

# 智能解压
MantisZip.UI.exe --extract-smart "D:\软件包.7z"

# 安装/卸载 Shell 右键菜单
MantisZip.UI.exe --install-shell
MantisZip.UI.exe --uninstall-shell

# 查看帮助
MantisZip.UI.exe --help
MantisZip.UI.exe -h
```

## IPC 多实例通信

`--compress`、`--compress-separate`、`--compress-combined` 三种模式使用 Mutex + NamedPipeServerStream 模式。Windows 为每个选定文件启动一个进程，第一个进程作为收集器，后续实例通过命名管道发送路径后退出。

- `--compress`: Mutex `MantisZipCompressMutex`, pipe `MantisZipCompressPipe`
- `--compress-separate`: Mutex `MantisZipCompressSeparateMutex`, pipe `MantisZipCompressSeparatePipe`
- `--compress-combined`: Mutex `MantisZipCompressCombinedMutex`, pipe `MantisZipCompressCombinedPipe`
