// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EQ-8 band labels and notes (specs/049, #135): each band keeps a label and a note, cleaned to
// their limits; an edit is one undo step; they ride on the device's state through a project, a
// user preset, a device duplicate and a track duplicate; a factory preset / Init clears them; a
// plain EQ-8 still saves no state; MCP reads them; and the sound is bit-identical with or
// without them. Also runnable alone: `--eq-labels`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class EqLabelTests
{
    private const int Eq8Kind = 0, LabelsP = 62, Rate = 44100;

    public static IEnumerable<(bool, string)> Run()
    {
        var tmp = new List<string>();
        try
        {
            foreach (var r in Text()) yield return r;
            foreach (var r in Undo()) yield return r;
            foreach (var r in Copies()) yield return r;
            foreach (var r in Project()) yield return r;
            foreach (var r in Presets()) yield return r;
            foreach (var r in Sound(tmp)) yield return r;
        }
        finally { foreach (var f in tmp) try { File.Delete(f); } catch (IOException) { } }
    }

    private static (NotaEngine E, int T, int D) NewEq()
    {
        var e = new NotaEngine();
        int t = e.AddAudioTrack();
        return (e, t, e.AddBuiltinDevice(t, Eq8Kind));
    }

    private static string L(IAudioEngine e, int t, int d, int b) => e.EqBandText(t, d, b, 0);
    private static string N(IAudioEngine e, int t, int d, int b) => e.EqBandText(t, d, b, 1);

    private static IEnumerable<(bool, string)> Text()
    {
        var (e, t, d) = NewEq();
        using var _ = e;
        yield return (e.DeviceParamCount(t, d) == 63 && e.DeviceParamName(t, d, LabelsP) == "Labels" && e.DeviceGetParam(t, d, LabelsP) == 1f
                      && e.DeviceGetState(t, d).Length == 0 && L(e, t, d, 0) == "",
            "EQ-8 appends Labels (on by default); a fresh EQ-8 has no labels and saves no state");

        bool set = e.EqSetBandText(t, d, 3, "  Resonance ", "Narrow cut for the ring.\nDon't go past −6 dB.");
        yield return (set && L(e, t, d, 3) == "Resonance" && N(e, t, d, 3) == "Narrow cut for the ring.\nDon't go past −6 dB."
                      && L(e, t, d, 2) == "",
            $"a band takes a label (trimmed) and a multiline note: \"{L(e, t, d, 3)}\"");

        e.EqSetBandText(t, d, 0, "Гул\nснизу\tи", "");
        string longLabel = new string('x', 40), longNote = new string('n', 2500);
        e.EqSetBandText(t, d, 1, longLabel, longNote);
        yield return (L(e, t, d, 0) == "Гул снизу и" && L(e, t, d, 1).Length == 24 && N(e, t, d, 1).Length == 2000,
            $"labels keep Cyrillic, turn line breaks into spaces (\"{L(e, t, d, 0)}\") and stop at 24 characters, notes at 2000");

        bool same = e.EqSetBandText(t, d, 3, "Resonance", N(e, t, d, 3));
        bool bad = e.EqSetBandText(t, d, 8, "x", "") || e.EqSetBandText(t, d, -1, "x", "");
        int other = e.AddBuiltinDevice(t, 3);   // a Delay
        bool notEq = e.EqSetBandText(t, other, 0, "x", "");
        yield return (!same && !bad && !notEq && L(e, t, other, 0) == "",
            "the same text changes nothing; bands outside 1–8 and other devices are refused");

        string summary = e.DeviceText(t, d, 0);
        yield return (summary.Contains("B4 label \"Resonance\"") && summary.Contains("note \"Narrow cut"),
            "the device text (MCP) lists the labels and notes");
    }

    private static IEnumerable<(bool, string)> Undo()
    {
        var (e, t, d) = NewEq();
        using var _ = e;
        e.EqSetBandText(t, d, 2, "Mud", "");
        e.EqSetBandText(t, d, 2, "Mud", "200 Hz build-up");
        bool u1 = e.Undo();
        bool afterOne = L(e, t, d, 2) == "Mud" && N(e, t, d, 2) == "";
        bool u2 = e.Undo();
        bool afterTwo = L(e, t, d, 2) == "" && e.DeviceGetState(t, d).Length == 0;
        bool r = e.Redo() && e.Redo();
        yield return (u1 && afterOne && u2 && afterTwo && r && N(e, t, d, 2) == "200 Hz build-up",
            "each label or note edit is one undo step, and redo brings it back");

        // A band param edit in between keeps its own step.
        e.DeviceSetParam(t, d, 2 * 5 + 2, 250f);
        e.EqSetBandText(t, d, 2, "Mud 250", "200 Hz build-up");
        e.Undo();
        yield return (L(e, t, d, 2) == "Mud" && e.DeviceGetParam(t, d, 2 * 5 + 2) == 250f,
            "undoing a label leaves the band's params as they are");
    }

    private static IEnumerable<(bool, string)> Copies()
    {
        var (e, t, d) = NewEq();
        using var _ = e;
        e.EqSetBandText(t, d, 5, "Air", "Gentle lift");
        int dd = e.DuplicateDevice(t, d);
        int t2 = e.DuplicateTrack(t);
        int d2 = -1;
        for (int i = 0; t2 > 0 && i < e.TrackDeviceCount(t2); i++)
            if (e.TrackDeviceBuiltinKind(t2, i) == Eq8Kind) { d2 = i; break; }
        yield return (dd >= 0 && L(e, t, dd, 5) == "Air" && N(e, t, dd, 5) == "Gentle lift" && d2 >= 0 && L(e, t2, d2, 5) == "Air",
            "a duplicated device and a duplicated track keep the labels and notes");
        e.EqSetBandText(t, dd, 5, "Copy", "");
        yield return (L(e, t, d, 5) == "Air", "editing the copy leaves the original alone");
    }

    private static IEnumerable<(bool, string)> Project()
    {
        var (e, t, d) = NewEq();
        using var _ = e;
        for (int b = 0; b < 8; b++) e.EqSetBandText(t, d, b, $"Band {b + 1} ✓", $"Line one of {b + 1}\nЛиния два");
        e.DeviceSetParam(t, d, LabelsP, 0f);
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-eql-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProjectService.Save(doc, dir, e);
            doc = ProjectService.Load(dir);
            using var back = new NotaEngine();
            ProjectService.Apply(doc, back, dir);
            int bt = -1;
            for (int i = 0; i < back.TrackCount; i++)
                if (back.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup) { bt = ti.Id; break; }
            bool all = bt >= 0;
            for (int b = 0; b < 8 && all; b++)
                all = L(back, bt, 0, b) == L(e, t, d, b) && N(back, bt, 0, b) == N(e, t, d, b);
            yield return (all && back.DeviceGetParam(bt, 0, LabelsP) == 0f
                          && back.DeviceGetState(bt, 0).AsSpan().SequenceEqual(e.DeviceGetState(t, d)),
                "all eight labels and notes and the Labels switch come back byte-identical from a saved project");

            // An EQ-8 saved before labels: 62 params, no state.
            var dev = doc.Tracks.SelectMany(tr => tr.Devices).First(x => x.BuiltinKind == Eq8Kind);
            dev.Params = dev.Params[..62];
            dev.State = null;
            using var old = new NotaEngine();
            ProjectService.Apply(doc, old, dir);
            int ot = -1;
            for (int i = 0; i < old.TrackCount; i++)
                if (old.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup) { ot = ti.Id; break; }
            yield return (ot >= 0 && L(old, ot, 0, 0) == "" && old.DeviceGetParam(ot, 0, LabelsP) == 1f,
                "an older project opens with no labels and Labels on");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static IEnumerable<(bool, string)> Presets()
    {
        var (e, t, d) = NewEq();
        using var _ = e;
        e.EqSetBandText(t, d, 1, "Body", "Warmth around 300 Hz");
        e.DeviceSetParam(t, d, 1 * 5 + 3, 3f);
        var doc = PresetService.Capture(e, t, d, "Vocal");
        yield return (doc?.BandText is { Count: 1 } bt && bt[0].Band == 2 && bt[0].Label == "Body",
            "a user preset of an EQ-8 saves its band labels and notes");

        // Through the file format, then onto a new EQ-8 and in place onto another one.
        string json = System.Text.Json.JsonSerializer.Serialize(doc);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PresetDocument>(json)!;
        int t2 = e.AddAudioTrack();
        PresetService.Apply(loaded, e, t2);
        int nd = e.TrackDeviceCount(t2) - 1;
        int other = e.AddBuiltinDevice(t2, Eq8Kind);
        e.EqSetBandText(t2, other, 6, "Old", "left over");
        PresetService.ApplyInPlace(loaded, e, t2, other);
        yield return (L(e, t2, nd, 1) == "Body" && N(e, t2, nd, 1) == "Warmth around 300 Hz"
                      && L(e, t2, other, 1) == "Body" && L(e, t2, other, 6) == "",
            "loading it, new or in place, gives the EQ-8 exactly the preset's labels");

        // A preset without labels (a factory one) and Init clear them.
        var plain = new PresetDocument { Type = "builtin-effect", BuiltinKind = Eq8Kind, NamedParams = new() };
        PresetService.ApplyInPlace(plain, e, t, d);
        bool clearedByPreset = L(e, t, d, 1) == "";
        e.EqSetBandText(t, d, 4, "Again", "");
        PresetService.ResetInPlace(e, t, d, false);
        yield return (clearedByPreset && L(e, t, d, 4) == "", "a factory preset and Init clear the labels");

        var tools = new Nota.Mcp.Tools.DeviceTools(e, new SyncDispatch(), new NoRefresh());
        var band = tools.SetEq8Band(t, d, 3, label: "Presence", note: "Keep it subtle").GetAwaiter().GetResult();
        yield return (band.Label == "Presence" && band.Note == "Keep it subtle" && L(e, t, d, 2) == "Presence",
            "MCP set_eq8_band sets a band's label and note, and read_eq8 reads them back");
    }

    private static IEnumerable<(bool, string)> Sound(List<string> tmp)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-eql-{Guid.NewGuid():N}.wav");
        int n = Rate * 2;
        var x = new float[n * 2];
        for (int i = 0; i < n; i++) x[2 * i] = x[2 * i + 1] = (float)(0.25 * Math.Sin(2 * Math.PI * 220 * i / Rate) + 0.1 * Math.Sin(2 * Math.PI * 3100 * i / Rate));
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, n);
        tmp.Add(path);

        float[] RenderWith(bool labelled)
        {
            using var e = new NotaEngine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, path, 0.0);
            int d = e.AddBuiltinDevice(t, Eq8Kind);
            e.DeviceSetParam(t, d, 1 * 5 + 3, 6f);   // band 2 bell +6 dB, so the EQ does something
            if (labelled)
            {
                for (int b = 0; b < 8; b++) e.EqSetBandText(t, d, b, $"B{b}", "note");
                e.DeviceSetParam(t, d, LabelsP, 0f);
            }
            var buf = new float[16384 * 2];
            e.Seek(0); e.Play(); e.RenderOffline(buf, 16384); e.StopTransport();
            return buf;
        }
        var plain = RenderWith(false);
        var labelled = RenderWith(true);
        yield return (plain.AsSpan().SequenceEqual(labelled) && plain.Any(v => Math.Abs(v) > 0.01f),
            "labels, notes and the Labels switch leave the sound bit-identical");
    }
}
