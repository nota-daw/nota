// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Multi-channel input (specs/086, #353): an audio track takes any input of the capture device
// alone or an odd-aligned stereo pair; a take records exactly that input; a pick the device
// lacks is kept and reported; projects keep the pick, and older ones open with the input they
// had (Ext 1 / Ext 2 / Ext 1/2). Driven without a device: a pretend 8-input device and the
// capture ring fed directly (nota_take_record_test_n).

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MultiInputRecordTests
{
    private const int Inputs = 8;
    private const int Frames = 4096;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in AnyInput()) yield return r;
        foreach (var r in Availability()) yield return r;
        foreach (var r in SavedPicks()) yield return r;
    }

    // Input c carries the constant (c + 1) / 16 on every frame, so a clip says which input it holds.
    private static float Level(int channel) => (channel + 1) / 16f;

    private static float[] DeviceFrames()
    {
        var buf = new float[Frames * Inputs];
        for (int f = 0; f < Frames; f++)
            for (int c = 0; c < Inputs; c++) buf[f * Inputs + c] = Level(c);
        return buf;
    }

    private static (float L, float R, int Channels)? Recorded(NotaEngine e, int track)
    {
        int last = -1;
        for (int i = 0; e.TryGetAudioClipInfo(track, i, out _); i++) last = i;
        if (last < 0 || !e.TryGetAudioClipInfo(track, last, out var clip) || clip.SampleId == 0) return null;
        if (!e.TryGetSampleInfo(clip.SampleId, out var info)) return null;
        var data = e.ReadSample(clip.SampleId);
        int mid = (int)(info.Frames / 2) * info.Channels;   // away from any edge fade
        return (data[mid], info.Channels > 1 ? data[mid + 1] : data[mid], info.Channels);
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-5f;

    private static IEnumerable<(bool, string)> AnyInput()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        yield return (e.AudioInputChannelCount() == Inputs && e.AudioInputChannelName(2) == "Test 3",
            $"the pretend device lists {Inputs} inputs with their names ({e.AudioInputChannelCount()}, \"{e.AudioInputChannelName(2)}\")");

        int t = e.AddAudioTrack();
        yield return (e.SetTrackInput(t, InputPick.Mono(2)) && e.GetTrackInput(t) == InputPick.Mono(2), "input 3 alone can be picked");
        bool ok = e.TakeRecordTestN(t, DeviceFrames(), Inputs, Frames, 0.0);
        var mono = Recorded(e, t);
        yield return (ok && mono is { } m && Near(m.L, Level(2)) && Near(m.R, Level(2)),
            $"a take on input 3 holds input 3, in the middle ({mono?.L:F4} / {mono?.R:F4}, want {Level(2):F4})");

        int p = e.AddAudioTrack();
        e.SetTrackInput(p, InputPick.Pair(2));
        ok = e.TakeRecordTestN(p, DeviceFrames(), Inputs, Frames, 8.0);
        var pair = Recorded(e, p);
        yield return (ok && pair is { } s && Near(s.L, Level(2)) && Near(s.R, Level(3)),
            $"a take on the pair 3/4 holds input 3 left and input 4 right ({pair?.L:F4} / {pair?.R:F4})");

        yield return (!e.SetTrackInput(p, new InputPick(3, 2)) && e.GetTrackInput(p) == InputPick.Pair(2),
            "a stereo pair can't start on an even input (4/5); the pick stays");
        yield return (!e.SetTrackInput(p, new InputPick(0, 3)) && !e.SetTrackInput(p, new InputPick(64, 1)),
            "a width other than 1/2 and an input past 64 are refused");
        int inst = e.AddInstrumentTrack();
        yield return (!e.SetTrackInput(inst, InputPick.Mono(0)), "an instrument track has no hardware input");

        // The old three-value API reads what it can name and nothing else.
        e.SetTrackInputChannels(t, InputChannels.Mono2);
        yield return (e.GetTrackInput(t) == InputPick.Mono(1), "Ext 2 (the old pick) is input 2 alone");
        e.SetTrackInput(t, InputPick.Mono(5));
        yield return ((int)e.GetTrackInputChannels(t) == -1, "input 6 has no old three-value name (-1)");
    }

    private static IEnumerable<(bool, string)> Availability()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(2);
        int t = e.AddAudioTrack();
        e.SetTrackInput(t, InputPick.Mono(5));
        yield return (!e.TrackInputAvailable(t) && e.GetTrackInput(t) == InputPick.Mono(5),
            "input 6 on a 2-input device is kept and reads as not available");
        e.SetTestInputDevice(Inputs);
        yield return (e.TrackInputAvailable(t), "on an 8-input device the same pick is available again, unchanged");
    }

    private static IEnumerable<(bool, string)> SavedPicks()
    {
        using var src = new NotaEngine();
        int a = src.AddAudioTrack(), b = src.AddAudioTrack(), c = src.AddAudioTrack();
        src.SetTrackInput(a, InputPick.Mono(2));
        src.SetTrackInput(b, InputPick.Stereo12);
        src.SetTrackInput(c, InputPick.Mono(1));
        var doc = ProjectService.Capture(src, new TransportState(120.0, 1.0, false, false), new List<string>());
        var audio = doc.Tracks.Where(t => t.InputFirst is not null).ToList();
        yield return (audio.Count == 3 && audio[0].InputFirst == 3 && audio[0].InputWidth == 1 && audio[0].InputChannels is null,
            $"input 3 saves as InputFirst 3 / width 1 and no old value ({audio.ElementAtOrDefault(0)?.InputFirst}/{audio.ElementAtOrDefault(0)?.InputWidth}/{audio.ElementAtOrDefault(0)?.InputChannels})");
        yield return (audio.Count == 3 && audio[1].InputChannels == 0 && audio[2].InputChannels == 2,
            "picks the old format can name still write it (1/2 → 0, input 2 → 2), so 0.59 opens them the same");

        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-multi-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                var ids = AudioTracks(dst);
                yield return (ids.Count == 3 && dst.GetTrackInput(ids[0]) == InputPick.Mono(2) && dst.GetTrackInput(ids[1]) == InputPick.Stereo12,
                    "the picks survive save and load");
            }

            // As saved by 0.59.2: only the old value (0 pair, 1 input 1, 2 input 2) or none at all.
            int?[] old = { 1, 2, 0 };
            for (int i = 0; i < audio.Count; i++) { audio[i].InputFirst = null; audio[i].InputWidth = null; audio[i].InputChannels = old[i]; }
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                var ids = AudioTracks(dst);
                yield return (ids.Count == 3 && dst.GetTrackInput(ids[0]) == InputPick.Mono(0) && dst.GetTrackInput(ids[1]) == InputPick.Mono(1)
                              && dst.GetTrackInput(ids[2]) == InputPick.Stereo12,
                    "a 0.59 project opens with the same inputs (Ext 1 → In 1, Ext 2 → In 2, Ext 1/2 → In 1/2)");
            }
            foreach (var t in audio) t.InputChannels = null;   // as written before 0.58
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                var ids = AudioTracks(dst);
                yield return (ids.Count == 3 && ids.All(id => dst.GetTrackInput(id) == InputPick.Stereo12), "a project from before 0.58 keeps the stereo pair");
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static List<int> AudioTracks(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && !t.IsInstrument && !t.IsReturn && !t.IsGroup) ids.Add(t.Id);
        return ids;
    }
}
