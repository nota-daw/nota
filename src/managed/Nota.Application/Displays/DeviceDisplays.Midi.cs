// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in MIDI effects' LED pictures (specs/113): notes rather than sound — the
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
                int root = c.P.Step("Root", 12);
                if (c.P.Raw("Root") > 1.0001 || c.P.Raw("Root") == 0) root = Math.Clamp((int)Math.Round(c.P.Raw("Root")), 0, 11);
                var allowed = new HashSet<int>();
                for (int k = 0; k < 12; k++)
                    if (!c.P.Has($"Mask {k}") ? new[] { 0, 2, 4, 5, 7, 9, 11 }.Contains(k) : c.P.On($"Mask {k}"))
                        for (int o = 3; o <= 6; o++) allowed.Add(o * 12 + root + k);
                var (lastIn, lastOut) = c.MidiLast();
                var lit = new HashSet<int>();
                if (lastOut >= 0) lit.Add(lastOut);
                var roots = new HashSet<int>(Enumerable.Range(3, 4).Select(o => o * 12 + root));
                Widgets.Keyboard(f, 48, 2, lit, new HashSet<int>(allowed.Concat(roots)), 0, 127, LedFrame.Top + 12);
                string line = lastIn >= 0 ? $"{Widgets.NoteName(lastIn)} > {Widgets.NoteName(lastOut)}" : Widgets.NoteName(root + 60).TrimEnd('3', '2', '1', '0', '-', '4', '5', '6', '7', '8', '9');
                f.Text(line, 0, LedFrame.Top + 2, Widgets.Hot);
            }, c => "ROOT " + new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[Math.Clamp((int)Math.Round(c.P.Raw("Root")), 0, 11)]),
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
                // In (x) → out (y): the range lo..hi, the drive bending it, a fixed velocity flat.
                double lo = c.P.N("Out Low"), hi = c.P.N("Out High", 1), drive = c.P.Bi("Drive"), rnd = c.P.N("Random");
                bool fixedOn = c.P.N("Fixed") > 0.01 && c.P.Raw("Fixed") > 0;
                int x0 = 20, x1 = 107, top = LedFrame.Top, bottom = LedFrame.Bottom;
                f.Box(x0 - 1, top - 1, x1 - x0 + 3, bottom - top + 2, Widgets.Grid);
                f.Line(x0, bottom, x1, top, Widgets.Grid, dotted: true);
                double exp = Math.Pow(4, -drive);
                f.Curve(x =>
                {
                    double i = (x - x0) / (double)(x1 - x0);
                    double o = fixedOn ? c.P.N("Fixed") : lo + (hi - lo) * Math.Pow(i, exp);
                    return bottom - o * (bottom - top);
                }, x0, x1, Widgets.Hot);
                if (rnd > 0.01)
                    for (int x = x0; x <= x1; x += 3)
                    {
                        double i = (x - x0) / (double)(x1 - x0), o = lo + (hi - lo) * Math.Pow(i, exp);
                        f.VLine(x, bottom - Math.Min(1, o + rnd * 0.3) * (bottom - top), bottom - Math.Max(0, o - rnd * 0.3) * (bottom - top), Widgets.Live);
                    }
                int n = c.Scope(out var s, 64);
                if (n >= 2) f.Rect(x0 + s[n - 2] / 127.0 * (x1 - x0) - 1, bottom - s[n - 1] / 127.0 * (bottom - top) - 1, 3, 3, Widgets.Hot);
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
}
