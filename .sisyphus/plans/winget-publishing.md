# Winget 发布

> 将 MantisZip 发布到 Windows Package Manager (winget) 社区仓库。
> **状态**: 📋 待执行 | **任务**: [⬜⬜⬜⬜⬜⬜] (0/6)
> 创建日期：2026-07-10

## 动机

| 收益 | 说明 |
|------|------|
| 一键安装 | `winget install MantisZip.MantisZip` — 用户无需手动下载安装包 |
| 自动更新发现 | winget 可检测新版本并通知用户 |
| 企业分发 | 支持 SCCM/Intune/GPO 等企业管理方案 |
| 可信度提升 | Microsoft 官方仓库收录，降低用户安全顾虑 |
| 自动化 | 首次提交后，后续版本通过 CI 自动提交 PR |

## 前置条件

- [x] **Inno Setup 安装包** — 已有（`installer.iss`，生成 `.exe`）
- [x] **GitHub Releases** — 已有自动化发布流程（`release.yml`，`v*` tag 触发）
- [x] **Silent install 支持** — Inno Setup 原生支持 `/SILENT`（winget 要求）
- [x] **MIT 许可证** — 已有
- [x] **稳定的 HTTPS 下载 URL** — GitHub Releases asset URL 格式稳定
- [ ] **GitHub classic PAT** — 需要 `public_repo` 权限（fine-grained token 不行）

## 选择提交用的安装包

winget 提交需要选一个安装程序 URL。MantisZip 有两个版本：

| 版本 | 文件 | 优点 | 缺点 |
|------|------|------|------|
| **WebSetup（框架依赖）** | `MantisZip-{version}-Setup-WebSetup.exe` | 体积小 (~3MB) | 需安装 .NET 9 Runtime + WebView2 —— 静默安装链可能超时 |
| **Offline（自包含）** 🔸 | `MantisZip-{version}-Setup-Offline.exe` | 自带 .NET 9，不依赖额外运行时下载 | 体积大 (~60MB) |

**推荐用 Offline（自包含）版本**，因为 winget 验证环境不一定有 .NET 9 Runtime，自包含版通过率更高。

## 任务清单

### Phase 1 — 首次提交（一次性手动操作）

- [ ] **1. 创建 GitHub classic PAT**
  - GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
  - 勾选 `public_repo` 权限
  - 将 token 添加到仓库 Secrets：`Settings → Secrets and variables → Actions`，命名为 `WINGETCREATE_TOKEN`
  - 注意：fine-grained token 对 `wingetcreate submit` 无效

- [ ] **2. 生成 winget 清单文件**

  在 Windows 环境（本地或 GitHub Actions runner）执行：

  ```powershell
  # 安装 wingetcreate
  winget install wingetcreate

  # 生成清单（替换版本号）
  wingetcreate new "https://github.com/mantis3d/MantisZip/releases/download/v0.4.4/MantisZip-0.4.4-Setup-Offline.exe"
  ```

  这会以交互方式提示输入元数据，完成后生成 3 个 YAML 文件：

  ```
  manifests/m/MantisZip/MantisZip/0.4.4/
  ├── MantisZip.yaml                # 版本文件
  ├── MantisZip.installer.yaml      # 安装器信息
  └── MantisZip.locale.en-US.yaml   # 本地化信息
  ```

  推荐的元数据填写：

  | 字段 | 值 |
  |------|-----|
  | PackageIdentifier | `MantisZip.MantisZip` |
  | Publisher | `MantisZip Contributors` |
  | PackageName | `MantisZip` |
  | License | `MIT` |
  | ShortDescription | `轻量级全功能 Windows 压缩/解压软件 / Lightweight Windows compression/decompression tool` |
  | InstallerType | `inno` |
  | InstallerUrl | GitHub Releases asset 地址 |
  | ReleaseDate | 版本发布日期 |

- [ ] **3. 验证清单**

  ```powershell
  winget validate --manifest manifests/m/MantisZip/MantisZip/0.4.4/
  ```

