<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: VST3 / AU plugin open-editor active processing

- **Slug**: `plugin-editor-sleep-freeze`
- **Tested**: 2026-11-25T17:59:56+01:00
- **Assessment**: `./assessment.md`
- **Fix**: `./fix.md`
- **Result**: verified

## Checks Executed

| Check | Command | Exit | Outcome |
|-------|---------|------|---------|
| Full build + SmokeTest | `./scripts/build.sh` | 0 | `SMOKE TEST PASSED`, `SCANNER SELFTEST PASSED`, `DEFAULT SCAN PATHS UPGRADE PASSED`; both VST3 and macOS AU open-editor checks `[PASS]` |
| Performance & hosted pool suite | `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --perf` | 0 | `PERF PASSED` |
| Phase 6 streaming & hybrid suite | `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --phase6` | 0 | `PHASE 6 PASSED` |
| Idle engine CPU | `nota_idle_cpu 5 5` | 0 | `0.0% of one core over 5.0 s (limit 5.0%): PASS` |
