// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/rt/OutputRouting.h"

namespace nota::io::audio {

using nota::dsp::kRoutingCueUnavailable;
using nota::dsp::kRoutingMainFellBack;
using nota::dsp::OutputRouting;
using nota::dsp::OutputStage;
using nota::dsp::packRouting;
using nota::dsp::ResolvedRouting;
using nota::dsp::resolveRouting;
using nota::dsp::scatterOutputs;
using nota::dsp::unpackRouting;
using nota::dsp::validRouting;

} // namespace nota::io::audio
