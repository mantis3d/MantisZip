# 多线程压缩方案 D：自适应 + 多线程（第四个选项）

> 状态: 📋 待定 | 阶段: [⬜⬜⬜⬜] (0/4)
> 前置: 自适应压缩已实现（`alpha` 分支 `e97010a`）

---

## 动机

当前 ZIP 压缩使用 SharpCompress `ZipWriter` 单线程写入，CPU 利用率低。
本方案将"多线程压缩"作为自适应压缩的**第四个选项**，让用户自主选择。

**量化预期**：
- 混合目录（代码 + 图片）：压缩速度提升 **2-4x**（图片 Store 不耗 CPU，文本走多线程）
- 纯文本目录：速度提升 **3-6x**（SharpSevenZip mt=on 多核压缩）
- 纯图片目录：无变化（全部 Store，本身瞬间完成）

---

## 设计

### 自适应压缩模式（四选一）

| 模式 | 行为 | 格式目录 | 用户规则 | 多线程提示 |
|------|------|---------|---------|-----------|
| 禁用 | 所有文件使用全局级别 | ❌ 隐藏 | ❌ 隐藏 | ❌ 隐藏 |
| 自适应（基础） | 已压缩格式 Store，其余全局级别 | ✅ 显示 | ✅ 显示 | ❌ 隐藏 |
| 自适应（智能） | 基础模式 + 魔数检测增强 | ✅ 显示 | ✅ 显示 | ❌ 隐藏 |
| 自适应 + 多线程 | 基础模式 + 需压缩文件 SharpSevenZip mt=on | ✅ 显示 | ❌ 隐藏 | ✅ 显示 |

**关键约束**：
- 用户规则对所有非 Disabled 模式生效（规则优先级最高）
- **多线程模式例外**：SharpSevenZip 不支持逐文件 CompressionLevel，用户规则无法执行，故隐藏规则区域
- 格式目录对所有自适应模式显示（让用户了解哪些格式走 Store）

### UI 设计

#### 标题区域

```
┌─────────────────────────────────────────────────────────┐
│  自适应压缩级别  [实验性]  [?]                            │
│  根据文件类型自动选择最佳压缩级别，提升压缩速度...          │
└─────────────────────────────────────────────────────────┘
```

AXAML 实现：

```xml
<!-- 标题行：文字 + 实验性标签 + 帮助按钮 -->
<Grid ColumnDefinitions="Auto,Auto,Auto,*">
  <TextBlock Grid.Column="0" Text="{Binding AdaptiveCompressionSectionText}"
             FontWeight="SemiBold" FontSize="14"
             Foreground="{DynamicResource ThemeTextPrimaryBrush}"
             VerticalAlignment="Center"/>
  <!-- 实验性标签 -->
  <Border Grid.Column="1" Background="#FF9800" CornerRadius="3"
          Padding="4,1" Margin="6,0,0,0" VerticalAlignment="Center">
    <TextBlock Text="实验性" FontSize="10" Foreground="White"
               FontWeight="SemiBold"/>
  </Border>
  <!-- 帮助按钮 -->
  <Button Grid.Column="2" Classes="ToolbarIcon"
          Content="?" FontSize="14" Width="20" Height="20"
          Margin="6,0,0,0" Click="OnAdaptiveHelpClick"
          ToolTip.Tip="自适应压缩级别介绍"/>
</Grid>
```

#### 模式选择（四选一 RadioButton）

```xml
<RadioButton Content="{Binding AdaptiveModeDisabledText}"
             IsChecked="{Binding AdaptiveModeDisabled}"
             GroupName="AdaptiveMode"/>
<RadioButton Content="{Binding AdaptiveModeStoreForCompressedText}"
             IsChecked="{Binding AdaptiveModeStoreForCompressed}"
             GroupName="AdaptiveMode"/>
<RadioButton Content="{Binding AdaptiveModeSmartDetectText}"
             IsChecked="{Binding AdaptiveModeSmartDetect}"
             GroupName="AdaptiveMode"/>
<RadioButton Content="{Binding AdaptiveModeMultiThreadedText}"
             IsChecked="{Binding AdaptiveModeMultiThreaded}"
             GroupName="AdaptiveMode"/>
```

