// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — device settings: audio output/input device / sample-rate / buffer
// (M7-1) and MIDI input enable/disable (M7-2). Device enumeration is a hardware
// query, so the snapshots below are file-static (not engine-bound).

#include "nota_engine_internal.h"

#include "AudioConfig.h"
#include "MidiConfig.h"
#include "MpeInput.h"

#include <algorithm>
#include <cmath>

#include <string>
#include <vector>

// ---- Audio device settings (M7-1) -----------------------------------------

namespace {
// Snapshot of connected devices, refreshed by nota_audio_refresh_devices().
// Global (not engine-bound): enumeration is a hardware query.
std::vector<nota::AudioDeviceInfo> g_outputDevices;
std::vector<nota::AudioDeviceInfo> g_inputDevices;

const char* deviceField(const std::vector<nota::AudioDeviceInfo>& list, int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(list.size())) return nullptr;
    return uid ? list[i].uid.c_str() : list[i].name.c_str();
}
} // namespace

extern "C" {

void nota_audio_refresh_devices(void) {
    g_outputDevices = nota::enumerateAudioDevices(/*inputScope=*/false);
    g_inputDevices  = nota::enumerateAudioDevices(/*inputScope=*/true);
}

int32_t nota_audio_output_device_count(void) { return static_cast<int32_t>(g_outputDevices.size()); }
int32_t nota_audio_input_device_count(void)  { return static_cast<int32_t>(g_inputDevices.size()); }

const char* nota_audio_output_device_uid(int32_t i)  { return deviceField(g_outputDevices, i, true); }
const char* nota_audio_output_device_name(int32_t i) { return deviceField(g_outputDevices, i, false); }
const char* nota_audio_input_device_uid(int32_t i)   { return deviceField(g_inputDevices, i, true); }
const char* nota_audio_input_device_name(int32_t i)  { return deviceField(g_inputDevices, i, false); }

NotaResult nota_audio_set_output_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioOutputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}
NotaResult nota_audio_set_input_device(NotaEngine* e, const char* uid) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioInputDevice(uid ? std::string(uid) : std::string{});
    return NOTA_OK;
}
NotaResult nota_audio_set_sample_rate(NotaEngine* e, double sr) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioSampleRate(sr);
    return NOTA_OK;
}
NotaResult nota_audio_set_buffer_frames(NotaEngine* e, int32_t frames) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioBufferFrames(frames);
    return NOTA_OK;
}
NotaResult nota_audio_set_wasapi_exclusive(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setAudioWasapiExclusive(enabled != 0);
    return NOTA_OK;
}
int32_t nota_audio_wasapi_exclusive(const NotaEngine* e) {
    return (e && CENG(e)->audioConfig().wasapiExclusive) ? 1 : 0;
}

const char* nota_audio_output_device(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioConfig().outputDeviceUid : std::string{};
    return s.c_str();
}
const char* nota_audio_input_device(const NotaEngine* e) {
    static std::string s; // owned by the engine, valid until the next call
    s = e ? CENG(e)->audioConfig().inputDeviceUid : std::string{};
    return s.c_str();
}
double  nota_audio_sample_rate(const NotaEngine* e)   { return e ? CENG(e)->audioConfig().sampleRate   : 0.0; }
int32_t nota_audio_buffer_frames(const NotaEngine* e) { return e ? CENG(e)->audioConfig().bufferFrames : 0; }

NotaResult nota_audio_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->applyAudioConfig() ? NOTA_OK : NOTA_ERR_AUDIO_DEVICE;
}
double  nota_audio_negotiated_sample_rate(const NotaEngine* e)   { return e ? CENG(e)->sampleRate()   : 0.0; }
int32_t nota_audio_negotiated_buffer_frames(const NotaEngine* e) { return e ? CENG(e)->bufferFrames() : 0; }
int32_t nota_audio_exclusive_fallback(const NotaEngine* e)       { return (e && CENG(e)->audioExclusiveFallback()) ? 1 : 0; }

} // extern "C"

