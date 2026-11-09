// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Several Session slots recording at once (specs/086, the acceptance case from #353): a
// vocalist and a cellist on two inputs record into two slots in one take; stopping one slot
// lands it and loops it back while the other keeps recording; both survive save and load with
// their own sources. Driven without a device: a pretend input device fed directly.

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MultiInputSessionTests
{
    private const double Sr = 48000.0;
    private const int Inputs = 4;
    private const int Beat = 24000;   // frames per beat at 120 BPM / 48 kHz

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in TwoSlotsOneTake()) yield return r;
        foreach (var r in RetryEverySlot()) yield return r;
    }

    private static float Level(int channel) => (channel + 1) / 8f;

    private static float[] Device(int frames)
    {
        var buf = new float[frames * Inputs];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < Inputs; c++) buf[f * Inputs + c] = Level(c);
        return buf;
    }

    private static IEnumerable<(bool, string)> TwoSlotsOneTake()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(0.0);
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, Sr);
        int vox = e.AddAudioTrack(), cello = e.AddAudioTrack();
        e.SetTrackInput(vox, InputPick.Mono(0));
        e.SetTrackInput(cello, InputPick.Mono(1));
        e.SetTrackArmed(vox, true);
        e.SetTrackArmed(cello, true);

        e.RecordSessionSlot(vox, 0);
        e.RecordSessionSlot(cello, 0);
        yield return (e.SessionSlotState(vox, 0) == 4 && e.SessionSlotState(cello, 0) == 4,
            $"two slots record at once (states {e.SessionSlotState(vox, 0)}, {e.SessionSlotState(cello, 0)})");
        yield return (e.RecordingTracks().Length == 2, $"…in one take of two lanes ({e.RecordingTracks().Length})");

        e.FeedInputForTest(Device(2 * Beat), 2 * Beat, Inputs);
        e.StopSessionRecordSlot(cello);   // the cello stops after two beats
        yield return (e.SessionSlotState(vox, 0) == 4, $"stopping the cello's slot leaves the vocal recording (state {e.SessionSlotState(vox, 0)})");
        yield return (e.SessionSlotState(cello, 0) is 1 or 2 or 3 && e.TryGetSessionAudioSlot(cello, 0, out var c1) && c1.SampleId != 0,
            $"…and the cello's take is in its slot (state {e.SessionSlotState(cello, 0)})");

        e.FeedInputForTest(Device(2 * Beat), 2 * Beat, Inputs);
        e.StopSessionRecordSlot(vox);
        yield return (e.RecordingTracks().Length == 0 && e.SessionSlotState(vox, 0) != 4, "stopping the last slot ends the take");

        bool vOk = e.TryGetSessionAudioSlot(vox, 0, out var v), cOk = e.TryGetSessionAudioSlot(cello, 0, out var c);
        yield return (vOk && cOk && v.SampleId != 0 && c.SampleId != 0 && v.SampleId != c.SampleId,
            "each slot holds its own take");
        if (!(vOk && cOk)) yield break;
        e.TryGetSampleInfo(v.SampleId, out var vi);
        e.TryGetSampleInfo(c.SampleId, out var ci);
        yield return (vi.Frames == 4 * Beat && ci.Frames == 2 * Beat, $"the vocal take is four beats, the cello's two ({vi.Frames / (double)Beat}, {ci.Frames / (double)Beat})");
        var vd = e.ReadSample(v.SampleId);
        var cd = e.ReadSample(c.SampleId);
        yield return (vi.Channels == 1 && ci.Channels == 1 && Math.Abs(vd[vd.Length / 2] - Level(0)) < 1e-5 && Math.Abs(cd[cd.Length / 2] - Level(1)) < 1e-5,
            "each holds its own input, recorded mono");

        // Saved and opened again: both slots, distinct sources.
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-multi-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProjectService.Save(doc, dir, e);
            using var back = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), back, dir);
            var ids = new List<int>();
            for (int i = 0; i < back.TrackCount; i++)
                if (back.TryGetTrackInfo(i, out var t) && !t.IsInstrument && !t.IsReturn && !t.IsGroup) ids.Add(t.Id);
            bool both = ids.Count == 2 && back.TryGetSessionAudioSlot(ids[0], 0, out var a) && back.TryGetSessionAudioSlot(ids[1], 0, out var b)
                        && a.SampleId != 0 && b.SampleId != 0 && a.SampleId != b.SampleId;
            yield return (both, "after save and load both slots hold their takes");
        }
        finally { Directory.Delete(dir, true); }
    }

    // Discard and Retry (FR-007): every slot the take still records starts again and keeps only
    // what follows the retry; a slot already stopped keeps its take.
    private static IEnumerable<(bool, string)> RetryEverySlot()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        e.SetLaunchQuant(0.0);
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, Sr);
        int vox = e.AddAudioTrack(), cello = e.AddAudioTrack(), bass = e.AddAudioTrack();
        e.SetTrackInput(vox, InputPick.Mono(0));
        e.SetTrackInput(cello, InputPick.Mono(1));
        e.SetTrackInput(bass, InputPick.Mono(2));
        e.SetTrackArmed(vox, true);
        e.SetTrackArmed(cello, true);
        e.SetTrackArmed(bass, true);

        e.RecordSessionSlot(vox, 0);
        e.RecordSessionSlot(cello, 0);
        e.FeedInputForTest(Device(Beat), Beat, Inputs);
        var r = e.RetryTake();
        yield return (r == RetryTakeResult.Session && e.RecordingTracks().Length == 2
                      && e.SessionSlotState(vox, 0) == 4 && e.SessionSlotState(cello, 0) == 4,
            $"retry restarts both slots of the take ({r}, {e.RecordingTracks().Length} lanes, states {e.SessionSlotState(vox, 0)}, {e.SessionSlotState(cello, 0)})");
        yield return (!e.TryGetSessionAudioSlot(vox, 0, out var v0) || v0.SampleId == 0,
            "…with nothing of the discarded attempt in a slot");

        e.FeedInputForTest(Device(2 * Beat), 2 * Beat, Inputs);
        e.StopSessionRecord();
        bool vOk = e.TryGetSessionAudioSlot(vox, 0, out var v), cOk = e.TryGetSessionAudioSlot(cello, 0, out var c);
        yield return (vOk && cOk && v.SampleId != 0 && c.SampleId != 0, "after the retry both slots land a take");
        if (!(vOk && cOk)) yield break;
        e.TryGetSampleInfo(v.SampleId, out var vi);
        e.TryGetSampleInfo(c.SampleId, out var ci);
        yield return (vi.Frames == 2 * Beat && ci.Frames == 2 * Beat,
            $"…each two beats, from the retry on ({vi.Frames / (double)Beat}, {ci.Frames / (double)Beat})");

        // A slot stopped before the retry keeps its take; the retry restarts only the rest.
        e.RecordSessionSlot(vox, 1);
        e.RecordSessionSlot(bass, 1);
        e.FeedInputForTest(Device(Beat), Beat, Inputs);
        e.StopSessionRecordSlot(bass);
        e.TryGetSessionAudioSlot(bass, 1, out var b1);
        e.FeedInputForTest(Device(Beat), Beat, Inputs);
        e.RetryTake();
        yield return (e.RecordingTracks().Length == 1 && e.SessionSlotState(vox, 1) == 4,
            $"retry after one slot stopped restarts only the one still recording ({e.RecordingTracks().Length} lanes)");
        yield return (b1.SampleId != 0 && e.TryGetSessionAudioSlot(bass, 1, out var b2) && b2.SampleId == b1.SampleId,
            "…and the stopped slot keeps its take");
        e.StopSessionRecord();
    }
}