#### 帮助弹窗内容

```
┌─── 自适应压缩级别介绍 ───────────────────┐
│                                          │
│  自适应压缩会根据文件类型自动选择最佳      │
│  压缩级别，避免对已压缩格式浪费 CPU。      │
│                                          │
│  📦 禁用                                 │
│  所有文件使用统一的压缩级别。              │
│                                          │
│  ⚡ 自适应（基础）                        │
│  已压缩格式（JPEG/PNG/MP4/ZIP 等）       │
│  自动 Store，其余使用全局级别。           │
│                                          │
│  🔍 自适应（智能）                        │
│  基础模式 + 支持用户自定义规则，           │
│  可为特定格式指定压缩级别。               │
│                                          │
│  🚀 自适应 + 多线程                       │
│  基础模式 + 需压缩文件多线程加速。         │
│  ⚠️ 用户规则在此模式下不生效。            │
│  ⚠️ 需压缩文件统一使用全局级别。           │
│                                          │
│  [确定]                                   │
└──────────────────────────────────────────┘
```

### 核心压缩流程（多线程模式）

```
文件列表
  ↓
AdaptiveRuleMatcher 分类（mode=Disabled，跳过用户规则）
  ├── Store 类文件（已压缩格式）
  │     → SharpCompress ZipWriter Store（直接复制，不压缩）
  └── 需压缩类文件（TXT/CS/XML/...）
        → SharpSevenZip mt=on（多线程压缩到临时 ZIP）
        → 合并到最终 ZIP
```

### 为什么多线程模式不执行用户规则？

SharpSevenZip 的 `CompressFilesEncrypted` / `CompressDirectory` 对整个压缩包应用**同一个** CompressionLevel，无法逐文件设置不同级别。

如果用户规则说"JPEG 用级别 6"，在多线程模式下：
- JPEG 不会被 Store（因为规则匹配到了非 Store 级别）
- SharpSevenZip 用全局级别压缩所有需压缩文件
- 用户规则被**静默忽略** → 造成困惑

因此多线程模式下隐藏用户规则区域，避免误导。

---

## 任务清单

### Phase 1: Enum 与 VM 扩展

- [ ] **1. AdaptiveCompressionMode 新增枚举值**
  - 文件: `Core/Models/AdaptiveOverrideRule.cs`
  - 新增: `MultiThreaded` 枚举值
  - 注释: `/// <summary>基础模式 + 需压缩文件多线程加速（用户规则不生效）。</summary>`

- [ ] **2. SettingsWindowViewModel 新增多线程模式属性**
  - 文件: `ViewModels/SettingsWindowViewModel.cs`
  - 新增:
    - `bool AdaptiveModeMultiThreaded`（RadioButton 绑定）
    - `string AdaptiveModeMultiThreadedText`（本地化文本）
    - `bool IsMultiThreadedMode`（计算属性，控制 UI 可见性）
  - 修改: `ApplyAdaptiveMode()` 方法处理新模式
  - 修改: `SaveAdaptiveModeToAppSettings()` 保存新模式

- [ ] **3. UI 可见性联动**
  - 文件: `ViewModels/SettingsWindowViewModel.cs`
  - 新增: `bool IsFormatCatalogVisible`（Disabled 时隐藏）
  - 新增: `bool IsUserRulesVisible`（Disabled 或 MultiThreaded 时隐藏）
  - 新增: `bool IsMultiThreadedHintVisible`（仅 MultiThreaded 时显示）
  - 绑定: AXAML 中各区域 `IsVisible="{Binding IsFormatCatalogVisible}"` 等

### Phase 2: UI AXAML 改造

