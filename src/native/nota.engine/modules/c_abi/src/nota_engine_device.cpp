// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — hosted plugins & device chain (M3): load instrument/effect, device
// CRUD & params, editor windows, plugin state, bypass, PDC latency, kind/id
// introspection (M7-6), hosted-plugin parameters (M9-B), plus built-in FX & PDC self-tests.

#include "nota/nota_engine.h"
#include "nota/engine/Types.h"
#include "nota/engine/Engine.h"
#include "nota_engine_internal.h"

#include "nota/devices/Register.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/primitives/CompensationDelay.h"
#include "nota/dsp/primitives/SpectrumAnalyzer.h"
#include "nota/core/EditorKeys.h"
#include "nota/pluginhost/PluginHostModule.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>
#include <utility>
#include <vector>

extern "C" {

// ---- Hosted plugins (M3-3) -------------------------------------------------

int32_t nota_engine_add_plugin_instrument_track(NotaEngine* e, int32_t catalog_index) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addPluginInstrument(catalog_index).id();
    return id > 0 ? id : 0;
}

NotaResult nota_track_set_instrument_plugin(NotaEngine* e, int32_t track_id, int32_t catalog_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().setPluginInstrument(catalog_index)
               ? NOTA_OK
               : NOTA_ERR_UNKNOWN;
}

NotaResult nota_track_set_builtin_instrument(NotaEngine* e, int32_t track_id, int32_t kind) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().setBuiltinInstrument(kind)
               ? NOTA_OK
               : NOTA_ERR_UNKNOWN;
}

int32_t nota_track_add_effect_plugin(NotaEngine* e, int32_t track_id, int32_t catalog_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().addPlugin(catalog_index) : -1;
}

// ---- Deferred plugin loads (#979, specs/113) -------------------------------

int32_t nota_engine_plugin_loads_service(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().servicePluginLoads() : 0;
}

int32_t nota_engine_plugin_loads_wait(NotaEngine* e, int32_t timeout_ms) {
    return e ? nota::c_abi::eng(e).diagnostics().waitPluginLoads(timeout_ms) : 0;
}

int32_t nota_device_plugin_load_state(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginLoadState(nullptr, nullptr)
             : 0;
}

int32_t nota_device_plugin_load_reason_code(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    int32_t code = 0;
    if (e) nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginLoadState(&code, nullptr);
    return code;
}

const char* nota_device_plugin_load_reason_arg(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    std::string s;
    if (e) nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginLoadState(nullptr, &s);
    return nota::c_abi::cacheThreadString(std::move(s));
}

NotaResult nota_device_plugin_load_action(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t action) {
    if (!e || action < 1 || action > 3) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).pluginLoadAction(action)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

void nota_pluginhost_autoload_scope(int32_t on) {
    nota::pluginhost::sharedPluginHost().setPluginAutoloadScope(on != 0);
}

const char* nota_pluginhost_frozen_notice(void) {
    return nota::c_abi::cacheThreadString(nota::pluginhost::sharedPluginHost().takeFrozenPluginNotice());
}

NotaResult nota_pluginhost_open_test_window(void) {
    return nota::pluginhost::openTestWindow() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_pluginhost_pump(void) {
    return nota::pluginhost::pumpMessageLoop();
}

int32_t nota_clip_audio_mono(
    const NotaEngine* e, int32_t track_id, int32_t clip_index, float* out, int32_t max_frames, double* out_sr) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .clip(clip_index)
                   .audioMono(out, max_frames, out_sr)
             : 0;
}

int32_t nota_track_device_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().count() : 0;
}

int32_t nota_track_add_builtin_device(NotaEngine* e, int32_t track_id, int32_t kind) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().addBuiltin(kind) : -1;
}

