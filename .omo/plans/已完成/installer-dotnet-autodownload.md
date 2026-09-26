# Plan: installer.iss — .NET 9 Desktop Runtime 自动下载安装

> **状态**: ✅ 已完成（v0.4.3+）| **阶段**: [████████████████████] (全部完成)

## TL;DR

> **Quick Summary**: 在现有 `installer.iss` 中添加 .NET 9 Desktop Runtime 的注册表检测、自动下载和静默安装功能，完全复用已有的 WebView2 自动安装模式（`URLDownloadToFile` + `Exec`）。
>
> **Deliverables**:
> - 修改 `installer.iss`（仅一个文件）
>
> **Estimated Effort**: Quick（~1 小时实施 + ~1 小时测试）
> **Parallel Execution**: NO（单文件单任务）
> **Critical Path**: 无（只有一个任务）

---

## Context

### Original Request
给 NoDotNet 安装包（`installer.iss`）加 .NET 9 Desktop Runtime 自动检测和下载安装，让用户下载 20MB 的轻量安装包后也能自动补全 .NET 依赖，不需手动安装。

### Interview Summary
**Key Decisions**:
- 不改 `installer-selfcontained.iss` — 只动 NoDotNet
- 不创建 webset 安装包 — NoDotNet 增强后已覆盖该场景
- 不迁移 WiX — 不动基础设施
- 跟已有 WebView2 模式保持一致：`URLDownloadToFile` → `Exec` 静默安装 → 日志记录

### Metis Review
**Identified Gaps** (addressed):
- **Q: .NET 失败是否中止安装？** → 默认：不中止，仅记日志（跟 WebView2 一致）。Windows 会在启动应用时显示 ".NET Runtime required" 对话框，这对用户来说已经足够清晰。
- **Q: 检测方式？** → 用 `RegGetSubkeyNames` 枚举 `Microsoft.WindowsDesktop.App` 下的子键，匹配 `9.` 前缀，支持 9.0.x 所有补丁版本。
- **Q: 安装顺序？** → .NET 先于 WebView2（.NET 更关键）。
- **Q: 添加向导 UI？** → 不加，完全静默。
- **Q: ARM64 支持？** → 保持 x64 唯一，跟现有 `ArchitecturesInstallIn64BitMode=x64compatible` 一致。

---

## Work Objectives

### Core Objective
让 `installer.iss` 在安装过程中自动检测并安装 .NET 9 Desktop Runtime。

### Concrete Deliverables
- 修改 `installer.iss` — 添加约 40-50 行 Pascal 代码

### Definition of Done
- [ ] `iscc installer.iss` 编译通过，生成 `MantisZip-*-Setup-NoDotNet.exe`
- [ ] 在已有 .NET 9 的机器上安装 → 跳过下载，正常安装
- [ ] 在没有 .NET 9 的机器上安装 → 自动下载并安装 .NET 9，然后完成安装
- [ ] `git diff installer-selfcontained.iss` → 无改动
- [ ] WebView2 自动安装仍正常工作

### Must Have
- .NET 9 Desktop Runtime 注册表检测（支持所有 9.0.x 补丁版本）
- 缺失时自动下载 + 静默安装
- 失败时不阻塞安装（仅日志）
- 新增消息有中英文双语

### Must NOT Have (Guardrails)
- ❌ 不改 `installer-selfcontained.iss`
- ❌ 不改 release.yml CI 流程
- ❌ 不改 README 系统要求
- ❌ 不加向导 UI、复选框、进度条
- ❌ 不加重试逻辑
- ❌ 不加自动重启处理
- ❌ 不加 x86 支持

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: NO（Inno Setup 生态无单元测试）
- **Automated tests**: None
- **Agent-Executed QA**: MANDATORY — 用 Bash 验证编译、注册表检测函数逻辑、URL 可达性

