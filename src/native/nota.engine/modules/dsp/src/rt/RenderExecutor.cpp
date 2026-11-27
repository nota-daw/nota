// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/rt/RenderExecutor.h"

namespace nota::dsp {

RenderExecutor::ThreadState& RenderExecutor::state() noexcept {
    static thread_local ThreadState st{};
    return st;
}

} // namespace nota::dsp
