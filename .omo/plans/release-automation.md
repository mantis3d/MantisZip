# Release Automation (GitHub Actions CI/CD)

> **状态**: 📋 待实现 | **阶段**: [ ] (1/1)

## TL;DR

> **Quick Summary**: 在现有 CI (`ci.yml`) 基础上增加 `dotnet publish` + Inno Setup 打包 + GitHub Releases 自动发布流程。打 tag 即触发构建安装包并上传 Release。
>
> **Deliverables**:
> - 修改 `.github/workflows/ci.yml`：拆分 PR/推送触发和 tag 触发两个 workflow；tag 触发时执行完整发布流程
> - 安装 Inno Setup (`iscc`) 并编译 `installer.iss` → 产出 `MantisZip-{version}-Setup.exe`
> - 自动创建 GitHub Release 并上传安装包
> - 版本号统一从 git tag 派生，消除三个文件手动同步的痛点
>
> **Estimated Effort**: 1-2h
> **Parallel Execution**: NO — 单 workflow 顺序步骤
> **Critical Path**: Task 1

---

## Context

### 当前状态

1. **已有 CI**（`.github/workflows/ci.yml`）：
   - 在 `main` 分支 push/PR 时触发
   - `dotnet restore` → `dotnet build -c Release` → `dotnet test`
   - **没有** publish / Inno Setup / Release 步骤

2. **已有 Inno Setup 脚本**（`installer.iss`）：
   - 读取 `publish_output\` 目录下的构建产物
   - 产出 `MantisZip-{version}-Setup.exe`
   - 包含 WebView2 引导安装、主题选择、右键菜单注册等

3. **版本号散落在三个文件**：
   - `src/MantisZip.UI/AppConstants.cs` — `Version = "0.3.13"`
   - `installer.iss` — `#define MyAppVersion "0.3.13"`
   - `src/MantisZip.UI/MantisZip.UI.csproj` — `<Version>0.3.13</Version>`

4. **7z.dll**：项目依赖 SharpSevenZip，需要原生 `7z.dll`（x64 + x86），已通过 `Directory.Build.props` 或手动复制到输出目录。CI 环境中也需要带上。

### 设计决策

| 决策 | 选项 | 选择 | 理由 |
|------|------|------|------|
| 触发方式 | ① tag push 触发 ② 手动 workflow_dispatch | **① tag push** | 符合语义化版本习惯，`git tag v0.4.0 && git push --tags` 即发布 |
| 版本号来源 | ① 从 tag 读取 ② 从 csproj 读 ③ 从 AppConstants 读 | **① 从 git tag 读** | 消除三个文件手动同步问题，tag 即信源 |
| 单 workflow vs 拆分 | ① 一个 yml 条件分支 ② 拆成 ci.yml + release.yml | **② 拆成两个** | 职责清晰，PR 触发的不跑发布逻辑，发布专用的 steps 不会影响 CI 速度 |
| 发布方式 | ① `gh release create` ② `softprops/action-gh-release` | **① `gh` CLI** | 无需额外 action，灵活度更高 |
| .NET 部署模式 | ① Framework-dependent ② Self-contained | **① Framework-dependent** | 安装包小，用户自行安装 .NET Runtime；需要用户装 Runtime 但安装体验更好 |

---

## Task 1: 改造 CI Workflow

**Files:**
- Modify: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`

### Step 1: 精简 ci.yml — 只保留 PR/推送校验

将现有 `ci.yml` 从按 `push` + `pull_request` 触发改为按分支 push 触发（去掉 release 相关逻辑）：

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v6

      - name: Setup .NET 9
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 9.0.x

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Test (excluding SevenZipEngine — requires native 7z.dll)
        run: >
          dotnet test --no-restore --configuration Release
          --filter "FullyQualifiedName!~SevenZipEngine"
          --logger "console;verbosity=normal"
```

> **注意**：`push: branches: [main]` 仍然保留在 ci.yml，因为 main 分支的 push 也应该跑测试。tag push 会由 release.yml 处理，两者不冲突（tag push 不触发分支 push 事件）。

### Step 2: 创建 release.yml

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'  # 匹配 v0.4.0, v0.4.1, v1.0.0 等