- [ ] **4. SettingsWindow.axaml 标题区域**
  - 文件: `Views/SettingsWindow.axaml`
  - 改动: 自适应标题行改为 Grid 布局（文字 + [实验性] 标签 + [?] 帮助按钮）
  - 新增: 第四个 RadioButton（自适应 + 多线程）
  - 新增: 多线程提示 TextBlock（`IsVisible="{Binding IsMultiThreadedHintVisible}"`）
  - 修改: 格式目录区域 `IsVisible="{Binding IsFormatCatalogVisible}"`
  - 修改: 用户规则区域 `IsVisible="{Binding IsUserRulesVisible}"`

- [ ] **5. SettingsWindow.axaml.cs 帮助按钮事件**
  - 文件: `Views/SettingsWindow.axaml.cs`
  - 新增: `OnAdaptiveHelpClick` 事件处理
  - 弹窗: `AdaptiveHelpDialog` 或内联 `ContentDialog` 显示介绍内容

- [ ] **6. 新增 AdaptiveHelpDialog**
  - 文件: `Dialogs/AdaptiveHelpDialog.axaml` + `.cs`（新增）
  - 内容: 四个模式的介绍文案（与设计一致）
  - 样式: 复用现有对话框样式（`ThemeSurfaceBgBrush` 背景、`ThemeTextPrimaryBrush` 文字）
  - 按钮: 单个"确定"按钮关闭

### Phase 3: i18n

- [ ] **7. 本地化 key**
  - 文件: `strings.zh-CN.json`, `strings.en.json`
  - 新增:
    - `Adaptive_Mode_MultiThreaded`: "自适应 + 多线程" / "Adaptive + Multi-threaded"
    - `Adaptive_MultiThreaded_Hint`: "需压缩文件将统一使用全局压缩级别，并通过多线程加速" / "Files needing compression will use the global level with multi-threaded acceleration"
    - `Adaptive_MultiThreaded_Warning`: "用户规则在此模式下不生效" / "User rules are disabled in this mode"
    - `Adaptive_Help_Title`: "自适应压缩级别介绍" / "Adaptive Compression Level Introduction"
    - `Adaptive_Help_Disabled`: "所有文件使用统一的压缩级别" / "All files use the same compression level"
    - `Adaptive_Help_Basic`: "已压缩格式自动 Store，其余使用全局级别" / "Compressed formats auto-Store, others use global level"
    - `Adaptive_Help_Smart`: "基础模式 + 支持用户自定义规则" / "Basic mode + user-defined rules"
    - `Adaptive_Help_MultiThreaded`: "基础模式 + 需压缩文件多线程加速" / "Basic mode + multi-threaded acceleration for compressible files"
  - 注册到 `MainWindowViewModel.UpdateLocalizedStrings()` keys 数组

### Phase 4: ZipEngine 接入

- [ ] **8. ZipEngine 路径分流**
  - 文件: `Core/Engines/ZipEngine.cs`
  - 改动: 在非加密路径中，当 `AdaptiveCompressionMode == MultiThreaded` 时：
    1. 用 `ZipEntryClassifier.GetAdaptiveLevel()` 分类（mode=Disabled，跳过用户规则）
    2. `entryLevel == 0` → StoreGroup → ZipWriter Store
    3. 其余 → CompressGroup → `CompressGroupWithSevenZip()` + `MergeTempZipToWriter()`
  - 验证: `dotnet build` 无错误

- [ ] **9. 实现 CompressGroupWithSevenZip()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 参数: `List<(string FullPath, string RelativePath)> files, string tempPath, ArchiveOptions options`
  - 逻辑:
    1. 创建 `SharpSevenZipCompressor`
    2. `ArchiveFormat = OutArchiveFormat.Zip`
    3. `CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel)`
    4. `CustomParameters["mt"] = "on"`（启用多线程）
    5. 处理压缩方法选项
    6. `compr.CompressFiles(tempPath, filePaths)`

