// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — tracks, sends/returns, built-in instrument tracks, Rhythm kits,
// Mosaic/Grain/Sampler instrument queries, and Aurora custom wavetables.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"
#include "nota_engine_internal.h"

#include "nota/devices/Register.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <string>
#include <string_view>

namespace {

int32_t addInstrumentOrZero(NotaEngine* e, std::string_view slug) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addInstrument(slug).id();
    return id > 0 ? id : 0;
}

} // namespace

extern "C" {

// ---- Tracks & clips --------------------------------------------------------

int32_t nota_engine_add_audio_track(NotaEngine* e) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addAudio().id();
    return id > 0 ? id : 0;
}

int32_t nota_track_add_audio_clip(NotaEngine* e, int32_t track_id, const char* path, double start_beat) {
    if (!e || !path) return -1;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addAudioClip(std::string(path), start_beat);
}

NotaResult nota_track_set_volume(NotaEngine* e, int32_t id, float v) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), id).setVolume(v);
    return NOTA_OK;
}

NotaResult nota_track_set_pan(NotaEngine* e, int32_t id, float p) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), id).setPan(p);
    return NOTA_OK;
}

NotaResult nota_track_set_mute(NotaEngine* e, int32_t id, int32_t m) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), id).setMute(m != 0);
    return NOTA_OK;
}

NotaResult nota_track_set_solo(NotaEngine* e, int32_t id, int32_t s) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), id).setSolo(s != 0);
    return NOTA_OK;
}

int32_t nota_engine_track_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).tracks().count() : 0;
}

int32_t nota_engine_duplicate_track(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::eng(e).tracks().duplicate(track_id) : -1;
}

int32_t nota_engine_add_track_like(NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::eng(e).tracks().addLike(track_id) : -1;
}

NotaResult nota_engine_remove_track(NotaEngine* e, int32_t track_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().remove(track_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_engine_move_track(NotaEngine* e, int32_t track_id, int32_t to_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().move(track_id, to_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_engine_add_group_track(NotaEngine* e) {
    if (!e) return -1;
    const auto tr = nota::c_abi::eng(e).tracks().addGroup();
    return tr.isValid() ? tr.id() : -1;
}

int32_t nota_engine_create_group(NotaEngine* e, const int32_t* track_ids, int32_t n) {
    return e ? nota::c_abi::eng(e).tracks().createGroup(track_ids, n) : -1;
}

NotaResult nota_engine_ungroup(NotaEngine* e, int32_t group_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().ungroup(group_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_engine_set_track_group(NotaEngine* e, int32_t track_id, int32_t group_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).setGroup(group_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_engine_duplicate_tracks(
    NotaEngine* e, const int32_t* track_ids, int32_t n, int32_t* out_ids, int32_t cap) {
    return e ? nota::c_abi::eng(e).tracks().duplicateMany(track_ids, n, out_ids, cap) : -1;
}

NotaResult nota_engine_remove_tracks(NotaEngine* e, const int32_t* track_ids, int32_t n) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).tracks().removeMany(track_ids, n) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

// ---- Send / return buses (M6-1) --------------------------------------------

int32_t nota_engine_add_return_track(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).mixer().addReturn() : 0;
}

NotaResult nota_track_set_send(NotaEngine* e, int32_t id, int32_t bus, float level) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), id).setSend(bus, level);
    return NOTA_OK;
}

float nota_track_get_send(const NotaEngine* e, int32_t id, int32_t bus) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), id).send(bus) : 0.0f;
}

int32_t nota_engine_return_track_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().returnCount() : 0;
}

int32_t nota_track_return_index(const NotaEngine* e, int32_t id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), id).returnIndex() : -1;
}

// ---- Built-in instrument tracks --------------------------------------------

