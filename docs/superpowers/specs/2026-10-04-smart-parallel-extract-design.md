# 智能并行解压线程数设计

**日期**: 2026-10-04  
**状态**: 已确认  
**需求来源**: HDD/SSD 并行解压基准测试（scripts/bench-extract-parallel.cs）

---

## 背景

实测发现（16 核 CPU，SSD + HDD 混合环境）：

| 磁盘类型 | 最佳线程数 | 相对串行加速比 |
|---------|-----------|---------------|
| SSD | 12 | ~2.4x |
| HDD | 12 | ~2.4x |
| HDD (degree=16) | 12 | 劣化 3-6% |

当前默认值 `Environment.ProcessorCount`（如 16）在所有场景下都不是最优。

---

## 目标

1. 根据目标磁盘类型自动选择合理的并行度默认值
2. 对大压缩包，通过快速预测试找到实际最优线程数
3. 用户可自定义或关闭智能功能

---

## 设计

### 1. 设置项

新增四个设置（AppSettings）：

| 设置键 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `EnableParallelExtract` | bool | true | **并行解压全局开关**：关闭 = 始终串行（线程数=1） |
| `SmartParallelDegree` | bool | true | 智能并行度开关（仅并行开启时生效） |
| `SmartSsdDegree` | int | 12 | 智能模式下 SSD 的线程数（1-16，用户可调） |
| `SmartHddDegree` | int | 8 | 智能模式下 HDD 的线程数（1-16，用户可调） |
| `LargeArchivePreTest` | bool | true | 大包预测试开关（仅智能模式开启时生效与显示；智能关闭时 UI 隐藏该选项，因预测试只在决策链第 4 步参与） |

现有的 `ParallelExtractDegree`（int，0=自动）保持不变，作为智能关闭后的手动覆盖。

### 2. 线程数决策逻辑

```
输入: archivePath, options.ParallelExtractDegree, AppSettings

1. options.ParallelExtractDegree > 0？
   → 直接使用（调用方明确指定，最高优先级，用于测试/特殊场景）

2. AppSettings.EnableParallelExtract = false？
   → 返回 1（串行，全局开关关闭）

3. AppSettings.SmartParallelDegree = false？
   → 若 AppSettings.ParallelExtractDegree > 0：用手动设置值
   → 否则：Environment.ProcessorCount（现有行为）

4. 智能模式开启：archiveSize ≥ 1GB（压缩包文件本身大小）
   且 AppSettings.LargeArchivePreTest = true？
   → 调用 ParallelDegreeOptimizer.FindOptimalDegreeAsync()

5. 智能模式开启且不满足预测试条件：
   → DiskTypeDetector.GetDiskType（优先取目标目录 destinationPath 所在磁盘，
     因解压是写目标盘，写盘通常是瓶颈；目标目录不可用时回退 archivePath）
   → Ssd: AppSettings.SmartSsdDegree（默认 12）
   → Hdd: AppSettings.SmartHddDegree（默认 8）
   → Unknown: min(ProcessorCount, 12)

注意：SmartParallelDegree = true 时，AppSettings.ParallelExtractDegree
不参与决策（UI 中该输入框仅在智能关闭时显示，隐藏期间其残留值被忽略）。
EnableParallelExtract = false 时，其余并行设置全部隐藏（规则 6）。
SmartParallelDegree = false 时，LargeArchivePreTest 选项隐藏（规则 6）——
预测试仅在决策链第 4 步（智能链路）参与，智能关闭时开关无意义。
```

### 3. 磁盘类型检测

新增 `MantisZip.Core/Utils/DiskTypeDetector.cs`：

```csharp
public enum DiskType { Ssd, Hdd, Unknown }

public static class DiskTypeDetector
{
    /// <summary>根据路径检测所在磁盘类型。</summary>
    public static DiskType GetDiskType(string path);
}
```

