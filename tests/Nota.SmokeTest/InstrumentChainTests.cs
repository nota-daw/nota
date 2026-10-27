// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The instrument as a first-class device in the chain (specs/043).
// - set_builtin_instrument swaps a track's instrument for an empty Instrument Rack / Drum Rack
//   and back, clips and effects untouched, one undo step each.
// - The instrument on the shared device clipboard: copy / paste across tracks keeps every
//   param and its state (racks with their chains, pads and mappings), survives the source
//   track's removal, and refuses what can't take it.
// - transfer_device moves or copies an audio effect, a MIDI effect or the instrument to another
//   track in one undo step: a move takes the device's automation along, a copy takes none, the
//   instrument's move replaces the target's and removes the source track, a sidechain key on the
//   target itself is cleared.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --instrument-chain`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class InstrumentChainTests
{
    private const int Volt = 6, Keys = 16, InstrumentRack = 3, DrumRack = 4, Reverb = 2, Eq8 = 0, Compressor = 1, Arp = 0;
    private const int ReverbMix = 12;
    private static readonly int[] InstrumentKinds = { 0, 2, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Replace()) yield return r;
        foreach (var r in Clipboard()) yield return r;
        foreach (var r in Transfer()) yield return r;
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Abs(a));

    private static float Off(float cur) => Math.Abs(cur - 0.37f) < 1e-3f ? 0.71f : 0.37f;

    private static bool Exists(NotaEngine e, int id)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.Id == id) return true;
        return false;
    }

    private static int Clips(NotaEngine e, int id)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.Id == id) return ti.ClipCount;
        return -1;
    }

    private static string Chain(NotaEngine e, int t)
    {
        var k = new int[e.TrackDeviceCount(t)];
        for (int i = 0; i < k.Length; i++) k[i] = e.TrackDeviceBuiltinKind(t, i);
        return string.Join(",", k);
    }

    private static float[] InstParams(NotaEngine e, int t)
    {
        var p = new float[e.PluginParamCount(t, -1)];
        for (int i = 0; i < p.Length; i++) p[i] = e.PluginParamGet(t, -1, i);
        return p;
    }

    private static int FirstDiff(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -2;
        for (int i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return i;
        return -1;
    }

    // "target:device:param:points" for every lane, in order.
    private static string Lanes(NotaEngine e, int t)
    {
        var s = new List<string>();
        for (int i = 0; i < e.AutomationLaneCount(t); i++)
        {
            var l = e.AutomationLaneInfo(t, i);
            s.Add($"{(int)l.Target}:{l.DeviceIndex}:{l.ParamIndex}:{l.PointCount}");
        }
        return string.Join(" ", s);
    }

    private static IEnumerable<(bool, string)> Replace()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiClip(t, 0, 4);
        e.AddBuiltinDevice(t, Eq8);
        int tracks = e.TrackCount;

        bool rack = e.SetTrackBuiltinInstrument(t, InstrumentRack);
        yield return (rack && e.TrackInstrumentKind(t) == InstrumentRack && e.RackChainCount(t) == 0
                      && Clips(e, t) == 1 && Chain(e, t) == "0" && e.TrackCount == tracks,
            $"instrument chain: Volt → an empty Instrument Rack, clip and EQ kept (kind {e.TrackInstrumentKind(t)})");
        bool drum = e.SetTrackBuiltinInstrument(t, DrumRack);
        yield return (drum && e.TrackInstrumentKind(t) == DrumRack && e.RackChainCount(t) == 0 && Clips(e, t) == 1,
            "instrument chain: → an empty Drum Rack");
        bool back = e.SetTrackBuiltinInstrument(t, Volt);
        yield return (back && e.TrackInstrumentKind(t) == Volt && Chain(e, t) == "0", "instrument chain: → Volt again");
        yield return (e.Undo() && e.TrackInstrumentKind(t) == DrumRack && e.Undo() && e.TrackInstrumentKind(t) == InstrumentRack
                      && e.Undo() && e.TrackInstrumentKind(t) == Volt,
            "instrument chain: each swap is one undo step");

        int a = e.AddAudioTrack();
        yield return (!e.SetTrackBuiltinInstrument(a, InstrumentRack) && !e.SetTrackBuiltinInstrument(a, DrumRack),
            "instrument chain: an audio track takes no rack");
    }

    private static IEnumerable<(bool, string)> Clipboard()
    {
        using var e = new NotaEngine();
        var bad = new List<string>();
        foreach (int kind in InstrumentKinds)
        {
            int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
            if (!e.SetTrackBuiltinInstrument(a, kind)) { bad.Add($"kind {kind}: not set"); continue; }
            e.SetTrackBuiltinInstrument(b, 0);
            int pc = e.PluginParamCount(a, -1);
            for (int i = 0; i < pc; i++) e.PluginParamSet(a, -1, i, Off(e.PluginParamGet(a, -1, i)));
            var want = InstParams(e, a);
            var wantState = e.GetPluginState(a, -1);
            string name = e.DeviceName(a, -1);
            if (!e.CopyInstrument(a) || e.DeviceClipboardKind() != 3) { bad.Add($"{name}: not copied"); continue; }
            e.RemoveTrack(a);   // a cut: the clipboard keeps its own copy
            if (e.PasteInstrument(b) != 0) { bad.Add($"{name}: not pasted"); continue; }
            if (e.TrackInstrumentKind(b) != kind) bad.Add($"{name}: kind {e.TrackInstrumentKind(b)}");
            int diff = FirstDiff(want, InstParams(e, b));
            if (diff != -1) bad.Add($"{name} param {diff}");
            if (!wantState.AsSpan().SequenceEqual(e.GetPluginState(b, -1))) bad.Add($"{name} state");
            if (!(e.Undo() && e.TrackInstrumentKind(b) == 0)) bad.Add($"{name}: undo");
        }
        yield return (bad.Count == 0, $"instrument chain: every built-in instrument copies and pastes with its settings ({string.Join("; ", bad)})");

        // Racks copy themselves: chains, a pad's sample, macro mappings.
        int r = e.AddInstrumentRackTrack();
        int c0 = e.RackAddChain(r, Volt);
        e.RackAddChain(r, Keys);
        int m = e.RackAddMacroMapping(r, 0, c0, -1, 0, 0f, 1f);
        int dst = e.AddInstrumentTrack();
        e.CopyInstrument(r);
        int p = e.PasteInstrument(dst);
        yield return (m >= 0 && p == 0 && e.TrackInstrumentKind(dst) == InstrumentRack && e.RackChainCount(dst) == 2
                      && e.RackMappingCount(dst) == e.RackMappingCount(r),
            $"instrument chain: an Instrument Rack pastes with its chains and mappings ({e.RackChainCount(dst)} chains, {e.RackMappingCount(dst)} mappings)");

        int d = e.AddDrumRackTrack();
        string wav = Wav();
        e.RackAddSamplerChain(d, wav, 36, false);
        int dst2 = e.AddInstrumentTrack();
        e.CopyInstrument(d);
        yield return (e.PasteInstrument(dst2) == 0 && e.TrackInstrumentKind(dst2) == DrumRack && e.RackChainCount(dst2) == 1,
            "instrument chain: a Drum Rack pastes with its pad");
        File.Delete(wav);

        int audio = e.AddAudioTrack();
        yield return (e.PasteInstrument(audio) == -2 && Exists(e, audio), "instrument chain: an audio track refuses a pasted instrument");
        int fx = e.AddBuiltinDevice(audio, Eq8);
        e.CopyDevice(audio, fx);
        yield return (e.DeviceClipboardKind() == 1 && e.PasteInstrument(dst) == -1,
            "instrument chain: copying an effect replaces the instrument on the clipboard");
        yield return (!e.CopyInstrument(audio), "instrument chain: an audio track has no instrument to copy");
    }

    private static IEnumerable<(bool, string)> Transfer()
    {
        using var e = new NotaEngine();

        // An audio effect with a lane, moved: the lane goes along, the source's lanes after it shift.
        int t1 = e.AddAudioTrack(), t2 = e.AddAudioTrack();
        int rv = e.AddBuiltinDevice(t1, Reverb);
        int eq = e.AddBuiltinDevice(t1, Eq8);
        e.AddBuiltinDevice(t2, Compressor);
        int lr = e.AddAutomationLane(t1, AutomationTarget.DeviceParam, rv, ReverbMix);
        e.SetAutomationPoints(t1, lr, new[] { new AutomationPoint(0, 0.2f), new AutomationPoint(4, 0.8f) });
        int le = e.AddAutomationLane(t1, AutomationTarget.DeviceParam, eq, 0);
        e.SetAutomationPoints(t1, le, new[] { new AutomationPoint(0, 0.5f) });
        string lanes1 = Lanes(e, t1), lanes2 = Lanes(e, t2);
        int at = e.TransferDevice(t1, DeviceFamily.Audio, rv, t2, -1, false, out var f0);
        yield return (at == 1 && Chain(e, t1) == "0" && Chain(e, t2) == "1,2" && f0 == TransferFlags.None,
            $"instrument chain: a Reverb moves to the end of another track ({Chain(e, t1)} | {Chain(e, t2)})");
        yield return (Lanes(e, t1) == "2:0:0:1" && Lanes(e, t2) == $"2:1:{ReverbMix}:2",
            $"instrument chain: its lane goes along, the EQ's lane shifts ({Lanes(e, t1)} | {Lanes(e, t2)})");
        yield return (e.Undo() && Chain(e, t1) == "2,0" && Chain(e, t2) == "1" && Lanes(e, t1) == lanes1 && Lanes(e, t2) == lanes2,
            "instrument chain: one undo puts both tracks back");

        // A copy (⌥-drag): the source is untouched, no lane on the target.
        at = e.TransferDevice(t1, DeviceFamily.Audio, rv, t2, 0, true, out _);
        yield return (at == 0 && Chain(e, t1) == "2,0" && Chain(e, t2) == "2,1" && Lanes(e, t1) == lanes1 && Lanes(e, t2) == lanes2,
            $"instrument chain: a copy leaves the source and carries no lane ({Chain(e, t2)}, {Lanes(e, t2)})");
        yield return (e.TransferDevice(t1, DeviceFamily.Audio, 0, t1, -1, false, out _) == -1, "instrument chain: onto its own track → −1");
        yield return (e.TransferDevice(t1, DeviceFamily.Audio, 9, t2, -1, false, out _) == -1, "instrument chain: a bad index → −1");

        // A MIDI effect: between instrument tracks only.
        int i1 = e.AddInstrumentTrack(), i2 = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(i1, Volt); e.SetTrackBuiltinInstrument(i2, Keys);
        int arp = e.AddMidiEffect(i1, Arp);
        yield return (e.TransferDevice(i1, DeviceFamily.Midi, arp, t1, -1, false, out _) == -2 && e.TrackMidiEffectCount(i1) == 1,
            "instrument chain: a MIDI effect won't go to an audio track");
        yield return (e.TransferDevice(i1, DeviceFamily.Midi, arp, i2, -1, false, out _) == 0
                      && e.TrackMidiEffectCount(i1) == 0 && e.TrackMidiEffectCount(i2) == 1 && e.MidiEffectKind(i2, 0) == Arp,
            "instrument chain: an Arp moves between instrument tracks");

        // The instrument: replaces the target's, takes its lane, removes the source track.
        int pc = e.PluginParamCount(i1, -1);
        for (int i = 0; i < pc; i++) e.PluginParamSet(i1, -1, i, Off(e.PluginParamGet(i1, -1, i)));
        var want = InstParams(e, i1);
        e.AddMidiClip(i1, 0, 4);
        e.AddPluginAutomationLane(i1, -1, e.PluginParamId(i1, -1, 0));
        int tracks = e.TrackCount;
        int r = e.TransferDevice(i1, DeviceFamily.Instrument, 0, i2, -1, false, out var fi);
        yield return (r == 0 && !Exists(e, i1) && e.TrackCount == tracks - 1 && e.TrackInstrumentKind(i2) == Volt
                      && FirstDiff(want, InstParams(e, i2)) == -1 && fi.HasFlag(TransferFlags.SourceTrackRemoved),
            $"instrument chain: Volt moves onto the Keys track and its own track goes (kind {e.TrackInstrumentKind(i2)}, flags {fi})");
        bool laneMoved = false;
        for (int i = 0; i < e.AutomationLaneCount(i2); i++)
        {
            var l = e.AutomationLaneInfo(i2, i);
            laneMoved |= l.Target == AutomationTarget.PluginParam && l.DeviceIndex < 0;
        }
        yield return (laneMoved, $"instrument chain: the instrument's lane moves with it ({Lanes(e, i2)})");
        yield return (e.Undo() && Exists(e, i1) && Clips(e, i1) == 1 && e.TrackInstrumentKind(i1) == Volt && e.TrackInstrumentKind(i2) == Keys
                      && e.TrackCount == tracks,
            "instrument chain: one undo brings back both tracks and the clip");
        yield return (e.TransferDevice(i1, DeviceFamily.Instrument, 0, t1, -1, false, out _) == -2 && Exists(e, i1)
                      && e.TrackInstrumentKind(i1) == Volt,
            "instrument chain: an instrument won't go to an audio track");
        r = e.TransferDevice(i1, DeviceFamily.Instrument, 0, i2, -1, true, out var fc);
        yield return (r == 0 && Exists(e, i1) && e.TrackInstrumentKind(i1) == Volt && e.TrackInstrumentKind(i2) == Volt
                      && !fc.HasFlag(TransferFlags.SourceTrackRemoved),
            "instrument chain: an instrument copy leaves the source track");

        // CV links into the MIDI-effect chain follow it (review of #350): with two MIDI effects,
        // moving the first drops the link to it and the second's link shifts to index 0.
        int m1 = e.AddInstrumentTrack(), m2 = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(m1, Volt); e.SetTrackBuiltinInstrument(m2, Volt);
        e.AddMidiEffect(m1, Arp); e.AddMidiEffect(m1, 1);
        int lfo = e.ModulatorAdd(m1, 0);
        e.CvLinkAddToTarget(m1, lfo, 2, -1, 0, 0);
        e.CvLinkAddToTarget(m1, lfo, 2, -1, 1, 0);
        e.TransferDevice(m1, DeviceFamily.Midi, 0, m2, -1, false, out var fm);
        bool oneLeft = e.CvLinkCount(m1) == 1 && e.CvLinkDevice(m1, 0) == 0 && e.CvLinkTargetKind(m1, 0) == 2;
        yield return (oneLeft && fm.HasFlag(TransferFlags.LinksDropped),
            $"instrument chain: a moved MIDI effect's CV link is dropped and the next one's shifts (links {e.CvLinkCount(m1)}, device {(e.CvLinkCount(m1) > 0 ? e.CvLinkDevice(m1, 0) : -9)}, flags {fm})");

        // One lane per instrument parameter (review of #350): moving an instrument onto a track
        // that automates the same parameter replaces that lane instead of adding a second one.
        int a1 = e.AddInstrumentTrack(), a2 = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a1, Volt); e.SetTrackBuiltinInstrument(a2, Volt);
        string pid = e.PluginParamId(a1, -1, 0);
        int la = e.AddPluginAutomationLane(a1, -1, pid);
        e.SetAutomationPoints(a1, la, new[] { new AutomationPoint(0, 0.1f), new AutomationPoint(2, 0.9f), new AutomationPoint(4, 0.5f) });
        int lb = e.AddPluginAutomationLane(a2, -1, pid);
        e.SetAutomationPoints(a2, lb, new[] { new AutomationPoint(0, 0.3f) });
        int lanesBefore = e.AutomationLaneCount(a2);
        e.TransferDevice(a1, DeviceFamily.Instrument, 0, a2, -1, false, out _);
        yield return (e.AutomationLaneCount(a2) == lanesBefore && e.AutomationLaneInfo(a2, lb).PointCount == 3,
            $"instrument chain: a moved instrument's lane takes the place of the target's lane for the same parameter ({e.AutomationLaneCount(a2)} lanes, {e.AutomationLaneInfo(a2, lb).PointCount} points)");

        // A sidechain key on the target itself is cleared.
        int k = e.AddAudioTrack();
        int comp = e.AddBuiltinDevice(t2, Compressor);
        e.SetDeviceSidechainSource(t2, comp, k);
        int moved = e.TransferDevice(t2, DeviceFamily.Audio, comp, k, -1, false, out var fs);
        yield return (moved >= 0 && e.DeviceSidechainSource(k, moved) == -1 && fs.HasFlag(TransferFlags.SidechainCleared),
            $"instrument chain: a Compressor keyed from the target loses its key there (flags {fs})");
    }

    private static string Wav()
    {
        const int rate = 44100;
        var path = Path.Combine(Path.GetTempPath(), $"nota-043-{Guid.NewGuid():N}.wav");
        var x = new float[rate / 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = 0.5f * (float)Math.Sin(2 * Math.PI * 60 * i / rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }
}
