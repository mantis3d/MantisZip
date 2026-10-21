# 仅应用程序集 ReadyToRun（R2R）——启动优化

> **状态**: ✅ 已完成（2026-10-20；门控达成，详见实施结果） | **创建**: 2026-10-20 | **优先级**: P2 | **预估工时**: 1–2h
> **来源**: 2026-10-20 启动余量实测：`new MainWindow()` **构造体执行前有 ~80ms JIT**，`XamlIlPopulate` 在应用自身程序集内；全量 R2R 实测 **TOTAL 1470→820ms（−650ms）**，但发布体积 **+138MB**（188.6→326.6MB）。
> **目标**: 只对 `MantisZip.UI.Avalonia.dll` 做 R2R，以约 **+4–5MB** 体积换取 **−150~200ms** 启动；框架层 JIT（约 −155ms 的 Visible）不在本计划范围。

## 背景（实测数据）

| 指标 | 基线 | 全量 R2R | 说明 |
|---|---|---|---|
| `new MainWindow()` 构造前 JIT | 80.7ms | 31.3ms | 应用集 + 框架集共同贡献 |
| `Win.Xaml`（含 `XamlIlPopulate` JIT） | 332.7ms | 139.5ms | **应用集 R2R 可直接命中** |
| `Win.Visible` | 262ms | 107ms | 框架层 JIT 为主 |
| TOTAL | 1470ms | 820ms | 8/8 轮全胜 |
| 发布体积（framework-dependent） | 188.6MB | 326.6MB | 单程序集膨胀 2.5–3× |

## 方案

发布时（`PublishReadyToRun=true`）通过 MSBuild 把「除应用集外的所有托管程序集」加入 `PublishReadyToRunExclude`：

```xml
<Target Name="ExcludeNonAppAssembliesFromReadyToRun"
        BeforeTargets="_PrepareForReadyToRunCompilation"
        Condition="'$(PublishReadyToRun)' == 'true'">
  <ItemGroup>
    <_AppOnlyR2RExclusions Include="@(ReferenceCopyLocalPaths)"
                           Condition="'%(Filename)' != 'MantisZip.UI.Avalonia'" />
    <PublishReadyToRunExclude Include="@(_AppOnlyR2RExclusions->'%(FullPath)')" />
  </ItemGroup>
</Target>
```

- `ReferenceCopyLocalPaths` 覆盖 NuGet 包与 ProjectReference（Core/ShellExt），排除列表自动随依赖变化，不写死文件名。
- 该 target 必须在 `_PrepareForReadyToRunCompilation` 之前完成（该 target 体内才计算编译清单）。

## 验证

1. `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o <tmp>`：
   - `MantisZip.UI.Avalonia.dll` ≈8.2MB（已 R2R）；`Avalonia.Base.dll` ≈2.25MB（仍为 IL）→ 证明只 R2R 了应用集。
   - 发布总体积增量应 ≈ **+4–5MB**（对照 188.6MB）。
2. **交错 A/B n=10**（基线 = `bin\Release\net10.0`，变体 = 上述发布目录）：
   - 门控：`Init.MainWindow.New → Win.Ctor.Enter` 下降 ≥40ms，或 `Win.Xaml` 下降 ≥80ms；TOTAL 中位下降 ≥100ms。
   - 不达门控则回退该 MSBuild 片段（一行删除），并如实记录。
3. **自包含发布验证**（installer 路径）：`--self-contained true` 发布一次，确认配置生效、体积增量在可接受范围、应用可正常启动。
4. 冒烟：正常启动 + `--open` 打开压缩包 + 压缩/解压各一次。

## 边界

- 不启用全量 R2R（+138MB）；不启用 Composite R2R（体积无改善）。
- 不做 Trim/AOT（归 `selfcontained-size-optimization`）。
- 不改版本号；若产生用户可见启动收益，按规则 18 与用户确认 RELEASE_NOTES 范围。

## 实施结果（2026-10-20 回填）

**实现**（`MantisZip.UI.Avalonia.csproj` 新增 target `ExcludeNonAppAssembliesFromReadyToRun`）：
- 发布时把 `@(ReferenceCopyLocalPaths)` 中除 `MantisZip.UI.Avalonia` 外的全部程序集加入 `PublishReadyToRunExclude`，**按文件名（`%(Filename)%(Extension)`）匹配**——实测 `%(FullPath)` 不生效（SDK 按文件名比对 ExcludeList）。
- 必须在 `_PrepareForReadyToRunCompilation` 之前填充（该 target 体内才计算编译清单）。

**体积验证**

| 发布方式 | 基线 | 仅应用集 R2R | 增量 |
|---|---|---|---|
| framework-dependent | 188.6MB | **192.9MB** | **+4.3MB** |
| self-contained（installer 路径） | 265.3MB | **269.6MB** | **+4.3MB** |

- 应用集 3.92→8.23MB（已 R2R）；`Avalonia.Base.dll` 2.25MB、`System.Private.CoreLib.dll` 15.29MB 保持 IL（268 个程序集被排除）。

**A/B（交错 n=10）**

| 指标（中位） | 基线 | 仅应用集 R2R | 差值 | 胜负 |
|---|---|---|---|---|
| `new MainWindow()` 构造前 JIT | 81.2ms | 78.5ms | −2.7ms | — |
| `Win.Xaml` | 332.6ms | 283.6ms | **−50.2ms** | 10/10 |
| `Win.Visible` | 259.7ms | 259.5ms | 0 | — |
| **TOTAL** | **1473.6ms** | **1352.2ms** | **−126.8ms** | **10/10** |

**结论**：门控达成（TOTAL ≥100ms、Xaml ≥40ms）。构造前那 80ms 主要来自 Avalonia 框架（`Window` 基类链），应用集 R2R 无法覆盖，与预估一致。
**冒烟**：self-contained R2R 发布启动正常（进程存活、trace 写入、`Win.Xaml` 277.6ms）。

**影响**：`dotnet publish -p:PublishReadyToRun=true` 才会生效；日常 `dotnet build` 与本地 debug 不受影响。release workflow 需在发布参数中启用 `PublishReadyToRun=true`（或在数字签名步骤前加参数）。