# 暗色主题 & 强调色自定义

> 两阶段实施：Phase 1 亮/暗切换（P1）+ Phase 2 强调色选择（P3）
> **状态**: ✅ Phase 1 已完成（v0.2.9）| 📋 Phase 2 待定 | **阶段**: [✅⬜] (1/2)
> 创建日期：2026-05-19

---

## 动机

当前 MantisZip 所有 XAML 颜色均为硬编码（`#F5F5F5` 背景、`#333` 文字、`#CCC` 边框……），没有主题抽象层。夜间使用刺眼，且代码难以维护。

**Phase 1 目标**：亮色/暗色一键切换，消除"晚上刺眼"的刚需。
**Phase 2 目标**：在亮暗基础上提供强调色自定义（预设色板），增加个性化空间。

---

## 改前状态（代码现状确认）

| 维度 | 现状 |
|------|------|
| XAML 文件数 | 14 个（App.xaml 无 Resources） |
| 硬编码颜色值 | ~40+ 个（#F5F5F5, #333, #CCC, #2196F3, #BBDEFB, #1565C0 ...） |
| 主题基础设施 | 无（App.xaml Resources 为空） |
| ResourceDictionary | 无 |
| 运行时切换 | 无机制 |
| 代码层面颜色 | ProgressWindow 等 .cs 文件中也有颜色赋值 |
| AssemblyInfo | 有 `ThemeInfo` 属性，目前 `ResourceDictionaryLocation.None` |

---

## 架构设计

### 总览：语义化颜色体系（Phase 1 + Phase 2 共用）

将所有硬编码颜色提炼为约 20 个语义化资源键。这些键在 Light.xaml 和 Dark.xaml 中分别定义值。

```
Theme_WindowBg            # 窗口/主背景
Theme_SurfaceBg           # 卡片/面板/输入框背景
Theme_MenuBg              # 菜单栏背景
Theme_HeaderBg            # 表头/标题栏背景

Theme_TextPrimary         # 主要文字
Theme_TextSecondary       # 次要文字/说明
Theme_TextDisabled        # 禁用文字
Theme_TextOnAccent        # 强调色上的文字（通常是白色）

Theme_Border              # 边框
Theme_BorderLight         # 浅边框/分隔线
Theme_BorderFocused       # 聚焦态边框

Theme_Accent              # 强调色（Phase 2 用户可选）
Theme_AccentLight         # 强调色淡版（选中行背景等）
Theme_AccentDark          # 强调色深版（边框高亮等）

Theme_SelectionBg         # 选中项背景（失焦）
Theme_SelectionText       # 选中项文字

Theme_ButtonBg            # 按钮背景
Theme_ButtonHover         # 按钮悬停
Theme_ButtonBorder        # 按钮边框

Theme_ProgressBg          # 进度条背景
Theme_ProgressFill        # 进度条填充

Theme_StatusSuccess       # 成功色
Theme_StatusWarning       # 警告色
Theme_StatusError         # 错误色

Theme_ScrollBarBg         # 滚动条背景
Theme_ScrollBarThumb      # 滚动条滑块

Theme_ToolTipBg           # 工具提示背景
Theme_ToolTipText         # 工具提示文字
```

### Phase 1：亮/暗切换机制 [✅✅✅✅✅] (5/5)

- [x] **1.1** 语义化颜色体系（~20 个 Theme_* 资源键）
- [x] **1.2** 亮色/暗色 ResourceDictionary（Light.xaml / Dark.xaml）
- [x] **1.3** 运行时切换机制（App.SwitchTheme / ApplyTheme）
- [x] **1.4** 所有 XAML 文件硬编码颜色替换为 Theme_* 引用（17 个 XAML + .cs）
- [x] **1.5** 设置窗口添加主题切换 UI + AppSettings 持久化

```csharp
// 核心切换代码（App.xaml.cs 新增方法）
public void ApplyTheme(string themeName)  // "Light" | "Dark"
{
    var dict = new ResourceDictionary
    {
        Source = new Uri($"Themes/{themeName}.xaml", UriKind.Relative)
    };

    // 替换 Application 级别的主题字典
    var existing = Application.Current.Resources.MergedDictionaries
        .FirstOrDefault(d => d.Source?.OriginalString?.StartsWith("Themes/") == true);

    if (existing != null)
        Application.Current.Resources.MergedDictionaries.Remove(existing);

    Application.Current.Resources.MergedDictionaries.Add(dict);
}
```

**原理**：WPF 的 `MergedDictionaries` 替换。所有 XAML 中用 `{StaticResource Theme_WindowBg}` 引用，字典替换后 StaticResource 在下一帧自动更新（WPF 在 ResourceDictionary 层级变动时触发重新解析）。

### Phase 2：强调色切换

Phase 1 完成后，强调色键（`Theme_Accent` / `Theme_AccentLight` / `Theme_AccentDark`）改为动态资源：

