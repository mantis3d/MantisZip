# 自包含体积优化（Avalonia 迁移后）

## TL;DR

> **Quick Summary**: 当前 WPF + .NET 9 自包含安装包 ~50 MB。迁移到 Avalonia 后叠加修剪（trimming）和全球化精简，目标降至 **~20–25 MB**。本计划在 Avalonia 迁移完成后执行，按渐进式三步走，每步可独立发布验证。
>
> **Deliverables**:
> - 三步配置变更，逐步激进
> - 每步有明确的回滚方式
> - 预估最终安装包体积：**20–25 MB**
>
> **Estimated Effort**: Medium（~4–6h，含测试验证）
> **Parallel Execution**: NO（严格顺序，每步验证通过才进下一步）
> **Critical Path**: InvariantGlobalization → Trimming(partial) → Trimming(full)

---

## Context

### Original Request
> "自包含能不能再小一些？还是说只能是这么大？"

### Discussion Summary
当前 WPF 自包含 ~50 MB 的原因是：
- .NET 运行时核心（coreclr/hostfxr/hostpolicy）~15–20 MB
- WPF 框架程序集 ~10–12 MB
- 基类库 ~10–15 MB
- 应用 + NuGet 依赖 ~5–8 MB
- PDB 调试符号 ~3–5 MB

WPF → Avalonia 迁移后的影响：
- 去掉 WPF 框架（-10~12 MB），加上 Avalonia + SkiaSharp（+6~8 MB），**净省 ~5 MB**
- **真正的质变**：Avalonia 天生对 trimming 友好（WPF 深度依赖反射，trimmable 很差）
- Avalonia + PublishTrimmed + InvariantGlobalization 可将体积降至 **~20–25 MB**

### Research Findings
- .NET 9 的 `PublishTrimmed` 支持 `TrimMode=partial`（保守）和 `TrimMode=full`（激进）
- WPF 在 trimming 下容易损坏，因为 PresentationFramework 大量使用反射
- Avalonia 从设计上支持 trimming/AOT，但仍需验证第三方库（SharpCompress、SharpSevenZip）的兼容性
- `InvariantGlobalization=true` 可移除 ICU 数据文件（~10 MB），但影响全球化排序/格式
- `PublishReadyToRun` 会增大体积（预生成本地代码），不应使用
- `PublishSingleFile` 会解压到临时目录再运行，对基于 COM 的 SharpSevenZip 有兼容风险

---

## Work Objectives

### Core Objective
把 Avalonia 迁移后的自包含安装包从 ~45 MB 降至 ~20–25 MB，同时确保所有功能正常。

### Concrete Deliverables
- 本方案文档（✅ 已完成）
- 三步逐步提交的 csproj/release.yml 配置变更
- 每步的测试报告

### Definition of Done
- [ ] 三步配置全部生效且 CI 通过
- [ ] 所有预览功能正常（详见 Must Have 中列出的功能）
- [ ] ShellExt COM 功能正常
- [ ] 压缩/解压功能正常（ZIP/7z/TarGz）
- [ ] 安装包体积降至 ~20–25 MB

### Must Have
每步验证的功能清单：
- 基础操作：打开压缩包、浏览条目列表、筛选搜索
- 压缩：ZIP（含密码）、7z、TarGz
- 解压：各格式、智能解压、单项解压
- 预览：文本、图片、PDF、PE、字体、音频、SQLite、Office、ISO、Torrent、SVG、视频
- 拖拽导出
- ShellExt COM 右键菜单（`--install-shell` 后）
- WebView2 预览（HTML/Markdown/PDF）

### Must NOT Have (Guardrails)
- 不加 `PublishReadyToRun`（增大体积）
- 不加 `PublishSingleFile`（SharpSevenZip COM 兼容风险）
- 不改 framework-dependent 发布配置
- 不改现有 installer.iss（框架依赖安装包）

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES（CI + 测试项目）
- **Automated tests**: 部分（40+ xUnit 单元测试，不含 SevenZipEngine）
- **Agent-Executed QA**:
  1. CI 构建验证（编译 + 单元测试通过）
  2. 发布产物体积比对
  3. 发布产物文件清单完整性检查
  4. 本地运行冒烟测试（启动 + 打开一个 zip + 预览一个文件）

### QA Policy
- 每步在本地 `dotnet publish` 验证后，对比发布目录大小
- 检查 `publish_output_selfcontained` 中的文件清单，确认关键 DLL 未被修剪掉
- 运行 `MantisZip.UI.exe --open test.zip` 验证基本功能

---

## Execution Strategy

### Three waves (strictly sequential)

```
Wave 1: InvariantGlobalization (安全无副作用)
  └─ Wave 2: TrimMode=partial (保守修剪)
       └─ Wave 3: TrimMode=full (激进修剪)
```

### Wave 1 — 全球化精简（安全层）

