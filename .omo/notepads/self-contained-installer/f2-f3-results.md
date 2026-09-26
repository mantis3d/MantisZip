# F2: ShellExt COM Compatibility — WARNING (2026-06-16)
Verdict: WARNING (not REJECT)
Recommendations (non-blocking):
1. Add verify step after self-contained publish in release.yml
2. Add MantisZip.ShellExt.runtimeconfig.json to self-contained installer
3. Add AfterTargets=""Publish"" to CopyShellExtComhost target

# F3: Scope Fidelity Check — PASS (2026-06-16)
TODOs: 3/3 compliant
Scope creep: CLEAN
Verdict: PASS
