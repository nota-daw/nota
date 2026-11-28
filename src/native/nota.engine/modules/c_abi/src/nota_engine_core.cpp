// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — engine lifecycle, transport, master mixer, realtime control,
// undo/redo, mix gestures, sections, meters, cue, preview, diagnostics & offline render.

#include "nota/nota_engine.h"
#include "nota/engine/Types.h"
#include "nota/engine/Transport.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "nota_engine_internal.h"

#include "nota/core/ConfigFile.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/pluginhost/PluginHostModule.h"
#include "nota/pluginscanner/PluginScannerModule.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <string>
#include <utility>
#include <vector>

namespace {

constexpr size_t kStackBufferLimit = 64;

} // namespace

extern "C" {

const char* nota_engine_version(void) {
    return nota::Engine::version();
}

NotaEngine* nota_engine_create(void) {
    nota::pluginhost::sharedPluginHost().setCatalog(nota::pluginscanner::sharedCatalogPtr());
    return new (std::nothrow) NotaEngine{};
}

void nota_engine_destroy(NotaEngine* engine) {
    delete engine;
}

NotaResult nota_set_data_dir(const char* path_utf8) {
    if (!path_utf8 || !*path_utf8) {
        nota::core::cfgfile::setDataDirOverride({});
        return NOTA_OK;
    }
    nota::core::cfgfile::setDataDirOverride(std::string(path_utf8));
    return NOTA_OK;
}

const char* nota_get_data_dir(void) {
    return nota::c_abi::cacheThreadString(nota::core::cfgfile::dataDir());
}

NotaResult nota_engine_start(NotaEngine* engine) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(engine).audio().start() ? NOTA_OK : NOTA_ERR_AUDIO_DEVICE;
}

NotaResult nota_engine_stop(NotaEngine* engine) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(engine).audio().stop();
    return NOTA_OK;
}

NotaResult nota_engine_set_test_tone(NotaEngine* engine, int32_t enabled) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(engine).preview().setToneEnabled(enabled != 0);
    return NOTA_OK;
}

NotaResult nota_engine_set_frequency(NotaEngine* engine, float hz) {
    if (!engine) return NOTA_ERR_INVALID_ARG;
    if (!(hz > 0.0f) || hz > 20000.0f) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(engine).preview().setToneFrequency(hz);
    return NOTA_OK;
}

double nota_engine_sample_rate(const NotaEngine* engine) {
    return engine ? nota::c_abi::ceng(engine).audio().sampleRate() : 0.0;
}

int32_t nota_engine_is_running(const NotaEngine* engine) {
    return (engine && nota::c_abi::ceng(engine).audio().isRunning()) ? 1 : 0;
}

// ---- Audio preview (M7-4a) -------------------------------------------------

NotaResult nota_engine_preview_file(NotaEngine* e, const char* path) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).preview().file(std::string(path)) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NotaResult nota_engine_stop_preview(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).preview().stop();
    return NOTA_OK;
}

int32_t nota_engine_preview_active(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).preview().active()) ? 1 : 0;
}

NotaResult nota_engine_preview_file_at(NotaEngine* e, const char* path, double start_seconds) {
    if (!e || !path) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).preview().file(std::string(path), start_seconds) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NotaResult nota_engine_set_preview_loop(NotaEngine* e, int32_t on) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).preview().setLoop(on != 0);
    return NOTA_OK;
}

NotaResult nota_engine_set_preview_gain(NotaEngine* e, float gain) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).preview().setGain(gain);
    return NOTA_OK;
}

double nota_engine_preview_position(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).preview().position() : 0.0;
}

int32_t nota_engine_preview_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).preview().selfTest()) ? 1 : 0;
}

// ---- Telemetry & Streaming & Hybrid ----------------------------------------

int32_t nota_engine_xrun_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().xrunCount() : 0;
}

int32_t nota_engine_xrun_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().xrunSelfTest()) ? 1 : 0;
}

int32_t nota_engine_graph_retire_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().graphRetireSelfTest()) ? 1 : 0;
}

float nota_engine_cpu_load(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().cpuLoad() : 0.0f;
}

