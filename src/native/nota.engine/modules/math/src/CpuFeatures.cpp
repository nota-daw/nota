// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/math/CpuFeatures.h"

#if defined(NOTA_ARCH_X86) && !defined(_MSC_VER)
#include <cpuid.h>
#endif

namespace nota::math {

CpuCapabilities detectCpuCapabilities() noexcept {
    CpuCapabilities caps{};
#if defined(NOTA_SIMD_NEON)
    caps.neon = true;
#endif
#if defined(NOTA_SIMD_FMA)
    caps.fma = true;
#endif
#if defined(NOTA_SIMD_SSE2)
    caps.sse2 = true;
#endif
#if defined(NOTA_ARCH_X86)
    int info[4]{};
#if defined(_MSC_VER)
    __cpuid(info, 1);
#else
    __cpuid(1, info[0], info[1], info[2], info[3]);
#endif
    caps.sse2 = (info[3] & (1 << 26)) != 0;
    const bool osxsave = (info[2] & (1 << 27)) != 0;
    const bool avx = (info[2] & (1 << 28)) != 0;
    caps.fma = caps.fma || ((info[2] & (1 << 12)) != 0);
    if (osxsave && avx) {
        int info7[4]{};
#if defined(_MSC_VER)
        __cpuidex(info7, 7, 0);
#else
        __cpuid_count(7, 0, info7[0], info7[1], info7[2], info7[3]);
#endif
        caps.avx2 = (info7[1] & (1 << 5)) != 0;
    }
#endif
    return caps;
}

} // namespace nota::math
