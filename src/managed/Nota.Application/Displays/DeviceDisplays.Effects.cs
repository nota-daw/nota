// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in audio effects' LED pictures (specs/113), one per controller page
// (DevicePages.json): EQ curves, dynamics transfer curves with the live level and gain
// reduction, waveshapers, Reverb's tail with your audio's measured tail (its scope telemetry,
// Reverb.h S_*), delay taps, modulation sweeps, a phaser's notches, a stereo field. Effects
// without telemetry of their own move with the track's output level.

namespace Nota.Application.Displays;

public static partial class DeviceDisplays
{
    // Reverb scope telemetry (Reverb.h S_*; the level history after kScope).
    private const int RvRt60 = 6, RvPreMs = 7, RvFrozen = 9, RvScope = 11, RvPoints = 1536;
    private static readonly double[] RvErGain = { 1.0, 0.78, 0.86, 0.62, 0.66, 0.5, 0.42, 0.34 };

    private static Widgets.FilterType EqType(int step) => step switch
    {
        0 => Widgets.FilterType.HighPass,
        1 => Widgets.FilterType.LowShelf,
        3 => Widgets.FilterType.Notch,
        4 => Widgets.FilterType.HighShelf,
        5 => Widgets.FilterType.LowPass,
        _ => Widgets.FilterType.Peak,
    };

    /// <summary>EQ-8 / Dynamic EQ-8: all eight bands summed ("n On", "n Type", "n Freq", "n Gain", "n Q"),
    /// the band whose knob turns brightest.</summary>
    private static void Eq8(DisplayContext c, LedFrame f, bool dynamic)
    {
        var bands = new List<Widgets.EqBand>(8);
        int hi = -1;
        for (int b = 1; b <= 8; b++)
        {
            string p = b.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ";
            bool on = !c.P.Has(p + "On") || c.P.On(p + "On");
            var type = c.P.Has(p + "Type") ? EqType(c.P.Step(p + "Type", 6)) : Widgets.FilterType.Peak;
            double q = c.P.Has(p + "Q") ? Math.Max(0.1, c.P.Raw(p + "Q")) : 0.7;
            bands.Add(new Widgets.EqBand(type, c.P.Hz(p + "Freq"), c.P.Db(p + "Gain"), q, on, type is Widgets.FilterType.HighPass or Widgets.FilterType.LowPass ? 2 : 2));
            if (c.TurningName is { } t && t.StartsWith(p, StringComparison.Ordinal)) hi = b - 1;
        }
        Widgets.EqCurve(f, bands, 18, hi);
        if (dynamic)
        {
            // Each dynamic band's range as a dotted tick from its gain, moving with the gain reduction.
            double gr = Math.Clamp(c.GainReduction() / 12, 0, 1);
            for (int b = 1; b <= 8; b++)
            {
                string p = b.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ";
                if (!c.P.Has(p + "Rng") || bands[b - 1].On == false) continue;
                double x = Widgets.XOfHz(bands[b - 1].Hz), y0 = Widgets.YOfDb(bands[b - 1].GainDb, 18, -18);
                double y1 = Widgets.YOfDb(bands[b - 1].GainDb + c.P.Db(p + "Rng") * (0.3 + 0.7 * gr), 18, -18);
                f.VLine(x, Math.Min(y0, y1), Math.Max(y0, y1), Widgets.Dim, dotted: true);
            }
        }
    }

    private static Func<DisplayContext, string> EqReadout => c =>
    {
        if (c.TurningName is { } t && t.Length > 2 && char.IsDigit(t[0]))
        {
            string p = t[..2];
            return HzShort(c.P.Hz(p + "Freq")) + " " + DbShort(c.P.Db(p + "Gain"));
        }
        return "";
    };

    private static double RateHz(DisplayContext c, string name)
    {
        double raw = c.P.Raw(name);
        if (raw > 1.0001) return Math.Clamp(raw, 0.01, 40);
        return HzOfRate(c, name);
    }

