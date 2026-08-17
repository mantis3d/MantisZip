# MSI 安装包 — WiX 方案

> 将 MantisZip 的安装包从 Inno Setup EXE 迁移为 WiX MSI。
> **状态**: 📋 待定 | **任务**: [⬜⬜⬜⬜] (0/4)
> 创建日期：2026-05-18

## 动机

当前 `installer.iss` 输出 EXE 安装包，但 MSI 有以下优势：

| 需求 | Inno Setup EXE | WiX MSI |
|------|---------------|---------|
| 企业组策略分发 (GPO) | ❌ | ✅ |
| 静默安装 `/quiet` / `/norestart` | 通过 `/VERYSILENT` | ✅ 原生 MSI 标准 |
| 标准 Windows 修复/卸载入口 | 部分支持 | ✅ 完整 |
| 安装日志标准化 | 自定义日志 | ✅ `msiexec /log` |
| 被 SCCM/Intune 管理 | ❌ | ✅ |

## 任务清单

- [ ] **1. WiX 环境搭建** — 安装 WiX Toolset v5，验证 `wix --version`
- [ ] **2. `installer.wxs` 编写** — Package + Product + Feature 描述
- [ ] **3. 构建与验证** — `wix build` 生成 MSI，验证安装/卸载
- [ ] **4. CI/CD 集成** — 构建脚本生成 MSI 作为发布工件

## 安装

### 1. 安装 WiX Toolset v5

```powershell
dotnet tool install --global wix
```

WiX v5 是 .NET 工具，无需 VS 扩展或 SDK 安装。

### 2. 验证

```powershell
wix --version
```

## 项目结构

```
MantisZip/
├── installer.wxs          # 主安装描述
├── installer/
│   └── MantisZip-*.msi    # 构建输出（已 gitignore）
├── src/
│   └── MantisZip.UI/
│       └── Resources/
│           └── Icons/
│               └── app.ico    # 应用图标
```

单个 `.wxs` 文件即可，复杂度适中。如果后续需多语言/多功能，可拆分为：

```
installer.wxs      # Package + Product + 主 Feature
Components.wxs     # heat 自动收集的文件组件（可选）
```

## installer.wxs 结构

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="MantisZip"
           Version="0.2.7"
           Manufacturer="MantisZip Contributors"
           UpgradeCode="PUT-GUID-HERE"
           Scope="perMachine">
    
    <MajorUpgrade DowngradeErrorMessage="!(loc.DowngradeError)" />

    <!-- 安装目录 -->
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLDIR" Name="MantisZip" />
    </StandardDirectory>

    <!-- 功能定义 -->
    <Feature Id="MainFeature" Title="MantisZip" Level="1">
      <ComponentGroupRef Id="MainFiles" />
      <!-- Registry 条目将通过组件引用 -->
    </Feature>

    <!-- 快捷方式 -->
    <Component Directory="INSTALLDIR">
      <Shortcut Id="StartMenuShortcut"
                Directory="ProgramMenuFolder"
                Name="MantisZip"
                Target="[#MantisZip.UI.exe]"
                WorkingDirectory="INSTALLDIR" />
      <Shortcut Id="DesktopShortcut"
                Directory="DesktopFolder"
                Name="MantisZip"
                Target="[#MantisZip.UI.exe]"
                WorkingDirectory="INSTALLDIR" />
      <RemoveFolder Id="RemoveProgramMenuDir" Directory="ProgramMenuFolder" />
      <RegistryValue Root="HKCU" Key="Software\[Manufacturer]\[ProductName]"
                     Name="installed" Type="integer" Value="1" />
    </Component>
  </Package>
</Wix>
```

## 发布+打包命令

```powershell
# 1. 发布
dotnet publish src\MantisZip.UI\MantisZip.UI.csproj `
  -c Release -o publish_output `
  --self-contained false

# 2. 运行 heat 自动收集文件（可选）
# wix heat dir publish_output -gg -sfrag -cg MainFiles `
#   -dr INSTALLDIR -out Components.wxs

# 3. 编译 .wxs → .wixobj
wix build installer.wxs -out installer\MantisZip-0.2.7.msi

