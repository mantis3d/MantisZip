# tools/measure-startup.ps1
# 重复启动 MantisZip N 次，解析 startup-trace.log 新增段落，聚合首帧总时长中位数与各阶段增量。
# 注意：应用是 GUI，不会自行退出 —— 脚本轮询 trace 文件（首帧 Flush 后出现）再终止进程。
# 用法: .\tools\measure-startup.ps1 -ExePath src\MantisZip.UI.Avalonia\bin\Release\net10.0\MantisZip.UI.Avalonia.exe -Runs 5
param(
    [Parameter(Mandatory)][string]$ExePath,
    [int]$Runs = 5,
    [int]$WarmupRuns = 1,
    [int]$TimeoutSec = 30
)
$ErrorActionPreference = 'Stop'
$trace = Join-Path $env:LOCALAPPDATA "MantisZip\startup-trace.log"
if (-not (Test-Path $ExePath)) { throw "exe not found: $ExePath" }
$ExePath = (Resolve-Path $ExePath).Path

function Invoke-OneRun([string]$exe) {
    $before = if (Test-Path $trace) { (Get-Content $trace -Raw) } else { "" }
    $p = Start-Process $exe -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $new = ""
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        if (-not (Test-Path $trace)) { continue }
        $after = Get-Content $trace -Raw
        if ($after.Length -le $before.Length) { continue }
        $new = $after.Substring($before.Length)
        if ($new -match "TOTAL to last mark") { break }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    return $new
}

# 预热（不计入样本）
1..$WarmupRuns | ForEach-Object { Invoke-OneRun $ExePath | Out-Null; "warmup $_ done" } | Out-Host

$totals = @()
$lastSamples = @()
for ($i = 1; $i -le $Runs; $i++) {
    $new = Invoke-OneRun $ExePath
    if ($new -match "TOTAL to last mark = ([\d\.]+)ms") {
        $totals += [double]$Matches[1]
        $lastSamples = $new -split "`r?`n"
        "run ${i}: $($Matches[1]) ms"
    } else {
        "run ${i}: NO TRACE (timeout or flush missing)"
    }
}
if ($totals.Count -eq 0) { throw "no samples parsed" }

$sorted = $totals | Sort-Object
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
"`n=== 聚合 ==="
"median TOTAL = $median ms   (samples: $($totals -join ', '))"
"`n=== 最近一次样本逐段 ==="
$lastSamples | ForEach-Object { $_ }
