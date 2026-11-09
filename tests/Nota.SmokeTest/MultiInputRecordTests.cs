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
        foreach (var r in SeveralTracksOnePass()) yield return r;
        foreach (var r in LinkedGroupRecordsBoth()) yield return r;
        foreach (var r in DisarmFirstLaneRecordsOn()) yield return r;
        foreach (var r in LoopPassesPerTrack()) yield return r;
        foreach (var r in SkippedTracks()) yield return r;
        foreach (var r in InputSetupPickers()) yield return r;
        foreach (var r in SwitchedOffInputSkipped()) yield return r;
        foreach (var r in SetupKeyIsTheOpenedDevice()) yield return r;
    }

    // Input names and switches belong to the device the config opens: "System default" and a
    // device that isn't connected both mean the system's default input — never a shared
    // "default" entry, never the missing device's names on another one.
    private static IEnumerable<(bool, string)> SetupKeyIsTheOpenedDevice()
    {
        using var e = new NotaEngine();
        string saved = e.GetAudioConfig().InputUid;
        e.SetAudioInputDevice("");
        string viaDefault = e.EffectiveInputUid();
        e.SetAudioInputDevice("nota-test-unplugged-input");
        string viaMissing = e.EffectiveInputUid();
        var devices = NotaEngine.AudioInputDevices();
        string? chosen = devices.Count > 0 ? devices[^1].Uid : null;
        string viaChosen = "";
        if (chosen is not null) { e.SetAudioInputDevice(chosen); viaChosen = e.EffectiveInputUid(); }
        e.SetAudioInputDevice(saved);   // staged only: nothing was applied
        yield return (viaMissing == viaDefault && (devices.Count == 0 || viaDefault.Length > 0),
            $"System default and a missing device key on the default input ({(viaDefault.Length > 0 ? "a device" : "none")})");
        yield return (chosen is null || viaChosen == chosen, "a chosen connected device keys on itself");
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

    // ---- PR 2: one take records every armed audio track -------------------------------------

    private static (double Start, long Frames, int Channels, float L, float R)? Take(NotaEngine e, int track)
    {
        int last = -1;
        for (int i = 0; e.TryGetAudioClipInfo(track, i, out _); i++) last = i;
        if (last < 0 || !e.TryGetAudioClipInfo(track, last, out var clip) || !e.TryGetSampleInfo(clip.SampleId, out var info)) return null;
        var data = e.ReadSample(clip.SampleId);
        int mid = (int)(info.Frames / 2) * info.Channels;
        return (clip.StartBeat, info.Frames, info.Channels, data[mid], info.Channels > 1 ? data[mid + 1] : data[mid]);
    }

    private static int Clips(NotaEngine e, int track)
    {
        int n = 0;
        while (e.TryGetAudioClipInfo(track, n, out _)) n++;
        return n;
    }

    private static IEnumerable<(bool, string)> SeveralTracksOnePass()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);   // a sample rate for the take
        int[] tracks = { e.AddAudioTrack(), e.AddAudioTrack(), e.AddAudioTrack(), e.AddAudioTrack() };
        InputPick[] picks = { InputPick.Mono(0), InputPick.Mono(1), InputPick.Pair(2), InputPick.Mono(4) };
        for (int k = 0; k < tracks.Length; k++) { e.SetTrackInput(tracks[k], picks[k]); e.SetTrackArmed(tracks[k], true); }
        int lanes = e.TakeRecordTestArmed(DeviceFrames(), Inputs, Frames, 4.0);
        yield return (lanes == 4, $"one take records all four armed tracks ({lanes} lanes)");
        var got = tracks.Select(t => Take(e, t)).ToArray();
        yield return (got.All(g => g is not null), "each track has a clip of the take");
        if (got.Any(g => g is null)) yield break;
        yield return (got.All(g => g!.Value.Start == got[0]!.Value.Start && g.Value.Frames == got[0]!.Value.Frames),
            $"every clip starts on the same beat with the same length ({string.Join(", ", got.Select(g => $"{g!.Value.Start}/{g.Value.Frames}"))})");
        yield return (got[0]!.Value.Channels == 1 && got[1]!.Value.Channels == 1 && got[3]!.Value.Channels == 1 && got[2]!.Value.Channels == 2,
            $"mono inputs record mono files, the pair a stereo one ({string.Join(",", got.Select(g => g!.Value.Channels))})");
        yield return (Near(got[0]!.Value.L, Level(0)) && Near(got[1]!.Value.L, Level(1)) && Near(got[2]!.Value.L, Level(2))
                      && Near(got[2]!.Value.R, Level(3)) && Near(got[3]!.Value.L, Level(4)),
            "each clip holds its own input, nothing of the others");

        yield return (e.Undo() && tracks.All(t => Clips(e, t) == 0), "one Undo takes the whole take back, on every track");
        yield return (e.Redo() && tracks.All(t => Clips(e, t) == 1), "one Redo brings all four clips back");

        // The same input on two tracks: both record it.
        using var d = new NotaEngine();
        d.SetTestInputDevice(Inputs);
        d.RenderOffline(buf, 512, 48000.0);
        int a = d.AddAudioTrack(), b = d.AddAudioTrack();
        foreach (int t in new[] { a, b }) { d.SetTrackInput(t, InputPick.Mono(5)); d.SetTrackArmed(t, true); }
        lanes = d.TakeRecordTestArmed(DeviceFrames(), Inputs, Frames, 0.0);
        var ta = Take(d, a); var tb = Take(d, b);
        yield return (lanes == 2 && ta is { } x && tb is { } y && Near(x.L, Level(5)) && Near(y.L, Level(5)) && x.Frames == y.Frames,
            "two tracks on the same input both record it");
    }

    // Disarming the first-armed track mid-take lands its clip; the others record on to the
    // end (specs/086), the take doesn't stop with it.
    private static IEnumerable<(bool, string)> DisarmFirstLaneRecordsOn()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        e.SetTrackInput(a, InputPick.Mono(0));
        e.SetTrackInput(b, InputPick.Mono(1));
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        var dev = DeviceFrames();
        int lanes = e.TakeRecordTestArmedDisarm(dev, Inputs, Frames, 0.0, a, Frames / 2);
        var ta = Take(e, a); var tb = Take(e, b);
        yield return (lanes == 2 && ta is { } x && tb is { } y && Near(x.L, Level(0)) && Near(y.L, Level(1))
                      && y.Frames > x.Frames * 3 / 2 && y.Start == x.Start,
            $"disarming the first-armed track mid-take keeps the other recording ({lanes} lanes, {ta?.Frames} / {tb?.Frames} frames)");
    }

    // #528: tracks of a linked group arm together — and now record together.
    private static IEnumerable<(bool, string)> LinkedGroupRecordsBoth()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        e.SetTrackInput(a, InputPick.Mono(0));
        e.SetTrackInput(b, InputPick.Mono(1));
        int g = e.CreateGroup(new[] { a, b });
        e.SetTrackEditLink(g, true);
        TrackArm.Set(e, a, true, additive: false);   // arming one arms the linked other
        yield return (e.TryGetTrackInfo(IndexOf(e, b), out var bi) && bi.Armed != 0, "arming one linked track arms the other");
        int lanes = e.TakeRecordTestArmed(DeviceFrames(), Inputs, Frames, 0.0);
        yield return (lanes == 2 && Clips(e, a) == 1 && Clips(e, b) == 1, $"both linked tracks record (#528; {lanes} lanes)");
    }

    private static IEnumerable<(bool, string)> LoopPassesPerTrack()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        e.SetTrackInput(a, InputPick.Mono(0));
        e.SetTrackInput(b, InputPick.Pair(2));
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        e.SetLoop(true, 0.0, 1.0);   // one beat = 24000 frames at 120 BPM / 48 kHz
        int frames = 24000 * 3;
        var dev = new float[frames * Inputs];
        for (int f = 0; f < frames; f++) for (int c = 0; c < Inputs; c++) dev[f * Inputs + c] = Level(c);
        int lanes = e.TakeRecordTestArmed(dev, Inputs, frames, 0.0);
        int ka = e.TakeCount(a), kb = e.TakeCount(b);
        yield return (lanes == 2 && ka == 3 && kb == 3, $"three loop passes keep three takes on each track ({ka}, {kb})");
        e.SetLoop(false, 0.0, 1.0);
    }

    private static IEnumerable<(bool, string)> SkippedTracks()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(2);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);
        int far = e.AddAudioTrack(), near = e.AddAudioTrack();
        e.SetTrackInput(far, InputPick.Mono(5));
        e.SetTrackInput(near, InputPick.Mono(0));
        e.SetTrackArmed(far, true);
        e.SetTrackArmed(near, true);
        var dev = new float[Frames * 2];
        int lanes = e.TakeRecordTestArmed(dev, 2, Frames, 0.0);
        var skipped = e.RecordSkipped();
        yield return (lanes == 1 && Clips(e, near) == 1 && Clips(e, far) == 0, $"a track on an input the device lacks doesn't stop the others ({lanes} lane)");
        yield return (skipped.Length == 1 && skipped[0].TrackId == far && skipped[0].Reason == RecordSkipReason.InputUnavailable,
            $"…and the take says which track it skipped and why ({string.Join(",", skipped.Select(x => $"{x.TrackId}:{x.Reason}"))})");
    }

    // ---- PR 3: the Audio I/O setup ------------------------------------------------------------

    private static IEnumerable<(bool, string)> InputSetupPickers()
    {
        InputSetup? s = null;
        for (int c = 4; c < 8; c++) s = InputSetups.WithOn(s, c, false);
        s = InputSetups.WithName(s, InputPick.Mono(0), " Vox ");
        s = InputSetups.WithName(s, InputPick.Pair(2), "Gtr");
        var list = InputPickList.Build(8, c => c == 1 ? "Mic 2" : "", s, null);
        yield return (list.Count == 6 && list.All(e => e.State == InputPickState.Ok)
                      && list.Select(e => e.Pick).SequenceEqual(new[] { InputPick.Mono(0), InputPick.Mono(1), InputPick.Mono(2), InputPick.Mono(3), InputPick.Pair(0), InputPick.Pair(2) }),
            $"inputs 5–8 switched off: the picker lists inputs 1–4 and the pairs 1/2, 3/4 ({list.Count} entries)");
        yield return (list[0].Short == "Vox" && list[0].Label.Contains("Vox") && list[1].Label.Contains("Mic 2") && list[5].Short == "Gtr",
            $"the user's names and the device's show in the labels ({list[0].Short} / {list[1].Label} / {list[5].Short})");
        var withCur = InputPickList.Build(8, null, s, InputPick.Mono(5));
        var cur = withCur.FirstOrDefault(e => e.Pick == InputPick.Mono(5));
        yield return (cur.State == InputPickState.SwitchedOff, "a track on a switched-off input still sees its pick, marked switched off");
        var far = InputPickList.Build(2, null, null, InputPick.Mono(5)).FirstOrDefault(e => e.Pick == InputPick.Mono(5));
        yield return (far.State == InputPickState.NotOnDevice, "a pick the device lacks is marked so");
        var big = InputPickList.Build(32, null, null, null);
        yield return (big.Count(e => !e.Pick.IsStereo) == 32 && big.Count(e => e.Pick.IsStereo) == 16
                      && big.Count(e => !e.Pick.IsStereo) > InputPickList.SubmenuThreshold,
            "a 32-input device lists 32 inputs and 16 pairs (grouped under submenus past 16)");

        // Back to defaults: the setup disappears, so a never-configured device has no entry.
        var back = InputSetups.WithName(InputSetups.WithName(s, InputPick.Mono(0), ""), InputPick.Pair(2), " ");
        for (int c = 4; c < 8; c++) back = InputSetups.WithOn(back, c, true);
        yield return (back is null, "clearing every name and switching every input back on leaves no setup");

        // Kept in the settings file per device.
        var settings = new Settings();
        settings.InputSetups[InputSetups.KeyOf(AudioSystem.Wasapi, "uid-1", "")] = s!;
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var read = System.Text.Json.JsonSerializer.Deserialize<Settings>(json)!;
        var rs = read.InputSetups.GetValueOrDefault("device:uid-1");
        yield return (rs is not null && rs.Off.SequenceEqual(new[] { 4, 5, 6, 7 }) && InputSetups.NameOf(rs, InputPick.Mono(0)) == "Vox"
                      && InputSetups.NameOf(rs, InputPick.Pair(2)) == "Gtr",
            "the setup survives the settings file, keyed by the device");
    }

    private static IEnumerable<(bool, string)> SwitchedOffInputSkipped()
    {
        using var e = new NotaEngine();
        e.SetTestInputDevice(Inputs);
        var buf = new float[512 * 2];
        e.RenderOffline(buf, 512, 48000.0);
        int on = e.AddAudioTrack(), off = e.AddAudioTrack();
        e.SetTrackInput(on, InputPick.Mono(0));
        e.SetTrackInput(off, InputPick.Mono(1));
        e.SetTrackArmed(on, true);
        e.SetTrackArmed(off, true);
        e.SetDisabledInputs(new[] { 1 });
        int lanes = e.TakeRecordTestArmed(DeviceFrames(), Inputs, Frames, 0.0);
        var skipped = e.RecordSkipped();
        yield return (lanes == 1 && Clips(e, on) == 1 && Clips(e, off) == 0 && skipped.Length == 1 && skipped[0].Reason == RecordSkipReason.SwitchedOff,
            $"a track on a switched-off input records nothing and is reported; the other records ({lanes} lane)");
        yield return (e.GetTrackInput(off) == InputPick.Mono(1), "…and it keeps its pick");
        e.SetDisabledInputs(Array.Empty<int>());
        lanes = e.TakeRecordTestArmed(DeviceFrames(), Inputs, Frames, 16.0);
        yield return (lanes == 2, "switched on again, both tracks record");
    }

    private static int IndexOf(NotaEngine e, int trackId)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && t.Id == trackId) return i;
        return -1;
    }

    private static List<int> AudioTracks(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && !t.IsInstrument && !t.IsReturn && !t.IsGroup) ids.Add(t.Id);
        return ids;
    }
}
