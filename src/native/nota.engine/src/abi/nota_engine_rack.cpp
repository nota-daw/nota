// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — Instrument Rack (nota_rack_*, the track's instrument) and Audio Effect
// Rack (nota_rackdev_*, a device in the chain). Both translate to the unified
// Engine::rack* methods addressed by (track_id, device_index): the instrument-rack
// calls pass device_index = -1, the effect-rack calls pass the device index.
// Name-returning functions use a call-local static std::string (owned by the
// engine, valid until the next call), matching the other ABI slices.

#include "nota_engine_internal.h"
#include "devices/racks/RackCore.h"

#include <algorithm>
#include <cstring>
#include <string>

extern "C" {

// ===================== Instrument Rack (device_index = -1) =================
int32_t nota_engine_add_instrument_rack_track(NotaEngine* e) {
    return e ? ENG(e)->addInstrumentRackTrack() : 0;
}
int32_t nota_engine_add_drum_rack_track(NotaEngine* e) {
    return e ? ENG(e)->addDrumRackTrack() : 0;
}

int32_t nota_rack_add_sampler_chain(NotaEngine* e, int32_t t, const char* path, int32_t root, int32_t loop) {
    return (e && path) ? ENG(e)->rackAddSamplerChain(t, -1, std::string(path), root, loop != 0) : -1;
}
int32_t nota_rack_set_chain_sampler_sample(NotaEngine* e, int32_t t, int32_t chain, const char* path, int32_t root) {
    return (e && path && ENG(e)->rackSetChainSamplerSample(t, -1, chain, std::string(path), root)) ? 1 : 0;
}
int32_t nota_rack_add_plugin_instrument_chain(NotaEngine* e, int32_t t, int32_t catalogIndex) {
    return e ? ENG(e)->rackAddPluginInstrumentChain(t, -1, catalogIndex) : -1;
}
int32_t nota_rack_add_plugin_chain_device(NotaEngine* e, int32_t t, int32_t chain, int32_t catalogIndex) {
    return e ? ENG(e)->rackAddPluginChainDevice(t, -1, chain, catalogIndex) : -1;
}
int32_t nota_rackdev_add_plugin_chain_device(
    NotaEngine* e, int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    return e ? ENG(e)->rackAddPluginChainDevice(t, di, chain, catalogIndex) : -1;
}
int32_t nota_rack_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainTriggerNote(t, -1, c) : -1;
}
void nota_rack_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t c, int32_t note) {
    if (e) ENG(e)->rackSetChainTriggerNote(t, -1, c, note);
}
const char* nota_rack_chain_name(const NotaEngine* e, int32_t t, int32_t c) {
    static std::string s;
    s = e ? CENG(e)->rackChainName(t, -1, c) : std::string{};
    return s.c_str();
}
void nota_rack_set_chain_name(NotaEngine* e, int32_t t, int32_t c, const char* name) {
    if (e) ENG(e)->rackSetChainName(t, -1, c, name ? name : "");
}
int32_t nota_rackdev_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainTriggerNote(t, di, c) : -1;
}
void nota_rackdev_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t note) {
    if (e) ENG(e)->rackSetChainTriggerNote(t, di, c, note);
}

int32_t nota_rack_chain_count(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackChainCount(t, -1) : 0;
}
int32_t nota_rack_add_chain(NotaEngine* e, int32_t t, int32_t k) {
    return e ? ENG(e)->rackAddChain(t, -1, k) : -1;
}
int32_t nota_rack_remove_chain(NotaEngine* e, int32_t t, int32_t c) {
    return (e && ENG(e)->rackRemoveChain(t, -1, c)) ? 1 : 0;
}
int32_t nota_rack_set_chain_instrument(NotaEngine* e, int32_t t, int32_t c, int32_t k) {
    return (e && ENG(e)->rackSetChainInstrument(t, -1, c, k)) ? 1 : 0;
}
int32_t nota_rack_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentKind(t, -1, c) : -2;
}
const char* nota_rack_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentName(t, -1, c) : "";
}
int32_t nota_rack_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentParamCount(t, -1, c) : 0;
}
const char* nota_rack_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentParamName(t, -1, c, p) : std::string{};
    return s.c_str();
}
const char* nota_rack_chain_instrument_param_id(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentParamId(t, -1, c, p) : std::string{};
    return s.c_str();
}
float nota_rack_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return e ? CENG(e)->rackChainInstrumentParamGet(t, -1, c, p) : 0.0f;
}
void nota_rack_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t p, float v) {
    if (e) ENG(e)->rackChainInstrumentParamSet(t, -1, c, p, v);
}
float nota_rack_chain_instrument_param_default(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    return e ? CENG(e)->rackChainInstrumentParamDefault(t, -1, c, p) : 0.0f;
}
int32_t nota_rack_chain_instrument_scope(const NotaEngine* e, int32_t t, int32_t c, float* out, int32_t n) {
    return e ? CENG(e)->rackChainInstrumentScope(t, -1, c, out, n) : 0;
}
int32_t nota_rack_chain_instrument_voice_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentVoiceCount(t, -1, c) : -1;
}
int32_t nota_rack_chain_instrument_held_notes(const NotaEngine* e, int32_t t, int32_t c, int32_t* out, int32_t n) {
    return e ? CENG(e)->rackChainInstrumentHeldNotes(t, -1, c, out, n) : 0;
}
void nota_rack_chain_instrument_action(NotaEngine* e, int32_t t, int32_t c, int32_t id, int32_t iarg, float farg) {
    if (e) ENG(e)->rackChainInstrumentAction(t, -1, c, id, iarg, farg);
}
void nota_rack_open_chain_instrument_editor(NotaEngine* e, int32_t t, int32_t c) {
    if (e) ENG(e)->rackOpenChainInstrumentEditor(t, -1, c);
}
void nota_rack_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    if (e) ENG(e)->rackOpenChainDeviceEditor(t, -1, c, d);
}
const char* nota_rack_chain_instrument_plugin_id(const NotaEngine* e, int32_t t, int32_t c) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentPluginId(t, -1, c) : std::string{};
    return s.c_str();
}
int32_t nota_rack_chain_instrument_get_state(const NotaEngine* e, int32_t t, int32_t c, uint8_t* out, int32_t cap) {
    return e ? CENG(e)->rackChainInstrumentGetState(t, -1, c, out, cap) : 0;
}
int32_t nota_rack_chain_sampler_info(const NotaEngine* e, int32_t t, int32_t c, NotaSamplerInfo* out) {
    return e && CENG(e)->rackChainSamplerInfo(t, -1, c, out) ? 1 : 0;
}
int32_t nota_rack_set_chain_sampler_root(NotaEngine* e, int32_t t, int32_t c, int32_t root) {
    return e && ENG(e)->rackSetChainSamplerRoot(t, -1, c, root) ? 1 : 0;
}
float nota_rack_chain_sampler_play_position(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainSamplerPlayPosition(t, -1, c) : -1.0f;
}

