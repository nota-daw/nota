# Bug Assessment: Linux AppImage requires too-new glibc (fails on RHEL 8/9)

- **Slug**: linux-glibc-baseline
- **Created**: 2026-10-18
- **Source**: https://github.com/nota-daw/nota/issues/82
  - Host parsed from URL: `github.com` — URL trust policy branch: `allowlisted` (public bug-report host).
- **Verdict**: valid
- **Severity**: medium

## Report (summarized)

Issue #82 «Linux: build the AppImage against an older glibc so it runs on RHEL 8/9» (open, label
`enhancement`). The Linux release jobs build on `ubuntu-latest` / `ubuntu-24.04-arm` (glibc 2.39),
so the produced binaries (`libnota_engine.so`, `nota-scanworker`, the .NET apphost) refuse to load
on RHEL/Alma/Rocky 8 (glibc 2.28) and 9 (2.34). The issue proposes building inside
`manylinux_2_28` containers (AlmaLinux 8) with `gcc-toolset-13/14` for C++20, static-linking
libstdc++/libgcc where needed, adding a CI `objdump -T` GLIBC-version guard, checking any bundled
prebuilt binaries' glibc floor, documenting the FUSE requirement, and verifying by launching the
AppImage in `rockylinux:8`/`:9` containers. Area: Build / packaging / CI. No comments.

## Symptom

The Linux AppImage built by CI does not start on distros with glibc < 2.39 (concretely RHEL 8/9
and derivatives): the dynamic loader rejects `libnota_engine.so` / `nota-scanworker` with
`GLIBC_x.xx not found`. Expected: the AppImage runs on glibc ≥ 2.28 (RHEL 8-era and practically
every maintained distro).

## Reproduction

1. Download `Nota-<version>-x86_64.AppImage` from a release (built by `.github/workflows/release.yml`
   on `ubuntu-latest`).
2. On RHEL 9 (glibc 2.34): `./Nota-*.AppImage` → loader error naming a `GLIBC_2.3x` symbol.
3. Confirm the floor: `objdump -T usr/bin/libnota_engine.so | grep GLIBC_ | sort -u` inside the
   extracted AppImage shows requirements above the host's glibc.

(Not reproduced locally in this assessment — macOS dev machine; the mechanism is well understood
and the issue reporter verified the glibc versions. Marking reproduction as discoverable-from-artifact.)

## Suspected Code Paths

- `.github/workflows/release.yml:167-203` — the `linux` job: matrix runners `ubuntu-latest` /
  `ubuntu-24.04-arm` (both glibc 2.39), apt-based dep install. This is the root cause and the
  primary place to fix (add `container:` per matrix leg, swap apt→dnf package names, drop `sudo`).
- `scripts/package-linux.sh` — the whole packaging flow. Already container-friendly: sets
  `APPIMAGE_EXTRACT_AND_RUN=1` (no FUSE needed), downloads `appimagetool` if absent, guards against
  cross-arch build dirs. Needs only: (a) libstdc++/libgcc static link flags for the engine when
  built with gcc-toolset, (b) a possible `-comp` fallback if EL8 `mksquashfs` (4.3, no zstd) gets
  used by the manual path, (c) hint-text updates (the macOS helper message and the mksquashfs error
  mention apt names).
- `src/native/nota.engine/CMakeLists.txt:134-146` — Linux link config: `asound pthread dl m` +
  static FLAC/LAME + whole-archive `nota_pluginhost` (JUCE static, links X11/freetype/fontconfig at
  build time). No runtime .so deps beyond `libasound.so.2` (present on RHEL 8) — so a
  manylinux_2_28-built binary is runtime-portable once the glibc/libstdc++ floor is pinned.
- `.github/workflows/ci.yml:70` — CI also builds on `ubuntu-latest`, but it never ships artifacts;
  glibc there is irrelevant. No change required (optional hardening only).
- `src/managed/Nota.Infrastructure/ModelStore/ModelCatalog.cs` — AI models (htdemucs ONNX,
  onnxruntime) are downloaded at runtime from GitHub releases, **not bundled** in the AppImage;
  Microsoft's onnxruntime Linux builds target an old glibc floor. Only worth a spot-check, no
  packaging change.

## Root Cause Hypothesis

High confidence. The Linux release binaries are compiled and linked on Ubuntu 24.04 (glibc 2.39);
ELF symbol versioning ties them to that glibc, so any older host fails at load. Nothing in the
codebase prevents an old-baseline build: the engine's only non-static runtime dependency is
`libasound`, the .NET self-contained runtime / Avalonia / SkiaSharp natives already target old
glibc, and `package-linux.sh` already runs FUSE-less in containers. The fix is confined to CI job
configuration plus small packaging-script adjustments.

## Proposed Remediation