int32_t nota_engine_add_instrument_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "synth");
}
int32_t nota_engine_add_physical_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "physical");
}
int32_t nota_engine_add_wavetable_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "aurora");
}
int32_t nota_engine_add_volt_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "volt");
}
int32_t nota_engine_add_bass_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "bass");
}
int32_t nota_engine_add_pendulum_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "pendulum");
}
int32_t nota_engine_add_operator_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "operator");
}
int32_t nota_engine_add_grain_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "grain");
}
int32_t nota_engine_add_flux_synth_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "flux");
}
int32_t nota_engine_add_rhythm_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "rhythm");
}
int32_t nota_engine_add_monolith_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "monolith");
}
int32_t nota_engine_add_pentad_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "pentad");
}
int32_t nota_engine_add_consort_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "consort");
}
int32_t nota_engine_add_keys_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "keys");
}
int32_t nota_engine_add_mosaic_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "mosaic");
}
int32_t nota_engine_add_twin_track(NotaEngine* e) {
    return addInstrumentOrZero(e, "twin");
}

int32_t nota_track_mosaic_set_program(NotaEngine* e, int32_t track_id, const char* text, int32_t checkpoint) {
    return (e && text &&
               nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .mosaicSetProgram(std::string(text), checkpoint != 0))
               ? 1
               : 0;
}

int32_t nota_track_mosaic_program(const NotaEngine* e, int32_t track_id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyRawBytes(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().mosaicProgram(), out, cap);
}

int32_t nota_track_mosaic_status(const NotaEngine* e, int32_t track_id, NotaMosaicStatus* out) {
    if (!e || !out) return 0;
    nota::engine::MosaicStatus st{};
    if (!nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().mosaicStatus(st)) return 0;
    *out = {
        st.state, st.filesDone, st.filesTotal, st.missing, st.zones, st.serial, st.diskDone, st.diskTotal, st.ramBytes};
    return 1;
}

int64_t nota_track_mosaic_zone_sample_id(const NotaEngine* e, int32_t track_id, int32_t zone) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().mosaicZoneSampleId(zone) : 0;
}

void nota_path_root_set(const char* name, const char* path) {
    if (name && path) nota::devices::setMosaicRoot(name, path);
}

void nota_track_instrument_action(NotaEngine* e, int32_t track_id, int32_t id, int32_t iarg, float farg) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().instrument().action(id, iarg, farg);
}

int32_t nota_track_instrument_set_state_undoable(NotaEngine* e, int32_t track_id, const uint8_t* data, int32_t size) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                     .devices()
                     .instrument()
                     .setInstrumentStateUndoable(-1, data, size))
               ? 1
               : 0;
}

int32_t nota_track_rhythm_set_voice_sample(NotaEngine* e, int32_t track_id, int32_t voice, const char* path) {
    return (e && path &&
               nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .setRhythmVoiceSample(voice, std::string(path)))
               ? 1
               : 0;
}

int32_t nota_track_rhythm_voice_info(const NotaEngine* e, int32_t track_id, int32_t voice, NotaSamplerInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SamplerInfo info{};
    if (!nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().rhythmVoiceInfo(voice, info)) return 0;
    *out = {info.sampleId, info.rootNote, info.loop};
    return 1;
}

int32_t nota_track_rhythm_voice_source(const NotaEngine* e, int32_t track_id, int32_t voice) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().rhythmVoiceSource(voice) : 0;
}

int32_t nota_rhythm_voice_device_count(const NotaEngine* e, int32_t t, int32_t v) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceCount(v) : 0;
}

int32_t nota_rhythm_add_voice_device(NotaEngine* e, int32_t t, int32_t v, int32_t kind) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmAddVoiceDevice(v, kind) : -1;
}

int32_t nota_rhythm_remove_voice_device(NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmRemoveVoiceDevice(v, d)) ? 1 : 0;
}

int32_t nota_rhythm_move_voice_device(NotaEngine* e, int32_t t, int32_t v, int32_t from, int32_t to) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmMoveVoiceDevice(v, from, to)) ? 1 : 0;
}

const char* nota_rhythm_voice_device_name(const NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceName(v, d) : std::string{});
}

int32_t nota_rhythm_voice_device_builtin_kind(const NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceBuiltinKind(v, d) : -1;
}

int32_t nota_rhythm_voice_device_param_count(const NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceParamCount(v, d) : 0;
}

const char* nota_rhythm_voice_device_param_name(const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t p) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceParamName(v, d, p)
          : std::string{});
}

float nota_rhythm_voice_device_param_min(const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceParamMin(v, d, p) : 0.0f;
}

