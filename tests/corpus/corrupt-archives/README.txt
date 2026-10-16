================================================================================
MantisZip 损坏压缩包测试集
语料目录: tests\corpus\corrupt-archives（仓库内；原生成目录 E:\github\corrupt-archives）
源内容: source\ 下 12 个 note*.txt + docs\readme.md + payload.bin(256KB 二进制)，
        共 14 个文件条目（valid.zip 的 14 entries 基准）
生成日期: 2026-10-15（corrupt-01..13 为更早一批；其余同日补齐）
加密样本统一密码: mantis

--------------------------------------------------------------------------------
实测行为说明
--------------------------------------------------------------------------------
"实测行为" 一律指 MantisZip.Core 引擎（ZipEngine=SharpCompress / SevenZipEngine=
SharpSevenZip / TarGzEngine=SharpCompress Tar+GZip）经探针 harness 实跑记录，
全部行级数据见 PROBE.md（108 行 = 每文件 × 密码组合 × List/Test/Extract）。

复跑方法（只读，不修改语料；仓库根执行）:
  dotnet run --project tools\CorruptProbe\CorruptProbe.csproj ^
      --corpus tests\corpus\corrupt-archives --out tests\corpus\corrupt-archives\PROBE.md
  （harness 位于 tools\CorruptProbe\，ProjectReference 为仓库内相对路径；
    支持 --only file1,file2 只跑指定样本）

回归测试: tests\MantisZip.Tests\Utils\ArchiveHeaderProbeTests.cs（头部结构探测，纯字节解析，
  无 7z.dll 依赖）与 tests\MantisZip.UI.Avalonia.Tests\SourceArchiveValidationTests.cs
  （打开阶段密码/损坏分类）均直接读取本语料。

早期 corrupt-01..12 描述中的 "Python zipfile/gzip 实测" 仅作生成期校验参考，
与 SharpCompress 行为不总一致（例如 .NET ZipArchive 不校验 CRC）。

--------------------------------------------------------------------------------
跨样本关键结论（写代码前必读）
--------------------------------------------------------------------------------
1. SharpSevenZip 的 "Invalid archive: open/read error! Is it encrypted and a
   wrong password was provided?..." 是大杂烩消息，以下场景抛同一条：
   - 7z 头损坏/截断 (corrupt-08/14)、RAR 签名损坏 (corrupt-24)
   - 7z/RAR 分卷缺失 (vol-02)
   - 文件名加密但未提供密码 (enc-04/enc-06)
   => ArchiveService 判定密码相关错误时不能只看 "password" 关键字，
      否则纯损坏包会被误判成"需要密码"。

2. "静默通过"类（Test=true 或无异常但结果错误/为空），SharpCompress
   TestArchiveAsync 不校验 gzip CRC、不检测 tar/tgz 截断：
   - corrupt-09/11/12/23 截断流: Test=true，只解出 13 个文件，无任何报错
   - corrupt-17 EOCD CD 偏移损坏: 0 entries + Test=true（当空包"成功"）
   - corrupt-20 CD 大小字段篡改: Test=true + 正常解出 14 个文件（静默忽略）
   - corrupt-21 tar checksum 字段损坏: 0 entries + Test=true（静默空）
   => 用户视角是"解压成功但文件缺了/空了"，属最难发现的损坏类别。

3. 错密码行为不一致（enc 组核心价值）：
   - ZIP AES 错密码: Extract 不抛异常，"OK: 0 files; failed=14"（静默失败）
   - 7z/RAR 错密码: ExtractionFailedException "File is corrupted. Data error"
     （与真实损坏同一条消息，无法区分错密码与损坏）

4. enc-02（AES 包 + 数据损坏）用正确密码 mantis 提取出 14 个文件、340.6 KB
   （基线 340 KB），全程无报错；而 7z.exe t -pmantis 报
   "Data Error in encrypted file. Wrong password? : note9.txt"。
   => MantisZip 解压路径对 AES 条目疑似不校验 CRC/MAC，损坏数据被静默写出。

5. corrupt-04/05（deflate 流损坏）Extract 记录为 "OK: 14 files; failed=1"，
   即个别条目失败但整体"成功"返回；且并行/串行路径行为有差异
   （冒烟测试中并行曾出现 13 文件 + 0 字节残留，以 PROBE.md 为准）。

