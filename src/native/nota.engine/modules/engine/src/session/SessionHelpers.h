// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared Session view inline helpers used across session/*.

#pragma once

#include "model/Graph.h"
#include "model/Track.h"

#include <cstdint>

namespace nota::detail::session_helpers {

inline bool sessionLaunchable(const Track& t) {
    return t.type() == TrackType::Instrument || t.type() == TrackType::Audio;
}

inline const SessionSlot* slotAt(const Track& t, int32_t scene) {
    return (scene >= 0 && scene < static_cast<int32_t>(t.sessionSlots.size()))
               ? &t.sessionSlots[static_cast<size_t>(scene)]
               : nullptr;
}

inline SceneInfo sceneOr(const Graph& g, int32_t scene) {
    return (scene >= 0 && scene < static_cast<int32_t>(g.scenes.size())) ? g.scenes[static_cast<size_t>(scene)]
                                                                         : SceneInfo{};
}

} // namespace nota::detail::session_helpers
