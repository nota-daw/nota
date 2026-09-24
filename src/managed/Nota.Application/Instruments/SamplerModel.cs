// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Sampler (built-in instrument kind 1) — the engine's own value maps, in one place
// for the editor card and the MCP server: what a normalized 0..1 parameter means (dB,
// semitones, cents, envelope times, cutoff, glide), the Loop / Filter / Voice choices, the
// sample tools (snap to a zero crossing, trim silence, the root note from a file name),
// how the telemetry is laid out, a one-line summary of the patch and a guide to every
// parameter. Mirrors Sampler.h.

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nota.Application;

public static class SamplerModel
{
    public const int Kind = 1;
    public const int Voices = 16;

    // scopeRead layout (Sampler::kTele).
    public const int ScopeLength = 8;

    public static readonly string[] LoopNames = { "Off", "Fwd", "Ping", "Rev" };
    public static readonly string[] FilterNames = { "Off", "LP", "HP", "BP" };
    public static readonly string[] VoiceNames = { "Poly 16", "Mono", "Choke" };
    public static readonly string[] StageNames = { "attack", "decay", "sustain", "release" };
    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));
    public static double ExpInv(double x, double lo, double hi) => Math.Clamp(Math.Log(Math.Clamp(x, lo, hi) / lo) / Math.Log(hi / lo), 0, 1);
    public static int Index(float v, int n) => Math.Clamp((int)Math.Round(v * (n - 1)), 0, n - 1);

    // ---- value maps ---------------------------------------------------------------------
    public static double LinDb(double lin) => lin <= 1e-5 ? double.NegativeInfinity : 20 * Math.Log10(lin);
    /// <summary>volume / output: a linear gain 0..1 (1 = 0 dB).</summary>
    public static double VolumeDb(float v) => LinDb(v);
    /// <summary>gain: the sample's gain before the envelope, ±24 dB (0.5 = 0 dB).</summary>
    public static double GainDb(float v) => (Math.Clamp(v, 0f, 1f) - 0.5) * 48.0;
    public static float GainNorm(double db) => (float)Math.Clamp(0.5 + db / 48.0, 0, 1);
    public static double TransposeSt(float v) => (v - 0.5) * 48.0;
    public static float TransposeNorm(double st) => (float)Math.Clamp(0.5 + st / 48.0, 0, 1);
    public static double DetuneCents(float v) => (v - 0.5) * 100.0;
    public static float DetuneNorm(double c) => (float)Math.Clamp(0.5 + c / 100.0, 0, 1);
    public static double AttackSec(float v) => ExpMap(v, 0.0005, 4.0);
    public static double DecaySec(float v) => ExpMap(v, 0.002, 6.0);
    public static double ReleaseSec(float v) => ExpMap(v, 0.002, 6.0);
    public static double CutoffHz(float v) => ExpMap(v, 20.0, 20000.0);
    public static double CrossfadeSec(float v) => v * 0.2;
    public static double GlideSec(float v) => v <= 0.001f ? 0 : 0.001 * Math.Pow(2000.0, Math.Clamp(v, 0f, 1f));
    public static float GlideNorm(double sec) => sec <= 0.0005 ? 0f : (float)Math.Clamp(Math.Log(sec / 0.001) / Math.Log(2000.0), 0.002, 1);
    public static double EnvOctaves(float v) => (Math.Clamp(v, 0f, 1f) - 0.5) * 12.0;
    public static float EnvOctavesNorm(double oct) => (float)Math.Clamp(0.5 + oct / 12.0, 0, 1);
    /// <summary>pan: 0 left … 0.5 centre … 1 right → −1..1.</summary>
    public static double PanSigned(float v) => (v - 0.5) * 2.0;

    /// <summary>The Loop choice from loopmode + reverse: Off, Fwd, Ping, Rev (a reversed forward loop).</summary>
    public static int LoopIndex(float loopmode, float reverse)
    {
        int lm = Index(loopmode, 3);
        return lm == 2 ? 2 : lm == 1 ? (reverse >= 0.5f ? 3 : 1) : 0;
    }
    /// <summary>loopmode + reverse for a Loop choice.</summary>
    public static (float LoopMode, float Reverse) LoopValues(int choice) => choice switch
    {
        1 => (0.5f, 0f), 2 => (1f, 0f), 3 => (0.5f, 1f), _ => (0f, 0f),
    };

    // ---- notes ----------------------------------------------------------------------------
    public static string NoteName(int note) => note < 0 ? "—" : $"{NoteNames[((note % 12) + 12) % 12]}{note / 12 - 1}";

    /// <summary>"C4", "F#3", "Eb2", "a-1" (C4 = 60) or a MIDI number "60" → the MIDI note, or −1.</summary>
    public static int ParseNote(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var t = text.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n is >= 0 and <= 127 ? n : -1;
        var m = Regex.Match(t, @"^([A-Ga-g])([#♯b♭]?)(-?\d)$");
        return m.Success ? NoteOf(m) : -1;
    }

    private static int NoteOf(Match m)
    {
        int pc = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
        string acc = m.Groups[2].Value;
        if (acc is "#" or "♯") pc++;
        else if (acc is "b" or "♭") pc--;
        int oct = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        int note = (oct + 1) * 12 + pc;
        return note is >= 0 and <= 127 ? note : -1;
    }

    /// <summary>The root note a sample's file name names — "Piano_C4", "Str Eb3 soft",
    /// "bass-F#1-rr2" (the last note-like token wins, C4 = 60). −1 when there is none.</summary>
    public static int DetectRoot(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return -1;
        string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        // A note letter not glued to a word before it, an optional accidental, an octave
        // −1..9 not followed by another digit.
        var ms = Regex.Matches(name, @"(?<![A-Za-z0-9])([A-Ga-g])([#♯b♭]?)(-?\d)(?!\d)");
        for (int i = ms.Count - 1; i >= 0; i--)
        {
            int note = NoteOf(ms[i]);
            if (note >= 0) return note;
        }
        return -1;
    }

    // ---- sample tools ---------------------------------------------------------------------
    private static float Mono(float[] raw, int ch, long f)
    {
        float s = 0;
        for (int c = 0; c < ch; c++) s += raw[f * ch + c];
        return s / ch;
    }

    /// <summary>The zero crossing nearest a normalized position (0..1), normalized.</summary>
    public static double SnapZero(float[] raw, int channels, long frames, double pos)
    {
        int ch = Math.Max(1, channels);
        if (frames < 2 || raw.Length < frames * ch) return pos;
        long target = (long)Math.Round(Math.Clamp(pos, 0, 1) * (frames - 1));
        bool Cross(long c) => c >= 1 && c < frames
            && ((Mono(raw, ch, c - 1) <= 0 && Mono(raw, ch, c) >= 0) || (Mono(raw, ch, c - 1) >= 0 && Mono(raw, ch, c) <= 0));
        for (long d = 0; d < frames; d++)
        {
            if (Cross(target - d)) return (target - d) / (double)(frames - 1);
            if (Cross(target + d)) return (target + d) / (double)(frames - 1);
        }
        return pos;
    }

    /// <summary>Where the sound starts and ends: the first and last frame louder than
    /// <paramref name="thresholdDb"/> below the sample's peak (normalized), or null when the
    /// sample is silent.</summary>
    public static (double Start, double End)? TrimSilence(float[] raw, int channels, long frames, double thresholdDb = -48)
    {
        int ch = Math.Max(1, channels);
        if (frames < 2 || raw.Length < frames * ch) return null;
        float peak = 0;
        for (long f = 0; f < frames; f++) peak = Math.Max(peak, Math.Abs(Mono(raw, ch, f)));
        if (peak <= 1e-6f) return null;
        float thr = peak * (float)Math.Pow(10, thresholdDb / 20.0);
        long a = 0, b = frames - 1;
        while (a < frames && Math.Abs(Mono(raw, ch, a)) < thr) a++;
        while (b > a && Math.Abs(Mono(raw, ch, b)) < thr) b--;
        if (a >= b) return null;
        return (a / (double)(frames - 1), Math.Min(1.0, (b + 1) / (double)(frames - 1)));
    }

    /// <summary>Downsample interleaved samples to min/max pairs per bucket (mono sum).</summary>
    public static float[] Peaks(float[] samples, int channels, int buckets)
    {
        var peaks = new float[buckets * 2];
        if (samples.Length == 0 || channels <= 0) return peaks;
        long fr = samples.Length / channels;
        for (int b = 0; b < buckets; b++)
        {
            long a = b * fr / buckets, e = (b + 1) * fr / buckets;
            if (e <= a) e = Math.Min(fr, a + 1);
            float mn = 1f, mx = -1f;
            for (long f = a; f < e; f++) { float s = Mono(samples, channels, f); if (s < mn) mn = s; if (s > mx) mx = s; }
            if (mn > mx) { mn = mx = 0; }
            peaks[b * 2] = mn; peaks[b * 2 + 1] = mx;
        }
        return peaks;
    }

    // ---- telemetry ------------------------------------------------------------------------

    public sealed class Snapshot
    {
        public int Voices, Stage = -1, Held;
        public double PlayPos = -1, Env, Note = -1, CutoffHz = -1, Peak;
        public bool Live;
    }

    /// <summary>Parse the engine scope. <c>Live</c> is false when the scope is empty (a
    /// Sampler inside a rack chain) — the caller then leans on the play position alone.</summary>
    public static void Parse(ReadOnlySpan<float> s, Snapshot o)
    {
        o.Live = s.Length >= ScopeLength;
        if (!o.Live) return;
        o.Voices = (int)s[0]; o.PlayPos = s[1]; o.Env = s[2]; o.Stage = (int)s[3];
        o.Note = s[4]; o.CutoffHz = s[5]; o.Peak = s[6]; o.Held = (int)s[7];
    }

    // ---- words ----------------------------------------------------------------------------

    private static string S(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
    public static string Secs(double s) => s < 1 ? S(s * 1000, "0") + " ms" : S(s, "0.00") + " s";
    public static string Db(double db) => double.IsNegativeInfinity(db) || db < -99 ? "-inf dB" : S(db, "+0.0;-0.0;0.0") + " dB";
    public static string Hz(double hz) => hz < 999.5 ? S(hz, "0") + " Hz" : S(hz / 1000, hz < 9950 ? "0.0" : "0") + " kHz";
    private static string Pct(double v) => S(v * 100, "0") + " %";

    /// <summary>A one-line summary of the patch, from a param-id getter, the root and the sample's length.</summary>
    public static string Summary(Func<string, float> g, int root, double durationSec, string sampleName = "")
    {
        var sb = new StringBuilder();
        if (sampleName.Length > 0) sb.Append('"').Append(sampleName).Append("\" · ");
        if (durationSec > 0)
            sb.Append(S(g("start") * durationSec, "0.00")).Append(" → ").Append(S(g("end") * durationSec, "0.00")).Append(" s · ");
        int loop = LoopIndex(g("loopmode"), g("reverse"));
        sb.Append(loop == 0 ? (g("reverse") >= 0.5f ? "one-shot reversed" : "one-shot") : "loop " + LoopNames[loop].ToLowerInvariant());
        if (loop > 0 && durationSec > 0)
            sb.Append(' ').Append(S(g("loopstart") * durationSec, "0.00")).Append("–").Append(S(g("loopend") * durationSec, "0.00")).Append(" s, crossfade ").Append(Secs(CrossfadeSec(g("loopxfade"))));
        if (Math.Abs(GainDb(g("gain"))) > 0.05) sb.Append(" · gain ").Append(Db(GainDb(g("gain"))));
        sb.Append(" · root ").Append(NoteName(root));
        double st = TransposeSt(g("transpose")), ct = DetuneCents(g("detune"));
        if (Math.Abs(st) > 0.01) sb.Append(" · transpose ").Append(S(st, "+0;-0")).Append(" st");
        if (Math.Abs(ct) > 0.5) sb.Append(" · detune ").Append(S(ct, "+0;-0")).Append(" c");
        if (g("pitchtrack") < 0.995f) sb.Append(" · keytrack ").Append(Pct(g("pitchtrack")));
        sb.Append(" · env ").Append(Secs(AttackSec(g("attack")))).Append(" / ").Append(Secs(DecaySec(g("decay"))))
          .Append(" / ").Append(Db(VolumeDb(g("sustain")))).Append(" / ").Append(Secs(ReleaseSec(g("release"))));
        int ft = Index(g("filtertype"), 4);
        if (ft > 0)
        {
            sb.Append(" · filter ").Append(FilterNames[ft]).Append(' ').Append(Hz(CutoffHz(g("cutoff")))).Append(", reso ").Append(Pct(g("resonance")));
            if (g("keytrack") > 0.005f) sb.Append(", keytrack ").Append(Pct(g("keytrack")));
            if (g("envcutoff") >= 0.5f) sb.Append(", env ").Append(S(EnvOctaves(g("envamount")), "+0.0;-0.0")).Append(" oct");
        }
        sb.Append(" · ").Append(VoiceNames[Index(g("voicemode"), 3)].ToLowerInvariant());
        if (GlideSec(g("glide")) > 0) sb.Append(", glide ").Append(Secs(GlideSec(g("glide"))));
        sb.Append(" · vol ").Append(Db(VolumeDb(g("volume"))));
        double pan = PanSigned(g("pan"));
        if (Math.Abs(pan) > 0.015) sb.Append(" · pan ").Append(pan < 0 ? "L" : "R").Append(S(Math.Abs(pan) * 100, "0"));
        if (g("output") < 0.995f) sb.Append(" · out ").Append(Pct(g("output")));
        sb.Append(" · vel→vol ").Append(Pct(g("velamount")));
        return sb.ToString();
    }

    /// <summary>What each 0..1 parameter value means, one line per group.</summary>
    public const string Guide =
        "All params are normalized 0..1 (set_instrument_param_by_id, or set_sampler in real units). The sampler plays one sample, pitched by "
        + "the key relative to the root note.\n"
        + "start / end: the played window, 0..1 of the sample. gain: sample gain ±24 dB, 0.5 = 0 dB. reverse: 1 plays backwards. "
        + "loopmode: Off 0 · Forward 0.5 · Ping-pong 1 (Rev = forward with reverse 1); loopstart / loopend 0..1 of the sample (inside start..end); "
        + "loopxfade: forward-loop crossfade 0..200 ms (v*200).\n"
        + "transpose: ±24 st, 0.5 = 0 ((v-0.5)*48). detune: ±50 cents, 0.5 = 0. pitchtrack: how far the key moves the pitch — 1 chromatic, "
        + "0 every key plays the root. Root note: set_sampler root (not a param).\n"
        + "attack 0.5 ms … 4 s, decay 2 ms … 6 s, release 2 ms … 6 s (all exp: lo·(hi/lo)^v); sustain: linear level 0..1 (1 = 0 dB).\n"
        + "filtertype: Off 0 · LP .333 · HP .667 · BP 1. cutoff 20 Hz … 20 kHz (exp). resonance 0..1. keytrack: cutoff follows the key 0..100 %. "
        + "envcutoff: 1 lets the amp envelope move the cutoff by envamount: ±6 octaves, 0.5 = none ((v-0.5)*12).\n"
        + "voicemode: Poly 16 0 · Mono .5 · Choke 1 (a new note cuts the others). glide: 0 off, else 1 ms … 2 s (exp) — a slide from the last "
        + "note; in Mono, legato (no retrigger, a release slides back to the held key). velamount: velocity → volume 0..100 %.\n"
        + "volume: voice level, linear (1 = 0 dB). pan: 0 left · 0.5 centre · 1 right. output: the level after everything, linear 0..1.";
}
