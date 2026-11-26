# 出厂默认设置改为内嵌 JSON 快照 + 安装器只写一次性向导选择

> **状态**: ✅ 已完成（随 **v0.5.7** 发布，2026-11-26 归档） | **创建**: 2026-11-22 | **完成**: 2026-11-22 | **分支**: `beta`
> **来源**: 用户「我希望新用户能默认就加载我刚刚修改的状态而不是什么都是空的状态」+ 随后「我保存的预设什么的怎么没了？」（用户设置被测试套件冲掉）
> **关联**: `.omo/plans/已完成/default-filename-template.md`（同一轮需求的另一半：出厂命名模板 + 删 `KeepOriginalExtension`）

## 背景

三件事凑到一起，暴露了同一个根问题：**「默认设置」有三个事实源，全都在漂**。

1. `AppSettings.CreateWithDefaults()` 只注入自适应规则，其余全是 C# 字段初值 —— 想改默认值只能改代码。
2. 安装版新用户的实际默认值来自 `installer/prebuilt/settings.json`（85 键的快照）—— 与 C# 属性比对后有 3 个键已不存在（`CloseAfterCompress`/`KeepOriginalExtension`/`UseColorEmoji`）、缺新增的 `FileNameTemplate*` 四键、十余处值与代码初值不同。两份事实源必然漂移。
3. 用户自己保存的命名预设被冲掉一次：全量 ui-tests 里 42 次 `SaveTo` 直接写真实 `%LOCALAPPDATA%\MantisZip\settings.json`，其中 35 次写的是「模板空、预设空」的裸实例。

顺带发现安装向导的语言/主题选择**空转两个月**：`__LANG__`/`__THEME__` 占位符在 `8ea6bdb2`（2026-08-08）被替换成字面量，而两份 iss 的 `PatchSettingsThemeAndLanguage` 还在 `StringChange` 找占位符。

## 目标

- 默认值收敛成**一份**可手工维护的 JSON，编译进程序，改完重新 build 即生效。
- 安装器不再预置 settings.json，只把向导选择写成一次性文件，由应用首启消费后删除。
- 测试永远不再碰真实设置文件 —— 靠基础设施保证，而不是靠每个用例记得写沙箱。

## 非目标

- 不新增「恢复默认设置」UI 按钮（只替换现有两条回落路径：设置文件缺失 / 损坏且备份也读不出）。
- 不做「老用户升级时迁移默认值」—— 已有 `settings.json` 逐字节不变。
- 不拆独立 CLI exe、不动工程结构。

## 实施

### 1. 内嵌快照

- 新文件 `src/MantisZip.UI.Avalonia/Resources/Defaults/settings.json`：117 键、UTF-8 无 BOM + CRLF + 2 空格缩进。以用户当时真实的 142 键 settings.json 为底，删掉 25 个「机器/会话相关 + 已被取代」的键：
  - 窗口几何与面板宽度：`CompressWindow{Width,Height,LeftColumnWidth}`、`ExtractWindow{...}`、`SettingsWindow{Width,Height}`、`PickerDialogWidth`、`ProgressWindowWidth`、`PickItemsPanelWidth`、`ExtractFolderPanelWidth`
  - 会话状态：`PickerSourceTab`、`LastUpdateCheckTime`、`LastSkippedVersion`
  - 已被取代：`Assoc{Zip,7z,Rar,Tar,TarGz,Gz,Iso}`、`OpenDialogExtensions`、`AdaptiveCompressionMode`、`MultiThreadedStoreFormatIds`
- csproj：`EmbeddedResource` + 固定 `LogicalName="MantisZip.UI.Avalonia.Resources.Defaults.settings.json"`（照 `metadata-panel.json` 先例），并从 `Resources\**` 通配里排除。

### 2. `AppSettings` 接线

```
CreateWithDefaults()
  = LoadDefaultSettingsTemplate() ?? CreateBuiltInDefaults()
  → ApplyInstallChoices(settings)   // 返回被读到的 choices 路径
  → ApplyMigrations(settings)
  → choicesFile != null && SaveTo(EffectiveSettingsFile) → 删除 choicesFile
```

