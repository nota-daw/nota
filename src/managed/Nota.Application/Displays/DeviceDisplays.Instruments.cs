// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in instruments' LED pictures (specs/114), one per controller page
// (DevicePages.json): a filter page shows the response over the instrument's live output, an
// envelope page its envelope with a playhead, an oscillator page the waveform it makes, a
// modulation page its LFOs, a mixer or level page bars. Parameter names are the engine's, as the
// catalog uses them; the smoke test checks they resolve.

namespace Nota.Application.Displays;

public static partial class DeviceDisplays
{
    private static readonly Widgets.Wave[] SawSqTriSin = { Widgets.Wave.Saw, Widgets.Wave.Square, Widgets.Wave.Triangle, Widgets.Wave.Sine };
    private static readonly Widgets.Wave[] SinTriSqSh = { Widgets.Wave.Sine, Widgets.Wave.Triangle, Widgets.Wave.Square, Widgets.Wave.Noise };

    // ---- shared page kinds -------------------------------------------------------------------

    /// <summary>A filter page: cutoff, resonance, the type from its text (LP / HP / BP / Notch),
    /// the slope (24 dB when the slope text says so), over the live spectrum.</summary>
    private static PageDraw Filter(string freq, string reso, string? type = null, string? slope = null, string? drive = null) => (c, f) =>
    {
        var t = type is not null ? Widgets.FilterTypeFromName(c.P.Txt(type)) : Widgets.FilterType.LowPass;
        int poles = slope is not null && (c.P.Txt(slope).Contains("24") || c.P.Txt(slope).Contains("4")) ? 4 : 2;
        Widgets.FilterResponse(f, c.P.Hz(freq), c.P.N(reso), t, poles, c.Spectrum(), drive is null ? 0 : c.P.N(drive));
    };

    private static Func<DisplayContext, string> HzOf(string name) => c => HzShort(c.P.Hz(name));

    /// <summary>An ADSR page, with an optional second envelope dotted behind it.</summary>
    private static PageDraw Env(string a, string d, string s, string r, string[]? second = null) => (c, f) =>
    {
        if (second is { Length: 4 })
            Widgets.Adsr(f, c.P.Sec(second[0]), c.P.Sec(second[1]), c.P.N(second[2]), c.P.Sec(second[3]), Widgets.Dim, 0, dotted: true);
        Widgets.Adsr(f, c.P.Sec(a), c.P.Sec(d), c.P.N(s), c.P.Sec(r), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1);
    };

    /// <summary>The left half a filter, the right half an envelope (a synth's main page).</summary>
    private static PageDraw FilterAndEnv(string freq, string reso, string? type, string a, string d, string s, string r, string? slope = null) => (c, f) =>
    {
        var t = type is not null ? Widgets.FilterTypeFromName(c.P.Txt(type)) : Widgets.FilterType.LowPass;
        int poles = slope is not null && c.P.Txt(slope).Contains("24") ? 4 : 2;
        double fc = c.P.Hz(freq), q = 0.5 + c.P.N(reso) * 11;
        const double hi = 18, lo = -42;
        var bands = c.Spectrum();
        Widgets.SpectrumBars(f, bands, hz => Widgets.FilterMag(t, hz, fc, q, poles), Widgets.Live, 0, 74);
        f.Curve(x => Widgets.YOfDb(20 * Math.Log10(Math.Max(1e-5, Widgets.FilterMag(t, Widgets.HzOfX(x, 0, 74), fc, q, poles))), hi, lo), 0, 74, Widgets.Hot, Widgets.Fill);
        f.VLine(Widgets.XOfHz(fc, 0, 74), LedFrame.Top, LedFrame.Bottom, Widgets.Dim, dotted: true);
        f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
        Widgets.Adsr(f, c.P.Sec(a), c.P.Sec(d), c.P.N(s), c.P.Sec(r), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 81, 127);
    };

    /// <summary>Oscillators summed by level: each (wave step name, level name, pitch name or null).</summary>
    private static PageDraw Oscs(params (string Wave, string? Level, string? Detune)[] oscs) => (c, f) =>
    {
        var waves = oscs.Select(o => Widgets.WaveFromStep(c.P.Step(o.Wave, 4), SawSqTriSin)).ToArray();
        var levels = oscs.Select(o => o.Level is null ? 1.0 : c.P.N(o.Level)).ToArray();
        var ratios = oscs.Select((o, i) => i == 0 || o.Detune is null ? 1.0 : 1 + (c.P.N(o.Detune) - 0.5) * 0.06 + i * 0.0).ToArray();
        double sum = Math.Max(0.3, levels.Sum());
        Widgets.Waveform(f, ph => Widgets.WaveAt(waves[0], ph), 2, Widgets.Dim, c.Time);
        Widgets.Waveform(f, ph =>
        {
            double v = 0;
            for (int i = 0; i < waves.Length; i++) v += levels[i] * Widgets.WaveAt(waves[i], ph * ratios[i] + (ratios[i] - 1) * c.Time * 20);
            return v / sum;
        }, 2, Widgets.Hot, c.Time, zero: false);
    };

