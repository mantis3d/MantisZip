# Office 文档内容预览 (Office Content Preview)

> **状态**: 📋 计划中 | **阶段**: 新增子计划

## TL;DR

> **核心目标**: 为 Office 文档（docx/xlsx/pptx）添加实质性内容预览，从当前仅显示元数据的"第三类"提升为显示富文本（docx）、表格（xlsx）、幻灯片文本（pptx）。
>
> **交付物**:
> - DOCX: Mammoth → HTML → WebView2 渲染
> - XLSX: ClosedXML → DataTable → DataGrid（复用 ShowTablePreview）
> - PPTX: 手动解析 XML 提取幻灯片文本 → 文本列表展示
>
> **预估**: ~8h
> **并行执行**: 是 — 3 个格式可并行开发
> **关键路径**: NuGet 集成 → 三个格式开发 → ShowPreviewAsync 调度改造 → 集成测试

---

## Context

### 原始需求

原始计划 [preview-extended-formats.md](preview-extended-formats.md) 将 Office 文档归类为"第三类（纯信息展示）"，内容区标为⬜空置。实际实现仅通过 `PreviewTextBox` 展示元数据文本，无实质性内容预览。

用户要求为 Office 文档添加内容预览能力。

### 调研结论

| 格式 | 方案 | 依赖 | 许可证 | 内容区 |
|------|------|------|--------|:------:|
| DOCX | Mammoth → HTML → WebView2 | `Mammoth` NuGet | BSD-2-Clause | ✅ 富文本渲染 |
| XLSX | ClosedXML → DataTable → DataGrid | `ClosedXML` NuGet | MIT | ✅ 表格视图 |
| PPTX | 手动解析 `a:t` XML → 文本列表 | 零新增 | — | ✅ 幻灯片文本 |

### 关键优化：XLSX/PPTX 无需完整文件提取

`OfficeParser.cs` 已用 `ZipFile.OpenRead(filePath)` 直接在源文件上读取内部 XML。XLSX 和 PPTX 的内容预览也走相同路径——**不需要调用 `ExtractPreviewFileAsync` 提取完整文件**。DOCX 仍需提取（Mammoth 需要文件路径或 Stream）。

### 用户明确决定

- PPTX: 手动解析 XML（零额外依赖）
- DOCX HTML: 默认 Mammoth 样式（不叠加自定义 CSS）

---

## Work Objectives

### 核心目标
将 Office 文档从"仅元数据"升级为"实质性内容预览"，同时保留现有信息面板元数据。

### 交付物
- `MainWindow.Preview.cs` — `ShowPreviewAsync` 调度分支改造
- `MainWindow.Preview.Office.cs` — 新增三个方法：`ShowDocxPreview` / `ShowXlsxPreview` / `ShowPptxPreview`
- `src/MantisZip.UI/MantisZip.UI.csproj` — 新增 `Mammoth` + `ClosedXML` NuGet 依赖
- `strings.zh.json` / `strings.en.json` — 新增错误/回退文本的翻译键

### 必须包含
- [ ] DOCX: Mammoth → HTML → WebView2，遵守 MaxPreviewFileSize 限制
- [ ] XLSX: ClosedXML → DataTable → ShowTablePreview，不提取完整文件
- [ ] PPTX: 手动解析 `ppt/slides/slideN.xml` → `a:t` 文本提取，不提取完整文件
- [ ] 所有三种格式保留现有信息面板元数据（标题/作者/页数）
- [ ] 所有三种格式正确调用 HideAllPreviewControls + ShowPreviewPanel 模式

### 必须不包含（护栏）
- [ ] 不解析 DOCX 中的嵌入图像（Mammoth 不提供图片保存回调）
- [ ] 不支持 .docm/.xlsm/.pptm（宏文件）——后续再说
- [ ] 不支持 .doc/.xls/.ppt（二进制格式）——无法作为 ZIP 打开
- [ ] PPTX 不解析 SmartArt/dgm、图表/c:、数学/m: 命名空间
- [ ] PPTX 不处理表格内的 a:t（`a:tbl` → `a:tc` 路径）
- [ ] XLSX 不尝试渲染图表为图像
- [ ] 不修改现有的 `OfficeParser.cs`——新代码与之分离

