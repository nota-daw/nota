// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EngineRuntime — tracks, mixer strips, send/return buses, arrangement clips (geometry + move/trim/split/duplicate/delete), MIDI clips & notes, and audio-clip/sample persistence.

#include "core/EnginePrelude.h"
#include "clips/ClipOps.h"
#include "tracks/TrackOps.h"
#include "core/EngineRuntime.h"
#include "model/Track.h"
#include "nota/devices/DeviceIds.h"
#include "nota/dsp/capabilities/IRhythmInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"

#include <cstdint>
#include <cstring>
#include <memory>
#include <string>

namespace nota::detail {

bool TrackOps::sampleInfo(int64_t sampleId, SampleInfo* out) const {
    if (!out) return false;
    auto s = rt_.clips().findSampleById(sampleId);
    if (!s) return false;
    out->channels = s->channels;
    out->frames = s->frames;
    out->sampleRate = s->sourceSampleRate;
    return true;
}

int64_t TrackOps::sampleRead(int64_t sampleId, float* out, int64_t cap) const {
    auto s = rt_.clips().findSampleById(sampleId);
    if (!s) return 0;
    return s->readAllInterleaved(out, cap);
}

bool TrackOps::samplerInfo(int32_t trackId, SamplerInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler))
        return false;
    auto* smp = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    if (!smp) return false;
    out->sampleId = smp->sample() ? smp->sample()->id : 0;
    out->rootNote = smp->rootNote();
    out->loop = smp->loopEnabled() ? 1 : 0;
    return true;
}

bool TrackOps::grainInfo(int32_t trackId, SamplerInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::GrainSynth))
        return false;
    auto* gr = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    if (!gr) return false;
    out->sampleId = gr->sample() ? gr->sample()->id : 0;
    out->rootNote = gr->rootNote();
    out->loop = 0;
    return true;
}

// Nota Rhythm Phase 2: per-voice sample id + source (0 Synth, 1 Sample).
bool TrackOps::rhythmVoiceInfo(int32_t trackId, int32_t voice, SamplerInfo* out) const {
    if (!out) return false;
    auto t = rt_.findTrackAuthoring(trackId);
    auto* rh = t ? dynamic_cast<const dsp::capabilities::IRhythmInstrument*>(t->instrument.get()) : nullptr;
    if (!rh) return false;
    out->sampleId = rh->voiceSampleId(voice);
    out->rootNote = 60;
    out->loop = 0;
    return true;
}
int32_t TrackOps::rhythmVoiceSource(int32_t trackId, int32_t voice) const {
    auto t = rt_.findTrackAuthoring(trackId);
    auto* rh = t ? dynamic_cast<const dsp::capabilities::IRhythmInstrument*>(t->instrument.get()) : nullptr;
    return rh ? rh->voiceSource(voice) : 0;
}

// Nota Rhythm voice FX. The machine swaps its chains as snapshots itself (like a rack), so
// these edit the live instrument in place — a clone would drop the running voices.
static dsp::capabilities::IRhythmInstrument* rhythmOf(const std::shared_ptr<Track>& t) {
    return t ? dynamic_cast<dsp::capabilities::IRhythmInstrument*>(t->instrument.get()) : nullptr;
}
int32_t TrackOps::rhythmVoiceDeviceCount(int32_t trackId, int32_t voice) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->voiceDeviceCount(voice) : 0;
}
int32_t TrackOps::rhythmAddVoiceDevice(int32_t trackId, int32_t voice, int32_t kind) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->addVoiceDevice(voice, kind) : -1;
}
bool TrackOps::rhythmRemoveVoiceDevice(int32_t trackId, int32_t voice, int32_t dev) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh && rh->removeVoiceDevice(voice, dev);
}
bool TrackOps::rhythmMoveVoiceDevice(int32_t trackId, int32_t voice, int32_t from, int32_t to) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh && rh->moveVoiceDevice(voice, from, to);
}
Device* TrackOps::rhythmVoiceDevice(int32_t trackId, int32_t voice, int32_t dev) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->voiceDevice(voice, dev) : nullptr;
}

void TrackOps::rhythmVoiceDeviceParamSet(int32_t trackId, int32_t voice, int32_t dev, int32_t param, float value) {
    if (auto* device = rhythmVoiceDevice(trackId, voice, dev)) {
        if (device->isLatencyConfigurationParam(param))
            rt_.devices().setLatencyConfigurationParam(*device, param, value);
        else
            device->setParam(param, value);
    }
}
std::string TrackOps::rhythmKitName(int32_t trackId) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->kitName() : std::string{};
}
void TrackOps::setRhythmKitName(int32_t trackId, const std::string& name) {
    if (auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId))) rh->setKitName(name);
}
std::string TrackOps::rhythmMacroName(int32_t trackId, int32_t macro) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->macroName(macro) : std::string{};
}
void TrackOps::setRhythmMacroName(int32_t trackId, int32_t macro, const std::string& name) {
    if (auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId))) rh->setMacroName(macro, name);
}
int32_t TrackOps::rhythmAddMacroMapping(
    int32_t trackId, int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->addMacroMapping(macro, voice, device, param, lo, hi) : -1;
}
int32_t TrackOps::rhythmMacroMappingCount(int32_t trackId) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh ? rh->macroMappingCount() : 0;
}
bool TrackOps::rhythmMacroMappingInfo(int32_t trackId, int32_t index, int32_t& macro, int32_t& voice, int32_t& device,
    int32_t& param, float& lo, float& hi, int32_t& curve) const {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    dsp::capabilities::RhythmMacroMap m;
    if (!rh || !rh->macroMapping(index, m)) return false;
    macro = m.macro;
    voice = m.voice;
    device = m.device;
    param = m.param;
    lo = m.lo;
    hi = m.hi;
    curve = m.curve;
    return true;
}
bool TrackOps::rhythmRemoveMacroMapping(int32_t trackId, int32_t index) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh && rh->removeMacroMapping(index);
}
bool TrackOps::rhythmSetMacroMappingRange(int32_t trackId, int32_t index, float lo, float hi) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh && rh->setMacroMappingRange(index, lo, hi);
}
bool TrackOps::rhythmSetMacroMappingCurve(int32_t trackId, int32_t index, int32_t curve) {
    auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId));
    return rh && rh->setMacroMappingCurve(index, curve);
}
void TrackOps::rhythmClearMacros(int32_t trackId) {
    if (auto* rh = rhythmOf(rt_.findTrackAuthoring(trackId))) rh->clearMacros();
}

int32_t TrackOps::grainPlayPositions(int32_t trackId, float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::GrainSynth))
        return 0;
    auto* gr = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    return gr ? gr->playPositions(out, maxN) : 0;
}

int32_t TrackOps::instrumentVoiceCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->activeVoiceCount() : -1;
}

int32_t TrackOps::instrumentHeldNotes(int32_t trackId, int32_t* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->heldNotes(out, maxN) : 0;
}

int32_t TrackOps::instrumentScope(int32_t trackId, float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->scopeRead(out, maxN) : 0;
}

int32_t TrackOps::instrumentOutput(int32_t trackId, float* out, int32_t maxN) const {
    if (!out || maxN <= 0) return 0;
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->readOutput(out, maxN) : 0;
}

// --- structural edits: audio (M1) ------------------------------------------

} // namespace nota::detail
