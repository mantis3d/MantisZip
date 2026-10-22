#Requires -Version 7.0
<#
.SYNOPSIS
    MantisZip repository self-check: build/test diagnostics, three-language i18n
    key parity, and version consistency across all 6 declared locations.

.DESCRIPTION
    Single source of truth for the checks that used to live inside
    tools/dsh-plugin-mantiszip/index.mjs. Any harness (DSH plugin, opencode,
    Reasonix, CI, a human shell) can call this script; the DSH plugin is now
    only a thin wrapper that spawns this file with -Json and renders the result.

    Tasks:
        build    dotnet build <target>, parse errors/warnings
        test     dotnet test  <target>, parse errors/warnings. When the VSTest
                 testhost process crashes (restricted environments where it may
                 not open a handle to its parent process), the script falls back
                 to `dotnet exec <test assembly>` and runs the xunit v3 MTP entry
                 point directly, which needs no testhost.
        i18n     compare strings.zh-CN / strings.en / strings.zh-TW
        version  compare the 6 version locations listed in AGENTS.md
        check    version + i18n (pure file checks, no build, ~instant)
        guards   machine-checkable AGENTS.md rules: keybinding registration
                 (rule 10.1), main-window localization keys (rule 13),
                 StorageProvider ban (rule 16), icon registry (rule 8)

    Output:
        default   human readable Chinese text (matches the old tool output)
        -Json     one JSON document on stdout (field names match the DSH tool
                  output schemas, so the plugin can return it unchanged)

    Exit code: 0 when ok, 1 otherwise. CI can gate on it directly.

.PARAMETER Task
    Which check to run. Defaults to 'check'.

.PARAMETER Target
    Build/test target key: avalonia (default), core, shell, tests, ui-tests,
    solution. Ignored by i18n / version / check.

.PARAMETER ExtraArgs
    Extra arguments appended to the dotnet command line (build/test only).

.PARAMETER TimeoutMs
    Wall-clock budget for the dotnet child process. On expiry the process tree
    is killed and timedOut = true is reported.

.PARAMETER Root
    Repository root. When omitted the script looks at its own parent directory
    first (scripts/..), then walks up from the current directory.

.PARAMETER LocalizationDir
    Directory containing the three strings.*.json files, relative to -Root.

.PARAMETER Json
    Emit machine readable JSON instead of human text.

.EXAMPLE
    pwsh scripts\verify-repo.ps1 -Task check

.EXAMPLE
    pwsh scripts\verify-repo.ps1 -Task build -Target avalonia -Json

.NOTES
    Windows + PowerShell 7 only (see AGENTS.md rule 9). No dependency beyond the
    .NET SDK; nothing here writes to the repository.
#>

[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'i18n', 'version', 'check', 'guards')]
    [string]$Task = 'check',

    [ValidateSet('avalonia', 'core', 'shell', 'tests', 'ui-tests', 'solution')]
    [string]$Target = 'avalonia',

    [string[]]$ExtraArgs = @(),

    # Programmatic callers (the DSH plugin) pass extra dotnet arguments as a JSON
    # array in ONE token: the -File argument binder cannot carry values such as
    # "-c Release" or anything containing commas/quoting without re-parsing them.
    [string]$ExtraArgsJson,

    [int]$TimeoutMs = 600000,

    [string]$Root,

    [string]$LocalizationDir = 'src/MantisZip.UI.Avalonia/Localization',

    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# Merge the JSON-encoded extra arguments (plugin path) into the native array.
if (-not [string]::IsNullOrWhiteSpace($ExtraArgsJson)) {
    $parsedExtra = @($ExtraArgsJson | ConvertFrom-Json)
    $ExtraArgs = @($ExtraArgs) + @($parsedExtra | ForEach-Object { [string]$_ })
}

# ---------------------------------------------------------------------------
# Constants (mirror of the previous DSH plugin implementation)
# ---------------------------------------------------------------------------

$MAX_ITEMS = 100
$MAX_RAW_BYTES = 256 * 1024

$TARGETS = [ordered]@{
    'avalonia' = 'src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj'
    'core'     = 'src/MantisZip.Core/MantisZip.Core.csproj'
    'shell'    = 'src/MantisZip.ShellExt/MantisZip.ShellExt.csproj'
    'tests'    = 'tests/MantisZip.Tests/MantisZip.Tests.csproj'
    'ui-tests' = 'tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj'
    'solution' = 'MantisZip.sln'
}

# locale key -> file name. zh-CN is the baseline for key set and placeholders.
$I18N_FILES = [ordered]@{
    'zhCN' = 'strings.zh-CN.json'
    'en'   = 'strings.en.json'
    'zhTW' = 'strings.zh-TW.json'
}


