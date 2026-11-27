// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EditorPreview — immutable plugin-window snapshot with single-process serials.

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace nota::dsp {

struct NOTA_DSP_API EditorPreview {
    std::shared_ptr<const std::vector<uint8_t>> jpeg;
    uint32_t serial = 0;

    [[nodiscard]] bool empty() const noexcept { return !jpeg || jpeg->empty(); }
    [[nodiscard]] static EditorPreview make(std::vector<uint8_t> bytes);
};

} // namespace nota::dsp
