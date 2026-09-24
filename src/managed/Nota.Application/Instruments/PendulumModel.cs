// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pendulum (built-in instrument kind 8) — the engine's own value maps, in one place
// for the editor card and the MCP server: what a normalized 0..1 parameter means (ball
// count, swing rate, cycle, envelope times, cents), how the engine's scope telemetry is
// laid out (balls, the bar's note grid, voice levels), a one-line summary of the patch and
// a guide to every parameter. Mirrors PendulumSynth.h.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nota.Application;

public static class PendulumModel
{
    public const int Kind = 8;
    public const int MaxBalls = 6;
    public const int Voices = 24;

    // scopeRead layout (PendulumSynth::kHead …).
    public const int ScopeHead = 12, BallStride = 8, MaxSteps = 32, MaxLevels = 8;
    public const int BallsAt = ScopeHead;
    public const int StepsAt = BallsAt + MaxBalls * BallStride;
    public const int LevelsAt = StepsAt + MaxSteps * 2;
    public const int ScopeLength = LevelsAt + MaxLevels;

    public static readonly double[] DivBeats = { 4, 2, 1, 0.5, 0.25 };
    public static readonly string[] DivNames = { "1/1", "1/2", "1/4", "1/8", "1/16" };
    public static readonly string[] MotionNames = { "Linear", "Pendulum", "Ease", "Bounce" };
    public static readonly string[] WaveNames = { "Keys", "Glass", "Saw", "Sqr", "Bell" };
    public static readonly string[] QuantNames = { "Off", "1/16", "1/8" };
    public static readonly string[] ModeNames = { "Off", "Major", "Minor", "Dorian", "Mixolydian", "Minor penta" };
    public static readonly string[] RootNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));
    public static int Index(float v, int n) => Math.Clamp((int)Math.Round(v * (n - 1)), 0, n - 1);

    public static int BallCount(float v) => Math.Clamp(2 + (int)Math.Round(v * 4), 2, MaxBalls);
    public static float BallCountNorm(int count) => (Math.Clamp(count, 2, MaxBalls) - 2) / 4f;
    /// <summary>Rate → signed swing speed, 1 = +100 % (the engine's ±2 multiplier halved).</summary>
    public static double RateSigned(float v) => (v - 0.5) * 2.0;
    /// <summary>A ball's own rate: the base, spread apart by Spread (ball 0 keeps the base).</summary>
    public static double BallRate(float rate, float spread, int ball) => RateSigned(rate) * (1.0 + spread * ball * 0.37);
    public static double FreeSeconds(float v) => ExpMap(v, 0.1, 4.0);
    public static double NoteSeconds(float v) => ExpMap(v, 0.05, 1.5);
    public static double AttackSeconds(float v) => ExpMap(v, 0.001, 1.0);
    public static double DecaySeconds(float v) => ExpMap(v, 0.01, 2.0);
    public static double ReleaseSeconds(float v) => ExpMap(v, 0.02, 3.0);
    public static double ToneHz(float v) => ExpMap(v, 300.0, 12000.0);
    public static double DetuneCents(float v) => v * 14.0;
    public static string ToneWord(float v) => v < 0.34f ? "dark" : v < 0.67f ? "warm" : "bright";

    /// <summary>Seconds one ball needs for a full swing (low → high → low) at the base rate, or
    /// +∞ when stopped; synced cycles take the tempo.</summary>
    public static double CycleSeconds(Func<string, float> g, double bpm)
    {
        double mult = Math.Abs(RateSigned(g("rate")) * 2.0);
        if (mult < 1e-6) return double.PositiveInfinity;
        double cyc = g("sync") >= 0.5f ? DivBeats[Index(g("division"), 5)] * 60.0 / Math.Max(1, bpm) : FreeSeconds(g("freerate"));
        return cyc / mult;
    }

    /// <summary>The four swing curves (low 0 → high 1 → low 0 over a cycle).</summary>
    public static double Position(double ph, int motion)
    {
        double tri = 1.0 - 2.0 * Math.Abs(ph - 0.5);
        return motion switch
        {
            1 => 0.5 - 0.5 * Math.Cos(2 * Math.PI * ph),
            2 => tri * tri * (3.0 - 2.0 * tri),
            3 => Math.Clamp(1.0 - Math.Pow(1.0 - tri, 2.0) + 0.06 * Math.Sin(2 * Math.PI * 3.0 * tri) * (1.0 - tri), 0, 1),
            _ => tri,
        };
    }

    public static string ScaleText(float root, float mode)
    {
        int m = Index(mode, ModeNames.Length);
        return m == 0 ? "Off" : $"{RootNames[Index(root, 12)]} {ModeNames[m].ToLowerInvariant()}";
    }

    public static string NoteName(int note) => note < 0 ? "—" : $"{RootNames[((note % 12) + 12) % 12]}{note / 12 - 1}";

    // ---- telemetry --------------------------------------------------------------------

    public readonly record struct Ball(double Phase, double Pos, int Dir, int Pitch, double ToWall, double Since, double Rate, int Degree);
    public readonly record struct Step(int Pitch, int Age);

    public sealed class Snapshot
    {
        public int Voices, Balls, Held, LastPitch = -1, LastBall = -1, StepCount = 16, BarNotes;
        public double BarPos, SwingSeconds, SinceNote = 1e9, BeatsPerBar = 4;
        public bool Playing, Live;
        public readonly Ball[] BallState = new Ball[MaxBalls];
        public readonly Step[] Steps = new Step[MaxSteps];
        public readonly double[] Levels = new double[MaxLevels];
    }

    /// <summary>Parse the engine scope into a snapshot. <c>Live</c> is false when the scope
    /// is empty (e.g. a Pendulum inside a rack chain) — the caller may simulate then.</summary>
    public static void Parse(ReadOnlySpan<float> s, Snapshot o)
    {
        o.Live = s.Length >= ScopeLength;
        if (!o.Live) return;
        o.Voices = (int)s[0]; o.Balls = (int)s[1]; o.Held = (int)s[2]; o.LastPitch = (int)s[3];
        o.BarPos = s[4]; o.StepCount = Math.Clamp((int)s[5], 1, MaxSteps); o.SwingSeconds = s[6];
        o.LastBall = (int)s[7]; o.SinceNote = s[8]; o.BarNotes = (int)s[9]; o.BeatsPerBar = s[10]; o.Playing = s[11] >= 0.5f;
        for (int b = 0; b < MaxBalls; b++)
        {
            int at = BallsAt + b * BallStride;
            o.BallState[b] = new Ball(s[at], s[at + 1], (int)s[at + 2], (int)s[at + 3], s[at + 4], s[at + 5], s[at + 6], (int)s[at + 7]);
        }
        for (int i = 0; i < MaxSteps; i++) o.Steps[i] = new Step((int)s[StepsAt + i * 2], (int)s[StepsAt + i * 2 + 1]);
        for (int i = 0; i < MaxLevels; i++) o.Levels[i] = s[LevelsAt + i];
    }

    // ---- words ------------------------------------------------------------------------

    private static string S(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
    private static string Secs(double s) => s < 1 ? S(s * 1000, "0") + " ms" : S(s, "0.00") + " s";
    private static string Pct(double v) => S(v * 100, "0") + " %";
    private static string Signed(double v) => (v > 0 ? "+" : v < 0 ? "-" : "") + S(Math.Abs(v) * 100, "0") + " %";

    /// <summary>A one-line summary of the patch, from a param-id getter.</summary>
    public static string Summary(Func<string, float> g)
    {
        var sb = new StringBuilder();
        int balls = BallCount(g("balls"));
        bool sync = g("sync") >= 0.5f;
        sb.Append(balls).Append(" balls · ");
        sb.Append(sync ? "sync " + DivNames[Index(g("division"), 5)] : "free " + Secs(FreeSeconds(g("freerate"))));
        sb.Append(" · ").Append(MotionNames[Index(g("motion"), 4)].ToLowerInvariant());
        double r = RateSigned(g("rate"));
        sb.Append(" · rate ").Append(Math.Abs(r) < 0.005 ? "stopped" : Signed(r) + (r < 0 ? " (reverse)" : ""));
        if (g("spread") > 0.005f) sb.Append(" · spread ").Append(Pct(g("spread")));
        sb.Append(" · sort ").Append(g("chordsort") >= 0.5f ? "down" : "up");
        sb.Append(" · quantize ").Append(QuantNames[Index(g("quantize"), 3)]);
        if (g("hold") >= 0.5f) sb.Append(" · hold");
        if (g("firstnote") >= 0.5f) sb.Append(" · restart on first note");
        sb.Append(" → ").Append(WaveNames[Index(g("wave"), 5)].ToLowerInvariant()).Append(' ').Append(ToneWord(g("tone")));
        sb.Append(" · bright ").Append(Pct(g("bright")));
        if (g("fm") > 0.005f) sb.Append(" · fm ").Append(Pct(g("fm")));
        sb.Append(" · env ").Append(Secs(AttackSeconds(g("attack")))).Append(" / ").Append(Secs(DecaySeconds(g("decay"))))
          .Append(" / ").Append(Secs(ReleaseSeconds(g("release")))).Append(", note ").Append(Secs(NoteSeconds(g("notelen"))));
        sb.Append(" · detune ").Append(S(DetuneCents(g("detune")), "0")).Append(" c · pan ").Append(Pct(g("panspread")));
        if (g("humanize") > 0.005f) sb.Append(" · humanize ").Append(Pct(g("humanize")));
        string sc = ScaleText(g("root"), g("scalemode"));
        if (sc != "Off") sb.Append(" · scale ").Append(sc);
        sb.Append(" · vol ").Append(Pct(g("volume")));
        return sb.ToString();
    }

    /// <summary>What each 0..1 parameter value means, one line per group.</summary>
    public const string Guide =
        "All params are normalized 0..1 (set_instrument_param_by_id). Hold a chord: the balls swing across it and every chord degree a ball "
        + "crosses plays that note — x = pitch (low … high), so the chord comes out as an evolving arpeggio.\n"
        + "balls: 2 + round(v*4) → 2..6 balls (0, .25, .5, .75, 1). rate: 0.5 = stopped, above = forward, below = reverse; speed (v-0.5)*200 %. "
        + "sync: 0 free (freerate: 0.1 … 4 s per swing, exp) / 1 synced (division: 1/1 0 · 1/2 .25 · 1/4 .5 · 1/8 .75 · 1/16 1 per swing). "
        + "motion: Linear 0 · Pendulum .33 · Ease .67 · Bounce 1 (the swing curve — pendulum dwells at the walls). "
        + "spread: how far the balls' rates drift apart (ball n runs at rate × (1 + spread·n·0.37)).\n"
        + "quantize: Off 0 · 1/16 0.5 · 1/8 1 (notes wait for the grid). chordsort: 0 up (low degree = low note) / 1 down. "
        + "hold: 1 latches the chord after release. firstnote: 1 restarts the swing when a new chord starts. reset: write 1 to restart every ball (self-clears).\n"
        + "wave: Keys 0 · Glass .25 · Saw .5 · Sqr .75 · Bell 1. tone: low-pass 300 Hz … 12 kHz (exp). bright: upper partials. fm: phase modulation depth. "
        + "attack 1 ms … 1 s, decay 10 ms … 2 s (to a 62 % sustain), release 20 ms … 3 s, notelen 50 ms … 1.5 s gate (all exp). volume: output level.\n"
        + "detune: chorus partner ±0 … 14 cents. panspread: balls fan across the stereo field by position. humanize: level / pitch jitter.\n"
        + "root: C 0 … B 1 in 1/11 steps. scalemode: Off 0 · Major .2 · Minor .4 · Dorian .6 · Mixolydian .8 · Minor penta 1 (snaps generated notes).";
}
