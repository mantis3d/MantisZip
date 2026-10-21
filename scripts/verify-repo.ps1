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
        test     dotnet test  <target>, parse errors/warnings
        i18n     compare strings.zh-CN / strings.en / strings.zh-TW
        version  compare the 6 version locations listed in AGENTS.md
        check    version + i18n (pure file checks, no build, ~instant)

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
    [ValidateSet('build', 'test', 'i18n', 'version', 'check')]
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
$RE_JSON_KEY = [regex]'(?m)^\s*"((?:[^"\\]|\\.)*)"\s*:'

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

function Invoke-VerifyDotnet {
    param(
        [string]$RepoRoot,
        [string]$Action,
        [string]$ProjectPath,
        [string[]]$AdditionalArgs,
        [int]$BudgetMs
    )

    $argv = @($Action, $ProjectPath, '-nologo', '-nodeReuse:false', '-v:m')
    if ($Action -eq 'build') { $argv += '-clp:NoSummary' }
    if ($AdditionalArgs) { $argv += $AdditionalArgs }

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

    $payload = [ordered]@{
        ok           = ($exitCode -eq 0 -and $errors.Count -eq 0)
        target       = $Target
        action       = $Action
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

    if (@($Result.errors).Count -gt 0) {
        $lines.Add('')
        $lines.Add('错误：')
        foreach ($entry in $Result.errors) {
            $where = if ($entry.file) { "$($entry.file)($($entry.line),$($entry.column))" } else { '(项目级)' }
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
            $where = if ($entry.file) { "$($entry.file)($($entry.line),$($entry.column))" } else { '(项目级)' }
            $label = if ($entry.code) { "$($entry.code): " } else { '' }
            $lines.Add("- $where $label$(Clip-Text $entry.message 300)")
            $shown++
        }
        if ([int]$Result.warningCount -gt $shown) {
            $lines.Add("- …共 $($Result.warningCount) 条警告")
        }
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
# main
# ---------------------------------------------------------------------------

$repoRoot = Resolve-RepoRoot -ExplicitRoot $Root

switch ($Task) {
    'build' {
        $projectPath = $TARGETS[$Target]
        $result = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'build' -ProjectPath $projectPath -AdditionalArgs $ExtraArgs -BudgetMs $TimeoutMs
        if ($Json) { $result | ConvertTo-Json -Depth 8 } else { Format-BuildResult $result }
        exit ([int](-not $result.ok))
    }
    'test' {
        $projectPath = $TARGETS[$Target]
        $result = Invoke-VerifyDotnet -RepoRoot $repoRoot -Action 'test' -ProjectPath $projectPath -AdditionalArgs $ExtraArgs -BudgetMs $TimeoutMs
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
    'check' {
        $versionResult = Test-VersionConsistency -RepoRoot $repoRoot
        $i18nResult = Test-I18n -RepoRoot $repoRoot -RelativeDir $LocalizationDir
        $ok = ($versionResult.ok -and $i18nResult.ok)

        if ($Json) {
            [ordered]@{
                ok     = $ok
                checks = @(
                    [ordered]@{ task = 'version'; result = $versionResult }
                    [ordered]@{ task = 'i18n'; result = $i18nResult }
                )
            } | ConvertTo-Json -Depth 10
        }
        else {
            Format-VersionResult $versionResult
            ''
            Format-I18nResult $i18nResult
        }

        exit ([int](-not $ok))
    }
}
