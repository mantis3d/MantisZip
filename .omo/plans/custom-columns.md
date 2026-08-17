# 文件列表自定义列 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for completion tracking.
>
> **Design doc**: `.sisyphus/drafts/custom-columns-design.md`

---

## TL;DR

> **Quick Summary**: Add customizable columns to the file list DataGrid showing file metadata (document title, image dimensions, audio duration, etc.) with lazy loading, configurable via a dialog.
>
> **Deliverables**:
> - CustomColumnDefinition data model + persistence in window.json
> - ChooseColumnsDialog (type→datasource→add flow)
> - Dynamic DataGrid column generation
> - MetadataExtractor in Core for lazy metadata extraction
> - Integration with existing column context menu and sorting
>
> **Estimated Effort**: Medium (12-14 tasks)
> **Parallel Execution**: YES - 4 waves
> **Critical Path**: Data model → MetadataExtractor → ChooseColumnsDialog → Dynamic columns → Integration

---

## Context

### Original Request

用户想要在文件列表添加自定义列功能，可选择不同格式（文本/整数/时间/大小）展示元数据，如文档标题、MP3 标题、图片宽度、音频时长等。

### Design Summary

**Approach**: 列类型系统 + 配置 UI + 懒加载
- `CustomColumnDefinition` 定义每个自定义列（类型 + 数据源 + 表头）
- `ArchiveItem.CustomMetadata` 字典缓存已提取的元数据
- `MetadataExtractor` 统一调度按 `MetadataKey` 路由到对应解析器
- `ChooseColumnsDialog` 先选类型 → 过滤数据源 → 编辑表头 → 添加
- 懒加载通过 ValueConverter 触发，后台异步提取

**Key Constraints**:
- `CanUserReorderColumns="False"` → 需用 DisplayIndex 程序化排序
- `FileListGrid_Sorting` 自定义排序 → 自定义列必须设置 SortMemberPath
- 加密 7z 条目不支持提取，需优雅降级
- 现有 7 列是 TemplateColumn（含进度条），自定义列只用 TextColumn（简洁）

### Metis Review

**Identified Gaps** (addressed in plan):
- **Parsers return FileFormatInfo not raw values** → MetadataExtractor 需要适配 FileFormatInfo 的属性路径
- **Column header context menu 需更新** → 加入自定义列切换入口
- **性能边界** → 懒加载 + VirtualizingStackPanel 自动节流
- **加密条目降级** → 提取失败时显示 "—" 而非崩溃

---

## Work Objectives

### Core Objective
在文件列表 DataGrid 上实现用户可自由配置的自定义列系统，支持按类型选择元数据列，懒加载显示。

### Concrete Deliverables
- `src/MantisZip.Core/Utils/MetadataExtractor.cs` — 元数据提取调度器
- `src/MantisZip.UI/MainWindow/MainWindow.Types.cs` — ArchiveItem 增加 CustomMetadata 字典 + CustomColumnDefinition 类
- `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs` — 动态列生成 + 懒加载逻辑
- `src/MantisZip.UI/MainWindow/MainWindow.xaml` — 右键/菜单增加"自定义列"项
- `src/MantisZip.UI/ChooseColumnsDialog.xaml` + `.cs` — 配置对话框
- `src/MantisZip.UI/Converters/CustomMetadataValueConverter.cs` — 值格式化

### Must Have
- [ ] 用户可以通过对话框添加、删除、排序自定义列
- [ ] 自定义列配置在重启后保留（window.json）
- [ ] 懒加载：只有可见行 + 可见列才触发元数据提取
- [ ] 支持 Text/Integer/Size/Time 四种类型格式化
- [ ] 加密/不可读条目优雅降级显示 "—"

### Must NOT Have (Guardrails)
- ❌ 列表达式/公式（`[Width] x [Height]`）— 本次不实现
- ❌ 不修改现有 7 个固定列的模板/行为
- ❌ 不引入 MVVM 框架（维持 code-behind 模式）
- ❌ 不对 MetadataKey 显示名做本地化（可后续加）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### QA Policy
- **Frontend/UI**: Build + run app, open archive, verify dialog opens, add custom column, verify column appears
- **Backend/Core**: Unit tests for MetadataExtractor with mock parsers
- **Evidence**: Screenshots of dialog + custom column in action

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — can start in parallel):
├── Task 1: CustomColumnDefinition + MetadataKey registry + persistence
├── Task 2: ArchiveItem.CustomMetadata dictionary
└── Task 3: CustomMetadataValueConverter

Wave 2 (Metadata extraction — after T1, parallel within):
├── Task 4: MetadataExtractor — core dispatch + existing parser integration
├── Task 5: Image dimension extraction (Width, Height)
├── Task 6: Duration extraction (audio/video) + Time format
└── Task 7: Text word count + remaining text/integer fields

Wave 3 (UI — after T4, parallel within):
├── Task 8: ChooseColumnsDialog XAML layout
├── Task 9: ChooseColumnsDialog logic (type→datasource→add)
└── Task 10: Dynamic column generation (RebuildCustomColumns)

Wave 4 (Integration — depends on T8+T10):
├── Task 11: Column header context menu + main menu integration
├── Task 12: Sort handling for custom columns
├── Task 13: Cache management + cleanup + edge cases
└── Task 14: Integration test + final QA

