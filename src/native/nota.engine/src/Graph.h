// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
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
#include <string>
#include <vector>

namespace nota {

// How a track's / return's pan is applied to a stereo signal (specs/006). Balance drops
// the far channel as the pan moves; Stereo folds it into the near side. Identical for
// mono material and at centre. Groups, clip pan envelopes and rack chains ignore it.
enum class PanLaw : int32_t { Balance = 0, Stereo = 1 };

// A named span of arrangement time — Intro / Verse / Drop (specs/016). Project-level
// authoring data: the audio thread never reads it.
struct Section {
    double startBeat = 0.0;     // >= 0
    double lengthBeats = 0.0;   // > 0
    std::string name;
    bool operator==(const Section&) const = default;
};

struct Graph {
    std::vector<std::shared_ptr<Track>> tracks;
    int32_t sceneCount = 8;   // Session view rows (M5)
    // Scene names / colours / tempo / follow (Session P0). May be shorter than sceneCount
    // (missing rows read as defaults). Carried forward wherever sceneCount is.
    std::vector<SceneInfo> scenes;
    // Master-volume automation (M9 follow-up): a graph-level lane (not tied to a
    // track), applied to the master gain in applyAutomation. Undo-free via the
    // snapshot, like track lanes.
    AutomationLane masterVolume;
    // Master effect chain: a dedicated track (id kMasterTrackId) that isn't in `tracks`,
    // so it stays out of the arrangement/mixer track lists. The render runs the full mix
    // through its device chain before the master fader.
    std::shared_ptr<Track> masterTrack;
    // Project pan law (specs/006). In the snapshot so undo/redo restore it; Balance is the
    // engine default (and every project before format v21).
    PanLaw panLaw = PanLaw::Balance;
    // Arrangement sections (specs/016), sorted by start. In the snapshot so undo/redo
    // restore them and the project saves them.
    std::vector<Section> sections;
};

// Graph-level fields (everything except `tracks`). A fresh graph built from scratch must
// carry them forward or the edit silently resets them — so every such build calls this,
// and a new graph-level field is added here.
inline void inheritGraphLevel(Graph& dst, const Graph& src) {
    dst.sceneCount   = src.sceneCount;
    dst.scenes       = src.scenes;
    dst.masterVolume = src.masterVolume;
    dst.masterTrack  = src.masterTrack;
    dst.panLaw       = src.panLaw;
    dst.sections     = src.sections;
}

} // namespace nota
