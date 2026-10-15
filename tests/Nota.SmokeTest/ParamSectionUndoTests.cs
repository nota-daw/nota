// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Undo for device parameters and arrangement sections (specs/016-device-param-section-undo):
// a parameter drag is one exact step on an effect, a MIDI effect and the instrument; the
// automation-write touch every card sends opens it; parameter, note and section steps share one
// ordered history; playback and loads add none; sections live in the project, undo and redo,
// survive save / load and migrate from the old sidecar; MIDI Learn and MCP each make one step.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --paramundo`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ParamSectionUndoTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Gestures()) yield return r;
        foreach (var r in Touches()) yield return r;
        foreach (var r in OneHistory()) yield return r;
        foreach (var r in NotEdits()) yield return r;
        foreach (var r in Sections()) yield return r;
        foreach (var r in SectionFiles()) yield return r;
        foreach (var r in Sources()) yield return r;
    }

    private const int Eq8 = 0, Reverb = 2;

    // ---- G1, G2: value gestures on the three parameter kinds ----------------------------

    private static IEnumerable<(bool, string)> Gestures()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int d = eng.AddBuiltinDevice(t, Reverb);
        int m = eng.AddMidiEffect(t, 0);   // Nota Arp
        yield return (d >= 0 && m >= 0 && eng.DeviceParamCount(t, d) > 1 && eng.MidiEffectParamCount(t, m) > 1
                      && eng.PluginParamCount(t, -1) > 1, "param undo: test track has an effect, a MIDI effect and an instrument");
        eng.ClearUndoHistory();

        eng.DeviceSetParam(t, d, 1, 0.123f);   // a plain set: never a step
        yield return (!eng.CanUndo, "param undo: a plain parameter set makes no step");

        // G1 on an audio effect: 50 values, one step, exact both ways.
        float start = eng.DeviceGetParam(t, d, 1);
        int h = eng.BeginMixGesture(MixTarget.DeviceParam(t, d, 1));
        for (int i = 1; i <= 50; i++) eng.DeviceSetParam(t, d, 1, 0.2f + i * 0.0119f);
        float last = eng.DeviceGetParam(t, d, 1);
        eng.EndMixGesture(h);
        bool one = eng.CanUndo;
        eng.Undo();
        bool back = eng.DeviceGetParam(t, d, 1) == start && !eng.CanUndo;
        eng.Redo();
        yield return (h > 0 && one && back && eng.DeviceGetParam(t, d, 1) == last,
            $"param undo: a 50-value effect drag is one step, undo → exactly {start}, redo → exactly {last}");

        // G1 on a MIDI effect.
        eng.ClearUndoHistory();
        float m0 = eng.MidiEffectGetParam(t, m, 3);
        Gesture(eng, MixTarget.MidiDeviceParam(t, m, 3), () => eng.MidiEffectSetParam(t, m, 3, m0 == 0.75f ? 0.25f : 0.75f));
        float m1 = eng.MidiEffectGetParam(t, m, 3);
        eng.Undo(); bool mBack = eng.MidiEffectGetParam(t, m, 3) == m0 && !eng.CanUndo;
        eng.Redo();
        yield return (mBack && eng.MidiEffectGetParam(t, m, 3) == m1 && m1 != m0, "param undo: a MIDI effect parameter undoes and redoes exactly");

        // G1 on the instrument (normalized, device -1).
        eng.ClearUndoHistory();
        float p0 = eng.PluginParamGet(t, -1, 1);
        float pWant = p0 > 0.5f ? 0.1f : 0.9f;
        Gesture(eng, MixTarget.PluginParam(t, -1, 1), () => eng.PluginParamSet(t, -1, 1, pWant));
        float p1 = eng.PluginParamGet(t, -1, 1);
        eng.Undo(); bool pBack = eng.PluginParamGet(t, -1, 1) == p0 && !eng.CanUndo;
        eng.Redo();
        yield return (pBack && eng.PluginParamGet(t, -1, 1) == p1 && p1 != p0, $"param undo: an instrument parameter undoes and redoes exactly ({p0} ↔ {p1})");

        // G2: no change, or a drag back to the start, is no step.
        eng.ClearUndoHistory();
        eng.EndMixGesture(eng.BeginMixGesture(MixTarget.DeviceParam(t, d, 1)));
        float s0 = eng.DeviceGetParam(t, d, 1);
        Gesture(eng, MixTarget.DeviceParam(t, d, 1), () => { eng.DeviceSetParam(t, d, 1, 0.9f); eng.DeviceSetParam(t, d, 1, s0); });
        yield return (!eng.CanUndo, "param undo: a gesture without a net change makes no step");

        // A target that doesn't exist is refused.
        yield return (eng.BeginMixGesture(MixTarget.DeviceParam(t, 99, 0)) == 0 && eng.BeginMixGesture(MixTarget.DeviceParam(t, d, 9999)) == 0
                      && eng.BeginMixGesture(MixTarget.MidiDeviceParam(t, 99, 0)) == 0 && eng.BeginMixGesture(MixTarget.PluginParam(t, 5, 0)) == 0,
            "param undo: a gesture on a missing device or parameter is refused");

        // Several parameters in one step (an A/B switch), also past one gesture's 64 targets.
        eng.ClearUndoHistory();
        int pc = eng.PluginParamCount(t, -1);
        var before = Enumerable.Range(0, pc).Select(i => eng.PluginParamGet(t, -1, i)).ToArray();
        MixEdit.Step(eng, () => { for (int i = 0; i < pc; i++) eng.PluginParamSet(t, -1, i, before[i] > 0.5f ? 0.2f : 0.8f); },
            Enumerable.Range(0, pc).Select(i => MixTarget.PluginParam(t, -1, i)).ToArray());
        eng.Undo();
        bool all = Enumerable.Range(0, pc).All(i => eng.PluginParamGet(t, -1, i) == before[i]);
        yield return (all && !eng.CanUndo, $"param undo: setting all {pc} instrument parameters at once is one step");
    }

    // ---- G3, G7: the automation-write touch opens the gesture ----------------------------

    private static IEnumerable<(bool, string)> Touches()
    {
        var buf = new float[512 * 2];
        using var eng = new NotaEngine();
        eng.SetBpm(120);
        int t = eng.AddInstrumentTrack();
        int d = eng.AddBuiltinDevice(t, Reverb);
        int m = eng.AddMidiEffect(t, 0);
        eng.ClearUndoHistory();

        // G3: an unlatched touch on each kind is one step.
        float v0 = eng.DeviceGetParam(t, d, 2);
        eng.BeginAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        for (int i = 0; i < 20; i++) eng.DeviceSetParam(t, d, 2, 0.1f + i * 0.03f);
        eng.EndAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        eng.Undo();
        yield return (eng.DeviceGetParam(t, d, 2) == v0 && !eng.CanUndo, "param undo: a knob touch (automation write) on an effect is one step");

        string pid = eng.PluginParamId(t, -1, 2);
        float i0 = eng.PluginParamGet(t, -1, 2);
        eng.BeginAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, pid);
        eng.PluginParamSet(t, -1, 2, i0 > 0.5f ? 0.05f : 0.95f);
        eng.EndAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, pid);
        bool iStep = eng.CanUndo;
        eng.Undo();
        yield return (iStep && eng.PluginParamGet(t, -1, 2) == i0 && !eng.CanUndo, "param undo: a knob touch on the instrument is one step");

        float a0 = eng.MidiEffectGetParam(t, m, 3);
        eng.BeginAutomationWrite(t, AutomationTarget.MidiDeviceParam, m, 3, "");
        eng.MidiEffectSetParam(t, m, 3, a0 == 0.6f ? 0.3f : 0.6f);
        eng.EndAutomationWrite(t, AutomationTarget.MidiDeviceParam, m, 3, "");
        bool aStep = eng.CanUndo;
        eng.Undo();
        yield return (aStep && eng.MidiEffectGetParam(t, m, 3) == a0 && !eng.CanUndo, "param undo: a knob touch on a MIDI effect is one step");

        // A latched (hardware) write opens nothing: its source makes the step.
        eng.BeginAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "", latch: true);
        eng.DeviceSetParam(t, d, 2, 0.77f);
        eng.EndAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        yield return (!eng.CanUndo, "param undo: a latched automation write makes no step of its own");

        // A gesture the caller already holds and the touch on top of it are still one step.
        eng.ClearUndoHistory();
        float n0 = eng.DeviceGetParam(t, d, 2);
        int h = eng.BeginMixGesture(MixTarget.DeviceParam(t, d, 2));
        eng.BeginAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        eng.DeviceSetParam(t, d, 2, 0.31f);
        eng.EndAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        eng.EndMixGesture(h);
        eng.Undo();
        bool nestedOne = eng.DeviceGetParam(t, d, 2) == n0;
        yield return (nestedOne, $"param undo: an outer gesture plus the touch undo together in one go ({eng.DeviceGetParam(t, d, 2)}, want {n0})");

        // G7: recording a touch — the lane it creates and the value are one step.
        eng.ClearUndoHistory();
        float r0 = eng.DeviceGetParam(t, d, 2);
        int lanes0 = eng.AutomationLaneCount(t);
        eng.SetAutomationRecord(true);
        eng.Seek(0); eng.Play();
        eng.BeginAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        for (int i = 0; i < 40; i++) { eng.DeviceSetParam(t, d, 2, 0.2f + i * 0.01f); eng.RenderOffline(buf, 512, 48000); }
        eng.EndAutomationWrite(t, AutomationTarget.DeviceParam, d, 2, "");
        eng.StopTransport();
        eng.SetAutomationRecord(false);
        int lanes1 = eng.AutomationLaneCount(t);
        float r1 = eng.DeviceGetParam(t, d, 2);
        eng.Undo();
        bool undone = eng.AutomationLaneCount(t) == lanes0 && eng.DeviceGetParam(t, d, 2) == r0 && !eng.CanUndo;
        eng.Redo();
        yield return (lanes1 > lanes0 && undone && eng.AutomationLaneCount(t) == lanes1 && eng.DeviceGetParam(t, d, 2) == r1,
            $"param undo: a recorded touch — lane and value — undoes and redoes as one step ({lanes0}→{lanes1} lanes)");
    }

    // ---- G4, G5: one ordered history with notes, sections and structure ------------------

    private static IEnumerable<(bool, string)> OneHistory()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int d = eng.AddBuiltinDevice(t, Reverb);
        int c = eng.AddMidiClip(t, 0, 4);
        eng.ClearUndoHistory();

        // notes → knob → section → knob, then undo each in reverse (#30's report: undo skipped the knob).
        var notes1 = new[] { new NotaNote(60, 0, 1, 0.8f) };
        eng.SetClipNotes(t, c, notes1);
        float k0 = eng.DeviceGetParam(t, d, 1);
        Gesture(eng, MixTarget.DeviceParam(t, d, 1), () => eng.DeviceSetParam(t, d, 1, 0.33f));
        eng.SetSections(new[] { new SectionInfo(0, 16, "Intro") });
        Gesture(eng, MixTarget.DeviceParam(t, d, 1), () => eng.DeviceSetParam(t, d, 1, 0.66f));

        eng.Undo(); bool s3 = eng.DeviceGetParam(t, d, 1) == 0.33f && eng.Sections.Count == 1 && Notes(eng, t, c) == 1;
        eng.Undo(); bool s2 = eng.Sections.Count == 0 && eng.DeviceGetParam(t, d, 1) == 0.33f && Notes(eng, t, c) == 1;
        eng.Undo(); bool s1 = eng.DeviceGetParam(t, d, 1) == k0 && Notes(eng, t, c) == 1;
        eng.Undo(); bool s0 = Notes(eng, t, c) == 0 && !eng.CanUndo;
        yield return (s3 && s2 && s1 && s0, $"param undo: note, knob, section, knob undo in reverse order ({s3},{s2},{s1},{s0})");
        eng.Redo(); eng.Redo(); eng.Redo(); eng.Redo();
        yield return (Notes(eng, t, c) == 1 && eng.Sections.Count == 1 && eng.DeviceGetParam(t, d, 1) == 0.66f && !eng.CanRedo,
            "param undo: …and redo replays them in order");

        // G5: a device removed mid-gesture: the removal is the only step.
        eng.ClearUndoHistory();
        int d2 = eng.AddBuiltinDevice(t, Eq8);
        eng.ClearUndoHistory();
        int h = eng.BeginMixGesture(MixTarget.DeviceParam(t, d2, 1));
        eng.DeviceSetParam(t, d2, 1, 0.4f);
        eng.RemoveDevice(t, d2);
        eng.EndMixGesture(h);
        eng.Undo();
        yield return (eng.TrackDeviceCount(t) == 2 && !eng.CanUndo, "param undo: a device removed mid-gesture leaves only the removal step");

        // Knob, then remove the device: undo brings the device back, then its value.
        eng.ClearUndoHistory();
        float e0 = eng.DeviceGetParam(t, d2, 1);
        Gesture(eng, MixTarget.DeviceParam(t, d2, 1), () => eng.DeviceSetParam(t, d2, 1, e0 == 0.25f ? 0.75f : 0.25f));
        float e1 = eng.DeviceGetParam(t, d2, 1);
        eng.RemoveDevice(t, d2);
        eng.Undo(); bool devBack = eng.TrackDeviceCount(t) == 2 && eng.DeviceGetParam(t, d2, 1) == e1;
        eng.Undo();
        yield return (devBack && eng.DeviceGetParam(t, d2, 1) == e0, "param undo: undo restores a removed device, then its value");

        // Steps on two tracks, then one of the tracks removed: each undoes in its turn.
        eng.ClearUndoHistory();
        int t2 = eng.AddAudioTrack();
        int d3 = eng.AddBuiltinDevice(t2, Eq8);
        eng.ClearUndoHistory();
        Gesture(eng, MixTarget.DeviceParam(t, d, 1), () => eng.DeviceSetParam(t, d, 1, 0.1f));
        Gesture(eng, MixTarget.DeviceParam(t2, d3, 1), () => eng.DeviceSetParam(t2, d3, 1, eng.DeviceGetParam(t2, d3, 1) == 0.5f ? 0.2f : 0.5f));
        eng.RemoveTrack(t2);
        eng.Undo();                       // the removal
        eng.Undo();                       // t2's value
        eng.Undo();                       // t's value
        yield return (eng.DeviceGetParam(t, d, 1) == 0.66f && !eng.CanUndo, "param undo: steps on two tracks and a removal undo in order");
    }

    // ---- G6 and loads: no steps ---------------------------------------------------------

    private static IEnumerable<(bool, string)> NotEdits()
    {
        var buf = new float[512 * 2];
        using (var eng = new NotaEngine())
        {
            eng.SetBpm(120);
            int t = eng.AddInstrumentTrack();
            int d = eng.AddBuiltinDevice(t, Reverb);
            int lane = eng.AddAutomationLane(t, AutomationTarget.DeviceParam, d, 1);
            eng.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0, 0.1f), new AutomationPoint(8, 0.9f) });
            eng.ClearUndoHistory();
            float a = eng.DeviceGetParam(t, d, 1);
            eng.Seek(0); eng.Play();
            for (int i = 0; i < 200; i++) eng.RenderOffline(buf, 512, 48000);
            eng.StopTransport();
            yield return (!eng.CanUndo && eng.DeviceGetParam(t, d, 1) != a,
                $"param undo: automation playback moves a device parameter ({a} → {eng.DeviceGetParam(t, d, 1)}) and adds no step");
        }

        using (var eng = new NotaEngine())
        {
            ProjectTemplate.ApplyDefault(eng);
            int t = eng.AddInstrumentTrack();
            int d = eng.AddBuiltinDevice(t, Reverb);
            eng.DeviceSetParam(t, d, 1, 0.42f);
            eng.SetSections(new[] { new SectionInfo(0, 8, "A") });
            string dir = TempDir();
            try
            {
                new ProjectStore().Save(eng, new TransportState(120, 1, false, false), dir);
                using var e2 = new NotaEngine();
                new ProjectStore().Load(e2, dir);
                // Loading replays the tracks through edit ops, which the history keeps; the sections
                // are the baseline under all of them, so no undo after a load takes them away.
                bool kept = e2.Sections.Count == 1;
                for (int i = 0; i < 300 && e2.CanUndo; i++) { e2.Undo(); kept &= e2.Sections.Count == 1; }
                yield return (kept, "param undo: undoing after a load never removes the loaded sections");
            }
            finally { Cleanup(dir); }
        }
    }

    // ---- US3: sections as project data ---------------------------------------------------

    private static IEnumerable<(bool, string)> Sections()
    {
        using var eng = new NotaEngine();
        yield return (eng.Sections.Count == 0, "sections: a new engine has none");
        eng.ClearUndoHistory();

        // create → rename → move → resize → duplicate → delete, each one step.
        var steps = new List<SectionInfo[]>
        {
            new[] { new SectionInfo(0, 16, "Section 1") },
            new[] { new SectionInfo(0, 16, "Intro") },
            new[] { new SectionInfo(8, 16, "Intro") },
            new[] { new SectionInfo(8, 32, "Intro") },
            new[] { new SectionInfo(8, 32, "Intro"), new SectionInfo(40, 32, "Intro") },
            new[] { new SectionInfo(40, 32, "Intro") },
        };
        foreach (var s in steps) eng.SetSections(s);
        bool atEnd = Same(eng.Sections, steps[^1]);
        bool reverse = true;
        for (int i = steps.Count - 2; i >= 0; i--) { eng.Undo(); reverse &= Same(eng.Sections, steps[i]); }
        eng.Undo(); reverse &= eng.Sections.Count == 0 && !eng.CanUndo;
        yield return (atEnd && reverse, "sections: create, rename, move, resize, duplicate and delete undo one at a time");
        bool forward = true;
        foreach (var s in steps) { eng.Redo(); forward &= Same(eng.Sections, s); }
        yield return (forward && !eng.CanRedo, "sections: …and redo replays them");

        // The same list again is no step; the engine normalises what it is given.
        eng.ClearUndoHistory();
        eng.SetSections(steps[^1]);
        yield return (!eng.CanUndo, "sections: setting the same list makes no step");
        eng.SetSections(new[] { new SectionInfo(32, 8, "B"), new SectionInfo(-4, 8, "A"), new SectionInfo(4, 0, "empty"), new SectionInfo(8, double.NaN, "nan") });
        var norm = eng.Sections;
        yield return (norm.Count == 2 && norm[0] == new SectionInfo(0, 8, "A") && norm[1] == new SectionInfo(32, 8, "B"),
            $"sections: stored sorted, start clamped to 0, empty spans dropped ({string.Join("; ", norm)})");

        // UTF-8 names survive the boundary.
        eng.SetSections(new[] { new SectionInfo(0, 4, "Куплет · drop ✦") });
        yield return (eng.Sections.Count == 1 && eng.Sections[0].Name == "Куплет · drop ✦", "sections: a non-ASCII name round-trips");

        // A load-style set (checkpoint off) is no step; reset clears them.
        eng.ClearUndoHistory();
        eng.SetSections(new[] { new SectionInfo(0, 4, "x") }, checkpoint: false);
        bool noStep = !eng.CanUndo && eng.Sections.Count == 1;
        eng.Reset();
        yield return (noStep && eng.Sections.Count == 0, "sections: a baseline set makes no step and reset clears them");

        // Sections survive edits that publish a new graph (a track add) and undo of those edits.
        eng.SetSections(new[] { new SectionInfo(0, 4, "keep") }, checkpoint: false);
        eng.ClearUndoHistory();
        int t = eng.AddAudioTrack();
        eng.SetTrackName(t, "x");
        eng.Undo();
        yield return (eng.Sections.Count == 1 && eng.Sections[0].Name == "keep", "sections: structural edits and their undo keep the sections");
    }

    private static IEnumerable<(bool, string)> SectionFiles()
    {
        // Save → load keeps them; the manifest holds them.
        string dir = TempDir();
        try
        {
            using var eng = new NotaEngine();
            ProjectTemplate.ApplyDefault(eng);
            var want = new[] { new SectionInfo(0, 16, "Intro"), new SectionInfo(16, 32, "Verse") };
            eng.SetSections(want);
            new ProjectStore().Save(eng, new TransportState(120, 1, false, false), dir);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "project.json")))!;
            bool inManifest = json["sections"] is JsonArray arr && arr.Count == 2;
            using var e2 = new NotaEngine();
            new ProjectStore().Load(e2, dir);
            yield return (inManifest && Same(e2.Sections, want), "sections: saved in project.json and loaded back");

            // An older bundle: no `sections` member, a sections.json sidecar → migrated, and the
            // next save drops the sidecar.
            json.AsObject().Remove("sections");
            File.WriteAllText(Path.Combine(dir, "project.json"), json.ToJsonString());
            File.WriteAllText(Path.Combine(dir, ProjectService.LegacySectionsSidecar),
                "[{\"Start\":4,\"Length\":8,\"Name\":\"Old\"},{\"Start\":12,\"Length\":0,\"Name\":\"bad\"}]");
            using var e3 = new NotaEngine();
            new ProjectStore().Load(e3, dir);
            bool migrated = e3.Sections.Count == 1 && e3.Sections[0] == new SectionInfo(4, 8, "Old");
            new ProjectStore().Save(e3, new TransportState(120, 1, false, false), dir);
            bool sidecarGone = !File.Exists(Path.Combine(dir, ProjectService.LegacySectionsSidecar));
            using var e4 = new NotaEngine();
            new ProjectStore().Load(e4, dir);
            yield return (migrated && sidecarGone && e4.Sections.Count == 1, "sections: an older project's sidecar is read once and removed on save");

            // A corrupt sidecar is no sections, not a failed load.
            json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "project.json")))!;
            json.AsObject().Remove("sections");
            File.WriteAllText(Path.Combine(dir, "project.json"), json.ToJsonString());
            File.WriteAllText(Path.Combine(dir, ProjectService.LegacySectionsSidecar), "{not json");
            using var e5 = new NotaEngine();
            new ProjectStore().Load(e5, dir);
            yield return (e5.Sections.Count == 0, "sections: a corrupt sidecar loads as none");
        }
        finally { Cleanup(dir); }
    }

    // ---- US4: MIDI Learn movement and MCP -------------------------------------------------

    private static IEnumerable<(bool, string)> Sources()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int d = eng.AddBuiltinDevice(t, Eq8);
        eng.ClearUndoHistory();

        // A controller knob on a device parameter: one step per movement.
        float c0 = eng.DeviceGetParam(t, d, 3);
        var knob = new MixMovement(eng, MixTarget.DeviceParam(t, d, 3), TimeSpan.FromSeconds(1));
        knob.Touch(() => eng.DeviceSetParam(t, d, 3, 0.3f), 0);
        knob.Touch(() => eng.DeviceSetParam(t, d, 3, 0.35f), 400);
        knob.Poll(1400);
        knob.Touch(() => eng.DeviceSetParam(t, d, 3, 0.6f), 3000);
        knob.Poll(4100);
        eng.Undo(); bool first = eng.DeviceGetParam(t, d, 3) == 0.35f;
        eng.Undo();
        yield return (first && eng.DeviceGetParam(t, d, 3) == c0 && !eng.CanUndo,
            "param undo: two controller movements on a device parameter are two steps");

        // MCP: the plain parameter tools and a multi-field device tool are one step each.
        var dev = new Nota.Mcp.Tools.DeviceTools(eng, new SyncDispatch(), new NoRefresh());
        var plug = new Nota.Mcp.Tools.InstrumentTools(eng, new SyncDispatch(), new NoRefresh());
        eng.ClearUndoHistory();
        float q0 = eng.DeviceGetParam(t, d, 1);
        dev.SetDeviceParam(t, d, 1, q0 == 0.4f ? 0.6f : 0.4f).GetAwaiter().GetResult();
        float i0 = eng.PluginParamGet(t, -1, 1);
        plug.SetInstrumentParam(t, 1, i0 > 0.5f ? 0.2f : 0.8f).GetAwaiter().GetResult();
        eng.Undo(); bool instBack = eng.PluginParamGet(t, -1, 1) == i0 && eng.DeviceGetParam(t, d, 1) != q0;
        eng.Undo();
        yield return (instBack && eng.DeviceGetParam(t, d, 1) == q0 && !eng.CanUndo,
            "param undo: MCP set_device_param and set_instrument_param are one step each");

        eng.ClearUndoHistory();
        var bandBefore = Enumerable.Range(0, 5).Select(i => eng.DeviceGetParam(t, d, 5 + i)).ToArray();
        dev.SetEq8Band(t, d, 2, on: true, freqHz: 1234, gainDb: 4.5, q: 2).GetAwaiter().GetResult();
        bool changed = Enumerable.Range(0, 5).Any(i => eng.DeviceGetParam(t, d, 5 + i) != bandBefore[i]);
        eng.Undo();
        bool bandBack = Enumerable.Range(0, 5).All(i => eng.DeviceGetParam(t, d, 5 + i) == bandBefore[i]);
        yield return (changed && bandBack && !eng.CanUndo, "param undo: MCP set_eq8_band (four fields) is one step");

        // MCP sections round-trip, one step.
        var proj = new Nota.Mcp.Tools.ProjectTools(eng, new SyncDispatch(), new NoRefresh());
        eng.ClearUndoHistory();
        var stored = proj.SetSections(new[] { new Nota.Mcp.Tools.ProjectTools.Section(16, 8, "Drop"), new Nota.Mcp.Tools.ProjectTools.Section(0, 16, "Build") })
            .GetAwaiter().GetResult();
        var listed = proj.ListSections().GetAwaiter().GetResult();
        bool ok = stored.Length == 2 && stored[0].Name == "Build" && listed.SequenceEqual(stored);
        eng.Undo();
        yield return (ok && eng.Sections.Count == 0 && !eng.CanUndo, "sections: MCP set_sections / list_sections round-trip as one step");
    }

    // ---- helpers ------------------------------------------------------------------------

    private static void Gesture(NotaEngine e, MixTarget t, Action set)
    {
        int h = e.BeginMixGesture(t);
        set();
        e.EndMixGesture(h);
    }

    private static int Notes(NotaEngine e, int t, int c) => e.GetClipNotes(t, c).Length;

    private static bool Same(IReadOnlyList<SectionInfo> a, IReadOnlyList<SectionInfo> b) => a.Count == b.Count && a.SequenceEqual(b);

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "nota-paramundo-" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}
