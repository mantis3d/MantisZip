# mantiszip-dsh-plugin 卸载脚本（Windows / PowerShell 7）
#
# 移除 install.ps1 写入的两个目录联接，以及 cordis.patch.yml 中带标记的 insert 段。
# 只动自己写入的内容，不改动其他插件条目。

[CmdletBinding()]
param(
    [string]$Profile = 'desktop',
    [string]$DshHome = (Join-Path $env:USERPROFILE '.dsh')
)

$ErrorActionPreference = 'Stop'

$profileDir = Join-Path $DshHome (Join-Path 'profiles' $Profile)
$packageName = 'mantiszip-dsh-plugin'

if (-not (Test-Path $profileDir)) {
    throw "找不到 profile 目录：$profileDir"
}

$linkParents = @(
    (Join-Path $profileDir 'node_modules'),
    (Join-Path $DshHome 'profiles\node_modules')
)

foreach ($parent in $linkParents) {
    $link = Join-Path $parent $packageName
    if (-not (Test-Path $link)) { continue }
    $item = Get-Item $link -Force
    if ($item.LinkType -ne 'Junction') {
        Write-Warning "跳过非联接路径：$link"
        continue
    }
    # 只删联接本身，Remove-Item 对 junction 不会递归删除目标内容；
    # 这里用 cmd 的 rmdir 以保证只移除链接、不触碰真实目录。
    cmd /c rmdir "$link"
    Write-Host "[完成] 已移除联接：$link"
}

$patchFile = Join-Path $profileDir 'cordis.patch.yml'
if (Test-Path $patchFile) {
    $lines = Get-Content $patchFile
    $kept = New-Object System.Collections.Generic.List[string]
    $skipping = $false
    $removed = 0
    foreach ($line in $lines) {
        if ($line -match '# >>> mantiszip-tools') { $skipping = $true; $removed++; continue }
        if ($line -match '# <<< mantiszip-tools') { $skipping = $false; continue }
        if (-not $skipping) { $kept.Add($line) }
    }
    if ($removed -gt 0) {
        Set-Content -Path $patchFile -Value ($kept -join "`r`n") -Encoding utf8
        Write-Host "[完成] 已从 patch 移除 mantiszip-tools 段：$patchFile"
    } else {
        Write-Host '[跳过] patch 中没有 mantiszip-tools 段'
    }
}

Write-Host ''
Write-Host '重启 DeepSeek Harness 后插件不再加载。'
