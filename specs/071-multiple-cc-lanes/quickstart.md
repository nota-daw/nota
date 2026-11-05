<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Validation

From Developer PowerShell, run scripts/build-win.ps1. Build and run the Windows UI
probe:

```powershell
dotnet build specs/071-multiple-cc-lanes/validation/CCProbe.csproj -c Release
dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll specs/071-multiple-cc-lanes/validation
dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll --read-lanes specs/071-multiple-cc-lanes/validation
```

Open a MIDI clip, add three lanes, pick Mod wheel / Expression / Sustain, and edit
independently. Hide and re-add a lane: points remain. Zoom/scroll and undo/reload;
all lanes follow. Add many lanes in a short editor: the overflow scrolls.
Drag Velocity and two extra dividers to different heights. Reopen and re-add those
lanes, then restart; sizes return. Resize and visibility changes create no MIDI edits.
Check Studio Dark/Light and the divider row in Settings > Shortcuts.
