// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Keys (built-in instrument kind 16) — the engine's own value maps in one place for the
// editor card and the MCP server: what a normalized 0..1 parameter means in dB / Hz / %,
// the PICKUP transfer curve (the same function the engine runs, so the graph is the sound),
// the harmonics it makes, the scope telemetry layout, a one-line summary and a parameter
// guide. Mirrors Keys.h.

using System;
using System.Globalization;
using System.Text;

namespace Nota.Application;

public static class KeysModel
{
    public const int Kind = 16;

    // scopeRead layout (Keys::Scope).
    public const int ScActive = 0, ScLimit = 1, ScHeld = 2, ScSustained = 3, ScPedal = 4, ScLfo = 5,
        ScPeakL = 6, ScPeakR = 7, ScLastNote = 8, ScopeLength = 9;

    public static readonly string[] ModelNames = { "Tine", "Suitcase", "Reed", "Clav" };
    public static readonly string[] ModelShort = { "Tine", "Case", "Reed", "Clav" };
    public static readonly string[] PickupPositions = { "Upper", "Both", "Lower" };
    public static readonly int[] VoiceCounts = { 8, 16, 32, 64 };
    public static readonly string[] VoiceNames = { "8", "16", "32", "64" };
    public static readonly string[] TremModes = { "Mono", "Stereo" };
    public static readonly string[] TremSyncs = { "Free", "Tempo" };
    public static readonly string[] TremDivisions = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    public static readonly string[] CabNames = { "Off", "Suitcase", "Combo", "DI" };
    public static readonly string[] CabNotes = { "bypass", "2×12″ wood", "Wurli speaker", "line" };

    /// <summary>What picking a model chip resets: the pickup it was voiced with and its usual cabinet.</summary>
    public static (double Sym, double Dist, int Cab) ModelDefaults(int model) => model switch
    {
        1 => (0.28, 0.46, 1),
        2 => (0.0, 0.28, 2),
        3 => (0.0, 0.5, 3),
        _ => (0.2, 0.4, 3),
    };

