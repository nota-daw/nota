# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
pkgs:
with pkgs;
[
  alsa-lib
  libpulseaudio # PulseAudio / PipeWire output: miniaudio dlopens libpulse.so.0
  libGL
  libxkbcommon
  fontconfig
  freetype
  icu
  openssl
  libx11
  libxcursor
  libxext
  libxi
  libxinerama
  libxrandr
  libxrender
  libxfixes
  libice
  libsm
  xorgproto
  zlib
]
