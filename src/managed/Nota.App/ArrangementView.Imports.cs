// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio files being imported in the background (MainWindow.AudioImport). Until the decode
// lands, the arrangement shows a placeholder clip whose waveform fills in block by block;
// a track created for the import is drawn translucent and titled "Processing…" until the
// whole import (decode + warp) has finished.

using System.Collections.Generic;

namespace Nota.App;

public sealed partial class ArrangementView
{
    /// <summary>One in-flight import, owned by the importer and read by the lanes.</summary>
    internal sealed class PendingImport
    {
        public int TrackId;
        /// <summary>The track was created for this import → the whole row is "processing".</summary>
        public bool OwnsTrack;
        public double StartBeat;
        public double LengthBeats;
        /// <summary>Waveform so far: (min,max) pairs; (1,-1) buckets are not decoded yet.</summary>
        public float[]? Peaks;
        public int PeakCount;
        public string Label = "";
        /// <summary>The real clip is on the track (now warping): stop drawing the placeholder.</summary>
        public bool ClipPlaced;
    }

    private readonly List<PendingImport> _pendingImports = new();

    internal void AddPendingImport(PendingImport p)
    {
        _pendingImports.Add(p);
        Refresh();
    }

    /// <summary>Repaint after the importer changed a pending entry (new peaks / label). Lanes
    /// only — no engine read — so it's cheap enough for every decoded block.</summary>
    internal void UpdatePendingImport() => _lanes.InvalidateVisual();

    internal void RemovePendingImport(PendingImport p)
    {
        if (_pendingImports.Remove(p)) Refresh();
    }

    /// <summary>Forget every placeholder (New/Open: the in-flight imports belong to the old
    /// graph and bow out on their own). The caller refreshes.</summary>
    internal void ClearPendingImports() => _pendingImports.Clear();

    internal bool IsProcessingTrack(int trackId)
    {
        foreach (var p in _pendingImports) if (p.OwnsTrack && p.TrackId == trackId) return true;
        return false;
    }

    // Placeholder clips on a track, as throwaway VMs the normal clip painter can draw. Not in
    // TrackVM.Clips, so they're never hit-tested, selected or sent to the engine.
    private IEnumerable<ClipVM> PendingClipsFor(int trackId)
    {
        foreach (var p in _pendingImports)
            if (p.TrackId == trackId && !p.ClipPlaced && p.LengthBeats > 0)
                yield return new ClipVM
                {
                    ClipIndex = -1, StartBeat = p.StartBeat, LengthBeats = p.LengthBeats,
                    Peaks = p.Peaks, PeakCount = p.PeakCount, Name = p.Label,
                };
    }
}