NotaResult nota_track_move_device(NotaEngine* e, int32_t track_id, int32_t from_index, int32_t to_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().move(from_index, to_index)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_remove_device(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().remove(device_index)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_copy_device(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().copy(device_index) ? NOTA_OK
                                                                                                : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_copy_midi_effect(NotaEngine* e, int32_t track_id, int32_t index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().copyMidiEffect(index)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_device_clipboard_kind(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).tracks().master().devices().clipboardKind() : 0;
}

int32_t nota_track_copy_instrument(NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().copyInstrument()) ? 1 : 0;
}

int32_t nota_track_paste_instrument(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().pasteInstrument() : -1;
}

int32_t nota_track_transfer_device(NotaEngine* e, int32_t src_track_id, int32_t family, int32_t index,
    int32_t dst_track_id, int32_t at_index, int32_t keep_source, int32_t* out_flags) {
    if (out_flags) *out_flags = 0;
    if (!e || family < 0 || family > 2) return -1;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), src_track_id)
        .devices()
        .transferDevice(static_cast<nota::engine::DeviceFamily>(family), index, dst_track_id, at_index,
            keep_source != 0, out_flags);
}

int32_t nota_device_clipboard_sidechain(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).tracks().master().devices().clipboardHasSidechain()) ? 1 : 0;
}

int32_t nota_track_paste_device(NotaEngine* e, int32_t track_id, int32_t at_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().paste(at_index) : -1;
}

int32_t nota_track_paste_midi_effect(NotaEngine* e, int32_t track_id, int32_t at_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().pasteMidiEffect(at_index) : -1;
}

int32_t nota_track_duplicate_device(NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().duplicate(device_index) : -1;
}

int32_t nota_track_duplicate_midi_effect(NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().duplicateMidiEffect(index) : -1;
}

int32_t nota_track_wrap_device_in_rack(NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().wrapInRack(device_index) : -1;
}

int32_t nota_track_unwrap_rack(NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().unwrapRack(device_index) : -1;
}

int32_t nota_track_group_devices(NotaEngine* e, int32_t track_id, int32_t midi_first, int32_t midi_count,
    int32_t with_instrument, int32_t fx_first, int32_t fx_count) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .groupDevices(midi_first, midi_count, with_instrument != 0, fx_first, fx_count)
             : -2;
}

int32_t nota_track_ungroup_rack(NotaEngine* e, int32_t track_id, int32_t family, int32_t index, int32_t* out_dropped) {
    if (out_dropped) *out_dropped = 0;
    if (!e || family < 0 || family > 2) return -2;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
        .devices()
        .ungroupRack(static_cast<nota::engine::DeviceFamily>(family), index, out_dropped);
}

int32_t nota_track_copy_devices(NotaEngine* e, int32_t track_id, const int32_t* midi_idx, int32_t midi_n,
    int32_t with_instrument, const int32_t* fx_idx, int32_t fx_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .copyDevices(midi_idx, midi_n, with_instrument != 0, fx_idx, fx_n)
             : -1;
}

int32_t nota_device_clipboard_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).tracks().master().devices().clipboardCount() : 0;
}

int32_t nota_track_paste_devices(
    NotaEngine* e, int32_t track_id, int32_t midi_at, int32_t fx_at, int32_t* out_skipped) {
    if (out_skipped) *out_skipped = 0;
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .pasteDevices(midi_at, fx_at, out_skipped)
             : -1;
}

int32_t nota_midi_effect_cc_dest_path(
    const NotaEngine* e, int32_t t, int32_t i, int32_t* out_chain, int32_t* out_slot) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), t)
                     .devices()
                     .midiEffect(i)
                     .ccDestPath(out_chain, out_slot))
               ? 0
               : -1;
}

int32_t nota_rack_chain_midi_cc_dest(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i,
    int32_t* out_dest_dev, int32_t* out_dest_chain, int32_t* out_dest_slot, int32_t* out_param, float* out_depth) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di)
                     .chain(c)
                     .midiCcDest(i, out_dest_dev, out_dest_chain, out_dest_slot, out_param, out_depth))
               ? 0
               : -1;
}

int32_t nota_rack_chain_midi_set_cc_dest(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t i, int32_t dest_dev,
    int32_t dest_chain, int32_t dest_slot, int32_t param, float depth) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di)
                     .chain(c)
                     .setMidiCcDest(i, dest_dev, dest_chain, dest_slot, param, depth))
               ? 0
               : -1;
}

