# COM 右键菜单（Shell Context Menu）

> 将当前基于注册表静态动词的右键菜单替换为 COM `IContextMenu` 实现，支持动态菜单文本、子菜单、自定义图标、预设集成。
> **状态**: 🚧 进行中 | **阶段**: [████████░░] (8/9)
> **前置依赖**: ✅ SharpCompress 引擎迁移完成（v0.3.4，`ListEntriesAsync` 用于动态显示压缩包文件名）
>
> ⚠️ **重要架构变更**: 本计划最初假设在 `MantisZip.UI` (WinExe) 项目内创建 COM 组件。但 .NET 9 的 COM 托管使用 `comhost.dll` 机制（非传统 regasm），且 WPF WinExe 不适合加载到 Explorer 进程。
> **修订方案**: 新建独立的类库项目 `MantisZip.ShellExt`（`net9.0-windows` 类库），不引用 WPF/MantisZip.UI 程序集，保持轻量。详情见 Task 7。

---

## Context

### 现状

当前 `ShellIntegration.cs`（~580 行）通过写入 `HKCU\Software\Classes` 注册表实现右键菜单：

| 特性 | 当前限制 |
|------|---------|
| 菜单文本 | **静态** — 编译时就写死了，如"用 MantisZip 解压到此处" |
| 文件名嵌入 | **做不到** — 无法显示"添加到 报告.zip"这种动态文本 |
| 预设支持 | **做不到** — 预设数量动态变化，无法静态注册 |
| 子菜单 | 层叠模式有 bug（`CommandFlags=8` 问题），`ExtendedSubCommandsKey` 不稳定 |
| 图标 | 只能从 `shell32.dll` 取索引，无法用自定义图标 |
| 排序 | 用编号前缀（`01_`、`02_`）勉强控制，不优雅 |
| 卸载 | 需要遍历删除注册表键，容易残留 |

### COM 菜单的优势

| 特性 | COM 方案 |
|------|---------|
| 菜单文本 | **运行时生成** — 可读取文件名，可调用 API |
| 子菜单 | **原生支持** — `IContextMenu` 原生支持 HMENU 子菜单 |
| 图标 | **HICON** — 可加载任意 .ico/.png 资源 |
| 动态条目 | 每次右键时调用 `QueryContextMenu`，天然动态 |
| 注册 | **仅一次** — 注册 `{GUID}` 到 `shellex`，后续全由代码控制 |
| 预设集成 | 可在菜单中枚举当前预设列表 |

### 依赖关系

```
文件过滤 (file-filter-feature.md)      压缩预设 (compress-preset.md)
        │                                       │
        └──────────┬────────────────────────────┘
                   ▼
          COM 右键菜单 ← 提供动态载体供预设展示
                   │
                   ▼
          压缩预设 Phase 2
        (CLI + COM 菜单集成)
```

三个计划互不阻塞，但 COM 菜单完成后，压缩预设 Phase 2 可以接入。

---

## Work Objectives

### Core Objective

用 COM `IContextMenu` + `IShellExtInit` 实现取代 `ShellIntegration.cs` 的静态注册表方案，支持动态菜单文本、子菜单、自定义图标，并为压缩预设提供动态展示载体。

### Concrete Deliverables

- **新项目**: 创建 `MantisZip.ShellExt` 独立类库 (`net9.0-windows`)，不引用 WPF/UI 程序集
- COM 组件：实现 `IShellExtInit`、`IContextMenu` 接口（在 ShellExt 项目中）
- 动态菜单：嵌入文件名（如"添加到 报告.zip"）
- 子菜单支持：替代现有层叠模式，更稳定
- 图标支持：为菜单项提供自定义图标
- 设置集成：保留现有各菜单项的开关逻辑（COM 组件通过注册表读取设置）
- 注册/反注册：32/64 位 COM host DLL 注册 + 清理旧注册表
- Install/Uninstall CLI：更新 `--install-shell` / `--uninstall-shell`（COM 注册独立于 `--install-assoc` 文件关联）

### Must Have

