# 紧凑度模式 (Compactness Mode)

## 背景

Avalonia Fluent 主题控件默认尺寸比 .NET 9 WPF 原生控件大一圈，导致原 WPF 设计合理的窗口迁移到 Avalonia 后间距过大、整体松散。需要引入用户可调节的「紧凑度」设置。

## 目标

- **P0**: 间距资源体系 + 3 档切换（紧凑/正常/松散）+ XAML 硬编码值替换
- **P1**: 运行时切换即时生效，无需重启
- **P2**: 设置窗口 UI（外观 Tab 内新增紧凑度下拉框）

## 设计

### 间距资源体系

定义 3 组间距资源，以 `DynamicResource` 引用。所有 `Margin`/`Padding`/`Spacing`/`MinHeight`/`Height` 引用这些资源而非硬编码。

| 资源键 | 紧凑 | 正常 | 松散 | 适用场景 |
|--------|------|------|------|---------|
| `SpacingXxs` | 2 | 4 | 6 | 图标与文字间距、极小间隙 |
| `SpacingXs` | 4 | 8 | 12 | 控件内部 Padding、小间距 |
| `SpacingSm` | 8 | 12 | 16 | 控件之间 Spacing、GroupBox 内边距 |
| `SpacingMd` | 12 | 16 | 24 | 容器 Padding、对话框边距 |
| `SpacingLg` | 16 | 24 | 32 | 区域间距、Section 间隔 |
| `SpacingXl` | 24 | 32 | 48 | 页面级大间距 |
| `ControlHeightSm` | 22 | 26 | 30 | 小控件（按钮/输入框） |
| `ControlHeightMd` | 28 | 32 | 38 | 中控件（工具栏按钮） |
| `ControlHeightLg` | 42 | 48 | 54 | 大控件（Tab 标题高度） |
| `ControlMinHeight` | 32 | 40 | 48 | ListBoxItem 等条目最小高度 |
| `BorderRadius` | 4 | 6 | 8 | `CornerRadius` |
| `DialogPadding` | 12 | 16 | 24 | 对话框窗口 Padding |

### 资源定义位置

- `Themes/ThemeLight.axaml` — 亮色主题 3 组资源
- `Themes/ThemeDark.axaml` — 暗色主题 3 组资源（与亮色共用间距值，仅颜色不同）
- `App.axaml.cs` — 运行时切换方法 `ApplyCompactness()`

### 运行时切换机制

```csharp
public enum CompactnessMode { Compact, Normal, Loose }

void ApplyCompactness(CompactnessMode mode) {
    var theme = _currentThemeIsLight ? ThemeLight : ThemeDark;
    var resources = mode switch {
        CompactnessMode.Compact => CompactResources,
        CompactnessMode.Normal => NormalResources,
        CompactnessMode.Loose => LooseResources,
    };
    // 移除旧资源键，添加新资源值
    foreach (var (key, value) in resources)
        theme[key] = value;
}
```

间距值在 `double` 层面相同，亮暗只需定义一组间距色值即可。`AppSettings.CompactnessMode` 持久化，启动时应用。

## Phase 划分

### Phase 1: 框架搭建 (~30min)

1. `AppSettings` 新增 `CompactnessMode` 属性（默认 `Normal`）
2. `CompactnessMode.cs` 枚举文件
3. 资源定义：`ThemeLight.axaml` / `ThemeDark.axaml` 3 套间距资源（以 **不同资源键** 承载，如 `SpacingXsCompact`/`SpacingXs`/`SpacingXsLoose`，或运行时动态覆盖同一键）
4. `ApplyCompactness()` 方法 + 启动调用
5. 设置窗口「外观」Tab 新增紧凑度 ComboBox（中文：紧凑/正常/松散）
6. 切换时调用 `ApplyCompactness()` 即时生效

### Phase 2: App.axaml 全局样式替换 (~30min)

替换 `App.axaml` 中全局样式内的硬编码值：

| 样式 | 属性 | 硬编码 → 资源 |
|------|------|-------------|
| ToolbarButton | Height="54" | `{DynamicResource ControlHeightLg}` |
| Button | MinHeight="26" | `{DynamicResource ControlHeightSm}` |
| Button | Padding="8,4" | 分解引用 |
| Dialog | Padding="16" | `{DynamicResource DialogPadding}` |
| Dialog 标题字号 | FontSize | 保持不动 |
| GroupBox | Margin/Padding | 对应 Spacing 资源 |

### Phase 3: 视图/对话框文件替换 (~90min)

逐个文件替换硬编码值。按文件分组批量操作：

1. `MainWindow.axaml` (~59 Spacing + 18 Margin + 6 Padding)
2. `SettingsWindow.axaml` (~48 Spacing + 36 Margin + 44 Padding)
3. `PreviewPanel.axaml` (~14 Spacing + 1 Margin + 2 Padding)
4. `CompressSettingsWindow.axaml` (~14 Spacing + 4 Margin + 13 Padding)
5. `AboutWindow.axaml` (~1 Spacing + 6 Margin + 45 Padding)
6. `PasswordHelpDialog.axaml` (~9 Spacing + 1 Margin + 6 Padding)
7. 其余 28 个文件（各 1-9 处）

对于常见模式，用 `ast_grep_replace` 批量替换，如：
```
Margin="4" → Margin="{DynamicResource SpacingXs}"
Margin="8" → Margin="{DynamicResource SpacingSm}"
Padding="4" → Padding="{DynamicResource SpacingXs}"
Padding="8" → Padding="{DynamicResource SpacingSm}"
Spacing="4" → Spacing="{DynamicResource SpacingXs}"
Spacing="8" → Spacing="{DynamicResource SpacingSm}"
```

不规则值（`Margin="4 8 12 16"` 等）逐处手动审查替换。

### Phase 4: 验证 (~15min)

1. 构建检查 0 错误
2. 视觉核验：紧凑/正常/松散 三档切换，主要窗口无明显布局断裂
3. `DynamicResource` 引用错误检查（运行时 ResourceNotFound 异常）

## 关键约束

- 间距值是跨主题一致的（亮暗共用），不能有亮暗差异
- 切换即时生效通过 `DynamicResource` 实现，不能用 `StaticResource`
- 保持 `.axaml` 与 `.cs` 文件分离，不在 code-behind 中写硬编码间距
- 按钮 `MinHeight` 如 `22`/`26`/`28` 统一为 `ControlHeightSm`，不复用无关高度
- 带方向的 Margin（如 `Margin="4 0 0 0"`）需要评估是否也应替换：如果仅为消除相邻元素间隙则替换为 `SpacingXxs`，如果有语义含义则保留或改用更精确的资源

## 风险

- **DynamicResource 性能**：频繁切换时会触发所有引用控件的重新测量。Button/TextBox 等有 Transition 动画的控件可能出现闪烁。解决方案：切换时暂挂 Transition，或接受短暂重排。
- **`CornerRadius` 非 DynamicResource 兼容**：Avalonia 12 中部分控件的 `CornerRadius` 不支持 `DynamicResource` 绑定（已知限制）。需 fallback 到 StaticResource + 页面刷新。
- **文件太多容易遗漏**：建议写完自动替换后逐文件 diff 审查。