```xml
<!-- 原来（Phase 1） -->
<Border Background="{StaticResource Theme_Accent}"/>

<!-- Phase 2 改为 -->
<Border Background="{DynamicResource Theme_Accent}"/>
```

用户选中色板颜色后，动态覆盖：
```csharp
Application.Current.Resources["Theme_Accent"] = new SolidColorBrush(selectedColor);
```

只有这 3 个键需要 DynamicResource，其余仍用 StaticResource。

---

## 改动清单

### Phase 1 文件改动（P1，共 3-4h）[✅✅✅✅✅✅✅✅✅✅] (10/10)

- [x] `Themes/Light.xaml` — 新建亮色主题 ResourceDictionary
- [x] `Themes/Dark.xaml` — 新建暗色主题 ResourceDictionary
- [x] `App.xaml` — 合并 Light.xaml 到 MergedDictionaries
- [x] `App.xaml.cs` — 新增 `ApplyTheme()` + 启动恢复
- [x] `AppSettings.cs` — 新增 `Theme` 属性
- [x] `SettingsWindow.xaml` — 外观标签页加主题下拉框
- [x] `SettingsWindow.xaml.cs` — 主题切换事件处理
- [x] 所有 XAML 文件（17 个）— 硬编码颜色替换为 `{StaticResource Theme_XXX}`
- [x] 受影响的 .cs 文件 — 程序化颜色替换
- [x] 验证 — 全局走查每个窗口亮暗截图对比

| 文件 | 改动内容 | 预估 |
|------|----------|:----:|
| `Themes/Light.xaml` | 🆕 新建 — 亮色主题 ResourceDictionary（约 20 个 SolidColorBrush） | 15min ✅ |
| `Themes/Dark.xaml` | 🆕 新建 — 暗色主题 ResourceDictionary（对应 20 个颜色暗色值） | 15min ✅ |
| `App.xaml` | 合并 Light.xaml 到 `MergedDictionaries` | 5min ✅ |
| `App.xaml.cs` | 新增 `ApplyTheme(string themeName)` + `_OnStartup` 恢复 `AppSettings.Theme` | 15min ✅ |
| `AppSettings.cs` | 新增 `Theme` 属性（"Light"/"Dark"，默认"Light"） | 5min ✅ |
| `SettingsWindow.xaml` | 外观标签页加「主题」下拉框 | 10min ✅ |
| `SettingsWindow.xaml.cs` | 主题切换事件处理 | 10min ✅ |
| **所有 17 个 XAML 文件** | 逐文件提取硬编码颜色 → 替换为 `{StaticResource Theme_XXX}` | 2-2.5h ✅ |
| **受影响的 .cs 文件** | ProgressWindow / MainWindow 等中用到的程序化颜色 | 15min ✅ |
| `MainWindow.xaml` | `Background="#F5F5F5"` → `Background="{StaticResource Theme_WindowBg}"` | — ✅ |
| 验证 | 全局走查：每个窗口亮暗都截屏检查对比度 | 10min |

**小计：3-4h**

### Phase 2 文件改动（P3，共 1-2h）[⬜⬜⬜⬜⬜⬜⬜] (0/7)

- [ ] `Themes/Light.xaml` / `Dark.xaml` — 强调色键改为 DynamicResource
- [ ] `AppSettings.cs` — 新增 `AccentColor` 属性
- [ ] `SettingsWindow.xaml` — 外观标签页加强调色色板选择 UI
- [ ] `SettingsWindow.xaml.cs` — 色板点击事件
- [ ] `App.xaml.cs` — 新增 `ApplyAccentColor(Color)`
- [ ] XAML 中 3-4 处 `StaticResource` → `DynamicResource`
- [ ] 验证 — 每种强调色切换后检查一致性

| 文件 | 改动内容 | 预估 |
|------|----------|:----:|
| `Themes/Light.xaml` | 强调色键（3 个）从静态值改为备注默认值 | 5min |
| `Themes/Dark.xaml` | 同上 | 5min |
| `AppSettings.cs` | 新增 `AccentColor` 属性（string，如 `"#2196F3"`） | 5min |
| `SettingsWindow.xaml` | 外观标签页加「强调色」色板选择 UI（5-8 色方块 Grid） | 30min |
| `SettingsWindow.xaml.cs` | 色板点击事件 → `ApplyAccentColor()` | 10min |
| `App.xaml.cs` | 新增 `ApplyAccentColor(Color)` 方法 | 10min |
| 涉及强调色的 XAML | `StaticResource Theme_Accent` → `DynamicResource Theme_Accent`（约 3-4 处） | 15min |
| 验证 | 每种强调色切换后检查所有窗口一致性 | 10min |

**小计：1-2h**

---

## 必须做的全部 XAML 颜色替换清单

### 1. `MainWindow.xaml`
- `Background="#F5F5F5"` → `Theme_WindowBg`
- Menu `Background="White"` → `Theme_MenuBg`
- TreeView / ListView 背景色
- StatusBar 背景