# The 6 version locations from AGENTS.md "Version bump checklist".
# Keys are kept identical to the historical DSH tool output so consumers that
# already parse that JSON keep working unchanged.
# NOTE: the csproj pattern uses a lookbehind instead of a leading space so that
# <AssemblyVersion>/<FileVersion> never match but a line-start <Version> does.
$VERSION_SOURCES = @(
    @{ key = 'appConstants'; file = 'src/MantisZip.UI.Avalonia/AppConstants.cs'; pattern = 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"' }
    @{ key = 'csproj'; file = 'src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj'; pattern = '(?<![A-Za-z])<Version>([^<]+)</Version>' }
    @{ key = 'plan'; file = 'docs/PLAN.md'; pattern = '\*\*当前版本\*\*\s*[:：]\s*v?([0-9][0-9A-Za-z.+\-]*)' }
    @{ key = 'progress'; file = 'docs/PROGRESS.md'; pattern = '\*\*当前版本\*\*\s*[:：]\s*v?([0-9][0-9A-Za-z.+\-]*)' }
    @{ key = 'installer'; file = 'installer.iss'; pattern = '#define\s+MyAppVersion\s+"([^"]+)"' }
    @{ key = 'installerSelfContained'; file = 'installer-selfcontained.iss'; pattern = '#define\s+MyAppVersion\s+"([^"]+)"' }
)

$RE_FILE_DIAG = [regex]'^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*)$'
$RE_BARE_DIAG = [regex]'^(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*)$'
$RE_DOTNET_BARE = [regex]'^(?<severity>error|warning)\s*:\s*(?<message>.+)$'
$RE_PROJ_SUFFIX = [regex]'\s*\[[^\]]+\]\s*$'
# 项目级诊断（无行列号）：`C:\...\App.csproj : error NU1301: ...`；restore / NuGet 失败专属形状。
$RE_PROJ_DIAG = [regex]'^(?<file>.+?)\s*:\s+(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*)$'
$RE_JSON_KEY = [regex]'(?m)^\s*"((?:[^"\\]|\\.)*)"\s*:'

# VSTest testhost 进程崩溃的特征串。命中说明测试根本没跑起来（例如受限环境下
# testhost 无法 OpenProcess 打开父进程句柄 → Win32Exception 5 / 访问被拒绝），
# 此时才回退到 dotnet exec 直跑测试程序集。
$RE_TESTHOST_CRASH = [regex]'(?i)(testhost[^\r\n]{0,80}(crash|exited|exit code)|测试宿主[^\r\n]{0,40}(崩溃|退出)|Win32Exception|Failed to connect to the test host|无法创建测试宿主|Test Run Aborted)'

# VSTest 只会在测试真正跑起来后打印带计数的汇总行（Passed! / Failed! / Total: N）。
# 没有汇总行 ⇒ 用例一条都没执行，才值得用 MTP 兜底重跑；用例真的失败时必须保留原结果。
$RE_TEST_SUMMARY = [regex]'(?i)(Passed!\s+-|Failed!\s+-|测试运行成功|Total:\s*\d+,\s*(Errors|Failed)|总计:\s*\d+)'

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------

function Clip-Text {
    param([string]$Text, [int]$Max = 300)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    $value = $Text.Trim()
    if ($value.Length -le $Max) { return $value }
    return $value.Substring(0, $Max) + '…'
}

function Format-Seconds {
    param([int]$Milliseconds)
    return ('{0:0.0}' -f ($Milliseconds / 1000.0))
}

function Resolve-RepoRoot {
    param([string]$ExplicitRoot)

    $solution = 'MantisZip.sln'
    $tried = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        if (-not (Test-Path -LiteralPath $ExplicitRoot)) {
            throw "指定的 root 不存在：$ExplicitRoot"
        }
        $full = (Resolve-Path -LiteralPath $ExplicitRoot).ProviderPath
        if (-not (Test-Path -LiteralPath (Join-Path $full $solution))) {
            throw "指定的 root 不是 MantisZip 仓库（缺少 $solution）：$full"
        }
        return $full
    }

    $seeds = [System.Collections.Generic.List[string]]::new()
    # scripts.. is the strongest hint: this file lives in <repo>/scripts/.
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $seeds.Add((Split-Path -Parent $PSScriptRoot))
    }
    $seeds.Add((Get-Location).ProviderPath)

    foreach ($seed in $seeds) {
        $cursor = $seed
        for ($depth = 0; $depth -lt 8 -and -not [string]::IsNullOrEmpty($cursor); $depth++) {
            if (-not $tried.Contains($cursor)) { $tried.Add($cursor) }
            if (Test-Path -LiteralPath (Join-Path $cursor $solution)) {
                return (Resolve-Path -LiteralPath $cursor).ProviderPath
            }
            $parent = Split-Path -Parent $cursor
            if ([string]::IsNullOrEmpty($parent) -or $parent -eq $cursor) { break }
            $cursor = $parent
        }
    }

    throw ("找不到 MantisZip 仓库根目录（缺少 $solution）。已尝试：`n" + ($tried -join "`n"))
}

function Read-TextFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return [System.IO.File]::ReadAllText($Path)
}

# ---------------------------------------------------------------------------
# build / test
# ---------------------------------------------------------------------------

function Read-AsyncText {
    param($Task)
    if ($null -eq $Task) { return '' }
    # A killed process tree can leave a pipe open; never block the report on it.
    if ($Task.Wait(15000)) { return [string]$Task.Result }
    return ''
}

function ConvertFrom-BuildOutput {
    param([string[]]$Lines)

    $errors = [System.Collections.Generic.List[object]]::new()
    $warnings = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($raw in $Lines) {
        if ($null -eq $raw) { continue }
        $line = $raw.Trim()
        if ($line.Length -eq 0) { continue }

        # MSBuild appends "[C:\...\Foo.csproj]" to diagnostics; drop the suffix.
        $line = $RE_PROJ_SUFFIX.Replace($line, '').Trim()
        if ($line.Length -eq 0) { continue }

        $severity = $null
        $code = ''
        $message = ''
        $file = $null
        $lineNumber = $null
        $column = $null

        $match = $RE_FILE_DIAG.Match($line)
        if ($match.Success) {
            $file = $match.Groups['file'].Value.Trim()
            $lineNumber = [int]$match.Groups['line'].Value
            $column = [int]$match.Groups['column'].Value
            $severity = $match.Groups['severity'].Value
            $code = $match.Groups['code'].Value
            $message = $match.Groups['message'].Value.Trim()
        }
        else {
            # 项目级诊断没有行列号：`<path>.csproj : error NU1301: ...`（restore / NuGet 失败
            # 都是这个形状）。以前这条落不到任何规则里，restore 失败会显示成「0 错误」。
            $match = $RE_PROJ_DIAG.Match($line)
            if ($match.Success) {
                $file = $match.Groups['file'].Value.Trim()
                $severity = $match.Groups['severity'].Value
                $code = $match.Groups['code'].Value
                $message = $match.Groups['message'].Value.Trim()
            }
        }

        if ([string]::IsNullOrEmpty($severity)) {
            $match = $RE_BARE_DIAG.Match($line)
            if ($match.Success) {
                $severity = $match.Groups['severity'].Value
                $code = $match.Groups['code'].Value
                $message = $match.Groups['message'].Value.Trim()
            }
            else {
                $match = $RE_DOTNET_BARE.Match($line)
                if ($match.Success) {
                    $severity = $match.Groups['severity'].Value
                    $code = 'DOTNET'
                    $message = $match.Groups['message'].Value.Trim()
                }
            }
        }

        if ([string]::IsNullOrEmpty($severity)) { continue }

        $signature = '{0}|{1}|{2}|{3}|{4}' -f $file, $lineNumber, $column, $code, $message
        if (-not $seen.Add($signature)) { continue }

        $item = [ordered]@{
            file    = $file
            line    = $lineNumber
            column  = $column
            code    = $code
            message = $message
        }

        if ($severity -eq 'error') { $errors.Add($item) } else { $warnings.Add($item) }
    }

    return @{ errors = $errors; warnings = $warnings }
}

# 找出测试工程生成的可执行程序集（xunit v3 的 MTP 入口就在测试 dll 里）。
# bin/ 下的 ref/ 目录是引用程序集，不能 exec，必须排除。
function Get-ConfigurationFromArgs {
    param([string[]]$Arguments)

    if (-not $Arguments) { return $null }

    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $argument = [string]$Arguments[$index]
        # 分开写：-c Release / --configuration Release
        if (($argument -eq '-c' -or $argument -eq '--configuration') -and ($index + 1) -lt $Arguments.Count) {
            return [string]$Arguments[$index + 1]
        }
        # 连写：-c:Release / --configuration=Release / -p:Configuration=Release
        if ($argument -match '^(?:-c|--configuration)[:=](?<value>.+)$') { return $Matches['value'] }
        if ($argument -match '^-p:Configuration=(?<value>.+)$') { return $Matches['value'] }
    }

    return $null
}

