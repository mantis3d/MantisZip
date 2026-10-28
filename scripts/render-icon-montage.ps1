<#
.SYNOPSIS
    把 AppIcons.axaml 里的全部图标渲染成一张 PNG 拼版，用于人工评审"图标风格是否统一"。

.DESCRIPTION
    图标集合的唯一事实源是 src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml，
    全部 UI 图标都通过资源键（{StaticResource IconXxx}）引用，因此只要这一个文件统一，
    全仓 220+ 处 PathIcon 自动跟着统一。

    脚本按**渲染包围盒**把图标分成三族，直接标出"混进来的非同族图标"：

        A = 包围盒落在 24 网格内（Fluent 24 Regular 期望值）。
        B = 包围盒超出 24 网格 ⇒ 别的坐标系（图标字体字形按 1000/2048 em 设计，坐标能到 800+）。
            这类图标是从图标字体里抠出来的字形，笔画比 A 粗，混排时明显"更黑更大"。
        C = 解析失败 / 空路径 —— 路径数据写错，渲染时会静默消失。

    判据是启发式的，只用来快速定位候选，最终是否"同族"仍要人眼看图（`-Check` 只保证"没混进别的坐标系"和"没有解析错误"）。

.PARAMETER OutputPath
    输出 PNG 路径。默认写到系统临时目录 mantiszip-icon-montage.png（不污染仓库）。

.PARAMETER Columns
    拼版列数，默认 10。

.PARAMETER IconSize
    每个图标单元格内的渲染尺寸（像素），默认 28。

.PARAMETER Check
    只输出分组报告、不渲染 PNG。发现 B/C 组图标时以退出码 1 结束，
    便于将来接进 scripts\verify-repo.ps1 的守卫任务。

.EXAMPLE
    pwsh -File scripts\render-icon-montage.ps1

.EXAMPLE
    pwsh -File scripts\render-icon-montage.ps1 -OutputPath .\icon-montage.png -Columns 12

.EXAMPLE
    pwsh -File scripts\render-icon-montage.ps1 -Check

.NOTES
    平台：仅 Windows（借 WPF 的 PresentationCore 做离屏渲染，不需要启动 Avalonia，秒级完成）。

    实现上有两个坑（都踩过，改脚本前先看）：
      1. [System.Windows.Media.Geometry]::Parse() 返回的对象是**冻结**的，
         给它设 Transform 会抛 SetValueInvocationException（"处于只读状态"），
         必须改用 DrawingContext.PushTransform(...) / Pop() 包住 DrawGeometry。
      2. New-Object 直接传枚举（[System.Windows.FlowDirection]::LeftToRight）会被当成字符串，
         必须先把枚举存进变量再传，否则 FormattedText 构造失败、标签画不出来。

    本脚本只读仓库、只写输出图片，不修改任何源文件。
#>

[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([System.IO.Path]::GetTempPath()) 'mantiszip-icon-montage.png'),
    [int]$Columns = 10,
    [int]$IconSize = 28,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# ── 定位仓库根目录（脚本可能从任意工作目录调用）─────────────────────────────
$repoRoot = Split-Path -Parent $PSScriptRoot
$iconFile = Join-Path $repoRoot 'src\MantisZip.UI.Avalonia\Resources\Icons\AppIcons.axaml'
if (-not (Test-Path $iconFile)) {
    Write-Error "找不到图标资源文件：$iconFile"
    exit 1
}

# ── 解析图标集合 + 网格分族 ─────────────────────────────────────────────────
# 判据用「实际渲染包围盒」而不是「路径字符串里最大的数字」：
#   A = 包围盒落在 24 网格内（Fluent 24 Regular 期望值）
#   B = 包围盒超出 24 网格 ⇒ 别的坐标系（图标字体字形按 1000/2048 em 设计，坐标能到 800+）
#   C = 解析失败 / 空路径
# 为什么不看「字符串里的最大数字」：圆弧半径、`A59 59` 这种上游数据会让朴素取最大值误判，
# 而 `.59.59` 这类连写小数还会被数字正则拼成 59；包围盒是渲染时真正生效的几何量。
Add-Type -AssemblyName PresentationCore

