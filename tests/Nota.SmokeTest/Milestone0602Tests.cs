// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Bug fixes of milestone 0.60.2 that the engine and the editing model can show:
// - A split or an overwrite keeps a clip's deactivation and gives each piece its own part of
//   the clip's envelopes (#467).
// - Every warp marker of a clip is read, not the first 128: the clip editor's warp length and
//   a save depend on the last one (#537).
// - A Clip editor whose clips all left (their track removed) still answers its active clip
//   instead of throwing (#530).
// - The keys plug-in editor windows hand the app: every bare key a binding can name has a code
//   both ways, and the queue drains oldest first into bounded buffers (#221).
// - Nota Monolith sounds every one of back-to-back notes of one pitch, Legato on or off (#645).
// - No note hangs: Volt turned mono with keys down releases them, and a clip deleted while its
//   note sounds lets the note go (#649).
// - Drum Rack: a muted pad keeps time, so unmuting it fires no stray hit (#650); hits on one
//   step share their humanize, and a choke group spares a pad struck at the same sample (#644).
// - A Compressor on a rack chain takes a sidechain key (#643).
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --m0602`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Input;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Milestone0602Tests
{
    private const int Rate = 48000;
    private const int OfflineRate = 44100;   // RenderOffline before a device opens (the Transport default)

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        foreach (var r in MidiSplitCarries()) yield return r;
        foreach (var r in AudioSplitRebases()) yield return r;
        foreach (var r in ManyWarpMarkers()) yield return r;
        foreach (var r in EmptyClipSet()) yield return r;
        foreach (var r in EditorKeys()) yield return r;
        foreach (var r in MonolithRepeats()) yield return r;
        foreach (var r in NoHangingNotes()) yield return r;
        foreach (var r in KompaktOpenHat()) yield return r;
        foreach (var r in UnmutedPadIsQuiet()) yield return r;
        foreach (var r in ChainCompressorKey()) yield return r;
    }

    // #644: the Kompakt kit's open hat on every 1/8 is heard on every hit, alone and with a
    // closed hat (the same choke group) struck on the same steps, the kit's humanize turned up.
    private static IEnumerable<(bool, string)> KompaktOpenHat()
    {
        const int bars = 4, hits = bars * 8;
        int sr = OfflineRate, per = sr / 4;   // a 1/8 at 120 BPM
        float[] Play(bool open, bool closed, float humanize)
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int t = new DrumKitService().CreateTrack(e, "kompakt", out _);
            e.RackSetHumanize(t, humanize);
            e.AddMidiClip(t, 0.0, bars * 4.0);
            var notes = new List<NotaNote>();
            for (int i = 0; i < hits; i++)
            {
                if (closed) notes.Add(new NotaNote(42, i * 0.5, 0.25, 0.8f));
                if (open) notes.Add(new NotaNote(46, i * 0.5, 0.5, 0.8f));
            }
            e.SetClipNotes(t, 0, notes.ToArray());
            e.SetLoop(true, 0.0, bars * 4.0);
            var buf = new float[per * hits * 2];
            e.Seek(0.0); e.Play(); RenderLong(e, buf, per * hits, 128); e.StopTransport();
            // Each step's body: past the closed hat's few ms, before the next step.
            return Enumerable.Range(0, hits).Select(i => RmsOf(buf, i * per + sr / 25, i * per + per - sr / 50)).ToArray();
        }
        var alone = Play(open: true, closed: false, humanize: 0.04f);
        float top = alone.Max();
        var weak = Enumerable.Range(0, hits).Where(i => alone[i] < top * 0.5f).ToList();
        yield return (top > 0.005f && weak.Count == 0,
            $"Kompakt: an open hat on every 1/8 sounds every hit (RMS {top:F3}; weak: {string.Join(" ", weak.Select(i => $"#{i}"))})");
        var both = Play(open: true, closed: true, humanize: 1f);
        var closedOnly = Play(open: false, closed: true, humanize: 1f);
        var cut = Enumerable.Range(0, hits).Where(i => both[i] < closedOnly[i] + top * 0.5f).ToList();
        yield return (cut.Count == 0,
            $"Kompakt: an open hat struck with a closed hat (one choke group, humanize 100 %) is heard on every step (cut: {string.Join(" ", cut.Select(i => $"#{i}"))})");
    }

    // #650: a pad that took a hit while muted stays quiet when unmuted between hits — with
    // another pad playing on, as in a beat (an idle rack isn't rendered at all).
    private static IEnumerable<(bool, string)> UnmutedPadIsQuiet()
    {
        int beat = OfflineRate / 2;   // 120 BPM
        // From 1.75 to 2.0, unmuted at 1.75; the hat's hit at 1 (or none) landed while muted.
        float[] Play(bool hatAtOne, out float heard)
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int t = new DrumKitService().CreateTrack(e, "kompakt", out _);
            int hat = Enumerable.Range(0, e.RackChainCount(t)).First(c => e.RackChainName(t, c) == "Closed Hat");
            e.RackSetHumanize(t, 0f);
            e.AddMidiClip(t, 0.0, 4.0);
            var notes = new List<NotaNote> { new(42, 0.0, 0.25, 0.9f) };
            if (hatAtOne) notes.Add(new NotaNote(42, 1.0, 0.25, 0.9f));
            notes.AddRange(Enumerable.Range(0, 16).Select(i => new NotaNote(51, i * 0.25, 0.25, 0.5f)));
            e.SetClipNotes(t, 0, notes.ToArray());
            var buf = new float[beat * 2 * 2];
            e.Seek(0.0); e.Play();
            RenderLong(e, buf, beat / 2, 128);
            heard = RmsOf(buf, 0, beat / 8);
            e.RackSetChainMute(t, hat, true);
            RenderLong(e, buf, beat * 5 / 4, 128);
            e.RackSetChainMute(t, hat, false);
            var after = new float[beat / 4 * 2];
            RenderLong(e, after, beat / 4, 128);
            e.StopTransport();
            return after;
        }
        var with = Play(true, out float heard);
        var without = Play(false, out _);
        var diff = with.Zip(without, (x, y) => x - y).ToArray();
        float stray = RmsOf(diff, 0, diff.Length / 2);
        yield return (heard > 0.005f && stray < heard * 0.05f,
            $"Drum Rack: unmuting a pad between hits fires no stray hit (hit {heard:F3}, stray after unmute {stray:F4})");
    }

    // #643: a Compressor on a Drum Rack pad's chain takes a sidechain key from another track.
    private static IEnumerable<(bool, string)> ChainCompressorKey()
    {
        using var e = new NotaEngine();
        int drums = e.AddDrumRackTrack();
        int src = e.AddInstrumentTrack();
        int chain = e.RackAddChain(drums, 6);
        int comp = e.RackAddChainDevice(drums, chain, 1);   // Compressor
        int eq = e.RackAddChainDevice(drums, chain, 0);     // EQ: takes no key
        bool accepts = e.RackChainAcceptsSidechain(drums, -1, chain, comp);
        bool eqRefuses = !e.RackChainAcceptsSidechain(drums, -1, chain, eq);
        int set = e.RackChainSetSidechainKey(drums, -1, chain, comp, new NestedSidechain(src, false, 3f, 1f), SidechainKey.WholeTrack);
        var back = e.RackChainSidechain(drums, -1, chain, comp);
        yield return (accepts && eqRefuses && set == 0 && back is { SourceTrack: var s, GainDb: var g } && s == src && Math.Abs(g - 3f) < 1e-4f,
            $"Drum Rack: a chain's Compressor accepts a sidechain key and keeps it (accepts {accepts}, EQ refuses {eqRefuses}, set {set}, source {back?.SourceTrack})");
    }

    private static float RmsOf(float[] buf, int from, int to)
    {
        double sum = 0;
        for (int i = from * 2; i < to * 2; i++) sum += buf[i] * (double)buf[i];
        return (float)Math.Sqrt(sum / Math.Max(1, (to - from) * 2));
    }

    private static int ParamIndex(NotaEngine e, int t, string id)
    {
        for (int i = 0; i < e.PluginParamCount(t, -1); i++) if (e.PluginParamId(t, -1, i) == id) return i;
        return -1;
    }

    // #645: four 1/16 notes of one pitch end to end on Monolith: each is heard about as loud as
    // the first, with Legato off and on.
    private static IEnumerable<(bool, string)> MonolithRepeats()
    {
        foreach (bool legato in new[] { false, true })
        {
            using var e = new NotaEngine();
            int sr = OfflineRate;
            e.SetBpm(120);
            int t = e.AddMonolithTrack();
            e.PluginParamSet(t, -1, ParamIndex(e, t, "legato"), legato ? 1f : 0f);
            e.AddMidiClip(t, 0.0, 4.0);
            e.SetClipNotes(t, 0, Enumerable.Range(0, 4).Select(i => new NotaNote(48, i * 0.25, 0.25, 0.9f)).ToArray());
            int note = sr / 8;   // a 1/16 at 120 BPM
            var buf = new float[note * 4 * 2];
            e.Seek(0.0); e.Play(); RenderLong(e, buf, note * 4); e.StopTransport();
            // Each note's body, past its attack and before the next note.
            var levels = Enumerable.Range(0, 4).Select(i => RmsOf(buf, i * note + note / 4, i * note + note * 3 / 4)).ToArray();
            yield return (levels[0] > 0.005f && levels.Skip(1).All(l => l > levels[0] * 0.5f),
                $"Monolith (Legato {(legato ? "on" : "off")}): back-to-back notes of one pitch all sound (RMS {string.Join(" / ", levels.Select(l => l.ToString("F3")))})");
        }
    }

    // #649: notes that lose their note-off source must still stop.
    private static IEnumerable<(bool, string)> NoHangingNotes()
    {
        // Volt holds a chord in poly, turns mono (a preset with mono = 1), the keys come up.
        {
            using var e = new NotaEngine();
            int sr = OfflineRate;
            int t = e.AddVoltSynthTrack();
            var buf = new float[4096 * 2];
            foreach (int p in new[] { 60, 64, 67 }) e.TrackNoteOn(t, p, 0.8f);
            e.RenderOffline(buf, 4096);
            float held = Rms(buf);
            e.PluginParamSet(t, -1, ParamIndex(e, t, "mono"), 1f);
            e.RenderOffline(buf, 4096);
            foreach (int p in new[] { 60, 64, 67 }) e.TrackNoteOff(t, p);
            var tail = new float[sr * 3 * 2];
            RenderLong(e, tail, sr * 3);
            float end = RmsOf(tail, sr * 3 - 4096, sr * 3);
            yield return (held > 0.001f && end < 1e-4f,
                $"Volt: a chord held while it turns mono stops when the keys come up (held {held:F4}, after {end:E1})");
        }
        // A clip deleted while its long note sounds.
        {
            using var e = new NotaEngine();
            int sr = OfflineRate;
            e.SetBpm(120);
            int t = e.AddVoltSynthTrack();
            e.AddMidiClip(t, 0.0, 16.0);
            e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 16.0, 0.8f) });
            var buf = new float[4096 * 2];
            e.Seek(0.0); e.Play();
            for (int i = 0; i < 4; i++) e.RenderOffline(buf, 4096);
            float playing = Rms(buf);
            bool deleted = e.TryDeleteClip(t, 0);
            var tail = new float[sr * 3 * 2];
            RenderLong(e, tail, sr * 3);
            e.StopTransport();
            float end = RmsOf(tail, sr * 3 - 4096, sr * 3);
            yield return (deleted && playing > 0.001f && end < 1e-4f,
                $"a clip deleted while its note sounds lets the note go (playing {playing:F4}, after {end:E1})");
        }
        // A sounding note deleted with a later note of its pitch just ahead: it still stops.
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int t = e.AddVoltSynthTrack();
            e.AddMidiClip(t, 0.0, 16.0);
            var later = new NotaNote(60, 1.0, 0.1, 0.8f);
            e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 8.0, 0.8f), later });
            var buf = new float[4096 * 2];
            e.Seek(0.0); e.Play();
            for (int i = 0; i < 4; i++) e.RenderOffline(buf, 4096);   // ≈ 0.74 beats: the next C is ≈ 0.26 beats away
            e.SetClipNotes(t, 0, new[] { later });
            int sr = OfflineRate;
            var tail = new float[sr * 3 * 2];
            RenderLong(e, tail, sr * 3);
            e.StopTransport();
            float end = RmsOf(tail, sr * 3 - 4096, sr * 3);
            yield return (end < 1e-4f, $"a deleted note stops even with a later note of its pitch just ahead (after {end:E1})");
        }
        // An edit that removes no note (a rename) releases nothing: a note the track plays
        // through MIDI routing isn't cut by its own unchanged later note of that pitch.
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int src = e.AddInstrumentTrack();
            e.AddMidiClip(src, 0.0, 16.0);
            e.SetClipNotes(src, 0, new[] { new NotaNote(60, 0.0, 8.0, 0.8f) });
            int dst = e.AddVoltSynthTrack();
            e.SetTrackMidiSource(dst, src);
            e.AddMidiClip(dst, 0.0, 16.0);
            e.SetClipNotes(dst, 0, new[] { new NotaNote(60, 1.0, 0.1, 0.8f) });
            var buf = new float[4096 * 2];
            e.Seek(0.0); e.Play();
            for (int i = 0; i < 4; i++) e.RenderOffline(buf, 4096);   // ≈ 0.74 beats
            float before = Rms(buf);
            e.SetTrackName(dst, "Renamed");
            e.RenderOffline(buf, 2048);                                // to ≈ 0.84, before the later note
            e.StopTransport();
            float after = RmsOf(buf, 1024, 2048);   // Volt's release would have faded by here
            yield return (before > 0.001f && after > before * 0.9f,
                $"a rename while a routed note plays doesn't cut it (before {before:F4}, after {after:F4})");
        }
    }

    // RenderOffline takes a block at a time.
    private static void RenderLong(NotaEngine e, float[] buf, int frames, int blockFrames = 4096)
    {
        var block = new float[blockFrames * 2];
        for (int at = 0; at < frames; at += blockFrames)
        {
            int n = Math.Min(blockFrames, frames - at);
            e.RenderOffline(block, n);
            Array.Copy(block, 0, buf, at * 2, n * 2);
        }
    }

    private static float Rms(float[] buf) => RmsOf(buf, 0, buf.Length / 2);

    // #221: key codes and the editor-window key queue through the managed wrappers.
    private static IEnumerable<(bool, string)> EditorKeys()
    {
        var tokens = new[] { "A", "Z", "0", ";", "F2", "F24", "↑", "→", "Space", "Return", "Esc", "⌫", "⌦", "PgUp", "Home" };
        var bad = tokens.Where(t => EditorKeyCodes.Of(t) <= 0 || EditorKeyCodes.TokenOf(EditorKeyCodes.Of(t)) != t).ToList();
        yield return (bad.Count == 0 && EditorKeyCodes.Of("F2") == 0xF705 && EditorKeyCodes.Of("a") == 'A',
            $"editor key codes: every bare key a binding can name round-trips (F2 = 0x{EditorKeyCodes.Of("F2"):X}; failing: {string.Join(" ", bad)})");

        using var e = new NotaEngine();
        var codes = new int[2];
        var down = new byte[2];
        while (e.DrainEditorKeys(codes, down) > 0) { }   // whatever an earlier check left
        e.SetEditorTypingKeys(new[] { (int)'A', EditorKeyCodes.Of("F2") });
        bool empty = e.DrainEditorKeys(codes, down) == 0;
        e.PushEditorKey('a', true);                      // a typed lower-case letter is its key's code
        e.PushEditorKey(EditorKeyCodes.Of("F2"), true);
        e.PushEditorKey('A', false);
        int n1 = e.DrainEditorKeys(codes, down);
        bool first = n1 == 2 && codes[0] == 'A' && down[0] == 1 && codes[1] == 0xF705 && down[1] == 1;
        int n2 = e.DrainEditorKeys(codes, down);
        bool second = n2 == 1 && codes[0] == 'A' && down[0] == 0;
        bool after = e.DrainEditorKeys(codes, down) == 0;
        yield return (empty && first && second && after,
            $"editor key queue: empty drains 0, presses and releases come out oldest first into a 2-slot buffer ({n1} then {n2})");
    }

    private static float ValueAt(AutomationPoint[] pts, double beat)
    {
        if (pts.Length == 0) return float.NaN;
        if (beat <= pts[0].Beat) return pts[0].Value;
        for (int i = 0; i + 1 < pts.Length; i++)
            if (beat <= pts[i + 1].Beat)
            {
                double span = pts[i + 1].Beat - pts[i].Beat;
                return span <= 0 ? pts[i + 1].Value : (float)(pts[i].Value + (pts[i + 1].Value - pts[i].Value) * (beat - pts[i].Beat) / span);
            }
        return pts[^1].Value;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-3f;

    // #467: a deactivated MIDI clip with a volume ramp, split in the middle and overwritten.
    private static IEnumerable<(bool, string)> MidiSplitCarries()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 0, 16);
        e.SetClipNotes(t, c, new[] { new NotaNote(60, 0, 1, 0.8f), new NotaNote(62, 10, 1, 0.8f) });
        e.SetMidiClipEnvelope(t, c, MidiClipEnvelope.Volume, new[] { new AutomationPoint(0, 0f), new AutomationPoint(16, 1f) });
        e.SetClipActive(t, c, false);
        int right = e.SplitClip(t, c, 8);
        bool inactive = right > 0 && e.TryGetClipInfo(t, c, out var li) && !li.IsActive
                        && e.TryGetClipInfo(t, right, out var ri) && !ri.IsActive;
        yield return (inactive, "split: both pieces of a deactivated MIDI clip stay deactivated");
        var lv = e.GetMidiClipEnvelope(t, c, MidiClipEnvelope.Volume);
        var rv = right > 0 ? e.GetMidiClipEnvelope(t, right, MidiClipEnvelope.Volume) : Array.Empty<AutomationPoint>();
        yield return (Near(ValueAt(lv, 4), 0.25f) && Near(ValueAt(rv, 0), 0.5f) && Near(ValueAt(rv, 4), 0.75f),
            $"split: each MIDI piece plays its own part of the volume envelope (left@4 {ValueAt(lv, 4):F2}, right@0 {ValueAt(rv, 0):F2}, right@4 {ValueAt(rv, 4):F2})");
    }

    // #467: an audio clip's volume ramp, split — the right piece starts where the ramp was.
    private static IEnumerable<(bool, string)> AudioSplitRebases()
    {
        string wav = Wav(8.0);
        try
        {
            using var e = new NotaEngine();
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0);
            if (!e.TryGetClipInfo(t, c, out var ci) || ci.LengthBeats <= 2)
            {
                yield return (false, "audio split: clip placed");
                yield break;
            }
            double len = ci.LengthBeats, mid = len / 2;
            e.SetClipVolumeEnvelope(t, c, new[] { new AutomationPoint(0, 0f), new AutomationPoint(len, 1f) });
            int right = e.SplitClip(t, c, mid);
            var rv = right > 0 ? e.GetClipVolumeEnvelope(t, right) : Array.Empty<AutomationPoint>();
            yield return (Near(ValueAt(rv, 0), 0.5f) && Near(ValueAt(rv, mid / 2), 0.75f),
                $"split: the right audio piece plays the volume envelope from the cut (@0 {ValueAt(rv, 0):F2}, @{mid / 2:F1} {ValueAt(rv, mid / 2):F2})");
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    // #537: 200 markers survive the read the clip editor and the save make.
    private static IEnumerable<(bool, string)> ManyWarpMarkers()
    {
        string wav = Wav(8.0);
        try
        {
            using var e = new NotaEngine();
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0);
            e.SetClipWarp(t, c, true, 0);
            const int n = 200;
            var src = new double[n];
            var beat = new double[n];
            for (int i = 0; i < n; i++) { src[i] = i * (8.0 * Rate / (n - 1)); beat[i] = i * 0.125; }
            e.SetClipWarpMarkers(t, c, src, beat);
            var (ms, mb) = WarpMarkers.Read(e, t, c);
            yield return (ms.Length == n && Math.Abs(mb[^1] - beat[^1]) < 1e-9,
                $"every warp marker is read, past the old 128 ({ms.Length} of {n}, last at beat {(mb.Length > 0 ? mb[^1] : -1):F3})");
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    // #530: the roll's set loses its last clip; reading the active clip must not throw.
    private static IEnumerable<(bool, string)> EmptyClipSet()
    {
        var set = ClipSet.Single(5, 0, 8, 4);
        set.Remove(5, 0);
        bool ok;
        try { ok = set.IsEmpty && set.ActiveClip is { TrackId: 5, ClipIndex: 0 } && Math.Abs(set.ActiveClip.LengthBeats - 4) < 1e-9; }
        catch (ArgumentOutOfRangeException) { ok = false; }
        yield return (ok, "an emptied clip set still answers its last active clip (no crash after its track is removed)");
    }

    private static string Wav(double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-m0602-{Guid.NewGuid():N}.wav");
        int frames = (int)(seconds * Rate);
        var x = new float[frames * 2];
        for (int i = 0; i < frames; i++) x[2 * i] = x[2 * i + 1] = 0.4f * (float)Math.Sin(2 * Math.PI * 220 * i / Rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, frames);
        return path;
    }
}