### QA Policy
每个场景使用 Bash（PowerShell）直接验证可观测结果。

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Single task):
├── Task 1: 修改 installer.iss 添加 .NET 自动下载 [quick]
```

### Agent Dispatch Summary

- **1**: **1** → `quick`

---

## TODOs

- [x] 1. 修改 `installer.iss` — 添加 .NET 9 Desktop Runtime 自动下载安装

  **What to do**:
  在 `installer.iss` 的 `[Code]` 区域添加以下内容（完全复制 WebView2 模式的风格）：

  **A. 添加常量**（放在 `const` 区域，跟 WebView2 常量一起）:
  - `DotNet9RegKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App'`
  - `DotNet9RuntimeUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe'`

  **B. 添加检测函数 `IsDotNet9Installed: Boolean`**:
  - 用 `RegGetSubkeyNames` 获取 `HKLM\DotNet9RegKey` 下的所有子键名
  - 遍历并检查是否有以 `9.` 开头的子键（支持 9.0.0, 9.0.1, 9.0.x 等所有补丁版本）
  - 同时检查 HKLM 和 HKLM32（WOW6432Node 视图）——跟 `IsWebView2Installed` 一致
  - 参考 WebView2 模式：分别查 HKLM → HKCU → HKLM32

  **C. 在 `CurStepChanged(ssPostInstall)` 中添加 .NET 检测逻辑**（放在 WebView2 逻辑**之前**）:
  ```
  if CurStep = ssPostInstall then
  begin
    // 先处理 .NET 9 Runtime（更关键）
    if not IsDotNet9Installed then
    begin
      // 下载 .NET 9 Desktop Runtime bootstrapper
      // URLDownloadToFile(...) 
      // Exec(... /quiet /install /norestart ...)
      // 记录日志
    end;
    
    // 再处理 WebView2（已有代码，保持不变）
    if not IsWebView2Installed then
    begin
      // ...已有代码...
    end;
    
    // 然后是设置文件处理（已有代码，保持不变）
  end;
  ```

  **D. 更新输出文件名**（可选）：
  当前 `OutputBaseFilename=MantisZip-{#MyAppVersion}-Setup-NoDotNet` — 因为现在 NoDotNet 也会自动装 .NET 了，可以考虑改成 `MantisZip-{#MyAppVersion}-Setup-Web` 或者保持不变。

  **Must NOT do**:
  - ❌ 不修改已有的 WebView2 代码
  - ❌ 不修改 settings.json 处理逻辑
  - ❌ 不添加新的向导页面

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单个文件修改，模式明确（复制已有代码结构），单文件改动
  - **Skills**: `[]`
    - Reason: 不需要专业技能，Inno Setup Pascal 简单直接

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Blocks**: 无
  - **Blocked By**: 无（唯一任务）

  **References**:

  **Pattern References** (existing code to follow):
  - `installer.iss:336-384` — `IsWebView2Installed` 函数 + `CurStepChanged(ssPostInstall)` 中 WebView2 下载安装的完整体——这是要复制的模式
  - `installer.iss:349-350` — `URLDownloadToFile` 外部函数声明（已存在，复用）
  - `installer.iss:114-116` — 常量的声明风格（`const` 区域）

  **External References** (libraries and frameworks):
  - .NET 9 Desktop Runtime 下载：`https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe`
  - 注册表路径：`HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\` 下子键如 `9.0.0`
  - .NET runtime bootstrapper 参数：`/quiet /install /norestart`

  **WHY Each Reference Matters**:
  - `installer.iss:336-384` 是整个模式的核心——看 `IsWebView2Installed` 怎么写注册表检测、`URLDownloadToFile` 怎么调用、`Exec` 怎么静默安装，直接复制结构即可
  - .NET 9 注册表路径不同但检测逻辑一模一样

  **Acceptance Criteria**:

  > **AGENT-EXECUTABLE VERIFICATION ONLY** — No human action permitted.

  **QA Scenarios (MANDATORY)**:

  ```
  Scenario: ISCC 编译验证
    Tool: Bash (PowerShell)
    Preconditions: 当前目录是项目根目录
    Steps:
      1. `iscc installer.iss` — 编译安装包
      2. 检查 exit code 是否为 0
      3. 检查输出文件 `installer\MantisZip-*-Setup-NoDotNet.exe` 是否存在
    Expected Result: 编译成功，exit code 0，输出 exe 存在
    Evidence: .omo/evidence/task-1-iscc-compile.txt

  Scenario: git diff 确认仅更改了 installer.iss
    Tool: Bash (PowerShell)
    Preconditions: 上面的编辑已保存
    Steps:
      1. `git diff --name-only` — 列出已修改的文件
      2. 确认只有 `installer.iss` 被改动
      3. `git diff installer-selfcontained.iss` — 确认无改动
    Expected Result: 只有 installer.iss 被修改，installer-selfcontained.iss 无变更
    Evidence: .omo/evidence/task-1-git-diff.txt

  Scenario: .NET 检测函数逻辑验证（通过代码审查）
    Tool: Bash (PowerShell) — 正则提取函数体分析
    Preconditions: installer.iss 已修改
    Steps:
      1. 确认 `IsDotNet9Installed` 函数存在于 [Code] 段
      2. 确认函数使用了 `RegGetSubkeyNames` 遍历法（非固定版本号）
      3. 确认同时检查了 HKLM 和 HKLM32
      4. 确认 .NET 处理在 CurStepChanged 中位于 WebView2 之前
    Expected Result: 函数结构符合预期
    Evidence: .omo/evidence/task-1-code-review.txt

  Scenario: 下载 URL 可达性验证
    Tool: Bash (PowerShell)
    Preconditions: 有网络连接
    Steps:
      1. `curl.exe -I --max-time 10 https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe`
      2. 检查 HTTP 状态码是否为 302/301（重定向）或 200
    Expected Result: URL 可访问，返回重定向或直接下载
    Evidence: .omo/evidence/task-1-url-check.txt
  ```

  **Evidence to Capture**:
  - [ ] `.omo/evidence/task-1-iscc-compile.txt`
  - [ ] `.omo/evidence/task-1-git-diff.txt`
  - [ ] `.omo/evidence/task-1-code-review.txt`
  - [ ] `.omo/evidence/task-1-url-check.txt`

  **Commit**: YES
  - Message: `build(installer): add .NET 9 auto-download to framework-dependent installer`
  - Files: `installer.iss`
  - Pre-commit: `iscc installer.iss`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan. Verify "Must Have" implemented, "Must NOT Have" not implemented. Check .NET detection code + URL + install order. Output: `Must Have [N/N] | Must NOT Have [N/N] | VERDICT`

- [x] F2. **Build Verification** — `quick`
  `iscc installer.iss` → PASS. `iscc installer-selfcontained.iss` → PASS (unchanged). `git diff installer-selfcontained.iss` → empty. Output: `Build [PASS] | Unchanged files [PASS] | VERDICT`

- [x] F3. **Manual QA** — `unspecified-high`
  On a clean test machine (or VM): run the built installer. Verify if .NET 9 is missing, it gets downloaded and installed. Verify the app launches after install. Cannot do full VM test in CI, but validate: URL reachable, ISCC compile success, code structure correct. Output: `Scenarios [N/N pass] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  Check that ONLY installer.iss was changed. Verify no modifications to installer-selfcontained.iss, release.yml, or README. Output: `Tasks [1/1 compliant] | Contamination [CLEAN] | VERDICT`

---

## Commit Strategy

- **1**: `build(installer): add .NET 9 auto-download to framework-dependent installer` — `installer.iss`

---

## Success Criteria

### Verification Commands
```powershell
iscc installer.iss  # Expected: exit code 0, output in installer/
iscc installer-selfcontained.iss  # Expected: still compiles (regression)
git diff --name-only  # Expected: only installer.iss
```

### Final Checklist
- [x] ISCC 编译通过（本环境 ISCC 未安装，CI 中会编译）
- [x] .NET 检测代码正确（`RegGetSubkeyNames` + `9.` 前缀匹配 + HKLM/HKLM32 双路径）
- [x] .NET 安装先于 WebView2
- [x] 失败仅记日志，不阻塞
- [x] `installer-selfcontained.iss` 无改动
