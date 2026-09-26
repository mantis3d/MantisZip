# 密码流程统一计划：多格式分支 → 统一入口

> 将 LoadArchiveAsync 中 3 条独立的密码分支（ZIP / EncryptHeaders=true 7z / EncryptHeaders=false 7z）合并为一条统一的高层流程，格式差异封装到引擎层。
> **状态**: ✅ 已完成 | **阶段**: [████] (4/4)

---

## 动机

### 现状问题

当前 `LoadArchiveAsync` 的密码处理是 3 条独立分支，加上 3 个提取路径也各有独立的密码逻辑：

```
LoadArchiveAsync
├── try ListEntriesAsync
│   ├── SUCCESS → hasEncrypted
│   │   ├── SevenZipEngine → dialog(no verify)  ← 分支 A
│   │   └── other → TryMatchPassword → dialog    ← 分支 B
│   └── CATCH password error → TryMatchPassword → dialog  ← 分支 C

ExtractAsync (MainWindow)    → 独立的 TryMatchPassword + dialog
RunExtractStatic (App.Extract) → 独立的 TryMatchPassword + ExtractWithPasswordAsync
HandleExtractBatchCore         → 独立的 TryMatchPassword + PromptForPassword
```

**问题清单：**

| # | 问题 | 后果 |
|---|------|------|
| 1 | `PasswordDialog` 创建代码重复 5+ 处 | 修改对话框行为需改 5 处，容易遗漏 |
| 2 | `TrySavePassword` + `UpdatePasswordStatus` 分散在各处 | 保存逻辑不一致 |
| 3 | `passwordHandledInCatch` 标志传递隐式状态 | 阅读者难以理解流程 |
| 4 | 提取路径各自实现密码弹窗逻辑 | EncryptHeaders=false 7z 在提取时需重新输入密码 |
| 5 | `ExtractWithPasswordAsync` 强依赖 `QuickVerifyPassword` | 对 EncryptHeaders=false 7z 永远返回 true，失去验证意义 |

### 目标

将所有密码交互（TryMatchPassword → Dialog → 保存 → 更新 UI）合并为一个可复用的方法，`LoadArchiveAsync` 和所有提取路径都调用同一入口。

---

## 改动范围

### 受影响的文件

| 文件 | 改动量 | 说明 |
|------|--------|------|
| `App.Password.cs` | 🔴 重 | 新增 `ResolvePasswordAsync` 统一入口；`ExtractWithPasswordAsync` 改为调用它 |
| `MainWindow.xaml.cs` | 🔴 重 | `LoadArchiveAsync` 密码部分替换为统一入口；`ExtractAsync` 复用同一入口 |
| `App.Extract.cs` | 🟡 中 | `RunExtractStatic`、`HandleExtractBatchCore` 的密码部分替换 |
| `MainWindow.Menu.cs` | 🟢 轻 | `EnterPassword_Click` 可考虑统一（可选） |
| `IArchiveEngine` (接口) | 🟢 轻 | 可能新增 `bool NeedsPasswordToList { get; }` 属性 |

### 涉及的调用点

```
TryMatchPassword (6 处调用)
├── MainWindow.xaml.cs:675   LoadArchiveAsync catch 分支 (EncryptHeaders=true 7z)
├── MainWindow.xaml.cs:781   LoadArchiveAsync 非 7z 分支 (ZIP)
├── MainWindow.xaml.cs:971   ExtractAsync
├── App.Extract.cs:362       HandleExtractBatchCore
└── App.Extract.cs:680       RunExtractStatic

PromptForPassword (3 处)
├── MainWindow.xaml.cs:↑ 内嵌在密码弹窗循环中
├── App.Extract.cs:372       HandleExtractBatchCore
└── App.Password.cs:106      定义

ExtractWithPasswordAsync (2 处)
├── MainWindow.xaml.cs:975   ExtractAsync dialog 后续
└── App.Extract.cs:742       RunExtractStatic
```

---

## 目标设计

### 统一密码解析器 `ResolvePasswordAsync`

核心思想：一个方法，处理所有格式的密码获取逻辑。

```csharp
/// <summary>
/// 统一密码解析入口。处理格式差异：
/// - ZIP: QuickVerifyPassword 可靠 → 可循环验证
/// - 7z EncryptHeaders=true: 需要密码才能列出条目
/// - 7z EncryptHeaders=false: QuickVerify 不可信，接受密码但不验证
/// 
/// 返回值:
///   (Password, Description, Patterns)  用户输入/匹配到的密码
///   null                              用户取消/不需要密码
/// </summary>
internal static async Task<PasswordResult?> ResolvePasswordAsync(
    string archivePath,
    IArchiveEngine engine,
    IReadOnlyList<ArchiveItem>? existingItems,  // null = 需要先获取密码才能列出
    ProgressWindow? progressWindow,
    Window? owner,
    CancellationToken ct)
```

### 统一流程