float nota_rhythm_voice_device_param_max(const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceParamMax(v, d, p) : 1.0f;
}

float nota_rhythm_voice_device_param_get(const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t p) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceParamGet(v, d, p) : 0.0f;
}

void nota_rhythm_voice_device_param_set(NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t p, float value) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmVoiceDeviceParamSet(v, d, p, value);
}

int32_t nota_rhythm_voice_device_bypassed(const NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceBypassed(v, d)) ? 1 : 0;
}

void nota_rhythm_set_voice_device_bypassed(NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t bypassed) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmSetVoiceDeviceBypassed(v, d, bypassed != 0);
}

float nota_rhythm_voice_device_gain_reduction(const NotaEngine* e, int32_t t, int32_t v, int32_t d) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceGainReduction(v, d) : 0.0f;
}

int32_t nota_rhythm_voice_device_scope(const NotaEngine* e, int32_t t, int32_t v, int32_t d, float* out, int32_t n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceScope(v, d, out, n) : 0;
}

int32_t nota_rhythm_voice_device_layer_wave(
    const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t layer, float* out, int32_t n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t)
                   .devices()
                   .rhythmVoiceDeviceLayerWave(v, d, layer, out, n)
             : 0;
}

int32_t nota_rhythm_voice_device_text(
    const NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t id, char* out, int32_t cap) {
    if (!e) return 0;
    return nota::c_abi::copyCStringNul(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmVoiceDeviceText(v, d, id), out, cap);
}

void nota_rhythm_voice_device_action(
    NotaEngine* e, int32_t t, int32_t v, int32_t d, int32_t id, int32_t iarg, float farg) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmVoiceDeviceAction(v, d, id, iarg, farg);
}

const char* nota_rhythm_kit_name(const NotaEngine* e, int32_t t) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmKitName() : std::string{});
}

void nota_rhythm_set_kit_name(NotaEngine* e, int32_t t, const char* name) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().setRhythmKitName(name ? name : "");
}

const char* nota_rhythm_macro_name(const NotaEngine* e, int32_t t, int32_t m) {
    return nota::c_abi::cacheThreadString(
        e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmMacroName(m) : std::string{});
}

void nota_rhythm_set_macro_name(NotaEngine* e, int32_t t, int32_t m, const char* name) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().setRhythmMacroName(m, name ? name : "");
}

int32_t nota_rhythm_add_macro_mapping(
    NotaEngine* e, int32_t t, int32_t m, int32_t v, int32_t d, int32_t p, float lo, float hi) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmAddMacroMapping(m, v, d, p, lo, hi)
             : -1;
}

int32_t nota_rhythm_macro_mapping_count(const NotaEngine* e, int32_t t) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmMacroMappingCount() : 0;
}

int32_t nota_rhythm_macro_mapping_info(const NotaEngine* e, int32_t t, int32_t i, int32_t* m, int32_t* v, int32_t* d,
    int32_t* p, float* lo, float* hi, int32_t* curve) {
    int32_t a = 0, b = 0, c = -1, pp = 0, cv = 0;
    float l = 0.0f, h = 1.0f;
    if (!e ||
        !nota::c_abi::trackHandle(nota::c_abi::ceng(e), t).devices().rhythmMacroMappingInfo(i, a, b, c, pp, l, h, cv))
        return 0;
    if (m) *m = a;
    if (v) *v = b;
    if (d) *d = c;
    if (p) *p = pp;
    if (lo) *lo = l;
    if (hi) *hi = h;
    if (curve) *curve = cv;
    return 1;
}

int32_t nota_rhythm_remove_macro_mapping(NotaEngine* e, int32_t t, int32_t i) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmRemoveMacroMapping(i)) ? 1 : 0;
}

int32_t nota_rhythm_set_macro_mapping_range(NotaEngine* e, int32_t t, int32_t i, float lo, float hi) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmSetMacroMappingRange(i, lo, hi)) ? 1
                                                                                                                   : 0;
}

int32_t nota_rhythm_set_macro_mapping_curve(NotaEngine* e, int32_t t, int32_t i, int32_t curve) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmSetMacroMappingCurve(i, curve)) ? 1
                                                                                                                  : 0;
}

