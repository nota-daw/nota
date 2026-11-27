// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackOps — chain key/select/transpose/vel controls, Drum Rack shaping,
// Audio-Effect Rack routing/stage controls, and macros/mappings.

#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/RackOps.h"
#include "nota/devices/racks/RackCore.h"

#include <cstdint>
#include <string>

namespace nota::detail {

// ---- chain select range / transpose / velocity scale, rack merge / pass-through (v9) ----
void RackOps::chainSelectRange(int32_t t, int32_t di, int32_t c, int32_t& lo, int32_t& hi) const {
    lo = 0;
    hi = 127;
    if (auto* r = coreAt(t, di)) r->chainSelectRange(c, lo, hi);
}
void RackOps::setChainSelectRange(int32_t t, int32_t di, int32_t c, int32_t lo, int32_t hi) {
    if (auto* r = coreAt(t, di)) r->setChainSelectRange(c, lo, hi);
}
int32_t RackOps::chainTranspose(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainTranspose(c) : 0;
}
void RackOps::setChainTranspose(int32_t t, int32_t di, int32_t c, int32_t st) {
    if (auto* r = coreAt(t, di)) r->setChainTranspose(c, st);
}
float RackOps::chainVelScale(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainVelScale(c) : 1.0f;
}
void RackOps::setChainVelScale(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = coreAt(t, di)) r->setChainVelScale(c, v);
}
bool RackOps::merge(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return !r || r->rackMerge();
}
void RackOps::setMerge(int32_t t, int32_t di, bool on) {
    if (auto* r = coreAt(t, di)) r->setRackMerge(on);
}
bool RackOps::thru(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return !r || r->rackThru();
}
void RackOps::setThru(int32_t t, int32_t di, bool on) {
    if (auto* r = coreAt(t, di)) r->setRackThru(on);
}

void RackOps::chainDeviceParamSet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (!dev) return;
    if (dev->isLatencyConfigurationParam(p)) {
        rt_.devices().setLatencyConfigurationParam(*dev, p, v);
        return;
    }
    dev->setParam(p, v);
    rt_.automation().routeNestedCvBaseEdit(t, di, c, d, p, v); // a grouped, CV-linked device (specs/044)
}
void RackOps::chainMidiParamSet(int32_t t, int32_t di, int32_t c, int32_t i, int32_t p, float v) {
    MidiDevice* m = chainMidiAt(t, di, c, i);
    if (!m) return;
    m->setParam(p, v);
    rt_.automation().routeNestedCvBaseEdit(t, di, c, RackCore::midiTarget(i), p, v);
}
void RackOps::setChainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d, bool b) {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->setBypassed(b);
}
bool RackOps::chainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev && dev->bypassed();
}
float RackOps::chainDeviceGainReduction(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->gainReductionDb() : 0.0f;
}
int32_t RackOps::chainDeviceScope(int32_t t, int32_t di, int32_t c, int32_t d, float* out, int32_t n) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->scopeRead(out, n) : 0;
}
int32_t RackOps::chainDeviceLayerWave(
    int32_t t, int32_t di, int32_t c, int32_t d, int32_t layer, float* out, int32_t n) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->layerWave(layer, out, n) : 0;
}
std::string RackOps::chainDeviceText(int32_t t, int32_t di, int32_t c, int32_t d, int32_t id) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->deviceText(id) : std::string{};
}
void RackOps::chainDeviceAction(int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, int32_t iarg, float farg) {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->deviceAction(id, iarg, farg);
}

// ---- per-chain controls --------------------------------------------------
void RackOps::setChainGain(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = coreAt(t, di)) r->setChainGain(c, v);
}
void RackOps::setChainPan(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = coreAt(t, di)) r->setChainPan(c, v);
}
void RackOps::setChainMute(int32_t t, int32_t di, int32_t c, bool b) {
    if (auto* r = coreAt(t, di)) r->setChainMute(c, b);
}
void RackOps::setChainSolo(int32_t t, int32_t di, int32_t c, bool b) {
    if (auto* r = coreAt(t, di)) r->setChainSolo(c, b);
}
float RackOps::chainGain(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainGain(c) : 1.0f;
}
float RackOps::chainPan(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainPan(c) : 0.0f;
}
bool RackOps::chainMute(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r && r->chainMute(c);
}
bool RackOps::chainSolo(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r && r->chainSolo(c);
}

// ---- Drum Rack per-pad shaping + kit-level swing/humanize -----------------
void RackOps::setChainChoke(int32_t t, int32_t di, int32_t c, int32_t g) {
    if (auto* r = coreAt(t, di)) r->setChainChoke(c, g);
}
int32_t RackOps::chainChoke(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainChoke(c) : 0;
}
void RackOps::setChainTune(int32_t t, int32_t di, int32_t c, int32_t st) {
    if (auto* r = coreAt(t, di)) r->setChainTune(c, st);
}
int32_t RackOps::chainTune(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainTune(c) : 0;
}
void RackOps::setChainDecay(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = coreAt(t, di)) r->setChainDecay(c, v);
}
float RackOps::chainDecay(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainDecay(c) : 1.0f;
}
void RackOps::setSwing(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setSwing(v);
}
float RackOps::swing(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->swing() : 0.0f;
}
void RackOps::setHumanize(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setHumanize(v);
}
float RackOps::humanize(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->humanize() : 0.0f;
}

