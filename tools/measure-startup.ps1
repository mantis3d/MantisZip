# tools/measure-startup.ps1
# 重复启动 MantisZip，解析 startup-trace.log 每次新增的末段，聚合首帧总时长与关键跨度中位数。
# 支持多 exe 交错测量（A/B 对比时按轮次轮流启动，抵消系统漂移）。
# 检测方式：trace 文件 LastWriteTime 变化（旧版按长度比较，在文件达到 20 段保留上限后长度近似恒定，会误判 NO TRACE）。
# 注意：应用是 GUI，不会自行退出 —— 脚本轮询 trace 刷新（首帧 Flush）后再终止进程。
# 用法:
#   .\tools\measure-startup.ps1 -ExePath src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe -Runs 5
#   .\tools\measure-startup.ps1 -ExePath @("baseline.exe", "new.exe") -Runs 8   # 交错 A/B
param(
    [Parameter(Mandatory)][string[]]$ExePath,
    [int]$Runs = 5,
    [int]$WarmupRuns = 1,
    [int]$TimeoutSec = 30
)
$ErrorActionPreference = 'Stop'
$trace = Join-Path $env:LOCALAPPDATA "MantisZip\startup-trace.log"
$resolved = @()
foreach ($e in $ExePath) {
    if (-not (Test-Path $e)) { throw "exe not found: $e" }
    $resolved += (Resolve-Path $e).Path
}

# 取 trace 原文中最后一个启动段（每次 Flush 追加一段）
function Get-LastSection([string]$raw) {
    $idx = $raw.LastIndexOf('==== MantisZip Startup Trace')
    if ($idx -lt 0) { return '' }
    return $raw.Substring($idx)
}

# 启动一次应用 → 轮询 trace 刷新 → 杀进程 →返回本次新增的启动段
function Invoke-OneRun([string]$exe) {
    $lastWrite = if (Test-Path $trace) { (Get-Item $trace).LastWriteTimeUtc } else { [datetime]::MinValue }
    $p = Start-Process $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $section = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        if (-not (Test-Path $trace)) { continue }
        if ((Get-Item $trace).LastWriteTimeUtc -le $lastWrite) { continue }
        $sec = Get-LastSection (Get-Content $trace -Raw)
        if ($sec -match 'TOTAL to last mark') { $section = $sec; break }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    return $section
}

# 从启动段中取某个 phase 的 cumulative 值（ms），无则返回 $null
function Get-Cumulative([string]$section, [string]$name) {
    $m = [regex]::Match($section, '(?m)^\s*' + [regex]::Escape($name) + '\s+[\d\.]+ms\s+([\d\.]+)ms')
    if ($m.Success) { return [double]$m.Groups[1].Value }
    return $null
}

function Get-Median([array]$values) {
    $sorted = @($values | Where-Object { $_ -ne $null } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return $sorted[[int][math]::Floor($sorted.Count / 2)]
}

function New-Sample([string]$section) {
    if ($section -notmatch 'TOTAL to last mark = ([\d\.]+)ms') { return $null }
    $total = [double]$Matches[1]
    $init = Get-Cumulative $section 'Avalonia.InitDone'
    $ctorEnter = Get-Cumulative $section 'Win.Ctor.Enter'
    $winXaml = Get-Cumulative $section 'Win.Xaml'
    $ctorExit = Get-Cumulative $section 'Win.Ctor.Exit'
    $firstFrame = Get-Cumulative $section 'Win.FirstFrame'
    $eager = $null
    if ($section -match 'preview-eager total = ([\d\.]+)ms') { $eager = [double]$Matches[1] }
    return [pscustomobject]@{
        Total    = $total
        Init     = $init
        XamlSpan = if ($null -ne $ctorEnter -and $null -ne $winXaml) { $winXaml - $ctorEnter } else { $null }
        CtorSpan = if ($null -ne $ctorEnter -and $null -ne $ctorExit) { $ctorExit - $ctorEnter } else { $null }
        WinSpan  = if ($null -ne $ctorEnter -and $null -ne $firstFrame) { $firstFrame - $ctorEnter } else { $null }
        Eager    = $eager
        HasPanel = [bool]($section -match 'Preview\.Panel\.Ctor')
    }
}

function Get-ExeLabel([string]$exe) {
    $dir = Split-Path (Split-Path $exe) -Leaf   # net10.0 上级目录（bin 下的 Debug/Release 不含路径特征时用更上一级）
    $up = Split-Path (Split-Path (Split-Path $exe)) -Leaf  # bin
    $up2 = Split-Path (Split-Path (Split-Path (Split-Path $exe))) -Leaf # 项目目录
    return "$up2/$up"
}

# 预热（每个 exe 各 WarmupRuns 次，不计入样本）
foreach ($exe in $resolved) {
    1..$WarmupRuns | ForEach-Object { Invoke-OneRun $exe | Out-Null }
    "warmup done: $(Get-ExeLabel $exe)"
}

# 交错轮次：每轮对每个 exe 各跑一次
$byExe = @{}
foreach ($exe in $resolved) { $byExe[$exe] = New-Object System.Collections.ArrayList }
$lastSection = ''
for ($i = 1; $i -le $Runs; $i++) {
    foreach ($exe in $resolved) {
        $section = Invoke-OneRun $exe
        $sample = if ($section) { New-Sample $section } else { $null }
        $tag = if ($resolved.Count -gt 1) { Get-ExeLabel $exe } else { '' }
        if ($sample) {
            [void]$byExe[$exe].Add($sample)
            $lastSection = $section
            "round ${i} [$tag]: total $($sample.Total), init $($sample.Init), xaml $($sample.XamlSpan), ctor $($sample.CtorSpan), win $($sample.WinSpan), eager $($sample.Eager), panel=$($sample.HasPanel)"
        } else {
            "round ${i} [$tag]: NO TRACE (timeout or flush missing)"
        }
    }
}

$any = $false
foreach ($exe in $resolved) { if ($byExe[$exe].Count -gt 0) { $any = $true } }
if (-not $any) { throw "no samples parsed" }

foreach ($exe in $resolved) {
    $s = $byExe[$exe]
    if ($s.Count -eq 0) { "`n=== $(Get-ExeLabel $exe): no samples ==="; continue }
    $eagers = @($s | ForEach-Object Eager)
    $panels = @($s | Where-Object HasPanel).Count
    "`n=== $(Get-ExeLabel $exe) 聚合（中位数，n=$($s.Count)） ==="
    "TOTAL            = $(Get-Median @($s | ForEach-Object Total)) ms"
    "InitDone         = $(Get-Median @($s | ForEach-Object Init)) ms"
    "XamlSpan         = $(Get-Median @($s | ForEach-Object XamlSpan)) ms   (Win.Ctor.Enter -> Win.Xaml)"
    "CtorSpan         = $(Get-Median @($s | ForEach-Object CtorSpan)) ms   (Win.Ctor.Enter -> Win.Ctor.Exit)"
    "WinSpan          = $(Get-Median @($s | ForEach-Object WinSpan)) ms   (Win.Ctor.Enter -> Win.FirstFrame)"
    if ($eagers.Count -gt 0) { "preview-eager    = $(Get-Median $eagers) ms" }
    "with panel marks = $panels/$($s.Count)"
}

if ($lastSection) {
    "`n=== 最近一次样本逐段 ==="
    $lastSection -split "`r?`n" | ForEach-Object { $_ }
}
