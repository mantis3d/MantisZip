# 便携版模式

> 为 MantisZip 增加便携模式：免安装、不写注册表、路径重定向到 exe 同目录。
> **状态**: 📋 待定（已修订 2026-07-08）| **任务**: [⬜⬜⬜⬜⬜⬜⬜] (0/7)
> 创建日期：2026-05-18，修订日期：2026-07-08
>
> **修订说明**：SharpSevenZip（7z.dll）已取代旧版 7z.exe 外部进程；补充了拖拽临时目录和启动清理的重定向；更新代码示例匹配当前 `Lazy<AppSettings>` 模式。

## 动机

用户可在 U 盘或移动硬盘上直接运行 MantisZip，设置和密码跟随 exe 携带，不污染系统。

## 核心设计：哨兵文件检测

```
MantisZip.UI.exe
├── MantisZip.UI.exe       # 主程序（或单文件发布包）
├── x64/                   # 64 位 7z.dll（可选，用户手动放入）
│   └── 7z.dll
├── x86/                   # 32 位 7z.dll（可选）
│   └── 7z.dll
└── Data/                  # 便携版数据目录（自动创建）
    ├── settings.json
    ├── passwords.json
    └── Temp/              # 预览 / 拖拽临时文件
```

exe 同级放一个空文本文件 `Portable.txt`（或 `.portable`），程序启动时检测到它就进入便携模式。

## 任务清单

- [ ] **1. `AppSettings.cs` — 路径重定向** — 哨兵文件检测 + Data 目录重定向
- [ ] **2. `PasswordManager.cs` — 数据路径注入** — `CustomDataDir` 支持
- [ ] **3. `App.OnStartup` — 跳过 Shell 注册** — 便携版不安装右键菜单和文件关联
- [ ] **4. `MainWindow.Preview.cs` — 预览临时目录重定向**
- [ ] **5. `SevenZipEngine.cs` — 便携 7z.dll 路径检测**
- [ ] **6. `MainWindow.DragDrop.cs` — 拖拽临时目录重定向**
- [ ] **7. `App.OnStartup` / `App.OnExit` — 启动及退出时仅清理便携 Temp**

## 代码改动

### 1. `AppSettings.cs` — 路径重定向

当前 `AppSettings` 使用 `Lazy<AppSettings>` 模式，`SettingsDir`/`SettingsFile` 是 `static readonly` 字段：

```csharp
// 当前（需修改）：
private static readonly string SettingsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
```

修改为：

```csharp
public static bool IsPortableMode { get; private set; }
private static string SettingsDir { get; set; }
private static string SettingsFile { get; set; }

static AppSettings()
{
    IsPortableMode = File.Exists(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Portable.txt"));

    if (IsPortableMode)
    {
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        SettingsDir = dataDir;
        SettingsFile = Path.Combine(dataDir, "settings.json");
        PasswordManager.CustomDataDir = dataDir;
    }
    else
    {
        SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");
        SettingsFile = Path.Combine(SettingsDir, "settings.json");
    }
}
```

`Save()` 中 `SyncContextMenuToRegistry()` 调用也要跳过：

```csharp
public bool Save()
{
    try
    {
        if (!Directory.Exists(SettingsDir))
            Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
        // 便携版不写注册表
        if (!IsPortableMode)
            SyncContextMenuToRegistry();
        return true;
    }
    ...
}
```

### 2. `PasswordManager.cs` — 数据路径注入

当前硬编码 `AppDataPath`：

```csharp
// 当前：
private static readonly string AppDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MantisZip");
private static readonly string PasswordFilePath = Path.Combine(AppDataPath, "passwords.json");
```

添加 `CustomDataDir` 静态属性，`PasswordFilePath` 改为动态计算：

```csharp
public static string? CustomDataDir { get; set; }

private static string GetPasswordsPath() =>
    CustomDataDir != null
        ? Path.Combine(CustomDataDir, "passwords.json")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MantisZip", "passwords.json");
```

所有原本直接引用 `PasswordFilePath` 的地方改为调用 `GetPasswordsPath()`。

### 3. `App.OnStartup` — 跳过 Shell 注册

当前 `OnStartup` 中检测 `FirstRunShell` / `FirstRunAssoc` 注册表标记来安装 Shell 集成和文件关联。

便携版需跳过的位置：

```csharp
// 在 OnStartup 的 first-run 处理块前增加判断：
if (AppSettings.IsPortableMode)
{
    TraceLog("OnStartup: portable mode, skipping shell integration and file association registration");
}
else
{
    // 原有的 FirstRunShell / FirstRunAssoc 检测块
    ...
}
```

