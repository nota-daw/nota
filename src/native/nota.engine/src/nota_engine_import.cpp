// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — background audio import (decode off the UI thread, then place the buffer).

#include "nota_engine_internal.h"
#include "AudioImport.h"

#include <algorithm>
#include <cstring>
#include <string>

#define JOB(j) reinterpret_cast<nota::AudioImportJob*>(j)
#define CJOB(j) reinterpret_cast<const nota::AudioImportJob*>(j)

extern "C" {

NotaAudioImport* nota_audio_import_open(const char* path) {
    if (!path) return nullptr;
    return reinterpret_cast<NotaAudioImport*>(nota::AudioImportJob::open(std::string(path)).release());
}
void nota_audio_import_close(NotaAudioImport* job) {
    delete JOB(job);
}
int32_t nota_audio_import_step(NotaAudioImport* job, int64_t max_frames) {
    return job ? JOB(job)->step(max_frames) : -1;
}
NotaResult nota_audio_import_info(const NotaAudioImport* job, NotaAudioImportInfo* out) {
    if (!job || !out) return NOTA_ERR_INVALID_ARG;
    const auto& b = CJOB(job)->buffer();
    out->channels = b ? b->channels : 0;
    out->sample_rate = b ? b->sourceSampleRate : 0.0;
    out->total_frames = b ? b->frames : 0;
    out->decoded_frames = CJOB(job)->decodedFrames();
    out->done = CJOB(job)->done() ? 1 : 0;
    return NOTA_OK;
}
int32_t nota_audio_import_peaks(const NotaAudioImport* job, float* out_min_max, int32_t max_points) {
    return job ? CJOB(job)->peaks(out_min_max, max_points) : 0;
}
int64_t nota_audio_import_peak_table(const NotaAudioImport* job, float* out, int64_t max_floats) {
    if (!job || !CJOB(job)->buffer()) return 0;
    const auto& t = CJOB(job)->buffer()->peakTable;
    const int64_t n = static_cast<int64_t>(t.size());
    if (out && max_floats > 0) std::memcpy(out, t.data(), sizeof(float) * static_cast<size_t>(std::min(n, max_floats)));
    return n;
}
int32_t nota_audio_import_seed_peak_table(NotaAudioImport* job, const float* table, int64_t count) {
    return job && JOB(job)->seedPeakTable(table, count) ? 1 : 0;
}
double nota_audio_import_detect_tempo(const NotaAudioImport* job) {
    return job ? CJOB(job)->detectTempo() : 0.0;
}
int32_t nota_track_add_imported_clip(NotaEngine* e, int32_t track_id, const NotaAudioImport* job, double start_beat) {
    if (!e || !job || !CJOB(job)->done()) return -1;
    return ENG(e)->addAudioClipBuffer(track_id, CJOB(job)->buffer(), start_beat);
}
double nota_clip_auto_warp_bpm(NotaEngine* e, int32_t track_id, int32_t clip_index, double bpm) {
    return e ? ENG(e)->autoWarpClip(track_id, clip_index, bpm) : 0.0;
}

} // extern "C"
