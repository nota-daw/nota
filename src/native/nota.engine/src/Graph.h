// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Immutable audio-graph snapshot (AR-5). The message thread builds a new Graph
// on every structural edit (add track / add clip) and publishes it atomically;
// the audio thread reads the current snapshot pointer once per block. Tracks are
// shared by pointer across snapshots so their atomic mix params stay stable.
//
// M1 simplification: retired snapshots are kept alive until stop() rather than
// reclaimed via hazard pointers. Edits are infrequent and small, so the bounded
// retention is acceptable; hardening to true lock-free reclamation is a later task.

#pragma once

#include "Track.h"
#include <memory>
#include <vector>

namespace nota {

struct Graph {
    std::vector<std::shared_ptr<Track>> tracks;
    int32_t sceneCount = 8;   // Session view rows (M5)
    // Master-volume automation (M9 follow-up): a graph-level lane (not tied to a
    // track), applied to the master gain in applyAutomation. Undo-free via the
    // snapshot, like track lanes. NB: fresh-graph builds must carry it forward
    // (same footgun as sceneCount) — copy it wherever sceneCount is copied.
    AutomationLane masterVolume;
    // Master effect chain: a dedicated track (id kMasterTrackId) that isn't in `tracks`,
    // so it stays out of the arrangement/mixer track lists. The render runs the full mix
    // through its device chain before the master fader. Carried forward like masterVolume.
    std::shared_ptr<Track> masterTrack;
};

} // namespace nota
