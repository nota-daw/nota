# Bug Fix: Audio dropouts every ~10 s while Nota Remote is open

- **Slug**: remote-networksetup-dropouts
- **Fixed**: 2026-11-05
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Remote no longer spawns `networksetup` to tell USB network links apart. The device → hardware-port
map is read in-process from SystemConfiguration and kept until the network changes, so nothing
on Remote's polling path can start a child process and stall the audio callback.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.Remote/RemoteLinks.cs` | modified | `UsbDevices()` reads via the new `MacHardwarePorts` (P/Invoke: `SCNetworkInterfaceCopyAll`, `SCNetworkInterfaceGetBSDName`, `SCNetworkInterfaceGetLocalizedDisplayName`, CF array/string helpers); the 10 s TTL is gone — the map is cached until `Invalidate()`, which `NetworkChange.NetworkAddressChanged` calls; `ParseHardwarePorts`/`ListHardwarePorts` and `System.Diagnostics` removed; `HardwarePorts()` and `PortReads` exposed for the smoke test |
| `src/managed/Nota.App/Remote/RemoteService.cs` | modified | `OnNetworkChanged` also calls `RemoteLinks.Invalidate()` so the map is fresh before the QR refresh |
| `tests/Nota.SmokeTest/RemoteTests.cs` | updated test | the `networksetup` parser check is replaced by two macOS checks (below) |
| `CHANGELOG.md` | modified | `[Unreleased]` → `Fixed` entry (#511) |

## Diff Highlights (optional)

```csharp
lock (Gate)
{
    if (_ports is not null) return _ports;
    _ports = MacHardwarePorts.Read();   // in-process, no child process
    PortReads++;
    return _ports;
}
```

## Tests Added or Updated

- `RemoteTests.Links` — "macOS hardware ports read in-process from SystemConfiguration": the map is non-empty with non-empty device and port names.
- `RemoteTests.Links` — "link queries reuse the port map until the network changes": after `Invalidate()`, 20 × (`All()` + `OnUsbLink()`) read the map exactly once.

## Local Verification

- Commands run: `scripts/build.sh` (worktree `../nota-remote-fix`, fresh engine build) → exit 0; `SCANNER SELFTEST PASSED`, `SMOKE TEST PASSED`, `SCAN SCALE PASSED`; both new checks `[PASS]`.
- Manual checks: the map read through SystemConfiguration (`en4=Ethernet Adapter (en4)`, `en5`, `en6`, `bridge0=Thunderbolt Bridge`, `en0=Wi-Fi`, `en1..en3=Thunderbolt 1..3`) is identical to `networksetup -listallhardwareports` on this Mac.

## Deviations from Assessment

- `RemoteService.OnNetworkChanged` (not listed in the assessment) gained one `RemoteLinks.Invalidate()` call, so the invalidation does not depend on the order of the two `NetworkAddressChanged` handlers.

## Follow-ups

- Runtime check in the real app with a connected Remote client (xrun count stays flat, no `networksetup` processes) — `/speckit-bug-test`.