---

## Verification Strategy

> **零人工干预**——所有验证由执行 agent 完成。

### Test Decision
- **基础设施**: 已有 `tests/MantisZip.Tests/`（xUnit，40+ cases）
- **自动化测试**: 无（UI 渲染预览手动验证更高效）
- **QA 验证**: Agent-Executed QA Scenarios（每个任务自带详细步骤）

### QA Policy
每个任务包含 Agent-Executed QA Scenarios。证据保存到 `.sisyphus/evidence/`。

- 前端/UI: 使用 Playwright / 手动检查控件可见性
- 构建验证: `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 必须通过

---

## Execution Strategy

### 并行执行波浪

```
Wave 1 (基础 — 可并行):
├── Task 1: NuGet 集成 (Mammoth + ClosedXML) [~0.5h]
├── Task 2: PPTX 文本解析 + 预览 [~3h]
├── Task 3: XLSX 表格预览 [~2h]
└── Task 4: DOCX Mammoth 预览 [~2h]

Wave 2 (集成 + 最终验证):
├── Task 5: ShowPreviewAsync 调度改造 [~1h]
├── Task 6: 本地化字符串 + 构建验证 [~0.5h]
└── Task 7: 集成回归测试 [~0.5h]
```

由于三个格式方法互不依赖，可并行开发。Wave 2 需要 Wave 1 所有任务完成。

---

## TODOs

- [ ] 1. NuGet 依赖集成

  **What to do**:
  - 在 `src/MantisZip.UI/MantisZip.UI.csproj` 中添加两个 NuGet 包：
    - `dotnet add src\MantisZip.UI\MantisZip.UI.csproj package Mammoth`
    - `dotnet add src\MantisZip.UI\MantisZip.UI.csproj package ClosedXML`
  - 运行 `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 确认编译通过
  - 确认两个包均为纯托管依赖，无 native 层

  **Must NOT do**:
  - 不添加到 `MantisZip.Core.csproj`（仅在 UI 层使用）
  - 不修改现有 package 引用

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 3, Task 4
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 通过
  - [ ] `dotnet list src\MantisZip.UI\MantisZip.UI.csproj package` 显示 Mammoth + ClosedXML

  **QA Scenarios**:
  ```
  Scenario: NuGet 包安装验证
    Tool: Bash
    Steps:
      1. 运行 dotnet list src\MantisZip.UI\MantisZip.UI.csproj package
    Expected Result: 输出包含 Mammoth 和 ClosedXML 条目
    Evidence: .sisyphus/evidence/task-1-nuget-list.txt

  Scenario: 构建验证
    Tool: Bash
    Steps:
      1. 运行 dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: Build succeeded，无错误
    Evidence: .sisyphus/evidence/task-1-build.txt
  ```

  **Commit**: YES
  - Message: `dep(ui): add Mammoth + ClosedXML NuGet packages for Office content preview`
  - Files: `src/MantisZip.UI/MantisZip.UI.csproj`

---

