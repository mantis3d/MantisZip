# 原生图标 DLL (Native Icon DLL)

> **状态**: 📋 规划中 | **阶段**: [⬜⬜⬜⬜⬜] (0/5)

## TL;DR

将 `Resources\Icons\*.ico` 从独立部署文件改为一个原生 C++/Win32 DLL（纯资源 DLL），编译进 `MantisZip.Icons.dll`，注册表通过 `{dllPath},{index}` 引用图标。消除文件路径依赖，提高兼容性。

> **依赖**: Windows SDK RC 编译器（`rc.exe`，Visual Studio 自带）
> **并行**: 各 Phase 可串行执行

---

## 背景

当前图标文件以 `Content` + `CopyToOutputDirectory` 方式部署，注册表写入绝对路径如 `E:\...\Icons\zip.ico`。问题：
- 文件移动/重命名后图标丢失
- `dotnet publish` 单文件发布时这些文件需额外处理
- 路径字符串长且易错

原生 DLL 方案把图标编译进 PE 资源节，注册表用 `{dllPath},{resId}` 格式引用，这是 Windows 最稳定、最原生的方式（参考 `zipfldr.dll`）。

---

## 任务

### Phase 1：创建 MantisZip.Icons 项目

- 新建 C++ 空项目 `src/MantisZip.Icons/MantisZip.Icons.vcxproj`
- 输出类型：动态库（`.dll`）
- 配置：使用 Windows SDK、无需 CRT 依赖（纯资源 DLL，`/NOENTRY`）
- 项目类型：仅生成 DLL，不链接 CRT

### Phase 2：编写 RC 脚本

- 创建 `src/MantisZip.Icons/icons.rc`，为每个格式定义图标资源 ID：

| 文件 | 资源 ID | 对应扩展名 |
|------|---------|-----------|
| `zip.ico` | `IDI_ZIP` (101) | `.zip` |
| `sevenz.ico` | `IDI_SEVENZ` (102) | `.7z` |
| `rar.ico` | `IDI_RAR` (103) | `.rar` |
| `tar.ico` | `IDI_TAR` (104) | `.tar` |
| `gz.ico` | `IDI_GZ` (105) | `.gz` |
| `tgz.ico` | `IDI_TGZ` (106) | `.tgz` / `.tar.gz` |
| `iso.ico` | `IDI_ISO` (107) | `.iso` |

- RC 语法示例：
```rc
IDI_ZIP       ICON        "zip.ico"
IDI_SEVENZ    ICON        "sevenz.ico"
```

### Phase 3：编写 .def / 编译配置

- 添加 `.def` 文件导出无函数（纯资源 DLL 不需要导出函数）
- 链接器选项：`/NOENTRY`（无入口点）、`/DLL`
- 确保 DLL 不含 CRT 依赖（检查 `dumpbin /dependents` 仅依赖 `ntdll.dll`）

### Phase 4：集成到构建流程

- `.sln` 中引用 `MantisZip.Icons` 项目
- `MantisZip.UI` 添加项目引用（用于输出路径对齐）
- 在 `MantisZip.UI.csproj` 中添加构建后事件，复制 `MantisZip.Icons.dll` 到 UI 输出目录：

```xml
<Target Name="CopyIconDll" AfterTargets="Build">
  <Copy SourceFiles="$(SolutionDir)src\MantisZip.Icons\$(OutputPath)MantisZip.Icons.dll"
        DestinationFolder="$(TargetDir)" />
</Target>
```

### Phase 5：更新 ShellIntegration 注册逻辑

- `GetIconPath` 改为返回图标 DLL 路径 + 资源索引：

```csharp
private static (string? dllPath, int? resId) GetIconResource(string extension)
{
    var iconMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { ".zip", 101 },
        { ".7z", 102 },
        // ...
    };
    if (!iconMap.TryGetValue(extension, out var id)) return (null, null);

    var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MantisZip.Icons.dll");
    return File.Exists(dllPath) ? (dllPath, id) : (null, null);
}
```

- `InstallAssociationForExtension` 写入：
```csharp
var (dllPath, resId) = GetIconResource(ext);
if (dllPath != null && resId.HasValue)
    SetRegistryValue($@"Software\Classes\{ext}\DefaultIcon", null, $@"""{dllPath}"",{resId.Value}");
```

- `EnsureProgIdRegistered` 的 ProgId 默认图标也改为指向 DLL：
```csharp
SetRegistryValue($@"{progIdKey}\DefaultIcon", null, $@"""{dllPath}"",101");  // IDI_ZIP 作为默认
```

- 删除 `Resources\Icons\` 的 `Content` 部署（不再需要独立 .ico 文件）
- 删除 `GetExePath()` 中查找 `MantisZip.UI.exe` 的逻辑（图标不再依赖 exe 路径）

---

## 工作量估算

| Phase | 内容 | 预估工时 |
|-------|------|:--------:|
| 1 | 创建 C++ 空 DLL 项目 | 0.5h |
| 2 | RC 脚本 + 编译验证 | 0.5h |
| 3 | 去 CRT 依赖 /NOENTRY 配置 | 0.5h |
| 4 | 构建集成 + .sln 配置 | 0.5h |
| 5 | ShellIntegration 适配 + 清理旧代码 | 1h |
| **合计** | | **3h** |

## 验证标准

- [ ] `MantisZip.Icons.dll` 在输出目录存在
- [ ] `dumpbin /dependents MantisZip.Icons.dll` 仅显示 `ntdll.dll`
- [ ] 注册表 `DefaultIcon` 写入格式为 `"<path>\MantisZip.Icons.dll",<resId>`
- [ ] 安装关联后资源管理器对应文件显示正确图标
- [ ] 刷新/重启 Explorer 后图标不变
- [ ] 删除独立 .ico 文件后图标仍正常工作
- [ ] `dotnet publish` 单文件发布时 DLL 正确包含
