/* SPDX-License-Identifier: AGPL-3.0-only */
/* Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. */
/* Hand-written portable config for Nota's encoder-only LAME build (see CMakeLists.txt
   one level up). Mirrors the relevant subset of upstream's configMS.h / configure
   output: no NASM, no SSE intrinsics, no front end — just what libmp3lame + the
   mpglib decoder it links against test for. */
#ifndef NOTA_LAME_CONFIG_H
#define NOTA_LAME_CONFIG_H

#define STDC_HEADERS 1
#define PROTOTYPES 1

#define HAVE_STDINT_H 1
#ifndef _MSC_VER /* MSVC has no <inttypes.h> */
#define HAVE_INTTYPES_H 1
#endif
#define HAVE_ERRNO_H 1
#define HAVE_FCNTL_H 1
#define HAVE_LIMITS_H 1
#define HAVE_STRCHR 1
#define HAVE_MEMCPY 1

/* The in-tree mpglib decoder is compiled in (lame.c calls the hip_* API). */
#define HAVE_MPGLIB 1

/* No HAVE_NASM / HAVE_XMMINTRIN_H on purpose: pure-C build, the i386/ (nasm) and
   vector/ (intrinsics) upstream directories are not vendored at all. */

/* Fixed-width type sizes for the three toolchains we ship (all LP64 except
   Windows LLP64; every supported target has 32-bit int and 16-bit short). */
#define SIZEOF_SHORT 2
#define SIZEOF_INT 4
#define SIZEOF_UNSIGNED_SHORT 2
#define SIZEOF_UNSIGNED_INT 4
#define SIZEOF_FLOAT 4
#define SIZEOF_DOUBLE 8
#if defined(_WIN32) || defined(_WIN64)
#define SIZEOF_LONG 4
#define SIZEOF_UNSIGNED_LONG 4
#else
#define SIZEOF_LONG 8
#define SIZEOF_UNSIGNED_LONG 8
#endif

/* IEEE float typedefs used by libmp3lame/util.h — upstream's configure writes
   the same ones into its generated config.h (config.h.in lines 56-74). */
typedef float ieee754_float32_t;
typedef double ieee754_float64_t;
typedef long double ieee854_float80_t;

#endif /* NOTA_LAME_CONFIG_H */
