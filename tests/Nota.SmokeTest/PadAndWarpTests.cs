// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Drum Rack pads and warp on/off.
// - A Sampler pad plays its sample through as a one-shot: a short note doesn't cut it (#312).
//   A looped pad still stops on note-off, so it can't ring forever.
// - A pad audition (TrackNoteOn) plays only the rack's own track, never the armed ones (#307).
// - Warp off → on restores the markers as they were, through undo and a save / load; a split
//   made with warp off seeds fresh markers for the half instead, after a save / load too (#310).
// - A one-shot pad whose loop turns on after its note-off releases instead of looping on.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --pad-warp`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PadAndWarpTests
{
    private const int Sr = 44100, Block = 512;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_pad_warp.wav");
        WavWriter.WriteSine(wav, seconds: 2.0, freq: 220, sampleRate: Sr);
        foreach (var r in OneShotPads(wav)) yield return r;
        foreach (var r in PadAudition(wav)) yield return r;
        foreach (var r in WarpToggle(wav)) yield return r;
    }

    private static double Rms(NotaEngine e, int blocks)
    {
        var buf = new float[Block * 2];
        double sum = 0;
        for (int b = 0; b < blocks; b++)
        {
            e.RenderOffline(buf, Block, Sr);
            foreach (var v in buf) sum += v * v;
        }
        return Math.Sqrt(sum / (blocks * Block * 2));
    }

    // A 2 s sample on a pad, struck with a ~20 ms note: still sounding half a second later.
    private static IEnumerable<(bool, string)> OneShotPads(string wav)
    {
        foreach (bool loop in new[] { false, true })
        {
            using var e = new NotaEngine();
            int t = e.AddDrumRackTrack();
            int p = e.RackAddSamplerChain(t, wav, 60, loop);
            e.RackSetChainTriggerNote(t, p, 60);
            e.TrackNoteOn(t, 60, 1f);
            Rms(e, 2);
            e.TrackNoteOff(t, 60);
            Rms(e, 40);                    // ~0.46 s on: well inside the sample, past any release
            double later = Rms(e, 4);
            if (!loop)
                yield return (later > 0.05, $"one-shot pad: a short note plays the sample through (rms {later:F3} after note-off)");
            else
                yield return (later < 1e-3, $"looped pad: note-off still stops it (rms {later:F5})");
        }

        // The loop switched on while a released one-shot still sounds: it releases rather
        // than looping on with nothing left to stop it.
        {
            using var e = new NotaEngine();
            int t = e.AddDrumRackTrack();
            int p = e.RackAddSamplerChain(t, wav, 60, false);
            e.RackSetChainTriggerNote(t, p, 60);
            int loopParam = -1;
            for (int i = 0; i < e.RackChainInstrumentParamCount(t, p); i++)
                if (e.RackChainInstrumentParamId(t, p, i) == "loopmode") loopParam = i;
            e.TrackNoteOn(t, 60, 1f);
            Rms(e, 2);
            e.TrackNoteOff(t, 60);
            Rms(e, 4);
            if (loopParam >= 0) e.RackChainInstrumentParamSet(t, p, loopParam, 0.5f);
            Rms(e, 350);                   // ~4 s: past the 2 s sample, so only a loop still sounds
            double later = Rms(e, 4);
            yield return (loopParam >= 0 && later < 1e-3, $"a released one-shot whose loop turns on fades out (rms {later:F5})");
        }

        // Switched to Mono with Glide while a released one-shot still sounds, the next note
        // glides on that voice and is held: it doesn't inherit the old note's key-up.
        {
            using var e = new NotaEngine();
            int t = e.AddDrumRackTrack();
            int p = e.RackAddSamplerChain(t, wav, 60, true);   // looped, so only a release ends it
            e.RackSetChainTriggerNote(t, p, 60);
            int ParamOf(string id)
            {
                for (int i = 0; i < e.RackChainInstrumentParamCount(t, p); i++)
                    if (e.RackChainInstrumentParamId(t, p, i) == id) return i;
                return -1;
            }
            int loopP = ParamOf("loopmode"), voiceP = ParamOf("voicemode"), glideP = ParamOf("glide");
            e.RackChainInstrumentParamSet(t, p, loopP, 0f);    // a one-shot for this first note
            e.TrackNoteOn(t, 60, 1f);
            Rms(e, 2);
            e.TrackNoteOff(t, 60);                              // held back: it plays through
            Rms(e, 1);
            e.RackChainInstrumentParamSet(t, p, voiceP, 0.5f);  // Mono …
            e.RackChainInstrumentParamSet(t, p, glideP, 0.3f);  // … with Glide
            e.RackChainInstrumentParamSet(t, p, loopP, 0.5f);   // looped again
            e.TrackNoteOn(t, 60, 1f);                           // legato onto the sounding voice
            Rms(e, 350);                                        // ~4 s, past the sample: only a held loop sounds
            double held = Rms(e, 4);
            yield return (loopP >= 0 && voiceP >= 0 && glideP >= 0 && held > 0.05,
                $"a note gliding onto a released one-shot's voice stays held (rms {held:F3})");
        }
    }

    private static IEnumerable<(bool, string)> PadAudition(string wav)
    {
        using var e = new NotaEngine();
        int synth = e.AddInstrumentTrack();
        e.SetTrackArmed(synth, true);
        int t = e.AddDrumRackTrack();
        int p = e.RackAddSamplerChain(t, wav, 60, false);
        e.RackSetChainTriggerNote(t, p, 72);   // the armed synth would sound on this note too
        e.SetTrackMute(t, true);               // hear only what reaches the armed track
        e.TrackNoteOn(t, 72, 1f);
        double armed = Rms(e, 8);
        e.TrackNoteOff(t, 72);
        yield return (armed < 1e-4, $"pad audition reaches only the rack's track, not an armed one (armed track rms {armed:F5})");
    }

    private static (double[] src, double[] beat) Markers(NotaEngine e, int t, int c)
    {
        var s = new double[16]; var b = new double[16];
        int n = e.GetClipWarpMarkers(t, c, s, b);
        return (s[..Math.Max(0, n)], b[..Math.Max(0, n)]);
    }

    private static bool Same((double[] src, double[] beat) a, (double[] src, double[] beat) b)
        => a.src.Length == b.src.Length && a.src.Zip(b.src).All(z => Math.Abs(z.First - z.Second) < 1e-6)
           && a.beat.Zip(b.beat).All(z => Math.Abs(z.First - z.Second) < 1e-6);

    private static IEnumerable<(bool, string)> WarpToggle(string wav)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        int c = e.AddAudioClip(t, wav, 0.0);
        e.SetClipWarp(t, c, true, 3);
        // A hand-placed middle marker, the kind of work a re-seed would throw away.
        e.SetClipWarpMarkers(t, c, new[] { 0.0, Sr * 0.5, Sr * 2.0 }, new[] { 0.0, 1.5, 4.0 });
        var placed = Markers(e, t, c);

        e.SetClipWarp(t, c, false, 3);
        e.SetClipWarp(t, c, true, 3);
        yield return (placed.src.Length == 3 && Same(Markers(e, t, c), placed),
            $"warp off → on restores the markers ({Markers(e, t, c).src.Length} markers)");
        e.Undo();   // back to off …
        bool offAgain = e.TryGetAudioClipInfo(t, c, out var ui) && ui.WarpEnabled == 0;
        e.Redo();   // … and on again
        yield return (offAgain && e.TryGetAudioClipInfo(t, c, out var ai) && ai.WarpEnabled != 0 && Same(Markers(e, t, c), placed),
            "warp toggle undo / redo keeps the markers");

        // Save with warp off, reload, switch it back on: the markers come back too.
        e.SetClipWarp(t, c, false, 3);
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-warp-off-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
            using var d = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), d, dir);
            int dt = -1;
            for (int i = 0; i < d.TrackCount; i++)
                if (d.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && ti.ClipCount == 1) dt = ti.Id;
            bool offAfterLoad = d.TryGetAudioClipInfo(dt, 0, out var li) && li.WarpEnabled == 0;
            d.SetClipWarp(dt, 0, true, 3);
            yield return (offAfterLoad && Same(Markers(d, dt, 0), placed), "warp-off markers survive a save / load and come back on re-enable");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Split with warp off: the right half's region moved, so re-enabling seeds markers for it.
        int right = e.SplitClip(t, c, 2.0);
        e.SetClipWarp(t, right, true, 3);
        var rm = Markers(e, t, right);
        bool inside = e.TryGetAudioClipInfo(t, right, out var ri) && rm.src.Length >= 2
                      && rm.src[0] >= ri.SourceOffsetFrames - 1 && rm.src[^1] <= ri.SourceOffsetFrames + ri.LengthFrames + 1;
        yield return (inside, $"a half split off with warp off gets markers for its own audio ({string.Join(", ", rm.src.Select(s => s.ToString("F0")))})");

        // The left half is still off with the whole clip's markers: saved and reloaded, they
        // mustn't come back on re-enable and play audio the split cut away.
        string dir2 = Path.Combine(Path.GetTempPath(), "nota-smoke-warp-split-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir2, e);
            using var d = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir2), d, dir2);
            int dt = -1;
            for (int i = 0; i < d.TrackCount; i++)
                if (d.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && ti.ClipCount == 2) dt = ti.Id;
            int left = -1;
            for (int k = 0; k < 2; k++)
                if (d.TryGetAudioClipInfo(dt, k, out var ki) && ki.WarpEnabled == 0) left = k;
            d.SetClipWarp(dt, left, true, 3);
            var lm = Markers(d, dt, left);
            bool within = d.TryGetAudioClipInfo(dt, left, out var lj) && lm.src.Length >= 2
                          && lm.src[^1] <= lj.SourceOffsetFrames + lj.LengthFrames + 1;
            yield return (left >= 0 && within,
                $"a half split with warp off, saved and reloaded, re-seeds on re-enable ({string.Join(", ", lm.src.Select(s => s.ToString("F0")))})");
        }
        finally { try { Directory.Delete(dir2, true); } catch { } }
    }
}
