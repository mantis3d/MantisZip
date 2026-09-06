# F1: Plan Compliance Audit — PASS (2026-06-16 04:31)

## Must Have: 4/4 ✅
1. Different AppId GUIDs ✅ — installer.iss: F7A3C8E1..., self-contained: 963001F3...
2. gh release create uploads both .exe ✅ — ForEach-Object FullName, not Select-Object -First 1
3. x64/x86 7z.dll retained ✅ — publish_output_selfcontained\x64\7z.dll and \x86\7z.dll
4. ShellExt COM files retained ✅ — wildcard *.dll catches both .dll and .comhost.dll

## Must NOT Have: 5/5 ✅
1. No PublishTrimmed ✅
2. No PublishReadyToRun ✅
3. No PublishSingleFile ✅
4. installer.iss unmodified ✅
5. Framework-dependent publish unmodified ✅
