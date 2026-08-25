# SPDX-License-Identifier: AGPL-3.0-only
#
# Package Nota for Windows: build the native engine, publish the self-contained
# app, then build an Inno Setup installer (scripts/nota.iss). Uses the Visual
# Studio generator so a single x64 host can cross-build both x64 and ARM64.
# Run from a "Developer PowerShell for VS 2022" (native build needs the MSVC env);
# the ARM64 target also needs the "MSVC v143 - ARM64 build tools" VS component.
# Requires Inno Setup 6.3+ (iscc / ISCC.exe) for the installer.
#
# Usage: pwsh scripts/package-win.ps1 [x64|arm64]   (default: x64)
# Output: dist/Nota-Setup-<version>-<arch>.exe

param(
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$Version   = (Get-Content VERSION -Raw).Trim()
$Rid       = "win-$Arch"
$CmakeArch = if ($Arch -eq 'arm64') { 'ARM64' } else { 'x64' }
$Native    = "src/native/nota.engine/build-$Arch"
$PubDir    = Join-Path (Get-Location) "dist/publish-$Arch"

Write-Host "==> Building native engine ($Arch / WASAPI)…"
cmake -G "Visual Studio 17 2022" -A $CmakeArch -S src/native/nota.engine -B $Native
cmake --build $Native --config Release

# Locate the freshly built engine DLL. The VS (multi-config) generator usually writes
# to build-<arch>/Release, but the exact layout varies across CMake/VS versions and CI
# images — so resolve it from the actual output instead of hardcoding the path.
$dll = Get-ChildItem -Path $Native -Recurse -Filter nota_engine.dll -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "nota_engine.dll not found under $Native after the native build." }
$NativeOut = $dll.Directory.FullName
Write-Host "    native artifacts: $NativeOut"

Write-Host "==> Publishing managed app ($Rid, self-contained)…"
if (Test-Path $PubDir) { Remove-Item -Recurse -Force $PubDir }
dotnet publish src/managed/Nota.App -c Release -r $Rid --self-contained true `
  "-p:NotaNativeDir=$NativeOut" -o $PubDir

# The csproj copies the native artifacts into the publish output for a win RID;
# copy them explicitly too as a safety net.
Copy-Item (Join-Path $NativeOut 'nota_engine.dll') $PubDir -Force
$scanWorker = Join-Path $NativeOut 'nota-scanworker.exe'
if (Test-Path $scanWorker) { Copy-Item $scanWorker $PubDir -Force }

Write-Host "==> Building installer with Inno Setup…"
function Find-ISCC {
    # PATH first.
    $cmd = (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
    if ($cmd) { return $cmd }
    # Both Program Files roots (Inno Setup 6.4+ installs as 64-bit into %ProgramFiles%).
    $candidates = @(
        (Join-Path ${env:ProgramFiles}       'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)}  'Inno Setup 6\ISCC.exe')
    )
    # Registry install location (per-machine 64/32-bit + per-user).
    $regKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($k in $regKeys) {
        $loc = (Get-ItemProperty -Path $k -ErrorAction SilentlyContinue).InstallLocation
        if ($loc) { $candidates += (Join-Path $loc 'ISCC.exe') }
    }
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    return $null
}
$iscc = Find-ISCC
if (-not $iscc) { throw "Inno Setup (ISCC.exe) not found — install Inno Setup 6.3+ (winget install JRSoftware.InnoSetup)." }
Write-Host "    using $iscc"
& $iscc "/DAppVersion=$Version" "/DArch=$Arch" "/DPubDir=$PubDir" (Join-Path $PSScriptRoot 'nota.iss')

Write-Host "==> Done: dist/Nota-Setup-$Version-$Arch.exe"
