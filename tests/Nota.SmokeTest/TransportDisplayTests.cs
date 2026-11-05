// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class TransportDisplayTests
{
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;

    public static IEnumerable<(bool, string)> Run()
    {
        yield return (Marshal.SizeOf<NotaTransportSnapshot>() == 64, "transport display: snapshot ABI size is 64 bytes");
        var s = new NotaTransportSnapshot
        {
            StartBeat = 8, PositionBeats = 8.2, Bpm = 120, Playing = 1,
            DurationSeconds = .1, ElapsedSeconds = .025
        };
        yield return (Near(TransportDisplayPosition.At(s), 8.05), "transport display: interpolates a rendered block between engine updates");
        s.ElapsedSeconds = .075;
        yield return (Near(TransportDisplayPosition.At(s), 8.15), "transport display: later display frame advances within the same block");
        s.ElapsedSeconds = 5;
        yield return (Near(TransportDisplayPosition.At(s), 8.2), "transport display: stalled audio or a delayed UI frame holds the exact rendered endpoint");
        s.Playing = 0; s.PositionBeats = 3; s.ElapsedSeconds = .01;
        yield return (Near(TransportDisplayPosition.At(s), 3), "transport display: stop/pause uses the authoritative position immediately");
        s.Playing = 1; s.StartBeat = 100; s.PositionBeats = 100.1; s.Bpm = 60;
        yield return (Near(TransportDisplayPosition.At(s), 100.01), "transport display: seek and tempo changes use the new block without old clock drift");
        s.ElapsedSeconds = -.5;
        yield return (Near(TransportDisplayPosition.At(s), 100), "transport display: a leading count-in cannot move the cursor early");
        s.DurationSeconds = 0;
        yield return (Near(TransportDisplayPosition.At(s), 100.1), "transport display: a zero-length playing portion uses its exact position");

        s = new() { StartBeat = 3.9, PositionBeats = 3.3, Bpm = 120, Playing = 1, Looping = 1,
            LoopStart = 3, LoopEnd = 4, DurationSeconds = .2, ElapsedSeconds = .075 };
        yield return (Near(TransportDisplayPosition.At(s), 3.05), "transport display: interpolates across a loop seam");
        s.StartBeat = 2; s.PositionBeats = 2.4; s.ElapsedSeconds = .05;
        yield return (Near(TransportDisplayPosition.At(s), 2.1), "transport display: playback before the loop start stays outside the loop");
        s.StartBeat = 3.9; s.PositionBeats = 4; s.ElapsedSeconds = .2;
        yield return (Near(TransportDisplayPosition.At(s), 3), "transport display: an exact loop endpoint wraps to the loop start");
        s.LoopEnd = s.LoopStart;
        yield return (Near(TransportDisplayPosition.At(s), 4), "transport display: an invalid loop range never divides by zero");

        using var engine = new NotaEngine();
        yield return (!engine.TryGetTransportSnapshot(out _), "transport snapshot: unavailable before the first block");
        var buffer = new float[1024];
        engine.SetBpm(120); engine.Seek(8); engine.Play();
        engine.RenderOffline(buffer, 512);
        bool got = engine.TryGetTransportSnapshot(out var native);
        yield return (got && native.IsPlaying && Near(native.StartBeat, 8) && Near(native.Bpm, 120)
            && Near(native.DurationSeconds, 512.0 / 44100) && Near(native.PositionBeats, engine.PositionBeats)
            && double.IsFinite(native.ElapsedSeconds), "transport snapshot: C ABI returns a coherent rendered block and native clock age");
        engine.StopTransport(); engine.RenderOffline(buffer, 512);
        yield return (engine.TryGetTransportSnapshot(out native) && !native.IsPlaying && Near(native.PositionBeats, 8)
            && Near(TransportDisplayPosition.At(native), 8), "transport snapshot: stop rewinds to the authoritative launch anchor");
        engine.SetBpm(60); engine.Seek(12); engine.Play(); engine.RenderOffline(buffer, 512);
        yield return (engine.TryGetTransportSnapshot(out native) && Near(native.StartBeat, 12) && Near(native.Bpm, 60)
            && Near(native.PositionBeats, 12 + 512.0 / 44100), "transport snapshot: a seek and tempo change replace the old block together");
        engine.SetBpm(120); engine.SetLoop(true, 3, 4); engine.Seek(3.9);
        engine.RenderOffline(new float[22050], 11025);
        yield return (engine.TryGetTransportSnapshot(out native) && native.Looping == 1 && Near(native.StartBeat, 3.9)
            && Near(native.LoopStart, 3) && Near(native.LoopEnd, 4) && Near(native.PositionBeats, 3.4),
            "transport snapshot: a block split at a loop seam retains its original start and wrapped end");
        engine.SetLoop(true, 1, 1.25); engine.Seek(1); engine.RenderOffline(buffer, 512);
        bool noLoopDrift = true;
        for (int i = 0; i < 2000; ++i)
        {
            double before = engine.PositionBeats;
            if (before >= 1.25 - .5 / 22050) before = 1;
            engine.RenderOffline(buffer, 512);
            noLoopDrift &= engine.TryGetTransportSnapshot(out native) && Near(native.StartBeat, before);
        }
        yield return (noLoopDrift, "transport snapshot: fractional-sample loop lengths do not accumulate display drift");
        engine.SetLoop(false, 0, 0); engine.StopTransport(); engine.Seek(6); engine.SetCountIn(1);
        engine.RenderOffline(buffer, 512); // apply stop before PlayWithCountIn checks the UI mirror
        engine.PlayWithCountIn(); engine.RenderOffline(buffer, 512);
        yield return (engine.TryGetTransportSnapshot(out native) && !native.IsPlaying && Near(native.PositionBeats, 6),
            "transport snapshot: count-in leaves the transport cursor stationary");
        engine.RenderOffline(new float[44100 * 4], 44100 * 2);
        yield return (engine.TryGetTransportSnapshot(out native) && native.IsPlaying && Near(native.StartBeat, 6)
            && native.DurationSeconds > 0 && native.DurationSeconds < .02,
            "transport snapshot: a block ending its count-in publishes only the playing portion");

        // The UI can overlap publication without waiting on the audio writer. The
        // payload must always describe one block, even during rapid offline rendering.
        engine.StopTransport(); engine.Seek(0); engine.Play(); engine.RenderOffline(buffer, 512);
        bool coherent = true;
        int reads = 0;
        var writer = Task.Run(() => { for (int i = 0; i < 4000; ++i) engine.RenderOffline(buffer, 512); });
        do
        {
            if (!engine.TryGetTransportSnapshot(out native)) continue;
            ++reads;
            coherent &= native.IsPlaying && Near(native.Bpm, 120) && native.Looping == 0
                && Near(native.PositionBeats - native.StartBeat, 512.0 / 22050);
        } while (!writer.IsCompleted);
        writer.GetAwaiter().GetResult();
        yield return (reads > 0 && coherent, "transport snapshot: concurrent reads never mix two published blocks");
    }
}