**目标**：去掉 ICU 数据文件，预计节省 ~10 MB

**修改内容**：在 `MantisZip.UI.csproj` 中添加：

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <!-- 自包含发布不携带 ICU 数据，改用操作系统 NLS -->
  <InvariantGlobalization>true</InvariantGlobalization>
  <!-- 移除全球化排序代码路径 -->
  <InvariantGlobalizationPrecise>true</InvariantGlobalizationPrecise>
</PropertyGroup>
```

**为什么用 Condition**：只在 `--self-contained`（有 RID）时生效，framework-dependent 发布不受影响。

**验证**：
1. 打开压缩包后按文件名排序是否正常？→ 使用 ordinal 排序不受影响
2. 中英文界面切换是否正常？→ 语言资源不受 `InvariantGlobalization` 影响
3. 日期格式显示是否正常？→ 会使用固定格式（InvariantCulture），需确认 UI 中日期的显示可接受

**风险**：极低。仅影响排序规则和日期/数字格式，不修剪任何代码。

**回滚方式**：删除或注释 `InvariantGlobalization` 行。

**预估效果**：
| 项目 | 当前（WPF） | Avalonia 迁移后（无修剪） | Wave 1 后 |
|------|------------|-------------------------|-----------|
| 安装包体积 | ~50 MB | ~42–45 MB | ~32–35 MB |

### Wave 2 — 保守修剪（TrimMode=partial）

**目标**：启用修剪但采用保守模式，预计再节省 ~5–8 MB

**修改内容**：在 `MantisZip.UI.csproj` 添加：

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>
  <!-- 防止误删反射调用的程序集 -->
  <TrimmerRootAssembly Include="SharpCompress" />
  <TrimmerRootAssembly Include="SharpSevenZip" />
  <TrimmerRootAssembly Include="Microsoft.Data.Sqlite" />
  <TrimmerRootAssembly Include="Markdig" />
</PropertyGroup>
```

**`TrimMode=partial` 的含义**：
- 只修剪未标记为 `IsTrimmable=false` 的程序集
- 框架内部程序集大部分已标记为 trimmable
- 第三方库通常不标记，所以修剪效果有限但更安全

**验证重点（必须全部手动测试）**：
1. 打开 ZIP 文件 → 列表显示正常
2. 打开 7z 文件 → 列表 + 密码弹窗正常
3. 各格式预览（文本/图片/PDF/PE/字体/音频/SQLite/Office/ISO/Torrent/SVG/视频）
4. 压缩/解压（含密码）
5. ShellExt COM 已正确复制到输出目录
6. 拖拽导出

**已知兼容性**：
- SharpCompress 0.48.1：需要 `TrimmerRootAssembly` 排除，否则 `ZipArchive` 反射会失败
- SharpSevenZip 2.0.45：P/Invoke 方式调用 7z.dll，修剪不影响
- CommunityToolkit.Mvvm 8.4.2：用源生成器，修剪安全
- Microsoft.Data.Sqlite 10.0.8：ADO.NET 反射，需排除
- Markdig 1.2.0：需排除

**预估效果**：
| 项目 | Wave 1 后 | Wave 2 后 |
|------|-----------|-----------|
| 安装包体积 | ~32–35 MB | ~25–28 MB |

### Wave 3 — 激进修剪（TrimMode=full）

**目标**：全部修剪，预计再节省 ~3–5 MB

**修改内容**：将 `TrimMode` 改为 `full`，并添加必要的 `DynamicDependency` 或 `UnconditionalSuppressMessage`

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>full</TrimMode>
</PropertyGroup>
```

或者不设置 `TrimMode`（默认就相当于 `full`），因为 .NET 8+ 的默认已经是 trim all assemblies。

**风险**：高。`full` 模式会修剪所有引用程序集（包括第三方库），可能破坏反射调用的代码。

**需要做的事**：
1. 运行 `dotnet publish` 检查修剪警告
2. 对产生 IL2110/IL2111/IL2112 等修剪警告的第三方库添加 `TrimmerRootAssembly`
3. 如果效果不佳（大量功能损坏），回退到 Wave 2

**预估效果**：
| 项目 | Wave 2 后 | Wave 3 后 |
|------|-----------|-----------|
| 安装包体积 | ~25–28 MB | ~20–25 MB |

### 关于 PDB（调试符号）

当前 release.yml 中自包含发布带了 `DebugType=portable` + `DebugSymbols=true`。PDB 文件总计 ~3–5 MB，可用于崩溃诊断。

建议：
- 可以在 installer 中去掉 PDB 的 Source 行（用户不需要）
- 或者保留但通过 Inno Setup 的 `Flags: ignoreversion` 安装但不影响体积对比
- 如果从安装包中去掉 PDB，可额外节省 ~3–5 MB

但这与修剪无关，是独立可做的优化。

---

## Relation to existing plans

本计划 **不能在前端进行 Avalonia 迁移完成前执行**，它假设：
- `MantisZip.UI` 已从 WPF 完全迁移到 Avalonia
- `TargetFramework` 已改为纯 `net9.0`（而非 `net9.0-windows*`）
- `UseWPF` 已移除
- 所有 WPF 特定依赖（PresentationFramework 等）已移除
- WpfAnimatedGif、Emoji.Wpf 等 WPF 专用包已替换

否则 `PublishTrimmed` 会严重破坏 WPF 的反射功能。

### 依赖关系图

```
┌─────────────────────┐
│ Avalonia 迁移       │ (外部计划：cross-platform-port.md)
└────────┬────────────┘
         ▼
