// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the shipped Drum Rack kits. The kits are synthesized from recipes on the
// user's machine rather than bundled as audio, so loading one may render its samples
// first; that happens inside the call and takes well under a second per kit.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class DrumKitTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IDrumKits kits)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record KitRow(string Id, string Name, string Description, int Pads, bool Rendered);
    public sealed record KitLoadResult(int TrackId, int Pads, string Warning);

    [McpServerTool(Name = "list_drum_kits"), Description(
        "List the factory drum kits (shared by the Drum Rack and Nota Rhythm): id, name, a one-line "
        + "description, pad count, and whether its samples have been rendered yet. Pads follow the "
        + "General MIDI drum map on notes 36-51; each pad carries its own effects (kick saturation, "
        + "snare / clap reverb, hat and percussion delay).")]
    public Task<KitRow[]> ListDrumKits() => Read(() =>
    {
        var all = kits.All();
        var rows = new KitRow[all.Count];
        for (int i = 0; i < all.Count; i++)
            rows[i] = new KitRow(all[i].Id, all[i].Name, all[i].Blurb, all[i].PadCount, kits.IsRendered(all[i].Id));
        return rows;
    });

    [McpServerTool(Name = "add_drum_kit_track"), Description(
        "Add a Drum Rack track loaded with a factory kit (see list_drum_kits for ids). "
        + "Returns the new track id and how many pads were loaded.")]
    public Task<KitLoadResult> AddDrumKitTrack(string kitId) => Mutate(() =>
    {
        int t = kits.CreateTrack(E, kitId, out string warning);
        return new KitLoadResult(t, t > 0 ? E.RackChainCount(t) : 0, warning);
    });

    [McpServerTool(Name = "add_rhythm_kit_track"), Description(
        "Add a Nota Rhythm (16-step drum machine) track with a factory kit on its eight voices — the kit's "
        + "samples and per-voice effects, the same sounds the Drum Rack gets. Returns the new track id and the "
        + "number of voices loaded.")]
    public Task<KitLoadResult> AddRhythmKitTrack(string kitId) => Mutate(() =>
    {
        int t = kits.CreateRhythmTrack(E, kitId, out string warning);
        return new KitLoadResult(t, t > 0 ? LoadedVoices(t) : 0, warning);
    });

    [McpServerTool(Name = "load_drum_kit"), Description(
        "Replace the pads of an existing Drum Rack track — or the voices of a Nota Rhythm track, keeping its "
        + "steps — with a factory kit.")]
    public Task<KitLoadResult> LoadDrumKit(int trackId, string kitId) => Mutate(() =>
    {
        bool ok = kits.LoadInto(E, trackId, kitId, out string warning);
        bool rhythm = E.TrackInstrumentKind(trackId) == RhythmModel.Kind;
        return new KitLoadResult(ok ? trackId : -1, !ok ? 0 : rhythm ? LoadedVoices(trackId) : E.RackChainCount(trackId), warning);
    });

    private int LoadedVoices(int trackId)
    {
        int n = 0;
        for (int v = 0; v < RhythmModel.Voices; v++) if (E.RhythmVoiceSource(trackId, v) == 1) n++;
        return n;
    }
}