function Get-TestAssemblyPath {
    param(
        [string]$RepoRoot,
        [string]$ProjectPath,
        [string]$Configuration
    )

    $projectFull = Join-Path $RepoRoot $ProjectPath
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($projectFull)
    $binDir = Join-Path (Split-Path -Parent $projectFull) 'bin'
    if (-not (Test-Path -LiteralPath $binDir)) { return $null }

    $candidates = @(
        Get-ChildItem -LiteralPath $binDir -Recurse -Filter "$assemblyName.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]ref[\\/]' }
    )
    if ($candidates.Count -eq 0) { return $null }

    # 显式配置优先：传了 -c Release 时只认 bin/Release/**，否则 MTP 兜底可能跑到
    # 上一次 Debug 构建留下的旧程序集上——测试照样「通过」，测的却是旧二进制。
    # 目标配置还没有产物时返回 $null，让调用方先按同样参数构建再取一次。
    if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
        $configurationSegment = "[\\/]$([regex]::Escape($Configuration))[\\/]"
        $scoped = @($candidates | Where-Object { $_.FullName -match $configurationSegment })
        if ($scoped.Count -eq 0) { return $null }
        $candidates = $scoped
    }

    $newest = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    return $newest.FullName
}

function Invoke-VerifyDotnet {
    param(
        [string]$RepoRoot,
        [string]$Action,
        [string]$ProjectPath,
        [string[]]$AdditionalArgs,
        [int]$BudgetMs,
        [string[]]$ArgvOverride,
        # 结果里回显的 target 别名。显式传参而不是靠动态作用域捕获脚本级 $Target。
        [string]$TargetName
    )

    if ($ArgvOverride) {
        # MTP 兜底：dotnet exec <测试程序集> 直接把命令行交给测试程序集自己解析，
        # 因此不能追加 -nologo 之类的 MSBuild 参数，也不能追加 -c Release 之类的
        # dotnet 参数（会被 xunit 当成未知选项）；配置由程序集所在 bin 目录决定。
        $argv = @($ArgvOverride)
    }
    else {
        $argv = @($Action, $ProjectPath, '-nologo', '-nodeReuse:false', '-v:m')
        if ($Action -eq 'build') { $argv += '-clp:NoSummary' }
        if ($AdditionalArgs) { $argv += $AdditionalArgs }
    }

    $truncated = $false
    $stdoutText = ''
    $stderrText = ''

    $previousCulture = $env:DOTNET_CLI_UI_LANGUAGE
    $previousVsLang = $env:VSLANG
    $previousNoLogo = $env:DOTNET_NOLOGO
    $previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT

    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $env:VSLANG = '1033'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $exitCode = -1

    try {
        if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) {
            throw '找不到 dotnet 命令，请确认已安装 .NET SDK 并在 PATH 中。'
        }

        # NOTE: Start-Process -PassThru never populates ExitCode when stdout/stderr
        # are redirected (verified on PowerShell 7 / Windows: ExitCode stays $null
        # even after WaitForExit). Driving System.Diagnostics.Process directly also
        # gives proper argv escaping via ArgumentList and lets us read both streams
        # asynchronously, so no temp files are needed.
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'dotnet'
        $startInfo.WorkingDirectory = $RepoRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
        foreach ($argument in $argv) { $startInfo.ArgumentList.Add([string]$argument) }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            [void]$process.Start()
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()

            if ($process.WaitForExit($BudgetMs)) {
                $exitCode = [int]$process.ExitCode
            }
            else {
                $timedOut = $true
                try { $process.Kill($true) } catch { }
                try { [void]$process.WaitForExit(10000) } catch { }
            }

            $stdoutText = Read-AsyncText -Task $stdoutTask
            $stderrText = Read-AsyncText -Task $stderrTask
        }
        finally {
            $process.Dispose()
        }
    }
    finally {
        $stopwatch.Stop()
        $env:DOTNET_CLI_UI_LANGUAGE = $previousCulture
        $env:VSLANG = $previousVsLang
        $env:DOTNET_NOLOGO = $previousNoLogo
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    }

    if ($stdoutText.Length -gt $MAX_RAW_BYTES) {
        $stdoutText = $stdoutText.Substring(0, $MAX_RAW_BYTES)
        $truncated = $true
    }

    $allLines = @(
        ($stdoutText -split "`r?`n")
        ($stderrText -split "`r?`n")
    ) | Where-Object { $_.Length -gt 0 }

    $parsed = ConvertFrom-BuildOutput -Lines @($allLines)
    $errors = $parsed.errors
    $warnings = $parsed.warnings

    $okValue = ($exitCode -eq 0 -and $errors.Count -eq 0)
    $runner = if ($ArgvOverride) { 'mtp' } else { 'vstest' }

    $hostCrashed = $false
    $testsReported = $false
    if ($Action -eq 'test' -and -not $ArgvOverride) {
        $probe = $stdoutText + "`n" + $stderrText
        $hostCrashed = $RE_TESTHOST_CRASH.IsMatch($probe)
        $testsReported = $RE_TEST_SUMMARY.IsMatch($probe)
    }

    # 测试失败时 dotnet test 往往不产生 file(line,col) 诊断，错误列表会是空的；
    # 保留输出尾部，让「哪些用例失败 / 为什么跑不起来」至少看得见。走 MTP 兜底时
    # 无论成败都保留尾部 —— 那一行 "Total: N, Failed: 0" 是唯一能自证跑过多少用例的东西。
    $tail = @()
    if ($Action -eq 'test' -and (-not $okValue -or $ArgvOverride)) {
        $tail = @($allLines | Select-Object -Last 20 | ForEach-Object { Clip-Text $_ 200 })
    }

    $payload = [ordered]@{
        ok           = $okValue
        target       = $TargetName
        action       = $Action
        runner       = $runner
        hostCrashed  = $hostCrashed
        testsReported = $testsReported
        exitCode     = $exitCode
        durationMs   = [int]$stopwatch.Elapsed.TotalMilliseconds
        command      = 'dotnet ' + ($argv -join ' ')
        errorCount   = $errors.Count
        warningCount = $warnings.Count
        truncated    = $truncated
        timedOut     = $timedOut
        aborted      = $false
        errors       = @($errors | Select-Object -First $MAX_ITEMS)
        warnings     = @($warnings | Select-Object -First $MAX_ITEMS)
        tail         = @($tail)
    }

    return $payload
}

