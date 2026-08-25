// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// C ABI — device settings: audio output/input device / sample-rate / buffer
// (M7-1) and MIDI input enable/disable (M7-2). Device enumeration is a hardware
// query, so the snapshots below are file-static (not engine-bound).

#include "nota_engine_internal.h"

#include "AudioConfig.h"
#include "MidiConfig.h"

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

} // extern "C"
