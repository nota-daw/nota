// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RC envelopes (Consort, Pentad, Twin; EnvShape.h advanceRcEnv) follow a live Sustain change
// on a held note, as the analog generator does, whether the envelope runs the generator
// (Exp on, straight curves) or the shaped step (a bent decay, or Lin) — review of #296.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RcEnvSustainTests
{
    private const double Sr = 48000.0;
    private const int Block = 512;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var (name, curve, exp) in new[] { ("straight, Exp", 0.5f, 1f), ("a bent decay", 0.85f, 1f), ("Lin", 0.5f, 0f) })
        {
            var (before, after) = HeldLevels(curve, exp);
            double db = 20 * Math.Log10(Math.Max(after, 1e-9) / Math.Max(before, 1e-9));
            yield return (before > 1e-3 && db < -6,
                $"Consort, {name}: a held note follows Sustain 0.9 → 0.15 ({before:0.0000} → {after:0.0000}, {db:0.0} dB)");
        }
        // The generator doesn't note that a voice reached its sustain; switching to the shaped
        // step (Lin) right after Sustain went up must not take the held note for a decay that
        // lands on the new level at once (review of #600).
        var (held, first) = RaisedThenLin();
        double jump = 20 * Math.Log10(Math.Max(first, 1e-9) / Math.Max(held, 1e-9));
        yield return (held > 1e-3 && jump < 3,
            $"Pentad: Sustain 0.2 → 0.8 then Lin on held notes rises smoothly, no jump ({held:0.0000} → {first:0.0000}, {jump:+0.0;-0.0} dB over the next 0.1 s)");
    }

    private static NotaEngine Instrument(int kind, out int t, out Action<string, float> set)
    {
        var e = new NotaEngine();
        e.SetMetronome(false);
        var buf = new float[Block * 2];
        e.RenderOffline(buf, Block, Sr);
        int tr = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(tr, kind);
        t = tr;
        set = (id, v) =>
        {
            for (int i = 0; i < e.PluginParamCount(tr, -1); i++)
                if (e.PluginParamId(tr, -1, i) == id) { e.PluginParamSet(tr, -1, i, v); return; }
            throw new InvalidOperationException("no param " + id);
        };
        return e;
    }

    private static double Rms(NotaEngine e, int blocks)
    {
        var buf = new float[Block * 2];
        double s = 0; int n = 0;
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, Block, Sr);
            foreach (var x in buf) { s += x * x; n++; }
        }
        return Math.Sqrt(s / n);
    }

    // RMS of two notes held at Sustain 0.2, then over the 0.1 s after Sustain goes to 0.8 (with
    // a slow decay) and the envelope switches to Lin.
    private static (double held, double first) RaisedThenLin()
    {
        using var e = Instrument(14, out int t, out var P); // Pentad: the graphs track only its newest voice
        P("aattack", 0f); P("adecay", 0.05f); P("asustain", 0.2f); P("arelease", 0.3f);
        P("adcurve", 0.5f); P("aenvexp", 1f);
        e.TrackNoteOn(t, 57, 0.9f);
        e.TrackNoteOn(t, 64, 0.9f);   // the graphs show this one; 57 is a voice they never track
        Rms(e, 90);                   // both notes sit at Sustain 0.2, on the generator
        double held = Rms(e, 1);
        P("adecay", 0.9f);            // a slow decay: the level climbs to a raised Sustain slowly
        P("asustain", 0.8f);
        P("aenvexp", 0f);             // Lin: the shaped step from here on
        double first = Rms(e, 10);   // ~0.1 s: the slow decay has barely moved
        e.TrackNoteOff(t, 57);
        e.TrackNoteOff(t, 64);
        return (held, first);
    }

    // RMS of a held note settled at Sustain 0.9, then after Sustain drops to 0.15.
    private static (double before, double after) HeldLevels(float decayCurve, float exp)
    {
        using var e = new NotaEngine();
        e.SetMetronome(false);
        var buf = new float[Block * 2];
        e.RenderOffline(buf, Block, Sr);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 15);
        void P(string id, float v)
        {
            for (int i = 0; i < e.PluginParamCount(t, -1); i++)
                if (e.PluginParamId(t, -1, i) == id) { e.PluginParamSet(t, -1, i, v); return; }
            throw new InvalidOperationException("no Consort param " + id);
        }
        P("aattack", 0f); P("adecay", 0.05f); P("asustain", 0.9f); P("arelease", 0.3f);
        P("adcurve", decayCurve); P("aenvexp", exp);
        double Rms(int blocks)
        {
            double s = 0; int n = 0;
            for (int b = 0; b < blocks; b++)
            {
                e.RenderOffline(buf, Block, Sr);
                foreach (var x in buf) { s += x * x; n++; }
            }
            return Math.Sqrt(s / n);
        }
        e.TrackNoteOn(t, 57, 0.9f);
        Rms(90);                      // ~1 s: attack and decay are over, the note sits at Sustain
        double before = Rms(10);
        P("asustain", 0.15f);
        Rms(60);                      // ~0.6 s to settle on the new level
        double after = Rms(10);
        e.TrackNoteOff(t, 57);
        return (before, after);
    }
}