--------------------------------------------------------------------------------
对照组（全部正常，用作行为基线）
--------------------------------------------------------------------------------
  valid.zip            正常 ZIP（14 entries，无目录条目）
                       Test=true → 14 files, 340 KB
  valid.tar            正常 TAR（16 entries，含 . 和 ./docs 目录条目）
  valid.tar.gz         正常 TAR.GZ（同上 16 entries）
  valid.7z             正常 7z（LZMA2 默认参数，15 entries，含 docs 目录条目）
  valid.rar            正常 RAR5（15 entries，含 docs 目录条目）
  valid-rar4.rar       正常 RAR4（15 entries）——RAR4/RAR5 双格式基线
  valid-empty.zip      空 ZIP（0 字节）: 0 entries, Test=true
  valid.iso            ISO9660（1 entry: README.TXT, 480 B）——ISO 引擎基线
  注意: 7z/RAR 均比 valid.zip 多一个 docs 目录条目（15 vs 14），系 7z/Rar
  命令行打包目录时自动追加目录项，非内容差异。

--------------------------------------------------------------------------------
ZIP 损坏变体
--------------------------------------------------------------------------------
  corrupt-01-truncated-tail.zip   去掉尾部 40%（中央目录 + EOCD 丢失）
                                  List/Test/Extract 全部 EXC:
                                  "Failed to locate the Zip Header"
  corrupt-02-truncated-half.zip   拦腰截断（保留前 50%）
                                  同 01: "Failed to locate the Zip Header"
  corrupt-03-bad-crc.zip          stored 条目 payload.bin 数据翻转 1 字节
                                  （中央目录完好）
                                  List=OK 1 entry; Test=false;
                                  Extract="OK: 0 files; failed=1"（CRC 失败被
                                  计数但不抛异常）
  corrupt-04-bad-deflate.zip      deflate 流中段翻转 1 字节
                                  List=OK 14; Test=false;
                                  Extract="OK: 14 files, 338.7 KB; failed=1"
  corrupt-05-bad-header.zip       第二个条目的 local file header 魔数
                                  PK\x03\x04 被清零
                                  同 04: Test=false, "14 files; failed=1"
  corrupt-06-empty.zip            0 字节空文件
                                  EXC: "Could not find Zip file Directory at
                                  the end of the file. File may be corrupted."
  corrupt-07-garbage.zip          4096 字节随机垃圾（.zip 扩展名）
                                  EXC: "Failed to locate the Zip Header"
  corrupt-13-crc-field-flipped.zip
                                  中央目录第一个条目（docs/readme.md）的 CRC
                                  字段翻转，local 头完好
                                  List=OK 14; Test=false;
                                  Extract="OK: 13 files, 338.7 KB; failed=1"
  corrupt-17-zip-eocd-cd-offset-bad.zip
                                  EOCD 中央目录偏移字段被改写（指向垃圾偏移）
                                  ★静默通过: List=OK 0 entries; Test=true;
                                  Extract="OK: 0 files"——空包当成功
  corrupt-18-zip-cd-local-offset-flipped.zip
                                  中央目录某条目 local 头偏移字段翻转
                                  List=OK 14; Test=false;
                                  Extract="OK: 14 files, 338.7 KB; failed=1"
  corrupt-19-zip-bad-compression-method.zip
                                  中央目录某条目压缩方法字段改为非法值 0x63
                                  List=OK 14; Test=false;
                                  Extract="OK: 13 files, 338.7 KB; failed=1"
  corrupt-20-zip-size-tampered.zip
                                  中央目录某条目未压缩大小字段篡改
                                  ★静默通过: List=OK 14; Test=true;
                                  Extract="OK: 14 files, 340 KB"——
                                  解压用 local 头大小，CD 字段被无视

--------------------------------------------------------------------------------
7z 损坏变体
--------------------------------------------------------------------------------
  corrupt-08-7z-bad-header.7z     伪造 7z: 正确签名 + 8KB 随机正文
                                  （非真 7z 截断）
                                  EXC SharpSevenZipArchiveException:
                                  "Invalid archive: open/read error! Is it
                                  encrypted and a wrong password was provided?..."
  corrupt-14-7z-truncated-tail.7z 真 7z（valid.7z）截断尾部 ~40%
                                  同 08 的误导性消息（见关键结论 1）
  corrupt-15-7z-solid-data-flipped.7z
                                  真 7z 固实压缩，固实数据区翻转 1 字节
                                  List=OK 15（目录完好）; Test=false;
                                  EXC ExtractionFailedException:
                                  "File is corrupted. Data error has occured."
  corrupt-16-7z-data-flipped.7z   真 7z 非固实，某条目数据区翻转 1 字节
                                  同 15: List=OK 15; Test=false; Data error

