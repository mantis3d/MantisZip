# mantiszip-dsh-plugin 安装脚本（Windows / PowerShell 7）
#
# 做两件事（幂等，可重复执行）：
#   1. 在 DSH profile 的 node_modules 下建一个目录联接（junction），指向本目录；
#   2. 在该 profile 的 cordis.patch.yml 末尾追加一行 insert（带起止标记，便于卸载）。
#
# 不调用 pnpm、不联网、不改 package.json 依赖。
#
# 用法：
#   pwsh -File tools\dsh-plugin-mantiszip\install.ps1
#   pwsh -File tools\dsh-plugin-mantiszip\install.ps1 -Profile web

[CmdletBinding()]
param(
    [string]$Profile = 'desktop',
    [string]$DshHome = (Join-Path $env:USERPROFILE '.dsh')
)

$ErrorActionPreference = 'Stop'

$pluginDir = (Resolve-Path (Split-Path -Parent $MyInvocation.MyCommand.Path)).Path
$profileDir = Join-Path $DshHome (Join-Path 'profiles' $Profile)
$packageName = 'mantiszip-dsh-plugin'

if (-not (Test-Path $profileDir)) {
    throw "找不到 profile 目录：$profileDir（可用 -Profile、-DshHome 指定）"
}

$patchFile = Join-Path $profileDir 'cordis.patch.yml'
if (-not (Test-Path $patchFile)) {
    throw "找不到 patch 文件：$patchFile"
}

# 1) 目录联接：profile 内的 node_modules 与共享 node_modules 各放一个，
#    两种解析起点都能命中，避免依赖加载器内部 baseUrl 细节。
$linkParents = @(
    (Join-Path $profileDir 'node_modules'),
    (Join-Path $DshHome 'profiles\node_modules')
)

foreach ($parent in $linkParents) {
    $link = Join-Path $parent $packageName
    if (Test-Path $link) {
        $item = Get-Item $link -Force
        if ($item.LinkType -eq 'Junction' -and $item.Target -contains $pluginDir) {
            Write-Host "[跳过] 联接已存在：$link"
            continue
        }
        throw "目标已存在且不是指向本插件的联接：$link（请先手动处理）"
    }
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    New-Item -ItemType Junction -Path $link -Target $pluginDir | Out-Null
    Write-Host "[完成] 联接 $link -> $pluginDir"
}

# 2) patch：追加带标记的 insert 块
$markerStart = '# >>> mantiszip-tools'
$markerEnd = '# <<< mantiszip-tools'
$content = Get-Content $patchFile -Raw

# 仓库根目录：本脚本位于 <repo>/tools/dsh-plugin-mantiszip/
$repoRoot = (Resolve-Path (Join-Path $pluginDir '..\..')).Path

# 先移除旧的标记段（若有），保证重复执行时 config.root 能刷新到当前仓库路径。
$lines = ($content -split "`r?`n")
$kept = New-Object System.Collections.Generic.List[string]
$skipping = $false
$hadBlock = $false
foreach ($line in $lines) {
    if ($line -match '# >>> mantiszip-tools') { $skipping = $true; $hadBlock = $true; continue }
    if ($line -match '# <<< mantiszip-tools') { $skipping = $false; continue }
    if (-not $skipping) { $kept.Add($line) }
}

$block = @"

$markerStart（由 tools/dsh-plugin-mantiszip/install.ps1 写入，卸载用 uninstall.ps1）
- insert:
    - id: mantiszip-tools
      name: '$packageName'
      config:
        root: '$repoRoot'
$markerEnd
"@

$trimmed = ($kept -join "`r`n").TrimEnd()
Set-Content -Path $patchFile -Value ($trimmed + $block) -NoNewline -Encoding utf8
if ($hadBlock) {
    Write-Host "[完成] 已刷新 patch 中的 mantiszip-tools 段（root=$repoRoot）"
} else {
    Write-Host "[完成] 已写入 patch：$patchFile（root=$repoRoot）"
}

Write-Host ''
Write-Host '下一步：完全退出并重新打开 DeepSeek Harness，插件才会加载。'
Write-Host '验证方式：新会话里问一句「调用 mantiszip_version 检查版本号」。'
Write-Host '卸载：pwsh -File tools\dsh-plugin-mantiszip\uninstall.ps1'
