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
    // Registered by address while running. Heap-held: if unregistering finds a buffer switch
    // that never returned, the callback is leaked rather than freed under it.
    InputCallback* cb_ = nullptr;
    bool registered_ = false;
};

} // namespace nota
