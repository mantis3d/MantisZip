# 日志隐私脱敏 — ✅ 已实现 (2026-05-19)

**实现版本**: v0.2.8  
**实现 PR**: 本次会话直接实现  
**关键文件**: `Core/Utils/LogRedactor.cs`, `UI/LogPrivacyHelpDialog.xaml`, `UI/LogPrivacyHelpDialog.xaml.cs`

## 目标

在"调试"面板新增日志隐私脱敏选项，开启后日志中的真实文件路径/文件名被脱敏处理，防止用户隐私信息外泄。

支持三种模式：
- **关闭**: 当前行为不变
- **仅保留文件名**: `D:\照片\私人\婚纱照.jpg` → `婚纱照.jpg`
- **完全脱敏**: 路径替换为 `[PATH_1]` 等顺序标记，相同路径映射到同一 ID

## 架构修正（对比 Draft）

原设计在几个关键问题上的修正：

| 原设计问题 | 修正 |
|---|---|
| 正则用 `[^\\\s]` 排除了空格 → 带空格路径截断泄露 | `[^\\"<>|]` 允许空格，用先行断言 `(?=[\s]|$)` 做边界检查 |
| 不支持 UNC 路径 `\\server\share` | 增加 UNC 正则分支 |
| CoreLog 读不到 UI 项目的 AppSettings | CoreLog 新增 `static Action<string, string>? RedactOverride` 委托，UI 初始化时注入；`LogRedactor` 纯函数放在 Core，两边调用 |
| `ConcurrentDictionary` 无限增长 | 添加 `MaxCachedPaths = 10000` 上限，超过时清空 |
| LogStartup 在 AppSettings 加载前调用 | 初始化时默认 `off`，`InitializeApp()` 后注入真实模式 |

## 工作项

### 1. `LogRedactor` 静态类（Core 项目）

新建 `src/MantisZip.Core/Utils/LogRedactor.cs`：

```csharp
namespace MantisZip.Core.Utils;

public enum LogPrivacyMode
{
    Off,
    FilenameOnly,
    Full
}

public static class LogRedactor
{
    private static readonly Regex _pathRegex;
    private static readonly ConcurrentDictionary<string, int> _pathIds = new();
    private const int MaxCachedPaths = 10000;

    static LogRedactor()
    {
        // 编译一次，避免每次 log 重编译
        _pathRegex = new Regex(
            // 分支1: 驱动器路径 (支持空格)  C:\Program Files\My App\file.txt
            @"[A-Za-z]:(?:\\[^\\""<>|]+)+\\?|" +
            // 分支2: UNC 路径  \\server\share\path\to\file
            @"\\\\[^\\""<>|]+(?:\\[^\\""<>|]+)+\\?",
            RegexOptions.Compiled);
    }

    public static string RedactPaths(string msg, LogPrivacyMode mode)
    {
        if (mode == LogPrivacyMode.Off || string.IsNullOrEmpty(msg))
            return msg;

        return _pathRegex.Replace(msg, match =>
        {
            var path = match.Value.TrimEnd('\\');
            if (string.IsNullOrEmpty(path)) return match.Value;

            switch (mode)
            {
                case LogPrivacyMode.FilenameOnly:
                    return Path.GetFileName(path) is { Length: > 0 } name ? name : "[DIR]";

                case LogPrivacyMode.Full:
                    var id = _pathIds.GetOrAdd(path, _ =>
                    {
                        // 上限保护：超限时清空重建
                        if (_pathIds.Count >= MaxCachedPaths)
                            _pathIds.Clear();
                        return _pathIds.Count + 1;
                    });
                    return $"[PATH_{id}]";

                default:
                    return match.Value;
            }
        });
    }

    /// <summary>清空路径 ID 映射（用于测试或重置）</summary>
    public static void Reset() => _pathIds.Clear();
}
```

**关键修正细节**:
- 正则两个分支（驱动器和 UNC），`[^\\"<>|]` **不含 `\s`**，允许空格，路径末尾用匹配结束自然截断
- `RegexOptions.Compiled` 静态缓存，避免每次调用编译
- `TrimEnd('\\')` 去掉尾部反斜杠，避免空文件名
- `ConcurrentDictionary` 设 10000 上限

### 2. `CoreLog` 注入 RedactOverride 委托

`src/MantisZip.Core/Utils/CoreLog.cs` 新增：

```csharp
// 脱敏覆盖委托：参数1=原始消息，返回值=脱敏后的消息
// 由 UI 层在初始化时注入（因为 CoreLog 不能直接引用 AppSettings）
internal static Func<string, string>? RedactOverride { get; set; }

private static void Write(string msg)
{
    var finalMsg = RedactOverride != null ? RedactOverride(msg) : msg;
    // ... 原有写入逻辑，用 finalMsg 替代 msg ...
}
```

`Write` 方法内用 `finalMsg` 替代 `msg` 写入文件。

### 3. `AppSettings` 新增属性

`src/MantisZip.UI/AppSettings.cs` 调试区域新增：

