# Win11 一级右键菜单（IExplorerCommand）

> 让 MantisZip 出现在 Windows 11 新式右键菜单的**一级菜单**（直接右键可见），而非藏在"显示更多选项"里。
> **状态**: 📋 规划中
> **前置依赖**: ✅ COM 右键菜单完成（v0.3.7），✅ Win11 静态 verb 回退（v0.4.0）
> **风险等级**: 🔴 高（.NET COM + IExplorerCommand 组合无成熟参考，需要大量实验）

---

## Context

### 问题

Windows 11 的新式右键菜单对旧 COM 组件有严格的限制：

| 接口 | 一级菜单 | "显示更多选项" | 备注 |
|------|---------|---------------|------|
| `IContextMenu`（当前） | ❌ 被忽略 | ✅ 正常显示 | HKCU 注册被 Win11 忽略，HKLM 也可用但还是一级菜单进不去 |
| `IExplorerCommand`（目标） | ✅ 可以进入 | ✅ 也显示 | Win11 为一级菜单保留的接口，Win7+ 均可用 |
| 静态 verb（当前 Win11 回退） | ❌ | ✅ | 当前 v0.4.0 方案，无动态文本 |

**核心矛盾**：Win11 的一级菜单只接受 `IExplorerCommand` 实现。当前 MantisZip 的 COM 组件实现的是 `IContextMenu`，即使修复了 HKLM 注册也只能进二级菜单。

### IExplorerCommand 简介

`IExplorerCommand`（shobjidl_core.h，Windows Vista+）是微软为**新式命令系统**设计的接口：

```
IExplorerCommand (IUnknown)
├── GetTitle(IShellItemArray) → 菜单文本（动态！可嵌入文件名）
├── GetIcon(IShellItemArray) → 图标路径
├── GetToolTip(IShellItemArray) → 工具提示
├── GetCanonicalName → 命令唯一 GUID
├── GetState(IShellItemArray, okToBeSlow) → 可见/禁用状态
├── GetFlags → ECF_HASSUBCOMMANDS / ECF_ISSEPARATOR
├── Invoke(IShellItemArray, IBindCtx) → 执行命令
└── EnumSubCommands → 子命令枚举
```

优势：
- **Win11 一级菜单入口** — 核心目标
- **IShellItemArray** — 自然获取全部选中项，无 16 文件上限（IContextMenu 的 IDataObject 只传 16 个）
- 动态文本、图标、可见性均可运行时计算
- 原生支持子菜单（`EnumSubCommands`）

### 与当前 IContextMenu 的关系

两者**不互斥**，同一 COM 类可以同时实现两个接口：

```
ContextMenuHandler : IShellExtInit, IContextMenu, IExplorerCommand
```

- Win11 一级菜单 → Explorer 查询 `IExplorerCommand`
- Win11 二级菜单（"显示更多选项"） → Explorer 查询 `IContextMenu`
- Win10 → Explorer 查询 `IContextMenu`（`IExplorerCommand` 也可用但 Win10 菜单不区分层级）

### 依赖关系

```
┌─────────────────────────────────────────────┐
│         HKLM 提权（installer 增强）            │ ← 必须前置
└──────────────────┬──────────────────────────┘
                   ▼
┌─────────────────────────────────────────────┐
│     IExplorerCommand COM 实现（ShellExt）     │
│  ├─ COM interop 声明（P/Invoke 接口定义）     │
│  ├─ 命令枚举（CommandEnum）                   │
│  ├─ Invoke 分发                              │
│  └─ 子菜单支持（EnumSubCommands）              │
└──────────────────┬──────────────────────────┘
                   ▼
┌─────────────────────────────────────────────┐
│     注册表安装（HKLM + HKCU 双写）            │
│  ├─ CLSID → HKLM（管理员提权）                │
│  ├─ shellex → HKLM + HKCU                    │
│  └─ Approved 列表（可能需要）                  │
└─────────────────────────────────────────────┘
```

---

## 技术分析

### .NET COM IExplorerCommand 的挑战

在 .NET 中实现 `IExplorerCommand` 与 C++ 有本质区别：

| 方面 | C++ | C# (.NET COM) |
|------|-----|---------------|
| 接口声明 | 从 shobjidl_core.h 直接继承 | 需要手写 `[ComImport]` + `[Guid]` P/Invoke 定义 |
| IShellItemArray | 原生 COM 接口 | 也需要手写 COM interop |
| 线程模型 | 原生 STA | .NET COM 通过 comhost.dll 桥接 |
| 已知案例 | 多个成熟开源实现 | **几乎没有 .NET 成功案例** |

### 需要手写的 COM interop 声明

