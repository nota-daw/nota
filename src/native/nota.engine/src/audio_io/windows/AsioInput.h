// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AsioInput — AudioInput over the shared AsioSession (specs/049, research R2). It never
// opens a second driver: start() registers the capture callback with the session the
// AsioBackend already runs, so input and output share one callback and clock. The device
// argument is ignored — the input pair is part of the session's configuration.

#pragma once

#include "audio_io/AudioInput.h"

namespace nota {

class AsioInput final : public AudioInput {
  public:
    ~AsioInput() override;

    bool start(InputCallback cb, uint32_t device = 0) override;
    void stop() override;
    bool isRunning() const override;
    double sampleRate() const override;

  private:
    InputCallback cb_; // registered by address while running
    bool registered_ = false;
};

} // namespace nota
