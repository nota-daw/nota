// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — offline export of the project to WAV / FLAC / MP3: the whole master
// bus, or one stem per track. The bounce is rendered offline at the project
// tempo/sample rate; playback is stopped first and the transport toggles restored
// afterwards. bitDepth = pcm16 | pcm24 | float32 (wav/flac), format = wav | flac | mp3,
// bitrateKbps = 128 | 192 | 256 | 320 (mp3).

using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class ExportTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IAudioExporter exporter)
    : EngineTools(engine, dispatch, refresh)
{
    private readonly IAudioExporter _exporter = exporter;

    private static WavBitDepth ParseDepth(string s) => s.ToLowerInvariant() switch
    {
        "pcm16" or "16" => WavBitDepth.Pcm16,
        "pcm24" or "24" => WavBitDepth.Pcm24,
        _ => WavBitDepth.Float32,
    };

    private static ExportFormat ParseFormat(string s) => s.ToLowerInvariant() switch
    {
        "flac" => ExportFormat.Flac,
        "mp3" => ExportFormat.Mp3,
        _ => ExportFormat.Wav,
    };

    private static readonly int[] ValidBitrates = { 128, 192, 256, 320 };

    private ExportRequest BuildRequest(string path, double totalBeats, string bitDepth,
                                       string format, int bitrateKbps, double startBeat)
    {
        int sr = (int)Math.Round(E.SampleRate);
        if (sr <= 0) sr = 48000;   // engine hasn't negotiated a device rate yet — pick a sane default
        var fmt = ParseFormat(format);

        // MP3 carries 44.1/48 kHz only (MPEG-1); clamp an unusual device rate (the
        // tool result says so) rather than silently writing a wrong-rate file.
        if (fmt == ExportFormat.Mp3 && sr != 44100 && sr != 48000) sr = 48000;

        // float32 has no FLAC target; float requests fall back to pcm24, matching
        // the dialog's FLAC depth list.
        var depth = ParseDepth(bitDepth);
        if (fmt == ExportFormat.Flac && depth == WavBitDepth.Float32) depth = WavBitDepth.Pcm24;

        int kbps = ValidBitrates.Contains(bitrateKbps) ? bitrateKbps : 320;
        return new ExportRequest(path, Math.Max(0.25, totalBeats), E.Bpm > 0 ? E.Bpm : 120, sr, depth,
            RestoreLoop: E.LoopEnabled, RestoreMetronome: false,
            Format: fmt, Mp3BitrateKbps: kbps, StartBeats: Math.Max(0, startBeat));
    }

    [McpServerTool(Name = "export_master"), Description(
        "Render the master bus offline to an audio file. path = output file, totalBeats = how many beats to render, "
        + "startBeat = where the render starts (default 0), "
        + "bitDepth = pcm16 | pcm24 | float32 (wav/flac), format = wav | flac | mp3 (default wav), "
        + "bitrateKbps = 128 | 192 | 256 | 320 (mp3 only, default 320). Stops playback first. Returns the output path.")]
    public Task<string> ExportMaster(string path, double totalBeats, string bitDepth = "float32",
                                     string format = "wav", int bitrateKbps = 320, double startBeat = 0) => Mutate(() =>
    {
        E.StopTransport();
        var req = BuildRequest(path, totalBeats, bitDepth, format, bitrateKbps, startBeat);
        _exporter.ExportMaster(E, req);
        string note = req.Format == ExportFormat.Wav ? "" : $" ({format.ToLowerInvariant()})";
        if (req.Format == ExportFormat.Mp3 && (int)Math.Round(E.SampleRate) > 48000)
            note += " [rate capped at 48 kHz]";
        return path + note;
    });

    [McpServerTool(Name = "export_stems"), Description(
        "Render one file per non-return track (stems) into the given folder. folder = output directory, "
        + "totalBeats = how many beats to render, startBeat = where the render starts (default 0), bitDepth = pcm16 | pcm24 | float32 (wav/flac), "
        + "format = wav | flac | mp3 (default wav), bitrateKbps = 128 | 192 | 256 | 320 (mp3 only, default 320). "
        + "Returns the number of stems written.")]
    public Task<int> ExportStems(string folder, double totalBeats, string bitDepth = "float32",
                                 string format = "wav", int bitrateKbps = 320, double startBeat = 0) => Mutate(() =>
    {
        E.StopTransport();
        return _exporter.ExportStems(E, BuildRequest(folder, totalBeats, bitDepth, format, bitrateKbps, startBeat));
    });
}
