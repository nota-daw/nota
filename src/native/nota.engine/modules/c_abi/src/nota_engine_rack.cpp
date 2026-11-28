// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — Instrument Rack and Drum Rack (device_index = -1).

#include "nota/nota_engine.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include <cstdint>
#include <string>

extern "C" {

// ===================== Instrument Rack (device_index = -1) ==================

int32_t nota_engine_add_instrument_rack_track(NotaEngine* e) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addInstrumentRack().id();
    return id > 0 ? id : 0;
}

int32_t nota_engine_add_drum_rack_track(NotaEngine* e) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addDrumRack().id();
    return id > 0 ? id : 0;
}

int32_t nota_rack_add_sampler_chain(NotaEngine* e, int32_t t, const char* path, int32_t root, int32_t loop) {
    return (e && path)
               ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).addSamplerChain(std::string(path), root, loop != 0)
               : -1;
}

int32_t nota_rack_set_chain_sampler_sample(NotaEngine* e, int32_t t, int32_t chain, const char* path, int32_t root) {
    return (e && path &&
               nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1)
                   .chain(chain)
                   .setSamplerSample(std::string(path), root))
               ? 1
               : 0;
}

int32_t nota_rack_add_plugin_instrument_chain(NotaEngine* e, int32_t t, int32_t catalogIndex) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).addPluginInstrumentChain(catalogIndex) : -1;
}

int32_t nota_rack_add_plugin_chain_device(NotaEngine* e, int32_t t, int32_t chain, int32_t catalogIndex) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).addPluginDevice(catalogIndex) : -1;
}

int32_t nota_rack_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).triggerNote() : -1;
}

void nota_rack_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t c, int32_t note) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setTriggerNote(note);
}

const char* nota_rack_chain_name(const NotaEngine* e, int32_t t, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).name() : std::string{});
}

void nota_rack_set_chain_name(NotaEngine* e, int32_t t, int32_t c, const char* name) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setName(name ? name : "");
}

int32_t nota_rack_chain_count(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chainCount() : 0;
}

int32_t nota_rack_add_chain(NotaEngine* e, int32_t t, int32_t k) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).addChain(k) : -1;
}

int32_t nota_rack_remove_chain(NotaEngine* e, int32_t t, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).removeChain(c)) ? 1 : 0;
}

int32_t nota_rack_set_chain_instrument(NotaEngine* e, int32_t t, int32_t c, int32_t k) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setInstrument(k)) ? 1 : 0;
}

int32_t nota_rack_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t c) {
    if (!e) return -2;
    const auto chain = nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c);
    return chain.isValid() ? chain.instrumentKind() : -2;
}

const char* nota_rack_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentName() : std::string{});
}

int32_t nota_rack_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentParamCount() : 0;
}

const char* nota_rack_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentParamName(p) : std::string{});
}

const char* nota_rack_chain_instrument_param_id(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentParamId(p) : std::string{});
}

float nota_rack_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentParamGet(p) : 0.0f;
}

void nota_rack_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t p, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).instrumentParamSet(p, v);
}

float nota_rack_chain_instrument_param_default(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentParamDefault(p) : 0.0f;
}

int32_t nota_rack_chain_instrument_scope(const NotaEngine* e, int32_t t, int32_t c, float* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentScope(out, n) : 0;
}

int32_t nota_rack_chain_instrument_voice_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentVoiceCount() : -1;
}

int32_t nota_rack_chain_instrument_held_notes(const NotaEngine* e, int32_t t, int32_t c, int32_t* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentHeldNotes(out, n) : 0;
}

void nota_rack_chain_instrument_action(NotaEngine* e, int32_t t, int32_t c, int32_t id, int32_t iarg, float farg) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).instrumentAction(id, iarg, farg);
}

int32_t nota_rack_chain_instrument_record_state_undo(
    NotaEngine* e, int32_t t, int32_t c, const uint8_t* before, int32_t size) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::eng(e), t, -1).recordInstrumentState(c, before, size)) ? 1 : 0;
}

int32_t nota_rack_chain_instrument_set_state_undoable(
    NotaEngine* e, int32_t t, int32_t c, const uint8_t* data, int32_t size) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::eng(e), t, -1).setInstrumentStateUndoable(c, data, size)) ? 1
                                                                                                                  : 0;
}

void nota_rack_open_chain_instrument_editor(NotaEngine* e, int32_t t, int32_t c) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).openInstrumentEditor();
}

void nota_rack_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).openDeviceEditor(d);
}

const char* nota_rack_chain_instrument_plugin_id(const NotaEngine* e, int32_t t, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentPluginId() : std::string{});
}

int32_t nota_rack_chain_instrument_get_state(const NotaEngine* e, int32_t t, int32_t c, uint8_t* out, int32_t cap) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).instrumentGetState(out, cap) : 0;
}

int32_t nota_rack_chain_sampler_info(const NotaEngine* e, int32_t t, int32_t c, NotaSamplerInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SamplerInfo info{};
    if (!nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).samplerInfo(info)) return 0;
    *out = {info.sampleId, info.rootNote, info.loop};
    return 1;
}

int32_t nota_rack_set_chain_sampler_root(NotaEngine* e, int32_t t, int32_t c, int32_t root) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setSamplerRoot(root)) ? 1 : 0;
}

float nota_rack_chain_sampler_play_position(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).samplerPlayPosition() : -1.0f;
}

int32_t nota_rack_chain_device_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceCount() : 0;
}

int32_t nota_rack_add_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t k) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).addDevice(k) : -1;
}

int32_t nota_rack_remove_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).removeDevice(d)) ? 1 : 0;
}