- [ ] 2. PPTX 幻灯片文本预览

  **What to do**:
  - 在 `MainWindow.Preview.cs` 附近新建 `MainWindow.Preview.Office.cs`（或直接在 `MainWindow.Preview.Metadata.cs` 追加）
  - 新增方法 `ShowPptxPreview(string filePath, ArchiveItem item)`:
    1. 直接从 `filePath` 打开 ZIP：`ZipFile.OpenRead(filePath)`
    2. 遍历 `ppt/slides/slideN.xml`（`StartsWith("ppt/slides/slide")` + `EndsWith(".xml")`）
    3. 每个 slide XML 用 `XDocument.Load`，提取 `a:t` 文本：
       ```csharp
       XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
       var texts = slideDoc.Descendants(a + "t").Select(t => t.Value).Where(v => !string.IsNullOrWhiteSpace(v));
       ```
    4. 每个 `a:t` 中的文本拼接为段落（`a:br` 也考虑进去）
    5. 构建 PreviewTextBox 文本格式：
       ```
       ── 幻灯片 1 ──
       标题文字
       正文段落...

       ── 幻灯片 2 ──
       标题文字
       ```
    6. 展示：`HideAllPreviewControls()` → `PreviewTextBox.Text = 结果` → `PreviewTextBox.Visibility = Visible`
    7. 保留现有 `SetPreviewInfo(item)` + `SetFormatSpecificInfo(...)` 调用
    8. 空幻灯片（无 `a:t` 节点）显示"（此幻灯片无文本）"

  **需要处理的边缘情况**:
  - 仅图片/SmartArt/图表的幻灯片（无 `a:t`）→ 显示回退文本
  - 在 MS 团队中 `a:br` 换行处理：`a:t` 之间可能有 `a:br`，读取时要在跑后面适当插入换行
  - 空的 `a:t` 节点（value=""）→ 跳过
  - 损坏的 slide XML → try-catch，记录日志，跳过该幻灯片
  - 文件总共 0 幻灯片 → 显示"此演示文稿为空"

  **Must NOT do**:
  - 不解析 SmartArt (`dgm:`)、图表 (`c:`)、数学 (`m:`) 命名空间
  - 不解析表格内的 `a:t`（`a:tbl` → `a:tc` 路径）
  - 不处理 `p:grpSp`（形状组）内的递归文本
  - 不提取 `a:fld`（页脚/字段）内容

  **References**:
  - `Core/Utils/OfficeParser.cs:117-128` — `CountPptxSlides` 的现有幻灯片计数逻辑（遍历 `ppt/slides/slide*`）
  - `UI/MainWindow.Preview.Metadata.cs:361-385` — 现有 `ShowOfficePreview` 模式
  - `UI/MainWindow.Preview.cs:261` — `HideAllPreviewControls()` 调用模式

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 5
  - **Blocked By**: None

  **Acceptance Criteria**:
  - [ ] 选择含文字的 .pptx → PreviewTextBox 显示幻灯片文本（每张幻灯片带分隔线）
  - [ ] 选择纯图片的 .pptx → 显示"（此幻灯片无文本）"
  - [ ] 空演示文稿 → 显示"此演示文稿为空"
  - [ ] 切换回其他格式 → PPTX 文本清理正常

  **QA Scenarios**:
  ```
  Scenario: PPTX 文本提取 — 正常文件
    Tool: Bash
    Preconditions: 在测试目录准备一个包含 3 张幻灯片、每张有标题+正文的 .pptx
    Steps:
      1. 运行 dotnet run --project src\MantisZip.UI --open <path/to/test.pptx>
      2. 使用 Playwright 或截图确认内容
    Expected Result: PreviewTextBox 显示 3 段幻灯片文本，每段以 "── 幻灯片 N ──" 开头
    Evidence: .sisyphus/evidence/task-2-pptx-text.txt（截图）

  Scenario: PPTX 空幻灯片
    Tool: Bash
    Preconditions: 准备一个纯图片（无文字）的 .pptx
    Steps:
      1. 打开该文件
    Expected Result: 对应幻灯片显示 "（此幻灯片无文本）" 或类似回退
    Evidence: .sisyphus/evidence/task-2-pptx-empty.txt
  ```

  **Commit**: YES
  - Message: `feat(preview): add PPTX slide text preview via manual XML parsing`
  - Files: `src/MantisZip.UI/MainWindow.Preview.Office.cs` (new)

---