int32_t nota_rack_chain_device_count(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainDeviceCount(t, -1, c) : 0;
}
int32_t nota_rack_add_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t k) {
    return e ? ENG(e)->rackAddChainDevice(t, -1, c, k) : -1;
}
int32_t nota_rack_remove_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return (e && ENG(e)->rackRemoveChainDevice(t, -1, c, d)) ? 1 : 0;
}
int32_t nota_rack_move_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t from, int32_t to) {
    return (e && ENG(e)->rackMoveChainDevice(t, -1, c, from, to)) ? 1 : 0;
}
const char* nota_rack_chain_device_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceName(t, -1, c, d) : "";
}
int32_t nota_rack_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceBuiltinKind(t, -1, c, d) : -1;
}
int32_t nota_rack_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceParamCount(t, -1, c, d) : 0;
}
const char* nota_rack_chain_device_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamName(t, -1, c, d, p) : "";
}
float nota_rack_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamMin(t, -1, c, d, p) : 0.0f;
}
float nota_rack_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamMax(t, -1, c, d, p) : 1.0f;
}
float nota_rack_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamGet(t, -1, c, d, p) : 0.0f;
}
void nota_rack_chain_device_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p, float v) {
    if (e) ENG(e)->rackChainDeviceParamSet(t, -1, c, d, p, v);
}
void nota_rack_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t b) {
    if (e) ENG(e)->rackSetChainDeviceBypassed(t, -1, c, d, b != 0);
}
int32_t nota_rack_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t c, int32_t d) {
    return (e && CENG(e)->rackChainDeviceBypassed(t, -1, c, d)) ? 1 : 0;
}

void nota_rack_set_chain_gain(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainGain(t, -1, c, v);
}
void nota_rack_set_chain_pan(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainPan(t, -1, c, v);
}
void nota_rack_set_chain_mute(NotaEngine* e, int32_t t, int32_t c, int32_t b) {
    if (e) ENG(e)->rackSetChainMute(t, -1, c, b != 0);
}
void nota_rack_set_chain_solo(NotaEngine* e, int32_t t, int32_t c, int32_t b) {
    if (e) ENG(e)->rackSetChainSolo(t, -1, c, b != 0);
}
float nota_rack_chain_gain(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainGain(t, -1, c) : 1.0f;
}
float nota_rack_chain_pan(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainPan(t, -1, c) : 0.0f;
}
int32_t nota_rack_chain_mute(const NotaEngine* e, int32_t t, int32_t c) {
    return (e && CENG(e)->rackChainMute(t, -1, c)) ? 1 : 0;
}
int32_t nota_rack_chain_solo(const NotaEngine* e, int32_t t, int32_t c) {
    return (e && CENG(e)->rackChainSolo(t, -1, c)) ? 1 : 0;
}

float nota_rack_macro_get(const NotaEngine* e, int32_t t, int32_t m) {
    return e ? CENG(e)->rackMacroGet(t, -1, m) : 0.0f;
}
void nota_rack_macro_set(NotaEngine* e, int32_t t, int32_t m, float v) {
    if (e) ENG(e)->rackMacroSet(t, -1, m, v);
}
int32_t nota_rack_add_macro_mapping(
    NotaEngine* e, int32_t t, int32_t macro, int32_t chain, int32_t di, int32_t pi, float lo, float hi) {
    return e ? ENG(e)->rackAddMacroMapping(t, -1, macro, chain, di, pi, lo, hi) : -1;
}
int32_t nota_rack_duplicate_chain(NotaEngine* e, int32_t t, int32_t chain) {
    return e ? ENG(e)->rackDuplicateChain(t, -1, chain) : -1;
}
int32_t nota_rackdev_duplicate_chain(NotaEngine* e, int32_t t, int32_t di, int32_t chain) {
    return e ? ENG(e)->rackDuplicateChain(t, di, chain) : -1;
}

int64_t nota_rackdev_chain_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainId(t, di, c) : 0;
}
int32_t nota_rackdev_checkpoint(NotaEngine* e, int32_t t, int32_t di) {
    return (e && ENG(e)->rackCheckpoint(t, di)) ? 1 : 0;
}