int32_t nota_rack_chain_sidechain(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot, int32_t* out_src,
    int32_t* out_pre, float* out_gain, float* out_mix) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di)
                     .chain(c)
                     .sidechain(slot, out_src, out_pre, out_gain, out_mix))
               ? 0
               : -1;
}

int32_t nota_rack_chain_set_sidechain(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot, int32_t src, int32_t pre, float gain, float mix) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setSidechain(slot, src, pre, gain, mix)
             : -1;
}

int32_t nota_rack_chain_accepts_sidechain(const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).acceptsSidechain(slot)) ? 1 : 0;
}

int32_t nota_midi_effect_set_cc_dest_path(NotaEngine* e, int32_t t, int32_t i, int32_t chain, int32_t slot) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).setCcDestPath(chain, slot))
               ? 0
               : -1;
}

const char* nota_device_name(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).name() : std::string{});
}

int32_t nota_device_param_count(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).paramCount() : 0;
}

const char* nota_device_param_name(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                                                  .devices()
                                                  .device(device_index)
                                                  .parameter(param_index)
                                                  .name()
                                            : std::string{});
}

float nota_device_param_min(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .device(device_index)
                   .parameter(param_index)
                   .min()
             : 0.0f;
}

float nota_device_param_max(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .device(device_index)
                   .parameter(param_index)
                   .max()
             : 1.0f;
}

float nota_device_get_param(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .device(device_index)
                   .parameter(param_index)
                   .get()
             : 0.0f;
}

NotaResult nota_device_set_param(
    NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index, float value) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
        .devices()
        .device(device_index)
        .parameter(param_index)
        .set(value);
    return NOTA_OK;
}

float nota_device_gain_reduction(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).gainReduction()
             : 0.0f;
}

float nota_device_param_default(const NotaEngine* e, int32_t t, int32_t d, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().device(d).parameter(p).defaultValue() : 0.0f;
}

float nota_instrument_param_default(const NotaEngine* e, int32_t t, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().instrument().parameter(p).defaultValue()
             : 0.0f;
}

float nota_midi_effect_param_default(const NotaEngine* e, int32_t t, int32_t i, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).parameter(p).defaultValue()
             : 0.0f;
}

float nota_builtin_device_param_default(const NotaEngine* e, int32_t kind, int32_t p) {
    return e ? nota::c_abi::ceng(e).tracks().master().devices().builtinParamDefault(kind, p) : 0.0f;
}

int32_t nota_device_scope(
    const NotaEngine* e, int32_t track_id, int32_t device_index, float* out, int32_t max_samples) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .device(device_index)
                   .scope(out, max_samples)
             : 0;
}

int32_t nota_engine_reverb_wrap_selftest(NotaEngine* e) {
    return (e && nota::devices::reverbCombWrapSelfTest()) ? 1 : 0;
}

void nota_device_action(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t id, int32_t iarg, float farg) {
    if (e)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().device(device_index).action(id, iarg, farg);
}

int32_t nota_device_layer_wave(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t layer, float* out, int32_t max_samples) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .device(device_index)
                   .layerWave(layer, out, max_samples)
             : 0;
}

int32_t nota_device_get_state(const NotaEngine* e, int32_t track_id, int32_t device_index, uint8_t* out, int32_t cap) {
    return (e && device_index >= 0) ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                                          .devices()
                                          .device(device_index)
                                          .getState(out, cap)
                                    : 0;
}

void nota_device_set_state(NotaEngine* e, int32_t track_id, int32_t device_index, const uint8_t* data, int32_t size) {
    if (e && device_index >= 0)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().device(device_index).setState(data, size);
}

NotaResult nota_device_load_file(NotaEngine* e, int32_t track_id, int32_t device_index, const char* path) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .device(device_index)
                   .loadFile(std::string(path))
               ? NOTA_OK
               : NOTA_ERR_UNKNOWN;
}

int32_t nota_device_text(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).text(id), out, cap);
}

int32_t nota_eq_set_band_text(
    NotaEngine* e, int32_t track_id, int32_t device_index, int32_t band, const char* label, const char* note) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                     .devices()
                     .device(device_index)
                     .setEqBandText(band, label ? label : "", note ? note : ""))
               ? 1
               : 0;
}

