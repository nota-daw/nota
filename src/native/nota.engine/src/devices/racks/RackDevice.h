// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice — a parallel-chain Audio Effect Rack. A container Device whose
// parallel chains are effects-only (no instrument): the incoming stereo buffer is
// fanned to every chain, each chain runs its own insert devices, and the results
// are summed (per-chain gain/pan/mute/solo) back in place. All the chain/macro/
// snapshot/persistence machinery is shared with the Instrument Rack via RackCore.
//
// An empty rack (no chains) passes audio through untouched, so dropping the device
// in is transparent until the user builds chains.

#pragma once

#include <memory>
#include <algorithm>

#include "RackCore.h"

#include <cstring>

namespace nota {

class RackDevice final : public Device, public RackCore {
  public:
    int32_t builtinKind() const override { return 5; } // Audio Effect Rack (project compat)
    const char* displayName() const override { return "Nota Audio Effect Rack"; }

    void setSampleRate(double sr, int32_t maxBlock) override { setRates(sr, maxBlock); }

    // Forward the DAW transport to every chain child (nested plugins sync too).
    void setTransportInfo(const TransportInfo& ti) override { forwardTransport(ti); }
    void setTransport(double beatStart, double spb, bool playing) override { forwardTempo(beatStart, spb, playing); }

    // ---- in-place processing: Parallel / Series / Select + dry-wet + gain ----
    void process(float* buf, int32_t frames) override;

    // PDC: report chain latency only when compensation is on. Series accumulates
    // along the path; parallel/select align to the longest chain.
    int32_t latencySamples() const override;

    // ---- macros over the plugin-param surface (so they automate/record) -----
    int32_t pluginParamCount() const override { return kNumMacros; }
    std::string pluginParamId(int32_t i) const override { return macroParamId(i); }
    std::string pluginParamName(int32_t i) const override { return macroParamName(i); }
    float pluginParamGet(int32_t i) const override { return macroGet(i); }
    void pluginParamSet(int32_t i, float v) override { macroSet(i, v); }
    int32_t pluginParamIndexOfId(const std::string& id) const override { return macroParamIndexOfId(id); }

    // ---- persistence + clone (shared blob) ----------------------------------
    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<Device> clone() const override;

  private:
    float input_[kMaxBlock * 2];
    float acc_[kMaxBlock * 2];
    float scratch_[kMaxBlock * 2];
};

} // namespace nota