// Rack chain devices on the shared device clipboard (specs/042).
int32_t nota_rackdev_chain_device_copy(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return (e && ENG(e)->rackChainDeviceCopy(t, di, c, i)) ? 1 : 0;
}
int32_t nota_rackdev_chain_midi_copy(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return (e && ENG(e)->rackChainMidiCopy(t, di, c, i)) ? 1 : 0;
}
int32_t nota_rackdev_chain_device_paste(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t at) {
    return e ? ENG(e)->rackChainDevicePaste(t, di, c, at) : -1;
}
int32_t nota_rackdev_chain_midi_paste(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t at) {
    return e ? ENG(e)->rackChainMidiPaste(t, di, c, at) : -1;
}
int32_t nota_rackdev_chain_device_duplicate(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return e ? ENG(e)->rackChainDeviceDuplicate(t, di, c, i) : -1;
}
int32_t nota_rackdev_chain_midi_duplicate(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i) {
    return e ? ENG(e)->rackChainMidiDuplicate(t, di, c, i) : -1;
}
int32_t nota_rackdev_chain_device_move_to(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    return e ? ENG(e)->rackChainDeviceMoveTo(t, di, c, i, to) : -1;
}
int32_t nota_rackdev_chain_midi_move_to(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    return e ? ENG(e)->rackChainMidiMoveTo(t, di, c, i, to) : -1;
}
void nota_rack_set_mono(NotaEngine* e, int32_t t, int32_t on) {
    if (e) ENG(e)->rackSetMono(t, on != 0);
}
int32_t nota_rack_mono(const NotaEngine* e, int32_t t) {
    return (e && CENG(e)->rackMono(t)) ? 1 : 0;
}
int32_t nota_rack_mapping_count(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackMappingCount(t, -1) : 0;
}
int32_t nota_rack_mapping_info(const NotaEngine* e, int32_t t, int32_t index, int32_t* macro, int32_t* chain,
    int32_t* di, int32_t* pi, float* lo, float* hi) {
    if (!e) return 0;
    int32_t mo, ch, dv, px;
    float rmin, rmax;
    if (!CENG(e)->rackMappingInfo(t, -1, index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo;
    if (chain) *chain = ch;
    if (di) *di = dv;
    if (pi) *pi = px;
    if (lo) *lo = rmin;
    if (hi) *hi = rmax;
    return 1;
}
int32_t nota_rack_remove_mapping(NotaEngine* e, int32_t t, int32_t index) {
    return (e && ENG(e)->rackRemoveMapping(t, -1, index)) ? 1 : 0;
}
// Instrument-Rack extras (di = -1 surface).
void nota_rack_chain_zone(
    const NotaEngine* e, int32_t t, int32_t c, int32_t* keyLo, int32_t* keyHi, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) CENG(e)->rackChainZone(t, -1, c, kl, kh, vl, vh);
    if (keyLo) *keyLo = kl;
    if (keyHi) *keyHi = kh;
    if (velLo) *velLo = vl;
    if (velHi) *velHi = vh;
}
void nota_rack_set_chain_zone(
    NotaEngine* e, int32_t t, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (e) ENG(e)->rackSetChainZone(t, -1, c, keyLo, keyHi, velLo, velHi);
}
float nota_rack_chain_meter(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainMeter(t, -1, c) : 0.0f;
}
const char* nota_rack_macro_name(const NotaEngine* e, int32_t t, int32_t m) {
    static std::string s;
    s = e ? CENG(e)->rackMacroName(t, -1, m) : std::string{};
    return s.c_str();
}
void nota_rack_set_macro_name(NotaEngine* e, int32_t t, int32_t m, const char* name) {
    if (e) ENG(e)->rackSetMacroName(t, -1, m, name ? std::string(name) : std::string{});
}
float nota_rack_volume(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackVolume(t, -1) : 1.0f;
}
void nota_rack_set_volume(NotaEngine* e, int32_t t, float v) {
    if (e) ENG(e)->rackSetVolume(t, -1, v);
}
float nota_rack_glide(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackGlide(t, -1) : 0.0f;
}
void nota_rack_set_glide(NotaEngine* e, int32_t t, float v) {
    if (e) ENG(e)->rackSetGlide(t, -1, v);
}
int32_t nota_rack_set_mapping_range(NotaEngine* e, int32_t t, int32_t i, float lo, float hi) {
    return (e && ENG(e)->rackSetMappingRange(t, -1, i, lo, hi)) ? 1 : 0;
}
int32_t nota_rack_mapping_curve(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? CENG(e)->rackMappingCurve(t, -1, i) : 0;
}
int32_t nota_rack_set_mapping_curve(NotaEngine* e, int32_t t, int32_t i, int32_t curve) {
    return (e && ENG(e)->rackSetMappingCurve(t, -1, i, curve)) ? 1 : 0;
}

// Drum Rack per-pad shaping + kit-level swing/humanize (instrument-rack surface, di = -1).
void nota_rack_set_chain_choke(NotaEngine* e, int32_t t, int32_t c, int32_t g) {
    if (e) ENG(e)->rackSetChainChoke(t, -1, c, g);
}
int32_t nota_rack_chain_choke(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainChoke(t, -1, c) : 0;
}
void nota_rack_set_chain_tune(NotaEngine* e, int32_t t, int32_t c, int32_t st) {
    if (e) ENG(e)->rackSetChainTune(t, -1, c, st);
}
int32_t nota_rack_chain_tune(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainTune(t, -1, c) : 0;
}
void nota_rack_set_chain_decay(NotaEngine* e, int32_t t, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainDecay(t, -1, c, v);
}
float nota_rack_chain_decay(const NotaEngine* e, int32_t t, int32_t c) {
    return e ? CENG(e)->rackChainDecay(t, -1, c) : 1.0f;
}
void nota_rack_set_swing(NotaEngine* e, int32_t t, float v) {
    if (e) ENG(e)->rackSetSwing(t, -1, v);
}
float nota_rack_swing(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackSwing(t, -1) : 0.0f;
}
void nota_rack_set_humanize(NotaEngine* e, int32_t t, float v) {
    if (e) ENG(e)->rackSetHumanize(t, -1, v);
}
float nota_rack_humanize(const NotaEngine* e, int32_t t) {
    return e ? CENG(e)->rackHumanize(t, -1) : 0.0f;
}

// ===================== Audio Effect Rack (a device in the chain) ===========
int32_t nota_rackdev_chain_count(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackChainCount(t, di) : 0;
}
int32_t nota_rackdev_add_chain(NotaEngine* e, int32_t t, int32_t di, int32_t k) {
    return e ? ENG(e)->rackAddChain(t, di, k) : -1;
}
int32_t nota_rackdev_remove_chain(NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && ENG(e)->rackRemoveChain(t, di, c)) ? 1 : 0;
}
int32_t nota_rackdev_set_chain_instrument(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) {
    return (e && ENG(e)->rackSetChainInstrument(t, di, c, k)) ? 1 : 0;
}
int32_t nota_rackdev_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentKind(t, di, c) : -2;
}
const char* nota_rackdev_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentName(t, di, c) : "";
}
int32_t nota_rackdev_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentParamCount(t, di, c) : 0;
}
const char* nota_rackdev_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentParamName(t, di, c, p) : std::string{};
    return s.c_str();
}
float nota_rackdev_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return e ? CENG(e)->rackChainInstrumentParamGet(t, di, c, p) : 0.0f;
}
void nota_rackdev_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p, float v) {
    if (e) ENG(e)->rackChainInstrumentParamSet(t, di, c, p, v);
}

