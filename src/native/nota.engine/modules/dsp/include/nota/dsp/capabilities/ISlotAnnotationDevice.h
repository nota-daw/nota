// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ISlotAnnotationDevice — optional capability interface for devices with slot labels/notes (e.g. EQ-8 bands).

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>
#include <string>

namespace nota::dsp::capabilities {

class NOTA_DSP_API ISlotAnnotationDevice {
  public:
    virtual ~ISlotAnnotationDevice();

    virtual int32_t slotAnnotationCount() const = 0;
    virtual const std::string& slotAnnotationLabel(int32_t slot) const = 0;
    virtual const std::string& slotAnnotationNote(int32_t slot) const = 0;
    virtual void setSlotAnnotationText(int32_t slot, const std::string& label, const std::string& note) = 0;
};

} // namespace nota::dsp::capabilities
