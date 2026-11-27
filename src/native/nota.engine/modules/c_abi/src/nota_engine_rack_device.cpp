// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — Audio Effect Rack, nested racks, and generic rack device surface.

#include "nota/nota_engine.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include <cstdint>
#include <string>

extern "C" {

// ===================== Audio Effect Rack & nested racks =====================

int32_t nota_rackdev_add_plugin_chain_device(
    NotaEngine* e, int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(chain).addPluginDevice(catalogIndex) : -1;
}

int32_t nota_rackdev_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).triggerNote() : -1;
}

void nota_rackdev_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t note) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setTriggerNote(note);
}

int32_t nota_rackdev_chain_instrument_record_state_undo(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, const uint8_t* before, int32_t size) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::eng(e), t, di).recordInstrumentState(c, before, size)) ? 1 : 0;
}

int32_t nota_rackdev_duplicate_chain(NotaEngine* e, int32_t t, int32_t di, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).duplicateChain(chain) : -1;
}

NotaResult nota_rackdev_chain_grain_set_sample(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, const char* path, int32_t root) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).grainSetSample(path, root)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int64_t nota_rackdev_chain_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).chainId() : 0;
}

int32_t nota_rackdev_checkpoint(NotaEngine* e, int32_t t, int32_t di) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).checkpoint()) ? 1 : 0;
}

int32_t nota_rackdev_chain_device_copy(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).copyDevice(i)) ? 1 : 0;
}

int32_t nota_rackdev_chain_midi_copy(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).copyMidiEffect(i)) ? 1 : 0;
}

int32_t nota_rackdev_chain_device_paste(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t at) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).pasteDevice(at) : -1;
}

int32_t nota_rackdev_chain_paste_devices(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t midi_at,
    int32_t fx_at, int32_t* out_skipped, int32_t* out_first_midi, int32_t* out_first_fx) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di)
                   .chain(c)
                   .pasteDevices(midi_at, fx_at, out_skipped, out_first_midi, out_first_fx)
             : -1;
}

int32_t nota_rackdev_chain_midi_paste(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t at) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).pasteMidiEffect(at) : -1;
}

int32_t nota_rackdev_chain_device_duplicate(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).duplicateDevice(i) : -1;
}

int32_t nota_rackdev_chain_midi_duplicate(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).duplicateMidiEffect(i) : -1;
}

int32_t nota_rackdev_chain_device_move_to(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).moveDeviceTo(i, to) : -1;
}

int32_t nota_rackdev_chain_midi_move_to(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).moveMidiTo(i, to) : -1;
}

int32_t nota_rackdev_chain_count(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chainCount() : 0;
}

int32_t nota_rackdev_add_chain(NotaEngine* e, int32_t t, int32_t di, int32_t k) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).addChain(k) : -1;
}

int32_t nota_rackdev_remove_chain(NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).removeChain(c)) ? 1 : 0;
}

int32_t nota_rackdev_set_chain_instrument(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setInstrument(k)) ? 1 : 0;
}

int32_t nota_rackdev_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    if (!e) return -2;
    const auto chain = nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c);
    return chain.isValid() ? chain.instrumentKind() : -2;
}

const char* nota_rackdev_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentName() : std::string{});
}

int32_t nota_rackdev_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentParamCount() : 0;
}

const char* nota_rackdev_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentParamName(p) : std::string{});
}

float nota_rackdev_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentParamGet(p) : 0.0f;
}

void nota_rackdev_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).instrumentParamSet(p, v);
}

int32_t nota_rackdev_chain_device_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceCount() : 0;
}

int32_t nota_rackdev_add_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).addDevice(k) : -1;
}

int32_t nota_rackdev_remove_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).removeDevice(d)) ? 1 : 0;
}

int32_t nota_rackdev_move_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).moveDevice(from, to)) ? 1 : 0;
}

const char* nota_rackdev_chain_device_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceName(d) : std::string{});
}

int32_t nota_rackdev_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceKind(d) : -1;
}

void nota_rackdev_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).openDeviceEditor(d);
}

int32_t nota_rackdev_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceParamCount(d) : 0;
}

const char* nota_rackdev_chain_device_param_name(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceParamName(d, p) : std::string{});
}

float nota_rackdev_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceParamMin(d, p) : 0.0f;
}

float nota_rackdev_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceParamMax(d, p) : 1.0f;
}

float nota_rackdev_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceParamGet(d, p) : 0.0f;
}

void nota_rackdev_chain_device_param_set(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).deviceParamSet(d, p, v);
}

void nota_rackdev_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setDeviceBypassed(d, b != 0);
}

int32_t nota_rackdev_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceBypassed(d)) ? 1 : 0;
}

float nota_rackdev_chain_device_gain_reduction(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceGainReduction(d) : 0.0f;
}