NotaResult nota_engine_set_device_telemetry(NotaEngine* e, int32_t mode) {
    if (!e || mode < NOTA_TELEMETRY_ON_DEMAND || mode > NOTA_TELEMETRY_ALWAYS_ON) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().setTelemetryMode(static_cast<nota::dsp::TelemetryMode>(mode));
    return NOTA_OK;
}

int32_t nota_engine_device_telemetry(const NotaEngine* e) {
    return e ? static_cast<int32_t>(nota::c_abi::ceng(e).diagnostics().telemetryMode()) : NOTA_TELEMETRY_ON_DEMAND;
}

NotaResult nota_engine_set_render_threads(NotaEngine* e, int32_t count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).diagnostics().setRenderThreads(count) ? NOTA_OK : NOTA_ERR_ALREADY_RUNNING;
}

int32_t nota_engine_render_threads(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().renderThreads() : 0;
}

double nota_engine_render_threads_cpu_seconds(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().renderThreadsCpuSeconds() : 0.0;
}

NotaResult nota_engine_set_stream_threshold_bytes(NotaEngine* e, int64_t bytes) {
    if (!e || bytes < 0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().setStreamThresholdBytes(bytes);
    return NOTA_OK;
}

int64_t nota_engine_stream_threshold_bytes(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).diagnostics().streamThresholdBytes() : 0;
}

NotaResult nota_engine_set_sample_load_policy(NotaEngine* e, int32_t policy) {
    if (!e || policy < NOTA_SAMPLE_LOAD_AUTO || policy > NOTA_SAMPLE_LOAD_STREAM) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().setSampleLoadPolicy(static_cast<nota::media::SampleLoadPolicy>(policy));
    return NOTA_OK;
}

int32_t nota_engine_sample_load_policy(const NotaEngine* e) {
    return e ? static_cast<int32_t>(nota::c_abi::ceng(e).diagnostics().sampleLoadPolicy()) : NOTA_SAMPLE_LOAD_AUTO;
}

int32_t nota_sample_is_streaming(const NotaEngine* e, int64_t sample_id) {
    return (e && nota::c_abi::ceng(e).diagnostics().isSampleStreaming(sample_id)) ? 1 : 0;
}

NotaResult nota_engine_streaming_stats(const NotaEngine* e, NotaStreamingStats* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    const auto st = nota::c_abi::ceng(e).diagnostics().streamingStats();
    out->active_streams = st.activeStreams;
    out->stream_threshold_bytes = st.streamThresholdBytes;
    out->stream_cache_bytes = st.streamCacheBytes;
    out->resident_sample_bytes = st.residentSampleBytes;
    out->pages_faulted = st.pagesFaulted;
    out->pages_served_async = st.pagesServedAsync;
    out->pages_served_sync = st.pagesServedSync;
    out->rt_underruns = st.rtUnderruns;
    return NOTA_OK;
}

NotaResult nota_engine_set_hybrid_buffering(NotaEngine* e, int32_t mode) {
    if (!e || (mode != NOTA_HYBRID_OFF && mode != NOTA_HYBRID_AUTO)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().setHybridBufferingMode(static_cast<nota::engine::HybridMode>(mode));
    return NOTA_OK;
}

int32_t nota_engine_hybrid_buffering(const NotaEngine* e) {
    return e ? static_cast<int32_t>(nota::c_abi::ceng(e).diagnostics().hybridBufferingMode()) : NOTA_HYBRID_OFF;
}

NotaResult nota_engine_hybrid_stats(const NotaEngine* e, NotaHybridBufferStats* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    const auto st = nota::c_abi::ceng(e).diagnostics().hybridStats();
    out->active = st.active;
    out->live_tracks = st.liveTracks;
    out->anticipative_tracks = st.anticipativeTracks;
    out->lookahead_frames = st.lookaheadFrames;
    out->chunk_frames = st.chunkFrames;
    out->cache_hits = st.cacheHits;
    out->cache_misses = st.cacheMisses;
    out->invalidations = st.invalidations;
    return NOTA_OK;
}

// ---- Transport -------------------------------------------------------------

NotaResult nota_transport_play(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().play();
    return NOTA_OK;
}

NotaResult nota_transport_play_count_in(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().playCountIn();
    return NOTA_OK;
}

NotaResult nota_transport_set_count_in(NotaEngine* e, int32_t bars) {
    if (!e || bars < 0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().setCountInBars(bars);
    return NOTA_OK;
}

double nota_transport_count_in_beats(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).transport().countInBeatsRemaining() : 0.0;
}

NotaResult nota_transport_set_loop_record_mode(NotaEngine* e, int32_t mode) {
    if (!e || mode < 0 || mode > 1) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().setLoopRecordReplace(mode == 1);
    return NOTA_OK;
}

int32_t nota_transport_loop_record_mode(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).transport().loopRecordReplace()) ? 1 : 0;
}

