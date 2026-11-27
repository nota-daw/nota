// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — MIDI Effect Rack and chain MIDI effects.

#include "nota/nota_engine.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include "nota/devices/racks/RackCore.h"

#include <cstdint>
#include <string>

namespace {

inline int32_t mr(int32_t mi) noexcept {
    return nota::engine::kMidiRackBase - mi;
}

} // namespace

extern "C" {

// ---- MIDI Effect Rack (specs/040) and chain MIDI effects (#329) ------------

int32_t nota_rack_chain_midi_count(const NotaEngine* e, int32_t t, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiCount() : 0;
}

int32_t nota_rack_chain_midi_add(NotaEngine* e, int32_t t, int32_t chain, int32_t midi_kind) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).addMidiEffect(midi_kind) : -1;
}

int32_t nota_rack_chain_midi_remove(NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).removeMidiEffect(index)) ? 1 : 0;
}

int32_t nota_rack_chain_midi_move(NotaEngine* e, int32_t t, int32_t chain, int32_t from_index, int32_t to_index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).moveMidiEffect(from_index, to_index))
               ? 1
               : 0;
}

int32_t nota_rack_chain_midi_kind(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiKind(index) : -1;
}

const char* nota_rack_chain_midi_name(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiName(index) : std::string{});
}

int32_t nota_rack_chain_midi_param_count(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiParamCount(index) : 0;
}

const char* nota_rack_chain_midi_param_name(
    const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiParamName(index, param)
          : std::string{});
}

float nota_rack_chain_midi_param_min(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiParamMin(index, param) : 0.0f;
}

float nota_rack_chain_midi_param_max(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiParamMax(index, param) : 1.0f;
}

float nota_rack_chain_midi_param_get(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiParamGet(index, param) : 0.0f;
}

void nota_rack_chain_midi_param_set(
    NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param, float value) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).midiParamSet(index, param, value);
}

void nota_rack_chain_midi_set_bypassed(NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t bypassed) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).setMidiBypassed(index, bypassed != 0);
}

int32_t nota_rack_chain_midi_bypassed(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiBypassed(index)) ? 1 : 0;
}

int32_t nota_rack_chain_midi_last_in(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiLastIn(index) : -1;
}

int32_t nota_rack_chain_midi_last_out(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiLastOut(index) : -1;
}

int32_t nota_rack_chain_midi_scope(
    const NotaEngine* e, int32_t t, int32_t chain, int32_t index, float* out, int32_t max_n) {
    return (e && out) ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, -1).chain(chain).midiScope(index, out, max_n)
                      : 0;
}

void nota_rack_chain_midi_command(NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t cmd) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, -1).chain(chain).midiCommand(index, cmd);
}

int32_t nota_midirack_chain_device_count(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiCount() : 0;
}

int32_t nota_midirack_chain_device_add(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t midi_kind) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).addMidiEffect(midi_kind) : -1;
}

int32_t nota_midirack_chain_device_remove(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).removeMidiEffect(index)) ? 1 : 0;
}

int32_t nota_midirack_chain_device_move(
    NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t from_index, int32_t to_index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi))
                     .chain(chain)
                     .moveMidiEffect(from_index, to_index))
               ? 1
               : 0;
}

int32_t nota_midirack_chain_device_kind(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiKind(index) : -1;
}

const char* nota_midirack_chain_device_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiName(index) : std::string{});
}

int32_t nota_midirack_chain_device_param_count(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiParamCount(index) : 0;
}

const char* nota_midirack_chain_device_param_name(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiParamName(index, param)
          : std::string{});
}

float nota_midirack_chain_device_param_min(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiParamMin(index, param) : 0.0f;
}

float nota_midirack_chain_device_param_max(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiParamMax(index, param) : 1.0f;
}

float nota_midirack_chain_device_param_get(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiParamGet(index, param) : 0.0f;
}

void nota_midirack_chain_device_param_set(
    NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param, float value) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).midiParamSet(index, param, value);
}

void nota_midirack_chain_device_set_bypassed(
    NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t bypassed) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setMidiBypassed(index, bypassed != 0);
}

int32_t nota_midirack_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiBypassed(index)) ? 1 : 0;
}

int32_t nota_midirack_chain_device_last_in(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiLastIn(index) : -1;
}

int32_t nota_midirack_chain_device_last_out(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiLastOut(index) : -1;
}

int32_t nota_midirack_chain_device_scope(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, float* out, int32_t max_n) {
    return (e && out)
               ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).midiScope(index, out, max_n)
               : 0;
}

void nota_midirack_chain_device_command(
    NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t cmd) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).midiCommand(index, cmd);
}

int32_t nota_midirack_chain_count(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chainCount() : 0;
}

int32_t nota_midirack_add_chain(NotaEngine* e, int32_t t, int32_t mi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).addChain(-1) : -1;
}

int32_t nota_midirack_remove_chain(NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    if (!e) return 0;
    auto rack = nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi));
    return (rack.chainCount() > 1 && rack.removeChain(chain)) ? 1 : 0;
}

int32_t nota_midirack_duplicate_chain(NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).duplicateChain(chain) : -1;
}

int32_t nota_midirack_checkpoint(NotaEngine* e, int32_t t, int32_t mi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).checkpoint()) ? 1 : 0;
}