# 或一步完成（推荐）：
wix build installer.wxs -arch x64 -out installer\MantisZip-0.2.7.msi
```

## 需要处理的关键点

### 1. Shell 右键菜单注册

MantisZip 在首次启动时通过 `ShellIntegration.Install()` 注册右键菜单（HKCU 路径）。两种策略：

**策略 A（推荐）**：安装包不写注册表，让应用首次启动时自行注册。
- 优点：保持逻辑集中，安装包不重复实现 Shell 注册
- 缺点：首次启动前右键菜单不可见

**策略 B**：在 `.wxs` 中用 Registry 元素写入 Shell 扩展。但 MantisZip 的 ShellIntegration 支持双模式（层叠/独立动词）且可切换，在 MSI 里静态声明无法覆盖所有动态配置。

**结论**：采用策略 A，安装包只部署文件，Shell 集成在 `App.OnStartup` 中首次自动触发。

### 2. 文件关联

同上，由应用在首次启动时调用 `ShellIntegration.InstallAssociations()`。

### 3. .NET 运行时依赖

当前使用 `--self-contained false`（依赖系统已安装的 .NET 9 Runtime）。如需离线安装：

```powershell
dotnet publish -c Release -o publish_output --self-contained true
```

但 MSI 体积会增大 ~50MB（含 runtime）。建议使用 **bootstrapper** 方案（如 WiX Bundle）让 MSI 自动下载 .NET Runtime。

### 4. 图标

Inno Setup 中的 Setup icon 来自编译时指定的图标文件。WiX 中同样可以在 Package 上指定 Icon：

```xml
<Icon Id="app.ico" SourceFile="src/MantisZip.UI/Resources/Icons/app.ico" />
<Property Id="ARPPRODUCTICON" Value="app.ico" />
```

应用图标需要准备一个标准 `.ico` 文件（含 16×16、32×32、48×48 等多尺寸）。

### 5. 静默安装

```powershell
msiexec /i MantisZip-0.2.7.msi /quiet /norestart
msiexec /x MantisZip-0.2.7.msi /quiet /norestart
msiexec /i MantisZip-0.2.7.msi /log install.log
```

## 可选增强：WiX Bundle（自动装 .NET）

创建一个 `.wixproj` Bundle 项目，用 `ExePackage` 引导 .NET 9 Runtime 安装：

```xml
<Bundle Name="MantisZip" Version="0.2.7" ...>
  <Chain>
    <PackageGroupRef Id="Net9Runtime" />
    <MsiPackage SourceFile="installer\MantisZip-0.2.7.msi" />
  </Chain>
</Bundle>
```

Bundle 产物是 `.exe`，内部嵌 MSI，但企业部署仍可用 MSI 部分。

## 迁移步骤

## COM 组件（v0.3.7 新增）

MantisZip.ShellExt 包含两个关键文件需纳入 MSI：

| 文件 | 说明 |
|------|------|
| `MantisZip.ShellExt.dll` | COM 类库（含 `ContextMenuHandler` COM 类） |
| `MantisZip.ShellExt.comhost.dll` | .NET 9 生成的 COM host DLL（注册入口） |

MSI 需：
1. 将上述两个文件部署到 `INSTALLDIR`
2. 安装时写入 HKCU CLSID `{C90B2A1E-5E4F-4A7A-9B0F-8C1D3E5F7A9B}` 指向 `MantisZip.ShellExt.comhost.dll`
3. 卸载时清除该 CLSID 及 shellex 子键

或者沿用现有策略：MSI 只部署文件，由应用首次启动时调用 `ShellIntegration.Install()`（会自动执行 `InstallCom()` + 回退静态注册）。

| 步骤 | 内容 | 工作量 |
|------|------|--------|
| 1 | 准备 `app.ico` 应用图标（多尺寸） | 低（~10min） |
| 2 | 编写 `installer.wxs`（含组件/快捷方式/注册 + ShellExt 组件） | 中（~2h） |
| 3 | 测试：安装/卸载/升级/静默 | 中（~1h） |
| 4 | 替换 `installer.iss`，移除 Inno Setup 依赖 | 低 |
| 5 | CI 集成（GitHub Actions 自动构建 MSI） | 低（~30min） |
| 6 | [可选] WiX Bundle + .NET 运行时引导 | 中（~1h） |

## 与现有 Inno Setup 对比

| 对比项 | Inno Setup (当前) | WiX MSI |
|--------|------------------|---------|
| 脚本格式 | Pascal 脚本 | XML |
| 产出格式 | EXE | MSI |
| 安装体验 | 有界面向导 | 有界面向导 |
| 静默安装 | `/VERYSILENT` | `/quiet` |
| 企业分发 | ❌ | ✅ |
| 学习曲线 | 低 | 中 |
| 社区生态 | 活跃 | 活跃（Microsoft 维护） |
| 与 .NET 9 兼容性 | ✅ 无问题 | ✅ 无问题 |

---

## Definition of Done

- [ ] WiX Toolset v5 安装并验证通过
- [ ] `installer.wxs` 编写完成，生成有效 MSI
- [ ] MSI 安装/卸载/修复功能正常
- [ ] 静默安装 (`msiexec /quiet`) 测试通过
- [ ] 桌面快捷方式、开始菜单、文件关联正确
- [ ] 与现有 Inno Setup 安装不冲突（不同 UpgradeCode）
- [ ] CI/CD 构建脚本生成 MSI 工件

### Final Checklist

- [ ] `wix build` 生成 MSI 无错误
- [ ] MSI 安装后应用正常运行
- [ ] 卸载完全清理
- [ ] 静默安装/卸载测试通过
- [ ] Inno Setup 安装包保留为备选方案
