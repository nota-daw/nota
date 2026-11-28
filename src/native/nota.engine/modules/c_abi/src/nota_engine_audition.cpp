// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — preset audition & browser preview cache.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota_engine_internal.h"

#include <cstdint>
#include <cstddef>
#include <array>
#include <new>
#include <string>
#include <vector>

namespace {
constexpr size_t kStackNoteLimit = 64;
} // namespace

extern "C" {

NotaAudition* nota_audition_create(double sample_rate) {
    return new (std::nothrow) NotaAudition(sample_rate);
}

void nota_audition_destroy(NotaAudition* rig) {
    delete rig;
}

int32_t nota_audition_set_instrument(NotaAudition* rig, int32_t kind) {
    return (rig && rig->session.setInstrument(kind)) ? 1 : 0;
}

int32_t nota_audition_instrument_param(NotaAudition* rig, const char* id, float value) {
    return (rig && id && rig->session.instrumentParam(id, value)) ? 1 : 0;
}

int32_t nota_audition_add_device(NotaAudition* rig, int32_t kind) {
    return rig ? rig->session.addDevice(kind) : -1;
}

int32_t nota_audition_device_param(NotaAudition* rig, int32_t index, const char* name, float value) {
    return (rig && name && rig->session.deviceParam(index, name, value)) ? 1 : 0;
}

int32_t nota_audition_add_midi_effect(NotaAudition* rig, int32_t kind) {
    return rig ? rig->session.addMidiEffect(kind) : -1;
}

int32_t nota_audition_midi_param(NotaAudition* rig, int32_t index, const char* name, float value) {
    return (rig && name && rig->session.midiParam(index, name, value)) ? 1 : 0;
}

int32_t nota_audition_set_sampler_sample(NotaAudition* rig, const char* path_utf8, int32_t root_note) {
    return (rig && path_utf8 && rig->session.setSamplerSample(path_utf8, root_note)) ? 1 : 0;
}

int32_t nota_audition_set_mosaic_program(NotaAudition* rig, const char* text_utf8) {
    return (rig && text_utf8 && rig->session.setMosaicProgram(text_utf8)) ? 1 : 0;
}

int32_t nota_audition_instrument_state(NotaAudition* rig, const uint8_t* data, int32_t size) {
    return (rig && rig->session.instrumentState(data, size)) ? 1 : 0;
}

int32_t nota_audition_add_source_from(NotaAudition* rig, const NotaAudition* part, float gain) {
    return (rig && part && rig->session.addSourceFrom(part->session, gain)) ? 1 : 0;
}

int32_t nota_audition_use_cached_source(NotaAudition* rig, const char* key) {
    return (rig && key && rig->session.useCachedSource(key)) ? 1 : 0;
}

int32_t nota_audition_cache_source(NotaAudition* rig, const char* key, float target_peak) {
    return (rig && key && rig->session.cacheSource(key, target_peak)) ? 1 : 0;
}

int32_t nota_audition_use_kit(NotaAudition* rig) {
    return (rig && rig->session.useKit()) ? 1 : 0;
}

int32_t nota_audition_kit_add_pad(
    NotaAudition* rig, int32_t note, const char* path_utf8, float gain, float pan, int32_t choke) {
    return (rig && path_utf8) ? rig->session.kitAddPad(note, path_utf8, gain, pan, choke) : -1;
}

int32_t nota_audition_kit_pad_add_device(NotaAudition* rig, int32_t pad, int32_t kind) {
    return rig ? rig->session.kitPadAddDevice(pad, kind) : -1;
}

int32_t nota_audition_kit_pad_device_param(
    NotaAudition* rig, int32_t pad, int32_t device, const char* name, float value) {
    return (rig && name && rig->session.kitPadDeviceParam(pad, device, name, value)) ? 1 : 0;
}

int32_t nota_audition_set_source_file(NotaAudition* rig, const char* path_utf8, double max_seconds) {
    return (rig && path_utf8 && rig->session.setSourceFile(path_utf8, max_seconds)) ? 1 : 0;
}

int64_t nota_audition_render(NotaAudition* rig, const NotaAuditionNote* notes, int32_t count, double bpm,
    double phrase_beats, double max_tail_seconds, int32_t flags) {
    if (!rig || count < 0 || (count > 0 && !notes)) return -1;
    std::array<nota::engine::AuditionNoteSpec, kStackNoteLimit> stack{};
    std::vector<nota::engine::AuditionNoteSpec> heap;
    nota::engine::AuditionNoteSpec* dst = nullptr;
    if (count > 0) {
        if (static_cast<size_t>(count) <= stack.size()) {
            dst = stack.data();
        } else {
            heap.resize(static_cast<size_t>(count));
            dst = heap.data();
        }
        for (int32_t i = 0; i < count; ++i)
            dst[i] = {notes[i].start_beat, notes[i].length_beats, notes[i].pitch, notes[i].velocity};
    }
    return rig->session.render(dst, count, bpm, phrase_beats, max_tail_seconds, flags);
}

void nota_audition_cancel(NotaAudition* rig) {
    if (rig) rig->session.cancel();
}

int32_t nota_audition_peaks(const NotaAudition* rig, float* out_min_max, int32_t max_points) {
    return (rig && out_min_max && max_points > 0) ? rig->session.peaks(out_min_max, max_points) : 0;
}

double nota_audition_seconds(const NotaAudition* rig) {
    return rig ? rig->session.seconds() : 0.0;
}

NotaResult nota_engine_audition_store(NotaEngine* e, const NotaAudition* rig, const char* key) {
    if (!e || !rig || !key) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).preview().storeAudition(key, rig->session);
    return NOTA_OK;
}

int32_t nota_engine_audition_cached(const NotaEngine* e, const char* key) {
    return (e && key && nota::c_abi::ceng(e).preview().cachedAudition(key)) ? 1 : 0;
}

int32_t nota_engine_preview_cached_at(NotaEngine* e, const char* key, double start_seconds) {
    return (e && key && nota::c_abi::eng(e).preview().playCached(key, start_seconds)) ? 1 : 0;
}

int32_t nota_engine_preview_scope(const NotaEngine* e, float* out, int32_t n) {
    return (e && out && n > 0) ? nota::c_abi::ceng(e).preview().scope(out, n) : 0;
}

} // extern "C"
