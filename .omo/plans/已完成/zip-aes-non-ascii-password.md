# ZIP + AES 非 ASCII 密码压缩（issue #92）+ WinZip AES 内层压缩方法回归

> **状态**：已完成（2026-11-27，`beta` 分支，随 v0.5.8 发布）
> **关联代码**：`src/MantisZip.Core/Utils/ZipWinZipAesRekey.cs`（新增）、`src/MantisZip.Core/Utils/ZipEntryAccess.cs`、`src/MantisZip.Core/Engines/ZipEngine.Compress.cs`、`src/MantisZip.UI.Avalonia/Services/WholeArchiveErrorLocalizer.cs`、三语 `strings.*.json`
> **关联**：GitHub issue [#92](https://github.com/mantis3d/MantisZip/issues/92)（写入侧，本条）、[#91](https://github.com/mantis3d/MantisZip/issues/91)（读取侧，回归来源）

## 背景

issue #92 报告的写入侧缺陷：**ZIP + AES 加密压缩时，密码含任意非 ASCII 字符就直接失败**。

实测（7z.dll 26.00 / SharpSevenZip 2.0.45）：

- 可打印 ASCII 32–126 共 95 个字符逐个测过，**95/95 通过**；任何 `> 127` 的字符必失败：
  `é` / `café` / `ñ` / `ü` / `aÿb` / `aĀb` / `测试` / `测试A123` / `中文` / `一二三` /
  `Пароль` / `日本語` / `한국어` / `😀` / `A😀` 全部抛
  `SharpSevenZipException: Invalid arguments provided. (0x80070057: E_INVALIDARG)`。
- 同一批密码换成 `OutArchiveFormat.SevenZip` 全部成功；`ZipEncryptionMethod.ZipCrypto` 同样失败。
- `SharpSevenZipException : System.Exception`，不派生自任何已映射类型 ⇒ 界面落到
  `default` → 用户看到「操作失败：Invalid arguments provided. (0x80070057: E_INVALIDARG)」。
- SharpSevenZip 所有 `Compress*` 方法只收 `string password`，没有密码字节入口；
  `SharpSevenZipBase.Password` 的 setter 不可访问（`CS0272`）。**上游无法绕过**。

与此同时发现 **issue #91 引入的一条读取回归**：`ZipEntryAccess.OpenAesReadStream` 最初只映射
Store(0) 与 Deflate(8)，而 v0.5.7 之前的纯 SharpCompress 路径能读 Deflate64(9) /
BZip2(12) / PPMd(98)（LZMA(14) 在旧路径上抛的是 ZipCrypto 限制，AES 下并不成立）
⇒ 新路径会把这些包全部变成「解不开」。v0.5.8 未发布，必须同批修掉。

## 目标

1. **A**：`OpenAesReadStream` 补齐六种内层压缩方法（Store / Deflate / Deflate64 / BZip2 / LZMA / PPMd），
   未知方法给出带方法号与可读建议的 `NotSupportedException`。
2. **B**：让「ZIP + AES + 非 ASCII 密码」能压出包，且包能被本程序与 7-Zip 打开。
3. **C**：`SharpSevenZipException` 与「Core 就地拒绝的 `NotSupportedException`」在界面有中文文案。

## 非目标

- **不改** ZipCrypto 的密码编码支持（KeyCrypto 密钥派生是 CRC 迭代，换不了密钥）——
  含非 ASCII 密码时给出明确报错并建议改用 AES。
- **不改** 分卷 + ZIP 加密（本来就抛 `CompressionFailedException: the creation of multi-volume
  non-7Zip archives is not implemented`，与本次无关）。
- **不引入** 密码编码设置项（读取侧走自动探测，写入侧走系统 ANSI）。

## 覆盖范围

| 层 | 文件 | 改动 |
|----|------|------|
| Core | `Utils/ZipEntryAccess.cs` | `ActualMethod` 分派从 2 种扩到 6 种；新增 `OpenPpmdStream` / `OpenLzmaStream` |
| Core | `Utils/ZipWinZipAesRekey.cs` | **新增**：占位密码 + 就地换密钥 |
| Core | `Engines/ZipEngine.Compress.cs` | 非 ASCII 密码 → 占位密码压缩 + `Rekey`；ZipCrypto/分卷明确报错 |
| UI | `Services/WholeArchiveErrorLocalizer.cs` | 补 `SharpSevenZipException` 与含 CJK 的 `NotSupportedException` |
| i18n | `strings.{zh-CN,en,zh-TW}.json` | 新增 `Error_Whole_SevenZipInvalidArguments` |

## 关键技术事实

### 为什么占位密码 + 换密钥可行

WinZip AES 用 **AES-CTR** 加密：`C = P ⊕ KS`，其中 `KS` 只由密码（PBKDF2 派生）与 salt 决定，
与压缩方法、明文内容无关。因此

```
C_new = C_old ⊕ KS_old ⊕ KS_new
```

只需两遍 AES-CTR 异或，**不必解压、包长度完全不变**。

### 换密钥要改的四段（其余字节一律不动）

```
LFH(30) + nameLen + extraLen ⇒ dataStart
data[dataStart .. +saltLen]       = salt         ← 改
data[dataStart+saltLen .. +2]     = verifier     ← 改
其后至末尾 -10 字节               = 密文          ← 改（两遍 CTR 异或）
最后 10 字节                      = HMAC-SHA1[:10] ← 改
```

- `cipherLength = CompressedSize - saltLength - 2 - 10`
- 派生密钥：`PBKDF2-HMAC-SHA1(passwordBytes, salt, 1000, 2*keyLen+2)`
  → `encKey = [0,keyLen)`、`macKey = [keyLen,2*keyLen)`、`verifier = [2*keyLen,2*keyLen+2)`
- **AES-CTR 计数器唯一可行写法**：128 位小端，块序号写 `counter[0..8]`，块号从 1 起，
  `counter[8..16]` 全 0；`Aes` 用 `CipherMode.ECB` + `PaddingMode.None` 逐块生成 keystream。
- **HMAC 对密文计算**：`HMACSHA1(macKey).ComputeHash(cipher)[..10]`。
- 条目名、中央目录、LFH 偏移、`CompressedSize`、CRC 全部不变。

### 密码字节编码：写入侧必须用系统 ANSI

实测（探针 21）：

| 包的密码字节 | 本程序能解 | 7z.dll 能解 |
|---|---|---|
| UTF-8 | ✅ | ❌ `Wrong password` |
| 系统 ANSI（中文 Windows = GBK） | ✅ | ✅ |

7z.dll 写 ZIP+AES 时自己就是把密码转成系统 ANSI 的（这也是它遇到非 ASCII 字符就抛
`E_INVALIDARG` 的原因）。所以 `NewPasswordBytes` **优先系统 ANSI**，无法表示时（如 emoji）
回退 UTF-8 —— 读取侧候选顺序是 UTF-8 → 系统 ANSI → GBK → GB18030，两种写法都能认回来。

### 内层压缩方法的流入口（实测）

| ActualMethod | 入口 | 备注 |
|---|---|---|
| 0 Store | `decrypt` 直通 | |
| 8 Deflate | `System.IO.Compression.DeflateStream` | |
| 9 Deflate64 | `new Deflate64Stream(s, CompressionMode.Decompress)` | |
| 12 BZip2 | `BZip2Stream.Create(s, Decompress, decompressConcatenated:false, leaveOpen:false, tolerateTruncatedStream:false)` | 只有静态 `Create` |
| 14 LZMA | `LzmaStream.Create(properties, s, leaveOpen:false)` | 数据头 `ver(2)+propsLen(2)+props`；**必须两参**，三/四参会 `DataErrorException` |
| 98 PPMd | `PpmdStream.Create(new PpmdProperties(head2), rest, compress:false)` | **前 2 字节就是属性**，不是长度前缀 |

## 可测接缝

1. `ZipWinZipAesRekey.NeedsRekey(string?)` —— 纯函数，逐字符判 `> 127`。
2. `ZipWinZipAesRekey.Rekey(archivePath, placeholderPassword, newPassword)` ——
   占位密码校验失败时**必须在写任何字节之前**抛 `InvalidFormatException`
   （测试可断言 `File.ReadAllBytes` 前后一致）。
3. `ZipEngine.CompressAsync` + `Encrypt=true` + 非 ASCII 密码 → 产出包能被 `ExtractAsync` 解开。
4. `ZipEngine.ExtractAsync` + 篡改 `actualMethod=99` → `NotSupportedException`（含方法号）。
5. `WholeArchiveErrorLocalizer.ToUserText` —— 纯函数，异常进、字符串出。

## 任务

- [x] **T1** 探针确认 Deflate64 / BZip2 / PPMd / LZMA 在 ZIP-AES 里的正确解压流入口
- [x] **T2** `OpenAesReadStream` 补齐六种方法分派 + 可读错误
- [x] **T3** 探针确认换密钥对全部内层压缩方法成立且长度不变
- [x] **T4** 新建 `ZipWinZipAesRekey.cs`（占位密码 + 就地换密钥 + 先校验后写）
- [x] **T5** `ZipEngine.Compress.cs` 接入重加密分支 + ZipCrypto/分卷明确报错
- [x] **T6** 探针确认写入侧密码字节必须用系统 ANSI（7-Zip 兼容）
- [x] **T7** `WholeArchiveErrorLocalizer` 补两条映射 + 三语文案
- [x] **T8** 测试：六种压缩方法解压 + 未知方法报错
- [x] **T9** 测试：`NeedsRekey` / `Rekey` / 错占位密码不损坏包 / ZipCrypto 拒绝
- [x] **T10** 测试：引擎端到端压缩解压 + ASCII 密码不走换密钥 + 明确报错消息
- [x] **T11** 测试：错误本地化三条新映射 + 派生类顺序守卫
- [x] **T12** 跑全量门禁（build / i18n / guards / tests / ui-tests）
- [x] **T13** 更新进度文档（PROGRESS / progress-avalonia-detail / PLAN）并归档本计划
- [x] **T14** 提交 + 回帖 issue #92

## 风险与回落

| 风险 | 处置 |
|------|------|
| 换密钥把包写坏 | 先校验占位密码 verifier，不匹配立刻抛，**写之前**；已用「包字节前后一致」钉住 |
| 某些 7z 变体的 salt 位置不同 | 只处理 0x9901 存在且 strength ∈ {1,2,3} 的条目；其余抛 `NotSupportedException` |
| ZIP64 包 | `ZipBinaryRewriter.ReadEocd` 本就抛 `ZipCopyModeException`，换密钥只在非 ZIP64 上生效 |
| 未知内层压缩方法 | 抛带方法号的 `NotSupportedException`，不静默出空文件 |
| 换密钥失败后留下半成品包 | `ZipEngine.CompressAsync` 的外层 `catch` 已有清理逻辑（既有行为，未改） |

## 验收标准

- [x] `pwsh -File scripts\verify-repo.ps1 -Task build -Target core` → 0 警告
- [x] `-Task build -Target avalonia` → 0 警告
- [x] `-Task i18n` → 三语一致 1555 key
- [x] `-Task guards` → 12 项守卫全过
- [x] `-Task test -Target tests` → 通过
- [x] `-Task test -Target ui-tests` → 通过（32 条警告均为存量）
- [x] 新增测试 22 例（CompressionMethod 6 theory + 1 fact，Rekey 15）全绿
- [x] 端到端：中文密码 `测试A123` × AES-256/192/128 × 六种压缩 = 18 组自解全通过
- [x] 兼容性：换密钥后的包 7-Zip 也能打开
