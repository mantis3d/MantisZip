# Avalonia: i18n 补齐 + 杂物清理

> **Status**: 📋 Planned | **Target**: v0.4.5
> **分支**: `avalonia-port`

## 概述

将 Avalonia 项目的国际化字符串补齐到与 WPF 一致，同时修复版本号、空资源目录、包版本不一致等零散问题。

## 1. i18n 补齐

### 现状

| 指标 | WPF | Avalonia | 差距 |
|------|-----|---------|------|
| i18n keys 总数 | 805 | 515 | **-290 (36%)** |
| 文件位置 | `Resources/strings.en.json` | `Localization/strings.en.json` | 文件名不同 |
| 中文文件名 | `Resources/strings.zh.json` | `Localization/strings.zh-CN.json` | 格式不同 |
| languages.json | 存在 | ❌ 缺失 | |

### 缺失 key 分组（按前缀）

| 分组 | 缺失数量 | 说明 |
|------|---------|------|
| `App_*` | ~45 | 应用级消息（启动、压缩、解压、shell） |
| `Settings_*` | ~35 | 设置窗口文本 |
| `Preview_*` | ~30 | 预览相关的 UI 文本 |
| `Compress_*` | ~25 | 压缩对话框文本 |
| `Dialog_*` | ~20 | 对话框通用文本 |
| `Shell_*` | ~15 | Shell 菜单文本 |
| `Progress_*` | ~15 | 进度窗口文本 |
| `About_*` | ~15 | 关于窗口文本 |
| `Password_*` | ~10 | 密码管理器文本 |
| 其他 | ~80 | 分散在各功能模块 |

### 文件变更清单

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | 新增 ~290 个 key-value |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | 新增 ~290 个 key-value |
| `src/MantisZip.UI.Avalonia/Resources/languages.json` | **新建** — 语言列表（从 WPF 复制） |

### 移植规则

1. **直接复制**: 纯 UI 文本 key（`Dialog_*`, `Settings_*`, `Preview_*` 等）直接从 WPF JSON 复制
2. **需要调整的 key**:
   - `App_*` 中引用 WPF 特定概念的（如 `App_Description: ".NET 9 + WPF"` → 改为 `.NET 9 + Avalonia`）
   - `About_*` 中显示技术栈的描述
3. **文件名统一**:
   - WPF: `strings.zh.json` → Avalonia: `strings.zh-CN.json`（这是更标准的格式，保持不变）
   - 但需确保 `LocalizationManager.cs` 正确加载该文件

### 任务分解

- [ ] 从 WPF `Resources/strings.en.json` 提取 WPF 独有 key（排除已存在的 515 个）
- [ ] 逐一检查每个 key 是否需要修改（Avalonia 特定差异）
- [ ] 添加到 `Localization/strings.en.json`
- [ ] 同步添加到 `Localization/strings.zh-CN.json`，中文值从 WPF `strings.zh.json` 中提取
- [ ] 从 WPF 复制 `Resources/languages.json` 到 Avalonia 的 `Resources/`
- [ ] 验证本地化正确加载

## 2. 版本号同步

### 现状

| 文件 | WPF | Avalonia |
|------|-----|---------|
| `AppConstants.cs` | `0.4.4` | `0.4.0` |
| `.csproj` | `<Version>0.4.4</Version>` | 无 `<Version>` |

### 变更

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/AppConstants.cs` | `Version` → `0.4.4` |
| `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | 添加 `<Version>0.4.4</Version>` |

### 任务分解

- [ ] 更新 `AppConstants.cs` 版本号
- [ ] 在 csproj 添加 `<Version>` 属性

## 3. Avalonia.Diagnostics 版本对齐

### 现状

```xml
<!-- WPF 项目（无 Avalonia） -->
<!-- Avalonia 项目 -->
<PackageReference Condition="'$(Configuration)' == 'Debug'" Include="Avalonia.Diagnostics" Version="11.3.17" />
```

Avalonia 主包为 `12.0.4`，但 Diagnostics 包为 `11.3.17` — 版本不匹配。

### 变更

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj` | `Avalonia.Diagnostics` → `12.0.4` |

### 任务分解

- [ ] 检查 `Avalonia.Diagnostics` 12.x 的 API 兼容性
- [ ] 更新版本号

## 4. 空 Icons 目录

### 现状

`src/MantisZip.UI.Avalonia/Resources/Icons/` 目录存在但为空。

### 变更

从 WPF 复制图标文件（或确认是否需要）。注意：WPF 项目有 `Resources/Icons/`（文件类型图标）和 `Resources/MenuIcons/`（右键菜单图标）。Icons/ 由 `SystemIconHelper` 在运行时从系统获取，所以空目录可能是正常的。但 MenuIcons/ 在 Plan 1 中处理。

### 任务分解

- [ ] 确认 Icons/ 是否确实需要静态图标文件
- [ ] 如果不需要，保留空目录或添加 `.gitkeep`

## 5. DonateQr.jpg 缺失

### 现状

WPF: `Resources/DonateQr.jpg` + `Resources/DonateQr.png`
Avalonia: 只有 `DonateQr.png`

### 变更

| 文件 | 变更 |
|------|------|
| `src/MantisZip.UI.Avalonia/Resources/DonateQr.jpg` | 从 WPF 复制 |

### 任务分解

- [ ] 复制 DonateQr.jpg

## 验证标准

- [ ] `strings.en.json` 和 `strings.zh-CN.json` 的 key 数量与 WPF 一致
- [ ] 所有 UI 文本正确显示
- [ ] `languages.json` 正常工作
- [ ] `AppConstants.Version` = `0.4.4`
- [ ] `dotnet build` 无警告