$entries = foreach ($line in (Select-String -Path $iconFile -Pattern 'x:Key="(Icon[^"]+)"')) {
    $key = [regex]::Match($line.Line, 'x:Key="(Icon[^"]+)"').Groups[1].Value
    $data = [regex]::Match($line.Line, '>([^<]*)</Geometry>').Groups[1].Value
    if (-not $data) { continue }

    $group = 'C'
    $boundsText = 'parse-error'
    try {
        $bounds = [System.Windows.Media.Geometry]::Parse($data).Bounds
        if (-not $bounds.IsEmpty -and $bounds.Width -gt 0 -and $bounds.Height -gt 0) {
            $boundsText = '{0:N1},{1:N1}-{2:N1},{3:N1}' -f $bounds.Left, $bounds.Top, $bounds.Right, $bounds.Bottom
            $outOfGrid = $bounds.Right -gt 26 -or $bounds.Bottom -gt 26 -or $bounds.Left -lt -2 -or $bounds.Top -lt -2
            $group = if ($outOfGrid) { 'B' } else { 'A' }
        }
    } catch {
        $group = 'C'
    }

    [pscustomobject]@{
        Key    = $key
        Data   = $data
        Bounds = $boundsText
        Group  = $group
    }
}

if (-not $entries) {
    Write-Error "没有解析到任何图标，检查 $iconFile 的格式是否变了"
    exit 1
}

$groupA = @($entries | Where-Object Group -eq 'A')
$groupB = @($entries | Where-Object Group -eq 'B')
$groupC = @($entries | Where-Object Group -eq 'C')

Write-Host ''
Write-Host "图标总数：$($entries.Count)" -ForegroundColor Cyan
Write-Host ("  A 24 网格（Fluent 系，期望值）: {0,3}" -f $groupA.Count) -ForegroundColor Green
Write-Host ("  B 别的坐标系（越界）         : {0,3}" -f $groupB.Count) -ForegroundColor DarkYellow
Write-Host ("  C 解析失败 / 空路径          : {0,3}" -f $groupC.Count) -ForegroundColor Red

foreach ($g in @(
        @{ Name = 'B（别的坐标系）'; Items = $groupB; Color = 'DarkYellow' }
        @{ Name = 'C（解析失败）'; Items = $groupC; Color = 'Red' }
    )) {
    if ($g.Items.Count -eq 0) { continue }
    Write-Host ''
    Write-Host "$($g.Name) 组明细（建议替换为同族 24 网格图标）：" -ForegroundColor $g.Color
    foreach ($item in $g.Items) {
        Write-Host ("    {0,-22} bounds={1,-20} len={2}" -f $item.Key, $item.Bounds, $item.Data.Length)
    }
}

if ($Check) {
    Write-Host ''
    if (($groupB.Count + $groupC.Count) -eq 0) {
        Write-Host '✔ 全部图标包围盒都在 24 网格内' -ForegroundColor Green
        exit 0
    }
    Write-Host ("✘ 有 {0} 个图标包围盒不在 24 网格内" -f ($groupB.Count + $groupC.Count)) -ForegroundColor Red
    exit 1
}

# ── 离屏渲染拼版 ────────────────────────────────────────────────────────────
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$cellWidth = [double]($IconSize + 50)
$cellHeight = [double]($IconSize + 38)
$legendHeight = 26.0
$rowCount = [int][math]::Ceiling($entries.Count / $Columns)
$canvasWidth = $Columns * $cellWidth
$canvasHeight = $rowCount * $cellHeight + $legendHeight

$visual = New-Object System.Windows.Media.DrawingVisual
$dc = $visual.RenderOpen()

