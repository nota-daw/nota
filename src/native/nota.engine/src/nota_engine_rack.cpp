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

#include <string>

extern "C" {

// ===================== Instrument Rack (device_index = -1) =================
int32_t nota_engine_add_instrument_rack_track(NotaEngine* e) { return e ? ENG(e)->addInstrumentRackTrack() : 0; }
int32_t nota_engine_add_drum_rack_track(NotaEngine* e) { return e ? ENG(e)->addDrumRackTrack() : 0; }

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
int32_t nota_rackdev_add_plugin_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    return e ? ENG(e)->rackAddPluginChainDevice(t, di, chain, catalogIndex) : -1;
}
int32_t nota_rack_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainTriggerNote(t, -1, c) : -1; }
void nota_rack_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t c, int32_t note) { if (e) ENG(e)->rackSetChainTriggerNote(t, -1, c, note); }
const char* nota_rack_chain_name(const NotaEngine* e, int32_t t, int32_t c) {
    static std::string s; s = e ? CENG(e)->rackChainName(t, -1, c) : std::string{}; return s.c_str();
}
void nota_rack_set_chain_name(NotaEngine* e, int32_t t, int32_t c, const char* name) {
    if (e) ENG(e)->rackSetChainName(t, -1, c, name ? name : "");
}
int32_t nota_rackdev_chain_trigger_note(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainTriggerNote(t, di, c) : -1; }
void nota_rackdev_set_chain_trigger_note(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t note) { if (e) ENG(e)->rackSetChainTriggerNote(t, di, c, note); }

int32_t nota_rack_chain_count(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackChainCount(t, -1) : 0; }
int32_t nota_rack_add_chain(NotaEngine* e, int32_t t, int32_t k) { return e ? ENG(e)->rackAddChain(t, -1, k) : -1; }
int32_t nota_rack_remove_chain(NotaEngine* e, int32_t t, int32_t c) { return (e && ENG(e)->rackRemoveChain(t, -1, c)) ? 1 : 0; }
int32_t nota_rack_set_chain_instrument(NotaEngine* e, int32_t t, int32_t c, int32_t k) { return (e && ENG(e)->rackSetChainInstrument(t, -1, c, k)) ? 1 : 0; }
int32_t nota_rack_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainInstrumentKind(t, -1, c) : -2; }
const char* nota_rack_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainInstrumentName(t, -1, c) : ""; }
int32_t nota_rack_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainInstrumentParamCount(t, -1, c) : 0; }
const char* nota_rack_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    static std::string s; s = e ? CENG(e)->rackChainInstrumentParamName(t, -1, c, p) : std::string{}; return s.c_str();
}
const char* nota_rack_chain_instrument_param_id(const NotaEngine* e, int32_t t, int32_t c, int32_t p) {
    static std::string s; s = e ? CENG(e)->rackChainInstrumentParamId(t, -1, c, p) : std::string{}; return s.c_str();
}
float nota_rack_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t p) { return e ? CENG(e)->rackChainInstrumentParamGet(t, -1, c, p) : 0.0f; }
void nota_rack_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t p, float v) { if (e) ENG(e)->rackChainInstrumentParamSet(t, -1, c, p, v); }
float nota_rack_chain_instrument_param_default(const NotaEngine* e, int32_t t, int32_t c, int32_t p) { return e ? CENG(e)->rackChainInstrumentParamDefault(t, -1, c, p) : 0.0f; }
void nota_rack_open_chain_instrument_editor(NotaEngine* e, int32_t t, int32_t c) { if (e) ENG(e)->rackOpenChainInstrumentEditor(t, -1, c); }
void nota_rack_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t c, int32_t d) { if (e) ENG(e)->rackOpenChainDeviceEditor(t, -1, c, d); }
const char* nota_rack_chain_instrument_plugin_id(const NotaEngine* e, int32_t t, int32_t c) {
    static std::string s; s = e ? CENG(e)->rackChainInstrumentPluginId(t, -1, c) : std::string{}; return s.c_str();
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

int32_t nota_rack_chain_device_count(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainDeviceCount(t, -1, c) : 0; }
int32_t nota_rack_add_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t k) { return e ? ENG(e)->rackAddChainDevice(t, -1, c, k) : -1; }
int32_t nota_rack_remove_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t d) { return (e && ENG(e)->rackRemoveChainDevice(t, -1, c, d)) ? 1 : 0; }
int32_t nota_rack_move_chain_device(NotaEngine* e, int32_t t, int32_t c, int32_t from, int32_t to) { return (e && ENG(e)->rackMoveChainDevice(t, -1, c, from, to)) ? 1 : 0; }
const char* nota_rack_chain_device_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceName(t, -1, c, d) : ""; }
int32_t nota_rack_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceBuiltinKind(t, -1, c, d) : -1; }
int32_t nota_rack_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceParamCount(t, -1, c, d) : 0; }
const char* nota_rack_chain_device_param_name(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamName(t, -1, c, d, p) : ""; }
float nota_rack_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamMin(t, -1, c, d, p) : 0.0f; }
float nota_rack_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamMax(t, -1, c, d, p) : 1.0f; }
float nota_rack_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamGet(t, -1, c, d, p) : 0.0f; }
void nota_rack_chain_device_param_set(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t p, float v) { if (e) ENG(e)->rackChainDeviceParamSet(t, -1, c, d, p, v); }
void nota_rack_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t c, int32_t d, int32_t b) { if (e) ENG(e)->rackSetChainDeviceBypassed(t, -1, c, d, b != 0); }
int32_t nota_rack_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t c, int32_t d) { return (e && CENG(e)->rackChainDeviceBypassed(t, -1, c, d)) ? 1 : 0; }

