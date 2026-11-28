// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — progressive audio import, sample analysis, mono decode & Nota Aurora wavetables.

#include "nota/nota_engine.h"
#include "nota_engine_internal.h"

#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/media/AudioImport.h"
#include "nota/media/SampleAnalysis.h"
#include "nota/media/SampleBuffer.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <string>
#include <utility>

struct NotaAudioImport {
    std::unique_ptr<nota::media::AudioImportJob> job;
};

namespace nota::c_abi {

void fillAuroraInfo(const dsp::WavetableAsset* t, NotaAuroraTableInfo* out) noexcept {
    if (!out) return;
    std::memset(out, 0, sizeof(*out));
    if (!t) return;
    out->frames = t->frames;
    out->origin = t->origin;
    out->truncated_length = t->truncatedLength ? 1 : 0;
    out->truncated_frames = t->truncatedFrames ? 1 : 0;
    out->serial = t->serial;
    std::memcpy(out->hash, t->hash, sizeof(out->hash));
    const size_t n = std::min(t->name.size(), sizeof(out->name) - 1);
    if (n > 0) std::memcpy(out->name, t->name.data(), n);
    out->name[n] = '\0';
}

} // namespace nota::c_abi

namespace {

void fillAnalysis(const nota::media::SampleBuffer& b, int64_t frames, double totalFrames, NotaSampleAnalysis* out) {
    const nota::media::SampleFeatures f = nota::media::analyzeSample(b, frames);
    const double sr = b.sourceSampleRate > 0.0 ? b.sourceSampleRate : 44100.0;
    out->duration_sec = totalFrames / sr;
    out->sample_rate = b.sourceSampleRate;
    out->bpm = f.bpm;
    out->channels = b.channels;
    out->key_tonic = f.keyTonic;
    out->key_mode = f.keyMode;
    out->key_confidence = f.keyConfidence;
    out->tail_ratio = f.tailRatio;
    out->peak_db = f.peakDb;
    out->rms_db = f.rmsDb;
    for (int i = 0; i < NOTA_TIMBRE_DIMS; ++i) out->timbre[i] = f.timbre[static_cast<size_t>(i)];
}

int32_t makeTableResult(int32_t rc, std::shared_ptr<const nota::dsp::WavetableAsset> asset, NotaAuroraTable** out) {
    if (rc != nota::dsp::WavetableAsset::Ok) return rc;
    auto* handle = new (std::nothrow) NotaAuroraTable{std::move(asset)};
    if (!handle) return NOTA_AURORA_ERR_ARGS;
    *out = handle;
    return NOTA_AURORA_OK;
}

} // namespace

extern "C" {

NotaAudioImport* nota_audio_import_open(const char* path_utf8) {
    if (!path_utf8) return nullptr;
    auto job = nota::media::AudioImportJob::open(std::string(path_utf8));
    if (!job) return nullptr;
    return new (std::nothrow) NotaAudioImport{std::move(job)};
}

void nota_audio_import_close(NotaAudioImport* job) {
    delete job;
}

int32_t nota_audio_import_step(NotaAudioImport* job, int64_t max_frames) {
    return (job && job->job) ? job->job->step(max_frames) : -1;
}

NotaResult nota_audio_import_info(const NotaAudioImport* job, NotaAudioImportInfo* out) {
    if (!job || !job->job || !out) return NOTA_ERR_INVALID_ARG;
    const auto& buf = job->job->buffer();
    out->channels = buf ? buf->channels : 0;
    out->sample_rate = buf ? buf->sourceSampleRate : 0.0;
    out->total_frames = job->job->sourceFrames();
    out->decoded_frames = job->job->decodedFrames();
    out->done = job->job->done() ? 1 : 0;
    return NOTA_OK;
}

int32_t nota_audio_import_peaks(const NotaAudioImport* job, float* out_min_max, int32_t max_points) {
    return (job && job->job) ? job->job->peaks(out_min_max, max_points) : 0;
}

int64_t nota_audio_import_peak_table(const NotaAudioImport* job, float* out, int64_t max_floats) {
    if (!job || !job->job || !job->job->buffer()) return 0;
    const auto& pt = job->job->buffer()->peakTable;
    const int64_t n = static_cast<int64_t>(pt.size());
    if (!out) return n;
    const int64_t m = std::min(n, max_floats);
    if (m > 0) std::memcpy(out, pt.data(), static_cast<size_t>(m) * sizeof(float));
    return m;
}

int32_t nota_audio_import_seed_peak_table(NotaAudioImport* job, const float* table, int64_t count) {
    return (job && job->job && table && job->job->seedPeakTable(table, count)) ? 1 : 0;
}

double nota_audio_import_detect_tempo(const NotaAudioImport* job) {
    return (job && job->job) ? job->job->detectTempo() : 0.0;
}

int32_t nota_track_add_imported_clip(NotaEngine* e, int32_t track_id, const NotaAudioImport* job, double start_beat) {
    if (!e || !job || !job->job || !job->job->done()) return -1;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addAudioClipBuffer(job->job->buffer(), start_beat);
}

double nota_clip_auto_warp_bpm(NotaEngine* e, int32_t track_id, int32_t clip_index, double bpm) {
    if (!e) return 0.0;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).clip(clip_index).autoWarp(bpm);
}