    private static void AddEffects(Dictionary<string, PageDisplay[]> d)
    {
        d["builtin:fx:eq-8"] = new[]
        {
            Page("Bands 1–4", (c, f) => Eq8(c, f, false), EqReadout),
            Page("Bands 5–8", (c, f) => Eq8(c, f, false), EqReadout),
            Page("Q", (c, f) => Eq8(c, f, false), EqReadout),
        };

        d["builtin:fx:dynamic-eq-8"] = new[]
        {
            Page("Bands 1–2", (c, f) => Eq8(c, f, true), EqReadout),
            Page("Bands 3–4", (c, f) => Eq8(c, f, true), EqReadout),
            Page("Bands 5–6", (c, f) => Eq8(c, f, true), EqReadout),
            Page("Bands 7–8", (c, f) => Eq8(c, f, true), EqReadout),
        };

        d["builtin:fx:compressor"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double th = c.P.Db("Thresh", -20), ratio = Math.Max(1, c.P.Raw("Ratio", 4)), knee = c.P.Has("Knee") ? c.P.Db("Knee") : 0, mk = c.P.Db("Makeup");
                Widgets.Transfer(f, Widgets.CompCurve(th, ratio, Math.Max(0, knee), mk), th, c.TrackLevelDb() + c.GainReduction(), c.GainReduction());
            }, c => "GR " + c.GainReduction().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)),
            Page("Sidechain", (c, f) =>
            {
                var bands = new[]
                {
                    new Widgets.EqBand(Widgets.FilterType.HighPass, c.P.Hz("SC HP", 20), 0, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.LowPass, c.P.Hz("SC LP", 20000), 0, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.Peak, Math.Sqrt(c.P.Hz("SC HP", 20) * c.P.Hz("SC LP", 20000)), c.P.Db("SC Gain"), Math.Max(0.3, c.P.Raw("SC Q", 1))),
                };
                Widgets.EqCurve(f, bands, 24);
            }),
        };

        d["builtin:fx:reverb"] = new[]
        {
            Page("Main", (c, f) =>
            {
                int n = c.Scope(out var s, RvScope + 2 * RvPoints + 2048);
                double rt = n > RvRt60 && s[RvRt60] > 0 ? s[RvRt60] : 0.2 * Math.Pow(60, c.P.N("Decay"));
                double pre = n > RvPreMs ? s[RvPreMs] / 1000.0 : c.P.N("Pre-Delay") * 0.2;
                bool frozen = (n > RvFrozen && s[RvFrozen] > 0.5) || c.P.On("Freeze");
                double er = c.P.N("Early Refl", 0.5), size = c.P.N("Size", 0.5);
                var erLv = RvErGain.Select(g => g * er * (0.6 + 0.4 * size)).ToArray();
                var input = n >= RvScope + 2 * RvPoints ? new ReadOnlySpan<float>(s, RvScope, RvPoints) : default;
                var tail = n >= RvScope + 2 * RvPoints ? new ReadOnlySpan<float>(s, RvScope + RvPoints, RvPoints) : default;
                Widgets.Tail(f, rt, pre, rt * (1 - 0.75 * c.P.N("HF Damp")), erLv, frozen, input, tail);
            }, c =>
            {
                int n = c.Scope(out var s);
                double rt = n > RvRt60 && s[RvRt60] > 0 ? s[RvRt60] : 0.2 * Math.Pow(60, c.P.N("Decay"));
                return (n > RvFrozen && s[RvFrozen] > 0.5) || c.P.On("Freeze") ? "FREEZE" : "RT " + SecShort(rt);
            }),
            Page("More", (c, f) =>
            {
                // The early reflections as taps (left), the tail's modulation (right).
                double er = c.P.N("Early Refl", 0.5), diff = c.P.N("Diffusion", 0.5);
                for (int i = 0; i < RvErGain.Length; i++)
                {
                    double x = 2 + i * 7 + diff * i * 0.8;
                    f.VLine(x, LedFrame.Bottom - RvErGain[i] * er * 36, LedFrame.Bottom, Widgets.Mark);
                }
                f.VLine(66, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Lfo(f, Widgets.Wave.Sine, RateHz(c, "Mod Rate"), Math.Max(0.1, c.P.N("Mod Depth")), c.Time, Widgets.Hot, 70, 127);
            }),
        };

        d["builtin:fx:chamber"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double rt = c.P.Sec("Algo Decay", 2);
                if (rt < 0.05) rt = 0.2 + c.P.N("Algo Decay") * 10;
                Widgets.Tail(f, rt, c.P.Sec("Algo Predelay", 0.02), rt * (1 - 0.75 * c.P.N("Algo Damping")),
                    RvErGain.Select(g => g * c.P.N("Algo Size", 0.5)).ToArray());
                double shimmer = c.P.N("Shimmer Amount");
                if (shimmer > 0.02)
                    for (int k = 0; k < 10; k++) f.Dot(Widgets.Hash(k + Math.Floor(c.Time * 6)) * 127, LedFrame.Top + Widgets.Hash(k * 3 + Math.Floor(c.Time * 6)) * 20, (int)Math.Round(4 + shimmer * 11));
            }, c => "BLEND " + PctShort(c.P.N("Blend"))),
            Page("More", (c, f) =>
            {
                var bands = new[]
                {
                    new Widgets.EqBand(Widgets.FilterType.HighPass, c.P.Hz("EQ Low Cut", 20), 0, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.LowPass, c.P.Hz("EQ High Cut", 20000), 0, 0.7),
                };
                Widgets.EqCurve(f, bands, 24);
                double duck = c.P.N("Duck Amount");
                if (duck > 0.01) Widgets.Meter(f, 124, LedFrame.Top, 3, LedFrame.Bottom - LedFrame.Top + 1, duck * c.TrackLevel(), Widgets.Mark, fromTop: true);
            }),
        };

        d["builtin:fx:delay"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double l = c.P.Sec("Time L", 0.25), r = c.P.Sec("Time R", 0.375);
                Widgets.Taps(f, l, r, c.P.N("Feedback"), c.Playing ? c.Time : -1, 1 - 0.5 * c.P.N("Low Cut") - 0.5 * (1 - c.P.N("High Cut", 1)));
            }, c => SecShort(c.P.Sec("Time L", 0.25)) + " " + SecShort(c.P.Sec("Time R", 0.375))),
            Page("More", (c, f) =>
            {
                Widgets.Sweep(f, RateHz(c, "Wow Rate"), Math.Max(0.05, c.P.N("Wow Depth")), 0.5, c.Time);
                Widgets.Meter(f, 0, LedFrame.Top, 3, LedFrame.Bottom - LedFrame.Top + 1, c.P.N("Spread"), Widgets.Mark);
            }),
        };

        d["builtin:fx:utility"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double width = c.P.Raw("Width", 1);
                double w01 = width > 2.0001 ? width / 100.0 : width;
                Widgets.Stereo(f, c.P.Bi("Balance"), w01, c.TrackLevel());
                double mono = c.P.Hz("Mono Freq", 20);
                f.HLine(0, Widgets.XOfHz(mono), LedFrame.Bottom, Widgets.Mark, dotted: true);
                f.Text("MONO", 0, LedFrame.Bottom - 6, Widgets.Dim);
                Widgets.Meter(f, 0, LedFrame.Bottom - 13, 128, 3, c.TrackLevel(), Widgets.Mid);
            }, c => DbShort(c.P.Db("Gain"))),
        };

        d["builtin:fx:valve"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double drive = 1 + c.P.N("Gain") * 12;
                Widgets.Shaper(f, x => Math.Tanh(x * drive) / Math.Tanh(drive), c.TrackLevel(), c.Time);
            }, c => "GAIN " + PctShort(c.P.N("Gain"))),
            Page("Cabinet", (c, f) =>
            {
                var bands = new[]
                {
                    new Widgets.EqBand(Widgets.FilterType.HighPass, c.P.Hz("Low Cut", 80), 0, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.LowPass, c.P.Hz("High Cut", 6000), 0, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.Peak, c.P.Hz("Mid Freq", 800), (c.P.N("Middle", 0.5) - 0.5) * 18, 0.8),
                    new Widgets.EqBand(Widgets.FilterType.HighShelf, 4000, -c.P.N("Mic Distance") * 12, 0.7),
                };
                Widgets.EqCurve(f, bands, 24);
            }),
        };

        d["builtin:fx:auto-filter"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double lfo = c.P.Bi("LFO Amt") * Math.Sin(2 * Math.PI * RateHz(c, "LFO Rate") * c.Time);
                double env = c.P.Bi("Env Amt") * c.TrackLevel();
                double fc = c.P.Hz("Freq") * Math.Pow(2, 3 * (lfo + env));
                double morph = c.P.N("Morph");
                var type = morph < 0.33 ? Widgets.FilterType.LowPass : morph < 0.66 ? Widgets.FilterType.BandPass : Widgets.FilterType.HighPass;
                Widgets.FilterResponse(f, Math.Clamp(fc, 20, 20000), c.P.N("Res"), type, 2, null, c.P.N("Drive"));
            }, c => HzShort(c.P.Hz("Freq"))),
            Page("Mod", (c, f) =>
            {
                Widgets.Adsr(f, c.P.Sec("Env Attack"), 0.001, 1, c.P.Sec("Env Release"), Widgets.Hot, Widgets.Fill, -1, 0, 60);
                Widgets.Meter(f, 0, LedFrame.Top, 2, LedFrame.Bottom - LedFrame.Top + 1, c.TrackLevel(), Widgets.Mark);
                f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Lfo(f, Widgets.Wave.Sine, RateHz(c, "LFO Rate"), Math.Max(0.15, Math.Abs(c.P.Bi("LFO Amt"))), c.Time + c.P.N("LFO Offset"), Widgets.Hot, 66, 127);
            }),
        };

        d["builtin:fx:vintage"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double drive = 1 + c.P.N("Drive") * 8, wow = c.P.N("Wow"), flutter = c.P.N("Flutter");
                Widgets.Shaper(f, x => Math.Tanh(x * drive) / Math.Tanh(drive), c.TrackLevel(), c.Time + wow * Math.Sin(c.Time * 3) * 4 + flutter * Math.Sin(c.Time * 37));
                double noise = c.P.N("Noise"), crackle = c.P.N("Crackle");
                for (int k = 0; k < (int)(noise * 24); k++) f.Dot(48 + Widgets.Hash(k + Math.Floor(c.Time * 20)) * 79, LedFrame.Top + Widgets.Hash(k * 7 + Math.Floor(c.Time * 20)) * 38, Widgets.Live);
                if (crackle > 0.02 && Widgets.Hash(Math.Floor(c.Time * 9)) < crackle) f.VLine(48 + Widgets.Hash(Math.Floor(c.Time * 9) + 3) * 79, LedFrame.Top, LedFrame.Bottom, Widgets.Mark);
            }),
            Page("More", (c, f) =>
            {
                Widgets.Sweep(f, RateHz(c, "Wow Rate"), Math.Max(0.1, c.P.N("Wow", 0.3)), 0.6, c.Time);
                Widgets.Lfo(f, Widgets.Wave.Sine, RateHz(c, "Flutter Rate"), Math.Max(0.05, c.P.N("Flutter", 0.1)) * 0.4, c.Time, Widgets.Dim, zero: false);
            }),
        };

        d["builtin:fx:orbit"] = new[]
        {
            Page("Main", (c, f) =>
            {
                int shape = c.P.Step("Shape", 4);
                var w = Widgets.WaveFromStep(shape, Widgets.Wave.Sine, Widgets.Wave.Triangle, Widgets.Wave.Square, Widgets.Wave.Saw);
                double rate = RateHz(c, "Rate"), amt = c.P.N("Amount", 0.5);
                Widgets.Lfo(f, w, rate, Math.Max(0.15, amt), c.Time, Widgets.Hot, 0, 127, LedFrame.Top, LedFrame.Bottom - 10);
                double pan = amt * Widgets.WaveAt(w, rate * c.Time);
                Widgets.Stereo(f, pan, 0.08, c.TrackLevel(), LedFrame.Bottom - 3);
            }, c => HzShort(RateHz(c, "Rate"))),
        };

        d["builtin:fx:auto-shift"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // A sung line (dim, wavering) and the corrected one (bright) pulled to the semitone
                // grid at the speed and by the amount set.
                double amount = c.P.N("Amount"), speed = c.P.N("Speed"), human = c.P.N("Human");
                for (int s = 0; s <= 6; s++) f.HLine(0, 127, LedFrame.Top + 2 + s * 6, Widgets.Grid, dotted: true);
                double prevC = double.NaN;
                for (int x = 0; x < 128; x++)
                {
                    double t = c.Time * 0.6 + x / 60.0;
                    double note = 3 + Math.Floor(Widgets.Hash(Math.Floor(t)) * 3) + 0.35 * Math.Sin(t * 9) + 0.2 * Math.Sin(t * 2.3);
                    double target = Math.Round(note);
                    double frac = t - Math.Floor(t);
                    double pull = amount * (1 - Math.Exp(-frac * (2 + (1 - speed) * 0 + speed * 30))) * (1 - human * 0.4);
                    double corr = note + (target - note) * pull;
                    f.Dot(x, LedFrame.Top + 2 + (6 - note) * 6, Widgets.Live);
                    double yc = LedFrame.Top + 2 + (6 - corr) * 6;
                    if (!double.IsNaN(prevC)) f.Line(x - 1, prevC, x, yc, Widgets.Hot); else f.Dot(x, yc, Widgets.Hot);
                    prevC = yc;
                }
            }, c => "AMT " + PctShort(c.P.N("Amount"))),
        };

        d["builtin:fx:beat-repeat"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // A bar of 16 sixteenths: the repeat starts at the offset, slices of the grid size
                // fill the gate, each a little quieter with the decay.
                int offset = (int)Math.Round(c.P.N("Offset") * 15), gate = 1 + (int)Math.Round(c.P.N("Gate", 0.5) * 15);
                double grid = 0.25 + c.P.N("Grid") * 1.75, decay = c.P.N("Decay"), chance = c.P.N("Chance", 1);
                int step = (int)Math.Floor(c.Beats * 4) & 15;
                for (int i = 0; i < 16; i++)
                {
                    bool rep = i >= offset && i < offset + gate;
                    double h = rep ? Math.Pow(1 - decay * 0.6, (i - offset) / Math.Max(0.25, grid)) : 0.25;
                    f.Rect(i * 8, LedFrame.Bottom - h * 34, 7, h * 34 + 1, i == step && c.Playing ? Widgets.Hot : rep ? (chance > 0.5 ? Widgets.Mark : Widgets.Mid) : Widgets.Grid + 1);
                }
            }, c => "CHANCE " + PctShort(c.P.N("Chance", 1))),
            Page("Pitch · Filter", (c, f) =>
            {
                Widgets.FilterResponse(f, c.P.Hz("Filter Freq"), 0.2 + (1 - c.P.N("Filter Width")) * 0.6, Widgets.FilterType.BandPass);
                double pitch = c.P.Bi("Pitch"), pd = c.P.N("Pitch Decay");
                f.Line(0, LedFrame.Top + 18, 60, LedFrame.Top + 18 - pitch * 8 - pd * 6, Widgets.Mark);
            }),
        };

        d["builtin:fx:crush"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double bits = c.P.Raw("Bits", 8);
                int levels = bits > 1.0001 ? (int)Math.Pow(2, Math.Clamp(bits, 1, 16)) : (int)Math.Pow(2, 1 + (1 - c.P.N("Bits")) * 0 + c.P.N("Bits") * 15);
                int hold = 1 + (int)Math.Round((1 - c.P.N("Rate", 1)) * 12);
                double drive = 1 + c.P.N("Drive") * 4;
                int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
                f.HLine(0, 127, mid, Widgets.Grid, dotted: true);
                f.Curve(x => mid - 17 * Math.Sin(2 * Math.PI * (x / 64.0 + c.Time * 0.05)), 0, 127, Widgets.Grid + 1);
                f.Curve(x =>
                {
                    int xs = x - x % hold;
                    double v = Math.Tanh(Math.Sin(2 * Math.PI * (xs / 64.0 + c.Time * 0.05)) * drive) / Math.Tanh(drive);
                    double q = Math.Round(v * levels / 2) / (levels / 2.0);
                    return mid - 17 * q;
                }, 0, 127, Widgets.Hot);
            }, c => c.P.Raw("Bits") > 1.0001 ? Math.Round(c.P.Raw("Bits")) + " BIT" : ""),
        };

        d["builtin:fx:ceiling"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double ceil = c.P.Db("Ceiling", -0.3), gain = c.P.Db("Gain");
                Widgets.Transfer(f, x => Math.Min(x + gain, ceil), ceil - gain, c.TrackLevelDb() - gain + c.GainReduction(), c.GainReduction());
            }, c => "GR " + c.GainReduction().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)),
        };

        d["builtin:fx:level"] = new[]
        {
            Page("Main", (c, f) =>
            {
                Widgets.History(f, c.LevelHistory());
                double target = c.P.Db("Target", -14), ceil = c.P.Db("Ceiling", -1);
                f.HLine(0, 127, Widgets.YOfDb(target, 0, -60), Widgets.Hot);
                f.HLine(0, 127, Widgets.YOfDb(ceil, 0, -60), Widgets.Mark, dotted: true);
            }, c => DbShort(c.P.Db("Target", -14))),
        };

        d["builtin:fx:strata"] = new[]
        {
            Page("Main", (c, f) =>
            {
                int n = c.Scope(out var s, 16);
                double progress = n > 5 ? s[5] : (c.Time * 0.25) % 1;
                Widgets.History(f, c.LevelHistory(), Widgets.Live, LedFrame.Top, LedFrame.Bottom - 6);
                f.Rect(0, LedFrame.Bottom - 3, 128, 3, Widgets.Grid);
                f.Rect(0, LedFrame.Bottom - 3, Math.Round(progress * 128), 3, Widgets.Hot);
                Widgets.Meter(f, 0, LedFrame.Top, 3, 30, c.P.N("Feedback"), Widgets.Mark);
            }, c => "FB " + PctShort(c.P.N("Feedback"))),
        };

        d["builtin:fx:eq-3"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double lf = c.P.Hz("Low Freq", 250), hf = c.P.Hz("High Freq", 2500);
                var bands = new[]
                {
                    new Widgets.EqBand(Widgets.FilterType.LowShelf, lf, Math.Max(-30, c.P.Db("Low")), 0.7),
                    new Widgets.EqBand(Widgets.FilterType.Peak, Math.Sqrt(lf * hf), Math.Max(-30, c.P.Db("Mid")), 0.5),
                    new Widgets.EqBand(Widgets.FilterType.HighShelf, hf, Math.Max(-30, c.P.Db("High")), 0.7),
                };
                Widgets.EqCurve(f, bands, 24, c.IsTurning("Low") || c.IsTurning("Low Freq") ? 0 : c.IsTurning("Mid") ? 1 : c.IsTurning("High") || c.IsTurning("High Freq") ? 2 : -1);
            }),
        };

        d["builtin:fx:forge"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double amt = 1 + c.P.N("Amount") * 10, bias = c.P.Bi("Bias") * 0.5;
                double lfo = c.P.N("LFO Drive") * Math.Sin(2 * Math.PI * RateHz(c, "LFO Rate") * c.Time);
                amt *= 1 + lfo * 0.5;
                Widgets.Shaper(f, x => (Math.Tanh((x + bias) * amt) - Math.Tanh(bias * amt)) / Math.Tanh(amt), c.TrackLevel(), c.Time);
            }, c => "AMT " + PctShort(c.P.N("Amount"))),
            Page("Stages", Named(new[] { "S1 Drive", "S2 Drive", "S3 Drive", "S1 Out", "S2 Out", "S3 Out", "Env Tone" },
                new[] { "D1", "D2", "D3", "O1", "O2", "O3", "TONE" })),
        };

        d["builtin:fx:shutter"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double th = c.P.Db("Threshold", -40), floor = c.P.Db("Floor", -60);
                Widgets.Transfer(f, x => x >= th ? x : Math.Max(floor + x - th, x - 60), th, c.TrackLevelDb(), c.GainReduction());
            }, c => DbShort(c.P.Db("Threshold", -40))),
            Page("Pattern", (c, f) =>
            {
                double depth = c.P.N("Pattern Depth"), cx = c.P.N("Pattern Complexity"), swing = c.P.N("Pattern Swing");
                var v = new double[16];
                for (int i = 0; i < 16; i++) v[i] = 1 - depth * (Widgets.Hash(i * 13 + Math.Round(cx * 7)) < 0.3 + cx * 0.5 ? 1 : 0) * (i % 2 == 1 ? 1 - swing * 0.3 : 1);
                Widgets.Steps(f, v, c.Playing ? (int)Math.Floor(c.Beats * 4) & 15 : -1);
            }),
        };

        d["builtin:fx:prism"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double xl = c.P.Hz("Crossover Low", 200), xh = c.P.Hz("Crossover High", 2500);
                double[] g = { c.P.Db("Low Gain"), c.P.Db("Mid Gain"), c.P.Db("High Gain") };
                double gr = c.GainReduction();
                double[] edges = { 0, Widgets.XOfHz(xl), Widgets.XOfHz(xh), 127 };
                int zero = (int)Math.Round(Widgets.YOfDb(0, 18, -18));
                f.HLine(0, 127, zero, Widgets.Mid, dotted: true);
                for (int b = 0; b < 3; b++)
                {
                    double y = Widgets.YOfDb(Math.Clamp(g[b], -18, 18), 18, -18);
                    f.Rect(edges[b] + 1, Math.Min(y, zero), edges[b + 1] - edges[b] - 2, Math.Abs(y - zero) + 1, Widgets.Fill);
                    f.HLine(edges[b] + 1, edges[b + 1] - 1, y, Widgets.Hot);
                    double gy = Widgets.YOfDb(Math.Clamp(g[b] - gr * c.P.N("Amount", 0.5), -18, 18), 18, -18);
                    f.HLine(edges[b] + 2, edges[b + 1] - 2, gy, Widgets.Mark, dotted: true);
                }
                f.VLine(edges[1], LedFrame.Top, LedFrame.Bottom, Widgets.Mark);
                f.VLine(edges[2], LedFrame.Top, LedFrame.Bottom, Widgets.Mark);
                Widgets.FreqTicks(f);
            }, c => HzShort(c.P.Hz("Crossover Low", 200)) + " " + HzShort(c.P.Hz("Crossover High", 2500))),
            Page("Thresholds", Named(new[] { "Low Above Thresh", "Mid Above Thresh", "High Above Thresh", "Low Above Ratio", "Mid Above Ratio", "High Above Ratio", "Low Below Thresh", "Mid Below Thresh" },
                new[] { "LT", "MT", "HT", "LR", "MR", "HR", "LB", "MB" })),
        };

        d["builtin:fx:lens"] = new[]
        {
            Page("Display", (c, f) =>
            {
                var h = c.LevelHistory();
                Widgets.History(f, h, Widgets.Mark);
                double tilt = c.P.Bi("Spectrum Tilt");
                f.Line(0, 28 + tilt * 10, 127, 28 - tilt * 10, Widgets.Grid + 1, dotted: true);
                f.HLine(0, 127, Widgets.YOfDb(0, 0, -60), Widgets.Grid, dotted: true);
            }, c => DbShort(Math.Round(c.TrackLevelDb()))),
        };

        d["builtin:fx:flanger"] = new[]
        {
            Page("Main", (c, f) => Widgets.Sweep(f, RateHz(c, "Rate"), Math.Max(0.05, c.P.N("Depth")), c.P.N("Delay"), c.Time,
                Widgets.Wave.Triangle, c.P.N("Stereo") > 0.05 ? 2 : 1), c => HzShort(RateHz(c, "Rate"))),
        };

        d["builtin:fx:chorus"] = new[]
        {
            Page("Main", (c, f) => Widgets.Sweep(f, RateHz(c, "Rate"), Math.Max(0.05, c.P.N("Amount")), 0.5 + c.P.N("Offset") * 0.3, c.Time,
                Widgets.Wave.Sine, 2 + (int)Math.Round(c.P.N("Width") * 1)), c => HzShort(RateHz(c, "Rate"))),
        };

        d["builtin:fx:phaser"] = new[]
        {
            Page("Main", (c, f) =>
            {
                double st = c.P.Raw("Stages", 4);
                int stages = st > 1.0001 ? (int)Math.Round(st) : 2 + 2 * (int)Math.Round(c.P.N("Stages") * 5);
                Widgets.Notches(f, c.P.Hz("Center", 800), c.P.N("Depth", 0.5), stages, RateHz(c, "Rate"), c.P.N("Feedback"), c.Time);
            }, c => HzShort(RateHz(c, "Rate"))),
        };

        d["builtin:fx:vocoder"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // The band levels: the track's level shaped by the formant and tilt, rippling.
                int n = 16;
                // Silent, the bands rest at a low level so the formant and tilt still show.
                double formant = c.P.Bi("Formant"), tilt = c.P.Bi("Tilt"), lvl = 0.3 + 0.7 * c.TrackLevel();
                for (int b = 0; b < n; b++)
                {
                    double pos = b / (double)(n - 1);
                    double shape = 0.5 + 0.5 * Math.Sin(pos * 9 + formant * 3 + c.Time * 2) * (0.6 + 0.4 * Widgets.Hash(b + Math.Floor(c.Time * 8)));
                    double v = Math.Clamp(lvl * (0.4 + shape) * (1 + tilt * (pos - 0.5) * 1.6), 0, 1);
                    f.Rect(b * 8 + 1, LedFrame.Bottom - v * 36, 6, v * 36 + 1, Widgets.Mark);
                }
            }),
            Page("Bands", (c, f) =>
            {
                double bands = c.P.Raw("Bands", 16);
                int n = bands > 1.0001 ? (int)Math.Round(bands) : 4 + (int)Math.Round(c.P.N("Bands") * 36);
                double lo = c.P.Hz("Low", 100), hi = c.P.Hz("High", 8000);
                Widgets.FreqTicks(f);
                for (int b = 0; b < Math.Clamp(n, 1, 64); b++)
                {
                    double hz = lo * Math.Pow(hi / lo, b / Math.Max(1.0, n - 1));
                    f.VLine(Widgets.XOfHz(hz), LedFrame.Top + 4, LedFrame.Bottom - 3, Widgets.Mark);
                }
            }, c => Math.Round(c.P.Raw("Bands", 16)) + " BANDS"),
        };
    }
}
