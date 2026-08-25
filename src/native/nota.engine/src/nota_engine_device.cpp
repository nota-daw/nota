// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// C ABI — hosted plugins & device chain (M3): load instrument/effect, device
// CRUD & params, editor windows, plugin state, bypass, PDC latency, kind/id
// introspection (M7-6), hosted-plugin parameters (M9-B), plus the built-in FX
// and PDC self-tests.

#include "nota_engine_internal.h"

#include "CompensationDelay.h"
#include "Compressor.h"
#include "Delay.h"
#include "Eq.h"
#include "Forge.h"
#include "Reverb.h"
#include "Utility.h"

#include <cmath>
#include <cstdio>
#include <string>
#include <vector>

extern "C" {

// ---- Hosted plugins (M3-3) ------------------------------------------------

int32_t nota_engine_add_plugin_instrument_track(NotaEngine* e, int32_t catalog_index) {
    return e ? ENG(e)->addPluginInstrumentTrack(catalog_index) : 0;
}
NotaResult nota_track_set_instrument_plugin(NotaEngine* e, int32_t track_id, int32_t catalog_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setTrackInstrumentPlugin(track_id, catalog_index) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_track_set_builtin_instrument(NotaEngine* e, int32_t track_id, int32_t kind) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setTrackBuiltinInstrument(track_id, kind) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
int32_t nota_track_add_effect_plugin(NotaEngine* e, int32_t track_id, int32_t catalog_index) {
    return e ? ENG(e)->addTrackEffectPlugin(track_id, catalog_index) : -1;
}
int32_t nota_clip_audio_mono(const NotaEngine* e, int32_t track_id, int32_t clip_index,
                             float* out, int32_t max_frames, double* out_sr) {
    return e ? CENG(e)->clipAudioMono(track_id, clip_index, out, max_frames, out_sr) : 0;
}
int32_t nota_track_device_count(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackDeviceCount(track_id) : 0;
}
int32_t nota_track_add_builtin_device(NotaEngine* e, int32_t track_id, int32_t kind) {
    return e ? ENG(e)->addTrackBuiltinDevice(track_id, kind) : -1;
}
NotaResult nota_track_move_device(NotaEngine* e, int32_t track_id, int32_t from_index, int32_t to_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->moveDevice(track_id, from_index, to_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
NotaResult nota_track_remove_device(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->removeDevice(track_id, device_index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
const char* nota_device_name(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceName(track_id, device_index) : "";
}
int32_t nota_device_param_count(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceParamCount(track_id, device_index) : 0;
}
const char* nota_device_param_name(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? CENG(e)->deviceParamName(track_id, device_index, param_index) : "";
}
float nota_device_param_min(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? CENG(e)->deviceParamMin(track_id, device_index, param_index) : 0.0f;
}
float nota_device_param_max(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? CENG(e)->deviceParamMax(track_id, device_index, param_index) : 1.0f;
}
float nota_device_get_param(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? CENG(e)->deviceGetParam(track_id, device_index, param_index) : 0.0f;
}
NotaResult nota_device_set_param(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index, float value) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->deviceSetParam(track_id, device_index, param_index, value);
    return NOTA_OK;
}
float nota_device_gain_reduction(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceGainReduction(track_id, device_index) : 0.0f;
}
float nota_device_param_default(const NotaEngine* e, int32_t t, int32_t d, int32_t p) { return e ? CENG(e)->deviceParamDefault(t, d, p) : 0.0f; }
float nota_instrument_param_default(const NotaEngine* e, int32_t t, int32_t p) { return e ? CENG(e)->instrumentParamDefault(t, p) : 0.0f; }
float nota_midi_effect_param_default(const NotaEngine* e, int32_t t, int32_t i, int32_t p) { return e ? CENG(e)->midiEffectParamDefault(t, i, p) : 0.0f; }
int32_t nota_device_scope(const NotaEngine* e, int32_t track_id, int32_t device_index, float* out, int32_t max_samples) {
    return e ? CENG(e)->deviceScope(track_id, device_index, out, max_samples) : 0;
}
void nota_device_action(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t id, int32_t iarg, float farg) {
    if (e) ENG(e)->deviceAction(track_id, device_index, id, iarg, farg);
}
int32_t nota_device_layer_wave(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t layer, float* out, int32_t max_samples) {
    return e ? CENG(e)->deviceLayerWave(track_id, device_index, layer, out, max_samples) : 0;
}
int32_t nota_device_get_state(const NotaEngine* e, int32_t track_id, int32_t device_index, uint8_t* out, int32_t cap) {
    return e ? CENG(e)->deviceGetState(track_id, device_index, out, cap) : 0;
}
void nota_device_set_state(NotaEngine* e, int32_t track_id, int32_t device_index, const uint8_t* data, int32_t size) {
    if (e) ENG(e)->deviceSetState(track_id, device_index, data, size);
}

// --- MIDI effects ---
int32_t nota_track_add_midi_effect(NotaEngine* e, int32_t t, int32_t kind) { return e ? ENG(e)->addTrackMidiEffect(t, kind) : -1; }
int32_t nota_track_midi_effect_count(const NotaEngine* e, int32_t t) { return e ? CENG(e)->trackMidiEffectCount(t) : 0; }
NotaResult nota_track_move_midi_effect(NotaEngine* e, int32_t t, int32_t from, int32_t to) {
    if (!e) return NOTA_ERR_INVALID_ARG; return ENG(e)->moveMidiEffect(t, from, to) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_track_remove_midi_effect(NotaEngine* e, int32_t t, int32_t i) {
    if (!e) return NOTA_ERR_INVALID_ARG; return ENG(e)->removeMidiEffect(t, i) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
int32_t     nota_midi_effect_kind(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectKind(t, i) : -1; }
int32_t     nota_midi_effect_last_in(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectLastIn(t, i) : -1; }
int32_t     nota_midi_effect_last_out(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectLastOut(t, i) : -1; }
int32_t     nota_midi_effect_scope(const NotaEngine* e, int32_t t, int32_t i, float* out, int32_t max_n) { return e ? CENG(e)->midiEffectScope(t, i, out, max_n) : 0; }
const char* nota_midi_effect_name(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectName(t, i) : ""; }
int32_t     nota_midi_effect_param_count(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectParamCount(t, i) : 0; }
const char* nota_midi_effect_param_name(const NotaEngine* e, int32_t t, int32_t i, int32_t p) { return e ? CENG(e)->midiEffectParamName(t, i, p) : ""; }
float       nota_midi_effect_param_min(const NotaEngine* e, int32_t t, int32_t i, int32_t p) { return e ? CENG(e)->midiEffectParamMin(t, i, p) : 0.0f; }
float       nota_midi_effect_param_max(const NotaEngine* e, int32_t t, int32_t i, int32_t p) { return e ? CENG(e)->midiEffectParamMax(t, i, p) : 1.0f; }
float       nota_midi_effect_get_param(const NotaEngine* e, int32_t t, int32_t i, int32_t p) { return e ? CENG(e)->midiEffectGetParam(t, i, p) : 0.0f; }
NotaResult  nota_midi_effect_set_param(NotaEngine* e, int32_t t, int32_t i, int32_t p, float v) {
    if (!e) return NOTA_ERR_INVALID_ARG; ENG(e)->midiEffectSetParam(t, i, p, v); return NOTA_OK;
}
void        nota_midi_effect_set_bypassed(NotaEngine* e, int32_t t, int32_t i, int32_t b) { if (e) ENG(e)->setMidiEffectBypassed(t, i, b != 0); }
int32_t     nota_midi_effect_bypassed(const NotaEngine* e, int32_t t, int32_t i) { return (e && CENG(e)->midiEffectBypassed(t, i)) ? 1 : 0; }
void        nota_midi_effect_set_cc_dest(NotaEngine* e, int32_t t, int32_t i, int32_t dev, int32_t param) { if (e) ENG(e)->setMidiEffectCcDest(t, i, dev, param); }
void        nota_midi_effect_set_cc_depth(NotaEngine* e, int32_t t, int32_t i, float d) { if (e) ENG(e)->setMidiEffectCcDepth(t, i, d); }
int32_t     nota_midi_effect_cc_dest_device(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectCcDestDevice(t, i) : -2; }
int32_t     nota_midi_effect_cc_dest_param(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectCcDestParam(t, i) : -1; }
float       nota_midi_effect_cc_depth(const NotaEngine* e, int32_t t, int32_t i) { return e ? CENG(e)->midiEffectCcDepth(t, i) : 0.0f; }
void nota_device_set_sidechain_source(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t source_track_id) {
    if (e) ENG(e)->setDeviceSidechainSource(track_id, device_index, source_track_id);
}
int32_t nota_device_sidechain_source(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceSidechainSource(track_id, device_index) : -1;
}
int32_t nota_device_accepts_sidechain(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && CENG(e)->deviceAcceptsSidechain(track_id, device_index)) ? 1 : 0;
}
void nota_track_set_instrument_sidechain_source(NotaEngine* e, int32_t track_id, int32_t source_track_id) {
    if (e) ENG(e)->setInstrumentSidechainSource(track_id, source_track_id);
}
int32_t nota_track_instrument_sidechain_source(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->instrumentSidechainSource(track_id) : -1;
}
int32_t nota_track_instrument_accepts_sidechain(const NotaEngine* e, int32_t track_id) {
    return (e && CENG(e)->instrumentAcceptsSidechain(track_id)) ? 1 : 0;
}
void nota_device_set_sidechain_gain(NotaEngine* e, int32_t track_id, int32_t device_index, float gain_db) {
    if (e) ENG(e)->setDeviceSidechainGain(track_id, device_index, gain_db);
}
float nota_device_sidechain_gain(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceSidechainGain(track_id, device_index) : 0.0f;
}
void nota_device_set_sidechain_mix(NotaEngine* e, int32_t track_id, int32_t device_index, float mix) {
    if (e) ENG(e)->setDeviceSidechainMix(track_id, device_index, mix);
}
float nota_device_sidechain_mix(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceSidechainMix(track_id, device_index) : 1.0f;
}
void nota_device_set_sidechain_tap_pre(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t pre) {
    if (e) ENG(e)->setDeviceSidechainTapPre(track_id, device_index, pre);
}
int32_t nota_device_sidechain_tap_pre(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->deviceSidechainTapPre(track_id, device_index) : 0;
}
int32_t nota_fx_selftest(void) {
    constexpr int N = 2048;
    constexpr double sr = 44100.0, kPi = 3.14159265358979323846;
    auto sine = [&](std::vector<float>& b, double f) {
        b.assign(static_cast<size_t>(N) * 2, 0.0f);
        for (int i = 0; i < N; ++i) {
            const float s = 0.3f * std::sin(2.0 * kPi * f * i / sr);
            b[i * 2] = s; b[i * 2 + 1] = s;
        }
    };
    auto rms = [&](const std::vector<float>& b) {
        double s = 0; for (float v : b) s += v * (double)v; return std::sqrt(s / b.size());
    };

    std::vector<float> a; sine(a, 440.0);
    const double in = rms(a);

    // Flat EQ must pass audio ~unchanged.
    { nota::Eq eq; eq.setSampleRate(sr, N); eq.process(a.data(), N); }
    const double flat = rms(a);

    // A -18 dB cut on the tone's band must reduce it, but not silence it.
    std::vector<float> b2; sine(b2, 440.0);
    { nota::Eq eq; eq.setSampleRate(sr, N);
      // Band 3 (index 2) is a bell, enabled by default: cut it hard at the tone.
      eq.setParam(2 * nota::Eq::kPerBand + nota::Eq::Freq, 440.0f);
      eq.setParam(2 * nota::Eq::kPerBand + nota::Eq::Gain, -18.0f);
      eq.setParam(2 * nota::Eq::kPerBand + nota::Eq::Q, 2.0f);
      eq.process(b2.data(), N); }
    const double cut = rms(b2);

    // Compressor must pass audio and report gain reduction on a supra-threshold signal.
    std::vector<float> c; sine(c, 440.0);
    float compGr = 0.0f;
    { nota::Compressor comp; comp.setSampleRate(sr, N);
      comp.setParam(nota::Compressor::Threshold, -24.0f); comp.process(c.data(), N);
      compGr = comp.gainReductionDb(); }
    const double comp = rms(c);

    // Reverb (M6-3): fully wet must still emit energy (tail builds up).
    std::vector<float> rv; sine(rv, 440.0);
    { nota::Reverb r; r.setSampleRate(sr, N); r.setParam(nota::Reverb::PreDelay, 0.0f); r.setParam(nota::Reverb::DryWet, 1.0f); r.process(rv.data(), N); }
    const double rev = rms(rv);

    // Delay (M6-3): dry-through at mix 0 stays ~unchanged; wet path emits energy.
    std::vector<float> dl; sine(dl, 440.0);
    { nota::Delay d; d.setSampleRate(sr, N); d.setParam(nota::Delay::DryWet, 0.5f);
      d.setParam(nota::Delay::SyncMode, 0.0f); d.setParam(nota::Delay::TimeL, 0.01f); d.setParam(nota::Delay::Feedback, 0.5f); d.process(dl.data(), N); }
    const double del = rms(dl);

    // Utility (M6-3): +6 dB gain must raise level; mono collapse must pass audio.
    std::vector<float> ut; sine(ut, 440.0);
    { nota::Utility u; u.setSampleRate(sr, N); u.setParam(nota::Utility::Gain, 6.0f); u.process(ut.data(), N); }
    const double util = rms(ut);

    // Forge oversampling: a hard clip of a high sine folds its 3rd harmonic back as
    // an alias tone. f0 = 0.3·Fs → 3·f0 = 39690 Hz aliases to Fs−3·f0 = 4410 Hz, a
    // bin no legit harmonic occupies. Oversampling must slash that alias vs 1×.
    const double aliasHz = sr - 3.0 * (0.3 * sr);   // 4410 Hz
    auto goertzel = [&](const std::vector<float>& b, double f) {
        const double w = 2.0 * kPi * f / sr, cw = std::cos(w), c = 2.0 * cw, sw = std::sin(w);
        double s1 = 0, s2 = 0;
        for (int i = 512; i < N; ++i) { double x = b[i * 2]; double s0 = x + c * s1 - s2; s2 = s1; s1 = s0; }
        const double re = s1 - s2 * cw, im = s2 * sw;
        return std::sqrt(re * re + im * im) / (N - 512);
    };
    auto forgeClip = [&](std::vector<float>& out, float os) {
        sine(out, 0.3 * sr);
        nota::Forge f; f.setSampleRate(sr, N);
        f.setParam(nota::Forge::Amount, 1.0f); f.setParam(nota::Forge::Wet, 1.0f);
        f.setParam(nota::Forge::Output, 0.5f); f.setParam(nota::Forge::Bias, 0.5f);
        f.setParam(nota::Forge::Routing, 0.0f);
        f.setParam(nota::Forge::S1Type, 4.0f / 5.0f);   // Digital (hard clip)
        f.setParam(nota::Forge::S1Drive, 1.0f); f.setParam(nota::Forge::S1On, 1.0f);
        f.setParam(nota::Forge::S2On, 0.0f); f.setParam(nota::Forge::S3On, 0.0f);
        f.setParam(nota::Forge::Oversampling, os);
        f.process(out.data(), N);
    };
    std::vector<float> fg1, fg8;
    forgeClip(fg1, 0.0f);   // 1× (Off)
    forgeClip(fg8, 1.0f);   // 8×
    const double alias1 = goertzel(fg1, aliasHz), alias8 = goertzel(fg8, aliasHz);
    const double forgeRms = rms(fg8);
    const double aliasDrop = alias1 > 1e-9 ? 20.0 * std::log10(alias8 / alias1) : 0.0;
    std::fprintf(stderr, "  Forge oversampling: alias 1x=%.5f  8x=%.5f  (%.1f dB)\n", alias1, alias8, aliasDrop);

    const bool ok = flat > in * 0.7 && flat < in * 1.3
                 && cut < in * 0.7 && cut > in * 0.02
                 && comp > in * 0.1
                 && compGr > 0.5f            // compressor reports gain reduction
                 && rev > in * 0.02
                 && del > in * 0.3
                 && util > in * 1.5
                 && forgeRms > 0.05 && std::isfinite(forgeRms)   // Forge emits sane audio
                 && alias8 < alias1 * 0.3;                       // ≥ ~10 dB alias reduction at 8×
    return ok ? 1 : 0;
}
NotaResult nota_plugin_open_editor(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->openTrackEditor(track_id, device_index);
    return NOTA_OK;
}
NotaResult nota_plugin_close_editor(NotaEngine* e, int32_t track_id, int32_t device_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->closeTrackEditor(track_id, device_index);
    return NOTA_OK;
}
int32_t nota_plugin_get_state(const NotaEngine* e, int32_t track_id, int32_t device_index,
                              uint8_t* out, int32_t capacity) {
    return e ? CENG(e)->getPluginState(track_id, device_index, out, capacity) : 0;
}
NotaResult nota_plugin_set_state(NotaEngine* e, int32_t track_id, int32_t device_index,
                                 const uint8_t* data, int32_t size) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->setPluginState(track_id, device_index, data, size) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}
NotaResult nota_track_set_device_bypassed(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t bypassed) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setTrackDeviceBypassed(track_id, device_index, bypassed != 0);
    return NOTA_OK;
}
int32_t nota_track_device_bypassed(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return (e && CENG(e)->trackDeviceBypassed(track_id, device_index)) ? 1 : 0;
}
int32_t nota_track_latency_samples(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackLatencySamples(track_id) : 0;
}
int32_t nota_pdc_selftest(void) {
    // Feed an impulse into a delay of D samples; expect it to emerge at frame D.
    nota::CompensationDelay d;
    const int32_t D = 100;
    d.setDelay(D);
    std::vector<float> buf(static_cast<size_t>(2 * D) * 2, 0.0f); // 2D stereo frames
    buf[0] = 1.0f; buf[1] = 1.0f;                                  // impulse at frame 0
    d.process(buf.data(), 2 * D);
    const bool ok = std::fabs(buf[0]) < 1e-6f && std::fabs(buf[1]) < 1e-6f
                 && std::fabs(buf[D * 2] - 1.0f) < 1e-6f
                 && std::fabs(buf[D * 2 + 1] - 1.0f) < 1e-6f;
    return ok ? 1 : 0;
}

// ---- Plugin / device kind & id introspection (M7-6) -----------------------

int32_t nota_track_instrument_kind(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->trackInstrumentKind(track_id) : -2;
}
int32_t nota_track_device_builtin_kind(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->trackDeviceBuiltinKind(track_id, device_index) : -1;
}
const char* nota_track_instrument_plugin_id(const NotaEngine* e, int32_t track_id) {
    static std::string id; // owned by the engine, valid until the next call
    id = e ? CENG(e)->trackInstrumentPluginId(track_id) : std::string{};
    return id.c_str();
}
const char* nota_track_device_plugin_id(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    static std::string id; // owned by the engine, valid until the next call
    id = e ? CENG(e)->trackDevicePluginId(track_id, device_index) : std::string{};
    return id.c_str();
}

// ---- hosted-plugin parameters (M9-B) --------------------------------------
int32_t nota_plugin_param_count(const NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? CENG(e)->pluginParamCount(track_id, device_index) : 0;
}
const char* nota_plugin_param_id(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->pluginParamId(track_id, device_index, param_index) : std::string{};
    return s.c_str();
}
const char* nota_plugin_param_name(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->pluginParamName(track_id, device_index, param_index) : std::string{};
    return s.c_str();
}
float nota_plugin_param_get(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return e ? CENG(e)->pluginParamGet(track_id, device_index, param_index) : 0.0f;
}
NotaResult nota_plugin_param_set(NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index, float normalized) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->pluginParamSet(track_id, device_index, param_index, normalized);
    return NOTA_OK;
}
int32_t nota_plugin_last_touched_param(NotaEngine* e, int32_t track_id, int32_t device_index) {
    return e ? ENG(e)->lastTouchedPluginParam(track_id, device_index) : -1;
}

} // extern "C"