NotaResult nota_transport_stop(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().stop();
    return NOTA_OK;
}

NotaResult nota_transport_set_bpm(NotaEngine* e, double bpm) {
    if (!e || !(bpm > 0.0) || bpm > 999.0) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).transport().setBpm(bpm) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

double nota_transport_bpm(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).transport().bpm() : 120.0;
}

double nota_tempo_beat_to_seconds(const NotaEngine* e, double b) {
    return e ? nota::c_abi::ceng(e).transport().beatToSeconds(b) : 0.0;
}

double nota_tempo_seconds_to_beat(const NotaEngine* e, double s) {
    return e ? nota::c_abi::ceng(e).transport().secondsToBeat(s) : 0.0;
}

double nota_tempo_bpm_at(const NotaEngine* e, double b) {
    return e ? nota::c_abi::ceng(e).transport().bpmAt(b) : 120.0;
}

int32_t nota_engine_tempo_automated(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).transport().tempoAutomated()) ? 1 : 0;
}

int32_t nota_engine_tempo_lane_selftest(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().tempoLaneSelfTest() : -1;
}

int32_t nota_engine_session_pass_selftest(NotaEngine* e) {
    return e ? nota::Engine::Diagnostics::sessionPassSelfTest() : -1;
}

NotaResult nota_transport_set_time_signature(NotaEngine* e, int32_t num, int32_t denom) {
    if (!e || num <= 0 || denom <= 0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().setTimeSignature(num, denom);
    return NOTA_OK;
}

NotaResult nota_transport_set_loop(NotaEngine* e, int32_t enabled, double s, double en) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().setLoop(enabled != 0, s, en);
    return NOTA_OK;
}

NotaResult nota_transport_set_metronome(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().setMetronome(enabled != 0);
    return NOTA_OK;
}

NotaResult nota_transport_seek(NotaEngine* e, double beat) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).transport().seekBeats(beat);
    return NOTA_OK;
}

double nota_transport_position_beats(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).transport().positionBeats() : 0.0;
}

int32_t nota_transport_snapshot(const NotaEngine* e, NotaTransportSnapshot* out_snapshot) {
    if (!e || !out_snapshot) return 0;
    nota::engine::TransportSnapshot snap{};
    if (!nota::c_abi::ceng(e).transport().snapshot(snap)) return 0;
    out_snapshot->start_beat = snap.startBeat;
    out_snapshot->position_beats = snap.positionBeats;
    out_snapshot->bpm = snap.bpm;
    out_snapshot->loop_start = snap.loopStart;
    out_snapshot->loop_end = snap.loopEnd;
    out_snapshot->duration_seconds = snap.durationSeconds;
    out_snapshot->elapsed_seconds = snap.elapsedSeconds;
    out_snapshot->playing = snap.playing;
    out_snapshot->looping = snap.looping;
    return 1;
}

int32_t nota_transport_is_playing(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).transport().isPlaying()) ? 1 : 0;
}

int32_t nota_transport_loop_enabled(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).transport().loopEnabled()) ? 1 : 0;
}

double nota_transport_loop_start(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).transport().loopStart() : 0.0;
}

double nota_transport_loop_end(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).transport().loopEnd() : 0.0;
}

// ---- Mixer -----------------------------------------------------------------

NotaResult nota_engine_set_master_volume(NotaEngine* e, float v) {
    if (!e || v < 0.0f || v > 4.0f) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).mixer().setMasterVolume(v);
    return NOTA_OK;
}

NotaResult nota_engine_set_master_pan(NotaEngine* e, float p) {
    if (!e || !std::isfinite(p)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).mixer().setMasterPan(p);
    return NOTA_OK;
}

float nota_engine_master_pan(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().masterPan() : 0.0f;
}

