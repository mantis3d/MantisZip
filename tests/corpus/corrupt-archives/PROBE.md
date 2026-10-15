# Corrupt Archives Probe Results
Generated: 2026-10-15 14:43:45

Corpus: `E:\github\corrupt-archives`

## Summary table

| File | Engine | Password | ListEntries | TestArchive | Extract |
|------|--------|----------|-------------|-------------|---------|
| corrupt-01-truncated-tail.zip | ZipEngine | (none) | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-01-truncated-tail.zip | ZipEngine | mantis | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-02-truncated-half.zip | ZipEngine | (none) | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-02-truncated-half.zip | ZipEngine | mantis | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-03-bad-crc.zip | ZipEngine | (none) | OK: 1 entries [payload.bin] | false | OK: 0 files, 0 B; failed=1 |
| corrupt-03-bad-crc.zip | ZipEngine | mantis | OK: 1 entries [payload.bin] | false | OK: 0 files, 0 B; failed=1 |
| corrupt-04-bad-deflate.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-04-bad-deflate.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-05-bad-header.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-05-bad-header.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-06-empty.zip | ZipEngine | (none) | EXC: ArchiveException: Could not find Zip file Directory at the end of the file. File may be corrupted. | false | EXC: ArchiveException: Could not find Zip file Directory at the end of the file. File may be corrupted. |
| corrupt-06-empty.zip | ZipEngine | mantis | EXC: ArchiveException: Could not find Zip file Directory at the end of the file. File may be corrupted. | false | EXC: ArchiveException: Could not find Zip file Directory at the end of the file. File may be corrupted. |
| corrupt-07-garbage.zip | ZipEngine | (none) | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-07-garbage.zip | ZipEngine | mantis | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| corrupt-08-7z-bad-header.7z | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-08-7z-bad-header.7z | SevenZipEngine | mantis | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-09-truncated.tar.gz | TarGzEngine | (none) | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 140.4 KB |
| corrupt-09-truncated.tar.gz | TarGzEngine | mantis | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 140.4 KB |
| corrupt-10-bad-gzip-magic.tar.gz | TarGzEngine | (none) | EXC: IncompleteArchiveException: Unexpected EOF reading tar file | false | EXC: IncompleteArchiveException: Unexpected EOF reading tar file |
| corrupt-10-bad-gzip-magic.tar.gz | TarGzEngine | mantis | EXC: IncompleteArchiveException: Unexpected EOF reading tar file | false | EXC: IncompleteArchiveException: Unexpected EOF reading tar file |
| corrupt-11-bad-gzip-stream.tar.gz | TarGzEngine | (none) | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 338.7 KB |
| corrupt-11-bad-gzip-stream.tar.gz | TarGzEngine | mantis | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 338.7 KB |
| corrupt-12-truncated.tar | TarGzEngine | (none) | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 200.9 KB |
| corrupt-12-truncated.tar | TarGzEngine | mantis | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 200.9 KB |
| corrupt-13-crc-field-flipped.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 13 files, 338.7 KB; failed=1 |
| corrupt-13-crc-field-flipped.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 13 files, 338.7 KB; failed=1 |
| corrupt-14-7z-truncated-tail.7z | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-14-7z-truncated-tail.7z | SevenZipEngine | mantis | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-15-7z-solid-data-flipped.7z | SevenZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-15-7z-solid-data-flipped.7z | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-16-7z-data-flipped.7z | SevenZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-16-7z-data-flipped.7z | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-17-zip-eocd-cd-offset-bad.zip | ZipEngine | (none) | OK: 0 entries [] | true | OK: 0 files, 0 B |
| corrupt-17-zip-eocd-cd-offset-bad.zip | ZipEngine | mantis | OK: 0 entries [] | true | OK: 0 files, 0 B |
| corrupt-18-zip-cd-local-offset-flipped.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-18-zip-cd-local-offset-flipped.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 14 files, 338.7 KB; failed=1 |
| corrupt-19-zip-bad-compression-method.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 13 files, 338.7 KB; failed=1 |
| corrupt-19-zip-bad-compression-method.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | false | OK: 13 files, 338.7 KB; failed=1 |
| corrupt-20-zip-size-tampered.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| corrupt-20-zip-size-tampered.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| corrupt-21-tar-checksum-field.tar | TarGzEngine | (none) | OK: 0 entries [] | true | OK: 0 files, 0 B |
| corrupt-21-tar-checksum-field.tar | TarGzEngine | mantis | OK: 0 entries [] | true | OK: 0 files, 0 B |
| corrupt-22-targz-inner-not-tar.tar.gz | TarGzEngine | (none) | EXC: InvalidFormatException: Not a tar file. | false | EXC: InvalidFormatException: Not a tar file. |
| corrupt-22-targz-inner-not-tar.tar.gz | TarGzEngine | mantis | EXC: InvalidFormatException: Not a tar file. | false | EXC: InvalidFormatException: Not a tar file. |
| corrupt-23-targz-inner-truncated.tar.gz | TarGzEngine | (none) | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 200.9 KB |
| corrupt-23-targz-inner-truncated.tar.gz | TarGzEngine | mantis | OK: 15 entries [., ./docs, ./note1.txt] | true | OK: 13 files, 200.9 KB |
| corrupt-24-rar-bad-signature.rar | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-24-rar-bad-signature.rar | SevenZipEngine | mantis | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| corrupt-25-rar-truncated.rar | SevenZipEngine | (none) | OK: 11 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-25-rar-truncated.rar | SevenZipEngine | mantis | OK: 11 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-26-rar-data-flipped.rar | SevenZipEngine | (none) | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-26-rar-data-flipped.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-27-rar-solid-data-flipped.rar | SevenZipEngine | (none) | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| corrupt-27-rar-solid-data-flipped.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| edge-01-gbk-filenames.zip | ZipEngine | (none) | OK: 3 entries [说明文件.txt, 子目录/数据.bin, 第二层/深层/文件.txt] | true | OK: 3 files, 95 B |
| edge-01-gbk-filenames.zip | ZipEngine | mantis | OK: 3 entries [说明文件.txt, 子目录/数据.bin, 第二层/深层/文件.txt] | true | OK: 3 files, 95 B |
| edge-02-zip-slip.zip | ZipEngine | (none) | OK: 4 entries [../evil.txt, ../../evil2.txt, /abs/evil3.txt] | true | EXC: InvalidOperationException: 压缩条目包含非法路径: ../evil.txt |
| edge-02-zip-slip.zip | ZipEngine | mantis | OK: 4 entries [../evil.txt, ../../evil2.txt, /abs/evil3.txt] | true | EXC: InvalidOperationException: 压缩条目包含非法路径: ../evil.txt |
| enc-01-zip-aes.zip | ZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: InvalidOperationException: 此压缩包已加密，请输入密码 (This archive is encrypted, password required) |
| enc-01-zip-aes.zip | ZipEngine | wrongpw | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | OK: 0 files, 0 B; failed=14 |
| enc-01-zip-aes.zip | ZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| enc-02-zip-aes-corrupt.zip | ZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: InvalidOperationException: 此压缩包已加密，请输入密码 (This archive is encrypted, password required) |
| enc-02-zip-aes-corrupt.zip | ZipEngine | wrongpw | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | OK: 0 files, 0 B; failed=14 |
| enc-02-zip-aes-corrupt.zip | ZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340.6 KB |
| enc-03-7z-encrypted.7z | SevenZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: InvalidOperationException: 此压缩包已加密，请输入密码 (This archive is encrypted, password required) |
| enc-03-7z-encrypted.7z | SevenZipEngine | wrongpw | OK: 15 entries [docs, docs/readme.md, note1.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| enc-03-7z-encrypted.7z | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| enc-04-7z-encrypt-headers.7z | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| enc-04-7z-encrypt-headers.7z | SevenZipEngine | wrongpw | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| enc-04-7z-encrypt-headers.7z | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| enc-05-rar-encrypted.rar | SevenZipEngine | (none) | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: InvalidOperationException: 此压缩包已加密，请输入密码 (This archive is encrypted, password required) |
| enc-05-rar-encrypted.rar | SevenZipEngine | wrongpw | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | false | EXC: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured. |
| enc-05-rar-encrypted.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| enc-06-rar-headers-encrypted.rar | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| enc-06-rar-headers-encrypted.rar | SevenZipEngine | wrongpw | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| enc-06-rar-headers-encrypted.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| mis-01-7z-renamed.zip | ZipEngine | (none) | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| mis-01-7z-renamed.zip | ZipEngine | mantis | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| valid-empty.zip | ZipEngine | (none) | OK: 0 entries [] | true | OK: 0 files, 0 B |
| valid-empty.zip | ZipEngine | mantis | OK: 0 entries [] | true | OK: 0 files, 0 B |
| valid-rar4.rar | SevenZipEngine | (none) | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| valid-rar4.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| valid.7z | SevenZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| valid.7z | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| valid.iso | SevenZipEngine | (none) | OK: 1 entries [README.TXT] | true | OK: 1 files, 480 B |
| valid.iso | SevenZipEngine | mantis | OK: 1 entries [README.TXT] | true | OK: 1 files, 480 B |
| valid.rar | SevenZipEngine | (none) | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| valid.rar | SevenZipEngine | mantis | OK: 15 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| valid.tar | TarGzEngine | (none) | OK: 16 entries [., ./docs, ./note1.txt] | true | OK: 14 files, 340 KB |
| valid.tar | TarGzEngine | mantis | OK: 16 entries [., ./docs, ./note1.txt] | true | OK: 14 files, 340 KB |
| valid.tar.gz | TarGzEngine | (none) | OK: 16 entries [., ./docs, ./note1.txt] | true | OK: 14 files, 340 KB |
| valid.tar.gz | TarGzEngine | mantis | OK: 16 entries [., ./docs, ./note1.txt] | true | OK: 14 files, 340 KB |
| valid.zip | ZipEngine | (none) | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| valid.zip | ZipEngine | mantis | OK: 14 entries [docs/readme.md, note1.txt, note10.txt] | true | OK: 14 files, 340 KB |
| vol-01-7z-split.001 | SevenZipEngine | (none) | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| vol-01-7z-split.001 | SevenZipEngine | mantis | OK: 15 entries [docs, docs/readme.md, note1.txt] | true | OK: 14 files, 340 KB |
| vol-01-7z-split.002 | null | (none) | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-01-7z-split.002 | null | mantis | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-01-7z-split.003 | null | (none) | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-01-7z-split.003 | null | mantis | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-02-7z-missing-part.001 | SevenZipEngine | (none) | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| vol-02-7z-missing-part.001 | SevenZipEngine | mantis | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... | false | EXC: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it i... |
| vol-03.zip.001 | ZipEngine | (none) | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| vol-03.zip.001 | ZipEngine | mantis | EXC: ArchiveException: Failed to locate the Zip Header | false | EXC: ArchiveException: Failed to locate the Zip Header |
| vol-03.zip.002 | null | (none) | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-03.zip.002 | null | mantis | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-03.zip.003 | null | (none) | SKIP: no engine | SKIP: no engine | SKIP: no engine |
| vol-03.zip.003 | null | mantis | SKIP: no engine | SKIP: no engine | SKIP: no engine |

## Raw exceptions

### 1. SharpSevenZipArchiveException — 24 occurrence(s)

- Full: SharpSevenZipArchiveException: Invalid archive: open/read error! Is it encrypted and a wrong password was provided? If your archive is an exotic one, it is possible that SharpSevenZipSharp has no signature for its format and thus decided it is TAR by mistake.
- Seen at: corrupt-08-7z-bad-header.7z [pw=(none)] ExtractAsync; corrupt-08-7z-bad-header.7z [pw=(none)] ListEntriesAsync; corrupt-08-7z-bad-header.7z [pw=mantis] ExtractAsync; corrupt-08-7z-bad-header.7z [pw=mantis] ListEntriesAsync; corrupt-14-7z-truncated-tail.7z [pw=(none)] ExtractAsync; corrupt-14-7z-truncated-tail.7z [pw=(none)] ListEntriesAsync; corrupt-14-7z-truncated-tail.7z [pw=mantis] ExtractAsync; corrupt-14-7z-truncated-tail.7z [pw=mantis] ListEntriesAsync; corrupt-24-rar-bad-signature.rar [pw=(none)] ExtractAsync; corrupt-24-rar-bad-signature.rar [pw=(none)] ListEntriesAsync; corrupt-24-rar-bad-signature.rar [pw=mantis] ExtractAsync; corrupt-24-rar-bad-signature.rar [pw=mantis] ListEntriesAsync; enc-04-7z-encrypt-headers.7z [pw=(none)] ExtractAsync; enc-04-7z-encrypt-headers.7z [pw=(none)] ListEntriesAsync; enc-04-7z-encrypt-headers.7z [pw=wrongpw] ExtractAsync; enc-04-7z-encrypt-headers.7z [pw=wrongpw] ListEntriesAsync; enc-06-rar-headers-encrypted.rar [pw=(none)] ExtractAsync; enc-06-rar-headers-encrypted.rar [pw=(none)] ListEntriesAsync; enc-06-rar-headers-encrypted.rar [pw=wrongpw] ExtractAsync; enc-06-rar-headers-encrypted.rar [pw=wrongpw] ListEntriesAsync; vol-02-7z-missing-part.001 [pw=(none)] ExtractAsync; vol-02-7z-missing-part.001 [pw=(none)] ListEntriesAsync; vol-02-7z-missing-part.001 [pw=mantis] ExtractAsync; vol-02-7z-missing-part.001 [pw=mantis] ListEntriesAsync

### 2. ArchiveException — 20 occurrence(s)

- Full: ArchiveException: Failed to locate the Zip Header
- Seen at: corrupt-01-truncated-tail.zip [pw=(none)] ExtractAsync; corrupt-01-truncated-tail.zip [pw=(none)] ListEntriesAsync; corrupt-01-truncated-tail.zip [pw=mantis] ExtractAsync; corrupt-01-truncated-tail.zip [pw=mantis] ListEntriesAsync; corrupt-02-truncated-half.zip [pw=(none)] ExtractAsync; corrupt-02-truncated-half.zip [pw=(none)] ListEntriesAsync; corrupt-02-truncated-half.zip [pw=mantis] ExtractAsync; corrupt-02-truncated-half.zip [pw=mantis] ListEntriesAsync; corrupt-07-garbage.zip [pw=(none)] ExtractAsync; corrupt-07-garbage.zip [pw=(none)] ListEntriesAsync; corrupt-07-garbage.zip [pw=mantis] ExtractAsync; corrupt-07-garbage.zip [pw=mantis] ListEntriesAsync; mis-01-7z-renamed.zip [pw=(none)] ExtractAsync; mis-01-7z-renamed.zip [pw=(none)] ListEntriesAsync; mis-01-7z-renamed.zip [pw=mantis] ExtractAsync; mis-01-7z-renamed.zip [pw=mantis] ListEntriesAsync; vol-03.zip.001 [pw=(none)] ExtractAsync; vol-03.zip.001 [pw=(none)] ListEntriesAsync; vol-03.zip.001 [pw=mantis] ExtractAsync; vol-03.zip.001 [pw=mantis] ListEntriesAsync

### 3. ExtractionFailedException — 12 occurrence(s)

- Full: ExtractionFailedException: Could not extract files! Message: File is corrupted. Data error has occured.
- Seen at: corrupt-15-7z-solid-data-flipped.7z [pw=(none)] ExtractAsync; corrupt-15-7z-solid-data-flipped.7z [pw=mantis] ExtractAsync; corrupt-16-7z-data-flipped.7z [pw=(none)] ExtractAsync; corrupt-16-7z-data-flipped.7z [pw=mantis] ExtractAsync; corrupt-25-rar-truncated.rar [pw=(none)] ExtractAsync; corrupt-25-rar-truncated.rar [pw=mantis] ExtractAsync; corrupt-26-rar-data-flipped.rar [pw=(none)] ExtractAsync; corrupt-26-rar-data-flipped.rar [pw=mantis] ExtractAsync; corrupt-27-rar-solid-data-flipped.rar [pw=(none)] ExtractAsync; corrupt-27-rar-solid-data-flipped.rar [pw=mantis] ExtractAsync; enc-03-7z-encrypted.7z [pw=wrongpw] ExtractAsync; enc-05-rar-encrypted.rar [pw=wrongpw] ExtractAsync

### 4. ArchiveException — 4 occurrence(s)

- Full: ArchiveException: Could not find Zip file Directory at the end of the file. File may be corrupted.
- Seen at: corrupt-06-empty.zip [pw=(none)] ExtractAsync; corrupt-06-empty.zip [pw=(none)] ListEntriesAsync; corrupt-06-empty.zip [pw=mantis] ExtractAsync; corrupt-06-empty.zip [pw=mantis] ListEntriesAsync

### 5. IncompleteArchiveException — 4 occurrence(s)

- Full: IncompleteArchiveException: Unexpected EOF reading tar file
- Seen at: corrupt-10-bad-gzip-magic.tar.gz [pw=(none)] ExtractAsync; corrupt-10-bad-gzip-magic.tar.gz [pw=(none)] ListEntriesAsync; corrupt-10-bad-gzip-magic.tar.gz [pw=mantis] ExtractAsync; corrupt-10-bad-gzip-magic.tar.gz [pw=mantis] ListEntriesAsync

### 6. InvalidFormatException — 4 occurrence(s)

- Full: InvalidFormatException: Not a tar file.
- Seen at: corrupt-22-targz-inner-not-tar.tar.gz [pw=(none)] ExtractAsync; corrupt-22-targz-inner-not-tar.tar.gz [pw=(none)] ListEntriesAsync; corrupt-22-targz-inner-not-tar.tar.gz [pw=mantis] ExtractAsync; corrupt-22-targz-inner-not-tar.tar.gz [pw=mantis] ListEntriesAsync

### 7. InvalidOperationException — 4 occurrence(s)

- Full: InvalidOperationException: 此压缩包已加密，请输入密码 (This archive is encrypted, password required)
- Seen at: enc-01-zip-aes.zip [pw=(none)] ExtractAsync; enc-02-zip-aes-corrupt.zip [pw=(none)] ExtractAsync; enc-03-7z-encrypted.7z [pw=(none)] ExtractAsync; enc-05-rar-encrypted.rar [pw=(none)] ExtractAsync

### 8. InvalidOperationException — 2 occurrence(s)

- Full: InvalidOperationException: 压缩条目包含非法路径: ../evil.txt
- Seen at: edge-02-zip-slip.zip [pw=(none)] ExtractAsync; edge-02-zip-slip.zip [pw=mantis] ExtractAsync

