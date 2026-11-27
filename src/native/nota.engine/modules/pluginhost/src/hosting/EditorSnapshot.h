// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <juce_gui_basics/juce_gui_basics.h>

#include <cstdint>
#include <vector>

namespace nota::pluginhost::detail {

std::vector<uint8_t> snapshotEditor(juce::DocumentWindow& window);

} // namespace nota::pluginhost::detail
