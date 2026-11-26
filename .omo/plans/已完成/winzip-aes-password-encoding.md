# WinZip AES 中文密码解不开（issue #91）—— 自研解密层 + 解压静默失败修复

> **状态**：已完成（2026-11-26，`beta` 分支，随 v0.5.8 发布）
> **分支**：`beta`
> **版本**：v0.5.8（6 处版本号已同步，本次不改版本号）
> **关联**：GitHub issue [#91](https://github.com/mantis3d/MantisZip/issues/91)（读取侧，本条）、[#92](https://github.com/mantis3d/MantisZip/issues/92)（写入侧，另案跟踪）、`docs/KNOWN_ISSUES.md` KI-016
> **关联代码**：
> - `src/MantisZip.Core/Utils/ZipWinZipAes.cs`（新增）
> - `src/MantisZip.Core/Utils/ZipEntryAccess.cs`（新增）
> - `src/MantisZip.Core/Engines/ZipEngine.cs`
> - `src/MantisZip.Core/Engines/ZipEngine.Extract.cs`
> - `src/MantisZip.Core/Engines/ZipEngine.CompressGroup.cs`
> - `src/MantisZip.Core/Utils/ArchiveEntryExtractor.cs`
> - `src/MantisZip.UI.Avalonia/Services/PasswordService.cs`

## 背景

issue #91（bylkuse，2026-11-25，v0.5.6 / Windows 11 / 系统代码页 UTF-8）：
用 pyzipper 以 `WinZip AES-256 + Deflate` 写两个内容相同的 ZIP，唯一差别是
`password.encode("gbk")` vs `password.encode("utf-8")`。MantisZip 里 UTF-8 那份正常，
GBK 那份无论怎么输正确密码都判「密码错误」。

根因（已核对上游源码 + 探针实测）：SharpCompress 0.50.4 的
`WinzipAesEncryptionData` 构造只收 `string password`，内部
`var passwordBytes = Encoding.UTF8.GetBytes(password);` 后跑 PBKDF2 —— **密码字节被写死成
UTF-8，且没有任何公开入口可注入密码字节**：

- `ReaderOptions` / `WriterOptions` 只有 `string Password`
- `ArchiveEncoding.Password` 存在但对 WinZip AES 分支完全无效（实测 GBK/UTF-8 都是同一异常）
- 7z.dll 走**系统 ANSI 代码页**（本机 ACP=936 时 GBK 能解、UTF-8 不能），与 SharpCompress 恰好互补，
  两者都不覆盖对方 —— 所以「换 7z.dll」也解决不了报告者（其机器代码页是 UTF-8）

用户可见症状三层：① 密码框验证失败；② 解压「看起来成功」但 0 文件（静默）；③ 完整性检测返回 false。

## 目标

1. **读取侧**自研 WinZip AES 解密层，密码字节编码**自动探测**（UTF-8 → 系统 ANSI → GBK → GB18030），
   由 2 字节 password verifier 判定命中，HMAC-SHA1 认证码兜底。用户不改 Windows 区域设置。
2. 密码校验 / 完整性检测 / 预览 / 解压四条入口**同一套策略**（都经同一个读取入口）。
3. 修掉「解压静默 0 文件」：条目全部失败时明确报错，区分「密码错误」与「数据损坏」。
4. 保留 AES 完整性认证（HMAC-SHA1）与 AE-1 的 CRC32 校验。

## 非目标

- **写入侧**「ZIP + AES + 非 ASCII 密码」压缩直接抛 `E_INVALIDARG`（7z.dll 的 ANSI 转换层限制）
  —— 本次不动，另开 [#92](https://github.com/mantis3d/MantisZip/issues/92) 跟踪。
- 传统 `ZipCrypto`（PKZIP 2.0）加密：SharpCompress 能处理，保持原样。
- RAR / 7z 的密码编码：不在本 issue 范围。
- 不做「密码编码」显式设置项（用户明确选了纯 auto 的 B 方案，不做 B+C）。
- ZIP64 与 Store/Deflate 之外的压缩方法：本次**安全回落**到 SharpCompress 旧路径（宁可不修也不劣化）。

## 覆盖范围

| 层 | 文件 | 改动 |
|---|---|---|
| Core 底层 | `Utils/ZipWinZipAes.cs`（新增） | 0x9901 解析、PBKDF2 派生、AES-CTR 计数器、HMAC 校验、CRC32 流、密码候选编码 |
| Core 接线 | `Utils/ZipEntryAccess.cs`（新增） | 中央目录缓存、条目定位、密码探测、解密+解压流水线、统一 `OpenReadStream` / `WriteTo` |
| Core 引擎 | `Engines/ZipEngine.cs` | `TestArchiveAsync` 走统一入口 |
| Core 引擎 | `Engines/ZipEngine.Extract.cs` | 串行/并行/过滤三条路径走统一入口 + 全失败明确抛错 |
| Core 引擎 | `Engines/ZipEngine.CompressGroup.cs` | AddToArchive / DeleteEntries 的旧条目提取走统一入口 |
| Core 工具 | `Utils/ArchiveEntryExtractor.cs` | 预览/头部/tail 提取走统一入口 |
| UI 层 | `Services/PasswordService.cs` | AES 条目直接校验 verifier（最快路径） |

## 关键技术事实（探针实测，实现时不得偏离）

```
LFH(30) + nameLen + extraLen ⇒ dataStart
data[dataStart .. +saltLen]  ⇒ salt（strength 1→8 / 2→12 / 3→16 字节）
data[+saltLen .. +2]         ⇒ password verifier（2 B 小端）
其后到末尾 -10 字节           ⇒ AES-CTR 密文
最后 10 字节                  ⇒ HMAC-SHA1 前 10 字节（作用于密文）
compSize = saltLen + 2 + 密文长度 + 10
```

- 0x9901 扩展字段：`id(2)=0x9901 size(2) version(2) vendor(2="AE") strength(1) actualMethod(2)`
- `PBKDF2-HMAC-SHA1(pwdBytes, salt, 1000, 2*keyLen+2)` → `encKey=[0,keyLen)`、`macKey=[keyLen,2*keyLen)`、
  verifier=`derived[2*keyLen..+2]`
- **AES-CTR 计数器只有一种写法可行**：128 位小端，块序号写 `counter[0..8]`，块号从 1 起，
  `counter[8..16]` 恒 0（序号写高 8 字节、NIST 大端都会解出垃圾）
- **salt 不在 0x9901 扩展字段里**（第一次实现最容易踩的坑）
- HMAC 作用于**密文**，不是明文

## 可测接缝

| 接缝 | 类型 | 断言点 |
|---|---|---|
| `ZipWinZipAes.TryReadExtraInfo` | `internal` 纯函数 | 合法/非法/无 0x9901 的 extra field |
| `ZipWinZipAes.CandidatePasswordBytes` | `internal` 纯函数 | ASCII 密码去重后只剩 1 个候选；中文密码 4 个候选；GBK 无法表示的字符被跳过 |
| `ZipWinZipAes.SaltLength` / `KeyLength` | `internal` 纯函数 | strength 1/2/3 映射，未知值返回 0 |
| `ZipEntryAccess.VerifyAesPassword` | `internal` | GBK 包 + 正确密码 → true；错密码 → false；UTF-8 包同理 |
| `ZipEntryAccess.IsWinZipAesEntry` | `internal` | GBK/UTF-8 AES 包 → true；普通 zip / ZipCrypto → false |
| `ZipEngine.ExtractAsync` | 公开 | GBK AES 包解出 1 个文件且内容正确；错密码 → 抛含 password 的异常 |
| `ZipEngine.ListEntriesAsync` | 公开 | GBK AES 包照旧列出条目（文件名编码与密码无关） |
| `ZipEngine.TestArchiveAsync` | 公开 | GBK AES 包 + 正确密码 → true |
| 静默失败 | 公开 | 条目全部失败时 `ExtractAsync` 抛错，不再返回 `SucceededEntries=0, FailedEntries=0` |

## 任务

- [x] **T1** 勘察 Core/UI 读取链路，确认全部 `OpenEntryStream()` / `WriteTo` 站点
- [x] **T2** 探针实测 WinZip AES 文件布局、CTR 计数器变体、HMAC 计算范围（`$env:TEMP\issue91probe`）
- [x] **T3** 探针确认 7z.dll 读取侧走 ANSI、写入侧对非 ASCII 密码直接 `E_INVALIDARG`
- [x] **T4** 反射确认 SharpCompress 0.50.4 无任何密码字节入口
- [x] **T5** `ZipWinZipAes.cs`：0x9901 解析 + PBKDF2 + AES-CTR 流 + HMAC 校验 + CRC32 流 + 密码候选编码
- [x] **T6** `ZipEntryAccess.cs`：中央目录缓存 + 条目定位 + 密码探测 + 解密/解压流水线 + 统一 `OpenReadStream`/`WriteTo`
- [x] **T7** 接入 `ZipEngine.cs`（TestArchiveAsync）
- [x] **T8** 接入 `ZipEngine.Extract.cs`（串行 / 并行 / 过滤三路径）+ 全失败明确抛错
- [x] **T9** 接入 `ZipEngine.CompressGroup.cs`（AddToArchive / DeleteEntries 旧条目提取）
- [x] **T10** 接入 `ArchiveEntryExtractor.cs`（预览头部 / 全文 / tail 提取）
- [x] **T11** 接入 `PasswordService.cs`（AES 条目走 verifier 快验）
- [x] **T12** 测试：语料生成器 + `ZipWinZipAesTests` + `ZipEntryAccessTests` + 解压/静默失败回归
- [x] **T13** 全量门禁：build / i18n / guards / tests
- [x] **T14** 回帖 issue #91 + 新开写入侧关联 issue 并互链 —— 回帖 <https://github.com/mantis3d/MantisZip/issues/91#issuecomment-7189257437>，写入侧新开 #92 并在两边互链
- [x] **T15** 更新进度文档（PROGRESS / progress-avalonia-detail / 本计划归档）

## 风险与回落

| 风险 | 处置 |
|---|---|
| 中央目录不可解析（ZIP64 / 畸形包） | `ZipEntryAccess` 捕获后按「无 AES 条目」处理 → 回落 SharpCompress 旧路径（不劣化） |
| 压缩方法不是 Store/Deflate | `NotSupportedException`（明确告知，不静默解出垃圾） |
| 密码候选全不匹配 | `InvalidFormatException("bad password (WinZip AES)")` —— 消息含 "password"，既有启发式命中 |
| HMAC 认证失败（数据被改） | `InvalidFormatException("WinZip AES authentication failed: ...")` —— 走既有损坏判定 |
| 并行解压反复解析中央目录 | 按 `路径 + 长度 + 修改时间` 缓存（上限 8 个），失效自动重建 |
| 预览只读头部不读到末尾 | 不校验 HMAC（此时错密码已在 verifier 处拦下，不会放过） |

## 验收标准

1. issue #91 的 GBK AES 样本在 MantisZip 里密码校验通过、能列出、能解出正确内容
2. UTF-8 AES 样本行为不回退（与改动前逐字节一致）
3. 错密码仍然明确报「密码错误」，不变成「文件损坏」
4. 条目全部失败时不再静默返回成功
5. `build` / `i18n` / `guards` / `tests` 全绿，无新增警告
