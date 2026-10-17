// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;

namespace Nota.Application;

/// <summary>WAV sample format for export.</summary>
public enum WavBitDepth
{
    Pcm16,
    Pcm24,
    Float32,
}

/// <summary>Output format for export. FLAC reuses <see cref="WavBitDepth"/> (16/24
/// only — no float); MP3 replaces bit depth with a CBR bitrate.</summary>
public enum ExportFormat
{
    Wav,
    Flac,
    Mp3,
}

/// <summary>A master-export request: where, how long, and in what format. The
/// restore flags carry the transport toggles to reinstate after the offline bounce.
/// <paramref name="Normalize"/> peak-normalizes the bounce to −1 dBTP (true peak);
/// <paramref name="Dither"/> adds TPDF dither before 16-bit quantization (ignored
/// for 24-bit / float output and for MP3, which has no integer target).
/// <paramref name="Format"/> picks the file format (WAV default);
/// <paramref name="Mp3BitrateKbps"/> is the MP3 CBR bitrate (Format == Mp3 only).
/// <paramref name="StartBeats"/> is where the bounce starts; it lasts <paramref name="TotalBeats"/>.</summary>
public readonly record struct ExportRequest(
    string Path,
    double TotalBeats,
    double Bpm,
    int SampleRate,
    WavBitDepth Depth,
    bool RestoreLoop,
    bool RestoreMetronome,
    bool Normalize = false,
    bool Dither = false,
    ExportFormat Format = ExportFormat.Wav,
    int Mp3BitrateKbps = 320,
    double StartBeats = 0);

/// <summary>Renders the master bus offline to an audio file (WAV / FLAC / MP3 —
/// M6-4). Stops the audio backend for exclusive engine access, then restarts it;
/// caller must have suspended the UI clock first.</summary>
public interface IAudioExporter
{
    /// <param name="progress">Optional 0..1 sink reported as the bounce advances.</param>
    void ExportMaster(IAudioEngine engine, ExportRequest request, IProgress<double>? progress = null);

    /// <summary>Renders one file per non-return track (stems) into the folder given by
    /// <see cref="ExportRequest.Path"/>, in the request's format. Each track is
    /// isolated (others muted) and bounced offline; returns fold into each stem via
    /// their sends. Restores mutes + backend afterwards. Returns the number of stem
    /// files written.</summary>
    /// <param name="progress">Optional 0..1 sink reported across all stems.</param>
    int ExportStems(IAudioEngine engine, ExportRequest request, IProgress<double>? progress = null);
}
