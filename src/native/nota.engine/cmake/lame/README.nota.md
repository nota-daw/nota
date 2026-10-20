# LAME 3.100 — Nota's encoder-only snapshot

Source: upstream `lame-3.100.tar.gz` (SourceForge), unmodified files.

What is vendored (and used by `CMakeLists.txt` here):

- `libmp3lame/` — the encoder library (`*.c/*.h` only, plus the
  `vector/lame_intrin.h` header that `fft.c` includes unconditionally)
- `mpglib/` — the MP3 decoder `mpglib_interface.c` links against (`lame.c`
  drives the `hip_*` API unconditionally; `HAVE_MPGLIB` is set in `config.h`)
- `include/lame.h` — the public header
- `COPYING` (LGPL-2.1 text — the library's license), `LICENSE` (usage FAQ),
  `README` — provenance

What is deliberately NOT vendored: `i386/` (nasm asm), `vector/` (MMX/SSE
intrinsics), the `frontend/` CLI, ACM/DLL/dshow project trees, autotools files.
This is a pure-C build; offline export speed is far beyond real-time anyway.

Files added by Nota (not upstream): `config.h`, `CMakeLists.txt`,
`README.nota.md`.

License: **LGPL-2.0-or-later** — see `COPYING` here and note 7 in
`LICENSES/third-party.md` (repo root) for how it is accounted in the AGPL build.