Critical Path: T1 → T4 → T8/T9/T10 → T11-T14
```

---

## TODOs

> Implementation + Test = ONE Task. Never separate.
> EVERY task MUST have: Recommended Agent Profile + Parallelization info + QA Scenarios.

- [ ] 1. 数据模型 — CustomColumnDefinition + MetadataKey 注册表 + 持久化

  **What to do**:
  - 在 `MainWindow.Types.cs`（或新建文件）中定义 `CustomColumnDefinition` 类：
    ```csharp
    public class CustomColumnDefinition
    {
        public string ColumnId { get; set; } = Guid.NewGuid().ToString("N");
        public string Header { get; set; } = "";
        public string MetadataKey { get; set; } = "";
        public string DataType { get; set; } = "Text";  // Text / Integer / Size / Time
        public double Width { get; set; } = 100;
        public bool Visible { get; set; } = true;
        public int DisplayIndex { get; set; }
    }
    ```
  - 定义静态 `MetadataKeyRegistry`：`Dictionary<string, (string DisplayName, string DataType)>` 包含所有数据源（见设计文档 3.2 节）
  - 修改 `WindowSize` 类（`MainWindow.xaml.cs`）增加 `List<CustomColumnDefinition> CustomColumns`
  - 修改 `SaveWindowSettings()` / `LoadWindowSettings()` 序列化/反序列化 `CustomColumns`
  - 注意反序列化时 ColumnId 缺失（旧配置）的处理：自动生成新 GUID

  **Must NOT do**:
  - 不要修改现有 ColumnState 序列化逻辑
  - 不要在 plan 中引入新 JSON 库（用现有 System.Text.Json）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 纯数据类定义 + 序列化改动，无复杂逻辑
  - **Skills**: `[]`
  - **Skills Evaluated but Omitted**: N/A

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Tasks 4, 8, 9, 10
  - **Blocked By**: None

  **References**:
  - `MainWindow.xaml.cs:150-252` — 现有 ColumnState + WindowSize 类，参考其序列化模式
  - `MainWindow.xaml.cs:268-310` — `SaveWindowSettings()` 方法，参考其 JSON 序列化
  - `MainWindow.Types.cs:36-130` — 现有 ArchiveItem 类定义位置
  - Design doc 3.1-3.4 — CustomColumnDefinition + MetadataKey 注册表定义

  **QA Scenarios**:
  ```
  Scenario: 数据模型创建和序列化
    Tool: Bash (dotnet test / console app test)
    Steps:
      1. 新建 CustomColumnDefinition 实例，设置各属性
      2. 用 System.Text.Json 序列化为 JSON
      3. 反序列化回对象，验证所有属性一致
      4. 测试旧的 JSON（无 ColumnId）反序列化时自动生成 GUID
    Expected Result: 序列化-反序列化 round-trip 无误
    Evidence: .sisyphus/evidence/task-1-roundtrip.txt

  Scenario: MetadataKey 注册表完整性
    Tool: Bash (dotnet test)
    Steps:
      1. 枚举 MetadataKeyRegistry 所有条目
      2. 验证每条有 DisplayName 和 DataType
      3. 验证没有重复的 MetadataKey
    Expected Result: 所有条目合法，无重复
    Evidence: .sisyphus/evidence/task-1-registry.txt
  ```

  **Commit**: YES
  - Message: `feat(data): add CustomColumnDefinition model and persistence`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.Types.cs`, `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

- [ ] 2. ArchiveItem 增加 CustomMetadata 字典

  **What to do**:
  - 在 UI 层 `ArchiveItem`（`MainWindow.Types.cs:36`）增加：
    ```csharp
    public Dictionary<string, object?> CustomMetadata { get; set; } = new();
    public Dictionary<string, bool> MetadataLoading { get; set; } = new();   // 跟踪加载中状态
    public Dictionary<string, bool> MetadataFailed { get; set; } = new();    // 跟踪失败状态
    ```
  - 让 `ArchiveItem` 实现 `INotifyPropertyChanged`（如果尚未实现），以便 DataGrid 在元数据加载完成后刷新
    - 当前 UI 层 ArchiveItem 没实现 INPC → 需要增加
    - 添加 `PropertyChanged` 事件
    - 添加 `OnPropertyChanged(string propertyName)` 辅助方法
  - 添加索引器属性：`public object? this[string key] => CustomMetadata.GetValueOrDefault(key);`（可选）
  - **重要**: 为 `CustomMetadata` 字典的修改触发 PropertyChanged：每次设置值后调用 `OnPropertyChanged($"CustomMetadata[{key}]")`

  **Must NOT do**:
  - 不要修改 Core 层的 `ArchiveItem`（只改 UI 层子类）
  - 不要引入 ObservableDictionary — 简单 PropertyChanged 足够

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单文件属性扩展，改动量小
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3)
  - **Blocks**: Tasks 4, 10
  - **Blocked By**: None

  **References**:
  - `MainWindow.Types.cs:36-130` — ArchiveItem 类定义
  - `AGENTS.md` — "UI pattern: code-behind, not MVVM" 章节，说明不走 MVVM
  - `FolderNode` 在同一文件（`MainWindow.Types.cs:12-33`）有 INPC 实现模板可参考

  **QA Scenarios**:
  ```
  Scenario: CustomMetadata 读写和 PropertyChanged
    Tool: Bash (small test program)
    Steps:
      1. 创建 ArchiveItem 实例
      2. 设置 CustomMetadata["Title"] = "Test Document"
      3. 读取 CustomMetadata["Title"] 验证为 "Test Document"
      4. 验证 PropertyChanged 被正确触发（订阅事件检查）
      5. 测试 MetadataLoading / MetadataFailed 跟踪
    Expected Result: 字典读写正常，PropertyChanged 事件正确触发
    Evidence: .sisyphus/evidence/task-2-metadata.txt
  ```

  **Commit**: YES (with Task 1)
  - Message: `feat(data): add CustomMetadata dictionary to ArchiveItem`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.Types.cs`

