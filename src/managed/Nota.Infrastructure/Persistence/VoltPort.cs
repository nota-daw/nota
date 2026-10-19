// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Turns a Nota Synth (kind 0) or Nota Bass (kind 7) patch into the Nota Volt (kind 6) patch
// that sounds like it (specs/026). Both are one-oscillator subsets of Volt's engine, so most
// knobs carry straight over; the ones whose ranges differ (cutoff, envelope times, glide,
// octave, resonance, loudness, LFO rate) are converted through their real units rather than
// copied as numbers. A parameter a patch does not name takes its instrument's default first,
// exactly as applying the preset would.

using System;
using System.Collections.Generic;

namespace Nota.Infrastructure;

internal static class VoltPort
{
    // Nota Synth's defaults (Synth.h constructor).
    private static readonly Dictionary<string, float> SynthDefaults = new()
    {
        ["wave"] = 0f, ["attack"] = 0.02f, ["decay"] = 0.25f, ["sustain"] = 0.7f, ["release"] = 0.20f,
        ["cutoff"] = 0.62f, ["resonance"] = 0.12f, ["gain"] = 0.80f, ["filtype"] = 1f / 3f, ["filenv"] = 0.5f,
        ["pulsewidth"] = 0.5f, ["detune"] = 0.5f, ["octave"] = 0.5f, ["unison"] = 0f, ["spread"] = 0.35f,
        ["glide"] = 0f, ["velamp"] = 1f, ["pan"] = 0.5f, ["voicemode"] = 0f,
    };

    // Nota Bass's defaults (BassSynth.h constructor).
    private static readonly Dictionary<string, float> BassDefaults = new()
    {
        ["oscshape"] = 0.62f, ["oscpw"] = 0.5f, ["oscoctave"] = 0.5f, ["oscsemi"] = 0.5f, ["osclevel"] = 0.85f,
        ["subwave"] = 0f, ["suboctave"] = 0f, ["sublevel"] = 0.7f,
        ["filtype"] = 0f, ["filslope"] = 1f, ["filfreq"] = 0.4f, ["filreso"] = 0.22f, ["fildrive"] = 0.25f,
        ["filenv"] = 0.72f, ["filkey"] = 0.35f, ["fillfo"] = 0.5f,
        ["fattack"] = 0f, ["fdecay"] = 0.35f, ["fsustain"] = 0.15f, ["frelease"] = 0.25f,
        ["attack"] = 0f, ["decay"] = 0.4f, ["sustain"] = 0.85f, ["release"] = 0.22f,
        ["lforate"] = 0.35f, ["lfowave"] = 0f, ["lfopitch"] = 0.5f,
        ["glide"] = 0.08f, ["unison"] = 0f, ["drive"] = 0.15f, ["volume"] = 0.85f,
        ["velamp"] = 0.4f, ["velfilter"] = 0.35f, ["mono"] = 1f,
        ["bend"] = 0.5f, ["bendrange"] = 1f / 11f, ["modwheel"] = 0f, ["outpan"] = 0.5f, ["legato"] = 0f,
    };

    // Volt's wave chips, by Volt's encoding.
    private const float Saw = 0f, Square = 1f / 3f, Tri = 2f / 3f, Sine = 1f;