    public static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));
    public static int Index(float v, int n) => Math.Clamp((int)Math.Round(v * (n - 1)), 0, n - 1);
    public static double Bipolar(float v) => (v - 0.5) * 2.0;

    // ---- value maps ---------------------------------------------------------------------
    /// <summary>Hammer Noise / Damper Release-Noise → dB (null = off).</summary>
    public static double? NoiseDb(float v) => v <= 1e-3f ? null : -60.0 + 48.0 * Math.Sqrt(v);
    /// <summary>Resonator Decay → % of the model's natural ring time.</summary>
    public static double DecayPct(float v) => ExpMap(v, 0.25, 4.0) * 100.0;
    /// <summary>Damper Amount → how long a released note takes to die (s, e-folding).</summary>
    public static double DamperSeconds(float v) => ExpMap(1.0 - v, 0.025, 3.0);
    public static double TuneCents(float v) => (v - 0.5) * 100.0;
    /// <summary>Preamp Bass / Treble → shelf gain (dB, ±12).</summary>
    public static double ShelfDb(float v) => (v - 0.5) * 24.0;
    public static double TremHz(float v) => ExpMap(v, 0.5, 15.0);
    public static double PhaserHz(float v) => ExpMap(v, 0.05, 5.0);
    /// <summary>Output Volume → dB: 0.8 = 0 dB, 1 = +6, log taper below; null = silent.</summary>
    public static double? VolumeDb(float v) => v <= 1e-3f ? null : v >= 0.8f ? (v - 0.8) * 30.0 : 60.0 * Math.Log10(v / 0.8);

    // ---- the PICKUP curve (Keys.h keys::pickupRaw) ---------------------------------------
    public static double PickupRaw(double x, double sym, double dist, int model)
    {
        double a = 0.3 + (1.0 - dist) * 3.2;
        if (model == 2)
        {
            double k = (1.0 - dist) * 0.8;
            return x / (1.0 + a * 0.6 * Math.Abs(x)) * (1.0 + k * x * x) / (1.0 + k) + sym * 0.35 * (2.0 - dist) * x * x;
        }
        double aa = model == 3 ? a * 0.5 : a;
        return x / (1.0 + aa * Math.Abs(x)) + sym * 0.55 * (2.0 - dist) * x * x;
    }

    public static double PickupNorm(double sym, double dist, int model)
    {
        double mx = 0;
        for (int i = 0; i <= 60; i++) mx = Math.Max(mx, Math.Abs(PickupRaw(-1.0 + i / 30.0, sym, dist, model)));
        return mx > 1e-6 ? mx : 1.0;
    }

    /// <summary>Magnitudes of harmonics 1..count of the curve driven by a sine of amplitude
    /// <paramref name="amp"/> (DFT, 128 points) — the editor's H1–H8 bars and the bark readout.</summary>
    public static double[] Harmonics(double sym, double dist, int model, double amp = 0.72, int count = 8)
    {
        const int N = 128;
        double norm = PickupNorm(sym, dist, model);
        var sig = new double[N];
        for (int n = 0; n < N; n++) sig[n] = PickupRaw(amp * Math.Sin(2 * Math.PI * n / N), sym, dist, model) / norm;
        var mags = new double[count];
        for (int k = 1; k <= count; k++)
        {
            double re = 0, im = 0;
            for (int n = 0; n < N; n++) { double ph = 2 * Math.PI * k * n / N; re += sig[n] * Math.Cos(ph); im += sig[n] * Math.Sin(ph); }
            mags[k - 1] = Math.Sqrt(re * re + im * im);
        }
        return mags;
    }

    /// <summary>The second harmonic against the fundamental, dB — the "bark".</summary>
    public static double BarkDb(double sym, double dist, int model)
    {
        var h = Harmonics(sym, dist, model, count: 2);
        return 20 * Math.Log10(h[1] / Math.Max(1e-9, h[0]) + 1e-6);
    }

    // ---- text ---------------------------------------------------------------------------
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Pct(double v) => Math.Round(v * 100).ToString(Inv) + "\u2009%";
    private static string SignedPct(double v) => (v >= 0 ? "+" : "−") + Math.Round(Math.Abs(v) * 100).ToString(Inv) + "\u2009%";

    public static string TremRateText(Func<string, float> g)
        => g("tremsync") >= 0.5f ? TremDivisions[Index(g("tremrate"), TremDivisions.Length)]
                                 : TremHz(g("tremrate")).ToString("0.0", Inv) + "\u2009Hz";

    /// <summary>One line: the model, the hammer, the pickup and the effects that are on.</summary>
    public static string Summary(Func<string, float> g)
    {
        int model = Index(g("model"), 4);
        var sb = new StringBuilder();
        sb.Append(ModelNames[model]);
        if (model == 3) sb.Append(" · ").Append(PickupPositions[Index(g("pupos"), 3)]).Append(" pickup");
        sb.Append(" · hard ").Append(Pct(g("hard")));
        sb.Append(" · pickup ").Append(Pct(g("dist"))).Append(" / ").Append(SignedPct(Bipolar(g("sym"))));
        sb.Append(" · damper ").Append(Pct(g("damper")));
        sb.Append(" · ").Append(VoiceCounts[Index(g("voices"), 4)]).Append(" voices");
        if (g("preon") >= 0.5f) sb.Append(" · drive ").Append(Pct(g("drive")));
        if (g("tremon") >= 0.5f) sb.Append(" · trem ").Append(TremModes[Index(g("tremmode"), 2)].ToLowerInvariant()).Append(' ').Append(TremRateText(g)).Append(' ').Append(Pct(g("tremdepth")));
        if (g("phaseron") >= 0.5f) sb.Append(" · phaser ").Append(PhaserHz(g("phaserrate")).ToString("0.00", Inv)).Append("\u2009Hz");
        if (g("choruson") >= 0.5f) sb.Append(" · chorus ").Append(Pct(g("chorusmix")));
        sb.Append(" · cab ").Append(CabNames[Index(g("cab"), 4)]);
        if (g("pedal") >= 0.5f) sb.Append(" · pedal down");
        return sb.ToString();
    }

    public const string Guide =
        "All values are normalized 0..1 (set_instrument_param_by_id, or set_keys in musical terms). "
        + "model: 0 Tine, 1/3 Suitcase, 2/3 Reed, 1 Clav. "
        + "hammer — hard: contact time 6.5 ms (0, soft, dark) .. 0.35 ms (1, hard, bright); velhard: how much velocity hardens the hammer; "
        + "noise: hammer thump, 0 off else -60..-12 dB (sqrt taper). "
        + "resonator — decay: ring time 25 %..400 % of the model's natural (0.5 = 100 %); body: tone-bar / fundamental weight (0.5 = natural); "
        + "bright: the bell partials (6.27x, 17.55x for tine/reed, the string's upper harmonics for clav), 0.5 = natural; "
        + "keybright: bipolar, 0.5 = flat, >0.5 brighter up the keyboard. "
        + "pickup — sym: bipolar offset of the tine against the pickup (0.5 = centred; off-centre = even harmonics, the bark); "
        + "dist: 0 = pickup close (strong saturation, growl) .. 1 = far (clean, linear); pupos (clav only): 0 Upper, 0.5 Both, 1 Lower. "
        + "damper — damper: how fast a released key dies, 0 = rings ~3 s .. 1 = stops in ~25 ms; relnoise: damper felt thump (as noise); "
        + "pedal: >= 0.5 holds released notes (OR'd with a keyboard's CC64). "
        + "play — tune: +-50 cents (0.5 = 0); age: per-key detune / decay / level spread; stretch: octave stretch (0..30 cents at the ends); "
        + "voices: 0 = 8, 1/3 = 16, 2/3 = 32, 1 = 64. "
        + "preamp — preon on/off, drive 1x..14x into a soft clipper, bass / treble: shelves +-12 dB (0.5 = flat). "
        + "tremolo — tremon, tremmode 0 mono amplitude / 1 stereo auto-pan, tremsync 0 free (tremrate 0.5..15 Hz) / 1 tempo (tremrate picks 1/1,1/2,1/4,1/8,1/8T,1/16,1/16T,1/32), tremdepth 0..1. "
        + "phaser — phaseron, phaserrate 0.05..5 Hz, phaserdepth. chorus — choruson, chorusmix. "
        + "cab: 0 Off, 1/3 Suitcase, 2/3 Combo, 1 DI. volume: 0.8 = 0 dB, 1 = +6 dB; pan: 0.5 = centre. "
        + "MPE: bend retunes, pressure drives the tine harder into the pickup, slide shifts the pickup symmetry.";
}
