# 便携版模式

> 为 MantisZip 增加便携模式：免安装、不写注册表、路径重定向到 exe 同目录。
> **状态**: 📋 待定 | **任务**: [⬜⬜⬜⬜⬜] (0/5)
> 创建日期：2026-05-18

## 动机

用户可在 U 盘或移动硬盘上直接运行 MantisZip，设置和密码跟随 exe 携带，不污染系统。

## 核心设计：哨兵文件检测

```
MantisZip.UI.exe
├── MantisZip.UI.exe       # 主程序
├── 7z.exe                 # [可选] 7z 压缩引擎
├── 7z.dll                 # [可选]
└── Data/                  # 便携版数据目录（自动创建）
    ├── settings.json
    ├── passwords.json
    └── Temp/              # 预览临时文件
```

exe 同级放一个空文本文件 `Portable.txt`（或 `.portable`），程序启动时检测到它就进入便携模式。

## 任务清单

- [ ] **1. `AppSettings.cs` — 路径重定向** — 哨兵文件检测 + Data 目录重定向
- [ ] **2. `PasswordManager.cs` — 数据路径注入** — `CustomDataDir` 支持
- [ ] **3. `App.OnStartup` — 跳过 Shell 注册** — 便携版不安装右键菜单
- [ ] **4. `MainWindow.Preview.cs` — 预览临时目录重定向**
- [ ] **5. `SevenZipEngine.cs` — 便携 7z 路径检测**

## 代码改动

### 1. `AppSettings.cs` — 路径重定向

```csharp
public static bool IsPortableMode { get; private set; }

static AppSettings()
{
    IsPortableMode = File.Exists(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Portable.txt"));

    if (IsPortableMode)
    {
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        // 覆盖默认路径
        _settingsPath = Path.Combine(dataDir, "settings.json");
        PasswordManager.CustomDataDir = dataDir;
    }
}
```

### 2. `PasswordManager.cs` — 数据路径注入

```csharp
public static string? CustomDataDir { get; set; }
private string GetPasswordsPath() =>
    CustomDataDir != null
        ? Path.Combine(CustomDataDir, "passwords.json")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MantisZip", "passwords.json");
```

### 3. `App.OnStartup` — 跳过 Shell 注册

```csharp
if (AppSettings.IsPortableMode)
{
    // 便携版不安装 Shell 右键菜单和文件关联
    // --install-shell / --install-assoc 命令行传入时报错提示
}
```

### 4. 预览临时文件路径

```csharp
private string GetPreviewTempDir() =>
    AppSettings.IsPortableMode
        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Temp")
        : Path.Combine(Path.GetTempPath(), "MantisZip");
```

### 5. 7z 压缩

便携版自带 `7z.exe`（用户手动放入 exe 目录）：
```csharp
if (AppSettings.IsPortableMode)
{
    var portable7z = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe");
    if (File.Exists(portable7z))
        SevenZipEngine.SevenZipPath = portable7z;
}
```

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
| `AppSettings.cs` | ~15 行 | 静态构造 + `IsPortableMode` 属性 + `_settingsPath` 重定向 |
| `PasswordManager.cs` | ~10 行 | `CustomDataDir` 注入 + `GetPasswordsPath()` |
| `App.xaml.cs` | ~5 行 | 启动时跳过 `--install-shell` |
| `MainWindow.Preview.cs` | ~3 行 | 临时目录重定向 |
| `SevenZipEngine.cs` | ~3 行 | 便携 7z 路径检测 |

总计约 **40 行代码**，无新增依赖。

## 发布命令

```powershell
# 便携版（含 runtime）
dotnet publish src\MantisZip.UI\MantisZip.UI.csproj `
  -c Release -o portable_output `
  --self-contained true -p:PublishSingleFile=true

# 创建哨兵文件
"" | Out-File -FilePath "portable_output\Portable.txt" -Encoding ascii

# [可选] 打包成 zip 分发
Compress-Archive -Path portable_output\* -DestinationPath MantisZip-Portable.zip
```

## 注意事项

- `PublishSingleFile=true` 会在首次启动时解压到临时目录，速度略慢于安装版
- `.NET 9` 的 SingleFile 支持原生 DLL（如 7z.dll），但不支持将外部 exe（7z.exe）嵌入单文件，需单独放在 exe 同目录
- 如果 7z.exe 不在同目录，便携版压缩时 7z 格式会报错——需在 UI 中给出明确提示或在 7z 选择时自动降级为 ZIP

---

## Definition of Done

- [ ] 哨兵文件 `Portable.txt` 检测完成，进入便携模式
- [ ] 设置文件（settings.json）保存到 exe 同目录 Data/ 下
- [ ] 密码库（passwords.json）保存到 Data/ 下
- [ ] 便携模式下跳过 Shell 右键菜单注册
- [ ] 预览临时文件保存到 Data/Temp/ 下
- [ ] 7z.exe 同目录时自动检测使用
- [ ] `dotnet build` 通过

### Final Checklist

- [ ] 普通模式下行为不变（不回归）
- [ ] `Portable.txt` 存在时进入便携模式
- [ ] 便携版设置随 exe 位置移动
- [ ] 便携版密码库随 exe 位置移动
- [ ] 便携版不写注册表（Shell 菜单不安装）
- [ ] 便携版预览临时文件不写入系统 Temp
