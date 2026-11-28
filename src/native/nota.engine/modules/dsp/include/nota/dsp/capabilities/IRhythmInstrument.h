// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IRhythmInstrument — capability interface for multi-voice drum/sample machines (RhythmMachine).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>
#include <memory>
#include <string>

namespace nota::dsp {
class Device;
} // namespace nota::dsp

namespace nota::dsp::capabilities {

struct RhythmMacroMap {
    int32_t macro = 0;
    int32_t voice = 0;
    int32_t device = -1;
    int32_t param = 0;
    float lo = 0.0f;
    float hi = 1.0f;
    int32_t curve = 0;
};

class NOTA_DSP_API IRhythmInstrument {
  public:
    virtual ~IRhythmInstrument();

    virtual int32_t rhythmVoiceCount() const = 0;
    virtual void setVoiceSample(int32_t voice, std::shared_ptr<media::SampleBuffer> buf) = 0;
    virtual int32_t voiceSource(int32_t voice) const = 0;
    virtual int64_t voiceSampleId(int32_t voice) const = 0;
    virtual std::shared_ptr<media::SampleBuffer> voiceSampleBuf(int32_t voice) const = 0;

    virtual int32_t voiceDeviceCount(int32_t voice) const = 0;
    virtual Device* voiceDevice(int32_t voice, int32_t dev) const = 0;
    virtual int32_t addVoiceDevice(int32_t voice, int32_t kind) = 0;
    virtual bool removeVoiceDevice(int32_t voice, int32_t dev) = 0;
    virtual bool moveVoiceDevice(int32_t voice, int32_t from, int32_t to) = 0;

    virtual const std::string& kitName() const = 0;
    virtual void setKitName(const std::string& name) = 0;

    virtual std::string macroName(int32_t macro) const = 0;
    virtual void setMacroName(int32_t macro, const std::string& name) = 0;
    virtual int32_t addMacroMapping(
        int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi) = 0;
    virtual int32_t macroMappingCount() const = 0;
    virtual bool macroMapping(int32_t index, RhythmMacroMap& out) const = 0;
    virtual bool removeMacroMapping(int32_t index) = 0;
    virtual bool setMacroMappingRange(int32_t index, float lo, float hi) = 0;
    virtual bool setMacroMappingCurve(int32_t index, int32_t curve) = 0;
    virtual void clearMacros() = 0;
};

} // namespace nota::dsp::capabilities
