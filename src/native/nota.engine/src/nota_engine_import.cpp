// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — background audio import (decode off the UI thread, then place the buffer).

#include "nota_engine_internal.h"
#include "AudioImport.h"
#include "SampleAnalysis.h"

#include <algorithm>
#include <cstring>
#include <string>

#define JOB(j) reinterpret_cast<nota::AudioImportJob*>(j)
#define CJOB(j) reinterpret_cast<const nota::AudioImportJob*>(j)

namespace {
void fillAnalysis(const nota::SampleBuffer& b, int64_t frames, double totalFrames, NotaSampleAnalysis* out) {
    const auto f = nota::analyzeSample(b, frames);
    *out = NotaSampleAnalysis{};
    out->sample_rate = b.sourceSampleRate;
    out->duration_sec = b.sourceSampleRate > 0 ? totalFrames / b.sourceSampleRate : 0.0;
    out->channels = b.channels;
    out->bpm = f.bpm;
    out->key_tonic = f.keyTonic;
    out->key_mode = f.keyMode;
    out->key_confidence = f.keyConfidence;
    out->tail_ratio = f.tailRatio;
    out->peak_db = f.peakDb;
    out->rms_db = f.rmsDb;
    std::copy(f.timbre.begin(), f.timbre.end(), out->timbre);
}
} // namespace

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


NotaResult nota_audio_import_analyze(const NotaAudioImport* job, NotaSampleAnalysis* out) {
    if (!job || !out || !CJOB(job)->done() || !CJOB(job)->buffer()) return NOTA_ERR_INVALID_ARG;
    const auto& b = *CJOB(job)->buffer();
    fillAnalysis(b, b.frames, static_cast<double>(b.frames), out);
    return NOTA_OK;
}

NotaResult nota_sample_analyze_file(const char* path, double max_seconds, NotaSampleAnalysis* out) {
    if (!path || !out) return NOTA_ERR_INVALID_ARG;
    // Only the head is decoded (and allocated): a long file costs max_seconds, not its length.
    auto job = nota::AudioImportJob::open(std::string(path), max_seconds);
    if (!job || !job->buffer()) return NOTA_ERR_INVALID_ARG;
    while (!job->done())
        if (job->step(1 << 17) < 0) return NOTA_ERR_INVALID_ARG;
    const auto& b = *job->buffer();
    if (b.frames <= 0) return NOTA_ERR_INVALID_ARG;
    // Uncapped (or a truncated file): what decoded is the file; capped: the header's length.
    const bool capped = b.frames < job->sourceFrames() && job->decodedFrames() >= b.frames;
    fillAnalysis(b, b.frames, static_cast<double>(capped ? job->sourceFrames() : b.frames), out);
    return NOTA_OK;
}

} // extern "C"