int32_t nota_rackdev_chain_device_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainDeviceCount(t, di, c) : 0;
}
int32_t nota_rackdev_add_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) {
    return e ? ENG(e)->rackAddChainDevice(t, di, c, k) : -1;
}
int32_t nota_rackdev_remove_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return (e && ENG(e)->rackRemoveChainDevice(t, di, c, d)) ? 1 : 0;
}
int32_t nota_rackdev_move_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    return (e && ENG(e)->rackMoveChainDevice(t, di, c, from, to)) ? 1 : 0;
}
const char* nota_rackdev_chain_device_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceName(t, di, c, d) : "";
}
int32_t nota_rackdev_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceBuiltinKind(t, di, c, d) : -1;
}
void nota_rackdev_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    if (e) ENG(e)->rackOpenChainDeviceEditor(t, di, c, d);
}
int32_t nota_rackdev_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceParamCount(t, di, c, d) : 0;
}
const char* nota_rackdev_chain_device_param_name(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamName(t, di, c, d, p) : "";
}
float nota_rackdev_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamMin(t, di, c, d, p) : 0.0f;
}
float nota_rackdev_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamMax(t, di, c, d, p) : 1.0f;
}
float nota_rackdev_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) {
    return e ? CENG(e)->rackChainDeviceParamGet(t, di, c, d, p) : 0.0f;
}
void nota_rackdev_chain_device_param_set(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) {
    if (e) ENG(e)->rackChainDeviceParamSet(t, di, c, d, p, v);
}
void nota_rackdev_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t b) {
    if (e) ENG(e)->rackSetChainDeviceBypassed(t, di, c, d, b != 0);
}
int32_t nota_rackdev_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return (e && CENG(e)->rackChainDeviceBypassed(t, di, c, d)) ? 1 : 0;
}
float nota_rackdev_chain_device_gain_reduction(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) {
    return e ? CENG(e)->rackChainDeviceGainReduction(t, di, c, d) : 0.0f;
}
int32_t nota_rackdev_chain_device_scope(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, float* out, int32_t n) {
    return e ? CENG(e)->rackChainDeviceScope(t, di, c, d, out, n) : 0;
}
int32_t nota_rackdev_chain_device_layer_wave(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t layer, float* out, int32_t n) {
    return e ? CENG(e)->rackChainDeviceLayerWave(t, di, c, d, layer, out, n) : 0;
}
int32_t nota_rackdev_chain_device_text(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, char* out, int32_t cap) {
    if (!e) return 0;
    const std::string s = CENG(e)->rackChainDeviceText(t, di, c, d, id);
    const int32_t n = static_cast<int32_t>(s.size());
    if (out && cap > 0) {
        const int32_t m = std::min(n, cap - 1);
        std::memcpy(out, s.data(), static_cast<size_t>(m));
        out[m] = '\0';
    }
    return n; // full length (probe with a null buffer, then fetch)
}
void nota_rackdev_chain_device_action(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, int32_t iarg, float farg) {
    if (e) ENG(e)->rackChainDeviceAction(t, di, c, d, id, iarg, farg);
}

void nota_rackdev_set_chain_gain(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainGain(t, di, c, v);
}
void nota_rackdev_set_chain_pan(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainPan(t, di, c, v);
}
void nota_rackdev_set_chain_mute(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) {
    if (e) ENG(e)->rackSetChainMute(t, di, c, b != 0);
}
void nota_rackdev_set_chain_solo(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) {
    if (e) ENG(e)->rackSetChainSolo(t, di, c, b != 0);
}
float nota_rackdev_chain_gain(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainGain(t, di, c) : 1.0f;
}
float nota_rackdev_chain_pan(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainPan(t, di, c) : 0.0f;
}
int32_t nota_rackdev_chain_mute(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && CENG(e)->rackChainMute(t, di, c)) ? 1 : 0;
}
int32_t nota_rackdev_chain_solo(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return (e && CENG(e)->rackChainSolo(t, di, c)) ? 1 : 0;
}

