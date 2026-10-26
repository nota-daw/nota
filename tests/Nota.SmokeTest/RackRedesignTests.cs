// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Racks redesign (specs/040-racks-redesign): what the reworked rack engine must keep and add.
// - v8 compatibility: rack blobs saved before the change (Fixtures/racks-v8) load into the
//   same settings, and the Audio Effect Rack renders the same samples (FR-029, SC-004).
//   The Instrument Rack is compared by its settings, not its audio: the built-in synths
//   free-run their oscillators, so two renders of the same notes never match sample for sample.
// - mapping rules, the selector as a macro target, the key-parameter table.
// Fixtures are written once, on the engine from before the change:
//   `dotnet run --project tests/Nota.SmokeTest -- --rack-fixtures`.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

using System.Globalization;
using System.Text;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class RackRedesignTests
{
    private const int Rate = 48000, Block = 512, EffectFrames = 24 * Block; // ~0.26 s of audio
    private const int Synth = 0, Physical = 2, Volt = 6;               // instrument kinds
    private const int Eq3 = 16, Reverb = 2, Utility = 4, EffectRack = 5; // device kinds
    private const int RackControl = -3, Selector = 0;                    // NOTA_RACK_CONTROL target

    /// <summary>The smoke-test sections, in run order.</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield return ("racks: v8 compatibility", V8Compatibility);
        yield return ("racks: mapping rules", MappingRules);
        yield return ("racks: key params table", KeyParamsTable);
    }

    // ---- fixtures -------------------------------------------------------------------

    private static string FixtureDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        if (d is null) throw new InvalidOperationException("repo root not found");
        return Path.Combine(d.FullName, "tests", "Nota.SmokeTest", "Fixtures", "racks-v8");
    }

    /// <summary>Writes the v8 fixtures. Run on the engine from before the racks redesign only.</summary>
    public static void WriteFixtures()
    {
        string dir = FixtureDir();
        Directory.CreateDirectory(dir);

        using (var e = new NotaEngine())
        {
            int t = BuildInstrumentRack(e);
            File.WriteAllBytes(Path.Combine(dir, "instrument.bin"), e.GetPluginState(t, -1));
            File.WriteAllText(Path.Combine(dir, "instrument.txt"), DumpInstrumentRack(e, t));
        }

        string wav = ToneFile();
        using (var e = new NotaEngine())
        {
            var (t, d) = BuildEffectRack(e, wav);
            File.WriteAllBytes(Path.Combine(dir, "effect.bin"), e.GetPluginState(t, d));
            File.WriteAllText(Path.Combine(dir, "effect.txt"), DumpEffectRack(e, t, d));
            File.WriteAllBytes(Path.Combine(dir, "effect.f32"), FloatsToBytes(RenderEffect(e)));
        }
        Console.WriteLine($"rack fixtures written to {dir}");
    }

    // Three chains with zones, devices, names and mix; four macros, one of them with two
    // targets on different curves (the "Mixed" case), one inverted range; glide set.
    private static int BuildInstrumentRack(NotaEngine e)
    {
        int t = e.AddInstrumentRackTrack();
        while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);
        int a = e.RackAddChain(t, Synth), b = e.RackAddChain(t, Physical), c = e.RackAddChain(t, Volt);
        e.RackSetChainZone(t, a, 0, 70, 0, 127);
        e.RackSetChainZone(t, b, 58, 127, 0, 127);
        e.RackSetChainZone(t, c, 36, 84, 96, 127);
        e.RackSetChainName(t, a, "Low");
        e.RackSetChainName(t, c, "Hard hits");
        e.RackSetChainGain(t, a, 0.8f);
        e.RackSetChainPan(t, b, 0.2f);
        e.RackSetChainPan(t, c, -0.15f);
        e.RackSetChainMute(t, b, true);
        e.RackSetChainSolo(t, c, true);
        e.RackChainInstrumentParamSet(t, a, 0, 0.62f);
        int eq = e.RackAddChainDevice(t, a, Eq3);
        e.RackChainDeviceParamSet(t, a, eq, 0, 3.5f);
        e.RackAddChainDevice(t, c, Reverb);

        e.RackSetMacroName(t, 0, "Bright");
        e.RackSetMacroName(t, 1, "Space");
        e.RackMacroSet(t, 0, 0.6f);
        e.RackMacroSet(t, 1, 0.35f);
        e.RackMacroSet(t, 3, 0.25f);
        int m0 = e.RackAddMacroMapping(t, 0, a, -1, 0, 0.3f, 0.9f);
        int m1 = e.RackAddMacroMapping(t, 0, b, -1, 1, 0.2f, 0.8f);
        e.RackSetMappingCurve(t, m0, 0);   // Lin
        e.RackSetMappingCurve(t, m1, 2);   // Log: macro 1 is "Mixed"
        int m2 = e.RackAddMacroMapping(t, 1, c, 0, 0, 0.6f, 0.0f); // inverted
        e.RackSetMappingCurve(t, m2, 1);   // Exp
        int m3 = e.RackAddMacroMapping(t, 3, a, 0, 0, -6f, 6f);
        e.RackSetMappingCurve(t, m3, 3);   // S
        e.RackSetVolume(t, 0.8f);
        e.RackSetGlide(t, 0.3f);
        return t;
    }

    // Select mode, two deterministic chains (Utility gain, EQ-3), select ranges that overlap
    // around the selector, dry/wet, latency compensation, one macro target.
    private static (int t, int d) BuildEffectRack(NotaEngine e, string wav)
    {
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, 0);
        int d = e.AddBuiltinDevice(t, EffectRack);
        while (e.RackDevChainCount(t, d) > 0) e.RackDevRemoveChain(t, d, 0);
        int a = e.RackDevAddChain(t, d, -1), b = e.RackDevAddChain(t, d, -1);
        int u = e.RackDevAddChainDevice(t, d, a, Utility);
        e.RackDevChainDeviceParamSet(t, d, a, u, 0, -6f);
        int q = e.RackDevAddChainDevice(t, d, b, Eq3);
        e.RackDevChainDeviceParamSet(t, d, b, q, 0, 6f);
        e.RackDevSetChainZone(t, d, a, 0, 63);
        e.RackDevSetChainZone(t, d, b, 30, 127);
        e.RackDevSetChainGain(t, d, b, 0.7f);
        e.RackDevSetChainPan(t, d, a, -0.3f);
        e.RackDevSetMode(t, d, 2);
        e.RackDevSetChainSelect(t, d, 0.3f);
        e.RackDevSetDryWet(t, d, 0.7f);
        e.RackDevSetPdc(t, d, true);
        e.RackDevSetVolume(t, d, 0.9f);
        e.RackDevSetMacroName(t, d, 0, "Tone");
        e.RackDevMacroSet(t, d, 0, 0.4f);
        int m = e.RackDevAddMacroMapping(t, d, 0, b, q, 2, -12f, 12f);
        e.RackDevSetMappingCurve(t, d, m, 3);
        return (t, d);
    }

    private static string ToneFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-rack-fixture-tones.wav");
        WavWriter.WriteTones(path, 2.0, new[] { 110.0, 880.0, 5000.0 }, 0.2, Rate);
        return path;
    }

    private static float[] RenderEffect(NotaEngine e)
    {
        var all = new float[EffectFrames * 2];
        var buf = new float[Block * 2];
        e.Seek(0);
        e.Play();
        for (int done = 0; done < EffectFrames; done += Block)
        {
            int n = Math.Min(Block, EffectFrames - done);
            e.RenderOffline(buf, n, Rate);
            Array.Copy(buf, 0, all, done * 2, n * 2);
        }
        e.Stop();
        return all;
    }

    // ---- settings dumps (the comparison key for v8 loads) ----------------------------

    private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    public static string DumpInstrumentRack(NotaEngine e, int t)
    {
        var s = new StringBuilder();
        int n = e.RackChainCount(t);
        s.AppendLine($"chains {n}");
        for (int c = 0; c < n; c++)
        {
            e.RackChainZone(t, c, out int kl, out int kh, out int vl, out int vh);
            s.AppendLine($"chain {c} name='{e.RackChainName(t, c)}' inst={e.RackChainInstrumentKind(t, c)} " +
                         $"gain={F(e.RackChainGain(t, c))} pan={F(e.RackChainPan(t, c))} mute={e.RackChainMute(t, c)} " +
                         $"solo={e.RackChainSolo(t, c)} keys={kl}-{kh} vel={vl}-{vh}");
            int ip = Math.Min(8, e.RackChainInstrumentParamCount(t, c));
            for (int p = 0; p < ip; p++) s.AppendLine($"  inst p{p}={F(e.RackChainInstrumentParamGet(t, c, p))}");
            for (int d = 0; d < e.RackChainDeviceCount(t, c); d++)
            {
                s.Append($"  dev {d} kind={e.RackChainDeviceBuiltinKind(t, c, d)}");
                for (int p = 0; p < e.RackChainDeviceParamCount(t, c, d); p++) s.Append(' ').Append(F(e.RackChainDeviceParamGet(t, c, d, p)));
                s.AppendLine();
            }
        }
        for (int m = 0; m < 8; m++) s.AppendLine($"macro {m} '{e.RackMacroName(t, m)}' {F(e.RackMacroGet(t, m))}");
        for (int i = 0; i < e.RackMappingCount(t); i++)
            if (e.RackTryGetMapping(t, i, out var mm))
                s.AppendLine($"map {i} m{mm.Macro} c{mm.Chain} d{mm.DeviceIndex} p{mm.ParamIndex} {F(mm.RangeMin)}..{F(mm.RangeMax)} curve={e.RackMappingCurve(t, i)}");
        s.AppendLine($"volume {F(e.RackVolume(t))} glide {F(e.RackGlide(t))}");
        return s.ToString();
    }

    public static string DumpEffectRack(NotaEngine e, int t, int d)
    {
        var s = new StringBuilder();
        int n = e.RackDevChainCount(t, d);
        s.AppendLine($"chains {n}");
        for (int c = 0; c < n; c++)
        {
            e.RackDevChainZone(t, d, c, out int lo, out int hi);
            s.AppendLine($"chain {c} gain={F(e.RackDevChainGain(t, d, c))} pan={F(e.RackDevChainPan(t, d, c))} " +
                         $"mute={e.RackDevChainMute(t, d, c)} solo={e.RackDevChainSolo(t, d, c)} select={lo}-{hi}");
            for (int k = 0; k < e.RackDevChainDeviceCount(t, d, c); k++)
            {
                s.Append($"  dev {k} kind={e.RackDevChainDeviceBuiltinKind(t, d, c, k)}");
                for (int p = 0; p < e.RackDevChainDeviceParamCount(t, d, c, k); p++) s.Append(' ').Append(F(e.RackDevChainDeviceParamGet(t, d, c, k, p)));
                s.AppendLine();
            }
        }
        for (int m = 0; m < 8; m++) s.AppendLine($"macro {m} '{e.RackDevMacroName(t, d, m)}' {F(e.RackDevMacroGet(t, d, m))}");
        for (int i = 0; i < e.RackDevMappingCount(t, d); i++)
            if (e.RackDevTryGetMapping(t, d, i, out var mm))
                s.AppendLine($"map {i} m{mm.Macro} c{mm.Chain} d{mm.DeviceIndex} p{mm.ParamIndex} {F(mm.RangeMin)}..{F(mm.RangeMax)} curve={e.RackDevMappingCurve(t, d, i)}");
        s.AppendLine($"mode {e.RackDevMode(t, d)} select {F(e.RackDevChainSelect(t, d))} drywet {F(e.RackDevDryWet(t, d))} " +
                     $"pdc {e.RackDevPdc(t, d)} volume {F(e.RackDevVolume(t, d))}");
        return s.ToString();
    }

    private static byte[] FloatsToBytes(float[] f)
    {
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return b;
    }

    private static float[] BytesToFloats(byte[] b)
    {
        var f = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
        return f;
    }

    // ---- sections -------------------------------------------------------------------

    private static IEnumerable<(bool, string)> V8Compatibility()
    {
        string dir = FixtureDir();
        if (!File.Exists(Path.Combine(dir, "instrument.bin")))
        {
            yield return (false, $"v8 fixtures missing in {dir}");
            yield break;
        }

        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentRackTrack();
            e.SetPluginState(t, -1, File.ReadAllBytes(Path.Combine(dir, "instrument.bin")));
            string want = File.ReadAllText(Path.Combine(dir, "instrument.txt")), got = DumpInstrumentRack(e, t);
            yield return (got == want, "v8 Instrument Rack blob loads with every setting (zones, mix, macros, per-target curves, glide)"
                                       + (got == want ? "" : $"\n--- want\n{want}--- got\n{got}"));
        }

        using (var e = new NotaEngine())
        {
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, ToneFile(), 0);
            int d = e.AddBuiltinDevice(t, EffectRack);
            e.SetPluginState(t, d, File.ReadAllBytes(Path.Combine(dir, "effect.bin")));
            string want = File.ReadAllText(Path.Combine(dir, "effect.txt")), got = DumpEffectRack(e, t, d);
            yield return (got == want, "v8 Audio Effect Rack blob loads with every setting (Select mode, select ranges, dry/wet, PDC)"
                                       + (got == want ? "" : $"\n--- want\n{want}--- got\n{got}"));
            var refAudio = BytesToFloats(File.ReadAllBytes(Path.Combine(dir, "effect.f32")));
            var audio = RenderEffect(e);
            double maxDiff = 0, peak = 0;
            for (int i = 0; i < Math.Min(audio.Length, refAudio.Length); i++)
            {
                maxDiff = Math.Max(maxDiff, Math.Abs(audio[i] - refAudio[i]));
                peak = Math.Max(peak, Math.Abs(refAudio[i]));
            }
            yield return (audio.Length == refAudio.Length && peak > 0.01 && maxDiff < 1e-5,
                string.Create(CultureInfo.InvariantCulture, $"v8 Audio Effect Rack renders the same samples as before (max diff {maxDiff:E1}, peak {peak:F3})"));
        }
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    private static IEnumerable<(bool, string)> MappingRules()
    {
        using var e = new NotaEngine();

        // One macro per target: mapping a mapped parameter again moves it, range kept.
        int t = e.AddInstrumentRackTrack();
        while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);
        int a = e.RackAddChain(t, Synth), b = e.RackAddChain(t, Physical);
        int m = e.RackAddMacroMapping(t, 0, a, -1, 5, 0.2f, 0.7f);
        int again = e.RackAddMacroMapping(t, 3, a, -1, 5, 0f, 1f);
        e.RackTryGetMapping(t, again, out var moved);
        yield return (again == m && e.RackMappingCount(t) == 1 && moved.Macro == 3 && Near(moved.RangeMin, 0.2f) && Near(moved.RangeMax, 0.7f),
            $"mapping a mapped parameter moves it to the new macro and keeps its range ({e.RackMappingCount(t)} mapping, macro {moved.Macro})");
        yield return (e.Undo() && e.RackTryGetMapping(t, 0, out var back) && back.Macro == 0, "moving a mapping to another macro is one undo step");

        // Deleting a chain drops its mappings and re-indexes the rest.
        e.RackAddMacroMapping(t, 1, b, -1, 0, 0f, 1f);
        e.RackRemoveChain(t, a);
        e.RackTryGetMapping(t, 0, out var left);
        yield return (e.RackMappingCount(t) == 1 && left.Chain == 0 && left.Macro == 1,
            $"deleting a chain drops its mappings and shifts the others ({e.RackMappingCount(t)} left, on chain {left.Chain})");

        // The selector as a macro target (Audio Effect Rack): the chain argument is ignored.
        int at = e.AddAudioTrack();
        int d = e.AddBuiltinDevice(at, EffectRack);
        while (e.RackDevChainCount(at, d) < 2) e.RackDevAddChain(at, d, -1);
        int sm = e.RackDevAddMacroMapping(at, d, 1, 99, RackControl, Selector, 0.2f, 0.8f);
        e.RackDevMacroSet(at, d, 1, 1f);
        float hi = e.RackDevChainSelect(at, d);
        e.RackDevMacroSet(at, d, 1, 0f);
        float lo = e.RackDevChainSelect(at, d);
        yield return (sm >= 0 && Near(hi, 0.8f) && Near(lo, 0.2f),
            string.Create(CultureInfo.InvariantCulture, $"a macro mapped to the selector drives it over its range ({lo:0.##}–{hi:0.##})"));
        e.RackDevRemoveChain(at, d, 0);
        yield return (e.RackDevMappingCount(at, d) == 1, "the selector mapping survives deleting a chain");

        // Mappings, the selector target and curves survive the blob (v9 round trip).
        e.RackDevSetMappingCurve(at, d, sm, 2);
        string before = DumpEffectRack(e, at, d);
        int at2 = e.AddAudioTrack();
        int d2 = e.AddBuiltinDevice(at2, EffectRack);
        e.SetPluginState(at2, d2, e.GetPluginState(at, d));
        string after = DumpEffectRack(e, at2, d2);
        yield return (before == after, "a rack blob round-trips its selector mapping and curves" + (before == after ? "" : $"\n{before}\n{after}"));

        // A MIDI effect without a state blob reports none and refuses an empty one.
        int it = e.AddInstrumentTrack();
        int arp = e.AddMidiEffect(it, 0);
        yield return (e.MidiEffectGetState(it, arp).Length == 0 && !e.MidiEffectSetState(it, arp, Array.Empty<byte>(), true),
            "a params-only MIDI effect has no state blob");
    }

    private static IEnumerable<(bool, string)> KeyParamsTable()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentRackTrack();
        while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);

        foreach (int k in RackKeyParams.Kinds(RackDeviceFamily.Instrument))
        {
            int c = e.RackAddChain(t, k);
            if (c < 0 || e.RackChainInstrumentKind(t, c) != k) { yield return (false, $"instrument kind {k} can be a rack chain"); continue; }
            int n = e.RackChainInstrumentParamCount(t, c);
            yield return Check(RackDeviceFamily.Instrument, k, e.RackChainInstrumentName(t, c), n, p => e.RackChainInstrumentParamName(t, c, p));
        }
        int host = e.RackAddChain(t, Synth);
        foreach (int k in RackKeyParams.Kinds(RackDeviceFamily.AudioEffect))
        {
            int d = e.RackAddChainDevice(t, host, k);
            if (d < 0) { yield return (false, $"audio effect kind {k} can be a chain device"); continue; }
            int n = e.RackChainDeviceParamCount(t, host, d);
            yield return Check(RackDeviceFamily.AudioEffect, k, e.RackChainDeviceName(t, host, d), n, p => e.RackChainDeviceParamName(t, host, d, p));
        }
        int it = e.AddInstrumentTrack();
        foreach (int k in RackKeyParams.Kinds(RackDeviceFamily.MidiEffect))
        {
            int m = e.AddMidiEffect(it, k);
            if (m < 0) { yield return (false, $"MIDI effect kind {k} exists"); continue; }
            int n = e.MidiEffectParamCount(it, m);
            yield return Check(RackDeviceFamily.MidiEffect, k, e.MidiEffectName(it, m), n, p => e.MidiEffectParamName(it, m, p));
        }

        // A kind without an entry (the Sampler) falls back to its first parameters.
        var fb = RackKeyParams.Resolve(RackDeviceFamily.Instrument, 1, 5, p => $"P{p}");
        yield return (fb.SequenceEqual(new[] { 0, 1, 2 }), "a kind without key params shows its first three");
    }

    // Every curated name is found on the device, and the picks are distinct and in range.
    private static (bool, string) Check(RackDeviceFamily f, int kind, string name, int count, Func<int, string> paramName)
    {
        var want = RackKeyParams.Names(f, kind);
        var got = RackKeyParams.Resolve(f, kind, count, paramName);
        var missing = want.Where(w => !Enumerable.Range(0, count).Any(p => string.Equals(paramName(p), w, StringComparison.OrdinalIgnoreCase))).ToList();
        bool ok = want.Count is > 0 and <= RackKeyParams.Max && missing.Count == 0 && got.Length == Math.Min(RackKeyParams.Max, count)
                  && got.Distinct().Count() == got.Length && got.All(p => p >= 0 && p < count);
        return (ok, $"{name}: key params {string.Join(", ", got.Select(paramName))}" + (missing.Count > 0 ? $" (missing: {string.Join(", ", missing)})" : ""));
    }
}
