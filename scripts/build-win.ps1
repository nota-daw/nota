# SPDX-License-Identifier: AGPL-3.0-only
#
# Local Windows build of Nota: native engine (x64) + managed + smoke test.
# Run from a "Developer PowerShell for VS 2022" so cl/clang-cl + ninja are on PATH.
#
# Usage: pwsh scripts/build-win.ps1

$ErrorActionPreference = 'Stop'
# Stop on a failing native command (cmake, dotnet, the smoke test) too — without this
# a non-zero exit code is ignored and the script, and CI, report success. PowerShell 7.3+.
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Join-Path $PSScriptRoot '..')

$Native = 'src/native/nota.engine/build'

Write-Host "==> Building native engine (x64/WASAPI)"
cmake -G Ninja -S src/native/nota.engine -B $Native "-DCMAKE_BUILD_TYPE=Release"
cmake --build $Native

Write-Host "==> Scanner self-test"
& "$Native/nota-scanner-selftest.exe"

Write-Host "==> Building managed (.NET)"
dotnet build Nota.sln -c Release --nologo

Write-Host "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release --nologo

Write-Host "==> Plugin scan check (large folders, scratch data dir)"
$env:NOTA_DATA_DIR = Join-Path ([System.IO.Path]::GetTempPath()) ("nota-scan-scale-" + [guid]::NewGuid().ToString('N'))
dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --scan-scale (Resolve-Path "$Native/nota-scanworker.exe").Path
Remove-Item Env:NOTA_DATA_DIR

Write-Host "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