float nota_rackdev_macro_get(const NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    return e ? CENG(e)->rackMacroGet(t, di, m) : 0.0f;
}
void nota_rackdev_macro_set(NotaEngine* e, int32_t t, int32_t di, int32_t m, float v) {
    if (e) ENG(e)->rackMacroSet(t, di, m, v);
}
int32_t nota_rackdev_add_macro_mapping(
    NotaEngine* e, int32_t t, int32_t di, int32_t macro, int32_t chain, int32_t tdi, int32_t pi, float lo, float hi) {
    return e ? ENG(e)->rackAddMacroMapping(t, di, macro, chain, tdi, pi, lo, hi) : -1;
}
int32_t nota_rackdev_mapping_count(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackMappingCount(t, di) : 0;
}
int32_t nota_rackdev_mapping_info(const NotaEngine* e, int32_t t, int32_t di, int32_t index, int32_t* macro,
    int32_t* chain, int32_t* tdi, int32_t* pi, float* lo, float* hi) {
    if (!e) return 0;
    int32_t mo, ch, dv, px;
    float rmin, rmax;
    if (!CENG(e)->rackMappingInfo(t, di, index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo;
    if (chain) *chain = ch;
    if (tdi) *tdi = dv;
    if (pi) *pi = px;
    if (lo) *lo = rmin;
    if (hi) *hi = rmax;
    return 1;
}
int32_t nota_rackdev_remove_mapping(NotaEngine* e, int32_t t, int32_t di, int32_t index) {
    return (e && ENG(e)->rackRemoveMapping(t, di, index)) ? 1 : 0;
}

// ---- Audio Effect Rack: rack-out, routing, meter, zone, macro-name, mapping edit ----
float nota_rackdev_chain_meter(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainMeter(t, di, c) : 0.0f;
}
void nota_rackdev_chain_zone(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) CENG(e)->rackChainZone(t, di, c, kl, kh, vl, vh);
    if (velLo) *velLo = vl;
    if (velHi) *velHi = vh;
}
void nota_rackdev_set_chain_zone(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t velLo, int32_t velHi) {
    if (e) ENG(e)->rackSetChainZone(t, di, c, 0, 127, velLo, velHi);
}
const char* nota_rackdev_macro_name(const NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    static std::string s;
    s = e ? CENG(e)->rackMacroName(t, di, m) : std::string{};
    return s.c_str();
}
void nota_rackdev_set_macro_name(NotaEngine* e, int32_t t, int32_t di, int32_t m, const char* name) {
    if (e) ENG(e)->rackSetMacroName(t, di, m, name ? std::string(name) : std::string{});
}
int32_t nota_rackdev_set_mapping_range(NotaEngine* e, int32_t t, int32_t di, int32_t i, float lo, float hi) {
    return (e && ENG(e)->rackSetMappingRange(t, di, i, lo, hi)) ? 1 : 0;
}
int32_t nota_rackdev_mapping_curve(const NotaEngine* e, int32_t t, int32_t di, int32_t i) {
    return e ? CENG(e)->rackMappingCurve(t, di, i) : 0;
}
int32_t nota_rackdev_set_mapping_curve(NotaEngine* e, int32_t t, int32_t di, int32_t i, int32_t curve) {
    return (e && ENG(e)->rackSetMappingCurve(t, di, i, curve)) ? 1 : 0;
}
float nota_rackdev_volume(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackVolume(t, di) : 1.0f;
}
void nota_rackdev_set_volume(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) ENG(e)->rackSetVolume(t, di, v);
}
int32_t nota_rackdev_mode(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackMode(t, di) : 0;
}
void nota_rackdev_set_mode(NotaEngine* e, int32_t t, int32_t di, int32_t m) {
    if (e) ENG(e)->rackSetMode(t, di, m);
}
float nota_rackdev_drywet(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackDryWet(t, di) : 1.0f;
}
void nota_rackdev_set_drywet(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) ENG(e)->rackSetDryWet(t, di, v);
}
int32_t nota_rackdev_pdc(const NotaEngine* e, int32_t t, int32_t di) {
    return (e && CENG(e)->rackPdc(t, di)) ? 1 : 0;
}
void nota_rackdev_set_pdc(NotaEngine* e, int32_t t, int32_t di, int32_t on) {
    if (e) ENG(e)->rackSetPdc(t, di, on != 0);
}
float nota_rackdev_chain_select(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackChainSelect(t, di) : 0.5f;
}
void nota_rackdev_set_chain_select(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) ENG(e)->rackSetChainSelect(t, di, v);
}
int32_t nota_rackdev_sel_follow(const NotaEngine* e, int32_t t, int32_t di) {
    return (e && CENG(e)->rackSelFollow(t, di)) ? 1 : 0;
}
void nota_rackdev_set_sel_follow(NotaEngine* e, int32_t t, int32_t di, int32_t on) {
    if (e) ENG(e)->rackSetSelFollow(t, di, on != 0);
}
float nota_rackdev_live_selector(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackLiveSelector(t, di) : 0.5f;
}


// ---- MIDI Effect Rack (specs/040) and chain MIDI effects (#329) --------------------------
// A MIDI Effect Rack is the MIDI effect at index mi; the engine addresses it as kMidiRackBase − mi.
static int32_t MR(int32_t mi) { return nota::Engine::kMidiRackBase - mi; }

