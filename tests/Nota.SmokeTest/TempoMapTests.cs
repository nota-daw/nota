// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMapTests
{
    private const int Sr = 48000;
    private static float[] Render(NotaEngine e, int frames)
    {
        var output = new float[frames * 2]; e.RenderOffline(output, frames, Sr); return output;
    }
    private static double Energy(float[] x, double from, double to)
    {
        int a = Math.Max(0, (int)(from * Sr) * 2), b = Math.Min(x.Length, (int)(to * Sr) * 2);
        double sum = 0; for (int i = a; i < b; ++i) sum += x[i] * x[i];
        return sum;
    }
    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        var steps = new[] { new TempoPoint(0, 120), new TempoPoint(4, 60), new TempoPoint(8, 180) };
        e.SetTempoPoints(steps); Render(e, 1);
        yield return (Math.Abs(e.BeatToSeconds(8) - 6) < 1e-10 && Math.Abs(e.SecondsToBeat(3) - 5) < 1e-10,
            "tempo: stepped beat/seconds conversion includes all earlier sections");
        e.SetTempoPoints(new[] { new TempoPoint(0, 120, TempoShape.Linear), new TempoPoint(8, 240) });
        yield return (Math.Abs(e.BeatToSeconds(8) - 4 * Math.Log(2)) < 1e-7,
            "tempo: linear BPM integrates reciprocal tempo accurately");
        var curved = new[] { new TempoPoint(0, 70, TempoShape.Smooth, .15, .8, .85, .2), new TempoPoint(12, 210), new TempoPoint(24, 95) };
        e.SetTempoPoints(curved);
        bool roundTrip = true, monotonic = true; double previous = -1;
        for (int i = 0; i < 2400; ++i)
        {
            double beat = i / 80.0, seconds = e.BeatToSeconds(beat);
            roundTrip &= Math.Abs(e.SecondsToBeat(seconds) - beat) < 1e-9;
            monotonic &= seconds > previous; previous = seconds;
        }
        yield return (roundTrip && monotonic, "tempo: Bezier and trailing conversions are monotonic and invertible");
        using (var preview = e.CreateTempoPreview(curved))
            yield return (Enumerable.Range(0, 100).All(i => Math.Abs(preview.BeatToSeconds(i / 4.0) - e.BeatToSeconds(i / 4.0)) < 1e-12),
                "tempo: UI preview and playback use identical compiled timing");

        foreach (var invalid in new[]
        {
            new[] { new TempoPoint(1, 120) }, new[] { new TempoPoint(0, double.NaN) },
            new[] { new TempoPoint(0, 120), new TempoPoint(0, 100) },
            new[] { new TempoPoint(0, 120, TempoShape.Smooth, .9, 0, .1, 1) }
        })
        {
            bool rejected = false;
            try { e.SetTempoPoints(invalid); } catch (NotaEngineException) { rejected = true; }
            yield return (rejected && e.TempoPoints.SequenceEqual(curved), "tempo: invalid map rejected without changing project");
        }

        e.SetTempoPoints(new[] { new TempoPoint(0, 999) });
        yield return (Math.Abs(e.TempoAt(0) - 999) < 1e-12,
            "tempo: the persisted maximum matches the transport ABI maximum of 999 BPM");
        bool thousandRejected = false;
        try { e.SetTempoPoints(new[] { new TempoPoint(0, 1000) }); } catch (NotaEngineException) { thousandRejected = true; }
        yield return (thousandRejected, "tempo: 1000 BPM is rejected before it can create an unloadable project");

        e.SetTempoPoints(steps); e.ClearUndoHistory();
        var remaining = TempoMapping.DeleteSelected(steps, new HashSet<int> { 0, 1, 2 });
        e.SetTempoPoints(remaining);
        yield return (remaining.Length == 1 && remaining[0].Beat == 0 && e.Undo() && e.TempoPoints.SequenceEqual(steps) && !e.CanUndo,
            "tempo: multi-delete preserves initial point and undoes in one step");
        yield return (e.Redo() && e.TempoPoints.SequenceEqual(remaining), "tempo: multi-delete redoes as one operation");

        e.SetTempoPoints(curved);
        double fixedTime = e.BeatToSeconds(12);
        var mapped = TempoMapping.MapBar(e, curved, 16, fixedTime + 3, true)!;
        e.SetTempoPoints(mapped);
        yield return (Math.Abs(e.BeatToSeconds(12) - fixedTime) < 1e-8 && Math.Abs(e.BeatToSeconds(16) - fixedTime - 3) < 1e-8,
            "tempo: bar mapping preserves preceding curved phrase and reaches target seconds");
        var map2 = TempoMapping.MapBar(e, mapped, 20, fixedTime + 5, true)!;
        e.SetTempoPoints(map2);
        var map3 = TempoMapping.MapBar(e, map2, 24, fixedTime + 8, true)!;
        e.SetTempoPoints(map3);
        yield return (Math.Abs(e.BeatToSeconds(16) - fixedTime - 3) < 1e-8 && Math.Abs(e.BeatToSeconds(20) - fixedTime - 5) < 1e-8 &&
            Math.Abs(e.BeatToSeconds(24) - fixedTime - 8) < 1e-8 && e.TempoPoints.Count(p => p.Beat == 24) == 1,
            "tempo: successive sections retain anchors without duplicate markers");
        var noMarker = TempoMapping.MapBar(e, map3, 28, fixedTime + 10, false)!;
        e.SetTempoPoints(noMarker);
        yield return (noMarker.Length == map3.Length && Math.Abs(e.BeatToSeconds(28) - fixedTime - 10) < 1e-8,
            "tempo: Ctrl+Alt mapping adjusts timing without inserting a point");
        yield return (TempoMapping.MapBar(e, noMarker, 28, -1, true) is null, "tempo: crossing an anchor is rejected");

        e.SetTempoPoints(steps); e.Seek(0); e.Play(); Render(e, Sr * 3);
        yield return (Math.Abs(e.PositionBeats - 5) < 1e-8, "tempo: playback advances through step changes using integrated time");
        e.SetLoop(true, 3, 5); e.Seek(3); Render(e, Sr * 3);
        Render(e, 1); // the seam is consumed when the next sample is requested
        yield return (Math.Abs(e.PositionBeats - 3 - 1.0 / 24000) < 1e-6, "tempo: loop crossing a tempo change wraps at the correct physical sample");
        e.SetLoop(false, 0, 0); e.StopTransport(); Render(e, 1);
        e.SetTempoPoints(Array.Empty<TempoPoint>()); e.SetBpm(120); e.SetMetronome(true); e.Seek(.5); e.Play();
        var silent = Render(e, 512);
        yield return (silent.All(v => v == 0), "tempo: starting between beats does not emit an extra metronome click");
        e.Seek(1); var clicked = Render(e, 512);
        yield return (clicked.Any(v => Math.Abs(v) > .001), "tempo: seeking exactly onto a beat still emits its click");

        using (var countIn = new NotaEngine())
        {
            countIn.SetBpm(120); countIn.SetTimeSignature(6, 8); countIn.SetCountIn(1); countIn.PlayWithCountIn();
            Render(countIn, Sr * 5 / 4);
            bool heldForFiveEighths = Math.Abs(countIn.PositionBeats) < 1e-9 && Math.Abs(countIn.CountInBeats - 1) < .02;
            Render(countIn, Sr / 3);
            yield return (heldForFiveEighths && countIn.PositionBeats > 0 && countIn.CountInBeats == 0,
                "tempo: a 6/8 count-in lasts six eighth notes before playback starts");
        }

        using (var clock = new NotaEngine())
        {
            foreach (var (rate, bpm) in new[] { (44100, 90.0), (96000, 137.5), (48000, 120.0) })
            {
                clock.SetBpm(bpm); clock.Seek(2); clock.Play();
                clock.RenderOffline(new float[rate * 2], rate, rate);
                yield return (Math.Abs(clock.PositionBeats - 2 - bpm / 60) < 1e-9,
                    $"tempo: fixed clock follows BPM and sample-rate changes at {rate} Hz / {bpm} BPM");
                clock.StopTransport(); clock.RenderOffline(new float[2], 1, rate);
                yield return (Math.Abs(clock.PositionBeats - 2) < 1e-9,
                    "tempo: fixed clock retains its stop anchor after conversion-factor changes");
            }
            clock.SetTempoPoints(steps); clock.Seek(4); clock.Play(); Render(clock, Sr);
            yield return (Math.Abs(clock.PositionBeats - 5) < 1e-9,
                "tempo: fixed clock switches to mapped timing after sample-rate changes");
            clock.SetTempoPoints(Array.Empty<TempoPoint>());
            clock.SetBpm(150); clock.Seek(2); Render(clock, Sr);
            yield return (Math.Abs(clock.PositionBeats - 4.5) < 1e-9 && Math.Abs(clock.Bpm - 150) < 1e-9,
                "tempo: removing the map restores the updated fixed conversion factor and BPM");
            clock.SetLoop(true, 2, 4); clock.Seek(3); Render(clock, Sr * 3 / 5);
            yield return (Math.Abs(clock.PositionBeats - 2.5) < 1e-9,
                "tempo: fixed loop endpoints use the updated conversion factor");
        }

        using (var midi = new NotaEngine())
        {
            midi.SetTempoPoints(steps);
            int track = midi.AddInstrumentTrack(), clip = midi.AddMidiClip(track, 0, 12);
            midi.SetClipNotes(track, clip, new[] { new NotaNote(60, 5, .25, 1) });
            midi.Seek(0); midi.Play(); var sound = Render(midi, Sr * 4);
            yield return (Energy(sound, 0, 2.99) == 0 && Energy(sound, 3, 3.2) > .001,
                "tempo: MIDI note at beat 5 sounds at integrated time 3 seconds");
        }

        string temp = Path.Combine(Path.GetTempPath(), "nota-tempo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string wav = Path.Combine(temp, "reference.wav"), bundle = Path.Combine(temp, "roundtrip.nota");
            WavWriter.WriteSine(wav, .15, 330, Sr);
            using var audio = new NotaEngine();
            audio.SetBpm(120); Render(audio, 1);
            int track = audio.AddAudioTrack(); audio.AddAudioClip(track, wav, 2);
            audio.SetTempoPoints(new[] { new TempoPoint(0, 60), new TempoPoint(4, 90) });
            audio.TryGetClipInfo(track, 0, out var info);
            yield return (Math.Abs(audio.BeatToSeconds(info.StartBeat) - 1) < 1e-9,
                "tempo: mapping leaves reference audio at its original physical time");
            audio.Seek(0); audio.Play(); var sound = Render(audio, Sr * 2);
            yield return (Energy(sound, 0, .99) == 0 && Energy(sound, 1, 1.14) > .001 && Energy(sound, 1.3, 2) == 0,
                "tempo: reference audio plays at natural rate without moving or stretching");
            audio.SetTempoPoints(curved);
            var doc = ProjectService.Capture(audio, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, bundle, audio);
            using var restored = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(bundle), restored, bundle);
            yield return (restored.TempoPoints.SequenceEqual(curved) && Math.Abs(restored.BeatToSeconds(9) - audio.BeatToSeconds(9)) < 1e-12,
                "tempo: save/reload preserves shapes, handles and compiled timing");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
