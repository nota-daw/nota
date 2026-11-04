# Validation

From the existing PR branch in a Visual Studio developer PowerShell:

```powershell
./scripts/build-win.ps1
dotnet run --project specs/071-multiple-cc-lanes/validation/CCProbe.csproj -c Release -- build/inline-cc-probe
dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll --read-lanes build/inline-cc-probe
git diff --check
```

Cover controls, scale mapping, independent curves/heights, stale gestures, minimum
heights, docked notes, folded pad gutters and stacked clips. Capture the same demo
clip at 1100 × 850, Studio Dark, before/after with validation/Capture and inspect it.
Check control pointer hits and graph endpoints, sidebar add after shrinking, primary
Controller-to-Velocity hide; no writes during control or layout-only operations.