int32_t nota_eq_band_text(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t band, int32_t which, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).eqBandText(band, which),
        out, cap);
}

// ---- MIDI effects ----------------------------------------------------------

int32_t nota_track_add_midi_effect(NotaEngine* e, int32_t t, int32_t kind) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().addBuiltinMidiEffect(kind) : -1;
}

int32_t nota_track_midi_effect_count(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffectCount() : 0;
}

NotaResult nota_track_move_midi_effect(NotaEngine* e, int32_t t, int32_t from, int32_t to) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().moveMidiEffect(from, to) ? NOTA_OK
                                                                                               : NOTA_ERR_UNKNOWN;
}

NotaResult nota_track_remove_midi_effect(NotaEngine* e, int32_t t, int32_t i) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().removeMidiEffect(i) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_midi_effect_kind(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).kind() : -1;
}

int32_t nota_midi_effect_last_in(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).midiLastIn() : -1;
}

int32_t nota_midi_effect_last_out(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).midiLastOut() : -1;
}

int32_t nota_midi_effect_scope(const NotaEngine* e, int32_t t, int32_t i, float* out, int32_t max_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).scope(out, max_n) : 0;
}

int32_t nota_midi_effect_get_state(const NotaEngine* e, int32_t t, int32_t i, uint8_t* out, int32_t cap) {
    if (!e) return 0;
    const auto st = nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).midiGetState();
    return nota::c_abi::copyByteVector(st, out, cap);
}

NotaResult nota_midi_effect_set_state(
    NotaEngine* e, int32_t t, int32_t i, const uint8_t* data, int32_t size, int32_t checkpoint) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), t)
                   .devices()
                   .midiEffect(i)
                   .setState(data, size, checkpoint != 0)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

void nota_midi_effect_command(NotaEngine* e, int32_t t, int32_t i, int32_t cmd) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).midiCommand(cmd);
}

const char* nota_midi_effect_name(const NotaEngine* e, int32_t t, int32_t i) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).name() : std::string{});
}

int32_t nota_midi_effect_param_count(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).paramCount() : 0;
}

const char* nota_midi_effect_param_name(const NotaEngine* e, int32_t t, int32_t i, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).parameter(p).name()
          : std::string{});
}

float nota_midi_effect_param_min(const NotaEngine* e, int32_t t, int32_t i, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).parameter(p).min() : 0.0f;
}

float nota_midi_effect_param_max(const NotaEngine* e, int32_t t, int32_t i, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).parameter(p).max() : 1.0f;
}

float nota_midi_effect_get_param(const NotaEngine* e, int32_t t, int32_t i, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).parameter(p).get() : 0.0f;
}

NotaResult nota_midi_effect_set_param(NotaEngine* e, int32_t t, int32_t i, int32_t p, float v) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).parameter(p).set(v);
    return NOTA_OK;
}

void nota_midi_effect_set_bypassed(NotaEngine* e, int32_t t, int32_t i, int32_t b) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).setBypassed(b != 0);
}

int32_t nota_midi_effect_bypassed(const NotaEngine* e, int32_t t, int32_t i) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).bypassed()) ? 1 : 0;
}

void nota_midi_effect_set_folded(NotaEngine* e, int32_t t, int32_t i, int32_t f) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).setFolded(f != 0);
}

int32_t nota_midi_effect_folded(const NotaEngine* e, int32_t t, int32_t i) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).folded()) ? 1 : 0;
}

void nota_midi_effect_set_cc_dest(NotaEngine* e, int32_t t, int32_t i, int32_t dev, int32_t param) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).setCcDest(dev, param);
}

void nota_midi_effect_set_cc_depth(NotaEngine* e, int32_t t, int32_t i, float d) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().midiEffect(i).setCcDepth(d);
}

int32_t nota_midi_effect_cc_dest_device(const NotaEngine* e, int32_t t, int32_t i) {
    if (!e) return -2;
    const auto me = nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i);
    return me.isValid() ? me.ccDestDevice() : -2;
}