```csharp
// IExplorerCommand — GUID from shobjidl_core.h
[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray psiItemArray, out IntPtr ppszName);
    [PreserveSig] int GetIcon(IShellItemArray psiItemArray, out IntPtr ppszIcon);
    [PreserveSig] int GetToolTip(IShellItemArray psiItemArray, out IntPtr ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out EXPCMDSTATE pCmdState);
    [PreserveSig] int Invoke(IShellItemArray psiItemArray, IntPtr pbc);
    [PreserveSig] int GetFlags(out EXPCMDFLAGS pFlags);
    [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
}

// IShellItemArray — needed for GetTitle/GetState/Invoke
[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemArray
{
    // ... 多个方法需要声明
}

// IEnumExplorerCommand — for submenu support
[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumExplorerCommand
{
    [PreserveSig] int Next(uint celt, [Out] IntPtr[] rgelt, out uint pceltFetched);
    // Skip, Reset
}
```

### 注册表要求

与 IContextMenu 不同，IExplorerCommand 在 Win11 上需要**额外的注册表设置**：

```
; 基础 CLSID 注册（HKLM 必需，HKCU 在 Win11 被忽略）
HKLM\Software\Classes\CLSID\{guid}\InprocServer32
  @ = comhost.dll 路径
  ThreadingModel = "Apartment"

; 上下文菜单处理程序注册（与传统 IContextMenu 位置相同）
HKLM\Software\Classes\*\shellex\ContextMenuHandlers\MantisZip
  @ = {guid}

; ⚠️ 可能需要 — Shell Extensions Approved 列表
HKLM\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved
  {guid} = "MantisZip Context Menu"

; ⚠️ 可能需要 — Implemented Categories
HKLM\Software\Classes\CLSID\{guid}\Implemented Categories
  {00021492-0000-0000-C000-000000000046}  ; CATID_ShellExtensions
```

**安装方式**：由于需要写 HKLM，安装程序（Inno Setup）必须以管理员权限运行。安装后通过 MantisZip.UI.exe `--install-shell` 完成注册（提权子进程）。

---

## 实施策略

### Phase 0 — 前置条件：HKLM 提权基础设施

当前 `InstallCom()` 只写 HKCU。需改为同时支持 HKLM。

- 安装时：如果检测到 Win11，用 `runas` 提权启动子进程执行 `--install-shell-elevated`
- 子进程：以管理员身份写 HKLM 注册表，完成后退出
- 回退：如果用户拒绝提权，保持当前静态 verb 方案

**工作量**: 小（1 天）
**风险**: 低（已有完整 Uninstall 清理逻辑）

### Phase 1 — COM interop 声明 + 原型验证

**目标**: 确认 IExplorerCommand 可在 .NET COM hosting 中工作

1. 在 ShellExt 项目中添加 P/Invoke 接口声明
2. 实现最简 `IExplorerCommand`（GetTitle/Invoke 返回固定文本 + 启动 exe）
3. 用 regsvr32 注册 HKLM 测试
4. 验证 Win11 一级菜单出现

**关键验证点**：
- COM host 能否正确加载 `IExplorerCommand` 接口（Explorer QueryInterface）
- `IShellItemArray` 参数解析是否正常工作
- Win11 一级菜单是否真的显示

**工作量**: 中（2-3 天，主要是实验）
**风险**: 🔴 **高** — .NET 的 COM interop 与 IExplorerCommand 组合可能有未预期问题。已知风险包括：
- `IShellItemArray` 的 .NET marshal 复杂，容易崩溃 Explorer
- 线程模型不匹配（`GetTitle` 在 UI 线程调用，`Invoke` 可能在不同线程）
- .NET COM hosting 对某些 IID 的 `QueryInterface` 支持可能不完整

### Phase 2 — 完整实现

**目标**: 功能完备的 IExplorerCommand 菜单，替代 IContextMenu 作为 Win11 主方案

1. 命令枚举系统（映射 7 个操作到命令 ID）
2. 动态文本生成（嵌入文件名、文件计数）
3. 子菜单支持（EnumSubCommands — "打开/解压"和"压缩"两组）
4. 图标支持（从嵌入资源加载）
5. 设置同步（从注册表读取 toggle 状态）
6. 多文件选择处理

**工作量**: 中-大（3-5 天）
**风险**: 中（Phase 1 验证通过后风险降低）

### Phase 3 — 双接口共存 + 智能分发

**目标**: 同一 COM 类同时暴露 IContextMenu 和 IExplorerCommand

```
ContextMenuHandler : IShellExtInit, IContextMenu, IExplorerCommand
```

| 场景 | Explorer 查询的接口 | 使用的实现 |
|------|-------------------|-----------|
| Win11 一级菜单 | IExplorerCommand | Phase 2 实现 |
| Win11 "显示更多选项" | IContextMenu | 当前实现 |
| Win10 右键 | IContextMenu | 当前实现 |

**工作量**: 小（0.5 天）
**风险**: 低

