// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — whole-project snapshot (so the AI can see before it acts), undo/redo and the
// arrangement's song sections, including rearranging the song by them (specs/073).

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
        float Volume, float Pan, int InstrumentKind, bool InstrumentOff, int GroupId, ClipInfo[] Clips, MidiFxInfo[] MidiFx, DeviceInfo[] Devices);
    public sealed record Overview(double Bpm, bool Playing, double PositionBeats, TrackInfo[] Tracks);

    [McpServerTool(Name = "get_overview"), Description(
        "Full snapshot of the open project: tempo, transport, and every track with its type, mixer state, "
        + "instrument kind (and whether its on/off switch is off), clips (index/start/length/midi), MIDI effects and audio devices. Call this first to see state.")]
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
                ti.IsInstrument && E.DeviceBypassed(ti.Id, -1), ti.GroupId, clips.ToArray(), mfx.ToArray(), devs.ToArray()));
        }
        return new Overview(E.Bpm, E.IsPlaying, E.PositionBeats, tracks.ToArray());
    });

    [McpServerTool(Name = "undo"), Description("Undo the last edit. Returns whether anything was undone.")]
    public Task<bool> Undo() => Mutate(() => E.Undo());

    [McpServerTool(Name = "redo"), Description("Redo the last undone edit. Returns whether anything was redone.")]
    public Task<bool> Redo() => Mutate(() => E.Redo());

    public sealed record Section(double StartBeat, double LengthBeats, string Name, bool Automatic = false);

    private Section[] Listed()
    {
        var all = E.Sections;
        return all.Select((s, i) => new Section(s.StartBeat, s.LengthBeats, SectionNames.Display(s.Name, i), SectionNames.IsAutomatic(s.Name))).ToArray();
    }

    [McpServerTool(Name = "list_sections"), Description(
        "List the arrangement's song sections (the named spans on the Sections lane — Intro, Verse, Drop …) in start order: "
        + "start and length in beats, the name as shown, and whether it is automatic (\"Section N\" for its position, "
        + "renumbered as sections are added, moved or removed) rather than a custom name.")]
    public Task<Section[]> ListSections() => Read(Listed);

    [McpServerTool(Name = "set_sections"), Description(
        "Replace the arrangement's song sections with this list (pass [] to remove them all). Each needs startBeat >= 0 and "
        + "lengthBeats > 0; they are sorted by start, and a span with no length is dropped. A section with automatic = true "
        +         "or an empty or whitespace-only name gets the automatic name \"Section N\" for its position. Any other name is stored "
        + "exactly as given, surrounding spaces included, so listing and setting the same list changes nothing. "
        + "One undo step. Returns the list as stored.")]
    public Task<Section[]> SetSections(Section[] sections) => Mutate(() =>
    {
        // A nonblank custom name is kept verbatim. Trimming belongs to the rename box; doing it
        // here would rewrite a legacy " Chorus " on a list → set round trip and add an undo step.
        E.SetSections(sections.Select(s => new SectionInfo(s.StartBeat, s.LengthBeats,
            s.Automatic || SectionNames.IsAutomatic(s.Name) ? "" : s.Name)).ToList());
        return Listed();
    });

    // The with-content edits refuse the same cases the Sections lane does; say which one.
    private string Refusal(int index, string what)
    {
        int n = E.Sections.Count;
        if (index < 0 || index >= n) return $"There is no section {index} (list_sections has {n}).";
        if (E.SectionOverlaps(index)) return $"Section {index} overlaps another section, so it can't {what}; separate them first with set_sections.";
        return $"Section {index} can't {what} there.";
    }

    [McpServerTool(Name = "move_section"), Description(
        "Rearrange the song: move section `index` (list_sections order) together with everything in its span — audio and MIDI "
        + "clips (cut at its edges), automation on every track and the master, take lanes — to boundary `toBoundary` "
        + "(0..count: before section k, or after the last section when it equals the count). The sections it passes shift by "
        + "its length to make room; the song's length is unchanged and nothing outside the affected span moves. "
        + "Moving to its own place (toBoundary = index or index + 1) is an error. A section that overlaps another can't be moved "
        + "with content. One undo step. Returns the sections in their new order.")]
    public Task<Section[]> MoveSection(int index, int toBoundary) => Mutate(() =>
    {
        if (toBoundary == index || toBoundary == index + 1) throw new ArgumentException($"Section {index} is already at boundary {toBoundary}.");
        if (!E.MoveSection(index, toBoundary)) throw new InvalidOperationException(Refusal(index, "move with its content"));
        return Listed();
    });

    [McpServerTool(Name = "duplicate_section_with_content"), Description(
        "Insert a copy of section `index` (list_sections order) and everything in its span — clips, automation — right after it, "
        + "with the same name (an automatic name stays automatic); the rest of the song moves later by its length. "
        + "One undo step. Returns the sections.")]
    public Task<Section[]> DuplicateSectionWithContent(int index) => Mutate(() =>
    {
        if (!E.DuplicateSectionWithContent(index)) throw new InvalidOperationException(Refusal(index, "be duplicated with its content"));
        return Listed();
    });

    [McpServerTool(Name = "delete_section_with_content"), Description(
        "Remove section `index` (list_sections order) and everything in its span — clips (cut at its edges), automation — and "
        + "move the rest of the song earlier by its length to close the gap. One undo step. Returns the remaining sections.")]
    public Task<Section[]> DeleteSectionWithContent(int index) => Mutate(() =>
    {
        if (!E.DeleteSectionWithContent(index)) throw new InvalidOperationException(Refusal(index, "be deleted with its content"));
        return Listed();
    });
}