- [ ] **10. 实现 MergeTempZipToWriter()**
  - 文件: `Core/Engines/ZipEngine.cs`（新增私有方法）
  - 方法: `MergeTempZipToWriter(string tempPath, ZipWriter zipWriter)`
  - 逻辑:
    1. `ZipArchive.OpenArchive(tempPath)` 打开临时 ZIP
    2. 遍历 `archive.Entries`（跳过目录条目）
    3. 逐条 `entry.OpenEntryStream()` → `zipWriter.WriteToStream(entry.Key, options)`
    4. 完成后 `File.Delete(tempPath)`

### Phase 5: 测试与验证

- [ ] **11. 单元测试**
  - 文件: `tests/MantisZip.Tests/Engines/MultiThreadedCompressTests.cs`（新增）
  - 测试用例:
    - 纯 Store 文件：压缩结果正确（全部 Store，文件大小与原文件一致）
    - 纯需压缩文件：压缩结果正确（可解压，内容一致）
    - 混合文件：Store 类 Store + 需压缩类使用全局级别
    - 加密 ZIP：不走新路径，保持现有行为
    - 临时文件清理：合并后临时 ZIP 被删除
    - 异常路径：SharpSevenZip 压缩失败时回退到串行

- [ ] **12. 性能基准测试**
  - 场景 1: 100 个小文件（1MB × 100，混合 Store + 压缩）
  - 场景 2: 10 个中等文件（10MB × 10，纯文本）
  - 场景 3: 1000 个小文件（100KB × 1000，纯图片 → 全 Store）
  - 对比: 串行 vs 多线程，记录压缩时间和 CPU 利用率

---

## 改动范围

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `Core/Models/AdaptiveOverrideRule.cs` | 修改 | 新增 MultiThreaded 枚举值 |
| `Core/Engines/ZipEngine.cs` | 修改 + 新增 | 路径分流 + CompressGroupWithSevenZip + MergeTempZipToWriter |
| `ViewModels/SettingsWindowViewModel.cs` | 修改 | 新增多线程模式属性 + UI 可见性联动 |
| `Views/SettingsWindow.axaml` | 修改 | 标题区域 + 第四个选项 + 可见性绑定 |
| `Views/SettingsWindow.axaml.cs` | 修改 | 帮助按钮事件 |
| `Dialogs/AdaptiveHelpDialog.axaml` + `.cs` | 新增 | 帮助弹窗 |
| `UI/Localization/strings.zh-CN.json` | 新增 | 8 个 key |
| `UI/Localization/strings.en.json` | 新增 | 8 个 key |
| `UI/ViewModels/MainWindowViewModel.cs` | 修改 | UpdateLocalizedStrings 注册新 key |
| `tests/MantisZip.Tests/.../MultiThreadedCompressTests.cs` | 新增 | 单元测试 |

---

## 风险与对策

| 风险 | 等级 | 对策 |
|------|------|------|
| 临时 ZIP 磁盘空间不足 | 🟡 中 | 检查可用空间；大文件集回退串行 |
| SharpSevenZip mt 对小文件效果有限 | 🟢 低 | 小文件 Store 类不走此路径；需压缩类文件数多时仍有收益 |
| 临时文件未清理 | 🟢 低 | finally 块确保删除；启动时扫描清理残留 |
| 加密 ZIP 不兼容 | 🟢 低 | 加密路径不走新逻辑，保持现有 SharpSevenZip 调用 |
| 用户规则在多线程模式下被忽略 | 🟢 低 | 隐藏规则区域 + 警告文案 |

---

## Definition of Done

- [ ] 自适应压缩模式新增"自适应 + 多线程"选项
- [ ] 切换模式时 UI 区域正确联动（格式目录/用户规则/多线程提示）
- [ ] 标题显示"实验性"标签 + 帮助按钮
- [ ] 帮助弹窗显示四个模式介绍
- [ ] 多线程模式下用户规则区域隐藏
- [ ] ZIP 非加密压缩支持 Store + 多线程两条路径
- [ ] 加密 ZIP 行为不变
- [ ] 单元测试全部通过
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` 全部通过