void nota_rhythm_clear_macros(NotaEngine* e, int32_t t) {
    if (e) nota::c_abi::trackHandle(nota::c_abi::eng(e), t).devices().rhythmClearMacros();
}

int32_t nota_track_set_grain_sample(NotaEngine* e, int32_t track_id, const char* path, int32_t root) {
    return (e && path &&
               nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .setGrainSample(std::string(path), root))
               ? 1
               : 0;
}

int32_t nota_track_set_grain_root(NotaEngine* e, int32_t track_id, int32_t root) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().setGrainRoot(root)) ? 1 : 0;
}

int32_t nota_track_grain_info(const NotaEngine* e, int32_t track_id, NotaSamplerInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SamplerInfo info{};
    if (!nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().grainInfo(info)) return 0;
    *out = {info.sampleId, info.rootNote, info.loop};
    return 1;
}

int32_t nota_track_grain_play_positions(const NotaEngine* e, int32_t track_id, float* out, int32_t max_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().grainPlayPositions(out, max_n) : 0;
}

int32_t nota_track_active_voices(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().activeVoices() : -1;
}

int32_t nota_track_held_notes(const NotaEngine* e, int32_t track_id, int32_t* out, int32_t max_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().heldNotes(out, max_n)
             : 0;
}

int32_t nota_track_instrument_scope(const NotaEngine* e, int32_t track_id, float* out, int32_t max_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().instrument().scope(out, max_n) : 0;
}

int32_t nota_track_instrument_output(const NotaEngine* e, int32_t track_id, float* out, int32_t max_n) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .devices()
                   .instrument()
                   .instrumentOutput(out, max_n)
             : 0;
}

int32_t nota_engine_add_sampler_track(NotaEngine* e, const char* path, int32_t root, int32_t loop) {
    if (!e || !path) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addSampler(std::string(path), root, loop != 0).id();
    return id > 0 ? id : 0;
}

int32_t nota_engine_add_sampler_instrument_track(NotaEngine* e) {
    if (!e) return 0;
    const int32_t id = nota::c_abi::eng(e).tracks().addSampler().id();
    return id > 0 ? id : 0;
}

int32_t nota_track_set_sampler_sample(NotaEngine* e, int32_t track_id, const char* path, int32_t root) {
    return (e && path &&
               nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .setSamplerSample(std::string(path), root))
               ? 1
               : 0;
}

int32_t nota_track_set_sampler_root(NotaEngine* e, int32_t track_id, int32_t root) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().setSamplerRoot(root)) ? 1 : 0;
}

float nota_track_sampler_play_position(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().samplerPlayPosition() : -1.0f;
}

int32_t nota_engine_get_track_info(const NotaEngine* e, int32_t index, NotaTrackInfo* out) {
    if (!e || !out) return 0;
    nota::engine::TrackInfo ti{};
    if (!nota::c_abi::ceng(e).tracks().infoAt(index, ti)) return 0;
    *out = {ti.id, ti.type, ti.clipCount, ti.muted, ti.soloed, ti.armed, ti.volume, ti.pan, ti.groupId};
    return 1;
}

int32_t nota_track_sampler_info(const NotaEngine* e, int32_t track_id, NotaSamplerInfo* out) {
    if (!e || !out) return 0;
    nota::engine::SamplerInfo si{};
    if (!nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().samplerInfo(si)) return 0;
    *out = {si.sampleId, si.rootNote, si.loop};
    return 1;
}

// ---- Nota Aurora custom wavetables (specs/053) ----------------------------

int32_t nota_track_aurora_set_table(
    NotaEngine* e, int32_t track_id, int32_t osc, const NotaAuroraTable* t, int32_t checkpoint) {
    if (!e || !t || !t->table) return 0;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .devices()
                   .auroraSetTable(osc, t->table, checkpoint != 0)
               ? 1
               : 0;
}

int32_t nota_track_aurora_clear_table(NotaEngine* e, int32_t track_id, int32_t osc) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).devices().auroraClearTable(osc)) ? 1 : 0;
}

int32_t nota_track_aurora_set_source(
    NotaEngine* e, int32_t track_id, int32_t osc, int32_t use_custom, int32_t checkpoint) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                     .devices()
                     .auroraSetSource(osc, use_custom != 0, checkpoint != 0))
               ? 1
               : 0;
}

