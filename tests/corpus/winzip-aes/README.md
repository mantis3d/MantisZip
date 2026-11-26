# WinZip AES 密码编码语料（issue #91）

这两个包用来回归「同一密码、不同密码字节编码」的 WinZip AES 读取行为。
内容与密码完全相同，**唯一差别是写入时密码被编码成什么字节**：

| 文件 | 密码 `测试A123` 的字节 | 大小 |
|---|---|---|
| `aes-utf8.zip` | `E6 B5 8B E8 AF 95 41 31 32 33`（UTF-8） | 203 B |
| `aes-gbk.zip` | `B2 E2 CA D4 41 31 32 33`（GBK / CP936） | 203 B |

两个包内都只有一条 `样本.txt`（33 B，内容 `MantisZip password encoding test\n`），
格式为 WinZip AES-256 + Deflate（0x9901：`version=2`、`vendor=AE`、`strength=3`、`actualMethod=8`）。

## 生成脚本

issue #91 报告者给出的原脚本（`pyzipper`）：

```python
import pyzipper

for codec in ("gbk", "utf-8"):
    with pyzipper.AESZipFile(f"aes-{codec}.zip", "w",
        compression=pyzipper.ZIP_DEFLATED, encryption=pyzipper.WZ_AES) as z:
        z.setencryption(pyzipper.WZ_AES, nbits=256)
        z.setpassword("测试A123".encode(codec))
        z.writestr("样本.txt", "MantisZip password encoding test\n")
```

注意 `setpassword` 收的是 **bytes**，所以 `encode("gbk")` 与 `encode("utf-8")` 会写出
两个 verifier 与密钥都不同、但密码字符串相同的包。这正是缺陷的判定面。

## 期望行为（修复后）

- `ZipEntryAccess.OpenReadStream` / `WriteTo`：两个包都能解出 `样本.txt`，内容逐字节一致
- `ZipEntryAccess.VerifyAesPassword`：正确密码两个包都 `true`，错密码 `false`
- `ZipEngine.TestArchiveAsync`：两个包都 `true`
- 错密码：抛含 `password` 的 `InvalidFormatException`，不落成「数据损坏」

回归用例：`tests/MantisZip.Tests/ZipWinZipAesTests.cs`、
`tests/MantisZip.Tests/Engines/ZipEngineWinZipAesTests.cs`、
`tests/MantisZip.UI.Avalonia.Tests/PasswordServiceWinZipAesTests.cs`。
