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
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --m0602`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Input;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class Milestone0602Tests
{
    private const int Rate = 48000;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        foreach (var r in MidiSplitCarries()) yield return r;
        foreach (var r in AudioSplitRebases()) yield return r;
        foreach (var r in ManyWarpMarkers()) yield return r;
        foreach (var r in EmptyClipSet()) yield return r;
        foreach (var r in EditorKeys()) yield return r;
    }

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
