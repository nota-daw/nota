// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The sustain pedal on every built-in instrument (#99): with CC64 down a released key keeps
// sounding exactly as if it were still held, the pedal coming up releases it exactly as a
// note-off would have, a re-struck held key starts again, racks sustain through their chains,
// and drums ignore the pedal.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SustainPedalTests
{
    private const int Block = 512;
    private const int Sustain = MidiController.Sustain;

    private static float[] Render(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block * 2];
        var b = new float[Block * 2];
        for (int i = 0; i < blocks; i++) { e.RenderOffline(b, Block); Array.Copy(b, 0, all, i * Block * 2, Block * 2); }
        return all;
    }
    private static double Rms(float[] st)
    {
        double s = 0;
        foreach (var v in st) s += v * (double)v;
        return Math.Sqrt(s / Math.Max(1, st.Length));
    }
    private static double MaxDiff(float[] a, float[] b)
    {
        double d = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d;
    }

    // One take: the key goes down, then (after `pedal` decides) up; what follows is returned.
    // pedal: 0 = never, 1 = down before the release and still down, 2 = down, then up later.
    private static (float[] AfterRelease, float[] AfterPedalUp) Take(Func<NotaEngine, int> add, bool release, int pedal)
    {
        using var e = new NotaEngine();
        int t = add(e);
        Render(e, 2);
        if (pedal > 0) { e.SendTrackController(t, Sustain, 1f); Render(e, 1); }
        else Render(e, 1);
        e.TrackNoteOn(t, 60, 0.9f);
        Render(e, 16);
        if (release) e.TrackNoteOff(t, 60);
        var afterRelease = Render(e, 16);
        if (pedal == 2) e.SendTrackController(t, Sustain, 0f);
        else if (pedal == 0 && !release) e.TrackNoteOff(t, 60);   // the reference for "pedal up"
        var afterUp = Render(e, 24);
        return (afterRelease, afterUp);
    }

    public static IEnumerable<(bool, string)> Run()
    {
        var melodic = new (string Name, Func<NotaEngine, int> Add)[]
        {
            ("Nota Synth", e => e.AddInstrumentTrack()),
            ("Nota Physical", e => e.AddPhysicalSynthTrack()),
            ("Nota Wavetable", e => e.AddWavetableSynthTrack()),
            ("Nota Volt", e => e.AddVoltSynthTrack()),
            ("Nota Bass", e => e.AddBassSynthTrack()),
            ("Nota Operator", e => e.AddOperatorSynthTrack()),
            ("Nota Grain", e => e.AddGrainSynthTrack()),
            ("Nota Flux", e => e.AddFluxSynthTrack()),
            ("Nota Monolith", e => e.AddMonolithTrack()),
            ("Nota Pentad", e => e.AddPentadTrack()),
            ("Nota Consort", e => e.AddConsortTrack()),
            ("Instrument Rack (a Synth chain)", e => { int t = e.AddInstrumentRackTrack(); e.RackAddChain(t, 0); return t; }),
        };
        foreach (var (name, add) in melodic)
        {
            var held = Take(add, release: false, pedal: 0);      // key held throughout, released at "pedal up"
            var released = Take(add, release: true, pedal: 0);   // plain release, no pedal
            var pedalled = Take(add, release: true, pedal: 2);   // released under the pedal, pedal up later
            double hold = MaxDiff(pedalled.AfterRelease, held.AfterRelease);
            double up = MaxDiff(pedalled.AfterPedalUp, held.AfterPedalUp);
            double sounding = Rms(held.AfterRelease), cut = Rms(released.AfterRelease);
            yield return (sounding > 1e-4 && hold < 1e-6,
                $"{name}: a key released with the pedal down sounds exactly as if held (rms {sounding:F4}, diff {hold:E1}; without the pedal {cut:F4})");
            yield return (up < 1e-6, $"{name}: the pedal coming up releases it exactly like a note-off (diff {up:E1})");
        }

        // A key re-struck under the pedal starts again, and a key still physically down when the
        // pedal comes up keeps sounding until its own release.
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            Render(e, 2);
            e.SendTrackController(t, Sustain, 1f); Render(e, 1);
            e.TrackNoteOn(t, 60, 0.9f); Render(e, 8);
            e.TrackNoteOff(t, 60); Render(e, 4);
            e.TrackNoteOn(t, 60, 0.9f); Render(e, 8);   // re-struck, still down
            e.SendTrackController(t, Sustain, 0f);
            double still = Rms(Render(e, 16));
            e.TrackNoteOff(t, 60);
            Render(e, 40);
            double after = Rms(Render(e, 8));
            yield return (still > 1e-3 && after < still * 0.05,
                $"a re-struck key sounds past the pedal while held and stops on its own release (held {still:F4}, after {after:F5})");
        }

        // Drums ignore the pedal: a Drum Rack pad renders the same with and without it.
        Func<NotaEngine, int> drums = e => { int t = e.AddDrumRackTrack(); int p = e.RackAddChain(t, 0); e.RackSetChainTriggerNote(t, p, 60); return t; };
        var dPlain = Take(drums, release: true, pedal: 0);
        var dPedal = Take(drums, release: true, pedal: 1);
        double dd = MaxDiff(dPlain.AfterRelease, dPedal.AfterRelease);
        yield return (dd < 1e-6, $"Drum Rack ignores the sustain pedal (diff {dd:E1})");
    }
}