┌─────────────────────┐
│ 本计划 Wave 1        │ InvariantGlobalization
└────────┬────────────┘
         ▼
┌─────────────────────┐
│ 本计划 Wave 2        │ TrimMode=partial
└────────┬────────────┘
         ▼
┌─────────────────────┐
│ 本计划 Wave 3        │ TrimMode=full
└─────────────────────┘
```

---

## TODOs

### Wave 1: InvariantGlobalization

**What to do**:
1. 在 `MantisZip.UI.csproj` 中添加条件属性（仅 RID 不为空时生效）
2. 本地运行 `dotnet publish -r win-x64 --self-contained -c Release -o publish_size_test`
3. 对比 `publish_size_test` 文件夹大小与 baseline
4. 运行 `publish_size_test\MantisZip.UI.exe` 确认基本功能
5. 提交：`feat: enable InvariantGlobalization for self-contained builds`

**Files to modify**: `src/MantisZip.UI/MantisZip.UI.csproj`

**Acceptance Criteria**:
- [ ] csproj 中新增 `InvariantGlobalization=true` 条件属性
- [ ] 自包含发布产物不再包含 `icudt*.dat` 或类似 ICU 文件
- [ ] 应用启动功能正常
- [ ] 框架依赖发布不受影响（building without RID 不应有 InvariantGlobalization）

### Wave 2: TrimMode=partial

**What to do**:
1. 在 `MantisZip.UI.csproj` 中的 RID 条件块添加 `PublishTrimmed=true` + `TrimMode=partial`
2. 添加 `TrimmerRootAssembly` 排除 SharpCompress、SharpSevenZip、Microsoft.Data.Sqlite、Markdig
3. 本地 publish + 冒烟测试
4. 提交：`feat: enable partial trimming for self-contained builds`

**Files to modify**: `src/MantisZip.UI/MantisZip.UI.csproj`

**Acceptance Criteria**:
- [ ] csproj 中新增 `PublishTrimmed=true` + `TrimMode=partial`
- [ ] 必要的 `TrimmerRootAssembly` 已添加
- [ ] 自包含发布后产物大小有下降
- [ ] 所有 Must Have 功能测试通过

### Wave 3: TrimMode=full

**What to do**:
1. 将 `TrimMode` 改为 `full`（或不显式设置）
2. 运行 `dotnet publish` 检查修剪警告
3. 根据警告逐个解决反射兼容问题
4. 完整手动测试所有功能
5. 提交：`feat: enable full trimming for self-contained builds`

**Files to modify**: `src/MantisZip.UI/MantisZip.UI.csproj`

**Acceptance Criteria**:
- [ ] `TrimMode=full` 生效
- [ ] 所有修剪警告已评估，必要的 `TrimmerRootAssembly` 已添加
- [ ] 所有 Must Have 功能测试通过
- [ ] 如无法全部通过，回退到 Wave 2 并记录原因

### （可选）去掉 PDB 减小安装包

**What to do**:
- 修改 `release.yml` 中自包含发布步骤的 `-p:DebugSymbols=false`
- 或修改 `installer-selfcontained.iss` 去掉 `*.pdb` 的 Source 行

**Files to modify**: `.github/workflows/release.yml` 和/或 `installer-selfcontained.iss`

---

## Rollback Strategy

| Wave | 回滚方式 | 影响范围 |
|------|---------|---------|
| Wave 1 | 删除 InvariantGlobalization 行 | 仅自包含发布 |
| Wave 2 | 删除 PublishTrimmed + TrimMode 行 | 仅自包含发布 |
| Wave 3 | 改回 TrimMode=partial 或删除 | 仅自包含发布 |

所有修改仅在 `Condition="'$(RuntimeIdentifier)' != ''"` 条件下生效，framework-dependent 发布完全不受影响，因此回滚也仅限于自包含场景。

---

## Commit Strategy

- **Wave 1**: `feat: enable InvariantGlobalization for self-contained builds`
- **Wave 2**: `feat: enable partial trimming for self-contained builds`
- **Wave 3**: `feat: enable full trimming for self-contained builds`
- **可选**: `chore: remove PDB from self-contained installer`