void  nota_rack_set_chain_gain(NotaEngine* e, int32_t t, int32_t c, float v) { if (e) ENG(e)->rackSetChainGain(t, -1, c, v); }
void  nota_rack_set_chain_pan (NotaEngine* e, int32_t t, int32_t c, float v) { if (e) ENG(e)->rackSetChainPan(t, -1, c, v); }
void  nota_rack_set_chain_mute(NotaEngine* e, int32_t t, int32_t c, int32_t b) { if (e) ENG(e)->rackSetChainMute(t, -1, c, b != 0); }
void  nota_rack_set_chain_solo(NotaEngine* e, int32_t t, int32_t c, int32_t b) { if (e) ENG(e)->rackSetChainSolo(t, -1, c, b != 0); }
float nota_rack_chain_gain(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainGain(t, -1, c) : 1.0f; }
float nota_rack_chain_pan (const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainPan(t, -1, c)  : 0.0f; }
int32_t nota_rack_chain_mute(const NotaEngine* e, int32_t t, int32_t c) { return (e && CENG(e)->rackChainMute(t, -1, c)) ? 1 : 0; }
int32_t nota_rack_chain_solo(const NotaEngine* e, int32_t t, int32_t c) { return (e && CENG(e)->rackChainSolo(t, -1, c)) ? 1 : 0; }