- [ ] **4. 提交 PR 到 microsoft/winget-pkgs**

  ```powershell
  # 方案 A：用 wingetcreate 自动提交
  wingetcreate submit manifests/m/MantisZip/MantisZip/0.4.4/ --token <你的PAT>

  # 方案 B：手动 fork + PR
  # fork https://github.com/microsoft/winget-pkgs
  # 将 manifests/ 目录复制到 fork 中
  # git commit + push → 在 GitHub 上创建 PR
  ```

- [ ] **5. 签署 CLA + 通过验证**

  PR 提交后：
  1. `microsoft-github-policy-service` bot 会要求签署 CLA，评论：
     ```
     @microsoft-github-policy-service agree
     ```
  2. 等待自动化验证流水线通过（检查 URL、SHA256、静默安装等）
  3. 如验证失败，根据 [ValidationFailureGuide](https://github.com/microsoft/winget-pkgs/blob/master/doc/ValidationFailureGuide.md) 修复
  4. 人工审核合并（通常 1~5 个工作日）

### Phase 2 — CI 自动化（首次 PR 合并后执行）

- [ ] **6. 在 release.yml 中添加 winget 自动发布 job**

  在 `.github/workflows/release.yml` 末尾添加（`needs: release`）：

  ```yaml
  publish-winget:
    name: Publish to Winget
    needs: release
    runs-on: windows-latest
    steps:
      - name: Extract version
        id: version
        shell: bash
        run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT

      - name: Download wingetcreate
        run: Invoke-WebRequest -Uri "https://aka.ms/wingetcreate/latest" -OutFile "wingetcreate.exe"

      - name: Submit update to winget-pkgs
        shell: pwsh
        env:
          GITHUB_TOKEN: ${{ secrets.WINGETCREATE_TOKEN }}
        run: |
          $version = "${{ steps.version.outputs.VERSION }}"
          $url = "https://github.com/mantis3d/MantisZip/releases/download/v$version/MantisZip-$version-Setup-Offline.exe"
          .\wingetcreate.exe update MantisZip.MantisZip `
            --version $version `
            --urls $url `
            --submit `
            --token $env:GITHUB_TOKEN
  ```

  之后每次推送 `v*` tag，CI 会自动：
  1. 构建发布
  2. 创建 GitHub Release
  3. 自动向 winget-pkgs 提交更新 PR
  4. 更新 PR 通常在 1 天内合并

## ⚠️ 注意事项

### Defender 误报

.NET 应用编译的安装包可能被 Windows Defender 标记。如验证流水线报 `Validation-Defender-Error`：

1. 到 [Microsoft Security Intelligence 提交](https://www.microsoft.com/en-us/wdsi/filesubmission) 申诉误报
2. 提交后重新触发 PR 验证（push 空 commit 或关/开 PR）

### 安装程序静默安装

Inno Setup 默认支持 `/SILENT` 和 `/VERYSILENT`。winget 使用 `/SILENT /SUPPRESSMSGBOXES /NORESTART /SP-` 参数。如果验证环境的 `Validation-Unattended-Failed` 失败，可能需要：

- 在 winget 清单中显式指定 `SilentSwitch` / `SilentWithExtractSwitch`
- 检查 Inno Setup 脚本中是否有阻塞静默安装的逻辑

### 管理员权限

`installer.iss` 设置 `PrivilegesRequired=admin`。Winget 支持提权安装，但：
- 验证环境默认以管理员身份运行
- 普通用户安装时 winget 会弹出 UAC 提示

这是正常行为，不影响通过验证。

### 首次 vs 后续提交

| | 首次 | 后续 |
|---|---|---|
| 工具 | `wingetcreate new`（交互式） | `wingetcreate update`（非交互式） |
| 位置 | 手动操作 | CI 自动执行 |
| 审核时间 | 1-5 个工作日 | 通常 <1 天 |
| CLA 签署 | 需要 | 已签署，自动通过 |

### 清单文件维护

首次生成的 3 个 YAML 文件建议保存到项目仓库（如 `docs/winget/` 目录），方便后续查证和手动修复 CI 失败。清单文件的内容随版本号变化（URL、SHA256），但模板结构和元数据字段保持不变。
