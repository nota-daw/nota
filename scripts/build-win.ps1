# SPDX-License-Identifier: AGPL-3.0-only
#
# Local Windows build of Nota: native engine (x64) + managed + smoke test.
# Run from a "Developer PowerShell for VS 2022" so cl/clang-cl + ninja are on PATH.
#
# Usage: pwsh scripts/build-win.ps1

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$Native = 'src/native/nota.engine/build'

Write-Host "==> Building native engine (x64/WASAPI)"
cmake -G Ninja -S src/native/nota.engine -B $Native "-DCMAKE_BUILD_TYPE=Release"
cmake --build $Native

Write-Host "==> Building managed (.NET)"
dotnet build Nota.sln -c Release --nologo

Write-Host "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release --nologo

Write-Host "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