float nota_rack_macro_get(const NotaEngine* e, int32_t t, int32_t m) { return e ? CENG(e)->rackMacroGet(t, -1, m) : 0.0f; }
void  nota_rack_macro_set(NotaEngine* e, int32_t t, int32_t m, float v) { if (e) ENG(e)->rackMacroSet(t, -1, m, v); }
int32_t nota_rack_add_macro_mapping(NotaEngine* e, int32_t t, int32_t macro, int32_t chain, int32_t di, int32_t pi, float lo, float hi) {
    return e ? ENG(e)->rackAddMacroMapping(t, -1, macro, chain, di, pi, lo, hi) : -1;
}
int32_t nota_rack_mapping_count(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackMappingCount(t, -1) : 0; }
int32_t nota_rack_mapping_info(const NotaEngine* e, int32_t t, int32_t index,
        int32_t* macro, int32_t* chain, int32_t* di, int32_t* pi, float* lo, float* hi) {
    if (!e) return 0;
    int32_t mo, ch, dv, px; float rmin, rmax;
    if (!CENG(e)->rackMappingInfo(t, -1, index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo; if (chain) *chain = ch; if (di) *di = dv;
    if (pi) *pi = px; if (lo) *lo = rmin; if (hi) *hi = rmax;
    return 1;
}
int32_t nota_rack_remove_mapping(NotaEngine* e, int32_t t, int32_t index) { return (e && ENG(e)->rackRemoveMapping(t, -1, index)) ? 1 : 0; }
// Instrument-Rack extras (di = -1 surface).
void nota_rack_chain_zone(const NotaEngine* e, int32_t t, int32_t c, int32_t* keyLo, int32_t* keyHi, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) CENG(e)->rackChainZone(t, -1, c, kl, kh, vl, vh);
    if (keyLo) *keyLo = kl; if (keyHi) *keyHi = kh; if (velLo) *velLo = vl; if (velHi) *velHi = vh;
}
void nota_rack_set_chain_zone(NotaEngine* e, int32_t t, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) { if (e) ENG(e)->rackSetChainZone(t, -1, c, keyLo, keyHi, velLo, velHi); }
float nota_rack_chain_meter(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainMeter(t, -1, c) : 0.0f; }
const char* nota_rack_macro_name(const NotaEngine* e, int32_t t, int32_t m) { static std::string s; s = e ? CENG(e)->rackMacroName(t, -1, m) : std::string{}; return s.c_str(); }
void nota_rack_set_macro_name(NotaEngine* e, int32_t t, int32_t m, const char* name) { if (e) ENG(e)->rackSetMacroName(t, -1, m, name ? std::string(name) : std::string{}); }
float nota_rack_volume(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackVolume(t, -1) : 1.0f; }
void nota_rack_set_volume(NotaEngine* e, int32_t t, float v) { if (e) ENG(e)->rackSetVolume(t, -1, v); }
float nota_rack_glide(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackGlide(t, -1) : 0.0f; }
void nota_rack_set_glide(NotaEngine* e, int32_t t, float v) { if (e) ENG(e)->rackSetGlide(t, -1, v); }
int32_t nota_rack_set_mapping_range(NotaEngine* e, int32_t t, int32_t i, float lo, float hi) { return (e && ENG(e)->rackSetMappingRange(t, -1, i, lo, hi)) ? 1 : 0; }
int32_t nota_rack_mapping_curve(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->rackMappingCurve(t, -1, i) : 0; }
int32_t nota_rack_set_mapping_curve(NotaEngine* e, int32_t t, int32_t i, int32_t curve) { return (e && ENG(e)->rackSetMappingCurve(t, -1, i, curve)) ? 1 : 0; }

// Drum Rack per-pad shaping + kit-level swing/humanize (instrument-rack surface, di = -1).
void    nota_rack_set_chain_choke(NotaEngine* e, int32_t t, int32_t c, int32_t g) { if (e) ENG(e)->rackSetChainChoke(t, -1, c, g); }
int32_t nota_rack_chain_choke(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainChoke(t, -1, c) : 0; }
void    nota_rack_set_chain_tune(NotaEngine* e, int32_t t, int32_t c, int32_t st) { if (e) ENG(e)->rackSetChainTune(t, -1, c, st); }
int32_t nota_rack_chain_tune(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainTune(t, -1, c) : 0; }
void    nota_rack_set_chain_decay(NotaEngine* e, int32_t t, int32_t c, float v) { if (e) ENG(e)->rackSetChainDecay(t, -1, c, v); }
float   nota_rack_chain_decay(const NotaEngine* e, int32_t t, int32_t c) { return e ? CENG(e)->rackChainDecay(t, -1, c) : 1.0f; }
void    nota_rack_set_swing(NotaEngine* e, int32_t t, float v) { if (e) ENG(e)->rackSetSwing(t, -1, v); }
float   nota_rack_swing(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackSwing(t, -1) : 0.0f; }
void    nota_rack_set_humanize(NotaEngine* e, int32_t t, float v) { if (e) ENG(e)->rackSetHumanize(t, -1, v); }
float   nota_rack_humanize(const NotaEngine* e, int32_t t) { return e ? CENG(e)->rackHumanize(t, -1) : 0.0f; }

// ===================== Audio Effect Rack (a device in the chain) ===========
int32_t nota_rackdev_chain_count(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackChainCount(t, di) : 0; }
int32_t nota_rackdev_add_chain(NotaEngine* e, int32_t t, int32_t di, int32_t k) { return e ? ENG(e)->rackAddChain(t, di, k) : -1; }
int32_t nota_rackdev_remove_chain(NotaEngine* e, int32_t t, int32_t di, int32_t c) { return (e && ENG(e)->rackRemoveChain(t, di, c)) ? 1 : 0; }
int32_t nota_rackdev_set_chain_instrument(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) { return (e && ENG(e)->rackSetChainInstrument(t, di, c, k)) ? 1 : 0; }
int32_t nota_rackdev_chain_instrument_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainInstrumentKind(t, di, c) : -2; }
const char* nota_rackdev_chain_instrument_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainInstrumentName(t, di, c) : ""; }
int32_t nota_rackdev_chain_instrument_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainInstrumentParamCount(t, di, c) : 0; }
const char* nota_rackdev_chain_instrument_param_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) {
    static std::string s; s = e ? CENG(e)->rackChainInstrumentParamName(t, di, c, p) : std::string{}; return s.c_str();
}
float nota_rackdev_chain_instrument_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p) { return e ? CENG(e)->rackChainInstrumentParamGet(t, di, c, p) : 0.0f; }
void nota_rackdev_chain_instrument_param_set(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t p, float v) { if (e) ENG(e)->rackChainInstrumentParamSet(t, di, c, p, v); }

