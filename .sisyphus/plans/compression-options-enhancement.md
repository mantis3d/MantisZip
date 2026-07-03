# 压缩选项增强：7z/ZIP 格式参数扩展

## 背景

当前压缩设置窗口（CompressSettingsWindow）中，7z 仅有「压缩方法」和「固实压缩」两个选项，
ZIP 仅有「文件名编码」一个选项。SharpSevenZip 和 SharpCompress 均暴露了更多可调参数，
但未在 UI 中呈现。

## 目标

1. 7z 面板补齐固实块大小、字典大小、Word Size、匹配器、加密文件名开关
2. ZIP 面板补齐压缩方法
3. 加密 Tab 补齐加密方式（ZIP 用）和加密文件名开关（7z 用）

## 完整选项清单

### 7z 面板（新增 4 项）

| 选项 | 控件类型 | 可选值 | 默认值 | 对应 SharpSevenZip 属性 |
|------|----------|--------|--------|------------------------|
| 固实块大小 | ComboBox | 默认 / 64MB / 256MB / 512MB / 1GB | 默认 | `CustomParameters["s"]` = `"64m"` 等，默认时不设（7z.dll 自行决定） |
| 字典大小 | ComboBox | 16MB / 32MB / 64MB / 128MB / 256MB | 64MB（7z 默认） | `LzmaDictionarySize` = 2^24 等 |
| Word Size（快速字节） | ComboBox | 32 / 64 / 128 / 255 | 255（7z Ultra 默认） | `LzmaNumFastBytes` |
| 匹配器 | ComboBox | BT2 / BT3 / BT4 | BT4（7z 默认） | `LzmaMatchFinder` = `"bt2"` / `"bt3"` / `"bt4"` |

**依赖关系**：字典大小和 Word Size 仅对 LZMA/LZMA2 方法有效，选 PPMd/BZip2/Deflate 时禁用或灰显。

### ZIP 面板（新增 1 项）

| 选项 | 控件类型 | 可选值 | 默认值 | 对应 API |
|------|----------|--------|--------|---------|
| 压缩方法 | ComboBox | Deflate / Deflate64 / BZip2 / LZMA / PPMd / Store | Deflate | `ZipWriterOptions.CompressionType`（非加密）/ `SharpSevenZipCompressor.CompressionMethod`（加密） |

### 加密 Tab（新增 2 项，放在密码区域下方）

| 选项 | 控件类型 | 可选值 | 默认值 | 格式 | 对应 API |
|------|----------|--------|--------|:----:|---------|
| 加密方式 | ComboBox | AES-256 / AES-192 / AES-128 / ZipCrypto | AES-256 | ZIP 专用 | `SharpSevenZipCompressor.ZipEncryptionMethod` |
| 加密文件名 | CheckBox | ☑ / ☐ | ☑ | 7z 专用 | `SharpSevenZipCompressor.EncryptHeaders` |

**可见性逻辑**：
- **加密方式**：仅 ZIP 且勾选加密时显示
- **加密文件名**：仅 7z 且勾选加密时显示
- 两个都不选时不显示对应控件（用 Visibility 控制）

## 数据模型变更

### ArchiveOptions 新增属性

```csharp
// 7z 选项
public string? SevenZipSolidBlockSize { get; set; }   // null/"64m"/"256m"/"512m"/"1g"
public int? SevenZipDictionarySize { get; set; }       // null/2^24/2^25/2^27/2^28
public int? SevenZipNumFastBytes { get; set; }         // null/32/64/128/255
public string? SevenZipMatchFinder { get; set; }       // null/"bt2"/"bt3"/"bt4"

// ZIP 选项
public string? ZipCompressionMethod { get; set; }      // null/"deflate"/"deflate64"/"bzip2"/"lzma"/"ppmd"/"store"

// 加密选项
public string? ZipEncryptionMethod { get; set; }       // null/"aes256"/"aes192"/"aes128"/"zipcrypto"
public bool SevenZipEncryptHeaders { get; set; } = true;
```

### AppSettings 新增默认值属性

```csharp
// 压缩 → 7z
public string SevenZipSolidBlockSize { get; set; } = "";   // 空=默认
public int SevenZipDictionarySize { get; set; } = 0;        // 0=默认
public int SevenZipNumFastBytes { get; set; } = 0;          // 0=默认
public string SevenZipMatchFinder { get; set; } = "";       // 空=默认

// 压缩 → ZIP
public string ZipCompressionMethod { get; set; } = "deflate";

// 压缩 → 加密
public string ZipEncryptionMethod { get; set; } = "aes256";
public bool SevenZipEncryptHeaders { get; set; } = true;
```

## UI 变更

### DynamicFormatOptionsPanel.xaml — 7z 面板

固实复选框下方追加 4 行：

```
固实块大小: [默认     ▾]     (仅当固实勾选时启用)
             64MB
             256MB
             512MB
             1GB
字典大小:   [64MB     ▾]     (仅 LZMA/LZMA2 时启用)
Word Size:  [255      ▾]     (仅 LZMA/LZMA2 时启用)
匹配器:     [BT4      ▾]     (仅 LZMA/LZMA2 时启用)
```