function Format-BuildResult {
    param($Result)

    $lines = [System.Collections.Generic.List[string]]::new()
    $verb = if ($Result.action -eq 'build') { '构建' } else { '测试' }
    $seconds = Format-Seconds $Result.durationMs

    if ($Result.ok) {
        $lines.Add("✅ ${verb}通过：$($Result.target)（${seconds}s，$($Result.warningCount) 警告）")
    }
    else {
        $lines.Add("❌ ${verb}失败：$($Result.target)（${seconds}s，$($Result.errorCount) 错误 / $($Result.warningCount) 警告，exit $($Result.exitCode)）")
    }

    if ($Result.runner -eq 'mtp') {
        $lines.Add('ℹ️ VSTest testhost 不可用，已回退 dotnet exec 直跑测试程序集（xunit MTP）。')
    }

    if (@($Result.errors).Count -gt 0) {
        $lines.Add('')
        $lines.Add('错误：')
        foreach ($entry in $Result.errors) {
            $where = if (-not $entry.file) { '(项目级)' } elseif ($null -eq $entry.line) { $entry.file } else { "$($entry.file)($($entry.line),$($entry.column))" }
            $label = if ($entry.code) { "$($entry.code): " } else { '' }
            $lines.Add("- $where $label$(Clip-Text $entry.message 300)")
        }
        $hidden = [int]$Result.errorCount - @($Result.errors).Count
        if ($hidden -gt 0) { $lines.Add("- …已省略 $hidden 条") }
    }

    if (@($Result.warnings).Count -gt 0) {
        $lines.Add('')
        $lines.Add('警告：')
        $shown = 0
        foreach ($entry in $Result.warnings) {
            if ($shown -ge 10) { break }
            $where = if (-not $entry.file) { '(项目级)' } elseif ($null -eq $entry.line) { $entry.file } else { "$($entry.file)($($entry.line),$($entry.column))" }
            $label = if ($entry.code) { "$($entry.code): " } else { '' }
            $lines.Add("- $where $label$(Clip-Text $entry.message 300)")
            $shown++
        }
        if ([int]$Result.warningCount -gt $shown) {
            $lines.Add("- …共 $($Result.warningCount) 条警告")
        }
    }

    if (@($Result.tail).Count -gt 0) {
        $lines.Add('')
        $lines.Add('输出尾部：')
        foreach ($entry in $Result.tail) { $lines.Add("- $entry") }
    }

    if ($Result.timedOut) {
        $lines.Add('')
        $lines.Add("⚠️ ${verb}超时，已强制终止（可调大 timeoutMs 或减少项目范围）。")
    }
    if ($Result.aborted) {
        $lines.Add('')
        $lines.Add('⚠️ 调用被取消。')
    }
    if ($Result.truncated) {
        $lines.Add('')
        $lines.Add('⚠️ 输出过长已截断，诊断可能不完整。')
    }

    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------
# i18n
# ---------------------------------------------------------------------------

function Get-PlaceholderIndexes {
    param([string]$Value)
    $set = [System.Collections.Generic.SortedSet[int]]::new()
    if ([string]::IsNullOrEmpty($Value)) { return $set }
    foreach ($match in [regex]::Matches($Value, '\{(\d+)')) {
        [void]$set.Add([int]$match.Groups[1].Value)
    }
    return $set
}

function Test-I18n {
    param([string]$RepoRoot, [string]$RelativeDir)

    $directory = if ([System.IO.Path]::IsPathRooted($RelativeDir)) { $RelativeDir } else { Join-Path $RepoRoot $RelativeDir }
    $documents = @{}
    $keyCounts = [ordered]@{}

    foreach ($locale in $I18N_FILES.Keys) {
        $path = Join-Path $directory $I18N_FILES[$locale]
        if (-not (Test-Path -LiteralPath $path)) {
            throw "读不到语言文件：$path"
        }
        $raw = [System.IO.File]::ReadAllText($path)
        if ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) { $raw = $raw.Substring(1) }

        try {
            $data = $raw | ConvertFrom-Json -AsHashtable
        }
        catch {
            throw "解析失败：$path`n$($_.Exception.Message)"
        }

        $documents[$locale] = @{ raw = $raw; data = $data }
        $keyCounts[$locale] = $data.Count
    }

    $baseline = 'zhCN'
    $baselineKeys = @($documents[$baseline].data.Keys)

    $missing = [ordered]@{}
    $extra = [ordered]@{}
    foreach ($locale in @('en', 'zhTW')) {
        $keys = @($documents[$locale].data.Keys)
        $missing[$locale] = @($baselineKeys | Where-Object { -not $documents[$locale].data.Contains($_) })
        $extra[$locale] = @($keys | Where-Object { -not $documents[$baseline].data.Contains($_) })
    }

    $placeholderMismatch = [System.Collections.Generic.List[object]]::new()
    $duplicates = [System.Collections.Generic.List[object]]::new()
    $emptyValues = [System.Collections.Generic.List[object]]::new()

    foreach ($locale in $I18N_FILES.Keys) {
        $data = $documents[$locale].data

        # Duplicate keys can only be seen in the raw text: JSON parsing folds them.
        $seenKey = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($match in $RE_JSON_KEY.Matches($documents[$locale].raw)) {
            $key = $match.Groups[1].Value
            if (-not $seenKey.Add($key)) {
                $duplicates.Add([ordered]@{ locale = $locale; key = $key })
            }
        }

        foreach ($key in $data.Keys) {
            $value = $data[$key]
            if ($value -is [string] -and $value.Trim().Length -eq 0) {
                $emptyValues.Add([ordered]@{ locale = $locale; key = $key })
            }
        }
    }

    # Placeholders are compared against the zh-CN baseline, in baseline key order.
    foreach ($locale in @('en', 'zhTW')) {
        foreach ($key in $baselineKeys) {
            $value = $documents[$locale].data[$key]
            if ($value -isnot [string]) { continue }

            $expectedSource = $documents[$baseline].data[$key]
            if ($expectedSource -isnot [string]) { continue }

            $expected = Get-PlaceholderIndexes $expectedSource
            $actual = Get-PlaceholderIndexes $value
            if ((@($expected) -join ',') -ne (@($actual) -join ',')) {
                $placeholderMismatch.Add([ordered]@{
                        key      = $key
                        locale   = $locale
                        expected = @($expected)
                        actual   = @($actual)
                    })
            }
        }
    }

    $issueCount = 0
    foreach ($locale in @('en', 'zhTW')) {
        $issueCount += @($missing[$locale]).Count
        $issueCount += @($extra[$locale]).Count
    }
    $issueCount += $placeholderMismatch.Count
    $issueCount += $duplicates.Count
    # emptyValues is informational only and intentionally NOT part of issueCount.

    $payload = [ordered]@{
        ok                  = ($issueCount -eq 0)
        keyCounts           = $keyCounts
        missing             = $missing
        extra               = $extra
        placeholderMismatch = @($placeholderMismatch)
        duplicates          = @($duplicates)
        emptyValues         = @($emptyValues)
        issueCount          = $issueCount
    }

    return $payload
}