int32_t nota_midi_effect_cc_dest_param(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).ccDestParam() : -1;
}

float nota_midi_effect_cc_depth(const NotaEngine* e, int32_t t, int32_t i) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().midiEffect(i).ccDepth() : 0.0f;
}

void nota_device_set_sidechain_source(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t source_track_id) {
    if (e)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
            .devices()
            .device(device_index)
            .setSidechainSource(source_track_id);
}

int32_t nota_device_sidechain_source(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).sidechainSource()
             : -1;
}

int32_t nota_device_accepts_sidechain(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                     .devices()
                     .device(device_index)
                     .acceptsSidechain())
               ? 1
               : 0;
}

void nota_track_set_instrument_sidechain_source(NotaEngine* e, int32_t track_id, int32_t source_track_id) {
    if (e)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
            .devices()
            .instrument()
            .setSidechainSource(source_track_id);
}

int32_t nota_track_instrument_sidechain_source(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().sidechainSource() : -1;
}

int32_t nota_track_instrument_accepts_sidechain(const NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().acceptsSidechain())
               ? 1
               : 0;
}

void nota_device_set_sidechain_gain(NotaEngine* e, int32_t track_id, int32_t device_index, float gain_db) {
    if (e)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
            .devices()
            .device(device_index)
            .setSidechainGain(gain_db);
}

float nota_device_sidechain_gain(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).sidechainGain()
             : 0.0f;
}

void nota_device_set_sidechain_mix(NotaEngine* e, int32_t track_id, int32_t device_index, float mix) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().device(device_index).setSidechainMix(mix);
}

float nota_device_sidechain_mix(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).sidechainMix()
             : 1.0f;
}

void nota_device_set_sidechain_tap_pre(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t pre) {
    if (e)
        nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().device(device_index).setSidechainTapPre(pre);
}

int32_t nota_device_sidechain_tap_pre(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).sidechainTapPre()
             : 0;
}

// ---- Sidechain sub-sources (specs/050) -------------------------------------

int32_t nota_track_part_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().partCount() : 0;
}

int32_t nota_track_part_name(const NotaEngine* e, int32_t track_id, int32_t index, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().partName(index), out, cap);
}

int32_t nota_device_set_sidechain_key(
    NotaEngine* e, int32_t track_id, int32_t device_index, int32_t source_track_id, int32_t part) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .device(device_index)
                   .setSidechainKey(source_track_id, part)
             : -1;
}

int32_t nota_device_sidechain_key(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t* out_source, int32_t* out_part) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                     .devices()
                     .device(device_index)
                     .sidechainKey(out_source, out_part))
               ? 0
               : -1;
}

int32_t nota_track_set_instrument_sidechain_key(
    NotaEngine* e, int32_t track_id, int32_t source_track_id, int32_t part) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .instrument()
                   .setSidechainKey(source_track_id, part)
             : -1;
}

int32_t nota_track_instrument_sidechain_key(
    const NotaEngine* e, int32_t track_id, int32_t* out_source, int32_t* out_part) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                     .devices()
                     .instrument()
                     .sidechainKey(out_source, out_part))
               ? 0
               : -1;
}

int32_t nota_rack_chain_sidechain_part(
    const NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot, int32_t* out_part) {
    return (e && nota::c_abi::rackHandle(nota::c_abi::ceng(e), t, di).chain(c).sidechainPart(slot, out_part)) ? 0 : -1;
}

int32_t nota_rack_chain_set_sidechain_key(NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot, int32_t src,
    int32_t part, int32_t pre, float gain, float mix) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setKey(slot, src, part, pre, gain, mix)
             : -1;
}

int32_t nota_rack_chain_set_sidechain_part(
    NotaEngine* e, int32_t t, int32_t di, int32_t c, int32_t slot, int32_t part) {
    return e ? nota::c_abi::rackHandle(nota::c_abi::eng(e), t, di).chain(c).setSidechainPart(slot, part) : -1;
}

int32_t nota_sidechain_route_capacity(const NotaEngine* e) {
    return e ? nota::engine::kSidechainRoutes : 0;
}