int32_t nota_rackdev_chain_device_scope(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, float* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceScope(d, out, n) : 0;
}

int32_t nota_rackdev_chain_device_layer_wave(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t layer, float* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceLayerWave(d, layer, out, n) : 0;
}

int32_t nota_rackdev_chain_device_text(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).deviceText(d, id), out, cap);
}

void nota_rackdev_chain_device_action(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, int32_t iarg, float farg) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).deviceAction(d, id, iarg, farg);
}

void nota_rackdev_set_chain_gain(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setVolume(v);
}

void nota_rackdev_set_chain_pan(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setPan(v);
}

void nota_rackdev_set_chain_mute(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setMute(b != 0);
}

void nota_rackdev_set_chain_solo(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setSolo(b != 0);
}

float nota_rackdev_chain_gain(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).volume() : 1.0f;
}

float nota_rackdev_chain_pan(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).pan() : 0.0f;
}

int32_t nota_rackdev_chain_mute(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).mute()) ? 1 : 0;
}

int32_t nota_rackdev_chain_solo(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).solo()) ? 1 : 0;
}

float nota_rackdev_macro_get(const NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).macro(m).get() : 0.0f;
}

void nota_rackdev_macro_set(NotaEngine* e, int32_t t, int32_t di, int32_t m, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).macro(m).set(v);
}

int32_t nota_rackdev_add_macro_mapping(
    NotaEngine* e, int32_t t, int32_t di, int32_t macro, int32_t chain, int32_t tdi, int32_t pi, float lo, float hi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).addMacroMapping(macro, chain, tdi, pi, lo, hi) : -1;
}

int32_t nota_rackdev_mapping_count(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).mappingCount() : 0;
}

int32_t nota_rackdev_mapping_info(const NotaEngine* e, int32_t t, int32_t di, int32_t index, int32_t* macro,
    int32_t* chain, int32_t* tdi, int32_t* pi, float* lo, float* hi) {
    return e ? nota::c_abi::rackMappingInfoImpl(nota::c_abi::ceng(e), t, di, index, macro, chain, tdi, pi, lo, hi) : 0;
}

int32_t nota_rackdev_remove_mapping(NotaEngine* e, int32_t t, int32_t di, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).removeMapping(index)) ? 1 : 0;
}

float nota_rackdev_chain_meter(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).meter() : 0.0f;
}

void nota_rackdev_chain_zone(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).zone(kl, kh, vl, vh);
    if (velLo) *velLo = vl;
    if (velHi) *velHi = vh;
}

void nota_rackdev_set_chain_zone(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t velLo, int32_t velHi) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setZone(0, 127, velLo, velHi);
}

const char* nota_rackdev_macro_name(const NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).macro(m).name() : std::string{});
}

void nota_rackdev_set_macro_name(NotaEngine* e, int32_t t, int32_t di, int32_t m, const char* name) {
    if (e)
        nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).macro(m).setName(name ? std::string(name) : std::string{});
}

int32_t nota_rackdev_set_mapping_range(NotaEngine* e, int32_t t, int32_t di, int32_t i, float lo, float hi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setMappingRange(i, lo, hi)) ? 1 : 0;
}

int32_t nota_rackdev_mapping_curve(const NotaEngine* e, int32_t t, int32_t di, int32_t i) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).mappingCurve(i) : 0;
}

int32_t nota_rackdev_set_mapping_curve(NotaEngine* e, int32_t t, int32_t di, int32_t i, int32_t curve) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setMappingCurve(i, curve)) ? 1 : 0;
}

float nota_rackdev_volume(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).volume() : 1.0f;
}

void nota_rackdev_set_volume(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setVolume(v);
}

int32_t nota_rackdev_mode(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).mode() : 0;
}

void nota_rackdev_set_mode(NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setMode(m);
}

float nota_rackdev_drywet(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).dryWet() : 1.0f;
}

void nota_rackdev_set_drywet(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setDryWet(v);
}

int32_t nota_rackdev_pdc(const NotaEngine* e, int32_t t, int32_t di) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).pdc()) ? 1 : 0;
}

void nota_rackdev_set_pdc(NotaEngine* e, int32_t t, int32_t di, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setPdc(on != 0);
}

float nota_rackdev_chain_select(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chainSelect() : 0.5f;
}

void nota_rackdev_set_chain_select(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setChainSelect(v);
}

int32_t nota_rackdev_sel_follow(const NotaEngine* e, int32_t t, int32_t di) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).selFollow()) ? 1 : 0;
}

void nota_rackdev_set_sel_follow(NotaEngine* e, int32_t t, int32_t di, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setSelFollow(on != 0);
}

float nota_rackdev_live_selector(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).liveSelector() : 0.5f;
}