int32_t nota_engine_master_automation_selftest(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().masterAutomationSelfTest() : -1;
}

// ---- Live MIDI, arming & recording (M2) ------------------------------------

NotaResult nota_engine_note_on(NotaEngine* e, int32_t pitch, float velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().noteOn(pitch, velocity);
    return NOTA_OK;
}

NotaResult nota_engine_note_off(NotaEngine* e, int32_t pitch) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().noteOff(pitch);
    return NOTA_OK;
}

NotaResult nota_engine_note_off_velocity(NotaEngine* e, int32_t pitch, float release_velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().noteOff(pitch, release_velocity);
    return NOTA_OK;
}

NotaResult nota_track_note_on(NotaEngine* e, int32_t track_id, int32_t pitch, float velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).noteOn(pitch, velocity);
    return NOTA_OK;
}

NotaResult nota_track_note_off(NotaEngine* e, int32_t track_id, int32_t pitch) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).noteOff(pitch);
    return NOTA_OK;
}

NotaResult nota_track_note_off_velocity(NotaEngine* e, int32_t track_id, int32_t pitch, float release_velocity) {
    if (!e || pitch < 0 || pitch > 127) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).noteOff(pitch, release_velocity);
    return NOTA_OK;
}

NotaResult nota_engine_note_expression(NotaEngine* e, int32_t pitch, int32_t dim, float value) {
    if (!e || pitch < -1 || pitch > 127 || dim < 0 || dim > 3 || (dim == 3 && pitch >= 0)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().noteExpression(pitch, dim, value);
    return NOTA_OK;
}

NotaResult nota_track_note_expression(NotaEngine* e, int32_t track_id, int32_t pitch, int32_t dim, float value) {
    if (!e || pitch < -1 || pitch > 127 || dim < 0 || dim > 3 || (dim == 3 && pitch >= 0)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).noteExpression(pitch, dim, value);
    return NOTA_OK;
}

NotaResult nota_engine_controller(NotaEngine* e, int32_t ctl, float value) {
    if (!e || nota::dsp::ctlIsReserved(ctl) || !std::isfinite(value)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).midi().controller(ctl, value);
    return NOTA_OK;
}

NotaResult nota_track_controller(NotaEngine* e, int32_t track_id, int32_t ctl, float value) {
    if (!e || nota::dsp::ctlIsReserved(ctl) || !std::isfinite(value)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).controller(ctl, value);
    return NOTA_OK;
}

float nota_track_delivered_controller(const NotaEngine* e, int32_t track_id, int32_t ctl) {
    if (!e || ctl < 0 || ctl >= nota::dsp::kCtlCount) return std::numeric_limits<float>::quiet_NaN();
    const auto tr = nota::c_abi::ceng(e).tracks().byId(track_id);
    return tr.isValid() ? tr.deliveredController(ctl) : std::numeric_limits<float>::quiet_NaN();
}

int32_t nota_engine_midi_ctl_selftest(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().midiCtlSelfTest() : -1;
}

void nota_engine_set_audition_track(NotaEngine* e, int32_t track_id) {
    if (e) nota::c_abi::eng(e).preview().setAuditionTrack(track_id);
}

NotaResult nota_engine_set_recording(NotaEngine* e, int32_t enabled) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).recorder().setRecording(enabled != 0);
    return NOTA_OK;
}

int32_t nota_engine_is_recording(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).recorder().isRecording()) ? 1 : 0;
}

int32_t nota_engine_retry_take(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).recorder().retryTake() : 0;
}

int32_t nota_engine_session_pass_spans(const NotaEngine* e, NotaPassSpan* out, int32_t cap) {
    if (!e) return 0;
    const auto sess = nota::c_abi::ceng(e).session();
    if (!out || cap <= 0) return sess.passSpans(nullptr, 0);
    std::array<nota::engine::PassSpan, kStackBufferLimit> stack{};
    std::vector<nota::engine::PassSpan> heap;
    nota::engine::PassSpan* dst = nullptr;
    if (static_cast<size_t>(cap) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(cap));
        dst = heap.data();
    }
    const int32_t n = sess.passSpans(dst, cap);
    const int32_t m = std::min(n, cap);
    for (int32_t i = 0; i < m; ++i) {
        out[i] = {
            dst[i].trackId, dst[i].slot, dst[i].startBeat, dst[i].endBeat, dst[i].slotPos, dst[i].open, dst[i].warped};
    }
    return n;
}