const char* nota_midirack_chain_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).name() : std::string{});
}

void nota_midirack_set_chain_name(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, const char* name) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setName(name ? name : "");
}

void nota_midirack_set_chain_mute(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setMute(on != 0);
}

int32_t nota_midirack_chain_mute(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).mute()) ? 1 : 0;
}

void nota_midirack_set_chain_solo(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setSolo(on != 0);
}

int32_t nota_midirack_chain_solo(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).solo()) ? 1 : 0;
}

void nota_midirack_set_chain_zone(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t key_lo, int32_t key_hi,
    int32_t vel_lo, int32_t vel_hi) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setZone(key_lo, key_hi, vel_lo, vel_hi);
}

void nota_midirack_chain_zone(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t* key_lo,
    int32_t* key_hi, int32_t* vel_lo, int32_t* vel_hi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).zone(kl, kh, vl, vh);
    if (key_lo) *key_lo = kl;
    if (key_hi) *key_hi = kh;
    if (vel_lo) *vel_lo = vl;
    if (vel_hi) *vel_hi = vh;
}

void nota_midirack_set_chain_select(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t lo, int32_t hi) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setSelectRange(lo, hi);
}

void nota_midirack_chain_select_range(
    const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t* lo, int32_t* hi) {
    int32_t l = 0, h = 127;
    if (e) nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).selectRange(l, h);
    if (lo) *lo = l;
    if (hi) *hi = h;
}

void nota_midirack_set_chain_transpose(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t semitones) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setTranspose(semitones);
}

int32_t nota_midirack_chain_transpose(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).transpose() : 0;
}

void nota_midirack_set_chain_vel_scale(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, float scale) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).chain(chain).setVelScale(scale);
}

float nota_midirack_chain_vel_scale(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).velScale() : 1.0f;
}

float nota_midirack_chain_activity(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chain(chain).meter() : 0.0f;
}

void nota_midirack_set_mode(NotaEngine* e, int32_t t, int32_t mi, int32_t mode) {
    if (e && (mode == 0 || mode == 2)) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setMode(mode);
}

int32_t nota_midirack_mode(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).mode() : 0;
}

void nota_midirack_set_selector(NotaEngine* e, int32_t t, int32_t mi, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setChainSelect(v);
}

float nota_midirack_selector(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).chainSelect() : 0.5f;
}

void nota_midirack_set_merge(NotaEngine* e, int32_t t, int32_t mi, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setMerge(on != 0);
}

int32_t nota_midirack_merge(const NotaEngine* e, int32_t t, int32_t mi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).merge()) ? 1 : 0;
}

void nota_midirack_set_thru(NotaEngine* e, int32_t t, int32_t mi, int32_t on) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setThru(on != 0);
}

int32_t nota_midirack_thru(const NotaEngine* e, int32_t t, int32_t mi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).thru()) ? 1 : 0;
}

float nota_midirack_macro_get(const NotaEngine* e, int32_t t, int32_t mi, int32_t macro) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).macro(macro).get() : 0.0f;
}

void nota_midirack_macro_set(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, float v) {
    if (e) nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).macro(macro).set(v);
}

const char* nota_midirack_macro_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t macro) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).macro(macro).name() : std::string{});
}

void nota_midirack_set_macro_name(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, const char* name) {
    if (e)
        nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi))
            .macro(macro)
            .setName(name ? std::string(name) : std::string{});
}

int32_t nota_midirack_add_macro_mapping(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, int32_t chain,
    int32_t device, int32_t param, float lo, float hi) {
    if (!e) return -1;
    const int32_t target = device >= 0 ? nota::devices::RackCore::midiTarget(device) : device;
    return nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).addMacroMapping(macro, chain, target, param, lo, hi);
}

int32_t nota_midirack_mapping_count(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi)).mappingCount() : 0;
}

int32_t nota_midirack_mapping_info(const NotaEngine* e, int32_t t, int32_t mi, int32_t index, int32_t* macro,
    int32_t* chain, int32_t* device, int32_t* param, float* lo, float* hi, int32_t* curve) {
    if (!e) return 0;
    const auto rack = nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, mr(mi));
    int32_t mo = 0, ch = 0, dv = 0, px = 0;
    float rmin = 0.0f, rmax = 1.0f;
    if (!rack.mappingInfo(index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo;
    if (chain) *chain = ch;
    if (device) *device = nota::devices::RackCore::isMidiTarget(dv) ? nota::devices::RackCore::midiTargetIndex(dv) : dv;
    if (param) *param = px;
    if (lo) *lo = rmin;
    if (hi) *hi = rmax;
    if (curve) *curve = rack.mappingCurve(index);
    return 1;
}

int32_t nota_midirack_remove_mapping(NotaEngine* e, int32_t t, int32_t mi, int32_t index) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).removeMapping(index)) ? 1 : 0;
}

int32_t nota_midirack_set_mapping_range(NotaEngine* e, int32_t t, int32_t mi, int32_t index, float lo, float hi) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setMappingRange(index, lo, hi)) ? 1 : 0;
}

int32_t nota_midirack_set_mapping_curve(NotaEngine* e, int32_t t, int32_t mi, int32_t index, int32_t curve) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, mr(mi)).setMappingCurve(index, curve)) ? 1 : 0;
}

} // extern "C"
