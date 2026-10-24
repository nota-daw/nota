// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device copy / duplicate audit (#238) and undo of rack edits (#236).
// - Copy/paste of every built-in audio effect, the Audio Effect Rack and every MIDI effect
//   keeps all of its settings (every param, the state blob, bypass, sidechain mix, the Arp's
//   CC modulation), survives a cut of the source, and is one undo step.
// - Duplicate track keeps every built-in instrument's params + state.
// - Drum Rack / Instrument Rack edits (a sample dropped on a pad, pad note + name, chains,
//   chain devices, zones) are undoable, and a pad drop grouped as one gesture is one step.
// - ⌘G wraps a device in an Audio Effect Rack and ⌘⇧G takes it out again (#257), keeping the
//   device's settings and the chain order, one undo step each.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --device-copy`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DeviceCopyTests
{
    private const int AudioEffectRack = 5, LastBuiltinEffect = 26, LastMidiEffect = 5, Arp = 0;
    private static readonly int[] InstrumentKinds = { 0, 2, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in AudioEffects()) yield return r;
        foreach (var r in EffectRack()) yield return r;
        foreach (var r in MidiEffects()) yield return r;
        foreach (var r in DuplicateInstruments()) yield return r;
        foreach (var r in DrumRackUndo()) yield return r;
        foreach (var r in InstrumentRackUndo()) yield return r;
        foreach (var r in WrapInRack()) yield return r;
    }

    private static string Chain(NotaEngine e, int t)
    {
        var k = new int[e.TrackDeviceCount(t)];
        for (int i = 0; i < k.Length; i++) k[i] = e.TrackDeviceBuiltinKind(t, i);
        return string.Join(",", k);
    }

    private static IEnumerable<(bool, string)> WrapInRack()
    {
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        e.AddBuiltinDevice(t, 0);
        int c = e.AddBuiltinDevice(t, 1);
        e.AddBuiltinDevice(t, 2);
        e.DeviceSetParam(t, c, 0, -30f);
        int r = e.WrapDeviceInRack(t, c);
        yield return (r == c && Chain(e, t) == "0,5,2" && e.RackDevChainCount(t, r) == 1 && e.RackDevChainDeviceCount(t, r, 0) == 1
                      && e.RackDevChainDeviceBuiltinKind(t, r, 0, 0) == 1 && Same(e.RackDevChainDeviceParamGet(t, r, 0, 0, 0), -30f),
            $"wrap in rack: ⌘G puts the Compressor in a rack in its place, with its settings ({Chain(e, t)})");
        yield return (e.Undo() && Chain(e, t) == "0,1,2" && Same(e.DeviceGetParam(t, c, 0), -30f), "wrap in rack: undo takes it out again");
        e.Redo();
        e.RackDevAddChainDevice(t, r, 0, 3);   // a Delay after it in the chain
        int n = e.UnwrapRack(t, r);
        yield return (n == 2 && Chain(e, t) == "0,1,3,2" && Same(e.DeviceGetParam(t, c, 0), -30f),
            $"wrap in rack: ⌘⇧G puts the chain's devices back in order ({Chain(e, t)})");
        yield return (e.Undo() && Chain(e, t) == "0,5,2", "wrap in rack: undo brings the rack back");

        // A keyed device stays out: a rack chain has no sidechain input.
        int src = e.AddAudioTrack();
        int keyed = e.AddBuiltinDevice(t, 1);
        e.SetDeviceSidechainSource(t, keyed, src);
        yield return (e.WrapDeviceInRack(t, keyed) < 0 && Chain(e, t) == "0,5,2,1", $"wrap in rack: a device with a sidechain key isn't wrapped ({Chain(e, t)})");
        // A rack with two chains isn't unwrapped (which chain would win?).
        e.RackDevAddChain(t, r, -1);
        yield return (e.UnwrapRack(t, r) < 0 && Chain(e, t) == "0,5,2,1", $"wrap in rack: a rack with two chains isn't unwrapped ({Chain(e, t)})");

        // A device an Arp's CC lane drives stays out: the Arp addresses it by chain index.
        int it = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(it, 0);
        int arp = e.AddMidiEffect(it, Arp);
        e.AddBuiltinDevice(it, 0);
        int target = e.AddBuiltinDevice(it, 1);
        e.SetMidiEffectCcDest(it, arp, target, 0);
        yield return (e.WrapDeviceInRack(it, target) < 0 && Chain(e, it) == "0,1", $"wrap in rack: a device an Arp's CC targets isn't wrapped ({Chain(e, it)})");
        e.SetMidiEffectCcDest(it, arp, -1, -1);
        yield return (e.WrapDeviceInRack(it, target) == target, "wrap in rack: it wraps once the Arp lets go");
        // Unwrapping a rack the Arp drives, or one whose devices would shift the Arp's target, is refused.
        e.SetMidiEffectCcDest(it, arp, target, 0);
        yield return (e.UnwrapRack(it, target) < 0 && Chain(e, it) == "0,5", "wrap in rack: a rack an Arp's CC lane drives isn't unwrapped");
        int after = e.AddBuiltinDevice(it, 3);
        e.RackDevAddChainDevice(it, target, 0, 3);
        e.SetMidiEffectCcDest(it, arp, after, 0);
        yield return (e.UnwrapRack(it, target) < 0 && Chain(e, it) == "0,5,3", "wrap in rack: an unwrap that would shift the Arp's target is refused");
        e.SetMidiEffectCcDest(it, arp, -1, -1);
        yield return (e.UnwrapRack(it, target) == 2 && Chain(e, it) == "0,1,3,3", $"wrap in rack: with the Arp unhooked it comes out ({Chain(e, it)})");

        foreach (var x in WrapKeepsTail()) yield return x;
    }

    // Wrapping moves the device in as it is: it isn't prepared again, which reset a Reverb's tail
    // (and reallocated its delay lines under a playing graph).
    private static IEnumerable<(bool, string)> WrapKeepsTail()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        int rv = e.AddBuiltinDevice(t, 2);
        e.DeviceSetParam(t, rv, 12, 1f);   // Dry/Wet
        e.DeviceSetParam(t, rv, 19, 1f);   // Wet Only
        e.SetBpm(120); e.SetMetronome(false);
        int clip = e.AddMidiClip(t, 0.0, 4.0);
        e.SetClipNotes(t, clip, new[] { new NotaNote(60, 0.0, 0.25, 1.0f) });
        var buf = new float[512 * 2];
        static float Rms(float[] b) { double a = 0; foreach (var v in b) a += v * v; return (float)Math.Sqrt(a / b.Length); }
        e.Seek(0); e.Play();
        for (int i = 0; i < 30; i++) e.RenderOffline(buf, 512, 44100);
        e.DeviceSetParam(t, rv, 11, 1f);   // Freeze: the tail holds while the synth has released
        for (int i = 0; i < 30; i++) e.RenderOffline(buf, 512, 44100);
        float before = Rms(buf);
        e.WrapDeviceInRack(t, rv);
        e.RenderOffline(buf, 512, 44100);
        float after = Rms(buf);
        e.StopTransport();
        yield return (before > 1e-4f && after > before * 0.5f, $"wrap in rack: a ringing Reverb keeps its tail (rms {before:F4} → {after:F4})");
    }

    // A value well inside the param's range and away from its default.
    private static float Off(float min, float max, float cur)
    {
        float v = min + 0.37f * (max - min);
        return Math.Abs(v - cur) < 1e-4f * Math.Max(1f, Math.Abs(max - min)) ? min + 0.71f * (max - min) : v;
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Abs(a));

    private static float[] Params(NotaEngine e, int t, int d)
    {
        var p = new float[e.DeviceParamCount(t, d)];
        for (int i = 0; i < p.Length; i++) p[i] = e.DeviceGetParam(t, d, i);
        return p;
    }

    private static int FirstDiff(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -2;
        for (int i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return i;
        return -1;
    }

    private static IEnumerable<(bool, string)> AudioEffects()
    {
        using var e = new NotaEngine();
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        var bad = new List<string>();
        bool cutOk = true, undoOk = true;
        for (int kind = 0; kind <= LastBuiltinEffect; kind++)
        {
            if (kind == AudioEffectRack) continue;
            int d = e.AddBuiltinDevice(a, kind);
            if (d < 0) { bad.Add($"kind {kind}: not added"); continue; }
            for (int i = 0; i < e.DeviceParamCount(a, d); i++)
                e.DeviceSetParam(a, d, i, Off(e.DeviceParamMin(a, d, i), e.DeviceParamMax(a, d, i), e.DeviceGetParam(a, d, i)));
            e.SetDeviceBypassed(a, d, true);
            e.SetDeviceSidechainMix(a, d, 0.25f);
            var want = Params(e, a, d);
            var wantState = e.DeviceGetState(a, d);

            e.CopyDevice(a, d);
            e.RemoveDevice(a, d);   // a cut: the clipboard holds its own copy
            int n = e.TrackDeviceCount(b);
            int p = e.PasteDevice(b, -1);
            if (p != n) { bad.Add($"kind {kind}: paste index {p}"); cutOk = false; continue; }
            int diff = FirstDiff(want, Params(e, b, p));
            if (diff != -1) bad.Add($"{e.DeviceName(b, p)} param {diff}");
            if (!wantState.AsSpan().SequenceEqual(e.DeviceGetState(b, p))) bad.Add($"{e.DeviceName(b, p)} state");
            if (!e.DeviceBypassed(b, p) || !Same(e.DeviceSidechainMix(b, p), 0.25f)) bad.Add($"{e.DeviceName(b, p)} bypass/sidechain");
            if (!(e.Undo() && e.TrackDeviceCount(b) == n && e.Redo() && e.TrackDeviceCount(b) == n + 1)) undoOk = false;
        }
        yield return (bad.Count == 0, $"device copy: every built-in audio effect pastes with all its settings ({string.Join("; ", bad)})");
        yield return (cutOk, "device copy: a cut device still pastes");
        yield return (undoOk, "device copy: a paste is one undo step");

        // Paste after the selected device: inserted at that index, the rest shift up.
        int t = e.AddAudioTrack();
        e.AddBuiltinDevice(t, 0); e.AddBuiltinDevice(t, 2);
        int c = e.AddBuiltinDevice(t, 1);
        e.DeviceSetParam(t, c, 0, -30f);
        e.CopyDevice(t, c);
        int at = e.PasteDevice(t, 1);
        yield return (at == 1 && e.TrackDeviceBuiltinKind(t, 1) == 1 && e.TrackDeviceBuiltinKind(t, 2) == 2
                      && Same(e.DeviceGetParam(t, 1, 0), -30f),
            "device copy: a paste at an index lands there with its values");

        // Modular's ⌘D: a full clone right after the device, one undo step, clipboard untouched.
        int u = e.AddAudioTrack();
        e.AddBuiltinDevice(u, 0);
        int f = e.AddBuiltinDevice(u, 7);
        e.AddBuiltinDevice(u, 2);
        for (int i = 0; i < e.DeviceParamCount(u, f); i++)
            e.DeviceSetParam(u, f, i, Off(e.DeviceParamMin(u, f, i), e.DeviceParamMax(u, f, i), e.DeviceGetParam(u, f, i)));
        var fp = Params(e, u, f);
        int dup = e.DuplicateDevice(u, f);
        yield return (dup == f + 1 && Chain(e, u) == "0,7,7,2" && FirstDiff(fp, Params(e, u, dup)) == -1 && e.DeviceClipboardKind() == 1,
            $"device copy: duplicate puts a full clone right after the device ({Chain(e, u)})");
        yield return (e.Undo() && Chain(e, u) == "0,7,2", "device copy: a duplicate is one undo step");
    }

    private static IEnumerable<(bool, string)> EffectRack()
    {
        using var e = new NotaEngine();
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        int r = e.AddBuiltinDevice(a, AudioEffectRack);
        int ch = e.RackDevAddChain(a, r, -1);
        int cd = e.RackDevAddChainDevice(a, r, ch, 1);
        e.RackDevChainDeviceParamSet(a, r, ch, cd, 0, -24f);
        var want = e.DeviceGetState(a, r);
        e.CopyDevice(a, r);
        int p = e.PasteDevice(b, -1);
        yield return (p == 0 && e.RackDevChainCount(b, p) == e.RackDevChainCount(a, r)
                      && Same(e.RackDevChainDeviceParamGet(b, p, ch, cd, 0), -24f)
                      && want.AsSpan().SequenceEqual(e.DeviceGetState(b, p)),
            "device copy: an Audio Effect Rack pastes with its chains and their devices");
    }

    private static IEnumerable<(bool, string)> MidiEffects()
    {
        using var e = new NotaEngine();
        int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(a, 0); e.SetTrackBuiltinInstrument(b, 0);
        var bad = new List<string>();
        for (int kind = 0; kind <= LastMidiEffect; kind++)
        {
            int m = e.AddMidiEffect(a, kind);
            int pc = e.MidiEffectParamCount(a, m);
            for (int i = 0; i < pc; i++)
                e.MidiEffectSetParam(a, m, i, Off(e.MidiEffectParamMin(a, m, i), e.MidiEffectParamMax(a, m, i), e.MidiEffectGetParam(a, m, i)));
            if (kind == Arp) { e.SetMidiEffectCcDest(a, m, -1, 3); e.SetMidiEffectCcDepth(a, m, 0.6f); }
            e.SetMidiEffectBypassed(a, m, true);
            var want = new float[pc];
            for (int i = 0; i < pc; i++) want[i] = e.MidiEffectGetParam(a, m, i);
            e.CopyMidiEffect(a, m);
            int p = e.PasteMidiEffect(b, -1);
            if (p < 0) { bad.Add($"kind {kind}: not pasted"); continue; }
            for (int i = 0; i < pc; i++)
                if (!Same(want[i], e.MidiEffectGetParam(b, p, i))) { bad.Add($"{e.MidiEffectName(b, p)} param {i}"); break; }
            if (!e.MidiEffectBypassed(b, p)) bad.Add($"{e.MidiEffectName(b, p)} bypass");
            if (kind == Arp && (e.MidiEffectCcDestParam(b, p) != 3 || !Same(e.MidiEffectCcDepth(b, p), 0.6f))) bad.Add("Arp CC modulation");
        }
        yield return (bad.Count == 0, $"device copy: every MIDI effect pastes with all its settings ({string.Join("; ", bad)})");
        int audio = e.AddAudioTrack();
        yield return (e.PasteMidiEffect(audio, -1) < 0, "device copy: a MIDI effect doesn't paste onto an audio track");
        int arp = 0;
        float rate = e.MidiEffectGetParam(a, arp, 1);
        int md = e.DuplicateMidiEffect(a, arp);
        yield return (md == 1 && e.MidiEffectKind(a, 1) == Arp && Same(e.MidiEffectGetParam(a, 1, 1), rate)
                      && e.MidiEffectCcDestParam(a, 1) == 3 && e.MidiEffectBypassed(a, 1),
            "device copy: duplicating a MIDI effect keeps its settings and CC modulation");
    }

    private static IEnumerable<(bool, string)> DuplicateInstruments()
    {
        using var e = new NotaEngine();
        var bad = new List<string>();
        foreach (int kind in InstrumentKinds)
        {
            int t = e.AddInstrumentTrack();
            if (!e.SetTrackBuiltinInstrument(t, kind)) { bad.Add($"kind {kind}: not set"); continue; }
            int pc = e.PluginParamCount(t, -1);
            for (int i = 0; i < pc; i++)
                e.PluginParamSet(t, -1, i, Off(0f, 1f, e.PluginParamGet(t, -1, i)));
            var want = new float[pc];
            for (int i = 0; i < pc; i++) want[i] = e.PluginParamGet(t, -1, i);
            var wantState = e.GetPluginState(t, -1);
            int d = e.DuplicateTrack(t);
            string name = e.DeviceName(t, -1);
            if (d < 0 || e.PluginParamCount(d, -1) != pc) { bad.Add($"{name}: not duplicated"); continue; }
            for (int i = 0; i < pc; i++)
                if (!Same(want[i], e.PluginParamGet(d, -1, i))) { bad.Add($"{name} param {i}"); break; }
            if (!wantState.AsSpan().SequenceEqual(e.GetPluginState(d, -1))) bad.Add($"{name} state");
        }
        yield return (bad.Count == 0, $"device copy: duplicate track keeps every instrument's settings ({string.Join("; ", bad)})");
    }

    private static string Wav(float hz)
    {
        const int rate = 44100;
        var path = Path.Combine(Path.GetTempPath(), $"nota-pad-{Guid.NewGuid():N}.wav");
        var x = new float[rate / 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = 0.5f * (float)Math.Sin(2 * Math.PI * hz * i / rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }

    private static IEnumerable<(bool, string)> DrumRackUndo()
    {
        using var e = new NotaEngine();
        int t = e.AddDrumRackTrack();
        string kick = Wav(60), snare = Wav(200);

        // An empty pad: the drop (add + note + name, one gesture) undoes in one step.
        e.BeginUndoGroup();
        int p = e.RackAddSamplerChain(t, kick, 36, false);
        e.RackSetChainTriggerNote(t, p, 36);
        e.RackSetChainName(t, p, "Kick");
        e.EndUndoGroup();
        yield return (e.RackChainCount(t) == 1 && e.RackChainName(t, 0) == "Kick", "rack undo: a sample dropped on an empty pad");
        yield return (e.Undo() && e.RackChainCount(t) == 0, "rack undo: one undo empties the pad again");
        yield return (e.Redo() && e.RackChainCount(t) == 1 && e.RackChainTriggerNote(t, 0) == 36 && e.RackChainName(t, 0) == "Kick",
            "rack undo: redo brings the sample back on its note with its name");

        // A replaced pad gets its previous sample back with its level, name and devices.
        e.RackSetChainGain(t, 0, 0.5f);
        e.RackAddChainDevice(t, 0, 1);
        e.RackChainSamplerInfo(t, 0, out var before);
        e.BeginUndoGroup();
        e.RackRemoveChain(t, 0);
        int q = e.RackAddSamplerChain(t, snare, 36, false);
        e.RackSetChainTriggerNote(t, q, 36);
        e.RackSetChainName(t, q, "Snare");
        e.EndUndoGroup();
        yield return (e.RackChainName(t, 0) == "Snare" && e.RackChainDeviceCount(t, 0) == 0, "rack undo: a sample dropped on a full pad replaces it");
        e.Undo();
        e.RackChainSamplerInfo(t, 0, out var back);
        yield return (e.RackChainCount(t) == 1 && e.RackChainName(t, 0) == "Kick" && Same(e.RackChainGain(t, 0), 0.5f)
                      && e.RackChainDeviceCount(t, 0) == 1 && back.SampleId == before.SampleId,
            "rack undo: undo puts the previous sample back with its level, name and devices");

        // Single edits are their own steps: rename, note, a chain device.
        e.RackSetChainName(t, 0, "Kick 2");
        yield return (e.Undo() && e.RackChainName(t, 0) == "Kick", "rack undo: a pad rename");
        e.RackSetChainTriggerNote(t, 0, 38);
        yield return (e.Undo() && e.RackChainTriggerNote(t, 0) == 36, "rack undo: a pad note change");
        e.RackRemoveChainDevice(t, 0, 0);
        yield return (e.Undo() && e.RackChainDeviceCount(t, 0) == 1, "rack undo: a removed pad device");

        // A no-op edit (the same name) doesn't add an undo step.
        int depth = 0;
        e.RackSetChainName(t, 0, "Kick");
        while (e.Undo()) depth++;
        yield return (e.RackChainCount(t) == 0 && depth > 0, "rack undo: undo walks the whole pad history back");

        // It still plays after edits and undo/redo (the forked rack renders).
        while (e.Redo()) { }
        e.SetBpm(120); e.SetMetronome(false);
        int clip = e.AddMidiClip(t, 0.0, 1.0);
        e.SetClipNotes(t, clip, new[] { new NotaNote(36, 0.0, 0.5, 1.0f) });
        var buf = new float[512 * 2];
        float peak = 0f;
        e.Seek(0); e.Play();
        for (int i = 0; i < 20; i++) { e.RenderOffline(buf, 512, 44100); foreach (var s in buf) peak = Math.Max(peak, Math.Abs(s)); }
        e.StopTransport();
        yield return (peak > 0.05f, $"rack undo: the pad plays after undo/redo (peak {peak:F2})");
        File.Delete(kick); File.Delete(snare);
    }

    private static IEnumerable<(bool, string)> InstrumentRackUndo()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentRackTrack();
        int c = e.RackAddChain(t, 0);
        yield return (e.RackChainCount(t) == 2 && e.Undo() && e.RackChainCount(t) == 1, "rack undo: an added Instrument Rack chain");
        e.RackSetChainZone(t, 0, 36, 60, 0, 127);
        e.RackChainZone(t, 0, out int lo, out int hi, out _, out _);
        bool set = lo == 36 && hi == 60;
        yield return (set && e.Undo() && Zone(e, t) == (0, 127), "rack undo: a key-zone edit");
        e.RackSetChainInstrument(t, 0, 2);
        yield return (e.RackChainInstrumentKind(t, 0) == 2 && e.Undo() && e.RackChainInstrumentKind(t, 0) != 2, "rack undo: a chain instrument swap");
        _ = c;
    }

    private static (int, int) Zone(NotaEngine e, int t)
    {
        e.RackChainZone(t, 0, out int lo, out int hi, out _, out _);
        return (lo, hi);
    }
}
