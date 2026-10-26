// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement keyboard moves and drop-below-the-last-track (specs/039-arrangement-keyboard-moves):
// the nudge step stops at the song start (#302), ⌥↑/⌥↓ skip rows that can't take the clips,
// a Shift-click selects the header range (#249), and a track "like" a source copies its
// instrument and devices but nothing it played, ahead of the returns, in one undo step (#301).

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ArrangementMovesTests
{
    private const int Utility = 4;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // Nudge: a full step, clamped at 0, nothing at 0.
        yield return (ArrangementMoves.NudgeDelta(4, 1, +1) == 1 && ArrangementMoves.NudgeDelta(4, 1, -1) == -1,
            "moves: ←/→ nudge by the grid step");
        yield return (ArrangementMoves.NudgeDelta(0.25, 1, -1) == -0.25, "moves: a nudge left stops at the song start");
        yield return (ArrangementMoves.NudgeDelta(0, 1, -1) == 0, "moves: nothing moves left of the song start");

        // Row shift: rows 0 (audio) 1 (instrument) 2 (audio) 3 (audio); audio only on audio.
        bool[] audio = { true, false, true, true };
        bool Accepts(int from, int to) => audio[from] == audio[to];
        yield return (ArrangementMoves.RowShift(new[] { 0 }, 4, +1, Accepts) == 2, "moves: ⌥↓ skips a track that can't take the clip");
        yield return (ArrangementMoves.RowShift(new[] { 2, 3 }, 4, +1, Accepts) == 0, "moves: ⌥↓ at the last track does nothing");
        // Rows 0 1 audio, 2 instrument, 3 4 audio: a block on 3+4 can't stop half on row 2.
        bool[] five = { true, true, false, true, true };
        yield return (ArrangementMoves.RowShift(new[] { 3, 4 }, 5, -1, (f, t) => five[f] == five[t]) == -3,
            "moves: a two-row block moves until every clip lands on a track that takes it");
        yield return (ArrangementMoves.RowShift(new[] { 2, 3 }, 4, -1, Accepts) == 0,
            "moves: no shift when the block can't fit on matching tracks");

        // Header range: either direction, unknown anchor → just the target.
        int[] order = { 10, 11, 12, 13 };
        yield return (ArrangementMoves.Range(order, 13, 11).SequenceEqual(new[] { 11, 12, 13 }), "moves: Shift-click selects up to the anchor");
        yield return (ArrangementMoves.Range(order, 99, 12).SequenceEqual(new[] { 12 }), "moves: no anchor selects just the clicked track");
        yield return (ArrangementMoves.Range(order, 10, 99).Count == 0, "moves: a hidden target selects nothing");

        // Re-indexing after a block move: a nudge of clip 1 over all of clip 0 carves clip 0
        // away, so clip 1 becomes clip 0 and clip 2 becomes clip 1 (Greptile on #324: an open
        // editor kept the old index and its next edit went into another clip).
        using (var re = new NotaEngine())
        {
            int t = re.AddInstrumentTrack();
            re.AddMidiClip(t, 4.0, 1.0);
            re.AddMidiClip(t, 5.0, 2.0);
            re.AddMidiClip(t, 12.0, 2.0);
            IReadOnlyList<double> Starts()
            {
                var l = new List<double>();
                while (re.TryGetClipInfo(t, l.Count, out var ci)) l.Add(ci.StartBeat);
                return l;
            }
            var before = new Dictionary<int, IReadOnlyList<double>> { [t] = Starts() };
            bool ok = re.MoveClipBlock(new[] { (t, 1, t, 4.0) });
            var map = ArrangementMoves.Reindex(before, new[] { (t, 1) }, re.LastPlacedClips(),
                new Dictionary<int, IReadOnlyList<double>> { [t] = Starts() });
            yield return (ok && map[(t, 1)] == (t, 0) && map[(t, 0)] is null && map[(t, 2)] == (t, 1),
                $"moves: after a move that carves a clip away, clips are found at their new indices ({string.Join(", ", map.Select(kv => $"{kv.Key.Clip}→{kv.Value?.Clip.ToString() ?? "gone"}"))})");
            // Undo brings the carved clip back: an editor on the moved clip (now 0) goes back to
            // 1, and one on the clip behind it (now 1) back to 2 (Greptile on #324, round 2).
            var after = new Dictionary<int, IReadOnlyList<double>> { [t] = Starts() };
            var inverse = ArrangementMoves.Invert(map, after);
            bool undone = re.Undo();
            var restored = new Dictionary<int, IReadOnlyList<double>> { [t] = Starts() };
            yield return (undone && ArrangementMoves.SameStarts(restored, before) && !ArrangementMoves.SameStarts(restored, after)
                          && inverse[(t, 0)] == (t, 1) && inverse[(t, 1)] == (t, 2),
                "moves: undoing the move is recognised, and sends the clips back to their old indices");
            yield return (re.Redo() && ArrangementMoves.SameStarts(new Dictionary<int, IReadOnlyList<double>> { [t] = Starts() }, after),
                "moves: redoing it is recognised too");
        }
        var cross = ArrangementMoves.Reindex(
            new Dictionary<int, IReadOnlyList<double>> { [1] = new[] { 0.0, 8.0 }, [2] = new[] { 0.0 } },
            new[] { (1, 0) }, new[] { (2, 1) },
            new Dictionary<int, IReadOnlyList<double>> { [1] = new[] { 8.0 }, [2] = new[] { 0.0, 4.0 } });
        yield return (cross[(1, 0)] == (2, 1) && cross[(1, 1)] == (1, 0) && cross[(2, 0)] == (2, 0),
            "moves: a clip moved to another track is where it landed, and the ones left behind close up");

        // A track like the source.
        using var eng = new NotaEngine();
        int inst = eng.AddInstrumentTrack();
        eng.AddBuiltinDevice(inst, Utility);
        eng.SetTrackVolume(inst, 0.5f);
        int clip = eng.AddMidiClip(inst, 4.0, 4.0);
        eng.AddAutomationLane(inst, AutomationTarget.Pan, -1, -1);
        int ret = eng.AddReturnTrack();
        int kind = eng.TrackInstrumentKind(inst);

        int like = eng.AddTrackLike(inst);
        int idx = IndexOf(eng, like);
        yield return (like > 0 && idx == IndexOf(eng, ret) - 1, $"moves: the new track lands before the returns (row {idx})");
        eng.TryGetTrackInfo(idx, out var info);
        yield return (info.IsInstrument && eng.TrackInstrumentKind(like) == kind && eng.TrackDeviceCount(like) == eng.TrackDeviceCount(inst),
            "moves: it has the source's instrument and devices");
        yield return (info.ClipCount == 0 && eng.AutomationLaneCount(like) == 0 && info.GroupId == -1,
            "moves: it has no clips, automation or group");
        yield return (Math.Abs(info.Volume - 0.5f) < 1e-4, "moves: it keeps the source's mix");

        // Track + clip move undo together.
        int track0 = eng.TrackCount;
        eng.BeginUndoGroup();
        int like2 = eng.AddTrackLike(inst);
        bool moved = eng.MoveClipBlock(new[] { (inst, clip, like2, 8.0) });
        eng.EndUndoGroup();
        eng.TryGetClipInfo(like2, 0, out var ci);
        yield return (moved && Math.Abs(ci.StartBeat - 8.0) < 1e-9, "moves: a clip moves onto the new track at its drop beat");
        yield return (eng.Undo() && eng.TrackCount == track0 && eng.TryGetClipInfo(inst, clip, out var back) && Math.Abs(back.StartBeat - 4.0) < 1e-9,
            "moves: one undo takes back the move and the new track");

        // A track added after the returns exist sits after them in the graph; the new track
        // still goes below the last regular track, not above it (Mac check of #324).
        int late = eng.AddInstrumentTrack();
        int like3 = eng.AddTrackLike(inst);
        yield return (like3 > 0 && IndexOf(eng, like3) == IndexOf(eng, late) + 1,
            $"moves: the new track lands below the last track added (row {IndexOf(eng, like3)}, last track row {IndexOf(eng, late)})");

        // Only regular tracks have a "like".
        int grp = eng.CreateGroup(new[] { inst });
        yield return (eng.AddTrackLike(ret) == -1 && eng.AddTrackLike(grp) == -1 && eng.AddTrackLike(9999) == -1,
            "moves: no new track like a return, a group or an unknown id");
        int child = eng.AddTrackLike(inst);
        eng.TryGetTrackInfo(IndexOf(eng, child), out var ci2);
        yield return (child > 0 && ci2.GroupId == -1, "moves: a track like a group member is top-level");
    }

    private static int IndexOf(NotaEngine eng, int id)
    {
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var t) && t.Id == id) return i;
        return -1;
    }
}
