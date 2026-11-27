// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#if defined(_WIN32)
#if defined(NOTA_DSP_API_EXPORTS)
#define NOTA_DSP_API __declspec(dllexport)
#else
#define NOTA_DSP_API __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_DSP_API __attribute__((visibility("default")))
#else
#define NOTA_DSP_API
#endif