### DynamicFormatOptionsPanel.xaml — ZIP 面板

编码下方追加 1 行：

```
压缩方法:   [Deflate  ▾]
```

### SettingsWindow.xaml — 加密 Tab

在密码区域下方追加：

```
加密方式:   [AES-256  ▾]     (Visibility=Collapsed 默认，ZIP+加密时 Visible)
☑ 加密文件名                (Visibility=Collapsed 默认，7z+加密时 Visible)
```

## 引擎变更

### SevenZipEngine.ConfigureCompressor

```csharp
// 固实块大小
if (!string.IsNullOrEmpty(options.SevenZipSolidBlockSize))
    compr.CustomParameters["s"] = options.SevenZipSolidBlockSize;
else if (!options.SevenZipSolid)
    compr.CustomParameters["s"] = "off";
// 注意：固实块大小和固实开关是同一个参数 s
// s=64m = 固实+块大小，s=off = 关闭固实

// 字典大小
if (options.SevenZipDictionarySize.HasValue)
    compr.LzmaDictionarySize = options.SevenZipDictionarySize.Value;

// Word Size
if (options.SevenZipNumFastBytes.HasValue)
    compr.LzmaNumFastBytes = options.SevenZipNumFastBytes.Value;

// 匹配器
if (!string.IsNullOrEmpty(options.SevenZipMatchFinder))
    compr.LzmaMatchFinder = options.SevenZipMatchFinder;

// 加密文件名（7z）
compr.EncryptHeaders = options.SevenZipEncryptHeaders;
```

### ZipEngine.CompressAsync

**非加密路径**（ZipWriterOptions）：

```csharp
var compressionType = options.ZipCompressionMethod?.ToLowerInvariant() switch
{
    "deflate64" => CompressionType.Deflate64,
    "bzip2" => CompressionType.BZip2,
    "lzma" => CompressionType.LZMA,
    "ppmd" => CompressionType.PPMd,
    "store" => CompressionType.None,
    _ => CompressionType.Deflate,
};
var writerOptions = new ZipWriterOptions(compressionType) { ... };
```

**加密路径**（SharpSevenZipCompressor）：

```csharp
compr.CompressionMethod = options.ZipCompressionMethod?.ToLowerInvariant() switch
{
    "deflate64" => SharpSevenZip.CompressionMethod.Deflate64,
    "bzip2" => SharpSevenZip.CompressionMethod.BZip2,
    "lzma" => SharpSevenZip.CompressionMethod.Lzma,
    "ppmd" => SharpSevenZip.CompressionMethod.Ppmd,
    "store" => SharpSevenZip.CompressionMethod.Copy,
    _ => SharpSevenZip.CompressionMethod.Deflate,
};

compr.ZipEncryptionMethod = options.ZipEncryptionMethod?.ToLowerInvariant() switch
{
    "zipcrypto" => ZipEncryptionMethod.ZipCrypto,
    "aes128" => ZipEncryptionMethod.Aes128,
    "aes192" => ZipEncryptionMethod.Aes192,
    _ => ZipEncryptionMethod.Aes256,
};
```

### CompressService / CompressRequest

`CompressRequest` 需传递新增属性，当前已逐字段赋值，追加即可。

## 边界情况

| 情况 | 处理 |
|------|------|
| 7z 固实勾选 + 固实块大小选"默认" | 不设 CustomParameters["s"]，7z.dll 默认固实行为 |
| 7z 固实勾选 + 固实块大小选 64MB | CustomParameters["s"] = "64m" |
| 7z 固实不勾选 | 固实块大小 ComboBox 禁用，CustomParameters["s"] = "off" |
| ZIP 选 Store | 不压缩，直接存储，压缩级别忽略 |
| ZIP 选 LZMA/PPMd | 兼容性差（部分解压软件不支持），在 UI 提示或 ComboBoxItem 加备注 |
| 7z 选 PPMd 时字典大小/Word Size/匹配器 | 禁用（仅 LZMA/LZMA2 有效） |
| ZIP 加密 + 选 ZipCrypto | 兼容性好（Windows 原生），但安全性低于 AES |

## 实施步骤

1. ArchiveOptions 新增属性
2. AppSettings 新增默认值属性
3. DynamicFormatOptionsPanel.xaml — 7z 面板追加 4 个控件
4. DynamicFormatOptionsPanel.xaml — ZIP 面板追加压缩方法
5. DynamicFormatOptionsPanel.xaml.cs — 新增 CLR 属性 + 可见性联动
6. SettingsWindow.xaml — 加密 Tab 追加加密方式 + 加密文件名
7. SettingsWindow.xaml.cs — Load/Save + 格式联动可见性
8. SevenZipEngine.ConfigureCompressor — 应用新参数
9. ZipEngine.CompressAsync — 非加密 + 加密路径应用新参数
10. CompressRequest / CompressService — 传递新属性
11. 编译验证
