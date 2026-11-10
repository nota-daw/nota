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