--------------------------------------------------------------------------------
TAR / TAR.GZ 损坏变体
--------------------------------------------------------------------------------
  corrupt-09-truncated.tar.gz     保留前 60%
                                  ★静默部分成功: List=OK 15; Test=true;
                                  Extract="OK: 13 files, 140.4 KB"（缺 1 个
                                  文件，无任何报错）
  corrupt-10-bad-gzip-magic.tar.gz
                                  gzip 魔数 1F 8B 被清零
                                  EXC IncompleteArchiveException:
                                  "Unexpected EOF reading tar file"
  corrupt-11-bad-gzip-stream.tar.gz
                                  gzip 压缩流中段翻转 1 字节
                                  ★静默: List=OK 15; Test=true;
                                  Extract="OK: 13 files, 338.7 KB"
  corrupt-12-truncated.tar        TAR 截断 60%（无 gzip 外壳）
                                  ★静默: List=OK 15; Test=true;
                                  Extract="OK: 13 files, 200.9 KB"
  corrupt-21-tar-checksum-field.tar
                                  第一个 tar header 的 checksum 字段（偏移
                                  148..155）被清零
                                  ★静默: List=OK 0 entries; Test=true;
                                  Extract="OK: 0 files"——整包被当空
  corrupt-22-targz-inner-not-tar.tar.gz
                                  gzip 外壳完好，内部是随机数据（非 tar）
                                  EXC InvalidFormatException: "Not a tar file."
  corrupt-23-targz-inner-truncated.tar.gz
                                  gzip 外壳完好，内部 tar 截断
                                  ★静默: List=OK 15; Test=true;
                                  Extract="OK: 13 files, 200.9 KB"

--------------------------------------------------------------------------------
RAR 损坏变体
--------------------------------------------------------------------------------
  corrupt-24-rar-bad-signature.rar
                                  RAR5 签名 "Rar!\x1A\x07\x01" 首字节翻转
                                  EXC SharpSevenZipArchiveException
                                  （与 7z 头损坏同一条误导消息）
  corrupt-25-rar-truncated.rar    RAR5 截断 40%
                                  List=OK 11 entries（残缺列表）; Test=false;
                                  EXC "File is corrupted. Data error"
  corrupt-26-rar-data-flipped.rar RAR5 某条目数据区翻转 1 字节
                                  List=OK 15; Test=false; EXC Data error
  corrupt-27-rar-solid-data-flipped.rar
                                  RAR5 固实压缩，固实数据区翻转 1 字节
                                  List=OK 15; Test=false; EXC Data error

--------------------------------------------------------------------------------
加密样本（密码: mantis；wrongpw = 随机错误密码）
--------------------------------------------------------------------------------
  enc-01-zip-aes.zip              ZIP + WinZip AES-256（7z 生成 -mem=AES256）
                                  无密码: List=OK 15; EXC "此压缩包已加密，请
                                  输入密码"（正确判定）
                                  wrongpw: Test=false; Extract 不抛异常，
                                  "OK: 0 files; failed=14"（★静默失败）
                                  mantis: Test=true; 14 files, 340 KB
  enc-02-zip-aes-corrupt.zip      同 enc-01 但压缩数据区翻转 1 字节
                                  无密码/wrongpw: 同 enc-01
                                  mantis: ★Test=true; "OK: 14 files,
                                  340.6 KB"——比基线多 0.6 KB 且无报错，
                                  而 7z.exe t -pmantis 报 "Data Error in
                                  encrypted file"（见关键结论 4）
  enc-03-7z-encrypted.7z          7z AES-256 加密（仅数据加密，文件名可见）
                                  无密码: List=OK 15; EXC "此压缩包已加密"
                                  wrongpw: EXC "File is corrupted. Data
                                  error"（★与真损坏同消息）
                                  mantis: Test=true; 14 files, 340 KB
  enc-04-7z-encrypt-headers.7z    7z -mhe=on（文件名也加密）
                                  无密码/wrongpw: List 即 EXC
                                  SharpSevenZipArchiveException 误导消息
                                  （★与损坏不可区分，见关键结论 1）
                                  mantis: List=OK 15; Test=true; 14 files
  enc-05-rar-encrypted.rar        RAR5 传统加密（文件名可见）
                                  无密码: List=OK 15; EXC "此压缩包已加密"
                                  wrongpw: EXC "File is corrupted. Data error"
                                  mantis: Test=true; 14 files, 340 KB
  enc-06-rar-headers-encrypted.rar
                                  RAR5 -hp（文件名加密）
                                  无密码/wrongpw: List 即 EXC 误导消息
                                  mantis: List=OK 15; Test=true; 14 files