function Format-I18nResult {
    param($Result)

    $lines = [System.Collections.Generic.List[string]]::new()
    $baselineCount = $Result.keyCounts['zhCN']

    if ($Result.ok) {
        $lines.Add("✅ 三语一致：$baselineCount 个 key（en / zh-TW 无缺失、无多余、占位符一致）")
    }
    else {
        $lines.Add("❌ 三语不一致：$($Result.issueCount) 个问题（基准 zh-CN 共 $baselineCount 个 key）")
    }

    foreach ($locale in @('en', 'zhTW')) {
        $label = $locale
        $items = @($Result.missing[$locale])
        if ($items.Count -gt 0) {
            $lines.Add('')
            $lines.Add("缺失（$label 少 $($items.Count) 个）：")
            $shown = 0
            foreach ($key in $items) {
                if ($shown -ge 20) { break }
                $lines.Add("- $key")
                $shown++
            }
            if ($items.Count -gt $shown) { $lines.Add("- …共 $($items.Count) 个") }
        }
    }

    foreach ($locale in @('en', 'zhTW')) {
        $label = $locale
        $items = @($Result.extra[$locale])
        if ($items.Count -gt 0) {
            $lines.Add('')
            $lines.Add("多余（$label 多 $($items.Count) 个）：")
            $shown = 0
            foreach ($key in $items) {
                if ($shown -ge 20) { break }
                $lines.Add("- $key")
                $shown++
            }
            if ($items.Count -gt $shown) { $lines.Add("- …共 $($items.Count) 个") }
        }
    }

    $mismatches = @($Result.placeholderMismatch)
    if ($mismatches.Count -gt 0) {
        $lines.Add('')
        $lines.Add("占位符不一致（$($mismatches.Count) 个）：")
        foreach ($entry in $mismatches) {
            $expected = @($entry.expected) -join '},{'
            $actual = @($entry.actual) -join '},{'
            $lines.Add("- $($entry.key): zh-CN {$expected} vs $($entry.locale) {$actual}")
        }
    }

    $duplicates = @($Result.duplicates)
    if ($duplicates.Count -gt 0) {
        $lines.Add('')
        $lines.Add("重复 key（$($duplicates.Count) 个）：")
        $shown = 0
        foreach ($entry in $duplicates) {
            if ($shown -ge 10) { break }
            $lines.Add("- $($entry.locale): $($entry.key)")
            $shown++
        }
        if ($duplicates.Count -gt $shown) { $lines.Add("- …共 $($duplicates.Count) 个") }
    }

    $empties = @($Result.emptyValues)
    if ($empties.Count -gt 0) {
        $lines.Add('')
        $lines.Add("空值（$($empties.Count) 个，仅提示）：")
        $shown = 0
        foreach ($entry in $empties) {
            if ($shown -ge 10) { break }
            $lines.Add("- $($entry.locale): $($entry.key)")
            $shown++
        }
        if ($empties.Count -gt $shown) { $lines.Add("- …共 $($empties.Count) 个") }
    }

    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------
# version
# ---------------------------------------------------------------------------

function Test-VersionConsistency {
    param([string]$RepoRoot)

    $versions = [ordered]@{}
    $missingSources = [System.Collections.Generic.List[string]]::new()

    foreach ($source in $VERSION_SOURCES) {
        $path = Join-Path $RepoRoot $source.file
        $text = Read-TextFile $path

        if ($null -eq $text) {
            $missingSources.Add("$($source.key)（读不到 $($source.file)）")
            continue
        }

        $match = [regex]::Match($text, $source.pattern)
        if (-not $match.Success -or $match.Groups.Count -lt 2) {
            $missingSources.Add($source.key)
            continue
        }

        $value = $match.Groups[1].Value.Trim()
        $value = $value -replace '[.。,，]$', ''
        $versions[$source.key] = $value
    }

    $unique = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $versions.Values) {
        if ([string]::IsNullOrEmpty($value)) { continue }
        if (-not $unique.Contains($value)) { $unique.Add($value) }
    }

    $payload = [ordered]@{
        ok             = ($missingSources.Count -eq 0 -and $unique.Count -eq 1)
        versions       = $versions
        unique         = @($unique)
        missingSources = @($missingSources)
    }

    return $payload
}