int32_t nota_track_aurora_set_missing(NotaEngine* e, int32_t track_id, int32_t osc, const char* name) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                     .devices()
                     .auroraSetMissing(osc, name ? std::string(name) : std::string{}))
               ? 1
               : 0;
}

int32_t nota_track_aurora_table_info(const NotaEngine* e, int32_t track_id, int32_t osc, NotaAuroraTableInfo* out) {
    if (!e || !out || osc < 0 || osc > 1) return 0;
    const auto chain = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices();
    if (!chain.hasAurora()) return 0;
    const auto t = chain.auroraTable(osc);
    nota::c_abi::fillAuroraInfo(t.get(), out);
    out->use_custom = chain.auroraUsesCustom(osc) ? 1 : 0;
    const std::string miss = chain.auroraMissingName(osc);
    if (!miss.empty()) {
        out->missing = 1;
        if (!t) {
            const size_t n = std::min(miss.size(), sizeof(out->name) - 1);
            if (n > 0) std::memcpy(out->name, miss.data(), n);
            out->name[n] = '\0';
        }
    }
    return 1;
}

int64_t nota_track_aurora_table_read(const NotaEngine* e, int32_t track_id, int32_t osc, float* out, int64_t cap) {
    if (!e || osc < 0 || osc > 1) return 0;
    const auto t = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().auroraTable(osc);
    if (!t) return 0;
    const int64_t n = static_cast<int64_t>(t->level[0].size());
    if (!out) return n;
    const int64_t k = std::min(n, cap);
    if (k <= 0) return 0;
    std::memcpy(out, t->base(), static_cast<size_t>(k) * sizeof(float));
    return k;
}

int32_t nota_track_aurora_table_preview(const NotaEngine* e, int32_t track_id, int32_t osc, int32_t rep_frames,
    int32_t grid_frames, int32_t points, float* out, int32_t cap) {
    if (!e || osc < 0 || osc > 1) return 0;
    const auto t = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices().auroraTable(osc);
    if (!t) return 0;
    rep_frames = std::clamp(rep_frames, 0, 16);
    grid_frames = std::clamp(grid_frames, 0, 64);
    points = std::clamp(points, 2, 256);
    const int64_t need = static_cast<int64_t>(rep_frames + grid_frames) * points;
    if (!out) return static_cast<int32_t>(need);
    if (cap < need) return 0;
    const int32_t len = nota::dsp::WavetableAsset::kSize;
    const int32_t nf = t->frames;
    const int32_t slices = points / 2;
    float* p = out;
    for (int pass = 0; pass < 2; ++pass) {
        const int32_t count = pass == 0 ? rep_frames : grid_frames;
        for (int32_t i = 0; i < count; ++i) {
            const int32_t f =
                count > 1 ? static_cast<int32_t>(std::lround(static_cast<double>(i) * (nf - 1) / (count - 1))) : 0;
            const float* src = t->frame(0, std::clamp(f, 0, nf - 1));
            for (int32_t s = 0; s < slices; ++s) {
                const int32_t a = s * len / slices;
                const int32_t b = (s + 1) * len / slices;
                int32_t lo = a;
                int32_t hi = a;
                for (int32_t k = a + 1; k < b; ++k) {
                    if (src[k] < src[lo]) lo = k;
                    if (src[k] > src[hi]) hi = k;
                }
                *p++ = src[std::min(lo, hi)];
                *p++ = src[std::max(lo, hi)];
            }
            if ((points & 1) != 0) *p++ = src[len - 1];
        }
    }
    return static_cast<int32_t>(need);
}

int32_t nota_track_aurora_state(
    const NotaEngine* e, int32_t track_id, int32_t include_tables, uint8_t* out, int32_t cap) {
    if (!e) return 0;
    const auto chain = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).devices();
    if (!chain.hasAurora()) return 0;
    const auto v = chain.auroraState(include_tables != 0);
    const int32_t n = static_cast<int32_t>(v.size());
    if (out && cap >= n && n > 0) std::memcpy(out, v.data(), static_cast<size_t>(n));
    return n;
}

} // extern "C"
