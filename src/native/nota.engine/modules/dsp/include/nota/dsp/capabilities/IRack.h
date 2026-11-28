// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IRack — capability interface for nested multi-chain racks.

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>

namespace nota::dsp::capabilities {

class NOTA_DSP_API IRack {
  public:
    virtual ~IRack();

    virtual int32_t chainCount() const = 0;
    virtual bool chainsHaveOpenEditor(bool includeInstruments) const = 0;
    virtual bool hasSidechainChild() const noexcept = 0;
};

} // namespace nota::dsp::capabilities