function Format-VersionResult {
    param($Result)

    $lines = [System.Collections.Generic.List[string]]::new()
    $unique = @($Result.unique)
    $missingSources = @($Result.missingSources)

    if ($Result.ok) {
        $lines.Add("✅ 版本号一致：$($unique[0])（6 处全部匹配）")
    }
    else {
        $lines.Add("❌ 版本号不一致：发现 $($unique.Count) 个不同取值 / $($missingSources.Count) 处缺失")
    }

    $lines.Add('')
    $lines.Add('| 位置 | 取值 |')
    $lines.Add('| --- | --- |')
    foreach ($key in $Result.versions.Keys) {
        $lines.Add("| $key | $($Result.versions[$key]) |")
    }

    if ($missingSources.Count -gt 0) {
        $lines.Add('')
        $lines.Add("未匹配到版本号：$($missingSources -join ', ')")
    }

    if (-not $Result.ok -and $unique.Count -gt 1) {
        $lines.Add('')
        $lines.Add("不同取值：$($unique -join ' / ')")
    }

    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------
# guards: 可机器验证的 AGENTS.md 规则子集（规则 8 / 10.1 / 13 / 16）
#
# 这些规则此前只有「人肉自查」或「构建不报错」兜底，踩过坑：
#   - 规则 10.1：MenuItem.InputGesture 只显示不生效（2026-10-21 修复 5 个）
#   - 规则 13：keys 数组漏登记 → 菜单文字空白，构建不报错
#   - 规则 16：StorageProvider 原生对话框必须走 CustomFilePickerDialog
#   - 规则 8：新增 AppIcons.axaml 图标必须登记到 IconTestViewModel
# ---------------------------------------------------------------------------

$GUARD_FILES = [ordered]@{
    keyBindingsCode = 'src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs'
    keyBindingsXaml = 'src/MantisZip.UI.Avalonia/Views/MainWindow.axaml'
    keyBindingsTest = 'tests/MantisZip.UI.Avalonia.Tests/MainWindowKeyBindingsTests.cs'
    mainWindowXaml  = 'src/MantisZip.UI.Avalonia/Views/MainWindow.axaml'
    filterBarXaml   = 'src/MantisZip.UI.Avalonia/Controls/MainFilterBar.axaml'
    mainViewModel   = 'src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs'
    appIcons        = 'src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml'
    iconTestVm      = 'src/MantisZip.UI.Avalonia/ViewModels/IconTestViewModel.cs'
    filePicker      = 'src/MantisZip.UI.Avalonia/Dialogs/CustomFilePickerDialog.axaml.cs'
    sourceDir       = 'src'
}

# 登记形态两种都覆盖：KeyModifiers.Control 与 KeyModifiers.Control | KeyModifiers.Shift
$RE_KEYBINDING_ADD = [regex]::new('Add\(\s*Key\.(\w+)\s*,\s*(?<mods>[^,]+?)\s*,\s*[\w.()]+\s*\)')
$RE_INPUT_GESTURE = [regex]::new('InputGesture="([^"]+)"')
$RE_LOCALIZED_BINDING = [regex]::new('LocalizedStrings\[([^\]]+)\]')
$RE_QUOTED_STRING = [regex]::new('"((?:[^"\\]|\\.)+)"')
$RE_FILE_PICKER_API = [regex]::new('\b(OpenFilePickerAsync|SaveFilePickerAsync|OpenFolderPickerAsync)\s*\(')
$RE_APP_ICON_KEY = [regex]::new('x:Key="([^"]+)"')
$RE_ICON_RESOURCE_REF = [regex]::new('"Icon[A-Z][A-Za-z0-9]*"')
$RE_SIGNED_METHOD = [regex]::new('(?m)^[ \t]*(?:private|protected|public|internal)[^\r\n;=]*?\b([A-Za-z_]\w*)\s*\(')

# 文件选择对话框的唯一豁免入口（规则 16）
$FILE_PICKER_WHITELIST_METHOD = 'SystemBrowse_Click'

function Get-LineNumber {
    param([string]$Text, [int]$Index)
    if ($Index -le 0) { return 1 }
    return ($Text.Substring(0, $Index) -split "`r?`n").Count
}

function ConvertTo-KeyGestureText {
    param([string]$KeyName, [string]$Modifiers)
    # 与 Avalonia KeyGesture.ToString() 的修饰键顺序一致：Ctrl+Shift+Alt+Win
    $parts = [System.Collections.Generic.List[string]]::new()
    if ($Modifiers -match '(?i)\bControl\b') { $parts.Add('Ctrl') }
    if ($Modifiers -match '(?i)\bShift\b') { $parts.Add('Shift') }
    if ($Modifiers -match '(?i)\bAlt\b') { $parts.Add('Alt') }
    if ($Modifiers -match '(?i)\b(?:Meta|Win)\b') { $parts.Add('Win') }
    $parts.Add($KeyName)
    return ($parts -join '+')
}

function ConvertTo-CanonicalGesture {
    param([string]$Raw)
    # 菜单里把逗号键写作 "Ctrl+,，KeyGesture.ToString() 是 "Ctrl+OemComma"
    $value = $Raw.Trim()
    if ($value.EndsWith(',')) {
        $value = $value.Substring(0, $value.Length - 1) + 'OemComma'
    }
    return $value
}

function Get-KeyBindingGestureList {
    param([string]$Code)
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($match in $RE_KEYBINDING_ADD.Matches($Code)) {
        $list.Add((ConvertTo-KeyGestureText -KeyName $match.Groups[1].Value -Modifiers $match.Groups[2].Value))
    }
    return @($list)
}

function Get-XamlInputGestureList {
    param([string]$Xaml)
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($match in $RE_INPUT_GESTURE.Matches($Xaml)) {
        $list.Add((ConvertTo-CanonicalGesture $match.Groups[1].Value))
    }
    return @($list)
}

function Get-ExpectedGestureList {
    param([string]$TestCode)
    $block = [regex]::Match($TestCode, 'string\[\] expected\s*=\s*\{(?<body>.*?)\}\s*;', 'Singleline')
    if (-not $block.Success) { return @() }
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($match in $RE_QUOTED_STRING.Matches($block.Groups['body'].Value)) {
        $value = $match.Groups[1].Value
        # 只收手势样式（英文/数字/+），中文注释或说明自然排除
        if ($value -notmatch '^[A-Za-z0-9+]+$') { continue }
        $list.Add($value)
    }
    return @($list)
}

function Get-RegisteredViewModelKeys {
    param([string]$Code)
    $block = [regex]::Match($Code, 'var keys = new\[\]\s*\{(?<body>.*?)\};', 'Singleline')
    if (-not $block.Success) { return @() }
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($match in $RE_QUOTED_STRING.Matches($block.Groups['body'].Value)) {
        $list.Add($match.Groups[1].Value)
    }
    return @($list)
}

function Get-JsonKeySet {
    param([string]$RepoRoot, [string]$RelativeDir)
    $directory = if ([System.IO.Path]::IsPathRooted($RelativeDir)) { $RelativeDir } else { Join-Path $RepoRoot $RelativeDir }
    $path = Join-Path $directory $I18N_FILES['zhCN']
    $raw = [System.IO.File]::ReadAllText($path)
    if ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) { $raw = $raw.Substring(1) }
    $data = $raw | ConvertFrom-Json -AsHashtable
    return @($data.Keys)
}

function Get-EnclosingMethodName {
    param([string]$Text, [int]$TargetLine)
    $current = $null
    foreach ($match in $RE_SIGNED_METHOD.Matches($Text)) {
        $line = Get-LineNumber -Text $Text -Index $match.Index
        if ($line -gt $TargetLine) { break }
        $current = $match.Groups[1].Value
    }
    return $current
}

function New-GuardResult {
    param([string]$Id, [string]$Title, [bool]$Ok, $Issues, $Details)
    return [ordered]@{
        id      = $Id
        title   = $Title
        ok      = $Ok
        issues  = @($Issues)
        details = $Details
    }
}

