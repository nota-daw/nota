// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Group devices into a rack and back (specs/044, #328).
// - group_devices folds a contiguous run into an Audio Effect, MIDI Effect or Instrument Rack in
//   place, the instances themselves (the output is the same sample for sample), one undo step;
//   ungroup_rack is the reverse; every refusal leaves the chain alone.
// - Automation lanes, CV links, an Arp's CC target and sidechain keys follow the devices into
//   the rack chain (nested addresses) and back, keep acting, follow edits inside the rack, and
//   survive save / reload.
// - Several devices on the clipboard paste in one undo step, the instrument replacing the
//   target's, what a track can't take skipped.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --device-group`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DeviceGroupTests
{
    private const int Eq8 = 0, Compressor = 1, Reverb = 2, Delay = 3, Utility = 4, AudioRack = 5;
    private const int Volt = 6, Arp = 0, Chord = 1, Scale = 2, MidiRackKind = 6;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in AudioGroup()) yield return r;
        foreach (var r in Refusals()) yield return r;
        foreach (var r in LinksFollow()) yield return r;
        foreach (var r in InstrumentGroup()) yield return r;
        foreach (var r in MidiGroup()) yield return r;
        foreach (var r in Clipboard()) yield return r;
        foreach (var r in ChainNames()) yield return r;
        foreach (var r in ArpInMidiRack()) yield return r;
        foreach (var r in RackChainPaste()) yield return r;
        foreach (var r in RackRenders()) yield return r;
    }

    // FR-015: the new rack's chain is named after its first device (an Instrument Rack's after
    // its instrument: an empty name shows the instrument's).
    private static IEnumerable<(bool, string)> ChainNames()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiEffect(t, Chord);
        e.AddMidiEffect(t, Scale);
        e.AddBuiltinDevice(t, Compressor);
        e.AddBuiltinDevice(t, Delay);
        e.GroupDevices(t, -1, 0, false, 0, 2);
        e.GroupDevices(t, 0, 2, false, -1, 0);
        yield return (e.RackDevChainName(t, 0, 0) == e.RackDevChainDeviceName(t, 0, 0, 0) && e.RackDevChainName(t, 0, 0).Length > 0,
            $"chain name: an Audio Effect Rack's chain takes its first device's name ({e.RackDevChainName(t, 0, 0)})");
        yield return (e.MidiRackChainName(t, 0, 0) == e.MidiRackChainDeviceName(t, 0, 0, 0) && e.MidiRackChainName(t, 0, 0).Length > 0,
            $"chain name: a MIDI Effect Rack's chain takes its first device's name ({e.MidiRackChainName(t, 0, 0)})");
    }

    // An Arp whose CC lane drives an effect, grouped into a MIDI Effect Rack: it keeps its target
    // (read through the rack chain's CC calls), and save / reload brings it back (NestedCcRoutes).
    private static IEnumerable<(bool, string)> ArpInMidiRack()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        int arp = e.AddMidiEffect(t, Arp);
        e.AddBuiltinDevice(t, Eq8);
        int dl = e.AddBuiltinDevice(t, Delay);
        e.SetMidiEffectCcDest(t, arp, dl, 1);
        e.SetMidiEffectCcDepth(t, arp, 0.5f);
        int r = e.GroupDevices(t, 0, 1, false, -1, 0);
        var d = e.RackChainMidiCcDest(t, -1000, 0, 0);
        yield return (r == -1000 && d is { Device: 1, Chain: -1, Param: 1 } && Math.Abs(d.Value.Depth - 0.5f) < 1e-4f,
            $"arp in a rack: the grouped Arp keeps its CC target ({d})");
        // Set through the chain call: the Delay goes into an Audio Effect Rack, the Arp follows it.
        e.GroupDevices(t, -1, 0, false, 1, 1);
        d = e.RackChainMidiCcDest(t, -1000, 0, 0);
        yield return (d is { Device: 1, Chain: 0, Slot: 0, Param: 1 },
            $"arp in a rack: grouping its target too makes the target nested ({d})");
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-group-arp-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>()), dir, e);
            using var x = new NotaEngine();
            var w = ProjectService.Apply(ProjectService.Load(dir), x, dir);
            int xt = -1;
            for (int i = 0; i < x.TrackCount; i++) if (x.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) xt = ti.Id;
            var xd = x.RackChainMidiCcDest(xt, -1000, 0, 0);
            yield return (w.Count == 0 && xd is { Device: 1, Chain: 0, Slot: 0, Param: 1 } && Math.Abs(xd.Value.Depth - 0.5f) < 1e-4f,
                $"arp in a rack: save / reload brings the CC target back ({xd}; {string.Join("; ", w)})");
            yield return (x.RackChainMidiSetCcDest(xt, -1000, 0, 0, new NestedCcDest(0, -1, -1, 2, 1f))
                          && x.RackChainMidiCcDest(xt, -1000, 0, 0) is { Device: 0, Chain: -1, Param: 2 },
                "arp in a rack: the chain call sets a track-level target");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // An Arp going into an Instrument Rack whose target stays outside: the target shifts once.
        using var ie = new NotaEngine();
        int it = ie.AddInstrumentTrack();
        ie.SetTrackBuiltinInstrument(it, Volt);
        int ia = ie.AddMidiEffect(it, Arp);
        ie.AddBuiltinDevice(it, Eq8);
        int idl = ie.AddBuiltinDevice(it, Delay);
        ie.SetMidiEffectCcDest(it, ia, idl, 1);
        ie.GroupDevices(it, 0, 1, true, 0, 1);   // Arp, Volt, EQ-8 in; the Delay stays at index 0
        yield return (ie.RackChainMidiCount(it, 0) == 1 && ie.RackChainMidiCcDest(it, -1, 0, 0) is { Device: 0, Chain: -1, Param: 1 },
            $"arp in a rack: a target left outside an Instrument Rack group shifts once ({ie.RackChainMidiCcDest(it, -1, 0, 0)})");
    }

    // Several devices pasted into a rack chain: what the chain takes, in order, one undo step.
    private static IEnumerable<(bool, string)> RackChainPaste()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiEffect(t, Chord);
        e.AddBuiltinDevice(t, Eq8);
        e.AddBuiltinDevice(t, Delay);
        int a = e.AddAudioTrack();
        e.AddBuiltinDevice(a, Utility);
        e.WrapDeviceInRack(a, 0);   // an Audio Effect Rack holding the Utility
        e.CopyDevices(t, new[] { 0 }, true, new[] { 0, 1 });
        int n = e.RackDevChainPasteDevices(a, 0, 0, -1, 0, out int skipped, out int fm, out int ff);
        string kinds = string.Join(",", Enumerable.Range(0, e.RackDevChainDeviceCount(a, 0, 0)).Select(i => e.RackDevChainDeviceBuiltinKind(a, 0, 0, i)));
        yield return (n == 2 && skipped == 2 && ff == 0 && fm == -1 && kinds == $"{Eq8},{Delay},{Utility}",
            $"rack chain paste: the effects land in order, the MIDI effect and the instrument are skipped ({n}, {skipped}, {kinds})");
        e.Undo();
        yield return (e.RackDevChainDeviceCount(a, 0, 0) == 1, "rack chain paste: one undo takes the whole paste back");
    }

    // SC-003 for the MIDI Effect Rack and the Instrument Rack: a deterministic Sampler track
    // renders the same samples grouped and ungrouped.
    private static IEnumerable<(bool, string)> RackRenders()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_group_sine.wav");
        WavWriter.WriteSine(wav, seconds: 2.0, freq: 220, sampleRate: 44100);
        foreach (var (name, group) in new (string, Func<NotaEngine, int, int>)[]
                 {
                     ("MIDI Effect Rack", (x, t) => x.GroupDevices(t, 0, 2, false, -1, 0)),
                     ("Instrument Rack", (x, t) => x.GroupDevices(t, 0, 2, true, 0, 1)),
                 })
        {
            using var a = new NotaEngine();
            using var b = new NotaEngine();
            int ta = Build(a), tb = Build(b);
            var ba = new float[512 * 2];
            var bb = new float[512 * 2];
            a.Seek(0); a.Play(); b.Seek(0); b.Play();
            for (int i = 0; i < 8; i++) { a.RenderOffline(ba, 512, 44100); b.RenderOffline(bb, 512, 44100); }
            int r = group(a, ta);
            double worst = 0;
            float level = 0;
            for (int i = 0; i < 60; i++)
            {
                a.RenderOffline(ba, 512, 44100);
                b.RenderOffline(bb, 512, 44100);
                float ra = Rms(ba), rb = Rms(bb);
                level = Math.Max(level, rb);
                if (rb > 1e-4f) worst = Math.Max(worst, Math.Abs(20 * Math.Log10(Math.Max(ra, 1e-9f) / rb)));
            }
            a.StopTransport(); b.StopTransport();
            yield return (r != -2 && level > 1e-3f && worst <= 0.1,
                $"{name}: a grouped Sampler track sounds the same (worst block {worst:F3} dB, level {level:F3})");
        }

        int Build(NotaEngine x)
        {
            x.SetBpm(120); x.SetMetronome(false);
            int t = x.AddSamplerTrack(wav, 60, false);
            x.AddMidiEffect(t, Chord);
            x.AddMidiEffect(t, Scale);
            x.AddBuiltinDevice(t, Eq8);
            int clip = x.AddMidiClip(t, 0.0, 8.0);
            x.SetClipNotes(t, clip, new[] { new NotaNote(60, 0.0, 6.0, 0.9f), new NotaNote(64, 0.0, 6.0, 0.7f) });
            return t;
        }
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Abs(a));

    private static string Chain(NotaEngine e, int t)
    {
        var k = new int[e.TrackDeviceCount(t)];
        for (int i = 0; i < k.Length; i++) k[i] = e.TrackDeviceBuiltinKind(t, i);
        return string.Join(",", k);
    }

    private static string RackChain(NotaEngine e, int t, int rack)
    {
        var k = new int[e.RackDevChainDeviceCount(t, rack, 0)];
        for (int i = 0; i < k.Length; i++) k[i] = e.RackDevChainDeviceBuiltinKind(t, rack, 0, i);
        return string.Join(",", k);
    }

    private static string Midi(NotaEngine e, int t)
    {
        var k = new int[e.TrackMidiEffectCount(t)];
        for (int i = 0; i < k.Length; i++) k[i] = e.MidiEffectKind(t, i);
        return string.Join(",", k);
    }

    private static float Rms(float[] b)
    {
        double a = 0;
        foreach (var v in b) a += v * v;
        return (float)Math.Sqrt(a / b.Length);
    }

    // An audio track playing a steady two-tone through EQ-8, Compressor, Utility and Delay:
    // deterministic, so two engines render the same samples.
    private static int SteadyChain(NotaEngine e)
    {
        e.SetBpm(120); e.SetMetronome(false);
        int t = e.AddAudioTrack();
        const int frames = 44100 * 4;
        var pcm = new float[frames * 2];
        for (int i = 0; i < frames; i++)
            pcm[2 * i] = pcm[2 * i + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 220 * i / 44100f) + 0.2f * MathF.Sin(2 * MathF.PI * 1330 * i / 44100f);
        e.PasteAudioFrames(t, pcm, frames, 0.0, "tone");
        e.AddBuiltinDevice(t, Eq8);
        int c = e.AddBuiltinDevice(t, Compressor);
        e.DeviceSetParam(t, c, 0, -24f);
        e.AddBuiltinDevice(t, Utility);
        int d = e.AddBuiltinDevice(t, Delay);
        e.DeviceSetParam(t, d, 1, e.DeviceParamMin(t, d, 1) + 0.4f * (e.DeviceParamMax(t, d, 1) - e.DeviceParamMin(t, d, 1)));
        return t;
    }

    private static IEnumerable<(bool, string)> AudioGroup()
    {
        using var a = new NotaEngine();
        using var b = new NotaEngine();
        int ta = SteadyChain(a), tb = SteadyChain(b);
        var ba = new float[512 * 2];
        var bb = new float[512 * 2];
        a.Seek(0); a.Play(); b.Seek(0); b.Play();
        for (int i = 0; i < 20; i++) { a.RenderOffline(ba, 512, 44100); b.RenderOffline(bb, 512, 44100); }
        int r = a.GroupDevices(ta, -1, 0, false, 1, 3);
        yield return (r == 1 && Chain(a, ta) == $"{Eq8},{AudioRack}" && RackChain(a, ta, 1) == $"{Compressor},{Utility},{Delay}"
                      && Same(a.RackDevChainDeviceParamGet(ta, 1, 0, 0, 0), -24f),
            $"group: Compressor..Delay become one Audio Effect Rack in place, in order, with their settings ({Chain(a, ta)} / {RackChain(a, ta, 1)})");
        double worst = 0;
        float maxDiff = 0;
        for (int i = 0; i < 40; i++)
        {
            a.RenderOffline(ba, 512, 44100);
            b.RenderOffline(bb, 512, 44100);
            float ra = Rms(ba), rb = Rms(bb);
            if (rb > 1e-5f) worst = Math.Max(worst, Math.Abs(20 * Math.Log10(ra / rb)));
            for (int s = 0; s < ba.Length; s++) maxDiff = Math.Max(maxDiff, Math.Abs(ba[s] - bb[s]));
        }
        a.StopTransport(); b.StopTransport();
        yield return (worst <= 0.1 && Rms(bb) > 1e-3f, $"group: the track sounds the same after grouping (worst block {worst:F3} dB, max sample diff {maxDiff:E1})");

        yield return (a.Undo() && Chain(a, ta) == $"{Eq8},{Compressor},{Utility},{Delay}" && Same(a.DeviceGetParam(ta, 1, 0), -24f),
            "group: one undo restores the four devices");
        a.Redo();
        int n = a.UngroupRack(ta, DeviceFamily.Audio, 1, out var dropped);
        yield return (n == 3 && Chain(a, ta) == $"{Eq8},{Compressor},{Utility},{Delay}" && dropped == UngroupDropped.None,
            $"ungroup: the chain's devices come back in place ({Chain(a, ta)}, {dropped})");
        yield return (a.Undo() && Chain(a, ta) == $"{Eq8},{AudioRack}", "ungroup: one undo brings the rack back");
        yield return (a.WrapDeviceInRack(ta, 0) == 0 && Chain(a, ta) == $"{AudioRack},{AudioRack}", "wrap: ⌘G on one device is a one-device group");
    }

    private static IEnumerable<(bool, string)> Refusals()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiEffect(t, Arp);
        e.AddBuiltinDevice(t, Eq8);
        e.AddBuiltinDevice(t, Reverb);
        string before = Chain(e, t);
        yield return (e.GroupDevices(t, 0, 1, false, 0, 1) == (int)GroupRefusal.NotContiguous && Chain(e, t) == before && Midi(e, t) == $"{Arp}",
            "refused: MIDI and audio effects without the instrument");
        yield return (e.GroupDevices(t, -1, 0, true, 1, 1) == (int)GroupRefusal.NotContiguous, "refused: an audio run that doesn't start at the instrument");
        yield return (e.GroupDevices(t, -1, 0, false, 1, 5) == (int)GroupRefusal.BadRun, "refused: a run past the end");
        e.GroupDevices(t, -1, 0, false, 1, 1);   // Reverb into a rack
        yield return (e.GroupDevices(t, -1, 0, false, 0, 2) == (int)GroupRefusal.RackInRun && Chain(e, t) == $"{Eq8},{AudioRack}",
            "refused: a rack inside the run");
        e.RackDevAddChain(t, 1, -1);
        yield return (e.UngroupRack(t, DeviceFamily.Audio, 1, out _) == (int)UngroupRefusal.SeveralChains, "refused: ungroup of a rack with two chains");
        yield return (e.UngroupRack(t, DeviceFamily.Audio, 0, out _) == (int)UngroupRefusal.NotARack, "refused: ungroup of a device that isn't a rack");
        int rack = e.AddInstrumentRackTrack();
        yield return (e.GroupDevices(rack, -1, 0, true, -1, 0) == (int)GroupRefusal.InstrumentCantNest, "refused: an Instrument Rack into an Instrument Rack");
        int drums = e.AddDrumRackTrack();
        yield return (e.UngroupRack(drums, DeviceFamily.Instrument, 0, out _) == (int)UngroupRefusal.DrumRack, "refused: ungroup of a Drum Rack");
        int audio = e.AddAudioTrack();
        yield return (e.GroupDevices(audio, -1, 0, true, -1, 0) == (int)GroupRefusal.NotInstrumentTrack, "refused: the instrument of an audio track");
    }

    // Lanes, CV links, an Arp's CC target and a sidechain key on grouped devices follow them.
    private static IEnumerable<(bool, string)> LinksFollow()
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetMetronome(false);
        int src = e.AddAudioTrack();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        int arp = e.AddMidiEffect(t, Arp);
        e.AddBuiltinDevice(t, Eq8);                       // 0
        int dl = e.AddBuiltinDevice(t, Delay);            // 1
        int rv = e.AddBuiltinDevice(t, Reverb);           // 2
        int cp = e.AddBuiltinDevice(t, Compressor);       // 3
        e.AddBuiltinDevice(t, Utility);                   // 4 (after the run)
        const int lanePar = 1;
        float lo = e.DeviceParamMin(t, dl, lanePar), hi = e.DeviceParamMax(t, dl, lanePar);
        float v = lo + 0.73f * (hi - lo);
        int lane = e.AddAutomationLane(t, AutomationTarget.DeviceParam, dl, lanePar);
        e.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0, v), new AutomationPoint(16, v) });
        int utilLane = e.AddAutomationLane(t, AutomationTarget.DeviceParam, 4, 0);
        e.SetAutomationPoints(t, utilLane, new[] { new AutomationPoint(0, e.DeviceParamMin(t, 4, 0)) });
        int mod = e.ModulatorAdd(t, 0);
        int link = e.CvLinkAddToTarget(t, mod, 0, -1, rv, 0);
        e.SetMidiEffectCcDest(t, arp, dl, 0);
        e.SetDeviceSidechainSource(t, cp, src);

        int r = e.GroupDevices(t, -1, 0, false, 1, 3);
        var li = e.AutomationLaneInfo(t, lane);
        yield return (r == 1 && li.DeviceIndex == 1 && e.AutomationLanePath(t, lane) == (0, 0),
            $"links: the Delay's lane points into the rack (device {li.DeviceIndex}, path {e.AutomationLanePath(t, lane)})");
        yield return (e.AutomationLaneInfo(t, utilLane).DeviceIndex == 2 && e.AutomationLanePath(t, utilLane) == (-1, -1),
            $"links: the Utility's lane after the run shifts with it (device {e.AutomationLaneInfo(t, utilLane).DeviceIndex})");
        var cvp = e.CvLinkPath(t, link);
        yield return (e.CvLinkDevice(t, link) == 1 && cvp.TargetChain == 0 && cvp.TargetSlot == 1,
            $"links: the LFO's CV link targets the Reverb in the rack ({e.CvLinkDevice(t, link)}, {cvp})");
        yield return (e.MidiEffectCcDestDevice(t, arp) == 1 && e.MidiEffectCcDestPath(t, arp) == (0, 0),
            $"links: the Arp's CC lane drives the Delay in the rack ({e.MidiEffectCcDestDevice(t, arp)}, {e.MidiEffectCcDestPath(t, arp)})");
        yield return (e.RackChainSidechain(t, 1, 0, 2) is { SourceTrack: var s } && s == src,
            "links: the Compressor in the rack keeps its sidechain key");

        // The lane still drives the Delay: write the other value into it, play, read it back.
        e.SetMidiEffectCcDest(t, arp, -1, -1);   // the Arp would drive the same param
        e.RackDevChainDeviceParamSet(t, 1, 0, 0, lanePar, lo);
        var buf = new float[512 * 2];
        e.Seek(0); e.Play();
        for (int i = 0; i < 4; i++) e.RenderOffline(buf, 512, 44100);
        e.StopTransport();
        yield return (Same(e.RackDevChainDeviceParamGet(t, 1, 0, 0, lanePar), v),
            $"links: the nested lane plays (Delay param {e.RackDevChainDeviceParamGet(t, 1, 0, 0, lanePar):F3}, lane {v:F3})");
        e.SetMidiEffectCcDest(t, arp, 1, 0);   // a live setting, not an undo step: put it back by hand
        e.SetMidiEffectCcDestPath(t, arp, 0, 0);

        // An edit inside the rack: the Delay moves to the end of its chain, its lane follows.
        e.RackDevMoveChainDevice(t, 1, 0, 0, 2);
        yield return (e.AutomationLanePath(t, lane) == (0, 2) && e.CvLinkPath(t, link).TargetSlot == 0,
            $"links: a move inside the rack takes the lane along ({e.AutomationLanePath(t, lane)}, CV slot {e.CvLinkPath(t, link).TargetSlot})");
        e.Undo();
        yield return (e.AutomationLanePath(t, lane) == (0, 0), $"links: undo of the move puts the lane back ({e.AutomationLanePath(t, lane)})");

        // Save and reload: the nested references come back.
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-group-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
            using var d = new NotaEngine();
            var w = ProjectService.Apply(ProjectService.Load(dir), d, dir);
            int dt = -1, ds = -1;
            for (int i = 0; i < d.TrackCount; i++)
                if (d.TryGetTrackInfo(i, out var ti))
                {
                    if (ti.IsInstrument) dt = ti.Id;
                    else if (ds < 0) ds = ti.Id;
                }
            int dLane = -1, dLink = d.CvLinkCount(dt) - 1;
            for (int l = 0; l < d.AutomationLaneCount(dt); l++)
                if (d.AutomationLanePath(dt, l).Chain >= 0) dLane = l;
            yield return (w.Count == 0 && dLane >= 0 && d.AutomationLanePath(dt, dLane) == (0, 0) && Chain(d, dt) == $"{Eq8},{AudioRack},{Utility}",
                $"reload: the nested lane comes back ({w.Count} warnings: {string.Join("; ", w)})");
            yield return (dLink >= 0 && d.CvLinkPath(dt, dLink).TargetChain == 0 && d.CvLinkPath(dt, dLink).TargetSlot == 1,
                "reload: the CV link into the rack comes back");
            yield return (d.MidiEffectCcDestPath(dt, 0) == (0, 0) && d.MidiEffectCcDestDevice(dt, 0) == 1, "reload: the Arp's CC target in the rack comes back");
            yield return (d.RackChainSidechain(dt, 1, 0, 2) is { SourceTrack: var s2 } && s2 == ds, "reload: the keyed Compressor's source comes back");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Ungroup: everything back at track level, in place.
        int n = e.UngroupRack(t, DeviceFamily.Audio, 1, out _);
        yield return (n == 3 && Chain(e, t) == $"{Eq8},{Delay},{Reverb},{Compressor},{Utility}" && e.AutomationLanePath(t, lane) == (-1, -1)
                      && e.AutomationLaneInfo(t, lane).DeviceIndex == 1 && e.AutomationLaneInfo(t, utilLane).DeviceIndex == 4,
            $"ungroup: lanes return to the track's devices ({e.AutomationLaneInfo(t, lane).DeviceIndex}, {e.AutomationLaneInfo(t, utilLane).DeviceIndex})");
        yield return (e.CvLinkDevice(t, link) == 2 && e.CvLinkPath(t, link).TargetChain == -1 && e.MidiEffectCcDestDevice(t, arp) == 1
                      && e.MidiEffectCcDestPath(t, arp) == (-1, -1) && e.DeviceSidechainSource(t, 3) == src,
            "ungroup: the CV link, the Arp's CC target and the key return too");
        yield return (e.Undo() && e.AutomationLanePath(t, lane) == (0, 0) && Chain(e, t) == $"{Eq8},{AudioRack},{Utility}",
            "undo: ungroup steps back to the rack with its lanes inside");
    }

    private static IEnumerable<(bool, string)> InstrumentGroup()
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetMetronome(false);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiEffect(t, Chord);
        e.AddMidiEffect(t, Scale);
        e.AddBuiltinDevice(t, Eq8);
        e.AddBuiltinDevice(t, Reverb);
        int lane = e.AddAutomationLane(t, AutomationTarget.DeviceParam, 0, 1);
        int clip = e.AddMidiClip(t, 0.0, 8.0);
        e.SetClipNotes(t, clip, new[] { new NotaNote(57, 0.0, 8.0, 0.9f) });
        var buf = new float[512 * 2];
        e.Seek(0); e.Play();
        for (int i = 0; i < 40; i++) e.RenderOffline(buf, 512, 44100);
        float before = Rms(buf);
        // Scale (the last MIDI effect), the instrument and the EQ-8 (the first audio effect).
        int r = e.GroupDevices(t, 1, 1, true, 0, 1);
        e.RenderOffline(buf, 512, 44100);
        float after = Rms(buf);
        e.StopTransport();
        yield return (r == -1 && e.TrackInstrumentKind(t) == 3 && e.RackChainCount(t) == 1 && e.RackChainMidiCount(t, 0) == 1
                      && e.RackChainMidiKind(t, 0, 0) == Scale && e.RackChainInstrumentKind(t, 0) == Volt
                      && e.RackChainDeviceCount(t, 0) == 1 && e.RackChainDeviceBuiltinKind(t, 0, 0) == Eq8
                      && Midi(e, t) == $"{Chord}" && Chain(e, t) == $"{Reverb}",
            $"instrument group: an Instrument Rack whose chain is Scale → Volt → EQ-8; Chord and Reverb stay around it ({Midi(e, t)} / {Chain(e, t)})");
        double db = before > 1e-5f && after > 1e-5f ? 20 * Math.Log10(after / before) : 99;
        yield return (Math.Abs(db) < 1.5, $"instrument group: the held note plays on through the edit ({db:+0.00;-0.00} dB)");
        yield return (e.AutomationLaneInfo(t, lane).DeviceIndex == -1 && e.AutomationLanePath(t, lane) == (0, 0),
            "instrument group: the EQ-8's lane points into the Instrument Rack's chain");
        int n = e.UngroupRack(t, DeviceFamily.Instrument, 0, out _);
        yield return (n == 3 && e.TrackInstrumentKind(t) == Volt && Midi(e, t) == $"{Chord},{Scale}" && Chain(e, t) == $"{Eq8},{Reverb}"
                      && e.AutomationLaneInfo(t, lane).DeviceIndex == 0 && e.AutomationLanePath(t, lane) == (-1, -1),
            $"instrument ungroup: MIDI effects after the track's, the instrument back, effects before the track's ({Midi(e, t)} / {Chain(e, t)})");
        yield return (e.Undo() && e.TrackInstrumentKind(t) == 3 && e.Undo() && e.TrackInstrumentKind(t) == Volt && Chain(e, t) == $"{Eq8},{Reverb}",
            "instrument group: undo steps back through ungroup and group");
    }

    private static IEnumerable<(bool, string)> MidiGroup()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, Volt);
        e.AddMidiEffect(t, Chord);
        e.AddMidiEffect(t, Scale);
        e.AddMidiEffect(t, Arp);
        int r = e.GroupDevices(t, 0, 3, false, -1, 0);
        yield return (r == -1000 && Midi(e, t) == $"{MidiRackKind}" && e.MidiRackChainCount(t, 0) == 1 && e.MidiRackChainDeviceCount(t, 0, 0) == 3
                      && e.MidiRackChainDeviceKind(t, 0, 0, 2) == Arp,
            $"MIDI group: Chord, Scale, Arp become one MIDI Effect Rack ({Midi(e, t)})");
        int n = e.UngroupRack(t, DeviceFamily.Midi, 0, out _);
        yield return (n == 3 && Midi(e, t) == $"{Chord},{Scale},{Arp}", $"MIDI ungroup: the three come back in order ({Midi(e, t)})");
    }

    private static IEnumerable<(bool, string)> Clipboard()
    {
        using var e = new NotaEngine();
        int a = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a, Volt);
        e.AddMidiEffect(a, Chord);
        e.AddBuiltinDevice(a, Eq8);
        e.AddBuiltinDevice(a, Delay);
        e.AddBuiltinDevice(a, Reverb);
        e.DeviceSetParam(a, 2, 1, e.DeviceParamMin(a, 2, 1) + 0.61f * (e.DeviceParamMax(a, 2, 1) - e.DeviceParamMin(a, 2, 1)));
        int b = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(b, 0);
        e.AddBuiltinDevice(b, Utility);
        yield return (e.CopyDevices(a, new[] { 0 }, false, new[] { 0, 2 }) == 3 && e.DeviceClipboardKind() == 4 && e.DeviceClipboardCount() == 3,
            "clipboard: Chord, EQ-8 and Reverb copied together");
        int n = e.PasteDevices(b, -1, 0, out int skipped);
        yield return (n == 3 && skipped == 0 && Chain(e, b) == $"{Eq8},{Reverb},{Utility}" && Midi(e, b) == $"{Chord}"
                      && Same(e.DeviceGetParam(b, 1, 1), e.DeviceGetParam(a, 2, 1)),
            $"clipboard: pasted in order into their sections, settings kept ({Chain(e, b)} / {Midi(e, b)})");
        yield return (e.Undo() && Chain(e, b) == $"{Utility}" && Midi(e, b) == "", "clipboard: one undo takes the whole paste back");
        int audio = e.AddAudioTrack();
        n = e.PasteDevices(audio, -1, -1, out skipped);
        yield return (n == 2 && skipped == 1 && Chain(e, audio) == $"{Eq8},{Reverb}", $"clipboard: an audio track takes the effects and skips the MIDI effect ({n}, {skipped})");
        yield return (e.CopyDevices(a, new[] { 0 }, true, new[] { 1 }) == 3, "clipboard: the instrument copies with its effects");
        e.PasteDevices(b, -1, -1, out _);
        yield return (e.TrackInstrumentKind(b) == Volt && Midi(e, b) == $"{Chord}" && Chain(e, b) == $"{Utility},{Delay}",
            $"clipboard: pasting it replaces the target's instrument and adds the effects ({e.TrackInstrumentKind(b)} / {Chain(e, b)})");
        yield return (e.Undo() && e.TrackInstrumentKind(b) == 0 && Chain(e, b) == $"{Utility}", "clipboard: one undo restores the target's instrument");
    }
}