$background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(252, 252, 252))
$dc.DrawRectangle($background, $null, (New-Object System.Windows.Rect 0, 0, $canvasWidth, $canvasHeight))

$penA = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::ForestGreen), 1
$penB = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::DarkOrange), 1
$penC = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::Crimson), 1
$ink = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(25, 25, 25))
$typeface = New-Object System.Windows.Media.Typeface 'Microsoft YaHei UI'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$flowDirection = [System.Windows.FlowDirection]::LeftToRight

# 图例：让 PNG 自己能说清三色含义
$legend = New-Object System.Windows.Media.FormattedText(
    ("绿 = 24 网格（{0}）   橙 = 别的坐标系（{1}）   红 = 解析失败（{2}）   共 {3} 个图标" -f $groupA.Count, $groupB.Count, $groupC.Count, $entries.Count),
    $culture, $flowDirection, $typeface, 12.0, $ink, 1.0)
$dc.DrawText($legend, (New-Object System.Windows.Point 8, 6))

$index = 0
foreach ($entry in $entries) {
    $x = ($index % $Columns) * $cellWidth
    $y = [int][math]::Floor($index / $Columns) * $cellHeight + $legendHeight

    $pen = switch ($entry.Group) { 'B' { $penB } 'C' { $penC } default { $penA } }
    $dc.DrawRectangle($null, $pen, (New-Object System.Windows.Rect ($x + 2), ($y + 2), ($cellWidth - 4), ($cellHeight - 4)))

    try {
        $geometry = [System.Windows.Media.Geometry]::Parse($entry.Data)
        $bounds = $geometry.Bounds
        if ($bounds.Width -gt 0 -and $bounds.Height -gt 0) {
            # 等比缩放铺满单元格内的正方形区域，再居中对齐（与 PathIcon 的 Stretch=Uniform 一致）
            $scale = $IconSize / [math]::Max($bounds.Width, $bounds.Height)
            $scaledX = $bounds.X * $scale
            $scaledY = $bounds.Y * $scale
            $scaledW = $bounds.Width * $scale
            $scaledH = $bounds.Height * $scale
            $offsetX = ($x + $cellWidth / 2) - ($scaledX + $scaledW / 2)
            $offsetY = ($y + 6) + ($IconSize - $scaledH) / 2 - $scaledY

            $transform = New-Object System.Windows.Media.TransformGroup
            $transform.Children.Add((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
            $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform $offsetX, $offsetY))

            # 注意：Geometry 是冻结对象，不能设 Transform，只能压栈
            $dc.PushTransform($transform)
            $dc.DrawGeometry($ink, $null, $geometry)
            $dc.Pop()
        }
    } catch {
        $errorText = New-Object System.Windows.Media.FormattedText('PARSE-ERROR', $culture, $flowDirection, $typeface, 8.0, $penC.Brush, 1.0)
        $dc.DrawText($errorText, (New-Object System.Windows.Point ($x + 6), ($y + 10)))
    }

    $label = $entry.Key -replace '^Icon', ''
    if ($label.Length -gt 16) { $label = $label.Substring(0, 16) }
    $text = New-Object System.Windows.Media.FormattedText($label, $culture, $flowDirection, $typeface, 9.0, $ink, 1.0)
    $dc.DrawText($text, (New-Object System.Windows.Point ($x + 4), ($y + $IconSize + 12)))

    $index++
}

$dc.Close()

$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
    [int]$canvasWidth, [int]$canvasHeight, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
$bitmap.Render($visual)

$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) { New-Item -ItemType Directory -Force -Path $outputDir | Out-Null }

$stream = [System.IO.File]::Create($OutputPath)
try { $encoder.Save($stream) } finally { $stream.Close() }

Write-Host ''
Write-Host "✔ 拼版已生成：$OutputPath" -ForegroundColor Green
exit 0