--------------------------------------------------------------------------------
边缘场景
--------------------------------------------------------------------------------
  edge-01-gbk-filenames.zip       3 个 GBK 编码文件名（无 EFS 标志）：
                                  说明文件.txt / 子目录\数据.bin /
                                  第二层\深层\文件.txt
                                  List=OK 3 entries（中文名完整显示）;
                                  Test=true; Extract=3 files, 95 B
                                  ——当前系统代码页下可用
  edge-02-zip-slip.zip            Zip Slip 攻击样本：../evil.txt、
                                  ../../evil2.txt、绝对路径 /abs/evil3.txt
                                  List=OK 4 entries（恶意路径原样列出）;
                                  Test=true;
                                  Extract EXC InvalidOperationException:
                                  "压缩条目包含非法路径: ../evil.txt"
                                  ——解压侧正确拦截（GetSafePath 生效）

--------------------------------------------------------------------------------
错扩展名
--------------------------------------------------------------------------------
  mis-01-7z-renamed.zip           valid.7z 改名为 .zip（内容仍是 7z）
                                  走扩展名分发 → ZipEngine:
                                  EXC ArchiveException:
                                  "Failed to locate the Zip Header"
                                  ——无魔数回退，属已知设计行为

--------------------------------------------------------------------------------
分卷压缩
--------------------------------------------------------------------------------
  vol-01-7z-split.001/.002/.003  7z -v300 分卷（300B/卷）
                                  打开 .001: SharpSevenZip 自动关联相邻
                                  .002/.003 → List=OK 15; Test=true;
                                  14 files, 340 KB（完整成功）
                                  单独打开 .002/.003: "SKIP: no engine"
                                  （无文件魔数，分发不到引擎）
  vol-02-7z-missing-part.001      分卷集只保留第一卷（.002/.003 删除）
                                  EXC SharpSevenZipArchiveException 误导
                                  消息（见关键结论 1）
                                  对照: 7z.exe t 反而 exit 0（Type=Split
                                  打开第一卷不报错），SharpSevenZip 更严格
  vol-03.zip.001/.002/.003        7z -tzip -v1500 分卷（命名 .zip.001 风格，
                                  非传统 .z01）
                                  打开 .001: EXC ArchiveException:
                                  "Failed to locate the Zip Header"
                                  （首卷无 EOCD，ZipEngine 不支持 ZIP 分卷）
                                  .002/.003: "SKIP: no engine"

--------------------------------------------------------------------------------
生成方法与偏差记录
--------------------------------------------------------------------------------
- 7z/RAR/TAR/ZIP 均从 source\ 打包；7z 固实块用 -ms=on（7z 无 -s 开关，
  生成脚本早期笔误已按 -ms=on 执行）
- 7z/RAR 包比 valid.zip 多 docs 目录条目（15 vs 14），文件条目内容一致
- ZIP 类损坏用 Python zipfile + 字节手术精确改写 CD/EOCD/local 头
- corrupt-08 为伪造 7z（生成环境当时无法立即产出真 7z 截断）；corrupt-14
  起为真 7z 损坏，两者行为一致可互为参照
- vol-02 保留 .001 原名（7z.exe 视其为 Split 不报错），以 SharpSevenZip
  行为为准记录如上
- vol-03 采用 7z 产出的 .zip.001 命名（非 WinRAR .z01 风格）
- 加密包由 7z.exe（ZIP AES/7z）与 Rar.exe（RAR4/RAR5）生成
- 探针 harness 源码见 harness\（CorruptProbe.csproj + Program.cs），
  含密码矩阵：enc-* 跑 [无密码, wrongpw, mantis]，其余跑 [无密码, mantis]

如需更多类型（RAR4 损坏、多卷 ZIP .z01 传统命名、BZip2/LZMA ZIP 条目、
分卷 TAR 等）请告知。