### 2. `AppMessageBox.xaml`
- `Background="#F5F5F5"` → `Theme_WindowBg`
- `Foreground="#333"` → `Theme_TextPrimary`
- `Background="White"` (buttons) → `Theme_ButtonBg`
- `BorderBrush="#CCC"` → `Theme_Border`

### 3. `CompressConflictDialog.xaml`
- 同上模式

### 4. `CompressSettingsWindow.xaml`
- `Background="#F5F5F5"` → `Theme_WindowBg`

### 5. `ConflictDialog.xaml`
- `Background="#F5F5F5"` → `Theme_WindowBg`
- `Foreground="#333"` → `Theme_TextPrimary`
- `Foreground="#555"` → `Theme_TextSecondary`
- `Foreground="#2196F3"` → `Theme_Accent`
- `Background="#FFE082"` → `Theme_StatusWarning`
- `BorderBrush="#DDD"` → `Theme_BorderLight`

### 6. `ErrorDialog.xaml`
### 7. `LogPrivacyHelpDialog.xaml`
### 8. `PasswordDialog.xaml`
### 9. `PasswordEditDialog.xaml`
### 10. `PasswordHelpDialog.xaml`
### 11. `PasswordManagerWindow.xaml`
### 12. `ProgressWindow.xaml`
### 13. `SettingsWindow.xaml`
(所有以上文件：`#F5F5F5` 背景 → `Theme_WindowBg`，按钮白色 → `Theme_ButtonBg`，边框 → `Theme_Border`，字体颜色对应替换)

### 14. `MainWindow.Resources` 中的系统颜色覆盖
- `SystemColors.InactiveSelectionHighlightBrushKey` → 改用 Theme key

---

## 验证策略

| 阶段 | 验证项 | 方法 |
|:----:|--------|------|
| P1 | 所有窗口亮色主题：与改前视觉一致 | 对照截图逐窗口检查，注意颜色偏移 |
| P1 | 切换暗色后所有窗口都变暗 | 手动切换，检查 14 个窗口是否有遗漏亮色角落 |
| P1 | 暗色模式下文字对比度 ≥ 4.5:1 | 抽样检查（白字在深灰背景上） |
| P1 | 设置中的主题选择持久化 | 重启应用后主题保持 |
| P2 | 切换强调色后强调色区域正确更新 | 检查按钮、链接、选中状态、进度条 |
| P2 | 强调色切换不破坏亮/暗底色 | 暗色 + 红色强调 / 暗色 + 蓝色强调 等组合 |

---

## 不作（Anti-Scope）

以下明确**不在本计划范围内**：

- ❌ 用户自定义任意颜色（ColorPicker 控件）
- ❌ 跟随系统主题自动切换（Windows light/dark mode 检测）
- ❌ 暗色 Mode 下的壁纸/背景图支持
- ❌ 为每个窗口独立设置主题
- ❌ 主题动画过渡效果

---

## 实施顺序

```
Phase 1
  ┣━ ① 建 Themes/Light.xaml + Themes/Dark.xaml（定义 20 个颜色键）
  ┣━ ② App.xaml + App.xaml.cs 初始化 + ApplyTheme()
  ┣━ ③ AppSettings + SettingsWindow UI（下拉框）
  ┣━ ④ 逐一修改 14 个 XAML 文件（最耗时，建议逐文件提交）
  ┣━ ⑤ 处理 .cs 代码层面的颜色
  ┗━ ⑥ 验证 → 修复对比度问题

Phase 2（Phase 1 完成后很久再执行）
  ┣━ ① 3 个强调色键改为 DynamicResource
  ┣━ ② SettingsWindow 色板 UI
  ┣━ ③ AppSettings.AccentColor + 运行时 ApplyAccentColor()
  ┗━ ④ 验证组合
```

## Momus 审查

提交此计划前应进行 Momus 审查以确保：

- 每个 XAML 文件的颜色键映射表是否完整
- 暗色值设计是否满足无障碍对比度要求
- 是否有遗漏的程序化颜色赋值
- 强调色在两种底色下都具备足够的对比度

---

## Definition of Done

- [ ] Phase 1：亮/暗主题切换功能完成
- [ ] 所有 14 个 XAML 文件的硬编码颜色替换为 Theme_* 引用
- [ ] .cs 代码中的程序化颜色已替换
- [ ] 亮/暗两套主题均可正常切换显示
- [ ] `AppSettings.Theme` 持久化并在启动时恢复
- [ ] Phase 2（可选）：强调色自定义完成
- [ ] `dotnet build` 通过，无颜色相关警告

### Final Checklist

- [ ] 亮色主题下所有窗口显示正常
- [ ] 暗色主题下所有窗口显示正常
- [ ] 主题切换无需重启
- [ ] 设置窗口可切换主题
- [ ] 重启后主题保持
- [ ] 强调色可自定义（Phase 2）
