// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PolyVoiceManager translation unit — explicit instantiations for the standard
// voice configurations so instruments link without header-only bloat in every
// TU (the template itself stays in PolyVoiceManager.h per FR-002).

#include "dsp/poly/PolyVoiceManager.h"

namespace nota {
namespace dsp {

template class PolyVoiceManager<SynthVoice, 8>;
template class PolyVoiceManager<SynthVoice, 16>;
template class PolyVoiceManager<SynthVoice, 32>;

} // namespace dsp
} // namespace nota
