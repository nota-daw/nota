// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Undo of a removed device (#109): undo puts the very device back where it was, with its
// parameter values and bypass, redo removes it again, and latency compensation follows both —
// a removed look-ahead device that comes back delays the other tracks again.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --device-undo`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DeviceRemoveUndoTests
{
    private const int Eq8 = 0, Compressor = 1, Reverb = 2, Arp = 0;
    private const int Lookahead = 7;   // Compressor look-ahead, ms (reported as latency)

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Restore()) yield return r;
        foreach (var r in Latency()) yield return r;
    }

    private static string Chain(NotaEngine e, int t)
    {
        var k = new int[e.TrackDeviceCount(t)];
        for (int i = 0; i < k.Length; i++) k[i] = e.TrackDeviceBuiltinKind(t, i);
        return string.Join(",", k);
    }

    private static IEnumerable<(bool, string)> Restore()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        e.AddBuiltinDevice(t, Eq8);
        int c = e.AddBuiltinDevice(t, Compressor);
        e.AddBuiltinDevice(t, Reverb);
        e.DeviceSetParam(t, c, 0, -30f);   // threshold
        e.DeviceSetParam(t, c, 1, 8f);     // ratio
        e.SetDeviceBypassed(t, c, true);
        int m = e.AddMidiEffect(t, Arp);
        e.MidiEffectSetParam(t, m, 1, 3f);
        float arpParam = e.MidiEffectGetParam(t, m, 1);
        string before = Chain(e, t);

        e.RemoveDevice(t, c);
        yield return (Chain(e, t) == "0,2", $"device undo: the Compressor is removed ({Chain(e, t)})");
        yield return (e.Undo() && Chain(e, t) == before, $"device undo: undo puts it back in place ({Chain(e, t)} vs {before})");
        yield return (Math.Abs(e.DeviceGetParam(t, c, 0) + 30f) < 1e-3 && Math.Abs(e.DeviceGetParam(t, c, 1) - 8f) < 1e-3 && e.DeviceBypassed(t, c),
            $"device undo: with its values and bypass (thr {e.DeviceGetParam(t, c, 0):F1}, ratio {e.DeviceGetParam(t, c, 1):F1}, bypassed {e.DeviceBypassed(t, c)})");
        yield return (e.Redo() && Chain(e, t) == "0,2", "device undo: redo removes it again");
        e.Undo();

        e.RemoveMidiEffect(t, m);
        yield return (e.TrackMidiEffectCount(t) == 0 && e.Undo() && e.TrackMidiEffectCount(t) == 1
                      && Math.Abs(e.MidiEffectGetParam(t, m, 1) - arpParam) < 1e-3,
            "device undo: a removed MIDI effect comes back with its values");
    }

    // Latency compensation: track B is held back by track A's look-ahead. Removing the
    // Compressor drops that; undo (and redo) must recompute it, not keep the last value.
    private static IEnumerable<(bool, string)> Latency()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int a = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a, 0);
        int c = e.AddBuiltinDevice(a, Compressor);
        e.DeviceSetParam(a, c, Lookahead, 10f);
        e.SetTrackMute(a, true);
        int b = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(b, 0);
        int clip = e.AddMidiClip(b, 0.0, 4.0);
        e.SetClipNotes(b, clip, new[] { new NotaNote(60, 0.0, 2.0, 1.0f) });
        int lat = e.TrackLatencySamples(a);

        int Onset()
        {
            e.StopTransport();
            e.Seek(0.0);
            var buf = new float[256 * 2];
            e.RenderOffline(buf, 256);    // drain the stop / seek
            e.RenderOffline(buf, 256);
            e.Play();
            for (int done = 0; done < 48000; done += 256)
            {
                e.RenderOffline(buf, 256);
                for (int i = 0; i < 256; i++)
                    if (Math.Abs(buf[i * 2]) > 1e-4f) { e.StopTransport(); return done + i; }
            }
            e.StopTransport();
            return -1;
        }

        int with = Onset();
        e.RemoveDevice(a, c);
        int without = Onset();
        e.Undo();
        int undone = Onset();
        e.Redo();
        int redone = Onset();
        yield return (lat > 0 && Math.Abs(with - without - lat) <= 2,
            $"device undo: the look-ahead delays the other track and removing it doesn't ({with} vs {without}, latency {lat})");
        yield return (Math.Abs(undone - with) <= 2, $"device undo: undo brings the compensation back ({undone} vs {with})");
        yield return (Math.Abs(redone - without) <= 2, $"device undo: redo drops it again ({redone} vs {without})");
    }
}