```
ResolvePasswordAsync(archivePath, engine, existingItems, ...)
│
├── [1] 检查是否需要密码
│   ├── existingItems != null → hasEncrypted = items.Any(i => i.IsEncrypted)
│   ├── existingItems == null → hasEncrypted = true (需要密码才能列出)
│   └── !hasEncrypted → return null (无需密码)
│
├── [2] 尝试已保存密码
│   ├── TryMatchPassword → 匹配成功?
│   │   └── YES → verify:
│   │       ├── existingItems == null → try ListEntriesAsync(password)
│   │       │   ├── success → return PasswordResult
│   │       │   └── fail → saved password wrong → fall through
│   │       └── existingItems != null → return PasswordResult
│   └── NO → fall through
│
├── [3] 密码对话框循环
│   ├── 用户取消 → return null (needToList ? throw : null)
│   └── 用户输入 ✓
│       ├── CanTrustQuickVerify && !QuickVerify → 提示重试
│       ├── existingItems == null → try ListEntriesAsync(password)
│       │   ├── success → save + return PasswordResult
│       │   └── fail → 提示重试
│       └── existingItems != null → save + return PasswordResult
│
└── 返回 PasswordResult / null
```

### 格式差异封装

```csharp
// 以下方法已存在或可新增:

// 1. QuickVerifyPassword — 已验证密码（ZIP 可靠，7z EncryptHeaders=true 可靠）
// 2. CanTrustQuickVerify — 验证结果是否可信（EncryptHeaders=false 7z 不可信）
// 3. HasEncryptedEntries — 检查是否有加密条目

// 新增:
// 4. engine.CanListWithoutPassword → 接口属性，判断是否需要先获取密码
//     true: ZIP, EncryptHeaders=false 7z
//     false: EncryptHeaders=true 7z
```

### 提取路径复用

所有提取路径调用同一入口：

```csharp
// ExtractAsync 中:
var pwdResult = await App.ResolvePasswordAsync(archivePath, engine, 
    existingItems: null, progressWindow, this, ct);
if (pwdResult == null) { /* 用户取消或无密码 */ return; }
// 用 pwdResult.Password 提取
```

`RunExtractStatic` 和 `HandleExtractBatchCore` 同理。

---

## Phase 1: 核心统一方法

**文件**: `App.Password.cs`

- 新增 `PasswordResult` 类（替代现有的 tuple 返回）
- 新增 `ResolvePasswordAsync` 内部实现，包含:
  - TryMatchPassword 调用
  - 密码对话框循环（从 5 处复制中提取公共逻辑）
  - CanTrustQuickVerify 分流
  - ListEntriesAsync 重试（对 EncryptHeaders=true 7z）
  - TrySavePassword 整合
- 现有 `ExtractWithPasswordAsync` 改为调用 `ResolvePasswordAsync`

**删除/简化**:
- `PromptForPassword` 保留但作为底层工具（`ResolvePasswordAsync` 内部调用）
- 删除 `_currentPassword` 直接操作的模式（统一由 `ResolvePasswordAsync` 返回）

## Phase 2: LoadArchiveAsync 接入

**文件**: `MainWindow.xaml.cs`

替换 ~90 行分支代码为:

```csharp
// 1. 尝试列出条目（无论是否加密）
try { items = await engine.ListEntriesAsync(archivePath); }
catch when (password error) { /* items=null, 标记需要密码 */ }

// 2. 统一密码解析
var pwdResult = await App.ResolvePasswordAsync(archivePath, engine,
    existingItems: items, owner: this, ...);
if (pwdResult != null)
{
    _currentPassword = pwdResult.Password;
    // 如果需要重新列出 (EncryptHeaders=true 7z):
    if (items == null && pwdResult != null)
        items = await engine.ListEntriesAsync(archivePath, _currentPassword);
}
```

删除 `passwordHandledInCatch` 标志。

## Phase 3: 提取路径接入

**文件**: `App.Extract.cs`、`MainWindow.xaml.cs`

- `ExtractAsync` (MainWindow): 替换 TryMatchPassword → dialog 为 `ResolvePasswordAsync`
- `RunExtractStatic`: 同上
- `HandleExtractBatchCore`: 同上
- `ExtractWithPasswordAsync`: 简化，移除 QuickVerify 逻辑（由 `ResolvePasswordAsync` 处理）

## Phase 4: 清理与验证

- 删除所有重复的 `PasswordDialog` 创建代码
- 统一 `UpdatePasswordStatus` 调用点
- 验证所有加密格式（ZIP AES-256 / 7z EncryptHeaders=true / 7z EncryptHeaders=false / RAR）都能正确走通
- 验证提取密码错误 → 重试弹窗
- 验证已保存密码自动匹配
- 验证"记住密码"保存到密码库

---

## 风险与注意事项

| 风险 | 缓解措施 |
|------|----------|
| EncryptHeaders=false 7z 无法验证密码，`ResolvePasswordAsync` 对 dialog 返回的密码无法判断对错 | 不循环，接受密码；提取时由 SharpSevenZip 抛 "data error" |
| `existingItems` 在 EncryptHeaders=true 7z 时为 null，需要 re-list | 通过 `CanListWithoutPassword` 属性区分 |
| 统一的 PasswordResult 需要同时兼容 LoadArchiveAsync（需要 `needToList`）和提取路径（不需要） | PasswordResult 包含 `Password` 字段，是否 re-list 由调用方根据 `items==null` 判断 |
| 提取路径（`HandleExtractBatchCore`）在 Task.Run 中，`owner=null` | 已支持，对话框居中显示 |
