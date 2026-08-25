# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Package Nota for Windows: build the native engine, publish the self-contained
# app, then build an Inno Setup installer (scripts/nota.iss). Uses the Visual
# Studio generator so a single x64 host can cross-build both x64 and ARM64.
# Run from a "Developer PowerShell for VS 2022" (native build needs the MSVC env);
# the ARM64 target also needs the "MSVC v143 - ARM64 build tools" VS component.
# Requires Inno Setup 6.3+ (iscc / ISCC.exe) for the installer.
#
# Usage: pwsh scripts/package-win.ps1 [free|pro] [x64|arm64]   (defaults: free x64)
# Output: dist/Nota-Setup-<version>-<arch>.exe

param(
    [string]$Edition = 'free',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$Version   = (Get-Content VERSION -Raw).Trim()
$Rid       = "win-$Arch"
$CmakeArch = if ($Arch -eq 'arm64') { 'ARM64' } else { 'x64' }
$Native    = "src/native/nota.engine/build-$Arch"
$NativeOut = Join-Path (Get-Location) "$Native/Release"    # VS is multi-config
$PubDir    = Join-Path (Get-Location) "dist/publish-$Arch"

Write-Host "==> Building native engine ($Arch / WASAPI)…"
# Args carrying a variable are quoted so PowerShell always expands them and passes
# a single token (bare `-DNOTA_EDITION=$Edition` can reach CMake unexpanded).
cmake -G "Visual Studio 17 2022" -A $CmakeArch -S src/native/nota.engine -B $Native "-DNOTA_EDITION=$Edition"
cmake --build $Native --config Release

Write-Host "==> Publishing managed app ($Rid, self-contained)…"
if (Test-Path $PubDir) { Remove-Item -Recurse -Force $PubDir }
dotnet publish src/managed/Nota.App -c Release -r $Rid --self-contained true `
  "-p:NotaEdition=$Edition" "-p:NotaNativeDir=$NativeOut" -o $PubDir

# The csproj copies the native artifacts into the publish output for a win RID;
# copy them explicitly too as a safety net.
Copy-Item "$NativeOut/nota_engine.dll" $PubDir -Force
if (Test-Path "$NativeOut/nota-scanworker.exe") { Copy-Item "$NativeOut/nota-scanworker.exe" $PubDir -Force }

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