int32_t nota_rack_chain_midi_count(const NotaEngine* e, int32_t t, int32_t chain) {
    return e ? CENG(e)->rackChainMidiCount(t, -1, chain) : 0;
}
int32_t nota_rack_chain_midi_add(NotaEngine* e, int32_t t, int32_t chain, int32_t midi_kind) {
    return e ? ENG(e)->rackAddChainMidi(t, -1, chain, midi_kind) : -1;
}
int32_t nota_rack_chain_midi_remove(NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    return (e && ENG(e)->rackRemoveChainMidi(t, -1, chain, index)) ? 1 : 0;
}
int32_t nota_rack_chain_midi_move(NotaEngine* e, int32_t t, int32_t chain, int32_t from_index, int32_t to_index) {
    return (e && ENG(e)->rackMoveChainMidi(t, -1, chain, from_index, to_index)) ? 1 : 0;
}
int32_t nota_rack_chain_midi_kind(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->midiKind() : -1;
}
const char* nota_rack_chain_midi_name(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->displayName() : "";
}
int32_t nota_rack_chain_midi_param_count(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->paramCount() : 0;
}
const char* nota_rack_chain_midi_param_name(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->paramName(param) : "";
}
float nota_rack_chain_midi_param_min(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->paramMin(param) : 0.0f;
}
float nota_rack_chain_midi_param_max(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->paramMax(param) : 1.0f;
}
float nota_rack_chain_midi_param_get(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->getParam(param) : 0.0f;
}
void nota_rack_chain_midi_param_set(NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t param, float value) {
    auto* m = e ? ENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    if (m) m->setParam(param, value);
}
void nota_rack_chain_midi_set_bypassed(NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t bypassed) {
    if (e) ENG(e)->rackSetChainMidiBypassed(t, -1, chain, index, bypassed != 0);
}
int32_t nota_rack_chain_midi_bypassed(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return (m && m->bypassed()) ? 1 : 0;
}
int32_t nota_rack_chain_midi_last_in(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->midiLastIn() : -1;
}
int32_t nota_rack_chain_midi_last_out(const NotaEngine* e, int32_t t, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return m ? m->midiLastOut() : -1;
}
int32_t nota_rack_chain_midi_scope(const NotaEngine* e, int32_t t, int32_t chain, int32_t index, float* out, int32_t max_n) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    return (m && out) ? m->midiScope(out, max_n) : 0;
}
void nota_rack_chain_midi_command(NotaEngine* e, int32_t t, int32_t chain, int32_t index, int32_t cmd) {
    auto* m = e ? ENG(e)->rackChainMidiAt(t, -1, chain, index) : nullptr;
    if (m) m->midiCommand(cmd);
}
int32_t nota_midirack_chain_device_count(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? CENG(e)->rackChainMidiCount(t, MR(mi), chain) : 0;
}
int32_t nota_midirack_chain_device_add(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t midi_kind) {
    return e ? ENG(e)->rackAddChainMidi(t, MR(mi), chain, midi_kind) : -1;
}
int32_t nota_midirack_chain_device_remove(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    return (e && ENG(e)->rackRemoveChainMidi(t, MR(mi), chain, index)) ? 1 : 0;
}
int32_t nota_midirack_chain_device_move(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t from_index, int32_t to_index) {
    return (e && ENG(e)->rackMoveChainMidi(t, MR(mi), chain, from_index, to_index)) ? 1 : 0;
}
int32_t nota_midirack_chain_device_kind(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->midiKind() : -1;
}
const char* nota_midirack_chain_device_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->displayName() : "";
}
int32_t nota_midirack_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->paramCount() : 0;
}
const char* nota_midirack_chain_device_param_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->paramName(param) : "";
}
float nota_midirack_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->paramMin(param) : 0.0f;
}
float nota_midirack_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->paramMax(param) : 1.0f;
}
float nota_midirack_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->getParam(param) : 0.0f;
}
void nota_midirack_chain_device_param_set(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t param, float value) {
    auto* m = e ? ENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    if (m) m->setParam(param, value);
}
void nota_midirack_chain_device_set_bypassed(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t bypassed) {
    if (e) ENG(e)->rackSetChainMidiBypassed(t, MR(mi), chain, index, bypassed != 0);
}
int32_t nota_midirack_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return (m && m->bypassed()) ? 1 : 0;
}
int32_t nota_midirack_chain_device_last_in(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->midiLastIn() : -1;
}
int32_t nota_midirack_chain_device_last_out(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return m ? m->midiLastOut() : -1;
}
int32_t nota_midirack_chain_device_scope(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, float* out, int32_t max_n) {
    auto* m = e ? CENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    return (m && out) ? m->midiScope(out, max_n) : 0;
}
void nota_midirack_chain_device_command(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t index, int32_t cmd) {
    auto* m = e ? ENG(e)->rackChainMidiAt(t, MR(mi), chain, index) : nullptr;
    if (m) m->midiCommand(cmd);
}
int32_t nota_midirack_chain_count(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? CENG(e)->rackChainCount(t, MR(mi)) : 0;
}
int32_t nota_midirack_add_chain(NotaEngine* e, int32_t t, int32_t mi) {
    return e ? ENG(e)->rackAddChain(t, MR(mi), -1) : -1;
}
int32_t nota_midirack_remove_chain(NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return (e && CENG(e)->rackChainCount(t, MR(mi)) > 1 && ENG(e)->rackRemoveChain(t, MR(mi), chain)) ? 1 : 0;
}
int32_t nota_midirack_duplicate_chain(NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? ENG(e)->rackDuplicateChain(t, MR(mi), chain) : -1;
}
int32_t nota_midirack_checkpoint(NotaEngine* e, int32_t t, int32_t mi) {
    return (e && ENG(e)->rackCheckpoint(t, MR(mi))) ? 1 : 0;
}
const char* nota_midirack_chain_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    static std::string s;
    s = e ? CENG(e)->rackChainName(t, MR(mi), chain) : std::string{};
    return s.c_str();
}
void nota_midirack_set_chain_name(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, const char* name) {
    if (e) ENG(e)->rackSetChainName(t, MR(mi), chain, name ? name : "");
}
void nota_midirack_set_chain_mute(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t on) {
    if (e) ENG(e)->rackSetChainMute(t, MR(mi), chain, on != 0);
}
int32_t nota_midirack_chain_mute(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return (e && CENG(e)->rackChainMute(t, MR(mi), chain)) ? 1 : 0;
}
void nota_midirack_set_chain_solo(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t on) {
    if (e) ENG(e)->rackSetChainSolo(t, MR(mi), chain, on != 0);
}
int32_t nota_midirack_chain_solo(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return (e && CENG(e)->rackChainSolo(t, MR(mi), chain)) ? 1 : 0;
}
void nota_midirack_set_chain_zone(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t key_lo, int32_t key_hi, int32_t vel_lo, int32_t vel_hi) {
    if (e) ENG(e)->rackSetChainZone(t, MR(mi), chain, key_lo, key_hi, vel_lo, vel_hi);
}
void nota_midirack_chain_zone(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t* key_lo, int32_t* key_hi, int32_t* vel_lo, int32_t* vel_hi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) CENG(e)->rackChainZone(t, MR(mi), chain, kl, kh, vl, vh);
    if (key_lo) *key_lo = kl;
    if (key_hi) *key_hi = kh;
    if (vel_lo) *vel_lo = vl;
    if (vel_hi) *vel_hi = vh;
}
void nota_midirack_set_chain_select(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t lo, int32_t hi) {
    if (e) ENG(e)->rackSetChainSelectRange(t, MR(mi), chain, lo, hi);
}
void nota_midirack_chain_select_range(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t* lo, int32_t* hi) {
    int32_t l = 0, h = 127;
    if (e) CENG(e)->rackChainSelectRange(t, MR(mi), chain, l, h);
    if (lo) *lo = l;
    if (hi) *hi = h;
}
void nota_midirack_set_chain_transpose(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, int32_t semitones) {
    if (e) ENG(e)->rackSetChainTranspose(t, MR(mi), chain, semitones);
}
int32_t nota_midirack_chain_transpose(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? CENG(e)->rackChainTranspose(t, MR(mi), chain) : 0;
}
void nota_midirack_set_chain_vel_scale(NotaEngine* e, int32_t t, int32_t mi, int32_t chain, float scale) {
    if (e) ENG(e)->rackSetChainVelScale(t, MR(mi), chain, scale);
}
float nota_midirack_chain_vel_scale(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? CENG(e)->rackChainVelScale(t, MR(mi), chain) : 1.0f;
}
float nota_midirack_chain_activity(const NotaEngine* e, int32_t t, int32_t mi, int32_t chain) {
    return e ? CENG(e)->rackChainMeter(t, MR(mi), chain) : 0.0f;
}
void nota_midirack_set_mode(NotaEngine* e, int32_t t, int32_t mi, int32_t mode) {
    if (e && (mode == 0 || mode == 2)) ENG(e)->rackSetMode(t, MR(mi), mode);
}
int32_t nota_midirack_mode(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? CENG(e)->rackMode(t, MR(mi)) : 0;
}
void nota_midirack_set_selector(NotaEngine* e, int32_t t, int32_t mi, float v) {
    if (e) ENG(e)->rackSetChainSelect(t, MR(mi), v);
}
float nota_midirack_selector(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? CENG(e)->rackChainSelect(t, MR(mi)) : 0.5f;
}
void nota_midirack_set_merge(NotaEngine* e, int32_t t, int32_t mi, int32_t on) {
    if (e) ENG(e)->rackSetMerge(t, MR(mi), on != 0);
}
int32_t nota_midirack_merge(const NotaEngine* e, int32_t t, int32_t mi) {
    return (e && CENG(e)->rackMerge(t, MR(mi))) ? 1 : 0;
}
void nota_midirack_set_thru(NotaEngine* e, int32_t t, int32_t mi, int32_t on) {
    if (e) ENG(e)->rackSetThru(t, MR(mi), on != 0);
}
int32_t nota_midirack_thru(const NotaEngine* e, int32_t t, int32_t mi) {
    return (e && CENG(e)->rackThru(t, MR(mi))) ? 1 : 0;
}
float nota_midirack_macro_get(const NotaEngine* e, int32_t t, int32_t mi, int32_t macro) {
    return e ? CENG(e)->rackMacroGet(t, MR(mi), macro) : 0.0f;
}
void nota_midirack_macro_set(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, float v) {
    if (e) ENG(e)->rackMacroSet(t, MR(mi), macro, v);
}
const char* nota_midirack_macro_name(const NotaEngine* e, int32_t t, int32_t mi, int32_t macro) {
    static std::string s;
    s = e ? CENG(e)->rackMacroName(t, MR(mi), macro) : std::string{};
    return s.c_str();
}
void nota_midirack_set_macro_name(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, const char* name) {
    if (e) ENG(e)->rackSetMacroName(t, MR(mi), macro, name ? std::string(name) : std::string{});
}
int32_t nota_midirack_add_macro_mapping(NotaEngine* e, int32_t t, int32_t mi, int32_t macro, int32_t chain, int32_t device, int32_t param, float lo, float hi) {
    if (!e) return -1;
    const int32_t target = device >= 0 ? nota::RackCore::midiTarget(device) : device;
    return ENG(e)->rackAddMacroMapping(t, MR(mi), macro, chain, target, param, lo, hi);
}
int32_t nota_midirack_mapping_count(const NotaEngine* e, int32_t t, int32_t mi) {
    return e ? CENG(e)->rackMappingCount(t, MR(mi)) : 0;
}
int32_t nota_midirack_mapping_info(const NotaEngine* e, int32_t t, int32_t mi, int32_t index, int32_t* macro, int32_t* chain, int32_t* device, int32_t* param, float* lo, float* hi, int32_t* curve) {
    if (!e) return 0;
    int32_t mo, ch, dv, px;
    float rmin, rmax;
    if (!CENG(e)->rackMappingInfo(t, MR(mi), index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo;
    if (chain) *chain = ch;
    if (device) *device = nota::RackCore::isMidiTarget(dv) ? nota::RackCore::midiTargetIndex(dv) : dv;
    if (param) *param = px;
    if (lo) *lo = rmin;
    if (hi) *hi = rmax;
    if (curve) *curve = CENG(e)->rackMappingCurve(t, MR(mi), index);
    return 1;
}
int32_t nota_midirack_remove_mapping(NotaEngine* e, int32_t t, int32_t mi, int32_t index) {
    return (e && ENG(e)->rackRemoveMapping(t, MR(mi), index)) ? 1 : 0;
}
int32_t nota_midirack_set_mapping_range(NotaEngine* e, int32_t t, int32_t mi, int32_t index, float lo, float hi) {
    return (e && ENG(e)->rackSetMappingRange(t, MR(mi), index, lo, hi)) ? 1 : 0;
}
int32_t nota_midirack_set_mapping_curve(NotaEngine* e, int32_t t, int32_t mi, int32_t index, int32_t curve) {
    return (e && ENG(e)->rackSetMappingCurve(t, MR(mi), index, curve)) ? 1 : 0;
}


// ---- specs/041: the nota_rack_* calls a Drum Rack card needs, for any rack address
// (device_index −1, an effect rack, or NOTA_RACK_NESTED_BASE − chain: a nested Drum Rack).
int32_t nota_rackdev_add_sampler_chain(NotaEngine* e, int32_t t, int32_t di, const char* path, int32_t root, int32_t loop) {
    return (e && path) ? ENG(e)->rackAddSamplerChain(t, di, std::string(path), root, loop != 0) : -1;
}
int32_t nota_rackdev_set_chain_sampler_sample(NotaEngine* e, int32_t t, int32_t di, int32_t chain, const char* path, int32_t root) {
    return (e && path && ENG(e)->rackSetChainSamplerSample(t, di, chain, std::string(path), root)) ? 1 : 0;
}
int32_t nota_rackdev_add_plugin_instrument_chain(NotaEngine* e, int32_t t, int32_t di, int32_t catalogIndex) {
    return e ? ENG(e)->rackAddPluginInstrumentChain(t, di, catalogIndex) : -1;
}
const char* nota_rackdev_chain_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    static std::string s;
    s = e ? CENG(e)->rackChainName(t, di, c) : std::string{};
    return s.c_str();
}
void nota_rackdev_set_chain_name(NotaEngine* e, int32_t t, int32_t di, int32_t c, const char* name) {
    if (e) ENG(e)->rackSetChainName(t, di, c, name ? name : "");
}
int32_t nota_rackdev_chain_choke(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainChoke(t, di, c) : 0;
}
void nota_rackdev_set_chain_choke(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t g) {
    if (e) ENG(e)->rackSetChainChoke(t, di, c, g);
}
int32_t nota_rackdev_chain_tune(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainTune(t, di, c) : 0;
}
void nota_rackdev_set_chain_tune(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t st) {
    if (e) ENG(e)->rackSetChainTune(t, di, c, st);
}
float nota_rackdev_chain_decay(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainDecay(t, di, c) : 1.0f;
}
void nota_rackdev_set_chain_decay(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) {
    if (e) ENG(e)->rackSetChainDecay(t, di, c, v);
}
float nota_rackdev_swing(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackSwing(t, di) : 0.0f;
}
void nota_rackdev_set_swing(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) ENG(e)->rackSetSwing(t, di, v);
}
float nota_rackdev_humanize(const NotaEngine* e, int32_t t, int32_t di) {
    return e ? CENG(e)->rackHumanize(t, di) : 0.0f;
}
void nota_rackdev_set_humanize(NotaEngine* e, int32_t t, int32_t di, float v) {
    if (e) ENG(e)->rackSetHumanize(t, di, v);
}
int32_t nota_rackdev_chain_sampler_info(const NotaEngine* e, int32_t t, int32_t di, int32_t c, NotaSamplerInfo* out) {
    return e && CENG(e)->rackChainSamplerInfo(t, di, c, out) ? 1 : 0;
}
int32_t nota_rackdev_set_chain_sampler_root(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t root) {
    return e && ENG(e)->rackSetChainSamplerRoot(t, di, c, root) ? 1 : 0;
}
float nota_rackdev_chain_sampler_play_position(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainSamplerPlayPosition(t, di, c) : -1.0f;
}
const char* nota_rackdev_chain_instrument_param_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentParamId(t, di, c, p) : std::string{};
    return s.c_str();
}
float nota_rackdev_chain_instrument_param_default(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    return e ? CENG(e)->rackChainInstrumentParamDefault(t, di, c, p) : 0.0f;
}
int32_t nota_rackdev_chain_instrument_scope(const NotaEngine* e, int32_t t, int32_t di, int32_t c, float* out, int32_t n) {
    return e ? CENG(e)->rackChainInstrumentScope(t, di, c, out, n) : 0;
}
int32_t nota_rackdev_chain_instrument_voice_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    return e ? CENG(e)->rackChainInstrumentVoiceCount(t, di, c) : -1;
}
int32_t nota_rackdev_chain_instrument_held_notes(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t* out, int32_t n) {
    return e ? CENG(e)->rackChainInstrumentHeldNotes(t, di, c, out, n) : 0;
}
void nota_rackdev_chain_instrument_action(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t id, int32_t iarg, float farg) {
    if (e) ENG(e)->rackChainInstrumentAction(t, di, c, id, iarg, farg);
}
void nota_rackdev_open_chain_instrument_editor(NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    if (e) ENG(e)->rackOpenChainInstrumentEditor(t, di, c);
}
const char* nota_rackdev_chain_instrument_plugin_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c) {
    static std::string s;
    s = e ? CENG(e)->rackChainInstrumentPluginId(t, di, c) : std::string{};
    return s.c_str();
}
int32_t nota_rackdev_chain_instrument_get_state(const NotaEngine* e, int32_t t, int32_t di, int32_t c, uint8_t* out, int32_t cap) {
    return e ? CENG(e)->rackChainInstrumentGetState(t, di, c, out, cap) : 0;
}

