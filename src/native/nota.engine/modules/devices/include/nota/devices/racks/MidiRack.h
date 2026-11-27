// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiRack — parallel-chain MIDI Effect Rack (zone routing, transpose, merge/thru).

#pragma once

#include "nota/devices/Export.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/capabilities/IRack.h"

#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::devices {

class NOTA_DEVICES_API MidiRack final : public dsp::MidiDevice, public RackCore {
  public:
    static constexpr int32_t kKind = 6;
    static constexpr int32_t kMaxChains = 16;
    static constexpr int32_t kMaxEvents = 1024;
    static constexpr int32_t kPoolEvents = 4096;

    MidiRack();
    ~MidiRack() override;

    int32_t midiKind() const override { return kKind; }
    const RackCore* asRack() const override { return this; }
    const dsp::capabilities::IRack* asRackCapability() const override { return this; }
    const char* displayName() const override { return "Nota MIDI Effect Rack"; }
    void setSampleRate(double sr, int32_t maxBlock) override { setRates(sr, maxBlock); }

    void process(const dsp::MidiEv* in, int nIn, dsp::MidiEv* out, int& nOut, int maxOut, int32_t frames,
        double beatStart, double spb, bool playing) override;
    void reset() override;
    void timeJump(double deltaBeats) override;
    bool isIdle() const override;

    int32_t paramCount() const override { return kNumMacros; }
    const char* paramName(int32_t index) const override;
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t index) const override { return macroGet(index); }
    void setParam(int32_t index, float value) override { macroSet(index, value); }

    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<dsp::MidiDevice> clone() const override;

    void shareRuntimeWith(const MidiRack& o) { rt_ = o.rt_; }
    float chainActivity(int32_t c) const { return chainMeter(c); }

  private:
    struct Runtime {
        uint16_t route[128] = {};
        bool thru[128] = {};
        int8_t outPitch[kMaxChains][128];
        uint8_t held[128] = {};
        uint8_t rawHeld[128] = {};
        bool bypassed = false;
        uint64_t structure = 0;
        Runtime() { clearOut(); }
        void clearOut() {
            for (auto& row : outPitch)
                for (auto& p : row) p = -1;
        }
    };
    struct Tagged {
        dsp::MidiEv ev;
        int16_t chain;
        int32_t seq;
    };

    uint64_t structureOf(const RackState& st) const;
    void cutAll(const RackState& st, dsp::MidiEv* out, int& nOut, int maxOut);
    void emitMerged(const dsp::MidiEv& ev, dsp::MidiEv* out, int& nOut, int maxOut);

    std::shared_ptr<Runtime> rt_ = std::make_shared<Runtime>();
    dsp::MidiEv bufA_[kMaxEvents];
    dsp::MidiEv bufB_[kMaxEvents];
    Tagged pool_[kPoolEvents];
    uint16_t evMask_[kMaxEvents] = {};
    uint16_t order_[kMaxEvents] = {};
    std::array<char[24], kNumMacros> paramNames_{};
};

} // namespace nota::devices
