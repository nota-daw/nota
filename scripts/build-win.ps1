# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Local Windows build of Nota: native engine (x64) + managed + smoke test.
# Run from a "Developer PowerShell for VS 2022" so cl/clang-cl + ninja are on PATH.
#
# Usage: pwsh scripts/build-win.ps1 [free|pro]   (default free)

param(
    [string]$Edition = 'free'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$Native = 'src/native/nota.engine/build'

# Args carrying a variable are quoted so PowerShell always expands them and passes
# a single token (bare `-DNOTA_EDITION=$Edition` can reach CMake unexpanded).
Write-Host "==> Building native engine (edition=$Edition, x64/WASAPI)"
cmake -G Ninja -S src/native/nota.engine -B $Native "-DCMAKE_BUILD_TYPE=Release" "-DNOTA_EDITION=$Edition"
cmake --build $Native

Write-Host "==> Building managed (.NET, edition=$Edition)"
dotnet build Nota.sln -c Release "-p:NotaEdition=$Edition" --nologo

Write-Host "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release "-p:NotaEdition=$Edition" --nologo

Write-Host "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
