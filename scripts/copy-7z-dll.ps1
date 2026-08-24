<#
.SYNOPSIS
    Copies architecture-matched 7z.dll from a 7-Zip installation to the publish
    output directory so SharpSevenZip can find it without requiring the user to
    install 7-Zip.

.DESCRIPTION
    Probes every candidate 7z.dll's PE header to determine its REAL machine type
    (7-Zip does NOT distribute universal binaries — x64 and x86 installers carry
    different, mutually incompatible DLLs). Each architecture is then copied from
    a genuinely matching file:

        publish_output\x64\7z.dll  ← PE Machine = 0x8664 (AMD64)
        publish_output\x86\7z.dll  ← PE Machine = 0x014C (i386)

    If no matching file exists for an architecture, that directory is NOT created
    (installer.iss uses skipifsourcedoesntexist for x86). Copying a wrong-arch
    DLL is strictly avoided: a 32-bit process cannot load a 64-bit DLL and vice
    versa, so a "best effort" copy would silently break 7z/RAR/ISO features on
    the other architecture.

    LGPL compliance: 7z.dll is dynamically linked (via SharpSevenZip's COM
    wrapper) and distributed under GNU LGPL. See lgpl.txt in the installer.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

# Guard: MSBuild $(PublishDir) may end with a trailing backslash, and when
# wrapped in &quot;…&quot; in the .csproj Exec command the \" sequence at the
# end is eaten by Windows command-line parsing, leaving a stray " in the value.
$PublishDir = $PublishDir.TrimEnd('"', '\')

$hostUi = $Host.UI

function Write-Info($msg) { $hostUi.WriteLine("INFO: $msg") }
function Write-Warn($msg) { $hostUi.WriteLine("WARN: $msg") }
function Write-Err($msg) { $hostUi.WriteLine("ERROR: $msg") }

# Candidate locations where a 7z.dll might exist (any architecture)
$candidates = @(
    # Standard 7-Zip installation paths
    "$env:ProgramFiles\7-Zip\7z.dll",
    "${env:ProgramFiles(x86)}\7-Zip\7z.dll",
    # PATH
    (Get-Command "7z.dll" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
)

<#
.SYNOPSIS
    Reads a PE file's COFF header and returns its machine type:
    'x64' (AMD64, 0x8664), 'x86' (i386, 0x014C), 'ARM64' (0xAA64),
    or $null when the file is not a recognized PE image.
#>
function Get-PEMachine([string]$path) {
    try {
        $stream = [System.IO.File]::OpenRead($path)
        try {
            $reader = New-Object System.IO.BinaryReader($stream)
            # DOS header: offset of PE signature (e_lfanew) lives at 0x3C
            $null = $stream.Seek(0x3C, 'Begin')
            if ($stream.Length -lt 0x40) { return $null }
            $peOffset = $reader.ReadInt32()
            if ($peOffset -le 0 -or $peOffset -gt ($stream.Length - 6)) { return $null }
            $null = $stream.Seek($peOffset, 'Begin')
            $sig = $reader.ReadBytes(4)
            if ($sig[0] -ne 0x50 -or $sig[1] -ne 0x45 -or $sig[2] -ne 0 -or $sig[3] -ne 0) { return $null }
            # COFF header first field: Machine (UInt16)
            switch ($reader.ReadUInt16()) {
                0x8664 { return 'x64' }
                0x014C { return 'x86' }
                0xAA64 { return 'ARM64' }
                default { return $null }
            }
        } finally {
            $reader.Dispose()
        }
    } catch {
        return $null
    }
}

# --- Locate one genuine DLL per architecture ---
$found = @{}
foreach ($c in $candidates) {
    if (-not $c -or -not (Test-Path $c)) { continue }
    if ($found.ContainsKey('x64') -and $found.ContainsKey('x86')) { break }

    $machine = Get-PEMachine $c
    if (-not $machine) {
        Write-Warn "Skipping '$c' (not a recognized PE image)."
        continue
    }
    if ($machine -eq 'ARM64') {
        Write-Warn "Skipping ARM64 7z.dll at '$c' (this package layout only ships x64/x86)."
        continue
    }
    if (-not $found.ContainsKey($machine)) {
        $found[$machine] = $c
        Write-Info "Found ${machine} 7z.dll: $c"
    }
}

if ($found.Count -eq 0) {
    Write-Warn "No usable 7z.dll found in standard locations."
    Write-Warn "7z operations will fall back to auto-detection at runtime."
    Write-Info "Install 7-Zip from https://www.7-zip.org/ (both x64 and x86 editions to bundle both architectures) and re-run this script."
    exit 0  # non-fatal — app works without bundled 7z.dll if 7-Zip is installed on the target system
}

# --- Copy each architecture from a genuinely matching source ---
foreach ($arch in @('x64', 'x86')) {
    $targetDir = Join-Path $PublishDir $arch

    if (-not $found.ContainsKey($arch)) {
        Write-Warn "No ${arch} 7z.dll found on this machine — '$targetDir' will not be bundled."
        Write-Warn "${arch} builds of MantisZip will need 7-Zip (${arch}) installed on the user's system, or the user must locate 7z.dll via the in-app dialog."
        continue
    }

    try {
        $null = New-Item -ItemType Directory -Path $targetDir -Force
        Copy-Item -Path $found[$arch] -Destination (Join-Path $targetDir "7z.dll") -Force
        Write-Info "Copied ${arch} 7z.dll from '$($found[$arch])' → '$targetDir'"
    } catch {
        Write-Err "Failed to copy ${arch} 7z.dll: $_"
        exit 1
    }
}

Write-Info "7z.dll bundling finished. LGPL 7-Zip DLL is dynamically linked."
exit 0