jobs:
  release:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v6

      - name: Setup .NET 9
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 9.0.x

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Test
        run: >
          dotnet test --no-restore --configuration Release
          --filter "FullyQualifiedName!~SevenZipEngine"
          --logger "console;verbosity=normal"

      - name: Publish (framework-dependent)
        run: >
          dotnet publish src\MantisZip.UI\MantisZip.UI.csproj
          --no-restore --configuration Release
          --output publish_output
          -p:DebugType=portable
          -p:DebugSymbols=true

      - name: Install Inno Setup
        run: |
          choco install innosetup --no-progress -y
          # 或使用 iscc 便携版：
          # curl -sL "https://jrsoftware.org/download.php/is.exe" -o is.exe
          # .\is.exe /verysilent /suppressmsgboxes /norestart

      - name: Extract version from tag
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}"
          $version = $tag -replace '^v', ''
          echo "VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
          echo "Building version: $version"

      - name: Update installer.iss version
        shell: pwsh
        run: |
          $iss = Get-Content installer.iss -Raw
          $iss = $iss -replace '(#define MyAppVersion ")[^"]*(")', "`$1$env:VERSION`$2"
          Set-Content installer.iss -Value $iss -NoNewline

      - name: Build installer
        shell: pwsh
        run: |
          # Inno Setup 默认安装路径
          $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
          if (-not (Test-Path $iscc)) {
            $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe"
          }
          if (-not (Test-Path $iscc)) {
            # 从 PATH 找
            $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
          }
          if (-not $iscc) { throw "ISCC.exe not found" }
          & $iscc installer.iss
          echo "Installer built"

      - name: Upload installer as artifact
        uses: actions/upload-artifact@v4
        with:
          name: MantisZip-${{ env.VERSION }}-Setup
          path: installer\MantisZip-${{ env.VERSION }}-Setup.exe

      - name: Create Release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          gh release create "${{ github.ref_name }}" `
            --title "MantisZip ${{ env.VERSION }}" `
            --notes "请参见 [PROGRESS.md](docs/PROGRESS.md) 了解本次更新内容。" `
            installer\MantisZip-${{ env.VERSION }}-Setup.exe
```

### Step 3: 额外产出 — 便携版 zip（可选）

在 release.yml 的 publish 步骤之后，增加一个便携版打包：

```yaml
      - name: Package portable zip
        shell: pwsh
        run: |
          Compress-Archive -Path publish_output\* -DestinationPath "installer\MantisZip-${{ env.VERSION }}-Portable.zip"
          echo "Portable zip created"

      - name: Upload portable artifact
        uses: actions/upload-artifact@v4
        with:
          name: MantisZip-${{ env.VERSION }}-Portable
          path: installer\MantisZip-${{ env.VERSION }}-Portable.zip

      # 在 gh release create 命令中追加便携版文件
      # gh release create 行加上 installer\MantisZip-${{ env.VERSION }}-Portable.zip
```

---

## Task 2: 验证与测试

### Step 1: 本地模拟

在本地测试发布流程的关键步骤：

```powershell
# 1. publish
dotnet publish src\MantisZip.UI\MantisZip.UI.csproj -c Release -o publish_output

# 2. 确认 publish_output 包含所需文件
ls publish_output\*.dll
ls publish_output\MantisZip.UI.exe
ls publish_output\x64\7z.dll

# 3. 编译安装包（需要本机有 ISCC）
iscc installer.iss
ls installer\MantisZip-*-Setup.exe
```

### Step 2: CI 端到端验证

1. 在 feature 分支上先测试 CI 通过：
   - 推送分支 → 确认 `ci.yml` build + test 通过
2. 创建一个测试 tag 触发 release：
   ```powershell
   git tag v999.0.0-test
   git push origin v999.0.0-test
   ```
3. 在 GitHub Actions 页面确认 release.yml 执行成功
4. 检查 GitHub Releases 页面是否出现了 Release 和附件
5. **清理测试 tag 和 Release**
   ```powershell
   git tag -d v999.0.0-test
   git push origin --delete v999.0.0-test
   ```
   GitHub Release 在网页端删除

---

## Task 3: 后续优化（可选）

- **Release notes 自动生成**：可以用 `git log --oneline --no-decorate $previous_tag..$current_tag` 自动生成变更列表
- **并发控制**：防止快速连续 tag 创建多个 Release 打架（加 `concurrency` 限制）
- **代码签名**：如果将来有代码签名证书，可以加 Authenticode 签名步骤
- **自动更新 installer.iss 的 `#define MyAppVersion`**：目前用 sed 在 CI 里替换，更干净的做法是 CI 从 csproj 读版本号

---

## 关键注意事项

1. **7z.dll**：需要确认 CI 环境中 `publish_output\x64\7z.dll` 和 `publish_output\x86\7z.dll` 正确存在。目前的项目配置应该会通过 `Directory.Build.props` 或 `.csproj` 的 `<Content>` 包含这两个文件。如果缺失，需要在 publish 前手动复制。
2. **choco install innosetup**：GitHub Actions 的 windows-latest 镜像已经有 Chocolatey，但首次安装可能较慢。首次运行后会被缓存。
3. **gh CLI 权限**：`${{ github.token }}` 默认只有 `contents: write` 权限，足够创建 Release 和上传附件。如果后续需要发 Issues 评论则需要额外权限。
4. **版本号不一致问题**：`git tag` 带 `v` 前缀（`v0.4.0`），内部版本号不带（`0.4.0`）。安装包文件名和 `.csproj` 版本用不带 `v` 的格式。这个方案彻底消除了三个文件的手动同步问题。
5. **installer.iss 版本号替换**：在 CI 运行时会修改 `installer.iss`，但不会提交回仓库。如果需要在本地构建，仍然需要手动改 `installer.iss` 或从 csproj 读版本号。