NotaResult nota_audio_import_analyze(const NotaAudioImport* job, NotaSampleAnalysis* out) {
    if (!job || !job->job || !out || !job->job->done() || !job->job->buffer()) return NOTA_ERR_INVALID_ARG;
    const auto& buf = *job->job->buffer();
    fillAnalysis(buf, buf.frames, static_cast<double>(buf.frames), out);
    return NOTA_OK;
}

NotaResult nota_sample_analyze_file(const char* path_utf8, double max_seconds, NotaSampleAnalysis* out) {
    if (!path_utf8 || !out) return NOTA_ERR_INVALID_ARG;
    auto job = nota::media::AudioImportJob::open(std::string(path_utf8), max_seconds);
    if (!job) return NOTA_ERR_UNKNOWN;
    while (job->step(std::numeric_limits<int64_t>::max()) == 1) {}
    if (!job->done() || !job->buffer()) return NOTA_ERR_UNKNOWN;
    fillAnalysis(*job->buffer(), job->decodedFrames(), static_cast<double>(job->sourceFrames()), out);
    return NOTA_OK;
}

int64_t nota_file_decode_mono(const char* path_utf8, double max_seconds, float* out, int64_t cap, double* sample_rate) {
    if (!path_utf8) return 0;
    auto job = nota::media::AudioImportJob::open(std::string(path_utf8), max_seconds);
    if (!job) return 0;
    while (job->step(std::numeric_limits<int64_t>::max()) == 1) {}
    if (!job->done() || !job->buffer()) return 0;
    const auto& b = *job->buffer();
    if (sample_rate) *sample_rate = b.sourceSampleRate;
    const int64_t avail = job->decodedFrames();
    if (!out) return avail;
    if (cap <= 0 || b.channels <= 0) return 0;
    const int64_t n = std::min(avail, cap);
    const int32_t ch = b.channels;
    const float invCh = 1.0f / static_cast<float>(ch);
    for (int64_t i = 0; i < n; ++i) {
        float s = 0.0f;
        for (int32_t c = 0; c < ch; ++c) s += b.at(i, c);
        out[i] = s * invCh;
    }
    return n;
}

int32_t nota_aurora_table_build(const char* path_utf8, NotaAuroraTable** out) {
    if (!path_utf8 || !out) return NOTA_AURORA_ERR_ARGS;
    std::shared_ptr<const nota::dsp::WavetableAsset> asset;
    const int32_t rc = nota::dsp::WavetableAsset::buildFromFile(std::string(path_utf8), asset);
    return makeTableResult(rc, std::move(asset), out);
}

int32_t nota_aurora_table_from_frames(
    const float* frames, int32_t frame_count, const char* name_utf8, int32_t origin, NotaAuroraTable** out) {
    if (!frames || frame_count <= 0 || !out) return NOTA_AURORA_ERR_ARGS;
    std::shared_ptr<const nota::dsp::WavetableAsset> asset;
    const int32_t rc = nota::dsp::WavetableAsset::buildFromFrames(
        frames, frame_count, name_utf8 ? std::string(name_utf8) : std::string{}, origin, asset);
    return makeTableResult(rc, std::move(asset), out);
}

int32_t nota_aurora_table_info(const NotaAuroraTable* table, NotaAuroraTableInfo* out) {
    if (!table || !table->table || !out) return 0;
    nota::c_abi::fillAuroraInfo(table->table.get(), out);
    return 1;
}

int64_t nota_aurora_table_frames(const NotaAuroraTable* table, float* out, int64_t cap) {
    if (!table || !table->table) return 0;
    const auto& lv = table->table->level[0];
    const int64_t n = static_cast<int64_t>(lv.size());
    if (!out) return n;
    const int64_t m = std::min(n, cap);
    if (m > 0) std::memcpy(out, lv.data(), static_cast<size_t>(m) * sizeof(float));
    return m;
}

void nota_aurora_table_free(NotaAuroraTable* table) {
    delete table;
}

} // extern "C"