// ---- Audio Effect Rack routing + output stage ----------------------------
void RackOps::setMode(int32_t t, int32_t di, int32_t m) {
    if (auto* r = coreAt(t, di)) r->setRackMode(m);
}
int32_t RackOps::mode(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->rackMode() : 0;
}
void RackOps::setDryWet(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setRackDryWet(v);
}
float RackOps::dryWet(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->rackDryWet() : 1.0f;
}
void RackOps::setPdc(int32_t t, int32_t di, bool b) {
    if (auto* r = coreAt(t, di)) r->setRackPdc(b);
}
bool RackOps::pdc(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r && r->rackPdc();
}
void RackOps::setChainSelect(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setChainSelect(v);
}
float RackOps::chainSelect(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->chainSelect() : 0.5f;
}
void RackOps::setSelFollow(int32_t t, int32_t di, bool b) {
    if (auto* r = coreAt(t, di)) r->setSelFollow(b);
}
bool RackOps::selFollow(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r && r->selFollow();
}
float RackOps::liveSelector(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->liveSelector() : 0.5f;
}

// ---- macros + mappings ---------------------------------------------------
float RackOps::macroGet(int32_t t, int32_t di, int32_t m) const {
    auto* r = coreAt(t, di);
    return r ? r->macroGet(m) : 0.0f;
}
void RackOps::macroSet(int32_t t, int32_t di, int32_t m, float v) {
    if (auto* r = coreAt(t, di)) r->macroSet(m, v);
}
int32_t RackOps::addMacroMapping(int32_t t, int32_t di, int32_t macro, int32_t chain, int32_t targetDevice,
    int32_t paramIndex, float rangeMin, float rangeMax) {
    auto* r = coreAt(t, di);
    if (!r || macro < 0 || macro >= RackCore::kNumMacros) return -1;
    if (targetDevice != RackCore::kRackControl && (chain < 0 || chain >= r->chainCount())) return -1;
    return forEdit(t, di)->addMacroMapping(macro, chain, targetDevice, paramIndex, rangeMin, rangeMax);
}
int32_t RackOps::mappingCount(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->mappingCount() : 0;
}
bool RackOps::mappingInfo(int32_t t, int32_t di, int32_t index, int32_t& macro, int32_t& chain, int32_t& targetDevice,
    int32_t& paramIndex, float& rangeMin, float& rangeMax) const {
    auto* r = coreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(index, m)) return false;
    macro = m.macro;
    chain = m.chainIndex;
    targetDevice = m.deviceIndex;
    paramIndex = m.paramIndex;
    rangeMin = m.rangeMin;
    rangeMax = m.rangeMax;
    return true;
}
bool RackOps::removeMapping(int32_t t, int32_t di, int32_t index) {
    auto* r = coreAt(t, di);
    if (!r || index < 0 || index >= r->mappingCount()) return false;
    return forEdit(t, di)->removeMapping(index);
}

// ---- Instrument-Rack extras (zones / meter / names / rack out / macro-map) ----
void RackOps::chainZone(
    int32_t t, int32_t di, int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    auto* r = coreAt(t, di);
    if (r) r->chainZone(c, keyLo, keyHi, velLo, velHi);
    else {
        keyLo = 0;
        keyHi = 127;
        velLo = 0;
        velHi = 127;
    }
}
void RackOps::setChainZone(
    int32_t t, int32_t di, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return;
    int32_t kl, kh, vl, vh;
    r->chainZone(c, kl, kh, vl, vh);
    auto cl = [](int32_t v) { return std::clamp(v, 0, 127); };
    if (kl == cl(keyLo) && kh == cl(keyHi) && vl == cl(velLo) && vh == cl(velHi)) return;
    forEdit(t, di)->setChainZone(c, keyLo, keyHi, velLo, velHi);
}
float RackOps::chainMeter(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainMeter(c) : 0.0f;
}
std::string RackOps::macroName(int32_t t, int32_t di, int32_t m) const {
    auto* r = coreAt(t, di);
    return r ? r->macroName(m) : std::string{};
}
void RackOps::setMacroName(int32_t t, int32_t di, int32_t m, const std::string& name) {
    auto* r = coreAt(t, di);
    if (!r || m < 0 || m >= RackCore::kNumMacros || r->macroName(m) == name) return;
    forEdit(t, di)->setMacroName(m, name); // undoable (specs/040)
}
float RackOps::volume(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->rackVolume() : 1.0f;
}
void RackOps::setVolume(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setRackVolume(v);
}
float RackOps::glide(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->rackGlide() : 0.0f;
}
void RackOps::setGlide(int32_t t, int32_t di, float v) {
    if (auto* r = coreAt(t, di)) r->setRackGlide(v);
}
// Range and curve edits fork the rack like any other rack edit, so they undo (specs/040);
// a range drag brackets itself in an undo group so the whole gesture is one step.
bool RackOps::setMappingRange(int32_t t, int32_t di, int32_t i, float lo, float hi) {
    auto* r = coreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(i, m)) return false;
    if (m.rangeMin == lo && m.rangeMax == hi) return true;
    return forEdit(t, di)->setMappingRange(i, lo, hi);
}
int32_t RackOps::mappingCurve(int32_t t, int32_t di, int32_t i) const {
    auto* r = coreAt(t, di);
    return r ? r->mappingCurve(i) : 0;
}
bool RackOps::setMappingCurve(int32_t t, int32_t di, int32_t i, int32_t curve) {
    auto* r = coreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(i, m)) return false;
    if (m.curve == std::clamp(curve, 0, 3)) return true;
    return forEdit(t, di)->setMappingCurve(i, curve);
}

} // namespace nota::detail
