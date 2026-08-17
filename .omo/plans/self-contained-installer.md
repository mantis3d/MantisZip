# 自包含安装包发布

> **状态**: ✅ 已完成 | **实现版本**: v0.4.2

## TL;DR

> **Quick Summary**: 在现有发布流程中增加一个自包含（--self-contained）安装包的构建和上传，实现每个 GitHub Release 同时产出两个安装包供用户按需下载。
>
> **Deliverables**:
> - 新建 `installer-selfcontained.iss`（基于现有 `installer.iss`，修改源目录和 AppId）
> - 修改 `.github/workflows/release.yml`（增加自包含发布 + 双安装包上传）
> - 每个 Release 产出两个文件：
>   - `MantisZip-{version}-Setup.exe`（框架依赖，~15MB）
>   - `MantisZip-{version}-Setup-SelfContained.exe`（免运行时，~70MB）
>
> **Estimated Effort**: Quick（1-2h）
> **Parallel Execution**: NO（顺序执行，仅 3 个任务）
> **Critical Path**: 复制 .iss → 修改 .iss → 修改 release.yml

---

## Context

### Original Request
> "能发布时生成两个文件吗？一个是现在的，一个是免依赖的？"

### Interview Summary
**Key Discussions**:
- 自包含版本使用 `--self-contained -r win-x64`，不带 ReadyToRun 和 PublishSingleFile
- 新建一个独立的 Inno Setup 脚本，不修改现有的 `installer.iss`
- 安装包命名增加 `-SelfContained` 后缀区分

### Metis Review
**Identified Gaps** (addressed):
- **AppId 冲突**：两个 Inno Setup 脚本不能使用相同的 Windows Installer AppId，否则会互相覆盖注册信息
- **gh release create 只上传一个安装包**：当前脚本用 `Select-Object -First 1` 只取第一个 .exe，必须修改为上传两个
- **ShellExt COM 兼容性风险**：自包含发布可能改变 COM 组件（ShellExt）的加载上下文，需验证

### Research Findings
- .NET 自包含发布的 RID 为 `win-x64`（项目目标平台为 x64）
- 现有 `publish_output` 目录结构：exe + dll + pdb + deps.json + runtimeconfig.json + x64/x86 子目录（7z.dll）
- GitHub Actions 的 `gh release create` 支持一次指定多个文件路径

---

## Work Objectives

### Core Objective
在 CI/CD 发布流程中同时构建并上传框架依赖和自包含两种安装包。

### Concrete Deliverables
- `.sisyphus/plans/` → 本方案文档（已完成）
- `installer-selfcontained.iss` → 新建的自包含 Inno Setup 脚本
- `.github/workflows/release.yml` → 修改后的发布工作流
- GitHub Release 产出两个安装包文件

### Definition of Done
- [ ] `git tag v0.4.1 && git push --tags` 触发的 Release 中同时包含两个 .exe 文件
- [ ] 框架依赖版安装后在新系统上提示需要 .NET Runtime（与现在一致）
- [ ] 自包含版安装后在新系统上可以直接运行，无需安装 .NET Runtime

### Must Have
- 两个安装包使用不同的 Windows Installer AppId（GUID）
- `gh release create` 上传两个 .exe，而非仅第一个
- 自包含版保留 x64/x86 子目录的 7z.dll 分发
- 自包含版保留 ShellExt COM 功能（MantisZip.ShellExt.comhost.dll + .dll）

### Must NOT Have (Guardrails)
- 不自带 PublishTrimmed（可能剪掉 Zip/7z 编码器等反射加载的组件）
- 不自带 ReadyToRun（增大安装包体积，当前启动性能已足够）
- 不自带 PublishSingleFile（WPF 应用中与原生互操作有兼容风险）
- 不修改现有的 `installer.iss`（保持现有发布流程不受影响）
- 不修改框架依赖版的发布行为（保持向后兼容）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES（GitHub Actions + Inno Setup）
- **Automated tests**: None（纯 CI/CD 流程变更，无单元测试覆盖）
- **Agent-Executed QA**: 本地模拟验证 + CI 输出检查

### QA Policy
- CI 日志检查：确认两个 publish 步骤均成功
- CI artifacts 检查：确认 `installer/` 目录下存在两个 .exe 文件
- Release 检查：确认 `gh release create` 输出包含两个文件

---

## Execution Strategy

