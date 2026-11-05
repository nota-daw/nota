# Bug Verification: Audio dropouts every ~10 s while Nota Remote is open

- **Slug**: remote-networksetup-dropouts
- **Tested**: 2026-11-05
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The reproduction no longer reproduces: with the fixed build playing `demo17.nota` and a Remote client connected, three minutes passed with zero dropouts and no `networksetup` process, where 0.59.1 dropped out every ~10 s in the same setup. The full build and smoke suite pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (pre-fix baseline) | 0.59.1 from /Applications, `demo17.nota` playing a Session scene, Remote open in a browser | fail (bug present) | dropouts #1–#98 at a steady ~10.07 s; a `networksetup` spawn matched dropout #39 within 2 ms |
| Reproduction (post-fix) | worktree Release build (`Nota.App`), same user data dir, same project playing, Remote client connected (192.168.31.249 → :7789 ESTABLISHED); 180 s watch of the log and `pgrep -x networksetup` every 50 ms | pass | 0 new `Audio dropout` lines; 0 `networksetup` processes; engine `xruns` = 0 over MCP while `playing: true` |
| New / updated tests | `scripts/build.sh` (smoke) | pass | `macOS hardware ports read in-process from SystemConfiguration`; `link queries reuse the port map until the network changes (read 1× for 40 queries)` |
| Regression suite | `scripts/build.sh` | pass | `SCANNER SELFTEST PASSED`, `SMOKE TEST PASSED`, `SCAN SCALE PASSED`, exit 0 |
| Port-map equivalence | SystemConfiguration map vs `networksetup -listallhardwareports` | pass | identical 8 device/port pairs on this Mac |
| Lint / type-check | `dotnet build Nota.sln -c Release` (inside build.sh) | pass | no new warnings in the changed files |

## Output Excerpts

```
start 22:39:50 dropouts_in_log=96
end 22:42:50 new_dropouts=0 networksetup_seen=0
{"sampleRate":44100,"xruns":0,"sceneCount":8}
```

## Residual Risks

- Not exercised: an actual USB-tethered phone (iPhone USB / Android) appearing mid-session — the map now refreshes on `NetworkAddressChanged` only; the read path is the same SystemConfiguration call the smoke test covers.
- Whether the Remote popup was open during the post-fix window was not recorded; the connected client alone drove `RemoteHub.Devices` → `OnUsbLink`, the path that spawned before.
- Only macOS 26.6 / Apple Silicon was tested; Windows and Linux paths are unchanged.

## Recommendation

Close the bug — verified end-to-end on the reporter's setup. Optionally confirm USB tethering still shows the USB address first in the Remote popup before release.
