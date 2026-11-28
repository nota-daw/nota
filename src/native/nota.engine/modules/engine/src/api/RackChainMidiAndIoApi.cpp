// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "devices/RackOps.h"

#include "nota/dsp/MidiDevice.h"
#include "nota/engine/Engine.h"
#include "nota/engine/RackChain.h"

#include <cstdint>
#include <memory>
#include <string>

namespace nota {

// ---- RackChain: MIDI effects, clipboard, CC & sidechain ------------------

int32_t Engine::RackChain::addMidiEffect(int32_t kind) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().addChainMidi(trackId_, di, ci, kind) : -1;
}
bool Engine::RackChain::removeMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().removeChainMidi(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::moveMidiEffect(int32_t from, int32_t to) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().moveChainMidi(trackId_, di, ci, from, to) : false;
}
int32_t Engine::RackChain::midiCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiCount(trackId_, di, ci) : 0;
}
int32_t Engine::RackChain::midiKind(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiKind() : -1;
}
std::string Engine::RackChain::midiName(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && m->displayName()) ? std::string(m->displayName()) : std::string{};
}
int32_t Engine::RackChain::midiParamCount(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->paramCount() : 0;
}
std::string Engine::RackChain::midiParamName(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    const char* n = (m && param >= 0 && param < m->paramCount()) ? m->paramName(param) : nullptr;
    return n ? std::string(n) : std::string{};
}
float Engine::RackChain::midiParamMin(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMin(param) : 0.0f;
}
float Engine::RackChain::midiParamMax(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMax(param) : 1.0f;
}
float Engine::RackChain::midiParamGet(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->getParam(param) : 0.0f;
}
int32_t Engine::RackChain::midiLastIn(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiLastIn() : -1;
}
int32_t Engine::RackChain::midiLastOut(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiLastOut() : -1;
}
int32_t Engine::RackChain::midiScope(int32_t slot, float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && out) ? m->midiScope(out, maxSamples) : 0;
}
void Engine::RackChain::midiCommand(int32_t slot, int32_t cmd) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr)
        m->midiCommand(cmd);
}
void Engine::RackChain::midiParamSet(int32_t slot, int32_t param, float value) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainMidiParamSet(trackId_, di, ci, slot, param, value);
}
void Engine::RackChain::setMidiBypassed(int32_t slot, bool bypassed) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainMidiBypassed(trackId_, di, ci, slot, bypassed);
}
bool Engine::RackChain::midiBypassed(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->bypassed() : false;
}
bool Engine::RackChain::copyDevice(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceCopy(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::copyMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiCopy(trackId_, di, ci, slot) : false;
}
int32_t Engine::RackChain::pasteDevice(int32_t at) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDevicePaste(trackId_, di, ci, at) : -1;
}
int32_t Engine::RackChain::pasteMidiEffect(int32_t at) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiPaste(trackId_, di, ci, at) : -1;
}
int32_t Engine::RackChain::pasteDevices(
    int32_t midiAt, int32_t fxAt, int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainPasteDevices(
                                    trackId_, di, ci, midiAt, fxAt, outSkipped, outFirstMidi, outFirstFx)
                              : -1;
}
int32_t Engine::RackChain::duplicateDevice(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceDuplicate(trackId_, di, ci, slot) : -1;
}
int32_t Engine::RackChain::duplicateMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiDuplicate(trackId_, di, ci, slot) : -1;
}
int32_t Engine::RackChain::moveDeviceTo(int32_t slot, int32_t toChain) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceMoveTo(trackId_, di, ci, slot, toChain) : -1;
}
int32_t Engine::RackChain::moveMidiTo(int32_t slot, int32_t toChain) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiMoveTo(trackId_, di, ci, slot, toChain) : -1;
}
bool Engine::RackChain::midiCcDest(
    int32_t slot, int32_t* dev, int32_t* destChain, int32_t* destSlot, int32_t* param, float* depth) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci)
               ? p->runtime.racks().chainMidiCcDest(trackId_, di, ci, slot, dev, destChain, destSlot, param, depth)
               : false;
}
bool Engine::RackChain::setMidiCcDest(
    int32_t slot, int32_t dev, int32_t destChain, int32_t destSlot, int32_t param, float depth) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci)
               ? p->runtime.racks().chainMidiSetCcDest(trackId_, di, ci, slot, dev, destChain, destSlot, param, depth)
               : false;
}
bool Engine::RackChain::acceptsSidechain(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainAcceptsSidechain(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::sidechain(int32_t slot, int32_t* src, int32_t* tapPre, float* gainDb, float* mix) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSidechain(trackId_, di, ci, slot, src, tapPre, gainDb, mix)
                              : false;
}
int32_t Engine::RackChain::setSidechain(int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetSidechain(trackId_, di, ci, slot, src, tapPre, gainDb, mix)
                              : -1;
}
bool Engine::RackChain::sidechainPart(int32_t slot, int32_t* part) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSidechainPart(trackId_, di, ci, slot, part) : false;
}
int32_t Engine::RackChain::setSidechainPart(int32_t slot, int32_t part) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetSidechainPart(trackId_, di, ci, slot, part) : -1;
}
int32_t Engine::RackChain::setKey(int32_t slot, int32_t src, int32_t part, int32_t tapPre, float gainDb, float mix) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetKey(trackId_, di, ci, slot, src, part, tapPre, gainDb, mix)
                              : -1;
}

} // namespace nota
