// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiDevice — MIDI effect interface transforming note event streams pre-instrument.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/MidiTypes.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::devices {
class RackCore;
} // namespace nota::devices

namespace nota::dsp {

namespace capabilities {
class IRack;
} // namespace capabilities

NOTA_DSP_API uint64_t nextMidiDeviceLineage() noexcept;

class NOTA_DSP_API MidiDevice {
  public:
    virtual const nota::devices::RackCore* asRack() const { return nullptr; }
    virtual const capabilities::IRack* asRackCapability() const { return nullptr; }
    virtual ~MidiDevice();

    uint64_t lineage() const noexcept { return lineage_; }
    void inheritLineage(uint64_t id) noexcept { lineage_ = id; }

    virtual void setSampleRate(double /*sr*/, int32_t /*maxBlock*/) {}

    virtual void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames,
        double beatStart, double spb, bool playing) = 0;

    virtual void reset() {}
    virtual void timeJump(double /*deltaBeats*/) {}
    virtual bool isIdle() const { return true; }

    virtual int32_t midiKind() const { return -1; }
    virtual const char* displayName() const { return "MidiDevice"; }

    virtual int32_t paramCount() const { return 0; }
    virtual const char* paramName(int32_t /*index*/) const { return ""; }
    virtual float paramMin(int32_t /*index*/) const { return 0.0f; }
    virtual float paramMax(int32_t /*index*/) const { return 1.0f; }
    virtual float getParam(int32_t /*index*/) const { return 0.0f; }
    virtual void setParam(int32_t /*index*/, float /*value*/) {}

    virtual int32_t midiLastIn() const { return -1; }
    virtual int32_t midiLastOut() const { return -1; }
    virtual int32_t midiScope(float* /*out*/, int32_t /*maxN*/) const { return 0; }
    virtual void midiCommand(int32_t /*cmd*/) {}

    virtual std::shared_ptr<MidiDevice> clone() const { return nullptr; }

    virtual std::vector<uint8_t> getState() const { return {}; }
    virtual void setState(const uint8_t* /*data*/, int32_t /*size*/) {}

    bool bypassed() const noexcept { return bypassed_.load(std::memory_order_relaxed); }
    void setBypassed(bool b) noexcept { bypassed_.store(b, std::memory_order_relaxed); }

    bool folded() const noexcept { return folded_; }
    void setFolded(bool f) noexcept { folded_ = f; }

    int32_t presetTag() const noexcept { return presetTag_; }
    void setPresetTag(int32_t t) noexcept { presetTag_ = t; }

    virtual int32_t ccDestDevice() const { return -2; }
    virtual int32_t ccDestParam() const { return -1; }
    virtual float ccDepth() const { return 0.0f; }
    virtual float ccValue() const { return 0.0f; }
    virtual void setCcDest(int32_t /*destDevice*/, int32_t /*destParam*/) {}
    virtual uint64_t ccDestChainUid() const { return 0; }
    virtual int32_t ccDestSlot() const { return -1; }
    virtual void setCcDestNested(uint64_t /*chainUid*/, int32_t /*slot*/) {}
    virtual void setCcDepth(float /*depth*/) {}

  private:
    std::atomic<bool> bypassed_{false};
    bool folded_ = false;
    int32_t presetTag_ = 0;
    uint64_t lineage_ = nextMidiDeviceLineage();
};

} // namespace nota::dsp
