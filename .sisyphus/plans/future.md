# 未来计划

## 1. COM 右键菜单 + VirtualFileDataObject 延迟渲染

**目标**：替换静态注册表动词，实现动态右键菜单和拖拽延迟渲染。

### 1.1 动态 Shell 右键菜单

用 COM `IContextMenu` 处理器替代当前 `ShellIntegration` 的静态注册表动词：

- 右键菜单构建时可获取文件名 → 动态显示文本（如"添加到 (文件名).zip"）
- 完全控制菜单排序、图标、子菜单
- 注册路径：`*\shellex\ContextMenuHandlers\{GUID}`
- 工作量：中等（COM 注册、`IContextMenu` / `IShellExtInit` 实现）

### 1.2 VirtualFileDataObject（拖拽延迟渲染）

自定义 COM `IDataObject` 实现，绕过 WPF OLE bridge bug（确认的 WPF 内部 bug，v8.0.1 仍存在）：

- 拖拽时文件在 Explorer 中立即显示，实际数据在 drop 后按需提取
- Explorer 通过 `GetData()` 分块请求数据
- 使用 `System.Runtime.InteropServices.ComTypes.IDataObject` 直接实现 COM 接口
- 需要 P/Invoke：`COMStreamWrapper`、`FORMATETC`、`STGMEDIUM`

### 1.3 打包建议

两者可打包为一个 COM 辅助库（C# COM Visible 程序集），通过 regasm 注册。

## 2. 未知扩展名智能文本检测（解压前 512B）

**目标**：对于无法通过扩展名识别的文件，不解压完整条目，仅解压前 512 字节到缓冲区，检测是否为有效文本（UTF-8/GBK 等）。

**技术要点**：
- ZIP 条目通过 `ZipInputStream` 仅读取开头 N 字节的压缩数据后停止
- Tar/Gz 通过 `GZipStream` + `TarInputStream` 类似处理
- 7z 的 LZMA 也可流式解压开头后停止（但若字典很大仍会解压大量数据）
- 文本检测算法：统计 null 字节比例、有效 UTF-8 序列比例
- 编码检测：结合 BOM 识别 / chardet 风格启发式

**优先级**：低（当前按扩展名判断已覆盖绝大多数场景）

## 3. 外部工具提取视频元数据

**目标**：通过 `ffprobe`（FFmpeg 套件）提取视频/音频文件的元数据显示在信息面板（时长、分辨率、编码、码率等）。

**技术要点**：
- 仍然需要先完整解压到临时文件（流式压缩不支持随机寻址）
- `Process.Start("ffprobe", ...)` 解析 JSON 输出
- MP4 的 `moov` box 在文件末尾时无法通过偏读取到元数据
- 需要处理 ffprobe 未安装的情况

**优先级**：低（需要用户额外安装 FFmpeg，且预览前完整解压视频文件较大）
