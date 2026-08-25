// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — whole-project snapshot (so the AI can see before it acts) + undo/redo.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record ClipInfo(int Index, double StartBeat, double LengthBeats, bool IsMidi, string Name);
    public sealed record DeviceInfo(int Index, int BuiltinKind, string Name, bool Bypassed);
    public sealed record MidiFxInfo(int Index, int Kind, string Name, bool Bypassed);
    public sealed record TrackInfo(int Id, string Name, string Type, int ColorIndex, bool Muted, bool Soloed,
        float Volume, float Pan, int InstrumentKind, int GroupId, ClipInfo[] Clips, MidiFxInfo[] MidiFx, DeviceInfo[] Devices);
    public sealed record Overview(double Bpm, bool Playing, double PositionBeats, TrackInfo[] Tracks);

    [McpServerTool(Name = "get_overview"), Description(
        "Full snapshot of the open project: tempo, transport, and every track with its type, mixer state, "
        + "instrument kind, clips (index/start/length/midi), MIDI effects and audio devices. Call this first to see state.")]
    public Task<Overview> GetOverview() => Read(() =>
    {
        int n = E.TrackCount;
        var tracks = new List<TrackInfo>(n);
        for (int i = 0; i < n; i++)
        {
            if (!E.TryGetTrackInfo(i, out var ti)) continue;
            var clips = new List<ClipInfo>(ti.ClipCount);
            for (int c = 0; c < ti.ClipCount; c++)
                if (E.TryGetClipInfo(ti.Id, c, out var ci))
                    clips.Add(new ClipInfo(c, ci.StartBeat, ci.LengthBeats, ci.IsMidi, E.GetClipName(ti.Id, c)));
            int mfc = E.TrackMidiEffectCount(ti.Id);
            var mfx = new List<MidiFxInfo>(mfc);
            for (int m = 0; m < mfc; m++) mfx.Add(new MidiFxInfo(m, E.MidiEffectKind(ti.Id, m), E.MidiEffectName(ti.Id, m), E.MidiEffectBypassed(ti.Id, m)));
            int dc = E.TrackDeviceCount(ti.Id);
            var devs = new List<DeviceInfo>(dc);
            for (int d = 0; d < dc; d++) devs.Add(new DeviceInfo(d, E.TrackDeviceBuiltinKind(ti.Id, d), E.DeviceName(ti.Id, d), E.DeviceBypassed(ti.Id, d)));
            tracks.Add(new TrackInfo(ti.Id, E.GetTrackName(ti.Id), TypeName(ti.Type), E.GetTrackColorIndex(ti.Id),
                ti.Muted != 0, ti.Soloed != 0, ti.Volume, ti.Pan, ti.IsInstrument ? E.TrackInstrumentKind(ti.Id) : -1,
                ti.GroupId, clips.ToArray(), mfx.ToArray(), devs.ToArray()));
        }
        return new Overview(E.Bpm, E.IsPlaying, E.PositionBeats, tracks.ToArray());
    });

    [McpServerTool(Name = "undo"), Description("Undo the last edit. Returns whether anything was undone.")]
    public Task<bool> Undo() => Mutate(() => E.Undo());

    [McpServerTool(Name = "redo"), Description("Redo the last undone edit. Returns whether anything was redone.")]
    public Task<bool> Redo() => Mutate(() => E.Redo());
}