- [ ] 3. XLSX 工作表内容预览

  **What to do**:
  - 新增方法 `ShowXlsxPreview(string filePath, ArchiveItem item)`:
    1. 直接用 `new XLWorkbook(filePath)` 打开 xlsx
    2. 读取第一个工作表 `workbook.Worksheet(1)`
    3. 获取 `RangeUsed()` 确定有效数据区域
    4. 受限 `MaxTablePreviewRows` (100) 和 `MaxTablePreviewCols` (100)
    5. 构建 `DataTable`：
       - 首行作为列名
       - 后续行作为数据行
       - 单元格值调用 `.GetString()` 或 `.ToString()`
    6. 调用 `ShowTablePreview(dataTable, item, $"{worksheet.Name} - {Path.GetFileName(filePath)}")`
    7. 在 extra info panel 添加工作表名
    8. `finally` 中 `workbook.Dispose()`

  **处理**:
  - 空工作表（`RangeUsed()` 为 null）→ 显示"此工作表中没有数据"
  - 受密码保护的 xlsx → `XLWorkbook` 构造函数抛出 `InvalidOperationException` → 捕获并显示回退
  - 合并单元格 → ClosedXML 只返回左上角的值——可以接受
  - 公式 → `.Value` 返回缓存值——正确行为
  - 日期格式 → ClosedXML 返回 `DateTime` 对象，`.ToString()` 可能显示序列号——考虑用 `cell.GetFormattedString()` 代替

  **Must NOT do**:
  - 不读取 Excel 图表
  - 不刷新外部数据连接（仅缓存值）
  - 只读第一个工作表（不处理多工作表）

  **References**:
  - `MainWindow.Preview.Text.cs` — `ShowTablePreview` 方法
  - `MainWindow.Preview.cs` — `MaxTablePreviewRows`, `MaxTablePreviewCols` 常量

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 5
  - **Blocked By**: Task 1 (NuGet)

  **Acceptance Criteria**:
  - [ ] 选择有数据的 .xlsx → DataGrid 显示前 100 行 x 100 列
  - [ ] 选择空 .xlsx → 显示回退文本
  - [ ] 列名取自第一行
  - [ ] 受密码保护的 .xlsx → 不崩溃，显示回退

  **QA Scenarios**:
  ```
  Scenario: XLSX 表格预览 — 正常文件
    Tool: Bash
    Preconditions: 准备一个 5 行 3 列带表头的 .xlsx
    Steps:
      1. 打开该文件
      2. 确认 PreviewCsvGrid 显示正确数据
    Expected Result: DataGrid 显示 5 行 × 3 列，表头正确
    Evidence: .sisyphus/evidence/task-3-xlsx-normal.txt

  Scenario: XLSX 密码保护
    Tool: Bash
    Preconditions: 准备一个受密码保护的 .xlsx
    Steps:
      1. 打开该文件
    Expected Result: 不崩溃，显示"无法预览"或类似回退，信息面板显示元数据
    Evidence: .sisyphus/evidence/task-3-xlsx-protected.txt
  ```

  **Commit**: YES
  - Message: `feat(preview): add XLSX worksheet preview via ClosedXML`
  - Files: `src/MantisZip.UI/MainWindow.Preview.Office.cs`

---