# 规则 10.1：菜单显示的手势必须真的登记在 MainWindow.KeyBindings 上
function Test-GuardKeyBindings {
    param([string]$RepoRoot)

    $issues = [System.Collections.Generic.List[string]]::new()
    $code = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.keyBindingsCode)
    $xaml = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.keyBindingsXaml)
    $test = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.keyBindingsTest)

    if ($null -eq $code -or $null -eq $xaml -or $null -eq $test) {
        $issues.Add('读不到快捷键相关文件（MainWindow.axaml.cs / MainWindow.axaml / MainWindowKeyBindingsTests.cs）')
        return New-GuardResult -Id 'keybindings' -Title '快捷键登记（规则 10.1）' -Ok $false -Issues $issues -Details ([ordered]@{})
    }

    $registered = Get-KeyBindingGestureList $code
    $shown = Get-XamlInputGestureList $xaml
    $expected = Get-ExpectedGestureList $test

    foreach ($gesture in ($shown | Sort-Object -Unique)) {
        if ($registered -notcontains $gesture) {
            $issues.Add("菜单 InputGesture 显示 $gesture，但 MainWindow.KeyBindings 未登记 → 只显示不生效")
        }
    }
    foreach ($gesture in ($expected | Sort-Object -Unique)) {
        if ($registered -notcontains $gesture) {
            $issues.Add("测试期望 $gesture，但 MainWindow.KeyBindings 未登记")
        }
    }
    foreach ($gesture in ($registered | Sort-Object -Unique)) {
        if ($expected -notcontains $gesture) {
            $issues.Add("KeyBindings 已登记 $gesture，但 MainWindowKeyBindingsTests 期望清单未同步")
        }
    }
    if ($registered.Count -ne ($registered | Sort-Object -Unique).Count) {
        $issues.Add('KeyBindings 存在重复的手势登记')
    }

    return New-GuardResult -Id 'keybindings' -Title '快捷键登记（规则 10.1）' -Ok ($issues.Count -eq 0) -Issues $issues -Details ([ordered]@{
            registered    = @($registered | Sort-Object -Unique)
            shownInMenu   = @($shown | Sort-Object -Unique)
            expectedTests = @($expected | Sort-Object -Unique)
        })
}

# 规则 13：主窗口 XAML 绑定的 LocalizedStrings key 必须登记且三语有文案
function Test-GuardI18nBindings {
    param([string]$RepoRoot, [string]$RelativeLocalizationDir)

    $issues = [System.Collections.Generic.List[string]]::new()
    $bound = [System.Collections.Generic.List[string]]::new()

    foreach ($relative in @($GUARD_FILES.mainWindowXaml, $GUARD_FILES.filterBarXaml)) {
        $text = Read-TextFile (Join-Path $RepoRoot $relative)
        if ($null -eq $text) {
            $issues.Add("读不到 $relative")
            continue
        }
        foreach ($match in $RE_LOCALIZED_BINDING.Matches($text)) {
            $key = $match.Groups[1].Value.Trim()
            if (-not $bound.Contains($key)) { $bound.Add($key) }
        }
    }

    $viewModel = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.mainViewModel)
    if ($null -eq $viewModel) {
        $issues.Add("读不到 $($GUARD_FILES.mainViewModel)")
        return New-GuardResult -Id 'i18n-bindings' -Title '主窗口绑定 key 登记（规则 13）' -Ok $false -Issues $issues -Details ([ordered]@{})
    }

    $registered = Get-RegisteredViewModelKeys $viewModel
    $jsonKeys = Get-JsonKeySet -RepoRoot $RepoRoot -RelativeDir $RelativeLocalizationDir

    foreach ($key in ($bound | Sort-Object)) {
        if ($registered -notcontains $key) {
            $issues.Add("$GUARD_FILES.mainWindowXaml 绑定 $key，但 UpdateLocalizedStrings() keys 数组未登记 → 运行时文字空白且构建不报错")
        }
    }
    foreach ($key in ($registered | Sort-Object)) {
        if ($jsonKeys -notcontains $key) {
            $issues.Add("UpdateLocalizedStrings() 登记 $key，但三语 JSON 缺少该 key")
        }
    }

    return New-GuardResult -Id 'i18n-bindings' -Title '主窗口绑定 key 登记（规则 13）' -Ok ($issues.Count -eq 0) -Issues $issues -Details ([ordered]@{
            boundCount      = $bound.Count
            registeredCount = $registered.Count
            jsonKeyCount    = $jsonKeys.Count
        })
}

# 规则 16：禁止原生 StorageProvider，唯一豁免 CustomFilePickerDialog.SystemBrowse_Click
function Test-GuardFilePicker {
    param([string]$RepoRoot)

    $issues = [System.Collections.Generic.List[string]]::new()
    $sourceDir = Join-Path $RepoRoot $GUARD_FILES.sourceDir
    if (-not (Test-Path -LiteralPath $sourceDir)) {
        $issues.Add("读不到源码目录：$($GUARD_FILES.sourceDir)")
        return New-GuardResult -Id 'filepicker' -Title '文件选择器入口（规则 16）' -Ok $false -Issues $issues -Details ([ordered]@{})
    }

    $hitCount = 0
    $allowedCount = 0

    $files = Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        if ($text.IndexOf('PickerAsync', [System.StringComparison]::Ordinal) -lt 0) { continue }

        $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
        foreach ($match in $RE_FILE_PICKER_API.Matches($text)) {
            $hitCount++
            $api = $match.Groups[1].Value
            $line = Get-LineNumber -Text $text -Index $match.Index

            if ($relative -ne $GUARD_FILES.filePicker) {
                $issues.Add("$relative`:$line 调用 $api（原生 StorageProvider 已禁用，改用 CustomFilePickerDialog）")
                continue
            }

            $method = Get-EnclosingMethodName -Text $text -TargetLine $line
            if ($method -ne $FILE_PICKER_WHITELIST_METHOD) {
                $issues.Add("$relative`:$line 在 $method 中调用 $api（唯一豁免点是 $FILE_PICKER_WHITELIST_METHOD 的降级通道）")
                continue
            }
            $allowedCount++
        }
    }

    return New-GuardResult -Id 'filepicker' -Title '文件选择器入口（规则 16）' -Ok ($issues.Count -eq 0) -Issues $issues -Details ([ordered]@{
            hits    = $hitCount
            allowed = $allowedCount
        })
}

# 规则 8：AppIcons.axaml 定义的图标必须登记到 IconTestViewModel
function Test-GuardIcons {
    param([string]$RepoRoot)

    $issues = [System.Collections.Generic.List[string]]::new()
    $icons = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.appIcons)
    $testVm = Read-TextFile (Join-Path $RepoRoot $GUARD_FILES.iconTestVm)

    if ($null -eq $icons -or $null -eq $testVm) {
        $issues.Add('读不到图标文件（AppIcons.axaml / IconTestViewModel.cs）')
        return New-GuardResult -Id 'icons' -Title '图标登记（规则 8）' -Ok $false -Issues $issues -Details ([ordered]@{})
    }

    $defined = @($RE_APP_ICON_KEY.Matches($icons) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $registered = @($RE_ICON_RESOURCE_REF.Matches($testVm) | ForEach-Object { $_.Value.Trim('"') } | Sort-Object -Unique)

    foreach ($key in $defined) {
        if ($registered -notcontains $key) {
            $issues.Add("$key 在 AppIcons.axaml 定义，但 IconTestViewModel.LoadAllIcons() 未登记")
        }
    }
    foreach ($key in $registered) {
        if ($defined -notcontains $key) {
            $issues.Add("IconTestViewModel 引用 $key，但 AppIcons.axaml 未定义")
        }
    }

    return New-GuardResult -Id 'icons' -Title '图标登记（规则 8）' -Ok ($issues.Count -eq 0) -Issues $issues -Details ([ordered]@{
            definedCount    = $defined.Count
            registeredCount = $registered.Count
        })
}