    /// <summary>The Volt parameters (by id) for a Nota Synth patch.</summary>
    public static (string Id, float Value)[] FromSynth(IReadOnlyDictionary<string, float> named)
    {
        var s = Merge(SynthDefaults, named);
        var v = Base();

        v["osc1wave"] = s["wave"];                                   // same encoding
        v["osc1pw"] = s["pulsewidth"];
        v["osc1octave"] = OctaveNorm(Steps(s["octave"], 4));         // Synth ±2 → Volt ±3

        int uni = new[] { 1, 2, 4, 7 }[Math.Clamp((int)MathF.Round(s["unison"] * 3f, MidpointRounding.AwayFromZero), 0, 3)];
        if (uni == 1)
        {
            v["osc1detune"] = s["detune"];                           // a plain fine tune, ±50 c in both
            v["unison"] = 0f;
        }
        else
        {
            v["univoices"] = uni switch { 2 => 1f / 3f, 4 => 2f / 3f, _ => 1f };
            v["unison"] = Math.Abs(s["detune"] - 0.5f) * 2f;         // the stack spans ±(unison × 50) c
            v["unispread"] = s["spread"];
        }

        // Synth's one envelope moves both the level and the cutoff; Volt has one for each.
        float a = s["attack"], d = Rescale(s["decay"], 0.002, 2.0, 0.002, 4.0), sus = s["sustain"], r = Rescale(s["release"], 0.002, 3.0, 0.002, 5.0);
        v["attack"] = a; v["decay"] = d; v["sustain"] = sus; v["release"] = r;
        v["fattack"] = a; v["fdecay"] = d; v["fsustain"] = sus; v["frelease"] = r;

        int filType = (int)MathF.Round(s["filtype"] * 3f, MidpointRounding.AwayFromZero);
        if (filType == 0)
        {
            // Off → the low-pass fully open, flat and still.
            v["fil1type"] = 0f; v["fil1freq"] = 1f; v["fil1reso"] = 0f; v["fil1env"] = 0.5f;
        }
        else
        {
            v["fil1type"] = (filType - 1) / 3f;                      // LP / HP / BP
            v["fil1freq"] = s["cutoff"];                             // 20 Hz..18 kHz in both
            v["fil1reso"] = Math.Clamp(s["resonance"] * 1.94f / 1.9f, 0f, 1f);
            v["fil1env"] = s["filenv"];
        }
        v["fil1key"] = 0f;
        v["velfilter"] = 0f;
        v["velamp"] = s["velamp"];

        int mode = (int)MathF.Round(s["voicemode"] * 2f, MidpointRounding.AwayFromZero);
        v["mono"] = mode == 0 ? 0f : 1f;
        v["legato"] = mode == 2 ? 1f : 0f;
        // Synth slides only in Mono / Legato; its glide time is 0.5·g² seconds.
        double g = s["glide"];
        v["glide"] = mode == 0 || g <= 1e-4 ? 0f : GlideNorm(0.5 * g * g);

        v["outpan"] = s["pan"];
        v["volume"] = Math.Clamp(s["gain"] * 0.5f, 0f, 1f);          // Synth's ×0.25 against Volt's ×0.5
        return ToArray(v);
    }

    /// <summary>The Volt parameters (by id) for a Nota Bass patch.</summary>
    public static (string Id, float Value)[] FromBass(IReadOnlyDictionary<string, float> named)
    {
        var b = Merge(BassDefaults, named);
        var v = Base();

        // Bass's continuous shape is a position along sine → tri → saw → pulse; Volt picks the
        // wave below it and morphs toward the next by the rest.
        float pos = Math.Clamp(b["oscshape"], 0f, 1f) * 3f;
        int chip = Math.Min(3, (int)MathF.Floor(pos));
        v["osc1wave"] = chip switch { 0 => Sine, 1 => Tri, 2 => Saw, _ => Square };
        v["osc1shape"] = chip >= 3 ? 0f : pos - chip;
        v["osc1pw"] = b["oscpw"];
        v["osc1octave"] = OctaveNorm(Steps(b["oscoctave"], 4));      // Bass ±2 → Volt ±3
        v["osc1semi"] = b["oscsemi"];                                // ±12 in both
        v["osc1level"] = b["osclevel"];

        // The sub oscillator becomes Osc 2, one or two octaves under the note.
        int subWave = Math.Clamp((int)MathF.Round(b["subwave"] * 2f, MidpointRounding.AwayFromZero), 0, 2);
        v["osc2wave"] = subWave switch { 0 => Sine, 1 => Square, _ => Tri };
        v["osc2octave"] = OctaveNorm(b["suboctave"] >= 0.5f ? -2 : -1);
        v["osc2semi"] = 0.5f; v["osc2detune"] = 0.5f;
        v["osc2level"] = b["sublevel"];

        v["fil1type"] = b["filtype"];                                // LP / HP / BP / Notch in both
        v["fil1slope"] = b["filslope"];
        v["fil1freq"] = (float)Math.Clamp(Math.Log(30.0 * Math.Pow(600.0, b["filfreq"]) / 20.0) / Math.Log(900.0), 0.0, 1.0);
        v["fil1reso"] = b["filreso"];                                // same k law
        v["fil1env"] = b["filenv"]; v["fil1key"] = b["filkey"]; v["fil1lfo"] = b["fillfo"];
        v["fildrive"] = b["fildrive"]; v["drive"] = b["drive"];
        foreach (var k in new[] { "fattack", "fdecay", "fsustain", "frelease", "attack", "decay", "sustain", "release",
                                  "velamp", "velfilter", "glide", "bendrange", "modwheel", "outpan" })
            v[k] = b[k];                                             // same ranges

        // LFO: 0.05..30 Hz → Volt's 0.05..20; Bass's saw has no Volt shape, triangle is nearest.
        double hz = 0.05 * Math.Pow(600.0, b["lforate"]);
        v["lfo1rate"] = (float)Math.Clamp(Math.Log(hz / 0.05) / Math.Log(400.0), 0.0, 1.0);
        int lw = Math.Clamp((int)MathF.Round(b["lfowave"] * 4f, MidpointRounding.AwayFromZero), 0, 4);
        v["lfo1shape"] = lw switch { 0 => 0f, 1 or 2 => 1f / 3f, 3 => 2f / 3f, _ => 1f };
        if (Math.Abs(b["lfopitch"] - 0.5f) > 1e-4f)
        {
            // LFO → Pitch through the matrix: ±24 st × amount at full depth vs Bass's ±12.
            v["lfo1depth"] = 1f;
            v["mtx2_0"] = 0.5f + (b["lfopitch"] - 0.5f) * 0.5f;
        }
        v["wheellfo"] = 1f;                                          // Bass's wheel always opens the LFO

        v["unison"] = Math.Clamp(b["unison"] * 22f / 25f, 0f, 1f);    // up to 22 c vs Volt's 25
        v["mono"] = b["mono"] >= 0.5f ? 1f : 0f;
        v["legato"] = b["legato"] >= 0.5f ? 1f : 0f;
        v["volume"] = Math.Clamp(b["volume"] * MathF.Sqrt(2f), 0f, 1f); // Volt's amp pan sits 3 dB down at centre
        return ToArray(v);
    }