```csharp
public string LogPrivacyMode { get; set; } = "full"; // "off" | "filename" | "full"
```

### 4. UI 初始化时注入委托

`src/MantisZip.UI/App.xaml.cs` -> `OnStartup` 中 `InitializeApp()` 之后添加：

```csharp
// 初始化日志脱敏
CoreLog.RedactOverride = msg =>
    LogRedactor.RedactPaths(msg, ParseLogPrivacyMode(AppSettings.Instance.LogPrivacyMode));

// App.Log / App.LogDebug 内联脱敏（它们自己能读到 AppSettings）
```

### 5. `App.Log()` / `App.LogDebug()` 添加脱敏

`src/MantisZip.UI/App.xaml.cs` 在 `Log(string msg)` 和 `LogDebug(string msg)` 写入前：

```csharp
var mode = ParseLogPrivacyMode(AppSettings.Instance.LogPrivacyMode);
msg = LogRedactor.RedactPaths(msg, mode);
```

`LogStartup` 同样处理。

### 6. 设置窗口 UI（XAML + C#）

`src/MantisZip.UI/SettingsWindow.xaml` 调试面板追加：

```xml
<ComboBox x:Name="LogPrivacyModeCombo" Margin="0,8,0,0" Width="200" HorizontalAlignment="Left">
    <ComboBoxItem Tag="off" Content="{l:L Settings_Debug_LogPrivacyMode_Off}"/>
    <ComboBoxItem Tag="filename" Content="{l:L Settings_Debug_LogPrivacyMode_Filename}"/>
    <ComboBoxItem Tag="full" Content="{l:L Settings_Debug_LogPrivacyMode_Full}"/>
</ComboBox>
```

`SettingsWindow.xaml.cs` 中：
- `LoadSettings()`: 读取 `s.LogPrivacyMode` 选中对应项
- `SaveSettings()`: 将选中 `Tag` 写入 `s.LogPrivacyMode`

### 7. 本地化键

`src/MantisZip.UI/Localization/L.cs` 和 `strings.zh.json` / `strings.en.json` 新增：

| Key | zh | en |
|-----|----|----|
| `Settings_Debug_LogPrivacyMode` | 日志隐私脱敏 | Log Privacy Redaction |
| `Settings_Debug_LogPrivacyMode_Off` | 关闭 | Off |
| `Settings_Debug_LogPrivacyMode_Filename` | 仅保留文件名 | Filename only |
| `Settings_Debug_LogPrivacyMode_Full` | 完全脱敏 | Full redaction |

### 8. 辅助方法 `ParseLogPrivacyMode`

`src/MantisZip.UI/App.xaml.cs` 或 `LogRedactor` 内部新增：

```csharp
private static LogPrivacyMode ParseLogPrivacyMode(string mode) => mode switch
{
    "filename" => LogPrivacyMode.FilenameOnly,
    "full" => LogPrivacyMode.Full,
    _ => LogPrivacyMode.Off
};
```

## 影响范围

| 文件 | 改动类型 |
|------|----------|
| `Core/Utils/LogRedactor.cs` | **新建** |
| `Core/Utils/CoreLog.cs` | 追加 `RedactOverride` + `Write` 调用 |
| `UI/AppSettings.cs` | 追加 1 个属性 |
| `UI/App.xaml.cs` | 3 个 Log 方法加脱敏调用 + 委托注入 |
| `UI/SettingsWindow.xaml` | 调试面板加 ComboBox |
| `UI/SettingsWindow.xaml.cs` | Load/Save 读取新属性 |
| `UI/Localization/L.cs` | 追加 4 个 key 常量 |
| `UI/Resources/strings.zh.json` | 追加 4+8 个翻译 |
| `UI/Resources/strings.en.json` | 追加 4+8 个翻译 |
| `UI/SettingsWindow.xaml` | 调试面板加 `LogPrivacyModeCombo` 下拉框 + `[?]` 帮助按钮 |
| `UI/SettingsWindow.xaml.cs` | Load/Save 读写新属性 + `LogPrivacyHelp_Click` 处理器 |
| `UI/LogPrivacyHelpDialog.xaml` | **新建**：帮助说明窗口 |
| `UI/LogPrivacyHelpDialog.xaml.cs` | **新建**：关闭按钮处理器 |

## 实现时做的修正

| 原计划 | 实现修正 |
|--------|----------|
| 默认值 `"off"` | 改为 `"full"`（完全脱敏） |
| 仅下拉框 | 右侧加 `[?]` 按钮 → 弹出 `LogPrivacyHelpDialog` 说明窗口 |

## 未覆盖的路径模式（明确 scope）

| 模式 | 原因 |
|------|------|
| `\\?\C:\Long\Path` | .NET 长路径前缀，极低概率出现在日志中 |
| `%USERPROFILE%\Docs` | 调用点传参时已展开，日志中看到的已是展开后路径 |
| `\Windows\System32` | 不带盘符，误匹配率 > 正确率，不处理 |
| `file:///C:/path` | URI 格式，跨平台路径风格，暂不处理 |
