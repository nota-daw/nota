# Bug Fix: Linux AppImage glibc baseline (runs on RHEL 8/9)

- **Slug**: linux-glibc-baseline
- **Fixed**: 2026-10-18
- **Assessment**: ./assessment.md
- **Status**: applied (CI verification run pending — see Local Verification)

## Summary

The Linux release now builds inside `manylinux_2_28` containers (AlmaLinux 8, glibc 2.28,
gcc-toolset-14) instead of on the Ubuntu 24.04 runners (glibc 2.39), with the engine's C++
runtime linked statically so nothing references a newer libstdc++ than RHEL 8's. A new
`check-glibc-floor.sh` guard fails the release build if any shipped ELF would need glibc > 2.28
or libstdc++ newer than GCC 8's.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `.github/workflows/release.yml` | modified | linux matrix legs get `container: quay.io/pypa/manylinux_2_28_{x86_64,aarch64}`; apt step → dnf (no sudo, root container); new "Guard the glibc floor" step after packaging |
| `scripts/check-glibc-floor.sh` | added | scans every ELF under the AppDir payload for `GLIBC_`/`GLIBCXX_`/`CXXABI_` symbol versions above the 2.28 / 3.4.25 / 1.3.11 baseline; exits 1 listing offenders |
| `scripts/package-linux.sh` | modified | engine build passes `-static-libstdc++ -static-libgcc` (toolset libstdc++ is newer than RHEL 8's system one); manual mksquashfs path falls back from zstd to gzip (EL8 squashfs-tools 4.3 has no zstd); error hint made distro-agnostic |
| `scripts/build-linux.sh` | modified | comment only: EL8/dnf equivalent of the dev-dependency list |
| `CHANGELOG.md` | modified | [Unreleased] › Fixed entry referencing nota-daw/nota#82 |
| `FEATURES.md` | modified | Linux bullet states the glibc ≥ 2.28 floor (RHEL/Alma/Rocky 8+, Debian 10+) |

## Diff Highlights

release.yml — per-arch container + guard step:

```yaml
        include:
          - arch: x64
            runner: ubuntu-latest
            image: quay.io/pypa/manylinux_2_28_x86_64
          - arch: arm64
            runner: ubuntu-24.04-arm
            image: quay.io/pypa/manylinux_2_28_aarch64
    runs-on: ${{ matrix.runner }}
    container: ${{ matrix.image }}
    ...
      - name: Install build + packaging deps
        run: |
          dnf install -y --setopt=install_weak_deps=False \
            alsa-lib-devel libX11-devel libXext-devel libXrandr-devel \
            libXinerama-devel libXcursor-devel freetype-devel fontconfig-devel \
            ninja-build squashfs-tools file libicu
      - name: Package AppImage
        run: bash scripts/package-linux.sh ${{ matrix.arch }}
      - name: Guard the glibc floor (the AppImage must run on glibc >= 2.28)
        run: bash scripts/check-glibc-floor.sh dist/Nota.AppDir/usr/bin
```

package-linux.sh — static C++ runtime into the engine build:

```bash
STATIC_CXX_RT="-static-libstdc++ -static-libgcc"
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_SHARED_LINKER_FLAGS="${STATIC_CXX_RT}" \
  -DCMAKE_EXE_LINKER_FLAGS="${STATIC_CXX_RT}" >/dev/null
```

## Tests Added or Updated

- `scripts/check-glibc-floor.sh` (run by release CI on every Linux build) — pins the whole
  shipped payload, not just our binaries: our `.so`/scanworker/apphost plus the bundled
  .NET/Avalonia/SkiaSharp natives. A build linked on a newer image fails the release.

## Local Verification

Everything below ran on this machine (macOS) against the real target image under Docker
(x86_64 via QEMU; compilation/linking results are unaffected by emulation):

- `quay.io/pypa/manylinux_2_28_x86_64`: gcc-toolset-14 (GCC 14.2.1) pre-enabled, cmake 4.4.4
  present, EPEL repos configured — confirmed from the image itself and the pypa build scripts.
- `-static-libstdc++ -static-libgcc` into a **shared** library (the main open risk in the
  assessment): links cleanly; `objdump -T` shows **zero** `GLIBCXX_`/`CXXABI_` references and
  max `GLIBC_2.25`. Same for a plain executable.
- The exact CI dnf install: all package names resolve on EL8 (alsa-lib-devel 1.2.10,
  ninja-build 1.8.2, squashfs-tools 4.3, …). mksquashfs 4.3 confirmed **without** zstd → the
  gzip fallback in package-linux.sh is required and covers it.
- .NET 10 SDK (10.0.402) in the container: `dotnet restore` from nuget.org (HTTPS via the
  image's OpenSSL) and `dotnet run` both work — after installing `libicu`, which the bare
  AlmaLinux 8 base lacks.
- `check-glibc-floor.sh` functional tests in the same container: real static binaries → OK
  (exit 0); stubbed symbol tables → correctly fails (exit 1) on `GLIBC_2.34`,
  `GLIBCXX_3.4.33`, `CXXABI_1.3.13` and the numeric edge `GLIBC_2.28.1 > 2.28`, listing each
  offender; good-only → exit 0; missing path → clear error, exit 1.
- `bash -n` on all three scripts; `release.yml` parses as YAML.

Not verifiable locally: the appimagetool primary packaging path inside the container — its
static-PIE runtime doesn't exec under QEMU (the exact limitation package-linux.sh's manual
fallback exists for, now with gzip). It runs on the real x86_64 runner today and the aarch64
runner is native; the manual CI run is the proof.

## Deviations from Assessment

- `libicu` added to the CI dnf deps (not in the assessment's package list): the .NET SDK
  hard-aborts without ICU and the AlmaLinux 8 base image doesn't include it. Build-time only;
  desktop distros the AppImage targets ship libicu anyway.
- The assessment's "verify by launching the AppImage in rockylinux:8/9 containers" is not part
  of this change's CI; it stays a manual/`/speckit-bug-test` step (see Follow-ups).

## Follow-ups

- Push the branch and run Actions ▸ Release ▸ Run workflow with `platforms=linux` (no release
  is created) — the real end-to-end proof, including appimagetool and the JUCE build under GCC 14.
- Then `/speckit-bug-test slug=linux-glibc-baseline`: launch the produced AppImage with
  `--appimage-extract-and-run` in `rockylinux:8` and `rockylinux:9` containers.
- nota-docs (separate repo): one line on the Linux install/download page — supported distros
  (glibc ≥ 2.28) and FUSE / `--appimage-extract-and-run` without it.
- Close nota-daw/nota#82 when the verified release ships.
