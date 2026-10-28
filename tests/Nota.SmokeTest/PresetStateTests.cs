// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preset state (specs/048, #157): a device knows which preset it was loaded from, and says so
// once a sound parameter moves away from it. Applying a preset is one undo step with its name;
// copies and duplicates keep the name; a saved project reopens with it; view settings and tiny
// float noise don't count as changes. Also runnable alone: `--preset-state`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class PresetStateTests
{
    private const int Eq = 0, Delay = 3, Arp = 0;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        using var eng = new NotaEngine();
        yield return (eng.PresetTagSelfTest(), "preset: the engine self-test (tag inside a gesture undoes and redoes)");

        var cat = new FactoryPresetCatalog();
        var delays = cat.All().Where(p => !p.IsInstrument && !p.IsMidiEffect && p.BuiltinKind == Delay).Take(2).ToList();
        var synths = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 0 && p.DisplayName != "Init").Take(1).ToList();
        var arps = cat.All().Where(p => p.IsMidiEffect && p.BuiltinKind == Arp).Take(1).ToList();
        yield return (delays.Count == 2 && synths.Count == 1 && arps.Count == 1, "preset: factory presets for Delay, Synth and Arpeggiator");
        if (delays.Count < 2 || synths.Count < 1 || arps.Count < 1) yield break;
        var (a, b) = (delays[0], delays[1]);

        int t = eng.AddInstrumentTrack();
        int d = eng.AddBuiltinDevice(t, Delay);
        const PresetSurface Fx = PresetSurface.Effect;
        yield return (eng.DevicePresetTag(t, Fx, d) == 0 && eng.DevicePresetModified(t, Fx, d) == -1 && PresetEdit.Name(eng, t, Fx, d) == "",
            "preset: a new device has no preset (Init)");
        yield return (eng.DevicePresetTag(t, Fx, 9) == -1 && eng.DevicePresetTag(9999, Fx, 0) == -1, "preset: no such device reads -1");

        eng.ClearUndoHistory();
        string w = PresetEdit.Apply(eng, t, Fx, d, a.Id, a.DisplayName, () => cat.ApplyInPlace(eng, a.Id, t, d));
        yield return (w == "" && PresetEdit.Name(eng, t, Fx, d) == a.DisplayName && eng.DevicePresetModified(t, Fx, d) == 0,
            $"preset: applying \"{a.DisplayName}\" names the device, unchanged");
        var tag = eng.PresetTagInfo(eng.DevicePresetTag(t, Fx, d));
        yield return (tag is { } tg && tg.Id == a.Id && tg.Loaded.Length == eng.DeviceParamCount(t, d), "preset: the tag keeps the id and the loaded values");

        // Turning a sound knob marks it; turning it back clears it.
        float v0 = eng.DeviceGetParam(t, d, 0), lo = eng.DeviceParamMin(t, d, 0), hi = eng.DeviceParamMax(t, d, 0);
        float moved = v0 + (v0 < (lo + hi) / 2 ? 0.3f : -0.3f) * (hi - lo);
        eng.DeviceSetParam(t, d, 0, moved);
        yield return (eng.DevicePresetModified(t, Fx, d) == 1, "preset: moving a parameter marks it changed");
        eng.DeviceSetParam(t, d, 0, v0);
        yield return (eng.DevicePresetModified(t, Fx, d) == 0, "preset: moving it back clears the mark");
        eng.DeviceSetParam(t, d, 0, v0 + 1e-6f * (hi - lo));
        yield return (eng.DevicePresetModified(t, Fx, d) == 0, "preset: float noise below the tolerance isn't a change");
        eng.DeviceSetParam(t, d, 0, v0);

        // One undo step per preset, name included.
        PresetEdit.Apply(eng, t, Fx, d, b.Id, b.DisplayName, () => cat.ApplyInPlace(eng, b.Id, t, d));
        var afterB = Enumerable.Range(0, eng.DeviceParamCount(t, d)).Select(i => eng.DeviceGetParam(t, d, i)).ToArray();
        yield return (PresetEdit.Name(eng, t, Fx, d) == b.DisplayName && eng.DevicePresetModified(t, Fx, d) == 0,
            $"preset: switching to \"{b.DisplayName}\"");
        bool u1 = eng.Undo();
        yield return (u1 && PresetEdit.Name(eng, t, Fx, d) == a.DisplayName && eng.DevicePresetModified(t, Fx, d) == 0,
            "preset: undo brings back the previous preset's sound and name");
        bool u2 = eng.Undo();
        yield return (u2 && eng.DevicePresetTag(t, Fx, d) == 0, "preset: undo again goes back to no preset");
        bool r1 = eng.Redo() && eng.Redo();
        var redone = Enumerable.Range(0, eng.DeviceParamCount(t, d)).Select(i => eng.DeviceGetParam(t, d, i)).ToArray();
        yield return (r1 && PresetEdit.Name(eng, t, Fx, d) == b.DisplayName && redone.SequenceEqual(afterB),
            "preset: redo applies both again");

        // Init clears the name, as one step.
        PresetEdit.Apply(eng, t, Fx, d, "", "", () => cat.ResetInPlace(eng, t, d, false));
        yield return (eng.DevicePresetTag(t, Fx, d) == 0 && PresetEdit.Name(eng, t, Fx, d) == "", "preset: Init clears the name");
        yield return (eng.Undo() && PresetEdit.Name(eng, t, Fx, d) == b.DisplayName, "preset: undoing Init brings the name back");

        // A preset that doesn't apply leaves the name alone.
        PresetEdit.Apply(eng, t, Fx, d, "no-such-id", "Nope", () => cat.ApplyInPlace(eng, "no-such-id", t, d));
        yield return (PresetEdit.Name(eng, t, Fx, d) == b.DisplayName, "preset: an unknown preset leaves the name as it was");

        // Copies keep the name and the loaded values.
        int dup = eng.DuplicateDevice(t, d);
        yield return (dup >= 0 && PresetEdit.Name(eng, t, Fx, dup) == b.DisplayName && eng.DevicePresetModified(t, Fx, dup) == 0,
            "preset: a duplicate keeps the preset name");
        eng.DeviceSetParam(t, dup, 0, moved);
        yield return (eng.DevicePresetModified(t, Fx, dup) == 1 && eng.DevicePresetModified(t, Fx, d) == 0,
            "preset: changing the duplicate marks only the duplicate");
        int t2 = eng.AddAudioTrack();
        bool pasted = eng.CopyDevice(t, d) && eng.PasteDevice(t2, -1) is var pi && pi >= 0
                      && PresetEdit.Name(eng, t2, Fx, pi) == b.DisplayName;
        yield return (pasted, "preset: copy and paste to another track keeps the name");
        eng.MoveDevice(t, dup, 0);
        yield return (PresetEdit.Name(eng, t, Fx, 0) == b.DisplayName && eng.DevicePresetModified(t, Fx, 0) == 1,
            "preset: a moved device keeps its name and its change");

        // View settings aren't the sound: EQ's analyzer switch doesn't mark it.
        int eq = eng.AddBuiltinDevice(t, Eq);
        PresetEdit.Apply(eng, t, Fx, eq, "test:eq", "Test EQ", () => "");
        int an = Enumerable.Range(0, eng.DeviceParamCount(t, eq)).FirstOrDefault(i => eng.DeviceParamName(t, eq, i) == "Analyzer", -1);
        if (an >= 0) eng.DeviceSetParam(t, eq, an, eng.DeviceGetParam(t, eq, an) > 0.5f ? 0f : 1f);
        yield return (an >= 0 && eng.DevicePresetModified(t, Fx, eq) == 0, "preset: the EQ analyzer switch is a view setting, not a change");

        // The instrument and MIDI effects carry names too.
        var s = synths[0];
        PresetEdit.Apply(eng, t, PresetSurface.Instrument, -1, s.Id, s.DisplayName, () => cat.ApplyInPlace(eng, s.Id, t, -1));
        yield return (PresetEdit.Name(eng, t, PresetSurface.Instrument, -1) == s.DisplayName
                      && eng.DevicePresetModified(t, PresetSurface.Instrument, -1) == 0, $"preset: the Synth takes \"{s.DisplayName}\"");
        int view = InstrumentView.Index(eng, t);
        if (view >= 0) eng.PluginParamSet(t, -1, view, eng.PluginParamGet(t, -1, view) > 0.5f ? 0f : 1f);
        yield return (eng.DevicePresetModified(t, PresetSurface.Instrument, -1) == 0, "preset: the card size (view) isn't a change");
        int sp = Enumerable.Range(0, eng.PluginParamCount(t, -1)).First(i => i != view);
        float sv = eng.PluginParamGet(t, -1, sp);
        eng.PluginParamSet(t, -1, sp, sv > 0.5f ? sv - 0.3f : sv + 0.3f);
        yield return (eng.DevicePresetModified(t, PresetSurface.Instrument, -1) == 1, "preset: a Synth knob marks it changed");

        var ap = arps[0];
        int m = eng.AddMidiEffect(t, Arp);
        PresetEdit.Apply(eng, t, PresetSurface.MidiEffect, m, ap.Id, ap.DisplayName, () => cat.ApplyInPlace(eng, ap.Id, t, m));
        yield return (PresetEdit.Name(eng, t, PresetSurface.MidiEffect, m) == ap.DisplayName
                      && eng.DevicePresetModified(t, PresetSurface.MidiEffect, m) == 0, $"preset: the Arpeggiator takes \"{ap.DisplayName}\"");

        // Save → reopen: names, ids and the changed state come back.
        string dir = Path.Combine(Path.GetTempPath(), "nota-preset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, eng);
            using var e2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), e2, dir);
            int rt = -1;
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) { rt = ti.Id; break; }
            yield return (rt > 0 && PresetEdit.Name(e2, rt, Fx, 0) == b.DisplayName && e2.DevicePresetModified(rt, Fx, 0) == 1
                          && PresetEdit.Name(e2, rt, Fx, 1) == b.DisplayName && e2.DevicePresetModified(rt, Fx, 1) == 0,
                "preset: a reopened project keeps each effect's preset and its changed state");
            yield return (rt > 0 && e2.PresetTagInfo(e2.DevicePresetTag(rt, Fx, 1))?.Id == b.Id, "preset: the preset id round-trips");
            yield return (rt > 0 && PresetEdit.Name(e2, rt, PresetSurface.Instrument, -1) == s.DisplayName
                          && e2.DevicePresetModified(rt, PresetSurface.Instrument, -1) == 1,
                "preset: the instrument reopens with its preset, still changed");
            yield return (rt > 0 && PresetEdit.Name(e2, rt, PresetSurface.MidiEffect, 0) == ap.DisplayName
                          && e2.DevicePresetModified(rt, PresetSurface.MidiEffect, 0) == 0,
                "preset: the MIDI effect reopens with its preset");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        // A v25 project has no preset names: it migrates on and every device opens at Init.
        var old = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 25 });
        yield return (old["formatVersion"]?.GetValue<int>() == ProjectService.CurrentFormatVersion
                      && new DeviceDto().Preset is null && new InstrumentDto().Preset is null && new MidiDeviceDto().Preset is null,
            "preset: a v25 project migrates and opens with no preset names");

        foreach (var r in InPlaceDrops()) yield return r;
    }

    // A preset dropped (or picked in the palette) onto the same kind of instrument changes the
    // sound in place: undo brings back the old sound together with the old name.
    private static IEnumerable<(bool, string)> InPlaceDrops()
    {
        using var eng = new NotaEngine();
        var cat = new FactoryPresetCatalog();
        var svc = new DeviceInsertService(eng, cat, null, null);
        var two = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 0 && p.DisplayName != "Init").Take(2).ToList();
        if (two.Count < 2) { yield return (false, "preset: two Synth presets for the in-place drop"); yield break; }
        int t = eng.AddInstrumentTrack();
        eng.SetTrackBuiltinInstrument(t, 0);
        BrowserItem Row(FactoryPresetInfo p) => new() { Name = p.DisplayName, Kind = BrowserItemKind.Preset, Path = "factory:" + p.Id };
        float[] Sound() => Enumerable.Range(0, eng.PluginParamCount(t, -1)).Select(i => eng.PluginParamGet(t, -1, i)).ToArray();
        const PresetSurface Ins = PresetSurface.Instrument;

        var r1 = svc.ApplyPreset(Row(two[0]), new InsertTarget(t, InPlace: true));
        var first = Sound();
        var r2 = svc.ApplyPreset(Row(two[1]), new InsertTarget(t, InPlace: true));
        var second = Sound();
        yield return (r1.Ok && r2.Ok && !r2.InstrumentReplaced && PresetEdit.Name(eng, t, Ins, -1) == two[1].DisplayName
                      && eng.DevicePresetModified(t, Ins, -1) == 0 && !first.SequenceEqual(second),
            $"preset: \"{two[1].DisplayName}\" dropped on the same instrument loads in place and names it");
        yield return (eng.Undo() && PresetEdit.Name(eng, t, Ins, -1) == two[0].DisplayName && Sound().SequenceEqual(first),
            "preset: undoing an in-place drop brings back the old sound and the old name together");
        // Another kind of instrument replaces the tagged one: undo brings the old one back named.
        var others = cat.All().Where(p => p.IsInstrument && p.BuiltinKind > 0 && p.DisplayName != "Init").Take(1).ToList();
        if (others.Count == 0) { yield return (false, "preset: a preset for another instrument kind"); yield break; }
        var other = others[0];
        var r3 = svc.ApplyPreset(Row(other), new InsertTarget(t, InPlace: true));
        bool swapped = r3.Ok && eng.TrackInstrumentKind(t) == other.BuiltinKind && PresetEdit.Name(eng, t, Ins, -1) == other.DisplayName;
        yield return (swapped && eng.Undo() && eng.TrackInstrumentKind(t) == 0 && PresetEdit.Name(eng, t, Ins, -1) == two[0].DisplayName,
            $"preset: undoing a swap to \"{other.DisplayName}\" brings back the old instrument with its name ({PresetEdit.Name(eng, t, Ins, -1)})");
        yield return (eng.Redo() && eng.TrackInstrumentKind(t) == other.BuiltinKind && PresetEdit.Name(eng, t, Ins, -1) == other.DisplayName,
            $"preset: redo brings the swap back with its name ({PresetEdit.Name(eng, t, Ins, -1)})");
    }
}