CLI 命令也要加守卫：

```csharp
case "--install-shell":
case "--uninstall-shell":
case "--install-assoc":
case "--uninstall-assoc":
    if (AppSettings.IsPortableMode)
    {
        LogStartup("便携模式不支持 Shell 集成安装");
        Shutdown();
        return;
    }
    // 原有逻辑
    ...
```

### 4. 预览临时文件路径

当前 `ExtractPreviewFileAsync` 中硬编码：

```csharp
// 当前：
_previewTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), Guid.NewGuid().ToString());
```

便携版需重定向：

```csharp
private string GetTempDir() =>
    AppSettings.IsPortableMode
        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Temp")
        : Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));

private async Task<string> ExtractPreviewFileAsync(...)
{
    var tempRoot = GetTempDir();
    _previewTempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString());
    Directory.CreateDirectory(_previewTempDir);
    ...
}
```

`ClearPreviewTemp()` 无需改动——它删除 `_previewTempDir` 变量指向的路径即可。

### 5. 7z 压缩 — 便携 7z.dll 路径检测

**重要：当前使用 SharpSevenZip（7z.dll COM 绑定），非旧版 7z.exe。**

`SevenZipEngine` 已有 `SevenZipDllPath` 属性和 `ResolveDefaultSevenZipDllPath()` 方法。便携版需要在 `InitializeApp()` 或 `SevenZipEngine` 的初始化中增加 exe 目录的平台子目录搜索。

当前 `ResolveDefaultSevenZipDllPath()` 已搜索 exe 目录下的 `x64/7z.dll` 和 `x86/7z.dll`：

```csharp
private static string ResolveDefaultSevenZipDllPath()
{
    var candidates = new List<string>
    {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll"),
        @"C:\Program Files\7-Zip\7z.dll",
        @"C:\Program Files (x86)\7-Zip\7z.dll",
    };
    return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
}
```

**便携版已有自带 7z.dll 的可选需求时，此逻辑已能满足**——因为默认第一个候选路径就是 `{BaseDir}/x64/7z.dll`，恰好与便携版目录结构吻合。

需要补充的是：如果用户将 `7z.dll` 直接放在 exe 同目录（而非 `x64/` 子目录），也应能找到：

```csharp
// 在 candidates 列表开头增加
Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll"),
```

此改动在 `SevenZipEngine.cs` 中，约 1 行。便携版检测到该文件即可正常工作。无需为便携版额外写条件分支。

### 6. 拖拽临时目录重定向

当前 `MainWindow.DragDrop.cs` 中硬编码：

```csharp
// 当前：
_dragTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), "DragDrop", Guid.NewGuid().ToString());
```

便携版使用与预览相同的 `GetTempDir()` 辅助方法：

```csharp
_dragTempDir = Path.Combine(GetTempDir(), "DragDrop", Guid.NewGuid().ToString());
```

`CleanupDragTempDir()` 无需改动——它删除 `_dragTempDir` 指向的路径。

（注意：`GetTempDir()` 如果定义在 MainWindow 中，预览和拖拽均可共用；也可以提取为 `AppSettings` 的静态方法。）

### 7. 启动 / 退出时清理

当前 `OnStartup` 中：

```csharp
// 当前：
var tempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));
if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
```

便携版改为清理 `Data/Temp/`：

```csharp
if (AppSettings.Instance.CleanTempOnStartup)
{
    var tempDir = AppSettings.IsPortableMode
        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Temp")
        : Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));
    if (Directory.Exists(tempDir))
    {
        Directory.Delete(tempDir, recursive: true);
        LogDebug("OnStartup: cleaned temp dir: {0}", tempDir);
    }
}
```

同理，`App.OnExit` 中的清理代码也要做相同判断。

## 哨兵文件创建

用户自行创建：
```
notepad Portable.txt
# 内容留空，保存即可
```

或在发布脚本中自动附带：
```powershell
# 构建便携版时自动生成
"" | Out-File -FilePath "portable_output\Portable.txt" -Encoding ascii
```

## 影响范围