int32_t nota_sidechain_routes_free(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().sidechainRoutesFree() : 0;
}

int32_t nota_sidechain_route_in_use(const NotaEngine* e, int32_t source_track_id, int32_t part) {
    return (e && nota::c_abi::ceng(e).diagnostics().sidechainRouteInUse(source_track_id, part)) ? 1 : 0;
}

int32_t nota_spectrum_analyze_offline(
    const float* mono, int32_t n, double sample_rate, int32_t hop, double dt, float* curves, int32_t max_frames) {
    using nota::dsp::SpectrumAnalyzer;
    if (!mono || !curves || n <= 0 || hop <= 0 || max_frames <= 0 || sample_rate <= 0.0) return 0;
    SpectrumAnalyzer a;
    a.prepare(sample_rate);
    const int fft = a.fftSize();
    if (n < fft) return 0;
    uint32_t size = 1;
    while (size < static_cast<uint32_t>(n)) size <<= 1;
    std::vector<float> ring(size, 0.0f);
    std::copy(mono, mono + n, ring.begin());
    int32_t frames = 0;
    for (int64_t w = fft; w <= n && frames < max_frames; w += hop, ++frames) {
        a.analyse(ring.data(), size - 1, static_cast<uint32_t>(w), frames == 0 ? 0.0 : dt, false);
        std::copy(a.curve(), a.curve() + SpectrumAnalyzer::kPoints, curves + frames * SpectrumAnalyzer::kPoints);
    }
    return frames;
}

int32_t nota_fx_selftest(void) {
    return nota::devices::runBuiltinFxSelfTest() ? 1 : 0;
}

NotaResult nota_plugin_open_editor(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).openEditor();
    return NOTA_OK;
}

NotaResult nota_plugin_close_editor(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).closeEditor();
    return NOTA_OK;
}

int32_t nota_plugin_editor_is_open(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).editorOpen()) ? 1 : 0;
}

void nota_editor_keys_set_claimed(const int32_t* chars, int32_t count) {
    nota::core::editorkeys::setClaimed(chars, count);
}

int32_t nota_editor_keys_drain(int32_t* out_chars, uint8_t* out_down, int32_t max) {
    if (!out_chars || !out_down || max <= 0) return 0;
    return nota::core::editorkeys::drain(out_chars, out_down, max);
}

void nota_editor_keys_push(int32_t code, int32_t down) {
    nota::core::editorkeys::push(code, down != 0);
}

int32_t nota_plugin_get_state(
    const NotaEngine* e, int32_t track_id, int32_t device_index, uint8_t* out, int32_t capacity) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginGetState(out, capacity)
             : 0;
}

NotaResult nota_plugin_set_state(
    NotaEngine* e, int32_t track_id, int32_t device_index, const uint8_t* data, int32_t size) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).pluginSetState(data, size)
               ? NOTA_OK
               : NOTA_ERR_UNKNOWN;
}

int32_t nota_plugin_preview_get(
    const NotaEngine* e, int32_t track_id, int32_t device_index, uint8_t* out, int32_t capacity) {
    if (!e) return 0;
    const auto p = nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).editorPreview();
    if (p.empty()) return 0;
    const auto n = static_cast<int32_t>(p.jpeg->size());
    if (out && capacity > 0) std::memcpy(out, p.jpeg->data(), static_cast<size_t>(std::min(n, capacity)));
    return n;
}

NotaResult nota_plugin_preview_set(
    NotaEngine* e, int32_t track_id, int32_t device_index, const uint8_t* data, int32_t size) {
    constexpr int32_t kMaxPreviewBytes = 4 * 1024 * 1024;
    if (!e || size < 0 || size > kMaxPreviewBytes || (size > 0 && !data)) return NOTA_ERR_INVALID_ARG;
    auto p =
        size > 0 ? nota::dsp::EditorPreview::make(std::vector<uint8_t>(data, data + size)) : nota::dsp::EditorPreview{};
    return nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).setEditorPreview(std::move(p))
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_plugin_preview_serial(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? static_cast<int32_t>(
                   nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).editorPreview().serial)
             : 0;
}