**检测方法**（Windows）：
1. 提取盘符（如 `E:\` → `E:`）
2. WMI 查询 `Win32_DiskDrive` 的 `MediaType` 属性
3. 或通过 `StorageDevice` API 检查 `SeekPenalty`
4. 失败时返回 `Unknown`

**注意**：检测结果可缓存（磁盘类型不会变），按盘符缓存到 `ConcurrentDictionary<char, DiskType>`。

### 4. 大包预测试

新增 `MantisZip.Core/Services/ParallelDegreeOptimizer.cs`：

```csharp
public static class ParallelDegreeOptimizer
{
    /// <summary>
    /// 快速测试找出最优并行度。
    /// 在目标目录临时解压小样本，对比不同线程数速度。
    /// </summary>
    public static async Task<int> FindOptimalDegreeAsync(
        string archivePath,
        string destinationPath,
        string? password,
        CancellationToken ct);
}
```

**测试流程**：

1. **选择测试配置**：根据 `Environment.ProcessorCount` 动态生成
   - 核数 ≥ 16: `[4, 8, 12, 16]`
   - 核数 8-15: `[2, 4, 8, ProcessorCount]`
   - 核数 < 8: `[1, 2, 4, ProcessorCount]`
   - 去重、排序、限制 4 个

2. **选取测试样本**：
   - 列出压缩包所有文件条目（跳过目录）
   - 按压缩包内条目顺序（Local Header Offset 顺序）依次累加文件大小
   - 累计达到约 50MB 时停止（不精确截断，以最后一个完整文件为准）
   - 至少选 5 个文件（避免单文件大包主导结果）
   - 最多选 50 个文件（避免文件数过多影响测试）

3. **逐配置测试**：
   ```
   foreach degree in testConfigs:
       tempDir = destinationPath\__parallel_test_{degree}
       解压样本到 tempDir（计时）
       删除 tempDir
   ```

4. **选择最优**：取耗时最短的配置

5. **清理**：确保所有临时目录已删除

**预计耗时**：HDD ~2秒 / SSD ~0.5秒

**注意事项**：
- 测试失败（异常/取消）时回退到磁盘类型默认值
- 测试文件写入目标目录（用户已确认），测完立即删除
- 预测试期间通过现有进度通道显示本地化文案 `Status_TestingParallelDegree`（"正在测试最优线程数..."），不显示百分比
- 1GB 阈值定义为 `ParallelDegreeOptimizer` 内的 `const long LargeArchiveThreshold = 1L * 1024 * 1024 * 1024`（暂不做成设置项，YAGNI）

### 5. UI 变更

**SettingsWindow（解压设置 Tab）**：

```xml
<!-- 并行解压区域（整体包在一个 Border 卡片内） -->
<StackPanel>
    <!-- 全局开关：关闭 = 始终串行，下方全部隐藏（规则 6） -->
    <CheckBox IsChecked="{Binding EnableParallelExtract}" />

    <StackPanel IsVisible="{Binding EnableParallelExtract}" Spacing="8">
        <!-- 智能并行度开关 + SSD/HDD 两个线程数输入（仅智能开启时显示） -->
        <CheckBox IsChecked="{Binding SmartParallelDegree}" />
        <StackPanel IsVisible="{Binding SmartParallelDegree}"
                    Orientation="Horizontal" Spacing="8">
            <TextBlock Text="{Binding SmartSsdLabel}" VerticalAlignment="Center" />
            <NumericUpDown Value="{Binding SmartSsdDegree}" Minimum="1" Maximum="16" Width="100" />
            <TextBlock Text="{Binding SmartHddLabel}" VerticalAlignment="Center" />
            <NumericUpDown Value="{Binding SmartHddDegree}" Minimum="1" Maximum="16" Width="100" />
        </StackPanel>

        <!-- 手动设置（仅关闭智能时显示） -->
        <StackPanel IsVisible="{Binding !SmartParallelDegree}"
                    Orientation="Horizontal" Spacing="8">
            <TextBlock Text="{Binding ManualDegreeLabel}" VerticalAlignment="Center" />
            <NumericUpDown Value="{Binding ParallelExtractDegree}" Minimum="1" Maximum="16" Width="100" />
        </StackPanel>

        <!-- 大包预测试开关（仅智能开启时显示——预测试只在决策链第 4 步参与） -->
        <CheckBox IsChecked="{Binding LargeArchivePreTest}"
                  IsVisible="{Binding SmartParallelDegree}" />

        <!-- 帮助按钮 -->
        <Button Command="{Binding ShowParallelHelpCommand}" />
    </StackPanel>
