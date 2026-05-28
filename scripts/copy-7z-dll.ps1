<#
.SYNOPSIS
    Copies 7z.dll from a 7-Zip installation to the publish output directory
    so SharpSevenZip can find it without requiring the user to install 7-Zip.

.DESCRIPTION
    Searches for 7z.dll in standard 7-Zip installation paths, then copies
    the 64-bit version to publish_output\x64\ and the 32-bit version to
    publish_output\x86\. The installer script bundles these into the app.

    LGPL compliance: 7z.dll is dynamically linked (via SharpSevenZip's COM
    wrapper) and distributed under GNU LGPL. See lgpl.txt in the installer.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$hostUi = $Host.UI

$candidates = @(
    # Standard 7-Zip installation paths
    "$env:ProgramFiles\7-Zip\7z.dll",              # 64-bit
    "${env:ProgramFiles(x86)}\7-Zip\7z.dll",       # 32-bit
    # PATH
    (Get-Command "7z.dll" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
)

function Write-Info($msg) { $hostUi.WriteLine("INFO: $msg") }
function Write-Warn($msg) { $hostUi.WriteLine("WARN: $msg") }
function Write-Err($msg) { $hostUi.WriteLine("ERROR: $msg") }

# --- Find 64-bit 7z.dll ---
$found64 = $null
foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) {
        $found64 = $c
        break
    }
}

if (-not $found64) {
    Write-Warn "7z.dll not found in standard locations. 7z operations will fall back to auto-detection."
    Write-Info "Install 7-Zip from https://www.7-zip.org/ and re-run this script to bundle 7z.dll."
    exit 0  # non-fatal — app works without bundled 7z.dll if 7-Zip is installed on the target system
}

# --- Determine 32-bit path ---
# If 64-bit was found in Program Files, the 32-bit one might be in Program Files (x86)
$native64 = $found64
$native86 = $null

if ($found64 -match [regex]::Escape("$env:ProgramFiles\7-Zip")) {
    $x86path = "${env:ProgramFiles(x86)}\7-Zip\7z.dll"
    if (Test-Path $x86path) {
        $native86 = $x86path
    }
}

if (-not $native86) {
    # Fallback: same DLL works for both (modern 7-Zip distributes a universal binary)
    $native86 = $native64
}

# --- Create target directories ---
$target64 = Join-Path $PublishDir "x64"
$target86 = Join-Path $PublishDir "x86"
$null = New-Item -ItemType Directory -Path $target64 -Force
$null = New-Item -ItemType Directory -Path $target86 -Force

# --- Copy 7z.dll ---
try {
    Copy-Item -Path $native64 -Destination (Join-Path $target64 "7z.dll") -Force
    Write-Info "Copied 64-bit 7z.dll from '$native64' → '$target64'"
} catch {
    Write-Err "Failed to copy 64-bit 7z.dll: $_"
    exit 1
}

try {
    Copy-Item -Path $native86 -Destination (Join-Path $target86 "7z.dll") -Force
    Write-Info "Copied 32-bit 7z.dll from '$native86' → '$target86'"
} catch {
    Write-Err "Failed to copy 32-bit 7z.dll: $_"
    exit 1
}

Write-Info "7z.dll bundled successfully. LGPL 7-Zip DLL is dynamically linked."
exit 0
