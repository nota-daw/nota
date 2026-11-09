# Bug Assessment: Audio dropouts every ~10 s while Nota Remote is open

- **Slug**: remote-networksetup-dropouts
- **Created**: 2026-11-05
- **Source**: https://github.com/nota-daw/nota/issues/511 (host: github.com, policy: allowlisted; the issue was filed from this session's investigation, so its content is first-hand)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

[nota-daw/nota#511](https://github.com/nota-daw/nota/issues/511) — "Audio dropouts every ~10 s while Nota Remote is open (networksetup spawned from the app process)". In 0.59.1 on macOS (Apple Silicon, built-in output, 44.1 kHz / 128 frames) the log shows `WARN Audio dropout #N` at a steady 10.07 s interval while a Remote client (browser tab) is connected or the Remote popup is open. A `networksetup` process caught at 16:08:18.413Z coincides with dropout #39 logged at 16:08:18.415Z. Average DSP load is ~14 %, so the audio callback is not CPU-bound.

## Symptom

With Remote in use, audible dropouts recur every ~10 s regardless of what is playing. Expected: Remote has no effect on audio.

## Reproduction

1. macOS, 128-frame buffer, any project playing.
2. Enable Remote; open it in a browser tab (or keep the Remote popup open).
3. `WARN Audio dropout #N` appears in `~/Library/Application Support/Nota/logs/nota-YYYYMMDD.log` about every 10 s.
4. Close the tab and popup — dropouts stop.

## Suspected Code Paths

- `src/managed/Nota.Remote/RemoteLinks.cs:UsbDevices()` — caches the device→hardware-port map for only 10 s, then calls `ListHardwarePorts()`.
- `src/managed/Nota.Remote/RemoteLinks.cs:ListHardwarePorts()` — `Process.Start("/usr/sbin/networksetup", "-listallhardwareports")`.
- `src/managed/Nota.App/Remote/RemoteService.cs:Links` — the popup reads it every second → `RemoteLinks.All()` → `UsbDevices()`.
- `src/managed/Nota.Remote/RemoteHub.cs:UsbLink()` — every `Devices` read, per connected client → `RemoteLinks.OnUsbLink()` → `UsbDevices()`.

## Root Cause Hypothesis

Confidence: high. While anything polls Remote's links or devices, `UsbDevices()` re-runs `networksetup` every 10 s. Spawning a child process from the large, many-threaded .NET app process (fork/exec) briefly stalls the whole process — COW page-table setup / VM-map contention — long enough for the real-time CoreAudio callback to miss its ~2.9 ms deadline. The timing match (2 ms) between the spawn and the dropout confirms the spawn as the trigger.

## Proposed Remediation

**Preferred**: Never spawn a process for this. On macOS read the BSD name → hardware-port map from SystemConfiguration (`SCNetworkInterfaceCopyAll`, `SCNetworkInterfaceGetBSDName`, `SCNetworkInterfaceGetLocalizedDisplayName`) via P/Invoke — an in-process call that returns the same strings `networksetup` prints ("iPhone USB", "Wi-Fi", "Thunderbolt Bridge"…). Additionally drop the 10 s time-to-live: keep the map until `NetworkChange.NetworkAddressChanged` fires (an interface appearing is exactly when a cable comes up), so even the in-process call happens only on a network change.

**Alternatives**:
- Keep `networksetup` but refresh only on network change — still a spawn whenever the network changes mid-playback (Wi-Fi roaming, AWDL), so dropouts remain possible.

**Files likely to change**:
- `src/managed/Nota.Remote/RemoteLinks.cs`
- `tests/Nota.SmokeTest/RemoteTests.cs`
- `CHANGELOG.md`

**Tests to add or update**:
- Smoke: on macOS the SystemConfiguration map is non-empty and names the built-in Wi-Fi/Ethernet port by BSD name (`en0` …) — proves the interop works without a process.
- Smoke: repeated `RemoteLinks.All()` / `OnUsbLink()` calls do not re-read the map until it is invalidated.
- Keep `ParseHardwarePorts` only if still used; otherwise remove it and its test.

## Risks & Considerations

- P/Invoke into SystemConfiguration/CoreFoundation: CF ownership rules (Copy → CFRelease; Get → no release) must be followed to avoid leaks/crashes.
- `SCNetworkInterfaceGetLocalizedDisplayName` is localized; the USB check matches "USB"/"Android", which Apple keeps untranslated in port names ("iPhone USB"). Same exposure as `networksetup`, which also prints localized names.
- Windows/Linux paths are unaffected (no process spawn there).

## Open Questions

- None blocking.