| 文件 | 改动量 | 说明 |
|------|--------|------|
| `AppSettings.cs` | ~25 行 | 静态构造 + `IsPortableMode` 属性 + `SettingsDir`/`SettingsFile` 动态化 + `Save()` 跳过 `SyncContextMenuToRegistry()` |
| `PasswordManager.cs` | ~15 行 | `CustomDataDir` 注入 + `GetPasswordsPath()` 方法 + 替换所有 `PasswordFilePath` 引用 |
| `App.xaml.cs` | ~15 行 | 启动时跳过 `FirstRunShell`/`FirstRunAssoc` + CLI 命令守卫 + 启动清理重定向 |
| `ShellIntegration.cs` | — | 无需改动（OnStartup 层已跳过调用） |
| `MainWindow.Preview.cs` | ~5 行 | `GetTempDir()` 辅助方法 + `ExtractPreviewFileAsync` 使用 |
| `MainWindow.DragDrop.cs` | ~2 行 | `_dragTempDir` 路径改为 `GetTempDir()` |
| `SevenZipEngine.cs` | ~1 行 | `ResolveDefaultSevenZipDllPath()` 首候选增加 exe 目录根级 `7z.dll` |

总计约 **65 行代码**，无新增依赖。

## 发布命令

```powershell
# 便携版（含 runtime）
dotnet publish src\MantisZip.UI\MantisZip.UI.csproj `
  -c Release -o portable_output `
  --self-contained true -p:PublishSingleFile=true

# 创建哨兵文件
"" | Out-File -FilePath "portable_output\Portable.txt" -Encoding ascii

# 如果需要 7z 压缩支持，将 7z.dll 放入对应平台子目录
# Copy-Item "$env:ProgramFiles\7-Zip\7z.dll" "portable_output\x64\7z.dll"

# [可选] 打包成 zip 分发
Compress-Archive -Path portable_output\* -DestinationPath MantisZip-Portable.zip
```

## 注意事项

- `PublishSingleFile=true` 会在首次启动时解压到临时目录，速度略慢于安装版
- `.NET 9` 的 SingleFile 支持原生 DLL（如 7z.dll），但不支持将外部 exe 嵌入单文件
- 如果 7z.dll 不在同目录或 `x64/` 子目录下，便携版压缩时 7z 格式会报错——需在 UI 中给出明确提示或在 7z 选择时自动降级为 ZIP（现有保留 `SevenZipDllResolveCallback` 弹出文件选择对话框的逻辑仍会运行）
- **便携版不写注册表**：`AppSettings.Save()` 跳过 `SyncContextMenuToRegistry()`；`OnStartup` 跳过所有 first-run Shell 注册；`--install-shell` 等 CLI 报错退出
- **拖拽和预览临时目录**：便携版统一使用 `Data/Temp/{GUID}`，不再写入系统 `%TEMP%`
- **与普通模式共存**：普通模式和便携模式共享 `AppSettings.SevenZipPath` 设置，便携版的 7z.dll 自动检测不会影响普通版行为

---

## Definition of Done

- [ ] 哨兵文件 `Portable.txt` 检测完成，进入便携模式
- [ ] 设置文件（settings.json）保存到 exe 同目录 Data/ 下
- [ ] 密码库（passwords.json）保存到 Data/ 下
- [ ] 便携模式下跳过 Shell 右键菜单注册（不在 `OnStartup` 调用 Shell 安装逻辑）
- [ ] 便携模式下跳过文件关联注册（`--install-assoc` 报错提示不可用）
- [ ] 便携模式 CLI `--install-shell` / `--uninstall-shell` / `--install-assoc` 报错退出
- [ ] 预览临时文件保存到 Data/Temp/ 下，不影响系统 %TEMP%
- [ ] 拖拽临时文件保存到 Data/Temp/DragDrop/ 下
- [ ] 启动清理 / 退出清理指向 Data/Temp/ 而非系统 %TEMP%
- [ ] 7z.dll 在 exe 同目录或 `x64/` 子目录时自动检测使用
- [ ] `dotnet build` 通过

### Final Checklist

- [ ] 普通模式下行为不变（不回归）
- [ ] `Portable.txt` 存在时进入便携模式
- [ ] 便携版设置随 exe 位置移动
- [ ] 便携版密码库随 exe 位置移动
- [ ] 便携版不写注册表（Shell 菜单、文件关联、COM CLSID、设置同步均不写入）
- [ ] 便携版预览临时文件不写入系统 Temp
- [ ] 便携版拖拽临时文件不写入系统 Temp
- [ ] 便携版启动/退出清理指向 Data/Temp/ 而非系统 Temp
- [ ] 便携版 7z.dll 自动检测（exe 同目录 / `x64/` 子目录）
- [ ] 便携版 `--install-shell` 等 CLI 命令正确报错
