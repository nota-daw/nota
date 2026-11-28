// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/EditorPreview.h"

#include <atomic>
#include <memory>
#include <cstdint>
#include <utility>
#include <vector>

namespace nota::dsp {
namespace {

std::atomic<uint32_t> gNextPreviewSerial{1};

} // namespace

EditorPreview EditorPreview::make(std::vector<uint8_t> bytes) {
    if (bytes.empty()) return {};
    return {
        std::make_shared<const std::vector<uint8_t>>(std::move(bytes)),
        gNextPreviewSerial.fetch_add(1u, std::memory_order_relaxed),
    };
}

} // namespace nota::dsp