// ---- Drum Rack / generic rack surface (di = -1, >=0, or <= -2000) ----------

int32_t nota_rackdev_add_sampler_chain(
    NotaEngine* e, int32_t t, int32_t di, const char* path, int32_t root, int32_t loop) {
    return (e && path)
               ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).addSamplerChain(std::string(path), root, loop != 0)
               : -1;
}

int32_t nota_rackdev_set_chain_sampler_sample(
    NotaEngine* e, int32_t t, int32_t di, int32_t chain, const char* path, int32_t root) {
    return (e && path &&
               nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di)
                   .chain(chain)
                   .setSamplerSample(std::string(path), root))
               ? 1
               : 0;
}

int32_t nota_rackdev_add_plugin_instrument_chain(NotaEngine* e, int32_t t, int32_t di, int32_t catalogIndex) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).addPluginInstrumentChain(catalogIndex) : -1;
}

const char* nota_rackdev_chain_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).name() : std::string{});
}

void nota_rackdev_set_chain_name(NotaEngine* e, int32_t t, int32_t di, int32_t c, const char* name) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setName(name ? name : "");
}

int32_t nota_rackdev_chain_choke(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).choke() : 0;
}

void nota_rackdev_set_chain_choke(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t g) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setChoke(g);
}

int32_t nota_rackdev_chain_tune(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).tune() : 0;
}

void nota_rackdev_set_chain_tune(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t st) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setTune(st);
}

float nota_rackdev_chain_decay(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).decay() : 1.0f;
}

void nota_rackdev_set_chain_decay(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setDecay(v);
}

float nota_rackdev_swing(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).swing() : 0.0f;
}

void nota_rackdev_set_swing(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setSwing(v);
}

float nota_rackdev_humanize(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).humanize() : 0.0f;
}

void nota_rackdev_set_humanize(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).setHumanize(v);
}

int32_t nota_rackdev_chain_sampler_info(const NotaEngine* e, int32_t t, int32_t di, int32_t c, NotaSamplerInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SamplerInfo info{};
    if (!nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).samplerInfo(info)) return 0;
    *out = {info.sampleId, info.rootNote, info.loop};
    return 1;
}

int32_t nota_rackdev_set_chain_sampler_root(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t root) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setSamplerRoot(root)) ? 1 : 0;
}

float nota_rackdev_chain_sampler_play_position(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).samplerPlayPosition() : -1.0f;
}

const char* nota_rackdev_chain_instrument_param_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentParamId(p) : std::string{});
}

float nota_rackdev_chain_instrument_param_default(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentParamDefault(p) : 0.0f;
}

int32_t nota_rackdev_chain_instrument_scope(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, float* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentScope(out, n) : 0;
}

int32_t nota_rackdev_chain_instrument_voice_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentVoiceCount() : -1;
}

int32_t nota_rackdev_chain_instrument_held_notes(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t* out, int32_t n) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentHeldNotes(out, n) : 0;
}

void nota_rackdev_chain_instrument_action(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t id, int32_t iarg, float farg) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).instrumentAction(id, iarg, farg);
}

int32_t nota_rackdev_chain_instrument_set_state_undoable(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, const uint8_t* data, int32_t size) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::eng(e), t, di).setInstrumentStateUndoable(c, data, size)) ? 1
                                                                                                                  : 0;
}

void nota_rackdev_open_chain_instrument_editor(NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).openInstrumentEditor();
}

const char* nota_rackdev_chain_instrument_plugin_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentPluginId() : std::string{});
}

int32_t nota_rackdev_chain_instrument_get_state(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, uint8_t* out, int32_t cap) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).instrumentGetState(out, cap) : 0;
}

// ---- Nota Mosaic as a chain instrument (specs/041) -------------------------

int32_t nota_rackdev_chain_mosaic_set_program(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, const char* text, int32_t undoable) {
    return (e && text &&
               nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di)
                   .chain(c)
                   .mosaicSetProgram(std::string(text), undoable != 0))
               ? 1
               : 0;
}

int32_t nota_rackdev_chain_mosaic_program(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyRawBytes(
        nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).mosaicProgram(), out, cap);
}

int32_t nota_rackdev_chain_mosaic_status(const NotaEngine* e, int32_t t, int32_t di, int32_t c, NotaMosaicStatus* out) {
    if (!e || !out) return 0;
    nota::engine::MosaicStatus st{};
    if (!nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).mosaicStatus(st)) return 0;
    *out = {
        st.state, st.filesDone, st.filesTotal, st.missing, st.zones, st.serial, st.diskDone, st.diskTotal, st.ramBytes};
    return 1;
}

int64_t nota_rackdev_chain_mosaic_zone_sample_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t zone) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).mosaicZoneSampleId(zone) : 0;
}

} // extern "C"
