// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/poly/PolyVoiceManager.h"
#include "nota/dsp/poly/SynthVoice.h"

namespace nota::dsp {

template class PolyVoiceManager<SynthVoice, 8>;
template class PolyVoiceManager<SynthVoice, 16>;
template class PolyVoiceManager<SynthVoice, 32>;

} // namespace nota::dsp
