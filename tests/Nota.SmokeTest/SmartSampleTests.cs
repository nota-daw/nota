// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Smart samples: key math, what file names say (tempo / key / loop or one-shot), the
// classifier, the engine's analysis on generated audio (chord loops in known keys at known
// tempos, a kick, a plucked note, a hi-hat loop), and the library index end to end — scan,
// persistence, filters and "similar sounds". Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --smart-samples`.

using Nota.Application;
using Nota.Application.Samples;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SmartSampleTests
{
    private const int Sr = 44100;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        foreach (var r in KeyMath()) yield return r;
        foreach (var r in NameHints()) yield return r;
        foreach (var r in Classifier()) yield return r;
        foreach (var r in EngineAndIndex()) yield return r;
    }

    /// <summary>`--sample-dump <folder>`: indexes a real library into a throwaway index and
    /// prints what it made of every file — for eyeballing the classifier on actual packs.</summary>
    public static void Dump(string folder)
    {
        var file = Path.Combine(Path.GetTempPath(), "nota-sample-dump-" + Guid.NewGuid().ToString("N")[..8] + ".bin");
        var index = new SampleLibraryIndex(new SampleAnalyzer(), file);
        var done = new ManualResetEventSlim();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        index.Changed += () => { var (d, t) = index.Progress; if (t == 0) done.Set(); else Console.Error.Write($"\r{d}/{t}"); };
        index.Watch(folder);
        done.Wait();
        Console.Error.WriteLine($"\rindexed in {sw.Elapsed.TotalSeconds:0.0} s");
        foreach (var s in index.Search(SampleFilter.Empty, null, int.MaxValue))
            Console.WriteLine($"{Path.GetRelativePath(folder, s.Path),-70} {s.Kind,-8} {s.KindSource,-8} {s.Tag,-12} {s.Describe()}");
        index.Dispose();
        File.Delete(file);
    }

    // ---- key math ------------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> KeyMath()
    {
        var am = MusicalKey.Parse("Am")!.Value;
        var c = MusicalKey.Parse("C major")!.Value;
        yield return (am == new MusicalKey(9, KeyMode.Minor) && c == new MusicalKey(0, KeyMode.Major)
                      && MusicalKey.Parse("f#min") == new MusicalKey(6, KeyMode.Minor) && MusicalKey.Parse("Bb") == new MusicalKey(10, KeyMode.Major)
                      && MusicalKey.Parse("Ebm") == new MusicalKey(3, KeyMode.Minor) && MusicalKey.Parse("H") is null,
            "keys parse from the usual spellings");
        yield return (am.Short == "Am" && am.Long == "A minor" && new MusicalKey(1, KeyMode.Minor).Short == "C#m"
                      && new MusicalKey(1, KeyMode.Major).Short == "Db" && new MusicalKey(6, KeyMode.Note).Long == "F#",
            "…and print the way packs spell them");
        yield return (am.Relative == c && c.Relative == am && am.FitsIn(c) && c.FitsIn(am) && !am.FitsIn(new MusicalKey(9, KeyMode.Major)),
            "a key fits itself and its relative, not its parallel");
        yield return (am.SemitonesTo(c) == 0 && am.SemitonesTo(new MusicalKey(11, KeyMode.Minor)) == 2
                      && c.SemitonesTo(new MusicalKey(7, KeyMode.Major)) == -5 && am.SemitonesTo(new MusicalKey(2, KeyMode.Major)) == 2
                      && new MusicalKey(5, KeyMode.Note).SemitonesTo(c) == -5,
            "transposing to a key takes the short way, via the relative across modes");
        yield return (MusicalKey.All.Count == 24 && MusicalKey.All.All(k => MusicalKey.FromCode(k.Code) == k) && MusicalKey.FromCode(-1) is null,
            "the 24 keys round-trip through their codes");
    }

    // ---- file names --------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> NameHints()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib");
        SampleNameHints H(string rel) => SampleNameHints.Parse(Path.Combine(root, rel), root);

        var a = H("Deep House/Loops/DH_Loop_124_Am.wav");
        yield return (a.Bpm == 124 && !a.BpmExplicit && a.Key == new MusicalKey(9, KeyMode.Minor) && a.NameKind == SampleKind.Loop,
            "\"DH_Loop_124_Am\": a loop at 124 in A minor");
        var b = H("Pads/Warm Pad C#m 90bpm.flac");
        yield return (b.Bpm == 90 && b.BpmExplicit && b.Key == new MusicalKey(1, KeyMode.Minor),
            "\"Warm Pad C#m 90bpm\": tempo written with bpm, sharp minor key");
        var c = H("Bass/bass_bpm128_F_minor.wav");
        yield return (c.Bpm == 128 && c.BpmExplicit && c.Key == new MusicalKey(5, KeyMode.Minor), "\"bpm128 … F minor\": words across tokens");
        var d = H("Drums/One Shots/Kick A.wav");
        yield return (d.Key is null && d.FolderKind == SampleKind.OneShot && d.DrumWord && d.Bpm == 0,
            "\"One Shots/Kick A\": a one-shot folder, and a take letter is not a key");
        var e = H("Keys/Piano_F#3_soft.wav");
        yield return (e.Key == new MusicalKey(6, KeyMode.Note), "\"Piano_F#3\": a note with its octave is a pitch");
        var f = H("Synth/Stab Cmaj7 120.wav");
        yield return (f.Key == new MusicalKey(0, KeyMode.Major) && f.Bpm == 120, "\"Stab Cmaj7 120\": a chord names its key");
        var g = H("Loops/Snare_Roll_01.wav");
        yield return (g.Bpm == 0 && g.Key is null && g.FolderKind == SampleKind.Loop, "a take number is not a tempo");
        var h = H("Guitar/Riff_E_95.wav");
        yield return (h.Key == new MusicalKey(4, KeyMode.Major) && h.Bpm == 95, "a lone letter next to the tempo is a key");
        var i = H("Vox/i am here.wav");
        yield return (i.Key is null, "lowercase words aren't keys");
    }

    // ---- classifier --------------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Classifier()
    {
        static SampleAnalysis A(double dur, double bpm, int tonic = -1, int mode = -1, float conf = 0, float tail = 1)
            => new(dur, Sr, 2, bpm, tonic, mode, conf, tail, -3, -12, new float[SampleAnalysis.TimbreDims]);

        yield return (SampleClassifier.LoopFit(7.742, 123.5) == 124 && SampleClassifier.LoopFit(8.0, 60.5) == 120
                      && SampleClassifier.LoopFit(8.0, 97) == 0 && SampleClassifier.LoopFit(0.5, 120) == 0,
            "a loop's tempo comes from its length: whole beats near the heard pulse (an octave either way)");

        var loop = SampleClassifier.Classify("/x/thing.wav", SampleNameHints.None, A(7.742, 123.5, 9, 1, 0.8f));
        yield return (loop is { Kind: SampleKind.Loop, Bpm: 124, BpmSource: SampleFactSource.Duration } && loop.Key == new MusicalKey(9, KeyMode.Minor),
            "an unnamed 16-beat file with a pulse is a loop, at the exact tempo, with its heard key");
        var named = SampleClassifier.Classify("/x/Loop_126_Gm.wav", SampleNameHints.Parse("/x/Loop_126_Gm.wav"), A(7.62, 124, 0, 0, 0.9f));
        yield return (named.Bpm == 126 && named.BpmSource == SampleFactSource.Name && named.Key == new MusicalKey(7, KeyMode.Minor),
            "the name's tempo and key beat what the analysis heard");
        var hit = SampleClassifier.Classify("/x/thing2.wav", SampleNameHints.None, A(0.45, 130, 5, 0, 0.8f, tail: 0.01f));
        yield return (hit is { Kind: SampleKind.OneShot, Bpm: 0 } && hit.Key == new MusicalKey(5, KeyMode.Note),
            "a short hit is a one-shot: no tempo, and its pitch is a note, not a key");
        var tail = SampleClassifier.Classify("/x/boom.wav", SampleNameHints.None, A(3.1, 110, tail: 0.02f));
        yield return (tail.Kind == SampleKind.OneShot && tail.Bpm == 0, "a sound that rings out to silence is a one-shot, however long");
        var song = SampleClassifier.Classify("/x/take.wav", SampleNameHints.None, A(47.3, 97));
        yield return (song is { Kind: SampleKind.Unknown, Bpm: 97, BpmSource: SampleFactSource.Audio },
            "a long recording that isn't a loop length keeps its heard tempo but stays unclassified");
        var bare = SampleClassifier.Classify("/x/Riser 95.wav", SampleNameHints.Parse("/x/Riser 95.wav"), A(5.3, 0, tail: 0.5f));
        yield return (bare.Bpm == 0, "a bare number the audio doesn't back up is not a tempo");

        var f = new SampleFilter(SampleKind.Loop, 120, 128, new MusicalKey(0, KeyMode.Major));
        yield return (f.Matches(loop with { Key = new MusicalKey(9, KeyMode.Minor) }) && !f.Matches(loop with { Bpm = 130 })
                      && !f.Matches(hit) && !f.Matches(null) && SampleFilter.Empty.Matches(null) && f.BpmCaption == "120–128",
            "the filter: kind, inclusive BPM range, key with its relative; unanalysed files only pass the empty filter");
        yield return (loop.Tag == "124 · Am" && hit.Tag == "F" && loop.Describe().StartsWith("124\u2009BPM · A minor · loop", StringComparison.Ordinal),
            "rows show \"124 · Am\"; a hit shows its note");
    }

    // ---- engine analysis + index ----------------------------------------------------------------

    private static IEnumerable<(bool, string)> EngineAndIndex()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nota-smart-samples-" + Guid.NewGuid().ToString("N")[..8]);
        var lib = Path.Combine(dir, "Samples");
        Directory.CreateDirectory(Path.Combine(lib, "Pack", "Loops"));
        Directory.CreateDirectory(Path.Combine(lib, "Pack", "Hits"));
        try
        {
            string cLoop = Path.Combine(lib, "Pack", "Loops", "keys_a.wav");
            string amLoop = Path.Combine(lib, "Pack", "Loops", "keys_b.wav");
            string hats = Path.Combine(lib, "Pack", "Loops", "tops.wav");
            string kick = Path.Combine(lib, "Pack", "Hits", "low_1.wav");
            string kick2 = Path.Combine(lib, "Pack", "Hits", "low_2.wav");
            string pluck = Path.Combine(lib, "Pack", "Hits", "pluck.wav");
            Write(cLoop, ChordLoop(120, new[] { new[] { 0, 4, 7 }, new[] { 5, 9, 0 }, new[] { 7, 11, 2 }, new[] { 0, 4, 7 } }, 48));
            Write(amLoop, ChordLoop(124, new[] { new[] { 9, 0, 4 }, new[] { 2, 5, 9 }, new[] { 4, 8, 11 }, new[] { 9, 0, 4 } }, 45));
            Write(hats, HatLoop(128, 4));
            Write(kick, Kick(150, 48, 0.45));
            Write(kick2, Kick(140, 45, 0.5));
            Write(pluck, Pluck(54, 0.9));   // F#3

            var analyzer = new SampleAnalyzer();
            var ac = analyzer.Analyze(cLoop, 60);
            yield return (ac is not null && Math.Abs(ac.DurationSec - 8.0) < 0.01 && ac.KeyTonic == 0 && ac.KeyMode == 0,
                $"engine: a I–IV–V–I loop is heard in C major (tonic {ac?.KeyTonic}, mode {ac?.KeyMode}, r {ac?.KeyConfidence:0.00})");
            var aa = analyzer.Analyze(amLoop, 60);
            yield return (aa is not null && aa.KeyTonic == 9 && aa.KeyMode == 1,
                $"engine: an i–iv–V–i loop is heard in A minor (tonic {aa?.KeyTonic}, mode {aa?.KeyMode}, r {aa?.KeyConfidence:0.00})");
            var ah = analyzer.Analyze(hats, 60);
            yield return (ah is not null && ah.KeyTonic < 0 && ah.Bpm > 0,
                $"engine: a hi-hat loop has a pulse ({ah?.Bpm:0.0}) and no key (tonic {ah?.KeyTonic})");
            var ak = analyzer.Analyze(kick, 60);
            yield return (ak is not null && ak.TailRatio < 0.12 && ak.Timbre.Length == SampleAnalysis.TimbreDims,
                $"engine: a kick rings out (tail {ak?.TailRatio:0.000}) and has a timbre fingerprint");
            var ap = analyzer.Analyze(pluck, 60);
            yield return (ap is not null && ap.KeyTonic == 6,
                $"engine: a plucked F#3 is heard as F# (tonic {ap?.KeyTonic}, mode {ap?.KeyMode})");
            var head = analyzer.Analyze(cLoop, 2);
            yield return (head is not null && Math.Abs(head.DurationSec - 8.0) < 0.01 && head.Timbre[15] < 1.5,
                "engine: analysing only the head still reports the whole file's length");

            // The index, end to end.
            string file = Path.Combine(dir, "index.bin");
            var index = new SampleLibraryIndex(analyzer, file);
            var done = new ManualResetEventSlim();
            index.Changed += () => { if (index.Progress.Total == 0 && index.Get(pluck) is not null) done.Set(); };
            index.Watch(lib);
            bool finished = done.Wait(TimeSpan.FromSeconds(60));
            yield return (finished && index.Progress == (0, 0), "index: a scan of the library finishes");

            var ic = index.Get(cLoop);
            yield return (ic is { Kind: SampleKind.Loop, Bpm: 120, KindSource: SampleFactSource.Folder } && ic.Key == new MusicalKey(0, KeyMode.Major),
                $"index: the C loop — a loop (its folder), 120 BPM (its length), C major [{ic?.Tag}]");
            var ia = index.Get(amLoop);
            yield return (ia is { Bpm: 124 } && ia.Key == new MusicalKey(9, KeyMode.Minor), $"index: the A minor loop at 124 [{ia?.Tag}]");
            var ih = index.Get(hats);
            yield return (ih is { Kind: SampleKind.Loop, Bpm: 128, Key: null }, $"index: the hats at 128, no key [{ih?.Tag}]");
            var ik = index.Get(kick);
            yield return (ik is { Kind: SampleKind.OneShot, Bpm: 0 }, $"index: the kick is a one-shot [{ik?.Describe()}]");
            var ip = index.Get(pluck);
            yield return (ip is { Kind: SampleKind.OneShot } && ip.Key == new MusicalKey(6, KeyMode.Note), $"index: the pluck is an F# one-shot [{ip?.Tag}]");

            var loops = index.Search(new SampleFilter(SampleKind.Loop, 120, 125), null, 100).Select(s => s.Path).ToList();
            yield return (loops.Count == 2 && loops.Contains(cLoop) && loops.Contains(amLoop), "index: \"loops · 120–125\" finds the two chord loops");
            var inC = index.Search(new SampleFilter(Key: new MusicalKey(0, KeyMode.Major)), null, 100).Select(s => s.Path).ToList();
            yield return (inC.Count == 2 && inC.Contains(amLoop), "index: \"C major\" also finds its relative, the A minor loop");
            var sim = index.Similar(kick, 3);
            yield return (sim.Count >= 1 && sim[0].Info.Path == kick2, $"index: the sound most like a kick is the other kick ({Path.GetFileName(sim.FirstOrDefault().Info?.Path ?? "-")})");
            yield return (sim.All(x => x.Info.Kind != SampleKind.Loop), "index: …and no loop is \"like\" a hit");

            index.Dispose();
            var reopened = new SampleLibraryIndex(new NoAnalyzer(), file);
            reopened.Watch(lib);
            var again = reopened.Get(amLoop);
            reopened.Dispose();
            yield return (again is { Bpm: 124 }, "index: results persist — a new session knows the library without analysing it again");

            File.AppendAllText(hats, "x");   // changed on disk → stale
            var re = new SampleLibraryIndex(analyzer, file);
            var fresh = re.GetOrAnalyze(hats);
            re.Dispose();
            yield return (fresh is { Bpm: 128 }, "index: a changed file is analysed again");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class NoAnalyzer : ISampleAnalyzer
    {
        public SampleAnalysis? Analyze(string path, double maxSeconds) => null;
    }

    // ---- generated audio -------------------------------------------------------------------------

    private static double Hz(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    // Four bars of 4/4: one chord per bar (pitch classes, voiced around C4, the root also in
    // the bass), decaying saw-ish stabs on every beat, a click on each beat for the pulse.
    private static float[] ChordLoop(double bpm, int[][] bars, int bassBase)
    {
        double beat = 60.0 / bpm;
        int frames = (int)Math.Round(16 * beat * Sr);
        var x = new float[frames];
        for (int b = 0; b < 16; b++)
        {
            var chord = bars[b / 4];
            int start = (int)Math.Round(b * beat * Sr);
            int len = (int)(beat * Sr);
            foreach (var pc in chord)
                AddTone(x, start, len, Hz(60 + pc), 0.12, decay: 2.5, partials: 5);
            AddTone(x, start, len, Hz(bassBase + (chord[0] + 12 - bassBase % 12) % 12), 0.18, decay: 2.0, partials: 3);
            for (int i = 0; i < 200 && start + i < frames; i++) x[start + i] += (float)(0.5 * Math.Exp(-i / 30.0) * (i % 2 == 0 ? 1 : -1));
        }
        return x;
    }

    private static float[] HatLoop(double bpm, int bars)
    {
        var rng = new Random(7);
        double eighth = 30.0 / bpm;
        int frames = (int)Math.Round(bars * 8 * eighth * Sr);
        var x = new float[frames];
        for (int k = 0; k < bars * 8; k++)
        {
            int start = (int)Math.Round(k * eighth * Sr);
            double amp = k % 2 == 0 ? 0.6 : 0.3;
            for (int i = 0; i < Sr / 20 && start + i < frames; i++)
                x[start + i] += (float)(amp * (rng.NextDouble() * 2 - 1) * Math.Exp(-i / (Sr * 0.012)));
        }
        return x;
    }

    private static float[] Kick(double f0, double f1, double seconds)
    {
        int frames = (int)(seconds * Sr);
        var x = new float[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / Sr;
            double f = f1 + (f0 - f1) * Math.Exp(-t * 30);
            phase += 2 * Math.PI * f / Sr;
            x[i] = (float)(0.9 * Math.Sin(phase) * Math.Exp(-t * 9));
        }
        return x;
    }

    private static float[] Pluck(int midi, double seconds)
    {
        var x = new float[(int)(seconds * Sr)];
        AddTone(x, 0, x.Length, Hz(midi), 0.5, decay: 5, partials: 6);
        return x;
    }

    private static void AddTone(float[] x, int start, int len, double hz, double amp, double decay, int partials)
    {
        for (int i = 0; i < len && start + i < x.Length; i++)
        {
            double t = (double)i / Sr, s = 0;
            for (int p = 1; p <= partials; p++) s += Math.Sin(2 * Math.PI * hz * p * t) / p;
            double attack = Math.Min(1.0, i / (0.004 * Sr));
            x[start + i] += (float)(amp * s * attack * Math.Exp(-t * decay));
        }
    }

    private static void Write(string path, float[] x)
    {
        float peak = x.Max(v => Math.Abs(v));
        float g = peak > 0.95f ? 0.95f / peak : 1f;
        using var w = new BinaryWriter(File.Create(path));
        int bytes = x.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Sr); w.Write(Sr * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (var v in x) w.Write((short)Math.Clamp(v * g * short.MaxValue, short.MinValue, short.MaxValue));
    }
}
