// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EditorKeys — thread-safe single-process queue routing plugin window keys to the app.

#pragma once

#include "nota/core/Export.h"

#include <cstdint>

namespace nota::core::editorkeys {

struct EditorKeyEvent {
    int32_t ch = 0;
    bool down = false;
};

constexpr int32_t tokenOf(int32_t ch) noexcept {
    return (ch >= 'a' && ch <= 'z') ? ch - 'a' + 'A' : ch;
}

NOTA_CORE_API void setClaimed(const int32_t* chars, int32_t count);
NOTA_CORE_API bool claims(int32_t ch);
NOTA_CORE_API void push(int32_t ch, bool down);
NOTA_CORE_API int32_t drain(int32_t* outChars, uint8_t* outDown, int32_t maxCount);

} // namespace nota::core::editorkeys