</StackPanel>
```

**帮助弹窗**：新增 `ParallelExtractHelpDialog`

### 6. 本地化

新增 key（三语成对）：

| Key | zh-CN | en-US | zh-TW |
|-----|-------|-------|-------|
| `Settings_EnableParallelExtract` | 并行解压（关闭则始终串行解压） | Parallel extract (off = always serial) | 並行解壓（關閉則始終串行解壓） |
| `Settings_SmartParallelDegree` | 智能并行度 | Smart Parallel Degree | 智能並行度 |
| `Settings_SmartSsdLabel` | SSD 线程数: | SSD threads: | SSD 執行緒數: |
| `Settings_SmartHddLabel` | HDD 线程数: | HDD threads: | HDD 執行緒數: |
| `Settings_ManualDegreeLabel` | 线程数: | Threads: | 執行緒數: |
| `Settings_LargeArchivePreTest` | 大包预测试（≥1GB 时自动测试最优线程数） | Large archive pre-test (auto-test optimal threads for ≥1GB) | 大包預測試（≥1GB 時自動測試最優執行緒數） |
| `ParallelHelp_Title` | 并行解压说明 | Parallel Extract Help | 並行解壓說明 |
| `ParallelHelp_EnableSection` | 并行解压允许同时用多个线程解压不同文件。关闭后始终串行解压（最兼容，速度最慢）。 | Parallel extract uses multiple threads to extract different files at once. Disable for always-serial extraction (most compatible, slowest). | 並行解壓允許同時用多個執行緒解壓不同檔案。關閉後始終串行解壓（最相容，速度最慢）。 |
| `ParallelHelp_SmartSection` | 智能并行度根据目标磁盘类型选择线程数：SSD 用上方 SSD 线程数（默认 12），HDD 用 HDD 线程数（默认 8），未知磁盘取 CPU 核数与 12 的较小值。关闭智能后可设固定线程数。 | Smart mode picks thread count by disk type: SSD uses the SSD thread count (default 12), HDD uses the HDD thread count (default 8), unknown disks use min(CPU cores, 12). Disable smart to set a fixed count. | 智能並行度根據目標磁碟類型選擇執行緒數：SSD 用上方 SSD 執行緒數（預設 12），HDD 用 HDD 執行緒數（預設 8），未知磁碟取 CPU 核心數與 12 的較小值。關閉智能後可設固定執行緒數。 |
| `ParallelHelp_PreTestSection` | 大包预测试在解压 ≥1GB 压缩包时，先用 1-2 秒在目标目录测试不同线程数的速度（临时解压约 50MB 数据后删除），选择最优配置再正式解压。关闭可节省这 1-2 秒。 | Large archive pre-test: for ≥1GB archives, briefly tests different thread counts (~50MB temp extraction, then deleted) to find the optimal configuration before full extraction. Disable to save 1-2 seconds. | 大包預測試在解壓 ≥1GB 壓縮包時，先用 1-2 秒在目標目錄測試不同執行緒數的速度（臨時解壓約 50MB 資料後刪除），選擇最優配置再正式解壓。關閉可節省這 1-2 秒。 |
| `Status_TestingParallelDegree` | 正在测试最优线程数... | Testing optimal thread count... | 正在測試最優執行緒數... |

### 7. 向后兼容

- `EnableParallelExtract` 默认 `true`：老用户升级后保持并行；想要串行的用户手动关闭
- `SmartParallelDegree` / `LargeArchivePreTest` 默认 `true`：新用户获得优化，老用户升级后行为变化（可接受）
- `SmartSsdDegree=12` / `SmartHddDegree=8` 为经基准测试验证的默认值，用户可调
- 优先级：`options.ParallelExtractDegree`（调用方）> 全局开关（关=串行）> 智能模式 > 用户手动设置 > `ProcessorCount`
- 老用户已手动设置 `ParallelExtractDegree > 0`：升级后若保留手动值，需先关闭智能模式才生效；UI 中手动输入框仅在智能关闭时显示，避免困惑
- 测试项目直接传 `ArchiveOptions.ParallelExtractDegree`，不受全局开关与智能模式影响（决策链第 1 步短路）

---

## 文件变更清单

### 新增

| 文件 | 说明 |
|------|------|
| `src/MantisZip.Core/Utils/DiskTypeDetector.cs` | 磁盘类型检测 |
| `src/MantisZip.Core/Services/ParallelDegreeOptimizer.cs` | 预测试优化器 |
| `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml` | 帮助弹窗 UI |
| `src/MantisZip.UI.Avalonia/Dialogs/ParallelExtractHelpDialog.axaml.cs` | 帮助弹窗代码 |

### 修改

| 文件 | 修改内容 |
|------|---------|
| `src/MantisZip.UI.Avalonia/Models/AppSettings.cs` | 新增 5 个属性 |
| `src/MantisZip.Core/Engines/ZipEngine.cs` | ExtractAsync 调用优化器 |
| `src/MantisZip.UI.Avalonia/ViewModels/SettingsWindowViewModel.cs` | 新增属性和命令 |
| `src/MantisZip.UI.Avalonia/Dialogs/SettingsWindow.axaml` | 新增 UI 控件 |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-CN.json` | 新增 key |
| `src/MantisZip.UI.Avalonia/Localization/strings.en.json` | 新增 key |
| `src/MantisZip.UI.Avalonia/Localization/strings.zh-TW.json` | 新增 key |

### 测试

| 文件 | 说明 |
|------|------|
| `tests/MantisZip.Tests/Utils/DiskTypeDetectorTests.cs` | 磁盘检测单元测试 |
| `tests/MantisZip.Tests/Services/ParallelDegreeOptimizerTests.cs` | 优化器单元测试 |

---

## 边界情况

1. **磁盘检测失败** → 返回 Unknown → 使用 `min(ProcessorCount, 12)`
2. **预测试异常** → 捕获后回退到磁盘类型默认值
3. **压缩包 < 1GB** → 跳过预测试，用磁盘类型默认值
4. **压缩包损坏无法列目录** → 跳过预测试，用磁盘类型默认值
5. **用户取消解压** → 预测试应响应 CancellationToken
6. **目标目录无写入权限** → 预测试失败，回退默认值
7. **全局开关关闭** → 强制线程数=1（串行），其余并行设置全部隐藏且不参与决策
8. **SmartSsdDegree/SmartHddDegree 越界** → UI NumericUpDown 已限制 1-16；加载设置时 clamp 到 [1,16]

---

## 性能影响

- 磁盘检测：首次 ~10ms，后续缓存 <1ms
- 预测试：HDD ~2秒 / SSD ~0.5秒（仅大包触发）
- 内存：额外 ~10KB（缓存字典）