    // The neutral Volt frame both ports start from: Osc 1 alone into Filter 1 → Amp 1, no
    // Osc 2 / noise / Filter 2 / vibrato, nothing on velocity → cutoff unless set.
    private static Dictionary<string, float> Base() => new()
    {
        ["osc1wave"] = Saw, ["osc1octave"] = 0.5f, ["osc1semi"] = 0.5f, ["osc1detune"] = 0.5f, ["osc1level"] = 1f, ["osc1route"] = 0f,
        ["osc2level"] = 0f, ["osc2route"] = 0f, ["noise"] = 0f,
        ["fil1lfo"] = 0.5f, ["fil1tof2"] = 0f,
        ["amp1pan"] = 0.5f, ["amp1level"] = 1f, ["amp2level"] = 0f,
        ["vibamt"] = 0f, ["unison"] = 0f,
    };

    private static Dictionary<string, float> Merge(Dictionary<string, float> defaults, IReadOnlyDictionary<string, float> named)
    {
        var m = new Dictionary<string, float>(defaults);
        foreach (var (k, val) in named) m[k] = val;
        return m;
    }

    private static (string, float)[] ToArray(Dictionary<string, float> v)
    {
        var a = new (string, float)[v.Count];
        int i = 0;
        foreach (var (k, val) in v) a[i++] = (k, Math.Clamp(val, 0f, 1f));
        return a;
    }

    // A bipolar stepped knob (centre 0.5) spanning ±span/2 steps → its integer step.
    private static int Steps(float norm, int span) => (int)MathF.Round((norm - 0.5f) * span, MidpointRounding.AwayFromZero);
    private static float OctaveNorm(int octaves) => 0.5f + octaves / 6f;   // Volt's ±3

    // The same time in seconds on a different exponential range.
    private static float Rescale(float v, double lo, double hi, double lo2, double hi2)
    {
        double t = lo * Math.Pow(hi / lo, v);
        return (float)Math.Clamp(Math.Log(t / lo2) / Math.Log(hi2 / lo2), 0.0, 1.0);
    }

    // Volt's glide time is 0.005·120^g seconds; anything above zero must stay "on".
    private static float GlideNorm(double seconds)
        => (float)Math.Clamp(Math.Log(seconds / 0.005) / Math.Log(120.0), 0.002, 1.0);
}