int32_t nota_engine_record_start_status(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).recorder().startStatus() : 0;
}

int32_t nota_engine_audio_record_track(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).recorder().audioRecordTrackId() : 0;
}

double nota_engine_audio_record_start_beat(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).recorder().audioRecordStartBeat() : 0.0;
}

double nota_engine_audio_record_length_beats(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).recorder().audioRecordLengthBeats() : 0.0;
}

int32_t nota_engine_master_track_id(const NotaEngine* e) {
    return e ? nota::Engine::Mixer::masterTrackId() : 0;
}

int32_t nota_engine_audio_record_peaks(const NotaEngine* e, float* out, int32_t maxPoints) {
    return e ? nota::c_abi::ceng(e).recorder().audioRecordPeaks(out, maxPoints) : 0;
}

int32_t nota_engine_recording_tracks(const NotaEngine* e, int32_t* out, int32_t cap) {
    return e ? nota::c_abi::ceng(e).recorder().audioRecordingTracks(out, cap) : 0;
}

double nota_engine_audio_record_length_beats_of(const NotaEngine* e, int32_t track_id) {
    return (e && track_id > 0) ? nota::c_abi::ceng(e).recorder().audioRecordLengthBeats(track_id) : 0.0;
}

int32_t nota_engine_audio_record_peaks_of(const NotaEngine* e, int32_t track_id, float* out, int32_t maxPoints) {
    return (e && track_id > 0) ? nota::c_abi::ceng(e).recorder().audioRecordPeaks(out, maxPoints, track_id) : 0;
}

int32_t nota_engine_record_skipped(const NotaEngine* e, int32_t* track_ids, int32_t* reasons, int32_t cap) {
    return e ? nota::c_abi::ceng(e).recorder().skipped(track_ids, reasons, cap) : 0;
}

int32_t nota_audio_record_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().audioRecordSelfTest()) ? 1 : 0;
}

NotaResult nota_engine_poll(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).recorder().poll();
    return NOTA_OK;
}

// ---- Undo / redo (M6-6) ----------------------------------------------------

NotaResult nota_engine_undo(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::engine::internal::EngineDirect::undo(nota::c_abi::eng(e)) ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

NotaResult nota_engine_redo(NotaEngine* e) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).history().redo() ? NOTA_OK : NOTA_ERR_UNKNOWN;
}

int32_t nota_engine_can_undo(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).history().canUndo()) ? 1 : 0;
}

int32_t nota_engine_can_redo(const NotaEngine* e) {
    return (e && nota::c_abi::ceng(e).history().canRedo()) ? 1 : 0;
}

void nota_engine_undo_group_begin(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).history().beginGroup();
}

void nota_engine_undo_group_end(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).history().endGroup();
}

void nota_engine_clear_undo_history(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).history().clear();
}

// ---- Value undo (009, 016) -------------------------------------------------

int32_t nota_mix_gesture_begin(NotaEngine* e, const NotaMixTarget* targets, int32_t count) {
    if (!e || !targets || count <= 0 || count > 64) return 0;
    std::array<nota::engine::MixTargetSpec, 64> t{};
    int32_t n = 0;
    for (int32_t i = 0; i < count; ++i) {
        const int32_t k = targets[i].kind;
        if ((k < NOTA_MIX_TRACK_VOLUME || k > NOTA_MIX_PRESET_TAG) && k != NOTA_MIX_TRACK_SEND) continue;
        t[static_cast<size_t>(n++)] = {static_cast<nota::engine::MixTargetKind>(k), targets[i].track_id,
            targets[i].device_index, targets[i].param_index};
    }
    return n > 0 ? nota::c_abi::eng(e).history().beginMixGesture(t.data(), n) : 0;
}

void nota_mix_gesture_end(NotaEngine* e, int32_t handle) {
    if (e) nota::c_abi::eng(e).history().endMixGesture(handle);
}

float nota_engine_master_volume(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().masterVolume() : 1.0f;
}

int32_t nota_engine_mix_undo_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).history().mixUndoSelfTest()) ? 1 : 0;
}

// ---- Stereo measurements (008) ---------------------------------------------

