# Download redistributable prerequisites for MantisZip offline installer
#
# Usage:
#   .\installer\download-redist.ps1
#
# This downloads the WebView2 Runtime Evergreen Standalone Installer (x64)
# which is required by installer-selfcontained.iss for fully offline installation.

$ErrorActionPreference = "Stop"

$redistDir = Join-Path $PSScriptRoot "redist"
if (-not (Test-Path $redistDir)) {
    New-Item -ItemType Directory -Path $redistDir -Force | Out-Null
}

# WebView2 Runtime Evergreen Standalone Installer (x64)
$webView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124701"
$webView2Output = Join-Path $redistDir "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"

if (-not (Test-Path $webView2Output)) {
    Write-Host "Downloading WebView2 Runtime Standalone Installer (x64)..." -ForegroundColor Cyan
    Write-Host "  URL: $webView2Url" -ForegroundColor Gray
    try {
        Invoke-WebRequest -Uri $webView2Url -OutFile $webView2Output -TimeoutSec 300 -ErrorAction Stop
        $size = (Get-Item $webView2Output).Length / 1MB
        Write-Host "  Downloaded: $('{0:N2}' -f $size) MB" -ForegroundColor Green
    }
    catch {
        Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}
else {
    $size = (Get-Item $webView2Output).Length / 1MB
    Write-Host "WebView2 Runtime Standalone Installer already exists: $('{0:N2}' -f $size) MB" -ForegroundColor Green
}

Write-Host ""
Write-Host "All redistributables ready. You can now compile installer-selfcontained.iss with ISCC." -ForegroundColor Cyan
