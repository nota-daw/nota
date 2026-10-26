// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Keyboard clip moves (specs/039-arrangement-keyboard-moves): ←/→ nudge the selected clips by
// the grid step, ⌥←/⌥→ by a 64th (#302); ⌥↑/⌥↓ move them to the next track that takes them,
// keeping their exact position (#301's comment). Each key press is one undo step.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application.Editing;

namespace Nota.App;

public partial class ArrangementView
{
    /// <summary>←/→: nudges the clip selection by the grid step (<paramref name="fine"/>: a 64th)
    /// as one block, stopping at the song start. False when no clip is selected.</summary>
    public bool NudgeSelectedClips(int dir, bool fine)
    {
        if (_engine is null || _sel.Count == 0) return false;
        var block = _sel.Select(s => (s.track, s.clip, vm: FindClipVM(s.track, s.clip))).Where(b => b.vm is not null).ToList();
        if (block.Count == 0) return false;
        double delta = ArrangementMoves.NudgeDelta(block.Min(b => b.vm!.StartBeat), fine ? ArrangementMoves.FineNudgeBeats : SnapStepBeats, dir);
        if (delta == 0) return true;   // already at the song start: the key is still ours
        if (MoveBlock(block.Select(b => (b.track, b.clip, b.track, b.vm!.StartBeat + delta)).ToArray()) is not { } move) return true;
        Refresh();
        ReselectPlaced();
        ClipsMoved?.Invoke(move);   // open editors follow their clips (index and START read-out)
        return true;
    }

    /// <summary>⌥↑/⌥↓: moves the clip selection to the nearest tracks above / below that take
    /// every clip (audio on audio, MIDI on instrument tracks), at the same beats. False when no
    /// clip is selected.</summary>
    public bool MoveSelectedClipsToTrack(int dir)
    {
        if (_engine is null || _sel.Count == 0) return false;
        var block = _sel.Select(s => (s.track, s.clip, vm: FindClipVM(s.track, s.clip), row: RowOfTrack(s.track)))
                        .Where(b => b.vm is not null && b.row >= 0).ToList();
        if (block.Count == 0) return false;
        int shift = ArrangementMoves.RowShift(block.Select(b => b.row).Distinct().ToList(), _tracks.Count, dir, TakesClipsFrom);
        if (shift == 0)
        {
            StatusMessage?.Invoke(dir < 0 ? "No track above takes these clips" : "No track below takes these clips");
            return true;
        }
        var moves = block.Select(b => (b.track, b.clip, _tracks[b.row + shift].Id, b.vm!.StartBeat)).ToArray();
        if (MoveBlock(moves) is not { } move) return true;
        if (_engine.LastMoveKeptDeviceAutomation())
            StatusMessage?.Invoke("Moved across tracks — device automation stayed on the source");
        Refresh();
        ReselectPlaced();
        ClipsMoved?.Invoke(move);
        if (SelTrackId > 0) RevealTrack(SelTrackId);
        AnnounceSelectedTrack();   // the destination track is now the active one
        return true;
    }

    /// <summary>Raised after a block of clips moved (drag, keys, drop onto new tracks) with where
    /// each clip of the touched tracks now is (null: carved away). Open editors hold clip indices,
    /// which a move can shift, so they re-target from this.</summary>
    public event Action<ClipBlockMove>? ClipsMoved;

    // One MoveClipBlock, plus where every clip of the touched tracks ended up; null if refused.
    internal ClipBlockMove? MoveBlock(
        (int trackId, int clipIndex, int destTrackId, double newStartBeat)[] moves)
    {
        if (_engine is null) return null;
        var touched = moves.SelectMany(m => new[] { m.trackId, m.destTrackId }).Distinct().ToList();
        var before = touched.ToDictionary(t => t, ClipStarts);
        if (!_engine.MoveClipBlock(moves)) return null;
        var after = touched.ToDictionary(t => t, ClipStarts);
        return new ClipBlockMove(ArrangementMoves.Reindex(before, moves.Select(m => (m.trackId, m.clipIndex)).ToList(),
            _engine.LastPlacedClips(), after), before, after);
    }

    internal void RaiseClipsMoved(ClipBlockMove move) => ClipsMoved?.Invoke(move);

    /// <summary>Each given track's clip starts, by index (a snapshot to compare after undo / redo).</summary>
    internal IReadOnlyDictionary<int, IReadOnlyList<double>> ClipStartsOf(IEnumerable<int> tracks)
        => tracks.ToDictionary(t => t, ClipStarts);

    private IReadOnlyList<double> ClipStarts(int trackId)
    {
        var starts = new List<double>();
        while (_engine!.TryGetClipInfo(trackId, starts.Count, out var ci)) starts.Add(ci.StartBeat);
        return starts;
    }

    // A row takes another row's clips when it holds the same kind of clip (a group or a return holds none).
    private bool TakesClipsFrom(int from, int to)
    {
        var src = _tracks[from];
        var dst = _tracks[to];
        return !dst.IsGroup && !dst.IsReturn && dst.IsInstrument == src.IsInstrument;
    }
}