int32_t nota_engine_stereo_measures(const NotaEngine* e, NotaStereoMeasure* out, int32_t cap) {
    if (!e) return 0;
    const int32_t safeCap = std::max<int32_t>(0, cap);
    const auto mixer = nota::c_abi::ceng(e).mixer();
    if (!out || safeCap == 0) return mixer.stereoMeasures(nullptr, 0);
    std::array<nota::engine::StereoMeasure, kStackBufferLimit> stack{};
    std::vector<nota::engine::StereoMeasure> heap;
    nota::engine::StereoMeasure* dst = nullptr;
    if (static_cast<size_t>(safeCap) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(safeCap));
        dst = heap.data();
    }
    const int32_t n = mixer.stereoMeasures(dst, safeCap);
    const int32_t m = std::min(n, safeCap);
    for (int32_t i = 0; i < m; ++i)
        out[i] = {dst[i].trackId, dst[i].levelDb, dst[i].balance, dst[i].width, dst[i].correlation};
    return n;
}

// ---- Pan law (006) ---------------------------------------------------------

NotaResult nota_engine_set_pan_law(NotaEngine* e, int32_t law, int32_t checkpoint) {
    if (!e || (law != NOTA_PAN_LAW_BALANCE && law != NOTA_PAN_LAW_STEREO)) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::eng(e).mixer().setPanLaw(static_cast<nota::engine::PanLawMode>(law), checkpoint != 0)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_engine_pan_law(const NotaEngine* e) {
    return e ? static_cast<int32_t>(nota::c_abi::ceng(e).mixer().panLaw()) : NOTA_PAN_LAW_BALANCE;
}

// ---- Arrangement sections (016) --------------------------------------------

int32_t nota_engine_section_count(const NotaEngine* e) {
    return e ? static_cast<int32_t>(nota::c_abi::ceng(e).mixer().sections().size()) : 0;
}

double nota_engine_section_start(const NotaEngine* e, int32_t i) {
    if (!e || i < 0) return 0.0;
    const auto sec = nota::c_abi::ceng(e).mixer().sections();
    return static_cast<size_t>(i) < sec.size() ? sec[static_cast<size_t>(i)].startBeat : 0.0;
}

double nota_engine_section_length(const NotaEngine* e, int32_t i) {
    if (!e || i < 0) return 0.0;
    const auto sec = nota::c_abi::ceng(e).mixer().sections();
    return static_cast<size_t>(i) < sec.size() ? sec[static_cast<size_t>(i)].lengthBeats : 0.0;
}

const char* nota_engine_section_name(const NotaEngine* e, int32_t i) {
    if (!e || i < 0) return "";
    const auto sec = nota::c_abi::ceng(e).mixer().sections();
    if (static_cast<size_t>(i) >= sec.size()) return "";
    if (e->sectionNamesCache.size() != sec.size()) e->sectionNamesCache.resize(sec.size());
    auto& slot = e->sectionNamesCache[static_cast<size_t>(i)];
    slot = sec[static_cast<size_t>(i)].name;
    return slot.c_str();
}

NotaResult nota_engine_set_sections(NotaEngine* e, const NotaSection* sections, int32_t count, int32_t checkpoint) {
    if (!e || count < 0 || (count > 0 && !sections)) return NOTA_ERR_INVALID_ARG;
    std::vector<nota::engine::SectionInfo> v;
    v.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i)
        v.push_back(
            {sections[i].start_beat, sections[i].length_beats, sections[i].name_utf8 ? sections[i].name_utf8 : ""});
    nota::c_abi::eng(e).mixer().setSections(std::move(v), checkpoint != 0);
    return NOTA_OK;
}

