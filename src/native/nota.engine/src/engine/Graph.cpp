// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Graph.h"

namespace nota {

void inheritGraphLevel(Graph& dst, const Graph& src) {
    dst.sceneCount = src.sceneCount;
    dst.scenes = src.scenes;
    dst.masterVolume = src.masterVolume;
    dst.masterTrack = src.masterTrack;
    dst.panLaw = src.panLaw;
    dst.sections = src.sections;
    dst.tempoMap = src.tempoMap;
}

} // namespace nota