- [ ] `MantisZip.ShellExt` 独立类库项目（`net9.0-windows`，`<EnableComHosting>true</EnableComHosting>`）
- [ ] COM 组件注册到 `*\shellex\ContextMenuHandlers\{GUID}` 和 `Directory\shellex\ContextMenuHandlers\{GUID}`
- [ ] 动态菜单文本（至少嵌入文件名）
- [ ] 保留现有 8 个菜单项和它们的 toggle 开关
- [ ] 保留层叠/独立动词两种模式
- [ ] 菜单项可显示图标
- [ ] `--install-shell` / `--uninstall-shell` 正常工作（仅 COM 注册，不涉及文件关联）
- [ ] 卸载时清除旧的静态注册表条目

### Must NOT Have (Phase 1)
- 不做文件关联（`OpenWithProgids` 功能保留在 ShellIntegration.cs 中）
- 不做拖拽相关的 COM 实现（那是 `VirtualFileDataObject` 计划）
- 不改变现有的 `AppSettings` 菜单开关接口

### Phase 2 Option: 砍掉静态回退 + 动词模式

所有 COM 测试走通后可选的清理项：

**前提**: COM 组件在所有目标环境下运行稳定，无 fallback 需求。

**改动**:
- ShellIntegration.cs: 删 `InstallCascade()` / `InstallVerbs()`、所有编号 verb 常量、分隔线 verb、
  Uninstall 中对应清理路径、关联的 SetRegistryValue 调用 → **−~280 行**
- `Install()` 简化：`if (!InstallCom()) return;` 不再有分支
- `Uninstall()` 简化：只调用 `UninstallCom()` + SHChangeNotify
- ContextMenuHandler.cs: 删 `QueryContextMenu` 中 `_cascadeMode` 条件判断的 `else` 分支 → **−~30 行**
- AppSettings: 删 `EnableCascadingMenu` 字段及设置 UI
- 归档 `com-migration-mapping.md`（不再需要）
- 仅保留层叠模式，不再区分 cascade/verb

**代价**: 无 comhost.dll 时完全失去右键菜单（安装包必须包含 ShellExt）

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: NO
- **Automated tests**: NO
- **Agent-Executed QA**: ALWAYS

### QA Policy
- **COM 组件加载测试**：注册后，用 PowerShell 或测试代码验证 CLSID 可创建
- **菜单显示测试**：用 `IExplorerPaneVisibility` 或手动验证
- **点击执行测试**：验证 InvokeCommand 正确触发 CLI
- **注册/反注册测试**：安装后注册表存在，卸载后注册表清理

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Research + setup):
├── Task 1: COM context menu research + .NET pattern setup
└── Task 2: Current ShellIntegration audit + migration mapping

Wave 2 (Core COM implementation):
├── Task 3: IShellExtInit — get file paths from shell
├── Task 4: IContextMenu.QueryContextMenu — build dynamic menu
├── Task 5: IContextMenu.InvokeCommand — execute actions
└── Task 6: IContextMenu.GetCommandString — help text

