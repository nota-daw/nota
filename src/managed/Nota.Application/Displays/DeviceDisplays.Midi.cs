// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in MIDI effects' LED pictures (specs/114): notes rather than sound — the
// arpeggiator's pattern stepping with the song, Chord's added notes on a keyboard over the last
// note played, Scale's allowed notes with the last note it remapped, Length's gate against the
// grid, Velocity's in → out curve, Random's spread of what it may change.

namespace Nota.Application.Displays;

public static partial class DeviceDisplays
{
    private static void AddMidi(Dictionary<string, PageDisplay[]> d)
    {
        d["builtin:midi:arp"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // Up through the octaves, four notes per octave, the gate as each note's length.
                int octaves = Math.Clamp((int)Math.Round(c.P.Num("Octaves", 1)), 1, 4);
                double gate = Math.Clamp(c.P.N("Gate", 0.5), 0.05, 1), swing = c.P.N("Swing");
                int steps = 4 * octaves, cols = Math.Max(steps, 8);
                double w = 128.0 / cols;
                int cur = c.Playing ? (int)Math.Floor(c.Beats * 4) % cols : -1;
                int rows = 4 * octaves, top = LedFrame.Top + 1, h = LedFrame.Bottom - top;
                for (int i = 0; i < cols; i++)
                {
                    int note = i % steps;
                    double x = i * w + (i % 2 == 1 ? swing * w * 0.4 : 0);
                    double y = LedFrame.Bottom - (note + 1) * h / (double)rows;
                    f.Rect(x, y, Math.Max(1, w * gate - 1), Math.Max(2, h / (double)rows - 1), i == cur ? Widgets.Hot : Widgets.Mark);
                }
                int tr = (int)Math.Round(c.P.Num("Transpose"));
                if (tr != 0) f.Text((tr > 0 ? "+" : "") + tr, 0, top, Widgets.Dim);
            }, c => "OCT " + Math.Clamp((int)Math.Round(c.P.Num("Octaves", 1)), 1, 4)),
        };

        d["builtin:midi:chord"] = new[]
        {
            Page("Main", (c, f) =>
            {
                var (lastIn, _) = c.MidiLast();
                int root = lastIn >= 0 ? lastIn : 60;
                int low = (root / 12 - 1) * 12;
                var lit = new HashSet<int> { root };
                var names = new List<string>();
                for (int v = 1; v <= 6; v++)
                {
                    string n = $"Voice {v}";
                    if (!c.P.Has(n)) continue;
                    int semi = (int)Math.Round(c.P.Num(n));
                    if (semi != 0) lit.Add(root + semi);
                }
                Widgets.Keyboard(f, low, 3, lit, null, 0, 127, LedFrame.Top + 12);
                string chord = string.Join(" ", lit.OrderBy(n => n).Select(n => Widgets.NoteName(n)));
                f.Text(LedFrame.Fit(chord, 128), 0, LedFrame.Top + 2, Widgets.Hot);
                double strum = c.P.N("Strum");
                if (strum > 0.01) f.Line(100, LedFrame.Top + 9, 100 + strum * 27, LedFrame.Top + 9, Widgets.Mark);
            }, c => "STRUM " + PctShort(c.P.N("Strum"))),
        };

        d["builtin:midi:scale"] = new[]
        {
            Page("Main", (c, f) =>
            {
                var (root, mask) = ScaleInEffect(c);
                // The notes it lets through, inside its range (Range Low / High, MIDI notes).
                double rlo = c.P.Raw("Range Low", 0), rhi = c.P.Raw("Range High", 127);
                if (rhi <= 1.0001) { rlo = 0; rhi = 127; }
                var allowed = new HashSet<int>();
                for (int k = 0; k < 12; k++)
                    if ((mask >> k & 1) != 0)
                        for (int o = 3; o <= 6; o++)
                        {
                            int note = o * 12 + root + k;
                            if (note >= rlo && note <= rhi) allowed.Add(note);
                        }
                var (lastIn, lastOut) = c.MidiLast();
                var lit = new HashSet<int>();
                if (lastOut >= 0) lit.Add(lastOut);
                var roots = new HashSet<int>(Enumerable.Range(3, 4).Select(o => o * 12 + root));
                Widgets.Keyboard(f, 48, 2, lit, new HashSet<int>(allowed.Concat(roots)), 0, 127, LedFrame.Top + 12);
                string line = lastIn >= 0 ? $"{Widgets.NoteName(lastIn)} > {Widgets.NoteName(lastOut)}" : NoteNames[root];
                f.Text(line, 0, LedFrame.Top + 2, Widgets.Hot);
            }, c => "ROOT " + NoteNames[ScaleInEffect(c).Root]),
        };

        d["builtin:midi:length"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // Notes on a beat grid, each as long as the gate sets, varied by velocity / key / random.
                double gate = c.P.N("Gate", 0.5), pct = c.P.N("Percent", 0.5), rnd = c.P.N("Random"), vel = c.P.N("Vel to Len"), key = c.P.N("Key to Len");
                for (int b = 0; b < 8; b++) f.VLine(b * 16, LedFrame.Top, LedFrame.Bottom, Widgets.Grid, dotted: true);
                for (int i = 0; i < 8; i++)
                {
                    double len = Math.Clamp(gate * 0.5 + pct * 0.5 + (Widgets.Hash(i) - 0.5) * rnd + (i % 3 - 1) * vel * 0.2 + (i % 4 - 1.5) * key * 0.1, 0.05, 1.6);
                    int y = LedFrame.Top + 3 + (i * 5) % 32;
                    f.Rect(i * 16 + 1, y, len * 15, 3, Widgets.Hot);
                }
            }),
        };

        d["builtin:midi:velocity"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // In (x) → out (y) as the engine shapes it (MidiVelocity::transfer): Curve bends by
                // Drive, Compand pushes away from the middle, Fixed is flat; then the Out range.
                double lo = c.P.N("Out Low"), hi = c.P.N("Out High", 1);
                if (lo > hi) (lo, hi) = (hi, lo);
                int mode = c.P.Has("Mode") ? (int)Math.Clamp(Math.Round(c.P.Raw("Mode")), 0, 2) : 0;
                double drive = Math.Max(0.05, c.P.Raw("Drive", 1)), fixedV = c.P.N("Fixed");
                bool random = !c.P.Has("Random On") || c.P.On("Random On");
                double rnd = random ? c.P.N("Random") : 0;
                double Out(double n)
                {
                    double shaped = mode switch
                    {
                        2 => fixedV,
                        1 => Math.Clamp(0.5 + Math.Sign(2 * n - 1) * Math.Pow(Math.Abs(2 * n - 1), 1 / drive) * 0.5, 0, 1),
                        _ => Math.Pow(n, 1 / drive),
                    };
                    return lo + shaped * (hi - lo);
                }
                int x0 = 20, x1 = 107, top = LedFrame.Top, bottom = LedFrame.Bottom;
                f.Box(x0 - 1, top - 1, x1 - x0 + 3, bottom - top + 2, Widgets.Grid);
                f.Line(x0, bottom, x1, top, Widgets.Grid, dotted: true);
                if (rnd > 0.01)
                    for (int x = x0; x <= x1; x += 3)
                    {
                        double o = Out((x - x0) / (double)(x1 - x0));
                        f.VLine(x, bottom - Math.Min(1, o + rnd * 0.3) * (bottom - top), bottom - Math.Max(0, o - rnd * 0.3) * (bottom - top), Widgets.Live);
                    }
                f.Curve(x => bottom - Out((x - x0) / (double)(x1 - x0)) * (bottom - top), x0, x1, Widgets.Hot);
                f.Text(mode switch { 2 => "FIXED", 1 => "COMPAND", _ => "CURVE" }, 0, top, Widgets.Dim);
            }),
        };

        d["builtin:midi:random"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // Sixteen steps: the chance a note is touched (bright) and how far its pitch moves.
                double chance = c.P.N("Chance"), range = c.P.N("Note Range"), skip = c.P.N("Skip");
                int step = c.Playing ? (int)Math.Floor(c.Beats * 4) & 15 : -1;
                int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
                f.HLine(0, 127, mid, Widgets.Grid, dotted: true);
                for (int i = 0; i < 16; i++)
                {
                    double seed = Math.Floor(c.Beats / 4) * 31 + i;
                    bool hit = Widgets.Hash(seed) < chance, skipped = Widgets.Hash(seed + 7) < skip * 0.5;
                    double dy = (Widgets.Hash(seed + 3) - 0.5) * 2 * range * 16;
                    int lv = i == step ? Widgets.Hot : hit ? Widgets.Mark : Widgets.Grid + 2;
                    if (skipped) { f.HLine(i * 8 + 1, i * 8 + 6, mid, Widgets.Grid + 1); continue; }
                    f.Rect(i * 8 + 1, mid - (hit ? dy : 0) - 1, 6, 3, lv);
                }
            }, c => "CHANCE " + PctShort(c.P.N("Chance"))),
        };
    }
    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    // MidiScale.cpp's presets (bit k = k semitones above the root); 10 = Custom (the Mask params).
    private static readonly int[] ScaleMasks = { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };

    /// <summary>The root and 12-bit mask Nota Scale runs: the selected preset (or the Custom mask)
    /// on the Root knob; while Follow Key or Learn moves them, what its telemetry reports
    /// ([2] root, [3] mask) — the telemetry only updates while audio runs.</summary>
    private static (int Root, int Mask) ScaleInEffect(DisplayContext c)
    {
        if (c.P.On("Follow Key") || c.P.On("Learn"))
        {
            int n = c.Scope(out var s, 64);
            if (n > 3 && s[3] > 0) return (((int)Math.Round(s[2]) % 12 + 12) % 12, (int)Math.Round(s[3]) & 4095);
        }
        int root = Math.Clamp((int)Math.Round(c.P.Raw("Root")), 0, 11);
        int scale = (int)Math.Round(c.P.Raw("Scale", 1));
        if (scale >= 0 && scale < ScaleMasks.Length) return (root, ScaleMasks[scale]);
        int mask = 0;
        for (int k = 0; k < 12; k++) if (c.P.On($"Mask {k}")) mask |= 1 << k;
        return (root, mask == 0 ? ScaleMasks[0] : mask);
    }
}