    /// <summary>One or two LFOs (rate name, depth name, optional shape name).</summary>
    private static PageDraw Lfos(string rate1, string depth1, string? shape1, string? rate2 = null, string? depth2 = null, string? shape2 = null) => (c, f) =>
    {
        if (rate2 is not null && depth2 is not null)
            Widgets.Lfo(f, shape2 is null ? Widgets.Wave.Triangle : Widgets.WaveFromStep(c.P.Step(shape2, 4), SinTriSqSh), HzOfRate(c, rate2), Math.Max(0.15, c.P.N(depth2)), c.Time, Widgets.Dim);
        Widgets.Lfo(f, shape1 is null ? Widgets.Wave.Sine : Widgets.WaveFromStep(c.P.Step(shape1, 4), SinTriSqSh), HzOfRate(c, rate1), Math.Max(0.15, c.P.N(depth1)), c.Time, Widgets.Hot, zero: rate2 is null);
    };

    /// <summary>An LFO rate in Hz: the text when it is in Hz, else 0.05–20 Hz on the knob.</summary>
    private static double HzOfRate(DisplayContext c, string name)
    {
        if (DisplayParams.ParseUnit(c.P.Txt(name), out double v, out string u) && u.StartsWith("hz", StringComparison.Ordinal)) return Math.Clamp(v, 0.01, 40);
        if (c.P.Raw(name) > 1.0001) return Math.Clamp(c.P.Raw(name), 0.01, 40);
        return 0.05 * Math.Pow(400, c.P.N(name));
    }

    private static PageDraw Named(string[] names, string[] labels, bool bipolar = false) => (c, f) => NamedBars(c, f, names, labels, bipolar);

    // ---- the instruments -----------------------------------------------------------------------