- `LoadDefaultSettingsTemplate()`：`GetManifestResourceStream` → `JsonSerializer.Deserialize(..., AppSettingsJsonContext.Default.AppSettings)`；缺失 / 为 null / 异常 → `LogDefaultsFallback(reason)` 返回 null。**缺键自动保留字段初值 ⇒ 不需要 merge 逻辑。**
- `CreateBuiltInDefaults()`：保留原代码兜底，核心语义（自适应默认规则 + `{name:auto}` + 两条预设）必须与 JSON 一致。
- `ApplyInstallChoices()`：`dir = IsPortableMode ? DataDir : Path.GetDirectoryName(EffectiveSettingsFile) ?? DataDir`，存在 `install-choices.json` 则读 `Language`/`Theme` 覆盖并**返回该路径**（不再自己删）；全 try/catch 静默（向导选择只是锦上添花，绝不能让首启失败）。
  - **为什么必须当场落盘再删**（2026-11-23 补）：`install-choices.json` 是一次性文件，而启动路径从不写 `settings.json`（唯一会自动写盘的是联网成功的更新检查）⇒ 早先「套用后即删、但不落盘」时，离线用户 / 把更新检查设为「关闭」的用户 / 装完立刻关掉的用户第二次启动就退回出厂默认（实测首启 `en`/`Dark`、二启 `zh`/`System`）。
  - 落盘失败（只读目录、磁盘满）就不删，下次启动再套一次；同值重复套用无副作用。

### 3. 安装器

- 两份 iss：`[Files]` 去掉 `installer\prebuilt\settings.json`（保留 `prebuilt\window.json`）。
- `CurStepChanged` 里原「CopyFile prebuilt settings.json + PatchSettingsThemeAndLanguage + 失败回落最小 JSON」整块换成：首次安装且 `settings.json` 不存在时写 `%LOCALAPPDATA%\MantisZip\install-choices.json`（`{"Language":"…","Theme":"…"}`）。
- 删掉死过程 `PatchSettingsThemeAndLanguage`（两份 iss 各一处）。
- `git rm installer/prebuilt/settings.json`。

### 4. 测试

- `tests/MantisZip.UI.Avalonia.Tests/AppSettingsTestRedirect.cs`：`[ModuleInitializer]` 在程序集加载期把落点重定向到 `Path.GetTempPath()/mz-ui-tests-settings-<guid>/settings.json`，句柄刻意不释放，`ProcessExit` 删目录。这是「忘写沙箱」的安全网。
- `tests/MantisZip.UI.Avalonia.Tests/AppSettingsDefaultSettingsJsonTests.cs`（10 例）：资源存在且可反序列化 / **每个键都对得上真实属性** / 出厂模板与预设 / 不含机器相关键 / 不含已废弃键 / `CreateBuiltInDefaults` 语义一致 / `install-choices` 生效即删 / 有设置文件时不覆盖 / 坏 JSON 不炸首启 / **向导选择套用后落盘，第二次启动仍是向导选择**（2026-11-23 补）。

### 5. 文档

- `docs/ARCHITECTURE.md` 设置系统节：内嵌快照 + 安装器不预置 + 测试重定向三段落。
- `docs/PROGRESS.md` + `docs/progress-avalonia-detail.md`（规则 3）。
- `AGENTS.md` 规则 24 追加「程序集级安全网」段落。
- `RELEASE_NOTES.md`：**已向用户确认（2026-11-22，规则 18）—— 见下**。

## 风险

| 风险 | 处置 |
|---|---|
| JSON 快照与 C# 属性漂移（键名打错 / 属性删了 json 没删） | `AppSettingsDefaultSettingsJsonTests.EveryKey_MapsToRealProperty` 双向体检，删属性忘改 json 当场红 |
| 内嵌资源读不出来（打包/裁剪问题） | `CreateBuiltInDefaults()` 兜底 + `LogDefaultsFallback` 记 `lifecycle.log` |
| 向导选择文件残留后误覆盖用户设置 | `ApplyInstallChoices` 只在「本次是 `CreateWithDefaults()`」路径里跑；已有 settings.json 时根本不调它。套用后在 `CreateWithDefaults()` 里立刻落盘、落盘成功才删文件 —— 否则离线用户第二次启动就退回出厂默认 |
| 端口/便携模式路径不一致 | `IsPortableMode ? DataDir : settings 同目录`，与 `EffectiveSettingsFile` 同源 |

## 验收

- `-Task build -Target solution` 0 错误
- `-Task test -Target tests` / `-Task test -Target ui-tests` 全绿
- `-Task check` 全绿
- 手工：删掉 `%LOCALAPPDATA%\MantisZip\settings.json` 后启动，压缩对话框模板框为 `{name:auto}`、预设下拉两条；全量 ui-tests 后真实 settings.json 的 md5/mtime 不变
