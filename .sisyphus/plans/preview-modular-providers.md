# 可插拔预览模块体系 (Pluggable Preview Providers)

## TL;DR

将部分预览格式的"数据读取"实现抽取为独立的类库项目，按需分发和加载。解决预览格式增多后单个安装包体积膨胀的问题。

> **状态**: 规划中（当前预览格式仍在主项目内，`ITableDataProvider` 接口已预留）
> **依赖**: 预览系统稳定后 / 安装包体积接近用户不可接受时启动

---

## 动机

当前所有预览格式（PE/PDF/字体/音频/SQLite/Office/视频等）的数据读取器都在 `MantisZip.Core` 项目中。每个格式都可能带来额外依赖（如 `Microsoft.Data.Sqlite` 约 2 MB）。

当预览格式继续增加（计划中的 PNG 元数据、HEIF、DICOM、EPUB 等），额外依赖可能累积到 10-20 MB。部分用户可能只需要其中一部分格式。

## 架构

```
MantisZip.Core (零第三方依赖)
├── Abstractions/
│   └── ITableDataProvider.cs        ← 共享接口，本项目内
│
MantisZip.UI (WPF, 引用 Core)
├── MainWindow.Preview.cs            ← ShowTablePreview() 共用方法
│
MantisZip.Preview.Sqlite (可选, 引用 Core)
├── SqliteDataReader.cs
├── MantisZip.Preview.Sqlite.csproj (+ Microsoft.Data.Sqlite)
│
MantisZip.Preview.Office (可选, 引用 Core)
├── OfficeDataReader.cs
├── MantisZip.Preview.Office.csproj (+ DocumentFormat.OpenXml)
│
... 其他可选模块
```

### 接口定义

```csharp
// Core/Abstractions/ — 零依赖
public interface ITableDataProvider
{
    string FormatName { get; }              // "SQLite"
    IEnumerable<string> SupportedExtensions { get; } // { ".db", ".sqlite", ... }
    Task<TableQueryResult?> QueryAsync(Stream data, CancellationToken ct);
}

public class TableQueryResult
{
    public List<TableData> Tables { get; init; } = new();
}

public class TableData
{
    public string Name { get; init; }
    public DataTable Data { get; init; }      // 最多 100 行 × 100 列
}
```

### 加载方式

```csharp
// UI 启动时扫描
var providers = new List<ITableDataProvider>();
var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
foreach (var dll in Directory.EnumerateFiles(dir, "MantisZip.Preview.*.dll"))
{
    var asm = Assembly.LoadFrom(dll);
    var providerType = asm.GetTypes().FirstOrDefault(t => t.IsAssignableTo(typeof(ITableDataProvider)));
    if (providerType != null)
        providers.Add((ITableDataProvider)Activator.CreateInstance(providerType));
}
```

## 安装集成 (Inno Setup)

```pascal
[Components]
Name: "preview"; Description: "预览模块"
Name: "preview\sqlite"; Description: "SQLite 数据库 (.db)"; Types: full custom
Name: "preview\office"; Description: "Office 文档 (.docx/.xlsx/.pptx)"; Types: full custom

[Files]
Source: "MantisZip.Preview.Sqlite.dll"; Dest: "{app}"; Components: preview\sqlite
Source: "Microsoft.Data.Sqlite.dll"; Dest: "{app}"; Components: preview\sqlite
Source: "SQLitePCLRaw.*.dll"; Dest: "{app}"; Components: preview\sqlite
Source: "e_sqlite3.dll"; Dest: "{app}"; Components: preview\sqlite
```

## 迁移策略

### Phase 1 (当前)
- 所有预览格式仍在主项目内
- 但已定义 `ITableDataProvider` 接口，代码按此结构编写
- `SqliteDataReader` 实现接口，引用在 Core 项目内

### Phase 2
- 将 `SqliteDataReader` 及其依赖移至独立项目 `MantisZip.Preview.Sqlite`
- UI 改为通过 `ITableDataProvider` 注册表+反射加载
- 安装脚本增加组件选项

### Phase 3（按需进行）
- Office 预览等其它格式逐步按同样方式分离

## 注意事项

- 加载失败的模块静默忽略，不阻塞启动
- 用户尝试预览没有对应 provider 的格式时，显示"此格式预览模块未安装"而非崩溃
- Provider DLLs 放主程序目录，不建子目录
- 不要在 `ITableDataProvider` 接口中引入 WPF/UI 类型，保持纯数据层