// ---- MIDI device settings (M7-2) ------------------------------------------

namespace {
// Snapshot of connected MIDI inputs, refreshed by nota_midi_refresh_devices().
std::vector<nota::MidiDeviceInfo> g_midiInputs;

const char* midiField(int32_t i, bool uid) {
    if (i < 0 || i >= static_cast<int32_t>(g_midiInputs.size())) return nullptr;
    return uid ? g_midiInputs[i].uid.c_str() : g_midiInputs[i].name.c_str();
}
} // namespace

extern "C" {

void nota_midi_refresh_devices(void) { g_midiInputs = nota::enumerateMidiInputs(); }

int32_t nota_midi_input_device_count(void) { return static_cast<int32_t>(g_midiInputs.size()); }
const char* nota_midi_input_device_uid(int32_t i)  { return midiField(i, true); }
const char* nota_midi_input_device_name(int32_t i) { return midiField(i, false); }

NotaResult nota_midi_set_input_enabled(NotaEngine* e, const char* uid, int32_t enabled) {
    if (!e || !uid) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setMidiInputEnabled(std::string(uid), enabled != 0);
    return NOTA_OK;
}
int32_t nota_midi_input_enabled(const NotaEngine* e, const char* uid) {
    return (e && uid && CENG(e)->midiInputEnabled(std::string(uid))) ? 1 : 0;
}
NotaResult nota_midi_set_mpe(NotaEngine* e, int32_t enabled, int32_t bend_range) {
    if (!e || bend_range < 1 || bend_range > 96) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setMpe(enabled != 0, bend_range); return NOTA_OK;
}
int32_t nota_midi_mpe_enabled(const NotaEngine* e) { return (e && CENG(e)->midiConfig().mpe) ? 1 : 0; }
int32_t nota_midi_mpe_bend_range(const NotaEngine* e) { return e ? CENG(e)->midiConfig().mpeBendRange : 48; }
int32_t nota_mpe_selftest(void) {
    struct Ev { int kind; int32_t pitch; int32_t dim; float v; };   // kind 0 on, 1 off, 2 expr
    struct Rec final : nota::MpeInput::Sink {
        std::vector<Ev> ev;
        void mpeNoteOn(int32_t p, float v) override { ev.push_back({0, p, -1, v}); }
        void mpeNoteOff(int32_t p) override { ev.push_back({1, p, -1, 0.0f}); }
        void mpeExpression(int32_t p, int32_t d, float v) override { ev.push_back({2, p, d, v}); }
        bool has(int kind, int32_t p, int32_t d, float v) const {
            for (const auto& e : ev) if (e.kind == kind && e.pitch == p && e.dim == d && std::fabs(e.v - v) < 0.02f) return true;
            return false;
        }
    } r;
    nota::MpeInput m;
    m.configure(true, 48);
    auto bend = [&](int ch, float x) { const int raw = std::clamp((int)std::lround(8192 + x * 8191), 0, 16383); m.message(0xE0, ch, raw & 0x7F, raw >> 7, r); };

    // 1: a member channel's state before its note-on lands on the note right after it.
    bend(1, 0.25f); m.message(0xD0, 1, 64, 0, r); m.message(0xB0, 1, 74, 100, r);
    r.ev.clear(); m.message(0x90, 1, 60, 100, r);
    if (r.ev.empty() || r.ev[0].kind != 0 || !r.has(2, 60, 0, 12.0f) || !r.has(2, 60, 1, 64 / 127.0f) || !r.has(2, 60, 2, 100 / 127.0f)) return 1;
    // 2: member bend is per-note, in semitones (±48 by default).
    r.ev.clear(); bend(1, -0.5f);
    if (r.ev.size() != 1 || !r.has(2, 60, 0, -24.0f)) return 2;
    // 3: another member channel's note is untouched by channel 2's bend.
    m.message(0x90, 2, 64, 100, r); r.ev.clear(); bend(1, 0.0f);
    for (const auto& e : r.ev) if (e.pitch == 64) return 3;
    // 4: the master channel addresses the whole instrument, wheel normalized.
    r.ev.clear(); bend(0, 1.0f); m.message(0xD0, 0, 127, 0, r);
    if (!r.has(2, -1, 0, 1.0f) || !r.has(2, -1, 1, 1.0f)) return 4;
    // 5: RPN 0 on a member channel re-ranges every member of the zone.
    m.message(0xB0, 3, 101, 0, r); m.message(0xB0, 3, 100, 0, r); m.message(0xB0, 3, 6, 24, r);
    if (m.bendRange(1) != 24 || m.bendRange(15) != 24 || m.bendRange(0) != 48) return 5;
    r.ev.clear(); bend(1, 0.5f);
    if (!r.has(2, 60, 0, 12.0f)) return 5;
    // 6: after note-off the channel no longer bends that pitch.
    m.message(0x80, 1, 60, 0, r); r.ev.clear(); bend(1, 0.1f);
    for (const auto& e : r.ev) if (e.pitch == 60) return 6;
    // 7: MCM on channel 1 shrinks the lower zone; channel 5 becomes an ordinary channel.
    m.message(0xB0, 0, 101, 0, r); m.message(0xB0, 0, 100, 6, r); m.message(0xB0, 0, 6, 3, r);
    if (m.lowerMembers() != 3) return 7;
    m.message(0x90, 5, 70, 100, r); r.ev.clear(); bend(5, 1.0f);
    if (!r.has(2, -1, 0, 1.0f)) return 7;
    // 8: MPE off: every channel is an ordinary keyboard; poly pressure stays per-note.
    nota::MpeInput off; off.configure(false, 48);
    r.ev.clear(); off.message(0x90, 1, 60, 100, r);
    { const int raw = 16383; off.message(0xE0, 1, raw & 0x7F, raw >> 7, r); }
    off.message(0xA0, 1, 60, 127, r);
    if (r.ev.size() != 3 || !r.has(2, -1, 0, 1.0f) || !r.has(2, 60, 1, 1.0f)) return 8;
    return 0;
}

NotaResult nota_midi_apply(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->applyMidiConfig() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

// MIDI-learn: drain incoming CC/note-on events into a caller buffer of int32 quads
// {kind, channel, number, value}. Returns the event count written (<= max).
int32_t nota_midi_poll_control_events(NotaEngine* e, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->pollControlEvents(reinterpret_cast<nota::ControlEvent*>(out), max);
}

} // extern "C"

// ---- Gamepad input ---------------------------------------------------------

extern "C" {

void    nota_gamepad_start(NotaEngine* e) { if (e) ENG(e)->startGamepadInput(); }
void    nota_gamepad_stop(NotaEngine* e)  { if (e) ENG(e)->stopGamepadInput(); }
int32_t nota_gamepad_count(NotaEngine* e) { return e ? ENG(e)->gamepadCount() : 0; }

const char* nota_gamepad_uid(NotaEngine* e, int32_t i) {
    const char* uid = nullptr; const char* name = nullptr;
    if (e) ENG(e)->gamepadInfo(i, &uid, &name);
    return uid ? uid : "";
}
const char* nota_gamepad_name(NotaEngine* e, int32_t i) {
    const char* uid = nullptr; const char* name = nullptr;
    if (e) ENG(e)->gamepadInfo(i, &uid, &name);
    return name ? name : "";
}

int32_t nota_gamepad_poll_events(NotaEngine* e, NotaGamepadButtonEvent* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->pollGamepadEvents(
        reinterpret_cast<nota::GamepadInput::ButtonEvent*>(out), max);
}

int32_t nota_gamepad_axis_values(NotaEngine* e, int32_t pad, int32_t* out, int32_t max) {
    if (!e || !out || max <= 0) return 0;
    return ENG(e)->gamepadAxisValues(pad, out, max);
}

} // extern "C"
