// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Editor preview (specs/046): a small JPEG of a hosted plug-in's own window, captured when the
// window is hidden, which the Devices panel draws behind the plug-in card. It lives on the
// device (Device / Instrument) like the fold flag, so moves, copies and undo carry it. The
// bytes are immutable once made: a clone shares them. View data, message thread only — the
// audio thread never reads it.

#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota {

struct EditorPreview {
    std::shared_ptr<const std::vector<uint8_t>> jpeg;
    // Unique per preview made (0 = none), so the UI can tell a new capture with an int
    // compare. A clone keeps its source's serial: it is the same picture.
    uint32_t serial = 0;

    bool empty() const { return !jpeg || jpeg->empty(); }

    static EditorPreview make(std::vector<uint8_t> bytes) {
        if (bytes.empty()) return {};
        static std::atomic<uint32_t> next{1};
        return {std::make_shared<const std::vector<uint8_t>>(std::move(bytes)), next.fetch_add(1)};
    }
};

} // namespace nota