- [ ] 4. DOCX Mammoth 内容预览

  **What to do**:
  - 新增方法 `ShowDocxPreview(string filePath, ArchiveItem item, CancellationToken ct)`:
    1. 检查 `item.Size > MaxPreviewFileSize`：
       - 如果超出 → 仅显示元数据（保留现有行为），内容区空置
       - 如果未超出 → 加载内容
    2. 调用 `ExtractPreviewFileAsync(item, "preview.docx", ct)` 提取到 temp
    3. 用 Mammoth 转换：
       ```csharp
       var converter = new DocumentConverter();
       var result = converter.ConvertToHtml(tempFile);
       ```
    4. 检查 HTML 大小 > 5MB → 截断或回退到元数据
    5. 展示：
       - `HideAllPreviewControls()`
       - `EnsureWebView2InitializedAsync()`
       - `PreviewWebView2.CoreWebView2.NavigateToString(result.Value)`
       - `PreviewWebView2.Visibility = Visibility.Visible`
     6. 异常捕获 → `ShowUnsupportedPreview` 并记录日志

  > **备选优化**: 对于超大 docx，可考虑"虚拟首页 docx"方案——只取 `word/document.xml` 中第一个分页符前的内容，构建最小 docx 喂给 Mammoth。
  > 详见下方 [备选方案](#备选方案docx-首页预览优化不完整提取文档内容) 章节。当前不实现。

  **Must NOT do**:
  - 不添加 Mammoth 图片保存回调（不处理嵌入图片）
  - 不修改 WebView2 安全设置（保持网络拦截）
  - 不为 Mammoth HTML 添加自定义 CSS（使用默认样式）

  **References**:
  - `UI/MainWindow.Preview.Metadata.cs:ShowPdfPreview` — WebView2 渲染模式（NavigateToString）
  - `UI/MainWindow.Preview.cs:MaxPreviewFileSize` — 15MB 常量

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 5
  - **Blocked By**: Task 1 (NuGet)

  **Acceptance Criteria**:
  - [ ] 选择 <15MB 的 .docx → WebView2 显示 Mammoth 生成的 HTML
  - [ ] WebView2 不发起外部网络请求（保持拦截）
  - [ ] 选择 >15MB 的 .docx → 仅显示元数据，内容区空置
  - [ ] 损坏的 .docx → 不崩溃，显示回退

  **QA Scenarios**:
  ```
  Scenario: DOCX 富文本预览 — 正常文件
    Tool: Bash
    Preconditions: 准备一个包含标题、段落、列表、表格的 .docx（<15MB）
    Steps:
      1. 打开该文件
    Expected Result: WebView2 显示格式化后的 HTML 内容
    Evidence: .sisyphus/evidence/task-4-docx-normal.png（WebView2 截图）

  Scenario: DOCX 超大文件
    Tool: Bash
    Preconditions: 准备一个 >15MB 的 .docx
    Steps:
      1. 打开该文件
    Expected Result: 内容区空置，信息面板依然显示元数据
    Evidence: .sisyphus/evidence/task-4-docx-large.txt
  ```

  **Commit**: YES
  - Message: `feat(preview): add DOCX content preview via Mammoth`
  - Files: `src/MantisZip.UI/MainWindow.Preview.Office.cs`

---

- [ ] 5. ShowPreviewAsync 调度改造

  **What to do**:
  - 在 `MainWindow.Preview.cs` 的 `ShowPreviewAsync` 中找到 `OfficeExtensions.Contains(ext)` 分支
  - 改造为按扩展名分发：
    ```csharp
    else if (OfficeExtensions.Contains(ext))
    {
        tempFile = await ExtractPreviewFileAsync(item, "preview" + Path.GetExtension(item.Name), ct);
        try
        {
            // DOCX: 传给 Mammoth 做 HTML 转换（需要完整文件）
            // XLSX/PPTX: 用 ZipFile.OpenRead 在 tempFile 上做 ZIP 内读取（不经过 Mammoth）
            if (ext == ".docx")
            {
                ShowDocxPreview(tempFile, item, ct);
            }
            else if (ext == ".xlsx")
            {
                ShowXlsxPreview(tempFile, item);
            }
            else if (ext == ".pptx")
            {
                ShowPptxPreview(tempFile, item);
            }
        }
        finally
        {
            // tempFile 在现有清理逻辑中处理
        }
    }
    ```
  - 三种格式都调用 `ExtractPreviewFileAsync` 获取临时文件（`tempFile` 变量）
  - XLSX/PPTX 在 `tempFile` 上用 `ZipFile.OpenRead` 做 ZIP 内读取——不需要像 Mammoth 那样流式处理
  - DOCX 把 `tempFile` 传给 Mammoth 做 HTML 转换
  - 验证所有三种路径的 `HideAllPreviewControls` + `ShowPreviewPanel` 行为正确

  **Must NOT do**:
  - 不修改 `OfficeExtensions` 集合（保留现有扩展名列表）
  - 不删除 `ShowOfficePreview` 方法（保留作为 fallback）
  - 不破坏其他格式的调度路径（PDF/Image/Text/Video 等保持不动）
  - 不假设 `ShowPreviewAsync` 作用域中存在 `filePath` 变量——从 `ExtractPreviewFileAsync` 返回的 `tempFile` 获取路径

  **References**:
  - `UI/MainWindow.Preview.cs:400-420` — ShowPreviewAsync 中的 Office 调度分支

  **Recommended Agent Profile**:
  - **Category**: `deep`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 6
  - **Blocked By**: Task 2, Task 3, Task 4

  **Acceptance Criteria**:
  - [ ] .docx → 进入 `ShowDocxPreview` 路径
  - [ ] .xlsx → 进入 `ShowXlsxPreview` 路径（提取 temp 后 ZIP 内读取）
  - [ ] .pptx → 进入 `ShowPptxPreview` 路径（提取 temp 后 ZIP 内读取）
  - [ ] 所有三种格式的元数据信息面板依然正确显示
  - [ ] 文件切换来回时清理正确

  **QA Scenarios**:
  ```
  Scenario: 调度分支验证 — .docx
    Tool: Bash
    Steps:
      1. 打开一个 .docx 文件
    Expected Result: ShowDocxPreview 被调用，WebView2 显示 HTML（或 >15MB 时回退到元数据）
    Evidence: .sisyphus/evidence/task-5-dispatch-docx.txt

  Scenario: 调度分支验证 — .xlsx
    Tool: Bash
    Steps:
      1. 打开一个 .xlsx 文件
    Expected Result: ShowXlsxPreview 被调用，DataGrid 显示表格
    Evidence: .sisyphus/evidence/task-5-dispatch-xlsx.txt

  Scenario: 调度分支验证 — .pptx
    Tool: Bash
    Steps:
      1. 打开一个 .pptx 文件
    Expected Result: ShowPptxPreview 被调用，PreviewTextBox 显示幻灯片文本
    Evidence: .sisyphus/evidence/task-5-dispatch-pptx.txt

  Scenario: 文件切换清理
    Tool: Bash
    Steps:
      1. 打开 .docx → 选择 .xlsx → 选择 .pptx → 选择 .txt
    Expected Result: 每次切换正确清理前一个预览控件（WebView2/DataGrid/TextBox）
    Evidence: .sisyphus/evidence/task-5-cleanup.txt
  ```

  **Commit**: YES
  - Message: `refactor(preview): dispatch Office previews to dedicated methods`
  - Files: `src/MantisZip.UI/MainWindow.Preview.cs`

---

- [ ] 6. 本地化字符串 + 构建验证

  **What to do**:
  - 在 `strings.zh.json` 和 `strings.en.json` 添加以下键：
    - `Preview_DocxRenderFailed` — "无法渲染 Word 文档内容" / "Failed to render Word content"
    - `Preview_XlsxRenderFailed` — "无法加载 Excel 工作表" / "Failed to load Excel sheet"
    - `Preview_PptxRenderFailed` — "无法解析演示文稿" / "Failed to parse presentation"
    - `Preview_PptxSlideEmpty` — "（此幻灯片无文本）" / "(No text on this slide)"
    - `Preview_PptxEmpty` — "此演示文稿为空" / "This presentation is empty"
    - `Preview_XlsxEmpty` — "此工作表中没有数据" / "No data in this worksheet"
    - `Preview_XlsxProtected` — "工作表受密码保护" / "Worksheet is password protected"
  - 确认 `L.T()` 调用与新增键匹配
  - 运行完整构建确认

  **Must NOT do**:
  - 不修改现有的翻译键
  - 不删除任何翻译

  **References**:
- `src/MantisZip.UI/Resources/strings.zh.json`
- `src/MantisZip.UI/Resources/strings.en.json`

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2
  - **Blocks**: None
  - **Blocked By**: Task 5

  **Acceptance Criteria**:
  - [ ] `dotnet build src\MantisZip.UI\MantisZip.UI.csproj` 通过
  - [ ] 新增翻译键在 zh 和 en 中均存在

  **QA Scenarios**:
  ```
  Scenario: 构建验证
    Tool: Bash
    Steps:
      1. dotnet build src\MantisZip.UI\MantisZip.UI.csproj
    Expected Result: Build succeeded
    Evidence: .sisyphus/evidence/task-6-build.txt
  ```

  **Commit**: NO (grupped with task 5)
  - Message: `feat(preview): add localization strings for Office preview`
  - Files: `src/MantisZip.UI/Localization/strings.zh.json`, `src/MantisZip.UI/Localization/strings.en.json`

---

- [ ] 7. 集成回归测试

  **What to do**:
  - 测试以下场景覆盖完整回归：
    1. 正常 .docx 预览（<15MB）
    2. 正常 .xlsx 预览
    3. 正常 .pptx 预览
    4. 大 .docx（>15MB）→ 仅元数据
    5. 空 .xlsx → 回退文本
    6. 空 .pptx → 回退文本
    7. 受保护的 .xlsx → 回退文本
    8. 文件切换：.docx ↔ .xlsx ↔ .pptx ↔ .txt ↔ .jpg（验证控件清理）
    9. 现有格式回归：PE、PDF、图片、文本、SQLite、音频、Torrent 等工作正常
    10. 压缩包内 Office 文件预览（zip/7z 内的 docx/xlsx/pptx）

  **Must NOT do**:
  - 不修改任何源文件（纯测试任务）

  **References**:
  - existing test plan in `tests/MantisZip.Tests/`

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave FINAL
  - **Blocks**: None
  - **Blocked By**: Task 5, Task 6

  **Acceptance Criteria**:
  - [ ] 格式切换不产生崩溃或异常
  - [ ] 现有所有格式预览不变

  **QA Scenarios**:
  ```
  Scenario: 回归测试 — 格式切换
    Tool: Bash
    Steps:
      1. 依次打开 .docx → .xlsx → .pptx → .txt → .jpg → .pdf → .exe → .torrent → 每步观察预览区域
    Expected Result: 所有步骤无崩溃，预览区域正确切换
    Evidence: .sisyphus/evidence/task-7-regression.txt
  ```

  **Commit**: YES
  - Message: `test: verify Office content preview with regression suite`
  - Files: (all changed files)

---

## 备选方案：DOCX 首页预览优化（不完整提取文档内容）

> **定位**: 当前方案将完整 docx 喂给 Mammoth 做全文 HTML 转换。
> 此备选方案为可选的性能优化，当前不实现，留作后续改进参考。

### 问题

对于超大 docx（几十 MB 甚至上百 MB），即使从容器中提取完整 docx 文件到 temp 是必须的（无法跳过），
Mammoth 全文转换本身也可能耗时较长。用户可能只想看第一页。

### 优化思路：虚拟首页 docx

DOCX 内部是一个 ZIP 包，`word/document.xml` 存储全部正文内容（也是 Deflate 压缩的）。
虽然必须完整解压这个 XML 才能读取它，但我们可以：

1. 完整提取 docx 到 temp ✅（必须）
2. 把 `word/document.xml` 解压到内存 ✅（必须）
3. **找到第一个显式分页符 `<w:br w:type="page"/>` 之前的内容**
4. 用这些内容构建一个**仅含第一页的最小 docx ZIP**
5. 把 mini docx 喂给 Mammoth 转换

```csharp
// 伪代码示意
using var docxArchive = ZipFile.OpenRead(tempFile);
var docEntry = docxArchive.GetEntry("word/document.xml");
using var docStream = docEntry.Open();
var doc = XDocument.Load(docStream);

XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

// 找到第一个显式分页符
var firstPageBreak = doc.Descendants(w + "br")
    .FirstOrDefault(b => (string)b.Attribute(w + "type") == "page");

if (firstPageBreak != null)
{
    // 提取分页符前的所有节点
    var firstPageContent = /* 分页符之前的 body 子节点 */;
    var miniDocx = BuildMiniDocx(firstPageContent);
    var result = new DocumentConverter().ConvertToHtml(miniDocx);
    // → WebView2 显示（和完整方案一致）
}
else
{
    // 无显式分页符 → 回退到全文 Mammoth
    var result = converter.ConvertToHtml(tempFile);
}
```

### 优点
- **显式分页符的 docx**：首页预览极快，Mammoth 只需处理几十 KB
- 对无分页符的文档无缝回退到全文方案

### 缺点与风险
| 问题 | 说明 |
|:----:|------|
| ❌ **隐式分页检测不到** | Word 自动换页（文字自然流到第二页）在 XML 中没有标记——**这是最大限制** |
| ❌ **虚拟 docx 完整性** | 需要正确处理 `[Content_Types].xml`、`_rels/`、`word/_rels/document.xml.rels` 等元文件，构建一个 Mammoth 能识别的合法 docx |
| ❌ **遗漏非内联内容** | 页眉/页脚/脚注/尾注/文本框的内容可能在 XML 中的不同位置，虚拟 docx 可能遗漏 |
| ❌ **复杂度** | 构建虚拟 ZIP、维护 XML 命名空间、处理各种边缘情况，预计 ~3-4h 额外工作 |

### 适用场景判断

```
推荐使用全文方案（当前计划）:
  ├── 大多数 docx ≤ 15MB，全文转换仅需 1-2 秒
  └── 简单、可靠、不出错

推荐使用首页优化（备选方案）:
  ├── 超大 docx（50MB+）频繁预览
  ├── 大多数文档使用显式分页符（报告/论文）
  └── 愿意接受边缘情况的不完美回退

底线: 当前计划（全文 Mammoth）对绝大多数场景已足够。
首页优化可做后续改进，但非必须。
```

---

## Commit Strategy

| Task | Message | Scope |
|------|---------|-------|
| 1 | `dep(ui): add Mammoth + ClosedXML NuGet packages for Office content preview` | .csproj |
| 2 | `feat(preview): add PPTX slide text preview via manual XML parsing` | Office.cs |
| 3 | `feat(preview): add XLSX worksheet preview via ClosedXML` | Office.cs |
| 4 | `feat(preview): add DOCX content preview via Mammoth` | Office.cs |
| 5+6 | `refactor(preview): dispatch Office previews to dedicated methods` + localization | Preview.cs, strings.*.json |
| 7 | `test: verify Office content preview with regression suite` | all |

---

## Success Criteria

### Verification Commands
```bash
dotnet build src\MantisZip.UI\MantisZip.UI.csproj
# Expected: Build succeeded
```

### Final Checklist
- [ ] .docx 内容区显示 Mammoth HTML（WebView2）
- [ ] .xlsx 内容区显示 ClosedXML 表格（DataGrid）
- [ ] .pptx 内容区显示幻灯片文本列表（TextBox）
- [ ] 大 .docx（>15MB）内容区空置，仅元数据
- [ ] 所有三种格式信息面板元数据正确
- [ ] XLSX/PPTX 在 temp 文件上用 `ZipFile.OpenRead` 做 ZIP 内读取（无需额外流处理）
- [ ] 文件切换清理正确（无控件残留）
- [ ] 现有非 Office 格式预览不受影响
- [ ] `dotnet build` 通过