- [ ] 3. CustomMetadataValueConverter

  **What to do**:
  - 新建 `src/MantisZip.UI/Converters/CustomMetadataValueConverter.cs`
  - 实现 `IValueConverter`：
    - `Convert`: 接收原始值 + `CustomColumnDefinition` 作为 parameter
      - 如果 value 为 null → 检查 MetadataLoading / MetadataFailed → 返回 "…"（加载中）或 "—"（失败或无数据）
      - 如果 value 不为 null → 按 DataType 格式化：
        - `Text`: `value.ToString()`
        - `Integer`: `string.Format("{0:N0}", value)`
        - `Size`: 调用 `FormatSize((long)value)`（复用 `MainWindow.Types.cs:132` 的方法）
        - `Time`: 秒 → `TimeSpan.FromSeconds((long)value)` 格式化为 `MM:SS` 或 `HH:MM:SS`
    - `ConvertBack`: 抛出 `NotSupportedException`（只读列）
  - 注册为 StaticResource 或在 Binding 中直接实例化

  **Must NOT do**:
  - 不要在 Converter 中触发 MetadataExtractor — 这个在 Binding 中另外处理
  - 不要复制 FormatSize 代码 — 把 `MainWindow.Types.cs` 中 `FormatSize` 改为 `internal static` 以供引用

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单一职责的 ValueConverter，逻辑简单
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2)
  - **Blocks**: Task 10
  - **Blocked By**: Task 1 (需要 CustomColumnDefinition 类型)

  **References**:
  - `MainWindow.Types.cs:132` — `FormatSize` 方法，参考其单位转换逻辑
  - `MainWindow.xaml:22-23` — 现有 Converter 注册模式（RatioToWidthConverter）
  - WPF IValueConverter 文档

  **QA Scenarios**:
  ```
  Scenario: 各类型格式化
    Tool: Bash (dotnet test)
    Steps:
      1. Text: "MyDoc" → "MyDoc"
      2. Integer: 1920 → "1,920"
      3. Size: 1048576 → "1 MB"
      4. Time: 3661 → "01:01:01"
      5. Time: 125 → "02:05"
      6. null → "—"
    Expected Result: 所有格式符合预期
    Evidence: .sisyphus/evidence/task-3-formats.txt
  ```

  **Commit**: YES (with Task 1)
  - Message: `feat(ui): add CustomMetadataValueConverter for column value formatting`
  - Files: `src/MantisZip.UI/Converters/CustomMetadataValueConverter.cs`

