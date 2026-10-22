// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Range copy / cut (.specify/bugs/arrangement-range-clipboard, issue #281): a time range's
// slice of every selected track goes to the block clipboard, cut at the range edges and
// positioned from the range start; copy leaves the project alone, cut is one undo step; a
// range block pastes overwriting what it lands on, and a cross-track paste shifts it so the
// range's top row (even an empty one) lands on the target. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --range-clipboard`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RangeClipboardTests
{
    private const double Eps = 1e-6;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // MIDI: copy [2,6) out of a [0,8) clip with notes at 1, 3 and 5.
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int a = e.AddInstrumentTrack();
            int c = e.AddMidiClip(a, 0.0, 8.0);
            e.SetClipNotes(a, c, new[] { new NotaNote(60, 1.0, 0.5, 0.8f), new NotaNote(62, 3.0, 0.5, 0.8f), new NotaNote(64, 5.0, 0.5, 0.8f) });

            yield return (e.CopyRange(new[] { a }, 2.0, 6.0), "range: copy [2,6) of a MIDI clip");
            yield return (e.ClipboardBlockCount() == 1, $"range: the block clipboard holds the slice ({e.ClipboardBlockCount()})");
            yield return (e.TryGetClipInfo(a, 0, out var src) && Near(src.StartBeat, 0) && Near(src.LengthBeats, 8)
                          && !e.TryGetClipInfo(a, 1, out _) && e.GetClipNotes(a, 0).Length == 3,
                "range: copy leaves the source clip whole");

            yield return (e.PasteClipBlock(16.0, -1) == 1, "range: paste at beat 16 on the source track");
            int pasted = LastIndexOn(e, a);
            yield return (e.TryGetClipInfo(a, pasted, out var p) && Near(p.StartBeat, 16) && Near(p.LengthBeats, 4),
                $"range: the pasted slice is [16,20) (start {p.StartBeat}, len {p.LengthBeats})");
            var notes = e.GetClipNotes(a, pasted).OrderBy(n => n.StartBeat).ToArray();
            yield return (notes.Length == 2 && notes[0].Pitch == 62 && notes[1].Pitch == 64
                          && Near(notes[1].StartBeat - notes[0].StartBeat, 2.0),
                $"range: the slice keeps only the notes inside the range ({notes.Length})");
        }

        // A clip starting inside the range keeps its offset from the range start.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack();
            e.AddMidiClip(a, 4.0, 4.0);
            e.CopyRange(new[] { a }, 2.0, 6.0);
            e.PasteClipBlock(16.0, -1);
            int pasted = LastIndexOn(e, a);
            yield return (e.TryGetClipInfo(a, pasted, out var p) && Near(p.StartBeat, 18) && Near(p.LengthBeats, 2),
                $"range: a leading gap is kept — the slice lands at 18 (got {p.StartBeat}, len {p.LengthBeats})");
        }

        // A range paste overwrites what it lands on instead of shifting past it.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack();
            int b = e.AddInstrumentTrack();
            e.AddMidiClip(a, 0.0, 4.0);
            e.AddMidiClip(b, 0.0, 8.0);
            e.CopyRange(new[] { a }, 0.0, 4.0);
            yield return (e.PasteClipBlock(2.0, b) == 1, "range: paste onto track b at beat 2");
            var spans = Spans(e, b);
            yield return (spans.Any(s => Near(s.start, 2) && Near(s.len, 4)) && spans.Any(s => Near(s.start, 0) && Near(s.len, 2))
                          && spans.Any(s => Near(s.start, 6) && Near(s.len, 2)) && spans.Count == 3,
                $"range: the paste carves [2,6) out of b's clip instead of shifting ({string.Join(" ", spans)})");
        }

        // Clip copy after a range copy pastes the old way again (shifts past a taken spot).
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack();
            int c = e.AddMidiClip(a, 0.0, 4.0);
            e.CopyRange(new[] { a }, 0.0, 2.0);
            e.CopyClipBlock(new[] { (a, c) });
            e.PasteClipBlock(2.0, -1);
            int pasted = LastIndexOn(e, a);
            yield return (e.TryGetClipInfo(a, pasted, out var p) && Near(p.StartBeat, 4),
                $"range: a clip block still shifts past the source (lands at {p.StartBeat})");
        }

        // Cut: carves the range out in one undo step, and the slice pastes back.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack();
            e.AddMidiClip(a, 0.0, 8.0);
            yield return (e.CutRange(new[] { a }, 2.0, 6.0), "range: cut [2,6)");
            var spans = Spans(e, a);
            yield return (spans.Count == 2 && spans.Any(s => Near(s.start, 0) && Near(s.len, 2)) && spans.Any(s => Near(s.start, 6) && Near(s.len, 2)),
                $"range: cut leaves [0,2) and [6,8) ({string.Join(" ", spans)})");
            yield return (e.Undo() && Spans(e, a).Count == 1, "range: one undo restores the whole clip");
            yield return (e.PasteClipBlock(20.0, -1) == 1 && Spans(e, a).Any(s => Near(s.start, 20) && Near(s.len, 4)),
                "range: the cut slice pastes at beat 20");
        }

        // Cross-track paste anchors on the range's top row, even when that row is empty.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
            int c = e.AddInstrumentTrack(), d = e.AddInstrumentTrack();
            e.AddMidiClip(b, 0.0, 4.0);
            e.CopyRange(new[] { a, b }, 0.0, 4.0);
            yield return (e.PasteClipBlock(8.0, c) == 1, "range: paste a two-row range onto track c");
            yield return (Spans(e, d).Any(s => Near(s.start, 8)) && Spans(e, c).Count == 0,
                "range: the row-b slice lands on d — the empty top row maps to c");
        }

        // Audio: the slice keeps its place in the sample.
        {
            string wav = Path.Combine(Path.GetTempPath(), $"nota-range-clip-{Guid.NewGuid():N}.wav");
            WavWriter.WriteSine(wav, 4.0, 440, 48000);
            try
            {
                using var e = new NotaEngine();
                e.SetBpm(120);   // 4 s = 8 beats
                int t = e.AddAudioTrack();
                e.AddAudioClip(t, wav, 0.0);
                yield return (e.CopyRange(new[] { t }, 2.0, 6.0), "range: copy [2,6) of an audio clip");
                yield return (e.PasteClipBlock(16.0, -1) == 1, "range: paste the audio slice at 16");
                int pasted = LastIndexOn(e, t);
                yield return (e.TryGetClipInfo(t, pasted, out var p) && Near(p.StartBeat, 16) && Math.Abs(p.LengthBeats - 4) < 1e-3,
                    $"range: the audio slice is [16,20) (start {p.StartBeat}, len {p.LengthBeats})");
                yield return (e.TryGetAudioClipInfo(t, pasted, out var ai) && ai.SourceOffsetFrames > 0,
                    $"range: the audio slice starts 1 s into the sample (offset {ai.SourceOffsetFrames} frames)");
            }
            finally { File.Delete(wav); }
        }

        // Nothing covered → nothing copied, the old clipboard survives.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack();
            int c = e.AddMidiClip(a, 0.0, 2.0);
            e.CopyClipBlock(new[] { (a, c) });
            yield return (!e.CopyRange(new[] { a }, 4.0, 8.0) && e.ClipboardBlockCount() == 1,
                "range: an empty range copies nothing and keeps the clipboard");
        }
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < Eps;

    private static List<(double start, double len)> Spans(NotaEngine e, int track)
    {
        var list = new List<(double, double)>();
        for (int i = 0; e.TryGetClipInfo(track, i, out var ci); i++) list.Add((ci.StartBeat, ci.LengthBeats));
        return list;
    }

    private static int LastIndexOn(NotaEngine e, int track)
    {
        var placed = e.LastPlacedClips();
        foreach (var (t, c) in placed) if (t == track) return c;
        return -1;
    }
}