function Test-Guards {
    param([string]$RepoRoot, [string]$RelativeLocalizationDir)

    $guards = [System.Collections.Generic.List[object]]::new()
    $guards.Add((Test-GuardKeyBindings -RepoRoot $RepoRoot))
    $guards.Add((Test-GuardI18nBindings -RepoRoot $RepoRoot -RelativeLocalizationDir $RelativeLocalizationDir))
    $guards.Add((Test-GuardFilePicker -RepoRoot $RepoRoot))
    $guards.Add((Test-GuardIcons -RepoRoot $RepoRoot))

    $failed = @($guards | Where-Object { -not $_.ok })
    return [ordered]@{
        ok          = ($failed.Count -eq 0)
        guards      = @($guards)
        failedCount = $failed.Count
    }
}

function Format-GuardsResult {
    param($Result)

    $lines = [System.Collections.Generic.List[string]]::new()
    $guards = @($Result.guards)
    $failed = @($guards | Where-Object { -not $_.ok })

    if ($Result.ok) {
        $lines.Add("✅ 规则守卫全部通过（$($guards.Count) 项）")
    }
    else {
        $lines.Add("❌ 规则守卫发现 $($Result.failedCount) 项失败（共 $($guards.Count) 项）")
    }

    foreach ($guard in $guards) {
        $lines.Add('')
        if ($guard.ok) {
            $lines.Add("### ✅ $($guard.title)")
        }
        else {
            $lines.Add("### ❌ $($guard.title)")
            foreach ($issue in $guard.issues) {
                $lines.Add("- $issue")
            }
        }
    }

    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

$repoRoot = Resolve-RepoRoot -ExplicitRoot $Root

switch ($Task) {
    'build' {
        $projectPath = $TARGETS[$Target]
        $result = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'build' -ProjectPath $projectPath -AdditionalArgs $ExtraArgs -BudgetMs $TimeoutMs -TargetName $Target
        if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Format-BuildResult $result }
        exit ([int](-not $result.ok))
    }
    'test' {
        $projectPath = $TARGETS[$Target]
        $result = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'test' -ProjectPath $projectPath -AdditionalArgs $ExtraArgs -BudgetMs $TimeoutMs -TargetName $Target

        # 只有当「dotnet test 失败、没有任何编译诊断、也没有打印过测试汇总行」时才回退：
        # 三者同时成立意味着用例一条都没跑起来（testhost 崩溃 / 宿主进程被受限环境拒绝 /
        # 输出整段为空），而不是源码编译错误（errorCount > 0）或用例真的失败（有汇总行）。
        # xunit v3 的测试程序集自带 MTP 入口，dotnet exec 直跑不经过 VSTest 宿主，
        # 因此能绕开这类环境限制；正常环境与 CI 上 dotnet test 直接通过，不会走到这里。
        if (-not $result.ok -and [int]$result.errorCount -eq 0 -and -not $result.testsReported) {
            # 配置取自 -ExtraArgs（-c Release 等）：程序集只认该配置的 bin 目录，
            # 避免在旧配置的产物上跑出「通过」。
            $configuration = Get-ConfigurationFromArgs -Arguments $ExtraArgs
            $assembly = Get-TestAssemblyPath -RepoRoot $repoRoot -ProjectPath $projectPath -Configuration $configuration
            if (-not $assembly) {
                $build = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'build' -ProjectPath $projectPath -AdditionalArgs $ExtraArgs -BudgetMs $TimeoutMs -TargetName $Target
                if ($build.ok) {
                    $assembly = Get-TestAssemblyPath -RepoRoot $repoRoot -ProjectPath $projectPath -Configuration $configuration
                }
            }

            if ($assembly) {
                $fallback = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'test' -ProjectPath $projectPath -ArgvOverride @('exec', $assembly) -BudgetMs $TimeoutMs -TargetName $Target
                if ($fallback.ok) {
                    $fallback.runner = 'mtp'
                    $fallback.fallbackFrom = 'vstest'
                    $fallback.primaryExitCode = $result.exitCode
                    $result = $fallback
                }
                else {
                    $result.mtpFallback = [ordered]@{
                        exitCode = $fallback.exitCode
                        tail     = @($fallback.tail)
                    }
                }
            }
        }

        if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Format-BuildResult $result }
        exit ([int](-not $result.ok))
    }
    'i18n' {
        $result = Test-I18n -RepoRoot $repoRoot -RelativeDir $LocalizationDir
        if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Format-I18nResult $result }
        exit ([int](-not $result.ok))
    }
    'version' {
        $result = Test-VersionConsistency -RepoRoot $repoRoot
        if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Format-VersionResult $result }
        exit ([int](-not $result.ok))
    }
    'guards' {
        $result = Test-Guards -RepoRoot $repoRoot -RelativeLocalizationDir $LocalizationDir
        if ($Json) { $result | ConvertTo-Json -Depth 10 } else { Format-GuardsResult $result }
        exit ([int](-not $result.ok))
    }
    'check' {
        $versionResult = Test-VersionConsistency -RepoRoot $repoRoot
        $i18nResult = Test-I18n -RepoRoot $repoRoot -RelativeDir $LocalizationDir
        $guardsResult = Test-Guards -RepoRoot $repoRoot -RelativeLocalizationDir $LocalizationDir
        $ok = ($versionResult.ok -and $i18nResult.ok -and $guardsResult.ok)

        if ($Json) {
            [ordered]@{
                ok     = $ok
                checks = @(
                    [ordered]@{ task = 'version'; result = $versionResult }
                    [ordered]@{ task = 'i18n'; result = $i18nResult }
                    [ordered]@{ task = 'guards'; result = $guardsResult }
                )
            } | ConvertTo-Json -Depth 10
        }
        else {
            Format-VersionResult $versionResult
            ''
            Format-I18nResult $i18nResult
            ''
            Format-GuardsResult $guardsResult
        }

        exit ([int](-not $ok))
    }
}