- [ ] 4. MetadataExtractor — 核心调度器

  **What to do**:
  - 新建 `src/MantisZip.Core/Utils/MetadataExtractor.cs`
  - 核心接口：
    ```csharp
    public static class MetadataExtractor
    {
        public static async Task<object?> ExtractAsync(
            string metadataKey,
            ArchiveItem item,
            string archivePath,
            string? password,
            IArchiveEngine engine,
            CancellationToken ct)
    }
    ```
  - 按 `metadataKey` switch 路由到对应提取方法
  - 对于需要临时提取的格式：复用 `ExtractPreviewFileAsync` 模式（见 `MainWindow.xaml.cs:139` 附近的 `ExtractPreviewFileAsync`）先提取文件到 `%TEMP%\MantisZip\Preview\`，然后调用 Core 解析器，最后清理临时文件
  - **关键集成**：现有 Core parsers 都返回 `FileFormatInfo`，所以 MetadataExtractor 需要从 `FileFormatInfo` 属性映射到 `metadataKey`
    - 例如 `"Title"` → 依次尝试 `PeParser` 的 `FileFormatInfo.ProductName`、`Id3v2Parser` 的 `Title`、`OfficeParser` 的 `Title`、`PdfParser` 的 `Title`
  - 对于不可提取的条目（加密 7z/RAR）：抛 `NotSupportedException`，调用处捕获返回 null
  - 提取结果缓存由调用方（ArchiveItem.CustomMetadata）管理，本类不做缓存

  **Must NOT do**:
  - 不要在 MetadataExtractor 里做 UI 层操作
  - 不要引入新的第三方包
  - 不要阻塞等待 — 所有提取方法都返回 Task

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: 需要理解 11 个解析器的 API，做统一调度和异常处理
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (块依赖)
  - **Parallel Group**: Wave 2 (needs Task 1)
  - **Blocks**: Tasks 5, 6, 7, 10
  - **Blocked By**: Task 1

  **References**:
  - `Core/Utils/PeParser.cs`、`Core/Utils/Id3v2Parser.cs`、`Core/Utils/FlacParser.cs` 等 — 各解析器的 Parse 方法签名和 FileFormatInfo 属性
  - `Core/Utils/FileFormatInfo.cs` — 所有解析器的返回类型
  - `MainWindow.xaml.cs:139` — `ExtractPreviewFileAsync` 临时提取模式
  - `Core/Abstractions/ArchiveEngine.cs:8-20` — Core 层的 ArchiveItem 类型（参数引用）
  - `MainWindow.xaml.cs:630-644` — 如何从 Core ArchiveItem 映射到 UI ArchiveItem

  **QA Scenarios**:
  ```
  Scenario: 调度器路由正确性
    Tool: Bash (单元测试)
    Steps:
      1. 调用 ExtractAsync("Title", ...) 验证路由到标题提取
      2. 调用 ExtractAsync("Width", ...) 验证路由到图片尺寸提取
      3. 调用 ExtractAsync("Duration", ...) 验证路由到时长提取
      4. 调用 ExtractAsync("INVALID_KEY", ...) 验证返回 null
    Expected Result: 路由正确，无效 key 优雅降级
    Evidence: .sisyphus/evidence/task-4-routing.txt

  Scenario: 加密条目降级
    Tool: Bash
    Steps:
      1. 模拟加密条目提取抛 NotSupportedException
      2. 验证返回 null（不崩溃）
    Expected Result: null 而非异常
    Evidence: .sisyphus/evidence/task-4-encrypted.txt
  ```

  **Commit**: YES
  - Message: `feat(core): add MetadataExtractor for lazy metadata extraction dispatch`
  - Files: `src/MantisZip.Core/Utils/MetadataExtractor.cs`

- [ ] 5. 图片尺寸提取 (Width, Height)

  **What to do**:
  - 在 MetadataExtractor 中实现 `ExtractImageDimension` 私有方法
  - 对于压缩包内图片：提取到临时目录 → 用 `System.Drawing.Bitmap` 或 `System.Windows.Media.Imaging.BitmapDecoder` 读取尺寸（只读 header，不需完整解码）
  - 使用 `BitmapDecoder` 的 `FrameCount > 0` 判断：用 `BitmapFrame.DecodePixelWidth = 1` 最小解码
  - 或者用 `FileStream` + 二进制解析图片头（更高效但实现更复杂），建议先尝试 `BitmapDecoder`
  - 返回 `(int Width, int Height)` 元组，MetadataExtractor 按 key 取对应值
  - 支持格式：JPG, PNG, GIF, BMP, TIFF, WebP（如果 BitmapDecoder 支持）

  **Must NOT do**:
  - 不要用 System.Drawing.Common（Windows-only 但可以，本项目就是 Windows-only）
  - 不要完整解码图片 — 只需读尺寸头

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单功能提取，调用现有 API
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 6, 7)
  - **Parallel Group**: Wave 2
  - **Blocks**: None
  - **Blocked By**: Task 4

  **References**:
  - `MainWindow.Preview.Image.cs:27` — `ShowImagePreviewAsync` 现有图片预览，参考其使用 BitmapImage 的模式
  - `BitmapDecoder` / `BitmapFrame` API

  **QA Scenarios**:
  ```
  Scenario: JPG 图片尺寸
    Tool: Bash
    Steps:
      1. 创建一个 1920x1080 的测试 JPG 文件
      2. 调用 ExtractAsync("Width", ...) → 1920
      3. 调用 ExtractAsync("Height", ...) → 1080
    Expected Result: 尺寸正确
    Evidence: .sisyphus/evidence/task-5-dimensions.txt
  ```

  **Commit**: YES (with Task 4)
  - Message: `feat(core): add image dimension extraction (Width, Height)`
  - Files: `src/MantisZip.Core/Utils/MetadataExtractor.cs`

- [ ] 6. 时长提取 (Duration, DurationSec)

  **What to do**:
  - 在 MetadataExtractor 中实现时长提取
  - 路由到现有解析器：
    - `FlacParser` → `FileFormatInfo.Duration`（如果有）
    - `RiffParser` → `FileFormatInfo.Duration`
    - `VideoParser` → `FileFormatInfo.Duration`
  - 格式化为秒（long）存入 `DurationSec`
  - 原始值可通过 Extra/RawFields 或直接存 TotalSeconds
  - `Duration`（Time 类型）也用秒存储，在 Converter 端格式化

  **Must NOT do**:
  - 不要重新实现音频/视频解析 — 复用现有 Parser

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 调用现有解析器，提取已有属性
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 5, 7)
  - **Parallel Group**: Wave 2
  - **Blocks**: None
  - **Blocked By**: Task 4

  **References**:
  - `Core/Utils/FlacParser.cs` — Duration 属性
  - `Core/Utils/RiffParser.cs` — Duration 属性
  - `Core/Utils/VideoParser.cs` — Duration 属性
  - `Core/Utils/FileFormatInfo.cs` — Duration 的类型

  **QA Scenarios**:
  ```
  Scenario: 音频时长提取
    Tool: Bash
    Steps:
      1. 用 FlacParser/RiffParser 解析测试文件
      2. 验证 DurationSec 返回正确的秒数（整数）
      3. 验证 Duration 在 Converter 中格式化为 MM:SS
    Expected Result: 时长提取和格式化正确
    Evidence: .sisyphus/evidence/task-6-duration.txt
  ```

  **Commit**: YES (with Task 4)
  - Message: `feat(core): add duration extraction (Duration, DurationSec)`
  - Files: `src/MantisZip.Core/Utils/MetadataExtractor.cs`

- [ ] 7. 文本字数 + 剩余 Text/Integer 字段

  **What to do**:
  - 文本字数 `WordCount`：
    - 对于 .txt, .md, .log, .csv, .xml, .json, .html 等文本格式
    - 提取到临时文件 → 用 `File.ReadAllText` 读取（UTF-8 检测复用 `Ude.NetStandard` 或用 StreamReader 自动检测）
    - 用 `text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length` 统计单词数
    - 如果文件过大（>10MB）跳过返回 null
  - 其他 Text 字段（Author, Company, ProductName, FontName 等）：
    - 从已有解析器的 `FileFormatInfo` 中提取对应属性
  - 其他 Integer 字段（SampleRate, PageCount, TableCount, GlyphCount 等）：
    - 从已有解析器的 `FileFormatInfo` 中提取对应属性
  - 确保所有提取方法都有超时/取消支持

  **Must NOT do**:
  - 不要对大文件（>10MB）做字数统计
  - 不要复制解析器逻辑 — 只是从 FileFormatInfo 映射字段

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 从现有 FileFormatInfo 映射字段 + 简单的文本字数统计
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 5, 6)
  - **Parallel Group**: Wave 2
  - **Blocks**: None
  - **Blocked By**: Task 4

  **References**:
  - `Core/Utils/FileFormatInfo.cs` — 所有可用属性
  - `Core/Utils/` 下各解析器 — 各字段映射来源
  - `MainWindow.Preview.Text.cs` — 现有文本预览编码检测（Ude.NetStandard）

  **QA Scenarios**:
  ```
  Scenario: 文本字数统计
    Tool: Bash
    Steps:
      1. 创建含 100 个单词的测试文本文件
      2. 调用 ExtractAsync("WordCount", ...) → 100
      3. 创建 15MB 大文件 → 返回 null（跳过）
    Expected Result: 字数正确，大文件跳过
    Evidence: .sisyphus/evidence/task-7-wordcount.txt
  ```

  **Commit**: YES (with Task 4)
  - Message: `feat(core): add remaining text/integer metadata extraction fields`
  - Files: `src/MantisZip.Core/Utils/MetadataExtractor.cs`

- [ ] 8. ChooseColumnsDialog XAML 布局

  **What to do**:
  - 新建 `src/MantisZip.UI/ChooseColumnsDialog.xaml` + `src/MantisZip.UI/ChooseColumnsDialog.xaml.cs`
  - 对话框窗口标题："自定义列"
  - 主题绑定（遵循 AGENTS.md 规则 3：新 UI 控件必须使用主题样式）：
    - `Background="{DynamicResource Theme_WindowBg}"`
    - `Foreground="{DynamicResource Theme_TextPrimary}"`
    - 按钮、ComboBox、ListBox 等均绑定主题色
  - 布局结构：
    ```
    ┌──────────────────────────────────────────┐
    │ 自定义列                          [?][X] │
    ├──────────────────────────────────────────┤
    │ ┌─ 添加自定义列 ─────────────────────┐   │
    │ │ 列类型: [Text ▼]  (ComboBox)      │   │
    │ │ 数据源: [Title ▼]  (ComboBox)      │   │
    │ │ 列表头: [标题         ] (TextBox)  │   │
    │ │                      [添加列 →]   │   │
    │ └────────────────────────────────────┘   │
    │ ┌─ 已选自定义列 ─────────────────────┐   │
    │ │ ListView/ListBox                   │   │
    │ │  ┌───────────────────────────┐     │   │
    │ │  │ 标题     Title     Text  │     │   │
    │ │  │ 宽度     Width     Int   │     │   │
    │ │  │ 时长     Duration  Time  │     │   │
    │ │  └───────────────────────────┘     │   │
    │ │  [↑] [↓] [×]                       │   │
    │ └────────────────────────────────────┘   │
    │                              [确定][取消] │
    └──────────────────────────────────────────┘
    ```
  - 尺寸：Width=480, Height=450, MinWidth=400, MinHeight=350
  - WindowStartupLocation="CenterOwner", ShowInTaskbar=False, ResizeMode="CanResizeWithGrip"

  **Must NOT do**:
  - 不要在 XAML 中写逻辑代码 — 所有交互逻辑在 .cs 中
  - 不要遗漏主题色绑定

  **Recommended Agent Profile**:
  - **Category**: `quick` (XAML layout)
    - Reason: 标准 WPF 对话框布局，纯 XAML 工作量
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Task 9)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 11
  - **Blocked By**: Task 1 (需要 CustomColumnDefinition 类型)

  **References**:
  - `MainWindow.xaml:1-40` — 主题色绑定模式（Theme_WindowBg, Theme_TextPrimary 等）
  - `CompressSettingsWindow.xaml` — 参考现有对话框的主题绑定风格
  - `AGENTS.md` — "规则 3：新 UI 控件必须应用主题样式"

  **QA Scenarios**:
  ```
  Scenario: 对话框打开
    Tool: Build + run app
    Steps:
      1. 编译成功
      2. 打开任意压缩包
      3. 右键列标题 → 点击"自定义列…"
      4. 对话框弹出，布局正确
    Expected Result: 对话框正常打开，主题应用正确
    Evidence: .sisyphus/evidence/task-8-dialog.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add ChooseColumnsDialog layout`
  - Files: `src/MantisZip.UI/ChooseColumnsDialog.xaml`, `src/MantisZip.UI/ChooseColumnsDialog.xaml.cs`

- [ ] 9. ChooseColumnsDialog 交互逻辑

  **What to do**:
  - 在 `ChooseColumnsDialog.xaml.cs` 中实现：
  - 加载时从 MetadataKeyRegistry 获取所有数据源，按 DataType 分组
  - ComboBox "列类型" 选项：Text / Integer / Size / Time
    - 选中类型 → 过滤数据源 ComboBox 只显示该类型的条目
    - 自动选中数据源 ComboBox 第一项
  - ComboBox "数据源" 选项：绑定到过滤后的数据源列表（显示 DisplayName，存储 MetadataKey）
  - TextBox "列表头"：数据源选中时自动填入 DisplayName，允许编辑
  - "添加列" 按钮：
    - 创建 CustomColumnDefinition 实例（ColumnId=新GUID, Header=表头文本, MetadataKey=选中值, DataType=当前类型）
    - 添加到已选列表
    - 已选列表绑定到 `ObservableCollection<CustomColumnDefinition>`
  - 已选列 ListBox/ListView：
    - 显示每行的：Header / MetadataKey / DataType
    - 每行右侧 × 按钮删除
    - ↑↓ 按钮调整 DisplayIndex
  - "确定" 按钮：`DialogResult = true + 返回 List<CustomColumnDefinition>`
  - "取消" 按钮：`DialogResult = false`
  - 打开对话框时代入现有配置（编辑模式），关闭时返回修改后的配置

  **Must NOT do**:
  - 不要在对话框里持久化 — 由调用方保存
  - 不要引入 MVVM — 直接 code-behind

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 标准对话框交互逻辑，无复杂异步操作
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Task 8)
  - **Parallel Group**: Wave 3
  - **Blocks**: Task 11
  - **Blocked By**: Task 1 (需要 CustomColumnDefinition 类型)

  **References**:
  - `CompressSettingsWindow.xaml.cs` — 参考现有对话框的交互模式
  - `PasswordDialog.xaml.cs` — 参考简单对话框的 DialogResult 模式
  - Design doc 4.2 — 对话框交互流程

  **QA Scenarios**:
  ```
  Scenario: 添加和删除自定义列
    Tool: Build + run app
    Steps:
      1. 打开对话框
      2. 选类型 Text → 数据源过滤为 Title/Author/Company/...
      3. 选 Title → 表头自动填入"标题"
      4. 改表头为"文档标题" → 点击添加
      5. 选类型 Integer → 选 Width → 点击添加
      6. 已选列显示 2 行
      7. 点 ↑ 调整顺序 → 点 × 删除第二行
      8. 点确定 → 对话框关闭，返回配置
    Expected Result: 添加/删除/排序正确
    Evidence: .sisyphus/evidence/task-9-dialog-flow.png
  ```

  **Commit**: YES (with Task 8)
  - Message: `feat(ui): implement ChooseColumnsDialog interaction logic`
  - Files: `src/MantisZip.UI/ChooseColumnsDialog.xaml.cs`

- [ ] 10. 动态列生成 (RebuildCustomColumns)

  **What to do**:
  - 在 `MainWindow.xaml.cs` 中新增 `RebuildCustomColumns()` 方法
  - 逻辑：
    1. 确定固定列的 SortMemberPath 集合：`Name`, `Size`, `CompressedSize`, `RatioSort`, `Crc32`, `LastModified`, `IsEncrypted`
    2. 从 `FileListGrid.Columns` 移除所有不属于固定列的列
    3. 从 `WindowSize.CustomColumns` 读取配置，筛选 Visible=true，按 DisplayIndex 排序
    4. 对每个定义创建 `DataGridTextColumn`：
       ```csharp
       var binding = new Binding($"CustomMetadata[{def.MetadataKey}]")
       {
           Converter = new CustomMetadataValueConverter(),
           ConverterParameter = def,
           TargetNullValue = "—",
           FallbackValue = "—"
       };
       var col = new DataGridTextColumn
       {
           Header = def.Header,
           Binding = binding,
           SortMemberPath = $"CustomMetadata[{def.MetadataKey}]",
           Width = new DataGridLength(def.Width),
           MinWidth = 60,
           MaxWidth = 400,
           CanUserSort = true,
           IsReadOnly = true
       };
       ```
    5. 添加到 `FileListGrid.Columns`
  - **懒加载触发**：在 ValueConverter 中，当值为 null 且未在加载中状态时，fire-and-forget 启动后台任务：
    ```csharp
    if (value == null && !item.MetadataLoading.ContainsKey(key))
    {
        item.MetadataLoading[key] = true;
        _ = LoadCustomMetadataAsync(item, key, ...);
    }
    ```
    `LoadCustomMetadataAsync` 调用 `MetadataExtractor.ExtractAsync()`，完成后设置 `item.CustomMetadata[key] = result`，然后触发 PropertyChanged
  - 调用时机：
    - `LoadWindowSettings()` 之后
    - `ChooseColumnsDialog` 确定关闭后
    - `LoadArchiveAsync` 加载档案后（filter 之前或之后）
  - 确保列标题右键菜单的 ColumnHeaderContextMenu_Opened 包含自定义列的切换项

  **Must NOT do**:
  - 不要修改固定列的定义和布局
  - 不要再造 DataGrid 排序轮子 — 用现有的 `FileListGrid_Sorting` 机制

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: 核心实现，涉及 DataGrid 动态操作 + 懒加载调度
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (核心集成)
  - **Parallel Group**: Wave 3 (after Tasks 1, 2, 3, 4)
  - **Blocks**: Tasks 11, 12, 13
  - **Blocked By**: Tasks 1, 2, 3, 4, 8, 9

  **References**:
  - `MainWindow.xaml:405-562` — DataGrid 定义和现有列模板
  - `MainWindow.xaml.cs:150-252` — SaveWindowSettings 列管理
  - `MainWindow.UI.cs:938-982` — ColumnHeaderContextMenu_Opened 列标题右键菜单
  - `MainWindow.UI.cs:874-933` — FileListGrid_Sorting 自定义排序
  - `MainWindow.xaml:456-465` — Name 列的 DataGridTemplateColumn 定义模式

  **QA Scenarios**:
  ```
  Scenario: 自定义列在 DataGrid 中显示
    Tool: Build + run app
    Steps:
      1. 添加自定义列（Title, Width, Duration）
      2. 确认 → 自定义列出现在 DataGrid 中
      3. 列标题显示正确，值显示 "…"（加载中）
      4. 等加载完成 → 值显示
      5. 重新打开档案 → 自定义列仍在
    Expected Result: 列生成正确，值懒加载后显示，重启持久化
    Evidence: .sisyphus/evidence/task-10-columns.png

  Scenario: 列右键菜单显示自定义列
    Tool: Build + run app
    Steps:
      1. 右键列标题 → 弹出菜单包含自定义列名称
      2. 可以切换显隐
    Expected Result: 右键菜单包含所有自定义列
    Evidence: .sisyphus/evidence/task-10-contextmenu.png
  ```

  **Commit**: YES
  - Message: `feat(ui): add dynamic column generation and lazy loading`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

- [ ] 11. 菜单位置整合

  **What to do**:
  - 列标题右键菜单 `ColumnHeaderContextMenu`（在 `ColumnHeaderContextMenu_Opened` 中动态构建）：
    - 在现有列切换项底部新增分隔线和"自定义列…"项
    - 动态项的事件处理
  - 主菜单 `查看` 下新增 `自定义列…` 菜单项：
    - Header: `☰ 自定义列…`
    - InputGestureText: `Ctrl+Shift+C`
    - Click: `OpenChooseColumnsDialog`
  - `OpenChooseColumnsDialog` 方法：
    1. 创建 ChooseColumnsDialog 实例（Owner=this）
    2. 代入当前 WindowSize.CustomColumns
    3. 如果 DialogResult=true → 保存 → 调用 RebuildCustomColumns()
  - 快捷键注册（在 MainWindow constructor 或 Loaded 中）
  - 首次使用（CustomColumns 为 null/空）时默认添加一个示例列？— **不做**，让用户自己配置

  **Must NOT do**:
  - 不要破坏现有右键菜单的行为
  - 不需要本地化"自定义列"字符串（后续可以加）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 菜单项添加 + 事件处理，改动小
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 12, 13)
  - **Parallel Group**: Wave 4
  - **Blocks**: None
  - **Blocked By**: Tasks 8, 9, 10

  **References**:
  - `MainWindow.UI.cs:938-982` — ColumnHeaderContextMenu_Opened 方法（动态构建菜单）
  - `MainWindow.xaml:50-120` — MenuBar 定义，"查看"菜单位置
  - `MainWindow.Menu.cs` — 现有菜单事件处理方法

  **QA Scenarios**:
  ```
  Scenario: 右键菜单打开对话框
    Tool: Build + run app
    Steps:
      1. 右键列标题 → 弹出菜单
      2. 菜单底部有分隔线 + "自定义列…"
      3. 点击 → ChooseColumnsDialog 弹出
      4. 点击确定 → 列刷新
    Expected Result: 入口正常，对话框正确打开
    Evidence: .sisyphus/evidence/task-11-menu.png
  ```

  **Commit**: YES (with Task 10)
  - Message: `feat(ui): add custom columns menu entries`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml`, `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`, `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

- [ ] 12. 自定义列排序支持

  **What to do**:
  - 查看现有 `FileListGrid_Sorting`（`MainWindow.UI.cs:874-933`）处理自定义排序逻辑
  - 确保自定义列的 `SortMemberPath` 设置为 `CustomMetadata[key]`
  - 对于自定义列点击排序时，DataGrid 默认排序能工作（因为 SortMemberPath 指向了字典键）
  - 如果默认排序不行（字典键无法直接被 DataGrid 排序），在 `FileListGrid_Sorting` 中检测 `e.Column.SortMemberPath` 是否以 `CustomMetadata[` 开头
    - 如果是，用 `ListCollectionView.CustomSort` 或手动排序：从 CustomMetadata 字典取值比较
    - 参考现有排序实现的 ListSortDirection 处理
  - 按 Text 类型排序时用字符串序，Integer/Size/Time 用数值序（需要从字典中取原始 long 值比较，而不是格式化后的字符串）

  **Must NOT do**:
  - 不要破坏现有 7 列的排序行为
  - 不要在自定义列排序中重新提取元数据 — 只用已缓存的值

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low`
    - Reason: 理解现有排序实现 + 扩展支持自定义列
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 11, 13)
  - **Parallel Group**: Wave 4
  - **Blocks**: None
  - **Blocked By**: Task 10

  **References**:
  - `MainWindow.UI.cs:874-933` — `FileListGrid_Sorting` 方法
  - `MainWindow.xaml:430` — Sorting 事件绑定
  - `MainWindow.xaml.cs:378-397` — `CaptureCurrentSort` 方法

  **QA Scenarios**:
  ```
  Scenario: 自定义列排序
    Tool: Build + run app
    Steps:
      1. 添加 Width (Integer) 列
      2. 点击 Width 列标题 → 升序排列
      3. 再次点击 → 降序排列
      4. 验证排序结果正确（数值序，非字典序）
    Expected Result: 排序正确
    Evidence: .sisyphus/evidence/task-12-sort.png
  ```

  **Commit**: YES (with Task 10)
  - Message: `feat(ui): support sorting on custom columns`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.UI.cs`

- [ ] 13. 缓存管理 + 清理 + 边缘情况

  **What to do**:
  - 缓存管理：
    - ArchiveItem.CustomMetadata 已缓存提取结果
    - 切换文件夹（`FilterFiles`）时不清缓存（同一个压缩包内）
    - 关闭压缩包（`CloseArchive_Click`）时清空所有 `CustomMetadata`
  - 边缘情况处理：
    - 加密条目无法提取 → MetadataFailed[key]=true，显示 "—"（锁图标或文字）
    - 提取超时/取消 → 显示 "—"
    - 大文件字数统计跳过（>10MB）→ 显示 "—"
    - 无该元数据的格式（如给 .txt 取 Width）→ 显示 "—"
    - 不支持的格式 → 解析器返回 null → 显示 "—"
    - 临时文件清理异常 → 记录日志（`App.TraceLog`）不抛给用户
    - 多个列同时触发同一条目的不同 metadataKey → MetadataExtractor 内部用 SemaphoreSlim 控制并发 (per-entry)
    - 快速滚动时的可见行变化 → VirtualizingStackPanel 自动管理，不需要手动处理
  - 添加取消支持：`CancellationToken` 传递到每个提取方法

  **Must NOT do**:
  - 不要在滚动时做额外优化 — VirtualizingStackPanel 已处理
  - 不要缓存临时文件 — 用后即删

  **Recommended Agent Profile**:
  - **Category**: `unspecified-low`
    - Reason: 清理逻辑 + 边界情况处理，分散在多个文件中
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES (with Tasks 11, 12)
  - **Parallel Group**: Wave 4
  - **Blocks**: None
  - **Blocked By**: Tasks 4, 10

  **References**:
  - `MainWindow.xaml.cs:93-114` — `CloseArchive_Click` 清理逻辑
  - `MainWindow.UI.cs:268-350` — `FilterFiles` 目录切换
  - `Core/Utils/LogRedactor.cs` — 日志记录模式
  - `MainWindow.xaml.cs:139` — `ExtractPreviewFileAsync` 临时文件模式

  **QA Scenarios**:
  ```
  Scenario: 关闭压缩包后缓存清理
    Tool: Build + run app
    Steps:
      1. 打开压缩包，添加自定义列，数据加载完成
      2. 关闭压缩包
      3. 检查 ArchiveItem.CustomMetadata 是否清空
    Expected Result: 缓存正确清理

  Scenario: 加密条目优雅降级
    Tool: Build + run app
    Steps:
      1. 打开含加密条目的压缩包
      2. 加密条目的自定义列显示 "—" 而非空白
    Expected Result: 降级正确
    Evidence: .sisyphus/evidence/task-13-cleanup.txt
  ```

  **Commit**: YES (with Task 10)
  - Message: `feat(ui): add cache management and edge case handling`
  - Files: `src/MantisZip.UI/MainWindow/MainWindow.xaml.cs`

- [ ] 14. 集成测试 + 最终 QA

  **What to do**:
  - 完整端到端验证：
    1. 编译项目 → `dotnet build` 无错误
    2. 运行应用，打开一个包含多种文件的 ZIP 压缩包
    3. 添加自定义列（Text: Title, Integer: Width, Time: Duration）
    4. 验证列显示正确，懒加载工作
    5. 排序、调整列宽、调整列顺序
    6. 关闭和重新打开应用 → 配置持久化
    7. 测试边缘情况：加密条目、空压缩包、大文件
  - 检查点：
    - `CanUserReorderColumns="False"` 不影响自定义列显示
    - 列标题右键菜单包含自定义列开关
    - 主菜单 "查看 → 自定义列…" 工作
    - 没有内存泄漏（临时文件清理）
    - 没有 UI 线程阻塞（懒加载）
  - 保存证据到 `.sisyphus/evidence/`

  **Must NOT do**:
  - 不需要自动化 UI 测试 — 手动验证足够（当前项目无测试框架）

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: 全面集成验证，需要手动操作和调试
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO (最终验证)
  - **Parallel Group**: Wave FINAL
  - **Blocks**: None
  - **Blocked By**: Tasks 11, 12, 13

  **References**:
  - 整份 plan 的所有任务
  - `AGENTS.md` — 项目构建命令

  **QA Scenarios**:
  ```
  Scenario: 完整端到端流程
    Tool: Build + run app
    Steps:
      1. dotnet build 通过
      2. 运行应用 → 打开一个含图片、文档、音频的测试 ZIP
      3. 添加自定义列：Title(Text), Width(Integer), Duration(Time)
      4. 各列正常显示值（图片显示 Title/Width，音频显示 Duration）
      5. 点击列标题排序
      6. 关闭应用 → 重新打开 → 自定义列还在
      7. 右键列标题 → 关闭某个自定义列 → 它隐藏了
    Expected Result: 所有功能正常
    Evidence: .sisyphus/evidence/task-14-e2e.png
  ```

  **Commit**: NO (验证任务)

---

## Final Verification Wave

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. Verify each "Must Have" item has a corresponding task. Check all "Must NOT Have" items are absent from implementation. Verify evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build`. Review changed files for: WPF anti-patterns (no UI thread blocking), proper theme binding on new controls, catch(Exception) without logging, code-behind consistency. Check for AI slop: over-abstraction, unused imports, commented-out code.
  Output: `Build [PASS/FAIL] | Theme [PASS/FAIL] | Code style [N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Execute key QA scenarios from all tasks: dialog flow, column generation, lazy loading, sorting, persistence, edge cases. Verify no crashes or visual glitches. Start from clean state.
  Output: `Scenarios [N/N pass] | Edge cases [N tested] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  Read each task's requirements, then verify against actual code. Ensure only planned files touched. Check no scope creep (no expression engine, no MVVM refactoring, no template changes to existing columns).
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