NotaResult nota_track_set_device_bypassed(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t bypassed) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).setBypassed(bypassed != 0);
    return NOTA_OK;
}

int32_t nota_track_device_bypassed(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).bypassed()) ? 1 : 0;
}

NotaResult nota_plugin_set_sleep_mode(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t mode) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).setSleepMode(mode)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_plugin_sleep_mode(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).sleepMode()
             : NOTA_PLUGIN_SLEEP_AUTO;
}

int32_t nota_engine_plugin_editor_sleep_selftest(int32_t catalog_index) {
    return nota::Engine::Diagnostics::pluginEditorSleepSelftest(catalog_index);
}

void nota_track_set_device_folded(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t folded) {
    if (e) nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).setFolded(folded != 0);
}

int32_t nota_track_device_folded(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).folded()) ? 1 : 0;
}

int32_t nota_track_latency_samples(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).latencySamples() : 0;
}

int32_t nota_pdc_selftest(void) {
    nota::dsp::CompensationDelay d;
    constexpr int32_t D = 100;
    d.setDelay(D);
    std::vector<float> buf(static_cast<size_t>(2 * D) * 2, 0.0f);
    buf[0] = 1.0f;
    buf[1] = 1.0f;
    d.process(buf.data(), 2 * D);
    const bool ok = std::fabs(buf[0]) < 1e-6f && std::fabs(buf[1]) < 1e-6f &&
                    std::fabs(buf[static_cast<size_t>(D) * 2] - 1.0f) < 1e-6f &&
                    std::fabs(buf[static_cast<size_t>(D) * 2 + 1] - 1.0f) < 1e-6f;
    return ok ? 1 : 0;
}

// ---- Plugin / device kind & id introspection (M7-6) ------------------------

int64_t nota_track_instrument_id(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().deviceId() : 0;
}

int32_t nota_track_instrument_kind(const NotaEngine* e, int32_t track_id) {
    if (!e) return -2;
    const auto inst = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument();
    return inst.isValid() ? inst.kind() : -2;
}

int32_t nota_track_instrument_supports_mpe(const NotaEngine* e, int32_t track_id) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().supportsMpe()) ? 1 : 0;
}

int32_t nota_track_device_builtin_kind(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).kind() : -1;
}

int64_t nota_track_device_id(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).deviceId() : 0;
}

const char* nota_track_instrument_plugin_id(const NotaEngine* e, int32_t track_id) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().pluginId() : std::string{});
}

const char* nota_track_device_plugin_id(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().device(device_index).pluginId()
          : std::string{});
}

// ---- Hosted-plugin parameters (M9-B) ---------------------------------------

int32_t nota_plugin_param_count(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginParamCount() : 0;
}

const char* nota_plugin_param_id(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginParameter(param_index).id()
          : std::string{});
}

int32_t nota_plugin_param_index_from_v1(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index)
                   .pluginParameter(param_index)
                   .indexFromV1()
             : param_index;
}

const char* nota_plugin_param_name(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginParameter(param_index).name()
          : std::string{});
}

const char* nota_plugin_param_text(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index).pluginParameter(param_index).text()
          : std::string{});
}

int32_t nota_plugin_param_steps(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index)
                   .pluginParameter(param_index)
                   .steps()
             : 0;
}

const char* nota_plugin_param_step_text(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index, int32_t step) {
    return nota::c_abi::cacheThreadString(e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index)
                                                  .pluginParameter(param_index)
                                                  .stepText(step)
                                            : std::string{});
}

float nota_plugin_param_default(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index)
                   .pluginParameter(param_index)
                   .defaultValue()
             : 0.0f;
}

float nota_plugin_param_get(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::ceng(e), track_id, device_index)
                   .pluginParameter(param_index)
                   .get()
             : 0.0f;
}

NotaResult nota_plugin_param_set(
    NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index, float normalized) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).pluginParameter(param_index).set(normalized);
    return NOTA_OK;
}

int32_t nota_plugin_last_touched_param(NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? nota::c_abi::deviceOrInst(nota::c_abi::eng(e), track_id, device_index).lastTouchedPluginParam() : -1;
}

} // extern "C"