int32_t nota_rack_move_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t from, int32_t to) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).moveDevice(from, to)) ? 1 : 0;
}

const char* nota_rack_chain_device_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceName(d) : std::string{});
}

int32_t nota_rack_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceKind(d) : -1;
}

int32_t nota_rack_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceParamCount(d) : 0;
}

const char* nota_rack_chain_device_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceParamName(d, p) : std::string{});
}

float nota_rack_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceParamMin(d, p) : 0.0f;
}

float nota_rack_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceParamMax(d, p) : 1.0f;
}

float nota_rack_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceParamGet(d, p) : 0.0f;
}

void nota_rack_chain_device_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).deviceParamSet(d, p, v);
}

void nota_rack_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setDeviceBypassed(d, b != 0);
}

int32_t nota_rack_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).deviceBypassed(d)) ? 1 : 0;
}

void nota_rack_set_chain_gain(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setVolume(v);
}

void nota_rack_set_chain_pan(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setPan(v);
}

void nota_rack_set_chain_mute(NotaEngine* e, int32_t t, int32_t c, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setMute(b != 0);
}

void nota_rack_set_chain_solo(NotaEngine* e, int32_t t, int32_t c, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setSolo(b != 0);
}

float nota_rack_chain_gain(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).volume() : 1.0f;
}

float nota_rack_chain_pan(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).pan() : 0.0f;
}

int32_t nota_rack_chain_mute(const NotaEngine* e, int32_t t, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).mute()) ? 1 : 0;
}

int32_t nota_rack_chain_solo(const NotaEngine* e, int32_t t, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).solo()) ? 1 : 0;
}

float nota_rack_macro_get(const NotaEngine* e, int32_t t, int32_t m) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).macro(m).get() : 0.0f;
}

void nota_rack_macro_set(NotaEngine* e, int32_t t, int32_t m, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).macro(m).set(v);
}

int32_t nota_rack_add_macro_mapping(
    NotaEngine* e, int32_t t, int32_t macro, int32_t chain, int32_t di, int32_t pi, float lo, float hi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).addMacroMapping(macro, chain, di, pi, lo, hi) : -1;
}

int32_t nota_rack_duplicate_chain(NotaEngine* e, int32_t t, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).duplicateChain(chain) : -1;
}

void nota_rack_set_mono(NotaEngine* e, int32_t t, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setMono(on != 0);
}

int32_t nota_rack_mono(const NotaEngine* e, int32_t t) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).mono()) ? 1 : 0;
}

int32_t nota_rack_mapping_count(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).mappingCount() : 0;
}

int32_t nota_rack_mapping_info(const NotaEngine* e, int32_t t, int32_t index, int32_t* macro, int32_t* chain,
    int32_t* di, int32_t* pi, float* lo, float* hi) {
    return e ? nota::c_abi::rackMappingInfoImpl(nota::c_abi::ceng(e), t, -1, index, macro, chain, di, pi, lo, hi) : 0;
}

int32_t nota_rack_remove_mapping(NotaEngine* e, int32_t t, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).removeMapping(index)) ? 1 : 0;
}

void nota_rack_chain_zone(
    const NotaEngine* e, int32_t t, int32_t c, int32_t* keyLo, int32_t* keyHi, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).zone(kl, kh, vl, vh);
    if (keyLo) *keyLo = kl;
    if (keyHi) *keyHi = kh;
    if (velLo) *velLo = vl;
    if (velHi) *velHi = vh;
}

void nota_rack_set_chain_zone(
    NotaEngine* e, int32_t t, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setZone(keyLo, keyHi, velLo, velHi);
}

float nota_rack_chain_meter(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).meter() : 0.0f;
}

const char* nota_rack_macro_name(const NotaEngine* e, int32_t t, int32_t m) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).macro(m).name() : std::string{});
}

void nota_rack_set_macro_name(NotaEngine* e, int32_t t, int32_t m, const char* name) {
    if (e)
        nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).macro(m).setName(name ? std::string(name) : std::string{});
}

float nota_rack_volume(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).volume() : 1.0f;
}

void nota_rack_set_volume(NotaEngine* e, int32_t t, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setVolume(v);
}

float nota_rack_glide(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).glide() : 0.0f;
}

void nota_rack_set_glide(NotaEngine* e, int32_t t, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setGlide(v);
}

int32_t nota_rack_set_mapping_range(NotaEngine* e, int32_t t, int32_t i, float lo, float hi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setMappingRange(i, lo, hi)) ? 1 : 0;
}

int32_t nota_rack_mapping_curve(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).mappingCurve(i) : 0;
}

int32_t nota_rack_set_mapping_curve(NotaEngine* e, int32_t t, int32_t i, int32_t curve) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setMappingCurve(i, curve)) ? 1 : 0;
}

void nota_rack_set_chain_choke(NotaEngine* e, int32_t t, int32_t c, int32_t g) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setChoke(g);
}

int32_t nota_rack_chain_choke(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).choke() : 0;
}

void nota_rack_set_chain_tune(NotaEngine* e, int32_t t, int32_t c, int32_t st) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setTune(st);
}

int32_t nota_rack_chain_tune(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).tune() : 0;
}

void nota_rack_set_chain_decay(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(c).setDecay(v);
}

float nota_rack_chain_decay(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(c).decay() : 1.0f;
}

void nota_rack_set_swing(NotaEngine* e, int32_t t, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setSwing(v);
}

float nota_rack_swing(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).swing() : 0.0f;
}

void nota_rack_set_humanize(NotaEngine* e, int32_t t, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).setHumanize(v);
}

float nota_rack_humanize(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).humanize() : 0.0f;
}

} // extern "C"