Wave 3 (Integration + polish):
├── Task 7: Registration + unregistration logic
├── Task 8: Integration with AppSettings menu toggles
├── Task 9: Icon support for menu items
└── Task F1-F4: Final verification
```

### Critical Path
Task 1 → Task 3 → Task 4 → Task 5 → Task 7 → Task 8 → Task 9 → F1-F4

---

## TODOs

- [x] 1. COM 右键菜单方案研究 + 环境搭建

  **What to do**:
  - 研究以下内容：
    - `IShellExtInit` 接口规范（`Initialize(LPCITEMIDLIST, IDataObject, HKEY)`）
    - `IContextMenu` 接口规范（`QueryContextMenu`、`InvokeCommand`、`GetCommandString`）
    - .NET 中实现 COM 接口的要求：`[ComVisible(true)]`、`[Guid]`、`[ClassInterface]`、`[ProgId]`
    - **.NET 9 COM 托管方案**（⚠️ 关键差异）：
      - 传统 regasm 在 .NET 5+ 已弃用，改为 `<EnableComHosting>true</EnableComHosting>` 生成 `.comhost.dll`
      - 注册方式：`regsvr32 MantisZip.ShellExt.comhost.dll`（而非 regasm）
      - 宿主要求：独立类库项目，不能是 WinExe
      - 架构：32/64 位需分别构建（`dotnet build -r win-x64` / `win-x86`）
      - 引用：该程序集不能引用 WPF 程序集（Explorer 进程隔离）
      - 独立项目必须将 Core 项目作为依赖，从注册表而非 AppSettings 读取配置
    - SNK 强命名程序集要求（.NET COM 仍然需要）
    - Windows 10/11 的 shell 扩展调试方法（`%LOCALAPPDATA%\Microsoft\Windows\Shell\` 日志）
  - 搭建测试项目（`MantisZip.ShellExt` 原型）验证 COM 接口可被 Explorer 加载
  - 输出研究报告（记录关键发现和风险点）

  **Must NOT do**:
  - 不要直接修改 ShellIntegration.cs
  - 不要在未验证的情况下注册到生产环境

  **Recommended Agent Profile**:
  - Category: `deep`（研究密集型，需要探索 .NET COM 互操作最佳实践）
  - Skills: 建议用 `librarian` 查找 .NET 5+ COM hosting + `EnableComHosting` 实现示例

  **Parallelization**:
  - Can Run In Parallel: YES
  - Wave: Wave 1
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] 确定 .NET 实现 COM shell extension 的最佳方案
  - [ ] 验证原型可被 Explorer 加载
  - [ ] 输出注册/调试/部署方案

  **References**:
  - MSDN: `IContextMenu` / `IShellExtInit` interface docs
  - GitHub .NET 5+ COM shell extension 示例（搜索 `EnableComHosting`）
  - UI/ShellIntegration.cs — 当前实现，了解要迁移的内容
  - src/MantisZip.ShellExt/ — 新建的目标项目位置

  **Commit**: NO（研究性任务，无代码产出）

---

- [x] 2. 当前 ShellIntegration 审计 + 迁移映射

  **What to do**:
  - 对 `ShellIntegration.cs` 进行逐功能审计：
    - 列出所有注册表写入点
    - 列出所有菜单项及其 toggle 设置
    - 列出所有目标类型（`*`、`Directory`、`Directory\Background`）
    - 列出层叠模式和独立动词模式的区别
    - 列出 `AppliesTo` 过滤规则
    - 列出图标设置逻辑
    - 列出 `--install-shell` / `--uninstall-shell` 调用点
  - 生成迁移映射表：每个注册表条目 → 对应的 COM 菜单处理方式
  - 标记哪些逻辑保留在 `ShellIntegration.cs`（如文件关联）、哪些迁移到 COM 组件

  **Must NOT do**:
  - 不要开始写任何代码

  **References**:
  - UI/ShellIntegration.cs — 审计对象

  **Parallelization**:
  - Can Run In Parallel: YES
  - Wave: Wave 1
  - Blocked By: None

  **Acceptance Criteria**:
  - [ ] 完整的迁移映射表
  - [ ] 明确文件关联 vs 右键菜单的界限

  **Commit**: NO（分析性任务）

---

- [x] 3. `IShellExtInit` 实现

  **What to do**:
  - 新建 `src/MantisZip.ShellExt/ContextMenuHandler.cs`（独立类库项目）
  - 实现 `IShellExtInit`：
    ```csharp
    [ComVisible(true)]
    [Guid("XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("MantisZip.ContextMenu")]
    public class ContextMenuHandler : IShellExtInit, IContextMenu
    {
        private string[]? _selectedFiles;
        private string? _targetFolder; // Directory\Background 模式下的目录

        public void Initialize(IntPtr pidlFolder, IntPtr pDataObj, IntPtr hKeyProgId)
        {
            // 从 IDataObject 提取选中的文件路径
            // 支持 *（文件）和 Directory（文件夹）两种调用上下文
        }
    }
    ```
  - 定义 COM GUID（用 `guidgen.exe` 或在线生成器生成新的 GUID）
  - 从 `IDataObject` 提取文件路径：
    - 获取 `FORMATETC` 为 `CF_HDROP`
    - 解析 `STGMEDIUM` 中的文件列表
  - 处理两种调用上下文：
    - `*`（文件）：`_selectedFiles` 填充选中的文件路径
    - `Directory`（文件夹）：`_selectedFiles` 填充选中的文件夹路径
    - `Directory\Background`（背景右键）：`_targetFolder` 填充当前目录

  **Must NOT do**:
  - 不要引用任何 WPF / MantisZip.UI 程序集（Explorer 进程隔离）
  - 不要依赖 WinForms（避免引入 System.Windows.Forms）
  - 不要引用 AppSettings（COM 组件通过注册表读取设置，见 Task 8）

  **References**:
  - [MSDN: IShellExtInit](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellextinit)
  - GitHub .NET 5+ COM host shell extension 示例（搜索 `EnableComHosting` + `IContextMenu`）

  **Parallelization**:
  - NO
  - Wave: Wave 2
  - Blocked By: Task 1

  **Acceptance Criteria**:
  - [ ] Initialize 正确提取文件路径
  - [ ] 支持文件、文件夹、背景三种调用上下文
  - [ ] 异常时安全退出（不崩溃 Explorer）
  - [ ] COM 组件在 Explorer 进程内加载时不拉起 WPF 程序集

  **Commit**: YES

---

- [x] 4. `IContextMenu.QueryContextMenu` — 构建动态菜单

  **What to do**:
  - 实现 `QueryContextMenu`：
    - 接收 `HMENU` 句柄，用 `Win32Native.InsertMenu` / `InsertMenuItem` 添加菜单项
    - 菜单结构（与现有 ShellIntegration 保持一致的 8 个菜单项）：
      ```
      ──────────── 分隔符 ────────────
      用 MantisZip 打开
      用 MantisZip 解压到此处
      智能解压到此处
      用 MantisZip 解压到（文件名）
      用 MantisZip 解压到……
      ──────────── 分隔符 ────────────
      压缩到独立的（文件名）
      压缩到（父目录名）
      用 MantisZip 压缩
      ──────────── 分隔符 ────────────
      ```
    - **动态文本**：将"（文件名）"替换为实际文件名
      - 单文件选中：`解压到 报告` / `压缩到 报告.zip`
      - 多文件选中：`解压到 报告 等 5 个文件` / `压缩到 文档 等 3 个文件`
    - **层叠模式**（`EnableCascadingMenu`）：用 `InsertMenu` 的 `MF_POPUP` 创建子菜单
    - **每个菜单项对应独立的命令 ID**，用于 `InvokeCommand` 区分
    - 读取注册表中的设置（见 Task 8 的注册表同步方案）判断各菜单项是否显示
    - 菜单项顺序严格保持（用 `idCmdFirst` + offset 控制）
    - 应用 `AppliesTo` 过滤（仅压缩包文件显示"打开/解压"菜单）

  **Must NOT do**:
  - 不要硬编码字符串（从 `L.cs` 获取，带动态参数）
  - 不要使用 `CommandFlags=8`（旧 bug 的根源）

  **References**:
  - UI/ShellIntegration.cs:200-260 — 当前菜单项注册逻辑
  - UI/AppSettings.cs — EnableCascadingMenu, ShowMenuIcons 等
  - [MSDN: IContextMenu.QueryContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu)

  **Parallelization**:
  - NO
  - Wave: Wave 2
  - Blocked By: Task 3

  **Acceptance Criteria**:
  - [ ] 8 个菜单项全部显示
  - [ ] 动态文本正确显示文件名
  - [ ] 层叠模式和独立动词模式都正常工作
  - [ ] 菜单 toggle 开关生效
  - [ ] 分隔符位置正确
  - [ ] AppliesTo 过滤生效

  **Commit**: YES (groups with 5, 6)

---

- [x] 5. `IContextMenu.InvokeCommand` — 执行菜单操作

  **What to do**:
  - 实现 `InvokeCommand`：
    - 根据命令 ID 映射到对应的 CLI 命令
    - 映射表：
      | 命令 ID | 用户点击的菜单 | CLI 调用 |
      |---------|--------------|---------|
      | 0 | 用 MantisZip 打开 | `--open "{path}"` |
      | 1 | 解压到此处 | `--extract-here "{path}"` |
      | 2 | 智能解压 | `--extract-smart "{path}"` |
      | 3 | 解压到（文件名） | `--extract-to-name "{path}"` |
      | 4 | 解压到… | `--extract "{path}"` |
      | 5 | 压缩到独立的 | `--compress-separate "{path1}" "{path2}"` |
      | 6 | 压缩到（父目录名） | `--compress-combined "{path1}" "{path2}"` |
      | 7 | 用 MantisZip 压缩 | `--compress "{path1}" "{path2}"` |
    - 支持 `Directory\Background` 模式：
      - 没有选中文件时，不在背景模式显示"打开/解压"菜单
      - 背景模式只显示压缩菜单，目标路径为当前目录
    - 使用 `Process.Start` 启动自身 exe 并传递 CLI 参数（与现有行为一致）
    - 异常处理：任何异常不抛到 Explorer

  **References**:
  - UI/App.xaml.cs — CLI 参数解析（OnStartup switch-case）
  - UI/App.Cli.cs — CLI 命令处理器（HandleCompress, HandleExtract 等）
  - [MSDN: IContextMenu.InvokeCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)

  **Parallelization**:
  - NO
  - Wave: Wave 2
  - Blocked By: Task 4

  **Acceptance Criteria**:
  - [ ] 每个菜单项点击后触发正确的 CLI 命令
  - [ ] 多文件选中时传递所有路径
  - [ ] 异常不会崩溃 Explorer

  **Commit**: YES (groups with 4, 6)

---

- [x] 6. `IContextMenu.GetCommandString` — 帮助文本

  **What to do**:
  - 实现 `GetCommandString`：
    - 返回每个命令的 Unicode 帮助文本（显示在状态栏）
    - 支持 `GCS_HELPTEXT` 和 `GCS_VERB`（动词名称）
    - 动词名称用于脚本调用：`explorer.exe` 可以通过动词名调用

  **Parallelization**:
  - NO
  - Wave: Wave 2
  - Blocked By: Task 4

  **Acceptance Criteria**:
  - [ ] 每个命令返回正确的帮助文本
  - [ ] 动词名称可用

  **Commit**: YES (groups with 4, 5)

---

- [x] 7. 注册/反注册逻辑（.NET 9 comhost 方式）

  **背景**: .NET 5+ 弃用了 regasm。.NET 9 的 COM 注册使用 `comhost.dll` 机制：
  - 项目设置 `<EnableComHosting>true</EnableComHosting>`，构建时生成 `MantisZip.ShellExt.comhost.dll`
  - 注册用 `regsvr32 MantisZip.ShellExt.comhost.dll`（Win32 标准 COM 注册）
  - 32/64 位分别构建：`dotnet build -r win-x64` 和 `dotnet build -r win-x86`

  **What to do**:
  - 在 `MantisZip.ShellExt` 项目中确保：
    - 项目文件设置 `<EnableComHosting>true</EnableComHosting>`
    - 项目文件设置 `<EnableRegFreeCom>false</EnableRegFreeCom>`（不需要 regfree）
    - COM 类使用 `[ComVisible(true)]`、`[Guid("...")]`、`[ProgId("MantisZip.ContextMenu")]`
    - 用 SNK 签名校验强命名
  - 在 `MantisZip.UI`（现有项目）中实现注册/反注册方法：
    - **推荐: 在 `ShellIntegration.cs` 中新增 `InstallCom()` / `UninstallCom()` 静态方法**
    - 注册 `InstallCom()`：
      - 调用 `regsvr32 MantisZip.ShellExt.comhost.dll`（通过 `Process.Start` 启动）
      - 或手动写入注册表：
        - `HKCU\Software\Classes\CLSID\{GUID}\InprocServer32` → 指向 `MantisZip.ShellExt.comhost.dll`
        - `HKCU\Software\Classes\CLSID\{GUID}\InprocServer32\ThreadingModel` → `"Apartment"`
        - `*\shellex\ContextMenuHandlers\MantisZip` → `{GUID}`
        - `Directory\shellex\ContextMenuHandlers\MantisZip` → `{GUID}`
        - `Directory\Background\shellex\ContextMenuHandlers\MantisZip` → `{GUID}`
      - 32/64 位：comhost 自带位数判断，只需正确分发对应架构的 DLL
      - 调用 `SHChangeNotify` 刷新 Shell
    - 反注册 `UninstallCom()`：
      - `regsvr32 /u MantisZip.ShellExt.comhost.dll`
      - 或手动删除上述注册表键
      - 调用 `SHChangeNotify` 刷新 Shell
    - 保留 `ShellIntegration.Install()` 方法但修改逻辑：
      - 如果 COM 模式启用（新的 `UseComContextMenu` 开关？或自动判断），调用 `InstallCom()` 而非静态注册
      - 否则回退到现有静态注册方式（向后兼容）
  - 保留现有文件关联功能（不动）
  - **`--install-shell` 负责 COM 注册**，`--install-assoc` 负责文件关联，两者保持独立（当前代码已分离）
  - 在卸载 COM 菜单时自动清理旧的静态注册表条目（调用 `ShellIntegration.Uninstall()` 中已有的清理逻辑）

  **Must NOT do**:
  - 不要删除文件关联功能（`OpenWithProgids` / `--install-assoc` 保留不动）
  - 不要在 COM 组件中引用任何 WPF / MantisZip.UI 程序集

  **References**:
  - UI/ShellIntegration.cs — 现有注册逻辑（`Install()` / `Uninstall()`）
  - UI/App.xaml.cs — `--install-shell` / `--uninstall-shell` CLI 参数解析（switch-case）
  - UI/App.Cli.cs — 不涉及（注册逻辑在 ShellIntegration 中）
  - [MSDN: .NET COM Hosting](https://learn.microsoft.com/en-us/dotnet/core/native-interop/expose-components-to-com)
  - 示例: `dotnet new classlib` + `<EnableComHosting>true</EnableComHosting>`

  **Parallelization**:
  - NO
  - Wave: Wave 3
  - Blocked By: Task 3（COM 组件需要先存在）

  **Acceptance Criteria**:
  - [ ] Register 后右键菜单正常出现
  - [ ] Unregister 后右键菜单消失
  - [ ] 32 位和 64 位都工作
  - [ ] `--install-shell` 正常完成（不涉及文件关联）
  - [ ] `--uninstall-shell` 清理旧的静态注册表条目
  - [ ] COM host DLL 被正确注册/反注册

  **Commit**: YES
  - Message: `feat(shell): add COM context menu registration for .NET 9`

---

- [x] 8. AppSettings 菜单开关集成

  **What to do**:
  - 在 COM 菜单处理程序（`ContextMenuHandler.cs`，位于 `MantisZip.ShellExt` 项目）中读取设置：
    - 在 `QueryContextMenu` 中判断各 toggle
    - ⚠️ **COM 组件在 Explorer 进程内运行，不能直接访问 WPF 的 `AppSettings`（JSON 文件）**
    - **推荐方案：注册表同步**——MantisZip 保存设置时，同时写入 `HKCU\Software\MantisZip\ContextMenu`，COM 组件从注册表读取
    - 备选：IPC（named pipe）与 MantisZip 进程通信（更复杂，不推荐）
  - 设置项（从注册表读取）：
    - `EnableCascadingMenu` (DWORD)
    - `ShowMenuIcons` (DWORD)
    - `EnableOpenMenu` ~ `EnableCompressMenu` 等 8 个 toggle (DWORD)
    - `EnableQuickCompress` (DWORD) — 注意：当前 QuickCompress 不在右键菜单内，但设置已存在
  - 修改 `AppSettings.cs` 的 `Save()` 方法：保存 JSON 后同步写入 `HKCU\Software\MantisZip\ContextMenu` 注册表键
    - 写入时机：每次 `Save()` 调用时，一次性写入所有相关 toggle
    - 读取时机：COM 组件在每次 `QueryContextMenu` 时从注册表读取
  - 如果设置变更后需要即时生效，调用 `SHChangeNotify` 刷新 Explorer

  **Must NOT do**:
  - 不要在 COM 组件中引用任何 WPF 或 MantisZip.UI 类型（Explorer 进程隔离）
  - COM 组件应该是**轻量**的，只读取注册表 + 调用 CLI
  - 不要用 IPC 方案（增加复杂度和 Explorer 崩溃风险）

  **References**:
  - UI/AppSettings.cs — 源设置（`Save()` 方法需扩展）
  - UI/ShellIntegration.cs — 当前如何读取设置
  - src/MantisZip.ShellExt/ContextMenuHandler.cs — COM 组件消费端

  **Parallelization**:
  - NO
  - Wave: Wave 3
  - Blocked By: Task 4

  **Acceptance Criteria**:
  - [ ] COM 组件从注册表读取设置
  - [ ] AppSettings 保存时同步到注册表
  - [ ] 设置变更后 Explorer 正确反映

  **Commit**: YES (groups with 7)

---

- [ ] 9. 菜单图标支持

  **What to do**:
  - 在 `QueryContextMenu` 中处理图标：
    - 使用 `MENUITEMINFO.fType = MFT_OWNERDRAW` 或 `MIIM_BITMAP`
    - 更好的方式：`MENUITEMINFO.dwItemData` + 所有者绘制
    - 或使用 `SetMenuItemBitmaps` 设置图标
  - 图标来源：
    - 内置资源：MantisZip 主程序图标
    - 分隔符不需要图标
  - 遵守注册表中的 `ShowMenuIcons` 设置（见 Task 8 注册表同步）
  - 图标缓存：避免每次右键都重新加载（`ConcurrentDictionary` 或静态字段）

  **Must NOT do**:
  - 不要从 `shell32.dll` 取图标（当前做法，不灵活）

  **References**:
  - UI/ShellIntegration.cs — 当前图标设置（shell32.dll 索引）
  - Win32 `MENUITEMINFO` / `SetMenuItemBitmaps` 文档

  **Parallelization**:
  - NO
  - Wave: Wave 3
  - Blocked By: Task 4

  **Acceptance Criteria**:
  - [ ] 菜单项显示图标
  - [ ] ShowMenuIcons=false 时无图标
  - [ ] 图标加载不影响菜单显示速度

  **Commit**: YES
  - Message: `feat(shell): add custom icons to COM context menu`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Verify all Must Have implemented. Check no static registry verbs remain (except file associations).

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Build check. Check COM visibility attributes correct. Verify no WPF dependency in COM component. Check exception handling in all COM interface methods.

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Full test cycle:
  1. Register COM menu → verify menus appear on all file types
  2. Click each menu item → verify correct CLI command executed
  3. Test on archive (.zip) → verify open/extract menus appear
  4. Test on non-archive (.txt) → verify only compress menus appear
  5. Toggle each setting → verify menu changes
  6. Cascade mode on/off → verify submenu vs flat menu
  7. Directory right-click → verify correct menus
  8. Directory background right-click → verify correct menus
  9. Multi-select → verify distribute paths correctly (compress)
  10. Unregister → verify menus disappear
  11. Old static entries cleaned up

- [ ] F4. **Scope Fidelity Check** — `deep`
  Verify no file association change. Verify no drag-drop COM work leaked in. Verify no WPF reference in COM component.

---

## Commit Strategy

- **3-6**: `feat(shell): implement COM IContextMenu handler`
- **7-8**: `feat(shell): add COM context menu registration and settings sync`
- **9**: `feat(shell): add custom icons to COM context menu`
- (Tasks 1-2 are research/audit, no commits)

---

## Success Criteria

### Final Checklist
- [ ] COM 组件在 Explorer 中加载正常，不崩溃
- [ ] 8 个菜单项全功能工作
- [ ] 动态文本正确显示文件名
- [ ] 层叠模式 / 独立动词模式都可正常工作
- [ ] 菜单开关 toggle 全部生效
- [ ] 图标显示正常
- [ ] 多文件选中传递所有路径
- [ ] 注册/反注册完整无残留
- [ ] 旧静态注册表条目清理干净
- [ ] 文件关联（OpenWithProgids）不受影响
- [ ] `--install-shell` / `--uninstall-shell` 同时处理 COM 注册和文件关联