int32_t nota_rackdev_chain_device_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainDeviceCount(t, di, c) : 0; }
int32_t nota_rackdev_add_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t k) { return e ? ENG(e)->rackAddChainDevice(t, di, c, k) : -1; }
int32_t nota_rackdev_remove_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { return (e && ENG(e)->rackRemoveChainDevice(t, di, c, d)) ? 1 : 0; }
int32_t nota_rackdev_move_chain_device(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) { return (e && ENG(e)->rackMoveChainDevice(t, di, c, from, to)) ? 1 : 0; }
const char* nota_rackdev_chain_device_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceName(t, di, c, d) : ""; }
int32_t nota_rackdev_chain_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceBuiltinKind(t, di, c, d) : -1; }
void nota_rackdev_open_chain_device_editor(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { if (e) ENG(e)->rackOpenChainDeviceEditor(t, di, c, d); }
int32_t nota_rackdev_chain_device_param_count(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { return e ? CENG(e)->rackChainDeviceParamCount(t, di, c, d) : 0; }
const char* nota_rackdev_chain_device_param_name(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamName(t, di, c, d, p) : ""; }
float nota_rackdev_chain_device_param_min(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamMin(t, di, c, d, p) : 0.0f; }
float nota_rackdev_chain_device_param_max(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamMax(t, di, c, d, p) : 1.0f; }
float nota_rackdev_chain_device_param_get(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) { return e ? CENG(e)->rackChainDeviceParamGet(t, di, c, d, p) : 0.0f; }
void nota_rackdev_chain_device_param_set(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) { if (e) ENG(e)->rackChainDeviceParamSet(t, di, c, d, p, v); }
void nota_rackdev_set_chain_device_bypassed(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d, int32_t b) { if (e) ENG(e)->rackSetChainDeviceBypassed(t, di, c, d, b != 0); }
int32_t nota_rackdev_chain_device_bypassed(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t d) { return (e && CENG(e)->rackChainDeviceBypassed(t, di, c, d)) ? 1 : 0; }

void  nota_rackdev_set_chain_gain(NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) { if (e) ENG(e)->rackSetChainGain(t, di, c, v); }
void  nota_rackdev_set_chain_pan (NotaEngine* e, int32_t t, int32_t di, int32_t c, float v) { if (e) ENG(e)->rackSetChainPan(t, di, c, v); }
void  nota_rackdev_set_chain_mute(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) { if (e) ENG(e)->rackSetChainMute(t, di, c, b != 0); }
void  nota_rackdev_set_chain_solo(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t b) { if (e) ENG(e)->rackSetChainSolo(t, di, c, b != 0); }
float nota_rackdev_chain_gain(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainGain(t, di, c) : 1.0f; }
float nota_rackdev_chain_pan (const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainPan(t, di, c)  : 0.0f; }
int32_t nota_rackdev_chain_mute(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return (e && CENG(e)->rackChainMute(t, di, c)) ? 1 : 0; }
int32_t nota_rackdev_chain_solo(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return (e && CENG(e)->rackChainSolo(t, di, c)) ? 1 : 0; }

float nota_rackdev_macro_get(const NotaEngine* e, int32_t t, int32_t di, int32_t m) { return e ? CENG(e)->rackMacroGet(t, di, m) : 0.0f; }
void  nota_rackdev_macro_set(NotaEngine* e, int32_t t, int32_t di, int32_t m, float v) { if (e) ENG(e)->rackMacroSet(t, di, m, v); }
int32_t nota_rackdev_add_macro_mapping(NotaEngine* e, int32_t t, int32_t di, int32_t macro, int32_t chain, int32_t tdi, int32_t pi, float lo, float hi) {
    return e ? ENG(e)->rackAddMacroMapping(t, di, macro, chain, tdi, pi, lo, hi) : -1;
}
int32_t nota_rackdev_mapping_count(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackMappingCount(t, di) : 0; }
int32_t nota_rackdev_mapping_info(const NotaEngine* e, int32_t t, int32_t di, int32_t index,
        int32_t* macro, int32_t* chain, int32_t* tdi, int32_t* pi, float* lo, float* hi) {
    if (!e) return 0;
    int32_t mo, ch, dv, px; float rmin, rmax;
    if (!CENG(e)->rackMappingInfo(t, di, index, mo, ch, dv, px, rmin, rmax)) return 0;
    if (macro) *macro = mo; if (chain) *chain = ch; if (tdi) *tdi = dv;
    if (pi) *pi = px; if (lo) *lo = rmin; if (hi) *hi = rmax;
    return 1;
}
int32_t nota_rackdev_remove_mapping(NotaEngine* e, int32_t t, int32_t di, int32_t index) { return (e && ENG(e)->rackRemoveMapping(t, di, index)) ? 1 : 0; }

// ---- Audio Effect Rack: rack-out, routing, meter, zone, macro-name, mapping edit ----
float nota_rackdev_chain_meter(const NotaEngine* e, int32_t t, int32_t di, int32_t c) { return e ? CENG(e)->rackChainMeter(t, di, c) : 0.0f; }
void nota_rackdev_chain_zone(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t* velLo, int32_t* velHi) {
    int32_t kl = 0, kh = 127, vl = 0, vh = 127;
    if (e) CENG(e)->rackChainZone(t, di, c, kl, kh, vl, vh);
    if (velLo) *velLo = vl; if (velHi) *velHi = vh;
}
void nota_rackdev_set_chain_zone(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t velLo, int32_t velHi) { if (e) ENG(e)->rackSetChainZone(t, di, c, 0, 127, velLo, velHi); }
const char* nota_rackdev_macro_name(const NotaEngine* e, int32_t t, int32_t di, int32_t m) { static std::string s; s = e ? CENG(e)->rackMacroName(t, di, m) : std::string{}; return s.c_str(); }
void nota_rackdev_set_macro_name(NotaEngine* e, int32_t t, int32_t di, int32_t m, const char* name) { if (e) ENG(e)->rackSetMacroName(t, di, m, name ? std::string(name) : std::string{}); }
int32_t nota_rackdev_set_mapping_range(NotaEngine* e, int32_t t, int32_t di, int32_t i, float lo, float hi) { return (e && ENG(e)->rackSetMappingRange(t, di, i, lo, hi)) ? 1 : 0; }
int32_t nota_rackdev_mapping_curve(const NotaEngine* e, int32_t t, int32_t di, int32_t i) { return e ? CENG(e)->rackMappingCurve(t, di, i) : 0; }
int32_t nota_rackdev_set_mapping_curve(NotaEngine* e, int32_t t, int32_t di, int32_t i, int32_t curve) { return (e && ENG(e)->rackSetMappingCurve(t, di, i, curve)) ? 1 : 0; }
float nota_rackdev_volume(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackVolume(t, di) : 1.0f; }
void nota_rackdev_set_volume(NotaEngine* e, int32_t t, int32_t di, float v) { if (e) ENG(e)->rackSetVolume(t, di, v); }
int32_t nota_rackdev_mode(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackMode(t, di) : 0; }
void nota_rackdev_set_mode(NotaEngine* e, int32_t t, int32_t di, int32_t m) { if (e) ENG(e)->rackSetMode(t, di, m); }
float nota_rackdev_drywet(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackDryWet(t, di) : 1.0f; }
void nota_rackdev_set_drywet(NotaEngine* e, int32_t t, int32_t di, float v) { if (e) ENG(e)->rackSetDryWet(t, di, v); }
int32_t nota_rackdev_pdc(const NotaEngine* e, int32_t t, int32_t di) { return (e && CENG(e)->rackPdc(t, di)) ? 1 : 0; }
void nota_rackdev_set_pdc(NotaEngine* e, int32_t t, int32_t di, int32_t on) { if (e) ENG(e)->rackSetPdc(t, di, on != 0); }
float nota_rackdev_chain_select(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackChainSelect(t, di) : 0.5f; }
void nota_rackdev_set_chain_select(NotaEngine* e, int32_t t, int32_t di, float v) { if (e) ENG(e)->rackSetChainSelect(t, di, v); }
int32_t nota_rackdev_sel_follow(const NotaEngine* e, int32_t t, int32_t di) { return (e && CENG(e)->rackSelFollow(t, di)) ? 1 : 0; }
void nota_rackdev_set_sel_follow(NotaEngine* e, int32_t t, int32_t di, int32_t on) { if (e) ENG(e)->rackSetSelFollow(t, di, on != 0); }
float nota_rackdev_live_selector(const NotaEngine* e, int32_t t, int32_t di) { return e ? CENG(e)->rackLiveSelector(t, di) : 0.5f; }

} // extern "C"
