// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — offline export of the project to WAV: the whole master bus, or one stem per track.
// The bounce is rendered offline at the project tempo/sample rate; playback is stopped first and
// the transport toggles restored afterwards. bitDepth = pcm16 | pcm24 | float32.

using System.ComponentModel;
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

    private ExportRequest BuildRequest(string path, double totalBeats, string bitDepth)
    {
        int sr = (int)Math.Round(E.SampleRate);
        if (sr <= 0) sr = 48000;   // engine hasn't negotiated a device rate yet — pick a sane default
        double bpm = E.Bpm > 0 ? E.Bpm : 120;
        return new ExportRequest(path, Math.Max(0.25, totalBeats), bpm, sr, ParseDepth(bitDepth),
            RestoreLoop: E.LoopEnabled, RestoreMetronome: false);
    }

    [McpServerTool(Name = "export_master"), Description(
        "Render the master bus offline to a WAV file. path = output .wav, totalBeats = how many beats to render, "
        + "bitDepth = pcm16 | pcm24 | float32. Stops playback first. Returns the output path.")]
    public Task<string> ExportMaster(string path, double totalBeats, string bitDepth = "float32") => Mutate(() =>
    {
        E.StopTransport();
        _exporter.ExportMaster(E, BuildRequest(path, totalBeats, bitDepth));
        return path;
    });

    [McpServerTool(Name = "export_stems"), Description(
        "Render one WAV per non-return track (stems) into the given folder. folder = output directory, "
        + "totalBeats = how many beats to render, bitDepth = pcm16 | pcm24 | float32. Returns the number of stems written.")]
    public Task<int> ExportStems(string folder, double totalBeats, string bitDepth = "float32") => Mutate(() =>
    {
        E.StopTransport();
        return _exporter.ExportStems(E, BuildRequest(folder, totalBeats, bitDepth));
    });
}