NotaResult nota_section_move(NotaEngine* e, int32_t index, int32_t to_boundary) {
    return (e && nota::c_abi::eng(e).mixer().moveSection(index, to_boundary)) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_section_duplicate_with_content(NotaEngine* e, int32_t index) {
    return (e && nota::c_abi::eng(e).mixer().duplicateSectionWithContent(index)) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_section_delete_with_content(NotaEngine* e, int32_t index) {
    return (e && nota::c_abi::eng(e).mixer().deleteSectionWithContent(index)) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_section_overlaps(const NotaEngine* e, int32_t index) {
    return (e && nota::c_abi::ceng(e).mixer().sectionOverlaps(index)) ? 1 : 0;
}

// ---- Project reset (M7-6) --------------------------------------------------

void nota_engine_reset(NotaEngine* e) {
    if (e) nota::c_abi::eng(e).reset();
}

// ---- Meters (M6-2) ---------------------------------------------------------

int32_t nota_engine_track_meter(const NotaEngine* e, int32_t track_id, NotaMeter* out) {
    if (!e || !out) return 0;
    return nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .meter(out->peak_l, out->peak_r, out->rms_l, out->rms_r)
               ? 1
               : 0;
}

int32_t nota_engine_read_track_meters(const NotaEngine* e, NotaTrackMeterEntry* out_entries, int32_t capacity) {
    if (!e) return 0;
    const auto mixer = nota::c_abi::ceng(e).mixer();
    if (!out_entries || capacity <= 0) return mixer.readTrackMeters(nullptr, 0);
    std::array<nota::engine::TrackMeterEntry, kStackBufferLimit> stack{};
    std::vector<nota::engine::TrackMeterEntry> heap;
    nota::engine::TrackMeterEntry* dst = nullptr;
    if (static_cast<size_t>(capacity) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(capacity));
        dst = heap.data();
    }
    const int32_t n = mixer.readTrackMeters(dst, capacity);
    const int32_t m = std::min(n, capacity);
    for (int32_t i = 0; i < m; ++i)
        out_entries[i] = {
            dst[i].trackId, {dst[i].meter.peakL, dst[i].meter.peakR, dst[i].meter.rmsL, dst[i].meter.rmsR}};
    return n;
}

NotaResult nota_engine_master_meter(const NotaEngine* e, NotaMeter* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::ceng(e).mixer().masterMeter(out->peak_l, out->peak_r, out->rms_l, out->rms_r);
    return NOTA_OK;
}

// ---- Cue (specs/089) -------------------------------------------------------

NotaResult nota_engine_set_cue_volume(NotaEngine* e, float gain) {
    if (!e || !std::isfinite(gain)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).mixer().setCueVolume(gain);
    return NOTA_OK;
}

float nota_engine_cue_volume(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().cueVolume() : 0.0f;
}

NotaResult nota_engine_cue_meter(const NotaEngine* e, NotaMeter* out) {
    if (!e || !out) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::ceng(e).mixer().cueMeter(out->peak_l, out->peak_r, out->rms_l, out->rms_r);
    return NOTA_OK;
}

NotaResult nota_transport_set_click_destination(NotaEngine* e, int32_t dest) {
    if (!e || dest < 0 || dest > 2) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).mixer().setClickDestination(dest);
    return NOTA_OK;
}

int32_t nota_transport_click_destination(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().clickDestination() : 0;
}

NotaResult nota_transport_set_click_volume(NotaEngine* e, float gain) {
    if (!e || !std::isfinite(gain)) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).mixer().setClickVolume(gain);
    return NOTA_OK;
}

// ---- Offline render --------------------------------------------------------

NotaResult nota_engine_render_offline(NotaEngine* e, float* out, int32_t frames) {
    if (!e || !out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().renderOffline(out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_device_block_cue(NotaEngine* e, float* main_out, float* cue_out, int32_t frames) {
    if (!e || !main_out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    auto audio = nota::c_abi::eng(e).audio();
    if (audio.isRunning()) return NOTA_ERR_ALREADY_RUNNING;
    audio.renderDeviceBlock(main_out, cue_out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_device_block(NotaEngine* e, float* out, int32_t frames) {
    if (!e || !out || frames <= 0) return NOTA_ERR_INVALID_ARG;
    auto audio = nota::c_abi::eng(e).audio();
    if (audio.isRunning()) return NOTA_ERR_ALREADY_RUNNING;
    audio.renderDeviceBlock(out, frames);
    return NOTA_OK;
}

NotaResult nota_engine_render_offline_at(NotaEngine* e, float* out, int32_t frames, double sr) {
    if (!e || !out || frames <= 0 || sr <= 0.0) return NOTA_ERR_INVALID_ARG;
    nota::c_abi::eng(e).diagnostics().renderOffline(out, frames, sr);
    return NOTA_OK;
}

} // extern "C"