    private static void AddInstruments(Dictionary<string, PageDisplay[]> d)
    {
        d["builtin:inst:synth"] = new[]
        {
            Page("Main", FilterAndEnv("Cutoff", "Resonance", "Filter Type", "Attack", "Decay", "Sustain", "Release"), HzOf("Cutoff")),
            Page("Osc", (c, f) =>
            {
                var w = Widgets.WaveFromStep(c.P.Step("Wave", 4), SawSqTriSin);
                double pw = 0.5 + (c.P.N("Pulse Width") - 0.5) * 0.9;
                int voices = 1 + (int)Math.Round(c.P.N("Unison") * 6);
                double det = c.P.N("Detune");
                for (int v = voices - 1; v >= 1; v--)
                {
                    double r = 1 + (v - voices / 2.0) * det * 0.02;
                    Widgets.Waveform(f, ph => Widgets.WaveAt(w, ph * r + v * 0.13, pw), 2, Widgets.Dim - 1, c.Time, zero: false);
                }
                Widgets.Waveform(f, ph => Widgets.WaveAt(w == Widgets.Wave.Square ? Widgets.Wave.Pulse : w, ph, pw), 2, Widgets.Hot, c.Time);
            }),
        };

        d["builtin:inst:sampler"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Cutoff", "Filter Resonance", "Filter Type", "Env Attack", "Env Decay", "Env Sustain", "Env Release"), HzOf("Filter Cutoff")),
            Page("Sample", SampleRegion("Sample Start", "Sample End", "Loop Start", "Loop End")),
        };

        d["builtin:inst:mosaic"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Cutoff", "Filter Resonance", "Filter Type", "Env Attack", "Env Decay", "Env Sustain", "Env Release"), HzOf("Filter Cutoff")),
            Page("Voice", Named(new[] { "Pitch Detune", "Voice Glide", "Out Volume", "Out Pan", "Release Volume", "Release Length", "Voice Velocity", "Filter Keytrack" },
                new[] { "DET", "GLD", "VOL", "PAN", "RVOL", "RLEN", "VEL", "KEY" })),
        };

        d["builtin:inst:physical"] = new[]
        {
            Page("Main", (c, f) => Resonator(c, f, "Res1 Decay", "Res1 Bright", "Res1 Inharm", "Mallet Stiffness"), c => SecShort(c.P.Sec("Res1 Decay"))),
            Page("Noise", (c, f) =>
            {
                Widgets.Adsr(f, c.P.Sec("Noise Attack"), c.P.Sec("Noise Decay"), c.P.N("Noise Sustain"), c.P.Sec("Noise Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 60, 127);
                double fc = c.P.Hz("Noise Freq"), q = 0.5 + c.P.N("Noise Reso") * 10;
                f.Curve(x => Widgets.YOfDb(20 * Math.Log10(Math.Max(1e-5, Widgets.FilterMag(Widgets.FilterType.BandPass, Widgets.HzOfX(x, 0, 55), fc, q))), 18, -42), 0, 55, Widgets.Hot, Widgets.Fill);
                for (int x = 0; x <= 55; x += 2) f.Dot(x, LedFrame.Bottom - Widgets.Hash(x + Math.Floor(c.Time * 12)) * 20 * c.P.N("Noise Volume"), Widgets.Live);
                f.VLine(58, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
            }, HzOf("Noise Freq")),
            Page("Res 2", (c, f) => Resonator(c, f, "Res2 Decay", "Res2 Bright", "Res2 Inharm", null, "Res2 Ratio"), c => SecShort(c.P.Sec("Res2 Decay"))),
        };

        d["builtin:inst:aurora"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // Two wavetable positions as two frames drawn over each other, the envelope at the right.
                double p1 = c.P.N("Osc1 Position"), p2 = c.P.N("Osc2 Position");
                Widgets.Waveform(f, ph => TableFrame(ph, p2), 1, Widgets.Dim, 0, x1: 74, zero: false);
                Widgets.Waveform(f, ph => TableFrame(ph, p1), 1, Widgets.Hot, 0, x1: 74);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Adsr(f, c.P.Sec("Attack"), c.P.Sec("Decay"), c.P.N("Sustain"), c.P.Sec("Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 81, 127);
            }, c => "POS " + PctShort(c.P.N("Osc1 Position"))),
            Page("Osc", Named(new[] { "Osc1 Warp", "Osc2 Warp", "Osc1 Level", "Osc2 Level", "Sub Level", "Unison", "Unison Detune", "Gain" },
                new[] { "WRP1", "WRP2", "LVL1", "LVL2", "SUB", "UNI", "DET", "GAIN" })),
            Page("Mod", (c, f) =>
            {
                Widgets.Adsr(f, c.P.Sec("Env2 Attack"), c.P.Sec("Env2 Decay"), c.P.N("Env2 Sustain"), c.P.Sec("Env2 Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 0, 60);
                f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Lfo(f, Widgets.WaveFromStep(c.P.Step("LFO2 Shape", 4), SinTriSqSh), HzOfRate(c, "LFO2 Rate"), Math.Max(0.15, c.P.N("LFO2 Depth")), c.Time, Widgets.Dim, 66, 127);
                Widgets.Lfo(f, Widgets.WaveFromStep(c.P.Step("LFO1 Shape", 4), SinTriSqSh), HzOfRate(c, "LFO1 Rate"), Math.Max(0.15, c.P.N("LFO1 Depth")), c.Time, Widgets.Hot, 66, 127, zero: false);
            }),
            Page("Tone", (c, f) =>
            {
                Widgets.FilterResponse(f, c.P.Hz("Cutoff"), c.P.N("Resonance"), Widgets.FilterTypeFromName(c.P.Txt("Filter 1 Type")), 2, c.Spectrum(), c.P.N("Drive"));
            }, HzOf("Cutoff")),
        };

        d["builtin:inst:volt"] = new[]
        {
            Page("Main", FilterAndEnv("Filter 1 Freq", "Filter 1 Reso", "Filter 1 Type", "Attack", "Decay", "Sustain", "Release", "Filter 1 Slope"),
                c => Widgets.FilterTypeFromName(c.P.Txt("Filter 1 Type")) switch
                {
                    Widgets.FilterType.HighPass => "HP ", Widgets.FilterType.BandPass => "BP ", Widgets.FilterType.Notch => "NT ", _ => "LP ",
                } + HzShort(c.P.Hz("Filter 1 Freq"))),
            Page("Osc", Oscs(("Osc1 Wave", "Osc1 Level", null), ("Osc2 Wave", "Osc2 Level", "Osc2 Detune")),
                c => WaveShort(c.P.Step("Osc1 Wave", 4)) + "+" + WaveShort(c.P.Step("Osc2 Wave", 4))),
            Page("Filter", (c, f) =>
            {
                Widgets.Adsr(f, c.P.Sec("Filter Attack"), c.P.Sec("Filter Decay"), c.P.N("Filter Sustain"), c.P.Sec("Filter Release"),
                    Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 0, 74);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                double fc = c.P.Hz("Filter 2 Freq"), q = 0.5 + c.P.N("Filter 2 Reso") * 11;
                var t2 = Widgets.FilterTypeFromName(c.P.Txt("Filter 2 Type"));
                int poles2 = c.P.Txt("Filter 2 Slope").Contains("24") ? 4 : 2;
                f.Curve(x => Widgets.YOfDb(20 * Math.Log10(Math.Max(1e-5, Widgets.FilterMag(t2, Widgets.HzOfX(x, 81, 127), fc, q, poles2))), 18, -42), 81, 127, Widgets.Mark, Widgets.Fill);
            }),
            Page("LFO", Lfos("LFO1 Rate", "LFO1 Depth", "LFO1 Shape", "LFO2 Rate", "LFO2 Depth", "LFO2 Shape"), c => HzShort(HzOfRate(c, "LFO1 Rate"))),
        };

        d["builtin:inst:bass"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Freq", "Filter Reso", "Filter Type", "Attack", "Decay", "Sustain", "Release", "Filter Slope"), HzOf("Filter Freq")),
            Page("Tone", (c, f) =>
            {
                double pw = 0.5 + (c.P.N("Pulse Width") - 0.5) * 0.9, shape = c.P.N("Osc Shape"), sub = c.P.N("Sub Level"), lvl = c.P.N("Osc Level");
                Widgets.Waveform(f, ph => Widgets.WaveAt(Widgets.Wave.Sine, ph * 0.5), 2, Widgets.Dim, c.Time, amp: sub, x1: 74);
                Widgets.Waveform(f, ph =>
                {
                    double saw = Widgets.WaveAt(Widgets.Wave.Saw, ph), pulse = Widgets.WaveAt(Widgets.Wave.Pulse, ph, pw);
                    return (saw * (1 - shape) + pulse * shape) * Math.Max(0.2, lvl) + 0.5 * sub * Widgets.WaveAt(Widgets.Wave.Sine, ph * 0.5);
                }, 2, Widgets.Hot, c.Time, x1: 74, zero: false);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Adsr(f, c.P.Sec("Filter Attack"), c.P.Sec("Filter Decay"), c.P.N("Filter Sustain"), 0.2, Widgets.Mark, Widgets.Fill, -1, 81, 127);
            }),
        };

        d["builtin:inst:pendulum"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // Balls on strings swinging at the motion rate, spread by the spread rate.
                int n = Math.Clamp((int)Math.Round(c.P.Num("Balls Count", 4)), 1, 12);
                if (!c.P.Has("Balls Count") || c.P.Num("Balls Count") <= 1.0001) n = 1 + (int)Math.Round(c.P.N("Balls Count") * 11);
                double rate = 0.2 + c.P.N("Motion Rate") * 2, spread = c.P.N("Spread Rate");
                double w = 128.0 / n;
                for (int i = 0; i < n; i++)
                {
                    double ax = w * (i + 0.5), ang = Math.Sin(2 * Math.PI * c.Time * rate * (1 + spread * i * 0.08)) * 0.7;
                    double len = 30, bx = ax + Math.Sin(ang) * len * 0.6, by = LedFrame.Top + Math.Cos(ang) * len;
                    f.Line(ax, LedFrame.Top, bx, by, Widgets.Dim);
                    f.Rect(bx - 1, by - 1, 3, 3, Widgets.Hot);
                }
                f.HLine(0, 127, LedFrame.Top, Widgets.Mid);
            }),
            Page("Voice", (c, f) =>
            {
                Widgets.Waveform(f, ph => Widgets.WaveAt(Widgets.WaveFromStep(c.P.Step("Voice Wave", 4), Widgets.Wave.Sine, Widgets.Wave.Triangle, Widgets.Wave.Saw, Widgets.Wave.Square), ph), 2, Widgets.Hot, c.Time, x1: 60);
                f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Adsr(f, c.P.Sec("Voice Attack"), c.P.Sec("Voice Length"), 0, c.P.Sec("Voice Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 66, 127);
            }),
        };

        d["builtin:inst:operator"] = new[]
        {
            Page("Main", (c, f) =>
            {
                OperatorAlgo(f, OperatorAlgoNumber(c), c.P.N("Feedback"), c.P.N("FM Depth"));
                double fc = c.P.Hz("Filter Freq"), q = 0.5 + c.P.N("Filter Reso") * 11;
                f.Curve(x => Widgets.YOfDb(20 * Math.Log10(Math.Max(1e-5, Widgets.FilterMag(Widgets.FilterType.LowPass, Widgets.HzOfX(x, 81, 127), fc, q))), 18, -42), 81, 127, Widgets.Mark, Widgets.Fill);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
            }, c => "ALG " + OperatorAlgoNumber(c).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Page("Operators", (c, f) =>
            {
                var values = new double?[8];
                string[] names = { "A Level", "B Level", "C Level", "D Level", "A Coarse", "B Coarse", "C Coarse", "D Coarse" };
                int hi = -1;
                for (int i = 0; i < 8; i++) { values[i] = c.P.N(names[i]); if (c.IsTurning(names[i])) hi = i; }
                Widgets.Bars(f, values, hi, new[] { "A", "B", "C", "D", "XA", "XB", "XC", "XD" });
            }),
            Page("Env A · B", (c, f) => TwoEnvs(c, f, "A", "B")),
            Page("Env C · D", (c, f) => TwoEnvs(c, f, "C", "D")),
        };

        d["builtin:inst:grain"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // The sample as a band of grains: position, scan spray as spread, size as grain width, density as count.
                double pos = c.P.N("Position"), spray = c.P.N("Spray"), size = c.P.N("Grain Size"), dens = c.P.N("Density");
                for (int x = 0; x < 128; x++)
                {
                    double a = 0.25 + 0.75 * Math.Abs(Math.Sin(x * 0.11) * Math.Sin(x * 0.037 + 1));
                    f.VLine(x, 28 - a * 15, 28 + a * 15, Widgets.Grid + 1);
                }
                int grains = 2 + (int)Math.Round(dens * 14);
                double scan = c.P.N("Scan");
                for (int g = 0; g < grains; g++)
                {
                    double life = (c.Time * (0.6 + dens) + g / (double)grains) % 1;
                    double gx = (pos + (Widgets.Hash(g * 7 + Math.Floor(c.Time * (0.6 + dens) + g / (double)grains)) - 0.5) * spray + scan * life * 0.1) % 1;
                    double gw = 1 + size * 10, amp = Math.Sin(Math.PI * life);
                    f.Rect(gx * 127 - gw / 2, 28 - amp * 12, gw, 2, (int)Math.Round(6 + 9 * amp));
                }
                f.VLine(pos * 127, LedFrame.Top, LedFrame.Bottom, Widgets.Hot, dotted: true);
            }, c => "POS " + PctShort(c.P.N("Position"))),
            Page("Pitch · Env", Env("Attack", "Decay", "Sustain", "Release")),
        };

        d["builtin:inst:flux"] = new[]
        {
            Page("Main", (c, f) =>
            {
                Widgets.Xy(f, c.P.N("Vector X"), c.P.N("Vector Y"), 2, LedFrame.Top, 38);
                // The motion: the point's recent path around the vector.
                double r = c.P.N("Motion") * 14, rate = 0.1 + c.P.N("Motion Rate") * 2;
                for (int k = 0; k < 24; k++)
                {
                    double t = c.Time - k * 0.04;
                    double x = 3 + c.P.N("Vector X") * 35 + Math.Sin(2 * Math.PI * rate * t) * r, y = LedFrame.Top + 37 - c.P.N("Vector Y") * 35 + Math.Cos(2 * Math.PI * rate * t * 1.3) * r;
                    f.Dot(Math.Clamp(x, 3, 38), Math.Clamp(y, LedFrame.Top + 1, LedFrame.Bottom - 1), Math.Max(2, 12 - k / 2));
                }
                Widgets.HBar(f, "FILT", LedFrame.Top + 2, c.P.N("Filter"));
                Widgets.HBar(f, "ENV", LedFrame.Top + 11, c.P.N("Env"));
                Widgets.HBar(f, "SPACE", LedFrame.Top + 20, c.P.N("Space"));
                Widgets.HBar(f, "GAIN", LedFrame.Top + 29, c.P.N("Gain"));
            }),
            Page("More", KnobBars),
        };

        d["builtin:inst:rhythm"] = new[]
        {
            Page("Levels", Named(RhythmVoices("Level"), RhythmLabels)),
            Page("Tune", Named(RhythmVoices("Tune"), RhythmLabels, bipolar: true)),
            Page("Decay", (c, f) =>
            {
                // Each drum's decay as a falling line, side by side.
                string[] names = RhythmVoices("Decay");
                for (int i = 0; i < 8; i++)
                {
                    int x0 = i * 16, x1 = x0 + 14;
                    double dsec = 0.05 + c.P.N(names[i]) * 1.5;
                    Widgets.Decay(f, 0.002, dsec, c.IsTurning(names[i]) ? Widgets.Hot : Widgets.Mark, Widgets.Fill, 1.6, x0, x1, LedFrame.Top + 1, LedFrame.Bottom - 7);
                    f.Text(RhythmLabels[i], x0 + Math.Max(0, (15 - LedFrame.TextWidth(RhythmLabels[i])) / 2), LedFrame.Bottom - 4, c.IsTurning(names[i]) ? Widgets.Hot : Widgets.Dim);
                }
            }),
            Page("Perform", (c, f) =>
            {
                // Sixteen steps with the swing pushing the off-beats, accent on the beats.
                double swing = c.P.N("Perform Swing"), human = c.P.N("Perform Humanize"), accent = c.P.N("Perform Accent");
                int step = (int)Math.Floor(c.Beats * 4) & 15;
                for (int i = 0; i < 16; i++)
                {
                    double x = i * 8 + (i % 2 == 1 ? swing * 4 : 0) + (Widgets.Hash(i * 3.1) - 0.5) * human * 3;
                    double h = (i % 4 == 0 ? 0.6 + 0.4 * accent : 0.45) * 26;
                    f.Rect(x, LedFrame.Bottom - 8 - h, 5, h, i == step && c.Playing ? Widgets.Hot : Widgets.Mark - 2);
                }
                Widgets.HBar(f, "MASTER", LedFrame.Bottom - 5, c.P.N("Master Volume"));
            }),
        };

        d["builtin:inst:monolith"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Cutoff", "Filter Emphasis", null, "Filter-Env Attack", "Filter-Env Decay", "Filter-Env Sustain", "Filter-Env Decay"), HzOf("Filter Cutoff")),
            Page("Mixer", (c, f) =>
            {
                Widgets.Waveform(f, ph => (c.P.N("Mixer Osc1 Level") * Widgets.WaveAt(Widgets.WaveFromStep(c.P.Step("Osc1 Wave", 4), SawSqTriSin), ph)
                    + c.P.N("Mixer Osc2 Level") * Widgets.WaveAt(Widgets.WaveFromStep(c.P.Step("Osc2 Wave", 4), SawSqTriSin), ph * (1.0 + (c.P.N("Osc2 Tune") - 0.5) * 0.04))
                    + c.P.N("Mixer Osc3 Level") * Widgets.WaveAt(Widgets.WaveFromStep(c.P.Step("Osc3 Wave", 4), SawSqTriSin), ph * (1.0 + (c.P.N("Osc3 Tune") - 0.5) * 0.04))
                    + c.P.N("Mixer Noise Level") * (Widgets.Hash(Math.Floor(ph * 40)) * 2 - 1))
                    / Math.Max(0.4, c.P.N("Mixer Osc1 Level") + c.P.N("Mixer Osc2 Level") + c.P.N("Mixer Osc3 Level") + c.P.N("Mixer Noise Level")), 2, Widgets.Hot, c.Time, x1: 74);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Adsr(f, c.P.Sec("Amp-Env Attack"), c.P.Sec("Amp-Env Decay"), c.P.N("Amp-Env Sustain", 0.7), c.P.Sec("Amp-Env Decay"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 81, 127);
            }),
        };

        d["builtin:inst:pentad"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Cutoff", "Filter Resonance", null, "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release"), HzOf("Filter Cutoff")),
            Page("Filter", (c, f) =>
            {
                Widgets.Adsr(f, c.P.Sec("Filter-Env Attack"), c.P.Sec("Filter-Env Decay"), c.P.N("Filter-Env Sustain"), c.P.Sec("Filter-Env Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 0, 74);
                f.VLine(78, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                MiniBars(f, 81, new[] { c.P.N("Mixer Osc-A Level"), c.P.N("Mixer Osc-B Level"), c.P.N("Mixer Noise Level"), c.P.N("LFO Rate") }, new[] { "A", "B", "N", "L" });
            }),
        };

        d["builtin:inst:consort"] = new[]
        {
            Page("Main", FilterAndEnv("Filter Cutoff", "Filter Resonance", "Filter Mode", "Amp-Env Attack", "Amp-Env Decay", "Amp-Env Sustain", "Amp-Env Release"), HzOf("Filter Cutoff")),
            Page("Mixer", (c, f) =>
            {
                MiniBars(f, 0, new[] { c.P.N("Mixer Osc-1"), c.P.N("Mixer Osc-2"), c.P.N("Mixer Osc-3"), c.P.N("Mixer Osc-4") }, new[] { "1", "2", "3", "4" }, 60);
                f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                Widgets.Adsr(f, c.P.Sec("Filter-Env Attack"), c.P.Sec("Filter-Env Decay"), c.P.N("Filter-Env Sustain"), c.P.Sec("Filter-Env Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 66, 127);
            }),
            Page("Effects", (c, f) =>
            {
                Widgets.Lfo(f, Widgets.Wave.Triangle, HzOfRate(c, "LFO Rate"), Math.Max(0.15, c.P.N("LFO Cutoff")), c.Time, Widgets.Hot, 0, 60);
                f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
                MiniTaps(f, 66, 127, c.P.N("Delay Time"), c.P.N("Delay Feedback"), c.P.N("Delay Mix"));
            }),
        };

        // External MIDI (specs/115): no knobs to picture — its destination, as a cable to a port.
        d["builtin:inst:external-midi"] = new[]
        {
            Page("External", (c, f) =>
            {
                var dest = c.Engine.GetExternalMidi(c.Address.TrackId);
                string port = dest is null || dest.Value.OutputUid.Length == 0 ? "NO OUTPUT"
                    : dest.Value.OutputName.Length > 0 ? dest.Value.OutputName : dest.Value.OutputUid;
                string ch = dest is { Channel: 0 } ? "MPE" : $"CH {dest?.Channel ?? 1}";
                int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
                f.Box(4, mid - 8, 16, 16, Widgets.Hot);                 // the track
                f.Line(20, mid, 100, mid, Widgets.Dim);                  // the cable
                f.Box(100, mid - 8, 24, 16, c.Engine.ExternalMidiStatus(c.Address.TrackId) == 1 ? Widgets.Hot : Widgets.Dim);   // the port
                f.Text(LedFrame.Normalize(ch), 104, mid - 2, Widgets.Hot);
                f.Text(LedFrame.Normalize(port), 4, LedFrame.Bottom - 6, Widgets.Hot);
            }),
        };
        d["builtin:inst:keys"] = new[]
        {
            Page("Main", (c, f) =>
            {
                // A tine's ring: the hammer's hardness makes the attack, the resonator's decay the tail,
                // the tremolo rides on it.
                double dec = 0.3 + c.P.N("Resonator Decay") * 5, trem = c.P.N("Tremolo Depth"), rate = HzOfRate(c, "Tremolo Rate");
                double bright = c.P.N("Resonator Bright"), hard = c.P.N("Hammer Hardness");
                int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
                f.HLine(0, 127, mid, Widgets.Grid, dotted: true);
                f.Curve(x =>
                {
                    double t = x / 127.0 * 3;
                    double env = Math.Exp(-t * 2.3 / dec) * (1 - trem * 0.5 * (1 + Math.Sin(2 * Math.PI * rate * t)));
                    double tone = Math.Sin(x * 0.9) + bright * 0.4 * Math.Sin(x * 2.7) + hard * 0.3 * Math.Exp(-t * 20) * Math.Sin(x * 5.1);
                    return mid - 17 * env * tone / 1.5;
                }, 0, 127, Widgets.Hot);
            }, c => "TREM " + PctShort(c.P.N("Tremolo Depth"))),
            Page("Tone", (c, f) =>
            {
                var bands = new[]
                {
                    new Widgets.EqBand(Widgets.FilterType.LowShelf, 120, (c.P.N("Preamp Bass") - 0.5) * 24, 0.7),
                    new Widgets.EqBand(Widgets.FilterType.HighShelf, 3000, (c.P.N("Preamp Treble") - 0.5) * 24, 0.7),
                };
                Widgets.EqCurve(f, bands, 18);
            }),
        };

        d["builtin:inst:twin"] = new[]
        {
            Page("Main", (c, f) =>
            {
                Widgets.Xy(f, c.P.N("Pad X"), c.P.N("Pad Y"), 2, LedFrame.Top, 38);
                f.Rect(44, LedFrame.Bottom - c.P.N("Pad Z") * 37, 3, c.P.N("Pad Z") * 37 + 1, Widgets.Mark);
                Widgets.HBar(f, "MORPH", LedFrame.Top + 2, c.P.N("Lower · Morph"));
                MiniBars(f, 56, new[] { c.P.N("Lower · Macros Macro1"), c.P.N("Lower · Macros Macro2"), c.P.N("Lower · Macros Macro3"), c.P.N("Lower · Macros Macro4") },
                    new[] { "1", "2", "3", "4" }, 127, LedFrame.Top + 11);
            }),
            Page("Macros", KnobBars),
            Page("Lower A", FilterAndEnv("Lower A · Multimode Cutoff", "Lower A · Multimode Resonance", null,
                "Lower A · AmpEnv Attack", "Lower A · AmpEnv Decay", "Lower A · AmpEnv Sustain", "Lower A · AmpEnv Release"), HzOf("Lower A · Multimode Cutoff")),
        };
    }

    private static readonly string[] RhythmLabels = { "KICK", "SNR", "CLAP", "RIM", "CH", "OH", "TOM", "PERC" };

    private static string[] RhythmVoices(string what) => new[]
    {
        $"Kick {what}", $"Snare {what}", $"Clap {what}", $"Rim {what}", $"Closed Hat {what}", $"Open Hat {what}", $"Tom {what}", $"Perc {what}",
    };

    private static string WaveShort(int step) => step switch { 0 => "SAW", 1 => "SQR", 2 => "TRI", _ => "SIN" };

    /// <summary>One frame of a wavetable at position 0..1: a sine that grows harmonics and folds.</summary>
    private static double TableFrame(double ph, double pos)
    {
        double s = Math.Sin(2 * Math.PI * ph);
        double h = pos * 0.8 * Math.Sin(2 * Math.PI * ph * 3) + pos * pos * 0.5 * Math.Sin(2 * Math.PI * ph * 5);
        return Math.Tanh((s + h) * (1 + pos * 2)) / Math.Tanh(1 + pos * 2);
    }

    /// <summary>A physical resonator: its partials as lines (inharmonicity spreads them, brightness
    /// lifts the high ones), each decaying over <paramref name="decay"/>.</summary>
    private static void Resonator(DisplayContext c, LedFrame f, string decay, string bright, string inharm, string? stiffness, string? ratio = null)
    {
        double dec = c.P.Sec(decay), br = c.P.N(bright), inh = c.P.N(inharm);
        double baseHz = 110 * (ratio is null ? 1 : 0.5 + c.P.N(ratio) * 3);
        // The ring's decay across the top, dotted, so the length reads even with few partials.
        Widgets.Decay(f, 0.002, Math.Max(0.05, dec), Widgets.Dim, 0, Math.Max(0.5, dec * 1.3), 0, 127, LedFrame.Top, LedFrame.Top + 14);
        for (int k = 1; k <= 14; k++)
        {
            double hz = baseHz * k * Math.Sqrt(1 + inh * 0.08 * k * k);
            if (hz > 18000) break;
            double amp = Math.Pow(k, -1.6 + br * 1.3);
            double x = Widgets.XOfHz(hz);
            double life = Math.Exp(-k * 0.15 / Math.Max(0.05, dec));
            f.VLine(x, LedFrame.Bottom - amp * life * 36, LedFrame.Bottom, k == 1 ? Widgets.Hot : Widgets.Mark);
        }
        if (stiffness is not null)
        {
            double st = c.P.N(stiffness);
            f.Text("MALLET", 0, LedFrame.Top, Widgets.Dim);
            f.Rect(26, LedFrame.Top + 1, 1 + st * 30, 3, Widgets.Mark);
        }
        Widgets.FreqTicks(f);
    }

    /// <summary>A sample's region: a waveform with start / end brackets and the loop shaded.</summary>
    private static PageDraw SampleRegion(string start, string end, string loopStart, string loopEnd) => (c, f) =>
    {
        double s = c.P.N(start), e = c.P.N(end, 1), ls = c.P.N(loopStart), le = c.P.N(loopEnd, 1);
        int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
        for (int x = 0; x < 128; x++)
        {
            double t = x / 127.0;
            double a = (0.2 + 0.8 * Math.Exp(-t * 3)) * (0.5 + 0.5 * Math.Abs(Math.Sin(x * 0.7) * Math.Cos(x * 0.13)));
            bool inside = t >= s && t <= e;
            f.VLine(x, mid - a * 17, mid + a * 17, inside ? Widgets.Mark : Widgets.Grid);
            if (t >= ls && t <= le) f.Dot(x, LedFrame.Bottom, Widgets.Hot);
        }
        f.VLine(s * 127, LedFrame.Top, LedFrame.Bottom, Widgets.Hot);
        f.VLine(e * 127, LedFrame.Top, LedFrame.Bottom, Widgets.Hot);
        f.VLine(ls * 127, LedFrame.Top, LedFrame.Bottom, Widgets.Dim, dotted: true);
        f.VLine(le * 127, LedFrame.Top, LedFrame.Bottom, Widgets.Dim, dotted: true);
    };

    /// <summary>Operator's four operators: carriers on the bottom row, modulators above them,
    /// stacked by algorithm (a simplified map of its 11), the feedback loop on A.</summary>
    private static void OperatorAlgo(LedFrame f, int algo, double feedback, double fm)
    {
        // Each algorithm as rows of operators from top (modulators) to bottom (carriers).
        string[][] layouts =
        {
            new[] { "A", "B", "C", "D" }, new[] { "AB", "C", "D" }, new[] { "A", "BC", "D" }, new[] { "A", "B", "CD" },
            new[] { "AB", "CD" }, new[] { "A", "BCD" }, new[] { "AC", "BD" }, new[] { "ABC", "D" },
            new[] { "A", "B", "C", "D" }, new[] { "AB", "CD" }, new[] { "ABCD" },
        };
        var rows = layouts[Math.Clamp(algo - 1, 0, layouts.Length - 1)];
        int rh = 36 / rows.Length;
        var pos = new Dictionary<char, (int X, int Y)>();
        for (int r = 0; r < rows.Length; r++)
        {
            int y = LedFrame.Top + r * rh + 1;
            for (int k = 0; k < rows[r].Length; k++)
            {
                int x = 4 + k * 18 + (4 - rows[r].Length) * 9;
                bool carrier = r == rows.Length - 1;
                f.Box(x, y, 12, Math.Min(rh - 2, 8), carrier ? Widgets.Hot : Widgets.Mark);
                f.Text(rows[r][k].ToString(), x + 4, y + 1 + Math.Max(0, (Math.Min(rh - 2, 8) - 7) / 2), carrier ? Widgets.Hot : Widgets.Mark);
                pos[rows[r][k]] = (x + 6, y);
            }
            if (r > 0)
                foreach (char ch in rows[r])
                {
                    var (cx, cy) = pos[ch];
                    f.VLine(cx, cy - rh + Math.Min(rh - 2, 8), cy - 1, (int)Math.Round(3 + fm * 10));
                }
        }
        if (pos.TryGetValue('A', out var a) && feedback > 0.02)
        {
            f.HLine(a.X + 6, a.X + 9, a.Y + 1, Widgets.Mid);
            f.VLine(a.X + 9, a.Y - 1, a.Y + 1, Widgets.Mid);
            f.HLine(a.X - 1, a.X + 9, a.Y - 1, (int)Math.Round(4 + feedback * 10), dotted: true);
        }
    }

    /// <summary>Operator's algorithm, 1..11: the number in its text, else its step on the knob.</summary>
    private static int OperatorAlgoNumber(DisplayContext c)
    {
        if (DisplayParams.ParseUnit(c.P.Txt("Algorithm"), out double v, out _) && v >= 1) return Math.Clamp((int)Math.Round(v), 1, 11);
        if (c.P.Raw("Algorithm") > 1.0001) return Math.Clamp((int)Math.Round(c.P.Raw("Algorithm")), 1, 11);
        return 1 + c.P.Step("Algorithm", 11);
    }

    private static void TwoEnvs(DisplayContext c, LedFrame f, string op1, string op2)
    {
        Widgets.Adsr(f, c.P.Sec($"{op1} Attack"), c.P.Sec($"{op1} Decay"), c.P.N($"{op1} Sustain"), c.P.Sec($"{op1} Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 0, 60);
        f.Text(op1, 0, LedFrame.Top, Widgets.Hot);
        f.VLine(63, LedFrame.Top, LedFrame.Bottom, Widgets.Grid);
        Widgets.Adsr(f, c.P.Sec($"{op2} Attack"), c.P.Sec($"{op2} Decay"), c.P.N($"{op2} Sustain"), c.P.Sec($"{op2} Release"), Widgets.Hot, Widgets.Fill, c.Playing ? c.Time : -1, 66, 127);
        f.Text(op2, 66, LedFrame.Top, Widgets.Hot);
    }

    /// <summary>A few labelled vertical bars in a column range.</summary>
    private static void MiniBars(LedFrame f, int x0, double[] values, string[] labels, int x1 = LedFrame.W - 1, int top = LedFrame.Top + 1)
    {
        int n = values.Length, bottom = LedFrame.Bottom - 7;
        double cw = (x1 - x0 + 1) / (double)n;
        for (int i = 0; i < n; i++)
        {
            int x = (int)Math.Round(x0 + i * cw) + 1, w = Math.Max(2, (int)cw - 3);
            Widgets.Meter(f, x, top, w, bottom - top + 1, values[i], Widgets.Mark);
            f.Text(labels[i], x + Math.Max(0, (w - LedFrame.TextWidth(labels[i])) / 2), LedFrame.Bottom - 4, Widgets.Dim);
        }
    }

    /// <summary>Echo bars in a column range: the delay time as spacing, feedback as decay, mix as height.</summary>
    private static void MiniTaps(LedFrame f, int x0, int x1, double time01, double feedback01, double mix01)
    {
        int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
        double gap = 4 + time01 * 20, a = 1;
        f.HLine(x0, x1, mid, Widgets.Grid, dotted: true);
        f.VLine(x0 + 1, mid - 16, mid + 16, Widgets.Hot);
        for (double x = x0 + 1 + gap; x <= x1; x += gap)
        {
            a *= Math.Clamp(feedback01, 0, 0.95);
            if (a < 0.04) break;
            int h = (int)Math.Round(a * 16 * (0.3 + 0.7 * mix01));
            f.VLine(x, mid - h, mid + h, Widgets.Mark);
        }
    }
}
