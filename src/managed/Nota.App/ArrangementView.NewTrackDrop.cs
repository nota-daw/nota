// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Dropping clips below the last track (#301): each source track gets a new track like it
// (same kind, instrument and devices; no clips), inserted after the last regular track,
// and its clips move there at the beats they were dropped at. One undo step.

using System.Collections.Generic;
using System.Linq;

namespace Nota.App;

public partial class ArrangementView
{
    internal void MoveClipsToNewTracks(IReadOnlyList<int> sourceTracks, IReadOnlyList<(int track, int clip, double start)> clips)
    {
        if (_engine is null || sourceTracks.Count == 0 || clips.Count == 0) { Refresh(); return; }
        var newIds = new Dictionary<int, int>();
        Dictionary<(int Track, int Clip), (int Track, int Clip)?>? map = null;
        bool ok = true;
        _engine.BeginUndoGroup();
        try
        {
            foreach (var src in sourceTracks)
            {
                int id = _engine.AddTrackLike(src);
                if (id < 0) { ok = false; break; }
                newIds[src] = id;
            }
            ok = ok && (map = MoveBlock(clips.Select(c => (c.track, c.clip, newIds[c.track], c.start)).ToArray())) is not null;
        }
        finally { _engine.EndUndoGroup(); }
        // Half-done (a source the engine wouldn't copy, or a refused move): take the tracks back.
        if (!ok && newIds.Count > 0) _engine.Undo();
        Refresh();
        if (!ok) { StatusMessage?.Invoke("Couldn't move the clips to new tracks"); return; }
        if (_engine.LastMoveKeptDeviceAutomation())
            StatusMessage?.Invoke("Moved to new tracks — device automation stayed on the source");
        ReselectPlaced();
        ClipsMoved?.Invoke(map!);
        RevealTrack(newIds[sourceTracks[^1]]);
        AfterTrackEdit();
    }
}
