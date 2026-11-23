// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// #981: Play from past the loop end plays on instead of jumping to the loop start. The loop
// wraps only a playhead that rolls into its end; a seek back before the end hands it back.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PlayPastLoopTests
{
    private const int Chunk = 256;
    private const double Sr = 48000.0;          // 120 BPM: 24 000 frames a beat

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetLoop(true, 0.0, 4.0);
        Apply(e);

        // Launched past the loop: plays on from there.
        e.Seek(8.0); Apply(e);
        e.Play();
        Render(e, 24000);                       // about one beat
        yield return (e.IsPlaying && e.PositionBeats > 8.5 && e.PositionBeats < 9.5,
            $"#981: Play past the loop end plays on ({e.PositionBeats:F2})");
        e.StopTransport(); Apply(e);

        // Launched before the loop end: still wraps.
        e.Seek(3.0); Apply(e);
        e.Play();
        Render(e, 48000);                       // about two beats: 3 → 4 → wrap → ~1
        yield return (e.PositionBeats < 2.0, $"#981: Play inside the loop still wraps ({e.PositionBeats:F2})");

        // While playing on past it, a seek back into the loop makes it wrap again.
        e.StopTransport(); e.Seek(8.0); Apply(e);
        e.Play(); Render(e, 12000);
        e.Seek(3.5); Render(e, 24000);          // 3.5 → 4 → wrap → ~0.5
        yield return (e.PositionBeats < 2.0, $"#981: a seek back into the loop wraps again ({e.PositionBeats:F2})");

        // The loop moved behind a rolling playhead lets it play on too.
        e.StopTransport(); e.Seek(2.0); Apply(e);
        e.Play(); Render(e, 24000);             // ~3
        e.SetLoop(true, 0.0, 1.0); Render(e, 24000);
        yield return (e.PositionBeats > 3.5, $"#981: a loop moved behind the playhead lets it play on ({e.PositionBeats:F2})");
        e.StopTransport(); e.SetLoop(false, 0, 0); Apply(e);
    }

    private static void Render(NotaEngine e, int frames)
    {
        for (int done = 0; done < frames; done += Chunk) Apply(e);
    }

    private static void Apply(NotaEngine e) => e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
}
