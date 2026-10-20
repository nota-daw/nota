// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ScopedNoDenormals — single RAII FTZ/DAZ guard for the whole engine (AR-6).
// Replaces the scattered copies in Engine_Render.cpp, Consort.h, Keys.h and
// Pentad.h. Construct on the audio thread and on every AudioThreadPool worker.

#pragma once

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#include <xmmintrin.h>
#endif
#include <cstdint>

namespace nota {
namespace dsp {

class ScopedNoDenormals {
  public:
    ScopedNoDenormals() noexcept { enter(); }
    ~ScopedNoDenormals() noexcept { leave(); }

    ScopedNoDenormals(const ScopedNoDenormals&) = delete;
    ScopedNoDenormals& operator=(const ScopedNoDenormals&) = delete;

  private:
#if defined(__aarch64__) || defined(_M_ARM64)
    uint64_t saved_ = 0;

    void enter() noexcept {
        __asm__ volatile("mrs %0, fpcr" : "=r"(saved_));
        // FPCR.FZ bit 24: flush-to-zero. ARM64 has no DAZ bit; FZ covers it.
        const uint64_t fz = saved_ | (1ULL << 24);
        __asm__ volatile("msr fpcr, %0" : : "r"(fz));
    }
    void leave() noexcept { __asm__ volatile("msr fpcr, %0" : : "r"(saved_)); }
#elif defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
    unsigned saved_ = 0;

    void enter() noexcept {
        saved_ = _mm_getcsr();
        _mm_setcsr(saved_ | 0x8040); // FTZ (bit 15) | DAZ (bit 6)
    }
    void leave() noexcept { _mm_setcsr(saved_); }
#else
    void enter() noexcept {}
    void leave() noexcept {}
#endif
};

} // namespace dsp
} // namespace nota