**Preferred** (container baseline — the issue's own plan, and the standard practice):

1. In `release.yml`, give each Linux matrix leg a `container:` — `quay.io/pypa/manylinux_2_28_x86_64`
   (x64 on `ubuntu-latest`) and `quay.io/pypa/manylinux_2_28_aarch64` (arm64 on `ubuntu-24.04-arm`).
   Replace the apt step with dnf installs (`alsa-lib-devel libX11-devel libXext-devel
   libXrandr-devel libXinerama-devel libXcursor-devel freetype-devel fontconfig-devel
   squashfs-tools …`, possibly enabling powertools/CRB + EPEL, which manylinux images largely have
   set up) and a `gcc-toolset` providing C++20; no `sudo` inside the container. `actions/checkout`,
   `setup-dotnet`, `setup-node` all work in glibc containers.
2. Pin the C++ runtime floor: build the engine with `-static-libstdc++ -static-libgcc` (gcc-toolset's
   libstdc++ is newer than RHEL 8's system one) — pass via `package-linux.sh` as CMake linker flags
   so `CMakeLists.txt` stays untouched, or as a small CMake option.
3. Add a regression guard step after packaging: `objdump -T` over the shipped ELF files
   (`libnota_engine.so`, `nota-scanworker`, `Nota.App`), fail if any `GLIBC_` version exceeds 2.28.
4. Verify: manual `workflow_dispatch` (linux) run, then launch the built AppImage with
   `--appimage-extract-and-run` inside `rockylinux:8` and `rockylinux:9` containers (at minimum
   headless start / smoke; `tests/Nota.SmokeTest` drives the engine end-to-end).
5. Docs: one line in the Linux install docs (nota-docs) about FUSE / `--appimage-extract-and-run`
   and the supported-distro floor.

**Alternatives**:
- `rockylinux:8` plain container instead of manylinux (same glibc; fewer preinstalled niceties —
  you wire up EPEL/CRB and the toolset yourself). Slightly more YAML, zero pypa dependency.
- Build on Ubuntu with a old-glibc sysroot / `cereal`-style toolchain or zig cc targeting glibc
  2.28. More fragile than a container of the real distro; not recommended.

**Effort estimate**: the code change itself is small — ~30–40 lines of workflow YAML, ~10 lines of
script tweaks, ~15 lines for the guard step. Realistically **half a day to a day and a half**
including 2–3 CI iteration cycles (each linux-only `workflow_dispatch` run is the slow part) and
the rockylinux verification launches. No engine C++ changes, no API/schema impact, nothing
user-facing except the expanded distro support.

**Files likely to change**:
- `.github/workflows/release.yml` (container + dnf deps + glibc guard step)
- `scripts/package-linux.sh` (linker flags, mksquashfs `-comp` fallback, hint text)
- `../nota-docs` (one docs line, optional, separate repo/PR)

**Tests to add or update**:
- CI glibc-floor guard (objdump GLIBC check) — this is the automated lock-in.
- Optional: a workflow step (or periodic job) launching the AppImage in `rockylinux:8` with
  `--appimage-extract-and-run` to prove it loads.

## Risks & Considerations

- **gcc-toolset activation**: the toolset must be on PATH for cmake (`scl enable` / image default).
  manylinux images ship an enabled modern GCC by default — verify which version; C++20 needs ≥ 10
  (prefer 12+).
- **libstdc++ runtime**: without `-static-libstdc++` the gcc-toolset-built binary would demand the
  newer libstdc++ and fail on RHEL 8 exactly like the glibc failure — the guard step should also
  check `GLIBCXX_` versions, not just `GLIBC_`.
- **mksquashfs/zstd**: EL8's squashfs-tools 4.3 lacks zstd. Primary path (appimagetool with
  `APPIMAGE_EXTRACT_AND_RUN=1`) should be unaffected, but the manual fallback in
  `package-linux.sh:157` uses `-comp zstd` — may need a gzip fallback on EL8.
- **Actions in containers**: JS actions run under the runner's node — standard, but the container
  must not break `setup-dotnet`'s install script (glibc-based, fine on 2.28).
- **Blast radius**: confined to the Linux release job; macOS/Windows jobs untouched. A bad change
  is caught by the manual-run path before any tag, and reverting is trivial.
- **SkiaSharp/Avalonia/.NET natives**: already old-glibc per the issue; the objdump guard over the
  whole AppDir would prove it for every shipped ELF, not just ours.

## Open Questions

- [NEEDS CLARIFICATION: manylinux_2_28 vs rockylinux:8 as the base — recommend manylinux (preconfigured
  EPEL/toolset, official arm64 image), but the choice is yours.]
- [NEEDS CLARIFICATION: should the rockylinux launch/smoke be a permanent CI step or a one-off manual
  verification of this change? Recommend one-off + keep the objdump guard permanent.]