| Task | Message | Files |
|------|---------|-------|
| 1+2+3 | `feat(data): add CustomColumnDefinition, CustomMetadata, and value converter` | `MainWindow.Types.cs`, `MainWindow.xaml.cs`, `Converters/CustomMetadataValueConverter.cs` |
| 4+5+6+7 | `feat(core): add MetadataExtractor with image/duration/text extraction` | `Core/Utils/MetadataExtractor.cs` |
| 8+9 | `feat(ui): add ChooseColumnsDialog for custom column configuration` | `ChooseColumnsDialog.xaml`, `ChooseColumnsDialog.xaml.cs` |
| 10+11+12+13 | `feat(ui): implement dynamic custom columns with lazy loading and sorting` | `MainWindow.xaml.cs`, `MainWindow.xaml`, `MainWindow.UI.cs` |

---

## Success Criteria

### Final Checklist
- [ ] User can add custom columns of Text/Integer/Size/Time types via ChooseColumnsDialog
- [ ] Custom columns display lazy-loaded metadata for archive entries
- [ ] Custom column configuration persists across app restarts
- [ ] Sorting works on custom columns (numeric sort for Integer/Size/Time)
- [ ] Encrypted/unreadable entries show "—" gracefully
- [ ] Column header right-click menu shows custom columns for toggle
- [ ] No UI thread blocking during metadata extraction
- [ ] All 7 existing fixed columns unchanged