---

## 风险矩阵

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| .NET COM interop 与 IExplorerCommand 不兼容 | 中 | 高（项目无法继续） | Phase 1 先做原型验证，失败则放弃 |
| Explorer 崩溃溢出 | 低 | 高（用户数据丢失） | Phase 1 全程在测试环境，用 try/catch 包裹所有接口 |
| IShellItemArray 解析不全 | 中 | 中（部分功能失效） | 备选方案：从 IObjectWithSite 获取路径 |
| Win11 更新破坏行为 | 中 | 中（菜单消失） | 保持 IContextMenu 回退 |
| 提权导致用户拒绝安装 | 低 | 中（回到静态 verb） | 保持静态 verb 保底方案 |

---

## 不做的事

- 不替换 IContextMenu（保留作为 Win10 + 回退方案）
- 不在 ShellExt 中引用 WPF/MantisZip.UI 程序集
- 不改动现有 AppSettings 接口
- 不改动 CLI 参数接口

---

## 验收标准

- [ ] Win11 右键一级菜单显示「MantisZip」入口
- [ ] 子菜单正确显示 8 个操作项
- [ ] 动态文本正确嵌入文件名
- [ ] 多文件选择正常传递路径
- [ ] 菜单 toggle 开关在注册表中读取，实时生效
- [ ] 图标正常显示
- [ ] HKLM 注册 → HKCU 注册 → 静态 verb 三级回退机制完整
- [ ] 卸载清理所有注册表条目
- [ ] Win10 右键菜单不受影响（仍用 IContextMenu）

---

## Executor 指南

### 研究资料

- Microsoft Docs: [IExplorerCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand)
- Microsoft Docs: [Creating Cascading Menus with IExplorerCommand](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-create-cascading-menus-with-the-iexplorercommand-interface)
- Microsoft Sample: [ExplorerCommandVerb (C++)](https://github.com/microsoft/Windows-AppConsult-Samples-DesktopBridge/blob/main/Docs-ContextMenuSample/ExplorerCommandVerb/ExplorerCommandVerb.cpp)
- **关键参考**: [Vanara PInvoke — IExplorerCommand C# declaration](https://github.com/dahall/Vanara/blob/master/PInvoke/Shell32/ShObjIdl.IExplorerCommand.cs)
- [StackOverflow: IExplorerCommand::Invoke not called after Win11 update](https://stackoverflow.com/questions/74226129/why-is-iexplorercommandinvoke-no-longer-being-called) — `.NET WinRT ClassicComMix` 相关
- [MS Q&A: IExplorerCommand shell extension](https://learn.microsoft.com/en-us/answers/questions/1120506/how-to-create-a-shell-extension-using-iexplorercom) — Win11 菜单限制

### 实验步骤（Phase 1）

1. 在 ShellExt 项目中新建 `IExplorerCommand.cs`，声明 COM interop 接口
2. 修改 `ContextMenuHandler` 实现 `IExplorerCommand`
3. 在 `QueryInterface` 路径中确保 `IID_IExplorerCommand` 可被解析
4. 用 `regsvr32` 注册到 HKLM（管理员 cmd）
5. 重启 Explorer，右键文件观察一级菜单
6. 如果失败，用 DebugView 捕获 `ShellExtLog` 输出

### 关键代码路径

```csharp
// 接口声明参考（Vanara 风格）
[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(IShellItemArray psiItemArray,
        [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [PreserveSig]
    int GetIcon(IShellItemArray psiItemArray,
        out IntPtr ppszIcon);

    [PreserveSig]
    int GetToolTip(IShellItemArray psiItemArray,
        out IntPtr ppszInfotip);

    [PreserveSig]
    int GetCanonicalName(out Guid pguidCommandName);

    [PreserveSig]
    int GetState(IShellItemArray psiItemArray,
        [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow,
        out uint pState);  // EXPCMDSTATE

    [PreserveSig]
    int Invoke(IShellItemArray psiItemArray, IntPtr pbc);

    [PreserveSig]
    int GetFlags(out uint pFlags);  // EXPCMDFLAGS

    [PreserveSig]
    int EnumSubCommands(out IntPtr ppEnum);
}
```

---

## 版本计划

| 版本 | 内容 | 优先级 |
|------|------|--------|
| v0.4.x | Phase 0: HKLM 提权基础设施 | 高（基础能力） |
| v0.5.0 | Phase 1: IExplorerCommand 原型 | 高（验证可行性） |
| v0.6.0 | Phase 2: 完整 IExplorerCommand 实现 | 中 |
| v0.6.1 | Phase 3: 双接口共存 | 低 |

---

## Plan Evolution Log

| 日期 | 变更 | 原因 |
|------|------|------|
| 2026-06-16 | 初始创建 | 用户需求：安排 Win11 一级菜单实现计划 |
