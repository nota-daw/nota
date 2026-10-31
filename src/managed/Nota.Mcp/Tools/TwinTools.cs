// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — Nota Twin (instrument kind 18, specs/055): read the patch with its two morph
// sides and its modulation matrix, and set a matrix route by source / destination name. Every
// other parameter goes through set_instrument_param_by_id with the ids read_twin names.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;
using Nota.Application.Twin;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class TwinTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record SideRow(string Id, string Name, string A, string B, bool Differs);
    public sealed record RouteRow(int Slot, string Source, string Destination, double DepthAPercent, double DepthBPercent);
    public sealed record PartsInfo(string Layout, string? SplitKey, int LowerVoices, int UpperVoices, string SequencerPlays);
    public sealed record TwinReading(string Summary, double MorphPercent, string VoiceMode, int Voices, int ActiveVoices,
        PartsInfo Parts, SideRow[] Sound, RouteRow[] Routes, string Guide);

    // The controls read_twin reports for each side: the ones that define a patch's character.
    private static readonly string[] MainIds =
    {
        "o1wave", "o1level", "o2wave", "o2level", "o2coarse", "sync", "noiselevel", "mmcut", "mmres", "mmmode",
        "ldcut", "ldres", "ldslope", "route", "brute", "drive", "fenva", "fenvd", "aenva", "aenvd", "aenvs", "aenvr",
    };
    private static readonly string[] VoiceModes = { "Poly", "Mono", "Legato", "Unison" };
    private static readonly string[] Layouts = { "Single", "Layer", "Split" };
    private static readonly string[] SeqTargets = { "Lower", "Upper", "Both" };
    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private const string Guide =
        "Ids: '<part>.<side>.<name>' for the sound (part lo = Lower, side a / b; e.g. lo.b.ldcut), '<part>.<name>' for "
        + "side-less part params (lo.morph, lo.voicemode, lo.glide, lo.vol, lo.octave), bare names for the patch (voices, padx, "
        + "bendrange; mode 0 Single / 0.5 Layer / 1 Split, splitkey = first Upper key / 127, share 0 / .25 / .5 / .75 / 1 = Lower "
        + "2 / 4 / 6 / 8 / 10 voices). "
        + "Morph 0 = side A, 1 = side B. Matrix: up to 64 routes per part, each a source, a destination and a depth per side "
        + "(−100..100 %); use set_twin_route.";

    private Dictionary<string, int> Ids(int t)
    {
        int n = E.PluginParamCount(t, -1);
        var m = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++) m[E.PluginParamId(t, -1, i)] = i;
        return m;
    }

    [McpServerTool(Name = "read_twin"), Description(
        "Read a Nota Twin track (built-in instrument kind 18 — a 12-voice analog poly whose every sound control has a side A and "
        + "a side B, and Morph sweeps between them; two parts, Lower and Upper, layered or split). part 'lo' or 'up' picks the "
        + "part to read. Returns a summary, the parts' layout (Single / Layer / Split, split key, voice share, the part the "
        + "sequencer plays), the morph position, voice mode and voice count, the main "
        + "sound controls with both sides' values (Differs = the morph moves it), every matrix route (slot, source, destination, "
        + "depth on A and on B in %), and an id guide for set_instrument_param_by_id. Null if the track isn't a Twin.")]
    public Task<TwinReading?> ReadTwin(int trackId, string part = "lo") => Read<TwinReading?>(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return null;
        if (part is not ("lo" or "up")) part = "lo";
        var at = Ids(trackId);
        float G(string id) => at.TryGetValue(id, out var i) ? E.PluginParamGet(trackId, -1, i) : 0f;
        string Text(string id) => at.TryGetValue(id, out var i) ? E.PluginParamText(trackId, -1, i) : "";
        var sound = new List<SideRow>();
        foreach (var id in MainIds)
        {
            string a = $"{part}.a.{id}", b = $"{part}.b.{id}";
            string name = E.PluginParamName(trackId, -1, at[a]);
            int dot = name.IndexOf(" · ", StringComparison.Ordinal);
            sound.Add(new SideRow(id, dot >= 0 ? name[(dot + 3)..] : name, Text(a), Text(b), Math.Abs(G(a) - G(b)) > 1e-4f));
        }
        var routes = TwinMatrix.Routes(E, trackId, part).Select(r => new RouteRow(r.Slot, TwinMatrix.Sources[r.Source],
            TwinMatrix.DestinationName(E, trackId, r.Destination), Math.Round(r.DepthA * 100, 1), Math.Round(r.DepthB * 100, 1))).ToArray();
        string mode = VoiceModes[Math.Clamp((int)Math.Round(G(part + ".voicemode") * 3), 0, 3)];
        int voices = 4 + Math.Clamp((int)Math.Round(G("voices") * 8), 0, 8);
        int layout = Math.Clamp((int)Math.Round(G("mode") * 2), 0, 2);
        int lower = layout == 0 ? voices : Math.Clamp(2 + 2 * Math.Clamp((int)Math.Round(G("share") * 4), 0, 4), 2, voices - 2);
        int key = Math.Clamp((int)Math.Round(G("splitkey") * 127), 0, 127);
        var parts = new PartsInfo(Layouts[layout], layout == 2 ? $"{NoteNames[key % 12]}{key / 12 - 1}" : null, lower, voices - lower,
            layout == 0 ? "Lower" : SeqTargets[Math.Clamp((int)Math.Round(G("seq.target") * 2), 0, 2)]);
        int differing = sound.Count(s => s.Differs);
        string partName = part == "lo" ? "Lower" : "Upper";
        string layoutText = layout switch { 1 => $"Layer {lower} + {voices - lower}", 2 => $"Split at {parts.SplitKey} {lower} + {voices - lower}", _ => "Single" };
        string summary = $"Nota Twin · {layoutText} · {partName}: {mode} · morph {G(part + ".morph") * 100:0}% · {differing} of {sound.Count} main controls differ between A and B · {routes.Length} routes";
        return new TwinReading(summary, Math.Round(G(part + ".morph") * 100, 1), mode, voices, E.InstrumentVoiceCount(trackId),
            parts, sound.ToArray(), routes, Guide);
    });

    [McpServerTool(Name = "set_twin_morph_side"), Description(
        "Set sound controls on one morph side of a Nota Twin (kind 18), one undo step. side 'a' or 'b'; values is a list "
        + "'id=value' separated by commas or semicolons, ids without the part / side prefix as read_twin's Sound rows name "
        + "them (e.g. 'ldcut=0.3, o1wave=1, aenva=0.6'), values normalized 0..1. part 'lo' (Lower) or 'up'. Returns what it "
        + "set, or an error line naming the ids it could not find.")]
    public Task<string> SetTwinMorphSide(int trackId, string side, string values, string part = "lo") => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return $"error: track {trackId} is not a Nota Twin";
        side = side.Trim().ToLowerInvariant();
        if (side is not ("a" or "b") || part is not ("lo" or "up")) return "error: side a | b, part lo | up";
        var at = Ids(trackId);
        var done = new List<string>();
        var bad = new List<string>();
        foreach (var raw in values.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = raw.Split('=', 2);
            if (kv.Length != 2 || !float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            { bad.Add(raw.Trim()); continue; }
            string id = $"{part}.{side}.{kv[0].Trim()}";
            if (!at.TryGetValue(id, out var i)) { bad.Add(kv[0].Trim()); continue; }
            PlugParam(trackId, -1, i, Math.Clamp(v, 0f, 1f));
            done.Add($"{kv[0].Trim()}={E.PluginParamText(trackId, -1, i)}");
        }
        return bad.Count > 0 ? $"error: unknown or malformed: {string.Join(", ", bad)}" + (done.Count > 0 ? $" (set {string.Join(", ", done)})" : "")
            : $"side {side.ToUpperInvariant()}: {string.Join(", ", done)}";
    });

    [McpServerTool(Name = "copy_twin_side"), Description(
        "Copy or swap the morph sides of a Nota Twin (kind 18) part, one undo step: edit 'a_to_b', 'b_to_a' or 'swap'; part "
        + "'lo' or 'up'. Every sound control and every route depth moves; Morph, the voice settings and the routes' sources and "
        + "destinations stay.")]
    public Task<string> CopyTwinSide(int trackId, string edit, string part = "lo") => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return $"error: track {trackId} is not a Nota Twin";
        TwinSides.Edit? op = edit.Trim().ToLowerInvariant() switch
        {
            "a_to_b" => TwinSides.Edit.CopyAToB, "b_to_a" => TwinSides.Edit.CopyBToA, "swap" => TwinSides.Edit.Swap, _ => null,
        };
        if (op is null) return $"error: edit '{edit}' — use a_to_b, b_to_a or swap";
        int n = TwinSides.Apply(E, trackId, op.Value, part);
        return $"{edit}: {n} params";
    });

    public sealed record PatternReading(int Pattern, int Length, string[] Rows, string[] MotionLanes, bool SequencerOn, string Kind);

    [McpServerTool(Name = "get_twin_pattern"), Description(
        "Read one of a Nota Twin's eight sequencer patterns (pattern 1..8): its steps as text rows — notes, then ':velocity' "
        + "and ':gate' ('50%' or 'tie'); '.' is a rest (e.g. 'C3 E3 G3:100:50%') — its motion lanes (target param and how many "
        + "steps set it), and whether the sequencer (or the arpeggiator) is on. Null if the track isn't a Twin.")]
    public Task<PatternReading?> GetTwinPattern(int trackId, int pattern = 1) => Read<PatternReading?>(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind || pattern is < 1 or > TwinPatterns.Patterns) return null;
        var set = TwinPatterns.FromState(E.GetPluginState(trackId, -1));
        if (set is null) return null;
        var p = set[pattern - 1];
        var at = Ids(trackId);
        var rows = p.Steps.Take(p.Length).Select(TwinPatterns.StepText).ToArray();
        var lanes = p.Lanes.Where(l => l.Target >= 0)
            .Select(l => $"{E.PluginParamId(trackId, -1, l.Target)} ({l.Values.Take(p.Length).Count(v => v != TwinPatterns.MotionUnset)} steps{(l.Smooth ? ", smooth" : "")})").ToArray();
        bool on = E.PluginParamGet(trackId, -1, at["seq.on"]) >= 0.5f;
        string kind = E.PluginParamGet(trackId, -1, at["seq.kind"]) >= 0.5f ? "arpeggiator" : "sequencer";
        return new PatternReading(pattern, p.Length, rows, lanes, on, kind);
    });

    [McpServerTool(Name = "set_twin_pattern"), Description(
        "Replace a Nota Twin sequencer pattern (1..8) with rows in get_twin_pattern's text form, one step per row (rows separated "
        + "by newlines or '|'; 1..64 rows, the count becomes the length). Turns the sequencer on and plays that pattern when "
        + "play = true. One undo step. Returns 'pattern N: L steps' or an error naming the row it couldn't read.")]
    public Task<string> SetTwinPattern(int trackId, string rows, int pattern = 1, bool play = true) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return $"error: track {trackId} is not a Nota Twin";
        if (pattern is < 1 or > TwinPatterns.Patterns) return "error: pattern 1..8";
        var list = rows.Split(new[] { '\n', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        string err = TwinPatterns.WritePattern(E, trackId, pattern - 1, list);
        if (err.Length > 0) return "error: " + err;
        if (play)
        {
            var at = Ids(trackId);
            PlugParam(trackId, -1, at["seq.on"], 1f);
            PlugParam(trackId, -1, at["seq.pattern"], (pattern - 1) / 7f);
        }
        return $"pattern {pattern}: {list.Count} steps";
    });

    [McpServerTool(Name = "twin_pattern_to_clip"), Description(
        "Write a Nota Twin sequencer pattern (1..8) as an ordinary MIDI clip on its track at a bar (1 = the first bar): notes, "
        + "velocities, gates, ties and swing; motion lanes become automation. Turns the sequencer off. One undo step. Returns "
        + "the clip index, or -1.")]
    public Task<int> TwinPatternToClip(int trackId, int pattern = 1, int startBar = 1) => Mutate(() =>
        E.TrackInstrumentKind(trackId) != TwinMatrix.Kind ? -1 : TwinPatterns.ToClip(E, trackId, pattern - 1, Math.Max(0, startBar - 1) * 4.0));

    [McpServerTool(Name = "copy_twin_part"), Description(
        "Copy or swap the two parts of a Nota Twin (kind 18), one undo step: edit 'lower_to_upper', 'upper_to_lower' or 'swap'. "
        + "The whole part moves — both morph sides, the matrix, the effects, morph and the voice settings; the layout (mode, split "
        + "key, share) stays. Set mode to Layer / Split with set_instrument_param_by_id to hear the Upper part.")]
    public Task<string> CopyTwinPart(int trackId, string edit) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return $"error: track {trackId} is not a Nota Twin";
        TwinParts.Edit? op = edit.Trim().ToLowerInvariant() switch
        {
            "lower_to_upper" => TwinParts.Edit.CopyLowerToUpper, "upper_to_lower" => TwinParts.Edit.CopyUpperToLower, "swap" => TwinParts.Edit.Swap, _ => null,
        };
        if (op is null) return $"error: edit '{edit}' — use lower_to_upper, upper_to_lower or swap";
        return $"{edit}: {TwinParts.Apply(E, trackId, op.Value)} params";
    });

    [McpServerTool(Name = "set_twin_route"), Description(
        "Set a modulation route on a Nota Twin (kind 18), one undo step. source / destination are names as read_twin and the card "
        + "show them (case-insensitive): sources LFO 1..3, Filter env, Amp env, Mod env, Velocity, Key, Pressure, Slide, Bend, Mod "
        + "wheel, Pitch wheel, Pad X/Y/Z, Random, Macro 1..8; destinations Morph, Pitch, Osc 1 pitch, Osc 2 pitch, Vibrato, or any "
        + "sound control by its name ('Ladder Cutoff', 'Osc1 Wave', 'AmpEnv Attack'). depthA / depthB in % (−100..100; B defaults "
        + "to A). slot 1..64 rewrites that slot; omit it for the first free one. remove = true empties the slot. Returns "
        + "'slot N: source → destination', or an error line ('matrix full', or the name it could not find).")]
    public Task<string> SetTwinRoute(int trackId, string? source = null, string? destination = null, double depthA = 50,
        double? depthB = null, int? slot = null, bool remove = false, string part = "lo") => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return $"error: track {trackId} is not a Nota Twin";
        if (part is not ("lo" or "up")) return $"error: part '{part}' — use lo or up";
        if (remove)
        {
            if (slot is not (>= 1 and <= TwinMatrix.Slots)) return "error: remove needs a slot 1..64";
            TwinMatrix.Remove(E, trackId, slot.Value, part);
            return $"slot {slot}: removed";
        }
        int src = Array.FindIndex(TwinMatrix.Sources, s => string.Equals(s, source?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (src <= 0) return $"error: no source '{source}'";
        int dst = -1;
        for (int k = 1; k < TwinMatrix.DestinationCount && dst < 0; k++)
            if (string.Equals(TwinMatrix.DestinationName(E, trackId, k), destination?.Trim(), StringComparison.OrdinalIgnoreCase)) dst = k;
        if (dst <= 0) return $"error: no destination '{destination}'";
        float a = (float)Math.Clamp(depthA / 100, -1, 1), b = (float)Math.Clamp((depthB ?? depthA) / 100, -1, 1);
        int got = TwinMatrix.Add(E, trackId, src, dst, a, b, part, slot ?? 0);
        return got < 0 ? "error: matrix full (64 routes) — remove one first"
            : $"slot {got}: {TwinMatrix.Sources[src]} → {TwinMatrix.DestinationName(E, trackId, dst)} (A {a * 100:0}%, B {b * 100:0}%)";
    });
}