// ---- Nota Mosaic as a chain instrument (specs/041) ----------------------------------
int32_t nota_rackdev_chain_mosaic_set_program(NotaEngine* e, int32_t t, int32_t di, int32_t c, const char* text, int32_t undoable) {
    return e && text && ENG(e)->rackChainMosaicSetProgram(t, di, c, std::string(text), undoable != 0) ? 1 : 0;
}
int32_t nota_rackdev_chain_mosaic_program(const NotaEngine* e, int32_t t, int32_t di, int32_t c, char* out, int32_t cap) {
    if (!e) return 0;
    const std::string s = CENG(e)->rackChainMosaicProgram(t, di, c);
    if (out && cap > 0) std::memcpy(out, s.data(), std::min<size_t>(s.size(), static_cast<size_t>(cap)));
    return static_cast<int32_t>(s.size());
}
int32_t nota_rackdev_chain_mosaic_status(const NotaEngine* e, int32_t t, int32_t di, int32_t c, NotaMosaicStatus* out) {
    return e && out && CENG(e)->rackChainMosaicStatus(t, di, c, out) ? 1 : 0;
}
int64_t nota_rackdev_chain_mosaic_zone_sample_id(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t zone) {
    return e ? CENG(e)->rackChainMosaicZoneSampleId(t, di, c, zone) : 0;
}

} // extern "C"
