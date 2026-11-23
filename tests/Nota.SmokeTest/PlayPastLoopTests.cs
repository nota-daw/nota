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
        e.StopTransport(); Apply(e);

        // A slower tempo keeps the sample position, so beat 6 at 120 BPM is beat ~3 at 60: back
        // before the loop end, the loop takes the playhead again.
        e.SetLoop(true, 0.0, 4.0); e.Seek(6.0); Apply(e);
        e.Play(); Apply(e);
        e.SetBpm(60); Apply(e);                  // ~3.0
        Render(e, 72000);                        // 1.5 beats at 60: 3 → 4 → wrap → ~0.5
        yield return (e.PositionBeats < 2.0, $"#981: a tempo change that brings the playhead back into the loop wraps again ({e.PositionBeats:F2})");
        e.StopTransport(); e.SetBpm(120); e.SetLoop(false, 0, 0); Apply(e);

        // Recording from past the loop end records straight through, where it is heard — not
        // folded into the loop as passes.
        using var r = new NotaEngine();
        r.SetTestInputDevice(2);
        r.RenderOffline(new float[512 * 2], 512, Sr);
        int t = r.AddAudioTrack();
        r.SetTrackArmed(t, true);
        r.SetLoop(true, 0.0, 1.0);
        int frames = 24000 * 3;                   // three beats at 120 BPM
        var dev = new float[frames * 2];
        for (int i = 0; i < dev.Length; i++) dev[i] = 0.25f;
        int lanes = r.TakeRecordTestArmed(dev, 2, frames, 4.0);
        bool one = r.TryGetAudioClipInfo(t, 0, out var clip) && !r.TryGetAudioClipInfo(t, 1, out _);
        yield return (lanes == 1 && one && Math.Abs(clip.StartBeat - 4.0) < 1e-6 && r.TakeCount(t) <= 1,
            $"#981: a take from past the loop end records straight through (start {clip.StartBeat:F2}, takes {r.TakeCount(t)})");
    }

    private static void Render(NotaEngine e, int frames)
    {
        for (int done = 0; done < frames; done += Chunk) Apply(e);
    }

    private static void Apply(NotaEngine e) => e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
}
