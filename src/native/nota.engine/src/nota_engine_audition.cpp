// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — preset audition (an offline-rendered standalone chain, see Audition.h).

#include "nota_engine_internal.h"
#include "Audition.h"

#include <string>

#define RIG(r) reinterpret_cast<nota::AuditionRig*>(r)
#define CRIG(r) reinterpret_cast<const nota::AuditionRig*>(r)

static_assert(sizeof(NotaAuditionNote) == sizeof(nota::AuditionNote), "NotaAuditionNote layout");

extern "C" {

NotaAudition* nota_audition_create(double sample_rate) {
    return reinterpret_cast<NotaAudition*>(new nota::AuditionRig(sample_rate));
}
void nota_audition_destroy(NotaAudition* rig) { delete RIG(rig); }

int32_t nota_audition_set_instrument(NotaAudition* rig, int32_t kind) {
    return rig && RIG(rig)->setInstrument(kind) ? 1 : 0;
}
int32_t nota_audition_instrument_param(NotaAudition* rig, const char* id, float value) {
    return rig && id && RIG(rig)->instrumentParam(id, value) ? 1 : 0;
}
int32_t nota_audition_add_device(NotaAudition* rig, int32_t kind) { return rig ? RIG(rig)->addDevice(kind) : -1; }
int32_t nota_audition_device_param(NotaAudition* rig, int32_t index, const char* name, float value) {
    return rig && name && RIG(rig)->deviceParam(index, name, value) ? 1 : 0;
}
int32_t nota_audition_add_midi_effect(NotaAudition* rig, int32_t kind) { return rig ? RIG(rig)->addMidiEffect(kind) : -1; }
int32_t nota_audition_midi_param(NotaAudition* rig, int32_t index, const char* name, float value) {
    return rig && name && RIG(rig)->midiParam(index, name, value) ? 1 : 0;
}
int32_t nota_audition_set_sampler_sample(NotaAudition* rig, const char* path, int32_t root_note) {
    return rig && RIG(rig)->setSamplerSample(path ? std::string(path) : std::string(), root_note) ? 1 : 0;
}
int32_t nota_audition_add_source_from(NotaAudition* rig, const NotaAudition* part, float gain) {
    return rig && part && RIG(rig)->addSourceFrom(*CRIG(part), gain) ? 1 : 0;
}
int32_t nota_audition_use_cached_source(NotaAudition* rig, const char* key) {
    return rig && key && RIG(rig)->useCachedSource(key) ? 1 : 0;
}
int32_t nota_audition_cache_source(NotaAudition* rig, const char* key, float target_peak) {
    return rig && key && RIG(rig)->cacheSource(key, target_peak) ? 1 : 0;
}
int32_t nota_audition_use_kit(NotaAudition* rig) { return rig && RIG(rig)->useKit() ? 1 : 0; }
int32_t nota_audition_kit_add_pad(NotaAudition* rig, int32_t note, const char* path, float gain, float pan, int32_t choke) {
    return rig && path ? RIG(rig)->kitAddPad(note, path, gain, pan, choke) : -1;
}
int32_t nota_audition_kit_pad_add_device(NotaAudition* rig, int32_t pad, int32_t kind) {
    return rig ? RIG(rig)->kitPadAddDevice(pad, kind) : -1;
}
int32_t nota_audition_kit_pad_device_param(NotaAudition* rig, int32_t pad, int32_t device, const char* name, float value) {
    return rig && name && RIG(rig)->kitPadDeviceParam(pad, device, name, value) ? 1 : 0;
}
int32_t nota_audition_set_source_file(NotaAudition* rig, const char* path, double max_seconds) {
    return rig && path && RIG(rig)->setSourceFile(path, max_seconds) ? 1 : 0;
}
int64_t nota_audition_render(NotaAudition* rig, const NotaAuditionNote* notes, int32_t count,
                             double bpm, double phrase_beats, double max_tail_seconds, int32_t flags) {
    if (!rig) return -1;
    return RIG(rig)->render(reinterpret_cast<const nota::AuditionNote*>(notes), count, bpm, phrase_beats,
                            max_tail_seconds, flags);
}
void nota_audition_cancel(NotaAudition* rig) { if (rig) RIG(rig)->cancel(); }
int32_t nota_audition_peaks(const NotaAudition* rig, float* out, int32_t max_points) {
    return rig ? CRIG(rig)->peaks(out, max_points) : 0;
}
double nota_audition_seconds(const NotaAudition* rig) { return rig ? CRIG(rig)->seconds() : 0.0; }

NotaResult nota_engine_audition_store(NotaEngine* e, const NotaAudition* rig, const char* key) {
    if (!e || !rig || !key || !CRIG(rig)->result()) return NOTA_ERR_INVALID_ARG;
    ENG(e)->auditionStore(key, CRIG(rig)->result());
    return NOTA_OK;
}
int32_t nota_engine_audition_cached(const NotaEngine* e, const char* key) {
    return e && key && CENG(e)->auditionCached(key) ? 1 : 0;
}
int32_t nota_engine_preview_cached_at(NotaEngine* e, const char* key, double start_seconds) {
    return e && key && ENG(e)->previewCached(key, start_seconds) ? 1 : 0;
}
int32_t nota_engine_preview_scope(const NotaEngine* e, float* out, int32_t n) {
    return e ? CENG(e)->previewScope(out, n) : 0;
}

} // extern "C"