### Sequential Flow（3 steps, no parallelism needed）

```
Step 1: Create installer-selfcontained.iss
  └─> Step 2: Modify release.yml (publish step)
        └─> Step 3: Modify release.yml (upload step)
```

No parallel waves needed — only 2 files to change, the release.yml changes depend on understanding the existing script structure first.

---

## TODOs

- [x] 1. 新建 `installer-selfcontained.iss`

  **What to do**:
  - 复制 `installer.iss` 为 `installer-selfcontained.iss`
  - 修改以下内容：
    - **AppId GUID**：改为新的唯一 GUID（`{生成新的 GUID}`，与现有不同）
    - **OutputBaseFilename**：`MantisZip-{#MyAppVersion}-Setup-SelfContained`
    - **所有 `publish_output\` 路径** → `publish_output_selfcontained\`
    - 注释中添加版本说明，区分此文件为自包含版安装脚本
  - 保留其余所有内容不变（语言、任务、图标、WebView2 检测、设置预置等）

  **Must NOT do**:
  - 不要修改现有的 `installer.iss`
  - 不要修改 WebView2 检测逻辑（自包含版同样需要 WebView2）
  - 不要添加任何仅在自包含版中生效的条件

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 简单的文件复制 + 字符串替换，无复杂逻辑
  - **Skills**: `[]`
    - 不需要特殊技能

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential（step 1 of 2）
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:
  - `installer.iss` 全文 — 源模板文件，逐行复制并修改 5 个位置

  **Acceptance Criteria**:
  - [ ] 文件 `installer-selfcontained.iss` 存在
  - [ ] 文件中的 AppId 与 `installer.iss` 不同
  - [ ] 所有 `publish_output\` 引用已改为 `publish_output_selfcontained\`
  - [ ] OutputBaseFilename 包含 `-SelfContained` 后缀

  **QA Scenarios**:

  ```
  Scenario: 验证自包含 .iss 的关键字段差异
    Tool: Bash
    Preconditions: installer.iss 和 installer-selfcontained.iss 均已存在
    Steps:
      1. grep "AppId=" installer.iss → 记录 GUID A
      2. grep "AppId=" installer-selfcontained.iss → 记录 GUID B
      3. 比较 A 和 B 是否不同
      4. grep "OutputBaseFilename" installer-selfcontained.iss → 确认包含 "SelfContained"
      5. grep "publish_output_selfcontained" installer-selfcontained.iss | wc -l → 确认有匹配项
      6. grep "publish_output\\\\" installer-selfcontained.iss → 确认不存在旧的 publish_output 引用
    Expected Result: GUID 不同、OutputBaseFilename 含 SelfContained、源目录已改为自包含版本
    Evidence: .sisyphus/evidence/task-1-iss-diff.txt

  Scenario: 验证未意外修改现有 installer.iss
    Tool: Bash
    Preconditions: installer.iss 存在且未被修改
    Steps:
      1. grep "AppId=" installer.iss → 确认 GUID 与 git 记录一致（未变）
      2. grep "OutputBaseFilename" installer.iss → 确认不含 "SelfContained"
    Expected Result: 现有 installer.iss 完全未改动
    Evidence: .sisyphus/evidence/task-1-iss-unchanged.txt
  ```

  **Commit**: YES
  - Message: `ci: add self-contained installer script`
  - Files: `installer-selfcontained.iss`

- [x] 2. 修改 `.github/workflows/release.yml` — 增加自包含发布步骤

  **What to do**:
  - 在现有 `Publish (framework-dependent)` 步骤之后，新增一个步骤：
    ```yaml
    - name: Publish (self-contained, x64)
      run: >
        dotnet publish src\MantisZip.UI\MantisZip.UI.csproj
        --no-restore --configuration Release
        --runtime win-x64 --self-contained
        --output publish_output_selfcontained
        -p:DebugType=portable
        -p:DebugSymbols=true
    ```
  - **注意**：`dotnet publish` 会自动触发 `AfterTargets="Publish"` 的 `CopySevenZipDll` MSBuild Target，该 Target 会调用 `scripts/copy-7z-dll.ps1`。但 `PublishDir` 只在单独的 `dotnet publish` 命令中才被正确传递。需要确认自包含发布的 7z.dll 也能被正确复制到 `publish_output_selfcontained\x64\` 和 `x86\`。验证方式见 QA 场景。

  **Must NOT do**:
  - 不要移除、注释或修改现有的框架依赖发布步骤
  - 不要添加 `PublishTrimmed`、`PublishReadyToRun`、`PublishSingleFile` 等参数
  - 不要修改 `--runtime` 之外的任何项目构建配置

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 在现有工作流中添加一个结构相同的步骤
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（必须在上一个 TODO 之后）
  - **Parallel Group**: Sequential（step 2 of 2）
  - **Blocks**: Task 3
  - **Blocked By**: Task 1 和现有 release.yml 结构理解

  **References**:
  - `.github/workflows/release.yml:38-44`（行 38-44） — 现有的框架依赖发布步骤，作为模板
  - `installer.iss` — 确认源目录路径格式

  **Acceptance Criteria**:
  - [ ] release.yml 中新增了一个名为 `Publish (self-contained, x64)` 的步骤
  - [ ] 该步骤使用 `--self-contained -r win-x64` 参数
  - [ ] 输出目录为 `publish_output_selfcontained`
  - [ ] 现有 framework-dependent publish 步骤未被修改
  - [ ] 自包含发布步骤在 framework-dependent 之后执行

  **QA Scenarios**:

  ```
  Scenario: 验证 release.yml 结构正确
    Tool: Bash
    Preconditions: release.yml 已修改
    Steps:
      1. grep -c "dotnet publish" .github/workflows/release.yml → 确认有 2 个 publish 步骤
      2. grep "self-contained" .github/workflows/release.yml → 确认新增的行包含 --self-contained
      3. grep "publish_output_selfcontained" .github/workflows/release.yml → 确认输出目录
    Expected Result: 2 个 publish 步骤存在，一个含 self-contained，输出到不同目录
    Evidence: .sisyphus/evidence/task-2-yml-structure.txt

  Scenario: 验证 7z.dll 复制逻辑在自包含发布中同样生效
    Tool: Bash
    Preconditions: release.yml 中 publish 步骤已添加
    Steps:
      1. 确认 copy-7z-dll.ps1 中的 $PublishDir 参数通过 PublishDir MSBuild 属性传入
      2. 确认 csproj 中的 CopySevenZipDll Target AfterTargets="Publish" 对两个 publish 都生效
    Expected Result: 7z.dll 自动复制逻辑对两种发布均生效
    Evidence: .sisyphus/evidence/task-2-sevenzip-check.txt
  ```

  **Commit**: YES（与 Task 3 合并提交）

- [x] 3. 修改 `.github/workflows/release.yml` — 增加自包含安装包构建 + 双包上传

  **What to do**:
  - 在现有 `Build installer` 步骤之后，新增一个步骤：
    ```yaml
    - name: Build self-contained installer
      shell: pwsh
      run: |
        $iscc = (Get-Command "iscc" ...)  # 复用现有的 ISCC 查找逻辑
        & $iscc "/dMyAppVersion=$env:VERSION" installer-selfcontained.iss
    ```
  - 修改 `List installer artifacts` 步骤，改为列出两个安装包
  - **关键修改**：修改 `Create Release` 步骤中的 `gh release create` 命令，当前脚本是 `Select-Object -First 1` 只取第一个 .exe。改为：
    ```pwsh
    $installers = Get-ChildItem -Path installer -Filter "*.exe" | Select-Object -ExpandProperty FullName
    ```
    然后 `gh release create "${{ github.ref_name }}" --title "..." --notes "$notes" $installers`
  - 或者在 `gh release create` 后追加 `gh release upload` 上传第二个安装包

  **Must NOT do**:
  - 不要删除现有的 `Build installer` 步骤
  - 不要修改框架依赖安装包的输出文件名
  - 不要添加额外的 Release 创建操作（只用一个 `gh release create`）

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 结构清晰的 CI 步骤添加
  - **Skills**: `[]`

  **Parallelization**:
  - **Can Run In Parallel**: NO（依赖 Task 2 完成）
  - **Parallel Group**: Sequential（step 3 of 3）
  - **Blocked By**: Task 2

  **References**:
  - `.github/workflows/release.yml:54-86` — 现有的 ISCC 编译步骤，作为模板
  - `.github/workflows/release.yml:113-123` — 现有的 gh release create 步骤，需要修改上传逻辑

  **Acceptance Criteria**:
  - [ ] release.yml 中新增了 `Build self-contained installer` 步骤
  - [ ] 该步骤使用 `installer-selfcontained.iss` 脚本
  - [ ] `gh release create` 命令同时上传两个 .exe 文件（而非仅第一个）
  - [ ] 两个安装包文件名不同：一个常规版，一个带 `-SelfContained` 后缀

  **QA Scenarios**:

  ```
  Scenario: 验证两个安装包都能被构建和上传
    Tool: Bash（本地模拟或 CI dry-run）
    Preconditions: release.yml 已修改完成
    Steps:
      1. 确认 release.yml 中包含 "Build self-contained installer" 步骤
      2. 确认该步骤引用的是 installer-selfcontained.iss（而非 installer.iss）
      3. 确认 gh release create 步骤不再使用 Select-Object -First 1
      4. 确认 gh release create 使用了两个 .exe 路径
    Expected Result: 两个构建步骤各引用正确的 .iss 文件，gh 上传两个文件
    Evidence: .sisyphus/evidence/task-3-dual-upload.txt

  Scenario: 模拟验证—本地构建两个安装包
    Tool: Bash
    Preconditions: 本地已安装 Inno Setup，publish_output 和 publish_output_selfcontained 目录存在
    Steps:
      1. & $iscc installer.iss → 产出 installer\MantisZip-*-Setup.exe
      2. & $iscc installer-selfcontained.iss → 产出 installer\MantisZip-*-Setup-SelfContained.exe
      3. Get-ChildItem installer\*.exe → 确认两个文件都存在且文件名不同
    Expected Result: 两个 .exe 文件均成功构建
    Evidence: .sisyphus/evidence/task-3-local-build.txt
  ```

  **Commit**: YES（与 Task 2 合并）
  - Message: `ci: add self-contained installer to release workflow`
  - Files: `.github/workflows/release.yml`

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `oracle`
  读取 release.yml 全文，确认：框架依赖发布步骤未修改；自包含发布步骤存在且参数正确；两个 ISCC 构建步骤存在；gh release create 上传两个文件。读取 installer-selfcontained.iss，确认 AppId 不同、OutputBaseFilename 含 SelfContained、源目录为 publish_output_selfcontained。
  Output: `Must Have [N/N] | Must NOT Have [N/N] | VERDICT`

- [x] F2. **ShellExt COM 兼容性检查** — `unspecified-high`
  读取 release.yml 中两个 publish 步骤的输出结构差异。确认自包含发布后 `MantisZip.ShellExt.comhost.dll` 和 `MantisZip.ShellExt.dll` 存在于 `publish_output_selfcontained` 中。确认 `publish_output_selfcontained\*.runtimeconfig.json` 存在（COM 宿主需要）。
  Output: `ShellExt files present [YES/NO] | Runtimeconfig present [YES/NO] | VERDICT`

- [x] F3. **Scope Fidelity Check** — `deep`
  逐 TODO 检查：TODO 1 → `installer-selfcontained.iss` 创建完成，AppId 不同；TODO 2 → release.yml 新增 self-contained publish；TODO 3 → release.yml 新增 ISCC + 双上传。确认无超出范围的修改。
  Output: `Tasks [N/N compliant] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **1**: `ci: add self-contained installer script` — `installer-selfcontained.iss`
- **2+3**（合并）: `ci: add self-contained installer to release workflow` — `.github/workflows/release.yml`

---

## Success Criteria

### Verification Commands
```bash
# 检查两个 .iss 的 AppId 是否不同
grep "AppId=" installer.iss installer-selfcontained.iss

# 检查 release.yml 两个 publish 步骤
grep -c "dotnet publish" .github/workflows/release.yml  # 期望: 2

# 检查自包含参数
grep "self-contained" .github/workflows/release.yml

# 检查两个 ISCC 步骤
grep -c "ISCC" .github/workflows/release.yml  # 期望: 2

# 检查 gh release create 不再用 -First 1
grep "First" .github/workflows/release.yml  # 期望: 无匹配（或仅出现在 List artifacts 中）
```

### Final Checklist
- [ ] `installer-selfcontained.iss` 存在且 AppId 与 `installer.iss` 不同
- [ ] `.github/workflows/release.yml` 有两个 publish、两个 ISCC 步骤
- [ ] `gh release create` 上传两个 .exe 文件
- [ ] 自包含版不需要 .NET Runtime
- [ ] 框架依赖版不受影响
